// TutorialStep6.cs -- 首次设置教程 script step 6 「左右 A / D」 (owner: STEP6 worker).
// Hold D alone, fire, change X until the ring covers the holes; then the same with A. Y is shown but not recommended.
// System.Drawing only (no WinForms), C# 5, no allocation per frame in Update / Draw.
// The pure maths (TutorialStep6Math) compiles on its own: tests-v4 defines TUTB_PURE_ONLY and compiles only that part.
// Every number in this file is 示意 (demo timing / layout / reference values), not measured game data.
using System;
using System.Collections.Generic;
#if !TUTB_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>Pure maths of step 6 (testable without System.Drawing). All values 示意.</summary>
    public static class TutorialStep6Math
    {
        /// <summary>Demo loop (ms): D held + two bursts (ring X slides between them), pause, then the same with A.</summary>
        public const int LoopMs = 10800;
        public const int DFrom = 300, DTo = 4700, DFire1From = 900, DFire1To = 1600, DFire2From = 3200, DFire2To = 4300;
        public const int DAdjFrom = 1900, DAdjTo = 2900;
        public const int AFrom = 5400, ATo = 9800, AFire1From = 6000, AFire1To = 6700, AFire2From = 8300, AFire2To = 9400;
        public const int AAdjFrom = 7000, AAdjTo = 8000;
        /// <summary>Live mode: shots per side (key held alone) before the prompt moves on / the step counts as done.</summary>
        public const int ShotsNeeded = 3;
        /// <summary>Minimum lock tolerance (UI px) for a very small ring.</summary>
        public const double MinTolPx = 4;
        /// <summary>Profile.Dirs index of D and A.</summary>
        public const int DirA = 2, DirD = 3;

        /// <summary>Scripted ring X progress of each side at loop time t: 0 = no X offset yet, 1 = the target value.</summary>
        public static void ScriptAdjust(double tMs, out double adjD, out double adjA)
        {
            adjD = Progress(tMs, DAdjFrom, DAdjTo - DAdjFrom);
            adjA = Progress(tMs, AAdjFrom, AAdjTo - AAdjFrom);
        }

        /// <summary>Smoothstep progress of an animation from startMs lasting durMs (0 before, 1 after; NaN = 0).</summary>
        public static double Progress(double tMs, double startMs, double durMs)
        {
            if (double.IsNaN(tMs)) return 0;
            if (!(durMs > 0)) return tMs >= startMs ? 1 : 0;
            return TutorialGeometry.Smooth((tMs - startMs) / durMs);
        }

        /// <summary>Side the scripted demo is on at loop time t: +1 = D, -1 = A, 0 = pause.</summary>
        public static int ScriptSide(double tMs)
        {
            if (tMs >= DFrom && tMs < DTo) return 1;
            if (tMs >= AFrom && tMs < ATo) return -1;
            return 0;
        }

        /// <summary>Which side a shot counts for: +1 = D held alone, -1 = A held alone, 0 = anything else
        /// (W / S also held, both A and D, or leaning). The step wants one key at a time.</summary>
        public static int ShotSide(bool w, bool s, bool a, bool d, int leanTarget)
        {
            if (w || s || leanTarget != 0) return 0;
            if (d && !a) return 1;
            if (a && !d) return -1;
            return 0;
        }

        /// <summary>Lock tolerance (UI px): the ring radius, at least MinTolPx.</summary>
        public static double Tolerance(double ringDiameterUi)
        {
            double t = ringDiameterUi / 2.0;
            if (double.IsNaN(t) || t < MinTolPx) t = MinTolPx;
            return t;
        }

        /// <summary>「扣住弹孔」 on the X axis: |hole centre X - ring X| within the tolerance.</summary>
        public static bool Locked(double holeX, double ringX, double ringDiameterUi)
        {
            double gap = Math.Abs(holeX - ringX);
            if (double.IsNaN(gap)) return false;
            return gap <= Tolerance(ringDiameterUi);
        }

        /// <summary>Live progress: prompt 0 (D) until ShotsNeeded D shots, then prompt 1 (A); done after both.</summary>
        public static int PromptIndex(int dShots, int aShots, out bool done)
        {
            done = dShots >= ShotsNeeded && aShots >= ShotsNeeded;
            return dShots >= ShotsNeeded ? 1 : 0;
        }

        /// <summary>Largest |x| of the given values plus margin, at least minExtent; NaN / infinity count as 0.</summary>
        public static double Extent(double a, double b, double c, double d, double margin, double minExtent)
        {
            double m = 0;
            m = Math.Max(m, Abs0(a)); m = Math.Max(m, Abs0(b)); m = Math.Max(m, Abs0(c)); m = Math.Max(m, Abs0(d));
            m += margin;
            if (m < minExtent) m = minExtent;
            if (m > 600) m = 600;
            return m;
        }

        private static double Abs0(double v) { return double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Abs(v); }

        /// <summary>Rig zoom (scene px per UI px before K) so that ±extentUi spans 'fill' of halfWidthPx. Clamped 0.05..12.</summary>
        public static double FitZoom(double halfWidthPx, double k, double extentUi, double fill)
        {
            if (!(k > 0)) k = 1;
            if (!(halfWidthPx > 0)) return 0.05;
            if (!(fill > 0)) fill = 1;
            if (!(extentUi > 0)) extentUi = 1;
            double z = halfWidthPx * fill / (extentUi * k);
            if (z < 0.05) z = 0.05;
            if (z > 12) z = 12;
            return z;
        }

        /// <summary>Ruler tick step (UI px) so that ±extent shows at most ~maxTicks ticks: 5, 10, 20, 25, 50, 100 ...</summary>
        public static double TickStep(double extentUi, int maxTicks)
        {
            if (maxTicks < 2) maxTicks = 2;
            if (!(extentUi > 0)) return 5;
            double[] steps = TickSteps;
            for (int i = 0; i < steps.Length; i++) if (2 * extentUi / steps[i] <= maxTicks) return steps[i];
            return steps[steps.Length - 1];
        }

        private static readonly double[] TickSteps = new double[] { 5, 10, 20, 25, 50, 100, 200, 250, 500 };
    }

