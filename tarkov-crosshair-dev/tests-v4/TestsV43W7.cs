// v4.3 (W7) PERF: overlay presents only on change, render errors around alt-tab never add up to a stop, the foreground
// check never waits for the UI thread and sees the game again in the same frame, the render thread's log never waits,
// raw mouse batches are summed without allocation, 高级设置 touches only the current page, wheel notches coalesce.
// Pure logic only (no Win32 / WinForms on this box): the Windows side is UNVERIFIED.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V43W7Init() { V43W7Tests(); }

        static void V43W7Tests()
        {
            // ---- W7-1 PresentGate
            {
                PresentGate pg = new PresentGate();
                int n = 0;
                for (int i = 0; i < 1000; i++) if (pg.Need(100, 200, 255, i == 0)) n++;
                bool still = n == 1;
                bool move = pg.Need(101, 200, 255, false) && !pg.Need(101, 200, 255, false);
                bool fade = pg.Need(101, 200, 254, false);
                bool img = pg.Need(101, 200, 254, true);
                pg.Invalidate();
                bool inval = pg.Need(101, 200, 254, false) && !pg.Need(101, 200, 254, false);
                Check("V43-W7-1 overlay skips unchanged frames: 1000 identical frames = 1 UpdateLayeredWindow; a move, an opacity/colour change, a new image or Invalidate (new DIB / refused present) presents again",
                    still && move && fade && img && inval, "n=" + n + " move=" + move + " fade=" + fade + " img=" + img + " inval=" + inval);
            }

            // ---- W7-2 RenderErrorBudget
            {
                long f = 1000; // ticks per second
                RenderErrorBudget b = new RenderErrorBudget(f);
                bool fatal = false; long t = 0;
                for (int burst = 0; burst < 10; burst++)
                {
                    for (int e = 0; e < 40; e++) { fatal |= b.Failed(); t += 7; }
                    for (int k = 0; k < 200; k++) { t += 7; b.Ok(t); } // 1.4 s of clean frames
                }
                RenderErrorBudget c = new RenderErrorBudget(f);
                bool hard = false; for (int e = 0; e < 100; e++) hard = c.Failed();
                RenderErrorBudget d = new RenderErrorBudget(f);
                for (int e = 0; e < 99; e++) d.Failed();
                long u = 0; for (int k = 0; k < 70; k++) { u += 7; d.Ok(u); } // 0.49 s clean: not enough
                bool shortGap = d.Failed();
                Check("V43-W7-2 render errors: 10 alt-tab bursts of 40 failed frames with 1.4 s clean between never stop the crosshair (was: stopped after 100 per session); 100 errors in a row, or 100 with < 1 s clean between, still stop it",
                    !fatal && hard && shortGap, "bursts fatal=" + fatal + " 100-in-a-row=" + hard + " 99+0.5s+1=" + shortGap);
            }

            // ---- W7-3 ForegroundTracker: switch-back latency + reads only on change
            {
                IntPtr game = new IntPtr(0x1234), ours = new IntPtr(0x9999), other = new IntPtr(0x5555);
                long freq = 144000; int frame = 1000; // 144 Hz frames, 1 tick = 1/144000 s
                ForegroundTracker tr = new ForegroundTracker();
                int ourTitleReads = 0;
                Func<IntPtr, int> oursOf = delegate(IntPtr h) { return h == ours ? 1 : 0; };
                Func<IntPtr, string> titleOf = delegate(IntPtr h)
                {
                    if (h == ours) { ourTitleReads++; return "Tarkov Hip Aimer"; }
                    return h == game ? "EscapeFromTarkov" : "Discord";
                };
                long t = 0; string pat = "EscapeFromTarkov";
                bool inGame = true;
                for (int i = 0; i < 144 * 3; i++) { t += frame; inGame &= tr.Update(game, t, freq, oursOf, titleOf, pat); } // 3 s in game
                int readsGame = tr.TitleReads;
                for (int i = 0; i < 144; i++) { t += frame; tr.Update(ours, t, freq, oursOf, titleOf, pat); } // settings window in front
                bool hiddenInOurs = !tr.GameFocused(true) && tr.Ours == 1;
                for (int i = 0; i < 144; i++) { t += frame; tr.Update(other, t, freq, oursOf, titleOf, pat); }
                bool hiddenOther = !tr.GameFocused(true);
                // alt-tab back: frames until the game counts as focused
                int framesBack = -1;
                for (int i = 0; i < 144; i++) { t += frame; if (tr.Update(game, t, freq, oursOf, titleOf, pat)) { framesBack = i; break; } }
                double msBack = framesBack < 0 ? double.MaxValue : (framesBack + 1) * 1000.0 / 144;
                Check("V43-W7-3 alt-tab back: the game counts as in front on the FIRST frame it is foreground (" + msBack.ToString("0.0") + " ms at 144 Hz, target < 100 ms; was a 10 Hz poll); 3 s in game read the title " + readsGame + "x (on change + once a second, no enumeration); our own window's title is never read",
                    inGame && framesBack == 0 && msBack < 100 && readsGame <= 4 && ourTitleReads == 0 && hiddenInOurs && hiddenOther,
                    "framesBack=" + framesBack + " reads=" + readsGame + " ourReads=" + ourTitleReads + " inGame=" + inGame);
            }

            // ---- W7-4 a 2 s UI-thread stall does not delay overlay frames
            {
                IntPtr ours = new IntPtr(0x9999);
                ManualResetEvent uiBusy = new ManualResetEvent(false);
                object uiLock = new object();
                // emulates GetWindowText(our window) = SendMessage(WM_GETTEXT) to a UI thread that is stuck for 2 s
                Func<IntPtr, string> titleOf = delegate(IntPtr h) { lock (uiLock) return "Tarkov Hip Aimer"; };
                Func<IntPtr, int> oursOf = delegate(IntPtr h) { return 1; };
                Thread ui = new Thread(delegate() { lock (uiLock) { uiBusy.Set(); Thread.Sleep(2000); } });
                ui.Start();
                uiBusy.WaitOne();
                ForegroundTracker tr = new ForegroundTracker();
                PresentGate pg = new PresentGate();
                Stopwatch sw = Stopwatch.StartNew();
                double maxGap = 0; long prev = sw.ElapsedTicks; int frames = 0;
                while (sw.ElapsedMilliseconds < 2200)
                {
                    long now = sw.ElapsedTicks;
                    tr.Update(ours, now, Stopwatch.Frequency, oursOf, titleOf, "EscapeFromTarkov");
                    pg.Need(frames & 7, 0, 255, false);
                    double gap = (now - prev) * 1000.0 / Stopwatch.Frequency;
                    if (frames > 0 && gap > maxGap) maxGap = gap;
                    prev = now; frames++;
                    Thread.Sleep(1);
                }
                ui.Join();

                object logLock = new object();
                Action<string> slowSink = delegate(string s) { lock (logLock) { } };
                Thread logger = new Thread(delegate() { lock (logLock) Thread.Sleep(2000); });
                logger.Start(); Thread.Sleep(50);
                Action<string> log = NonBlockingLog.Wrap(slowSink);
                Stopwatch lw = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++) log("render: test " + i);
                double logMs = lw.Elapsed.TotalMilliseconds;
                logger.Join();
                Check("V43-W7-4 a 2 s UI-thread stall does not delay overlay frames: with our window in front for 2.2 s the frame loop's longest gap was " + maxGap.ToString("0.0") + " ms (limit 50; the old title read waited for the UI thread); 20 render log lines while crash.log is locked for 2 s returned in " + logMs.ToString("0.0") + " ms",
                    maxGap < 50 && frames > 200 && logMs < 50, "gap=" + maxGap + " frames=" + frames + " logMs=" + logMs);
            }

            // ---- W7-6 高级设置 toggle touches only the current page
            {
                LazyPageState s = new LazyPageState(12);
                bool fresh = s.StaleCount() == 0;
                s.ChangedOn(3);
                bool one = !s.Stale(3) && s.StaleCount() == 11;
                s.MarkApplied(5);
                bool shown = !s.Stale(5) && s.StaleCount() == 10;
                s.ChangedOn(5); // toggled back on page 5: page 3 is stale again
                bool back = s.Stale(3) && !s.Stale(5) && s.StaleCount() == 11;
                s.MarkAllApplied();
                bool all = s.StaleCount() == 0;
                Check("V43-W7-6 高级设置 toggle: applied to the current page only (11 of 12 pages left for their first show); a page applied on show; toggling again re-stales the others; a full refresh applies all",
                    fresh && one && shown && back && all, "fresh=" + fresh + " one=" + one + " shown=" + shown + " back=" + back + " all=" + all);
            }

            // ---- W7-7 wheel notches coalesce into one scroll of the inner panel per frame
            {
                WheelCoalescer w = new WheelCoalescer();
                int schedules = 0;
                for (int i = 0; i < 5; i++) if (w.Add(-120)) schedules++;
                int px = w.Take();
                bool one = schedules == 1 && px == -600 && !w.Pending && w.Take() == 0;
                bool target = WheelCoalescer.Target(1000, px) == 1600 && WheelCoalescer.Target(100, 600) == 0 && WheelCoalescer.Target(500, 120) == 380;
                Check("V43-W7-7 wheel: 5 notches inside one frame = 1 scheduled scroll of 600 px (one move of the page's single inner panel instead of 5); same distance as WinForms' AutoScroll wheel (e.Delta px), clamped at the top",
                    one && target, "schedules=" + schedules + " px=" + px + " target=" + target);
            }
        }
    }
}
