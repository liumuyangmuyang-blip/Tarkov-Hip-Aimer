using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程: pure maths of TutorialModel.cs (parallax model, peek timing, demo scripts, live/script arbiter,
    // number cache, RNG, terminal). Drawing is checked by tarkov-crosshair-dev/tutorial-render (PNG + alloc per frame).
    public static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V4TutorialInit() { V4TutorialTests(); }

        static void V4TutorialTests()
        {
            TutParallax();
            TutPeek();
            TutScript();
            TutScriptWithEngine();
            TutArbiter();
            TutNum();
            TutRng();
            TutTerminal();
        }

        static void TutParallax()
        {
            double x5, y5, x10, y10, x15, y15, x25, y25, x40, y40;
            TutorialGeometry.HipImpactPx(5, out x5, out y5);
            TutorialGeometry.HipImpactPx(10, out x10, out y10);
            TutorialGeometry.HipImpactPx(15, out x15, out y15);
            TutorialGeometry.HipImpactPx(25, out x25, out y25);
            TutorialGeometry.HipImpactPx(40, out x40, out y40);
            Check("TUT-1 25 m (归零点) impact is exactly the screen centre", x25 == 0 && y25 == 0, F(x25) + "," + F(y25));
            bool br = x5 > 0 && y5 < 0 && x10 > 0 && y10 < 0 && x15 > 0 && y15 < 0;
            Check("TUT-2 5/10/15 m impacts are bottom-right of the centre (X right +, Y up +)", br,
                "5m=(" + F(x5) + "," + F(y5) + ") 10m=(" + F(x10) + "," + F(y10) + ") 15m=(" + F(x15) + "," + F(y15) + ")");
            double m5 = Math.Sqrt(x5 * x5 + y5 * y5), m10 = Math.Sqrt(x10 * x10 + y10 * y10), m15 = Math.Sqrt(x15 * x15 + y15 * y15);
            Check("TUT-3 the closer the wall, the farther the impact from the centre", m5 > m10 && m10 > m15 && m15 > 0,
                F(m5) + " > " + F(m10) + " > " + F(m15));
            Check("TUT-4 beyond 25 m the line crosses to the top-left (model consistency)", x40 < 0 && y40 > 0, F(x40) + "," + F(y40));
            // same direction at every distance (a straight bullet line)
            double a5 = Math.Atan2(y5, x5), a10 = Math.Atan2(y10, x10), a15 = Math.Atan2(y15, x15);
            Check("TUT-5 impact direction is the same at 5/10/15 m", Math.Abs(a5 - a10) < 1e-12 && Math.Abs(a10 - a15) < 1e-12, F(a5) + " " + F(a10));
            // 1/d - 1/25 ratio: 5 m : 10 m = (0.2-0.04) / (0.1-0.04)
            Check("TUT-6 offsets follow f*m*(1/d - 1/25)", Math.Abs(m5 / m10 - (0.16 / 0.06)) < 1e-9, F(m5 / m10));
            double bx, by;
            TutorialGeometry.HipImpactPx(0, out bx, out by);
            double cx, cy;
            TutorialGeometry.HipImpactPx(double.NaN, out cx, out cy);
            double ix, iy;
            TutorialGeometry.HipImpactPx(double.PositiveInfinity, out ix, out iy);
            Check("TUT-7 distance 0 / NaN / infinity -> 0 (no crash, no NaN)", bx == 0 && by == 0 && cx == 0 && cy == 0 && ix == 0 && iy == 0, "");
            // wall <-> screen round trip
            bool rt = true;
            foreach (double d in new double[] { 5, 10, 15, 25, 7.3 })
                foreach (double px in new double[] { -300, -12.5, 0, 33, 480 })
                    if (Math.Abs(TutorialGeometry.WallToScreen(TutorialGeometry.ScreenToWall(px, d), d) - px) > 1e-9) rt = false;
            Check("TUT-8 screen px -> wall m -> screen px round trip", rt, "");
            Check("TUT-9 projected size halves when the distance doubles",
                Math.Abs(TutorialGeometry.ProjectedPx(1.6, 5) - 2 * TutorialGeometry.ProjectedPx(1.6, 10)) < 1e-9, "");
            Check("TUT-10 side view: path height is the muzzle drop at 0 m and 0 at 25 m",
                TutorialGeometry.PathHeightM(0, 25, -0.11) == -0.11 && Math.Abs(TutorialGeometry.PathHeightM(25, 25, -0.11)) < 1e-15, "");
            bool sm = TutorialGeometry.Smooth(-1) == 0 && TutorialGeometry.Smooth(0) == 0 && TutorialGeometry.Smooth(1) == 1 && TutorialGeometry.Smooth(2) == 1
                && Math.Abs(TutorialGeometry.Smooth(0.5) - 0.5) < 1e-12;
            double prev = -1; bool mono = true;
            for (int i = 0; i <= 100; i++) { double v = TutorialGeometry.Smooth(i / 100.0); if (v < prev) mono = false; prev = v; }
            Check("TUT-11 smoothstep clamps and is monotonic", sm && mono, "");
        }

        static void TutPeek()
        {
            // Leo: D+E (strafe + lean) is the fastest relative peek. The model must agree for every corner it can clear.
            bool fastest = true;
            string worst = "";
            for (int i = 1; i <= 60; i++)
            {
                double clear = i * 0.02; // 2 cm .. 1.2 m
                double de = PeekModel.TimeToClear(true, true, clear, 3.0);
                double d = PeekModel.TimeToClear(true, false, clear, 3.0);
                double e = PeekModel.TimeToClear(false, true, clear, 3.0);
                if (!(de <= d && de <= e)) { fastest = false; worst = F(clear) + "m de=" + F(de) + " d=" + F(d) + " e=" + F(e); }
            }
            Check("TUT-12 peek: D+E clears every corner no later than D or E alone (示意 model)", fastest, worst);
            bool strict = PeekModel.TimeToClear(true, true, 0.3, 3) < PeekModel.TimeToClear(true, false, 0.3, 3)
                && PeekModel.TimeToClear(true, true, 0.3, 3) < PeekModel.TimeToClear(false, true, 0.3, 3);
            Check("TUT-13 peek: D+E strictly faster at 0.3 m", strict, "");
            Check("TUT-14 peek: lean alone never clears more than its full shift",
                double.IsPositiveInfinity(PeekModel.TimeToClear(false, true, PeekModel.LeanShiftM + 0.01, 5)), "");
            bool mono = true; double prev = -1;
            for (int i = 0; i <= 2000; i++)
            {
                double x = PeekModel.CameraX(true, true, i / 1000.0);
                if (x < prev - 1e-12) mono = false;
                prev = x;
            }
            double r = PeekModel.StrafeRampS;
            bool cont = Math.Abs(PeekModel.StrafeX(r - 1e-9) - PeekModel.StrafeX(r + 1e-9)) < 1e-6;
            Check("TUT-15 peek: camera position monotonic, strafe continuous at the ramp end", mono && cont, "");
            Check("TUT-16 peek: t <= 0 / NaN gives 0", PeekModel.CameraX(true, true, 0) == 0 && PeekModel.CameraX(true, true, -1) == 0
                && PeekModel.CameraX(true, true, double.NaN) == 0 && PeekModel.TimeToClear(true, true, 0, 1) == 0, "");
        }

        static void TutScript()
        {
            TutorialScript s = new TutorialScript(4000,
                new ScriptSeg(500, 1500, TKey.Q | TKey.Fire), new ScriptSeg(2000, 3000, TKey.D | TKey.Hand), new ScriptSeg(3200, 3600, TKey.Jump));
            bool fire;
            InputState a = s.Sample(1000, false, false, out fire);
            bool holdOk = a.LeanL && fire && a.Fire && !a.Right;
            InputState b = s.Sample(1600, false, false, out fire);
            holdOk &= !b.LeanL && !fire;
            Check("TUT-17 script hold mode: Q and fire held for the interval", holdOk, "");
            // toggle mode: Q is a short press at 500 and at 1500, not held in between
            bool tog = s.Sample(510, true, true, out fire).LeanL && !s.Sample(1000, true, true, out fire).LeanL
                && s.Sample(1510, true, true, out fire).LeanL && !s.Sample(1600, true, true, out fire).LeanL;
            Check("TUT-18 script toggle mode: Q pressed at the start and the end of the interval", tog, "");
            bool hand = s.Sample(2010, false, true, out fire).HandSwitch && !s.Sample(2500, false, true, out fire).HandSwitch
                && s.Sample(2500, false, false, out fire).HandSwitch;
            Check("TUT-19 script 换手: toggle = presses, hold = held", hand, "");
            bool jump = s.Sample(3210, false, false, out fire).Jump && !s.Sample(3300, false, false, out fire).Jump
                && !s.Sample(3600, false, false, out fire).Jump;
            Check("TUT-20 script 跳跃: one short press at the interval start only", jump, "");
            bool wrap = s.Sample(4000 + 1000, false, false, out fire).LeanL && s.HeldAt(8000 + 2500) == (TKey.D | TKey.Hand)
                && s.HeldAt(-5) == 0 && s.HeldAt(double.NaN) == 0;
            Check("TUT-21 script loops (time wraps), negative / NaN time = start", wrap, "");
            Check("TUT-22 TKey.Of maps an InputState", TKey.Of(a, true) == (TKey.Q | TKey.Fire), TKey.Of(a, true).ToString());
        }

        static void TutScriptWithEngine()
        {
            // The conversion must make the REAL engine show the lean / hand exactly during the interval in both modes.
            foreach (string mode in new string[] { "hold", "toggle" })
            {
                GlobalSettings g = GlobalSettings.CreateDefault(); g.Validate();
                g.LeanMode = mode; g.HandMode = mode == "hold" ? "hold" : "toggle"; g.HandEnabled = true;
                g.OnlyWhenGameFocused = false;
                Profile p = g.Active;
                TutorialScript s = new TutorialScript(6000, new ScriptSeg(1000, 2500, TKey.Q), new ScriptSeg(3500, 4500, TKey.Hand));
                MotionEngine e = new MotionEngine();
                bool ok = true; string why = "";
                for (int f = 0; f < 6000 / 5; f++)
                {
                    double t = f * 5.0;
                    bool fire;
                    InputState i = s.Sample(t, g.LeanMode == "toggle", g.HandMode != "hold", out fire);
                    e.Update(0.005, i, p, g, PreviewMode.Live, 1.0);
                    // allow one frame for the edge
                    if (t > 1010 && t < 2490 && e.LeanTarget != -1) { ok = false; why = "lean off at " + t; }
                    if ((t < 995 || t > 2570) && e.LeanTarget != 0) { ok = false; why = "lean on at " + t; }
                    if (t > 3510 && t < 4490 && !e.LiveSwitched) { ok = false; why = "hand off at " + t; }
                    if ((t < 3495 || t > 4570) && e.LiveSwitched) { ok = false; why = "hand on at " + t; }
                }
                Check("TUT-23 script + MotionEngine (" + mode + "): lean / 换手 on exactly for the interval", ok, why);
            }
        }

        static void TutArbiter()
        {
            LiveArbiter a = new LiveArbiter();
            for (int i = 0; i < 100; i++) a.Update(10, false);
            bool s1 = !a.Live && Math.Abs(a.ScriptMs - 1000) < 1e-9;
            a.Update(10, true);
            bool l1 = a.Live;
            double t = 0; bool resumed = false; double resumeAt = -1;
            while (t < 4000) { a.Update(10, false); t += 10; if (a.Resumed && !resumed) { resumed = true; resumeAt = t; } }
            bool back = !a.Live && resumed && Math.Abs(resumeAt - LiveArbiter.HoldMs) <= 10 && a.ScriptMs > 0 && a.ScriptMs <= 4000 - LiveArbiter.HoldMs + 10;
            a.Update(double.NaN, false); a.Update(-5, false);
            Check("TUT-24 arbiter: script runs, live input holds 2.5 s, then the script restarts", s1 && l1 && back, "resumeAt=" + F(resumeAt) + " script=" + F(a.ScriptMs));
        }

        static void TutNum()
        {
            bool ok = TutorialNum.Int(0) == "0" && TutorialNum.Int(-30) == "-30" && TutorialNum.Int(2.5) == "3" && TutorialNum.Int(-2.5) == "-3"
                && TutorialNum.Signed(12) == "+12" && TutorialNum.Signed(-5) == "-5" && TutorialNum.Signed(0) == "0"
                && TutorialNum.Int(5000) == "5000" && TutorialNum.Signed(5000) == "+5000" && TutorialNum.Int(double.NaN) == "0"
                && TutorialNum.Int(1e300) != null && TutorialNum.Int(-1e300) != null;
            bool same = object.ReferenceEquals(TutorialNum.Int(-42), TutorialNum.Int(-42.2)); // cached: no new string per frame
            Check("TUT-25 number strings: rounding away from zero, signs, extremes, cached", ok && same, "");
        }

        static void TutRng()
        {
            TutorialRng a = new TutorialRng(7), b = new TutorialRng(7);
            bool same = true, range = true;
            for (int i = 0; i < 10000; i++)
            {
                double u = a.Unit();
                if (u != b.Unit()) same = false;
                if (u < 0 || u >= 1) range = false;
                double s = a.Spread(); b.Spread();
                if (s < -1 || s > 1) range = false;
            }
            TutorialRng z = new TutorialRng(0);
            Check("TUT-26 RNG deterministic, Unit in [0,1), Spread in [-1,1], seed 0 works", same && range && z.Next() != 0, "");
        }

        static void TutTerminal()
        {
            TutorialTerminal t = new TutorialTerminal();
            t.CharsPerSec = 100; // 10 ms per char
            t.Push("> abc");
            t.Push("> de");
            bool start = t.Count == 2 && t.Visible(0) == "" && t.Visible(1) == "";
            t.Update(25);
            bool partial = t.Visible(0) == "> " && t.Typing(0) && t.Visible(1) == "";
            t.Update(1000);
            bool full = t.Visible(0) == "> abc" && t.Visible(1) == "> de" && !t.Typing(1) && t.Idle;
            for (int i = 0; i < 10; i++) t.Push("> line " + i);
            t.Update(10000);
            bool cap = t.Count == TutorialTerminal.MaxLines && t.Visible(TutorialTerminal.MaxLines - 1) == "> line 9" && t.Visible(0) == "> line 6";
            bool oob = t.Visible(-1) == "" && t.Visible(99) == "" && !t.Typing(99);
            t.Clear();
            t.Push(null);
            t.Update(100);
            Check("TUT-27 terminal: lines type in one after another, last 4 kept, null-safe", start && partial && full && cap && oob && t.Visible(0) == "",
                "start=" + start + " partial=" + partial + " full=" + full + " cap=" + cap + " oob=" + oob);
        }
    }
}
