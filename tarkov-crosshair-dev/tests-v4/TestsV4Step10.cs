using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 step 10 跳跃与拉枪 (src/TutorialStep10.cs, pure part TutorialStep10Math / TutorialStep10Flow,
    // compiled with STEP10_PURE_ONLY). Self-registers through a module initializer (Tests.cs Main needs no edit).
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V4Step10Init() { V4Step10Tests(); }

        static void V4Step10Tests()
        {
            Step10Reveal();
            Step10ClearTimes();
            Step10Scenes();
            Step10Shots();
            Step10Helpers();
            Step10FlowIdle();
            Step10FlowLive();
            Step10FlowFuzz();
        }

        static void Step10Reveal()
        {
            double c = TutorialStep10Math.RevealCamX();
            bool ok = c > 0 && c < TutorialStep10Math.MaxCamM
                && !TutorialStep10Math.TargetVisible(0) && !TutorialStep10Math.TargetVisible(c - 1e-6)
                && TutorialStep10Math.TargetVisible(c + 1e-6) && TutorialStep10Math.TargetVisible(TutorialStep10Math.MaxCamM)
                && TutorialStep10Math.RevealCamX(0.35, 1, 0.6, 1) == 0 && TutorialStep10Math.ScreenX(1, 0, 0, 100) == 0;
            Check("S10-1 peek reveal geometry", ok, "reveal camX=" + F(c));
            // the camera shift never decreases and stays capped
            double prev = -1; bool mono = true;
            for (int i = 0; i <= 4000; i++)
            {
                double x = TutorialStep10Math.CamX(true, true, i / 1000.0);
                if (x < prev - 1e-12 || x > TutorialStep10Math.MaxCamM + 1e-12) mono = false;
                prev = x;
            }
            Check("S10-2 camera shift monotone, capped", mono && TutorialStep10Math.CamX(true, true, double.NaN) == 0, "");
        }

        static void Step10ClearTimes()
        {
            double d = TutorialStep10Math.ClearTime(true, false), e = TutorialStep10Math.ClearTime(false, true), de = TutorialStep10Math.ClearTime(true, true);
            bool ok = de > 0 && !double.IsInfinity(de) && de < d && de < e;
            Check("S10-3 D+E clears the corner sooner than D or E alone (Leo)", ok, "D=" + F(d) + " E=" + F(e) + " D+E=" + F(de));
            // the scripted first shot happens after the reveal, inside the scene
            double first = TutorialStep10Math.KeysAt + de * 1000 + TutorialStep10Math.ReactMs;
            bool vis = TutorialStep10Math.TargetVisible(TutorialStep10Math.CamX(true, true, (first - TutorialStep10Math.KeysAt) / 1000.0));
            bool fits = first + TutorialStep10Math.ShotsPerPeek * TutorialStep10Math.ShotGapMs < TutorialStep10Math.PeekSceneMs - TutorialStep10Math.FadeMs;
            Check("S10-4 shots start after reveal and fit the scene", vis && fits, "first=" + F(first));
        }

        static void Step10Scenes()
        {
            bool ok = true; double l;
            for (int t = 0; t < TutorialStep10Math.PeekTotalMs; t += 7)
            {
                int s = TutorialStep10Math.PeekScene(t, out l);
                if (s != t / TutorialStep10Math.PeekSceneMs || l < 0 || l >= TutorialStep10Math.PeekSceneMs) ok = false;
                double f = TutorialStep10Math.SceneFade(l);
                if (f < 0 || f > 1) ok = false;
            }
            ok = ok && TutorialStep10Math.PeekScene(-5, out l) == 0 && TutorialStep10Math.PeekScene(double.NaN, out l) == 0
                && TutorialStep10Math.PeekScene(1e9, out l) == 2 && TutorialStep10Math.SceneFade(0) == 0
                && TutorialStep10Math.SceneFade(TutorialStep10Math.PeekSceneMs / 2.0) == 1;
            Check("S10-5 peek scenes / fade", ok, "");
        }

        static void Step10Shots()
        {
            int prev = 0; bool ok = true;
            for (int t = 0; t <= TutorialStep10Math.PeekSceneMs; t++)
            {
                int n = TutorialStep10Math.ShotsAt(t);
                if (n < prev || n > TutorialStep10Math.ShotsPerPeek) ok = false;
                double s = TutorialStep10Math.SinceShot(t);
                if (n == 0 && !double.IsInfinity(s)) ok = false;
                if (n > 0 && (s < 0 || s >= TutorialStep10Math.ShotGapMs + 1e-9) && n < TutorialStep10Math.ShotsPerPeek) ok = false;
                prev = n;
            }
            Check("S10-6 scripted shots monotone, capped", ok && prev == TutorialStep10Math.ShotsPerPeek, "total=" + prev);
        }

        static void Step10Helpers()
        {
            bool ok = TutorialStep10Math.AirHeight01(400, 800) == 1 && TutorialStep10Math.AirHeight01(0, 800) == 0
                && TutorialStep10Math.AirHeight01(800, 800) == 0 && TutorialStep10Math.AirHeight01(100, 0) == 0
                && TutorialStep10Math.AirHeight01(double.NaN, 800) == 0;
            ok = ok && TutorialStep10Math.JumpExtent(true, -300, 0, false, 900, 900, 24, 60) == 324
                && TutorialStep10Math.JumpExtent(false, -300, 0, false, 0, 0, 24, 60) == 60
                && TutorialStep10Math.JumpExtent(true, double.NaN, double.PositiveInfinity, true, -30, 10, 0, 1) == 30;
            ok = ok && Math.Abs(TutorialStep10Math.FitZoom(300, 1, 324, 0.74) - 300 * 0.74 / 324) < 1e-12
                && TutorialStep10Math.FitZoom(0, 1, 10, 1) == 0.05 && TutorialStep10Math.FitZoom(1e9, 1, 1e-9, 1) == 40;
            ok = ok && TutorialStep10Math.PlotY(0, 0, 10, 100, 50) == 150 && TutorialStep10Math.PlotY(10, 0, 10, 100, 50) == 100
                && TutorialStep10Math.PlotY(99, 0, 10, 100, 50) == 100 && TutorialStep10Math.PlotY(double.NaN, -10, 10, 100, 50) == 125
                && TutorialStep10Math.PlotY(5, 3, 3, 0, 10) >= 0;
            double wm = TutorialStep10Math.WallHeightM(200, 0.5, 10);
            ok = ok && Math.Abs(TutorialGeometry.ProjectedPx(wm, 10) * 0.5 / 2 - 200) < 1e-9 && TutorialStep10Math.WallHeightM(0, 1, 10) == 1;
            Check("S10-7 helpers (air, extent, zoom, plot, wall)", ok, "");
        }

        static void Run10(TutorialStep10Flow f, double ms, bool live, int shots, bool want)
        {
            for (double t = 0; t < ms; t += 16) f.Tick(16, live, shots, want);
        }

        static void Step10FlowIdle()
        {
            TutorialStep10Flow f = new TutorialStep10Flow(); f.Reset();
            Run10(f, TutorialStep10Flow.JumpIdleMs - 50, false, 0, false);
            bool a = f.Mode == TutorialStep10Flow.ModeJump && f.Prompt == 0 && !f.Done;
            Run10(f, 100, false, 0, false);
            bool b = f.Mode == TutorialStep10Flow.ModePeek && f.Prompt == 1 && !f.Done;
            int watched = 0;
            for (double t = 0; t < TutorialStep10Math.PeekTotalMs + 200; t += 16) { f.Tick(16, false, 0, false); if (f.JustWatched) watched++; }
            // v4.1: demo B loops (stays in B after the first pass)
            bool c = f.Mode == TutorialStep10Flow.ModePeek && f.Prompt == 2 && f.Done && watched == 1;
            // keeps cycling; prompt stays 2, JustWatched never again
            for (double t = 0; t < 60000; t += 16) { f.Tick(16, false, 0, false); if (f.JustWatched) watched++; if (f.Prompt != 2 || !f.Done) c = false; }
            Check("S10-8 flow without input: A -> B -> done, cycles", a && b && c && watched == 1, "");
        }

        static void Step10FlowLive()
        {
            TutorialStep10Flow f = new TutorialStep10Flow(); f.Reset();
            // user keeps playing in A: never forced to B
            for (double t = 0; t < 20000; t += 16) f.Tick(16, true, 0, true);
            bool a = f.Mode == TutorialStep10Flow.ModeJump && f.Prompt == 0;
            // two shots during a jump, then a pause -> B after AfterTaskMs
            f.Tick(16, true, 2, true);
            bool task = f.JumpTaskDone;
            Run10(f, TutorialStep10Flow.AfterTaskMs - 40, false, 0, false);
            bool still = f.Mode == TutorialStep10Flow.ModeJump;
            Run10(f, 80, false, 0, false);
            bool b = f.Mode == TutorialStep10Flow.ModePeek && f.Prompt == 1;
            // jump / fire during B -> back to A; prompt does not go back
            f.Tick(16, true, 0, true);
            bool back = f.Mode == TutorialStep10Flow.ModeJump && f.ModeChanged && f.Prompt == 1 && !f.Done;
            f.Select(TutorialStep10Flow.ModePeek);
            bool sel = f.Mode == TutorialStep10Flow.ModePeek && f.PeekMs == 0;
            f.Select(7);
            Check("S10-9 flow with live input", a && task && still && b && back && sel && f.Mode == TutorialStep10Flow.ModePeek, "");
        }

        static void Step10FlowFuzz()
        {
            TutorialStep10Flow f = new TutorialStep10Flow(); f.Reset();
            TutorialRng r = new TutorialRng(99);
            bool ok = true; int prevPrompt = 0;
            double[] dts = { 0, 16, 7, 33, 100, 250, -5, double.NaN, double.PositiveInfinity };
            for (int i = 0; i < 300000; i++)
            {
                double dt = dts[r.Next() % (uint)dts.Length];
                bool live = (r.Next() & 7) == 0;
                f.Tick(dt, live, live ? (int)(r.Next() % 3) : 0, live && (r.Next() & 1) == 0);
                if ((r.Next() & 1023) == 0) f.Select((int)(r.Next() % 3));
                if (f.Prompt < prevPrompt || f.Prompt < 0 || f.Prompt > 2 || double.IsNaN(f.ModeMs) || double.IsNaN(f.PeekMs)
                    || f.PeekMs < 0 || f.PeekMs > TutorialStep10Math.PeekTotalMs || (f.Mode != 0 && f.Mode != 1)) { ok = false; break; }
                prevPrompt = f.Prompt;
            }
            Check("S10-10 flow fuzz 300k ticks (NaN / inf / negative dt)", ok, "prompt=" + f.Prompt);
        }
    }
}
