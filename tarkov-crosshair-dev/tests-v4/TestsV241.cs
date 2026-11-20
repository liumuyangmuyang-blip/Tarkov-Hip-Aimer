using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    // v2.4.1 tests: opposite keys, the last pressed wins (InputResolver / LiveInputFilter).
    public static partial class Program
    {
        static void V241Tests()
        {
            bool fb = InputResolver.FallbackToHeld, ap = InputResolver.ApplyToFwdBack;
            try
            {
                D1Overlap(); D2Rules(); D3Random(); D4Filter();
            }
            finally { InputResolver.FallbackToHeld = fb; InputResolver.ApplyToFwdBack = ap; }
        }

        /// <summary>A physical key held from Press (inclusive) to Release (exclusive), ms.</summary>
        struct Hold
        {
            public char Key; public double Press, Release;
            public Hold(char k, double p, double r) { Key = k; Press = p; Release = r; }
        }

        static InputState At(List<Hold> tl, double t)
        {
            string keys = "";
            foreach (Hold h in tl) if (t >= h.Press - 1e-9 && t < h.Release - 1e-9) keys += h.Key;
            return In(keys);
        }

        /// <summary>Runs a timeline; returns per frame [t, moveX, moveY, dir]. resolve = v2.4.1, else raw (= v2.4).</summary>
        static List<double[]> RunTimeline(Profile p, GlobalSettings g, List<Hold> tl, double fps, double endMs, bool resolve)
        {
            MotionEngine e = new MotionEngine();
            InputResolver r = new InputResolver();
            List<double[]> tr = new List<double[]>();
            int n = (int)Math.Round(endMs / 1000.0 * fps);
            for (int k = 0; k <= n; k++)
            {
                double t = k * 1000.0 / fps;
                InputState raw = At(tl, t);
                InputState inp = resolve ? r.Resolve(raw) : raw;
                e.Update(1.0 / fps, inp, p, g, PreviewMode.Live, 1);
                tr.Add(new double[] { t, e.MoveX, e.MoveY, e.CurrentDir });
            }
            return tr;
        }

        /// <summary>ms from 'fromMs' until the axis value is at least 90 % of the way from a to b (NaN if never).</summary>
        static double T90(List<double[]> tr, int axis, double fromMs, double a, double b)
        {
            double goal = a + 0.9 * (b - a);
            foreach (double[] r in tr)
            {
                if (r[0] < fromMs - 1e-9) continue;
                double v = r[1 + axis];
                if ((b < a && v <= goal + 1e-9) || (b > a && v >= goal - 1e-9)) return r[0] - fromMs;
            }
            return double.NaN;
        }

        // ---------------- D1: A->D / S->W with overlaps ----------------

        static void D1Overlap()
        {
            GlobalSettings g = G();
            Profile m4 = M4();
            double ax = m4.Dirs[2].OffX, dx = m4.Dirs[3].OffX, sy = m4.Dirs[1].OffY, wy = m4.Dirs[0].OffY;
            double[] dur = { m4.CurveAD.DurationMs, m4.CurveWS.DurationMs }; // v2.4.4: read from CreateLeoM4 (600 / 600 ms)
            double endMs = 1000 + 2 * Math.Max(dur[0], dur[1]) + 400; // v5.1 (W21): a reversal = back to 0, then out
            double[] overlaps = { 0, 1, 20, 100, 300 };
            string bad = "";
            int cases = 0;
            foreach (double fps in new double[] { 60, 144, 360 })
                foreach (double ov in overlaps)
                    for (int axis = 0; axis < 2; axis++)
                    {
                        char from = axis == 0 ? 'A' : 'S', to = axis == 0 ? 'D' : 'W';
                        double a = axis == 0 ? ax : sy, b = axis == 0 ? dx : wy;
                        List<Hold> tl = new List<Hold> { new Hold(from, 0, 1000 + ov), new Hold(to, 1000, 3000) };
                        List<double[]> tr = RunTimeline(m4, g, tl, fps, endMs, true);
                        // first frame with the new key down
                        int k0 = -1;
                        for (int i = 0; i < tr.Count; i++) if (tr[i][0] >= 1000 - 1e-9) { k0 = i; break; }
                        bool ok = true;
                        int wantDir = axis == 0 ? 3 : 0;
                        double last = tr[k0 - 1][1 + axis];
                        for (int i = k0; i < tr.Count; i++)
                        {
                            double v = tr[i][1 + axis];
                            if ((int)tr[i][3] != wantDir) ok = false;                         // heads to D / W from the press frame on
                            if ((b < a && v > last + 1e-12) || (b > a && v < last - 1e-12)) ok = false; // monotone, never back to centre
                            if (tr[i][0] >= 1000 + 2 * dur[axis] + 1e-9 && Math.Abs(v - b) > 1e-9) ok = false;   // v5.1: on target 2 x 总时长 after the press (back to 0, then out)
                            last = v;
                        }
                        if (Math.Abs(tr[k0][1 + axis] - a) < 1e-12 && tr[k0][0] > 1000 + 1e-9) ok = false;
                        cases++;
                        if (!ok) bad += " " + from + "->" + to + " ov" + ov + "@" + fps;
                    }
            Check("D1 A->D and S->W, overlaps 0/1/20/100/300 ms at 60/144/360 fps: heads to the new key from its press frame, monotone, on target 2 x 总时长 later (M4, v5.1 reversal)",
                bad.Length == 0, bad.Length == 0 ? cases + " cases" : "bad:" + bad);

            // time-to-90 %, 100 ms overlap, v2.4 (raw) vs v2.4.1 (resolved)
            string rep = "";
            bool faster = true;
            foreach (double fps in new double[] { 60, 144, 360 })
                for (int axis = 0; axis < 2; axis++)
                {
                    char from = axis == 0 ? 'A' : 'S', to = axis == 0 ? 'D' : 'W';
                    double a = axis == 0 ? ax : sy, b = axis == 0 ? dx : wy;
                    List<Hold> tl = new List<Hold> { new Hold(from, 0, 1100), new Hold(to, 1000, 3000) };
                    double old = T90(RunTimeline(m4, g, tl, fps, endMs, false), axis, 1000, a, b);
                    double now = T90(RunTimeline(m4, g, tl, fps, endMs, true), axis, 1000, a, b);
                    if (!(now < old)) faster = false;
                    rep += " " + from + "->" + to + "@" + fps + ": v2.4 " + F(old) + " ms, v2.4.1 " + F(now) + " ms;";
                }
            Check("D1 time-to-90 % with a 100 ms overlap (M4: A " + F(ax) + " -> D " + F(dx) + ", S " + F(sy) + " -> W " + F(wy) +
                "; curves " + F(dur[0]) + " / " + F(dur[1]) + " ms 「v2.3 手感」)", faster, rep);
        }

        // ---------------- D2: rules ----------------

        static string Pattern(List<Hold> tl, double fps, double endMs, int axis)
        {
            InputResolver r = new InputResolver();
            string s = "";
            char last = '?';
            int n = (int)Math.Round(endMs / 1000.0 * fps);
            for (int k = 0; k <= n; k++)
            {
                InputState o = r.Resolve(At(tl, k * 1000.0 / fps));
                char c = axis == 0 ? (o.Left ? 'A' : (o.Right ? 'D' : '-')) : (o.Fwd ? 'W' : (o.Back ? 'S' : '-'));
                if (c != last) { s += c; last = c; }
            }
            return s;
        }

        static void D2Rules()
        {
            GlobalSettings g = G();
            Profile m4 = M4Curves100(); // v2.4.4: the rule timelines below were written around a 100 ms travel
            double ax = m4.Dirs[2].OffX, dx = m4.Dirs[3].OffX;
            const double fps = 144;

            // hold A, D 500..800 while A stays held until 1500, A again 1600..2200
            List<Hold> tl = new List<Hold> { new Hold('A', 0, 1500), new Hold('D', 500, 800), new Hold('A', 1600, 2200) };
            InputResolver.FallbackToHeld = false;
            string pLeo = Pattern(tl, fps, 2400, 0);
            List<double[]> tr = RunTimeline(m4, g, tl, fps, 2400, true);
            double xCentre = tr[(int)Math.Round(1.4 * fps)][1], xA = tr[(int)Math.Round(2.1 * fps)][1];
            InputResolver.FallbackToHeld = true;
            string pFb = Pattern(tl, fps, 2400, 0);
            List<double[]> tf = RunTimeline(m4, g, tl, fps, 2400, true);
            double xBack = tf[(int)Math.Round(1.4 * fps)][1];
            Check("D2 release D while A held -> back to A (default, Leo checked in game); FallbackToHeld = false -> centre until A is pressed again",
                pLeo == "AD-A-" && xCentre == 0 && xA == ax && pFb == "ADA-A-" && xBack == ax,
                "default: " + pFb + " (x at 1.4 s=" + F(xBack) + "); FallbackToHeld = false: " + pLeo + " (x at 1.4 s=" + F(xCentre) + ", after re-press=" + F(xA) + ")");

            // same-frame tie: both pressed at 0 -> centre; release A at 400 -> D active
            List<Hold> tie = new List<Hold> { new Hold('A', 0, 400), new Hold('D', 0, 1000) };
            string pt = Pattern(tie, fps, 1100, 0);
            List<double[]> tt = RunTimeline(m4, g, tie, fps, 1100, true);
            Check("D2 same-poll press = cancel (centre); releasing one leaves the other active", pt == "-D-" && tt[(int)(0.3 * fps)][1] == 0 && tt[(int)(0.9 * fps)][1] == dx,
                "pattern " + pt + " x at 0.3 s=" + F(tt[(int)(0.3 * fps)][1]) + " at 0.9 s=" + F(tt[(int)(0.9 * fps)][1]));

            // tap overlaps: A held 0..2000, D tapped 30 ms every 300 ms
            List<Hold> taps = new List<Hold> { new Hold('A', 0, 2000) };
            for (int i = 0; i < 5; i++) taps.Add(new Hold('D', 300 + 300 * i, 330 + 300 * i));
            InputResolver.FallbackToHeld = false;
            string tLeo = Pattern(taps, 360, 2100, 0);
            InputResolver.FallbackToHeld = true;
            string tFb = Pattern(taps, 360, 2100, 0);
            Check("D2 tap-overlaps (A held, D tapped 5 x 30 ms): default (back to A) A D A D A …; FallbackToHeld = false A D - D - …",
                tLeo == "AD-D-D-D-D-" && tFb == "ADADADADADA-", "default: " + tFb + "  FallbackToHeld = false: " + tLeo);

            // diagonals: WA held, D pressed -> WD
            List<Hold> dg = new List<Hold> { new Hold('W', 0, 2000), new Hold('A', 0, 2000), new Hold('D', 800, 2000) };
            List<double[]> td = RunTimeline(m4, g, dg, fps, 1500, true);
            double[] end = td[td.Count - 1];
            Check("D2 WA held + D pressed -> WD", (int)td[(int)(0.7 * fps)][3] == 4 && (int)end[3] == 5 && end[1] == m4.Dirs[5].OffX && end[2] == m4.Dirs[5].OffY,
                "dir before=" + Name((int)td[(int)(0.7 * fps)][3]) + " after=" + Name((int)end[3]) + " at (" + F(end[1]) + "," + F(end[2]) + ")");

            // lean + move (v3.0: always additive): E + A held, D pressed -> D + E
            Profile c = M4();
            List<Hold> lc = new List<Hold> { new Hold('E', 0, 3000), new Hold('A', 0, 3000), new Hold('D', 1000, 3000) };
            MotionEngine e = new MotionEngine(); InputResolver r = new InputResolver();
            for (int k = 0; k <= (int)(2.5 * fps); k++) e.Update(1.0 / fps, r.Resolve(At(lc, k * 1000.0 / fps)), c, g, PreviewMode.Live, 1);
            Check("D2 E + A held, D pressed -> D + E (lean + move additive)",
                Math.Abs(e.Debug.UiOffsetX - (c.Dirs[3].OffX + c.LeanEX)) < 1e-6 && Math.Abs(e.Debug.UiOffsetY - (c.Dirs[3].OffY + c.LeanEY)) < 1e-6,
                "settled (" + F(e.Debug.UiOffsetX) + "," + F(e.Debug.UiOffsetY) + ") D+E (" + F(c.Dirs[3].OffX + c.LeanEX) + "," + F(c.Dirs[3].OffY + c.LeanEY) + ")");

            // W/S rule on; ApplyToFwdBack = false passes W/S through
            List<Hold> ws = new List<Hold> { new Hold('S', 0, 1000), new Hold('W', 400, 1000) };
            string wOn = Pattern(ws, fps, 1100, 1);
            InputResolver.ApplyToFwdBack = false;
            InputResolver rr = new InputResolver();
            InputState both = rr.Resolve(In("WS"));
            InputResolver.ApplyToFwdBack = true;
            Check("D2 W/S: S held, W pressed -> W (ApplyToFwdBack); off -> both pass through (v2.4 cancel)", wOn == "SW-" && both.Fwd && both.Back,
                "pattern " + wOn + ", off: Fwd=" + both.Fwd + " Back=" + both.Back);
        }

        // ---------------- D3: random timelines ----------------

        static List<Hold> RandomTimeline(Random rnd, string keys, double endMs)
        {
            List<Hold> tl = new List<Hold>();
            foreach (char k in keys)
            {
                double t = rnd.NextDouble() * 200;
                while (t < endMs)
                {
                    double len = rnd.Next(3) == 0 ? rnd.NextDouble() * 30 : rnd.NextDouble() * 400;
                    tl.Add(new Hold(k, t, t + len));
                    t += len + (rnd.Next(3) == 0 ? rnd.NextDouble() * 20 : rnd.NextDouble() * 300);
                }
            }
            return tl;
        }

        static void D3Random()
        {
            GlobalSettings g = G();
            Profile m4 = M4();
            double[] rates = { 60, 144, 360 };
            Random rnd = new Random(2410);
            int timelines = 10000, both = 0, freezes = 0, oldFreezes = 0, dirMismatch = 0, passMismatch = 0;
            double worstFreeze = 0;
            long frames = 0;
            for (int it = 0; it < timelines; it++)
            {
                double fps = rates[it % 3];
                List<Hold> tl = RandomTimeline(rnd, "WASD", 1500);
                MotionEngine e = new MotionEngine(), eOld = new MotionEngine();
                InputResolver r = new InputResolver();
                double[] still = new double[2], stillOld = new double[2];
                double[] last = new double[2], lastOld = new double[2];
                int n = (int)Math.Round(1.5 * fps);
                Random lean = new Random(it);
                for (int k = 0; k <= n; k++)
                {
                    InputState raw = At(tl, k * 1000.0 / fps);
                    raw.Fire = lean.Next(10) == 0; raw.LeanL = lean.Next(7) == 0; raw.Ads = lean.Next(13) == 0; raw.FreeLook = lean.Next(11) == 0; raw.HandSwitch = lean.Next(17) == 0;
                    InputState o = r.Resolve(raw);
                    if ((o.Left && o.Right) || (o.Fwd && o.Back)) both++;
                    if (o.Fire != raw.Fire || o.LeanL != raw.LeanL || o.LeanR != raw.LeanR || o.Ads != raw.Ads || o.FreeLook != raw.FreeLook || o.HandSwitch != raw.HandSwitch) passMismatch++;
                    raw.LeanL = false; o.LeanL = false; raw.HandSwitch = false; o.HandSwitch = false; // targets = plain 正常手 direction offsets for the freeze check
                    e.Update(1.0 / fps, o, m4, g, PreviewMode.Live, 1);
                    eOld.Update(1.0 / fps, raw, m4, g, PreviewMode.Live, 1);
                    frames++;
                    int dir = MotionEngine.DirIndex(o);
                    if (e.CurrentDir != dir) dirMismatch++;
                    bool[] active = { o.Left || o.Right, o.Fwd || o.Back };
                    for (int a = 0; a < 2; a++)
                    {
                        double tgt = dir < 0 ? 0 : (a == 0 ? m4.Dirs[dir].OffX : m4.Dirs[dir].OffY);
                        double v = a == 0 ? e.MoveX : e.MoveY, vo = a == 0 ? eOld.MoveX : eOld.MoveY;
                        // frozen = an active (non-superseded) key held, ring not moving on that axis, not on its target
                        if (active[a] && k > 0 && Math.Abs(v - last[a]) < 1e-12 && Math.Abs(v - tgt) > 1e-6) still[a] += 1000.0 / fps; else still[a] = 0;
                        if (active[a] && k > 0 && Math.Abs(vo - lastOld[a]) < 1e-12 && Math.Abs(vo - tgt) > 1e-6) stillOld[a] += 1000.0 / fps; else stillOld[a] = 0;
                        if (still[a] > 50) freezes++;
                        if (stillOld[a] > 50 && stillOld[a] - 1000.0 / fps <= 50) oldFreezes++;
                        worstFreeze = Math.Max(worstFreeze, still[a]);
                        last[a] = v; lastOld[a] = vo;
                    }
                }
            }
            Check("D3 " + timelines + " random W/A/S/D timelines (60/144/360 fps): never both keys of a pair active, other keys untouched, engine follows the resolved keys",
                both == 0 && passMismatch == 0 && dirMismatch == 0, frames + " frames; both-active=" + both + " pass-through mismatches=" + passMismatch + " dir mismatches=" + dirMismatch);
            Check("D3 while a non-superseded key is held the ring never freezes > 50 ms", freezes == 0,
                "longest stall away from the active key's target=" + F(worstFreeze) + " ms (v2.4 on the same timelines: " + oldFreezes + " stalls > 50 ms, parked at centre during overlaps)");

            // ApplyToFwdBack = false: W/S exactly as v2.4
            InputResolver.ApplyToFwdBack = false;
            int wsMismatch = 0; double worst = 0;
            Random r2 = new Random(77);
            for (int it = 0; it < 2000; it++)
            {
                double fps = rates[it % 3];
                List<Hold> mixed = RandomTimeline(r2, "WASD", 1000);
                List<Hold> wsOnly = RandomTimeline(r2, "WS", 1000);
                InputResolver r = new InputResolver();
                MotionEngine a = new MotionEngine(), b = new MotionEngine();
                InputResolver rw = new InputResolver();
                int n = (int)Math.Round(fps);
                for (int k = 0; k <= n; k++)
                {
                    double t = k * 1000.0 / fps;
                    InputState raw = At(mixed, t), o = r.Resolve(raw);
                    if (o.Fwd != raw.Fwd || o.Back != raw.Back) wsMismatch++;
                    InputState w = At(wsOnly, t);
                    a.Update(1.0 / fps, rw.Resolve(w), m4, g, PreviewMode.Live, 1);
                    b.Update(1.0 / fps, w, m4, g, PreviewMode.Live, 1);
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.OffsetX - b.OffsetX), Math.Abs(a.OffsetY - b.OffsetY)));
                }
            }
            InputResolver.ApplyToFwdBack = true;
            Check("D3 ApplyToFwdBack = false: W/S exactly v2.4 (2000 timelines)", wsMismatch == 0 && worst == 0,
                "W/S resolved != raw: " + wsMismatch + " frames; W/S-only ring max |diff| vs v2.4=" + worst.ToString("E1"));
        }

        // ---------------- D4: live-input filter (reset on ring on / back to 实时; previews untouched) ----------------

        static void D4Filter()
        {
            LiveInputFilter f = new LiveInputFilter();
            InputState a = In("A"), ad = In("AD");
            f.Next(a, true, true);
            InputState s1 = f.Next(ad, true, true);                 // D pressed after A -> D
            InputState p1 = f.Next(ad, true, false);                // preview: raw, untouched
            InputState s2 = f.Next(ad, true, true);                 // back to 实时: reset -> both fresh in one poll -> centre
            InputState s3 = f.Next(ad, false, true);                // ring off: keeps resolving, no reset
            InputState s4 = f.Next(ad, true, true);                 // ring on again: reset
            InputState s5 = f.Next(In("D"), true, true);             // A released -> D (the tie is resolved)
            bool ok = s1.Right && !s1.Left && p1.Left && p1.Right && !s2.Left && !s2.Right && !s3.Left && !s3.Right && !s4.Left && !s4.Right && s5.Right;
            // without the reset at 实时 the stale history would keep D active (A superseded) — show the reset matters:
            LiveInputFilter g2 = new LiveInputFilter();
            g2.Next(a, true, true); g2.Next(ad, true, true);
            InputState noReset = g2.Next(ad, true, true);
            Check("D4 LiveInputFilter: resolves live only, previews get raw keys, resets when the ring is switched on and when the preview returns to 实时",
                ok && noReset.Right, "D after A=" + s1.Right + " preview raw=" + (p1.Left && p1.Right) + " reset on 实时=" + (!s2.Left && !s2.Right) +
                " ring on reset=" + (!s4.Left && !s4.Right) + " then D=" + s5.Right);
        }
    }
}
