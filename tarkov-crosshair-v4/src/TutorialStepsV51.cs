// TutorialStepsV51.cs -- 首次设置教程 v5.1 pages (owner: W27). Pages for the v5.1 features:
//   站立静止偏移 (decision 5), 短按与反向 (7), 开镜完全隐藏, 丢雷隐藏 (2), 强化准星 (3, corrected 20 Nov: boost = the tactical
//   device MODE-SWITCH key, T keeps the device on in IR), 弹药提示 (1).
// TutorialStepsA.Create appends 站立 + 短按 after 前后 W/S; TutorialStepsB.AddSteps inserts the four feature pages before 完成.
// TutorialV51Math (top) is pure (System only): tests-v4 compiles only it (V51TUT_PURE_ONLY or TUTB_PURE_ONLY).
// C# 5. No WinForms. No allocation per frame in Update / Draw (strings are built on Enter or cached per value).
using System;
using System.Collections.Generic;
#if !V51TUT_PURE_ONLY && !TUTB_PURE_ONLY
using System.Drawing;
#endif

namespace TarkovCrosshair
{
    /// <summary>Pure maths of the v5.1 tutorial pages (unit-tested).</summary>
    public static class TutorialV51Math
    {
        /// <summary>The five 余弹 bands, fullest first. Only these words are shown, never a number.</summary>
        public static readonly string[] BandName = new string[] { "满", "大半", "一半", "少", "快空" };

        /// <summary>Band 0 (满) .. 4 (快空) of 'left' rounds in a 'mag' magazine. 0 rounds = 快空; bad input = 满.</summary>
        public static int AmmoBand(double left, double mag)
        {
            if (!(mag > 0) || double.IsNaN(left) || double.IsInfinity(left) || double.IsInfinity(mag)) return 0;
            double f = left / mag;
            if (f >= 0.9) return 0;
            if (f >= 0.6) return 1;
            if (f >= 0.35) return 2;
            if (f >= 0.15) return 3;
            return 4;
        }

        /// <summary>Rounds fired while the trigger is held for dtMs at rpm (full auto): dtMs x rpm / 60000.</summary>
        public static double RoundsFor(double dtMs, double rpm)
        {
            if (!(dtMs > 0) || !(rpm > 0) || double.IsInfinity(dtMs) || double.IsInfinity(rpm)) return 0;
            return dtMs * rpm / 60000.0;
        }

        /// <summary>R2: both reloads put in a NEW FULL magazine once the reload time is over; until then the old count stays.</summary>
        public static double AfterReload(double left, double mag, double tMs, double durMs)
        {
            if (!(mag > 0)) return 0;
            if (double.IsNaN(left) || left < 0) left = 0;
            if (left > mag) left = mag;
            if (!(durMs > 0) || tMs >= durMs) return mag;
            return left;
        }

        /// <summary>Share of the reload time the old magazine takes to leave (normal: slides out; quick: dropped at once).</summary>
        public const double NormalOutShare = 0.4, QuickOutShare = 0.15;
        /// <summary>Share of the reload time after which the new full magazine starts to slide in.</summary>
        public const double NormalInFrom = 0.5, QuickInFrom = 0.2;

        /// <summary>Mag-swap animation at tMs into a reload of durMs: oldOut 0 (in place) .. 1 (gone), newIn 0 (not shown) ..
        /// 1 (seated). NaN / bad input = finished swap (old gone, new seated).</summary>
        public static void Swap(double tMs, double durMs, bool quick, out double oldOut, out double newIn)
        {
            oldOut = 1; newIn = 1;
            if (!(durMs > 0) || double.IsNaN(tMs) || double.IsInfinity(durMs)) return;
            if (tMs < 0) tMs = 0;
            double outEnd = durMs * (quick ? QuickOutShare : NormalOutShare), inFrom = durMs * (quick ? QuickInFrom : NormalInFrom);
            oldOut = tMs >= outEnd ? 1 : TutorialGeometry.Smooth(tMs / outEnd);
            newIn = tMs <= inFrom ? 0 : (tMs >= durMs ? 1 : TutorialGeometry.Smooth((tMs - inFrom) / (durMs - inFrom)));
        }

        /// <summary>Reload times when the settings have none yet (R2 defaults).</summary>
        public const double DefaultNormalMs = 2400, DefaultQuickMs = 900;

        /// <summary>Decision 5 example: a direction's offset measured from the standing point = offset from the screen
        /// centre minus the standing offset (percent of the screen). Standing right 1 %, D at -4.17 % -> -5.17 %.</summary>
        public static double FromStand(double offFromCentre, double stand)
        {
            if (double.IsNaN(offFromCentre) || double.IsInfinity(offFromCentre)) return 0;
            if (double.IsNaN(stand) || double.IsInfinity(stand)) stand = 0;
            return offFromCentre - stand;
        }

