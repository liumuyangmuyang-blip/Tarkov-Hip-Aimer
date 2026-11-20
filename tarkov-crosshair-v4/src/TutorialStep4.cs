// TutorialStep4.cs -- 首次设置教程 script step 4 (移动的惯性). Owner: STEP4 worker.
// System.Drawing only (no WinForms), C# 5, no per-frame allocation.
// The pure part (TutorialStep4Math, TutorialStep4Trail) uses System only; tests-v4 compiles this file with
// TUTORIAL_MATH_ONLY defined, which leaves out the drawing page.
// Every number shown by this step is 示意 (the demo uses Leo's default profile as a stand-in for "where the gun points").
using System;
using System.Collections.Generic;
#if !TUTORIAL_MATH_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>Pure timing / mapping helpers of step 4 (System only, unit-tested in tests-v4/TestsV4Step4.cs).</summary>
    public static class TutorialStep4Math
    {
        // Same bit values as TKey (TutorialModel.cs); repeated here so this class stays independent of that file.
        public const int W = 1, S = 2, A = 4, D = 8, Fire = 64;
        public const int Move = W | S | A | D;

        /// <summary>Demo loop length (ms): W, A, S, D one after another, each held 1.4 s with a short burst in the middle.</summary>
        public const int LoopMs = 8000;

        /// <summary>Demo script as triples (fromMs, toMs, keys).</summary>
        public static readonly int[] Script = new int[]
        {
            200, 1600, W,   850, 1250, Fire,
            2100, 3500, A,  2750, 3150, Fire,
            4000, 5400, S,  4650, 5050, Fire,
            5900, 7300, D,  6550, 6950, Fire,
        };

        /// <summary>Keys pressed this frame that were not pressed last frame.</summary>
        public static int Rising(int prevMask, int nowMask) { return nowMask & ~prevMask; }

        /// <summary>Adds the movement keys of liveMask to the tested set.</summary>
        public static int AddTested(int tested, int liveMask) { return (tested | (liveMask & Move)) & Move; }

        public static bool AllTested(int tested) { return (tested & Move) == Move; }

        /// <summary>How many of W / A / S / D are in the mask (0..4).</summary>
        public static int CountMove(int mask)
        {
            int n = 0;
            if ((mask & W) != 0) n++;
            if ((mask & A) != 0) n++;
            if ((mask & S) != 0) n++;
            if ((mask & D) != 0) n++;
            return n;
        }

        /// <summary>Order the terminal reports keys in: W, A, S, D. Index of the first movement key in mask, or -1.</summary>
        public static int FirstMoveIndex(int mask)
        {
            if ((mask & W) != 0) return 0;
            if ((mask & A) != 0) return 1;
            if ((mask & S) != 0) return 2;
            if ((mask & D) != 0) return 3;
            return -1;
        }

        /// <summary>Hole colour group: 0 = 静止, 1 = W, 2 = A, 3 = S, 4 = D, 5 = 斜向 (two axes at once).</summary>
        public static int HoleKind(bool w, bool s, bool a, bool d)
        {
            bool y = w ^ s, x = a ^ d;   // opposite keys together cancel on that axis
            if (x && y) return 5;
            if (y) return w ? 1 : 3;
            if (x) return a ? 2 : 4;
            return 0;
        }

        /// <summary>Half-range (UI px) of the mini plot: the largest offset rounded up to a multiple of 20, at least 20.</summary>
        public static double PlotScale(double maxAbs)
        {
            if (double.IsNaN(maxAbs) || double.IsInfinity(maxAbs) || maxAbs <= 20) return 20;
            if (maxAbs > 2000) maxAbs = 2000;
            return Math.Ceiling(maxAbs / 20.0) * 20.0;
        }

        /// <summary>Maps v (-scale..scale) to -half..half, clamped (keeps the plotted point inside the plot).</summary>
        public static double PlotMap(double v, double scale, double half)
        {
            if (double.IsNaN(v) || !(scale > 0)) return 0;
            double t = v / scale;
            if (t > 1) t = 1;
            if (t < -1) t = -1;
            return t * half;
        }

        /// <summary>Demo zoom: the 10 m wall (1.2 m tall = 180 UI px) fills about 55 % of the scene height.</summary>
        public static double SceneZoom(double sceneH, double k)
        {
            if (!(sceneH > 0) || !(k > 0)) return 1.8;
            double z = 0.55 * sceneH / (180.0 * k);
            if (z < 1.1) z = 1.1;
            if (z > 3.2) z = 3.2;
            return z;
        }

        /// <summary>Zoom that also keeps the 10 m wall (1.6 m wide = 240 UI px) inside availW px (between the side columns).
        /// Never above the height rule; never below 1.1.</summary>
        public static double FitZoom(double zoomH, double availW, double k)
        {
            if (double.IsNaN(zoomH) || zoomH <= 0) zoomH = 1.8;
            if (!(k > 0) || double.IsNaN(availW)) return Math.Max(1.1, Math.Min(3.2, zoomH));
            double zw = availW / (240.0 * k);
            double z = Math.Min(zoomH, zw);
            if (z < 1.1) z = 1.1;
            if (z > 3.2) z = 3.2;
            return z;
        }

        /// <summary>Compact scene (review B22): below 700 x 330 logical px only the core picture + one tag is drawn.</summary>
        public static bool IsCompact(double sceneW, double sceneH, double k)
        {
            if (!(k > 0)) k = 1;
            return sceneW / k < 700 || sceneH / k < 330;
        }

        /// <summary>Hole kind of key index q (0 W, 1 A, 2 S, 3 D), see HoleKind.</summary>
        public static int KindOfKey(int q)
        {
            switch (q) { case 0: return 1; case 1: return 2; case 2: return 3; case 3: return 4; default: return 0; }
        }

        /// <summary>Distance between two points (UI px).</summary>
        public static double Dist(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>Fixed-size trail of recent points (oldest dropped). No allocation after construction.</summary>
    public sealed class TutorialStep4Trail
    {
        private readonly double[] xs, ys;
        private int head, count;
        private double sinceMs;
        /// <summary>A point is stored every IntervalMs (and the first one at once).</summary>
        public double IntervalMs = 30;

        public TutorialStep4Trail(int capacity)
        {
            if (capacity < 2) capacity = 2;
            xs = new double[capacity]; ys = new double[capacity];
        }

        public int Capacity { get { return xs.Length; } }
        public int Count { get { return count; } }

        public void Clear() { head = 0; count = 0; sinceMs = IntervalMs; }

        /// <summary>Advances time; stores (x, y) when IntervalMs has passed since the last stored point.</summary>
        public void Update(double dtMs, double x, double y)
        {
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            if (double.IsNaN(x) || double.IsNaN(y)) return;
            sinceMs += dtMs;
            if (count > 0 && sinceMs < IntervalMs) return;
            sinceMs = 0;
            Add(x, y);
        }

        public void Add(double x, double y)
        {
            int slot;
            if (count < xs.Length) { slot = (head + count) % xs.Length; count++; }
            else { slot = head; head = (head + 1) % xs.Length; }
            xs[slot] = x; ys[slot] = y;
        }

        /// <summary>Point i, 0 = oldest.</summary>
        public double X(int i) { return xs[(head + i) % xs.Length]; }
        public double Y(int i) { return ys[(head + i) % xs.Length]; }
    }

#if !TUTORIAL_MATH_ONLY
    public static class TutorialStep4
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order.</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep4Inertia());
        }
    }

    /// <summary>
    /// Script step 4 「移动的惯性」: first person at a 10 m wall. W / A / S / D (live or the demo loop) move the 示意 gun:
    /// the real barrel direction (dashed) and the impact point (bright dot) drift away from the standing impact, holes
    /// stay on the wall in one colour per key, a mini plot draws the offset path. No ring here: this page shows WHY.
    /// </summary>
    internal sealed class TutorialStep4Inertia : TutorialStep
    {
        private readonly FpRig rig = new FpRig();
        private readonly TutorialScript script;
        private readonly Profile demo;
        private readonly TutorialStep4Trail trail = new TutorialStep4Trail(56);
        private double restX, restY;      // standing impact at 10 m (parallax only)
        private double plotScale = 80;
        private int tested;               // W/A/S/D pressed LIVE on this page (kept across revisits)
        private int prevLive;
        private int lastPushed = -1;
        private double lastPushMs = -99999;
        private int kind;
        private Color[] kindColors;
        private double nowMs;

        private const int ChipClear = 1;

        private static readonly string[] TermKey = new string[]
        {
            "> 输入 W  枪口偏移", "> 输入 A  枪口偏移", "> 输入 S  枪口偏移", "> 输入 D  枪口偏移"
        };
        private static readonly string[] KeyLabel = new string[] { "W", "A", "S", "D" };
        private static readonly int[] KeyBit = new int[] { TutorialStep4Math.W, TutorialStep4Math.A, TutorialStep4Math.S, TutorialStep4Math.D };
        private static readonly string[] TestedText = new string[] { "已试 0 / 4", "已试 1 / 4", "已试 2 / 4", "已试 3 / 4", "四个方向都试过了 ✓" };
        private static readonly string[] KindName = new string[] { "静止", "W", "A", "S", "D", "斜向" };

        public TutorialStep4Inertia()
        {
            Id = "inertia"; Chapter = "原理"; Title = "移动的惯性";
            Body = "移动时，惯性会让枪口偏开。\n上下、左右都会偏一点，\n落点也跟着偏。";
            DevNote = "移动时的惯性会让枪口在上下、左右方向上偏移，落点也会跟着偏。这就是我们要按方向调偏移的原因。在这个距离上，呼吸晃动可以忽略。";
            Prompts = new string[] { "分别连按 W、A、S、D，也可以开枪" };
            Terminal = new string[] { "> 输入 W  枪口偏移", "> 呼吸晃动：忽略" };
            KeysShown = TKey.W | TKey.A | TKey.S | TKey.D | TKey.Fire;
            ClickFires = true;

            int[] sc = TutorialStep4Math.Script;
            ScriptSeg[] segs = new ScriptSeg[sc.Length / 3];
            for (int i = 0; i < segs.Length; i++) segs[i] = new ScriptSeg(sc[3 * i], sc[3 * i + 1], sc[3 * i + 2]);
            script = new TutorialScript(TutorialStep4Math.LoopMs, segs);

            // 示意 truth: Leo's default directions (where the gun drifts while moving); recoil off so only inertia shows.
            demo = Profile.CreateLeoDefault("参考");
            demo.RecoilMaxPx = 0; demo.RecoilClickKickPx = 0; demo.RecoilDriftX = 0;
            demo.StandOffsetX = 0; demo.StandOffsetY = 0;
            demo.Jump.Enabled = false;
            double m = 0;
            for (int i = 0; i < 4 && i < demo.Dirs.Count; i++)
                m = Math.Max(m, Math.Max(Math.Abs(demo.Dirs[i].OffX), Math.Abs(demo.Dirs[i].OffY)));
            plotScale = TutorialStep4Math.PlotScale(m);
            TutorialGeometry.HipImpactPx(10, out restX, out restY);
        }

        public override void Enter(TutorialContext c)
        {
            rig.TargetDistanceM = 10;
            rig.Setup(c, demo, demo, false);
            rig.SetDistanceNow(10);
            rig.UseParallax = true;
            rig.ShowRing = false; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallLabel = "10 米";
            rig.SpreadPx = 1.6;
            trail.Clear();
            prevLive = 0; lastPushed = -1; lastPushMs = -99999; nowMs = 0;
            if (TutorialStep4Math.AllTested(tested)) Done = true;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            nowMs += dtMs;
            if (c.Arbiter.Resumed) { trail.Clear(); rig.ClearHoles(); }
            bool fire; int lit;
            InputState i = c.Input(script, out fire, out lit);
            LitKeys = lit;
            kind = TutorialStep4Math.HoleKind(i.Fwd, i.Back, i.Left, i.Right);
            rig.NextHoleKind = kind;
            rig.Update(dtMs, i, fire);
            trail.Update(dtMs, rig.AimX, rig.AimY);

            // live key tracking: tested set, terminal line per newly pressed key
            int live = c.LiveMask & TutorialStep4Math.Move;
            int rise = TutorialStep4Math.Rising(prevLive, live);
            prevLive = live;
            if (rise != 0)
            {
                tested = TutorialStep4Math.AddTested(tested, rise);
                int k = TutorialStep4Math.FirstMoveIndex(rise);
                if (k >= 0 && (k != lastPushed || nowMs - lastPushMs > 4000))
                {
                    c.Term.Push(TermKey[k]);
                    lastPushed = k; lastPushMs = nowMs;
                }
            }
            if (TutorialStep4Math.AllTested(tested)) Done = true;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id == ChipClear) { rig.ClearHoles(); trail.Clear(); }
        }

        // ---- colours: the shared per-key map (TutorialPalette.KeyColor, same in every step; never the ring green) ----
        private TutorialPalette lastPal;

        private void EnsureColors(TutorialPalette p)
        {
            if (kindColors != null && ReferenceEquals(lastPal, p)) return;
            lastPal = p;
            kindColors = new Color[] { p.Muted, p.KeyColor(TKey.W), p.KeyColor(TKey.A), p.KeyColor(TKey.S), p.KeyColor(TKey.D), p.Text }; // 静止 W A S D 斜向
            rig.KindColors = kindColors;
        }

        // ---- layout cache: rebuilt only when the scene size, DPI or font set (dynamic font size) changes ----
        private float lW = -1, lH = -1, lK = -1;
        private TutorialFonts lF;
        private bool compact;
        private float uPad, modeH, tagH, boxW, boxH, titleH, rowH, labW, valW, plotS, chipW, chipH, smallH, capLog, clusterH, markStep, markR, markH;
        private double zoom = 1.8;

        private bool lC;

        // v4.1 百分比偏移 readout (rebuilt only when the shown value changes)
        private const string PctProbe = "← 00.00%";
        private readonly TutorialPctLabel dxPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel dyPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);

        private void Layout(TutorialGfx gx, RectangleF scene, bool frameCompact)
        {
            if (scene.Width == lW && scene.Height == lH && gx.K == lK && ReferenceEquals(gx.F, lF) && frameCompact == lC) return;
            lW = scene.Width; lH = scene.Height; lK = gx.K; lF = gx.F; lC = frameCompact;
            float k = gx.K; // K = DPI scale (v4.3.1: fixed text size)
            uPad = gx.S(12);
            tagH = gx.Measure("落点", gx.F.Tag).Height + gx.S(5);
            modeH = tagH;
            SizeF ts = gx.Measure("惯性偏移", gx.F.MonoSmall);
            titleH = ts.Height;
            smallH = gx.Measure("已试", gx.F.Small).Height;
            rowH = gx.Measure("Δx", gx.F.Mono).Height * 1.22f;
            labW = Math.Max(gx.Measure("按键", gx.F.Mono).Width, gx.Measure("Δy", gx.F.Mono).Width);
            valW = Math.Max(gx.Measure(PctProbe, gx.F.Mono).Width, gx.Measure("静止", gx.F.Mono).Width);
            float unitW = 0; // v4.1: values are 百分比偏移 (the % sign is part of the value)
            boxW = Math.Max(ts.Width, labW + gx.S(14) + valW + gx.S(8) + unitW) + gx.S(20);
            boxH = gx.S(8) + titleH * 1.2f + 3 * rowH + gx.S(8);
            capLog = 30;
            float cap = gx.S(capLog), gap = gx.S(4);
            float clusterW = cap * 3.25f + 2 * gap + gx.S(10) + cap * 0.9f;
            clusterH = 2 * cap + gap;
            markR = gx.S(4.5f);
            markStep = Math.Max(markR * 2 + gx.S(10), gx.Measure("W", gx.F.MonoSmall).Width + gx.S(10));
            markH = 2 * markR + gx.S(3) + titleH;
            float triedW = gx.Measure(TestedText[4], gx.F.Small).Width;
            float colL = Math.Max(Math.Max(boxW, clusterW), Math.Max(4 * markStep, triedW));
            chipW = gx.Measure("清空弹孔", gx.F.Button).Width + gx.S(22);
            chipH = gx.Measure("清空弹孔", gx.F.Button).Height + gx.S(10);
            float breathW = gx.Measure("呼吸晃动：忽略", gx.F.MonoSmall).Width;
            plotS = Math.Max(gx.S(90), Math.Min(gx.S(150), scene.Height * 0.34f));
            float colR = Math.Max(plotS, Math.Max(chipW, breathW));
            compact = frameCompact || TutorialStep4Math.IsCompact(scene.Width, scene.Height, k);
            float leftH = 2 * uPad + modeH + gx.S(8) + boxH + gx.S(12) + clusterH + gx.S(8) + markH + gx.S(4) + smallH;
            float rightH = 2 * uPad + plotS + titleH + gx.S(6) + titleH + gx.S(8) + chipH;
            if (leftH > scene.Height || rightH > scene.Height) compact = true;
            double zh = TutorialStep4Math.SceneZoom(scene.Height, k);
            double avail = scene.Width - 2 * uPad - 2 * Math.Max(colL, colR) - 2 * gx.S(14);
            if (!compact && avail < 240 * 1.1 * k) compact = true;
            if (compact) avail = scene.Width - 2 * uPad;
            zoom = TutorialStep4Math.FitZoom(zh, avail, k);
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            TutorialPalette p = gx.Pal;
            Graphics g = gx.G;
            EnsureColors(p);
            Layout(gx, scene, c.Compact);
            rig.Zoom = zoom;
            rig.Draw(gx, scene);

            g.SetClip(scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);

            // trail of the impact point (fading)
            int n = trail.Count;
            for (int t = 1; t < n; t++)
            {
                PointF a = rig.ToScreen(scene, gx.K, trail.X(t - 1), trail.Y(t - 1));
                PointF b = rig.ToScreen(scene, gx.K, trail.X(t), trail.Y(t));
                g.DrawLine(gx.P(TutorialGfx.A(p.Cyan, 0.75 * t / n), 1.6f), a, b);
            }

            // standing impact (dashed circle) + 屏幕中心, and the inertia arrow to the moving impact
            PointF rest = rig.ToScreen(scene, gx.K, restX, restY);
            PointF aim = rig.ToScreen(scene, gx.K, rig.AimX, rig.AimY);
            float rr = gx.S(8);
            g.DrawEllipse(gx.Dashed(p.Text, 1.1f), rest.X - rr, rest.Y - rr, 2 * rr, 2 * rr);
            if (!compact) gx.Text("屏幕中心", gx.F.MonoSmall, p.Text, cx, cy - gx.S(6) - titleH, 1);
            // labels go on the side away from the moving impact (review B15: the impact / muzzle line crossed them)
            bool aimLeft = aim.X < rest.X - rr;
            if (compact) { } // v4.1: small scene: these two labels sat under the 落点 tag
            else if (aimLeft) gx.Text("站定落点", gx.F.MonoSmall, p.Muted, rest.X + rr + gx.S(4), rest.Y - rr - titleH, 0); // above-right: the barrel line runs below-right
            else gx.TextMid("站定落点", gx.F.MonoSmall, p.Muted, rest.X - rr - gx.S(6), rest.Y + gx.S(2), 2);
            double off = TutorialStep4Math.Dist(restX, restY, rig.AimX, rig.AimY);
            if (off * z > gx.S(12)) gx.Arrow(p.Accent, 1.6f, rest.X, rest.Y, aim.X, aim.Y);

            // the real barrel direction (dashed, from the muzzle). FpRig does not expose the muzzle point; for the right hand the
            // muzzle is the top-left corner of the drawn gun (GunBounds). Request filed: v4-requests/STEP4.md.
            RectangleF gb = rig.GunBounds;
            float mx = gb.IsEmpty ? scene.Right - gx.S(60) : gb.Left + gx.S(4), my = gb.IsEmpty ? scene.Bottom - gx.S(40) : gb.Top + gx.S(4);
            g.DrawLine(gx.Dashed(TutorialGfx.A(p.Cyan, 200), 1.4f), mx, my, aim.X, aim.Y);
            gx.GlowDot(p.Cyan, aim.X, aim.Y, gx.S(3.2f));
            // tags above the impact (away from 站定落点) and left of the barrel line, never on the gun (bottom-right)
            if (off * z > gx.S(18) || compact) gx.Tag("落点", aimLeft ? aim.X - gx.S(10) : aim.X + gx.S(10), aim.Y - gx.S(8) - tagH, p.Cyan, aimLeft ? 2 : 0);
            float tx = aim.X + (mx - aim.X) * 0.72f, ty = aim.Y + (my - aim.Y) * 0.72f;
            if (!compact) gx.Legend("真实枪口", tx - gx.S(14), ty - tagH * 0.5f, p.Cyan, TutorialGfx.MarkDash, 2); // v4.1: max 2 tags per view

            // HUD: mode tag (always)
            bool live = !c.Scripted;
            float L = scene.Left + uPad, T = scene.Top + uPad;
            gx.Tag(live ? "● 实时输入" : "▶ 自动演示", L, T, live ? p.Neon : p.Muted, 0);
            int cnt = TutorialStep4Math.CountMove(tested);
            if (compact)
            {
                gx.Text(TestedText[cnt], gx.F.Small, cnt == 4 ? p.Neon : p.Text, scene.Right - uPad, T + gx.S(2), 2);
                g.ResetClip();
                return;
            }

            // inertia readout
            RectangleF box = new RectangleF(L, T + modeH + gx.S(8), boxW, boxH);
            gx.RegisterView(box, true);
            g.FillRectangle(gx.B(TutorialGfx.A(p.SceneBg, 230)), box);
            gx.HudFrame(box, TutorialGfx.A(p.Neon, 170), 8);
            float lx = box.Left + gx.S(10), vx = lx + labW + gx.S(14), ly = box.Top + gx.S(8);
            gx.Text("惯性偏移", gx.F.MonoSmall, p.Neon, lx, ly, 0);
            ly += titleH * 1.2f;
            gx.Text("按键", gx.F.Mono, p.Muted, lx, ly, 0);
            gx.Text(KindName[kind], gx.F.Mono, kind == 0 ? p.Muted : kindColors[kind], vx, ly, 0);
            ly += rowH;
            gx.Text("Δx", gx.F.Mono, p.Muted, lx, ly, 0);
            gx.Text(dxPct.Of(rig.AimX - restX, c.G), gx.F.Mono, p.Text, vx, ly, 0);
            ly += rowH;
            gx.Text("Δy", gx.F.Mono, p.Muted, lx, ly, 0);
            gx.Text(dyPct.Of(rig.AimY - restY, c.G), gx.F.Mono, p.Text, vx, ly, 0);

            // mini plot: offset path around the standing impact
            RectangleF pr = new RectangleF(scene.Right - uPad - plotS, T, plotS, plotS + titleH);
            gx.RegisterView(pr, true);
            g.FillRectangle(gx.B(TutorialGfx.A(p.SceneBg, 230)), pr);
            g.DrawRectangle(gx.P(TutorialGfx.A(p.Neon, 90), 1f), pr.X, pr.Y, pr.Width, pr.Height);
            gx.Text("偏移轨迹", gx.F.MonoSmall, p.Neon, pr.Left + gx.S(6), pr.Top + gx.S(4), 0);
            float itop = pr.Top + gx.S(8) + titleH;
            RectangleF inner = new RectangleF(pr.Left + gx.S(8), itop, pr.Width - gx.S(16), pr.Bottom - gx.S(8) - itop);
            float pcx = inner.Left + inner.Width / 2, pcy = inner.Top + inner.Height / 2;
            float half = Math.Min(inner.Width, inner.Height) / 2;
            Pen axis = gx.P(TutorialGfx.A(p.Neon, 45), 1f);
            g.DrawLine(axis, inner.Left, pcy, inner.Right, pcy);
            g.DrawLine(axis, pcx, inner.Top, pcx, inner.Bottom);
            g.DrawEllipse(gx.P(TutorialGfx.A(p.Neon, 25), 1f), pcx - half * 0.5f, pcy - half * 0.5f, half, half);
            gx.Text("上", gx.F.MonoSmall, p.Muted, pcx + gx.S(4), inner.Top, 0);
            gx.Text("右", gx.F.MonoSmall, p.Muted, inner.Right, pcy + gx.S(2), 2);
            for (int t = 1; t < n; t++)
            {
                float ax = pcx + (float)TutorialStep4Math.PlotMap(trail.X(t - 1) - restX, plotScale, half);
                float ay = pcy - (float)TutorialStep4Math.PlotMap(trail.Y(t - 1) - restY, plotScale, half);
                float bx = pcx + (float)TutorialStep4Math.PlotMap(trail.X(t) - restX, plotScale, half);
                float by = pcy - (float)TutorialStep4Math.PlotMap(trail.Y(t) - restY, plotScale, half);
                g.DrawLine(gx.P(TutorialGfx.A(p.Cyan, 0.15 + 0.7 * t / n), 1.3f), ax, ay, bx, by);
            }
            float dpx = pcx + (float)TutorialStep4Math.PlotMap(rig.AimX - restX, plotScale, half);
            float dpy = pcy - (float)TutorialStep4Math.PlotMap(rig.AimY - restY, plotScale, half);
            gx.GlowDot(p.Cyan, dpx, dpy, gx.S(2.4f));
            float by2 = pr.Bottom + gx.S(6);
            gx.Text("呼吸晃动：忽略", gx.F.MonoSmall, p.Muted, pr.Right, by2, 2);
            RectangleF chip = new RectangleF(pr.Right - chipW, by2 + titleH + gx.S(8), chipW, chipH);
            gx.Chip(ChipClear, chip, "清空弹孔", false);

            // key cluster + tried marks in each key's hole colour (bottom-left)
            float triedY = scene.Bottom - uPad - smallH;
            float markY = triedY - gx.S(4) - markH;
            float ky = markY - gx.S(8) - clusterH;
            gx.KeyCluster(L, ky, capLog, LitKeys, TKey.W | TKey.A | TKey.S | TKey.D, true);
            for (int q = 0; q < 4; q++)
            {
                bool ok = (tested & KeyBit[q]) != 0;
                Color kc = kindColors[TutorialStep4Math.KindOfKey(q)];
                float mcx = L + markR + q * markStep, mcy = markY + markR;
                if (ok) gx.GlowDot(kc, mcx, mcy, markR);
                else g.DrawEllipse(gx.P(kc, 1.2f), mcx - markR, mcy - markR, 2 * markR, 2 * markR);
                gx.Text(KeyLabel[q], gx.F.MonoSmall, kc, mcx, mcy + markR + gx.S(3), 1);
            }
            gx.Text(TestedText[cnt], gx.F.Small, cnt == 4 ? p.Neon : p.Text, L, triedY, 0);
            g.ResetClip();
        }
    }
#endif
}
