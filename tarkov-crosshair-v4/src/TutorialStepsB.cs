// TutorialStepsB.cs -- 首次设置教程 (v4.0), script steps 8-10 (owner: TUT-B).
//   Step 8 侧身 (LeanStep) lives here. Step 9 换手 = src/TutorialStep9.cs, step 10 跳跃与拉枪 + 完成 = src/TutorialStep10.cs
//   (one worker each); AddSteps calls their `public static void Add(List<TutorialStep>)` after step 8.
// The pure maths (LeanStepMath) compiles on its own (tests-v4 defines TUTB_PURE_ONLY and compiles only that part).
// C# 5. No WinForms. No allocation per frame in Update / Draw.
using System;
using System.Collections.Generic;
#if !TUTB_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
#endif

namespace TarkovCrosshair
{
    /// <summary>
    /// Pure maths of the 侧身 step (testable without System.Drawing). Every number here is 示意 (demo timing / layout),
    /// not measured game data.
    /// </summary>
    public static class LeanStepMath
    {
        /// <summary>Demo loop (ms) and its key segments: Q held, fire, E held, fire.</summary>
        public const int LoopMs = 8400;
        public const int QFrom = 600, QTo = 3600, QFireFrom = 1100, QFireTo = 3400;
        public const int EFrom = 4500, ETo = 7500, EFireFrom = 5000, EFireTo = 7300;
        /// <summary>Scripted ring adjustment: the ring starts at 0 lean offset and slides to the profile value
        /// (「调整偏移量，直到对上弹孔」), starting AdjustDelayMs after the first shot of that side.</summary>
        public const int AdjustDelayMs = 900, AdjustMs = 750;
        /// <summary>Live mode: shots per side before the prompt moves on / the step counts as done.</summary>
        public const int ShotsNeeded = 3;
        /// <summary>Minimum match tolerance (UI px) when the ring is very small.</summary>
        public const double MinTolPx = 4;
        /// <summary>|lean| above this counts as leaned to that side.</summary>
        public const double SideThreshold = 0.5;
        /// <summary>Scene roll direction of a lean, the ONE place to flip it: +1 = Q (左侧身) rotates the scene
        /// COUNTER-clockwise on screen, E clockwise (Leo confirmed this, 16 Nov 2026). -1 reverses both.</summary>
        public const double LeanRollSign = 1.0;
        /// <summary>Scene roll of a full lean in degrees (示意).</summary>
        public const double LeanRollDeg = 6.0;
        /// <summary>Below this scene size (logical px) the HUD shows only the core picture + one tag (review B18).</summary>
        public const double CompactW = 700, CompactH = 330;

        /// <summary>FpRig.RollPerLean for this step (radians per full lean, sign = LeanRollSign).</summary>
        public static double RollPerLean() { return LeanRollSign * LeanRollDeg * Math.PI / 180.0; }

        /// <summary>Screen rotation used by TutorialGfx.BeginRoll (GDI+ RotateTransform, y-down): (dx, dyDown) around the
        /// centre by roll radians; positive roll = clockwise on screen.</summary>
        public static void RollPoint(double dx, double dyDown, double roll, out double x, out double yDown)
        {
            double c = Math.Cos(roll), s = Math.Sin(roll);
            x = dx * c - dyDown * s;
            yDown = dx * s + dyDown * c;
        }

        /// <summary>True when the scene is too small for the full HUD (logical px = px / k).</summary>
        public static bool Compact(double sceneW, double sceneH, double k)
        {
            if (!(k > 0)) k = 1;
            return sceneW / k < CompactW || sceneH / k < CompactH;
        }

        /// <summary>Label box (left, top, w, h) vs a block that covers everything below blockTop between blockLeft and
        /// blockRight (the gun): returns a top that ends 'gap' above the block when they would overlap, else top.</summary>
        public static double AvoidBelow(double left, double top, double w, double h, double blockLeft, double blockRight, double blockTop, double gap)
        {
            bool overlapX = left + w > blockLeft && left < blockRight;
            if (overlapX && top + h > blockTop - gap) return blockTop - gap - h;
            return top;
        }

        /// <summary>Smoothstep progress of an animation that starts at startMs and lasts durMs (0 before, 1 after).</summary>
        public static double Progress(double tMs, double startMs, double durMs)
        {
            if (double.IsNaN(tMs)) return 0;
            if (!(durMs > 0)) return tMs >= startMs ? 1 : 0;
            return TutorialGeometry.Smooth((tMs - startMs) / durMs);
        }

        /// <summary>Scripted ring adjustment of each side at loop time t (0 = ring at no lean offset, 1 = profile value).</summary>
        public static void ScriptAdjust(double tMs, out double adjQ, out double adjE)
        {
            adjQ = Progress(tMs, QFireFrom + AdjustDelayMs, AdjustMs);
            adjE = Progress(tMs, EFireFrom + AdjustDelayMs, AdjustMs);
        }

        /// <summary>-1 leaned left, +1 leaned right, 0 otherwise.</summary>
        public static int SideOf(double lean)
        {
            if (lean <= -SideThreshold) return -1;
            if (lean >= SideThreshold) return 1;
            return 0;
        }

