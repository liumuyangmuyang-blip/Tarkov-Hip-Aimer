using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TarkovCrosshair.Tests
{
    // v2.4 tests: 移动速率曲线, 换手, 保存为默认 / 恢复默认, migration, v2.3 equivalence of everything that remains.
    public static partial class Program
    {
        static void V24Tests()
        {
            C1Curve(); C2EngineCurve(); C3Continuity(); C4Hand(); C5SaveRestore(); C6Migration(); C7SameAsV23(); C8KeysResolution();
        }

        static bool CurveIs(MoveCurve c, double ms)
        {
            if (c == null || c.DurationMs != ms || c.Speeds == null || c.Speeds.Length != 5) return false;
            double[] v = MotionCurve.V23Feel();
            for (int i = 0; i < 5; i++) if (c.Speeds[i] != v[i]) return false;
            return true;
        }

        static Profile M4() { Profile p = Profile.CreateLeoDefaultLegacy(GlobalSettings.DefaultProfileName); return p; } // v5.0: the v3.1 values stay the fixture

        /// <summary>v2.4.4: the built-in M4 with both curves at 100 ms (the v2.4–v2.4.3 default), for tests about curve
        /// mechanics that were written around a 100 ms travel. Every offset still comes from CreateLeoM4.</summary>
        static Profile M4Curves100() { Profile p = M4(); p.CurveAD.DurationMs = 100; p.CurveWS.DurationMs = 100; return p; }

        static MotionEngine Step(MotionEngine e, Profile p, GlobalSettings g, string keys, int frames, double dt)
        {
            for (int i = 0; i < frames; i++) e.Update(dt, In(keys), p, g, PreviewMode.Live, 1);
            return e;
        }

        // ---------------- C1: MotionCurve maths ----------------

        static void C1Curve()
        {
            Random rnd = new Random(20261012);
            List<double[]> curves = new List<double[]>();
            foreach (double[] pr in MotionCurve.Presets) curves.Add(pr);
            for (int i = 0; i < 300; i++)
            {
                double[] c = new double[5];
                for (int k = 0; k < 5; k++) c[k] = i % 3 == 0 ? rnd.Next(101) : rnd.NextDouble() * 100;
                if (i % 17 == 0) c[rnd.Next(5)] = 0;
                curves.Add(c);
            }
            curves.Add(new double[] { 0, 0, 0, 0, 0 });
            curves.Add(new double[] { 0, 0, 100, 0, 0 });
            curves.Add(new double[] { 100, 0, 0, 0, 100 });
            int bad = 0; string first = "";
            foreach (double[] c in curves)
            {
                bool ok = MotionCurve.Progress(c, 0) == 0 && MotionCurve.Progress(c, 1) == 1 && MotionCurve.Progress(c, -0.3) == 0 && MotionCurve.Progress(c, 1.7) == 1;
                double last = 0;
                for (int i = 0; i <= 1000; i++)
                {
                    double u = i / 1000.0, p = MotionCurve.Progress(c, u), s = MotionCurve.SpeedAt(c, u);
                    if (p < last - 1e-12 || p < 0 || p > 1 || s < 0) ok = false;
                    // no overshoot: inside the band of the two neighbouring points
                    int k = Math.Min(3, (int)Math.Floor(u * 4));
                    double lo = Math.Min(c[k], c[k + 1]), hi = Math.Max(c[k], c[k + 1]);
                    if (s < lo - 1e-9 || s > hi + 1e-9) ok = false;
                    last = p;
                }
                for (int k = 0; k < 5; k++) if (Math.Abs(MotionCurve.SpeedAt(c, k / 4.0) - c[k]) > 1e-9) ok = false;
                if (!ok) { bad++; if (first.Length == 0) first = string.Join(",", Array.ConvertAll(c, F)); }
            }
            Check("C1 Progress(0)=0, Progress(1)=1, non-decreasing, SpeedAt ≥ 0, through the 5 points, no overshoot (" + curves.Count + " curves x 1001 points)",
                bad == 0, bad + " bad " + first);

            double[] zero = { 0, 0, 0, 0, 0 }, flat = MotionCurve.Presets[3];
            double dz = 0, df = 0;
            for (int i = 0; i <= 1000; i++)
            {
                double u = i / 1000.0;
                dz = Math.Max(dz, Math.Abs(MotionCurve.Progress(zero, u) - u));
                df = Math.Max(df, Math.Abs(MotionCurve.Progress(flat, u) - u));
            }
            double slowIn = MotionCurve.Progress(MotionCurve.Presets[0], 0.5), slowOut = MotionCurve.Progress(MotionCurve.Presets[1], 0.5);
            Check("C1 all-zero = linear, 匀速 linear, 慢起 P(0.5) < 0.5, 慢停 P(0.5) > 0.5", dz == 0 && df <= 1e-6 && slowIn < 0.5 && slowOut > 0.5,
                "|zero−u|=" + dz.ToString("E1") + " |匀速−u|=" + df.ToString("E1") + " 慢起(0.5)=" + F(slowIn) + " 慢停(0.5)=" + F(slowOut));

            // v4.0: 「v2.3 手感」 removed from the UI list (Presets[4] kept internally for old-file migration).
            bool presets = MotionCurve.PresetNames.Length == 4 && MotionCurve.UserPresetCount == 4 && Same(MotionCurve.Presets[0], 0, 25, 50, 75, 100) && Same(MotionCurve.Presets[1], 100, 75, 50, 25, 0)
                && Same(MotionCurve.Presets[2], 0, 70, 100, 70, 0) && Same(MotionCurve.Presets[3], 100, 100, 100, 100, 100) && Same(MotionCurve.Presets[4], 0, 100, 61, 28, 11)
                && Array.IndexOf(MotionCurve.PresetNames, "v2.3 手感") < 0 && MotionCurve.PresetNames[MotionCurve.RecommendedPreset] == "慢起";
            Check("C1 presets exact", presets, string.Join(" / ", MotionCurve.PresetNames));

            // Informational: 「v2.3 手感」 vs the old critically damped follow (95 % at 总时长).
            double worst = 0;
            const double w = 4.744;
            for (int i = 0; i <= 1000; i++)
            {
                double u = i / 1000.0, old = 1 - (1 + w * u) * Math.Exp(-w * u);
                worst = Math.Max(worst, Math.Abs(MotionCurve.Progress(MotionCurve.Presets[4], u) - old));
            }
            Console.WriteLine("   info: 「v2.3 手感」 progress vs old 跟随 (95 % at 总时长): max |diff| = " + F(worst) +
                " of the distance (the old follow was only 95 % there at that time; the curve is 100 %)");
        }

        static bool Same(double[] a, params double[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------------- C2: engine travel along the curves ----------------

        static void C2EngineCurve()
        {
            GlobalSettings g = G();
            // Held direction reaches EXACTLY its target at t = 总时长, no overshoot, exact path, for every preset; release the same way.
            string badPath = "";
            foreach (double fps in new double[] { 1000, 144, 60 })
                for (int pr = 0; pr < MotionCurve.Presets.Length; pr++)
                {
                    Profile p = M4(); p.CurveAD.SetPreset(pr); p.CurveAD.DurationMs = 100;
                    MotionEngine e = new MotionEngine();
                    double dt = 1.0 / fps, t = 0, target = p.Dirs[2].OffX; // A
                    bool ok = true; double arrived = -1;
                    for (int i = 0; i < (int)(0.3 * fps); i++)
                    {
                        e.Update(dt, In("A"), p, g, PreviewMode.Live, 1);
                        t += dt * 1000;
                        double x = e.MoveX, want = t >= 100 ? target : target * MotionCurve.Progress(p.CurveAD.Speeds, t / 100);
                        if (Math.Abs(x - want) > 1e-6 || x > target + 1e-9 || x < -1e-9) ok = false;
                        if (arrived < 0 && t >= 100 - 1e-9) { arrived = t; if (Math.Abs(x - target) > 1e-6) ok = false; }
                    }
                    t = 0;
                    for (int i = 0; i < (int)(0.3 * fps); i++)
                    {
                        e.Update(dt, In(""), p, g, PreviewMode.Live, 1);
                        t += dt * 1000;
                        double x = e.MoveX, want = t >= 100 ? 0 : target + (0 - target) * MotionCurve.Progress(p.CurveAD.Speeds, t / 100);
                        if (Math.Abs(x - want) > 1e-6 || x > target + 1e-9 || x < -1e-9) ok = false;
                    }
                    if (!ok || e.MoveX != 0) badPath += " " + PName(pr) + "@" + fps;
                }
            Check("C2 hold A: exact curve path, on target at t = 总时长 (±1e-6), no overshoot; release back the same way (5 presets x 1000/144/60 fps)",
                badPath.Length == 0, badPath.Length == 0 ? "all exact" : "bad:" + badPath);

            // X and Y each use their own 总时长 (WA: 左右 100 ms, 前后 300 ms).
            {
                Profile p = M4Curves100(); p.CurveWS.DurationMs = 300;
                MotionEngine e = new MotionEngine();
                Step(e, p, g, "WA", 99, 0.001);
                bool xBefore = e.Traveling(0);
                Step(e, p, g, "WA", 1, 0.001);
                bool xDone = !e.Traveling(0) && e.MoveX == p.Dirs[4].OffX, yMid = e.Traveling(1);
                Step(e, p, g, "WA", 199, 0.001);
                bool yBefore = e.Traveling(1);
                Step(e, p, g, "WA", 1, 0.001);
                bool yDone = !e.Traveling(1) && e.MoveY == p.Dirs[4].OffY;
                Check("C2 左右 and 前后 use their own 总时长 (100 / 300 ms)", xBefore && xDone && yMid && yBefore && yDone,
                    "X travelling at 99 ms=" + xBefore + " X on WA.x at 100 ms=" + xDone + " Y still travelling=" + yMid + " Y on WA.y at 300 ms=" + yDone);
            }

            // A change that keeps an axis's target does not restart that axis (A→D keeps Y = A.y = D.y; Q keeps both).
            {
                Profile p = M4Curves100(); p.Dirs[3].OffY = p.Dirs[2].OffY; Profile.RecomputeDiagonals(p); // Leo's D has y = 3: make A.y = D.y for this check
                MotionEngine e = new MotionEngine();
                Step(e, p, g, "A", 500, 0.001);
                Step(e, p, g, "D", 1, 0.001);
                bool ad = p.Dirs[2].OffY == p.Dirs[3].OffY && e.Traveling(0) && !e.Traveling(1) && e.MoveY == p.Dirs[3].OffY;
                Step(e, p, g, "D", 500, 0.001);
                Step(e, p, g, "QD", 1, 0.001);
                bool q = !e.Traveling(0) && !e.Traveling(1);
                Profile c = M4Curves100(); // v3.0: lean + move always additive, so E added to D never restarts the move either
                MotionEngine ec = new MotionEngine();
                Step(ec, c, g, "D", 500, 0.001);
                Step(ec, c, g, "ED", 1, 0.001);
                bool ed = !ec.Traveling(0) && !ec.Traveling(1);
                Check("C2 only an axis whose target changes restarts", ad && q && ed,
                    "A→D: X restarts, Y (" + F(p.Dirs[2].OffY) + " → " + F(p.Dirs[3].OffY) + ") does not=" + ad + "; Q added: nothing restarts=" + q + "; E added: nothing restarts=" + ed);
            }

            // 总时长 0 = instant; a change mid-travel restarts from where the ring is with the full 总时长.
            {
                Profile z = M4(); z.CurveAD.DurationMs = 0; z.CurveWS.DurationMs = 0;
                double ax = z.Dirs[2].OffX, dx = z.Dirs[3].OffX;
                MotionEngine e = new MotionEngine();
                e.Update(0.001, In("A"), z, g, PreviewMode.Live, 1);
                double x0 = e.MoveX;
                e.Update(0.001, In("D"), z, g, PreviewMode.Live, 1);
                bool inst = x0 == ax && e.MoveX == dx && !e.Traveling(0);
                Profile p = M4Curves100();
                MotionEngine m = new MotionEngine();
                Step(m, p, g, "A", 40, 0.001);
                double at = m.MoveX;
                m.Update(0.001, In("D"), p, g, PreviewMode.Live, 1);
                // v5.1 (W21, decision 7): A->D = back to 0 in the time A took to get there (40 ms; at least MinReverseMs − 总时长
                // = 50 ms here), then out from rest over 总时长
                double d1 = Math.Max(40, MotionEngine.MinReverseMs - 100);
                // v5.1.1 (X4, B6): a tap (not a real hold) goes back DOWN the ramp: curve position u falls linearly from ux
                double ux = MotionCurve.ProgressInverse(p.CurveAD.Speeds, Math.Abs(at / p.Dirs[2].OffX));
                double first = m.MoveX, want1 = at * MotionCurve.Progress(p.CurveAD.Speeds, ux * (1 - 1 / d1)) / MotionCurve.Progress(p.CurveAD.Speeds, ux);
                Step(m, p, g, "D", (int)d1 + 98, 0.001);
                bool still = m.Traveling(0);
                Step(m, p, g, "D", 1, 0.001);
                Check("C2 总时长 0 instant; change mid-travel (A 40 ms -> D): from the ring back to 0 (40 ms, min 150 ms in all), then the full 总时长", inst && Math.Abs(first - want1) < 1e-9 && still && Math.Abs(m.MoveX - dx) < 1e-9 && !m.Traveling(0),
                    "0 ms: A=" + F(x0) + " then D=" + F(e.MoveX) + "; A 40 ms at x=" + F(at) + ", D: 1st frame " + F(first) + " (= x·(1−P(1/" + F(d1) + "))), on D " + F(d1 + 100) + " ms after the press=" + (Math.Abs(m.MoveX - dx) < 1e-9));
            }

            // Live edits and hand blending move the target without restarting the travel.
            {
                Profile p = M4Curves100(); p.HandSwitchMs = 300;
                MotionEngine e = new MotionEngine();
                Step(e, p, g, "A", 30, 0.001);
                double tBefore = e.TravelMs(0);
                p.Dirs[2].OffX = 60; // edit mid-travel
                Step(e, p, g, "A", 1, 0.001);
                bool cont = e.Traveling(0) && Math.Abs(e.TravelMs(0) - (tBefore + 1)) < 1e-9;
                Step(e, p, g, "A", 69, 0.001);
                bool arrive = !e.Traveling(0) && e.MoveX == 60;
                double switchedA = -p.Dirs[3].OffX; // mirror: switched A = −D.x
                MotionEngine h = new MotionEngine();
                Step(h, p, g, "A", 500, 0.001);
                h.Update(0.001, In("AK"), p, g, PreviewMode.Live, 1);
                bool noRestart = true; double worstBlend = 0;
                for (int i = 2; i <= 400; i++)
                {
                    h.Update(0.001, In("A"), p, g, PreviewMode.Live, 1);
                    if (h.Traveling(0) || h.Traveling(1)) noRestart = false;
                    double u = Math.Min(1, i / 300.0), s = u * u * (3 - 2 * u);
                    double want = 60 + (switchedA - 60) * s;
                    worstBlend = Math.Max(worstBlend, Math.Abs(h.MoveX - want));
                }
                Check("C2 edit mid-travel / 换手 blend: no restart, target followed live", cont && arrive && noRestart && worstBlend < 1e-9,
                    "edit kept the travel clock=" + cont + " arrived on the new value at 100 ms=" + arrive + "; hand blend never restarted=" + noRestart + " |x − smoothstep blend|≤" + worstBlend.ToString("E1"));
            }

            // Printed path: Leo's M4 (built-in values), A held, then D (1000 fps).
            {
                Profile m4 = M4();
                MotionEngine e = new MotionEngine();
                Step(e, m4, g, "A", 1000, 0.001);
                Console.WriteLine("   Leo默认 A→D path (A=" + F(m4.Dirs[2].OffX) + ", D=" + F(m4.Dirs[3].OffX) + ", 左右 " + F(m4.CurveAD.DurationMs) +
                    " ms 慢起; t = ms after pressing D, 1000 fps):");
                Console.Write("     ");
                int end = (int)m4.CurveAD.DurationMs + 50, every = Math.Max(10, (int)m4.CurveAD.DurationMs / 12 / 10 * 10);
                for (int t = 1; t <= end; t++)
                {
                    e.Update(0.001, In("D"), m4, g, PreviewMode.Live, 1);
                    if (t % every == 0) Console.Write(" " + t + "ms:" + e.MoveX.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
                Console.WriteLine();
            }
        }

        // ---------------- C3: continuity at 1000 fps ----------------

        static string PName(int i) { return i < MotionCurve.PresetNames.Length ? MotionCurve.PresetNames[i] : "旧速度(内部)"; }

        static void C3Continuity()
        {
            string res = ""; bool all = true;
            foreach (string mode in new string[] { "toggle", "hold" })
                foreach (int preset in new int[] { 4, 3, 1 })
                {
                    GlobalSettings g = G(); g.HandMode = mode;
                    Profile p = M4(); p.Jump.Enabled = false;
                    p.StandOffsetX = 3; p.CurveAD.SetPreset(preset); p.CurveWS.SetPreset(preset); p.CurveWS.DurationMs = 160;
                    if (preset == 3) p.HandSwitchMs = 150;
                    Random rnd = new Random(7 + preset + (mode == "hold" ? 100 : 0));
                    MotionEngine e = new MotionEngine();
                    double ox = 0, oy = 0, worst = 0; int switches = 0, edits = 0; bool wasSw = false, firstF = true;
                    for (int sgm = 0; sgm < 500; sgm++)
                    {
                        string k = "";
                        foreach (char c in "WASDQE") if (rnd.Next(4) == 0) k += c;
                        if (rnd.Next(3) == 0) k = rnd.Next(2) == 0 ? "A" : "D";
                        bool hand = rnd.Next(4) == 0;
                        int len = 1 + rnd.Next(rnd.Next(2) == 0 ? 40 : 300);
                        for (int i = 0; i < len; i++)
                        {
                            string kk = k + ((hand && (mode == "hold" || i < 3)) ? "K" : "");
                            if (rnd.Next(37) == 0)
                            {
                                // a value edit mid-travel (like one click on a spin box)
                                int d = rnd.Next(8); double v = rnd.Next(2) == 0 ? 1 : -1;
                                if (rnd.Next(2) == 0) { p.Dirs[d].OffX += v; Profile.RecomputeDiagonals(p); } else p.LeanQX += v;
                                edits++;
                            }
                            e.Update(0.001, In(kk), p, g, PreviewMode.Live, 1);
                            if (e.LiveSwitched != wasSw) { switches++; wasSw = e.LiveSwitched; }
                            if (!firstF) worst = Math.Max(worst, Math.Sqrt((e.OffsetX - ox) * (e.OffsetX - ox) + (e.OffsetY - oy) * (e.OffsetY - oy)));
                            ox = e.OffsetX; oy = e.OffsetY; firstF = false;
                        }
                    }
                    if (worst > 3) all = false;
                    res += mode + "/" + PName(preset) + ": " + F(worst) + "px (" + switches + " hand changes, " + edits + " edits)  ";
                }
            Check("C3 @1000fps random moves/reversals/leans/换手 (切换 + 按住)/value edits: max step ≤ 3 px", all, res);
        }

        // ---------------- C4: 换手 ----------------

        static bool[] HandSteps(GlobalSettings g, params string[] steps)
        {
            MotionEngine e = new MotionEngine(); Profile p = Example();
            bool[] r = new bool[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                for (int k = 0; k < 3; k++) e.Update(0.005, In(steps[i]), p, g, PreviewMode.Live, 1);
                r[i] = e.LiveSwitched;
            }
            return r;
        }

        static Profile HandProfile()
        {
            Profile p = Profile.CreateBlank("h");
            for (int i = 0; i < 8; i++) p.Dirs[i] = new DirSetting(10 + 3 * i, -5 - 2 * i);
            p.LeanQX = -11; p.LeanQY = -6; p.LeanEX = 23; p.LeanEY = -9; p.StandOffsetX = 4; p.StandOffsetY = -7; p.RecoilDriftX = 5;
            p.RecoilMaxPx = 30;
            return p;
        }

        static void C4Hand()
        {
            GlobalSettings tg = G();
            bool[] t = HandSteps(tg, "", "K", "", "K", "K", "", "K", "");
            GlobalSettings hg = G(); hg.HandMode = "hold";
            bool[] h = HandSteps(hg, "", "K", "K", "", "K", "");
            GlobalSettings off = G(); off.HandEnabled = false;
            bool[] o1 = HandSteps(off, "K", "", "K", ""); GlobalSettings off2 = G(); off2.HandEnabled = false; off2.HandMode = "hold";
            bool[] o2 = HandSteps(off2, "K", "K", "");
            bool[] hidden = HandSteps(G(), "R", "RK", "R", "F", "FK", "");
            Check("C4 换手 tracking: 切换 flips per press, 按住 while held, 换手检测 off = never, still tracked while 开镜 / 自由视角",
                !t[0] && t[1] && t[2] && !t[3] && !t[4] && !t[5] && t[6] && t[7] && !h[0] && h[1] && h[2] && !h[3] && h[4] && !h[5]
                && !o1[0] && !o1[2] && !o2[0] && !o2[1] && hidden[1] && hidden[3] && !hidden[4],
                "toggle=" + string.Join("", Array.ConvertAll(t, b => b ? "1" : "0")) + " hold=" + string.Join("", Array.ConvertAll(h, b => b ? "1" : "0")) +
                " off=" + (!o1[0] && !o2[0]) + " hidden=" + string.Join("", Array.ConvertAll(hidden, b => b ? "1" : "0")));

            // Ring switched back on (toggle key or tray) -> 正常手.
            {
                GlobalSettings g = G(); Profile p = Example();
                MotionEngine e = new MotionEngine();
                e.Update(0.01, In("K"), p, g, PreviewMode.Live, 1); e.Update(0.01, In(""), p, g, PreviewMode.Live, 1);
                bool sw = e.LiveSwitched;
                g.Enabled = false; e.Update(0.01, In(""), p, g, PreviewMode.Live, 1);
                bool stillWhileOff = e.LiveSwitched;
                g.Enabled = true; e.Update(0.01, In(""), p, g, PreviewMode.Live, 1);
                Check("C4 switching the ring back on resets to 正常手 (resync)", sw && stillWhileOff && !e.LiveSwitched,
                    "switched=" + sw + " while off=" + stillWhileOff + " after on=" + e.LiveSwitched);
            }

            // v3.0: 换手 = always 左右镜像, exactly per the table; no per-hand values in the model or the file.
            {
                Profile p = HandProfile(); Profile.RecomputeDiagonals(p); // v3.1: hand-1 diagonals come from the hand-1 cardinals; valid profiles always have derived diagonals
                HandView n = new HandView(), m = new HandView();
                p.FillView(0, n);
                p.FillView(1, m);
                int[] dp = { 0, 1, 3, 2, 5, 4, 7, 6 };
                bool ok = true;
                for (int i = 0; i < 8; i++) if (m.DX[i] != -n.DX[dp[i]] || m.DY[i] != n.DY[dp[i]]) ok = false;
                ok = ok && m.QX == -n.EX && m.QY == n.EY && m.EX == -n.QX && m.EY == n.QY && m.SX == -n.SX && m.SY == n.SY && m.Drift == -n.Drift;
                // mirror of the mirror = the normal hand
                Profile mp = HandProfile();
                for (int i = 0; i < 8; i++) { mp.Dirs[i].OffX = m.DX[i]; mp.Dirs[i].OffY = m.DY[i]; }
                mp.LeanQX = m.QX; mp.LeanQY = m.QY; mp.LeanEX = m.EX; mp.LeanEY = m.EY; mp.StandOffsetX = m.SX; mp.RecoilDriftX = m.Drift;
                HandView back = new HandView(); mp.FillView(1, back);
                bool inv = true;
                for (int i = 0; i < 8; i++) if (back.DX[i] != n.DX[i] || back.DY[i] != n.DY[i]) inv = false;
                inv = inv && back.QX == n.QX && back.EX == n.EX && back.SX == n.SX && back.Drift == n.Drift;
                string json = SettingsIO.ExportProfile(p);
                bool noHand = !json.Contains("\"Hand\"") && !json.Contains("Manual") && !json.Contains("\"Rule\"");
                Check("C4 换手 = 左右镜像 always: A↔D, WA↔WD, SA↔SD, Q↔E swap with X negated, 站着 X and 水平偏移 negated; mirror twice = 正常手; no per-hand values saved",
                    ok && inv && noHand, "A=(" + F(m.DX[2]) + "," + F(m.DY[2]) + ") from D=(" + F(n.DX[3]) + "," + F(n.DY[3]) + "), Q=(" + F(m.QX) + "," + F(m.QY) + ") from E, stand X " + F(m.SX) +
                    ", drift " + F(m.Drift) + " inverse=" + inv + " no Hand block=" + noHand);
            }

            // Blend: smoothstep over HandSwitchMs, reverses smoothly mid-blend; 0 ms instant.
            foreach (double fps in new double[] { 1000, 144 })
            {
                GlobalSettings g = G(); Profile p = HandProfile(); p.HandSwitchMs = 300;
                MotionEngine e = new MotionEngine();
                double dt = 1.0 / fps;
                for (int i = 0; i < 20; i++) e.Update(dt, In(""), p, g, PreviewMode.Live, 1);
                double tt = 0, worst = 0, done = double.NaN, maxStep = 0, last = e.Debug.UiOffsetX;
                bool first = true;
                while (tt < 500)
                {
                    e.Update(dt, In(first ? "K" : ""), p, g, PreviewMode.Live, 1); first = false;
                    tt += dt * 1000;
                    double u = Math.Min(1, tt / 300), s = u * u * (3 - 2 * u), want = 4 + (-4 - 4) * s;
                    worst = Math.Max(worst, Math.Abs(e.Debug.UiOffsetX - want));
                    if (double.IsNaN(done) && e.HandBlend == 1) done = tt;
                    maxStep = Math.Max(maxStep, Math.Abs(e.Debug.UiOffsetX - last)); last = e.Debug.UiOffsetX;
                }
                // switch back at 150 ms of a new blend: continuous, ends on 正常手 300 ms after the second press
                MotionEngine r = new MotionEngine();
                for (int i = 0; i < 20; i++) r.Update(dt, In(""), p, g, PreviewMode.Live, 1);
                double tr = 0, rlast = r.Debug.UiOffsetX, rstep = 0, at2 = -1; int phase = 0;
                while (tr < 700)
                {
                    string k = (phase == 0) ? "K" : "";
                    if (phase == 1 && tr >= 150) { k = "K"; phase = 2; at2 = tr; }
                    else if (phase == 0) phase = 1;
                    r.Update(dt, In(k), p, g, PreviewMode.Live, 1);
                    tr += dt * 1000;
                    rstep = Math.Max(rstep, Math.Abs(r.Debug.UiOffsetX - rlast)); rlast = r.Debug.UiOffsetX;
                }
                Profile z = HandProfile(); z.HandSwitchMs = 0;
                MotionEngine ze = new MotionEngine();
                ze.Update(dt, In(""), z, g, PreviewMode.Live, 1); ze.Update(dt, In("K"), z, g, PreviewMode.Live, 1);
                Check("C4 hand blend: smoothstep, done at HandSwitchMs, reverses without a jump; 0 ms instant @" + fps,
                    worst < 1e-9 && done >= 300 - 1e-6 && done < 300 + dt * 1000 + 1e-6 && rstep <= 0.05 * dt * 1000 && Math.Abs(rlast - 4) < 1e-12 && ze.Debug.UiOffsetX == -4,
                    "max |x − smoothstep|=" + worst.ToString("E1") + " blend done at " + F(done) + " ms, reversal max step " + F(rstep) + "px, back on 正常手=" + (Math.Abs(rlast - 4) < 1e-12) +
                    ", 0 ms: x=" + F(ze.Debug.UiOffsetX));
            }

            // Previews use the hand being edited; 换手来回切 flips every 1.2 s; the status shows the hand.
            {
                GlobalSettings g = G(); g.EditHand = 1;
                Profile p = HandProfile();
                MotionEngine e = new MotionEngine();
                for (int i = 0; i < 1000; i++) e.Update(0.002, In(""), p, g, PreviewMode.DirA, 1);
                HandView v = new HandView(); p.FillView(1, v);
                bool dirA = Math.Abs(e.Debug.UiOffsetX - (v.DX[2] + v.SX)) < 1e-9 && e.Debug.Switched && e.Debug.HandName == "换手";
                MotionEngine f = new MotionEngine();
                bool sawNormal = false, sawSwitched = false;
                for (int i = 0; i < 2600; i++)
                {
                    f.Update(0.002, In(""), p, g, PreviewMode.HandFlip, 1);
                    if (i == 500) sawSwitched = f.Hand == 1;       // first 1.2 s: the edited hand (换手)
                    if (i == 1100) sawNormal = f.Hand == 0;        // next 1.2 s: the other one
                }
                Check("C4 previews use the hand being edited; 换手来回切 flips every 1.2 s; status shows 正常手 / 换手", dirA && sawNormal && sawSwitched,
                    "DirA preview with 换手 edited x=" + F(e.Debug.UiOffsetX) + " (switched A + stand " + F(v.DX[2] + v.SX) + ") status=" + e.Debug.HandName +
                    " flip: 1st half 换手=" + sawSwitched + " 2nd half 正常手=" + sawNormal);
            }

            // Persistence + clamps; an old file's 换手 block (rule, manual values) is dropped.
            {
                GlobalSettings g = G(); Profile p = g.Active;
                p.HandSwitchMs = 450;
                g.HandMode = "hold"; g.HandEnabled = false; g.Keys.HandSwitch = 0x47;
                GlobalSettings back; string err;
                bool ok = SettingsIO.TryParse(SettingsIO.Serialize(g), out back, out err);
                bool rt = ok && back.Active.HandSwitchMs == 450 && back.HandMode == "hold" && !back.HandEnabled && back.Keys.HandSwitch == 0x47
                    && SettingsIO.Serialize(back) == SettingsIO.Serialize(g);
                GlobalSettings j;
                bool ok2 = SettingsIO.TryParse("{\"HandMode\":\"x\",\"Keys\":{\"HandSwitch\":999},\"Profiles\":[{\"Name\":\"a\",\"HandSwitchMs\":9000,\"Hand\":{\"Rule\":\"swap\",\"Dirs\":[{\"Manual\":true,\"X\":900}],\"Stand\":{\"Manual\":true,\"X\":-900}}}]}", out j, out err);
                string js = ok2 ? SettingsIO.Serialize(j) : "";
                bool cl = ok2 && j.HandMode == "toggle" && j.Keys.HandSwitch == 5 && j.Profiles[0].HandSwitchMs == 2000 && !js.Contains("\"Hand\"") && !js.Contains("Manual");
                Check("C4 换手 settings round-trip + clamps; an old 换手 block (rule / 手动 values) is dropped", rt && cl, "round-trip=" + rt + " clamps/dropped=" + cl + (ok2 ? "" : " " + err));
            }
        }

        // ---------------- C5: 保存为默认 / 恢复默认 ----------------

        static void C5SaveRestore()
        {
            GlobalSettings g = G();
            g.Keys.Toggle = 0x75; g.Fps = 90; g.ResGameH = 1080; g.HandMode = "hold";
            Profile cur = g.Active; string name = cur.Name;
            cur.Diameter = 21; cur.Dirs[2].OffX = -17; Profile.RecomputeDiagonals(cur); cur.CurveAD.DurationMs = 140; cur.CurveWS.SetPreset(2);
            cur.Shape = "cross"; cur.HandColor = "#102030"; cur.Boost.Diameter = 30; cur.HandSwitchMs = 250; cur.RecoilMaxPx = 51;
            string snapJson = SettingsIO.ExportProfile(cur);
            cur.SaveAsDefault("2026-10-12 13:40");
            bool notNested = cur.SavedDefault != null && cur.SavedDefault.SavedDefault == null && cur.SavedDefault.SavedDefaultAt == null;
            cur.SaveAsDefault("2026-10-12 13:41"); // overwrite, still not nested
            notNested = notNested && cur.SavedDefault.SavedDefault == null && cur.SavedDefaultAt == "2026-10-12 13:41";
            // edit everything
            cur.Diameter = 9; cur.Dirs[2].OffX = 3; Profile.RecomputeDiagonals(cur); cur.CurveAD.DurationMs = 700; cur.CurveWS.SetPreset(0);
            cur.Shape = "dot"; cur.HandColorOn = false; cur.Boost.FollowHandColor = false; cur.Dashes = 6;
            cur.HandSwitchMs = 0; cur.RecoilMaxPx = 2; cur.StandOffsetY = 5;
            Profile other = g.Profiles[1]; string otherJson = SettingsIO.ExportProfile(other);
            string globals = SettingsIO.Serialize(g.Clone(false));
            Profile n = g.RestoreSaved(cur);
            bool same = SettingsIO.ExportProfile(n) == snapJson && n.Name == name && g.Active == n && n.SavedDefault != null && n.SavedDefaultAt == "2026-10-12 13:41";
            bool untouched = SettingsIO.Serialize(g.Clone(false)) == globals && SettingsIO.ExportProfile(g.Profiles[1]) == otherJson;
            Check("C5 保存为默认 → edit → 恢复到我存的默认 = identical values, name kept, snapshot kept, never nested; globals + other profiles untouched",
                notNested && same && untouched, "identical=" + same + " not nested=" + notNested + " untouched=" + untouched);

            n.Diameter = 44;
            Profile m = g.RestoreLeoDefault(n);
            Profile want = Profile.CreateLeoDefault(name); want.Validate();
            bool m4 = SettingsIO.ExportProfile(m) == SettingsIO.ExportProfile(want) && m.Name == name && m.SavedDefault != null && m.SavedDefaultAt == "2026-10-12 13:41"
                && SettingsIO.ExportProfile(m.SavedDefault).Contains("\"Diameter\": 21");
            Profile again = g.RestoreSaved(m);
            Check("C5 恢复到内置默认 = CreateLeoDefault values, name + snapshot kept; restoring the snapshot afterwards still works",
                m4 && SettingsIO.ExportProfile(again) == snapJson && SettingsIO.Serialize(g.Clone(false)) == globals, "default values=" + m4 + " snapshot restore after it=" + (SettingsIO.ExportProfile(again) == snapJson));

            // Persistence, export without snapshot, import clears it, copy keeps it, nested in a file is dropped.
            string full = SettingsIO.Serialize(g);
            GlobalSettings back; string err;
            bool ok = SettingsIO.TryParse(full, out back, out err);
            bool persists = ok && back.Active.SavedDefault != null && back.Active.SavedDefaultAt == "2026-10-12 13:41" && SettingsIO.Serialize(back) == full;
            string exp = SettingsIO.ExportProfile(g.Active);
            bool noSnap = !exp.Contains("SavedDefault") && full.Contains("\"SavedDefault\"") && full.Contains("\"SavedDefaultAt\"");
            List<Profile> imp; bool ok2 = SettingsIO.TryParseImport(full, out imp, out err);
            bool impClear = ok2 && imp.TrueForAll(delegate(Profile q) { return q.SavedDefault == null && q.SavedDefaultAt == null; });
            Profile raw = g.Active.Clone(); raw.SavedDefault.SavedDefault = g.Active.CloneValues(); raw.SavedDefault.SavedDefaultAt = "x";
            string nestedJson = "{\"Profiles\":[" + RawProfileJson(raw) + "]}";
            GlobalSettings nested; bool ok3 = SettingsIO.TryParse(nestedJson, out nested, out err);
            bool dropNested = ok3 && nested.Profiles[0].SavedDefault != null && nested.Profiles[0].SavedDefault.SavedDefault == null && nestedJson.Contains("\"SavedDefaultAt\": \"x\"");
            Profile copy = g.Active.Clone();
            bool copyKeeps = copy.SavedDefault != null && copy.SavedDefault != g.Active.SavedDefault && SettingsIO.ExportProfile(copy.SavedDefault) == SettingsIO.ExportProfile(g.Active.SavedDefault);
            List<Profile> one; bool ok4 = SettingsIO.TryParseImport(RawProfileJson(g.Active), out one, out err);
            GlobalSettings tgt = G(); string first = ok4 ? tgt.AddImported(one) : null;
            bool oneClear = ok4 && one[0].SavedDefault == null && tgt.FindProfile(first).SavedDefault == null;
            Check("C5 snapshot persists in settings.json; export omits it; import (single or full file) clears it; 复制 keeps it; a nested snapshot in a file is dropped",
                persists && noSnap && impClear && oneClear && copyKeeps && dropNested,
                "persists=" + persists + " export omits=" + noSnap + " import clears=" + (impClear && oneClear) + " copy keeps=" + copyKeeps + " nested dropped=" + dropNested + (ok ? "" : " " + err));
        }

        /// <summary>A profile serialized WITH its snapshot (as inside settings.json).</summary>
        static string RawProfileJson(Profile p)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Profile));
                using (var w = System.Runtime.Serialization.Json.JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    ser.WriteObject(w, p);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        // ---------------- C6: migration ----------------

        static readonly string[] RemovedProfile = { "FollowMs", "ReverseLR", "ReverseFB", "CrouchEnabled", "CrouchPct", "CrouchSplit", "CrouchMaxPct", "CrouchKickPct",
            "CrouchDriftPct", "CrouchColorEnabled", "CrouchColor", "CrouchMoveEnabled", "CrouchMovePct", "SprintDimEnabled", "SprintRecoveryMs", "SprintDimPct",
            "Combos", "Hand" }; // v3.0: lean+move combos and per-hand values removed
        static readonly string[] RemovedKeys = { "Crouch", "Sprint", "Prone" };

        static bool Removed(string path)
        {
            string last = path.Substring(path.LastIndexOf('.') + 1);
            int br = last.IndexOf('[');
            if (br >= 0) last = last.Substring(0, br);
            if (path == "$.SprintMode") return true;
            if (path.StartsWith("$.Keys.") && Array.IndexOf(RemovedKeys, last) >= 0) return true;
            if (path.StartsWith("$.Profiles[")) foreach (string r in RemovedProfile) if (path.Contains("." + r)) return true;
            // v3.0: the diagonals are recomputed from the cardinals (checked separately)
            if (path.StartsWith("$.Profiles[")) for (int d = 4; d < 8; d++) if (path.Contains(".Dirs[" + d + "]")) return true;
            return false;
        }

        /// <summary>Walks every leaf of the old file; each one that v2.4 still has must be identical in the re-saved file.</summary>
        static void CompareLeaves(JsonElement oldE, JsonElement newE, string path, ref int compared, ref int removed, List<string> diffs)
        {
            if (Removed(path)) { removed++; return; }
            switch (oldE.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty prop in oldE.EnumerateObject())
                    {
                        string sub = path + "." + prop.Name;
                        if (Removed(sub)) { removed++; continue; }
                        JsonElement nv;
                        if (newE.ValueKind != JsonValueKind.Object || !newE.TryGetProperty(prop.Name, out nv)) { diffs.Add(sub + " missing"); continue; }
                        CompareLeaves(prop.Value, nv, sub, ref compared, ref removed, diffs);
                    }
                    break;
                case JsonValueKind.Array:
                    if (newE.ValueKind != JsonValueKind.Array || newE.GetArrayLength() != oldE.GetArrayLength()) { diffs.Add(path + " array length"); break; }
                    for (int i = 0; i < oldE.GetArrayLength(); i++) CompareLeaves(oldE[i], newE[i], path + "[" + i + "]", ref compared, ref removed, diffs);
                    break;
                case JsonValueKind.Number:
                    compared++;
                    if (newE.ValueKind != JsonValueKind.Number || oldE.GetDouble() != newE.GetDouble()) diffs.Add(path + " " + oldE.GetRawText() + " -> " + newE.GetRawText());
                    break;
                case JsonValueKind.String:
                    compared++;
                    if (path.EndsWith(".Name") && oldE.GetString() == GlobalSettings.LegacyDefaultProfileName && newE.ValueKind == JsonValueKind.String
                        && newE.GetString() == GlobalSettings.DefaultProfileName) break; // v3.0 rename
                    if (newE.ValueKind != JsonValueKind.String || oldE.GetString() != newE.GetString()) diffs.Add(path + " '" + oldE.GetString() + "' -> " + newE.GetRawText());
                    break;
                default:
                    compared++;
                    if (oldE.ValueKind != newE.ValueKind) diffs.Add(path + " " + oldE.GetRawText() + " -> " + newE.GetRawText());
                    break;
            }
        }

        static void C6Migration()
        {
            // Leo's v2.3 file (written by the real v2.3 serializer, non-default tuned values everywhere): field by field.
            {
                string text = File.ReadAllText(Fixture("settings-v2.3.json"));
                GlobalSettings g; string err;
                bool ok = SettingsIO.TryParse(text, out g, out err);
                string saved = ok ? SettingsIO.Serialize(g) : "{}";
                int compared = 0, removed = 0; List<string> diffs = new List<string>();
                using (JsonDocument a = JsonDocument.Parse(text), b = JsonDocument.Parse(saved))
                    CompareLeaves(a.RootElement, b.RootElement, "$", ref compared, ref removed, diffs);
                bool isV23 = text.Contains("\"SprintMode\"") && text.Contains("\"Prone\"") && text.Contains("\"ReverseLR\"") && text.Contains("\"CrouchMovePct\"") && !text.Contains("CurveAD");
                Check("C6 Leo's v2.3 settings.json: every tuned value kept, field by field", ok && isV23 && diffs.Count == 0 && compared > 100, // v3.0: combos + derived diagonals no longer compared
                    ok ? compared + " values compared, " + removed + " removed-feature fields skipped, differences: " + (diffs.Count == 0 ? "none" : string.Join("; ", diffs.GetRange(0, Math.Min(5, diffs.Count)))) : err);

                // The fixture really is tuned: its M4 differs from the built-in M4 in every compared number/string/bool.
                Profile m = g.FindProfile(GlobalSettings.DefaultProfileName); // v3.0: renamed from 「M4（Leo 改装）」
                bool diag = m != null && m.Dirs[4].OffX == 0.7071 * (m.Dirs[0].OffX + m.Dirs[2].OffX) && m.Dirs[7].OffY == 0.7071 * (m.Dirs[1].OffY + m.Dirs[3].OffY)
                    && g.FindProfile("M4（Leo 改装）") == null;
                int same = 0, total = 0; string sameList = "";
                using (JsonDocument a = JsonDocument.Parse(RawProfileJson(m)), b = JsonDocument.Parse(RawProfileJson(Profile.CreateLeoDefault("x"))))
                    foreach (JsonProperty prop in a.RootElement.EnumerateObject())
                    {
                        if (prop.Name == "Name" || prop.Name == "CurveAD" || prop.Name == "CurveWS" || prop.Name == "HandSwitchMs" || prop.Name == "Jump") continue;
                        if (Array.IndexOf(V3New, prop.Name) >= 0) continue; // v3.0 fields: defaults on both sides
                        if (prop.Name == "Style") continue; // v4.0 准星样式: defaults on both sides
                        if (prop.Name.StartsWith("MouseLag")) continue; // v4.1 鼠标滞后: defaults on both sides
                        CountSame(prop.Value, b.RootElement.GetProperty(prop.Name), prop.Name, ref same, ref total, ref sameList);
                    }
                Check("C6 fixture sanity: the tuned v2.3 M4 differs from the built-in 「Leo默认」 in (nearly) every value; diagonals recomputed; renamed 「Leo默认」",
                    diag && total > 25 && same <= 3,
                    (total - same) + "/" + total + " values differ; equal: " + (sameList.Length == 0 ? "none" : sameList));

                // Curves from FollowMs, 换手 defaults, keys/globals, removed fields gone, reload stable.
                Profile ak = ok ? g.FindProfile("AK-74N") : null, mp = ok ? g.FindProfile("MP-153") : null;
                bool curves = ok && CurveIs(m.CurveAD, 95) && CurveIs(m.CurveWS, 95) && CurveIs(ak.CurveAD, 130) && CurveIs(ak.CurveWS, 130) && CurveIs(mp.CurveAD, 0) && CurveIs(mp.CurveWS, 0);
                bool hand = ok && g.Keys.HandSwitch == 5 && g.HandEnabled && g.HandMode == "toggle" && g.Keys.Boost == 6 && !g.BoostEnabled // 自由视角 = 6 in this file
                    && g.Profiles.TrueForAll(delegate(Profile q) { return q.HandSwitchMs == 300 && q.SavedDefault == null && q.Shape == "ring" && q.HandColorOn; });
                bool gone = !saved.Contains("FollowMs") && !saved.Contains("\"Reverse\"") /* v5.1 (W34): the brief's new field "ReverseMode" contains the text; only the old key counts */ && !saved.Contains("Crouch") && !saved.Contains("Sprint") && !saved.Contains("Prone");
                GlobalSettings again; bool stable = ok && SettingsIO.TryParse(saved, out again, out err) && SettingsIO.Serialize(again) == saved;
                bool spot = ok && m.Dirs[0].OffY == -21.25 && m.LeanQY == -8.75 && m.RecoilDriftX == -3.5 && m.StandOffsetX == 2.5
                    && m.Color == "#11EE22" && g.Keys.Toggle == 0x75 && g.Keys.FreeLook == 6 && g.ResGameH == 1080 && g.ActiveProfile == "AK-74N" && !g.Enabled && g.LeanMode == "toggle";
                Check("C6 v2.3 file: 总时长 = old 跟随时间 (95 / 130 / 0) + 「v2.3 手感」; 换手 defaults; removed fields not written back; reload stable",
                    curves && hand && gone && stable && spot, "curves=" + curves + " 换手=" + hand + " removed gone=" + gone + " stable=" + stable + " spot checks=" + spot);
            }

            // Every older fixture: loads, curves from FollowMs (absent -> 100), 换手 key conflict handling, nothing removed is written back.
            {
                string[] names = { "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json", "settings-v2.0.json", "settings-v2.1.json", "settings-v2.2.json", "settings-v2.3.json" };
                int good = 0; string bad = "";
                foreach (string name in names)
                {
                    string text = File.ReadAllText(Fixture(name));
                    GlobalSettings g; string err;
                    bool ok = SettingsIO.TryParse(text, out g, out err);
                    bool fine = ok;
                    if (ok)
                    {
                        using (JsonDocument d = JsonDocument.Parse(text))
                        {
                            JsonElement profs = d.RootElement.GetProperty("Profiles");
                            for (int i = 0; i < profs.GetArrayLength(); i++)
                            {
                                JsonElement fm; double want = profs[i].TryGetProperty("FollowMs", out fm) ? fm.GetDouble() : 100;
                                string pn = profs[i].GetProperty("Name").GetString();
                                Profile q = g.FindProfile(pn);
                                if (q == null && pn == GlobalSettings.LegacyDefaultProfileName) q = g.FindProfile(GlobalSettings.DefaultProfileName); // v3.0 rename
                                if (q == null || !CurveIs(q.CurveAD, want) || !CurveIs(q.CurveWS, want) || q.SavedDefault != null) fine = false;
                                if (q != null && q.Dirs[5].OffX != 0.7071 * (q.Dirs[0].OffX + q.Dirs[3].OffX)) fine = false; // diagonals recomputed
                            }
                            bool used5 = false, used6 = false;
                            foreach (JsonProperty k in d.RootElement.GetProperty("Keys").EnumerateObject())
                                if (Array.IndexOf(RemovedKeys, k.Name) < 0) { if (k.Value.GetInt32() == 5) used5 = true; if (k.Value.GetInt32() == 6) used6 = true; }
                            fine = fine && g.Keys.HandSwitch == 5 && g.HandEnabled == !used5 && g.HandMode == "toggle";
                            fine = fine && g.Keys.Boost == 6 && g.BoostEnabled == !used6 && g.OnlyWhenGameFocused && g.GameWindowTitle == GlobalSettings.DefaultGameWindowTitle
                                && !g.Autostart && g.SplashOnStart;
                            if (name == "settings-v2.1.json") fine = fine && used5 && !g.HandEnabled && g.Keys.FreeLook == 5; // 自由视角 is on side button 1 there
                        }
                        string saved = SettingsIO.Serialize(g);
                        fine = fine && !saved.Contains("FollowMs") && !saved.Contains("\"Reverse\"") /* v5.1 (W34): the brief's new field "ReverseMode" contains the text; only the old key counts */ && !saved.Contains("Crouch") && !saved.Contains("Sprint") && !saved.Contains("Prone")
                            && !saved.Contains("\"Combos\"") && !saved.Contains("\"Hand\"");
                        GlobalSettings again; fine = fine && SettingsIO.TryParse(saved, out again, out err) && SettingsIO.Serialize(again) == saved;
                    }
                    if (fine) good++; else bad += " " + name;
                }
                Check("C6 fixtures v1 … v2.3 load: 总时长 = file's 跟随时间 (absent -> 100), 换手 on unless side button 1 is taken (v2.1: 自由视角 = 5 -> off), 强化准星 = 侧键2 on unless taken, v3.0 globals default, diagonals recomputed, combos/换手 not written, reload stable",
                    good == names.Length, good + "/" + names.Length + (bad.Length > 0 ? " bad:" + bad : ""));
            }

            // FollowMs clamp; fresh install; blank profile.
            {
                GlobalSettings c; string err;
                bool ok = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"FollowMs\":5000},{\"Name\":\"b\",\"FollowMs\":-3},{\"Name\":\"c\"},{\"Name\":\"d\",\"CurveAD\":{\"DurationMs\":77,\"Speeds\":[5,500,-2]}}]}", out c, out err);
                bool cl = ok && CurveIs(c.Profiles[0].CurveAD, 2000) && CurveIs(c.Profiles[1].CurveWS, 0) && CurveIs(c.Profiles[2].CurveAD, 100)
                    && c.Profiles[3].CurveAD.DurationMs == 77 && Same(c.Profiles[3].CurveAD.Speeds, 5, 100, 0, 75, 100) && CurveIs(c.Profiles[3].CurveWS, 100);
                Check("C6 跟随时间 -> 总时长 clamped (5000 -> 2000, -3 -> 0, absent -> 100); bad curve values clamped, short Speeds padded from 推荐 (AUDIT-LOGIC 5)", cl, ok ? "" : err);

                GlobalSettings d = GlobalSettings.CreateDefault(); d.Validate();
                Profile m = d.Active, b = Profile.CreateBlank("b");
                bool look = m.Diameter == 12 && m.Thickness == 1.2 && m.Feather == 1.0 && m.OpacityPct == 65 && m.Color == "#00DF00" && m.Color2 == "#000000"
                    && m.Gradient == "twotone" && m.OuterThickness == 1.8 && !m.CenterDot && m.CenterDotSize == 2 && m.Shape == "cross" && m.CrossLength == 6.3 && m.CrossGap == 4.3 && m.Dashes == 0 && m.GlowPx == 0
                    && m.RotationDeg == 0 && m.HandColorOn && m.HandColor == "#FF9500" && m.HandColor2 == "#000000";
                double[,] dirs = { { 0, -14.4 }, { 0, 14.4 }, { 97.2, 0 }, { -37.4, 0 }, { 68.7, -10.2 }, { -26.5, -10.2 }, { 68.7, 10.2 }, { -26.5, 10.2 } }; // v5.0: Leo's v5 file 「Leo默认 (旧)」
                bool mv = m.CurveAD.DurationMs == 430 && m.CurveWS.DurationMs == 430 && Same(m.CurveAD.Speeds, 0, 15, 45, 75, 100) && Same(m.CurveWS.Speeds, 0, 15, 45, 75, 100)
                    && m.StandOffsetX == 0 && m.StandOffsetY == 0;
                for (int i = 0; i < 8; i++) if (Math.Round(m.Dirs[i].OffX, 1) != dirs[i, 0] || Math.Round(m.Dirs[i].OffY, 1) != dirs[i, 1]) mv = false;
                bool rc = m.RecoilMaxPx == 24 && m.RecoilRiseMs == 300 && m.RecoilClickKickPx == 8 && m.RecoilFallMs == 180 && m.RecoilDelayMs == 30 && m.RecoilDriftX == 0;
                bool ln = Math.Abs(m.LeanQX + 11.808) < 1e-9 && m.LeanQY == -18 && Math.Abs(m.LeanEX - 11.52) < 1e-9 && Math.Abs(m.LeanEY + 16.56) < 1e-9 && m.LeanMs == 280;
                bool hd = m.HandSwitchMs == 450 && m.SavedDefault == null && b.CurveAD.IsRecommended && b.CurveWS.IsRecommended && b.HandSwitchMs == 450; // v4.1 (BRUTE40 P1-1): 新建空白配置 = 推荐 curve, not the deleted v2.3 one; v5.0.2 (W18): its times are Leo's (450 ms)
                JumpSettings js = m.Jump;
                bool jp = js.Enabled && js.TotalMs == 800 && js.Phases[0].Ms == 200 && js.Phases[1].Ms == 305 && js.Phases[2].Ms == 295
                    && js.Phases[0].EndPx == -300 && js.Phases[1].EndPx == 0 && js.Phases[2].EndPx == 0
                    && js.Phases[0].Curve == "smooth" && js.Phases[1].Curve == "smooth" && js.Phases[2].Curve == "in";
                BoostStyle bs = m.Boost;
                bool bo = bs.Diameter == 15 && bs.Thickness == 1.8 && bs.OuterThickness == 2.5 && bs.OpacityPct == 90 && bs.GlowPx == 4 && bs.GlowOpacityPct == 45
                    && bs.FollowHandColor && bs.Color == "#00FF00" && bs.Color2 == "#000000";
                KeyBindings k = d.Keys;
                bool gl = k.Toggle == 0x7B && k.Fire == 1 && k.Ads == 2 && k.FreeLook == 4 && k.HandSwitch == 5 && k.Boost == 0x54 && k.BoostMods == KeyBindings.ModCtrl /* v5.1 (W23): 强化 = Ctrl+T */ && k.LeanLeft == 0x51 && k.LeanRight == 0x45
                    && k.Forward == 0x57 && k.Back == 0x53 && k.Left == 0x41 && k.Right == 0x44 && k.Jump == 0x20 && d.AdsMode == "hold" && d.LeanMode == "hold" && d.HideOnFreeLook
                    && d.HandEnabled && d.HandMode == "toggle" && d.BoostEnabled && d.OnlyWhenGameFocused && d.GameWindowTitle == "EscapeFromTarkov" && !d.Autostart
                    && d.SplashOnStart && d.Enabled && d.Fps == 360 && d.ShowFadeMs == 50 && d.ResBaseH == 1440 && d.ResGameH == 1440 && d.ResBaseW == 2160 && d.ResGameW == 1920
                    && d.Profiles.Count == 1 && d.ActiveProfile == "Leo默认" && m.Name == "Leo默认";
                string scratch = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
                if (string.IsNullOrEmpty(scratch)) scratch = Path.GetTempPath();
                string dir = Path.Combine(scratch, "c6-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "settings.json");
                string msg;
                GlobalSettings fresh = SettingsIO.Load(path, out msg);
                GlobalSettings reread = SettingsIO.Load(path, out msg);
                bool file = File.Exists(path) && SettingsIO.Serialize(fresh) == SettingsIO.Serialize(reread) && fresh.Keys.HandSwitch == 5 && fresh.HandEnabled
                    && fresh.Profiles.Count == 1 && fresh.Active.Name == "Leo默认" && SettingsIO.ExportProfile(fresh.Active) == SettingsIO.ExportProfile(m);
                File.Delete(path); Directory.Delete(dir);
                Check("C6 fresh install = exactly one profile 「Leo默认」 with Leo's v5.0 values (move, 430 ms 0/15/45/75/100 curves, recoil, lean, 换手 450 ms, 十字, 跳跃 on 800 = 200/305/295 −300/0/0), v2.4.5 look, v3.0 defaults; keys F12 / LMB / RMB / MMB / 侧键1 / 侧键2 / Q E / WASD / Space",
                    look && mv && rc && ln && hd && jp && bo && gl && file, "look=" + look + " move=" + mv + " recoil=" + rc + " lean=" + ln + " hand=" + hd + " jump=" + jp + " boost=" + bo + " keys/globals=" + gl + " file=" + file);
            }
        }

        static readonly string[] V3New = { "Shape", "CrossLength", "CrossThickness", "CrossGap", "CrossTop", "Dashes", "DashGapPct", "GlowPx", "GlowOpacityPct",
            "RotationDeg", "HandColorOn", "HandColor", "HandColor2", "Boost", "HandCustom" };

        static void CountSame(JsonElement a, JsonElement b, string path, ref int same, ref int total, ref string list)
        {
            if (a.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in a.EnumerateObject()) { JsonElement bv; if (b.TryGetProperty(p.Name, out bv)) CountSame(p.Value, bv, path + "." + p.Name, ref same, ref total, ref list); }
                return;
            }
            if (a.ValueKind == JsonValueKind.Array)
            {
                for (int i = 0; i < a.GetArrayLength() && i < b.GetArrayLength(); i++) CountSame(a[i], b[i], path + "[" + i + "]", ref same, ref total, ref list);
                return;
            }
            if (path.EndsWith(".Key")) return; // combo names
            if (path.StartsWith("Dirs[4]") || path.StartsWith("Dirs[5]") || path.StartsWith("Dirs[6]") || path.StartsWith("Dirs[7]")) return; // derived
            total++;
            if (a.GetRawText() == b.GetRawText()) { same++; list += " " + path; }
        }

        // ---------------- C7: everything that remains = v2.3 ----------------

        static V23.InputState V23In(InputState i)
        {
            V23.InputState l = new V23.InputState();
            l.Fwd = i.Fwd; l.Back = i.Back; l.Left = i.Left; l.Right = i.Right; l.LeanL = i.LeanL; l.LeanR = i.LeanR; l.Fire = i.Fire; l.Ads = i.Ads;
            l.FreeLook = i.FreeLook;
            return l;
        }

        static void SameAsV23(Profile p, GlobalSettings g, string[] script, double fps, out double full, out double nonMove, out double alphaRecoil)
        {
            List<V23.Profile> vp; string err;
            if (!V23.SettingsIO.TryParseImport(SettingsIO.ExportProfile(p), out vp, out err)) throw new Exception("v2.3 import: " + err);
            V23.GlobalSettings vg;
            if (!V23.SettingsIO.TryParse(SettingsIO.Serialize(g), out vg, out err)) throw new Exception("v2.3 parse: " + err);
            V23.Profile q = vp[0];
            MotionEngine a = new MotionEngine();
            V23.MotionEngine b = new V23.MotionEngine();
            full = 0; nonMove = 0; alphaRecoil = 0;
            int seg = 0;
            double rs = g.ResolutionScale;
            foreach (string k in script)
            {
                int n = (int)Math.Round((0.05 + 0.137 * (seg++ % 5)) * fps);
                for (int i = 0; i < n; i++)
                {
                    InputState inp = In(k);
                    a.Update(1.0 / fps, inp, p, g, PreviewMode.Live, 1);
                    b.Update(1.0 / fps, V23In(inp), q, vg, V23.PreviewMode.Live, 1);
                    full = Math.Max(full, Math.Max(Math.Abs(a.OffsetX - b.OffsetX), Math.Abs(a.OffsetY - b.OffsetY)));
                    nonMove = Math.Max(nonMove, Math.Abs((a.OffsetX - rs * a.MoveX) - (b.OffsetX - rs * b.SpringX)));
                    nonMove = Math.Max(nonMove, Math.Abs((a.OffsetY + rs * a.MoveY) - (b.OffsetY + rs * b.SpringY)));
                    alphaRecoil = Math.Max(alphaRecoil, Math.Max(Math.Abs(a.Alpha - b.Alpha), Math.Abs(a.Recoil - b.Recoil)));
                    alphaRecoil = Math.Max(alphaRecoil, Math.Abs(a.Debug.Recoil - b.Debug.Recoil) + Math.Abs(b.Dim - 1));
                }
            }
        }

        static void C7SameAsV23()
        {
            string[] still = { "", "Q", "", "E", "QL", "L", "", "LR", "R", "EL", "F", "FL", "", "Q", "QE", "E", "L", "", "R", "" };
            string[] moving = { "", "A", "D", "A", "", "D", "WA", "WD", "SD", "WA", "SA", "S", "W", "S", "QA", "QD", "ED", "A", "AL", "DL", "WDL", "R", "AR", "D", "WSA", "AD", "E", "EWD", "W", "F", "FD", "" };
            foreach (double fps in Rates)
            {
                Profile ex = Example(); ex.RecoilDriftX = 7; ex.StandOffsetX = 3; ex.StandOffsetY = -2;
                Profile m4 = M4(); m4.Jump.Enabled = false;
                GlobalSettings g = G();
                GlobalSettings tg = G(); tg.LeanMode = "toggle"; tg.AdsMode = "toggle"; tg.HideOnFreeLook = false;
                GlobalSettings rg = G(); rg.ResGameW = 1920; rg.ResGameH = 1080; rg.ShowFadeMs = 0; rg.HideFadeMs = 120;
                double worstFull = 0, worstNon = 0, worstAR = 0;
                foreach (Profile p in new Profile[] { ex, m4 })
                    foreach (GlobalSettings gg in new GlobalSettings[] { g, tg, rg })
                    {
                        double f1, n1, ar1, f2, n2, ar2;
                        SameAsV23(p, gg, still, fps, out f1, out n1, out ar1);
                        SameAsV23(p, gg, moving, fps, out f2, out n2, out ar2);
                        worstFull = Math.Max(worstFull, f1); worstNon = Math.Max(worstNon, Math.Max(n1, n2)); worstAR = Math.Max(worstAR, Math.Max(ar1, ar2));
                    }
                Check("C7 lean / recoil / visibility / resolution identical to v2.3 (V23 reference, removed features off) @" + fps,
                    worstFull <= 1e-12 && worstNon <= 1e-9 && worstAR <= 1e-12,
                    "no movement keys: max |offset diff|=" + worstFull.ToString("E1") + "; with movement: max |non-movement part diff|=" + worstNon.ToString("E1") +
                    ", alpha/recoil=" + worstAR.ToString("E1") + " (2 profiles x hold/toggle/1080p)");
            }

            // Settled positions for every key combination = v2.3 with its combos off (v3.0: lean + move additive).
            {
                string[] keys = { "", "W", "S", "A", "D", "WA", "WD", "SA", "SD", "Q", "E", "QA", "QWA", "QAS", "ED", "WED", "EDS", "QD", "QW", "EA", "EWA", "QEA" };
                Profile p = M4(); p.Jump.Enabled = false; p.StandOffsetX = 4;
                GlobalSettings g = G();
                List<V23.Profile> vp; string err;
                V23.SettingsIO.TryParseImport(SettingsIO.ExportProfile(p), out vp, out err);
                V23.GlobalSettings vg; V23.SettingsIO.TryParse(SettingsIO.Serialize(g), out vg, out err);
                double worst = 0;
                foreach (string k in keys)
                {
                    MotionEngine a = new MotionEngine(); V23.MotionEngine b = new V23.MotionEngine();
                    for (int i = 0; i < 2000; i++) { a.Update(0.001, In(k), p, g, PreviewMode.Live, 1); b.Update(0.001, V23In(In(k)), vp[0], vg, V23.PreviewMode.Live, 1); }
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.OffsetX - b.OffsetX), Math.Abs(a.OffsetY - b.OffsetY)));
                }
                Check("C7 settled position for " + keys.Length + " key combos = v2.3", worst <= 1e-6, "max |diff| after 2 s=" + worst.ToString("E1"));
            }
        }

        // ---------------- C8: keys, 按键恢复默认, resolution ----------------

        static void C8KeysResolution()
        {
            KeyBindings k = new KeyBindings();
            HashSet<int> held = new HashSet<int>();
            Func<int, bool> down = delegate(int vk) { return held.Contains(vk); };
            held.Add(5); bool side = MotionEngine.ReadInput(k, down).HandSwitch;
            held.Clear(); held.Add(6); bool other = MotionEngine.ReadInput(k, down).HandSwitch;
            KeyBindings r = new KeyBindings(); r.HandSwitch = 0x47; held.Clear(); held.Add(0x47);
            bool rebound = MotionEngine.ReadInput(r, down).HandSwitch;
            bool table = KeyBindings.ActionNames.Length == KeyBindings.ActionCount && KeyBindings.ActionNames[4] == "换手";
            for (int a = 0; a < KeyBindings.ActionCount; a++)
            {
                // v4.0: 前进/后退/左移/右移 are fixed W/S/A/D (Set ignored); every other action is rebindable.
                KeyBindings t = new KeyBindings(); int before = t.Get(a); t.Set(a, 200 + a);
                if (t.Get(a) != (KeyBindings.IsMovementAction(a) ? before : 200 + a)) table = false;
            }
            bool find = k.FindOther(0x41, 7) == 9 && k.FindOther(0x41, 9) == -1 && k.FindOther(5, 0) == 4 && k.FindOther(0x99, -1) == -1;
            Check("C8 换手 key = side button 1 (rebindable); action table; 「这个键已经用于」 lookup", side && !other && rebound && table && find,
                "XButton1=" + side + " XButton2=" + other + " rebound=" + rebound + " table=" + table + " find=" + find);

            GlobalSettings g = G();
            g.Keys.Toggle = 0x70; g.Keys.Fire = 0x11; g.Keys.HandSwitch = 6; g.Keys.Left = 0x25; g.AdsMode = "toggle"; g.LeanMode = "toggle"; g.HideOnFreeLook = false;
            g.HandMode = "hold"; g.HandEnabled = false; g.Fps = 77; g.ResGameH = 1080;
            string prof = SettingsIO.Serialize(g).Substring(SettingsIO.Serialize(g).IndexOf("\"Profiles\""));
            g.ResetKeys();
            GlobalSettings d = GlobalSettings.CreateDefault();
            bool keys = SettingsIO.Serialize(g).Contains("\"HandSwitch\": 5") && g.Keys.Toggle == 0x7B && g.Keys.Fire == 1 && g.Keys.Ads == 2
                && g.Keys.FreeLook == 4 && g.Keys.HandSwitch == 5 && g.Keys.LeanLeft == 0x51 && g.Keys.LeanRight == 0x45 && g.Keys.Forward == 0x57 && g.Keys.Back == 0x53
                && g.Keys.Left == 0x41 && g.Keys.Right == 0x44 && g.AdsMode == "hold" && g.LeanMode == "hold" && g.HideOnFreeLook && g.HandMode == "toggle" && g.HandEnabled
                && g.Keys.Toggle == d.Keys.Toggle;
            bool rest = g.Fps == 77 && g.ResGameH == 1080 && SettingsIO.Serialize(g).Substring(SettingsIO.Serialize(g).IndexOf("\"Profiles\"")) == prof;
            Check("C8 按键恢复默认 = fresh-install keys + modes; everything else untouched", keys && rest, "keys=" + keys + " others untouched=" + rest);

            foreach (double fps in Rates)
            {
                Profile p = M4();
                p.StandOffsetX = 5; p.StandOffsetY = -3; p.RecoilDriftX = 6;
                string ringBefore = p.AppearanceKey(); int canvas = RingRaster.CanvasSize(p);
                GlobalSettings g1 = G(), g2 = G(); g2.ResGameW = 1920; g2.ResGameH = 1080;
                MotionEngine a = new MotionEngine(), b = new MotionEngine();
                double worst = 0, worstA = 0, worstR = 0; int frames = 0, seg = 0;
                foreach (string kk in new string[] { "", "A", "D", "AK", "WA", "QA", "QAK", "ED", "L", "AL", "DLK", "R", "W", "K", "S", "E", "" })
                {
                    int n = (int)Math.Round((0.05 + 0.137 * (seg++ % 5)) * fps);
                    for (int i = 0; i < n; i++)
                    {
                        InputState inp = In(i < 2 ? kk : kk.Replace("K", ""));
                        a.Update(1.0 / fps, inp, p, g1, PreviewMode.Live, 1);
                        b.Update(1.0 / fps, inp, p, g2, PreviewMode.Live, 1);
                        worst = Math.Max(worst, Math.Max(Math.Abs(b.OffsetX - 0.75 * a.OffsetX), Math.Abs(b.OffsetY - 0.75 * a.OffsetY)));
                        worstA = Math.Max(worstA, Math.Abs(b.Alpha - a.Alpha));
                        worstR = Math.Max(worstR, Math.Abs(b.Debug.Recoil - 0.75 * a.Debug.Recoil));
                        frames++;
                    }
                }
                bool ringSame = p.AppearanceKey() == ringBefore && RingRaster.CanvasSize(p) == canvas;
                Check("C8 2160x1440 -> 1920x1080: all offsets x 0.75 (incl. 换手), ring size unchanged @" + fps, worst <= 1e-9 && worstA == 0 && worstR <= 1e-9 && ringSame,
                    "max |off − 0.75·off|=" + worst.ToString("E1") + " recoil=" + worstR.ToString("E1") + " over " + frames + " frames");
            }
            GlobalSettings c; string err;
            bool ok = SettingsIO.TryParse("{\"ResBaseW\":100,\"ResBaseH\":99999,\"ResGameW\":-5,\"ResGameH\":1080.4}", out c, out err);
            Check("C8 resolution fields clamped", ok && c.ResBaseW == 640 && c.ResBaseH == 4320 && c.ResGameW == 640 && c.ResGameH == 1080,
                ok ? c.ResBaseW + "x" + c.ResBaseH + " / " + c.ResGameW + "x" + c.ResGameH : err);
        }
    }
}
