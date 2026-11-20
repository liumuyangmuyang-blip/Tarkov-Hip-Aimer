using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 script step 7 「前后 W / S」: pure part TutorialStep7Math (src/TutorialStep7.cs, compiled with
    // TUTSTEP7_PURE_ONLY). Self-registers through a module initializer, so Tests.cs Main needs no edit.
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V4Step7Init() { V4Step7Tests(); }

        static void V4Step7Tests()
        {
            // S7-1 diagonal rule equals Profile.RecomputeDiagonals for many random cardinals
            Random rnd = new Random(7);
            double worst = 0;
            for (int n = 0; n < 2000; n++)
            {
                Profile p = new Profile();
                for (int i = 0; i < 4; i++) { p.Dirs[i].OffX = rnd.Next(-500, 501); p.Dirs[i].OffY = rnd.Next(-500, 501); }
                Profile.RecomputeDiagonals(p);
                int[,] pair = { { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 } };
                for (int k = 0; k < 4; k++)
                {
                    double ex = TutorialStep7Math.Diag(p.Dirs[pair[k, 0]].OffX, p.Dirs[pair[k, 1]].OffX);
                    double ey = TutorialStep7Math.Diag(p.Dirs[pair[k, 0]].OffY, p.Dirs[pair[k, 1]].OffY);
                    worst = Math.Max(worst, Math.Max(Math.Abs(ex - p.Dirs[4 + k].OffX), Math.Abs(ey - p.Dirs[4 + k].OffY)));
                }
            }
            Check("S7-1 diagonals = Profile.RecomputeDiagonals (2000 profiles)", worst < 1e-9, "max diff=" + F(worst));

            // S7-2 scripted adjust: 0 before the first shot + delay, 1 after, monotone, W before S
            bool mono = true; double lw = -1, ls = -1;
            for (int t = 0; t < TutorialStep7Math.LoopMs; t += 5)
            {
                double aw, a2;
                TutorialStep7Math.ScriptAdjust(t, out aw, out a2);
                if (aw < lw - 1e-12 || a2 < ls - 1e-12 || aw < 0 || aw > 1 || a2 < 0 || a2 > 1) mono = false;
                lw = aw; ls = a2;
            }
            double w0, s0, w1, s1;
            TutorialStep7Math.ScriptAdjust(0, out w0, out s0);
            TutorialStep7Math.ScriptAdjust(TutorialStep7Math.SFireTo, out w1, out s1);
            Check("S7-2 scripted Y adjust 0 -> 1, monotone", mono && w0 == 0 && s0 == 0 && w1 == 1 && s1 == 1, "w1=" + F(w1) + " s1=" + F(s1));
            double wMid, sMid;
            TutorialStep7Math.ScriptAdjust(TutorialStep7Math.SFrom, out wMid, out sMid);
            Check("S7-3 W finished before S starts", wMid == 1 && sMid == 0, "w=" + F(wMid) + " s=" + F(sMid));
            Check("S7-4 adjust starts after first shots (fire window starts after the key)",
                TutorialStep7Math.WFireFrom > TutorialStep7Math.WFrom && TutorialStep7Math.SFireFrom > TutorialStep7Math.SFrom
                && TutorialStep7Math.WFireTo <= TutorialStep7Math.WTo && TutorialStep7Math.SFireTo <= TutorialStep7Math.STo
                && TutorialStep7Math.WFireFrom + TutorialStep7Math.AdjustDelayMs + TutorialStep7Math.AdjustMs < TutorialStep7Math.WFireTo
                && TutorialStep7Math.SFireFrom + TutorialStep7Math.AdjustDelayMs + TutorialStep7Math.AdjustMs < TutorialStep7Math.SFireTo
                && TutorialStep7Math.DiagTo <= TutorialStep7Math.LoopMs && TutorialStep7Math.STo < TutorialStep7Math.DiagFrom, "");

            // S7-5 phases and diagonal holds cover WA WD SA SD in order, each once
            int[] seen = new int[4]; int last = -1; bool order = true;
            for (int t = 0; t < TutorialStep7Math.LoopMs; t++)
            {
                int d = TutorialStep7Math.DiagHeld(t);
                if (d >= 0 && d != last) { seen[d]++; if (d < last) order = false; last = d; }
                if (d >= 0 && TutorialStep7Math.Phase(t) != 2) order = false;
            }
            Check("S7-5 diagonals held once each, in order, inside phase 2", order && seen[0] == 1 && seen[1] == 1 && seen[2] == 1 && seen[3] == 1,
                seen[0] + "," + seen[1] + "," + seen[2] + "," + seen[3]);
            Check("S7-6 phases", TutorialStep7Math.Phase(0) == 0 && TutorialStep7Math.Phase(TutorialStep7Math.WFireTo) == 0
                && TutorialStep7Math.Phase(TutorialStep7Math.SFireFrom) == 1 && TutorialStep7Math.Phase(TutorialStep7Math.DiagFrom) == 2, "");

            // S7-7 shot kinds: only a settled single W / S
            bool k1 = TutorialStep7Math.ShotKind(true, false, false, false, true) == TutorialStep7Math.KindW
                && TutorialStep7Math.ShotKind(false, true, false, false, true) == TutorialStep7Math.KindS
                && TutorialStep7Math.ShotKind(true, false, false, false, false) == TutorialStep7Math.KindOther
                && TutorialStep7Math.ShotKind(true, false, true, false, true) == TutorialStep7Math.KindOther
                && TutorialStep7Math.ShotKind(false, true, false, true, true) == TutorialStep7Math.KindOther
                && TutorialStep7Math.ShotKind(true, true, false, false, true) == TutorialStep7Math.KindOther
                && TutorialStep7Math.ShotKind(false, false, false, false, true) == TutorialStep7Math.KindOther;
            int combos = 0, wOrS = 0;
            for (int m = 0; m < 32; m++)
            {
                int kd = TutorialStep7Math.ShotKind((m & 1) != 0, (m & 2) != 0, (m & 4) != 0, (m & 8) != 0, (m & 16) != 0);
                combos++; if (kd != TutorialStep7Math.KindOther) wOrS++;
            }
            Check("S7-7 shot kinds (settled single W / S only)", k1 && combos == 32 && wOrS == 2, "counted combos=" + wOrS);

            // S7-8 prompt progress
            bool d0, d1, d2, d3;
            int p0 = TutorialStep7Math.PromptIndex(0, 0, out d0);
            int p1 = TutorialStep7Math.PromptIndex(TutorialStep7Math.ShotsNeeded, 0, out d1);
            int p2 = TutorialStep7Math.PromptIndex(TutorialStep7Math.ShotsNeeded, TutorialStep7Math.ShotsNeeded, out d2);
            int p3 = TutorialStep7Math.PromptIndex(0, 99, out d3);
            Check("S7-8 prompts W -> S -> done", p0 == 0 && !d0 && p1 == 1 && !d1 && p2 == 1 && d2 && p3 == 0 && !d3, "");

            // S7-9 caught / gap
            Check("S7-9 caught inside ring radius, min tolerance", TutorialStep7Math.Caught(5.9, 12) && !TutorialStep7Math.Caught(6.1, 12)
                && TutorialStep7Math.Caught(3.9, 1) && !TutorialStep7Math.Caught(4.1, 1) && !TutorialStep7Math.Caught(double.PositiveInfinity, 12)
                && Math.Abs(TutorialStep7Math.Gap(3, 4, 0, 0) - 5) < 1e-12 && double.IsPositiveInfinity(TutorialStep7Math.Gap(double.NaN, 0, 0, 0)), "");

            // S7-10 zoom fit: extents fit inside 'fill' of the half sizes for many sizes / DPI scales, never NaN
            bool zok = true; string zd = "";
            double[] ks = { 1, 1.25, 1.5, 1.75, 2 };
            for (int n = 0; n < 5000 && zok; n++)
            {
                double hw = rnd.Next(60, 1300), hh = rnd.Next(60, 800), k = ks[n % ks.Length];
                double ex = rnd.Next(0, 600), ey = rnd.Next(0, 600);
                double z = TutorialStep7Math.FitZoom(hw, hh, k, ex, ey, 34, 0.6);
                double rx = Math.Max(ex, 34) * z * k, ry = Math.Max(ey, 34) * z * k;
                if (double.IsNaN(z) || z < 0.05 || z > 40 || (z > 0.05 && (rx > hw * 0.6 + 1e-6 || ry > hh * 0.6 + 1e-6)))
                { zok = false; zd = "hw=" + hw + " hh=" + hh + " ex=" + ex + " ey=" + ey + " z=" + F(z); }
            }
            double zBad = TutorialStep7Math.FitZoom(double.NaN, 100, double.NaN, double.NaN, double.PositiveInfinity, 34, double.NaN);
            Check("S7-10 zoom fits extents (5000 cases) + bad input", zok && !double.IsNaN(zBad) && zBad >= 0.05, zd + " bad=" + F(zBad));
            Check("S7-11 MaxAbs ignores NaN / inf", TutorialStep7Math.MaxAbs(new double[] { -3, double.NaN, 2, double.NegativeInfinity }) == 3
                && TutorialStep7Math.MaxAbs(null) == 0, "");

            // S7-12 the reference (Leo默认) W / S: Y set, X 0 (X 不推荐改), so the demo shows a pure vertical shift
            Profile leo = Profile.CreateLeoDefault("x");
            Check("S7-12 Leo默认 W/S have Y offsets and X = 0", leo.Dirs[0].OffX == 0 && leo.Dirs[1].OffX == 0 && leo.Dirs[0].OffY != 0 && leo.Dirs[1].OffY != 0,
                "W=" + F(leo.Dirs[0].OffY) + " S=" + F(leo.Dirs[1].OffY));

            // S7-13 untested key shows 「—」: scripted before its slide, live before its first counted shot
            double tw, ts; TutorialStep7Math.ScriptAdjust(TutorialStep7Math.WFireTo, out tw, out ts);
            Check("S7-13 untested key -> —", !TutorialStep7Math.Tested(1, true, tw, ts, 0, 0, false) && TutorialStep7Math.Tested(0, true, tw, ts, 0, 0, false)
                && !TutorialStep7Math.Tested(1, false, 1, 1, 3, 0, false) && TutorialStep7Math.Tested(1, false, 1, 1, 3, 1, false)
                && TutorialStep7Math.Tested(1, false, 1, 1, 0, 0, true) && !TutorialStep7Math.Tested(0, true, 0, 0, 9, 9, true), "");
        }
    }
}
