using System;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 script step 4 (移动的惯性): pure helpers + the demo truth profile (worker STEP4).
    // Needs TUTORIAL_MATH_ONLY defined (TutorialStep4.cs drawing page left out). Runs from a module initializer (counts in Main TOTAL), like steps 3/5/7.
    public static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V4Step4Init() { V4Step4Tests(); }

        static void V4Step4Tests()
        {
            const int W = TutorialStep4Math.W, S = TutorialStep4Math.S, A = TutorialStep4Math.A, D = TutorialStep4Math.D, FI = TutorialStep4Math.Fire;
            int[] sc = TutorialStep4Math.Script;

            // script: triples, inside the loop, every movement key held once with a burst fully inside its hold
            bool shape = sc.Length % 3 == 0;
            for (int i = 0; shape && i < sc.Length; i += 3) shape = sc[i] >= 0 && sc[i] < sc[i + 1] && sc[i + 1] <= TutorialStep4Math.LoopMs;
            Check("S4-1 script triples inside loop", shape, "len=" + sc.Length);
            int[] keys = { W, A, S, D };
            bool cover = true;
            foreach (int k in keys)
            {
                int holds = 0; bool burst = false;
                for (int i = 0; i < sc.Length; i += 3)
                {
                    if (sc[i + 2] != k) continue;
                    holds++;
                    for (int j = 0; j < sc.Length; j += 3)
                        if (sc[j + 2] == FI && sc[j] >= sc[i] + 300 && sc[j + 1] <= sc[i + 1]) burst = true;
                }
                if (holds != 1 || !burst) cover = false;
            }
            Check("S4-2 each of W A S D held once with a burst after 300 ms", cover, "");
            bool noOverlap = true;
            for (int i = 0; i < sc.Length; i += 3)
                for (int j = i + 3; j < sc.Length; j += 3)
                    if (sc[i + 2] != FI && sc[j + 2] != FI && sc[i] < sc[j + 1] && sc[j] < sc[i + 1]) noOverlap = false;
            Check("S4-3 movement holds never overlap (one direction at a time)", noOverlap, "");

            // masks
            Check("S4-4 Rising", TutorialStep4Math.Rising(W, W | D) == D && TutorialStep4Math.Rising(W | D, W) == 0, "");
            int t = 0;
            t = TutorialStep4Math.AddTested(t, W | FI);
            t = TutorialStep4Math.AddTested(t, A);
            Check("S4-5 AddTested keeps only movement", t == (W | A) && !TutorialStep4Math.AllTested(t) && TutorialStep4Math.CountMove(t) == 2, "t=" + t);
            t = TutorialStep4Math.AddTested(t, S | D);
            Check("S4-6 AllTested after W A S D", TutorialStep4Math.AllTested(t) && TutorialStep4Math.CountMove(t) == 4, "");
            Check("S4-7 AllTested ignores fire/other bits", !TutorialStep4Math.AllTested(FI | 16 | 32 | 128 | 256), "");
            Check("S4-8 FirstMoveIndex order W A S D",
                TutorialStep4Math.FirstMoveIndex(D | S) == 2 && TutorialStep4Math.FirstMoveIndex(D) == 3 && TutorialStep4Math.FirstMoveIndex(A | D) == 1
                && TutorialStep4Math.FirstMoveIndex(W | D) == 0 && TutorialStep4Math.FirstMoveIndex(FI) == -1, "");

            // hole kinds
            Check("S4-9 HoleKind single keys", TutorialStep4Math.HoleKind(true, false, false, false) == 1 && TutorialStep4Math.HoleKind(false, false, true, false) == 2
                && TutorialStep4Math.HoleKind(false, true, false, false) == 3 && TutorialStep4Math.HoleKind(false, false, false, true) == 4, "");
            Check("S4-10 HoleKind diagonal / none / opposite cancel", TutorialStep4Math.HoleKind(true, false, false, true) == 5
                && TutorialStep4Math.HoleKind(false, false, false, false) == 0 && TutorialStep4Math.HoleKind(true, true, false, false) == 0
                && TutorialStep4Math.HoleKind(true, true, true, false) == 2, "");
            bool kindRange = true;
            for (int m = 0; m < 16; m++)
            {
                int k = TutorialStep4Math.HoleKind((m & 1) != 0, (m & 2) != 0, (m & 4) != 0, (m & 8) != 0);
                if (k < 0 || k > 5) kindRange = false;
            }
            Check("S4-11 HoleKind always 0..5 (16 combos)", kindRange, "");

            // plot mapping
            Check("S4-12 PlotScale", TutorialStep4Math.PlotScale(0) == 20 && TutorialStep4Math.PlotScale(75) == 80 && TutorialStep4Math.PlotScale(80) == 80
                && TutorialStep4Math.PlotScale(81) == 100 && TutorialStep4Math.PlotScale(double.NaN) == 20 && TutorialStep4Math.PlotScale(1e9) == 2000, "");
            bool clamp = true;
            for (double v = -1000; v <= 1000; v += 7.3)
            {
                double r = TutorialStep4Math.PlotMap(v, 80, 50);
                if (Math.Abs(r) > 50 + 1e-9 || (Math.Abs(v) <= 80 && Math.Abs(r - v / 80 * 50) > 1e-9)) clamp = false;
            }
            Check("S4-13 PlotMap linear inside, clamped outside", clamp && TutorialStep4Math.PlotMap(double.NaN, 80, 50) == 0 && TutorialStep4Math.PlotMap(5, 0, 50) == 0, "");
            bool zoom = TutorialStep4Math.SceneZoom(486, 1) > 1.1 && TutorialStep4Math.SceneZoom(486, 1) < TutorialStep4Math.SceneZoom(846, 1)
                && TutorialStep4Math.SceneZoom(0, 1) == 1.8 && TutorialStep4Math.SceneZoom(100000, 1) == 3.2 && TutorialStep4Math.SceneZoom(10, 1) == 1.1;
            Check("S4-14 SceneZoom grows with the scene, clamped", zoom, "");
            for (int h = 300; h <= 3000; h += 100)
            {
                double wallPx = 180 * TutorialStep4Math.SceneZoom(h, 1.5) * 1.5;
                if (h >= 600 && h <= 1500 && Math.Abs(wallPx / h - 0.55) > 0.01) { zoom = false; break; }
            }
            Check("S4-15 10 m wall about 55 % of the scene height (K 1.5)", zoom, "");

            // review fixes: wall fits between the side columns, compact rule, key -> hole colour group
            bool fit = TutorialStep4Math.FitZoom(2.5, 1000, 1) == 2.5 && Math.Abs(TutorialStep4Math.FitZoom(2.5, 480, 1) - 2.0) < 1e-9
                && TutorialStep4Math.FitZoom(2.5, 10, 1) == 1.1 && TutorialStep4Math.FitZoom(9, 1e6, 1) == 3.2
                && TutorialStep4Math.FitZoom(double.NaN, 1000, 1) == 1.8 && Math.Abs(TutorialStep4Math.FitZoom(2.5, 720, 1.5) - 2.0) < 1e-9;
            Check("S4-21 FitZoom keeps the 240 px wall inside the free width, clamped 1.1..3.2", fit, "");
            Check("S4-22 IsCompact below 700 x 330 logical", TutorialStep4Math.IsCompact(1000, 450, 1.5) && !TutorialStep4Math.IsCompact(1050, 495, 1.5)
                && TutorialStep4Math.IsCompact(699, 900, 1) && TutorialStep4Math.IsCompact(900, 329, 1) && !TutorialStep4Math.IsCompact(700, 330, 0), "");
            Check("S4-23 KindOfKey W A S D = HoleKind of that key alone",
                TutorialStep4Math.KindOfKey(0) == TutorialStep4Math.HoleKind(true, false, false, false) && TutorialStep4Math.KindOfKey(1) == TutorialStep4Math.HoleKind(false, false, true, false)
                && TutorialStep4Math.KindOfKey(2) == TutorialStep4Math.HoleKind(false, true, false, false) && TutorialStep4Math.KindOfKey(3) == TutorialStep4Math.HoleKind(false, false, false, true)
                && TutorialStep4Math.KindOfKey(9) == 0, "");

            // trail ring buffer
            TutorialStep4Trail tr = new TutorialStep4Trail(5);
            tr.Clear();
            for (int i = 0; i < 12; i++) tr.Add(i, -i);
            Check("S4-16 Trail keeps the newest N in order", tr.Count == 5 && tr.X(0) == 7 && tr.X(4) == 11 && tr.Y(4) == -11, "x0=" + tr.X(0));
            tr = new TutorialStep4Trail(100);
            tr.Clear();
            for (int i = 0; i < 60; i++) tr.Update(1000.0 / 60, i, 0);
            Check("S4-17 Trail samples every 30 ms (1 s at 60 fps -> ~34 points)", tr.Count >= 30 && tr.Count <= 36, "n=" + tr.Count);
            tr.Update(double.NaN, double.NaN, 0); tr.Update(-5, 1, 1);
            Check("S4-18 Trail ignores NaN / negative dt", tr.Count >= 30 && tr.Count <= 37, "n=" + tr.Count);
            Check("S4-19 Trail capacity floor 2", new TutorialStep4Trail(0).Capacity == 2, "");

            // the demo truth (Leo's default, recoil off): every key moves the impact, in the profile's direction
            Profile p = Profile.CreateLeoDefault("示意");
            p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0; p.RecoilDriftX = 0;
            GlobalSettings g = GlobalSettings.CreateDefault(); g.Validate();
            g.Enabled = true; g.OnlyWhenGameFocused = false; g.ResGameW = g.ResBaseW; g.ResGameH = g.ResBaseH;
            g.ShowFadeMs = 0; g.HideFadeMs = 0; g.HandEnabled = false; g.BoostEnabled = false;
            string[] names = { "W", "S", "A", "D" };
            bool dirs = true; string why = "";
            for (int d = 0; d < 4; d++)
            {
                MotionEngine e = new MotionEngine();
                InputState i = new InputState();
                i.Fwd = d == 0; i.Back = d == 1; i.Left = d == 2; i.Right = d == 3;
                i.Fire = true;
                for (int f = 0; f < 90; f++) e.Update(1.0 / 60, i, p, g, PreviewMode.Live, 1.0);
                double x = e.OffsetX, y = -e.OffsetY, ex = p.Dirs[d].OffX, ey = p.Dirs[d].OffY;
                if (Math.Abs(x - ex) > 1.5 || Math.Abs(y - ey) > 1.5) { dirs = false; why += names[d] + " got " + F(x) + "," + F(y) + " want " + ex + "," + ey + "; "; }
                if (Math.Abs(x) + Math.Abs(y) < 5) { dirs = false; why += names[d] + " no visible offset; "; }
            }
            Check("S4-20 demo truth: W S A D each reach the profile offset with fire held, no recoil", dirs, why);
        }
    }
}
