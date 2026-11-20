using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 step 3 (后坐力上抬): pure maths of src/TutorialStep3.cs (compiled with STEP3_PURE_ONLY).
    public static partial class Program
    {
        private static bool step3Ran;

        // Self-registering so Tests.cs needs no edit; also callable from Main (runs once).
        [ModuleInitializer]
        internal static void Step3Init() { V4Step3Tests(); }

        public static void V4Step3Tests()
        {
            if (step3Ran) return;
            step3Ran = true;
            const double full = 24;

            Check("S3-1 first shot (rise 0) = 首发", TutorialStep3Math.HoleKind(0, full) == 0, "");
            Check("S3-2 half rise = 连续", TutorialStep3Math.HoleKind(12, full) == 1, "");
            Check("S3-3 just under half = 首发", TutorialStep3Math.HoleKind(11.99, full) == 0, "");
            Check("S3-4 cap 0 / NaN -> 首发", TutorialStep3Math.HoleKind(5, 0) == 0 && TutorialStep3Math.HoleKind(double.NaN, full) == 0
                && TutorialStep3Math.HoleKind(5, double.NaN) == 0, "");
            Check("S3-5 sustained at 85 %", TutorialStep3Math.Sustained(20.4, full) && !TutorialStep3Math.Sustained(20.3, full)
                && !TutorialStep3Math.Sustained(10, 0) && !TutorialStep3Math.Sustained(double.NaN, full), "");
            Check("S3-6 burst done needs shots AND sustained", TutorialStep3Math.BurstDone(8, true) && !TutorialStep3Math.BurstDone(7, true)
                && !TutorialStep3Math.BurstDone(100, false), "");

            double e = TutorialStep3Math.Extent(6.3, -7.65, 24, 40, 12, 10);
            Check("S3-7 extent covers ring rise + radius + margin", Math.Abs(e - (40 + 6 + 10)) < 1e-9, "e=" + e);
            double e2 = TutorialStep3Math.Extent(6.3, -7.65, 300, 0, 12, 10);
            Check("S3-8 extent covers risen impact", Math.Abs(e2 - (300 - 7.65 + 10)) < 1e-9, "e=" + e2);
            double e3 = TutorialStep3Math.Extent(double.NaN, double.PositiveInfinity, double.NaN, double.NegativeInfinity, double.NaN, 10);
            Check("S3-9 extent NaN/inf safe", !double.IsNaN(e3) && !double.IsInfinity(e3) && e3 >= 10, "e=" + e3);

            // zoom: fitted extent fills the requested share; clamps; NaN-safe
            double z = TutorialStep3Math.FitZoom(400, 1.5, 56, 30, 0.58);
            Check("S3-10 zoom fit", Math.Abs(z * 1.5 * 56 - 400 * 0.58) < 1e-6, "z=" + z);
            Check("S3-11 zoom min extent", Math.Abs(TutorialStep3Math.FitZoom(400, 1, 2, 30, 0.5) - 400 * 0.5 / 30) < 1e-9, "");
            Check("S3-12 zoom clamps", TutorialStep3Math.FitZoom(400, 1, 1e9, 30, 0.5) == 0.05 && TutorialStep3Math.FitZoom(1e9, 1, 1, 1, 1) == 40
                && TutorialStep3Math.FitZoom(0, 1, 10, 1, 1) == 0.05, "");
            double zn = TutorialStep3Math.FitZoom(400, double.NaN, double.NaN, 30, double.NaN);
            Check("S3-13 zoom NaN safe", zn >= 0.05 && zn <= 40, "z=" + zn);

            // brute: every user rise 0..1000 and every DPI scale -> the ring's full rise stays inside the scene half
            bool fitOk = true; string bad = "";
            double[] ks = { 1, 1.25, 1.5, 1.75, 2 };
            for (int rise = 0; rise <= 1000 && fitOk; rise++)
                foreach (double k in ks)
                    foreach (double half in new double[] { 150, 230, 400, 700 })
                    {
                        double ext = TutorialStep3Math.Extent(6.3, -7.65, 24, rise, 30, 10);
                        double zz = TutorialStep3Math.FitZoom(half, k, ext, 30, 0.58);
                        double topPx = (rise + 15) * zz * k;
                        if (zz > 0.05 + 1e-12 && topPx > half) { fitOk = false; bad = "rise=" + rise + " k=" + k + " half=" + half; break; }
                    }
            Check("S3-14 brute: ring rise 0..1000 fits the scene at 5 DPI scales", fitOk, bad);

            // plot mapping
            Check("S3-15 plot y bottom/top/clamp", TutorialStep3Math.PlotY(0, 10, 100, 50) == 150f && TutorialStep3Math.PlotY(10, 10, 100, 50) == 100f
                && TutorialStep3Math.PlotY(99, 10, 100, 50) == 100f && TutorialStep3Math.PlotY(-5, 10, 100, 50) == 150f
                && TutorialStep3Math.PlotY(double.NaN, 10, 100, 50) == 150f && TutorialStep3Math.PlotY(5, 0, 100, 50) == 100f, "");
            Check("S3-16 plot max", Math.Abs(TutorialStep3Math.PlotMax(24, 40) - 46) < 1e-9 && TutorialStep3Math.PlotMax(0, 0) == 10
                && TutorialStep3Math.PlotMax(double.NaN, 2) == 10, "");

            // ruler grow-in and smoothstep
            bool mono = true; double prev = -1;
            for (int ms = -100; ms <= 600; ms += 5) { double v = TutorialStep3Math.RulerIn(ms); if (v < prev - 1e-12 || v < 0 || v > 1) mono = false; prev = v; }
            Check("S3-17 ruler grow-in 0..1 monotonic", mono && TutorialStep3Math.RulerIn(0) == 0 && TutorialStep3Math.RulerIn(400) == 1
                && TutorialStep3Math.RulerIn(double.NaN) == 0, "");

            // script: fire segment inside the loop, long enough to reach the sustained rise (> 8 shots at 75 ms)
            Check("S3-18 demo script timing", TutorialStep3Math.FireFrom >= 0 && TutorialStep3Math.FireTo < TutorialStep3Math.LoopMs
                && (TutorialStep3Math.FireTo - TutorialStep3Math.FireFrom) / 75 >= TutorialStep3Math.ShotsForDone
                && TutorialStep3Math.LoopMs - TutorialStep3Math.FireTo >= 2500, "");
            Check("S3-19 plot history >= fire segment", TutorialStep3Math.PlotSamples * TutorialStep3Math.PlotPeriodMs
                >= TutorialStep3Math.FireTo - TutorialStep3Math.FireFrom, "");

            // compact rule (review B21/B22): scene px / layout scale below 640 x 330 logical -> core picture only
            Check("S3-20 compact: 1180x760 default scene is full, 1280x720@150 scene is compact",
                !TutorialStep3Math.Compact(712, 512, 1.0) && TutorialStep3Math.Compact(740, 405, 1.5)
                && TutorialStep3Math.Compact(700, 400, double.NaN) == false && TutorialStep3Math.Compact(double.NaN, 400, 1), "");
            Check("S3-21 compact boundary", !TutorialStep3Math.Compact(640, 330, 1) && TutorialStep3Math.Compact(639.9, 330, 1)
                && TutorialStep3Math.Compact(640, 329.9, 1) && !TutorialStep3Math.Compact(1280, 660, 2), "");
            Check("S3-22 plot fits <= 40 % x 38 %", TutorialStep3Math.PlotFits(240, 140, 712, 512) && !TutorialStep3Math.PlotFits(300, 140, 712, 512)
                && !TutorialStep3Math.PlotFits(240, 200, 712, 512) && !TutorialStep3Math.PlotFits(double.NaN, 1, 712, 512)
                && !TutorialStep3Math.PlotFits(10, 10, 0, 0), "");
        }
    }
}