        /// <summary>Scene px per UI px so that extentUi (at least minExtentUi) fills 'fill' of halfPx; k = DPI scale.
        /// Returns the rig Zoom (before K).</summary>
        public static double FitZoom(double halfPx, double k, double extentUi, double minExtentUi, double fill)
        {
            if (!(k > 0)) k = 1;
            if (!(halfPx > 0)) return 0.05;
            if (!(fill > 0)) fill = 1;
            double e = Math.Abs(extentUi);
            if (double.IsNaN(e) || double.IsInfinity(e)) e = 0;
            if (e < minExtentUi) e = minExtentUi;
            if (!(e > 0)) e = 1;
            double z = halfPx * fill / (e * k);
            if (z < 0.05) z = 0.05;
            if (z > 40) z = 40;
            return z;
        }

        /// <summary>v4.1 skeleton panel (left part of the scene): share of the scene width (normal / compact).</summary>
        public const double SkelShare = 0.38, SkelShareCompact = 0.42;
        /// <summary>v4.1 reduced motion: the scripted demo stops here (Q held, ring already on the holes).</summary>
        public const int FreezeMs = QFireFrom + AdjustDelayMs + AdjustMs + 400;
        /// <summary>3/4 back view of the skeleton (rad): from behind, turned so the rifle points to the screen right.</summary>
        public const double SkelViewYaw = -0.42;

        /// <summary>Width (px) of the skeleton panel for a scene width; 0 when the scene is too narrow for two panels.</summary>
        public static double SkelPanelW(double sceneW, double k, bool compact)
        {
            if (!(k > 0)) k = 1;
            if (!(sceneW > 0) || sceneW / k < 300) return 0;
            return Math.Floor(sceneW * (compact ? SkelShareCompact : SkelShare));
        }

        /// <summary>Skeleton scale (px per metre) that fits a 1.95 m figure, a full lean and the rays into w x h px.</summary>
        public static double SkelScale(double w, double h)
        {
            if (!(w > 0) || !(h > 0)) return 1;
            return Math.Max(1, Math.Min(h * 0.84 / 1.95, w / 1.75));
        }

        /// <summary>Distance between the hole centre and the ring's full-lean point (UI px).</summary>
        public static double Gap(double holeX, double holeY, double ringX, double ringY)
        {
            double dx = holeX - ringX, dy = holeY - ringY;
            double d = Math.Sqrt(dx * dx + dy * dy);
            return double.IsNaN(d) ? double.PositiveInfinity : d;
        }

        /// <summary>「大致对上」: the hole centre lies inside the ring (radius = diameter / 2, at least MinTolPx).</summary>
        public static bool Explained(double gapPx, double ringDiameterUi)
        {
            double tol = ringDiameterUi / 2.0;
            if (double.IsNaN(tol) || tol < MinTolPx) tol = MinTolPx;
            return gapPx <= tol;
        }

        /// <summary>Live progress: prompt 0 (Q) until ShotsNeeded left-lean shots, then prompt 1 (E); done after both.</summary>
        public static int PromptIndex(int qShots, int eShots, out bool done)
        {
            done = qShots >= ShotsNeeded && eShots >= ShotsNeeded;
            return qShots >= ShotsNeeded ? 1 : 0;
        }

        /// <summary>Largest |value| of the lean offsets of two profiles (for the zoom), plus the given margin.</summary>
        public static double LeanExtent(double qx1, double qy1, double ex1, double ey1, double qx2, double qy2, double ex2, double ey2, double margin)
        {
            double m = 0;
            m = Math.Max(m, Math.Abs(qx1)); m = Math.Max(m, Math.Abs(qy1)); m = Math.Max(m, Math.Abs(ex1)); m = Math.Max(m, Math.Abs(ey1));
            m = Math.Max(m, Math.Abs(qx2)); m = Math.Max(m, Math.Abs(qy2)); m = Math.Max(m, Math.Abs(ex2)); m = Math.Max(m, Math.Abs(ey2));
            if (double.IsNaN(m) || double.IsInfinity(m)) m = 0;
            return m + margin;
        }
    }

#if !TUTB_PURE_ONLY
    public static partial class TutorialStepsB
    {
        static partial void AddSteps(List<TutorialStep> steps)
        {
            steps.Add(new LeanStep());
            AddFrom("TarkovCrosshair.TutorialStep9", steps);   // 换手 (src/TutorialStep9.cs)
            AddFrom("TarkovCrosshair.TutorialStep10", steps);  // 跳跃与拉枪 + 完成 (src/TutorialStep10.cs)
            TutorialStepsV51.AddFeatures(steps);               // v5.1: 开镜 / 丢雷 / 强化 / 弹药, before 完成 (src/TutorialStepsV51.cs)
        }

