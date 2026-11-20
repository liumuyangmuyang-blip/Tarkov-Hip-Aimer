using System;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 step 9 「换手」 (worker STEP9): pure timing / geometry of TutorialStep9Math.
    public static partial class Program
    {
        static void V4Step9Tests()
        {
            // ---- script timing ----
            Check("S9-1 loop order", TutorialStep9Math.D1From < TutorialStep9Math.Fire1From && TutorialStep9Math.Fire1To <= TutorialStep9Math.D1To
                && TutorialStep9Math.D1To < TutorialStep9Math.HandFrom && TutorialStep9Math.HandFrom < TutorialStep9Math.D2From
                && TutorialStep9Math.D2From < TutorialStep9Math.Fire2From && TutorialStep9Math.Fire2To < TutorialStep9Math.AdjustFrom
                && TutorialStep9Math.AdjustFrom + TutorialStep9Math.AdjustMs < TutorialStep9Math.Fire3From
                && TutorialStep9Math.Fire3To <= TutorialStep9Math.D2To && TutorialStep9Math.D2To < TutorialStep9Math.HandTo
                && TutorialStep9Math.HandTo < TutorialStep9Math.LoopMs, "segments in order");
            Check("S9-2 adjust 0 before / 1 after", TutorialStep9Math.ScriptAdjust(TutorialStep9Math.AdjustFrom - 1) == 0
                && TutorialStep9Math.ScriptAdjust(TutorialStep9Math.AdjustFrom + TutorialStep9Math.AdjustMs + 1) == 1, "");
            bool mono = true; double prev = -1;
            for (int t = 0; t < TutorialStep9Math.LoopMs; t += 5)
            {
                double a = TutorialStep9Math.ScriptAdjust(t);
                if (a < prev - 1e-12 || a < 0 || a > 1) mono = false;
                prev = a;
            }
            Check("S9-3 adjust monotonic in 0..1 over the loop", mono, "");
            Check("S9-4 adjust wraps (loop + t == t)", Math.Abs(TutorialStep9Math.ScriptAdjust(TutorialStep9Math.LoopMs + 6200) - TutorialStep9Math.ScriptAdjust(6200)) < 1e-12, "");
            Check("S9-5 NaN / negative / infinity time safe", TutorialStep9Math.ScriptAdjust(double.NaN) == 0 && TutorialStep9Math.ScriptAdjust(-50) == 0
                && TutorialStep9Math.ScriptAdjust(double.PositiveInfinity) == 0 && TutorialStep9Math.ScriptPhase(double.NaN) == 0, "");
            Check("S9-6 phases", TutorialStep9Math.ScriptPhase(1000) == 0 && TutorialStep9Math.ScriptPhase(4800) == 1
                && TutorialStep9Math.ScriptPhase(TutorialStep9Math.AdjustFrom + 10) == 2 && TutorialStep9Math.ScriptPhase(8000) == 3
                && TutorialStep9Math.ScriptPhase(TutorialStep9Math.HandTo + 10) == 0, "");

            // ---- demo differences (示意): D/A only X, W/S only Y, Q/E both, all non-zero ----
            double dx, dy;
            bool axes = true;
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemD, out dx, out dy); axes &= dx != 0 && dy == 0;
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemA, out dx, out dy); axes &= dx != 0 && dy == 0;
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemW, out dx, out dy); axes &= dx == 0 && dy != 0;
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemS, out dx, out dy); axes &= dx == 0 && dy != 0;
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemQ, out dx, out dy); axes &= dx != 0 && dy != 0;
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemE, out dx, out dy); axes &= dx != 0 && dy != 0;
            TutorialStep9Math.DemoDelta(7, out dx, out dy); axes &= dx == 0 && dy == 0;
            Check("S9-7 demo differences on the axes the earlier steps tune", axes, "");
            bool visible = true;
            foreach (int it in TutorialStep9Math.Order)
            {
                TutorialStep9Math.DemoDelta(it, out dx, out dy);
                if (TutorialStep9Math.Matched(Math.Sqrt(dx * dx + dy * dy), 12)) visible = false; // Leo's ring 12 px
            }
            Check("S9-8 every demo difference is visible (not 对上) with the 12 px ring", visible, "");

            // ---- truth: auto + delta, or the user's own value once typed ----
            double tx, ty;
            TutorialStep9Math.TruthValue(TutorialStep9Math.ItemD, false, -75, 3, -75, 3, out tx, out ty);
            TutorialStep9Math.DemoDelta(TutorialStep9Math.ItemD, out dx, out dy);
            Check("S9-9 auto item: truth = mirror + 示意 difference", tx == -75 + dx && ty == 3 + dy, tx + "," + ty);
            TutorialStep9Math.TruthValue(TutorialStep9Math.ItemD, true, -60, 1, -75, 3, out tx, out ty);
            Check("S9-10 custom item: truth = the user's value (gap 0)", tx == -60 && ty == 1, tx + "," + ty);

            // ---- pose -> item ----
            Check("S9-11 W S A D items", TutorialStep9Math.ItemOf(0, 0) == 0 && TutorialStep9Math.ItemOf(1, 0) == 1
                && TutorialStep9Math.ItemOf(2, 0) == 2 && TutorialStep9Math.ItemOf(3, 0) == 3, "");
            Check("S9-12 lean items only standing", TutorialStep9Math.ItemOf(-1, -1) == TutorialStep9Math.ItemQ
                && TutorialStep9Math.ItemOf(-1, 1) == TutorialStep9Math.ItemE && TutorialStep9Math.ItemOf(3, 1) == -1 && TutorialStep9Math.ItemOf(0, -1) == -1, "");
            bool diag = true;
            for (int d = 4; d < 8; d++) if (TutorialStep9Math.ItemOf(d, 0) != -1) diag = false;
            Check("S9-13 diagonals / nothing held test nothing", diag && TutorialStep9Math.ItemOf(-1, 0) == -1 && TutorialStep9Math.ItemOf(99, 0) == -1, "");

            // ---- settled ----
            Check("S9-14 settled", TutorialStep9Math.Settled(1, 1, 0, 0, false, false) && TutorialStep9Math.Settled(0, 0, -0.97, -1, false, false), "");
            Check("S9-15 not settled while switching / leaning / travelling / NaN",
                !TutorialStep9Math.Settled(0.5, 1, 0, 0, false, false) && !TutorialStep9Math.Settled(1, 1, 0.5, 1, false, false)
                && !TutorialStep9Math.Settled(1, 1, 0, 0, true, false) && !TutorialStep9Math.Settled(1, 1, 0, 0, false, true)
                && !TutorialStep9Math.Settled(double.NaN, 1, 0, 0, false, false) && !TutorialStep9Math.Settled(1, 1, double.NaN, 0, false, false), "");

            // ---- progress ----
            bool done;
            int pi = TutorialStep9Math.PromptIndex(false, TutorialStep9Math.AllMask, out done);
            Check("S9-16 prompt 0 until switched (even if all marked)", pi == 0 && !done, "");
            int mask = 0, steps = 0; bool early = false;
            foreach (int it in TutorialStep9Math.Order)
            {
                TutorialStep9Math.PromptIndex(true, mask, out done);
                if (done) early = true;
                mask |= TutorialStep9Math.Bit(it); steps++;
            }
            pi = TutorialStep9Math.PromptIndex(true, mask, out done);
            Check("S9-17 done only after all six", !early && done && pi == 1 && steps == 6 && TutorialStep9Math.CountTested(mask) == 6, "");
            Check("S9-18 stray bits do not count", TutorialStep9Math.CountTested(TutorialStep9Math.Bit(4) | TutorialStep9Math.Bit(7)) == 0
                && TutorialStep9Math.Bit(-1) == 0 && TutorialStep9Math.Bit(40) == 0, "");
            Check("S9-19 order D A W S Q E", TutorialStep9Math.OrderNames.Length == 6 && TutorialStep9Math.Order.Length == 6
                && TutorialStep9Math.Order[0] == 3 && TutorialStep9Math.Order[1] == 2 && TutorialStep9Math.Order[2] == 0
                && TutorialStep9Math.Order[3] == 1 && TutorialStep9Math.Order[4] == 5 && TutorialStep9Math.Order[5] == 6, "");

            // ---- gap / match / zoom / map ----
            Check("S9-20 gap", Math.Abs(TutorialStep9Math.Gap(3, 4, 0, 0) - 5) < 1e-12 && double.IsPositiveInfinity(TutorialStep9Math.Gap(double.NaN, 0, 0, 0)), "");
            Check("S9-21 match tolerance = ring radius, min 4 px", TutorialStep9Math.Matched(6, 12) && !TutorialStep9Math.Matched(6.1, 12)
                && TutorialStep9Math.Matched(4, 2) && !TutorialStep9Math.Matched(4.1, 2) && TutorialStep9Math.Matched(4, double.NaN)
                && !TutorialStep9Math.Matched(double.PositiveInfinity, 12), "");
            bool zoomOk = true;
            double[] halves = { 0, 100, 360, 540, 1080 }, ks = { 1, 1.25, 1.5, 2 }, exts = { 0, 20, 91, 500, double.NaN, double.PositiveInfinity };
            foreach (double h in halves) foreach (double k in ks) foreach (double e in exts)
                    {
                        double z = TutorialStep9Math.FitZoom(h, k, e, 40, 0.6);
                        if (double.IsNaN(z) || z < 0.05 || z > 40) zoomOk = false;
                        if (h > 0 && !double.IsNaN(e) && !double.IsInfinity(e) && Math.Max(Math.Abs(e), 40) * z * k > h * 0.6 + 1e-9 && z > 0.05) zoomOk = false;
                    }
            Check("S9-22 zoom bounded and the extent fits the scene", zoomOk, "");
            Check("S9-23 map scale", Math.Abs(TutorialStep9Math.MapScale(60, 75, 10) * 75 - 60) < 1e-9
                && Math.Abs(TutorialStep9Math.MapScale(60, 2, 10) - 6) < 1e-9 && TutorialStep9Math.MapScale(60, double.NaN, 10) == 6
                && TutorialStep9Math.MapScale(0, 75, 10) == 0, "");

            // ---- brute: every loop time at 1 ms, phases and adjust consistent ----
            bool brute = true;
            for (int t = 0; t < 3 * TutorialStep9Math.LoopMs; t++)
            {
                int ph = TutorialStep9Math.ScriptPhase(t);
                double a = TutorialStep9Math.ScriptAdjust(t);
                if (ph == 0 && TutorialStep9Math.Wrap(t) < TutorialStep9Math.HandFrom && a != 0) brute = false;
                if (ph == 1 && a != 0) brute = false;
                if (ph == 3 && a != 1) brute = false;
                if (ph < 0 || ph > 3) brute = false;
            }
            Check("S9-24 brute 31.8k loop times: phase vs adjust", brute, "");
        }
    }
}
