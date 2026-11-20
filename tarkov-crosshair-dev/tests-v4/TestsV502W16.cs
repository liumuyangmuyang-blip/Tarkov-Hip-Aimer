// v5.0.2 (W16): overlay + admin. 开镜 = one all-zero frame in the unchanged window (Leo 18 Nov 2026); SW_HIDE only when the
// crosshair is off / the game is not in front; opacity and fades in the pixels; 1 ms key poll with an immediate push on a key
// change (minimum latency); vblank phase lock; key reads once per frame; 换手颜色 fade without key strings; Hidden counts outline /
// feather / glow; LeanQ / LeanE previews in 切换 mode; 自定义图片 file stat off the render thread; one decode per upload;
// requireAdministrator + logon task (TestsV41Settings V502-ST19). Pure logic + source checks; Windows is UNVERIFIED.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V502W16Init() { V502W16Tests(); }

        /// <summary>One render frame with the decisions of Overlay.Run / Draw / PresentHidden (window calls counted).</summary>
        sealed class W16Sim
        {
            public readonly MotionEngine E = new MotionEngine();
            public readonly RingStyle Style = new RingStyle();
            public readonly FrameVisibility Vis = new FrameVisibility();
            public readonly PresentGate Gate = new PresentGate();
            public readonly TopmostPolicy Top = new TopmostPolicy();
            public readonly ForegroundTracker Fg = new ForegroundTracker();
            public readonly WindowShowPolicy Win = new WindowShowPolicy();
            public readonly RasterChangeDetector Changed = new RasterChangeDetector();
            public readonly RasterCache Cache = new RasterCache();
            public GlobalSettings G; public Profile P;
            public bool Enabled = true, GameFront = true;
            public int Uploads, Moves, Blanks, BlankBad, Shows, Hides, TopmostCalls, SizeChanges, ScaNot255, Draws, Frames, PushAlpha0Bad;
            public int LastS = -1, MaxS;
            public int[] Buf = new int[0];
            int dibLevel = -1; bool surface;
            public long T; public readonly long Freq; public readonly long Frame;
            readonly IntPtr game = new IntPtr(0x1234), other = new IntPtr(0x999);
            readonly Func<IntPtr, int> oursOf = delegate(IntPtr h) { return 0; };
            readonly Func<IntPtr, string> titleOf;

            public W16Sim(int hz, string adsMode)
            {
                Freq = 360000; Frame = Freq / hz;
                G = GlobalSettings.CreateDefault(); G.AdsMode = adsMode; G.OnlyWhenGameFocused = true; G.Enabled = true;
                P = G.Active;
                titleOf = delegate(IntPtr h) { return h == game ? "EscapeFromTarkov" : "Explorer"; };
                Top.Created(0);
            }

            public void Step(InputState input)
            {
                T += Frame; Frames++;
                Fg.Update(GameFront ? game : other, T, Freq, oursOf, titleOf, G.GameWindowTitle);
                bool focused = Fg.GameFocused(G.OnlyWhenGameFocused);
                E.GameFocused = focused; E.OwnForeground = false;
                if (!FocusGate.KeysLive(G.OnlyWhenGameFocused, focused)) input = new InputState();
                G.Enabled = Enabled;
                E.Update(Frame / (double)Freq, input, P, G, PreviewMode.Live, 1.0);
                Style.Set(P, E.HandBlend, E.Boosted);
                byte level = FrameVisibility.Level(Style.OpacityPct, E.Alpha);
                bool longHidden = WindowShowPolicy.LongHidden(Enabled, G.OnlyWhenGameFocused, focused, false);
                bool shownNow = false;
                if (Win.BeforeDraw(longHidden) == WindowOp.Show) { Shows++; shownNow = true; }
                FrameAction act = Win.Shown ? Vis.Next(level) : FrameAction.Skip;
                if (act == FrameAction.Draw) Draw(level);
                else if (act == FrameAction.PresentHidden && Gate.Have)
                {
                    // the cached all-zero bitmap (Overlay.PresentHidden), same size / place / constant alpha
                    FadePixels.Scale(Buf, Buf, Buf.Length, 0);
                    for (int i = 0; i < Buf.Length; i++) if (((Buf[i] >> 24) & 0xFF) != 0 || Buf[i] != 0) { BlankBad++; break; }
                    Blanks++;
                    if (Gate.LastSca != FrameVisibility.WindowSca) ScaNot255++;
                    Gate.Invalidate(); surface = false; dibLevel = -1;
                }
                if (Win.AfterDraw(longHidden, Vis.Hidden) == WindowOp.Hide) Hides++;
                if (Top.Need(Fg.Changed || shownNow, T, Freq)) TopmostCalls++;
            }

            void Draw(byte level)
            {
                Draws++;
                int S = RingRaster.CanvasSize(Style);
                if (S != LastS) { if (LastS >= 0) SizeChanges++; LastS = S; Buf = new int[S * S]; dibLevel = -1; Changed.Reset(); Gate.Invalidate(); surface = false; }
                if (S > MaxS) MaxS = S;
                double X = 1280 + E.OffsetX, Y = 720 + E.OffsetY;
                if (S > 128) { X = Math.Round(X); Y = Math.Round(Y); }
                int wx = (int)Math.Floor(X) - S / 2, wy = (int)Math.Floor(Y) - S / 2;
                double cx = Math.Round((X - wx) * 8.0) / 8.0, cy = Math.Round((Y - wy) * 8.0) / 8.0;
                RasterCache.Normalise(S, ref wx, ref cx); RasterCache.Normalise(S, ref wy, ref cy);
                bool img = Changed.Changed(Style, S, cx, cy) || level != dibLevel;
                if (img) { FadePixels.Scale(Cache.Image(Style, S, cx, cy), Buf, S * S, level); dibLevel = level; surface = false; }
                if (!Gate.Need(wx, wy, FrameVisibility.WindowSca, img)) return;
                if (surface) Moves++; else Uploads++;
                surface = true;
            }
        }

        static void V502W16Tests()
        {
            InputState idle = new InputState();
            InputState ads = new InputState(); ads.Ads = true;

            // ---- W16-1 开镜 1000 times: one all-zero frame per 开镜, nothing else changes on the window
            foreach (string mode in new string[] { "hold", "toggle" })
            {
                W16Sim f = new W16Sim(360, mode);
                for (int i = 0; i < 360; i++) f.Step(idle);
                for (int c = 0; c < 2; c++) W16Cycle(f, mode, ads, idle); // warm-up
                int sh = f.Shows, hi = f.Hides, top = f.TopmostCalls, sz = f.SizeChanges, bl = f.Blanks, up = f.Uploads, dr = f.Draws, fr = f.Frames;
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int c = 0; c < 1000; c++) W16Cycle(f, mode, ads, idle);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                int blanks = f.Blanks - bl, frames = f.Frames - fr, draws = f.Draws - dr;
                Check("V502-W16-1 开镜 (" + mode + ") 1000 times: " + blanks + " all-zero frames (every pushed pixel 0), 0 ShowWindow / SW_HIDE, 0 size changes, 0 TOPMOST calls, constant alpha always 255, nothing drawn on the other " + (frames - draws - blanks) + " hidden frames, " + bytes + " bytes allocated",
                    blanks == 1000 && f.BlankBad == 0 && f.Shows == sh && f.Hides == hi && f.TopmostCalls == top && f.SizeChanges == sz && f.ScaNot255 == 0 && bytes == 0 && frames - draws >= 1000 * 29,
                    "blanks=" + blanks + " bad=" + f.BlankBad + " shows=" + (f.Shows - sh) + " hides=" + (f.Hides - hi) + " top=" + (f.TopmostCalls - top) + " sizes=" + (f.SizeChanges - sz) + " uploads=" + (f.Uploads - up) + " bytes=" + bytes);
            }

            // ---- W16-2 off / game not in front: blank once, SW_HIDE once, nothing drawn; back: SW_SHOWNOACTIVATE + one TOPMOST
            {
                W16Sim f = new W16Sim(144, "hold");
                for (int i = 0; i < 144; i++) f.Step(idle);
                int up0 = f.Uploads;
                f.Enabled = false; for (int i = 0; i < 144; i++) f.Step(idle);
                bool offOk = f.Hides == 1 && f.Blanks == 1 && f.Uploads == up0 && !f.Win.Shown;
                int top0 = f.TopmostCalls;
                f.Enabled = true; for (int i = 0; i < 144; i++) f.Step(idle);
                bool onOk = f.Shows == 1 && f.Win.Shown && f.Uploads > up0 && f.TopmostCalls >= top0;
                int up1 = f.Uploads;
                f.GameFront = false; for (int i = 0; i < 144; i++) f.Step(idle);
                bool awayOk = f.Hides == 2 && f.Blanks == 2 && f.Uploads == up1;
                f.GameFront = true; for (int i = 0; i < 144; i++) f.Step(idle);
                bool backOk = f.Shows == 2 && f.Win.Shown;
                // 开镜 while away and back: never an extra hide/show from 开镜
                Check("V502-W16-2 crosshair off / game not in front: one all-zero frame, one SW_HIDE, nothing drawn while hidden; on again / game back: one SW_SHOWNOACTIVATE + TOPMOST, drawn",
                    offOk && onOk && awayOk && backOk, "off=" + offOk + " on=" + onOk + " away=" + awayOk + " back=" + backOk + " shows=" + f.Shows + " hides=" + f.Hides);
            }

            // ---- W16-3 FadePixels: level 255 copy, level 0 all zero, level k = round(c * k / 255) per channel (all c, k)
            {
                bool exact = true; int[] src = new int[256], dst = new int[256];
                for (int k = 0; k < 256 && exact; k++)
                {
                    for (int c = 0; c < 256; c++) src[c] = (c << 24) | (((c * 7) & 255) << 16) | (((c * 13) & 255) << 8) | ((c * 29) & 255);
                    FadePixels.Scale(src, dst, 256, (byte)k);
                    for (int c = 0; c < 256; c++)
                        for (int sh = 0; sh < 32; sh += 8)
                        {
                            int v = (src[c] >> sh) & 255, got = (dst[c] >> sh) & 255;
                            if (got != (int)(v * k / 255.0 + 0.5)) { exact = false; break; }
                        }
                }
                byte l0 = FrameVisibility.Level(100, 0), l1 = FrameVisibility.Level(100, 1), l50 = FrameVisibility.Level(50, 1), lnan = FrameVisibility.Level(double.NaN, 1);
                Check("V502-W16-3 opacity / fades in the pixels: every channel x level / 255 rounded exactly (65,536 cases), level 0 = all zero, level 255 = copy; Level(100%,0)=0, (100%,1)=255, (50%,1)=128, NaN=0",
                    exact && l0 == 0 && l1 == 255 && l50 == 128 && lnan == 0, "exact=" + exact + " " + l0 + "/" + l1 + "/" + l50 + "/" + lnan);
            }

            // ---- W16-4 latency: a key change is pushed within 1.5 ms of happening (1 ms poll, immediate frame)
            {
                long fq = 10000000; long t = 0; long jitter = fq / 1000 * 3 / 10; // timer wakes 0.3 ms late
                FramePacer p = new FramePacer(delegate { return t; }, fq, delegate(long ticks) { t += ticks + jitter; return true; }, delegate { t += fq / 1000; });
                InputWake w = new InputWake();
                Random r = new Random(5);
                double worst = 0, sum = 0; int n = 0; bool keyState = false; long changeAt = long.MaxValue;
                double frameWorkMax = 0;
                W16Sim sim = new W16Sim(144, "hold");
                InputState mv = new InputState(); mv.Fwd = true;
                Func<bool> poll = delegate { bool down = t >= changeAt; if (down != keyState) { keyState = down; return true; } return false; };
                foreach (int hz in new int[] { 60, 144, 240, 360 })
                {
                    for (int i = 0; i < 400; i++)
                    {
                        changeAt = t + (long)(r.NextDouble() * fq / hz * 3);
                        keyState = false;
                        int guard = 0;
                        // a refresh frame reads the keys at its start too: a change it sees is pushed by that frame
                        while (!w.WaitNext(p, hz, fq, poll) && t < changeAt && guard++ < 100) { }
                        double lat = (t - changeAt) * 1000.0 / fq; // the push follows at once (frame work below)
                        worst = Math.Max(worst, lat); sum += lat; n++;
                        changeAt = long.MaxValue;
                    }
                }
                // real frame work from "seen" to "pushed": motion + style + image + fade copy (this machine)
                Stopwatch sw = new Stopwatch();
                for (int i = 0; i < 2000; i++)
                {
                    sw.Restart(); sim.Step((i / 50) % 2 == 0 ? mv : idle); sw.Stop();
                    if (i > 200) frameWorkMax = Math.Max(frameWorkMax, sw.Elapsed.TotalMilliseconds);
                }
                double avg = sum / n;
                Check("V502-W16-4 key change -> push: seen by the 1 ms poll and pushed at once (timer 0.3 ms late): worst " + worst.ToString("0.00") + " ms, average " + avg.ToString("0.00") + " ms after the change over " + n + " changes at 60/144/240/360 Hz; frame work seen -> pushed max " + frameWorkMax.ToString("0.000") + " ms",
                    worst <= 1.5 && frameWorkMax < 1.5 && w.InputFrames > n / 2, "worst=" + worst + " avg=" + avg + " input=" + w.InputFrames + " polls=" + w.Polls + " work=" + frameWorkMax);
            }

            // ---- W16-5 the refresh frames keep their schedule around input frames; no polling while the keys are not live
            {
                long fq = 10000000; long t = 0;
                FramePacer p = new FramePacer(delegate { return t; }, fq, delegate(long ticks) { t += ticks; return true; }, delegate { t += fq / 1000; });
                InputWake w = new InputWake();
                int calls = 0;
                Func<bool> poll = delegate { calls++; return calls % 3 == 0; };
                int deadlines = 0;
                long end = fq; // 1 s at 144 Hz
                while (t < end) { if (!w.WaitNext(p, 144, fq, poll)) deadlines++; }
                int polls0 = w.Polls; long t1 = t; int dl = 0;
                while (t < t1 + fq) { w.WaitNext(p, 60, fq, null); dl++; }
                Check("V502-W16-5 refresh frames stay on schedule with input frames in between (" + deadlines + " of 144 in 1 s); not live: no key polls, " + dl + " frames in 1 s at 60 Hz",
                    deadlines >= 142 && deadlines <= 146 && w.Polls == polls0 && dl >= 59 && dl <= 61, "deadlines=" + deadlines + " input=" + w.InputFrames + " dl=" + dl);
            }

            // ---- W16-6 vblank phase lock (DwmGetCompositionTimingInfo values), and no lock when the period disagrees > 5 %
            {
                long fq = 10000000; long t = 0;
                FramePacer p = new FramePacer(delegate { return t; }, fq, delegate(long ticks) { t += ticks; return true; }, delegate { t += fq / 1000; });
                double period = fq / 143.856; long anchor = 1234567; long lead = (long)(FramePacer.LeadMs * fq / 1000.0);
                p.SetVBlank(anchor, period);
                double worstOff = 0;
                for (int i = 0; i < 500; i++)
                {
                    p.Wait(144);
                    double ph = ((t + lead - anchor) % period + period) % period;
                    worstOff = Math.Max(worstOff, Math.Min(ph, period - ph));
                }
                bool locked = p.PhaseLocked(144) && worstOff <= 2;
                p.SetVBlank(anchor, fq / 120.0);
                bool unlocked = !p.PhaseLocked(144);
                Check("V502-W16-6 frames end " + FramePacer.LeadMs + " ms before a predicted vblank (143.856 Hz, worst phase error " + worstOff.ToString("0.0") + " ticks of 100 ns over 500 frames); a 120 Hz DWM period at 144 Hz is not used",
                    locked && unlocked, "locked=" + locked + " off=" + worstOff + " unlocked=" + unlocked);
            }

            // ---- W16-7 KeySnapshot: each virtual key read once per frame, same InputState as direct reads
            {
                GlobalSettings g = GlobalSettings.CreateDefault();
                int direct = 0; HashSet<int> seen = new HashSet<int>(); HashSet<int> down = new HashSet<int> { KeyBindings.VkW, 0x10 };
                Func<int, bool> raw = delegate(int vk) { direct++; seen.Add(vk); return down.Contains(vk); };
                KeySnapshot ks = new KeySnapshot(raw);
                InputState a = MotionEngine.ReadInput(g.Keys, raw); bool ta = MotionEngine.IsActionDown(g.Keys, 0, raw);
                int directReads = direct; direct = 0;
                ks.NextFrame();
                InputState b = MotionEngine.ReadInput(g.Keys, ks.IsDown); bool tb = MotionEngine.IsActionDown(g.Keys, 0, ks.IsDown);
                int snapReads = ks.Reads;
                bool same = a.Fwd == b.Fwd && a.Shift == b.Shift && a.Ads == b.Ads && a.Fire == b.Fire && a.LeanL == b.LeanL && ta == tb && b.Fwd && b.Shift;
                ks.NextFrame(); MotionEngine.ReadInput(g.Keys, ks.IsDown);
                Check("V502-W16-7 key reads per frame: " + directReads + " -> " + snapReads + " (each of the " + seen.Count + " distinct keys once), same result, re-read next frame",
                    same && snapReads == seen.Count && snapReads <= directReads && ks.Reads > snapReads, "direct=" + directReads + " distinct=" + seen.Count + " snap=" + snapReads + " same=" + same);
            }

            // ---- W16-8 换手颜色 fade: no key strings / phase rebuild per frame, exact pixels
            {
                Profile p = Profile.CreateLeoDefault("fade"); p.HandColorOn = true; p.HandColor = "#FF2000"; p.HandColor2 = "#0030FF";
                RingStyle s = new RingStyle(); RasterCache rc = new RasterCache();
                s.Set(p, 0, false); int S = RingRaster.CanvasSize(s);
                double cx = S / 2.0 + 3 / 8.0, cy = S / 2.0 + 5 / 8.0;
                bool exact = true; long bytes = 0; int fadeR = 0, phaseR = 0;
                for (int rep = 0; rep < 3; rep++)
                {
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    int f0 = rc.FadeRenders, p0 = rc.PhaseRenders;
                    for (int i = 0; i <= 162; i++)
                    {
                        double w = rep % 2 == 0 ? i / 162.0 : 1 - i / 162.0;
                        s.Set(p, w, false);
                        int[] img = rc.Image(s, S, cx, cy);
                        if (rep == 2 && (i % 9 == 0))
                        {
                            int[] ex = RingRaster.Render(s, S, cx, cy);
                            for (int k = 0; k < ex.Length; k++) if (ex[k] != img[k]) { exact = false; break; }
                        }
                    }
                    for (int i = 0; i < 20; i++) rc.Image(s, S, cx, cy); // stable again
                    if (rep == 1) { bytes = GC.GetAllocatedBytesForCurrentThread() - b0; fadeR = rc.FadeRenders - f0; phaseR = rc.PhaseRenders - p0; }
                }
                Check("V502-W16-8 换手颜色 fade (Leo default, 163 frames): " + bytes + " bytes allocated in RasterCache (v5.0.1: about 0.94 KB per frame), " + fadeR + " direct fade frames, " + phaseR + " phase images rebuilt after it, pixels = exact render",
                    exact && bytes < 1024 && phaseR <= 2, "exact=" + exact + " bytes=" + bytes + " fade=" + fadeR + " phase=" + phaseR);
            }

            // ---- W16-9 RingStyle.Hidden counts outline / feather / glow (BRUTE P3-4)
            {
                Profile dr = R41Tile("dotring");
                dr.Diameter = 43.4; dr.Thickness = 7.8; dr.Feather = 26.5; dr.Gradient = "twotone"; dr.OuterThickness = 12.4; StyleShapes.Of(dr).Hole = 28.6;
                RingStyle st = RingStyle.From(dr, 0, false);
                bool example = !st.Hidden;
                string wrongD = "";
                Profile tiny = R41Tile("corners"); tiny.Diameter = 4; RingStyle ts = RingStyle.From(tiny, 0, false);
                int tn = RingRaster.CanvasSize(ts); int tMax = 0; foreach (int px in RingRaster.Render(ts, tn, tn / 2.0, tn / 2.0)) tMax = Math.Max(tMax, (px >> 24) & 255);
                wrongD += "tiny corners hidden=" + ts.Hidden + " maxA=" + tMax + " feather=" + ts.Feather + " glow=" + ts.GlowPx + "; ";
                Random r = new Random(11); int wrong = 0, checkedN = 0;
                string[] ids = { "dotring", "ring", "chevron", "corners", "fourdots" };
                for (int i = 0; i < 600; i++)
                {
                    Profile p = R41Tile(ids[i % ids.Length]);
                    p.Feather = r.NextDouble() * 20; p.GlowPx = r.Next(3) == 0 ? r.NextDouble() * 12 : 0; p.GlowOpacityPct = 80;
                    p.Gradient = r.Next(2) == 0 ? "twotone" : "none"; p.OuterThickness = r.NextDouble() * 10;
                    StyleShapes.Of(p).Hole = r.NextDouble() * 30;
                    RingStyle q = RingStyle.From(p, 0, false);
                    int n = RingRaster.CanvasSize(q);
                    int[] a = RingRaster.Render(q, n, n / 2.0, n / 2.0);
                    int maxA = 0; foreach (int px in a) maxA = Math.Max(maxA, (px >> 24) & 255);
                    checkedN++;
                    if (maxA > 128 && q.Hidden) { wrong++; if (wrong <= 3) wrongD += ids[i % ids.Length] + " f" + p.Feather.ToString("0.0") + " g" + p.GlowPx.ToString("0.0") + " " + p.Gradient + " o" + p.OuterThickness.ToString("0.0") + " h" + StyleShapes.Of(p).Hole.ToString("0.0") + " a" + maxA + "; "; }
                }
                Check("V502-W16-9 a look with visible outline / feather / glow past 中心留空 is not flagged Hidden (brute example dotring D43.4 T7.8 feather 26.5 outline 12.4 hole 28.6, and " + checkedN + " random looks: " + wrong + " wrong)",
                    example && wrong == 0, "example=" + example + " wrong=" + wrong + " " + wrongD);
            }

            // ---- W16-10 LeanQ / LeanE previews show the lean in 侧身方式 切换 (BRUTE P3-6), 按住 unchanged
            {
                bool ok = true; string d = "";
                foreach (string mode in new string[] { "toggle", "hold" })
                    foreach (PreviewMode pm in new PreviewMode[] { PreviewMode.LeanQ, PreviewMode.LeanE })
                    {
                        GlobalSettings g = GlobalSettings.CreateDefault(); g.LeanMode = mode; g.Enabled = true;
                        MotionEngine e = new MotionEngine();
                        for (int i = 0; i < 144 * 4; i++) e.Update(1 / 144.0, new InputState(), g.Active, g, pm, 1.0);
                        double want = pm == PreviewMode.LeanQ ? -1 : 1;
                        if (Math.Abs(e.Lean - want) > 0.05) { ok = false; d += mode + "/" + pm + "=" + e.Lean.ToString("0.00") + " "; }
                    }
                Check("V502-W16-10 LeanQ / LeanE previews reach full lean after 4 s in 切换 and 按住", ok, d);
            }

            // ---- W16-11 PngCache: a non-blocking caller (render thread) never stats the file; Put = no second decode
            {
                // (no real thread here: threads started from a module initializer wait for it -> the Background job runs inline
                // and marks itself, which is what "not on the calling thread" means for the cache)
                bool inBg = false; int stampOnMain = 0, decodes = 0; long now = 0;
                bool broken = true;
                PngCache c = new PngCache();
                c.Stamp = delegate(string n) { if (!inBg) stampOnMain++; return broken ? 5 : 6; };
                c.Decode = delegate(string n, long st) { decodes++; return broken ? null : new PngImage(new int[4], 2, 2, n + st); };
                c.NowMs = delegate { return now; };
                c.Background = delegate(Action a) { inBg = true; try { a(); } finally { inBg = false; } };
                PngImage g1 = c.Get("x.png", false);            // queued, decode fails
                PngImage g2 = c.Get("x.png", false);            // within RetryMs: remembered failure
                now += PngCache.RetryMs + 1;
                PngImage g3 = c.Get("x.png", false);            // retry: stat on the background, unchanged -> still null
                broken = false; now += PngCache.RetryMs + 1;
                PngImage g4 = c.Get("x.png", false);            // retry: changed -> loads on the background
                PngImage g5 = c.Get("x.png", false);
                int dec0 = decodes;
                c.Put("y.png", new PngImage(new int[4], 2, 2, "y"));
                PngImage g6 = c.Get("y.png", true);
                Check("V502-W16-11 自定义图片: the render thread never stats the file (0 stat calls on it, first use, retries, change), a failed picture stays empty, a changed one loads; an upload's decoded picture is stored (no second decode)",
                    stampOnMain == 0 && g1 == PngImage.Loading && g2 == null && g3 == null && g5 != null && g5 != PngImage.Loading && g6 != null && decodes == dec0,
                    "stampOnMain=" + stampOnMain + " g1=" + (g1 == PngImage.Loading ? "loading" : (g1 == null ? "null" : "img")) + " g2=" + (g2 == null) + " g3=" + (g3 == null) + " g4=" + (g4 == null ? "null" : "x") + " g5=" + (g5 != null) + " decodes=" + decodes);
            }

            // ---- W16-12 source checks: manifest, Overlay.cs threads / priorities / timer, upload path
            {
                string dir = W14SrcDir();
                string man = File.ReadAllText(Path.Combine(dir, "app.manifest"));
                string ov = File.ReadAllText(Path.Combine(dir, "Overlay.cs"));
                string cc = File.ReadAllText(Path.Combine(dir, "CustomCrosshair.cs"));
                bool admin = man.Contains("level=\"requireAdministrator\"") && !man.Contains("level=\"asInvoker\"");
                bool prio = ov.Contains("thread.Priority = ThreadPriority.AboveNormal") && ov.Contains("OptOutOfPowerThrottling()") && ov.Contains("MakeThreadPerMonitorAware()");
                bool timer = W14Count(ov, "timeBeginPeriod(1)") == 2 && ov.Contains("if (!pacer.UsingPrimary) { Native.timeBeginPeriod(1); periodSet = true; }");
                bool zero = ov.Contains("Marshal.Copy(new int[S * S], 0, blank.Bits, S * S)") && !ov.Contains("dib.Present(hwnd, x, y, 0)");
                bool once = W14Count(cc, "DecodeBytes(") == 3 && cc.Contains("BeginImport(");
                bool noDebug = !ov.Contains("DebugInfo") && !ov.Contains("RequestReset");
                Check("V502-W16-12 app.manifest requireAdministrator; overlay thread AboveNormal, power throttling opted out, per-monitor DPI on the overlay threads; timeBeginPeriod only on the Sleep(1) fallback; 开镜 bitmap all zero (no alpha-0 present); one decode per upload + BeginImport; no dead debug snapshot / RequestReset",
                    admin && prio && timer && zero && once && noDebug, "admin=" + admin + " prio=" + prio + " timer=" + timer + " zero=" + zero + " once=" + once + " debug=" + noDebug);
            }

            // ---- W16-13 numbers for V502-W16.md: window size, us per frame, pushes per second (scripted player, Leo default)
            {
                string line = "";
                foreach (int hz in new int[] { 144, 240, 360 })
                {
                    W16Sim f = new W16Sim(hz, "hold");
                    InputState w = new InputState(); w.Fwd = true;
                    InputState wd = new InputState(); wd.Fwd = true; wd.Right = true;
                    Stopwatch sw = new Stopwatch();
                    int frames = hz * 30;
                    for (int i = 0; i < hz; i++) f.Step(idle);
                    int up0 = f.Uploads, mv0 = f.Moves, bl0 = f.Blanks; long ticks = 0;
                    for (int i = 0; i < frames; i++)
                    {
                        double s = i / (double)hz % 3.0; // 3 s pattern: W 0.7 s, idle 0.5 s, WD 0.7 s, 开镜 0.6 s, idle 0.5 s
                        InputState inp = s < 0.7 ? w : (s < 1.2 ? idle : (s < 1.9 ? wd : (s < 2.5 ? ads : idle)));
                        long t0 = Stopwatch.GetTimestamp(); f.Step(inp); ticks += Stopwatch.GetTimestamp() - t0;
                    }
                    double us = ticks * 1e6 / Stopwatch.Frequency / frames;
                    double pushes = (f.Uploads - up0 + f.Moves - mv0 + f.Blanks - bl0) / 30.0;
                    line += hz + " Hz: window " + f.MaxS + "x" + f.MaxS + " px, " + us.ToString("0.0") + " us/frame, " + pushes.ToString("0.0") + " pushes/s ("
                        + ((f.Moves - mv0) / 30.0).ToString("0.0") + " moves without upload); ";
                }
                Check("V502-W16-13 perf (this host, pure loop): " + line, true, "");
            }
        }

        /// <summary>One 开镜 cycle: 30 frames ADS (按住 held / 切换 pressed once), then 60 frames visible.</summary>
        static void W16Cycle(W16Sim f, string mode, InputState ads, InputState idle)
        {
            if (mode == "hold") { for (int i = 0; i < 30; i++) f.Step(ads); }
            else { f.Step(ads); for (int i = 0; i < 29; i++) f.Step(idle); }
            if (mode == "toggle") { f.Step(ads); for (int i = 0; i < 59; i++) f.Step(idle); }
            else { for (int i = 0; i < 60; i++) f.Step(idle); }
        }
    }
}
