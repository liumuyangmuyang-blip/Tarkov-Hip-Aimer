using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 script step 6 「左右 A / D」: pure maths in src/TutorialStep6.cs (TutorialStep6Math).
    // Self-registers through a module initializer (Tests.cs Main needs no edit).
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V4Step6Init() { V4Step6Tests(); }

        static void V4Step6Tests()
        {
            // S6-1 script timeline: D phase then A phase, bursts inside the held key, adjust between the two bursts
            bool ok = TutorialStep6Math.DFrom < TutorialStep6Math.DFire1From && TutorialStep6Math.DFire1To < TutorialStep6Math.DAdjFrom
                && TutorialStep6Math.DAdjTo < TutorialStep6Math.DFire2From && TutorialStep6Math.DFire2To <= TutorialStep6Math.DTo
                && TutorialStep6Math.DTo < TutorialStep6Math.AFrom && TutorialStep6Math.AFrom < TutorialStep6Math.AFire1From
                && TutorialStep6Math.AFire1To < TutorialStep6Math.AAdjFrom && TutorialStep6Math.AAdjTo < TutorialStep6Math.AFire2From
                && TutorialStep6Math.AFire2To <= TutorialStep6Math.ATo && TutorialStep6Math.ATo < TutorialStep6Math.LoopMs;
            Check("S6-1 script order D -> A, adjust between bursts", ok, "");

            // S6-2 adjust progress: 0 during the first burst, 1 during the second, monotone, in 0..1
            bool mono = true; double pd = -1, pa = -1; string bad = "";
            for (int t = 0; t <= TutorialStep6Math.LoopMs; t += 5)
            {
                double d, a; TutorialStep6Math.ScriptAdjust(t, out d, out a);
                if (d < 0 || d > 1 || a < 0 || a > 1 || d < pd - 1e-12 || a < pa - 1e-12) { mono = false; bad = "t=" + t; break; }
                if (t >= TutorialStep6Math.DFire1From && t < TutorialStep6Math.DFire1To && d != 0) { mono = false; bad = "D burst1 t=" + t; break; }
                if (t >= TutorialStep6Math.DFire2From && t < TutorialStep6Math.DFire2To && d != 1) { mono = false; bad = "D burst2 t=" + t; break; }
                if (t >= TutorialStep6Math.AFire1From && t < TutorialStep6Math.AFire1To && a != 0) { mono = false; bad = "A burst1 t=" + t; break; }
                if (t >= TutorialStep6Math.AFire2From && t < TutorialStep6Math.AFire2To && a != 1) { mono = false; bad = "A burst2 t=" + t; break; }
                pd = d; pa = a;
            }
            double nd, na; TutorialStep6Math.ScriptAdjust(double.NaN, out nd, out na);
            Check("S6-2 adjust 0 -> 1, monotone, NaN = 0", mono && nd == 0 && na == 0, bad);

            // S6-3 script side
            ok = TutorialStep6Math.ScriptSide(0) == 0 && TutorialStep6Math.ScriptSide(TutorialStep6Math.DFrom) == 1
                && TutorialStep6Math.ScriptSide(TutorialStep6Math.DTo - 1) == 1 && TutorialStep6Math.ScriptSide(TutorialStep6Math.DTo) == 0
                && TutorialStep6Math.ScriptSide(TutorialStep6Math.AFrom) == -1 && TutorialStep6Math.ScriptSide(TutorialStep6Math.ATo) == 0
                && TutorialStep6Math.ScriptSide(double.NaN) == 0;
            Check("S6-3 script side D / A / pause", ok, "");

            // S6-4 shot side: brute force over all 2^4 keys x lean -1/0/1 against the rule "only D or only A, no lean"
            bool all = true; bad = "";
            for (int m = 0; m < 16; m++)
                for (int lean = -1; lean <= 1; lean++)
                {
                    bool w = (m & 1) != 0, s = (m & 2) != 0, a = (m & 4) != 0, d = (m & 8) != 0;
                    int want = (lean == 0 && !w && !s && d != a) ? (d ? 1 : -1) : 0;
                    int got = TutorialStep6Math.ShotSide(w, s, a, d, lean);
                    if (got != want) { all = false; bad = "mask=" + m + " lean=" + lean + " got=" + got; }
                }
            Check("S6-4 shot side = D alone / A alone only (48 cases)", all, bad);

            // S6-5 lock: ring radius tolerance, floor MinTolPx, NaN never locks
            ok = TutorialStep6Math.Locked(-30, -24, 12) && !TutorialStep6Math.Locked(-30, -23.9, 12)
                && TutorialStep6Math.Locked(10, 6, 1) && !TutorialStep6Math.Locked(10, 5.9, 1)
                && TutorialStep6Math.Locked(5, 4, double.NaN) && !TutorialStep6Math.Locked(double.NaN, 0, 12)
                && !TutorialStep6Math.Locked(0, double.PositiveInfinity, 12) && TutorialStep6Math.Tolerance(-5) == TutorialStep6Math.MinTolPx;
            Check("S6-5 lock tolerance = ring radius (>= 4 px)", ok, "");

            // S6-6 prompt progress
            bool dn;
            int p0 = TutorialStep6Math.PromptIndex(0, 0, out dn); bool d0 = dn;
            int p1 = TutorialStep6Math.PromptIndex(2, 5, out dn); bool d1 = dn;
            int p2 = TutorialStep6Math.PromptIndex(3, 0, out dn); bool d2 = dn;
            int p3 = TutorialStep6Math.PromptIndex(3, 3, out dn); bool d3 = dn;
            Check("S6-6 prompt D -> A, done after 3 + 3", p0 == 0 && !d0 && p1 == 0 && !d1 && p2 == 1 && !d2 && p3 == 1 && d3, "");

            // S6-7 extent / zoom: covers every value, clamped, robust to NaN / infinity / zero sizes
            Random r = new Random(6);
            all = true; bad = "";
            for (int i = 0; i < 20000; i++)
            {
                double a = (r.NextDouble() - 0.5) * 1000, b = (r.NextDouble() - 0.5) * 1000, c = (r.NextDouble() - 0.5) * 1000, d = (r.NextDouble() - 0.5) * 1000;
                double e = TutorialStep6Math.Extent(a, b, c, d, 18, 40);
                double mx = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
                if (e < 40 || e > 600 || (mx + 18 <= 600 && e < mx + 18 - 1e-9)) { all = false; bad = "extent " + e; break; }
                double half = 200 + r.NextDouble() * 1800, k = 0.75 + r.NextDouble() * 2;
                double z = TutorialStep6Math.FitZoom(half, k, e, 0.78);
                if (!(z >= 0.05 && z <= 12)) { all = false; bad = "zoom " + z; break; }
                if (z > 0.05 && z < 12 && Math.Abs(e * z * k - half * 0.78) > 1e-6) { all = false; bad = "fit " + (e * z * k) + " vs " + half * 0.78; break; }
            }
            ok = all && TutorialStep6Math.Extent(double.NaN, double.PositiveInfinity, 0, 0, 18, 40) == 40
                && TutorialStep6Math.FitZoom(0, 1, 50, 0.8) == 0.05 && TutorialStep6Math.FitZoom(500, double.NaN, double.NaN, double.NaN) <= 12;
            Check("S6-7 extent covers values, zoom fits and clamps (20k random)", ok, bad);

            // S6-8 ruler ticks: at most maxTicks over the full range, from the nice list
            all = true; bad = "";
            for (double e = 1; e <= 600; e += 0.5)
            {
                double st = TutorialStep6Math.TickStep(e, 16);
                if (2 * e / st > 16 + 1e-9 && st < 500) { all = false; bad = "e=" + e + " step=" + st; break; }
            }
            Check("S6-8 ruler tick step keeps <= 16 ticks", all && TutorialStep6Math.TickStep(0, 16) == 5 && TutorialStep6Math.TickStep(double.NaN, 16) == 5, bad);

            // S6-9 the demo reference values the step uses exist and lock their own holes (Leo默认 D / A)
            Profile leo = Profile.CreateLeoDefault("示意");
            double rdx = leo.Dirs[TutorialStep6Math.DirD].OffX, rax = leo.Dirs[TutorialStep6Math.DirA].OffX;
            Check("S6-9 reference D / A X (示意) differ in sign and lock at adj = 1", rdx < 0 && rax > 0
                && TutorialStep6Math.Locked(rdx, rdx * 1.0, leo.Diameter) && !TutorialStep6Math.Locked(rdx, rdx * 0.0, leo.Diameter),
                "D=" + rdx + " A=" + rax);

            // S6-10 engine: D held alone in the demo engine reaches the D offset; the ring follows a per-frame Dirs write
            GlobalSettings g = GlobalSettings.CreateDefault(); g.Validate();
            g.Enabled = true; g.OnlyWhenGameFocused = false; g.ShowFadeMs = 0; g.HideFadeMs = 0; g.HandEnabled = false;
            g.ResGameW = g.ResBaseW; g.ResGameH = g.ResBaseH;
            Profile pr = leo.CloneValues(); pr.RecoilMaxPx = 0; pr.RecoilClickKickPx = 0; pr.StandOffsetX = 0; pr.StandOffsetY = 0;
            MotionEngine en = new MotionEngine();
            InputState inp = new InputState(); inp.Right = true;
            for (int f = 0; f < 120; f++) en.Update(1 / 60.0, inp, pr, g, PreviewMode.Live, 1.0);
            double x1 = en.OffsetX;
            pr.Dirs[TutorialStep6Math.DirD].OffX = -10;
            for (int f = 0; f < 3; f++) en.Update(1 / 60.0, inp, pr, g, PreviewMode.Live, 1.0);
            double x2 = en.OffsetX;
            Check("S6-10 engine: hold D -> D X; editing D X moves the ring at once", Math.Abs(x1 - rdx) < 0.5 && Math.Abs(x2 + 10) < 0.5,
                "x1=" + x1 + " x2=" + x2);
        }
    }
}
