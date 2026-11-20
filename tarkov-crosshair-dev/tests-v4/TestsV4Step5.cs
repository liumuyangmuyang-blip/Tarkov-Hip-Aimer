using System;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 step 5 (去靶场): pure timeline / geometry of TutorialStep5Math (src/TutorialStep5.cs, compiled with
    // TUTORIAL_MATH_ONLY). Runs from a module initializer so Tests.cs (not owned by step worker 5) needs no edit; the
    // results still count in Main's TOTAL (shared pass/fail counters).
    public static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V4Step5Init() { V4Step5Tests(); }

        static void V4Step5Tests()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            const double end = TutorialStep5Math.WalkEndMs;

            // S5-1 endpoints: starts at StartM, ends at exactly 10 m, stays there
            Check("S5-1 walk endpoints", TutorialStep5Math.DistanceM(0) == TutorialStep5Math.StartM
                && TutorialStep5Math.DistanceM(end) == 10.0 && TutorialStep5Math.DistanceM(end + 1e7) == 10.0
                && TutorialStep5Math.DistanceM(-50) == TutorialStep5Math.StartM && TutorialStep5Math.DistanceM(double.NaN) == TutorialStep5Math.StartM, "");

            // S5-2 distance never increases, stays in [10, StartM], continuous (no jump > 0.05 m per ms step)
            bool mono = true, range = true, cont = true; double prev = TutorialStep5Math.DistanceM(0);
            for (double t = 0; t <= end + 2000; t += 1)
            {
                double d = TutorialStep5Math.DistanceM(t);
                if (d > prev + 1e-12) mono = false;
                if (d < 10 - 1e-12 || d > TutorialStep5Math.StartM + 1e-12) range = false;
                if (Math.Abs(d - prev) > 0.05) cont = false;
                prev = d;
            }
            Check("S5-2 walk monotone / range / continuous", mono && range && cont, "mono=" + mono + " range=" + range + " cont=" + cont);

            // S5-3 checklist ticks in order, the last one exactly on arrival; Tick01 in 0..1
            bool order = TutorialStep5Math.TickMs[0] < TutorialStep5Math.TickMs[1] && TutorialStep5Math.TickMs[1] <= TutorialStep5Math.WalkStartMs
                && TutorialStep5Math.TickMs[2] == end;
            bool tick = !TutorialStep5Math.Ticked(2, end - 1) && TutorialStep5Math.Ticked(2, end) && TutorialStep5Math.Arrived(end)
                && !TutorialStep5Math.Arrived(end - 1) && !TutorialStep5Math.Ticked(-1, 1e9) && !TutorialStep5Math.Ticked(3, 1e9);
            bool t01 = true;
            for (int i = 0; i < 3; i++) for (double t = 0; t < end + 1000; t += 7) { double k = TutorialStep5Math.Tick01(i, t); if (k < 0 || k > 1 || double.IsNaN(k)) t01 = false; }
            Check("S5-3 checklist order + arrival tick", order && tick && t01, "order=" + order + " tick=" + tick + " t01=" + t01);

            // S5-4 footprints: count grows by at most 1 per ms, ends at (StartM - 10) / StrideM, every print between 10 m and StartM
            int last = 0; bool fpOk = true;
            for (double t = 0; t <= end + 500; t += 1)
            {
                int n = TutorialStep5Math.Footprints(t);
                if (n < last || n - last > 1) fpOk = false;
                last = n;
            }
            int expect = (int)Math.Floor((TutorialStep5Math.StartM - 10.0) / TutorialStep5Math.StrideM + 1e-9);
            for (int i = 0; i < last; i++) { double m = TutorialStep5Math.FootprintM(i); if (m < 10 - 1e-9 || m > TutorialStep5Math.StartM) fpOk = false; }
            Check("S5-4 footprints", fpOk && last == expect && last <= 200, "final=" + last + " expect=" + expect);

            // S5-5 bob 0 while standing, |bob| <= 1 while walking
            bool bob = TutorialStep5Math.Bob(0) == 0 && TutorialStep5Math.Bob(end) == 0 && TutorialStep5Math.Bob(end + 5000) == 0;
            for (double t = 0; t < end; t += 3) if (Math.Abs(TutorialStep5Math.Bob(t)) > 1 + 1e-12) bob = false;
            Check("S5-5 head bob", bob, "");

            // S5-6 map mapping: wall at top, MapM at bottom, linear, clamped, NaN-safe
            bool map = TutorialStep5Math.MapY(0, 100, 540) == 100 && TutorialStep5Math.MapY(TutorialStep5Math.MapM, 100, 540) == 540
                && Math.Abs(TutorialStep5Math.MapY(11, 100, 540) - 320) < 1e-9 && TutorialStep5Math.MapY(-3, 100, 540) == 100
                && TutorialStep5Math.MapY(999, 100, 540) == 540 && TutorialStep5Math.MapY(double.NaN, 100, 540) == 100
                && TutorialStep5Math.MapY(10, 100, 540) > TutorialStep5Math.MapY(5, 100, 540);
            Check("S5-6 map mapping", map, "");

            // S5-7 projection: halves at double distance, finite at 0 / negative
            double p10 = TutorialStep5Math.ProjectPx(2.4, 10, 1000), p20 = TutorialStep5Math.ProjectPx(2.4, 20, 1000);
            bool proj = Math.Abs(p10 - 240) < 1e-9 && Math.Abs(p10 - 2 * p20) < 1e-9
                && !double.IsInfinity(TutorialStep5Math.ProjectPx(2.4, 0, 1000)) && !double.IsNaN(TutorialStep5Math.ProjectPx(2.4, -1, 1000));
            Check("S5-7 first-person projection", proj, "p10=" + F(p10) + " p20=" + F(p20));

            // S5-8 label index: 10 m -> 100, clamped 0..max, NaN -> 0; every walk sample in range
            bool idx = TutorialStep5Math.TenthsIndex(10, 300) == 100 && TutorialStep5Math.TenthsIndex(12.34, 300) == 123
                && TutorialStep5Math.TenthsIndex(-1, 300) == 0 && TutorialStep5Math.TenthsIndex(double.NaN, 300) == 0
                && TutorialStep5Math.TenthsIndex(1e9, 300) == 300;
            for (double t = 0; t < end + 100; t += 5) { int i = TutorialStep5Math.TenthsIndex(TutorialStep5Math.DistanceM(t), 300); if (i < 100 || i > 200) idx = false; }
            Check("S5-8 distance label index", idx, "");

            // S5-9 pulse: none before arrival, 0..1 after, bad period -> none; boot 0..1
            bool pulse = TutorialStep5Math.Pulse01(end - 1, 1600) == -1 && TutorialStep5Math.Pulse01(end, 1600) == 0
                && TutorialStep5Math.Pulse01(end + 800, 1600) == 0.5 && TutorialStep5Math.Pulse01(end + 10, 0) == -1;
            bool boot = TutorialStep5Math.Boot01(0) == 0 && TutorialStep5Math.Boot01(TutorialStep5Math.BootMs) == 1
                && TutorialStep5Math.Boot01(double.NaN) == 0 && TutorialStep5Math.Boot01(1e9) == 1;
            Check("S5-9 pulse + boot", pulse && boot, "");
        }
    }
}
