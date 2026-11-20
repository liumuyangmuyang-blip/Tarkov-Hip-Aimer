// TutorialStep7.cs -- 首次设置教程 script step 7 「前后 W / S」 (+ WA WD SA SD 自动计算). Owner: step worker 7.
// System.Drawing only (no WinForms), C# 5, no per-frame allocation in Update / Draw.
// The pure maths (TutorialStep7Math) compiles on its own: tests-v4 defines TUTSTEP7_PURE_ONLY and compiles only that part.
// Every number in the demo is 示意 (demo timing / layout), not measured game data.
using System;
using System.Collections.Generic;
#if !TUTSTEP7_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>Pure maths of step 7 (System only). All timings and sizes are 示意.</summary>
    public static class TutorialStep7Math
    {
        /// <summary>Demo loop (ms): W held + fire, S held + fire, then the four diagonals one after another.</summary>
        public const int LoopMs = 13600;
        public const int WFrom = 500, WTo = 3900, WFireFrom = 1000, WFireTo = 3700;
        public const int SFrom = 4700, STo = 8100, SFireFrom = 5200, SFireTo = 7900;
        /// <summary>Diagonal phase: all four light up, then WA, WD, SA, SD are held in turn (no fire).</summary>
        public const int DiagFrom = 8700, DiagHoldMs = 900, DiagGapMs = 150, DiagTo = 8700 + 4 * 1050;
        /// <summary>Scripted Y adjustment: the ring starts at Y = 0 and slides to the profile value, AdjustDelayMs after
        /// the first shot of that key (「Y 数值变化时准星上下移动，套住弹孔」).</summary>
        public const int AdjustDelayMs = 900, AdjustMs = 800;
        /// <summary>Live: settled shots per key before the prompt moves on / the step counts as done.</summary>
        public const int ShotsNeeded = 3;
        /// <summary>Minimum match tolerance (UI px) for a very small ring.</summary>
        public const double MinTolPx = 4;
        /// <summary>The program's diagonal rule (Profile.RecomputeDiagonals): 0.7071 × (sum of the two cardinals).</summary>
        public const double DiagK = 0.7071;
        /// <summary>Hole kinds: settled W-only shot, settled S-only shot, anything else (not counted).</summary>
        public const int KindW = 0, KindS = 1, KindOther = 2;

        public static double Smooth(double u)
        {
            if (double.IsNaN(u) || u <= 0) return 0;
            if (u >= 1) return 1;
            return u * u * (3 - 2 * u);
        }

        /// <summary>Smoothstep progress of an animation starting at startMs, lasting durMs (0 before, 1 after).</summary>
        public static double Progress(double tMs, double startMs, double durMs)
        {
            if (double.IsNaN(tMs)) return 0;
            if (!(durMs > 0)) return tMs >= startMs ? 1 : 0;
            return Smooth((tMs - startMs) / durMs);
        }

        /// <summary>Scripted Y adjustment of W and S at loop time t (0 = ring at Y 0, 1 = profile value). Each resets to 0
        /// at the start of the loop so the demo replays.</summary>
        public static void ScriptAdjust(double tMs, out double adjW, out double adjS)
        {
            adjW = Progress(tMs, WFireFrom + AdjustDelayMs, AdjustMs);
            adjS = Progress(tMs, SFireFrom + AdjustDelayMs, AdjustMs);
        }

        /// <summary>Scripted phase at loop time t: 0 = W part, 1 = S part, 2 = diagonals (自动计算).</summary>
        public static int Phase(double tMs)
        {
            if (tMs >= DiagFrom) return 2;
            if (tMs >= (WTo + SFrom) / 2) return 1;
            return 0;
        }

        /// <summary>Index 0..3 (WA, WD, SA, SD) of the diagonal held at t in the scripted loop, or -1.</summary>
        public static int DiagHeld(double tMs)
        {
            if (tMs < DiagFrom || tMs >= DiagTo) return -1;
            double u = tMs - DiagFrom;
            int i = (int)(u / (DiagHoldMs + DiagGapMs));
            if (i < 0 || i > 3) return -1;
            double inSeg = u - i * (DiagHoldMs + DiagGapMs);
            return inSeg < DiagHoldMs ? i : -1;
        }

        /// <summary>One diagonal component from its two cardinals (same rule as Profile.RecomputeDiagonals).</summary>
        public static double Diag(double a, double b) { return DiagK * (a + b); }

        /// <summary>Kind of a new hole: KindW / KindS only for a settled shot with exactly that one key held.</summary>
        public static int ShotKind(bool fwd, bool back, bool left, bool right, bool settled)
        {
            if (!settled || left || right) return KindOther;
            if (fwd && !back) return KindW;
            if (back && !fwd) return KindS;
            return KindOther;
        }

        /// <summary>Live progress: prompt 0 (W) until ShotsNeeded W shots, then prompt 1 (S); done after both.</summary>
        public static int PromptIndex(int wShots, int sShots, out bool done)
        {
            done = wShots >= ShotsNeeded && sShots >= ShotsNeeded;
            return wShots >= ShotsNeeded ? 1 : 0;
        }

        /// <summary>Key k (0 = W, 1 = S) counts as tested: scripted = its Y slide has started; live = it has a counted shot
        /// (or the step was completed before).</summary>
        public static bool Tested(int k, bool scripted, double adjW, double adjS, int wShots, int sShots, bool everDone)
        {
            if (scripted) return (k == 0 ? adjW : adjS) > 0.02;
            if (everDone) return true;
            return (k == 0 ? wShots : sShots) > 0;
        }

        /// <summary>Distance hole centre -> ring point (UI px); +infinity for NaN.</summary>
        public static double Gap(double holeX, double holeY, double ringX, double ringY)
        {
            double dx = holeX - ringX, dy = holeY - ringY;
            double d = Math.Sqrt(dx * dx + dy * dy);
            return double.IsNaN(d) ? double.PositiveInfinity : d;
        }

        /// <summary>「扣住弹孔」: the hole centre lies inside the ring (radius = diameter / 2, at least MinTolPx).</summary>
        public static bool Caught(double gapPx, double ringDiameterUi)
        {
            double tol = ringDiameterUi / 2.0;
            if (double.IsNaN(tol) || tol < MinTolPx) tol = MinTolPx;
            return gapPx <= tol;
        }

        /// <summary>Rig zoom (before DPI scale k) so that extX / extY (at least minExt) fill 'fill' of the half sizes.</summary>
        public static double FitZoom(double halfW, double halfH, double k, double extX, double extY, double minExt, double fill)
        {
            if (!(k > 0)) k = 1;
            if (!(fill > 0)) fill = 1;
            if (!(halfW > 0) || !(halfH > 0)) return 0.05;
            double ex = Clean(extX), ey = Clean(extY);
            if (ex < minExt) ex = minExt;
            if (ey < minExt) ey = minExt;
            if (!(ex > 0)) ex = 1;
            if (!(ey > 0)) ey = 1;
            double z = Math.Min(halfW * fill / (ex * k), halfH * fill / (ey * k));
            if (z < 0.05) z = 0.05;
            if (z > 40) z = 40;
            return z;
        }

        /// <summary>Largest |value| in a, NaN / infinity treated as 0.</summary>
        public static double MaxAbs(double[] a)
        {
            double m = 0;
            if (a == null) return 0;
            for (int i = 0; i < a.Length; i++) { double v = Clean(a[i]); if (v > m) m = v; }
            return m;
        }

        private static double Clean(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return Math.Abs(v);
        }
    }

