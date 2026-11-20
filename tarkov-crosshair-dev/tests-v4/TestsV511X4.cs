// v5.1.1 (X4, Leo feedback B6): rapid W A S D alternation must never build up offset. Before the fix, A/D every 60-150 ms
// ratcheted the crosshair to the full A offset (97.2 px on Leo's default profile): the way back started slowly and the next
// press climbed again from high up the ramp. Now every way back runs down the ramp (the outward travel played backwards)
// and a press is undone in at most its own held time (MotionEngine.BackMs). Single holds from rest are unchanged (v5.0.2).
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X4Init() { V511X4Tests(); }

        sealed class X4Press { public double S, E; public char K; public X4Press(double s, double e, char k) { S = s; E = e; K = k; } }

        static InputState X4Keys(List<X4Press> ps, double t)
        {
            InputState i = new InputState();
            foreach (X4Press p in ps)
            {
                if (t < p.S || t >= p.E) continue;
                if (p.K == 'W') i.Fwd = true; else if (p.K == 'S') i.Back = true; else if (p.K == 'A') i.Left = true; else if (p.K == 'D') i.Right = true;
            }
            return i;
        }

        /// <summary>Presses of keys[k % n], each period ms (± jitter, 0..1) long, the next one starting overlap ms before it ends.</summary>
        static List<X4Press> X4Alt(string keys, double period, double totalMs, double overlap, double jitter, Random rnd, double t0)
        {
            List<X4Press> ps = new List<X4Press>();
            double t = 0; int k = 0;
            while (t < totalMs)
            {
                double d = period * (1 + jitter * (rnd.NextDouble() * 2 - 1));
                ps.Add(new X4Press(t0 + t, t0 + t + d + overlap, keys[k % keys.Length]));
                t += d; k++;
            }
            return ps;
        }

        sealed class X4Result
        {
            public double Max, MaxLate, AtLast, MaxStep, SettledX = double.NaN, SettledY = double.NaN, LastKey, LongestPress;
            public bool Finite = true, Settled;
        }

        /// <summary>Runs a key script through the live path (LiveInputFilter + engine) on Leo's default profile.
        /// Max = largest |MoveX|,|MoveY| while keys are used; MaxLate = the same over the last second of key use.</summary>
        static X4Result X4Run(List<X4Press> ps, double fps, Action<GlobalSettings> tune)
        {
            GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false;
            if (tune != null) tune(g);
            Profile p = g.Active;
            MotionEngine e = new MotionEngine();
            LiveInputFilter f = new LiveInputFilter();
            X4Result r = new X4Result();
            foreach (X4Press q in ps) { r.LastKey = Math.Max(r.LastKey, q.E); r.LongestPress = Math.Max(r.LongestPress, q.E - q.S); }
            double dt = 1000.0 / fps, px = 0, py = 0;
            int n = (int)Math.Ceiling((r.LastKey + 4000) / dt);
            for (int k = 0; k < n; k++)
            {
                double t = k * dt;
                InputState i = f.Next(X4Keys(ps, t), true, true);
                e.Update(dt / 1000.0, i, p, g, PreviewMode.Live, 1.0);
                double x = e.MoveX, y = e.MoveY;
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) { r.Finite = false; break; }
                double m = Math.Max(Math.Abs(x), Math.Abs(y));
                if (t < r.LastKey)
                {
                    if (m > r.Max) r.Max = m;
                    if (t >= r.LastKey - 1000 && m > r.MaxLate) r.MaxLate = m;
                    r.AtLast = m;
                }
                r.MaxStep = Math.Max(r.MaxStep, Math.Max(Math.Abs(x - px), Math.Abs(y - py)));
                px = x; py = y;
            }
            r.SettledX = e.MoveX; r.SettledY = e.MoveY;
            r.Settled = e.MoveX == 0 && e.MoveY == 0 && !e.Traveling(0) && !e.Traveling(1);
            return r;
        }

        /// <summary>Peak of ONE press of heldMs from rest on Leo's default profile (largest axis offset): the yardstick for "small".</summary>
        static double X4TapPeak(double heldMs)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile p = g.Active;
            double m = 0;
            for (int d = 0; d < 4; d++) m = Math.Max(m, Math.Max(Math.Abs(p.Dirs[d].OffX), Math.Abs(p.Dirs[d].OffY)));
            double T = Math.Min(p.CurveAD.DurationMs, p.CurveWS.DurationMs);
            return m * MotionCurve.Progress(p.CurveAD.Speeds, T > 0 ? heldMs / T : 1);
        }

        static void V511X4Tests()
        {
            double[] rates = { 60, 144, 240 };

            // X4-1 a real hold from rest still follows the curve exactly (v5.0.2 travel: target x Progress(t / 总时长)), 8 directions
            {
                bool ok = true; string bad = "";
                foreach (double fps in rates)
                    for (int d = 0; d < 8 && ok; d++)
                    {
                        GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false;
                        Profile p = g.Active;
                        MotionEngine e = new MotionEngine();
                        InputState i = new InputState();
                        string[] keys = { "W", "S", "A", "D", "WA", "WD", "SA", "SD" };
                        foreach (char c in keys[d]) { if (c == 'W') i.Fwd = true; if (c == 'S') i.Back = true; if (c == 'A') i.Left = true; if (c == 'D') i.Right = true; }
                        MotionEngine full = new MotionEngine();
                        for (int k = 0; k < (int)(fps * 2); k++) full.Update(1.0 / fps, i, p, g, PreviewMode.Live, 1.0);
                        double tx = full.MoveX, ty = full.MoveY, t = 0;
                        for (int k = 0; k < (int)(fps * 0.6) && ok; k++)
                        {
                            e.Update(1.0 / fps, i, p, g, PreviewMode.Live, 1.0);
                            t += (1.0 / fps) * 1000.0;
                            double ex = tx * (t >= p.CurveAD.DurationMs ? 1 : MotionCurve.Progress(p.CurveAD.Speeds, t / p.CurveAD.DurationMs));
                            double ey = ty * (t >= p.CurveWS.DurationMs ? 1 : MotionCurve.Progress(p.CurveWS.Speeds, t / p.CurveWS.DurationMs));
                            if (Math.Abs(e.MoveX - ex) > 1e-12 * (1 + Math.Abs(ex)) || Math.Abs(e.MoveY - ey) > 1e-12 * (1 + Math.Abs(ey)))
                            { ok = false; bad = keys[d] + " @" + fps + " t " + F(t) + " x " + e.MoveX + " want " + ex + " y " + e.MoveY + " want " + ey; }
                        }
                        if (ok && (e.MoveX != tx || e.MoveY != ty || e.Traveling(0) || e.Traveling(1))) { ok = false; bad = keys[d] + " not on target"; }
                    }
                Check("X4-1 hold from rest = v5.0.2 travel (target x Progress(t/总时长)), 8 dirs x 60/144/240 fps", ok, bad);
            }

            // X4-2 clean A/D alternation every 60..200 ms (no overlap): stays within 2 single taps of the press length + 0.5 px,
            // the last second is no worse than that (no build-up), settles at exactly 0
            {
                bool ok = true; string bad = "", worst = "";
                foreach (double fps in rates)
                    foreach (double per in new double[] { 60, 80, 100, 120, 150, 200 })
                    {
                        X4Result r = X4Run(X4Alt("AD", per, 3000, 0, 0, new Random(1), 0), fps, null);
                        double lim = 2 * X4TapPeak(r.LongestPress) + 0.5;
                        if (!r.Finite || r.Max > lim || r.MaxLate > lim || !r.Settled) { ok = false; bad += "A/D " + per + " @" + fps + " max " + F(r.Max) + " late " + F(r.MaxLate) + " lim " + F(lim) + " settled " + r.Settled + "; "; }
                        worst = "e.g. A/D 100 @144 lim " + F(2 * X4TapPeak(100) + 0.5);
                    }
                Check("X4-2 clean A/D every 60-200 ms x 3 fps: max <= 2 x one tap + 0.5 px, no build-up, settles at 0", ok, bad == "" ? worst : bad);
            }

            // X4-3 sloppy spamming (±30 % timing, the next key pressed 0-40 ms before the last is let go; last-pressed-wins
            // filter): A/D, W/S, W A S D cycle, mixed 8-key sequence; 3 fps x 6 periods x 4 seeds
            {
                bool ok = true; string bad = ""; int runs = 0; double worstRatio = 0;
                string[] seqs = { "AD", "WS", "WASD", "ADSDWDAS" };
                foreach (string seq in seqs)
                    foreach (double fps in rates)
                        foreach (double per in new double[] { 60, 80, 100, 120, 150, 200 })
                            for (int seed = 0; seed < 4; seed++)
                            {
                                Random rnd = new Random(seed * 7919 + (int)per);
                                X4Result r = X4Run(X4Alt(seq, per, 3000, rnd.Next(0, 41), 0.3, rnd, 0), fps, null);
                                runs++;
                                double lim = 3 * X4TapPeak(r.LongestPress) + 1.0;
                                worstRatio = Math.Max(worstRatio, r.Max / lim);
                                if (!r.Finite || r.Max > lim || !r.Settled) { ok = false; if (bad.Length < 400) bad += seq + " " + per + " @" + fps + " s" + seed + " max " + F(r.Max) + " lim " + F(lim) + " settled " + r.Settled + "; "; }
                            }
                Check("X4-3 sloppy W A S D spam (" + runs + " runs, ±30 % timing, overlaps): within 3 x one tap (longest press) + 1 px, settles at 0", ok,
                    bad == "" ? "worst max/limit " + F(worstRatio) : bad);
            }

            // X4-4 lopsided spam (A 100 / D 70 ms, A 120 / D 60 ms for 6 s). More A than D is a real net move left in the game, so a
            // steady part offset is right; it must not keep building: 100/70 stays within 3 single A taps and its last second is
            // no higher than seconds 2-3 (+0.5 px); 120/60 stays below half the A offset
            foreach (double[] ad in new double[][] { new double[] { 100, 70 }, new double[] { 120, 60 } })
            {
                List<X4Press> ps = new List<X4Press>();
                double c = ad[0] + ad[1];
                for (int k = 0; k * c < 6000; k++) { ps.Add(new X4Press(k * c, k * c + ad[0], 'A')); ps.Add(new X4Press(k * c + ad[0], k * c + c, 'D')); }
                List<X4Press> first3 = new List<X4Press>();
                foreach (X4Press q in ps) if (q.S < 3000) first3.Add(q);
                bool ok = true; string det = "";
                double full = Math.Abs(GlobalSettings.CreateDefault().Active.Dirs[2].OffX);
                foreach (double fps in rates)
                {
                    X4Result r = X4Run(ps, fps, null), early = X4Run(first3, fps, null);
                    double lim = ad[1] >= 70 ? 3 * X4TapPeak(ad[0]) + 0.5 : 0.5 * full;
                    det += "@" + fps + " max " + F(r.Max) + " s2-3 " + F(early.MaxLate) + " last s " + F(r.MaxLate) + " (lim " + F(lim) + ") ";
                    // 120 / 60 is a 2:1 duty (a real net move left): it hovers (28-42 px over 20 s in the X4 sim), so only the bound
                    if (!r.Finite || r.Max > lim || (ad[1] >= 70 && r.MaxLate > early.MaxLate + 0.5) || !r.Settled) ok = false;
                }
                Check("X4-4 lopsided spam A " + ad[0] + " / D " + ad[1] + " ms for 6 s: steady, no build-up, bounded", ok, det);
            }

            // X4-5 a short tap goes out and comes back along the same path (the way back = the outward travel played backwards)
            foreach (double held in new double[] { 50, 100, 150 })
            {
                GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false;
                Profile p = g.Active;
                MotionEngine e = new MotionEngine();
                const double fps = 240;
                int n = (int)Math.Round(held * fps / 1000);
                List<double> outX = new List<double>();
                InputState a = new InputState(); a.Left = true;
                outX.Add(0);
                for (int k = 0; k < n; k++) { e.Update(1 / fps, a, p, g, PreviewMode.Live, 1); outX.Add(e.MoveX); }
                double peak = e.MoveX, worst = 0;
                for (int k = 1; k <= n; k++)
                {
                    e.Update(1 / fps, new InputState(), p, g, PreviewMode.Live, 1);
                    worst = Math.Max(worst, Math.Abs(e.MoveX - outX[n - k]));
                }
                bool ok = worst < 1e-9 && e.MoveX == 0 && !e.Traveling(0) && peak > 0 && peak <= X4TapPeak(held) + 1e-9;
                Check("X4-5 tap " + held + " ms: small (peak " + F(peak) + " px), back at 0 after " + held + " ms along the same path", ok, "max diff " + worst);
            }

            // X4-6 spam, then a real hold: the hold reaches its target exactly and stays; release returns to 0
            {
                bool ok = true; string det = "";
                foreach (double fps in rates)
                {
                    List<X4Press> ps = X4Alt("ADAD", 80, 1500, 20, 0.3, new Random(66), 0);
                    double t0 = 0; foreach (X4Press q in ps) t0 = Math.Max(t0, q.E);
                    ps.Add(new X4Press(t0, t0 + 1500, 'D'));
                    GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false;
                    Profile p = g.Active;
                    MotionEngine e = new MotionEngine(); LiveInputFilter f = new LiveInputFilter();
                    double dt = 1000 / fps; bool onTarget = false;
                    for (double t = 0; t < t0 + 1500; t += dt)
                    {
                        e.Update(dt / 1000, f.Next(X4Keys(ps, t), true, true), p, g, PreviewMode.Live, 1);
                        if (t > t0 + 1000) onTarget = e.MoveX == p.Dirs[3].OffX && !e.Traveling(0);
                    }
                    for (int k = 0; k < (int)(fps * 2); k++) e.Update(dt / 1000, f.Next(new InputState(), true, true), p, g, PreviewMode.Live, 1);
                    det += "@" + fps + " onTarget " + onTarget + " end " + F(e.MoveX) + " ";
                    if (!onTarget || e.MoveX != 0) ok = false;
                }
                Check("X4-6 spam then hold D: D target exactly after 总时长 + spam; release back to 0", ok, det);
            }

            // X4-7 hold A (full offset), then spam D/A: comes down toward rest, never a jump (no frame step above
            // AltReturnMaxSpeed x the largest frame step of a normal hold from rest, + 10 %)
            {
                bool ok = true; string det = "";
                foreach (double fps in rates)
                {
                    List<X4Press> hold = new List<X4Press>(); hold.Add(new X4Press(0, 800, 'A'));
                    double normStep = X4Run(hold, fps, null).MaxStep;
                    List<X4Press> ps = new List<X4Press>(); ps.Add(new X4Press(0, 800, 'A'));
                    ps.AddRange(X4Alt("DA", 90, 2000, 10, 0.3, new Random(77), 800));
                    X4Result r = X4Run(ps, fps, null);
                    double full = Math.Abs(GlobalSettings.CreateDefault().Active.Dirs[2].OffX);
                    bool down = r.MaxLate <= 0.5 * full;
                    bool smooth = r.MaxStep <= MotionEngine.AltReturnMaxSpeed * normStep * 1.1 + 1e-9; // + 10 %: frame rounding at the top of the ramp
                    det += "@" + fps + " late max " + F(r.MaxLate) + "/" + F(full) + " step " + F(r.MaxStep) + " vs hold " + F(normStep) + " ";
                    if (!down || !smooth || !r.Settled) ok = false;
                }
                Check("X4-7 hold A then spam D/A: last second <= half the A offset, no frame step > " + MotionEngine.AltReturnMaxSpeed + " x a hold's", ok, det);
            }

            // X4-8 the tunables still work while spamming: 回程时间 50/200 %, 换向停顿 80 ms, 换向最短时间 300 ms, 短按阈值 250 ms
            {
                bool ok = true; string det = "";
                List<X4Press> ps = X4Alt("AD", 90, 3000, 15, 0.3, new Random(88), 0);
                X4Result baseR = X4Run(ps, 144, null);
                double lim = 3 * X4TapPeak(baseR.LongestPress) + 0.5;
                foreach (double pct in new double[] { 50, 200 })
                {
                    X4Result r = X4Run(ps, 144, delegate(GlobalSettings g) { g.ReturnTimePct = pct; });
                    det += "回程 " + pct + "% max " + F(r.Max) + " ";
                    if (!r.Finite || r.Max > (pct > 100 ? 2 * lim : lim) || !r.Settled) ok = false;
                    if (Math.Abs(r.Max - baseR.Max) < 1e-9) { ok = false; det += "(no effect) "; }
                }
                X4Result rp = X4Run(ps, 144, delegate(GlobalSettings g) { g.ReversePauseMs = 80; });
                X4Result rm = X4Run(ps, 144, delegate(GlobalSettings g) { g.ReverseMinMs = 300; });
                det += "停顿80 max " + F(rp.Max) + " 最短300 max " + F(rm.Max) + " ";
                if (rp.Max > lim || rm.Max > lim || !rp.Settled || !rm.Settled) ok = false;
                // 短按阈值 labels the spam presses
                GlobalSettings gt = GlobalSettings.CreateDefault(); gt.OnlyWhenGameFocused = false; gt.TapThresholdMs = 250;
                MotionEngine et = new MotionEngine(); LiveInputFilter ft = new LiveInputFilter();
                for (double t = 0; t < 3200; t += 1000 / 144.0) et.Update(1 / 144.0, ft.Next(X4Keys(ps, t), true, true), gt.Active, gt, PreviewMode.Live, 1);
                det += "taps " + et.TapCount;
                if (et.TapCount < 10) ok = false;
                Check("X4-8 tunables while spamming: 回程时间 50/200 % change it and stay bounded, 换向停顿 / 换向最短时间 bounded, 短按 counted", ok, det);
            }

            // X4-9 回程时间 / 换向停顿 come from the settings (移动 page sliders); the engine fields only override when set
            {
                GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false; g.ReturnTimePct = 200;
                Profile p = g.Active;
                MotionEngine e = new MotionEngine();
                InputState a = new InputState(); a.Left = true;
                for (int k = 0; k < 36; k++) e.Update(1 / 240.0, a, p, g, PreviewMode.Live, 1); // 150 ms
                int back = 0;
                do { e.Update(1 / 240.0, new InputState(), p, g, PreviewMode.Live, 1); back++; } while (e.Traveling(0) && back < 2000);
                MotionEngine o = new MotionEngine(); o.ReturnTimePct = 50;
                for (int k = 0; k < 36; k++) o.Update(1 / 240.0, a, p, g, PreviewMode.Live, 1);
                int back2 = 0;
                do { o.Update(1 / 240.0, new InputState(), p, g, PreviewMode.Live, 1); back2++; } while (o.Traveling(0) && back2 < 2000);
                bool ok = Math.Abs(back - 72) <= 1 && Math.Abs(back2 - 18) <= 1;
                Check("X4-9 回程时间 from the settings (200 %: 150 ms tap back in 300 ms); engine field overrides (50 %: 75 ms)", ok,
                    "frames back " + back + " (want 72), override " + back2 + " (want 18)");
            }
        }
    }
}
