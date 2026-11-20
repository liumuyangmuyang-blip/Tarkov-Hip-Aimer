using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.0 首次设置教程 step 8 侧身 (src/TutorialStepsB.cs, pure part LeanStepMath, compiled with TUTB_PURE_ONLY).
    // Self-registers through a module initializer (Tests.cs Main needs no edit). Do NOT also call it from Main.
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V4TutBInit() { V4TutBTests(); }

        static void V4TutBTests()
        {
            TutBProgress();
            TutBScriptTimes();
            TutBSide();
            TutBZoom();
            TutBGap();
            TutBPrompt();
            TutBEngine();
            TutBDemoShowsMissThenMatch();
            TutBNoAlloc();
            TutBRoll();
            TutBLayout();
        }

        static void TutBRoll()
        {
            // Leo (16 Nov): Q (左侧身) = the scene rotates COUNTER-clockwise on screen. FpRig: Roll = Lean * RollPerLean,
            // Lean = -1 for Q; BeginRoll rotates by Roll (GDI+, y-down, + = clockwise). RollPoint must match that rotation.
            double rollQ = -1 * LeanStepMath.RollPerLean(), rollE = 1 * LeanStepMath.RollPerLean();
            double x, y;
            LeanStepMath.RollPoint(100, 0, rollQ, out x, out y);   // a point right of the centre ...
            bool qCcw = y < 0;                                     // ... goes UP on screen = counter-clockwise
            LeanStepMath.RollPoint(100, 0, rollE, out x, out y);
            bool eCw = y > 0;
            bool sign = LeanStepMath.LeanRollSign == 1.0 && Math.Abs(LeanStepMath.RollPerLean() - 6.0 * Math.PI / 180.0) < 1e-12;
            // RollPoint == a GDI+ style rotation matrix (clockwise for + in y-down) and keeps lengths, 100k random points
            Random r = new Random(83);
            string bad = "";
            for (int i = 0; i < 100000 && bad == ""; i++)
            {
                double dx = (r.NextDouble() - 0.5) * 2000, dy = (r.NextDouble() - 0.5) * 2000, a = (r.NextDouble() - 0.5) * 0.6;
                LeanStepMath.RollPoint(dx, dy, a, out x, out y);
                double ex = dx * Math.Cos(a) - dy * Math.Sin(a), ey = dx * Math.Sin(a) + dy * Math.Cos(a);
                if (Math.Abs(x - ex) > 1e-9 || Math.Abs(y - ey) > 1e-9 || Math.Abs(Math.Sqrt(x * x + y * y) - Math.Sqrt(dx * dx + dy * dy)) > 1e-6) bad = dx + "," + dy + "," + a;
            }
            Check("TB10 lean roll: LeanRollSign +1 keeps Q = scene counter-clockwise, E = clockwise (Leo, 16 Nov); RollPoint = BeginRoll rotation (100k)",
                qCcw && eCw && sign && bad == "", bad);
        }

        static void TutBLayout()
        {
            bool compact = LeanStepMath.Compact(683, 240, 1) && LeanStepMath.Compact(1000, 300, 1) && !LeanStepMath.Compact(700, 330, 1)
                && !LeanStepMath.Compact(1400, 660, 2) && LeanStepMath.Compact(1300, 660, 2) && !LeanStepMath.Compact(800, 400, double.NaN);
            // AvoidBelow: overlapping boxes end exactly 'gap' above the block; boxes beside / above it are unchanged
            bool avoid = LeanStepMath.AvoidBelow(0, 100, 50, 20, 40, 200, 110, 5) == 85
                && LeanStepMath.AvoidBelow(0, 100, 30, 20, 40, 200, 110, 5) == 100      // left of the block
                && LeanStepMath.AvoidBelow(250, 100, 30, 20, 40, 200, 110, 5) == 100    // right of the block
                && LeanStepMath.AvoidBelow(50, 10, 30, 20, 40, 200, 110, 5) == 10;      // already above
            Random r = new Random(84);
            string bad = "";
            for (int i = 0; i < 200000 && bad == ""; i++)
            {
                double l = r.NextDouble() * 1000, t = r.NextDouble() * 800, w = 1 + r.NextDouble() * 300, h = 1 + r.NextDouble() * 80;
                double bl = r.NextDouble() * 1000, br = bl + r.NextDouble() * 500, bt = r.NextDouble() * 800, gap = r.NextDouble() * 10;
                double nt = LeanStepMath.AvoidBelow(l, t, w, h, bl, br, bt, gap);
                bool overlapX = l + w > bl && l < br;
                if (nt > t) bad = "moved down";
                else if (overlapX && nt + h > bt - gap + 1e-9) bad = "still overlaps";
                else if (!overlapX && nt != t) bad = "moved without overlap";
            }
            Check("TB11 layout: compact scene rule (700x330 logical), label-vs-gun AvoidBelow (200k)", compact && avoid && bad == "", bad);
        }

        static void TutBProgress()
        {
            bool ok = LeanStepMath.Progress(0, 100, 200) == 0 && LeanStepMath.Progress(100, 100, 200) == 0
                && LeanStepMath.Progress(300, 100, 200) == 1 && LeanStepMath.Progress(1e9, 100, 200) == 1
                && Math.Abs(LeanStepMath.Progress(200, 100, 200) - 0.5) < 1e-12
                && LeanStepMath.Progress(99, 100, 0) == 0 && LeanStepMath.Progress(100, 100, 0) == 1
                && LeanStepMath.Progress(double.NaN, 0, 100) == 0 && LeanStepMath.Progress(5, 0, double.NaN) == 1;
            Random r = new Random(81);
            string bad = "";
            for (int i = 0; i < 200000 && bad == ""; i++)
            {
                double s = r.NextDouble() * 5000, d = r.NextDouble() * 3000, t = r.NextDouble() * 9000, t2 = t + r.NextDouble() * 500;
                double a = LeanStepMath.Progress(t, s, d), b = LeanStepMath.Progress(t2, s, d);
                if (!(a >= 0 && a <= 1 && b >= a - 1e-12)) bad = t + "/" + s + "/" + d;
            }
            Check("TB1 Progress 0..1, monotonic, NaN / 0 duration safe (200k)", ok && bad == "", bad);
        }

        static void TutBScriptTimes()
        {
            // the scripted story must fit inside its segments: fire starts after the lean starts, the ring finishes sliding
            // before the fire stops, and the two sides do not overlap inside the loop.
            bool ok = LeanStepMath.QFrom < LeanStepMath.QFireFrom && LeanStepMath.QFireTo <= LeanStepMath.QTo
                && LeanStepMath.QTo < LeanStepMath.EFrom && LeanStepMath.EFrom < LeanStepMath.EFireFrom
                && LeanStepMath.EFireTo <= LeanStepMath.ETo && LeanStepMath.ETo < LeanStepMath.LoopMs
                && LeanStepMath.QFireFrom + LeanStepMath.AdjustDelayMs + LeanStepMath.AdjustMs < LeanStepMath.QFireTo - 300
                && LeanStepMath.EFireFrom + LeanStepMath.AdjustDelayMs + LeanStepMath.AdjustMs < LeanStepMath.EFireTo - 300;
            double q, e;
            LeanStepMath.ScriptAdjust(LeanStepMath.QFireFrom + 100, out q, out e);
            bool early = q == 0 && e == 0;
            LeanStepMath.ScriptAdjust(LeanStepMath.QFireTo - 1, out q, out e);
            bool qDone = q == 1 && e == 0;
            LeanStepMath.ScriptAdjust(LeanStepMath.EFireTo - 1, out q, out e);
            bool both = q == 1 && e == 1;
            LeanStepMath.ScriptAdjust(0, out q, out e);
            bool reset = q == 0 && e == 0;
            Check("TB2 script segments / ring slide fit the loop; adjust 0 -> 1 per side, 0 at loop start", ok && early && qDone && both && reset, "");
        }

        static void TutBSide()
        {
            bool ok = LeanStepMath.SideOf(-1) == -1 && LeanStepMath.SideOf(-0.5) == -1 && LeanStepMath.SideOf(-0.49) == 0
                && LeanStepMath.SideOf(0) == 0 && LeanStepMath.SideOf(0.49) == 0 && LeanStepMath.SideOf(0.5) == 1
                && LeanStepMath.SideOf(1) == 1 && LeanStepMath.SideOf(double.NaN) == 0;
            Check("TB3 SideOf thresholds, NaN = 0", ok, "");
        }

        static void TutBZoom()
        {
            Random r = new Random(82);
            string bad = "";
            for (int i = 0; i < 200000 && bad == ""; i++)
            {
                double half = r.NextDouble() * 1500 + 20, k = 0.5 + r.NextDouble() * 2.5, ext = (r.NextDouble() - 0.5) * 2000;
                double min = 1 + r.NextDouble() * 80, fill = 0.2 + r.NextDouble() * 0.8;
                double z = LeanStepMath.FitZoom(half, k, ext, min, fill);
                double e = Math.Max(Math.Abs(ext), min);
                bool clamped = z == 0.05 || z == 40;
                // the extent drawn (UI px * zoom * k) fills exactly 'fill' of the half size unless the zoom is clamped
                if (!(z >= 0.05 && z <= 40)) bad = "range " + z;
                else if (!clamped && Math.Abs(e * z * k - half * fill) > 1e-6 * half) bad = "fit " + half + "/" + k + "/" + ext;
                else if (z == 40 && e * z * k > half * fill + 1e-6) bad = "max clamp overflow";
            }
            bool nan = LeanStepMath.FitZoom(300, 1, double.NaN, 30, 0.6) == LeanStepMath.FitZoom(300, 1, 0, 30, 0.6)
                && LeanStepMath.FitZoom(300, 1, double.PositiveInfinity, 30, 0.6) > 0
                && LeanStepMath.FitZoom(0, 1, 50, 30, 0.6) == 0.05 && LeanStepMath.FitZoom(300, 0, 50, 30, 0.6) == LeanStepMath.FitZoom(300, 1, 50, 30, 0.6)
                && LeanStepMath.FitZoom(300, 1, 0, 0, 0.6) > 0;
            Check("TB4 FitZoom: extent fills the scene part exactly, clamped 0.05..40, NaN / inf / 0 safe (200k)", bad == "" && nan, bad);
        }

        static void TutBGap()
        {
            bool ok = LeanStepMath.Gap(3, 4, 0, 0) == 5 && LeanStepMath.Gap(-5, -22, -5, -22) == 0
                && double.IsPositiveInfinity(LeanStepMath.Gap(double.NaN, 0, 0, 0))
                && LeanStepMath.Explained(6, 12) && !LeanStepMath.Explained(6.01, 12)
                && LeanStepMath.Explained(4, 2) && !LeanStepMath.Explained(4.01, 2)       // tiny ring: at least MinTolPx
                && LeanStepMath.Explained(4, double.NaN) && !LeanStepMath.Explained(double.PositiveInfinity, 50);
            Check("TB5 Gap / Explained (ring radius, min 4 px, NaN safe)", ok, "");
        }

        static void TutBPrompt()
        {
            bool done;
            bool ok = LeanStepMath.PromptIndex(0, 0, out done) == 0 && !done
                && LeanStepMath.PromptIndex(2, 9, out done) == 0 && !done
                && LeanStepMath.PromptIndex(3, 0, out done) == 1 && !done
                && LeanStepMath.PromptIndex(3, 2, out done) == 1 && !done
                && LeanStepMath.PromptIndex(3, 3, out done) == 1 && done
                && LeanStepMath.PromptIndex(50, 50, out done) == 1 && done;
            Check("TB6 live prompts: Q first, then E, done after 3 shots each", ok, "");
        }

        static TutorialScript TutBScript()
        {
            return new TutorialScript(LeanStepMath.LoopMs,
                new ScriptSeg(LeanStepMath.QFrom, LeanStepMath.QTo, TKey.Q),
                new ScriptSeg(LeanStepMath.QFireFrom, LeanStepMath.QFireTo, TKey.Fire),
                new ScriptSeg(LeanStepMath.EFrom, LeanStepMath.ETo, TKey.E),
                new ScriptSeg(LeanStepMath.EFireFrom, LeanStepMath.EFireTo, TKey.Fire));
        }

        static void TutBEngine()
        {
            // The step's script drives a real MotionEngine (hold and toggle 侧身, several 侧身时间): every scripted shot of
            // the Q part is fired while leaned left, every E shot while leaned right, no shot unleaned (so the hole
            // centres the step shows are clean), and toggle mode never drifts over 4 loops.
            TutorialScript sc = TutBScript();
            string bad = "";
            string[] modes = { "hold", "toggle" };
            double[] leanMs = { 0, 150, 280, 600 };
            int[] fpsList = { 30, 60, 144 };
            foreach (string mode in modes)
                foreach (double lm in leanMs)
                    foreach (int fps in fpsList)
                    {
                        Profile p = Profile.CreateLeoDefault("t"); p.LeanMs = lm; p.RecoilMaxPx = 0;
                        GlobalSettings g = new GlobalSettings(); g.LeanMode = mode; g.OnlyWhenGameFocused = false; g.Enabled = true;
                        MotionEngine e = new MotionEngine();
                        double dt = 1.0 / fps, t = 0;
                        int qShots = 0, eShots = 0, wrong = 0;
                        int frames = (int)(4 * LeanStepMath.LoopMs / 1000.0 * fps);
                        for (int f = 0; f < frames; f++)
                        {
                            bool fire;
                            InputState i = sc.Sample(t, mode == "toggle", true, out fire);
                            e.Update(dt, i, p, g, PreviewMode.Live, 1.0);
                            double w = sc.Wrap(t);
                            // the step's hole kind is decided from the lean target; the counted side from the eased lean
                            if (fire)
                            {
                                bool inQ = w >= LeanStepMath.QFireFrom && w < LeanStepMath.QFireTo;
                                int target = e.LeanTarget;
                                if (inQ) { if (target != -1) wrong++; else qShots++; }
                                else { if (target != 1) wrong++; else eShots++; }
                                // after 300 ms of firing the eased lean is past the side threshold (for 侧身时间 <= 600 ms)
                                double since = inQ ? w - LeanStepMath.QFireFrom : w - LeanStepMath.EFireFrom;
                                if (since > 300 && lm <= 280 && LeanStepMath.SideOf(e.Lean) != (inQ ? -1 : 1)) wrong++;
                            }
                            if (w > LeanStepMath.QTo + 150 && w < LeanStepMath.EFrom && e.LeanTarget != 0) wrong++;
                            if (w > LeanStepMath.ETo + 150 && e.LeanTarget != 0) wrong++;
                            t += dt * 1000;
                        }
                        if (wrong > 0 || qShots == 0 || eShots == 0) { bad = mode + " leanMs " + lm + " fps " + fps + " wrong " + wrong + " q " + qShots + " e " + eShots; break; }
                    }
            Check("TB7 step-8 script x MotionEngine: shots leaned to the right side, no toggle drift (hold/toggle x 4 lean times x 3 fps)", bad == "", bad);
        }

        static void TutBDemoShowsMissThenMatch()
        {
            // Story check with the shipped defaults: before the slide the ring (no lean offset) misses the reference holes,
            // after it the ring sits on them (the user's values = Leo's reference values by default).
            Profile p = Profile.CreateLeoDefault("t");
            double before = LeanStepMath.Gap(p.LeanQX, p.LeanQY, 0, 0), after = LeanStepMath.Gap(p.LeanQX, p.LeanQY, p.LeanQX, p.LeanQY);
            double beforeE = LeanStepMath.Gap(p.LeanEX, p.LeanEY, 0, 0);
            bool ok = !LeanStepMath.Explained(before, p.Diameter) && !LeanStepMath.Explained(beforeE, p.Diameter) && LeanStepMath.Explained(after, p.Diameter);
            Check("TB8 demo story with defaults: miss before the slide, match after (Q and E)", ok,
                "before Q " + before.ToString("0.0") + " E " + beforeE.ToString("0.0") + " ring " + p.Diameter);
        }

        static void TutBNoAlloc()
        {
            // warm up, then measure: the per-frame maths must not allocate
            double sink = 0; bool done;
            for (int i = 0; i < 1000; i++) sink += LeanStepMath.FitZoom(300, 1.5, i, 30, 0.6);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000000; i++)
            {
                double q, e;
                LeanStepMath.ScriptAdjust(i % LeanStepMath.LoopMs, out q, out e);
                sink += q + e + LeanStepMath.FitZoom(300, 1.5, i % 400, 30, 0.6) + LeanStepMath.SideOf((i % 200) / 100.0 - 1);
                sink += LeanStepMath.Gap(i, 2, 3, 4) + (LeanStepMath.Explained(i % 9, 12) ? 1 : 0) + LeanStepMath.PromptIndex(i % 5, i % 7, out done);
                sink += LeanStepMath.LeanExtent(i, -i, 1, 2, 3, 4, 5, 6, 14);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Check("TB9 LeanStepMath allocates nothing (1M frames)", after - before == 0 && !double.IsNaN(sink), (after - before) + " bytes");
        }
    }
}
