// v5.1.1 (X12, Leo 21 Nov 2026): (1) recoil rises straight up from the CURRENT point (the 站立 point standing still, the
// mirrored one after 换手, the moved spot while moving), not from the screen centre; lean keeps X8's rule. (2) W/S use the
// A/D tap / reversal logic: with the 站立 point on, W and S can sit on the same side of the stand point (Leo默认: W 10.8,
// S 39.6), and a W<->S switch now still goes back to rest first and starts out slowly (>= ReverseMinMs), like A<->D.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X12Init() { V511X12Tests(); }

        static GlobalSettings X12G()
        {
            GlobalSettings g = X5G(75); g.OnlyWhenGameFocused = false;
            return g;
        }

        /// <summary>Runs a key script (live path: LiveInputFilter + engine) on the built-in 站立 profile at fps; perFrame(t, e).</summary>
        static MotionEngine X12Script(Profile p, GlobalSettings g, List<X4Press> ps, double totalMs, double fps, Action<double, MotionEngine> perFrame)
        {
            MotionEngine e = new MotionEngine(); e.GameFocused = true; e.OwnForeground = true;
            LiveInputFilter f = new LiveInputFilter();
            double dt = 1000.0 / fps;
            int n = (int)Math.Ceiling(totalMs / dt);
            for (int k = 0; k < n; k++)
            {
                double t = k * dt;
                e.Update(dt / 1000.0, f.Next(X4Keys(ps, t), true, true), p, g, PreviewMode.Live, 1.0);
                if (perFrame != null) perFrame(t, e);
            }
            return e;
        }

        static void V511X12Tests()
        {
            GlobalSettings g = X12G();
            // X12-1 / X12-2 standing, hold fire to the top: = 站立 point + (0, -RecoilMaxPx); x constant (< 0.01 px) every frame;
            // 换手 the same from the mirrored 站立 point
            for (int h = 0; h < 2; h++)
            {
                Profile p = X18Pre("x12");
                bool hand1 = h == 1;
                double sx = hand1 ? -24.48 : 24.48;
                double worstX = 0;
                InputState fire = new InputState(); fire.Fire = true;
                MotionEngine e = X8Run(p, g, hand1, fire, 4, delegate(MotionEngine m) { worstX = Math.Max(worstX, Math.Abs(m.OffsetX - sx)); });
                double ex = e.OffsetX, ey = e.OffsetY;
                // release: comes straight back down
                InputState none = new InputState();
                for (int k = 0; k < 1440; k++) { e.Update(1.0 / 360, none, p, g, PreviewMode.Live, 1.0); worstX = Math.Max(worstX, Math.Abs(e.OffsetX - sx)); }
                bool ok = p.RecoilDriftX == 0 && Math.Abs(ex - sx) < 0.01 && Math.Abs(ey - (25.2 - p.RecoilMaxPx)) < 0.01 && worstX < 0.01
                    && Math.Abs(e.OffsetY - 25.2) < 0.01;
                Check("X12-" + (h + 1) + (hand1 ? " 换手" : "") + " standing + fire: top = 站立 point (" + F(sx) + ", 25.2) + (0, -" + F(p.RecoilMaxPx) + "), straight vertical (x within 0.01 px every frame), back to the 站立 point on release",
                    ok, "top " + F(ex) + "," + F(ey) + " worst x " + worstX + " after release y " + F(e.OffsetY));
            }
            // X12-3 moving (W held) + fire: rises from where the movement put it
            {
                Profile p = X18Pre("x12");
                MotionEngine w = X8Run(p, g, false, W23Keys(0, 0), 3, null);
                InputState wf = W23Keys(0, 0); wf.Fire = true;
                MotionEngine wfe = X8Run(p, g, false, wf, 4, null);
                bool ok = Math.Abs(wfe.OffsetX - w.OffsetX) < 0.01 && Math.Abs(wfe.OffsetY - (w.OffsetY - p.RecoilMaxPx)) < 0.01;
                Check("X12-3 W held + fire: top = the W spot + (0, -RecoilMaxPx)", ok, "W " + F(w.OffsetX) + "," + F(w.OffsetY) + " W+fire " + F(wfe.OffsetX) + "," + F(wfe.OffsetY));
            }
            // X12-4 W tap shorter than TapThresholdMs (200 ms): moves only a little (< 25 % of the W offset from the 站立 point)
            // and comes back to the 站立 point exactly
            foreach (double held in new double[] { 60, 100, 150 })
            {
                Profile p = X18Pre("x12");
                List<X4Press> ps = new List<X4Press>(); ps.Add(new X4Press(100, 100 + held, 'W'));
                double peak = 0;
                MotionEngine e = X12Script(p, g, ps, 2500, 144, delegate(double t, MotionEngine m) { peak = Math.Max(peak, Math.Max(Math.Abs(m.MoveX), Math.Abs(m.MoveY))); });
                double full = Math.Max(Math.Abs(p.Dirs[0].OffX), Math.Abs(p.Dirs[0].OffY));
                bool ok = held < g.TapThresholdMs && peak < 0.25 * full && e.MoveX == 0 && e.MoveY == 0 && !e.Traveling(0) && !e.Traveling(1) && e.TapCount >= 1 && e.LastTap;
                // TapCount counts per axis: W moves both axes on Leo默认 (X to the centre, Y), so one W tap = 2
                Check("X12-4 W tap " + held + " ms (< 短按 " + F(g.TapThresholdMs) + " ms): peak " + F(peak) + " px of " + F(full) + ", back at the 站立 point, counted as 短按", ok, "taps " + e.TapCount + " end " + e.MoveX + "," + e.MoveY);
            }
            // X12-5 W held, then S (W -> S reversal): Y goes back to rest (the 站立 height, MoveY 0) first, the whole reversal takes
            // >= ReverseMinMs, then lands exactly on S; S -> W the same way back
            foreach (string order in new string[] { "WS", "SW" })
            {
                Profile p = X18Pre("x12");
                List<X4Press> ps = new List<X4Press>(); ps.Add(new X4Press(0, 1500, order[0])); ps.Add(new X4Press(1500, 4000, order[1]));
                double tRest = -1, tLand = -1, minY = double.MaxValue; bool sawRev = false;
                int d2 = order[1] == 'S' ? 1 : 0;
                double ty = p.Dirs[d2].OffY;
                MotionEngine e = X12Script(p, g, ps, 3500, 240, delegate(double t, MotionEngine m)
                {
                    if (t < 1500) return;
                    if (m.Reversing(1)) sawRev = true;
                    if (Math.Abs(m.MoveY) < minY) { minY = Math.Abs(m.MoveY); tRest = t; } // the to-zero leg ends mid-frame, so 0 itself is between frames
                    if (tLand < 0 && m.MoveY == ty && !m.Traveling(1)) tLand = t;
                });
                double took = tLand - 1500;
                bool ok = sawRev && minY < 0.05 && tRest >= 1500 && tLand > tRest && took >= g.ReverseMinMs - 1e-6 && e.MoveY == ty && e.MoveX == p.Dirs[d2].OffX;
                Check("X12-5 " + order[0] + " -> " + order[1] + ": back to rest first (closest " + F(minY) + " px at " + F(tRest - 1500) + " ms), lands on " + order[1] + " exactly after " + F(took) + " ms (>= 换向最短 " + F(g.ReverseMinMs) + " ms)",
                    ok, "sawRev " + sawRev + " rest " + tRest + " land " + tLand);
            }
            // X12-6 rapid W/S alternation (clean and sloppy, 60-200 ms, 3 fps): no build-up: the Y offset in the last second <= 5 px
            // from rest, never more than 3 single taps, settles at the 站立 point; X never past the W/S x (-24.48)
            {
                bool ok = true; string bad = ""; double worstLate = 0, worstMax = 0; int runs = 0;
                foreach (double fps in new double[] { 60, 144, 240 })
                    foreach (double per in new double[] { 60, 80, 100, 120, 150, 200 })
                        for (int seed = 0; seed < 3; seed++)
                        {
                            Profile p = X18Pre("x12");
                            Random rnd = new Random(seed * 131 + (int)per);
                            List<X4Press> ps = seed == 0 ? X4Alt("WS", per, 3000, 0, 0, rnd, 0) : X4Alt("WS", per, 3000, rnd.Next(0, 41), 0.3, rnd, 0);
                            double last = 0, longest = 0;
                            foreach (X4Press q in ps) { last = Math.Max(last, q.E); longest = Math.Max(longest, q.E - q.S); }
                            double late = 0, mx = 0, xs = 0;
                            MotionEngine e = X12Script(p, g, ps, last + 3000, fps, delegate(double t, MotionEngine m)
                            {
                                if (t >= last) return;
                                double y = Math.Abs(m.MoveY);
                                if (y > mx) mx = y;
                                if (t >= last - 1000 && y > late) late = y;
                                if (Math.Abs(m.MoveX) > xs) xs = Math.Abs(m.MoveX);
                            });
                            runs++;
                            double tap = Math.Abs(p.Dirs[1].OffY) * MotionCurve.Progress(p.CurveWS.Speeds, Math.Min(1.0, longest / p.CurveWS.DurationMs));
                            if (per <= 150) worstLate = Math.Max(worstLate, late); worstMax = Math.Max(worstMax, mx);
                            bool settled = e.MoveX == 0 && e.MoveY == 0 && !e.Traveling(0) && !e.Traveling(1);
                            if ((per <= 150 && late > 5) || mx > 3 * tap + 1 || xs > 24.48 + 1e-9 || !settled)
                            { ok = false; if (bad.Length < 400) bad += per + " @" + fps + " s" + seed + " late " + F(late) + " max " + F(mx) + " (3 taps " + F(3 * tap + 1) + ") x " + F(xs) + " settled " + settled + "; "; }
                        }
                Check("X12-6 rapid W/S alternation (" + runs + " runs, 60-200 ms): last second <= 5 px from rest (presses <= 150 ms), always <= 3 single presses + 1 px, settles at the 站立 point", ok,
                    bad == "" ? "worst last second " + F(worstLate) + " px, worst max " + F(worstMax) + " px" : bad);
            }
            // X12-7 held W, S, A, D and the 4 diagonals land exactly on their stored offsets (from rest and after a W<->S / diagonal
            // reversal), both hands' 站立 profile
            {
                bool ok = true; string bad = "";
                string[] keys = { "W", "S", "A", "D", "WA", "WD", "SA", "SD" };
                for (int d = 0; d < 8; d++)
                    foreach (string pre in new string[] { "", "W", "S", "SA", "WD" })
                    {
                        Profile p = X18Pre("x12");
                        List<X4Press> ps = new List<X4Press>();
                        double t0 = 0;
                        if (pre != "") { foreach (char c in pre) ps.Add(new X4Press(0, 800, c)); t0 = 800; }
                        foreach (char c in keys[d]) ps.Add(new X4Press(t0, t0 + 3000, c));
                        MotionEngine e = X12Script(p, g, ps, t0 + 2500, 144, null);
                        if (e.MoveX != p.Dirs[d].OffX || e.MoveY != p.Dirs[d].OffY || e.Traveling(0) || e.Traveling(1))
                        { ok = false; bad += pre + "->" + keys[d] + " " + e.MoveX + "," + e.MoveY + " want " + p.Dirs[d].OffX + "," + p.Dirs[d].OffY + "; "; }
                    }
                Check("X12-7 held W/S/A/D/diagonals land exactly on the stored offsets (from rest and after W, S, SA, WD)", ok, bad);
            }
            // X12-8 text: the 站立 notes say recoil rises from the current point (not from the centre)
            {
                string dir = UiSrcDir();
                string mv = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "SettingsForm.PageMove.cs")), ht = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "HelpText.cs")),
                    gt = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "GuideText.cs"));
                bool ok = !mv.Contains("侧身和后坐力照旧从屏幕中心算起") && !ht.Contains("侧身（Q / E）和后坐力照旧从屏幕中心算起") && !gt.Contains("和后坐力上抬照旧从屏幕中心算起")
                    && mv.Contains("后坐力从准星当前的位置往上抬") && ht.Contains("后坐力从准星当前的位置往上抬");
                Check("X12-8 移动 page / help / guide: recoil rises from the current point; lean still from the centre", ok, "");
            }
        }
    }
}
