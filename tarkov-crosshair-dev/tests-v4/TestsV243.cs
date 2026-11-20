using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    // v2.4.3 tests: 跳跃 (Space) in 3 phases.
    public static partial class Program
    {
        static void V243Tests()
        {
            E1Path(); E2Split(); E3Edges(); E4SameAsV242(); E5Compose(); E6Settings();
        }

        static Profile JumpProfile()
        {
            Profile p = M4();
            p.Jump = new JumpSettings(); // the v2.4.3 jump defaults these tests were written around (v3.0: the built-in has Leo's jump)
            p.Jump.Enabled = true;
            return p;
        }

        static InputState Space(InputState i) { i.Jump = true; return i; }

        static JumpSettings RandomJump(Random rnd)
        {
            JumpSettings j = new JumpSettings();
            j.TotalMs = 100 + rnd.Next(2901);
            JumpSplit.SetTotal(j, (int)j.TotalMs);
            JumpSplit.SetPhase(j, rnd.Next(3), rnd.Next((int)j.TotalMs + 1));
            for (int i = 0; i < 3; i++)
            {
                j.Phases[i].EndPx = i == 2 ? 0 : Math.Round(rnd.NextDouble() * 200 - 100, 1);
                j.Phases[i].Curve = JumpSettings.Curves[rnd.Next(4)];
            }
            j.Enabled = true;
            return j;
        }

        // ---------------- E1: path ----------------

        static void E1Path()
        {
            Random rnd = new Random(243);
            List<JumpSettings> js = new List<JumpSettings>();
            JumpSettings def = new JumpSettings(); def.Enabled = true; js.Add(def);
            for (int i = 0; i < 300; i++) js.Add(RandomJump(rnd));
            int bad = 0; string first = ""; double worstSeam = 0;
            foreach (JumpSettings j in js)
            {
                double[] b = JumpPath.Boundaries(j);
                bool ok = JumpPath.Offset(j, 0) == 0 && JumpPath.Offset(j, j.TotalMs) == 0 && JumpPath.Offset(j, j.TotalMs + 50) == 0 && b[3] == j.TotalMs;
                for (int k = 1; k <= 2; k++)
                {
                    // (a phase boundary at t = 0, i.e. a 0 ms first phase, is never sampled: the first frame is already past it)
                    if ((j.Phases[k].Ms > 0 || k == 2) && b[k] > 0) { if (JumpPath.Offset(j, b[k]) != j.Phases[k - 1].EndPx && b[k] < j.TotalMs) ok = false; }
                    double left = JumpPath.Offset(j, b[k] - 1e-9);
                    if (j.Phases[k - 1].Ms > 0) worstSeam = Math.Max(worstSeam, Math.Abs(left - j.Phases[k - 1].EndPx));
                }
                if (j.Phases[2].Ms > 0) worstSeam = Math.Max(worstSeam, Math.Abs(JumpPath.Offset(j, b[3] - 1e-9)));
                // within each phase the offset stays between the phase's start and end (curves cannot overshoot)
                double from = 0;
                for (int k = 0; k < 3; k++)
                {
                    double to = k == 2 ? 0 : j.Phases[k].EndPx, lo = Math.Min(from, to) - 1e-9, hi = Math.Max(from, to) + 1e-9;
                    for (int s = 0; s <= 50; s++)
                    {
                        double v = JumpPath.Offset(j, b[k] + j.Phases[k].Ms * s / 50.0 * 0.999999);
                        if (j.Phases[k].Ms > 0 && (v < lo || v > hi)) ok = false;
                    }
                    from = to;
                }
                if (!ok) { bad++; if (first.Length == 0) first = j.TotalMs + "=" + string.Join("/", JumpSplit.Get(j)); }
            }
            Check("E1 path: each phase ends exactly at its EndPx, exactly 0 at 总时长, no overshoot inside a phase, continuous at the seams (" + js.Count + " jumps)",
                bad == 0 && worstSeam < 1e-5, bad + " bad " + first + "; max jump at a phase seam=" + worstSeam.ToString("E1") + " px");

            // engine at 60 / 144 / 360 fps: the ring follows JumpPath exactly, max frame step bounded, back on 0 at 总时长
            string res = ""; bool all = true;
            foreach (double fps in new double[] { 60, 144, 360 })
            {
                Profile p = JumpProfile();
                GlobalSettings g = G();
                MotionEngine e = new MotionEngine();
                for (int i = 0; i < 30; i++) e.Update(1.0 / fps, In(""), p, g, PreviewMode.Live, 1);
                double baseY = e.Debug.UiOffsetY, worst = 0, step = 0, last = baseY, endT = double.NaN, peak = 0, dip = 0;
                int n = (int)Math.Round(1.0 * fps);
                for (int k = 1; k <= n; k++)
                {
                    e.Update(1.0 / fps, k == 1 ? Space(In("")) : In(""), p, g, PreviewMode.Live, 1);
                    double t = k * 1000.0 / fps;
                    double want = t >= p.Jump.TotalMs ? 0 : JumpPath.Offset(p.Jump, t);
                    worst = Math.Max(worst, Math.Abs(e.Debug.UiOffsetY - baseY - want));
                    step = Math.Max(step, Math.Abs(e.Debug.UiOffsetY - last)); last = e.Debug.UiOffsetY;
                    if (double.IsNaN(endT) && !e.Jumping) endT = t;
                    peak = Math.Max(peak, e.Debug.UiOffsetY - baseY); dip = Math.Min(dip, e.Debug.UiOffsetY - baseY);
                }
                bool ok = worst < 1e-12 && endT >= 700 - 1e-9 && endT < 700 + 1000.0 / fps + 1e-9 && e.Debug.UiOffsetY == baseY && step <= 0.3 * 1000.0 / fps + 1e-9;
                if (!ok) all = false;
                res += "@" + fps + ": |ring − path|≤" + worst.ToString("E1") + " ended at " + F(endT) + " ms, peak +" + F(peak) + " dip " + F(dip) + ", max step " + F(step) + "px; ";
            }
            Check("E1 engine follows the path at 60/144/360 fps, ends exactly on the start at 总时长 (defaults 700 = 210/280/210, −30/+10/0)", all, res);
        }

        // ---------------- E2: total lock ----------------

        static void E2Split()
        {
            Random rnd = new Random(20261012);
            JumpSettings j = new JumpSettings();
            int bad = 0; string first = "";
            for (int it = 0; it < 20000; it++)
            {
                int[] old = JumpSplit.Get(j);
                int t0 = (int)j.TotalMs;
                int op = rnd.Next(10);
                bool ok = true; string what;
                if (op < 4)
                {
                    int nt = rnd.Next(-50, 3200);
                    JumpSplit.SetTotal(j, nt);
                    int ct = Math.Max(100, Math.Min(3000, nt));
                    what = "SetTotal(" + nt + ")";
                    if (j.TotalMs != ct) ok = false;
                    if (old[0] + old[1] + old[2] > 0)
                        for (int i = 0; i < 3; i++) if (Math.Abs(JumpSplit.Get(j)[i] - old[i] * (double)ct / t0) > 2.0 + 1e-9) ok = false;
                }
                else if (op < 9)
                {
                    int idx = rnd.Next(3), v = rnd.Next(-50, 3200);
                    if (rnd.Next(8) == 0) v = t0; // pushes the other two to 0
                    JumpSplit.SetPhase(j, idx, v);
                    what = "SetPhase(" + idx + "," + v + ")";
                    int[] ph = JumpSplit.Get(j);
                    int cv = Math.Max(0, Math.Min(t0, v)), a = (idx + 1) % 3, b = (idx + 2) % 3, rest = t0 - cv;
                    double wantA = (old[a] + old[b]) > 0 ? rest * (double)old[a] / (old[a] + old[b]) : rest / 2.0;
                    if (j.TotalMs != t0 || ph[idx] != cv || Math.Abs(ph[a] - wantA) > 0.5 + 1e-9) ok = false;
                }
                else
                {
                    JumpSplit.Auto(j);
                    what = "Auto";
                    int[] ph = JumpSplit.Get(j);
                    for (int i = 0; i < 3; i++) if (Math.Abs(ph[i] - t0 * JumpSplit.AutoPct[i] / 100.0) > 1.0 + 1e-9) ok = false;
                }
                int[] now = JumpSplit.Get(j);
                if (now[0] + now[1] + now[2] != (int)j.TotalMs || now[0] < 0 || now[1] < 0 || now[2] < 0 || j.TotalMs < 100 || j.TotalMs > 3000) ok = false;
                if (!ok) { bad++; if (first.Length == 0) first = what + " -> " + string.Join("/", now) + " T=" + j.TotalMs; }
            }
            Check("E2 total lock: 20000 random edits keep sum == 总时长, no negatives, proportional, 自动分配 30/40/30", bad == 0, bad + " bad " + first);

            JumpSettings a1 = new JumpSettings(); JumpSplit.SetTotal(a1, 1000);
            JumpSettings a2 = new JumpSettings(); JumpSplit.SetPhase(a2, 0, 350);
            JumpSettings a3 = new JumpSettings(); JumpSplit.SetPhase(a3, 1, 700); JumpSplit.SetPhase(a3, 1, 300); // the other two were 0 -> evenly
            JumpSettings a4 = new JumpSettings(); JumpSplit.SetPhase(a4, 2, 700); JumpSplit.SetTotal(a4, 1400);
            JumpSettings a5 = new JumpSettings(); a5.Phases[0].Ms = 0; a5.Phases[1].Ms = 0; a5.Phases[2].Ms = 0; JumpSplit.SetTotal(a5, 500);
            JumpSettings a6 = new JumpSettings(); JumpSplit.SetTotal(a6, 20); JumpSettings a7 = new JumpSettings(); JumpSplit.SetTotal(a7, 9000);
            JumpSettings a8 = new JumpSettings(); JumpSplit.SetPhase(a8, 0, 50); JumpSplit.Auto(a8);
            Check("E2 examples: 700→1000, phase edits, both-zero split evenly, all-zero → 自动分配, clamps 100..3000",
                S(a1) == "300/400/300" && S(a2) == "350/200/150" && S(a3) == "200/300/200" && S(a4) == "0/0/1400" && S(a5) == "150/200/150"
                && a6.TotalMs == 100 && S(a6) == "30/40/30" && a7.TotalMs == 3000 && S(a7) == "900/1200/900" && S(a8) == "210/280/210",
                "700→1000: " + S(a1) + "; 1→350: " + S(a2) + "; 2→700 then 300: " + S(a3) + "; 3→700 then 1400: " + S(a4) + "; zeros→500: " + S(a5) +
                "; 20→" + a6.TotalMs + " " + S(a6) + "; 9000→" + a7.TotalMs + "; 自动分配: " + S(a8));
        }

        static string S(JumpSettings j) { return string.Join("/", JumpSplit.Get(j)); }

        // ---------------- E3: press edges ----------------

        static int CountJumps(Profile p, GlobalSettings g, double fps, Func<double, bool> space, double endMs, out double lastEnd)
        {
            MotionEngine e = new MotionEngine();
            int jumps = 0; bool was = false; lastEnd = double.NaN;
            int n = (int)Math.Round(endMs / 1000.0 * fps);
            for (int k = 0; k <= n; k++)
            {
                double t = k * 1000.0 / fps;
                InputState i = In(""); i.Jump = space(t);
                e.Update(1.0 / fps, i, p, g, PreviewMode.Live, 1);
                if (e.Jumping && !was) jumps++;
                if (was && !e.Jumping) lastEnd = t;
                was = e.Jumping;
            }
            return jumps;
        }

        static void E3Edges()
        {
            GlobalSettings g = G();
            Profile p = JumpProfile();
            string res = ""; bool all = true;
            foreach (double fps in new double[] { 60, 144, 360 })
            {
                double f = 1000.0 / fps, end;
                int hold = CountJumps(p, g, fps, delegate(double t) { return t < 2000; }, 2500, out end);
                int mid = CountJumps(p, g, fps, delegate(double t) { return t < f / 2 || (t >= 300 && t < 300 + f); }, 1500, out end);
                double midEnd = end;
                // press 1 frame at 0; press again on the first frame after the jump ended (that frame + the release before it)
                MotionEngine e = new MotionEngine();
                int starts = 0; bool was = false; double endAt = double.NaN, secondAt = double.NaN; bool released = false;
                for (int k = 0; k <= (int)(2.0 * fps); k++)
                {
                    double t = k * f;
                    InputState i = In("");
                    if (k == 0) i.Jump = true;
                    else if (!double.IsNaN(endAt) && double.IsNaN(secondAt)) { i.Jump = true; secondAt = t; }
                    e.Update(1.0 / fps, i, p, g, PreviewMode.Live, 1);
                    if (e.Jumping && !was) starts++;
                    if (was && !e.Jumping && double.IsNaN(endAt)) endAt = t;
                    if (!i.Jump) released = true;
                    was = e.Jumping;
                }
                bool ok = hold == 1 && mid == 1 && midEnd >= 700 - f - 1e-9 && midEnd <= 700 + 1e-9 && starts == 2 && secondAt - endAt <= f + 1e-9 && released;
                if (!ok) all = false;
                res += "@" + fps + ": hold 2 s=" + hold + " jump, 2nd press at 300 ms ignored (end " + F(midEnd) + " ms), press 1 frame after the end → " + starts + " jumps; ";
            }
            Check("E3 press edge only: holding Space = one jump; a press mid-jump is ignored; a press right after the end starts a new jump", all, res);

            // hidden (开镜 / 自由视角) keeps the clock; ring switched on resets it; Enabled off mid-jump stops it
            MotionEngine h = new MotionEngine();
            h.Update(0.01, Space(In("")), p, g, PreviewMode.Live, 1);
            for (int i = 0; i < 30; i++) h.Update(0.01, In("R"), p, g, PreviewMode.Live, 1);
            bool hidden = h.Jumping && Math.Abs(h.JumpMs - 310) < 1e-9 && h.Alpha == 0;
            for (int i = 0; i < 20; i++) h.Update(0.01, In("F"), p, g, PreviewMode.Live, 1);
            bool hiddenF = h.Jumping && Math.Abs(h.JumpMs - 510) < 1e-9;
            GlobalSettings og = G(); MotionEngine o = new MotionEngine();
            o.Update(0.01, Space(In("")), p, og, PreviewMode.Live, 1);
            og.Enabled = false; o.Update(0.01, In(""), p, og, PreviewMode.Live, 1);
            bool offStill = o.Jumping;
            og.Enabled = true; o.Update(0.01, In(""), p, og, PreviewMode.Live, 1);
            bool reset = !o.Jumping && o.JumpOffset == 0;
            Profile q = JumpProfile(); MotionEngine d = new MotionEngine();
            d.Update(0.01, Space(In("")), q, g, PreviewMode.Live, 1);
            q.Jump.Enabled = false; d.Update(0.01, In(""), q, g, PreviewMode.Live, 1);
            Check("E3 clock keeps running while hidden (开镜 / 自由视角); switching the ring on resets it; unticking stops it",
                hidden && hiddenF && offStill && reset && !d.Jumping, "开镜 at 310 ms=" + hidden + " 自由视角 at 510 ms=" + hiddenF + " ring off→on resets=" + (offStill && reset) + " untick stops=" + !d.Jumping);

            // preview 「跳跃（站着）」: one jump every 总时长 + 500 ms; status flag
            MotionEngine pv = new MotionEngine();
            int jumps = 0; bool pw = false; double firstStart = double.NaN, secondStart = double.NaN;
            for (int k = 1; k <= 3000; k++)
            {
                pv.Update(0.001, In("AD"), p, g, PreviewMode.JumpStand, 1);
                if (pv.Jumping && !pw) { jumps++; if (jumps == 1) firstStart = k; else if (jumps == 2) secondStart = k; }
                pw = pv.Jumping;
                if (pv.Debug.Jumping != pv.Jumping) jumps = -99;
            }
            Check("E3 preview 「跳跃（站着）」: a jump every 总时长 + 500 ms (1200 ms), no other keys; status shows 跳跃中 while it runs",
                jumps == 3 && Math.Abs(secondStart - firstStart - 1200) <= 1 && pv.MoveX == 0, "jumps in 3 s=" + jumps + " first at " + F(firstStart) + " ms, second " + F(secondStart) + " ms, move x=" + F(pv.MoveX));
        }

        // ---------------- E4: off = v2.4.2 ----------------

        static V242.InputState V242In(InputState i)
        {
            V242.InputState l = new V242.InputState();
            l.Fwd = i.Fwd; l.Back = i.Back; l.Left = i.Left; l.Right = i.Right; l.LeanL = i.LeanL; l.LeanR = i.LeanR; l.Fire = i.Fire; l.Ads = i.Ads;
            l.FreeLook = i.FreeLook; l.HandSwitch = i.HandSwitch;
            return l;
        }

        static void E4SameAsV242()
        {
            Random rnd = new Random(4242);
            double[] rates = { 60, 144, 360 };
            double worst = 0; long frames = 0; int presses = 0;
            for (int it = 0; it < 2000; it++)
            {
                double fps = rates[it % 3];
                Profile p = M4(); p.Jump.Enabled = false; // v3.0: the built-in has 跳跃 on
                if (it % 4 == 1) { p.Dirs[2].OffX += 7; Profile.RecomputeDiagonals(p); p.RecoilDriftX = 4; p.StandOffsetY = -3; }
                if (it % 4 == 2) { p.HandSwitchMs = 150; p.CurveWS.SetPreset(1); }
                p.Jump.TotalMs = 300 + rnd.Next(800); JumpSplit.SetTotal(p.Jump, (int)p.Jump.TotalMs); // disabled, but tuned
                GlobalSettings g = G();
                if (it % 5 == 3) { g.LeanMode = "toggle"; g.AdsMode = "toggle"; g.HandMode = "hold"; }
                if (it % 7 == 4) { g.ResGameW = 1920; g.ResGameH = 1080; }
                // v5.1 (W21, decision 7): A->D / mid-travel changes now go back to 0 first, so v2.4.2 is no longer the reference for
                // key timelines; 跳跃 off is checked against the same engine fed the same keys WITHOUT Space (b).
                MotionEngine a = new MotionEngine(); MotionEngine b = new MotionEngine();
                int segs = 6 + rnd.Next(10);
                for (int sgm = 0; sgm < segs; sgm++)
                {
                    string k = "";
                    foreach (char c in "WASDQELRFK") if (rnd.Next(5) == 0) k += c;
                    bool space = rnd.Next(2) == 0;
                    int len = 1 + rnd.Next((int)(0.4 * fps));
                    for (int i = 0; i < len; i++)
                    {
                        InputState inp = In(i < 2 ? k : k.Replace("K", ""));
                        inp.Jump = space && i < 3;
                        if (inp.Jump && i == 0) presses++;
                        // v4.1 (BRUTE40, Leo 17 Nov): 开关键 off->on now SNAPS 换手 / 侧身 back (V41-B5), where v2.4.2 slid, so the
                        // ring stays on here; the off->on path is covered by V41-B5 and the PERF P1 golden trace.
                        a.Update(1.0 / fps, inp, p, g, PreviewMode.Live, 1);
                        InputState noSpace = inp; noSpace.Jump = false;
                        b.Update(1.0 / fps, noSpace, p, g, PreviewMode.Live, 1);
                        double d = Math.Max(Math.Abs(a.OffsetX - b.OffsetX), Math.Abs(a.OffsetY - b.OffsetY));
                        d = Math.Max(d, Math.Max(Math.Abs(a.Alpha - b.Alpha), Math.Abs(a.Recoil - b.Recoil)));
                        if (a.OffsetX != b.OffsetX || a.OffsetY != b.OffsetY || a.Alpha != b.Alpha) d = Math.Max(d, 1e-300);
                        worst = Math.Max(worst, d);
                        frames++;
                    }
                }
            }
            // previews 1..15 (all v2.4.2 previews) with the jump off
            double pw = 0;
            for (int pm = 1; pm <= 15; pm++)
            {
                if (pm == (int)PreviewMode.TurnAD || pm == (int)PreviewMode.TurnWS) continue; // v5.1: reversals changed on purpose
                Profile p = M4(); p.Jump.Enabled = false; GlobalSettings g = G(); g.EditHand = pm % 2;
                MotionEngine a = new MotionEngine(); V242.MotionEngine b = new V242.MotionEngine();
                for (int i = 0; i < 1500; i++)
                {
                    a.Update(0.002, In(""), p, g, (PreviewMode)pm, 0.7);
                    b.Update(0.002, new V242.InputState(), p, g, (V242.PreviewMode)pm, 0.7);
                    if (a.OffsetX != b.OffsetX || a.OffsetY != b.OffsetY || a.Alpha != b.Alpha) pw = Math.Max(pw, 1);
                }
            }
            Check("E4 跳跃 off: Space changes nothing (bit-identical to the same keys without Space) on 2000 random timelines, + the old previews = v2.4.2 (A->D / W->S previews excluded: v5.1 reversal)",
                worst == 0 && pw == 0, frames + " frames, " + presses + " Space presses; max |diff|=" + worst.ToString("E1") + " (0 = bit-identical); previews identical=" + (pw == 0));
        }

        // ---------------- E5: composition + resolution ----------------

        static void E5Compose()
        {
            string res = ""; bool all = true;
            foreach (double h in new double[] { 1440, 1080 })
            {
                Profile on = JumpProfile(); on.RecoilDriftX = 6; on.StandOffsetY = 2;
                Profile off = on.Clone(); off.Jump.Enabled = false;
                GlobalSettings g = G(); g.ResGameH = h; g.ResGameW = h * 1.5;
                double rs = g.ResolutionScale;
                MotionEngine a = new MotionEngine(), b = new MotionEngine();
                double worst = 0, peak = 0; int seg = 0;
                foreach (string k in new string[] { "A", "AL", "QA", "QAL", "WD", "", "L", "D", "S" })
                {
                    for (int i = 0; i < 260; i++)
                    {
                        InputState inp = In(k);
                        inp.Jump = i == 3 && seg % 2 == 0;
                        a.Update(0.003, inp, on, g, PreviewMode.Live, 1);
                        b.Update(0.003, inp, off, g, PreviewMode.Live, 1);
                        double want = rs * a.JumpOffset;
                        worst = Math.Max(worst, Math.Max(Math.Abs(a.OffsetX - b.OffsetX), Math.Abs((b.OffsetY - a.OffsetY) - want)));
                        peak = Math.Max(peak, Math.Abs(b.OffsetY - a.OffsetY));
                    }
                    seg++;
                }
                bool ok = worst < 1e-9 && Math.Abs(peak - rs * 30) < 0.5;
                if (!ok) all = false;
                res += "s=" + F(rs) + ": |(with − without) − s·jump|≤" + worst.ToString("E1") + ", peak lift " + F(peak) + " px (s·30=" + F(rs * 30) + "); ";
            }
            Check("E5 跳跃 is additive (vertical only, on top of movement / lean combos / recoil / 站着偏移) and scaled by 分辨率换算", all, res);

            // both hands share it
            Profile p = JumpProfile(); p.StandOffsetX = 5;
            GlobalSettings gg = G();
            MotionEngine e = new MotionEngine();
            e.Update(0.001, In("K"), p, gg, PreviewMode.Live, 1);
            for (int i = 0; i < 400; i++) e.Update(0.001, In(""), p, gg, PreviewMode.Live, 1);
            e.Update(0.001, Space(In("")), p, gg, PreviewMode.Live, 1);
            for (int i = 0; i < 209; i++) e.Update(0.001, In(""), p, gg, PreviewMode.Live, 1);
            Check("E5 跳跃 shared by both hands (换手 active: same offset)", e.LiveSwitched && Math.Abs(e.JumpOffset - new JumpSettings().Phases[0].EndPx) < 1e-9 && Math.Abs(e.Debug.UiOffsetY - new JumpSettings().Phases[0].EndPx) < 1e-9,
                "switched=" + e.LiveSwitched + " jump at 210 ms=" + F(e.JumpOffset) + " ring y=" + F(e.Debug.UiOffsetY));
        }

        // ---------------- E6: settings ----------------

        static void E6Settings()
        {
            // defaults + round-trip
            JumpSettings d = new JumpSettings();
            bool defs = !d.Enabled && d.TotalMs == 700 && S(d) == "210/280/210" && d.Phases[0].EndPx == -30 && d.Phases[1].EndPx == 10 && d.Phases[2].EndPx == 0
                && d.Phases[0].Curve == "out" && d.Phases[1].Curve == "smooth" && d.Phases[2].Curve == "out";
            GlobalSettings g = G();
            Profile a = g.Active;
            a.Jump.Enabled = true; JumpSplit.SetTotal(a.Jump, 900); a.Jump.Phases[0].EndPx = -12.5; a.Jump.Phases[1].Curve = "linear";
            g.Keys.Jump = 0x58;
            GlobalSettings back; string err;
            bool ok = SettingsIO.TryParse(SettingsIO.Serialize(g), out back, out err);
            bool rt = ok && back.Active.Jump.Enabled && back.Active.Jump.TotalMs == 900 && S(back.Active.Jump) == S(a.Jump) && back.Active.Jump.Phases[0].EndPx == -12.5
                && back.Active.Jump.Phases[1].Curve == "linear" && back.Keys.Jump == 0x58 && SettingsIO.Serialize(back) == SettingsIO.Serialize(g);
            List<Profile> imp; bool ok2 = SettingsIO.TryParseImport(SettingsIO.ExportProfile(a), out imp, out err);
            bool exp = ok2 && imp[0].Jump.Enabled && imp[0].Jump.TotalMs == 900;
            Check("E6 defaults (off, 700 = 210/280/210, −30 慢停 / +10 慢起慢停 / 0 慢停); settings round-trip + export/import carry 跳跃", defs && rt && exp,
                "defaults=" + defs + " round-trip=" + rt + " export/import=" + exp + (ok ? "" : " " + err));

            // clamps + inconsistent files
            GlobalSettings c;
            bool ok3 = SettingsIO.TryParse("{\"Keys\":{\"Jump\":999},\"Profiles\":[" +
                "{\"Name\":\"a\",\"Jump\":{\"Enabled\":true,\"TotalMs\":50,\"Phases\":[{\"Ms\":-5,\"EndPx\":9000,\"Curve\":\"zz\"},{\"Ms\":30,\"EndPx\":-9000,\"Curve\":\" LINEAR \"},{\"Ms\":30,\"EndPx\":7,\"Curve\":\"in\"}]}}," +
                "{\"Name\":\"b\",\"Jump\":{\"TotalMs\":9999,\"Phases\":[{\"Ms\":100},{\"Ms\":200}]}}," +
                "{\"Name\":\"c\",\"Jump\":{\"TotalMs\":800,\"Phases\":[{\"Ms\":2000,\"EndPx\":1},{\"Ms\":2000},{\"Ms\":2000}]}}," +
                "{\"Name\":\"e\",\"Jump\":{\"TotalMs\":800,\"Phases\":[{\"Ms\":0},{\"Ms\":0},{\"Ms\":0}]}}," +
                "{\"Name\":\"f\",\"Jump\":null}]}", out c, out err);
            bool cl = ok3 && c.Keys.Jump == 0x20;
            if (ok3)
            {
                JumpSettings ja = c.Profiles[0].Jump, jb = c.Profiles[1].Jump, jc = c.Profiles[2].Jump, je = c.Profiles[3].Jump, jf = c.Profiles[4].Jump;
                cl = cl && ja.Enabled && ja.Phases[0].Ms == 0 && ja.Phases[0].EndPx == 500 && ja.Phases[0].Curve == "out" && ja.Phases[1].EndPx == -500 && ja.Phases[1].Curve == "linear"
                    && ja.Phases[2].EndPx == 0 && ja.Phases[2].Curve == "in" && ja.TotalMs == 100 && S(ja) == "0/50/50"
                    && jb.TotalMs == 510 && S(jb) == "100/200/210"
                    && jc.TotalMs == 800 && JumpSplit.Get(jc)[0] + JumpSplit.Get(jc)[1] + JumpSplit.Get(jc)[2] == 800
                    && je.TotalMs == 800 && S(je) == "240/320/240" && !jf.Enabled && jf.TotalMs == 700;
            }
            Check("E6 clamps: key 999 → Space, 总时长 100..3000, phase ms ≥ 0, EndPx ±500, phase 3 end forced 0, bad curve → default, sums fixed, missing / null → defaults",
                cl, ok3 ? "a=" + c.Profiles[0].Jump.TotalMs + " " + S(c.Profiles[0].Jump) + " b=" + c.Profiles[1].Jump.TotalMs + " " + S(c.Profiles[1].Jump) +
                    " c=" + c.Profiles[2].Jump.TotalMs + " " + S(c.Profiles[2].Jump) + " e=" + S(c.Profiles[3].Jump) : err);

            // old files: every fixture + a v2.4.2-shaped file (current file with every 跳跃 field removed)
            string[] names = { "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json", "settings-v2.0.json", "settings-v2.1.json", "settings-v2.2.json", "settings-v2.3.json" };
            int good = 0; string bad = "";
            foreach (string name in names)
            {
                GlobalSettings f;
                bool fine = SettingsIO.TryParse(System.IO.File.ReadAllText(Fixture(name)), out f, out err) && f.Keys.Jump == 0x20
                    && f.Profiles.TrueForAll(delegate(Profile q) { return !q.Jump.Enabled && q.Jump.TotalMs == 700 && S(q.Jump) == "210/280/210" && q.Jump.Phases[0].EndPx == -30; });
                if (fine) good++; else bad += " " + name;
            }
            GlobalSettings cur = G(); cur.Active.Jump.Enabled = true; cur.Keys.Toggle = 0x75;
            JsonNode n = JsonNode.Parse(SettingsIO.Serialize(cur));
            n["Keys"].AsObject().Remove("Jump");
            n["Keys"].AsObject().Remove("JumpMods"); // v4.0 组合键 field (a v2.4.2 file has none)
            foreach (JsonNode pr in n["Profiles"].AsArray()) pr.AsObject().Remove("Jump");
            string v242 = n.ToJsonString();
            GlobalSettings v; bool vok = SettingsIO.TryParse(v242, out v, out err);
            bool v242ok = vok && !v242.Contains("Jump") && v.Keys.Jump == 0x20 && v.Keys.Toggle == 0x75 && v.Profiles.TrueForAll(delegate(Profile q) { return !q.Jump.Enabled && q.Jump.TotalMs == 700; });
            Check("E6 old files load with 跳跃 defaults (off) and 跳跃 key Space: fixtures v1 … v2.3 + a v2.4.2-shaped file", good == names.Length && v242ok,
                good + "/" + names.Length + (bad.Length > 0 ? " bad:" + bad : "") + "; v2.4.2-shaped file=" + v242ok);

            // key table
            KeyBindings k = new KeyBindings();
            HashSet<int> held = new HashSet<int>();
            held.Add(0x20);
            bool rd = MotionEngine.ReadInput(k, delegate(int vk) { return held.Contains(vk); }).Jump;
            GlobalSettings rk = G(); rk.Keys.Jump = 0x43; rk.ResetKeys();
            bool table = KeyBindings.ActionCount == 13 /* v4.3: the v4.1 转一圈校准 key (13) is gone */ && KeyBindings.ActionNames[11] == "跳跃" && k.Get(11) == 0x20 && k.FindOther(0x20, 0) == 11 && k.FindOther(0x20, 11) == -1
                && GlobalSettings.CreateDefault().Keys.Jump == 0x20 && rk.Keys.Jump == 0x20;
            k.Set(11, 0x51);
            bool conflict = k.FindOther(0x51, 11) == 5;
            Check("E6 key table: 跳跃 = action 11, Space (0x20), read by ReadInput, 按键恢复默认 → Space, 「这个键已经用于」 lookup (Q → 左侧身)", rd && table && conflict,
                "read=" + rd + " table=" + table + " conflict=" + conflict);

            // 保存为默认 / 恢复默认 include 跳跃; built-in 「Leo默认」 = Leo's jump (v3.0: on, 800 = 200 / 305 / 295)
            GlobalSettings s = G(); Profile p = s.Active;
            p.Jump.Enabled = true; JumpSplit.SetTotal(p.Jump, 1200); p.Jump.Phases[1].EndPx = -25;
            string snap = SettingsIO.ExportProfile(p);
            p.SaveAsDefault("2026-10-12 19:00");
            p.Jump.Enabled = false; JumpSplit.Auto(p.Jump); p.Jump.Phases[0].EndPx = 2;
            Profile r = s.RestoreSaved(p);
            bool saved = SettingsIO.ExportProfile(r) == snap && r.Jump.Enabled && r.Jump.TotalMs == 1200 && r.Jump.Phases[1].EndPx == -25;
            Profile m = s.RestoreLeoDefault(r);
            bool m4 = m.Jump.Enabled && m.Jump.TotalMs == 800 && S(m.Jump) == "200/305/295" && m.SavedDefault.Jump.TotalMs == 1200
                && Profile.CreateLeoDefault("x").Jump.Enabled && GlobalSettings.CreateDefault().Active.Jump.Enabled;
            Check("E6 保存为默认 / 恢复到我存的默认 include 跳跃; 恢复到内置默认 → Leo's 跳跃 (on, 800 = 200/305/295), snapshot kept", saved && m4, "saved restore=" + saved + " default=" + m4);
        }
    }
}