        /// <summary>「← 5.17%」 / 「→ 1.00%」 (two decimals, arrow = direction). Allocates: call on change only.</summary>
        public static string PctText(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            string a = v < 0 ? "← " : "→ ";
            return a + Math.Abs(v).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Boost after one press of the boost key (the device mode-switch key). Toggle mode flips it; hold mode
        /// follows the key (down = on). Each 切换 press = IR -> 手电 (on) or 手电 -> IR (off).</summary>
        public static bool BoostAfter(bool on, bool toggleMode, bool keyDown, bool pressEdge)
        {
            if (!toggleMode) return keyDown;
            return pressEdge ? !on : on;
        }

        /// <summary>Grenade toggle: each press of the grenade key flips hidden; it never comes back by itself.</summary>
        public static bool GrenadeAfter(bool hidden, bool pressEdge) { return pressEdge ? !hidden : hidden; }

        /// <summary>Tap-demo loop (ms) and key segments {from, to, TKey bits}: tap D, tap A, hold A, then switch to D.</summary>
        public const int TapLoopMs = 8200;
        public static readonly int[] TapScript = new int[]
        {
            700, 780, TKey.D,
            1900, 1980, TKey.A,
            3100, 4700, TKey.A,
            4700, 6500, TKey.D
        };

        /// <summary>Phase shown above the tap demo at loop time t: 0 = 短按, 1 = 按住, 2 = 反向, 3 = 松开 (-1 = idle).</summary>
        public static int TapPhase(double tMs)
        {
            double t = tMs > 0 ? tMs % TapLoopMs : 0;
            if (t >= 600 && t < 3000) return 0;
            if (t >= 3000 && t < 4700) return 1;
            if (t >= 4700 && t < 6500) return 2;
            if (t >= 6500) return 3;
            return -1;
        }

        /// <summary>Fit a w:h rectangle (aspect) into avail, centred. Bad input = avail.</summary>
        public static void Fit(double ax, double ay, double aw, double ah, double aspect, out double x, out double y, out double w, out double h)
        {
            x = ax; y = ay; w = aw; h = ah;
            if (!(aw > 0) || !(ah > 0) || !(aspect > 0) || double.IsInfinity(aspect)) return;
            if (aw / ah > aspect) { w = ah * aspect; x = ax + (aw - w) / 2; }
            else { h = aw / aspect; y = ay + (ah - h) / 2; }
        }
    }

#if !V51TUT_PURE_ONLY && !TUTB_PURE_ONLY
    /// <summary>Adds the v5.1 pages to the page list (called by TutorialStepsA / TutorialStepsB).</summary>
    public static class TutorialStepsV51
    {
        /// <summary>After 前后 W/S: 站立静止偏移, 短按与反向.</summary>
        public static void AddCalibration(List<TutorialStep> l)
        {
            l.Add(new TutorialStepStandZero());
            l.Add(new TutorialStepTaps());
        }

        /// <summary>Before 完成 (or at the end when there is none): 开镜, 丢雷, 强化, 弹药.</summary>
        public static void AddFeatures(List<TutorialStep> l)
        {
            int at = l.Count;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].Id == "done") { at = i; break; }
            l.Insert(at, new TutorialStepAmmo());
            l.Insert(at, new TutorialStepBoost());
            l.Insert(at, new TutorialStepGrenade());
            l.Insert(at, new TutorialStepAds());
        }

        /// <summary>A millisecond setting by name (read once per Enter, so this file compiles before the field exists);
        /// missing / not positive = fallback.</summary>
        public static double Ms(GlobalSettings g, string field, double fallback)
        {
            if (g == null) return fallback;
            try
            {
                Type t = g.GetType();
                object v = null;
                System.Reflection.FieldInfo f = t.GetField(field);
                if (f != null) v = f.GetValue(g);
                else { System.Reflection.PropertyInfo pi = t.GetProperty(field); if (pi != null) v = pi.GetValue(g, null); }
                if (v == null) return fallback;
                double d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                return d > 0 && !double.IsInfinity(d) ? d : fallback;
            }
            catch (Exception) { return fallback; }
        }

