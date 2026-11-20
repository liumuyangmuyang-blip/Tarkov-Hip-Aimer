// TutorialSteps12.cs -- 首次设置教程 script steps 1 (为什么做这个) and 2 (落点在右下). Owner: TUTORIAL.
// Text: v4-tutorial-script.md. System.Drawing only (no WinForms), C# 5, no allocation per frame (strings are static or
// built when a value changes). Every distance / offset in the animations is 示意.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TarkovCrosshair
{
    /// <summary>
    /// Script step 1: a small computing animation (keys -> 计算 -> ring on a mini screen), then the first-person view with
    /// a wall: W A S D / Q E move the gun and the program's ring (the user's current values).
    /// </summary>
    internal sealed class TutorialStepWhy : TutorialStep
    {
        private const double IntroMs = 3300, WipeMs = 420;
        private readonly FpRig rig = new FpRig();
        private double introT;
        private bool introDone;
        private int lastMask = -1;
        private double lastPushMs = -1000;
        private readonly TutorialRng rng = new TutorialRng(4242);
        private const int Trail = 48;
        private readonly double[] trailX = new double[Trail], trailY = new double[Trail];
        private int trailHead, trailCount;
        private string lineCache;

        private static readonly string[] Pct = MakePct();
        private static readonly string[] Bin = { "0", "1" };
        private static readonly TutorialScript Script = new TutorialScript(11600,
            new ScriptSeg(600, 1800, TKey.D), new ScriptSeg(2600, 3800, TKey.A), new ScriptSeg(4600, 5600, TKey.W),
            new ScriptSeg(6200, 7000, TKey.S), new ScriptSeg(7800, 9000, TKey.Q), new ScriptSeg(9600, 10800, TKey.E));
        // intro key-light sequence (independent of the demo script)
        private static readonly TutorialScript IntroKeys = new TutorialScript(3300,
            new ScriptSeg(200, 700, TKey.W), new ScriptSeg(600, 1200, TKey.D), new ScriptSeg(1300, 1800, TKey.Q),
            new ScriptSeg(1700, 2300, TKey.A), new ScriptSeg(2200, 2800, TKey.E), new ScriptSeg(2500, 3100, TKey.S));

        private const string TagFp = "第一人称", ReplayText = "重播动画";

        private static string[] MakePct()
        {
            string[] a = new string[101];
            for (int i = 0; i <= 100; i++) a[i] = i + "%";
            return a;
        }

        public TutorialStepWhy()
        {
            Id = "why"; Chapter = "开始"; Title = "为什么做这个";
            Body = "塔科夫的腰射落点，不在屏幕正中。\n这个工具在屏幕上画一个准星，\n告诉你子弹大概落在哪。";
            DevNote = "这个项目只做一件事：把腰射的落点画在屏幕上。先看几段演示，你就会明白每一项为什么要这样调。";
            Hint = "按 W A S D 和 {左侧身} {右侧身}，动一动";
            Terminal = new string[] { "> 读取按键 ........ 完成", "> 计算落点 ........ 完成", "> 绘制准星 ........ 完成" };
            KeysShown = TKey.W | TKey.A | TKey.S | TKey.D | TKey.Q | TKey.E;
        }

        public override void Enter(TutorialContext c)
        {
            Profile p = c.P;
            rig.Setup(c, p, p, false);
            rig.Zoom = 1.7;
            rig.SetDistanceNow(10);
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowCentre = false; rig.ShowAim = false;
            rig.WallLabel = null;
            introT = 0; introDone = false; lastMask = -1; trailCount = 0; trailHead = 0;
        }

        public override void OnFieldChanged(TutorialContext c) { Enter(c); }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire;
            int lit;
            InputState i = c.Input(Script, out fire, out lit);
            if (!introDone)
            {
                introT += dtMs;
                if (!c.Scripted) introT = Math.Max(introT, IntroMs); // a key press skips the intro
                if (introT >= IntroMs + WipeMs) introDone = true;
                if (introT < IntroMs) { lit = c.Scripted ? IntroKeys.HeldAt(introT) : c.LiveMask; i = new InputState(); fire = false; }
            }
            LitKeys = lit;
            rig.Update(dtMs, i, fire);
            // mini-screen trail (UI px of the ring)
            trailX[trailHead] = rig.RingX; trailY[trailHead] = rig.RingY;
            trailHead = (trailHead + 1) % Trail;
            if (trailCount < Trail) trailCount++;
            // terminal: one line when the held keys change (live only, at most every 250 ms)
            int m = c.LiveMask & (TKey.Move | TKey.Q | TKey.E);
            if (!c.Scripted && m != lastMask && m != 0 && c.StepMs - lastPushMs > 250)
            {
                lastPushMs = c.StepMs;
                c.Term.Push(Describe(c.P, m, c.G));
            }
            if (m != lastMask) lastMask = m;
            if (introDone) Done = true;
        }

        /// <summary>"> 输入 W+D → 偏移 ← 2.92%  ↓ 2.92%" for the profile's target of that key set (built on key change only).</summary>
        private string Describe(Profile p, int m, GlobalSettings g)
        {
            double x = 0, y = 0;
            int f = ((m & TKey.W) != 0 ? 1 : 0) - ((m & TKey.S) != 0 ? 1 : 0);
            int s = ((m & TKey.D) != 0 ? 1 : 0) - ((m & TKey.A) != 0 ? 1 : 0);
            int dir = MotionEngine.DirFromSigns(f, s);
            if (dir >= 0 && p != null && p.Dirs != null && dir < p.Dirs.Count) { x += p.Dirs[dir].OffX; y += p.Dirs[dir].OffY; }
            if (p != null && (m & TKey.Q) != 0 && (m & TKey.E) == 0) { x += p.LeanQX; y += p.LeanQY; }
            if (p != null && (m & TKey.E) != 0 && (m & TKey.Q) == 0) { x += p.LeanEX; y += p.LeanEY; }
            string keys = "";
            if ((m & TKey.W) != 0) keys += "W+";
            if ((m & TKey.A) != 0) keys += "A+";
            if ((m & TKey.S) != 0) keys += "S+";
            if ((m & TKey.D) != 0) keys += "D+";
            if ((m & TKey.Q) != 0) keys += "Q+";
            if ((m & TKey.E) != 0) keys += "E+";
            if (keys.EndsWith("+")) keys = keys.Substring(0, keys.Length - 1);
            lineCache = "> 输入 " + keys + "  → 偏移 " + TutorialPct.Vec(x, y, g); // v4.1 百分比偏移
            return lineCache;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id == 1) { introT = 0; introDone = false; } // 重播动画
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            Graphics g = gx.G;
            TutorialPalette pal = gx.Pal;
            if (introT < IntroMs + WipeMs)
            {
                float wipe = introT <= IntroMs ? 0f : (float)TutorialGeometry.Smooth((introT - IntroMs) / WipeMs);
                float cut = scene.Top + scene.Height * wipe;
                g.SetClip(new RectangleF(scene.Left, cut, scene.Width, scene.Bottom - cut));
                DrawIntro(gx, scene, introT);
                g.ResetClip();
                if (wipe > 0)
                {
                    RectangleF shown = new RectangleF(scene.Left, scene.Top, scene.Width, cut - scene.Top);
                    rig.ClipOverride = shown;
                    rig.Draw(gx, scene);
                    rig.ClipOverride = RectangleF.Empty;
                    g.SetClip(shown);
                    gx.Tag(TagFp, scene.Left + gx.S(10), scene.Top + gx.S(10), pal.Neon, 0); // the HUD label comes in with the view
                    g.ResetClip();
                    gx.GlowLine(pal.Neon, 1.5f, scene.Left, cut, scene.Right, cut);
                }
            }
            else
            {
                rig.Draw(gx, scene);
                gx.Tag(TagFp, scene.Left + gx.S(10), scene.Top + gx.S(10), pal.Neon, 0);
                float rw = gx.Measure(ReplayText, gx.F.Button).Width + gx.S(26), rh = gx.F.Button.GetHeight(g) + gx.S(10);
                gx.Chip(1, new RectangleF(scene.Right - rw - gx.S(10), scene.Top + gx.S(10), rw, rh), ReplayText, false);
            }
            float cap = c.Compact ? 24f : 30f;
            if (introT >= IntroMs) gx.KeyCluster(scene.Left + gx.S(14), scene.Bottom - gx.S(2 * cap + 18), cap, LitKeys, KeysShown, false);
        }

        private void DrawIntro(TutorialGfx gx, RectangleF scene, double t)
        {
            Graphics g = gx.G;
            TutorialPalette pal = gx.Pal;
            gx.SceneBackground(scene);
            float k = gx.K;
            // faint grid
            Pen grid = gx.P(TutorialGfx.A(pal.Neon, 14), 1f);
            for (float x = scene.Left; x < scene.Right; x += gx.S(32)) g.DrawLine(grid, x, scene.Top, x, scene.Bottom);
            for (float y = scene.Top; y < scene.Bottom; y += gx.S(32)) g.DrawLine(grid, scene.Left, y, scene.Right, y);

            float cy = scene.Top + scene.Height * 0.45f;
            // chip in the middle
            float cs = Math.Min(scene.Width * 0.2f, scene.Height * 0.34f);
            float capPx = gx.S(Math.Max(24f, Math.Min(40f, scene.Width / gx.K / 17f)));
            float cx = Math.Max(scene.Left + scene.Width * 0.47f, scene.Left + scene.Width * 0.05f + capPx * 3.4f + gx.S(56) + cs / 2);
            RectangleF chip = new RectangleF(cx - cs / 2, cy - cs / 2, cs, cs);
            double pulse = 0.5 + 0.5 * Math.Sin(t / 140.0);
            g.FillRectangle(gx.B(TutorialGfx.A(pal.Neon, (int)(18 + 22 * pulse))), chip.X - gx.S(6), chip.Y - gx.S(6), chip.Width + gx.S(12), chip.Height + gx.S(12));
            g.FillRectangle(gx.B(Color.FromArgb(255, 10, 14, 12)), chip);
            g.DrawRectangle(gx.P(pal.Neon, 1.6f), chip.X, chip.Y, chip.Width, chip.Height);
            int pins = 8;
            Pen pin = gx.P(TutorialGfx.A(pal.Neon, 180), 1.4f);
            for (int i = 0; i < pins; i++)
            {
                float u = chip.Left + chip.Width * (i + 0.5f) / pins, v = chip.Top + chip.Height * (i + 0.5f) / pins;
                g.DrawLine(pin, u, chip.Top, u, chip.Top - gx.S(8)); g.DrawLine(pin, u, chip.Bottom, u, chip.Bottom + gx.S(8));
                g.DrawLine(pin, chip.Left, v, chip.Left - gx.S(8), v); g.DrawLine(pin, chip.Right, v, chip.Right + gx.S(8), v);
            }
            // rotating arcs + percentage
            float r1 = cs * 0.33f, r2 = cs * 0.25f;
            float a = (float)(t * 0.25 % 360);
            g.DrawArc(gx.P(pal.Neon, 2.2f), cx - r1, cy - r1, 2 * r1, 2 * r1, a, 110);
            g.DrawArc(gx.P(pal.Neon, 2.2f), cx - r1, cy - r1, 2 * r1, 2 * r1, a + 180, 110);
            g.DrawArc(gx.P(pal.Accent, 1.6f), cx - r2, cy - r2, 2 * r2, 2 * r2, -a * 1.6f, 70);
            g.DrawArc(gx.P(pal.Accent, 1.6f), cx - r2, cy - r2, 2 * r2, 2 * r2, -a * 1.6f + 180, 70);
            int pct = (int)Math.Min(100, Math.Max(0, t / (IntroMs - 300) * 100));
            gx.TextMid(Pct[pct], gx.F.Big, pal.Text, cx, cy - gx.S(2), 1);
            gx.Text(pct < 100 ? "计算落点" : "完成", gx.F.Tag, pal.Neon, cx, chip.Bottom + gx.S(14), 1);

            // keys on the left, traces into the chip with travelling pulses
            int lit = LitKeys;
            float cap = Math.Max(24f, Math.Min(40f, scene.Width / gx.K / 17f));
            float kx = scene.Left + scene.Width * 0.05f, ky = cy - gx.S(cap);
            SizeF ks = gx.KeyCluster(kx, ky, cap, lit, KeysShown, false);
            float tx0 = kx + ks.Width + gx.S(10);
            for (int i = 0; i < 3; i++)
            {
                float y = cy - cs * 0.25f + i * cs * 0.25f;
                Color lc = lit != 0 ? pal.Neon : TutorialGfx.A(pal.Neon, 70);
                float mid = (tx0 + chip.Left - gx.S(8)) / 2 + (i - 1) * gx.S(10);
                PointF[] tr = gx.Pts(4);
                tr[0] = new PointF(tx0, ky + ks.Height * (i + 0.5f) / 3); tr[1] = new PointF(mid, tr[0].Y);
                tr[2] = new PointF(mid, y); tr[3] = new PointF(chip.Left - gx.S(8), y);
                g.DrawLines(gx.P(TutorialGfx.A(lc, 150), 1.3f), tr);
                if (lit != 0)
                {
                    double ph = ((t / 520.0) + i * 0.33) % 1.0;
                    PointF pt = Along(tr, ph);
                    gx.GlowDot(pal.Neon, pt.X, pt.Y, gx.S(2.6f));
                }
            }
            // mini screen on the right with the ring and its trail
            float mw = Math.Min(scene.Width * 0.27f, gx.S(240)), mh = mw * 0.62f;
            RectangleF mon = new RectangleF(scene.Right - scene.Width * 0.05f - mw, cy - mh / 2, mw, mh);
            g.FillRectangle(gx.B(Color.FromArgb(255, 8, 10, 12)), mon);
            g.DrawRectangle(gx.P(pal.Border, 2f), mon.X, mon.Y, mon.Width, mon.Height);
            g.DrawLine(gx.P(pal.Border, 2f), mon.Left + mon.Width / 2, mon.Bottom, mon.Left + mon.Width / 2, mon.Bottom + gx.S(14));
            g.DrawLine(gx.P(pal.Border, 2f), mon.Left + mon.Width * 0.35f, mon.Bottom + gx.S(14), mon.Left + mon.Width * 0.65f, mon.Bottom + gx.S(14));
            float mcx = mon.Left + mon.Width / 2, mcy = mon.Top + mon.Height / 2;
            Pen ax = gx.P(TutorialGfx.A(pal.Neon, 60), 1f);
            g.DrawLine(ax, mon.Left + gx.S(6), mcy, mon.Right - gx.S(6), mcy);
            g.DrawLine(ax, mcx, mon.Top + gx.S(6), mcx, mon.Bottom - gx.S(6));
            // a Lissajous path stands in for "calculated offsets" during the intro
            float rx = mon.Width * 0.32f, ry = mon.Height * 0.3f;
            PointF prev = PointF.Empty;
            for (int i = 0; i < 24; i++)
            {
                double tt = t - i * 40;
                if (tt < 0) break;
                PointF q = new PointF(mcx + (float)(Math.Sin(tt / 610.0) * rx), mcy + (float)(Math.Sin(tt / 380.0 + 0.6) * ry));
                if (i > 0) g.DrawLine(gx.P(TutorialGfx.A(pal.Cyan, 200 - i * 8), 1.4f), prev, q);
                prev = q;
            }
            PointF head = new PointF(mcx + (float)(Math.Sin(t / 610.0) * rx), mcy + (float)(Math.Sin(t / 380.0 + 0.6) * ry));
            gx.Ring(head.X, head.Y, gx.S(14), pal.Neon, Color.Black, 1.6f);
            // chip -> screen trace
            gx.Arrow(TutorialGfx.A(pal.Neon, 170), 1.3f, chip.Right + gx.S(10), cy, mon.Left - gx.S(6), cy);
            double ph2 = (t / 400.0) % 1.0;
            gx.GlowDot(pal.Neon, chip.Right + gx.S(10) + (float)ph2 * (mon.Left - chip.Right - gx.S(16)), cy, gx.S(2.4f));
            // binary rain under the chip
            float by = chip.Bottom + gx.S(34);
            for (int col = 0; col < 18; col++)
            {
                float bx = cx - gx.S(9 * 9) + col * gx.S(9);
                string b = Bin[(int)((rng.Next() >> 7) & 1)];
                gx.Text(b, gx.F.MonoSmall, TutorialGfx.A(pal.Neon, 60 + (col * 37 % 120)), bx, by + (float)((t / 9 + col * 13) % 18) * k, 0);
            }
            gx.Text("读取按键", gx.F.Tag, pal.Muted, kx, ky + ks.Height + gx.S(10), 0);
            gx.Text("绘制准星", gx.F.Tag, pal.Muted, mon.Left, mon.Bottom + gx.S(20), 0);
        }

        private static PointF Along(PointF[] p, double u)
        {
            double total = 0;
            for (int i = 1; i < p.Length; i++) total += Dist(p[i - 1], p[i]);
            double d = u * total;
            for (int i = 1; i < p.Length; i++)
            {
                double s = Dist(p[i - 1], p[i]);
                if (d <= s && s > 0)
                {
                    double f = d / s;
                    return new PointF((float)(p[i - 1].X + (p[i].X - p[i - 1].X) * f), (float)(p[i - 1].Y + (p[i].Y - p[i - 1].Y) * f));
                }
                d -= s;
            }
            return p[p.Length - 1];
        }

        private static double Dist(PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Script step 2: 落点在右下. The wall starts at 5 m; each shot marks the impact (落点), then the wall is pushed to 10, 15
    /// and 25 m. The impact follows TutorialGeometry.HipImpactPx (示意 parallax: bottom-right, centre at 25 m). A side view
    /// shows the bullet line meeting the sight line at 25 m. Idle users get an automatic shot after a few seconds.
    /// </summary>
    internal sealed class TutorialStepHipfire : TutorialStep
    {
        private static readonly double[] Dist = TutorialGeometry.DemoDistancesM; // 5 10 15 25
        private static readonly string[] DistTag = { "5 米", "10 米", "15 米", "25 米 · 归零点" };
        private static readonly string[] DistShort = { "5 米", "10 米", "15 米", "25 米" };
        private static readonly string[] TermLine =
        {
            "> 距离 5 米   落点 右下", "> 距离 10 米  落点 右下", "> 距离 15 米  落点 右下", "> 距离 25 米  落点 中心  [归零点]"
        };
        private const double AutoShotMs = 2600, AdvanceMs = 1100, LoopRestMs = 6500;

        private readonly FpRig rig = new FpRig();
        private int stage;            // distance index now
        private double waitMs;        // time in this stage waiting for a shot
        private double sinceShotMs;   // time since the shot of this stage (-1 = none yet)
        private bool userFired;       // the user fired at least once (no auto loop then)
        private readonly bool[] recorded = new bool[4];
        private readonly double[] recX = new double[4], recY = new double[4];
        private double doneMs;
        private bool autoPulse;
        private double pulseLeft;

        public TutorialStepHipfire()
        {
            Id = "hipfire"; Chapter = "原理"; Title = "落点在右下";
            Body = "枪口在屏幕右下方，\n子弹从右下往屏幕中心打。\n到 25 米，正好落在中心。";
            DevNote = "塔科夫的腰射准星并不在屏幕正中间。枪口在右下方，子弹是从右下往屏幕中心打的，在 25 米处正好落到中心，也就是归零点。平时近距离腰射大多在 5–15 米，所以落点都在中心的右下方。\n"
                    + "站着不动时，推荐设置里准星在屏幕中心右下一点（「站立静止偏移」，后面会讲）；站着开枪弹孔不在那里，可以改它。近距离对枪基本都在移动中，这个工具要做的，是让移动扫射时准星对准弹孔。"; // Leo, 17 Nov 2026; v5.1 decision 5
            Prompts = new string[]
            {
                "点击{开枪键}开枪，看子弹落在哪", "再开一枪 · 墙推到 10 米", "再开一枪 · 墙推到 15 米", "再开一枪 · 墙推到 25 米",
                "25 米正好落在中心 · 点左上角的距离可以再看"
            };
        }

        public override void Enter(TutorialContext c)
        {
            // a clean gun: only the hipfire offset moves the impact here (no movement / lean / recoil values)
            Profile p = c.P.CloneValues();
            for (int i = 0; i < p.Dirs.Count; i++) { p.Dirs[i].OffX = 0; p.Dirs[i].OffY = 0; }
            p.LeanQX = p.LeanQY = p.LeanEX = p.LeanEY = 0;
            p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0;
            if (p.Jump != null) p.Jump.Enabled = false;
            rig.Setup(c, p, p, false);
            rig.Zoom = 4.0;
            rig.UseParallax = true;
            rig.ShowRing = false; rig.ShowCentre = true; rig.ShowAim = false;
            rig.SpreadPx = 0.8;
            rig.ShotIntervalMs = 400; // single shots (示意)
            for (int i = 0; i < 4; i++) recorded[i] = false;
            stage = 0; waitMs = 0; sinceShotMs = -1; userFired = false; doneMs = 0; autoPulse = false;
            SetStage(0, true);
            PromptIndex = 0;
        }

        private void SetStage(int s, bool now)
        {
            stage = s;
            if (now) rig.SetDistanceNow(Dist[s]); else rig.MoveWallTo(Dist[s]);
            rig.ClearHoles();
            rig.WallLabel = null;
            waitMs = 0; sinceShotMs = -1;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id >= 10 && id < 14) { SetStage(id - 10, false); PromptIndex = Done ? 4 : Math.Min(PromptIndex, 3); }
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool liveFire = c.LiveFire;
            if (liveFire) userFired = true;
            // automatic demo shot when nobody clicks
            if (!userFired && sinceShotMs < 0 && waitMs >= AutoShotMs && !autoPulse) { autoPulse = true; pulseLeft = 60; }
            bool fire = liveFire || autoPulse;
            if (autoPulse) { pulseLeft -= dtMs; if (pulseLeft <= 0) autoPulse = false; }
            InputState i = c.Live;
            i.Fwd = i.Back = i.Left = i.Right = false; // movement does not change this demo
            rig.Update(dtMs, i, fire);
            waitMs += dtMs;
            if (rig.ShotsThisFrame > 0)
            {
                bool first = !recorded[stage];
                recorded[stage] = true;
                double px, py;
                TutorialGeometry.HipImpactPx(rig.DistanceM, out px, out py);
                recX[stage] = px; recY[stage] = py;
                if (first) c.Term.Push(TermLine[stage]);
                if (sinceShotMs < 0) sinceShotMs = 0;
                if (!Done && recorded[0] && recorded[1] && recorded[2] && recorded[3] && stage != PromptIndex)
                {
                    Done = true; PromptIndex = 4; doneMs = 0; // all four distances seen (in any order, via the chips)
                }
            }
            if (sinceShotMs >= 0) sinceShotMs += dtMs;
            // next distance after the shot of this stage
            if (!Done && sinceShotMs >= AdvanceMs && stage == PromptIndex)
            {
                if (stage < 3) { SetStage(stage + 1, false); PromptIndex = stage; }
                else { Done = true; PromptIndex = 4; doneMs = 0; }
            }
            if (Done)
            {
                doneMs += dtMs;
                if (!userFired && c.Scripted && doneMs > LoopRestMs) // idle: replay the whole demo
                {
                    for (int k = 0; k < 4; k++) recorded[k] = false;
                    SetStage(0, false); PromptIndex = 0; Done = false; doneMs = 0;
                    c.Term.Clear();
                }
            }
            LitKeys = fire ? TKey.Fire : 0;
        }

        private static readonly string[] ImpactTag = { "落点 · 5 米", "落点 · 10 米", "落点 · 15 米" };
        private static readonly string[] TickLabel = { "5", "10", "15", "25 米" };
        private static readonly string[] DistNum = { "5", "10", "15" };
        private const string ZeroTag = "归零点 = 屏幕中心", CentreLabel = "屏幕中心";

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            Graphics g = gx.G;
            TutorialPalette pal = gx.Pal;
            // zoom (示意): as large as possible (up to 4x) while the 5 m impact and its label stay inside the scene AND sit
            // above-left of the gun's muzzle (the bullet flies from the muzzle up-left toward the centre, as the text says)
            double x5, y5;
            TutorialGeometry.HipImpactPx(Dist[0], out x5, out y5);
            float room = scene.Height / 2f - gx.LineH(gx.F.Tag) - gx.S(18);
            PointF muz = TutorialGfx.RestMuzzleOffset(scene);
            double zRoom = room / (Math.Abs(y5) * gx.K), zGun = 0.72 * muz.Y / (Math.Abs(y5) * gx.K);
            rig.Zoom = Math.Max(1.2, Math.Min(4.0, Math.Min(zRoom, zGun)));
            rig.Draw(gx, scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);
            // screen centre cross (+ label above it until the 25 m shot names it)
            Pen cross = gx.P(TutorialGfx.A(Color.White, 150), 1f);
            float cl = gx.S(10), gap = gx.S(4);
            g.DrawLine(cross, cx - cl - gap, cy, cx - gap, cy); g.DrawLine(cross, cx + gap, cy, cx + cl + gap, cy);
            g.DrawLine(cross, cx, cy - cl - gap, cx, cy - gap); g.DrawLine(cross, cx, cy + gap, cx, cy + cl + gap);
            bool zeroShown = stage == 3 && recorded[3] && sinceShotMs >= 0;
            if (!zeroShown) gx.Text(CentreLabel, gx.F.Tag, Color.White, cx, cy - cl - gap - gx.LineH(gx.F.Tag) - gx.S(2), 1);
            // recorded impacts per distance (screen space), dotted line converging to the centre; labels sit on the LEFT
            // of the markers (the side away from the gun)
            PointF last = PointF.Empty;
            bool have = false;
            for (int i = 0; i < 4; i++)
            {
                if (!recorded[i]) continue;
                PointF q = new PointF((float)(cx + recX[i] * z), (float)(cy - recY[i] * z));
                if (have) g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 160), 1.2f), last, q);
                bool cur = i == stage;
                float d = gx.S(cur ? 6 : 4.5f);
                PointF[] dia = gx.Pts(4);
                dia[0] = new PointF(q.X, q.Y - d); dia[1] = new PointF(q.X + d, q.Y); dia[2] = new PointF(q.X, q.Y + d); dia[3] = new PointF(q.X - d, q.Y);
                g.DrawPolygon(gx.P(cur ? pal.Hot : TutorialGfx.A(pal.Hot, 200), cur ? 1.8f : 1.2f), dia);
                if (!cur && i < 3) gx.TextMid(DistNum[i], gx.F.MonoSmall, pal.Hot, q.X - gx.S(9), q.Y, 2);
                last = q; have = true;
            }
            if (recorded[stage] && sinceShotMs >= 0)
            {
                PointF q = new PointF((float)(cx + recX[stage] * z), (float)(cy - recY[stage] * z));
                float th = gx.LineH(gx.F.Tag) + gx.S(5);
                if (stage == 3) gx.Tag(ZeroTag, q.X - gx.S(18), q.Y - th / 2, pal.Neon, 2);
                else gx.Tag(ImpactTag[stage], q.X - gx.S(12), q.Y - th / 2, pal.Hot, 2);
            }
            // HUD tag, the distance chips under it (top-left, away from the impacts and the gun), the side view top-right
            RectangleF tag = gx.Tag(DistTag[stage], scene.Left + gx.S(10), scene.Top + gx.S(10), stage == 3 ? pal.Neon : pal.Accent, 0);
            float chipH = gx.F.Button.GetHeight(g) + gx.S(10), chipGap = gx.S(8);
            float xx = scene.Left + gx.S(10), y0 = tag.Bottom + gx.S(8);
            for (int i = 0; i < 4; i++)
            {
                float w = gx.Measure(DistShort[i], gx.F.Button).Width + gx.S(22);
                gx.Chip(10 + i, new RectangleF(xx, y0, w, chipH), DistShort[i], i == stage);
                xx += w + chipGap;
            }
            if (!c.Compact) DrawSideView(gx, scene);
        }

        /// <summary>Side view (示意): sight line, bullet path from below the eye, meeting it at 25 m; current wall + impact.
        /// Sized from the label font so it stays tidy at every window size; labels sit in a left gutter.</summary>
        private void DrawSideView(TutorialGfx gx, RectangleF scene)
        {
            Graphics g = gx.G;
            TutorialPalette pal = gx.Pal;
            float fh = gx.F.MonoSmall.GetHeight(g);
            float w = Math.Min(Math.Max(gx.S(250), fh * 17), scene.Width * 0.42f), h = fh * 6.4f;
            RectangleF r = new RectangleF(scene.Right - w - gx.S(10), scene.Top + gx.S(10), w, h);
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 235)), r);
            g.DrawRectangle(gx.P(TutorialGfx.A(pal.Neon, 90), 1f), r.X, r.Y, r.Width, r.Height);
            gx.Text("侧视图", gx.F.MonoSmall, pal.Neon, r.Left + gx.S(6), r.Top + gx.S(3), 0);
            float gutter = gx.Measure("枪口", gx.F.MonoSmall).Width + gx.S(14);
            float x0 = r.Left + gutter, x1 = r.Right - gx.S(12);
            float ySight = r.Top + fh * 2.3f, yMuzzle = r.Top + fh * 4.0f, yTicks = r.Bottom - fh * 1.15f;
            double maxM = 30;
            // eye + sight line (labels in the left gutter)
            gx.Dot(pal.Text, x0, ySight, gx.S(3));
            g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Text, 150), 1f), x0, ySight, x1, ySight);
            gx.TextMid("视线", gx.F.MonoSmall, pal.Muted, x0 - gx.S(6), ySight, 2);
            // bullet path: from the muzzle (below the eye) to (25 m, sight line), continued to the edge
            float xz = x0 + (float)(TutorialGeometry.ZeroDistanceM / maxM) * (x1 - x0);
            float yEnd = (float)(yMuzzle + (ySight - yMuzzle) * ((x1 - x0) / (xz - x0)));
            g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, 220), 1.5f), x0, yMuzzle, x1, yEnd);
            gx.Dot(pal.Hot, x0, yMuzzle, gx.S(2.6f));
            gx.TextMid("枪口", gx.F.MonoSmall, pal.Hot, x0 - gx.S(6), yMuzzle, 2);
            // distance ticks + labels on one baseline
            for (int i = 0; i < 4; i++)
            {
                float tx = x0 + (float)(Dist[i] / maxM) * (x1 - x0);
                g.DrawLine(gx.P(TutorialGfx.A(pal.Muted, 120), 1f), tx, yTicks - gx.S(5), tx, yTicks);
                gx.Text(TickLabel[i], gx.F.MonoSmall, pal.Muted, tx, yTicks, 1);
            }
            gx.GlowDot(pal.Neon, xz, ySight, gx.S(2.6f));
            gx.Text("归零点", gx.F.MonoSmall, pal.Neon, xz - gx.S(6), ySight - fh * 1.15f, 2);
            // the wall now
            float xw = x0 + (float)(rig.DistanceM / maxM) * (x1 - x0);
            g.DrawLine(gx.P(pal.Accent, 2.2f), xw, ySight - fh * 0.4f, xw, yTicks - gx.S(7));
            float yHit = (float)(yMuzzle + (ySight - yMuzzle) * ((xw - x0) / (xz - x0)));
            gx.GlowDot(pal.Hot, xw, yHit, gx.S(2.4f));
        }
    }
}
