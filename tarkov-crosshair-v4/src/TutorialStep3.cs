// TutorialStep3.cs -- 首次设置教程 script step 3 (后坐力上抬). System.Drawing only (no WinForms), C# 5, no per-frame allocation.
// Owner: step worker 3. The pure maths (TutorialStep3Math) compiles on its own: tests-v4 defines STEP3_PURE_ONLY and
// compiles only that part (tests in tests-v4/TestsV4Step3.cs).
//
// Demo (示意): the wall stays at 10 m. The bullets go where the 示意 gun points: the hipfire parallax (bottom-right at
// 10 m) plus the 示意 reference rise (Leo's default 上抬偏移量). The first shot lands bottom-right, the following shots
// jump up and keep spraying top-right. A vertical ruler measures the jump (上跳幅度 · 示意); a small Y/time plot shows the
// rise of the bullets (reference) and of the ring (the user's own 上抬偏移量). Only Y matters here; X is left for later.
using System;
using System.Collections.Generic;
#if !STEP3_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>
    /// Pure maths of the 后坐力上抬 step (System only, unit-tested). Every number here is 示意 (demo timing / layout),
    /// not measured game data.
    /// </summary>
    public static class TutorialStep3Math
    {
        /// <summary>Demo loop (ms): fire held FireFrom..FireTo, then the result stays on screen until the loop restarts.</summary>
        public const int LoopMs = 7600, FireFrom = 700, FireTo = 3900;
        /// <summary>Wall distance of this step (script: 「墙固定在 10 米」).</summary>
        public const double DistanceM = 10;
        /// <summary>Shots in one live burst (fire held) before the step counts as done (and the rise must be sustained).</summary>
        public const int ShotsForDone = 8;
        /// <summary>A shot counts as 「连续 · 右上」 once the bullets' rise is at least this share of the full rise.</summary>
        public const double SprayShare = 0.5;
        /// <summary>The rise counts as sustained (reached its full value) at this share.</summary>
        public const double SustainShare = 0.85;
        /// <summary>Plot: samples kept and sample period (ms) = PlotSamples * PlotPeriodMs of history.</summary>
        public const int PlotSamples = 150;
        public const double PlotPeriodMs = 28;

        /// <summary>Hole kind of a shot: 0 = 首发 (still bottom-right), 1 = 连续 (risen, top-right).</summary>
        public static int HoleKind(double rise, double fullRise)
        {
            if (!(fullRise > 0) || double.IsNaN(rise)) return 0;
            return rise >= fullRise * SprayShare ? 1 : 0;
        }

        /// <summary>True when the rise has (nearly) reached its full value.</summary>
        public static bool Sustained(double rise, double fullRise)
        {
            if (!(fullRise > 0) || double.IsNaN(rise)) return false;
            return rise >= fullRise * SustainShare;
        }

        /// <summary>Live 「完成」: one burst with at least ShotsForDone shots whose rise got sustained.</summary>
        public static bool BurstDone(int burstShots, bool sustainedSeen)
        {
            return burstShots >= ShotsForDone && sustainedSeen;
        }

        /// <summary>Largest UI extent the scene must show: the first-shot impact (parallax), the risen impact, the ring's
        /// full rise (+ its radius), plus margin. NaN / infinity count as 0.</summary>
        public static double Extent(double parX, double parY, double truthRise, double ringRise, double ringDiameter, double margin)
        {
            double m = 0;
            m = Math.Max(m, Fin(Math.Abs(parX)));
            m = Math.Max(m, Fin(Math.Abs(parY)));
            m = Math.Max(m, Fin(Math.Abs(parY + Math.Max(0, truthRise))));
            m = Math.Max(m, Fin(Math.Max(0, ringRise)) + Fin(Math.Abs(ringDiameter)) / 2.0);
            return m + Fin(margin);
        }

        private static double Fin(double v) { return (double.IsNaN(v) || double.IsInfinity(v)) ? 0 : v; }

        /// <summary>Scene px per UI px so that extentUi (at least minExtentUi) fills 'fill' of halfPx; k = DPI scale.
        /// Returns the rig Zoom (before K), clamped to 0.05 .. 40.</summary>
        public static double FitZoom(double halfPx, double k, double extentUi, double minExtentUi, double fill)
        {
            if (!(k > 0)) k = 1;
            if (!(halfPx > 0)) return 0.05;
            if (!(fill > 0)) fill = 1;
            double e = Fin(Math.Abs(extentUi));
            if (e < minExtentUi) e = minExtentUi;
            if (!(e > 0)) e = 1;
            double z = halfPx * fill / (e * k);
            if (z < 0.05) z = 0.05;
            if (z > 40) z = 40;
            return z;
        }

        /// <summary>Plot y (pixels, y down) of a value in 0..maxValue inside [top, top + height]; clamped to the box.</summary>
        public static float PlotY(double value, double maxValue, float top, float height)
        {
            if (!(maxValue > 0)) maxValue = 1;
            double u = Fin(value) / maxValue;
            if (u < 0) u = 0;
            if (u > 1) u = 1;
            return (float)(top + height - u * height);
        }

        /// <summary>Upper end of the plot's value axis: the larger rise + 15 %, at least 10 px.</summary>
        public static double PlotMax(double truthRise, double ringRise)
        {
            double m = Math.Max(Fin(truthRise), Fin(ringRise)) * 1.15;
            return m < 10 ? 10 : m;
        }

        /// <summary>Compact scene: below 640 x 330 logical px (scene px / layout scale k) only the core picture and the wall
        /// tag are drawn (no tag column, readout or plot), so nothing piles up in a small window or at high DPI.</summary>
        public static bool Compact(double sceneW, double sceneH, double k)
        {
            if (!(k > 0)) k = 1;
            if (double.IsNaN(sceneW) || double.IsNaN(sceneH)) return true;
            return sceneW / k < 640 || sceneH / k < 330;
        }

        /// <summary>The plot (w x h px) fits bottom-left: at most 40 % of the scene width and 38 % of its height.</summary>
        public static bool PlotFits(double w, double h, double sceneW, double sceneH)
        {
            if (!(w > 0) || !(h > 0) || !(sceneW > 0) || !(sceneH > 0)) return false;
            return w <= sceneW * 0.40 && h <= sceneH * 0.38;
        }

        /// <summary>Smoothstep 0..1.</summary>
        public static double Smooth(double u)
        {
            if (double.IsNaN(u) || u <= 0) return 0;
            if (u >= 1) return 1;
            return u * u * (3 - 2 * u);
        }

        /// <summary>Ruler grow-in after the burst starts (0..1 over 400 ms).</summary>
        public static double RulerIn(double msSinceBurst)
        {
            return Smooth(msSinceBurst / 400.0);
        }
    }