        /// <summary>Calls `public static void Add(List&lt;TutorialStep&gt;)` of a per-step class. Found by reflection so this file
        /// compiles before TutorialStep9.cs / TutorialStep10.cs exist; a missing class gives a placeholder page instead.
        /// Runs once per tutorial window (not per frame).</summary>
        private static void AddFrom(string typeName, List<TutorialStep> steps)
        {
            Type t = typeof(TutorialStepsB).Assembly.GetType(typeName, false);
            MethodInfo m = t == null ? null : t.GetMethod("Add", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(List<TutorialStep>) }, null);
            if (m != null)
            {
                m.Invoke(null, new object[] { steps });
                return;
            }
            if (typeName.EndsWith("9", StringComparison.Ordinal))
                steps.Add(new TutorialPlaceholderStep("hand", "换手", "换手", false));
            else
            {
                steps.Add(new TutorialPlaceholderStep("jump", "跳跃与拉枪", "跳跃与拉枪", false));
                steps.Add(new TutorialPlaceholderStep("done", "完成", "全部完成！", true));
            }
        }
    }

    /// <summary>
    /// Script step 8 「侧身 Q / E」: stand still, lean, fire; the lean offsets should roughly explain the holes.
    /// Demo: holes come from a 示意 truth (Leo's reference lean values, recoil removed so only the lean shows); the ring
    /// uses the user's own lean values. In the scripted loop the ring starts at no lean offset and slides onto the holes
    /// (「调整偏移量」); in live mode the ring always shows the user's values, so editing the fields moves it at once.
    /// </summary>
    internal sealed class LeanStep : TutorialStep
    {
        private static readonly TutorialScript Script = new TutorialScript(LeanStepMath.LoopMs,
            new ScriptSeg(LeanStepMath.QFrom, LeanStepMath.QTo, TKey.Q),
            new ScriptSeg(LeanStepMath.QFireFrom, LeanStepMath.QFireTo, TKey.Fire),
            new ScriptSeg(LeanStepMath.EFrom, LeanStepMath.ETo, TKey.E),
            new ScriptSeg(LeanStepMath.EFireFrom, LeanStepMath.EFireTo, TKey.Fire));

        private static readonly TutorialField[] LeanFields = new TutorialField[]
        {
            TutorialField.Offset("左侧身：左右", "推荐改", true, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return p.LeanQX; }, delegate(Profile p, double v) { p.LeanQX = v; }),
            TutorialField.Offset("左侧身：上下", "推荐改", true, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return p.LeanQY; }, delegate(Profile p, double v) { p.LeanQY = v; }),
            TutorialField.Offset("右侧身：左右", "推荐改", true, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return p.LeanEX; }, delegate(Profile p, double v) { p.LeanEX = v; }),
            TutorialField.Offset("右侧身：上下", "推荐改", true, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return p.LeanEY; }, delegate(Profile p, double v) { p.LeanEY = v; }),
        };

        private const string TagStand = "站立不动";
        private const string TagStandWarn = "请站着不动";
        private const string TagOffset = "侧身偏移";
        private const string TagHoles = "弹孔：开发者参考值";
        private const string TagRing = "准星：你的数值";
        private const string TagMatch = "大致对上";
        private const string TagMiss = "还差";
        private const string TagAdjust = "调整偏移量 …";
        private const string LabelQ = "左侧身 弹孔中心", LabelE = "右侧身 弹孔中心", LabelPx = "px";
        private const string WallLabel = "10 米";

        private readonly FpRig rig = new FpRig();
        // v4.1 skeleton panel
        private readonly SkelRig skel = new SkelRig(), ghost = new SkelRig();
        private readonly SkelCamera skCam = new SkelCamera();
        private SkelStyle skStyle, ghostStyle;
        private bool paused, frozeOnce;
        private RectangleF skelChip;
        private const int ChipPause = 1;
        private string leanQLabel = "左侧身", leanELabel = "右侧身";
        private const string SkTitle = "你的角色 · 背后看";
        private const string LabelEye = "眼睛＝屏幕中心", LabelMuzzle = "枪口", LabelPivot = "转轴：骨盆";
        private const string LabelCentreRay = "中心线", LabelBulletRay = "子弹线", LabelUpright = "站直时";
        private const string LabelCentre = "屏幕中心";
        private const string ChipPauseText = "暂停", ChipPlayText = "继续";
        private double refQX, refQY, refEX, refEY;   // 示意 truth (Leo's reference lean values)
        private double adjQ = 1, adjE = 1;
        private double prevT = -1;
        private int qShots, eShots;
        private bool moving;
        private Color[] kindColors;

        // per-frame hole stats (no allocation)
        private int nQ, nE;
        private double mQX, mQY, mEX, mEY;

        public LeanStep()
        {
            Id = "lean";
            Chapter = "实战校准";
            Title = "侧身 Q / E";
            Body = "站着不动，按 {左侧身} 或 {右侧身} 侧身（歪头）。\n头、身体和枪一起绕骨盆歪，准星跟着移开。\n侧身开几枪，偏移量大致对上弹孔就行。";
            DevNote = "侧身要站着不动测：侧身开枪，让偏移量差不多能解释弹孔就可以。最后还会再统一精调。";
            Prompts = new string[] { "站定，按 {左侧身} 侧身，开几枪", "站定，按 {右侧身} 侧身，开几枪" };
            Hint = Prompts[0];
            Terminal = new string[] { "> 侧身  站立  ……完成", "> 精调：留到最后" };
            FieldNote = NoteBase;
            SettingsPage = TutorialPages.Lean;
            Fields = LeanFields;
            KeysShown = TKey.Q | TKey.E | TKey.Fire;
            ClickFires = true;
        }

        public override void Enter(TutorialContext c)
        {
            Profile reference = Profile.CreateLeoDefault("参考");
            refQX = reference.LeanQX; refQY = reference.LeanQY; refEX = reference.LeanEX; refEY = reference.LeanEY;
            rig.TargetDistanceM = 10;
            rig.Setup(c, c.P, c.P, false);
            Profile t = rig.TruthP, r = rig.RingP;
            t.LeanQX = refQX; t.LeanQY = refQY; t.LeanEX = refEX; t.LeanEY = refEY;
            // only the lean shows: no recoil, no jump (示意)
            t.RecoilMaxPx = 0; t.RecoilClickKickPx = 0; r.RecoilMaxPx = 0; r.RecoilClickKickPx = 0;
            if (t.Jump != null) t.Jump.Enabled = false;
            if (r.Jump != null) r.Jump.Enabled = false;
            rig.SetDistanceNow(10);
            rig.WallLabel = WallLabel;
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallWm = 3.2; rig.WallHm = 2.2;
            rig.RollPerLean = LeanStepMath.RollPerLean();
            prevT = -1; qShots = 0; eShots = 0; adjQ = 1; adjE = 1;
            PromptIndex = 0; Done = false;
            paused = false; frozeOnce = false;
            string qn = c.PromptKey(KeyBindings.ActionLeanLeft), en = c.PromptKey(KeyBindings.ActionLeanRight);
            leanQLabel = "左侧身 " + (string.IsNullOrEmpty(qn) ? "Q" : qn);
            leanELabel = "右侧身 " + (string.IsNullOrEmpty(en) ? "E" : en);
            RefreshFieldNote(c);
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id == ChipPause) { paused = !paused; frozeOnce = true; }
        }

        private const string NoteBase = "真实数值以游戏里的弹孔为准。";

        /// <summary>FieldNote with the profile's current lean values (built on enter / field edit only, never per frame).</summary>
        private void RefreshFieldNote(TutorialContext c)
        {
            Profile p = c.P;
            string cur = p == null ? "" :
                "当前：左 " + TutorialPct.Vec(p.LeanQX, p.LeanQY, c.G)
                + "　右 " + TutorialPct.Vec(p.LeanEX, p.LeanEY, c.G) + "\n";
            FieldNote = cur + NoteBase;
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            RefreshFieldNote(c);
            // lean values are copied every frame; refresh the rest (look, curves) from the profile
            Profile p = c.P, r = rig.RingP;
            if (p == null || r == null) return;
            r.Diameter = p.Diameter; r.Thickness = p.Thickness; r.Color = p.Color; r.Color2 = p.Color2; r.LeanMs = p.LeanMs;
            rig.TruthP.LeanMs = p.LeanMs;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            // 暂停 (chip, or reduced motion after one pass): the picture holds; any live key or click resumes
            if (paused && (c.LiveMask != 0 || c.LiveFire)) paused = false;
            if (paused) { LitKeys = 0; return; }
            bool fire; int lit;
            InputState i = c.Input(Script, out fire, out lit);
            LitKeys = lit;
            if (c.Scripted)
            {
                double t = Script.Wrap(c.Arbiter.ScriptMs);
                if (t < prevT || c.Arbiter.Resumed) rig.ClearHoles();
                prevT = t;
                LeanStepMath.ScriptAdjust(t, out adjQ, out adjE);
                if (c.ReducedMotion && !frozeOnce && t >= LeanStepMath.FreezeMs) { frozeOnce = true; paused = true; }
                // the footer prompt follows the demo (Q part, then E part) until the user has done the step live
                if (!Done) PromptIndex = (qShots >= LeanStepMath.ShotsNeeded || t >= LeanStepMath.QTo) ? 1 : 0;
            }
            else
            {
                prevT = -1;
                adjQ = 1; adjE = 1;
            }
            Profile p = c.P, r = rig.RingP;
            if (p != null && r != null)
            {
                r.LeanQX = p.LeanQX * adjQ; r.LeanQY = p.LeanQY * adjQ;
                r.LeanEX = p.LeanEX * adjE; r.LeanEY = p.LeanEY * adjE;
            }
            moving = i.Fwd || i.Back || i.Left || i.Right;
            int side = rig.Truth.LeanTarget;
            rig.NextHoleKind = side > 0 ? 1 : (side < 0 ? 0 : 2); // 2 = not leaned (not counted)
            rig.Update(dtMs, i, fire);

            if (!c.Scripted && rig.ShotsThisFrame > 0 && !moving)
            {
                int s = LeanStepMath.SideOf(rig.Truth.Lean);
                if (s < 0) qShots += rig.ShotsThisFrame;
                else if (s > 0) eShots += rig.ShotsThisFrame;
                bool done;
                int pi = LeanStepMath.PromptIndex(qShots, eShots, out done);
                PromptIndex = pi;
                if (done) Done = true;
            }
        }

        private void HoleStats()
        {
            nQ = 0; nE = 0; mQX = mQY = mEX = mEY = 0;
            double d = rig.DistanceM;
            for (int k = 0; k < rig.HoleCount; k++)
            {
                TutorialHole h = rig.Holes[k];
                if (h.AgeMs >= rig.HoleFadeMs) continue;
                double x = TutorialGeometry.WallToScreen(h.WX, d), y = TutorialGeometry.WallToScreen(h.WY, d);
                if (h.Kind == 1) { nE++; mEX += x; mEY += y; }
                else if (h.Kind == 0) { nQ++; mQX += x; mQY += y; }
            }
            if (nQ > 0) { mQX /= nQ; mQY /= nQ; }
            if (nE > 0) { mEX /= nE; mEY /= nE; }
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            curG = c.G;
            // v4.1: left = the skeleton (what Q / E do to head, body and gun), right = first person (ring vs screen centre)
            bool compact = LeanStepMath.Compact(scene.Width, scene.Height, gx.K) || c.Compact;
            float skW = (float)LeanStepMath.SkelPanelW(scene.Width, gx.K, compact);
            RectangleF fp = new RectangleF(scene.Left + skW, scene.Top, scene.Width - skW, scene.Height);
            DrawFp(gx, c, fp, compact);
            if (skW > 0) DrawSkeleton(gx, c, new RectangleF(scene.Left, scene.Top, skW, scene.Height), compact);
            else { skelChip = RectangleF.Empty; }
        }

        private void DrawFp(TutorialGfx gx, TutorialContext c, RectangleF scene, bool compact)
        {
            TutorialPalette pal = gx.Pal;
            if (kindColors == null) kindColors = new Color[] { pal.Cyan, pal.Hot, pal.Muted };
            rig.KindColors = kindColors;
            Profile p = c.P;
            double ext = LeanStepMath.LeanExtent(refQX, refQY, refEX, refEY,
                p == null ? 0 : p.LeanQX, p == null ? 0 : p.LeanQY, p == null ? 0 : p.LeanEX, p == null ? 0 : p.LeanEY, 14);
            float half = Math.Min(scene.Width, scene.Height) / 2f;
            rig.Zoom = LeanStepMath.FitZoom(half, gx.K, ext, 36, 0.62);
            // wall = about 62 % x 70 % of the scene at any zoom (示意 board, so its edges stay visible when leaning)
            double zk = rig.Zoom * gx.K;
            if (zk > 0)
            {
                rig.WallWm = TutorialGeometry.ScreenToWall(scene.Width * 0.62 / zk, rig.DistanceM);
                rig.WallHm = TutorialGeometry.ScreenToWall(scene.Height * 0.70 / zk, rig.DistanceM);
            }
            rig.Draw(gx, scene);

            Graphics g = gx.G;
            g.SetClip(scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);
            HoleStats();
            double ringD = rig.RingP != null ? rig.RingP.Diameter : 12;
            // Labels belong to the side the scene is leaned to NOW (the ring sits on that side's full-lean point only then):
            // no 「大致对上」 once the ring is back in the centre, no Q labels during the E lean (review B9).
            int active = LeanStepMath.SideOf(rig.Truth.Lean);
            GunBox(scene, gx);
            float rx = (float)(cx + rig.RingX * z), ry = (float)(cy - rig.RingY * z);
            float qsx, qsy, esx, esy;
            HoleScreen(cx, cy, z, mQX, mQY, out qsx, out qsy);
            HoleScreen(cx, cy, z, mEX, mEY, out esx, out esy);
            if (nQ >= 2) DrawSide(gx, scene, cx, cy, z, mQX, mQY, rx, ry, ringD, kindColors[0], LabelQ, active < 0, nE >= 2, esx, esy);
            if (nE >= 2) DrawSide(gx, scene, cx, cy, z, mEX, mEY, rx, ry, ringD, kindColors[1], LabelE, active > 0, nQ >= 2, qsx, qsy);

            // offset arrow: screen centre -> ring
            double len = Math.Sqrt((rx - cx) * (rx - cx) + (ry - cy) * (ry - cy));
            float pad = gx.S(10), gap = gx.S(5);
            if (len > gx.S(10))
            {
                gx.Arrow(TutorialGfx.A(pal.Neon, 200), 1.4f, cx, cy, rx, ry);
            }
            // left of the ring (it sits on the centre while not leaning), not under it
            float ringRs = Math.Max(gx.S(16), (float)(ringD * z)) / 2f;
            if (!compact && active == 0) gx.Text(LabelCentre, gx.F.Small, pal.Text, cx - Math.Max(gx.S(8), ringRs + gx.S(6)), cy + gx.S(6), 2);

            // HUD (compact scene: only the 站立不动 tag, review B18). 「侧身偏移 · 示意」 sits in a fixed spot (top centre,
            // same neon as the arrow) so it never lands on the hole labels near the centre.
            RectangleF t1 = gx.Tag(moving ? TagStandWarn : TagStand, scene.Left + pad, scene.Top + pad, moving ? pal.Hot : pal.Accent, 0);
            if (!compact)
            {
                // v4.1: max 2 tags per view (站立不动 + the result pill); the rest are plain legend lines
                RectangleF t2 = gx.Legend(TagHoles, scene.Left + pad, t1.Bottom + gap, pal.Hot, TutorialGfx.MarkDot, 0);
                gx.Legend(TagRing, scene.Left + pad, t2.Bottom + gx.S(3), pal.Neon, TutorialGfx.MarkRing, 0);
                RectangleF to = gx.Legend(TagOffset, cx, scene.Top + pad, pal.Neon, TutorialGfx.MarkLine, 1);
                if (c.Scripted && ((adjQ > 0.02 && adjQ < 0.98) || (adjE > 0.02 && adjE < 0.98)))
                    gx.Legend(TagAdjust, cx, to.Bottom + gx.S(3), pal.Neon, TutorialGfx.MarkDot, 1);
                // key caps top-right, sized for the key font (fonts follow the window size)
                float cap = Math.Max(26f, M(gx, KeySample, gx.F.Key).Height / gx.K + 10f);
                float clusterW = gx.S(cap) * 3.25f + gx.S(4) * 2 + gx.S(10) + gx.S(cap) * 0.9f;
                gx.KeyCluster(scene.Right - pad - clusterW, scene.Top + pad, cap, LitKeys, KeysShown, true);
            }
            g.ResetClip();
        }

        /// <summary>v4.1: the skeleton seen from behind (3/4): a faint upright ghost, the leaned pose (spine + neck turn around
        /// the pelvis, head + gun follow), the eye (= screen centre) and the muzzle, the centre line and the bullet line,
        /// the lean angle at the pelvis, and the 暂停 / 继续 chip. Follows the same lean as the first-person view.</summary>
        private void DrawSkeleton(TutorialGfx gx, TutorialContext c, RectangleF sk, bool compact)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            if (skStyle == null)
            {
                skStyle = SkelStyle.Friendly(pal);
                skStyle.ShowCentre = true; skStyle.ShowBullet = true; skStyle.CentreLenM = 1.2; skStyle.BulletLenM = 1.2;
                ghostStyle = SkelStyle.Friendly(pal);
                ghostStyle.Outline = false; ghostStyle.ShowJoints = false; ghostStyle.ShowEye = false; ghostStyle.Alpha = 0.28;
                ghostStyle.Bone = pal.Muted; ghostStyle.Rifle = pal.Muted;
            }
            g.SetClip(sk);
            gx.SceneBackground(sk);
            float pad = gx.S(8);
            RectangleF box = new RectangleF(sk.Left + pad, sk.Top + pad, sk.Width - 2 * pad, sk.Height - 2 * pad);
            RectangleF inr = gx.PlotFrame(box, compact ? null : SkTitle);
            // 暂停 / 继续 chip, bottom-left of the panel
            Font cf = gx.F.Button;
            string chipText = paused ? ChipPlayText : ChipPauseText;
            float chH = Math.Max(gx.S(24), cf.GetHeight(g) + gx.S(8));
            float chW = Math.Max(gx.S(60), M(gx, chipText, cf).Width + gx.S(22));
            skelChip = new RectangleF(inr.Left, inr.Bottom - chH, chW, chH);
            gx.Chip(ChipPause, skelChip, chipText, paused);
            float ground = skelChip.Top - gx.S(8);
            double scale = LeanStepMath.SkelScale(inr.Width, ground - inr.Top);
            float ox = inr.Left + inr.Width * 0.44f;
            skCam.SetBack(ox, ground, 0, 0, 0, LeanStepMath.SkelViewYaw, scale);
            // ground
            g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 110), 1f), inr.Left, ground, inr.Right, ground);
            for (float x = inr.Left + gx.S(6); x < inr.Right; x += gx.S(12))
                g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 40), 1f), x, ground, x - gx.S(6), ground + gx.S(5));

            double lean = rig.Truth.Lean;
            SkelPose pose = SkelPose.Standing(0, 0, 0);
            pose.Hand = rig.HandBlend;
            ghost.Solve(pose);
            pose.Lean = lean;
            skel.Solve(pose);
            SkelDraw.Draw(gx, ghost, skCam, ghostStyle);
            SkelDraw.Draw(gx, skel, skCam, skStyle);

            float px, py, nx, ny, ex, ey, gex, gey, mx, my, hx, hy;
            SkelDraw.Joint(skCam, skel, SkelJ.Pelvis, out px, out py);
            SkelDraw.Joint(skCam, skel, SkelJ.Neck, out nx, out ny);
            SkelDraw.Joint(skCam, skel, SkelJ.Eye, out ex, out ey);
            SkelDraw.Joint(skCam, ghost, SkelJ.Eye, out gex, out gey);
            SkelDraw.Joint(skCam, skel, SkelJ.Muzzle, out mx, out my);
            SkelDraw.Joint(skCam, skel, SkelJ.Head, out hx, out hy);
            int side = LeanStepMath.SideOf(lean);
            Color sideCol = side < 0 ? pal.KeyQ : (side > 0 ? pal.KeyE : pal.Text);
            // pivot + lean angle at the pelvis
            float pr = gx.S(5);
            g.DrawEllipse(gx.P(TutorialGfx.A(pal.Text, 230), 1.4f), px - pr, py - pr, 2 * pr, 2 * pr);
            double ang = Math.Atan2(ny - py, nx - px) * 180.0 / Math.PI;   // -90 = upright (y down)
            float sweep = (float)(ang + 90.0);
            if (Math.Abs(sweep) > 1.5f)
            {
                float ar = (float)(0.42 * scale);
                g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Muted, 170), 1f), px, py, px, py - ar * 1.25f);
                g.DrawArc(gx.P(TutorialGfx.A(sideCol, 230), 2f), px - ar, py - ar, 2 * ar, 2 * ar, -90f, sweep);
            }
            // the eye moved: ghost eye -> eye
            if (Math.Abs(ex - gex) > gx.S(8)) gx.Arrow(TutorialGfx.A(pal.Neon, 210), 1.4f, gex, gey, ex, ey);

            float fh = gx.F.Small.GetHeight(g);
            float topLimit = inr.Top + gx.S(2);
            // above the head, one column: which side is leaned now (tag), then 「眼睛＝屏幕中心」 (the diamond at the eye)
            float tagH = M(gx, leanQLabel, gx.F.Tag).Height + gx.S(5);
            float colH = (side != 0 ? tagH + gx.S(4) : fh + gx.S(2)) + (compact ? 0 : fh + gx.S(2));
            float colTop = Math.Max(topLimit, hy - (float)(SkelDims.HeadR * scale) - gx.S(10) - colH);
            if (side != 0)
            {
                string t = side < 0 ? leanQLabel : leanELabel;
                float tw = M(gx, t, gx.F.Tag).Width + gx.S(12);
                float tx = Math.Max(inr.Left + tw / 2 + gx.S(2), Math.Min(inr.Right - tw / 2 - gx.S(2), hx));
                gx.Tag(t, tx, colTop, sideCol, 1);
                colTop += tagH + gx.S(4);
            }
            else
            {
                float tw = M(gx, LabelUpright, gx.F.Small).Width;
                gx.Text(LabelUpright, gx.F.Small, pal.Muted, Math.Max(inr.Left + tw / 2, Math.Min(inr.Right - tw / 2, hx)), colTop, 1);
                colTop += fh + gx.S(2);
            }
            if (compact) { g.ResetClip(); return; }
            float ew = M(gx, LabelEye, gx.F.Small).Width;
            gx.Text(LabelEye, gx.F.Small, pal.Neon, Math.Max(inr.Left + ew / 2 + gx.S(2), Math.Min(inr.Right - ew / 2 - gx.S(2), ex)), colTop, 1);
            gx.Text(LabelMuzzle, gx.F.Small, pal.Hot, mx - gx.S(4), my + gx.S(8), 2);
            gx.Text(LabelPivot, gx.F.Small, pal.Text, px - gx.S(10), py + gx.S(6), 2);
            // v4.1: the two lines are named once, in a small legend on the chip row (labels at the ray ends crowded the
            // muzzle): dashed = 中心线 (from the eye), solid = 子弹线 (from the muzzle)
            float lgx = skelChip.Right + gx.S(8), lgy = skelChip.Top + (skelChip.Height - fh - gx.S(2)) / 2f;
            float w1 = M(gx, LabelCentreRay, gx.F.Small).Width + gx.S(25), w2 = M(gx, LabelBulletRay, gx.F.Small).Width + gx.S(25);
            if (lgx + w1 <= inr.Right)
            {
                RectangleF l1 = gx.Legend(LabelCentreRay, lgx, lgy, pal.Neon, TutorialGfx.MarkDash, 0);
                if (l1.Right + gx.S(6) + w2 <= inr.Right) gx.Legend(LabelBulletRay, l1.Right + gx.S(6), lgy, pal.Hot, TutorialGfx.MarkLine, 0);
            }
            g.ResetClip();
        }

        private const string KeySample = "W";
        // v4.1 百分比偏移 (rebuilt only when the shown value changes) + the settings it converts with
        private GlobalSettings curG;
        private readonly TutorialPctLabel missPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Magnitude);

        private float TagH(TutorialGfx gx) { return M(gx, TagOffset, gx.F.Tag).Height + gx.S(5); }

        // gun drawing area (TutorialGfx.Gun geometry: muzzle 17 % of the width beside the centre, 66 % down; the body
        // runs from there to the bottom corner, about 30 * u above that line at most)
        private float gunLeft, gunRight, gunTop;

        /// <summary>The gun's screen box from the last rig.Draw (FpRig.GunBounds), grown by the muzzle-flash size and run to
        /// the scene edge on its side; no gun -> a block below the scene.</summary>
        private void GunBox(RectangleF scene, TutorialGfx gx)
        {
            RectangleF b = rig.GunBounds;
            if (!rig.ShowGun || b.Width <= 0 || b.Height <= 0) { gunLeft = scene.Left; gunRight = scene.Right; gunTop = scene.Bottom + 10000; return; }
            float flash = gx.S(20);
            gunTop = b.Top - flash;
            if (rig.HandBlend < 0.5) { gunLeft = b.Left - flash; gunRight = scene.Right; }
            else { gunLeft = scene.Left; gunRight = b.Right + flash; }
        }

        // MeasureString cache (labels are constants / TutorialNum strings, so a few slots cover every frame). Fonts are
        // rebuilt by the host on resize: the cache keys on the Font object, so new fonts are measured again.
        private const int MeasureSlots = 24;
        private readonly string[] mStr = new string[MeasureSlots];
        private readonly Font[] mFont = new Font[MeasureSlots];
        private readonly SizeF[] mSize = new SizeF[MeasureSlots];
        private int mNext;

        private SizeF M(TutorialGfx gx, string s, Font f)
        {
            for (int i = 0; i < MeasureSlots; i++) if ((object)mStr[i] == (object)s && (object)mFont[i] == (object)f) return mSize[i];
            SizeF r = gx.Measure(s, f);
            mStr[mNext] = s; mFont[mNext] = f; mSize[mNext] = r; mNext = (mNext + 1) % MeasureSlots;
            return r;
        }

        /// <summary>Hole-centre marker of one side (follows the rolled wall); its label and 「大致对上」 / 「还差 n px」 only while
        /// that side is leaned. Labels are upright, above the marker, clear of the ring, inside the scene and off the gun.</summary>
        /// <summary>Wall point (UI px) -> screen with the current roll (same rotation as FpRig's BeginRoll).</summary>
        private void HoleScreen(float cx, float cy, float z, double hx, double hy, out float x, out float y)
        {
            double sx, sy;
            LeanStepMath.RollPoint(hx * z, -hy * z, rig.Roll, out sx, out sy);
            x = (float)(cx + sx); y = (float)(cy + sy);
        }

        private void DrawSide(TutorialGfx gx, RectangleF scene, float cx, float cy, float z, double hx, double hy, float rx, float ry,
            double ringD, Color col, string label, bool active, bool hasOther, float ox, float oy)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            float x, y;
            HoleScreen(cx, cy, z, hx, hy, out x, out y);
            float a = gx.S(7);
            Pen pen = gx.P(TutorialGfx.A(col, active ? 240 : 110), 1.2f);
            g.DrawLine(pen, x - a, y, x + a, y);
            g.DrawLine(pen, x, y - a, x, y + a);
            g.DrawEllipse(gx.P(TutorialGfx.A(col, active ? 130 : 55), 1f), x - a * 1.6f, y - a * 1.6f, a * 3.2f, a * 3.2f);
            if (!active) return;

            // gap between the ring and the hole centre on screen, in UI px
            double gapUi = z > 0 ? LeanStepMath.Gap(x, y, rx, ry) / z : double.PositiveInfinity;
            bool ok = LeanStepMath.Explained(gapUi, ringD);
            if (!ok) g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 200), 1.2f), x, y, rx, ry);

            // label block: line 1 「左侧身 弹孔中心」, line 2 the tag; heights are measured (no overlapping line boxes)
            SizeF ml = M(gx, label, gx.F.Tag);
            string n = ok ? null : missPct.Of(gapUi, curG);
            SizeF mt = M(gx, ok ? TagMatch : TagMiss, gx.F.Tag);
            float tagW = mt.Width + gx.S(12), tagH = mt.Height + gx.S(5);
            float numW = 0, numPx = 0;
            if (!ok)
            {
                numPx = M(gx, n, gx.F.Mono).Width;
                numW = gx.S(4) + numPx;
            }
            float lineGap = gx.S(4);
            float w = Math.Max(ml.Width, tagW + numW), h = ml.Height + lineGap + tagH;
            // FpRig draws the ring with diameter max(S(16), Diameter * Zoom * K); start above the ring when it is near
            // FpRig draws the ring with diameter max(S(16), Diameter * Zoom * K)
            float ringR = Math.Max(gx.S(16), (float)(ringD * z)) / 2f;
            float halo = a * 1.8f + gx.S(8);
            float m = gx.S(6), sp = gx.S(6);
            bool gunRightSide = rig.HandBlend < 0.5;
            // candidates in order: above the hole, below it, beside it away from the gun, beside it toward the gun, above
            // the ring. The first one inside the scene that touches neither the ring, the gun nor the other side's marker wins.
            float left = 0, top = 0;
            bool found = false;
            for (int k = 0; k < 5 && !found; k++)
            {
                float cl, ct;
                switch (k)
                {
                    case 0: cl = x - w / 2; ct = y - halo - h; break;
                    case 1: cl = x - w / 2; ct = y + halo; break;
                    case 2: cl = gunRightSide ? x - halo - w : x + halo; ct = y - h / 2; break;
                    case 3: cl = gunRightSide ? x + halo : x - halo - w; ct = y - h / 2; break;
                    default: cl = x - w / 2; ct = ry - ringR - sp - h; break;
                }
                if (k < 2 || k == 4)
                {
                    if (cl < scene.Left + m) cl = scene.Left + m;
                    if (cl + w > scene.Right - m) cl = scene.Right - m - w;
                }
                bool inScene = cl >= scene.Left + m && cl + w <= scene.Right - m && ct >= scene.Top + m && ct + h <= scene.Bottom - m;
                bool hitsRing = rx + ringR + sp > cl && rx - ringR - sp < cl + w && ry + ringR + sp > ct && ry - ringR - sp < ct + h;
                bool hitsGun = LeanStepMath.AvoidBelow(cl, ct, w, h, gunLeft, gunRight, gunTop, sp) != ct;
                bool hitsOther = hasOther && ox + halo > cl && ox - halo < cl + w && oy + halo > ct && oy - halo < ct + h;
                if (inScene && !hitsRing && !hitsGun && !hitsOther) { left = cl; top = ct; found = true; }
                else if (k == 4) { left = cl; top = (float)LeanStepMath.AvoidBelow(cl, ct, w, h, gunLeft, gunRight, gunTop, sp); }
            }
            if (top < scene.Top + m) top = scene.Top + m;
            float mid = left + w / 2;
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 170)), mid - ml.Width / 2 - gx.S(4), top - gx.S(1), ml.Width + gx.S(8), ml.Height + gx.S(2));
            gx.Text(label, gx.F.Tag, gx.Readable(col), mid, top, 1);
            float ty = top + ml.Height + lineGap;
            if (ok) gx.Tag(TagMatch, mid, ty, pal.Neon, 1);
            else
            {
                RectangleF tr = gx.Tag(TagMiss, mid - (tagW + numW) / 2, ty, pal.Hot, 0);
                float nx = tr.Right + gx.S(4), my = tr.Top + tr.Height / 2;
                gx.TextMid(n, gx.F.Mono, gx.Readable(pal.Hot), nx, my, 0);
            }
        }
    }
#endif
}
