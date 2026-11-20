// v5.0.1 (W14): 开镜 never touches the overlay window (one alpha-0 present, then nothing); no per-second TOPMOST re-assert;
// frame pacing without DwmFlush keeps ticking when the timer fails; the render loop never waits for the UI thread, the
// log lock or a callback; stall / heartbeat lines for crash.log. Pure logic + source checks: the Windows side is UNVERIFIED.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V501W14Init() { V501W14Tests(); }

        sealed class W14Frame
        {
            public readonly MotionEngine E = new MotionEngine();
            public readonly RingStyle Style = new RingStyle();
            public readonly FrameVisibility Vis = new FrameVisibility();
            public readonly PresentGate Gate = new PresentGate();
            public readonly TopmostPolicy Top = new TopmostPolicy();
            public readonly ForegroundTracker Fg = new ForegroundTracker();
            public GlobalSettings G; public Profile P;
            public int Presents, HiddenPresents, TopmostCalls, SizeChanges, Draws, Frames;
            public int LastS = -1, lastLevel = -1;
            public long T; public const long Freq = 360000; public const long Frame = 1000; // 360 Hz
            public Func<IntPtr, int> OursOf = delegate(IntPtr h) { return 0; };
            public Func<IntPtr, string> TitleOf = delegate(IntPtr h) { return "EscapeFromTarkov"; };
            public readonly IntPtr Game = new IntPtr(0x1234);

            public W14Frame(string adsMode)
            {
                G = GlobalSettings.CreateDefault(); G.AdsMode = adsMode; G.OnlyWhenGameFocused = true; G.Enabled = true;
                P = G.Active;
                Top.Created(0);
            }

            /// <summary>One render frame, the same decisions as Overlay.Run (window ops counted, not done).</summary>
            public void Step(InputState input)
            {
                T += Frame; Frames++;
                Fg.Update(Game, T, Freq, OursOf, TitleOf, G.GameWindowTitle);
                E.GameFocused = Fg.GameFocused(G.OnlyWhenGameFocused);
                E.OwnForeground = Fg.Ours == 1;
                E.Update(1.0 / 360, input, P, G, PreviewMode.Live, 1.0);
                Style.Set(P, E.HandBlend, E.Boosted);
                // v5.0.2 (W16): opacity / fades in the pixels, constant alpha always FrameVisibility.WindowSca
                byte sca = FrameVisibility.Level(Style.OpacityPct, E.Alpha);
                FrameAction act = Vis.Next(sca);
                if (act == FrameAction.Draw)
                {
                    Draws++;
                    int s = RingRaster.CanvasSize(Style);
                    if (s != LastS) { if (LastS >= 0) SizeChanges++; LastS = s; }
                    int x = 1280 - s / 2 + (int)Math.Floor(E.OffsetX), y = 720 - s / 2 + (int)Math.Floor(E.OffsetY);
                    if (Gate.Need(x, y, FrameVisibility.WindowSca, sca != lastLevel)) Presents++;
                    lastLevel = sca;
                }
                else if (act == FrameAction.PresentHidden)
                {
                    if (Gate.Have) { HiddenPresents++; Gate.Invalidate(); lastLevel = -1; } // the all-zero frame (v5.0.2)
                }
                if (Top.Need(Fg.Changed, T, Freq)) TopmostCalls++;
            }
        }

        static string W14SrcDir()
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string p = Path.Combine(d, "..", "tarkov-crosshair-v4", "src");
                if (Directory.Exists(p)) return Path.GetFullPath(p);
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return "/home/dev/tarkov-crosshair-v4/src";
        }

        static int W14Count(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }

        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] static extern uint W14TimeBeginPeriod(uint ms);
        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] static extern uint W14TimeEndPeriod(uint ms);

        static void V501W14Tests()
        {
            // ---- W14-1/2 开镜 1000 times (按住 and 点一下切换): no window op, one alpha-0 present per hide, no allocation
            foreach (string mode in new string[] { "hold", "toggle" })
            {
                W14Frame f = new W14Frame(mode);
                InputState idle = new InputState();
                InputState ads = new InputState(); ads.Ads = true;
                for (int i = 0; i < 360; i++) f.Step(idle); // 1 s visible: settles
                // warm-up: two 开镜 cycles (first-use JIT / lazy fields)
                for (int c = 0; c < 2; c++) W14Cycle(f, mode, ads, idle);
                int pres0 = f.Presents, hid0 = f.HiddenPresents, top0 = f.TopmostCalls, size0 = f.SizeChanges, draws0 = f.Draws, frames0 = f.Frames;
                long bytes0 = GC.GetAllocatedBytesForCurrentThread();
                for (int c = 0; c < 1000; c++) W14Cycle(f, mode, ads, idle);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
                int hidden = f.HiddenPresents - hid0, top = f.TopmostCalls - top0, sizes = f.SizeChanges - size0;
                int presents = f.Presents - pres0, draws = f.Draws - draws0, frames = f.Frames - frames0;
                // each cycle: 30 frames 开镜 (hidden), 60 frames visible (50 ms fade-in = 18 frames, then still)
                int hiddenFrames = frames - draws;
                Check("V501-W14-1 开镜 (" + mode + ") 1000 times: 0 SetWindowPos/TOPMOST, 0 canvas size changes, no ShowWindow path, exactly 1 alpha-0 present per hide (" + hidden + "), nothing presented or drawn on the other " + (hiddenFrames - hidden) + " hidden frames; " + presents + " presents for the fade-ins",
                    top == 0 && sizes == 0 && hidden == 1000 && hiddenFrames >= 1000 * 29 && presents <= 1000 * 20,
                    "top=" + top + " sizes=" + sizes + " hidden=" + hidden + " hiddenFrames=" + hiddenFrames + " presents=" + presents + " draws=" + draws);
                Check("V501-W14-2 开镜 (" + mode + ") 1000 times = " + frames + " frames of motion + visibility + present decisions allocate " + bytes + " bytes (no per-frame allocation)",
                    bytes == 0, "bytes=" + bytes);
            }

            // ---- W14-3 TOPMOST policy
            {
                TopmostPolicy t = new TopmostPolicy(); long fq = 1000;
                t.Created(0);
                int calls = 0;
                for (long ms = 1; ms <= 60000; ms++) if (t.Need(false, ms, fq)) calls++;          // 60 s, same foreground
                int still = calls;
                bool first = t.Need(true, 60001, fq);                                              // alt-tab
                int burst = 0; for (long ms = 60002; ms < 60900; ms++) if (t.Need(ms % 100 == 0, ms, fq)) burst++; // more changes inside 1 s
                bool late = false; for (long ms = 60900; ms <= 61100; ms++) if (t.Need(false, ms, fq)) late = true; // pending one after 1 s
                bool quiet = true; for (long ms = 61101; ms < 70000; ms++) if (t.Need(false, ms, fq)) quiet = false;
                TopmostPolicy u = new TopmostPolicy(); u.Created(0); bool soon = u.Need(true, 10, fq);
                Check("V501-W14-3 TOPMOST: 60 s with the same foreground window = 0 SetWindowPos (was 60); a foreground change re-asserts once, more changes inside the second wait and run once when it ends; none within 1 s of creating the window",
                    still == 0 && first && burst == 0 && late && quiet && !soon && t.Calls == 2,
                    "still=" + still + " first=" + first + " burst=" + burst + " late=" + late + " quiet=" + quiet + " soon=" + soon + " calls=" + t.Calls);
            }

            // ---- W14-4 pacer: the primary wait fails / throws / returns at once -> Sleep(1) fallback, frames keep coming
            {
                // v5.1.1 (X2): on Windows Thread.Sleep(1) sleeps 15.6 ms unless the timer resolution is 1 ms; the app sets
                // timeBeginPeriod(1) for its Sleep(1) fallback (Overlay.cs), so this check does the same (CI run 44729023467:
                // with 15.6 ms sleeps the "returns at once" timer was never seen as broken).
                bool w14Period = false;
                if (OperatingSystem.IsWindows()) { try { w14Period = W14TimeBeginPeriod(1) == 0; } catch (Exception) { } }
                try {
                string[] names = { "fails", "throws", "returns at once", "works" };
                foreach (string n in names)
                {
                    Stopwatch sw = Stopwatch.StartNew(); long fq = Stopwatch.Frequency;
                    string kind = n;
                    FramePacer.PrimaryWait prim = delegate(long ticks)
                    {
                        if (kind == "fails") return false;
                        if (kind == "throws") throw new InvalidOperationException("DwmFlush-like failure");
                        if (kind == "returns at once") return true;
                        Thread.Sleep((int)Math.Max(1, ticks * 1000 / fq)); return true;
                    };
                    FramePacer p = new FramePacer(delegate { return sw.ElapsedTicks; }, fq, prim, delegate { Thread.Sleep(1); });
                    int ticks = 0; double maxGap = 0; long prev = sw.ElapsedTicks;
                    while (sw.ElapsedMilliseconds < 1000)
                    {
                        p.Wait(144); ticks++;
                        long now = sw.ElapsedTicks; double gap = (now - prev) * 1000.0 / fq; prev = now;
                        if (gap > maxGap) maxGap = gap;
                    }
                    bool fell = n == "works" ? p.UsingPrimary : !p.UsingPrimary;
                    Check("V501-W14-4 pacing when the timer " + n + ": " + ticks + " frames in 1 s at 144 Hz (limit 60..160), longest gap " + maxGap.ToString("0.0") + " ms, " + (p.UsingPrimary ? "timer kept" : "Sleep(1) fallback"),
                        ticks >= 60 && ticks <= 160 && maxGap < 60 && fell, "ticks=" + ticks + " gap=" + maxGap + " primary=" + p.UsingPrimary);
                }
                } finally { if (w14Period) { try { W14TimeEndPeriod(1); } catch (Exception) { } } }
            }

            // ---- W14-5 a 5 s UI-thread stall (window title, toggle callback, crash.log lock) + failing timer: the loop keeps ticking
            {
                object uiLock = new object();
                ManualResetEvent uiBusy = new ManualResetEvent(false);
                Thread ui = new Thread(delegate() { lock (uiLock) { uiBusy.Set(); Thread.Sleep(5000); } });
                ui.Start(); uiBusy.WaitOne();
                OverlayWorker w = new OverlayWorker();
                int written = 0, toggles = 0;
                w.Sink = delegate(string s) { lock (uiLock) { written++; } };          // Program.Log waiting for its lock
                int ticks = 0; w.Tick = delegate { ticks++; };
                // The worker thread is not started here: these tests run inside the module initializer, and another thread
                // calling into this module waits until it ends. Drain() below does the worker's job after the stall.
                IntPtr ours = new IntPtr(0x9999);
                Func<IntPtr, int> oursOf = delegate(IntPtr h) { return 1; };
                Func<IntPtr, string> titleOf = delegate(IntPtr h) { lock (uiLock) return "Tarkov Hip Aimer"; }; // would wait: must never run
                Stopwatch sw = Stopwatch.StartNew(); long fq = Stopwatch.Frequency;
                FramePacer pacer = new FramePacer(delegate { return sw.ElapsedTicks; }, fq, delegate(long t) { return false; }, delegate { Thread.Sleep(1); });
                ForegroundTracker fg = new ForegroundTracker();
                int frames = 0; double maxGap = 0; long prev = sw.ElapsedTicks;
                while (sw.ElapsedMilliseconds < 5300)
                {
                    fg.Update(ours, sw.ElapsedTicks, fq, oursOf, titleOf, "EscapeFromTarkov");
                    if (frames % 50 == 0) w.Post(delegate { lock (uiLock) { toggles++; } });   // toggle -> UI (BeginInvoke stand-in)
                    if (frames % 10 == 0) w.Log("render: frame " + frames);
                    pacer.Wait(144); frames++;
                    long now = sw.ElapsedTicks; double gap = (now - prev) * 1000.0 / fq; prev = now;
                    if (frames > 1 && gap > maxGap) maxGap = gap;
                }
                ui.Join();
                int queuedBefore = w.Queued;
                w.Drain(); // the worker's pass after the stall
                int queuedLeft = w.Queued;
                int wrote = written, tg = toggles;
                bool delivered = queuedBefore > 0 && queuedLeft == 0 && wrote == queuedBefore && tg == (frames + 49) / 50;
                Check("V501-W14-5 UI thread stuck 5 s (our window title, toggle callback and crash.log all wait for it) and the timer failing: the render loop ran " + frames + " frames, longest gap " + maxGap.ToString("0.0") + " ms (limit 50); the queued " + wrote + " log lines / " + tg + " callbacks all arrive in the worker's pass after the stall",
                    frames > 500 && maxGap < 50 && delivered && !pacer.UsingPrimary,
                    "frames=" + frames + " gap=" + maxGap + " wrote=" + wrote + " toggles=" + tg + " queuedLeft=" + queuedLeft);
            }

            // ---- W14-6 stall watch + heartbeat + phase line + bounded log queue
            {
                StallWatch st = new StallWatch(); long fq = 1000;
                bool none = st.Check(500, fq) == null;               // no frame yet
                st.Frame(1000); st.Enter(PhaseStats.Wait);
                bool ok1 = st.Check(2500, fq) == null;
                string s1 = st.Check(3100, fq);
                bool once = st.Check(3500, fq) == null;
                st.Frame(4000);
                string s2 = st.Check(4100, fq);
                bool stall = s1 != null && s1.Contains("wait") && s2 != null && s2.Contains("3.0");
                bool hb = HeartbeatSchedule.NextAfter(0, 0, fq) == 5000 && HeartbeatSchedule.NextAfter(0, 5000, fq) == 65000
                    && HeartbeatSchedule.NextAfter(0, 299000, fq) == 359000 && HeartbeatSchedule.NextAfter(0, 300000, fq) == 900000;
                PhaseStats ph = new PhaseStats();
                for (int i = 0; i < 4; i++) { ph.Add(PhaseStats.Pump, 10); ph.Add(PhaseStats.Wait, 2000 + i * 1000); ph.Frame(); }
                string line = ph.TakeLine(1000000);
                bool phase = line.StartsWith("frames=4") && line.Contains("pump 0.010/0.01 ms") && line.Contains("wait 3.500/5.00 ms") && ph.Frames == 0;
                OverlayWorker q = new OverlayWorker(); int got = 0; q.Sink = delegate(string s) { got++; };
                for (int i = 0; i < OverlayWorker.MaxLines + 100; i++) q.Log("x");
                int dropped = q.Dropped; q.Drain();
                Check("V501-W14-6 crash.log diagnostics: a 2 s render stall is logged once with its phase and once when frames return; heartbeat 5 s, then 60 s for 5 min, then 10 min; per-phase avg/max line; the log queue holds " + OverlayWorker.MaxLines + " lines and counts the rest",
                    none && ok1 && once && stall && hb && phase && dropped == 100 && got == OverlayWorker.MaxLines + 1,
                    "none=" + none + " ok1=" + ok1 + " s1=" + s1 + " once=" + once + " s2=" + s2 + " hb=" + hb + " line=" + line + " dropped=" + dropped + " got=" + got);
            }

            // ---- W14-7 source: what the render thread may call
            {
                string src = File.ReadAllText(Path.Combine(W14SrcDir(), "Overlay.cs"));
                int run = src.IndexOf("private void Run()", StringComparison.Ordinal);
                int runEnd = src.IndexOf("private bool PresentHidden()", StringComparison.Ordinal);
                string body = run >= 0 && runEnd > run ? src.Substring(run, runEnd - run) : "";
                bool noDwm = !src.Contains("DwmFlush(");
                // v5.0.2 (W16, Leo decision 1): ShowWindow at create, plus SW_SHOWNOACTIVATE / SW_HIDE only behind
                // WindowShowPolicy (crosshair off / game not in front); 开镜 never reaches them (TestsV502W16).
                bool oneShow = W14Count(src, "ShowWindow(") == 3 && Regex.IsMatch(src, @"windowShow\.BeforeDraw\(longHidden\) == WindowOp\.Show\)\s*\{\s*Native\.ShowWindow\(hwnd, Native\.SW_SHOWNOACTIVATE\)")
                    && Regex.IsMatch(src, @"windowShow\.AfterDraw\(longHidden, visibility\.Hidden\) == WindowOp\.Hide\)\s*\n\s*Native\.ShowWindow\(hwnd, Native\.SW_HIDE\)");
                bool topGuard = W14Count(src, "SetWindowPos(") == 1 && Regex.IsMatch(src, @"topmost\.Need\([^\n]*\)\)\s*\n\s*Native\.SetWindowPos\(");
                bool noBreak = body.Length > 0 && !Regex.IsMatch(body, @"\bbreak;");
                bool noWait = !src.Contains("BeginInvoke") && !src.Contains(".Invoke(") && !src.Contains("lock (") && !src.Contains("Program.Log") && !src.Contains("SendMessage")
                    && !src.Contains("NativeWindow :") && !body.Contains("RefreshRate.Primary()") && !body.Contains("cb(") ;
                bool noAlpha0Hide = W14Count(src, "SW_HIDE") == 1;
                Check("V501-W14-7 Overlay.cs: no DwmFlush, ShowWindow at create + behind WindowShowPolicy only, SetWindowPos only behind TopmostPolicy, no break out of the render loop, no BeginInvoke / Invoke / lock / Program.Log / SendMessage / WinForms window / EnumDisplaySettings on the render thread, SW_HIDE only behind WindowShowPolicy",
                    noDwm && oneShow && topGuard && noBreak && noWait && noAlpha0Hide && body.Length > 0,
                    "dwm=" + noDwm + " show=" + oneShow + " top=" + topGuard + " break=" + noBreak + " wait=" + noWait + " hide=" + noAlpha0Hide);
            }
        }

        /// <summary>One 开镜 cycle: 30 frames ADS (按住 held / 切换 pressed once), then 60 frames visible.</summary>
        static void W14Cycle(W14Frame f, string mode, InputState ads, InputState idle)
        {
            if (mode == "hold") { for (int i = 0; i < 30; i++) f.Step(ads); }
            else { f.Step(ads); for (int i = 0; i < 29; i++) f.Step(idle); }
            if (mode == "toggle") { f.Step(ads); for (int i = 0; i < 59; i++) f.Step(idle); }
            else { for (int i = 0; i < 60; i++) f.Step(idle); }
        }
    }
}