#if !STEP3_PURE_ONLY
    public static class TutorialStep3
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order.</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep3Page());
        }
    }

    /// <summary>Script step 3 「后坐力上抬」.</summary>
    internal sealed class TutorialStep3Page : TutorialStep
    {
        private static readonly TutorialScript Script = new TutorialScript(TutorialStep3Math.LoopMs,
            new ScriptSeg(TutorialStep3Math.FireFrom, TutorialStep3Math.FireTo, TKey.Fire));

        private static readonly TutorialField[] RiseFields = new TutorialField[]
        {
            TutorialField.Offset("上抬偏移量", "推荐改", true, OffsetAxis.Y, 0, 1000,
                delegate(Profile p) { return p.RecoilMaxPx; }, delegate(Profile p, double v) { p.RecoilMaxPx = v; }),
        };

        // HUD text (constants: no per-frame strings)
        private const string WallLabel = "10 米 · 固定";
        private const string TagCentre = "屏幕中心";
        private const string TagFirst = "首发 · 右下";
        private const string TagSpray = "连续 · 右上";
        private const string TagRuler = "上跳幅度";
        // v4.1: a view shows at most 2 tags (the wall tag + the ruler); the rest is a plain legend
        private const string LegHoles = "弹孔：参考数值";
        private const string LegRing = "准星：你的上抬偏移量";
        private const string ReadTitle = "后坐力 · Y";
        private const string ReadRise = "上抬";
        private const string ReadRing = "你的";
        private const string ReadSample = "↑ 00.00%";
        private const string ReadX = "X   先不管";
        private const string PlotTitle = "Y 上抬 / 时间";
        private const string PlotRef = "参考";
        private const string PlotYou = "你的";
        private const string Approx = "≈";

        private readonly FpRig rig = new FpRig();
        private double refRise;               // 示意 truth: Leo's default 上抬偏移量
        private double parX, parY;            // hipfire parallax at 10 m (UI px)
        private double prevT = -1;
        private bool prevFire;
        private int burstShots;
        private bool burstSustained;
        private double burstMs;               // ms since the current burst started (or since the last one)
        private double burstMaxRise;          // largest bullet rise in the last burst (ruler)
        private int nFirst, nSpray;           // holes of each kind still visible
        private bool reportedLive;
        private Color[] kindColors;
        // 百分比偏移 labels (v4.1), rebuilt only when the shown value changes
        private readonly TutorialPctLabel rulerPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel truthPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel ringPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);

        // Y/time plot ring buffer
        private readonly double[] plotTruth = new double[TutorialStep3Math.PlotSamples];
        private readonly double[] plotRing = new double[TutorialStep3Math.PlotSamples];
        private int plotHead;
        private double plotAcc;
        private readonly PointF[] plotPtsA = new PointF[TutorialStep3Math.PlotSamples];
        private readonly PointF[] plotPtsB = new PointF[TutorialStep3Math.PlotSamples];

        public TutorialStep3Page()
        {
            Id = "recoil";
            Chapter = "原理";
            Title = "后坐力上抬";
            Body = "开枪后，准星会往上抬。\n所以要加一个向上的偏移量。\n只量竖直方向，左右先不管。";
            DevNote = "腰射开枪后准星会上抬，所以要给准星加一个向上的移动量来矫正。连续开枪时，落点会从右下方跳到右上方，然后一直在右上方扫射。你只需要量出这段竖向上跳有多少，左右先不用管，后面在移动里调。\n"
                    + "每把枪不需要单独改：偏移量是固定的，只有后坐力不同。我实测 50 到 100 后坐力用同一套方案都可以，近距离差别不大。想给每把枪单独设方案，也可以。";
            Prompts = new string[] { "按住{开枪键}，连续开枪" };
            Hint = Prompts[0];
            Terminal = new string[] { "> 连续开枪：右下 → 右上", "> 只量 Y，X 留到后面" };
            FieldNote = "当前：{当前上抬}\n可以先跳过，之后在「后坐力」页再填。";
            SettingsPage = TutorialPages.Recoil;
            Fields = RiseFields;
            KeysShown = TKey.Fire;
            ClickFires = true;
        }

        public override void Enter(TutorialContext c)
        {
            Profile reference = Profile.CreateLeoDefault("参考");
            refRise = reference.RecoilMaxPx;
            rig.TargetDistanceM = TutorialStep3Math.DistanceM;
            rig.Setup(c, reference, c.P, false);
            Profile t = rig.TruthP, r = rig.RingP;
            // only the rise shows: no recoil drift (X), no jump; movement / lean keys are not fed (see Update)
            t.RecoilDriftX = 0; r.RecoilDriftX = 0;
            if (t.Jump != null) t.Jump.Enabled = false;
            if (r.Jump != null) r.Jump.Enabled = false;
            rig.SetDistanceNow(TutorialStep3Math.DistanceM);
            rig.WallLabel = null;   // the wall fills the view: its distance is the HUD tag (top-left)
            rig.UseParallax = true;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallWm = 1.6; rig.WallHm = 1.2;
            rig.SpreadPx = 1.6;
            TutorialGeometry.HipImpactPx(TutorialStep3Math.DistanceM, out parX, out parY);
            prevT = -1; prevFire = false; burstShots = 0; burstSustained = false; burstMs = 1e9; burstMaxRise = 0;
            reportedLive = false;
            ClearPlot();
            PromptIndex = 0; Done = false;
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            Profile p = c.P, r = rig.RingP;
            if (p == null || r == null) return;
            r.RecoilMaxPx = p.RecoilMaxPx;
            r.RecoilRiseMs = p.RecoilRiseMs; r.RecoilClickKickPx = p.RecoilClickKickPx; r.RecoilFallMs = p.RecoilFallMs;
            r.RecoilDelayMs = p.RecoilDelayMs;
            r.Diameter = p.Diameter; r.Thickness = p.Thickness; r.Color = p.Color; r.Color2 = p.Color2;
        }

        private void ClearPlot()
        {
            for (int i = 0; i < plotTruth.Length; i++) { plotTruth[i] = 0; plotRing[i] = 0; }
            plotHead = 0; plotAcc = 0;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire; int lit;
            InputState i = c.Input(Script, out fire, out lit);
            // this step is about the rise only: movement, lean, jump and hand keys do not move the demo
            i.Fwd = false; i.Back = false; i.Left = false; i.Right = false;
            i.LeanL = false; i.LeanR = false; i.Jump = false; i.HandSwitch = false;
            LitKeys = lit & TKey.Fire;

            if (c.Scripted)
            {
                double t = Script.Wrap(c.Arbiter.ScriptMs);
                if (t < prevT || c.Arbiter.Resumed) { rig.ClearHoles(); ClearPlot(); burstMaxRise = 0; burstMs = 1e9; }
                prevT = t;
            }
            else prevT = -1;

            // keep the ring's look in step with the profile (cheap field copies, no allocation)
            Profile p = c.P, r = rig.RingP;
            if (p != null && r != null) r.RecoilMaxPx = p.RecoilMaxPx;

            bool press = fire && !prevFire;
            if (press) { burstShots = 0; burstSustained = false; burstMs = 0; burstMaxRise = 0; }
            rig.NextHoleKind = TutorialStep3Math.HoleKind(rig.Truth.Recoil, refRise);
            rig.Update(dtMs, i, fire);
            burstMs += dtMs;
            if (burstMs > 1e9) burstMs = 1e9;
            double rise = rig.Truth.Recoil;
            if (fire)
            {
                burstShots += rig.ShotsThisFrame;
                if (rise > burstMaxRise) burstMaxRise = rise;
                if (TutorialStep3Math.Sustained(rise, refRise)) burstSustained = true;
            }
            prevFire = fire;

            if (!c.Scripted && !reportedLive && TutorialStep3Math.BurstDone(burstShots, burstSustained))
            {
                reportedLive = true;
                Done = true;
                c.Term.Push("> 实测：上跳 ≈ " + TutorialPct.Y(burstMaxRise, c.G)); // once per visit
            }

            // plot samples (fixed period, ring buffer)
            plotAcc += dtMs;
            int guard = 0;
            while (plotAcc >= TutorialStep3Math.PlotPeriodMs && guard++ < 8)
            {
                plotAcc -= TutorialStep3Math.PlotPeriodMs;
                plotTruth[plotHead] = rise;
                plotRing[plotHead] = rig.Ring.Recoil;
                plotHead = (plotHead + 1) % plotTruth.Length;
            }
            if (plotAcc > TutorialStep3Math.PlotPeriodMs) plotAcc = 0;

            // visible holes by kind
            nFirst = 0; nSpray = 0;
            for (int k = 0; k < rig.HoleCount; k++)
            {
                if (rig.Holes[k].AgeMs >= rig.HoleFadeMs) continue;
                if (rig.Holes[k].Kind == 1) nSpray++; else nFirst++;
            }
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            TutorialPalette pal = gx.Pal;
            if (kindColors == null) kindColors = new Color[] { pal.Cyan, pal.Hot };
            rig.KindColors = kindColors;
            Profile p = c.P;
            double ringRise = p == null ? 0 : p.RecoilMaxPx;
            double ringD = rig.RingP != null ? rig.RingP.Diameter : 12;
            double ext = TutorialStep3Math.Extent(parX, parY, refRise, ringRise, ringD, 10);
            float half = Math.Min(scene.Width * 0.5f, scene.Height * 0.5f);
            rig.Zoom = TutorialStep3Math.FitZoom(half, gx.K, ext, 30, 0.58);
            rig.Draw(gx, scene);

            Graphics g = gx.G;
            g.SetClip(scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);
            float pad = gx.S(10);

            // screen-centre label
            float ringR = (float)(Math.Max(gx.S(16), ringD * z) / 2f);
            if (!TutorialStep3Math.Compact(scene.Width, scene.Height, gx.K)) // small scene: it sat on 首发 / 连续 (v4.1)
                gx.TextMid(TagCentre, gx.F.Small, TutorialGfx.A(pal.Text, 200), cx - Math.Max(gx.S(10), ringR + gx.S(8)), cy, 2);

            // first-shot level and risen level (bullets, 示意)
            float yFirst = (float)(cy - parY * z);
            float xHit = (float)(cx + parX * z);
            float yTop = (float)(cy - (parY + burstMaxRise) * z);
            if (nFirst > 0)
            {
                gx.G.DrawLine(gx.Dashed(TutorialGfx.A(pal.Cyan, 110), 1f), xHit - gx.S(26), yFirst, xHit + gx.S(26), yFirst);
                gx.Text(TagFirst, gx.F.Tag, pal.Cyan, xHit - gx.S(30), yFirst - gx.S(6), 2);
            }
            if (nSpray > 0)
            {
                gx.G.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 110), 1f), xHit - gx.S(26), yTop, xHit + gx.S(26), yTop);
                gx.Text(TagSpray, gx.F.Tag, pal.Hot, xHit - gx.S(30), yTop - gx.S(6), 2);
            }
            // the ruler: first-shot level -> highest rise of this burst
            if ((nFirst > 0 || nSpray > 0) && burstMaxRise > 0.5)
            {
                double grow = TutorialStep3Math.RulerIn(burstMs);
                float rx = xHit + gx.S(42);
                float y2 = yFirst + (yTop - yFirst) * (float)grow;
                gx.GlowLine(TutorialGfx.A(pal.Accent, 220), 1.2f, rx, yFirst, rx, y2);
                gx.Bracket(rx, yFirst, y2, pal.Accent, null);
                if (Math.Abs(yFirst - y2) > gx.S(4)) gx.Arrow(pal.Accent, 1.4f, rx, yFirst - gx.S(2), rx, y2 + gx.S(1));
                float ly = (yFirst + y2) / 2f;
                RectangleF tr = gx.Tag(TagRuler, rx + gx.S(10), ly - gx.S(18), pal.Accent, 0);
                float nx = tr.Left;
                gx.Text(Approx, gx.F.Mono, pal.Accent, nx, tr.Bottom + gx.S(3), 0);
                float ax = nx + gx.Measure(Approx, gx.F.Mono).Width + gx.S(3);
                string n = rulerPct.Of(burstMaxRise * grow, c.G);   // 百分比偏移, e.g. 「↑ 3.33%」
                gx.Text(n, gx.F.Big, pal.Text, ax, tr.Bottom + gx.S(1), 0);
            }

            // first-shot holes again ABOVE the gun layer (the 右下 impact is the key fact of this step)
            for (int k = 0; k < rig.HoleCount; k++)
            {
                TutorialHole h = rig.Holes[k];
                if (h.Kind != 0 || h.AgeMs >= rig.HoleFadeMs) continue;
                float hx = (float)(cx + TutorialGeometry.WallToScreen(h.WX, rig.DistanceM) * z);
                float hy = (float)(cy - TutorialGeometry.WallToScreen(h.WY, rig.DistanceM) * z);
                double life = Math.Min(1, (1.0 - h.AgeMs / rig.HoleFadeMs) * 2);
                gx.G.DrawEllipse(gx.P(TutorialGfx.A(pal.Cyan, life * 0.95), 1.4f), hx - gx.S(5), hy - gx.S(5), gx.S(10), gx.S(10));
                gx.Dot(TutorialGfx.A(pal.Cyan, life), hx, hy, gx.S(1.8f));
            }

            // compact scene (small window / high DPI): only the core picture + the wall tag (review B21/B22)
            bool compact = TutorialStep3Math.Compact(scene.Width, scene.Height, gx.K);

            // HUD (top-left): the wall tag, then a plain legend (v4.1: at most 2 tags per view = wall + ruler)
            RectangleF t0 = gx.Tag(WallLabel, scene.Left + pad, scene.Top + pad, pal.Accent, 0);
            if (!compact)
            {
                RectangleF l1 = gx.Legend(LegHoles, scene.Left + pad, t0.Bottom + gx.S(6), pal.Hot, TutorialGfx.MarkDot, 0);
                gx.Legend(LegRing, scene.Left + pad, l1.Bottom + gx.S(3), pal.Neon, TutorialGfx.MarkRing, 0);
            }

            if (!compact)
            {
                // readout (top-right): mouse icon + live Y values
                DrawReadout(gx, c, scene, ringRise);
                // Y / time plot (bottom-left)
                DrawPlot(gx, scene, ringRise);
            }
            g.ResetClip();
        }

        private void DrawReadout(TutorialGfx gx, TutorialContext c, RectangleF scene, double ringRise)
        {
            TutorialPalette pal = gx.Pal;
            Font fl = gx.F.Small, fv = gx.F.Mono, ft = gx.F.MonoSmall;
            float padX = gx.S(8), padY = gx.S(6), gap = gx.S(8);
            float titleH = gx.Measure(ReadTitle, ft).Height;
            float rowH = Math.Max(gx.Measure(ReadRise, fl).Height, gx.Measure(ReadSample, fv).Height) + gx.S(3);
            float labW = Math.Max(gx.Measure(ReadRise, fl).Width, gx.Measure(ReadRing, fl).Width);
            float valW = gx.Measure(ReadSample, fv).Width;
            float mw = gx.S(16), mh = gx.S(24);
            float w = padX + labW + gap + valW + padX; // v4.3: no unit label
            float w2 = padX + gx.Measure(ReadTitle, ft).Width + gap + mw + padX;
            float w3 = padX + gx.Measure(ReadX, ft).Width + padX;
            w = Math.Max(w, Math.Max(w2, w3));
            float headH = Math.Max(titleH, mh);
            float h = padY + headH + gx.S(4) + 3 * rowH + padY;
            RectangleF r = new RectangleF(scene.Right - w - gx.S(10), scene.Top + gx.S(10), w, h);
            gx.RegisterView(r, true);
            gx.G.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 225)), r);
            gx.HudFrame(r, TutorialGfx.A(pal.Neon, 170), 8);
            float x = r.Left + padX, y = r.Top + padY;
            gx.Text(ReadTitle, ft, TutorialGfx.A(pal.Neon, 220), x, y, 0);
            // mouse icon (lit while firing)
            bool on = (LitKeys & TKey.Fire) != 0;
            float mx = r.Right - mw - padX, my = r.Top + padY;
            gx.G.FillEllipse(gx.B(TutorialGfx.A(pal.Surface, 235)), mx, my, mw, mh);
            gx.G.DrawEllipse(gx.P(on ? pal.Hot : pal.Border, 1.1f), mx, my, mw, mh);
            gx.G.FillPie(gx.B(on ? pal.Hot : TutorialGfx.A(pal.Border, 160)), mx, my, mw, mw, 180, 90);
            if (on) gx.Dot(TutorialGfx.A(pal.Hot, 60), mx + mw / 2, my + mh / 2, mw);
            // rows (vertically centred in each row)
            float vx = x + labW + gap;
            float rowY = y + headH + gx.S(4) + rowH / 2f;
            gx.TextMid(ReadRise, fl, pal.Text, x, rowY, 0);
            gx.TextMid(truthPct.Of(rig.Truth.Recoil, c.G), fv, pal.Hot, vx, rowY, 0);
            rowY += rowH;
            gx.TextMid(ReadRing, fl, pal.Text, x, rowY, 0);
            gx.TextMid(ringPct.Of(rig.Ring.Recoil, c.G), fv, pal.Neon, vx, rowY, 0);
            rowY += rowH;
            gx.TextMid(ReadX, ft, pal.Muted, x, rowY, 0);
        }

        private void DrawPlot(TutorialGfx gx, RectangleF scene, double ringRise)
        {
            TutorialPalette pal = gx.Pal;
            Font ft = gx.F.MonoSmall;
            SizeF ts = gx.Measure(PlotTitle, ft);
            float lineW = gx.S(14), sp = gx.S(5);
            float wRef = gx.Measure(PlotRef, ft).Width, wYou = gx.Measure(PlotYou, ft).Width;
            float legendW = lineW + sp + wRef + gx.S(14) + lineW + sp + wYou;
            float padX = gx.S(7), padY = gx.S(4);
            // title row, legend row UNDER it (never on the same line), then the curve area
            float w = Math.Max(gx.S(240), Math.Max(ts.Width, legendW) + 2 * padX);
            float curveH = gx.S(56);
            float h = padY + ts.Height + gx.S(2) + ts.Height + gx.S(6) + curveH + padY;
            if (!TutorialStep3Math.PlotFits(w, h, scene.Width, scene.Height)) return;
            RectangleF box = new RectangleF(scene.Left + gx.S(10), scene.Bottom - h - gx.S(10), w, h);
            gx.RegisterView(box, true);
            gx.G.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 230)), box);
            gx.G.DrawRectangle(gx.P(TutorialGfx.A(pal.Neon, 90), 1f), box.X, box.Y, box.Width, box.Height);
            float x = box.Left + padX, y = box.Top + padY;
            gx.Text(PlotTitle, ft, TutorialGfx.A(pal.Neon, 220), x, y, 0);
            float ly = y + ts.Height + gx.S(2), lmid = ly + ts.Height / 2f;
            gx.G.DrawLine(gx.P(pal.Hot, 1.6f), x, lmid, x + lineW, lmid);
            gx.TextMid(PlotRef, ft, pal.Hot, x + lineW + sp, lmid, 0);
            float x2 = x + lineW + sp + wRef + gx.S(14);
            gx.G.DrawLine(gx.P(pal.Neon, 1.6f), x2, lmid, x2 + lineW, lmid);
            gx.TextMid(PlotYou, ft, pal.Neon, x2 + lineW + sp, lmid, 0);
            RectangleF inr = new RectangleF(box.Left + padX, ly + ts.Height + gx.S(6), box.Width - 2 * padX, curveH);
            double max = TutorialStep3Math.PlotMax(refRise, ringRise);
            // full-value lines
            float yRef = TutorialStep3Math.PlotY(refRise, max, inr.Top, inr.Height);
            float yYou = TutorialStep3Math.PlotY(ringRise, max, inr.Top, inr.Height);
            gx.G.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 120), 1f), inr.Left, yRef, inr.Right, yRef);
            gx.G.DrawLine(gx.Dashed(TutorialGfx.A(pal.Neon, 120), 1f), inr.Left, yYou, inr.Right, yYou);
            gx.G.DrawLine(gx.P(TutorialGfx.A(pal.Border, 200), 1f), inr.Left, inr.Bottom, inr.Right, inr.Bottom);
            int n = plotTruth.Length;
            float dx = inr.Width / (n - 1);
            for (int i = 0; i < n; i++)
            {
                int s = (plotHead + i) % n;
                float px = inr.Left + i * dx;
                plotPtsA[i] = new PointF(px, TutorialStep3Math.PlotY(plotTruth[s], max, inr.Top, inr.Height));
                plotPtsB[i] = new PointF(px, TutorialStep3Math.PlotY(plotRing[s], max, inr.Top, inr.Height));
            }
            gx.G.DrawLines(gx.P(TutorialGfx.A(pal.Neon, 200), 1.3f), plotPtsB);
            gx.G.DrawLines(gx.P(TutorialGfx.A(pal.Hot, 60), 4f), plotPtsA);
            gx.G.DrawLines(gx.P(pal.Hot, 1.4f), plotPtsA);
        }
    }
#endif
}