#if !TUTB_PURE_ONLY
    public static class TutorialStep6
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order.</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep6Page());
        }
    }

    /// <summary>
    /// Script step 6 「左右 A / D」. Demo: holes come from a 示意 truth (the developer's reference D / A values, recoil removed so
    /// only the sideways offset shows); the ring uses the user's own values in live mode. In the scripted loop the ring starts
    /// with no X offset, the first burst misses, X slides until the ring covers the holes, the second burst lands inside.
    /// </summary>
    internal sealed class TutorialStep6Page : TutorialStep
    {
        private static readonly TutorialScript Script = new TutorialScript(TutorialStep6Math.LoopMs,
            new ScriptSeg(TutorialStep6Math.DFrom, TutorialStep6Math.DTo, TKey.D),
            new ScriptSeg(TutorialStep6Math.DFire1From, TutorialStep6Math.DFire1To, TKey.Fire),
            new ScriptSeg(TutorialStep6Math.DFire2From, TutorialStep6Math.DFire2To, TKey.Fire),
            new ScriptSeg(TutorialStep6Math.AFrom, TutorialStep6Math.ATo, TKey.A),
            new ScriptSeg(TutorialStep6Math.AFire1From, TutorialStep6Math.AFire1To, TKey.Fire),
            new ScriptSeg(TutorialStep6Math.AFire2From, TutorialStep6Math.AFire2To, TKey.Fire));

        private static readonly TutorialField[] ADFields = MakeFields();

        private static TutorialField[] MakeFields()
        {
            Action<Profile> diag = delegate(Profile p) { Profile.RecomputeDiagonals(p); };
            TutorialField dx = TutorialField.Offset("D：左右", "推荐改", true, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return p.Dirs[TutorialStep6Math.DirD].OffX; },
                delegate(Profile p, double v) { p.Dirs[TutorialStep6Math.DirD].OffX = v; });
            TutorialField ax = TutorialField.Offset("A：左右", "推荐改", true, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return p.Dirs[TutorialStep6Math.DirA].OffX; },
                delegate(Profile p, double v) { p.Dirs[TutorialStep6Math.DirA].OffX = v; });
            TutorialField dy = TutorialField.Offset("D：上下", "上下不推荐改", false, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return p.Dirs[TutorialStep6Math.DirD].OffY; },
                delegate(Profile p, double v) { p.Dirs[TutorialStep6Math.DirD].OffY = v; });
            TutorialField ay = TutorialField.Offset("A：上下", "上下不推荐改", false, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return p.Dirs[TutorialStep6Math.DirA].OffY; },
                delegate(Profile p, double v) { p.Dirs[TutorialStep6Math.DirA].OffY = v; });
            dy.Optional = true; ay.Optional = true;
            dx.After = diag; ax.After = diag; dy.After = diag; ay.After = diag;
            return new TutorialField[] { dx, ax, dy, ay };
        }

        // HUD text (constants: no per-frame strings)
        private const string WallLabel = "10 米";
        private const string NoteBase = "上下不推荐改，除非你觉得合适。\n上下的偏差，最好交给后坐力数值。\n真实数值以游戏里的弹孔为准。";
        private const string TagX = "X 偏移";
        private const string TagHoles = "弹孔";
        private const string TagRingLive = "准星：你的数值";
        private const string TagAdjust = "调 X …";
        private const string TagLocked = "扣住弹孔";
        private const string TagMiss = "还差";
        private const string TagYNo = "上下不推荐改";
        private const string LabelD = "D：左右", LabelA = "A：左右", LabelPx = "px", LabelZero = "0", LabelUntested = "—";
        private const string LabelHoleD = "D 弹孔", LabelHoleA = "A 弹孔", LabelReadout = "X 偏移";

        private readonly FpRig rig = new FpRig();
        private double refDX, refDY, refAX, refAY;   // 示意 truth (developer reference values)
        private double adjD = 1, adjA = 1;
        private double prevT = -1;
        private int dShots, aShots;
        private int side;                            // +1 D held alone, -1 A held alone, 0 otherwise (this frame)
        private int lastSide = 1;                    // side the readout highlights
        private Color[] kindColors;

        // per-frame hole stats (no allocation)
        private int nD, nA;
        private double mDX, mDY, mAX, mAY;

        public TutorialStep6Page()
        {
            Id = "tuneAD";
            Chapter = "实战校准";
            Title = "左右 A / D";
            Body = "单独按住 D，开枪。\n调左右，直到准星扣住弹孔。\n再单独按住 A，同样调左右。";
            DevNote = "按住 D 或 A 时，只调左右，直到准星能扣住弹孔。上下不推荐改，除非你认为合适；竖直方向最好用后坐力数值来概括。";
            Prompts = new string[] { "按住 D 开枪", "按住 A 开枪" };
            Hint = Prompts[0];
            Terminal = new string[] { "> 按住 D  调 X  ……扣住弹孔 完成" };
            FieldNote = NoteBase;
            SettingsPage = TutorialPages.Move;
            Fields = ADFields;
            KeysShown = TKey.A | TKey.D | TKey.Fire;
            ClickFires = true;
        }

        public override void Enter(TutorialContext c)
        {
            Profile reference = Profile.CreateLeoDefault("参考");
            refDX = reference.Dirs[TutorialStep6Math.DirD].OffX; refDY = reference.Dirs[TutorialStep6Math.DirD].OffY;
            refAX = reference.Dirs[TutorialStep6Math.DirA].OffX; refAY = reference.Dirs[TutorialStep6Math.DirA].OffY;
            rig.TargetDistanceM = 10;
            rig.Setup(c, c.P, c.P, false);
            Profile t = rig.TruthP, r = rig.RingP;
            t.Dirs[TutorialStep6Math.DirD].OffX = refDX; t.Dirs[TutorialStep6Math.DirD].OffY = refDY;
            t.Dirs[TutorialStep6Math.DirA].OffX = refAX; t.Dirs[TutorialStep6Math.DirA].OffY = refAY;
            // only the sideways offset shows: no recoil, no jump, no stand offset (示意)
            t.RecoilMaxPx = 0; t.RecoilClickKickPx = 0; r.RecoilMaxPx = 0; r.RecoilClickKickPx = 0;
            t.StandOffsetX = 0; t.StandOffsetY = 0; r.StandOffsetX = 0; r.StandOffsetY = 0;
            if (t.Jump != null) t.Jump.Enabled = false;
            if (r.Jump != null) r.Jump.Enabled = false;
            // the demo strafes with the developer's curve, so both engines move the same way
            t.CurveAD = reference.CurveAD; r.CurveAD = reference.CurveAD.Clone();
            t.CurveWS = reference.CurveWS; r.CurveWS = reference.CurveWS.Clone();
            rig.SetDistanceNow(10);
            rig.WallLabel = WallLabel;
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallWm = 3.4; rig.WallHm = 2.2;
            prevT = -1; dShots = 0; aShots = 0; adjD = 1; adjA = 1; side = 0; lastSide = 1;
            PromptIndex = 0; Done = false; lastScripted = true;
            RefreshFieldNote(c);
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            // D / A values are copied every frame; refresh the ring's look from the profile
            Profile p = c.P, r = rig.RingP;
            RefreshFieldNote(c);
            if (p == null || r == null) return;
            r.Diameter = p.Diameter; r.Thickness = p.Thickness; r.Color = p.Color; r.Color2 = p.Color2;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire; int lit;
            InputState i = c.Input(Script, out fire, out lit);
            LitKeys = lit;
            Profile p = c.P, r = rig.RingP;
            double userDX = 0, userDY = 0, userAX = 0, userAY = 0;
            if (p != null && p.Dirs != null && p.Dirs.Count > TutorialStep6Math.DirD)
            {
                userDX = p.Dirs[TutorialStep6Math.DirD].OffX; userDY = p.Dirs[TutorialStep6Math.DirD].OffY;
                userAX = p.Dirs[TutorialStep6Math.DirA].OffX; userAY = p.Dirs[TutorialStep6Math.DirA].OffY;
            }
            lastScripted = c.Scripted;
            if (c.Scripted)
            {
                double t = Script.Wrap(c.Arbiter.ScriptMs);
                if (t < prevT || c.Arbiter.Resumed) rig.ClearHoles();
                prevT = t;
                TutorialStep6Math.ScriptAdjust(t, out adjD, out adjA);
                // the demo slides X from 0 to the reference value (示意); Y stays as it is (not tuned here)
                r.Dirs[TutorialStep6Math.DirD].OffX = refDX * adjD; r.Dirs[TutorialStep6Math.DirD].OffY = refDY;
                r.Dirs[TutorialStep6Math.DirA].OffX = refAX * adjA; r.Dirs[TutorialStep6Math.DirA].OffY = refAY;
                int ss = TutorialStep6Math.ScriptSide(t);
                if (ss != 0) lastSide = ss;
                if (dShots == 0 && aShots == 0) PromptIndex = ss < 0 ? 1 : 0;
            }
            else
            {
                prevT = -1;
                adjD = 1; adjA = 1;
                r.Dirs[TutorialStep6Math.DirD].OffX = userDX; r.Dirs[TutorialStep6Math.DirD].OffY = userDY;
                r.Dirs[TutorialStep6Math.DirA].OffX = userAX; r.Dirs[TutorialStep6Math.DirA].OffY = userAY;
            }
            side = TutorialStep6Math.ShotSide(i.Fwd, i.Back, i.Left, i.Right, rig.Truth.LeanTarget);
            if (side != 0) lastSide = side;
            rig.NextHoleKind = side > 0 ? 0 : (side < 0 ? 1 : 2); // 2 = not counted (muted)
            rig.Update(dtMs, i, fire);

            if (!c.Scripted && rig.ShotsThisFrame > 0 && side != 0)
            {
                if (side > 0) dShots += rig.ShotsThisFrame; else aShots += rig.ShotsThisFrame;
                bool done;
                PromptIndex = TutorialStep6Math.PromptIndex(dShots, aShots, out done);
                if (done) Done = true;
            }
        }

        private void HoleStats()
        {
            nD = 0; nA = 0; mDX = mDY = mAX = mAY = 0;
            double d = rig.DistanceM;
            for (int k = 0; k < rig.HoleCount; k++)
            {
                TutorialHole h = rig.Holes[k];
                if (h.AgeMs >= rig.HoleFadeMs) continue;
                double x = TutorialGeometry.WallToScreen(h.WX, d), y = TutorialGeometry.WallToScreen(h.WY, d);
                if (h.Kind == 0) { nD++; mDX += x; mDY += y; }
                else if (h.Kind == 1) { nA++; mAX += x; mAY += y; }
            }
            if (nD > 0) { mDX /= nD; mDY /= nD; }
            if (nA > 0) { mAX /= nA; mAY /= nA; }
        }

        // hole / key colours: the shared per-key map (TutorialPalette.KeyD / KeyA, review B6), never the ring's Neon

        private float readoutBottom = -1;

        /// <summary>Scene smaller than this (logical px) = compact HUD: core picture + one tag (review C-size note).</summary>
        private const float CompactW = 700, CompactH = 330;

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            curG = c.G;
            TutorialPalette pal = gx.Pal;
            if (kindColors == null) kindColors = new Color[] { pal.KeyD, pal.KeyA, pal.Muted };
            rig.KindColors = kindColors;
            Profile rp = rig.RingP;
            double ringDX = rp.Dirs[TutorialStep6Math.DirD].OffX, ringAX = rp.Dirs[TutorialStep6Math.DirA].OffX;
            Profile p = c.P;
            double uD = p == null ? 0 : p.Dirs[TutorialStep6Math.DirD].OffX, uA = p == null ? 0 : p.Dirs[TutorialStep6Math.DirA].OffX;
            double ext = TutorialStep6Math.Extent(refDX, refAX, uD, uA, 18, 40);
            bool compact = scene.Width / gx.K < CompactW || scene.Height / gx.K < CompactH;
            rig.Zoom = TutorialStep6Math.FitZoom(scene.Width / 2f, gx.K, ext, 0.74);
            // wall sized to the scene (示意): its edges stay visible at any zoom
            double pxPerM = TutorialGeometry.ProjectedPx(1, rig.DistanceM) * rig.Zoom * gx.K;
            if (pxPerM > 0)
            {
                // keep the wall's top edge below last frame's readout panel (no HUD over the wall corner)
                float cyS = scene.Top + scene.Height / 2f;
                double wallH = scene.Height * 0.56;
                if (!compact && readoutBottom > scene.Top) wallH = Math.Min(wallH, Math.Max(scene.Height * 0.36, 2 * (cyS - readoutBottom - gx.S(8))));
                rig.WallWm = scene.Width * 0.84 / pxPerM; rig.WallHm = wallH / pxPerM;
            }
            rig.ShowGun = true;   // shared gun (bottom centre-right); ruler and labels sit above it
            rig.Draw(gx, scene);

            Graphics g = gx.G;
            g.SetClip(scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);
            HoleStats();
            // ruler between the holes (around the centre) and the lowered gun
            RulerY = cy + scene.Height * 0.17f;
            double ringD = rp.Diameter;
            bool scripted = c.Scripted;
            bool adjusting = scripted && ((adjD > 0.01 && adjD < 0.99) || (adjA > 0.01 && adjA < 0.99));


            // ghost of the untuned ring (no X offset) while the demo slides X
            if (scripted && side != 0)
            {
                double a = side > 0 ? adjD : adjA;
                if (a < 0.99)
                {
                    float gy = (float)(cy - rig.RingY * z);
                    float gr = Math.Max(gx.S(8), (float)(ringD * z / 2));
                    g.DrawEllipse(gx.Dashed(TutorialGfx.A(pal.Muted, 170), 1.2f), cx - gr, gy - gr, 2 * gr, 2 * gr);
                    float rx = (float)(cx + rig.RingX * z);
                    if (Math.Abs(rx - cx) > gx.S(12)) gx.Arrow(TutorialGfx.A(pal.Neon, 210), 1.4f, cx, gy, rx, gy);
                }
            }

            // hole centres + X gap to the ring's X for that side
            if (nD >= 2) DrawSide(gx, scene, cx, cy, z, mDX, mDY, ringDX, ringD, kindColors[0], LabelHoleD, side > 0);
            if (nA >= 2) DrawSide(gx, scene, cx, cy, z, mAX, mAY, ringAX, ringD, kindColors[1], LabelHoleA, side < 0);

            // ring glow when the active side is locked
            if (side != 0)
            {
                bool any = side > 0 ? nD >= 2 : nA >= 2;
                double hx = side > 0 ? mDX : mAX;
                double rxUi = side > 0 ? ringDX : ringAX;
                if (any && TutorialStep6Math.Locked(hx, rxUi, ringD))
                {
                    float rx = (float)(cx + rig.RingX * z), ry = (float)(cy - rig.RingY * z);
                    double pulse = 0.5 + 0.5 * Math.Sin(gx.TimeMs / 180.0);
                    float rr = Math.Max(gx.S(10), (float)(ringD * z / 2)) + gx.S(5) + gx.S(3) * (float)pulse;
                    gx.GlowCircle(TutorialGfx.A(pal.Neon, 120 + (int)(100 * pulse)), 1.2f, rx, ry, rr);
                }
            }

            DrawRuler(gx, scene, cx, z, ext, ringDX, ringAX, compact);

            // HUD: tags top-left
            float pad = gx.S(10);
            // v4.1: max 2 tags per view (the result pill + 「X 偏移」); these are plain legend lines
            RectangleF t1 = compact ? new RectangleF(scene.Left + pad, scene.Top + pad, 0, 0) // small scene: it covered 「D 弹孔」
                : gx.Legend(TagHoles, scene.Left + pad, scene.Top + pad, pal.Hot, TutorialGfx.MarkDot, 0);
            float tagBottom = t1.Bottom;
            if (!scripted && !compact) tagBottom = gx.Legend(TagRingLive, scene.Left + pad, t1.Bottom + gx.S(3), pal.Neon, TutorialGfx.MarkRing, 0).Bottom;
            if (adjusting && !compact) gx.Legend(TagAdjust, cx, scene.Top + pad, pal.Neon, TutorialGfx.MarkDot, 1);

            // readout panel top-right (D：左右 / A：左右, 上下不推荐改) + key caps
            if (!compact) DrawReadout(gx, scene, ringDX, ringAX, tagBottom);
            g.ResetClip();
        }

        private void DrawSide(TutorialGfx gx, RectangleF scene, float cx, float cy, float z, double hx, double hy, double ringX,
            double ringD, Color col, string label, bool active)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            float x = (float)(cx + hx * z), y = (float)(cy - hy * z);
            float a = gx.S(7);
            Pen pen = gx.P(TutorialGfx.A(col, active ? 240 : 150), 1.2f);
            g.DrawLine(pen, x - a, y, x + a, y);
            g.DrawLine(pen, x, y - a, x, y + a);
            g.DrawEllipse(gx.P(TutorialGfx.A(col, active ? 130 : 70), 1f), x - a * 1.6f, y - a * 1.6f, a * 3.2f, a * 3.2f);
            // vertical guide from the hole centre down to the ruler
            if (RulerY > y + a * 2) g.DrawLine(gx.Dashed(TutorialGfx.A(col, active ? 110 : 55), 1f), x, y + a * 1.8f, x, RulerY);
            bool ok = TutorialStep6Math.Locked(hx, ringX, ringD);
            // label block above the hole centre, clear of the ring (which may sit right on the hole); sizes measured
            SizeF ls = M(gx, label, gx.F.Tag), ts = M(gx, TagLocked, gx.F.Tag);
            float tagH = ts.Height + gx.S(5);
            float ringR = Math.Max(gx.S(8), (float)(ringD * z / 2));
            float blockH = ls.Height + gx.S(3) + tagH;
            float top = y - Math.Max(a * 1.6f, ringR + gx.S(4)) - gx.S(4) - blockH;
            if (top < scene.Top + gx.S(4)) top = scene.Top + gx.S(4);
            float half = Math.Max(ls.Width, ts.Width + gx.S(12)) / 2 + gx.S(2);
            float lx = Clamp(x, scene.Left + half + gx.S(4), scene.Right - half - gx.S(4));
            gx.Text(label, gx.F.Tag, col, lx, top, 1);
            float tagY = top + ls.Height + gx.S(3);
            if (ok)
            {
                if (active) gx.Tag(TagLocked, lx, tagY, pal.Neon, 1);
            }
            else if (active)
            {
                // horizontal X gap: ring X -> hole X, on the hole's row
                float rx = (float)(cx + ringX * z);
                g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 200), 1.2f), rx, y, x, y);
                string n = missPct.Of(Math.Abs(hx - ringX), curG);
                ValuePill(gx, scene, TagMiss, n, lx, tagY, pal.Hot, 1);
            }
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (hi < lo) return (lo + hi) / 2;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>One pill holding 「label  value px」 (value inside the border), anchored like Text (align 0/1/2) and kept
        /// inside the scene. Returns its rectangle.</summary>
        private RectangleF ValuePill(TutorialGfx gx, RectangleF scene, string label, string value, float x, float y, Color col, int align)
        {
            TutorialPalette pal = gx.Pal;
            SizeF sl = M(gx, label, gx.F.Tag), sv = M(gx, value, gx.F.MonoSmall), sp = SizeF.Empty; // v4.1: the value carries its % sign
            float padX = gx.S(6), padY = gx.S(2.5f), gap = gx.S(6), gap2 = 0;
            float h = Math.Max(sl.Height, sv.Height) + 2 * padY;
            float w = padX + sl.Width + gap + sv.Width + gap2 + sp.Width + padX;
            float left = align == 1 ? x - w / 2 : (align == 2 ? x - w : x);
            left = Clamp(left, scene.Left + gx.S(4), scene.Right - gx.S(4) - w);
            RectangleF r = new RectangleF(left, y, w, h);
            gx.G.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 215)), r);
            gx.G.DrawRectangle(gx.P(TutorialGfx.A(col, 200), 1f), r.X, r.Y, r.Width, r.Height);
            float mid = r.Top + h / 2;
            gx.TextMid(label, gx.F.Tag, col, r.Left + padX, mid, 0);
            float vx = r.Left + padX + sl.Width + gap;
            gx.TextMid(value, gx.F.MonoSmall, pal.Text, vx, mid, 0);
            gx.NoteTag(r, label);   // a pill: counted in the per-view tag budget (max 2)
            return r;
        }

        private float RulerY;
        // v4.1 百分比偏移 labels (rebuilt only when the shown value changes) + the settings they convert with
        private GlobalSettings curG;
        private readonly TutorialPctLabel missPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Magnitude);
        private readonly TutorialPctLabel xPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel rowDPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel rowAPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);

        // MeasureString results by (string reference, font): numbers come from TutorialNum's cached strings, so a few slots
        // cover every label without measuring (or allocating) each frame. Fonts are recreated on resize (new references).
        private const int MeasureSlots = 24;
        private readonly string[] mStr = new string[MeasureSlots];
        private readonly Font[] mFont = new Font[MeasureSlots];
        private readonly SizeF[] mSize = new SizeF[MeasureSlots];
        private int mNext;

        private SizeF M(TutorialGfx gx, string str, Font f)
        {
            for (int i = 0; i < MeasureSlots; i++)
                if (ReferenceEquals(mStr[i], str) && ReferenceEquals(mFont[i], f)) return mSize[i];
            SizeF sz = gx.Measure(str, f);
            mStr[mNext] = str; mFont[mNext] = f; mSize[mNext] = sz;
            mNext = (mNext + 1) % MeasureSlots;
            return sz;
        }

        private void DrawRuler(TutorialGfx gx, RectangleF scene, float cx, float z, double ext, double ringDX, double ringAX, bool compact)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            float y = RulerY;
            float half = (float)(ext * z);
            float x0 = Math.Max(scene.Left + gx.S(12), cx - half), x1 = Math.Min(scene.Right - gx.S(12), cx + half);
            SizeF num = M(gx, LabelZero, gx.F.MonoSmall);
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 190)), x0 - gx.S(8), y - gx.S(10), x1 - x0 + gx.S(16), gx.S(14) + num.Height);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 110), 1f), x0, y, x1, y);
            double step = TutorialStep6Math.TickStep(ext, compact ? 8 : 16);
            int n = (int)Math.Floor(ext / step);
            for (int i = -n; i <= n; i++)
            {
                double v = i * step;
                float x = (float)(cx + v * z);
                if (x < x0 - 0.5f || x > x1 + 0.5f) continue;
                bool major = i % 2 == 0;
                float h = major ? gx.S(6) : gx.S(3);
                g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, major ? 150 : 80), 1f), x, y - h, x, y);
                if (major && i == 0) gx.Text(LabelZero, gx.F.MonoSmall, TutorialGfx.A(pal.Muted, 230), x, y + gx.S(2), 1);
            }
            // active side: neon bar 0 -> ring X, marker on the ring X
            int s = side != 0 ? side : lastSide;
            double rx = s > 0 ? ringDX : ringAX;
            float xr = (float)(cx + rx * z);
            if (xr < x0) xr = x0; if (xr > x1) xr = x1;
            gx.GlowLine(pal.Neon, 2f, cx, y, xr, y);
            PointF[] tri = gx.Pts(3);
            float tsz = gx.S(5);
            tri[0] = new PointF(xr, y - gx.S(2)); tri[1] = new PointF(xr - tsz, y - gx.S(2) - tsz * 1.4f); tri[2] = new PointF(xr + tsz, y - gx.S(2) - tsz * 1.4f);
            g.FillPolygon(gx.B(pal.Neon), tri);
            // 「X 偏移 · 示意  +75 px」 pill above the marker, value inside the pill, kept inside the scene (review B16)
            SizeF th = M(gx, TagX, gx.F.Tag);
            float tagY = y - gx.S(10) - th.Height - gx.S(5) - tsz * 1.4f;
            ValuePill(gx, scene, TagX, xPct.Of(rx, curG), xr, tagY, pal.Neon, xr >= cx ? 0 : 2);
        }

        private void DrawReadout(TutorialGfx gx, RectangleF scene, double ringDX, double ringAX, float minTop)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            float pad = gx.S(10);
            SizeF sh = M(gx, LabelReadout, gx.F.MonoSmall), sl = M(gx, LabelD, gx.F.Small), sv = M(gx, ValueWide, gx.F.Big);
            SizeF sp = SizeF.Empty, sy = M(gx, TagYNo, gx.F.Small);
            float cap = Math.Max(gx.S(20), sh.Height + gx.S(4));
            float headW = sh.Width + gx.S(12) + 2 * (cap + gx.S(4)) + cap * 0.8f;
            float rowW = sl.Width + gx.S(14) + sv.Width + gx.S(4) + sp.Width;
            float w = Math.Max(Math.Max(headW, rowW), sy.Width) + 2 * pad;
            float rowH = Math.Max(sl.Height, sv.Height) + gx.S(6);
            float h = pad * 0.6f + cap + gx.S(8) + 2 * rowH + gx.S(6) + sy.Height + pad * 0.8f;
            float x = scene.Right - gx.S(10) - w, y = scene.Top + gx.S(10);
            if (x < scene.Left + scene.Width * 0.45f) { x = scene.Left + gx.S(10); y = minTop + gx.S(8); } // narrow: below the tags
            RectangleF r = new RectangleF(x, y, w, h);
            gx.RegisterView(r, true);
            readoutBottom = x > scene.Left + scene.Width * 0.45f ? r.Bottom : -1;
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 225)), r);
            g.DrawRectangle(gx.P(TutorialGfx.A(pal.Neon, 60), 1f), r.X, r.Y, r.Width, r.Height);
            gx.HudFrame(r, pal.Neon, 9);
            float top = r.Top + pad * 0.6f;
            gx.TextMid(LabelReadout, gx.F.MonoSmall, TutorialGfx.A(pal.Neon, 220), r.Left + pad, top + cap / 2, 0);
            // key caps A / D (lit while held) + a small mouse (lit while firing)
            float kx = r.Right - pad - cap * 0.8f - 2 * (cap + gx.S(4));
            gx.KeyCap(kx, top, cap, "A", (LitKeys & TKey.A) != 0, lastSide < 0);
            gx.KeyCap(kx + cap + gx.S(4), top, cap, "D", (LitKeys & TKey.D) != 0, lastSide > 0);
            DrawMouse(gx, kx + 2 * (cap + gx.S(4)), top, cap, (LitKeys & TKey.Fire) != 0);
            float row = top + cap + gx.S(8);
            DrawRow(gx, r, pad, row, rowH, LabelD, ringDX, pal.KeyD, lastSide > 0, Tested(1));
            DrawRow(gx, r, pad, row + rowH, rowH, LabelA, ringAX, pal.KeyA, lastSide < 0, Tested(-1));
            gx.Text(TagYNo, gx.F.Small, pal.Muted, r.Left + pad, row + 2 * rowH + gx.S(6), 0);
        }

        /// <summary>A side shows its X only once it was tried: scripted = its X slide has started, live = a shot with that key
        /// held alone. Until then the readout shows 「—」 (not a misleading 0; the real value is in the field note 当前：).</summary>
        private bool Tested(int s)
        {
            if (lastScripted) return s > 0 ? adjD > 0.01 : adjA > 0.01;
            return s > 0 ? dShots > 0 : aShots > 0;
        }

        private bool lastScripted = true;

        /// <summary>FieldNote with the profile's current D / A X (built on enter / field edit only, never per frame).</summary>
        private void RefreshFieldNote(TutorialContext c)
        {
            Profile p = c.P;
            string cur = p == null || p.Dirs == null || p.Dirs.Count <= TutorialStep6Math.DirD ? "" :
                "当前：D " + TutorialPct.X(p.Dirs[TutorialStep6Math.DirD].OffX, c.G) + "　A " + TutorialPct.X(p.Dirs[TutorialStep6Math.DirA].OffX, c.G) + "\n";
            FieldNote = cur + NoteBase;
        }

        /// <summary>Widest value the readout must hold (for its width).</summary>
        private const string ValueWide = "← 00.00%";

        private void DrawRow(TutorialGfx gx, RectangleF r, float pad, float y, float h, string label, double v, Color col, bool active, bool tested)
        {
            TutorialPalette pal = gx.Pal;
            if (active)
            {
                gx.G.FillRectangle(gx.B(TutorialGfx.A(col, 34)), r.Left + gx.S(3), y, r.Width - gx.S(6), h - gx.S(2));
                gx.G.FillRectangle(gx.B(col), r.Left + gx.S(3), y, gx.S(2.5f), h - gx.S(2));
            }
            float mid = y + (h - gx.S(2)) / 2;
            gx.TextMid(label, gx.F.Small, active ? col : pal.Text, r.Left + pad, mid, 0);
            float right = r.Right - pad;
            string vs = tested ? ((object)label == (object)LabelD ? rowDPct : rowAPct).Of(v, curG) : LabelUntested;
            gx.TextMid(vs, gx.F.Big, active ? pal.Text : TutorialGfx.A(pal.Text, 200), right, mid, 2);
        }

        private static void DrawMouse(TutorialGfx gx, float x, float y, float cap, bool on)
        {
            TutorialPalette pal = gx.Pal;
            RectangleF body = new RectangleF(x, y - gx.S(1), cap * 0.8f, cap * 1.1f);
            gx.G.FillEllipse(gx.B(TutorialGfx.A(pal.Surface, 235)), body);
            gx.G.FillPie(gx.B(on ? pal.Hot : TutorialGfx.A(pal.Border, 160)), body.X, body.Y, body.Width, body.Width, 180, 90);
            gx.G.DrawEllipse(gx.P(on ? pal.Hot : pal.Border, 1.1f), body);
            gx.G.DrawLine(gx.P(pal.Border, 1f), body.X + body.Width / 2, body.Y, body.X + body.Width / 2, body.Y + body.Width / 2);
        }
    }
#endif
}