#if !TUTSTEP7_PURE_ONLY
    public static class TutorialStep7
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order.</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep7Page());
        }
    }

    /// <summary>
    /// Script step 7 「前后 W / S」. Demo: holes come from a 示意 truth (Leo's built-in W / S values, recoil removed so only the
    /// movement shows); the ring uses the user's own values. In the scripted loop the ring starts at Y 0 and slides onto the
    /// holes while the Y number counts; then the four diagonals light up with 「自动计算」 and the ring visits each one.
    /// Live: hold W (then S) and fire; settled single-key shots are counted and marked.
    /// </summary>
    internal sealed class TutorialStep7Page : TutorialStep
    {
        private static readonly TutorialScript Script = new TutorialScript(TutorialStep7Math.LoopMs,
            new ScriptSeg(TutorialStep7Math.WFrom, TutorialStep7Math.WTo, TKey.W),
            new ScriptSeg(TutorialStep7Math.WFireFrom, TutorialStep7Math.WFireTo, TKey.Fire),
            new ScriptSeg(TutorialStep7Math.SFrom, TutorialStep7Math.STo, TKey.S),
            new ScriptSeg(TutorialStep7Math.SFireFrom, TutorialStep7Math.SFireTo, TKey.Fire),
            DiagSeg(0, TKey.W | TKey.A), DiagSeg(1, TKey.W | TKey.D), DiagSeg(2, TKey.S | TKey.A), DiagSeg(3, TKey.S | TKey.D));

        private static ScriptSeg DiagSeg(int i, int keys)
        {
            int a = TutorialStep7Math.DiagFrom + i * (TutorialStep7Math.DiagHoldMs + TutorialStep7Math.DiagGapMs);
            return new ScriptSeg(a, a + TutorialStep7Math.DiagHoldMs, keys);
        }

        private static readonly Action<Profile> Diagonals = Profile.RecomputeDiagonals;

        private static readonly TutorialField[] WsFields = MakeFields();

        private static TutorialField[] MakeFields()
        {
            TutorialField wy = TutorialField.Offset("W：上下", "推荐改", true, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return p.Dirs[0].OffY; }, delegate(Profile p, double v) { p.Dirs[0].OffY = v; });
            TutorialField sy = TutorialField.Offset("S：上下", "推荐改", true, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return p.Dirs[1].OffY; }, delegate(Profile p, double v) { p.Dirs[1].OffY = v; });
            TutorialField wx = TutorialField.Offset("W：左右", "左右不推荐改", false, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return p.Dirs[0].OffX; }, delegate(Profile p, double v) { p.Dirs[0].OffX = v; });
            TutorialField sx = TutorialField.Offset("S：左右", "左右不推荐改", false, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return p.Dirs[1].OffX; }, delegate(Profile p, double v) { p.Dirs[1].OffX = v; });
            wx.Optional = true; sx.Optional = true;
            TutorialField[] all = new TutorialField[] { wy, sy, wx, sx };
            foreach (TutorialField f in all) f.After = Diagonals;
            return all;
        }

        // ---- HUD text (constants: no per-frame strings) ----
        private const string WallLabel = "10 米";
        private const string TagY = "Y 偏移";
        private const string TagHoles = "弹孔：开发者参考值";
        private const string TagRing = "准星：你的数值";
        private const string TagAuto = "WA · WD · SA · SD 自动";
        private const string TagAutoCalc = "自动计算";
        private const string TagAdjust = "调整 Y …";
        private const string TagCaught = "扣住弹孔";
        private const string TagMiss = "还差";
        private const string TagXNo = "左右不推荐改";
        private const string Untested = "—", LabelCurrent = "当前设置", LabelCurW = "W", LabelCurS = "S";
        private const string LabelPx = "px", LabelY = "Y", LabelX = "X";
        private const string PanelTitle = "偏移量";
        private static readonly string[] DirShort = new string[] { "W", "S", "A", "D", "WA", "WD", "SA", "SD" };
        private static readonly string[] Formula = new string[]
        {
            "WA = 0.7071 × (W + A)", "WD = 0.7071 × (W + D)", "SA = 0.7071 × (S + A)", "SD = 0.7071 × (S + D)"
        };

        private readonly FpRig rig = new FpRig();
        private double refWX, refWY, refSX, refSY;   // 示意 truth: Leo's built-in W / S values
        private double adjW = 1, adjS = 1;
        private double prevT = -1, loopT;
        private int wShots, sShots;
        private bool everDone;
        private Color[] kindColors;
        private readonly double[] ext = new double[12];

        // per-frame hole stats (no allocation)
        private int nW, nS;
        private double mWX, mWY, mSX, mSY;

        public TutorialStep7Page()
        {
            Id = "tuneWS";
            Chapter = "实战校准";
            Title = "前后 W / S";
            Body = "单独按住 W，开枪，调上下。\n再单独按住 S，同样调上下。\n左右不推荐改。";
            DevNote = "W、S 和 A、D 同理：这里不推荐改左右，因为理论上前后移动时 左右不会变化。四个方向都调好后，WA、WD、SA、SD 这些斜向会自动计算，不用再单独调。";
            Prompts = new string[] { "按住 W 开枪", "按住 S 开枪" };
            Hint = Prompts[0];
            Terminal = new string[] { "> 按住 W  调 Y  ……完成", "> WA WD SA SD  自动计算 完成" };
            SettingsPage = TutorialPages.Move;
            Fields = WsFields;
            KeysShown = TKey.W | TKey.S | TKey.Fire;
            ClickFires = true;
        }

        /// <summary>FieldNote with the profile's current W / S Y (built on enter / field edit only, never per frame).</summary>
        private void RefreshFieldNote(TutorialContext c)
        {
            Profile p = c.P;
            FieldNote = p == null || p.Dirs == null || p.Dirs.Count < 2 ? null :
                "当前：W " + TutorialPct.Y(p.Dirs[0].OffY, c.G) + "　S " + TutorialPct.Y(p.Dirs[1].OffY, c.G);
        }

        public override void Enter(TutorialContext c)
        {
            RefreshFieldNote(c);
            Profile reference = Profile.CreateLeoDefault("参考");
            refWX = reference.Dirs[0].OffX; refWY = reference.Dirs[0].OffY;
            refSX = reference.Dirs[1].OffX; refSY = reference.Dirs[1].OffY;
            rig.TargetDistanceM = 10;
            Profile src = c.P ?? reference;
            rig.Setup(c, src, src, false);
            Profile t = rig.TruthP, r = rig.RingP;
            // the truth = Leo's reference cardinals (diagonals by the same rule); only the movement shows: no recoil / lean / jump
            for (int i = 0; i < 4; i++) { t.Dirs[i].OffX = reference.Dirs[i].OffX; t.Dirs[i].OffY = reference.Dirs[i].OffY; }
            WriteDiagonals(t);
            t.RecoilMaxPx = 0; t.RecoilClickKickPx = 0; r.RecoilMaxPx = 0; r.RecoilClickKickPx = 0;
            if (t.Jump != null) t.Jump.Enabled = false;
            if (r.Jump != null) r.Jump.Enabled = false;
            rig.SetDistanceNow(10);
            rig.WallLabel = WallLabel;
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallWm = 1.6; rig.WallHm = 1.2;
            prevT = -1; loopT = 0; adjW = 1; adjS = 1;
            if (!everDone) { wShots = 0; sShots = 0; PromptIndex = 0; Done = false; }
            else { PromptIndex = 1; Done = true; }
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            RefreshFieldNote(c);
            // W / S values are copied every frame; refresh look + curves from the profile
            Profile p = c.P, r = rig.RingP;
            if (p == null || r == null) return;
            r.Diameter = p.Diameter; r.Thickness = p.Thickness; r.Color = p.Color; r.Color2 = p.Color2;
            CopyCurve(p.CurveWS, r.CurveWS); CopyCurve(p.CurveAD, r.CurveAD);
        }

        private static void CopyCurve(MoveCurve from, MoveCurve to)
        {
            if (from == null || to == null) return;
            to.DurationMs = from.DurationMs;
            if (from.Speeds != null && to.Speeds != null && from.Speeds.Length == to.Speeds.Length)
                for (int i = 0; i < from.Speeds.Length; i++) to.Speeds[i] = from.Speeds[i];
        }

        /// <summary>Diagonals by the program's rule, written into the existing DirSetting objects (no allocation).</summary>
        private static void WriteDiagonals(Profile p)
        {
            if (p == null || p.Dirs == null || p.Dirs.Count < Profile.DirCount) return;
            List<DirSetting> d = p.Dirs;
            SetDiag(d[4], d[0], d[2]); SetDiag(d[5], d[0], d[3]); SetDiag(d[6], d[1], d[2]); SetDiag(d[7], d[1], d[3]);
        }

        private static void SetDiag(DirSetting o, DirSetting a, DirSetting b)
        {
            o.OffX = TutorialStep7Math.Diag(a.OffX, b.OffX);
            o.OffY = TutorialStep7Math.Diag(a.OffY, b.OffY);
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire; int lit;
            InputState i = c.Input(Script, out fire, out lit);
            LitKeys = lit;
            if (c.Scripted)
            {
                double t = Script.Wrap(c.Arbiter.ScriptMs);
                if (t < prevT || c.Arbiter.Resumed) rig.ClearHoles();
                prevT = t; loopT = t;
                TutorialStep7Math.ScriptAdjust(t, out adjW, out adjS);
            }
            else
            {
                prevT = -1; loopT = -1;
                adjW = 1; adjS = 1;
            }
            Profile p = c.P, r = rig.RingP;
            if (p != null && r != null && p.Dirs != null && p.Dirs.Count >= Profile.DirCount)
            {
                r.Dirs[0].OffX = p.Dirs[0].OffX; r.Dirs[0].OffY = p.Dirs[0].OffY * adjW;
                r.Dirs[1].OffX = p.Dirs[1].OffX; r.Dirs[1].OffY = p.Dirs[1].OffY * adjS;
                r.Dirs[2].OffX = p.Dirs[2].OffX; r.Dirs[2].OffY = p.Dirs[2].OffY;
                r.Dirs[3].OffX = p.Dirs[3].OffX; r.Dirs[3].OffY = p.Dirs[3].OffY;
                WriteDiagonals(r);
                r.StandOffsetX = p.StandOffsetX; r.StandOffsetY = p.StandOffsetY;
                rig.TruthP.StandOffsetX = p.StandOffsetX; rig.TruthP.StandOffsetY = p.StandOffsetY;
            }
            bool settled = !rig.Truth.Traveling(0) && !rig.Truth.Traveling(1);
            int kind = TutorialStep7Math.ShotKind(i.Fwd, i.Back, i.Left, i.Right, settled);
            rig.NextHoleKind = kind;
            rig.Update(dtMs, i, fire);

            if (!c.Scripted && rig.ShotsThisFrame > 0)
            {
                if (kind == TutorialStep7Math.KindW) wShots += rig.ShotsThisFrame;
                else if (kind == TutorialStep7Math.KindS) sShots += rig.ShotsThisFrame;
                bool done;
                int pi = TutorialStep7Math.PromptIndex(wShots, sShots, out done);
                if (!everDone) PromptIndex = pi;
                if (done) { Done = true; everDone = true; }
            }
        }

        private void HoleStats()
        {
            nW = 0; nS = 0; mWX = mWY = mSX = mSY = 0;
            double d = rig.DistanceM;
            for (int k = 0; k < rig.HoleCount; k++)
            {
                TutorialHole h = rig.Holes[k];
                if (h.AgeMs >= rig.HoleFadeMs) continue;
                double x = TutorialGeometry.WallToScreen(h.WX, d), y = TutorialGeometry.WallToScreen(h.WY, d);
                if (h.Kind == TutorialStep7Math.KindW) { nW++; mWX += x; mWY += y; }
                else if (h.Kind == TutorialStep7Math.KindS) { nS++; mSX += x; mSY += y; }
            }
            if (nW > 0) { mWX /= nW; mWY /= nW; }
            if (nS > 0) { mSX /= nS; mSY /= nS; }
        }

        // ---- measured layout (rebuilt only when the fonts, the scale or the scene size change) ----
        private const string LabelWCentre = "W 弹孔中心", LabelSCentre = "S 弹孔中心", NumProbe = "← 00.00%";
        private const string KeyW = "W", KeyS = "S", KeyA = "A", KeyD = "D";
        private TutorialFonts layFonts;
        private float layK = -1, layW = -1, layH = -1;
        private bool compact;
        private float pxW, numW, numSmallW, keyW, rowH, titleH, noteH, panelW, panelH, monoH;
        private float formulaW, compassTitleW, compassTitleH, smallH, centreLabelW, tagH, keyH;

        /// <summary>Compact scene (small window / high DPI): below 640 x 330 logical px only the core picture is drawn.</summary>
        public static bool IsCompact(double sceneW, double sceneH, double k)
        {
            if (!(k > 0)) k = 1;
            if (double.IsNaN(sceneW) || double.IsNaN(sceneH)) return true;
            return sceneW / k < 640 || sceneH / k < 330;
        }

        private void Layout(TutorialGfx gx, RectangleF scene)
        {
            if (layFonts == gx.F && layK == gx.K && layW == scene.Width && layH == scene.Height) return;
            layFonts = gx.F; layK = gx.K; layW = scene.Width; layH = scene.Height;
            compact = IsCompact(scene.Width, scene.Height, gx.K);
            TutorialFonts f = gx.F;
            pxW = gx.Measure(LabelPx, f.MonoSmall).Width;
            numW = gx.Measure(NumProbe, f.Mono).Width;
            numSmallW = gx.Measure(NumProbe, f.MonoSmall).Width;
            keyW = gx.Measure(KeyW, f.Key).Width;
            keyH = gx.Measure(KeyW, f.Key).Height;
            monoH = gx.Measure(NumProbe, f.Mono).Height;
            rowH = Math.Max(gx.Measure(KeyW, f.Key).Height, monoH) + gx.S(6);
            titleH = gx.Measure(PanelTitle, f.Tag).Height;
            noteH = gx.Measure(TagXNo, f.MonoSmall).Height;
            smallH = gx.Measure(LabelWCentre, f.Small).Height;
            centreLabelW = gx.Measure(LabelWCentre, f.Small).Width;
            tagH = gx.Measure(TagCaught, f.Tag).Height + gx.S(5);
            // value panel: title / W row / S row / note, each on its own line (no text shares a line with another)
            float rowW = keyW + gx.S(10) + gx.Measure(LabelY, f.MonoSmall).Width + gx.S(4) + numW + gx.S(14)
                + gx.Measure(LabelX, f.MonoSmall).Width + gx.S(4) + numSmallW;
            float tw = gx.Measure(PanelTitle, f.Tag).Width, nw = gx.Measure(TagXNo, f.MonoSmall).Width;
            float curW = gx.Measure(LabelCurrent, f.MonoSmall).Width + 2 * (gx.S(10) + keyW + gx.S(4) + numSmallW);
            panelW = Math.Max(Math.Max(rowW, curW), Math.Max(tw, nw)) + gx.S(24);
            panelH = gx.S(8) + titleH + gx.S(6) + 2 * rowH + gx.S(4) + noteH + gx.S(4) + noteH + gx.S(8);
            // compass: title / plot / formula
            formulaW = 0;
            for (int i = 0; i < Formula.Length; i++) formulaW = Math.Max(formulaW, gx.Measure(Formula[i], f.MonoSmall).Width);
            SizeF ct = gx.Measure(TagAuto, f.MonoSmall);
            compassTitleW = ct.Width; compassTitleH = ct.Height;
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            curG = c.G;
            TutorialPalette pal = gx.Pal;
            if (kindColors == null) kindColors = new Color[] { pal.Cyan, pal.Hot, TutorialGfx.A(pal.Muted, 200) };
            rig.KindColors = kindColors;
            Profile rp = rig.RingP, p = c.P;
            if (rp == null) { gx.SceneBackground(scene); return; }
            Layout(gx, scene);

            // zoom: fit every point the ring / holes can reach (ring = user values, holes = reference)
            double sx = rp.StandOffsetX, sy = rp.StandOffsetY;
            int n = 0;
            for (int i = 0; i < Profile.DirCount; i++) { ext[n++] = sx + rp.Dirs[i].OffX; }
            ext[n++] = sx + refWX; ext[n++] = sx + refSX;
            double exX = TutorialStep7Math.MaxAbs(ext);
            n = 0;
            for (int i = 0; i < Profile.DirCount; i++) { ext[n++] = sy + rp.Dirs[i].OffY; }
            ext[n++] = sy + refWY; ext[n++] = sy + refSY;
            double exY = TutorialStep7Math.MaxAbs(ext);
            if (p != null && p.Dirs != null && p.Dirs.Count >= 2)
            {
                exY = Math.Max(exY, Math.Abs(sy + p.Dirs[0].OffY)); exY = Math.Max(exY, Math.Abs(sy + p.Dirs[1].OffY));
            }
            rig.Zoom = TutorialStep7Math.FitZoom(scene.Width / 2f, scene.Height / 2f, gx.K, exX + 16, exY + 16, 80, 0.6);
            rig.Draw(gx, scene);

            Graphics g = gx.G;
            g.SetClip(scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);
            HoleStats();
            double ringD = rp.Diameter;
            float ringR = Math.Max(gx.S(6), (float)(ringD * z / 2));
            int dir = rig.Ring.CurrentDir;
            bool scripted = c.Scripted;
            int phase = scripted ? TutorialStep7Math.Phase(loopT) : (dir >= 4 ? 2 : (dir == 1 ? 1 : 0));
            bool diagShow = phase == 2;
            int held = scripted ? TutorialStep7Math.DiagHeld(loopT) : (dir >= 4 ? dir - 4 : -1);
            float pad = gx.S(10);

            // hole centres + gap to the ring's full W / S point (labels hidden while the diagonals are shown)
            if (nW >= 2) DrawHoleCentre(gx, cx, cy, z, mWX, mWY, sx + rp.Dirs[0].OffX, sy + rp.Dirs[0].OffY, ringD, ringR, kindColors[0], LabelWCentre, !diagShow);
            if (nS >= 2) DrawHoleCentre(gx, cx, cy, z, mSX, mSY, sx + rp.Dirs[1].OffX, sy + rp.Dirs[1].OffY, ringD, ringR, kindColors[1], LabelSCentre, !diagShow);

            // diagonal targets on the scene: all four light up together (自动计算), the held one brightest
            if (diagShow)
            {
                for (int k = 0; k < 4; k++)
                {
                    DirSetting d = rp.Dirs[4 + k];
                    float gxp = (float)(cx + (sx + d.OffX) * z), gyp = (float)(cy - (sy + d.OffY) * z);
                    bool on = held == k;
                    if (on) gx.GlowCircle(pal.Neon, 1.2f, gxp, gyp, ringR);
                    else g.DrawEllipse(gx.Dashed(TutorialGfx.A(pal.Neon, 150), 1.1f), gxp - ringR, gyp - ringR, 2 * ringR, 2 * ringR);
                    gx.TextMid(DirShort[4 + k], gx.F.Tag, on ? pal.Neon : TutorialGfx.A(pal.Neon, 190), gxp, gyp - ringR - gx.S(4) - tagH / 2f, 1);
                }
            }

            // Y bracket: screen centre height -> ring height (W / S only), tag + number to its left
            float rx = (float)(cx + rig.RingX * z), ry = (float)(cy - rig.RingY * z);
            float cyStand = (float)(cy - sy * z);
            if ((dir == 0 || dir == 1) && Math.Abs(ry - cyStand) > gx.S(8))
            {
                float bx = Math.Min(cx, rx) - ringR - gx.S(28);
                gx.Bracket(bx, cyStand, ry, pal.Neon, null);
                gx.G.DrawLine(gx.Dashed(TutorialGfx.A(pal.Neon, 120), 1f), bx, ry, rx - ringR - gx.S(2), ry);
                float my = (cyStand + ry) / 2f;
                float top = my - (tagH + monoH) / 2f;
                RectangleF tr = gx.Legend(TagY, bx - gx.S(8), top, pal.Neon, TutorialGfx.MarkLine, 2); // v4.1: max 2 tags (the results)
                string v = tagYPct.Of(rig.RingY - sy, curG);
                float ny = tr.Bottom + gx.S(2);
                gx.Text(v, gx.F.Mono, pal.Text, bx - gx.S(8), ny, 2);
            }

            // centre top: what the demo is doing now
            float topY = scene.Top + pad;
            if (scripted && ((adjW > 0.02 && adjW < 0.98) || (adjS > 0.02 && adjS < 0.98)))
                topY = gx.Legend(TagAdjust, cx, topY, pal.Neon, TutorialGfx.MarkDot, 1).Bottom + gx.S(4);
            if (diagShow)
            {
                double pulse = 0.65 + 0.35 * Math.Sin(gx.TimeMs / 180.0);
                gx.Legend(TagAutoCalc, cx, topY, TutorialGfx.A(pal.Neon, pulse), TutorialGfx.MarkDot, 1);
            }

            if (compact)
            {
                // small scene: the core picture + the result tags only (v4.1: the 弹孔 tag covered the hole labels)
                g.ResetClip();
                return;
            }

            // top-left tags ((「10 米」 is the wall's own label)
            // v4.1: max 2 tags per view (「Y 偏移」 + the result pill); these are plain legend lines
            RectangleF t2 = gx.Legend(TagHoles, scene.Left + pad, scene.Top + pad, pal.Hot, TutorialGfx.MarkDot, 0);
            RectangleF t3 = gx.Legend(TagRing, scene.Left + pad, t2.Bottom + gx.S(3), pal.Neon, TutorialGfx.MarkRing, 0);

            // top-right: key cluster + value panel (right-aligned, measured)
            float capPx = 26;
            float clusterW = gx.S(capPx) * 3.25f + gx.S(8) + gx.S(10) + gx.S(capPx) * 0.9f;
            SizeF kc = gx.KeyCluster(scene.Right - pad - clusterW, scene.Top + pad, capPx, LitKeys, TKey.W | TKey.S, true);
            RectangleF vp = new RectangleF(scene.Right - pad - panelW, scene.Top + pad + kc.Height + gx.S(10), panelW, panelH);
            if (vp.Bottom < cy) DrawValues(gx, vp, rp, p, dir, scripted);

            // bottom-left: direction compass (keys on a fixed layout, offset vectors separately; diagonals 自动)
            float plotSide = Math.Min(gx.S(150), Math.Min(scene.Height * 0.26f, scene.Width * 0.2f));
            float cw = Math.Max(plotSide + 2 * keyW + gx.S(36), Math.Max(formulaW, compassTitleW) + gx.S(20));
            float ch = gx.S(6) + compassTitleH + gx.S(6) + keyH + gx.S(4) + plotSide + gx.S(4) + keyH + gx.S(6) + noteH + gx.S(8);
            RectangleF cp = new RectangleF(scene.Left + pad, scene.Bottom - pad - ch, cw, ch);
            if (cp.Top > t3.Bottom + gx.S(6) && cp.Right < cx - gx.S(40))
                DrawCompass(gx, cp, plotSide, rp, dir, diagShow, held);
            g.ResetClip();
        }

        // v4.1 百分比偏移 labels (rebuilt only when the shown value changes) + the settings they convert with
        private GlobalSettings curG;
        private readonly TutorialPctLabel tagYPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel missPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Magnitude);
        private readonly TutorialPctLabel valWY = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed), valSY = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel valWX = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed), valSX = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel curWPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed), curSPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);

        private void DrawValues(TutorialGfx gx, RectangleF r, Profile rp, Profile p, int dir, bool scripted)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            gx.RegisterView(r, true);
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 230)), r);
            g.DrawRectangle(gx.P(TutorialGfx.A(pal.Neon, 90), 1f), r.X, r.Y, r.Width, r.Height);
            float x = r.Left + gx.S(12), y = r.Top + gx.S(8);
            gx.Text(PanelTitle, gx.F.Tag, TutorialGfx.A(pal.Neon, 220), x, y, 0);
            y += titleH + gx.S(6);
            for (int k = 0; k < 2; k++)
            {
                DirSetting d = rp.Dirs[k];
                float my = y + rowH / 2f;
                bool on = dir == k;
                Color kc = k == 0 ? pal.Cyan : pal.Hot;
                if (on) g.FillRectangle(gx.B(TutorialGfx.A(kc, 28)), r.Left + gx.S(2), y, r.Width - gx.S(4), rowH);
                if (on) g.DrawLine(gx.P(kc, 2f), r.Left + gx.S(4), y + gx.S(3), r.Left + gx.S(4), y + rowH - gx.S(3));
                gx.TextMid(k == 0 ? KeyW : KeyS, gx.F.Key, on ? kc : pal.Text, x, my, 0);
                float x0 = x + keyW + gx.S(10);
                gx.TextMid(LabelY, gx.F.MonoSmall, pal.Muted, x0, my, 0);
                float xn = x0 + gx.Measure(LabelY, gx.F.MonoSmall).Width + gx.S(4) + numW;
                // a key not tested yet shows 「—」 (not the ring's 0 before its Y slide / before its first shot)
                bool tested = TutorialStep7Math.Tested(k, scripted, adjW, adjS, wShots, sShots, everDone);
                if (tested) gx.TextMid((k == 0 ? valWY : valSY).Of(d.OffY, curG), gx.F.Mono, on ? pal.Neon : pal.Text, xn, my, 2);
                else gx.TextMid(Untested, gx.F.Mono, pal.Muted, xn, my, 2);
                float x1 = xn + gx.S(14);
                gx.TextMid(LabelX, gx.F.MonoSmall, pal.Muted, x1, my, 0);
                gx.TextMid((k == 0 ? valWX : valSX).Of(d.OffX, curG), gx.F.MonoSmall, pal.Muted, r.Right - gx.S(12), my, 2);
                y += rowH;
            }
            y += gx.S(4);
            gx.Text(TagXNo, gx.F.MonoSmall, pal.Accent, x, y, 0);
            // 当前设置: the profile's W / S Y (what the input boxes hold)
            y += noteH + gx.S(4);
            if (p == null || p.Dirs == null || p.Dirs.Count < 2) return;
            gx.Text(LabelCurrent, gx.F.MonoSmall, pal.Muted, x, y, 0);
            float cxp = x + gx.Measure(LabelCurrent, gx.F.MonoSmall).Width + gx.S(10);
            gx.Text(LabelCurW, gx.F.MonoSmall, pal.Cyan, cxp, y, 0);
            cxp += keyW + gx.S(4);
            gx.Text(curWPct.Of(p.Dirs[0].OffY, curG), gx.F.MonoSmall, pal.Text, cxp, y, 0);
            cxp += numSmallW + gx.S(10);
            gx.Text(LabelCurS, gx.F.MonoSmall, pal.Hot, cxp, y, 0);
            cxp += keyW + gx.S(4);
            gx.Text(curSPct.Of(p.Dirs[1].OffY, curG), gx.F.MonoSmall, pal.Text, cxp, y, 0);
        }

        private void DrawCompass(TutorialGfx gx, RectangleF r, float side, Profile rp, int dir, bool diagShow, int diagHeld)
        {
            gx.RegisterView(r, true);
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 230)), r);
            g.DrawRectangle(gx.P(TutorialGfx.A(pal.Neon, 90), 1f), r.X, r.Y, r.Width, r.Height);
            gx.Text(TagAuto, gx.F.MonoSmall, TutorialGfx.A(pal.Neon, 220), r.Left + gx.S(10), r.Top + gx.S(6), 0);
            float pcx = r.Left + r.Width / 2f, pcy = r.Top + gx.S(6) + compassTitleH + gx.S(6) + keyH + gx.S(4) + side / 2f;
            float half = side / 2f;
            if (diagShow && diagHeld < 0) diagHeld = (int)((gx.TimeMs / 1200.0) % 4);
            double m = 1;
            for (int i = 0; i < Profile.DirCount; i++)
            {
                double l = Math.Sqrt(rp.Dirs[i].OffX * rp.Dirs[i].OffX + rp.Dirs[i].OffY * rp.Dirs[i].OffY);
                if (!double.IsNaN(l) && l > m) m = l;
            }
            double sc = (half - gx.S(4)) / m;
            Pen axis = gx.P(TutorialGfx.A(pal.Neon, 35), 1f);
            g.DrawLine(axis, pcx - half, pcy, pcx + half, pcy);
            g.DrawLine(axis, pcx, pcy - half, pcx, pcy + half);
            g.DrawEllipse(gx.Dashed(TutorialGfx.A(pal.Neon, 40), 1f), pcx - half, pcy - half, 2 * half, 2 * half);
            // fixed key letters (where you move): W top, S bottom, A left, D right; the diagonals between them
            float lr = half + gx.S(4) + keyH / 2f;
            float lrx = half + gx.S(6) + keyW / 2f;
            KeyLetter(gx, KeyW, pcx, pcy - lr, dir == 0, pal.Cyan);
            KeyLetter(gx, KeyS, pcx, pcy + lr, dir == 1, pal.Hot);
            KeyLetter(gx, KeyA, pcx - lrx, pcy, dir == 2, pal.Text);
            KeyLetter(gx, KeyD, pcx + lrx, pcy, dir == 3, pal.Text);
            // offset vectors (where the ring goes), coloured like their key
            for (int i = 0; i < Profile.DirCount; i++)
            {
                DirSetting d = rp.Dirs[i];
                float ex = (float)(pcx + d.OffX * sc), ey = (float)(pcy - d.OffY * sc);
                bool isDiag = i >= 4;
                bool lit = dir == i || (isDiag && diagHeld == i - 4);
                Color col;
                if (isDiag) col = diagShow ? (lit ? pal.Neon : TutorialGfx.A(pal.Neon, 160)) : TutorialGfx.A(pal.Muted, 110);
                else col = i == 0 ? pal.Cyan : (i == 1 ? pal.Hot : pal.Text);
                if (!isDiag && !lit) col = TutorialGfx.A(col, i <= 1 ? 210 : 150);
                double len = Math.Sqrt((ex - pcx) * (ex - pcx) + (ey - pcy) * (ey - pcy));
                if (len > gx.S(6))
                {
                    if (isDiag && !lit) g.DrawLine(gx.Dashed(col, 1.1f), pcx, pcy, ex, ey);
                    else if (lit) gx.GlowLine(col, 1.4f, pcx, pcy, ex, ey);
                    else gx.Arrow(col, 1.2f, pcx, pcy, ex, ey);
                    if (lit) gx.GlowDot(col, ex, ey, gx.S(2.2f));
                }
                else gx.Dot(col, ex, ey, gx.S(2f));
                if (isDiag && lit && diagShow)
                {
                    double ux = len > 0.5 ? (ex - pcx) / len : 0, uy = len > 0.5 ? (ey - pcy) / len : -1;
                    gx.TextMid(DirShort[i], gx.F.MonoSmall, col, (float)(ex + ux * gx.S(14)), (float)(ey + uy * gx.S(10)), 1);
                }
            }
            gx.GlowDot(pal.Text, pcx, pcy, gx.S(1.4f));
            float fy = r.Bottom - gx.S(8) - noteH;
            if (diagShow)
            {
                int f = diagHeld;
                if (f < 0 || f > 3) f = 0;
                gx.Text(Formula[f], gx.F.MonoSmall, pal.Neon, pcx, fy, 1);
            }
            else gx.Text(TagAutoCalc, gx.F.MonoSmall, pal.Muted, pcx, fy, 1);
        }

        private void KeyLetter(TutorialGfx gx, string s, float x, float y, bool lit, Color c)
        {
            gx.TextMid(s, gx.F.Key, lit ? c : gx.Pal.Muted, x, y, 1);
        }

        /// <summary>Hole-centre cross + gap line; the label block (「W 弹孔中心」 + 扣住弹孔 / 还差 n px) sits on the side AWAY from the
        /// screen centre, horizontally centred on the hole, so it never runs into the gun (bottom-right) or the Y bracket (left).</summary>
        private void DrawHoleCentre(TutorialGfx gx, float cx, float cy, float z, double hx, double hy, double ringX, double ringY,
            double ringD, float ringR, Color col, string label, bool labels)
        {
            TutorialPalette pal = gx.Pal;
            float x = (float)(cx + hx * z), y = (float)(cy - hy * z);
            float rx = (float)(cx + ringX * z), ry = (float)(cy - ringY * z);
            float a = gx.S(7);
            Pen pen = gx.P(TutorialGfx.A(col, 230), 1.2f);
            gx.G.DrawLine(pen, x - a, y, x + a, y);
            gx.G.DrawLine(pen, x, y - a, x, y + a);
            gx.G.DrawEllipse(gx.P(TutorialGfx.A(col, 120), 1f), x - a * 1.6f, y - a * 1.6f, a * 3.2f, a * 3.2f);
            double gap = TutorialStep7Math.Gap(hx, hy, ringX, ringY);
            bool ok = TutorialStep7Math.Caught(gap, ringD);
            if (!ok) gx.G.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 200), 1.2f), x, y, rx, ry);
            if (!labels) return;
            float reach = Math.Max(a * 1.6f, ringR) + gx.S(6);
            float blockH = smallH + gx.S(3) + tagH;
            bool below = hy < 0; // hole below the centre -> label below it
            float top = below ? Math.Max(y, ry) + reach : Math.Min(y, ry) - reach - blockH;
            gx.Text(label, gx.F.Small, col, x, top, 1);
            float ty = top + smallH + gx.S(3);
            if (ok) gx.Tag(TagCaught, x, ty, pal.Neon, 1);
            else
            {
                string nstr = missPct.Of(gap, curG);
                float nw = gx.Measure(nstr, gx.F.Mono).Width;
                float tw = gx.Measure(TagMiss, gx.F.Tag).Width + gx.S(12);
                float total = tw + gx.S(4) + nw + gx.S(2) + pxW;
                float lx = x - total / 2f;
                RectangleF tr = gx.Tag(TagMiss, lx, ty, pal.Hot, 0);
                float nx = tr.Right + gx.S(4);
                float my = tr.Top + tr.Height / 2f;
                gx.TextMid(nstr, gx.F.Mono, pal.Hot, nx, my, 0);
            }
        }
    }
#endif
}