        /// <summary>Running-text name of a virtual-key code (「G」, 「R」 ...).</summary>
        public static string Vk(int vk, string fallback)
        {
            if (vk <= 0) return fallback;
            try { string s = KeyBindings.KeyName(vk); return string.IsNullOrEmpty(s) ? fallback : s; } catch (Exception) { return fallback; }
        }
    }

    /// <summary>
    /// Shared scene for the feature pages: a 16:9 「游戏画面」 with the screen centre, the crosshair ring, a status line in
    /// words (the same words as the home page) and up to three chips under it. Layout is cached per scene size / DPI / font.
    /// </summary>
    internal abstract class TutorialV51Scene : TutorialStep
    {
        protected const int Chip1 = 1, Chip2 = 2, Chip3 = 3;
        protected RectangleF screen, chipRow;
        protected float ringD, chipH;
        private float lW = -1, lH = -1, lK = -1; private TutorialFonts lF; private bool lC;
        protected string[] ChipText = new string[0];

        protected void Layout(TutorialGfx gx, RectangleF scene, bool compact)
        {
            if (scene.Width == lW && scene.Height == lH && gx.K == lK && ReferenceEquals(gx.F, lF) && compact == lC) return;
            lW = scene.Width; lH = scene.Height; lK = gx.K; lF = gx.F; lC = compact;
            float pad = gx.S(compact ? 8 : 12);
            chipH = gx.Measure("开镜", gx.F.Button).Height + gx.S(12);
            float rowH = ChipText.Length > 0 ? chipH + pad : 0;
            double x, y, w, h;
            TutorialV51Math.Fit(scene.X + pad, scene.Y + pad, scene.Width - 2 * pad, scene.Height - 2 * pad - rowH, 16.0 / 9.0, out x, out y, out w, out h);
            screen = new RectangleF((float)x, (float)y, (float)w, (float)h);
            chipRow = new RectangleF(scene.X + pad, scene.Bottom - pad - chipH, scene.Width - 2 * pad, chipH);
            ringD = Math.Max(gx.S(18), Math.Min(gx.S(30), screen.Height * 0.09f));
        }

        protected void DrawScreen(TutorialGfx gx, string status, Color statusColor)
        {
            Graphics g = gx.G;
            TutorialPalette p = gx.Pal;
            gx.RegisterView(screen, false);
            g.FillRectangle(gx.B(TutorialGfx.A(p.Panel, 235)), screen);
            g.DrawRectangle(gx.P(TutorialGfx.A(p.Neon, 70), 1f), screen.X, screen.Y, screen.Width, screen.Height);
            gx.HudFrame(screen, TutorialGfx.A(p.Neon, 200), 10);
            gx.Wall(Cx, Cy, screen.Width * 0.62f, screen.Height * 0.62f, TutorialGfx.A(p.Accent, 120), null);
            if (!string.IsNullOrEmpty(status)) gx.Tag(status, screen.X + gx.S(8), screen.Y + gx.S(8), statusColor, 0);
        }

        protected float Cx { get { return screen.X + screen.Width / 2f; } }
        protected float Cy { get { return screen.Y + screen.Height / 2f; } }

        protected void CentreMark(TutorialGfx gx)
        {
            float r = gx.S(2.2f);
            gx.Dot(TutorialGfx.A(gx.Pal.Muted, 200), Cx, Cy, r);
        }

        /// <summary>The crosshair ring at (x, y); alpha 0 = fully invisible (nothing drawn).</summary>
        protected void Ring(TutorialGfx gx, float x, float y, double alpha, float scale, bool glow)
        {
            if (alpha <= 0.01) return;
            Color c = TutorialGfx.A(gx.Pal.Neon, alpha);
            if (glow) gx.GlowCircle(TutorialGfx.A(gx.Pal.Neon, alpha * 0.55), gx.S(3), x, y, ringD * scale / 2f);
            gx.Ring(x, y, ringD * scale, c, TutorialGfx.A(Color.Black, alpha * 0.9), gx.S(glow ? 2.6f : 1.8f));
        }

        protected void DrawChips(TutorialGfx gx, bool[] active)
        {
            int n = ChipText.Length;
            if (n == 0) return;
            float gap = gx.S(8);
            float w = 0;
            for (int i = 0; i < n; i++) w = Math.Max(w, gx.Measure(ChipText[i], gx.F.Button).Width + gx.S(24));
            float total = n * w + (n - 1) * gap;
            if (total > chipRow.Width) { w = (chipRow.Width - (n - 1) * gap) / n; total = chipRow.Width; }
            float x = chipRow.X + (chipRow.Width - total) / 2f;
            for (int i = 0; i < n; i++)
            {
                gx.Chip(i + 1, new RectangleF(x, chipRow.Y, w, chipH), ChipText[i], active != null && i < active.Length && active[i]);
                x += w + gap;
            }
        }
    }

    // ===================================== 站立静止偏移 =====================================

    /// <summary>
    /// Decision 5: the standing point becomes the new zero; every other offset is converted so its place on screen stays.
    /// Demo uses the brief's example (standing right 1 %, D at ← 4.17 % from the centre = ← 5.17 % from the standing point).
    /// The chip switches 默认 / 填了站立偏移; the scripted loop switches every 3.5 s until the user clicks.
    /// </summary>
    internal sealed class TutorialStepStandZero : TutorialV51Scene
    {
        private const double StandPct = 1.0, DPct = -4.17;
        /// <summary>Demo exaggeration so 1 % is visible in the small scene (positions only; the numbers are the real example).</summary>
        private const float Zoom = 4f;
        private bool userSet, standOn;
        private readonly string txtFromCentre = "D 离屏幕中心 " + TutorialV51Math.PctText(DPct);
        private readonly string txtFromStand = "D 离站立点 " + TutorialV51Math.PctText(TutorialV51Math.FromStand(DPct, StandPct));
        private readonly string txtStand = "站立点 " + TutorialV51Math.PctText(StandPct);
        private const string StatusOff = "零点 = 屏幕中心", StatusOn = "零点 = 站立点";
        private readonly bool[] act = new bool[2];

        public TutorialStepStandZero()
        {
            Id = "standZero"; Chapter = "实战校准"; Title = "站立静止偏移（可选）";
            Body = "站着开枪，弹孔和屏幕中心对不上时用它。\n填了以后，站立点就是新的零点。\n其他方向自动换算，准星位置不变。";
            DevNote = "默认是 0，大多数人不用填。例：站立往右 1%，D 离屏幕中心 ← 4.17%，换算后离站立点 ← 5.17%，准星位置不变。改之前会弹出确认框；按 F12 复位，准星回到站立点。";
            Hint = "点下面两个按钮对比一下";
            ChipText = new string[] { "默认：屏幕中心", "填了站立偏移" };
            ClickFires = false;
            DoneOnNext = true;
            SettingsPage = TutorialPages.Move;
        }

        public override void Enter(TutorialContext c) { userSet = false; standOn = false; }

        public override void Update(TutorialContext c, double dtMs)
        {
            if (!userSet && !c.ReducedMotion) standOn = ((int)(c.StepMs / 3500)) % 2 == 1;
            LitKeys = 0;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id == Chip1) { standOn = false; userSet = true; }
            else if (id == Chip2) { standOn = true; userSet = true; Done = true; }
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            Graphics g = gx.G;
            gx.SceneBackground(scene);
            Layout(gx, scene, c.Compact);
            TutorialPalette p = gx.Pal;
            DrawScreen(gx, standOn ? StatusOn : StatusOff, p.Accent);
            float unit = screen.Width / 100f * Zoom;   // px per 1 % of screen width (zoomed)
            float sx = Cx + (float)(StandPct * unit), dx = Cx + (float)(DPct * unit), y = Cy;
            CentreMark(gx);
            gx.Text("屏幕中心", gx.F.MonoSmall, p.Muted, Cx, Cy - ringD / 2f - gx.LineH(gx.F.MonoSmall) - gx.S(16), 1);
            float zeroX = standOn ? sx : Cx;
            // standing point ring (the crosshair while standing) and the D position (same place in both modes)
            if (standOn) Ring(gx, sx, y, 1, 1, false);
            else gx.Ring(sx, y, ringD, TutorialGfx.A(p.Neon, 80), TutorialGfx.A(Color.Black, 120), gx.S(1.2f));
            Ring(gx, dx, y, 1, 1, false);
            gx.Text("D", gx.F.Mono, p.KeyColor(TKey.D), dx, y - ringD / 2f - gx.LineH(gx.F.Mono) - gx.S(2), 1);
            // measuring line from the zero point to D
            float ly = y + ringD / 2f + gx.S(c.Compact ? 12 : 18);
            gx.Arrow(p.Accent, gx.S(1.6f), zeroX, ly, dx, ly);
            g.DrawLine(gx.Dashed(TutorialGfx.A(p.Accent, 160), gx.S(1)), zeroX, y, zeroX, ly + gx.S(4));
            gx.Tag(standOn ? txtFromStand : txtFromCentre, (zeroX + dx) / 2f, ly + gx.S(6), p.Accent, 1);
            if (!c.Compact && standOn) gx.Text(txtStand, gx.F.MonoSmall, p.Neon, sx + ringD / 2f + gx.S(4), y - gx.LineH(gx.F.MonoSmall) / 2f, 0);
            act[0] = !standOn; act[1] = standOn;
            DrawChips(gx, act);
            g.ResetClip();
        }
    }

    // ===================================== 短按与反向 =====================================

    /// <summary>
    /// Decision 7: the user's own profile in the real MotionEngine (FpRig, ring = truth). Taps move a little and come back;
    /// A -> D starts from where the crosshair is and eases out (slow, then faster), never jumps. A trace plots X over time.
    /// Live A / D (or the script) drive it.
    /// </summary>
    internal sealed class TutorialStepTaps : TutorialStep
    {
        private readonly FpRig rig = new FpRig();
        private readonly TutorialScript script;
        private const int TraceN = 160;
        private const double TraceStepMs = 40;
        private readonly double[] trace = new double[TraceN];
        private int head, count;
        private double acc, maxAbs = 20;
        private int liveSeen;
        private Profile leo, demoP;
        private static readonly string[] PhaseText = new string[] { "短按：只动一点，马上回来", "按住 A：往左", "换成 D：先慢下来，再加速往右", "松开：回到原位" };
        private const string PlotTitle = "准星左右位置 / 时间";

        public TutorialStepTaps()
        {
            Id = "taps"; Chapter = "实战校准"; Title = "短按与反向";
            Body = "轻点 A 或 D：准星只挪一点，马上回来。\n按住 A 再换 D：准星先慢下来，\n再加速往右，不会一下跳过去。";
            DevNote = "游戏里轻点方向键，人物只挪一小步，枪口几乎不偏，所以准星也只动一点。反向时枪口要先停下来再往另一边偏，准星按同样的节奏走，和弹孔对得上。手感不对，可以在「移动」页的高级设置里调「短按阈值」（默认 120 毫秒）和「换向最短时间」（默认 150 毫秒）。";
            Prompts = new string[] { "轻点 A、D，再按住 A 换成 D，看准星怎么走" };
            Hint = Prompts[0];
            KeysShown = TKey.A | TKey.D;
            ClickFires = true;
            int[] sc = TutorialV51Math.TapScript;
            ScriptSeg[] segs = new ScriptSeg[sc.Length / 3];
            for (int i = 0; i < segs.Length; i++) segs[i] = new ScriptSeg(sc[3 * i], sc[3 * i + 1], sc[3 * i + 2]);
            script = new TutorialScript(TutorialV51Math.TapLoopMs, segs);
            SettingsPage = TutorialPages.Move;
        }

        public override void Enter(TutorialContext c)
        {
            // the user's own offsets; while they are still all 0 (fresh install) Leo's defaults show the movement
            Profile src = c.P;
            bool any = false;
            if (src != null && src.Dirs != null)
                for (int i = 0; i < 4 && i < src.Dirs.Count; i++) if (Math.Abs(src.Dirs[i].OffX) > 0.01) any = true;
            if (!any) { if (leo == null) leo = Profile.CreateLeoDefault("参考"); src = leo; }
            rig.Setup(c, src, src, false);
            demoP = src;
            rig.SetDistanceNow(10);
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallLabel = "10 米";
            ClearTrace(); acc = 0;
            double m = 20;
            if (demoP != null && demoP.Dirs != null)
                for (int i = 0; i < 4 && i < demoP.Dirs.Count; i++) m = Math.Max(m, Math.Abs(demoP.Dirs[i].OffX));
            maxAbs = m * 1.15;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire; int lit;
            InputState i = c.Input(script, out fire, out lit);
            LitKeys = lit;
            rig.Update(dtMs, i, fire);
            liveSeen |= c.LiveMask & (TKey.A | TKey.D);
            if (liveSeen == (TKey.A | TKey.D)) Done = true;
            if (c.Arbiter.Resumed) ClearTrace();
            acc += dtMs;
            while (acc >= TraceStepMs)
            {
                acc -= TraceStepMs;
                trace[head] = rig.RingX; head = (head + 1) % TraceN; if (count < TraceN) count++;
            }
        }

        /// <summary>The trace always holds TraceN samples (0 = centre) so the point array is one fixed size (no per-frame allocation).</summary>
        private void ClearTrace() { Array.Clear(trace, 0, TraceN); head = 0; count = TraceN; }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            Graphics g = gx.G;
            rig.Draw(gx, scene);
            g.ResetClip();
            TutorialPalette p = gx.Pal;
            if (c.Scripted)
            {
                int ph = TutorialV51Math.TapPhase(c.Arbiter.ScriptMs);
                if (ph >= 0) gx.Tag(PhaseText[ph], scene.X + scene.Width / 2f, scene.Y + gx.S(10), p.Accent, 1);
            }
            if (c.Compact || count < 2) return;
            float pw = Math.Min(gx.S(300), scene.Width * 0.38f), ph2 = Math.Min(gx.S(110), scene.Height * 0.3f);
            RectangleF r = new RectangleF(scene.Right - pw - gx.S(10), scene.Bottom - ph2 - gx.S(10), pw, ph2);
            gx.RegisterView(r, false);
            g.FillRectangle(gx.B(TutorialGfx.A(p.Panel, 225)), r);
            g.DrawRectangle(gx.P(TutorialGfx.A(p.Neon, 70), 1f), r.X, r.Y, r.Width, r.Height);
            float th = gx.LineH(gx.F.MonoSmall);
            gx.Text(PlotTitle, gx.F.MonoSmall, TutorialGfx.A(p.Neon, 210), r.X + gx.S(6), r.Y + gx.S(4), 0);
            float top = r.Y + th + gx.S(8), bot = r.Bottom - gx.S(6), mid = (top + bot) / 2f;
            g.DrawLine(gx.Dashed(TutorialGfx.A(p.Muted, 140), 1f), r.X + gx.S(6), mid, r.Right - gx.S(6), mid);
            PointF[] pts = gx.Pts(count);
            float x0 = r.X + gx.S(6), xw = r.Width - gx.S(12);
            int start = (head - count + TraceN) % TraceN;
            for (int i = 0; i < count; i++)
            {
                double v = trace[(start + i) % TraceN] / maxAbs;
                if (double.IsNaN(v)) v = 0;
                if (v > 1) v = 1; if (v < -1) v = -1;
                // rig X: left of centre = up on the plot (labels 左 / 右 at the left edge)
                pts[i] = new PointF(x0 + xw * i / (float)(TraceN - 1), mid - (float)(v * (bot - top) / 2f));
            }
            g.DrawLines(gx.P(p.Neon, gx.S(1.6f)), pts);
            gx.Text("左", gx.F.MonoSmall, p.Muted, r.Right - gx.S(6), top, 2);
            gx.Text("右", gx.F.MonoSmall, p.Muted, r.Right - gx.S(6), bot - th, 2);
        }
    }

    // ===================================== 开镜完全隐藏 =====================================

    internal sealed class TutorialStepAds : TutorialV51Scene
    {
        private bool ads, userSet;
        private double fade = 1;
        private readonly bool[] act = new bool[1];
        private const string StatusOn = "已隐藏：开镜", StatusOff = "腰射：准星显示";

        public TutorialStepAds()
        {
            Id = "ads"; Chapter = "功能"; Title = "开镜时完全隐藏";
            Body = "开镜时，准星完全消失。\n松开开镜键，准星回来。\n开镜还看到准星？检查「按键」页的开镜方式。";
            DevNote = "开镜方式有「按住」和「切换」两种，要和游戏里的设置一样：切换方式下，再按一次开镜键准星才回来。丢雷、强化的状态也会写在首页的状态行里。";
            Hint = "点「开镜」试一下";
            ChipText = new string[] { "开镜" };
            ClickFires = false;
            DoneOnNext = true;
            SettingsPage = TutorialPages.Keys;
        }

        public override void Enter(TutorialContext c)
        {
            userSet = false; ads = false; fade = 1;
            ChipText[0] = "开镜（" + c.PromptKey(KeyBindings.ActionAds) + "）";
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            if (!userSet && !c.ReducedMotion) ads = ((int)(c.StepMs / 2600)) % 2 == 1;
            fade = ads ? 0 : 1;   // instant: the overlay hides on the same frame
        }

        public override void OnChip(TutorialContext c, int id) { if (id == Chip1) { ads = !ads; userSet = true; Done = true; } }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            gx.SceneBackground(scene);
            Layout(gx, scene, c.Compact);
            DrawScreen(gx, ads ? StatusOn : StatusOff, ads ? gx.Pal.Accent : gx.Pal.Neon);
            CentreMark(gx);
            if (ads)
            {
                // scope picture: a dark vignette ring, nothing of ours on it
                float r = screen.Height * 0.34f;
                gx.G.DrawEllipse(gx.P(TutorialGfx.A(gx.Pal.Muted, 160), gx.S(2)), Cx - r, Cy - r, 2 * r, 2 * r);
                gx.G.DrawLine(gx.P(TutorialGfx.A(gx.Pal.Muted, 160), 1f), Cx - r, Cy, Cx + r, Cy);
                gx.G.DrawLine(gx.P(TutorialGfx.A(gx.Pal.Muted, 160), 1f), Cx, Cy - r, Cx, Cy + r);
            }
            Ring(gx, Cx + screen.Width * 0.04f, Cy + screen.Height * 0.05f, fade, 1, false);
            act[0] = ads;
            DrawChips(gx, act);
            gx.G.ResetClip();
        }
    }

    // ===================================== 丢雷隐藏 =====================================

    internal sealed class TutorialStepGrenade : TutorialV51Scene
    {
        private bool hidden, userSet;
        private int presses;
        private readonly bool[] act = new bool[2];
        private const string StatusOn = "已隐藏：丢雷", StatusOff = "准星显示";
        private string key = "G";

        public TutorialStepGrenade()
        {
            Id = "grenade"; Chapter = "功能"; Title = "丢雷时隐藏";
            ChipText = new string[] { "按 G", "按两下 F12（复位）" };
            ClickFires = false;
            DoneOnNext = true;
            SettingsPage = TutorialPages.Keys;
        }

        public override void Enter(TutorialContext c)
        {
            key = TutorialStepsV51.Vk(c.G.GrenadeKey, "G");
            Body = "按 " + key + " 拿雷，准星隐藏。\n再按一次 " + key + "，准星回来。\n它不会自己回来；按两下 F12 也会显示。";
            DevNote = "这个功能可以关掉，按键也能改，都在「按键」页。";
            Hint = "点「按 " + key + "」试一下";
            ChipText[0] = "按 " + key;
            userSet = false; hidden = false; presses = 0;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            if (!userSet && !c.ReducedMotion) hidden = ((int)(c.StepMs / 2800)) % 2 == 1;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            userSet = true;
            if (id == Chip1) { hidden = TutorialV51Math.GrenadeAfter(hidden, true); presses++; if (presses >= 2) Done = true; }
            else if (id == Chip2) hidden = false;
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            gx.SceneBackground(scene);
            Layout(gx, scene, c.Compact);
            DrawScreen(gx, hidden ? StatusOn : StatusOff, hidden ? gx.Pal.Accent : gx.Pal.Neon);
            CentreMark(gx);
            if (hidden)
            {
                // a grenade in hand, bottom right
                float r = screen.Height * 0.08f, gxp = screen.Right - screen.Width * 0.2f, gyp = screen.Bottom - screen.Height * 0.2f;
                gx.G.FillEllipse(gx.B(TutorialGfx.A(gx.Pal.Muted, 150)), gxp - r, gyp - r * 1.2f, 2 * r, 2.4f * r);
                gx.G.DrawRectangle(gx.P(TutorialGfx.A(gx.Pal.Muted, 200), gx.S(1.5f)), gxp - r * 0.35f, gyp - r * 1.7f, r * 0.7f, r * 0.5f);
            }
            Ring(gx, Cx + screen.Width * 0.04f, Cy + screen.Height * 0.05f, hidden ? 0 : 1, 1, false);
            act[0] = hidden; act[1] = false;
            DrawChips(gx, act);
            gx.G.ResetClip();
        }
    }

    // ===================================== 强化准星 =====================================

    /// <summary>
    /// Decision 3 (corrected 20 Nov): T turns the tactical device on and it stays on (IR). The boost key is the device
    /// MODE-SWITCH key (IR <-> 手电): one press = 手电 + 强化准星 on, next press = IR + off. T does nothing to boost.
    /// </summary>
    internal sealed class TutorialStepBoost : TutorialV51Scene
    {
        private bool on, userSet, toggle = true, held;
        private double holdLeft;
        private int presses;
        private readonly bool[] act = new bool[2];
        private const string StatusOn = "强化：开（手电）", StatusOff = "强化：关（IR）";
        private string modeKey = "";

        public TutorialStepBoost()
        {
            Id = "boost"; Chapter = "功能"; Title = "强化准星";
            ChipText = new string[] { "", "按 T" };
            ClickFires = false;
            DoneOnNext = true;
            SettingsPage = TutorialPages.Keys;
        }

        public override void Enter(TutorialContext c)
        {
            modeKey = c.PromptKey(KeyBindings.ActionBoost);
            if (string.IsNullOrEmpty(modeKey) || modeKey == "?") modeKey = KeyBindings.ComboName(0x54, KeyBindings.ModCtrl);   // Tarkov default 切换战术装置模式
            toggle = c.G.BoostToggle;
            if (toggle)
                Body = "先按 T 打开战术装置，保持 IR 模式。\n按 " + modeKey + "：切到手电，准星同时变亮。\n再按一次：回到 IR，准星恢复。";
            else
                Body = "先按 T 打开战术装置，保持 IR 模式。\n按住 " + modeKey + "：切到手电，准星变亮。\n松开：回到 IR，准星恢复。";
            DevNote = "强烈推荐 WMX200 的 IR 模式（任何战术装置的 IR 模式都行），不要准星和激光一起开。T 只管装置开关，T 不管强化；强化跟着 " + modeKey + "（切换装置模式）走：平时开 IR，接敌时切到手电，准星同时变亮。按 F12 复位时，强化会关掉。强化键和「按住 / 切换」都在「按键」页改。";
            Hint = "点「按 " + modeKey + "」试一下";
            ChipText[0] = "按 " + modeKey;
            userSet = false; on = false; presses = 0; held = false; holdLeft = 0;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            if (!userSet && !c.ReducedMotion) on = ((int)(c.StepMs / 2800)) % 2 == 1;
            if (userSet && !toggle)
            {
                // hold mode: a chip click = a short hold of the key
                holdLeft -= dtMs;
                held = holdLeft > 0;
                on = TutorialV51Math.BoostAfter(on, false, held, false);
            }
        }

        public override void OnChip(TutorialContext c, int id)
        {
            userSet = true;
            if (id == Chip1)
            {
                presses++;
                if (toggle) on = TutorialV51Math.BoostAfter(on, true, true, true);
                else holdLeft = 1500;
                if (presses >= 2 || !toggle) Done = true;
            }
            // Chip2 = T: the device stays on; boost unchanged
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            gx.SceneBackground(scene);
            Layout(gx, scene, c.Compact);
            TutorialPalette p = gx.Pal;
            DrawScreen(gx, on ? StatusOn : StatusOff, on ? p.Neon : p.Muted);
            // the device beam: IR = faint (invisible to the eye), 手电 = a bright cone on the wall
            if (on)
            {
                float r = screen.Height * 0.3f;
                gx.G.FillEllipse(gx.B(TutorialGfx.A(Color.White, 34)), Cx - r, Cy - r * 0.8f, 2 * r, 1.6f * r);
            }
            CentreMark(gx);
            Ring(gx, Cx + screen.Width * 0.04f, Cy + screen.Height * 0.05f, 1, on ? 1.25f : 1f, on);
            act[0] = on; act[1] = false;
            DrawChips(gx, act);
            gx.G.ResetClip();
        }
    }

    // ===================================== 弹药提示 =====================================

    /// <summary>
    /// Decision 1: main weapon only; count = trigger hold time x RPM (半自动: 1 per click); only the five bands are shown.
    /// Side brackets fill with the rounds left and colour green -> yellow -> red; the centre stays empty; the ring keeps its
    /// own colour. R2: R = normal reload, double-tap R = quick reload; both swap in a NEW FULL magazine after the reload
    /// time (the old bar slides out / is dropped, the new full bar slides in; quick is faster).
    /// Hold the left mouse button on the scene to fire (or watch the script).
    /// </summary>
    internal sealed class TutorialStepAmmo : TutorialV51Scene
    {
        private double normalMs = TutorialV51Math.DefaultNormalMs, quickMs = TutorialV51Math.DefaultQuickMs;
        private double mag = 30, rpm = 600, left = 30;
        private bool semi, prevFire;
        private int reload;   // 0 none, 1 normal, 2 quick
        private double reloadMs, reloadFrom;
        private bool userActed;
        private double scriptMs;
        private int band;
        private readonly bool[] act = new bool[2];
        private readonly string[] bandStatus = new string[5];
        private const string StatusNormal = "换弹中：新弹匣", StatusQuick = "快速换弹：旧弹匣丢掉";
        private string reloadKey = "R";

        public TutorialStepAmmo()
        {
            Id = "ammo"; Chapter = "功能"; Title = "弹药提示（主武器）";
            ChipText = new string[] { "按 R：换弹", "双击 R：快速换弹" };
            ClickFires = true;
            DoneOnNext = true;
            SettingsPage = TutorialPages.Look;
            for (int i = 0; i < 5; i++) bandStatus[i] = "余弹：" + TutorialV51Math.BandName[i];
        }

        public override void Enter(TutorialContext c)
        {
            GlobalSettings g = c.G;
            mag = g.AmmoMagSize > 0 ? g.AmmoMagSize : 30;
            rpm = g.AmmoRpm > 0 ? g.AmmoRpm : 600;
            semi = g.AmmoSemi;
            reloadKey = TutorialStepsV51.Vk(g.AmmoReloadKey, "R");
            normalMs = TutorialStepsV51.Ms(g, "AmmoReloadMs", TutorialV51Math.DefaultNormalMs);
            quickMs = TutorialStepsV51.Ms(g, "AmmoQuickReloadMs", TutorialV51Math.DefaultQuickMs);
            Body = "只算主武器：按住开枪的时间 × 射速。\n只显示大概：满、大半、一半、少、快空。\n按 " + reloadKey + " 或双击 " + reloadKey + "：换上一个新的满弹匣。";
            DevNote = "先在「外观」页打开它（默认关闭），填好弹匣容量和射速（每分钟发数）；半自动按一下算一发。两种换弹都是换上一个新的满弹匣：按 " + reloadKey + " 是普通换弹，旧弹匣退出，新弹匣装上；快速连按两下 " + reloadKey + " 是快速换弹，旧弹匣直接丢在地上，新弹匣装得更快。换弹时间在设置里和局内「常调」都能改。开镜时它和准星一起隐藏。准星本身的颜色不变，中间一直是空的。几种样式可以叠加。";
            Hint = "在画面上按住左键开枪，再点换弹";
            ChipText[0] = "按 " + reloadKey + "：换弹";
            ChipText[1] = "双击 " + reloadKey + "：快速换弹";
            left = mag; reload = 0; userActed = false; scriptMs = 0; prevFire = false;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire = c.LiveFire;
            if (fire) userActed = true;
            if (!userActed && !c.ReducedMotion)
            {
                // script: fire bursts until 少, then a normal reload; second loop ends with a quick reload
                scriptMs += dtMs;
                double t = scriptMs % 9000;
                fire = (t > 400 && t < 1300) || (t > 1800 && t < 2500);
                if (t >= 3000 && t - dtMs < 3000) StartReload(scriptMs % 18000 < 9000 ? 1 : 2);
            }
            if (reload != 0)
            {
                reloadMs += dtMs;
                double dur = reload == 1 ? normalMs : quickMs;
                left = TutorialV51Math.AfterReload(reloadFrom, mag, reloadMs, dur);
                if (reloadMs >= dur) reload = 0;
            }
            else if (fire)
            {
                if (semi) { if (!prevFire) left -= 1; }
                else left -= TutorialV51Math.RoundsFor(dtMs, rpm);
                if (left < 0) left = 0;
            }
            prevFire = fire;
            band = TutorialV51Math.AmmoBand(left, mag);
        }

        private void StartReload(int kind) { reload = kind; reloadMs = 0; reloadFrom = left; }

        public override void OnChip(TutorialContext c, int id)
        {
            userActed = true;
            if (id == Chip1) StartReload(1);
            else if (id == Chip2) { StartReload(2); Done = true; }
        }

        private static Color BandColour(TutorialPalette p, int b)
        {
            if (b <= 1) return p.Neon;
            if (b == 2) return TutorialPalette.Mix(p.Neon, p.Accent, 0.6);
            if (b == 3) return p.Accent;
            return p.Red;
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            Graphics g = gx.G;
            gx.SceneBackground(scene);
            Layout(gx, scene, c.Compact);
            TutorialPalette p = gx.Pal;
            Color bc = BandColour(p, band);
            DrawScreen(gx, reload == 1 ? StatusNormal : (reload == 2 ? StatusQuick : bandStatus[band]), reload != 0 ? p.Accent : bc);
            CentreMark(gx);
            float rx = Cx + screen.Width * 0.04f, ry = Cy + screen.Height * 0.05f;
            Ring(gx, rx, ry, 1, 1, false);
            // side brackets with a fill bar (outside the ring; the centre stays empty). During a reload the old bar leaves
            // (normal: slides down; quick: falls further and fades = dropped) and a NEW FULL bar slides in from above.
            double frac = mag > 0 ? Math.Max(0, Math.Min(1, (reload != 0 ? reloadFrom : left) / mag)) : 0;
            double oldOut = 0, newIn = 0;
            if (reload != 0) TutorialV51Math.Swap(reloadMs, reload == 1 ? normalMs : quickMs, reload == 2, out oldOut, out newIn);
            float bh = ringD * 1.5f, bw = gx.S(4), off = ringD * 0.95f;
            bool flash = reload == 0 && band == 4 && ((int)(gx.TimeMs / 250)) % 2 == 0;
            Color fc = flash ? TutorialGfx.A(bc, 90) : bc;
            float fall = (float)(oldOut * bh * (reload == 2 ? 1.6f : 0.8f));
            double oldA = 1 - oldOut, newA = newIn;
            Color oldC = reload != 0 ? TutorialGfx.A(bc, oldA) : fc, newC = TutorialGfx.A(p.Neon, newA);
            float rise = (float)((1 - newIn) * bh * 0.8f);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = rx + s * off - bw / 2f, top = ry - bh / 2f;
                g.DrawRectangle(gx.P(TutorialGfx.A(p.Muted, 200), 1f), x, top, bw, bh);
                float fh = (float)(bh * frac);
                if (fh > 0.5f && (reload == 0 || oldA > 0.02)) g.FillRectangle(gx.B(oldC), x, top + bh - fh + fall, bw, fh);
                if (reload != 0 && newA > 0.02) g.FillRectangle(gx.B(newC), x, top - rise, bw, bh);
            }
            // bottom thin bar: old part shrinks away, the new full bar grows in
            float barW = ringD * 2.2f, barY = ry + ringD * 1.1f;
            g.FillRectangle(gx.B(TutorialGfx.A(p.Muted, 90)), rx - barW / 2f, barY, barW, gx.S(2.5f));
            if (reload == 0) g.FillRectangle(gx.B(fc), rx - barW / 2f, barY, (float)(barW * frac), gx.S(2.5f));
            else
            {
                if (oldA > 0.02) g.FillRectangle(gx.B(oldC), rx - barW / 2f, barY + fall * 0.3f, (float)(barW * frac), gx.S(2.5f));
                if (newA > 0.02) g.FillRectangle(gx.B(newC), rx - barW / 2f, barY, (float)(barW * newIn), gx.S(2.5f));
            }
            act[0] = reload == 1; act[1] = reload == 2;
            DrawChips(gx, act);
            g.ResetClip();
        }
    }
#endif
}
