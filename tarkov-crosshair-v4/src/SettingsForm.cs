using System;
#if !UI_PURE_CHECK
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
#endif

// UI_PURE_CHECK: when defined, only the pure logic (KeyCapture, UiPages) is compiled -- for the Linux tests.

namespace TarkovCrosshair
{
    /// <summary>
    /// The 按键 page's capture state machine (pure, no WinForms). Key down of a normal key finishes with the held
    /// modifiers (Ctrl+D); a modifier key waits and shows "Ctrl+…"; releasing a modifier that was pressed on its own
    /// binds the modifier itself (e.g. 强化 = Shift); Esc without modifiers cancels.
    /// </summary>
    internal sealed class KeyCapture
    {
        public const string Prompt = "请按键…（Esc 取消）";
        public const int VkEscape = 0x1B;
        public enum Result { Ignored, Waiting, Cancelled, Done }

        private int modKey; // the generic modifier pressed alone: 0 none, -1 several held
        public int Vk, Mods;
        public string Shown = Prompt;

        public void Start() { modKey = 0; Vk = 0; Mods = 0; Shown = Prompt; }

        /// <summary>Generic VK_SHIFT / VK_CONTROL / VK_MENU for any (left/right) modifier key, else vk.</summary>
        public static int GenericModifier(int vk)
        {
            switch (vk)
            {
                case 0xA0: case 0xA1: return 0x10;
                case 0xA2: case 0xA3: return 0x11;
                case 0xA4: case 0xA5: return 0x12;
                default: return vk;
            }
        }

        public static string PendingText(int mods)
        {
            string s = "";
            if ((mods & KeyBindings.ModCtrl) != 0) s += "Ctrl+";
            if ((mods & KeyBindings.ModShift) != 0) s += "Shift+";
            if ((mods & KeyBindings.ModAlt) != 0) s += "Alt+";
            return s.Length == 0 ? Prompt : s + "…";
        }

        /// <summary>mods = the Ctrl/Shift/Alt flags Windows reports with this key (they include the key's own flag).</summary>
        public Result KeyDown(int vk, int mods)
        {
            mods &= KeyBindings.ModMask;
            if (vk == VkEscape && mods == 0) return Result.Cancelled;
            if (vk <= 0 || vk > 254) return Result.Waiting;
            if (KeyBindings.IsModifierVk(vk))
            {
                int self = GenericModifier(vk);
                int bit = KeyBindings.ModBitOf(self);
                modKey = (mods & ~bit) == 0 ? self : -1;
                Shown = PendingText(mods | bit);
                return Result.Waiting;
            }
            Vk = vk;
            Mods = KeyBindings.NormalizeMods(vk, mods);
            return Result.Done;
        }

        /// <summary>heldMods = modifiers still down after this release (the released one may still be reported).</summary>
        public Result KeyUp(int vk, int heldMods)
        {
            vk = GenericModifier(vk);
            if (!KeyBindings.IsModifierVk(vk)) return Result.Ignored;
            if (modKey > 0 && vk == modKey) { Vk = vk; Mods = 0; return Result.Done; }
            modKey = 0;
            Shown = PendingText(heldMods & KeyBindings.ModMask & ~KeyBindings.ModBitOf(vk));
            return Result.Waiting;
        }

        /// <summary>Mouse button 1/2/4/5/6 with the modifiers held at the click.</summary>
        public Result Mouse(int vk, int mods)
        {
            if (vk <= 0) return Result.Ignored;
            Vk = vk;
            Mods = KeyBindings.NormalizeMods(vk, mods & KeyBindings.ModMask);
            return Result.Done;
        }
    }

    /// <summary>
    /// A captured key that another action already uses (pure). Answering 是 MOVES the key: the capturing action gets it
    /// and the other action takes the capturing action's old key, so no key ever ends up on two actions. W/A/S/D are
    /// fixed movement keys and cannot move: the question says so and 是 only binds the capturing action.
    /// </summary>
    internal static class KeyConflict
    {
        /// <summary>The action that already has vk+mods, other than 'action': a rebindable one first (that is the one
        /// that must move), else a fixed W/A/S/D action, else -1.</summary>
        public static int Find(KeyBindings k, int vk, int mods, int action)
        {
            int m = KeyBindings.NormalizeMods(vk, mods), movement = -1;
            for (int a = 0; a < KeyBindings.ActionCount; a++)
            {
                if (a == action || k.Get(a) != vk || k.GetMods(a) != m) continue;
                if (!KeyBindings.IsMovementAction(a)) return a;
                if (movement < 0) movement = a;
            }
            return movement;
        }

        public static string Question(KeyBindings k, int action, int vk, int mods, int other)
        {
            string key = KeyBindings.ComboName(vk, mods), name = KeyBindings.ActionNames[other];
            if (KeyBindings.IsMovementAction(other))
                return "「" + key + "」是固定的「" + name + "」键（W A S D 不能改）。还要用它当「" + KeyBindings.ActionNames[action]
                    + "」吗？按它会同时移动。";
            return "「" + key + "」现在是「" + name + "」。换过来吗？（「" + name + "」会改成 " + k.ComboOf(action) + "）";
        }

        /// <summary>Binds vk+mods to action; a non-movement 'other' (>= 0) gets action's previous combo. Returns the text
        /// for the status line.</summary>
        public static string Apply(KeyBindings k, int action, int vk, int mods, int other)
        {
            int oldVk = k.Get(action), oldMods = k.GetMods(action);
            k.SetCombo(action, vk, mods);
            string s = "已设置：" + KeyBindings.ActionNames[action] + " = " + k.ComboOf(action);
            if (other >= 0 && other != action && !KeyBindings.IsMovementAction(other))
            {
                k.SetCombo(other, oldVk, oldMods);
                s += "；" + KeyBindings.ActionNames[other] + " = " + k.ComboOf(other);
            }
            return s;
        }
    }

    /// <summary>Settings pages: ids for TutorialForm's jumpToPage, titles, lookup (pure).</summary>
    internal static class UiPages
    {
        /// <summary>Order = navigation order (SPEC v4).</summary>
        /// v5.1 (W25): "faq" (常见问题, W26's FaqPage) appended at the end, so every older index stays the same; the menu order is
        /// NavModel's, not this one.
        public static readonly string[] Ids = { "dashboard", "keys", "general", "recoil", "movement", "lean", "hand", "jump", "style", "filter", "guide", "feedback", "faq" };
        public static readonly string[] Titles = { "首页", "按键", "分辨率 · 常规", "后坐力", "移动", "侧身", "换手", "跳跃", "外观", "滤镜", "指南", "反馈", "常见问题" };
        /// <summary>Other names a page answers to (TutorialPages uses the short 「常规」).</summary>
        public static readonly string[] Aliases = { "仪表盘", "按键映射", "常规|分辨率", "", "", "", "", "", "准星样式", "画面滤镜", "说明|README|使用说明", "", "FAQ|问题" };
        /// <summary>Tag drawn after a page title as 「// tag」 (PageTitle skips an empty one). v4.1: all empty -- Leo 17 Nov 2026,
        /// no English in what the user reads except key names and the product name (the v4.0 tags were 「SYS.LIVE」 etc.).</summary>
        public static readonly string[] Codes = { "", "", "", "", "", "", "", "", "", "", "", "", "" };

        /// <summary>The 换手 page's rows (Profile hand item indexes). Never Profile.HandStand: the standing-still position is
        /// always the screen centre (Leo, 17 Nov 2026), so no page shows a 站着偏移 / 静止位置 control.</summary>
        public static int[] HandRows()
        {
            System.Collections.Generic.List<int> r = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Profile.HandItemCount; i++) if (i != Profile.HandStand) r.Add(i);
            return r.ToArray();
        }

        /// <summary>Index of a page by id ("keys", any case), Chinese title ("按键") or alias ("常规"); -1 if unknown.</summary>
        public static int IndexOf(string idOrTitle)
        {
            string s = (idOrTitle ?? "").Trim();
            if (s.Length == 0) return -1;
            for (int i = 0; i < Ids.Length; i++)
            {
                if (string.Equals(Ids[i], s, StringComparison.OrdinalIgnoreCase) || Titles[i] == s) return i;
                foreach (string a in Aliases[i].Split('|')) if (a.Length > 0 && a == s) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Number-box value conversion (pure, brute-ui F4): the double is clamped to [min, max] BEFORE it becomes a decimal, so
    /// a value of any size (1e300, ±Infinity, NaN) can never throw OverflowException while a page refreshes.
    /// SettingsForm.ToDec and StylePanel.Dec both use it.
    /// </summary>
    internal static class UiNum
    {
        public static decimal ToDec(double v, decimal min, decimal max, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            double lo = (double)min, hi = (double)max;
            if (v < lo) v = lo;
            if (v > hi) v = hi;
            if (decimals < 0) decimals = 0;
            if (decimals > 15) decimals = 15;
            decimal d;
            try { d = (decimal)Math.Round(v, decimals, MidpointRounding.AwayFromZero); }
            catch (OverflowException) { d = v < 0 ? min : max; }
            if (d < min) d = min;
            if (d > max) d = max;
            return d;
        }

        /// <summary>The largest % (2 decimals, rounded DOWN) whose px stays within maxPx: the 百分比偏移 box maximum.</summary>
        public static decimal MaxPct(double maxPx, GlobalSettings g)
        {
            double p = Math.Abs(OffsetUnits.ToPct(maxPx, g));
            if (double.IsNaN(p) || double.IsInfinity(p) || p > 99999) p = 99999;
            return (decimal)(Math.Floor(p * 100.0 + 1e-6) / 100.0);
        }
    }

    /// <summary>
    /// v4.1 (REBRUTE41 P2-2, pure) 外观 preview footer text: 「▣ 中心留空 x px ✓」 only while the look can be seen. A look the
    /// 中心留空 hides (RingStyle.Hidden) or whose raster has no visible pixel gets an amber 「▲ … 看不见」 instead, never a ✓.
    /// StylePanel.PreviewBox draws the first candidate that fits.
    /// </summary>
    internal static class StyleFooter
    {
        /// <summary>The style cannot be seen in game: RingStyle.Hidden, or the rendered pixels (premultiplied ARGB) are all
        /// transparent. pixels null = not rendered, only Hidden counts.</summary>
        public static bool Invisible(RingStyle st, int[] pixels)
        {
            if (st == null) return false;
            if (st.Hidden) return true;
            if (pixels == null) return false;
            for (int i = 0; i < pixels.Length; i++)
                if (((uint)pixels[i] >> 24) != 0) return false;
            return true;
        }

        /// <summary>Footer texts, longest first (the caller takes the first that fits; the last always fits).</summary>
        public static string[] Candidates(double hole, bool invisible)
        {
            string hv = (double.IsNaN(hole) || double.IsInfinity(hole) ? 0 : hole).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            if (invisible)
                return new string[] { "▲ 中心留空 " + hv + " px 时准星看不见", "▲ 留空 " + hv + " px：看不见", "▲ 看不见", "▲" };
            return new string[] { "▣ 中心留空 " + hv + " px ✓", "留空 " + hv + " px ✓", "✓" };
        }
    }

    /// <summary>v4.3.1 (pure, Alt+Tab lag): the window is minimised (or has no client area): no layout pass, nothing to
    /// fit, so restoring it (Alt+Tab back from the game) needs no relayout.</summary>
    internal static class LayoutSkip
    {
        public static bool Minimised(bool minimized, int clientW, int clientH) { return minimized || clientW <= 0 || clientH <= 0; }
    }

    /// <summary>
    /// v4.3 PERF (W7, 高级设置 toggle lag): which pages already show the current 高级设置 state. A toggle applies it to the
    /// current page only (ChangedOn); every other page becomes stale and is applied on its first ShowPage. A full refresh
    /// (RefreshValues) marks every page applied. Pure.
    /// </summary>
    internal sealed class LazyPageState
    {
        private readonly int[] applied;
        private int version;

        public LazyPageState(int pages) { applied = new int[Math.Max(0, pages)]; }

        public int Count { get { return applied.Length; } }

        /// <summary>The state changed while 'current' is shown: only that page is up to date now.</summary>
        public void ChangedOn(int current)
        {
            version++;
            if (current >= 0 && current < applied.Length) applied[current] = version;
        }

        public bool Stale(int page) { return page >= 0 && page < applied.Length && applied[page] != version; }

        public void MarkApplied(int page) { if (page >= 0 && page < applied.Length) applied[page] = version; }

        public void MarkAllApplied() { for (int i = 0; i < applied.Length; i++) applied[i] = version; }

        public int StaleCount()
        {
            int n = 0;
            for (int i = 0; i < applied.Length; i++) if (applied[i] != version) n++;
            return n;
        }
    }

    /// <summary>
    /// v4.3 PERF (W7, 「卡成 PPT」 wheel scrolling): wheel notches that arrive before the next frame are summed and applied
    /// as ONE scroll of the page's single inner panel. Add returns true when the caller must schedule the one flush;
    /// Take returns the summed pixels (positive = up, like MouseEventArgs.Delta) and clears. Pure.
    /// </summary>
    internal sealed class WheelCoalescer
    {
        private int pixels;
        private bool pending;

        public bool Pending { get { return pending; } }

        public bool Add(int px)
        {
            pixels += px;
            if (pending) return false;
            pending = true;
            return true;
        }

        public int Take()
        {
            int p = pixels;
            pixels = 0;
            pending = false;
            return p;
        }

        /// <summary>New scroll offset (>= 0, px from the top) after scrolling 'px' up from 'scrolled'.</summary>
        public static int Target(int scrolled, int px) { return Math.Max(0, scrolled - px); }
    }

#if !UI_PURE_CHECK
    /// <summary>
    /// v4.3 PERF (W7): wheel input of one scrolling surface. The first notch of a burst scrolls at once; notches that arrive
    /// within the next frame (15 ms timer: WM_TIMER comes after queued input, so a fast flick is summed) are applied as ONE
    /// scroll. Pause(true) at the first notch, Pause(false) 150 ms after the last (timers / animations stop meanwhile).
    /// </summary>
    internal sealed class WheelScroller : IDisposable
    {
        private readonly WheelCoalescer acc = new WheelCoalescer();
        private readonly Timer frame = new Timer();
        private readonly Timer idle = new Timer();
        private readonly Action<int> apply;
        private bool paused, disposed;
        /// <summary>Called with true when a burst starts, false 150 ms after its last notch.</summary>
        public Action<bool> Pause;
        /// <summary>Diagnostics: scrolls applied / notches received.</summary>
        public int Applied, Notches;

        public WheelScroller(Action<int> apply)
        {
            this.apply = apply;
            frame.Interval = 15;
            frame.Tick += delegate
            {
                int px = acc.Take();
                if (px != 0) Do(px); else frame.Stop();
            };
            idle.Interval = 150;
            idle.Tick += delegate { idle.Stop(); SetPaused(false); };
        }

        /// <summary>px > 0 scrolls up (MouseEventArgs.Delta sign).</summary>
        public void Queue(int px)
        {
            if (disposed || px == 0) return;
            Notches++;
            SetPaused(true);
            idle.Stop();
            idle.Start();
            if (!frame.Enabled) { Do(px); frame.Start(); }
            else acc.Add(px);
        }

        private void Do(int px)
        {
            Applied++;
            try { apply(px); } catch (Exception ex) { Program.Log("UI: wheel: " + ex.Message); }
        }

        private void SetPaused(bool on)
        {
            if (paused == on) return;
            paused = on;
            Action<bool> h = Pause;
            if (h != null) { try { h(on); } catch (Exception) { } }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            frame.Stop(); idle.Stop();
            frame.Dispose(); idle.Dispose();
            SetPaused(false);
        }
    }

    /// <summary>
    /// v4.3.1: row counter for grids filled at fixed cells (SettingsForm.Cells, StylePanel.Put). The next row = the grid's
    /// RowCount, which is raised by one (with an AutoSize row style) for each new row, so it never depends on flow.
    /// </summary>
    internal static class GridRows
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TableLayoutPanel, GridRowMark> marks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TableLayoutPanel, GridRowMark>();

        /// <summary>Rows handed out so far for this grid.</summary>
        public static int Used(TableLayoutPanel g)
        {
            GridRowMark m;
            return marks.TryGetValue(g, out m) ? m.Rows : 0;
        }

        public static int Next(TableLayoutPanel g)
        {
            GridRowMark m = marks.GetOrCreateValue(g);
            int r = m.Rows;
            m.Rows = r + 1;
            if (g.RowCount < r + 1) g.RowCount = r + 1;
            while (g.RowStyles.Count < g.RowCount) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return r;
        }

        /// <summary>Forget the rows (the grid was cleared to be filled again).</summary>
        public static void Reset(TableLayoutPanel g)
        {
            GridRowMark m;
            if (marks.TryGetValue(g, out m)) m.Rows = 0;
            g.RowCount = 0;
            g.RowStyles.Clear();
        }

        public static void EnsureColumn(TableLayoutPanel g, int col)
        {
            while (g.ColumnCount <= col)
            {
                g.ColumnCount++;
                g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            }
        }
    }

    internal sealed class GridRowMark { public int Rows; }

    /// <summary>
    /// v4.3 PERF (W7): the scrolling body of a settings page. Double-buffered. v4.3.1: no WS_EX_COMPOSITED any more -- on
    /// a page-sized container it made every repaint (also the one after Alt+Tab back from the game) compose the whole page
    /// bottom-up before anything showed. Holds ONE inner content panel, so a scroll moves that one panel. The wheel goes through WheelScroller with
    /// the same distance as WinForms' own AutoScroll wheel (e.Delta px per message).
    /// </summary>
    internal sealed class ScrollHost : Panel
    {
        public readonly WheelScroller Wheel;

        public ScrollHost()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Wheel = new WheelScroller(ScrollUp);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!VScroll) { base.OnMouseWheel(e); return; }
            HandledMouseEventArgs he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            Wheel.Queue(e.Delta);
        }

        private void ScrollUp(int px)
        {
            Point cur = AutoScrollPosition; // negative
            AutoScrollPosition = new Point(-cur.X, WheelCoalescer.Target(-cur.Y, px));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Wheel.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// v4.1 百分比偏移 number box (positive magnitude, 2 decimals). Typing also accepts 「← 4.17%」, 「-4.17」, 「4,17」, 「左 4.17」,
    /// 「<- 4.17」, 「^ 1」 (OffsetUnits.TryParse): a typed sign / arrow / direction word names the direction (DirectionTyped), a plain number
    /// keeps the current arrow.
    /// </summary>
    internal sealed class PctUpDown : NumericUpDown
    {
        public OffsetAxis Axis;
        /// <summary>The typed text named a direction: true = + (→ / ↑), false = − (← / ↓).</summary>
        public event Action<bool> DirectionTyped;

        protected override void ValidateEditText()
        {
            string t = Text ?? "";
            double pct;
            if (t.Trim().Length > 0 && OffsetUnits.TryParse(t, Axis, out pct))
            {
                bool named = OffsetUnits.NamesDirection(t); // sign, arrow (ASCII 「<-」 「^」 too) or 左 / 右 / 上 / 下
                UserEdit = false;
                if (named && pct != 0)
                {
                    Action<bool> h = DirectionTyped;
                    if (h != null) h(pct > 0);
                }
                decimal d = UiNum.ToDec(Math.Abs(pct), Minimum, Maximum, DecimalPlaces);
                if (Value != d) Value = d;
                UpdateEditText();
                return;
            }
            base.ValidateEditText();
        }
    }

    /// <summary>
    /// v4.1 百分比偏移 direction toggle: a drawn arrow (no font glyph, so it shows the same on every system), X → / ←, Y ↑ / ↓
    /// in the profile convention (+ = right / up). Click, Space or Enter flips it (Toggled). Fixed = a plain arrow that only
    /// shows the direction (上抬 = ↑): no frame, no focus, no click. ForeColor = the arrow colour (muted when disabled).
    /// </summary>
    internal sealed class ArrowToggle : Control
    {
        public readonly OffsetAxis Axis;
        private bool positive = true, hover, down, isFixed;
        /// <summary>The user flipped the arrow (the owner changes Positive).</summary>
        public event Action Toggled;

        public ArrowToggle(OffsetAxis axis, float scale)
        {
            Axis = axis;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Tag = "theme:keep-arrow";
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            UpdateName();
        }

        public bool Positive
        {
            get { return positive; }
            set { if (positive == value) return; positive = value; UpdateName(); Invalidate(); }
        }

        private bool dim;

        /// <summary>The value is 0.00 %: the arrow is drawn muted (the direction does not matter yet, but can be chosen).</summary>
        public bool Dim
        {
            get { return dim; }
            set { if (dim == value) return; dim = value; Invalidate(); }
        }

        public bool Fixed
        {
            get { return isFixed; }
            set
            {
                isFixed = value;
                TabStop = !value;
                SetStyle(ControlStyles.Selectable, !value);
                Cursor = value ? Cursors.Default : Cursors.Hand;
                AccessibleRole = value ? AccessibleRole.Graphic : AccessibleRole.PushButton;
                Invalidate();
            }
        }

        private void UpdateName()
        {
            AccessibleName = Axis == OffsetAxis.X ? (positive ? "往右" : "往左") : (positive ? "往上" : "往下");
            Text = OffsetUnits.Arrow(Axis, positive); // screen readers / tests; painting does not use the glyph
        }

        private void Flip()
        {
            if (isFixed || !Enabled) return;
            Action h = Toggled;
            if (h != null) h();
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (!isFixed) { Focus(); Flip(); } }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!isFixed && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { e.Handled = true; Flip(); }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                if (w < 4 || h < 4) return;
                Color parentBack = Parent != null && Parent.BackColor.A == 255 ? Parent.BackColor : Theme.ColSurface;
                bool live = Enabled && !isFixed;
                Color back = isFixed ? parentBack : (!Enabled ? Theme.ColInput : (down ? Theme.ColPressed : (hover ? Theme.ColHover : Theme.ColInput)));
                using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, 0, 0, w, h);
                if (!isFixed)
                    using (Pen p = new Pen(live && (hover || Focused) ? Theme.ColAccent2 : Theme.ColBorder))
                        g.DrawRectangle(p, 0, 0, w - 1, h - 1);
                Color fg = Enabled && !dim ? ForeColor : Theme.ColMuted;
                float s = Math.Min(w, h);
                float len = s * 0.56f, head = s * 0.30f, stroke = Math.Max(1.5f, s * 0.085f);
                float cx = w / 2f, cy = h / 2f;
                // direction unit vector in screen space (y down): X+ = right, Y+ = up
                float dx = Axis == OffsetAxis.X ? (positive ? 1 : -1) : 0, dy = Axis == OffsetAxis.Y ? (positive ? -1 : 1) : 0;
                PointF tip = new PointF(cx + dx * len / 2f, cy + dy * len / 2f), tail = new PointF(cx - dx * len / 2f, cy - dy * len / 2f);
                PointF baseC = new PointF(tip.X - dx * head, tip.Y - dy * head);
                float nx = -dy, ny = dx; // normal
                PointF[] tri = { tip, new PointF(baseC.X + nx * head * 0.62f, baseC.Y + ny * head * 0.62f), new PointF(baseC.X - nx * head * 0.62f, baseC.Y - ny * head * 0.62f) };
                System.Drawing.Drawing2D.SmoothingMode sm = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(fg, stroke)) g.DrawLine(p, tail, new PointF(baseC.X + dx * 0.5f, baseC.Y + dy * 0.5f));
                using (SolidBrush b = new SolidBrush(fg)) g.FillPolygon(b, tri);
                g.SmoothingMode = sm;
                if (live && Focused && ShowFocusCues)
                    using (Pen fp = new Pen(Theme.Blend(back, Theme.ColAccent2, 0.7)))
                    {
                        fp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                        g.DrawRectangle(fp, 2, 2, w - 5, h - 5);
                    }
            }
            catch (Exception) { }
        }
    }

    /// <summary>Button that captures a key, mouse button or combo (Ctrl/Shift/Alt + key) for one action of the 按键 page.</summary>
    internal sealed class KeyButton : Button
    {
        public int Action; // KeyBindings action index
    }

    /// <summary>
    /// Settings window (v4.0): header, one-line top bar, left navigation, one page at a time.
    /// Page order (SPEC v4; v4.2 adds 滤镜 after 外观): 首页 dashboard, 按键, 常规, 后坐力, 移动, 侧身, 换手, 跳跃, 外观, 滤镜, 指南, 反馈.
    /// Always live: no preview modes.
    /// Layout rules (from v3.0): every grid is a TableLayoutPanel, every fixed pixel size goes through S()
    /// (system DPI / 96, AutoScaleMode None), fonts are in points, pages scroll, minimum size keeps the bars on one line.
    /// </summary>
    internal sealed partial class SettingsForm : Form
    {
        /// <summary>Layout scale (system DPI / 96). Settable before construction for layout tests only.</summary>
        internal static float ScaleOverride = 0f;

        public const string GitHubUrl = "https://github.com/liumuyangmuyang-blip/Tarkov-Hip-Aimer";
        public const string Email = "liumuyangmuyang@gmail.com";
        public const string Douyin = "Muuslati";
        /// <summary>The 反馈 page sentence, word for word (SPEC v4 item 12).</summary>
        public const string FeedbackText = "功能建议 / 反馈创意 / Bug 反馈：GitHub、邮箱 liumuyangmuyang@gmail.com、抖音号 Muuslati（主页有交流群）。更新会发布在 GitHub 和抖音群内。";


        private sealed class Page
        {
            public string Id, Title;
            public Panel Host;       // what is shown / hidden
            public Panel Scroller;   // the scrolling body (null for the dashboard)
            public Control Content;  // measured for the window size (null = fills)
            public PageTitle Heading;
            public NavItem Nav;
            // v4.3 PERF (W7): what this page registered while it was built (refreshers / 高级设置 controls / folds)
            public int RefStart, RefEnd, AdvStart, AdvEnd, FoldStart, FoldEnd;
            // v5.0.2 (W17, Leo: settings window first paint < 500 ms): the page body is built on its first visit
            public Func<Control> Build;
            public bool Scroll, Built;
            public long BuildMs = -1, ApplyMs = -1;
        }

        /// <summary>v5.0.2: true once the constructor is done (a page built later themes, fills and fits itself).</summary>
        private bool constructed;

        private LazyPageState advLazy;          // v4.3: pages still showing the previous 高级设置 state

        private readonly App app;
        private readonly float k;
        private Font monoFont;
        private bool loading;
        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<NumericUpDown> nums = new List<NumericUpDown>();
        /// <summary>Controls shown only while 「高级设置」 is on (UiPrefs).</summary>
        private readonly List<Control> advOnly = new List<Control>();
        /// <summary>Every 「可选：…」 fold: opened when 高级设置 turns on (no second gate), closed again when it turns off.</summary>
        private readonly List<FoldHeader> folds = new List<FoldHeader>();
        /// <summary>v5.0.2 (UI M2): folds that 高级设置 never opens by itself (滤镜 › 显示器硬件: opening it starts the monitor
        /// probe, so only a click opens it). A page registers a fold here with ManualFold right after creating it.</summary>
        private readonly HashSet<FoldHeader> manualFolds = new HashSet<FoldHeader>();

        /// <summary>v5.0.2 (UI M2): the fold whose header is the first FoldHeader inside foldControl (what Fold returns) stays
        /// closed when 高级设置 turns on; only a click opens it. Returns foldControl.</summary>
        private Control ManualFold(Control foldControl)
        {
            FoldHeader h = foldControl as FoldHeader;
            if (h == null && foldControl != null)
                foreach (Control c in foldControl.Controls) { h = c as FoldHeader; if (h != null) break; }
            if (h != null) manualFolds.Add(h);
            return foldControl;
        }
        private int advState = -1; // last applied 高级设置: -1 never, 0 off, 1 on
        private CheckBox chkAdvanced;
        private readonly ToolTip tips = new ToolTip();
        private readonly List<Page> pages = new List<Page>();
        private int current = -1;

        private ComboBox cbProfile;
        private CheckBox chkEnabled;
        private CheckBox chkAutostart;
        private ComboBox cbMonitor;
        private ContextMenuStrip profileMenu;
        private ContextMenuStrip restoreMenu;
        private ContextMenuStrip windowMenu;
        private ToolStripMenuItem restoreSaved;
        private Label lblSave;
        private GlowButton btnTutorial;
        private TableLayoutPanel root;
        private Control header;
        private TableLayoutPanel topBar;
        private TableLayoutPanel navBar;
        private Panel navHost;       // scrolls the navigation when the window is very short
        private TableLayoutPanel body;
        private bool fittingNav;
        private Panel pageHost;
        private Control dashboard;
        private Control stylePanel;
        private Form tutorial;
        private readonly MotionEngine dashEngine = new MotionEngine();
        private readonly Timer saveFlash = new Timer();

        private KeyButton capturing;
        private readonly KeyCapture capture = new KeyCapture();
        private bool suppressNextClick;
        public bool AllowClose;

        private GlobalSettings G { get { return app.Settings; } }
        private Profile Cur { get { return app.Settings.Active; } }

        public SettingsForm(App app)
        {
            this.app = app;
            k = ScaleOverride > 0 ? ScaleOverride : SystemScale();
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None; // every pixel size is scaled explicitly by S()
            // v4.3.1 (Leo): ONE fixed text size, whatever the window size (Windows display scaling still applies)
            Font = Theme.Ui(BaseUiPt);     // SPEC v4 readability: body / labels / buttons 11 pt
            monoFont = Theme.Mono(BaseMonoPt); // numbers / data 11 pt
            Text = Program.ProductName + "  " + Program.Version;
            if (app.RingIcon != null) Icon = app.RingIcon;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = G.SettingsTopMost;
            tips.AutoPopDelay = 15000;
            tips.InitialDelay = 400;
            Theme.StyleToolTip(tips, delegate { return Font; }); // 11 pt, dark (stock tooltips are ~9 pt)

            root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // header
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // top bar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // nav + page
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            header = Theme.Header(Program.ShortName, Program.ChineseName + " · by Leo", Program.Version);
            header.Dock = DockStyle.Top;
            header.Margin = new Padding(0);
            root.Controls.Add(header, 0, 0);
            BuildTopBar();

            body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = new Padding(0);
            body.Padding = new Padding(0);
            body.ColumnCount = 3;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavWidth));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));     // divider
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(body, 0, 2);

            navHost = new Panel();
            navHost.Dock = DockStyle.Fill;
            navHost.Margin = new Padding(0);
            navHost.AutoScroll = true;
            navHost.BackColor = Theme.ColBackground;
            body.Controls.Add(navHost, 0, 0);
            navBar = new TableLayoutPanel();
            navBar.Dock = DockStyle.None;
            navBar.Location = new Point(0, 0);
            navBar.Margin = new Padding(0);
            navBar.Padding = P(0, 10, 0, 10);
            navBar.ColumnCount = 1;
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navBar.BackColor = Theme.ColBackground;
            navHost.Controls.Add(navBar);
            Panel divider = new Panel();
            divider.Dock = DockStyle.Fill;
            divider.Margin = new Padding(0);
            divider.BackColor = Theme.ColBorder;
            divider.Tag = "theme:keep-divider";
            body.Controls.Add(divider, 1, 0);

            pageHost = new Panel();
            pageHost.Dock = DockStyle.Fill;
            pageHost.Margin = new Padding(0);
            pageHost.BackColor = Theme.ColSurface;
            body.Controls.Add(pageHost, 2, 0);
            pageHost.Resize += delegate { if (!LayoutSkip.Minimised(WindowState == FormWindowState.Minimized, ClientSize.Width, ClientSize.Height)) FitWrapping(); };

            // v5.0.2 (W17): only the shells (navigation item, heading, scroller) are made here; a page body is built on its
            // first visit (EnsureBuilt). 首页 is the first page shown, so it is built now.
            AddPage(0, BuildDashboard, false);
            AddPage(1, BuildKeys, true);
            AddPage(2, BuildGeneral, true);
            AddPage(3, BuildRecoil, true);
            AddPage(4, BuildMovement, true);
            AddPage(5, BuildLean, true);
            AddPage(6, BuildHand, true);
            AddPage(7, BuildJump, true);
            AddPage(8, BuildStyle, false);
            AddPage(9, BuildFilter, true); // v4.2 滤镜 (SettingsForm.PageFilter.cs)
            AddPage(10, BuildGuide, false);
            AddPage(11, BuildFeedback, true);
            AddPage(12, BuildFaq, true); // v5.1 (W26's FaqPage): a bottom link before 反馈 (NavModel.AddLink)
            advLazy = new LazyPageState(pages.Count);
            EnsureBuilt(0);
            BuildNav();
            FixUp(FitControlHeights); // last: runs after every control's own size rule

            saveFlash.Interval = 2500;
            saveFlash.Tick += delegate { saveFlash.Stop(); if (lblSave.ForeColor != Theme.ColWarn) lblSave.ForeColor = Theme.ColMuted; };
            saveStatusHandler = delegate { if (!IsDisposed) SetStatus(app.SaveStatus, app.SaveStatus.StartsWith("保存失败")); };
            app.SaveStatusChanged += saveStatusHandler; // removed in Dispose: App must not keep a closed window alive

            RefreshAll();
            ResumeLayout(false);
            PerformLayout();
            Theme.Apply(this);
            FitControlHeights(); // the theme may change the boxes' borders, so their height
            FitComboHeights(this);
            PaintHandRows(); // after the theme, so 自动 rows are grey
            constructed = true;
            ForegroundTracking();
            ShowPage(0);
            ActiveControl = pages[0].Nav; // no highlighted text box on open
        }

        /// <summary>
        /// v5.0.2 (W17): builds a page body the first time it is needed (its first ShowPage, a 「去调 →」 jump, the tutorial).
        /// The page's refreshers, 高级设置 controls and folds are recorded as its own range (they are appended at the end of
        /// the lists, so every range stays contiguous). After the constructor the new body is themed (the scroller's
        /// ControlAdded hook), sized (FitControlHeights / combos), filled (ApplyAdvancedPage) and logged to crash.log.
        /// </summary>
        private void EnsureBuilt(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            Page pg = pages[index];
            if (pg.Built) return;
            pg.Built = true;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            int r0 = refreshers.Count, a0 = advOnly.Count, f0 = folds.Count;
            Control content;
            try { content = pg.Build(); }
            catch (Exception ex) { Program.Log("UI: page " + pg.Id + ": " + ex); content = Missing("「" + pg.Title + "」页"); }
            pg.RefStart = r0; pg.RefEnd = refreshers.Count;
            pg.AdvStart = a0; pg.AdvEnd = advOnly.Count;
            pg.FoldStart = f0; pg.FoldEnd = folds.Count;
            pg.Host.SuspendLayout();
            try { AttachContent(pg, index, content); }
            finally { pg.Host.ResumeLayout(false); }
            pg.BuildMs = sw.ElapsedMilliseconds;
            if (pg.Id == "style")
            {
                if (stylePanel is StylePanel) ((StylePanel)stylePanel).ScrollPause = ScrollPaused;
                Theme.StyleToolTips(stylePanel, delegate { return Font; }); // 外观 tips: same readable look as the rest
            }
            if (constructed)
            {
                // one layout pass for the whole fill-in (sizes, values, 高级设置 rows), like the constructor's SuspendLayout
                SuspendLayout();
                pg.Host.SuspendLayout();
                if (pg.Content != null) pg.Content.SuspendLayout();
                try
                {
                    FitControlHeights();
                    FitComboHeights(pg.Host);
                    ApplyAdvancedPage(index); // values + 高级设置 rows and folds of this page
                    if (pg.Id == "hand") PaintHandRows();
                    if (stylePanel != null && pg.Id == "style") { StylePanel sp = stylePanel as StylePanel; if (sp != null) { bool was = loading; loading = true; try { sp.RefreshValues(); } finally { loading = was; } } }
                }
                finally
                {
                    if (pg.Content != null) pg.Content.ResumeLayout(false);
                    pg.Host.ResumeLayout(false);
                    ResumeLayout(false);
                }
                pg.Host.PerformLayout();
            }
            // v5.1 (W30, decision 9): every label / button / box fits its text (real font metrics + margin), no CJK in mono
            try { TextFit.FitPage(pg.Host, pg.Id); } catch (Exception ex) { Program.Log("UI: text fit " + pg.Id + ": " + ex.Message); }
            pg.ApplyMs = sw.ElapsedMilliseconds - pg.BuildMs;
            PageBuildTimes.Add(pg.Id, sw.ElapsedMilliseconds);
            Program.Log(PageBuildTimes.Line(pg.Title, pg.BuildMs, pg.ApplyMs));
        }

        /// <summary>v5.0.2: refreshers that belong to no page (top bar, navigation): not inside any page's range.</summary>
        private bool InAnyPage(int refIndex)
        {
            foreach (Page pg in pages) if (pg.Built && refIndex >= pg.RefStart && refIndex < pg.RefEnd) return true;
            return false;
        }

        /// <summary>v5.0.2: Windows re-reads 常规's monitor list on a display change; nothing to do before 常规 exists.</summary>
        public void DisplaysChanged()
        {
            if (cbMonitor == null || cbMonitor.IsDisposed) return;
            bool was = loading;
            loading = true;
            try { RefreshMonitors(); }
            finally { loading = was; }
        }

        /// <summary>Navigation width: 188 px (100 %), 132 px when the window is narrower than 940 px logical (tiny screen at high scaling).</summary>
        private int NavWidth { get { return ClientSize.Width > 0 && ClientSize.Width < S(940) ? F(164) : F(204); } }

        private const float BaseUiPt = 11f, BaseMonoPt = 11f;

        /// <summary>Scales a 96-DPI length that belongs to text (box widths, wrap widths, nav rows). v4.3.1: the text size is
        /// fixed, so this is the DPI scale only (same as S).</summary>
        private int F(int px) { return (int)Math.Round(px * k); }

        /// <summary>Runs a size-from-font rule once (v4.3.1: fonts never change after the window is built).</summary>
        private static void FixUp(Action a) { a(); }

        private bool topBarTwoRows;

        /// <summary>Very narrow window (e.g. 1280x720 at 200 %): the two switches and the status move to a second line, so
        /// nothing in the top bar is ever cut off. One line again as soon as it fits (24 px hysteresis).</summary>
        private void FitTopBar()
        {
            if (topBar == null || chkEnabled == null || chkAdvanced == null || lblSave == null) return;
            int oneRow = 0;
            foreach (Control c in new Control[] { topBar.GetControlFromPosition(0, 0), cbProfile, topBar.GetControlFromPosition(2, 0),
                topBar.GetControlFromPosition(3, 0), topBar.GetControlFromPosition(4, 0), chkAdvanced, chkEnabled })
                if (c != null) oneRow += c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal;
            oneRow += topBar.Padding.Horizontal + F(40); // + a little room for the status text
            int avail = ClientSize.Width;
            bool two = topBarTwoRows ? oneRow > avail - S(24) : oneRow > avail;
            if (two == topBarTwoRows) return;
            topBarTwoRows = two;
            topBar.SuspendLayout();
            if (two)
            {
                topBar.SetCellPosition(chkAdvanced, new TableLayoutPanelCellPosition(0, 1));
                topBar.SetColumnSpan(chkAdvanced, 2);
                topBar.SetCellPosition(chkEnabled, new TableLayoutPanelCellPosition(2, 1));
                topBar.SetColumnSpan(chkEnabled, 2);
                topBar.SetCellPosition(lblSave, new TableLayoutPanelCellPosition(4, 1));
                topBar.SetColumnSpan(lblSave, 2);
                chkAdvanced.Anchor = AnchorStyles.Left;
                chkEnabled.Anchor = AnchorStyles.Left;
            }
            else
            {
                topBar.SetColumnSpan(chkAdvanced, 1);
                topBar.SetColumnSpan(chkEnabled, 1);
                topBar.SetColumnSpan(lblSave, 1);
                topBar.SetCellPosition(lblSave, new TableLayoutPanelCellPosition(5, 0));
                topBar.SetCellPosition(chkAdvanced, new TableLayoutPanelCellPosition(6, 0));
                topBar.SetCellPosition(chkEnabled, new TableLayoutPanelCellPosition(7, 0));
                chkAdvanced.Anchor = AnchorStyles.Right;
                chkEnabled.Anchor = AnchorStyles.Right;
            }
            topBar.ResumeLayout(true);
        }


        /// <summary>
        /// v4.1 window drag freeze (Leo, 17 Nov 2026). ResizeBegin (= WM_ENTERSIZEMOVE, moving or resizing): layout suspended,
        /// every IPausable inside (dashboard, 外观 preview, animated titles, glow button) paused, debounce timers stopped; the
        /// window keeps its last frame and newly exposed area is plain background. ResizeEnd (= WM_EXITSIZEMOVE): one relayout
        /// at the final size (v4.3.1: the text size is fixed, nothing is rescaled), everything resumed.
        /// </summary>
        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            Freeze();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            Unfreeze();
        }

        private bool frozen;

        private void Freeze()
        {
            if (frozen || IsDisposed) return;
            frozen = true;
            saveFlash.Stop();
            SuspendLayout();
            SyncPause();
        }

        // v5.0.2: three reasons stop every animation / timer of the window (IPausable): a window drag (frozen), wheel
        // scrolling (wheelPaused) and another program in front (background, Leo: no timer ticks while the game is in
        // front). PauseTree runs only when the combined state changes, so one reason ending never resumes another's pause.
        private bool wheelPaused, background, treePaused;

        private void SyncPause()
        {
            bool want = frozen || wheelPaused || background || !Visible; // v5.1: hidden (prebuilt / closed) = paused
            if (want == treePaused || IsDisposed) return;
            treePaused = want;
            Theme.PauseTree(this, want);
        }

        /// <summary>v5.0.2: Activated / Deactivate drive ForegroundState (no polling); the window pauses itself while another
        /// program is in front and the save-flash timer stops.</summary>
        private void ForegroundTracking()
        {
            Theme.TrackForeground(this);
            foregroundHandler = delegate(bool ours)
            {
                if (IsDisposed) return;
                background = !ours;
                if (background) saveFlash.Stop();
                else if (lblSave != null && lblSave.ForeColor != Theme.ColWarn && lblSave.ForeColor != Theme.ColMuted) saveFlash.Start();
                SyncPause();
            };
            ForegroundState.Changed += foregroundHandler;
            background = !ForegroundState.Ours;
            SyncPause();
        }

        private Action<bool> foregroundHandler;

        /// <summary>Tests / probe: true while the window's animations are paused because another program is in front.</summary>
        internal bool BackgroundPaused { get { return background && treePaused; } }

        private void Unfreeze()
        {
            if (!frozen) return;
            frozen = false;
            if (IsDisposed) return;
            try
            {
                if (body != null && body.ColumnStyles.Count > 0 && (int)body.ColumnStyles[0].Width != NavWidth) body.ColumnStyles[0].Width = NavWidth;
            }
            finally { ResumeLayout(true); } // the one relayout
            FitCompact();
            FitTopBar();
            SyncPause();
            if (!background && lblSave != null && lblSave.ForeColor != Theme.ColWarn && lblSave.ForeColor != Theme.ColMuted) saveFlash.Start();
        }

        private bool compactChrome;

        /// <summary>
        /// UI41-review #1: below 500 logical px of client height (e.g. 1280x720 at 150 %) the header strip is hidden (the window
        /// caption already shows name and version), the top bar and page tops get thinner and page titles become one line
        /// without the hairline, so the values people tune move up into view. 20 px hysteresis.
        /// </summary>
        private void FitCompact()
        {
            if (header == null || topBar == null || pages.Count == 0 || ClientSize.Height <= 0) return;
            int logicalH = (int)Math.Round(ClientSize.Height / k);
            bool c = compactChrome ? logicalH < 520 : logicalH < 500;
            if (c == compactChrome) return;
            compactChrome = c;
            SuspendLayout();
            try
            {
                header.Visible = !c;
                if (tuneStrip != null) tuneStrip.Visible = !c; // the dashboard needs every pixel of a short window
                topBar.Padding = c ? P(12, 3, 12, 3) : P(12, 8, 12, 8);
                foreach (Page pg in pages)
                {
                    pg.Heading.Compact = c;
                    pg.Heading.Height = pg.Heading.GetPreferredSize(Size.Empty).Height;
                    Padding hp = pg.Host.Padding;
                    pg.Host.Padding = new Padding(hp.Left, c ? S(pg.Id == "dashboard" ? 6 : 4) : S(pg.Id == "dashboard" ? 10 : 12), hp.Right, hp.Bottom);
                    if (pg.Scroller != null) pg.Scroller.Padding = c ? P(0, 4, 0, 12) : P(0, 10, 0, 12);
                }
            }
            finally { ResumeLayout(true); }
            FitNav();
        }

        /// <summary>
        /// v4.3.1 (Alt+Tab lag): no layout while minimised. A minimised window has a 0 x 0 client area, so WinForms used to
        /// squash every docked page to nothing and lay the whole window out again at full size on restore (Alt+Tab back
        /// from a fullscreen game). Skipping it keeps every child at its size: restoring is a plain repaint.
        /// </summary>
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (LayoutSkip.Minimised(WindowState == FormWindowState.Minimized, ClientSize.Width, ClientSize.Height)) return;
            base.OnLayout(e);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            if (body == null || body.ColumnStyles.Count == 0) return;
            if (LayoutSkip.Minimised(WindowState == FormWindowState.Minimized, ClientSize.Width, ClientSize.Height)) return; // v4.3.1
            if (frozen) return; // dragging: nothing is laid out or rescaled until ResizeEnd (Unfreeze)
            FitCompact();
            int nw = NavWidth;
            if ((int)body.ColumnStyles[0].Width != nw) body.ColumnStyles[0].Width = nw;
            FitTopBar();
            if (btnTutorial != null)
            {
                Padding m = nw < F(204) ? P(8, 6, 8, 6) : P(14, 6, 14, 6);
                if (btnTutorial.Margin != m) btnTutorial.Margin = m;
            }
        }

        private static float SystemScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float s = g.DpiX / 96f;
                    if (s >= 0.5f && s <= 5f) return s;
                }
            }
            catch (Exception) { }
            return 1f;
        }

        // ---------------- layout helpers ----------------

        /// <summary>Scales a 96-DPI pixel length.</summary>
        private int S(int px) { return (int)Math.Round(px * k); }
        private Size Sz(int w, int h) { return new Size(S(w), S(h)); }
        private Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        private TableLayoutPanel Grid(int cols)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = cols;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Margin = new Padding(0);
            t.Padding = new Padding(0);
            for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        /// <summary>One row of controls side by side (a 1-row grid: never wraps).</summary>
        private TableLayoutPanel Line(params Control[] items)
        {
            TableLayoutPanel t = Grid(items.Length);
            t.RowCount = 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Anchor = AnchorStyles.Left;
            for (int i = 0; i < items.Length; i++) t.Controls.Add(items[i], i, 0);
            return t;
        }

        /// <summary>Vertical stack of blocks (1-column grid).</summary>
        private TableLayoutPanel Stack(params Control[] items)
        {
            TableLayoutPanel t = Grid(1);
            foreach (Control c in items)
            {
                c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                t.Controls.Add(c);
            }
            return t;
        }

        private Label Lbl(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = P(3, 3, 6, 3);
            return l;
        }

        /// <summary>Empty, zero-width cell filler (a plain new Label() would be 100 px wide).</summary>
        private static Label Blank()
        {
            // zero-size: a row whose only visible items are hidden collapses instead of leaving an empty line
            Label l = new Label();
            l.AutoSize = false;
            l.Size = Size.Empty;
            l.Text = "";
            l.Margin = new Padding(0);
            return l;
        }

        private Label Unit(string text)
        {
            Label l = Lbl(text);
            l.Margin = P(0, 3, 6, 3);
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        /// <summary>Wrapping labels and their preferred width (96-DPI px); narrowed to the page when the window is small.</summary>
        private readonly List<KeyValuePair<Label, int>> wrapping = new List<KeyValuePair<Label, int>>();
        /// <summary>Drawn controls (plots) and their preferred width (96-DPI px): narrower on a narrow page, never wider.</summary>
        private readonly List<KeyValuePair<Control, int>> fitWidth = new List<KeyValuePair<Control, int>>();
        /// <summary>Page rules for a narrow page (argument = the right edge a control may reach in page-host px); true = changed.</summary>
        private readonly List<Func<int, bool>> narrowFixes = new List<Func<int, bool>>();

        /// <summary>x of a control inside the settings page host (sum of Left up the parents); -1 when not inside it.</summary>
        private int LeftInPageHost(Control c)
        {
            int x = 0;
            for (Control p = c; p != null; p = p.Parent)
            {
                if (p == pageHost) return x;
                x += p.Left;
            }
            return -1;
        }

        /// <summary>
        /// Wrap width = min(preferred, what is left of the visible page to the right of the label), so long notes and the
        /// hint columns never run off the page (brute-ui F2: 1280x720 at 200 % scrolled sideways and cut the hints).
        /// </summary>
        private void FitWrapping()
        {
            if (pageHost == null) return;
            int flat = pageHost.ClientSize.Width - S(22 + 10) - SystemInformation.VerticalScrollBarWidth - S(60);
            if (flat < S(160)) flat = S(160);
            // right edge a label may reach: page padding (10) + the section's padding / margin (14 + 12) + label margin + slack
            int right = pageHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(10 + 14 + 12 + 6 + 6);
            bool changed = false;
            foreach (KeyValuePair<Label, int> kv in wrapping)
            {
                int avail = flat;
                int x = LeftInPageHost(kv.Key);
                if (x > 0) avail = Math.Max(S(120), right - x);
                int w = Math.Min(F(kv.Value), avail);
                if (kv.Key.MaximumSize.Width != w) { kv.Key.MaximumSize = new Size(w, 0); changed = true; }
            }
            // drawn controls (跳跃 plot) narrower than their preferred width on a narrow page (F2: no sideways scrolling)
            foreach (KeyValuePair<Control, int> kv in fitWidth)
            {
                int x = LeftInPageHost(kv.Key);
                int avail = x > 0 ? Math.Max(S(200), right - x) : flat;
                int w = Math.Min(F(kv.Value), avail);
                if (kv.Key.Width != w) { kv.Key.Width = w; changed = true; }
            }
            foreach (Func<int, bool> fix in narrowFixes)
            {
                try { if (fix(right)) changed = true; } catch (Exception) { }
            }
            if (FitWidths()) changed = true;
            // brute-ui F7: notes that got shorter must also shrink the scroll range (no scroll bar when the page fits)
            if (changed && current >= 0 && current < pages.Count && pages[current].Scroller != null)
            {
                try { pages[current].Content.PerformLayout(); pages[current].Scroller.PerformLayout(); } catch (Exception) { }
            }
        }

        /// <summary>Every group card (Section) of the scrolling pages, for the shared width (UI41-review #5).</summary>
        private readonly List<Section> sections = new List<Section>();

        /// <summary>The 「内容宽度」 token (UI41-review #5): logical px every group and fold of a page reaches, at most.</summary>
        private const int ContentWidthPx = 840;

        /// <summary>
        /// UI41-review #5: on the shown page every group card and every 「可选：…」 fold header ends at the same right edge:
        /// min(page width, 840 logical px x text size). A card inside another card ends that card's padding earlier. Content
        /// wider than the token keeps its own width (never cut). Returns true when a width changed.
        /// </summary>
        private bool FitWidths()
        {
            if (pageHost == null || current < 0 || current >= pages.Count) return false;
            Page pg = pages[current];
            if (pg.Scroller == null) return false;
            int avail = pageHost.ClientSize.Width - pg.Host.Padding.Right - SystemInformation.VerticalScrollBarWidth - S(12 + 4);
            int right = Math.Min(avail, pg.Host.Padding.Left + F(ContentWidthPx));
            if (right <= pg.Host.Padding.Left + S(100)) return false;
            // a fold inside a card goes back to its own width while the card widths are measured: stretched, it fed the card's
            // width back into the natural width below and the page never got narrower again (full -> minimum window probe).
            // UIALIGN41 #2: once the edge is known it is stretched to the card's inner right edge again (below).
            Dictionary<FoldHeader, int> inCard = new Dictionary<FoldHeader, int>();
            foreach (FoldHeader f in folds)
            {
                if (f.IsDisposed || !InsideSection(f)) continue;
                inCard[f] = f.Width;
                int pref = f.GetPreferredSize(Size.Empty).Width;
                if (f.Width != pref) f.Width = pref;
            }
            // a card whose content is wider than the token (e.g. two curves side by side) sets the edge for the whole page,
            // so every card still ends at one x (it is wider than its minimum = its own content decides its width)
            foreach (Section sec in sections)
            {
                if (sec.IsDisposed || !sec.Visible) continue;
                int x = LeftInPageHost(sec);
                if (x < 0) continue;
                bool onPage = false;
                for (Control p = sec.Parent; p != null; p = p.Parent) if (p == pg.Host) { onPage = true; break; }
                if (onPage) right = Math.Max(right, x + NaturalWidth(sec));
            }
            bool changed = false;
            // a hidden card (closed fold) has no real position yet: it is fitted when it shows (PostRefit)
            foreach (Section sec in sections)
                if (!sec.IsDisposed && sec.Visible && FitRight(sec, pg, right)) changed = true;
            foreach (FoldHeader f in folds)
                if (!InsideSection(f) && FitRight(f, pg, right)) changed = true;
            // in-card fold bars end at the card's inner right edge (FitRight takes off the padding of every card around them)
            foreach (KeyValuePair<FoldHeader, int> kv in inCard)
            {
                if (kv.Key.Visible) FitRight(kv.Key, pg, right);
                if (kv.Key.Width != kv.Value) changed = true;
            }
            if (FitLabelColumn(pg)) changed = true;
            return changed;
        }

        private static bool InsideSection(Control c)
        {
            for (Control p = c == null ? null : c.Parent; p != null; p = p.Parent) if (p is Section) return true;
            return false;
        }

        /// <summary>
        /// UI41-review #5 + Leo 17 Nov 2026 (aligned UI): ONE label column for every page. Its width is the widest row label of
        /// ALL pages (text width, hidden 高级设置 rows included so the grid does not jump; a label over 220 logical px is left
        /// out and only pushes its own row), so the value boxes start at the same x on every page. A row label further right
        /// (a row inside an indented fold) gets that much less, so its box still lines up with the rows above it.
        /// </summary>
        private bool FitLabelColumn(Page pg)
        {
            int widest = 0, cap = F(220);
            foreach (Label l in rowLabels)
            {
                if (l.IsDisposed || l.Text.Length == 0) continue;
                int w = LabelTextWidth(l);
                if (w <= cap) widest = Math.Max(widest, w); // one long label must not push every box to the right
            }
            List<Label> mine = new List<Label>();
            List<int> xs = new List<int>();
            int baseX = int.MaxValue;
            foreach (Label l in rowLabels)
            {
                if (l.IsDisposed || l.Text.Length == 0 || !l.Visible) continue;
                bool onPage = false;
                for (Control p = l.Parent; p != null; p = p.Parent) if (p == pg.Host) { onPage = true; break; }
                if (!onPage) continue;
                int x = LeftInPageHost(l);
                if (x < 0) continue;
                mine.Add(l);
                xs.Add(x);
                baseX = Math.Min(baseX, x);
            }
            bool changed = false;
            for (int i = 0; i < mine.Count; i++)
            {
                Label l = mine[i];
                int own = LabelTextWidth(l);
                Size want = own > cap ? Size.Empty : new Size(Math.Max(own, baseX + widest - xs[i]), 0);
                if (l.MinimumSize != want) { l.MinimumSize = want; changed = true; }
            }
            return changed;
        }

        private static int LabelTextWidth(Label l) { return TextRenderer.MeasureText(l.Text, l.Font).Width + l.Padding.Horizontal; }

        /// <summary>A card's width from its own content (title + body), not from the MinimumSize FitWidths gave it.</summary>
        private static int NaturalWidth(Section sec)
        {
            int w = 0;
            foreach (Control ch in sec.Controls)
            {
                if (!ch.Visible) continue;
                w = Math.Max(w, ch.GetPreferredSize(Size.Empty).Width + ch.Margin.Horizontal);
            }
            return w + sec.Padding.Horizontal;
        }

        /// <summary>Makes c end at 'right' (minus the padding of every card it sits in); c must be on page pg.</summary>
        private bool FitRight(Control c, Page pg, int right)
        {
            if (c == null || c.IsDisposed) return false;
            bool onPage = false;
            int r = right;
            for (Control p = c.Parent; p != null; p = p.Parent)
            {
                if (p == pg.Host) { onPage = true; break; }
                Section outer = p as Section;
                if (outer != null) r -= outer.Padding.Right;
            }
            if (!onPage) return false;
            int x = LeftInPageHost(c);
            if (x < 0) return false;
            int w = Math.Max(0, r - x);
            if (c is FoldHeader)
            {
                int pref = c.GetPreferredSize(Size.Empty).Width;
                int want = Math.Max(pref, w);
                if (c.Width == want) return false;
                c.Width = want;
                return true;
            }
            Size min = new Size(w, 0);
            if (c.MinimumSize == min) return false;
            c.MinimumSize = min;
            return true;
        }

        /// <summary>Muted note that wraps at maxWidth (96-DPI px), narrower when the page is.</summary>
        private Label Note(string text, int maxWidth)
        {
            Label l = Lbl(text);
            wrapping.Add(new KeyValuePair<Label, int>(l, maxWidth));
            l.ForeColor = SystemColors.GrayText;
            l.MaximumSize = new Size(S(maxWidth), 0);
            l.Margin = P(3, 6, 6, 6);
            return l;
        }

        /// <summary>Muted hint in a grid column (like Unit) that wraps instead of pushing the page sideways (F2).</summary>
        private Label Hint(string text, int maxWidth)
        {
            Label l = Unit(text);
            wrapping.Add(new KeyValuePair<Label, int>(l, maxWidth));
            l.MaximumSize = new Size(S(maxWidth), 0);
            return l;
        }

        /// <summary>Small coloured tag ("推荐") drawn as plain text in the accent colour.</summary>
        private Label TagLabel(string text, Color c)
        {
            Label l = Lbl(text);
            l.ForeColor = c;
            // v5.0.2 (UI L5): Chinese text in the UI font (Consolas / Cascadia Mono have no CJK glyphs, Windows fell back
            // to another face); mono stays for numbers only
            l.Tag = "theme:keep-tag";
            l.BackColor = Color.Transparent;
            return l;
        }

        private void Tip(Control c, string text) { tips.SetToolTip(c, text); }

        /// <summary>First-column labels of every grid row, for one label-column width per page (UI41-review #5).</summary>
        private readonly List<Label> rowLabels = new List<Label>();

        private void Row(TableLayoutPanel g, Control label, params Control[] cells)
        {
            Label rl = label as Label;
            if (rl != null && g.ColumnCount >= 2) rowLabels.Add(rl);
            foreach (Control c in cells)
            {
                if (c.Anchor == (AnchorStyles.Top | AnchorStyles.Left)) c.Anchor = AnchorStyles.Left;
                FlushLeft(c);
            }
            Control[] all = new Control[cells.Length + 1];
            all[0] = label;
            Array.Copy(cells, 0, all, 1, cells.Length);
            Cells(g, all);
        }

        /// <summary>
        /// v4.3.1 (Leo, 换手 grid scrambled on Windows at 2160x1440 / 200 %): one grid row at FIXED cells -- column i, the
        /// grid's next row. A TableLayoutPanel that places its children by flow skips hidden ones, so hiding one cell (the
        /// 正常手 column on a narrow page, a 高级设置 cell) used to slide every later cell one place left and wrap labels
        /// into the wrong rows. With fixed cells a hidden control just leaves its cell empty (an AutoSize column / row with
        /// nothing visible collapses to 0). Every multi-column grid on the pages is filled through here (Row / AdvRow / Wide).
        /// </summary>
        private void Cells(TableLayoutPanel g, params Control[] cs)
        {
            int r = GridRows.Next(g);
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null) continue;
                GridRows.EnsureColumn(g, i);
                g.Controls.Add(cs[i], i, r);
            }
        }

        /// <summary>v4.3.1: one control across every column of the grid's next row (notes, wide check boxes).</summary>
        private void Wide(TableLayoutPanel g, Control c)
        {
            int r = GridRows.Next(g);
            g.Controls.Add(c, 0, r);
            if (g.ColumnCount > 1) g.SetColumnSpan(c, g.ColumnCount);
        }

        /// <summary>
        /// UIALIGN41 #7 (Leo 17 Nov, aligned UI): ONE cell-margin rule -- a value cell starts exactly at its column (Margin.Left
        /// 0; the gap after the label is the label's right margin), and so does the first control of a Line and every row of
        /// a Stack inside it. Number box, combo, check box, key button, arrow and hint then share one x in every column.
        /// </summary>
        private static void FlushLeft(Control c)
        {
            if (c == null) return;
            if (c.Margin.Left != 0) c.Margin = new Padding(0, c.Margin.Top, c.Margin.Right, c.Margin.Bottom);
            TableLayoutPanel t = c as TableLayoutPanel;
            if (t == null || t is Section) return;
            foreach (Control ch in t.Controls)
            {
                int col = t.ColumnCount <= 1 ? 0 : t.GetCellPosition(ch).Column;
                if (col < 0) col = t.GetPositionFromControl(ch).Column;
                if (col == 0) FlushLeft(ch);
            }
        }

        private void Row(TableLayoutPanel g, string label, params Control[] cells) { Row(g, Lbl(label), cells); }

        private Section Group(string title, Control content)
        {
            Section sec = new Section(title, content, k);
            sec.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            sections.Add(sec); // UI41-review #5: one content width per page (FitWidths)
            return sec;
        }

        /// <summary>"▸ title" header + content that is hidden until the header is clicked.</summary>
        private Control Fold(string title, Control content)
        {
            FoldHeader h = new FoldHeader(title, k);
            h.Font = Font;
            h.Margin = P(0, 4, 0, 4);
            FixUp(delegate { h.Size = h.GetPreferredSize(Size.Empty); });
            content.Visible = false;
            // Leo 17 Nov (aligned UI): a card under its fold header starts at the header's left edge (one left edge for every
            // card of the page); plain rows keep the small indent that shows they belong to the header
            // (the card keeps its own right margin like every other card: same width, same right edge)
            content.Margin = content is Section ? new Padding(0, 0, content.Margin.Right, S(6)) : P(14, 0, 0, 6);
            h.Toggled += delegate
            {
                content.Visible = h.Open;
                // the body was laid out while hidden (at x 0): re-fit card widths, notes and the label column at its real place
                PostRefit();
            };
            folds.Add(h);
            TableLayoutPanel t = Stack(h, content);
            t.Margin = P(0, 4, 0, 0);
            return t;
        }

        /// <summary>Model double -> box value; clamped before the decimal cast (brute-ui F4: 1e300 used to throw here).</summary>
        private static decimal ToDec(double v, NumericUpDown n)
        {
            return UiNum.ToDec(v, n.Minimum, n.Maximum, n.DecimalPlaces);
        }

        /// <summary>Yes / no question in the window's own look and text size (CX2: the stock MessageBox is ~9 pt).</summary>
        private bool Ask(string question)
        {
            return ThemedDialog.Show(this, question, true);
        }

        /// <summary>Information / error message with one 「知道了」 button, same look as Ask.</summary>
        private void Say(string message)
        {
            ThemedDialog.Show(this, message, false);
        }

        /// <summary>Width that fits the widest value of a number box at the mono font and DPI.</summary>
        private int NumWidth(double min, double max, int dec)
        {
            string wide = (min < 0 ? "-" : "") + new string('8', Math.Max(1, ((long)Math.Max(Math.Abs(min), Math.Abs(max))).ToString().Length))
                + (dec > 0 ? "." + new string('8', dec) : "");
            return TextRenderer.MeasureText(wide, monoFont).Width + SystemInformation.VerticalScrollBarWidth + S(16);
        }

        /// <summary>The mouse wheel scrolls the page, never the value of a box that happens to have focus.</summary>
        private void WheelScrollsPage(Control c)
        {
            c.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                ComboBox cb = s as ComboBox;
                if (cb != null && cb.DroppedDown) return;
                HandledMouseEventArgs he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
                ScrollCurrentPage(e.Delta);
            };
        }

        private void ScrollCurrentPage(int delta)
        {
            if (current < 0 || pages[current].Scroller == null) return;
            Panel p = pages[current].Scroller;
            int step = Math.Max(S(40), SystemInformation.MouseWheelScrollLines * Font.Height);
            ScrollHost h = p as ScrollHost;
            if (h != null) { h.Wheel.Queue(Math.Sign(delta) * step); return; } // v4.3: coalesced, one scroll per frame
            Point cur = p.AutoScrollPosition; // negative
            int y = Math.Max(0, -cur.Y - Math.Sign(delta) * step);
            p.AutoScrollPosition = new Point(-cur.X, y);
        }

        /// <summary>v4.3 PERF (W7): while the wheel scrolls, every animation / timer of the window stops (dashboard, previews,
        /// page titles: the same IPausable freeze as a window drag) and resumes 150 ms after the last notch. Not while the
        /// window itself is being dragged (Unfreeze resumes then).</summary>
        private void ScrollPaused(bool on)
        {
            if (IsDisposed) return;
            wheelPaused = on;
            SyncPause();
        }

        /// <summary>NumericUpDown bound to a model value. ValueChanged writes the model, publishes and schedules a save.</summary>
        private NumericUpDown Num(double min, double max, double inc, int dec, Func<double> get, Action<double> set)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = (decimal)min;
            n.Maximum = (decimal)max;
            n.Increment = (decimal)inc;
            n.DecimalPlaces = dec;
            n.Font = monoFont;
            FixUp(delegate { n.Width = NumWidth(min, max, dec); });
            n.Margin = P(0, 3, 2, 3); // the 百分比偏移 box's margins: number boxes of both kinds line up (UIALIGN41 #7)
            n.Anchor = AnchorStyles.Left;
            n.TextAlign = HorizontalAlignment.Left;
            n.ValueChanged += delegate
            {
                if (loading) return;
                set((double)n.Value);
                app.Changed();
            };
            WheelScrollsPage(n);
            refreshers.Add(delegate { n.Value = ToDec(get(), n); });
            nums.Add(n);
            return n;
        }

        // ---------------- v4.1 百分比偏移 inputs ----------------

        /// <summary>How an offset box shows its direction.</summary>
        private enum OffsetKind { Toggle, AlwaysPositive, Size }

        /// <summary>One 百分比偏移 input: [arrow toggle] + positive number + 「%」, bound to a signed PROFILE px value.</summary>
        private sealed class OffsetField
        {
            public OffsetAxis Axis;
            public OffsetKind Kind;
            public double MaxPx;
            public bool Positive = true;     // arrow state (kept while the value is 0)
            public ArrowToggle Arrow;        // Toggle only
            public ArrowToggle FixedArrow;   // AlwaysPositive only (上抬 = ↑, not clickable)
            public PctUpDown Box;
            public Label UnitLabel;
            public TableLayoutPanel Line;    // what a page puts into its grid
            public Func<double> Get;
            public Action<double> Set;
        }

        /// <summary>Re-reads the 百分比偏移 boxes (resolution / FOV changed on 常规, or another page edited the profile).</summary>
        private readonly List<Action> offsetRefreshers = new List<Action>();

        private const string UnitTip = "百分比偏移：屏幕中心到上边缘 = 100%（按屏幕高度的一半算）。换分辨率不用重调。";

        /// <summary>
        /// v4.1 百分比偏移 input (Leo, 17 Nov 2026): storage stays px (double, never rounded); the box shows |px| as % of half
        /// the game picture height (OffsetUnits, i.e. the ViewScale conversion), 2 decimals, spin step 0.10 %, typing 0.01 %.
        /// Toggle: the arrow button flips the sign (X → + right / ← left, Y ↑ + up / ↓ down, the profile convention).
        /// AlwaysPositive: fixed arrow (上抬 ↑). Size: no arrow (a distance with no direction). maxPx = the px range the model allows.
        /// </summary>
        private OffsetField Offset(OffsetAxis axis, OffsetKind kind, double maxPx, Func<double> get, Action<double> set)
        {
            OffsetField f = new OffsetField();
            f.Axis = axis; f.Kind = kind; f.MaxPx = maxPx; f.Get = get; f.Set = set;
            PctUpDown n = new PctUpDown();
            n.Axis = axis;
            n.Minimum = 0;
            n.Maximum = UiNum.MaxPct(maxPx, G);
            n.DecimalPlaces = OffsetUnits.Decimals;
            n.Increment = (decimal)OffsetUnits.SpinStepPct;
            n.Font = monoFont;
            n.Margin = P(0, 3, 2, 3);
            n.Anchor = AnchorStyles.Left;
            n.TextAlign = HorizontalAlignment.Left;
            FixUp(delegate { n.Width = NumWidth(0, 999, OffsetUnits.Decimals); });
            f.Box = n;
            n.ValueChanged += delegate
            {
                if (loading) return;
                WriteOffset(f);
            };
            n.DirectionTyped += delegate(bool pos)
            {
                if (f.Kind != OffsetKind.Toggle || pos == f.Positive) return;
                f.Positive = pos;
                ShowArrow(f);
                if (!loading) WriteOffset(f); // same magnitude, new direction
            };
            WheelScrollsPage(n);
            nums.Add(n);

            List<Control> parts = new List<Control>();
            if (kind == OffsetKind.Toggle)
            {
                // drawn arrow (ArrowToggle): the Button glyph did not show under Mono's flat buttons at this size
                ArrowToggle b = new ArrowToggle(axis, k);
                b.Margin = P(3, 3, 2, 3);
                b.Anchor = AnchorStyles.Left;
                FixUp(delegate { b.Size = ArrowSize(n); });
                b.Toggled += delegate
                {
                    f.Positive = !f.Positive;
                    ShowArrow(f);
                    double cur = f.Get();
                    if (cur != 0 && !double.IsNaN(cur)) { f.Set(-cur); app.Changed(); } // exact: no rounding on a flip
                };
                Tip(b, axis == OffsetAxis.X ? "点一下换方向：← 往左 / → 往右" : "点一下换方向：↑ 往上 / ↓ 往下");
                f.Arrow = b;
                parts.Add(b);
            }
            else if (kind == OffsetKind.AlwaysPositive)
            {
                ArrowToggle a = new ArrowToggle(axis, k);
                a.Fixed = true;
                a.Margin = P(3, 3, 2, 3);
                a.Anchor = AnchorStyles.Left;
                a.ForeColor = Theme.ColAccent2;
                FixUp(delegate { a.Size = ArrowSize(n); });
                f.FixedArrow = a;
                parts.Add(a);
            }
            parts.Add(n);
            Label u = Unit(OffsetUnits.Unit);
            Tip(u, UnitTip);
            f.UnitLabel = u;
            parts.Add(u);
            f.Line = Line(parts.ToArray());
            offsetFields.Add(f);
            ShowArrow(f);
            Action refresh = delegate { RefreshOffset(f); };
            refreshers.Add(refresh);
            offsetRefreshers.Add(refresh);
            return f;
        }

        /// <summary>Width of the arrow slot: the arrow button and the spacer of rows without one (one value, UIALIGN41 #7).</summary>
        private int ArrowW() { return Math.Max(Font.Height + S(6), TextRenderer.MeasureText("←", Font).Width + S(10)); }

        /// <summary>Arrow button size: exactly as tall as its number box (UIALIGN41 #4: one height within the row), the slot width.</summary>
        private Size ArrowSize(NumericUpDown n)
        {
            return new Size(ArrowW(), Math.Max(S(12), n.Height));
        }

        /// <summary>Empty cell as wide as an arrow button: keeps number boxes of mixed rows in one column.</summary>
        private Label ArrowSpacer()
        {
            Label l = Blank();
            l.Margin = P(3, 3, 2, 3);
            FixUp(delegate { l.Size = new Size(ArrowW(), 1); });
            return l;
        }

        /// <summary>Buttons and key buttons of the pages (FitControlHeights gives them one height).</summary>
        private readonly List<Control> lineControls = new List<Control>();
        private readonly List<OffsetField> offsetFields = new List<OffsetField>();

        /// <summary>
        /// UIALIGN41 #4 (Leo 17 Nov 2026, aligned UI): ONE height for the controls of a line = the height the system gives a
        /// drop-down list / number box with the current fonts (both follow the font; their exact height is UNVERIFIED off
        /// Windows), and never less than the text plus 2 px above and below.
        /// </summary>
        private int ControlH()
        {
            // v5.0.2 (UI M7): the number box is the one control whose height Windows fixes (UpDownBase: font height + 7 px
            // with a single border), so the line height is ITS preferred height; buttons and combos are fitted to it. A
            // probe box with the number font is measured, so the rule holds before any page with a number box is built.
            // never less than the button text plus the button's own frame and padding (the 150 % layout probe: text 28,
            // inner 26 at Font.Height + 4 px), so a button is never cut to fit a shorter number box
            int h = Font.Height + S(6);
            if (lineH <= 0)
            {
                try
                {
                    using (NumericUpDown probe = new NumericUpDown())
                    {
                        probe.Font = monoFont;
                        lineH = probe.PreferredHeight;
                    }
                }
                catch (Exception) { lineH = 0; }
            }
            if (lineH > 0) return Math.Max(h, lineH);
            if (cbProfile != null && !cbProfile.IsDisposed) h = Math.Max(h, cbProfile.Height);
            return h;
        }

        private int lineH;

        /// <summary>v5.0.2 (UI M7): every themed drop-down list below root gets the line height (ItemHeight = line height
        /// minus the combo's own frame), so combo = number box = button in every row.</summary>
        private void FitComboHeights(Control root)
        {
            int h = ControlH();
            Stack<Control> todo = new Stack<Control>();
            todo.Push(root);
            while (todo.Count > 0)
            {
                Control c = todo.Pop();
                ComboBox cb = c as ComboBox;
                if (cb != null) Theme.MatchComboHeight(cb, h);
                foreach (Control ch in c.Controls) todo.Push(ch);
            }
        }

        /// <summary>Applies ControlH to every button / key button and sizes every arrow to its box (after fonts or theme changed).</summary>
        private void FitControlHeights()
        {
            int h = ControlH();
            lineControls.RemoveAll(delegate(Control c) { return c.IsDisposed; });
            foreach (Control c in lineControls)
            {
                Size mn = new Size(c.MinimumSize.Width, h), mx = new Size(c.MaximumSize.Width, h);
                try
                {
                    if (c.MinimumSize != mn) c.MinimumSize = mn;
                    if (c.MaximumSize != mx) c.MaximumSize = mx;
                }
                catch (Exception ex) { Program.Log("UI: line height " + c.Name + ": " + ex.Message); } // one bad grid never stops a page
            }
            foreach (OffsetField f in offsetFields)
            {
                ArrowToggle a = f.Arrow ?? f.FixedArrow;
                if (a == null || a.IsDisposed || f.Box.IsDisposed) continue;
                Size s = ArrowSize(f.Box);
                if (a.Size != s) a.Size = s;
            }
        }

        private void ShowArrow(OffsetField f)
        {
            if (f.Arrow == null) return;
            f.Arrow.Positive = f.Positive;
            double px = 0;
            try { px = f.Get(); } catch (Exception) { }
            f.Arrow.Dim = OffsetUnits.Round(OffsetUnits.ToPct(px, G)) == 0;
        }

        /// <summary>Box -> model: px = ±ToPx(box %), never rounded.</summary>
        private void WriteOffset(OffsetField f)
        {
            double mag = OffsetUnits.ToPx((double)f.Box.Value, G);
            double px = (f.Kind == OffsetKind.Toggle && !f.Positive) ? -mag : mag;
            if (px > f.MaxPx) px = f.MaxPx;
            if (px < -f.MaxPx) px = -f.MaxPx;
            f.Set(px);
            ShowArrow(f); // dim while 0.00 %
            app.Changed();
        }

        /// <summary>Model -> box: range follows the current resolution / FOV, arrow follows the sign (0 keeps the arrow).</summary>
        private void RefreshOffset(OffsetField f)
        {
            bool was = loading;
            loading = true;
            try
            {
                decimal max = UiNum.MaxPct(f.MaxPx, G);
                if (f.Box.Maximum != max)
                {
                    if (f.Box.Value > max) f.Box.Value = max;
                    f.Box.Maximum = max;
                }
                double px = f.Get();
                if (double.IsNaN(px) || double.IsInfinity(px)) px = 0;
                if (f.Kind == OffsetKind.Toggle && OffsetUnits.Round(OffsetUnits.ToPct(px, G)) != 0) f.Positive = px > 0;
                ShowArrow(f);
                f.Box.Value = UiNum.ToDec(Math.Abs(OffsetUnits.ToPct(px, G)), f.Box.Minimum, f.Box.Maximum, f.Box.DecimalPlaces);
            }
            finally { loading = was; }
        }

        /// <summary>After 常规 changed 分辨率 / 画面比例 / FOV: every 百分比偏移 box re-reads (its % changes, the px do not).</summary>
        private void RefreshOffsets()
        {
            foreach (Action a in offsetRefreshers) { try { a(); } catch (Exception ex) { Program.Log("UI: offset refresh: " + ex.Message); } }
        }

        private ComboBox Choice(string[] labels, string[] values, Func<string> get, Action<string> set)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(labels);
            FixUp(delegate
            {
                int w = F(70);
                foreach (string l in labels) w = Math.Max(w, TextRenderer.MeasureText(l, Font).Width + SystemInformation.VerticalScrollBarWidth + S(16));
                c.Width = w;
            });
            c.Margin = P(3, 3, 3, 3);
            c.Anchor = AnchorStyles.Left;
            c.SelectedIndexChanged += delegate
            {
                if (loading || c.SelectedIndex < 0) return;
                set(values[c.SelectedIndex]);
                app.Changed();
            };
            WheelScrollsPage(c);
            refreshers.Add(delegate
            {
                int idx = Array.IndexOf(values, get());
                c.SelectedIndex = idx < 0 ? 0 : idx;
            });
            return c;
        }

        private CheckBox Check(string text, Func<bool> get, Action<bool> set)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Anchor = AnchorStyles.Left;
            c.Margin = P(3, 3, 8, 3);
            c.CheckedChanged += delegate
            {
                if (loading) return;
                set(c.Checked);
                app.Changed();
            };
            refreshers.Add(delegate { c.Checked = get(); });
            return c;
        }

        private Button Btn(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Anchor = AnchorStyles.Left;
            b.Margin = P(3, 2, 3, 2);
            b.Padding = P(6, 1, 6, 1);
            b.Click += onClick;
            lineControls.Add(b); // one height with the boxes (FitControlHeights)
            return b;
        }

        /// <summary>Primary (neon) button.</summary>
        private Button AccentBtn(string text, EventHandler onClick)
        {
            Button b = Btn(text, onClick);
            b.Tag = "theme:accent";
            return b;
        }

        /// <summary>Enables a set of controls while cond() holds (refreshed with the values and on demand).</summary>
        private Action EnableWhen(Func<bool> cond, params Control[] targets)
        {
            Action a = delegate { bool on = cond(); foreach (Control c in targets) c.Enabled = on; };
            refreshers.Add(a);
            return a;
        }

        private void SetStatus(string text, bool warn)
        {
            if (lblSave == null) return;
            lblSave.Text = text ?? "";
            Tip(lblSave, text ?? "");
            lblSave.ForeColor = warn ? Theme.ColWarn : Theme.ColAccent;
            saveFlash.Stop();
            if (!warn) saveFlash.Start(); // fades back to muted
        }

        /// <summary>
        /// Commits text typed into a number box that still has focus (NumericUpDown only parses its text on leave;
        /// reading Value parses it). Called before anything that could overwrite or lose it: page switch, profile switch,
        /// tutorial, hiding the window, 退出.
        /// </summary>
        public void CommitEdits()
        {
            foreach (NumericUpDown n in nums)
            {
                try { if (!n.IsDisposed) { decimal unused = n.Value; } }
                catch (Exception) { }
            }
        }

        /// <summary>「高级设置」 on: every page also shows its non-essential options.</summary>
        private bool Advanced { get { return UiPrefs.Advanced(G); } }

        /// <summary>Registers controls that are visible only in 「高级设置」; returns the first.</summary>
        private Control AdvOnly(params Control[] cs)
        {
            foreach (Control c in cs) advOnly.Add(c);
            return cs.Length > 0 ? cs[0] : null;
        }

        /// <summary>A grid row (label + cells) that exists only in 「高级设置」.</summary>
        private void AdvRow(TableLayoutPanel g, Control label, params Control[] cells)
        {
            AdvOnly(label);
            AdvOnly(cells);
            Row(g, label, cells);
        }

        private void AdvRow(TableLayoutPanel g, string label, params Control[] cells) { AdvRow(g, Lbl(label), cells); }

        private void RefreshAdvanced()
        {
            bool on = Advanced;
            if (pageHost != null) pageHost.SuspendLayout(); // v4.3: one layout pass, not one per Visible change
            try
            {
                foreach (Control c in advOnly) c.Visible = on;
                int state = on ? 1 : 0;
                if (state != advState)
                {
                    // CX2 B9/B10: with 高级设置 on the extra options are shown at once (the fold headers stay, so a section
                    // can still be closed); off -> every fold closes again (fewest options).
                    if (advState >= 0 || on) foreach (FoldHeader f in folds) if (!manualFolds.Contains(f)) f.Open = on;
                    advState = state;
                }
                StylePanel sp = stylePanel as StylePanel;
                if (sp != null) sp.Advanced = on;
            }
            finally { if (pageHost != null) pageHost.ResumeLayout(false); }
            if (advLazy != null) advLazy.MarkAllApplied();
            PostRefit(); // rows came or went: card widths / notes / label column follow
        }

        /// <summary>v4.3 PERF (W7, 高级设置 toggle lag): the toggle changes no value, only what is shown. Applied now to the
        /// current page (its refreshers, its 高级设置 rows and folds) plus the refreshers that belong to no page, inside one
        /// SuspendLayout; every other page is applied on its first ShowPage. No whole-window RefreshValues, no thumbnail
        /// re-render, no filter/hardware read beyond the current page's own refreshers (which read FilterHost's snapshot).</summary>
        private void ToggleAdvanced()
        {
            bool was = loading;
            loading = true;
            try
            {
                advState = Advanced ? 1 : 0; // folds of each page follow the toggle when that page is applied
                if (advLazy != null) advLazy.ChangedOn(current);
                for (int i = 0; i < refreshers.Count; i++) if (!InAnyPage(i)) refreshers[i](); // v5.0.2: pages built in any order
                if (current >= 0 && current < pages.Count) ApplyAdvancedPage(current);
            }
            finally { loading = was; }
            PostRefit();
        }

        /// <summary>v4.3: 高级设置 state + the page's own refreshers for one page (also its folds and, for 外观, StylePanel).</summary>
        private void ApplyAdvancedPage(int index)
        {
            Page pg = pages[index];
            bool on = Advanced;
            bool was = loading;
            loading = true;
            pg.Host.SuspendLayout();
            if (pg.Content != null) pg.Content.SuspendLayout();
            try
            {
                for (int i = pg.RefStart; i < pg.RefEnd && i < refreshers.Count; i++) refreshers[i]();
                for (int i = pg.AdvStart; i < pg.AdvEnd && i < advOnly.Count; i++) advOnly[i].Visible = on;
                for (int i = pg.FoldStart; i < pg.FoldEnd && i < folds.Count; i++) if (!manualFolds.Contains(folds[i])) folds[i].Open = on;
                StylePanel sp = stylePanel as StylePanel;
                if (sp != null && sp.Parent == pg.Host) sp.Advanced = on;
            }
            finally
            {
                if (pg.Content != null) pg.Content.ResumeLayout(false);
                pg.Host.ResumeLayout(false);
                loading = was;
            }
            if (advLazy != null) advLazy.MarkApplied(index);
        }

        private bool refitPosted;

        /// <summary>Leo 17 Nov (aligned UI): one FitWrapping pass after the pending layout, e.g. when a fold opened or 高级设置
        /// changed, so cards, notes and the label column use the real positions of what just became visible.</summary>
        private void PostRefit()
        {
            if (refitPosted || IsDisposed || !IsHandleCreated) return;
            refitPosted = true;
            try
            {
                BeginInvoke((Action)delegate
                {
                    refitPosted = false;
                    if (IsDisposed || current < 0 || current >= pages.Count) return;
                    try
                    {
                        Page pg = pages[current];
                        if (pg.Content != null) pg.Content.PerformLayout();
                        FitWrapping();
                        if (pg.Content != null) pg.Content.PerformLayout();
                        if (pg.Scroller != null) pg.Scroller.PerformLayout();
                    }
                    catch (Exception ex) { Program.Log("UI: refit: " + ex.Message); }
                });
            }
            catch (Exception) { refitPosted = false; }
        }

        // ---------------- pages & navigation ----------------

        private void AddPage(int index, Func<Control> build, bool scroll)
        {
            Page pg = new Page();
            pg.Build = build;
            pg.Scroll = scroll;
            pg.Id = UiPages.Ids[index];
            pg.Title = UiPages.Titles[index];
            pg.Host = new Panel();
            pg.Host.Dock = DockStyle.Fill;
            pg.Host.Visible = false;
            pg.Host.Padding = P(22, 12, 10, 0);
            pg.Host.BackColor = Theme.ColSurface;
            pg.Heading = new PageTitle(pg.Title, UiPages.Codes[index], k);
            pg.Heading.Font = Font;
            pg.Heading.Dock = DockStyle.Top;
            PageTitle heading = pg.Heading;
            FixUp(delegate { heading.Height = heading.GetPreferredSize(Size.Empty).Height; });
            if (scroll)
            {
                pg.Scroller = new ScrollHost(); // v4.3 PERF (W7): double-buffered, coalesced wheel
                ((ScrollHost)pg.Scroller).Wheel.Pause = ScrollPaused;
                pg.Scroller.Dock = DockStyle.Fill;
                pg.Scroller.AutoScroll = true;
                pg.Scroller.Padding = P(0, 10, 0, 12);
                pg.Host.Controls.Add(pg.Heading);
                pg.Host.Controls.Add(pg.Scroller);
                pg.Scroller.BringToFront(); // docked last -> fills below the heading
            }
            else if (index == 0)
            {
                // the dashboard brings its own title bar: no page heading above it
                pg.Host.Padding = P(12, 10, 10, 10);
            }
            else
            {
                // a control that fills and scrolls by itself (外观 = StylePanel): heading + content, no outer scroller
                pg.Host.Padding = P(22, 12, 10, 8);
                pg.Host.Controls.Add(pg.Heading);
            }
            pageHost.Controls.Add(pg.Host);

            // v5.1 (W25): the navigation item is made by BuildNav (menu order = NavModel, not page order)
            pg.RefStart = pg.RefEnd = refreshers.Count; // empty until the page is built (EnsureBuilt)
            pg.AdvStart = pg.AdvEnd = advOnly.Count;
            pg.FoldStart = pg.FoldEnd = folds.Count;
            pages.Add(pg);
        }

        /// <summary>v5.0.2: puts a built page body into its shell (same placement rules as v5.0.1's AddPage).</summary>
        private void AttachContent(Page pg, int index, Control content)
        {
            if (pg.Scroll)
            {
                content.Location = new Point(0, S(12));
                pg.Scroller.Controls.Add(content);
                pg.Content = content;
            }
            else if (index == 0)
            {
                content.Dock = DockStyle.Fill;
                pg.Host.Controls.Add(content);
            }
            else
            {
                content.Dock = DockStyle.Fill;
                pg.Host.Controls.Add(content);
                content.BringToFront();
            }
        }

        // ---------------- v5.1 (W25, decision 13) navigation: 准星 ▾ (sub-menu) + 滤镜; small links below ----------------

        private NavModel navModel;
        /// <summary>One entry per navModel.Items item: the NavItem drawn for it and its navBar row.</summary>
        private readonly List<NavItem> navItems = new List<NavItem>();
        private readonly List<int> navRows = new List<int>();
        private int navSpacerRow = -1, navTutorialRow = -1;

        /// <summary>Short menu labels (the page heading keeps UiPages.Titles, e.g. 「分辨率 · 常规」).</summary>
        internal static string NavLabel(string id, string title)
        {
            switch (id)
            {
                case "general": return "常规";
                case "dashboard": return "首页";
                default: return title;
            }
        }

        private void BuildNav()
        {
            navModel = NavModel.Default();
            foreach (Page pg in pages) if (navModel.IndexOfPage(pg.Id) < 0) navModel.AddLink(pg.Id, pg.Title); // pages others add
            int row = 0;
            bool spacerDone = false;
            for (int i = 0; i < navModel.Items.Count; i++)
            {
                NavModel.Item it = navModel.Items[i];
                if (it.Link && !spacerDone)
                {
                    navSpacerRow = row;
                    navBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // spacer: links sit at the bottom
                    navBar.Controls.Add(Blank(), 0, row++);
                    spacerDone = true;
                }
                int pi = it.Group ? -1 : UiPages.IndexOf(it.Id);
                Page pg = pi >= 0 && pi < pages.Count ? pages[pi] : null;
                string title = it.Title ?? (pg != null ? NavLabel(pg.Id, pg.Title) : it.Id);
                string index = it.Group ? "▾" : "";
                if (it.Parent != null) title = "    " + title; // sub-menu: indented under 准星
                NavItem n = new NavItem(index, title, k);
                n.Font = Font;
                n.Dock = DockStyle.Fill;
                n.Margin = new Padding(0);
                int item = i;
                Tip(n, it.Group ? "准星的细节设置：外观、移动、侧身、换手、跳跃、后坐力、按键、常规" : "Ctrl+Tab 切换页面，↑ ↓ 选择");
                n.Click += delegate { NavActivate(item); };
                n.Navigate += delegate(int d)
                {
                    int to = navModel.Step(item, d);
                    if (to < 0) return;
                    NavActivate(to);
                    navItems[to].Focus();
                };
                if (pg != null) pg.Nav = n;
                navItems.Add(n);
                navRows.Add(row);
                navBar.RowStyles.Add(new RowStyle(SizeType.Absolute, n.Height));
                navBar.Controls.Add(n, 0, row++);
            }
            if (!spacerDone)
            {
                navSpacerRow = row;
                navBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                navBar.Controls.Add(Blank(), 0, row++);
            }
            foreach (Page pg in pages)
                if (pg.Nav == null) { pg.Nav = new NavItem("", pg.Title, k); pg.Nav.Visible = false; } // never: every page has an entry

            btnTutorial = new GlowButton("", k);
            btnTutorial.Font = Font;
            btnTutorial.Dock = DockStyle.Fill;
            btnTutorial.Margin = P(14, 6, 14, 6);
            btnTutorial.Click += delegate { OpenTutorial(); };
            navTutorialRow = row;
            navBar.RowStyles.Add(new RowStyle(SizeType.Absolute, F(56)));
            navBar.Controls.Add(btnTutorial, 0, row++);
            navBar.RowCount = row;
            navHost.ClientSizeChanged += delegate { FitNav(); };
            navHost.Resize += delegate { FitNav(); };
            RefreshTutorialButton();
        }

        /// <summary>Click / Enter / ↑ ↓ on a menu item: 准星 opens its sub-menu and shows its first page (a second click on
        /// 准星 while one of its pages is shown closes the sub-menu); a page item shows that page.</summary>
        private void NavActivate(int item)
        {
            if (navModel == null || item < 0 || item >= navModel.Items.Count) return;
            NavModel.Item it = navModel.Items[item];
            if (it.Group)
            {
                bool inGroup = current >= 0 && current < pages.Count && navModel.IndexOfPage(pages[current].Id) >= 0
                               && navModel.Items[navModel.IndexOfPage(pages[current].Id)].Parent == it.Id;
                if (inGroup && navModel.Expanded) { SetNavExpanded(false); return; }
                SetNavExpanded(true);
                if (!inGroup) ShowPage(UiPages.IndexOf(navModel.FirstChild(it.Id)));
                return;
            }
            ShowPage(UiPages.IndexOf(it.Id));
        }

        private void SetNavExpanded(bool on)
        {
            if (navModel == null) return;
            navModel.Expanded = on;
            navBar.SuspendLayout();
            try
            {
                for (int i = 0; i < navItems.Count; i++)
                {
                    bool vis = navModel.IsVisible(navModel.Items[i]);
                    if (navItems[i].Visible != vis) navItems[i].Visible = vis;
                }
            }
            finally { navBar.ResumeLayout(false); }
            FitNav();
        }

        /// <summary>Pages in menu order (Ctrl+Tab walks this, the sub-menu opening as needed).</summary>
        private List<int> NavOrder()
        {
            List<int> r = new List<int>();
            if (navModel != null)
                foreach (NavModel.Item it in navModel.Items)
                {
                    if (it.Group) continue;
                    int i = UiPages.IndexOf(it.Id);
                    if (i >= 0 && !r.Contains(i)) r.Add(i);
                }
            for (int i = 0; i < pages.Count; i++) if (!r.Contains(i)) r.Add(i);
            return r;
        }

        /// <summary>
        /// Fits the navigation into its column: rows shrink from 40 to 26 px (100 %) on short windows; if even that does not
        /// fit, the navigation scrolls (navHost) instead of hiding the tutorial button. Hidden sub-menu rows are 0 px.
        /// </summary>
        private void FitNav()
        {
            if (fittingNav || navModel == null || navTutorialRow < 0 || navBar.RowStyles.Count <= navTutorialRow) return;
            fittingNav = true;
            try
            {
                int shown = 0;
                for (int i = 0; i < navItems.Count; i++) if (navModel.IsVisible(navModel.Items[i])) shown++;
                shown = Math.Max(1, shown);
                int pad = navBar.Padding.Vertical + S(4);
                int availH = Math.Max(0, navHost.Height);
                int gap = F(12); // the spacer keeps at least this between the main menu and the links
                int tut = availH >= shown * F(26) + F(56) + pad + gap ? F(56) : F(44);
                if ((int)navBar.RowStyles[navTutorialRow].Height != tut) navBar.RowStyles[navTutorialRow].Height = tut;
                int h = Math.Max(F(26), Math.Min(F(40), (availH - pad - tut - gap) / shown));
                int needed = shown * h + tut + pad + gap;
                bool scroll = needed > availH;
                int w = Math.Max(0, navHost.Width - (scroll ? SystemInformation.VerticalScrollBarWidth : 0));
                navBar.SuspendLayout();
                for (int i = 0; i < navItems.Count; i++)
                {
                    int want = navModel.IsVisible(navModel.Items[i]) ? h : 0;
                    RowStyle rs = navBar.RowStyles[navRows[i]];
                    if ((int)rs.Height != want) rs.Height = want;
                }
                Size size = new Size(w, scroll ? needed : availH);
                if (navBar.Size != size) navBar.Size = size;
                navBar.ResumeLayout(true);
                navHost.HorizontalScroll.Value = 0;
            }
            finally { fittingNav = false; }
        }

        private void RefreshTutorialButton()
        {
            if (btnTutorial == null) return;
            bool first = !G.TutorialDone;
            btnTutorial.Text = first ? "▶ 首次设置教程" : "设置教程"; // one space: the README quotes 「▶ 首次设置教程」
            btnTutorial.Highlight = first;
            Tip(btnTutorial, first ? "一步一步带你设置好，随时可以退出" : "随时可以再看一遍");
        }

        private void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            if (index == current) return;
            CommitEdits();
            CancelCapture();
            Page next = pages[index];
            bool fresh = !next.Built;
            EnsureBuilt(index); // v5.0.2: first visit builds the body (filled by EnsureBuilt)
            if (!fresh && advLazy != null && advLazy.Stale(index)) ApplyAdvancedPage(index); // v4.3: 高级设置 changed on another page
            next.Host.Visible = true;
            next.Host.BringToFront();
            if (current >= 0)
            {
                pages[current].Host.Visible = false;
                pages[current].Nav.Selected = false;
            }
            current = index;
            next.Nav.Selected = true;
            if (navModel != null)
            {
                // v5.1: the 准星 sub-menu is open while one of its pages is shown, closed on 滤镜 / 首页 / 指南 / 反馈
                int mi = navModel.IndexOfPage(next.Id);
                SetNavExpanded(mi >= 0 && navModel.Items[mi].Parent != null);
            }
            RefreshOffsets(); // 常规 (分辨率 / FOV) or the tutorial may have changed what 1 % is
            if (next.Scroller != null && next.Content != null)
            {
                // F2 / F7: wrap widths follow this page's real column positions; then recompute its scroll range
                try { FitWrapping(); next.Content.PerformLayout(); next.Scroller.PerformLayout(); } catch (Exception) { }
            }
            if (next.Heading.Parent != null) next.Heading.Restart();
            if (next.Id == "hand" && handSeen != app.ChangeCount) // v5.1: only when something changed since 换手 was last shown
            {
                handSeen = app.ChangeCount;
                // the 自动 rows follow the normal hand, which other pages may have changed
                bool was = loading;
                loading = true;
                try { foreach (Action r in handRefreshers) r(); }
                finally { loading = was; }
                PaintHandRows();
            }
        }

        /// <summary>For TutorialForm: show a page by id or title, with fresh values.</summary>
        public void JumpToPage(string idOrTitle)
        {
            if (InvokeRequired) { try { BeginInvoke((Action<string>)JumpToPage, idOrTitle); } catch (Exception) { } return; }
            CommitEdits();
            RefreshValues();
            app.UpdateTray();
            app.Changed();
            int i = UiPages.IndexOf(idOrTitle);
            if (i < 0) return;
            if (!Visible) app.ShowSettings();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowPage(i);
        }

        // ---------------- other workers' controls (DashboardPanel, StylePanel, TutorialForm) ----------------

        private Control Missing(string what)
        {
            Label l = Note(what + "没有加载成功，详细信息在 crash.log。", 460);
            return l;
        }

        /// <summary>
        /// 「首次设置教程」 (v4-requests/TUTORIAL.md): modal TutorialForm; ValuesChanged -> app.Changed() (overlay + save while the
        /// user tests in game); afterwards the pages re-read the profile and the button highlight follows G.TutorialDone.
        /// The tutorial's jumpToPage keys (首页 按键 常规 后坐力 移动 侧身 换手 跳跃 外观 指南 反馈) map through UiPages.IndexOf.
        /// </summary>
        private void OpenTutorial()
        {
            if (tutorial != null && !tutorial.IsDisposed) { tutorial.Activate(); return; }
            CommitEdits();
            CancelCapture();
            TutorialForm t = null;
            try { t = new TutorialForm(G, delegate(string page) { JumpToPage(page); }); }
            catch (Exception ex) { Program.Log("UI: TutorialForm: " + ex); t = null; }
            if (t == null)
            {
                Say("教程打不开，详细信息在 crash.log。");
                return;
            }
            try
            {
                tutorial = t;
                t.ValuesChanged += delegate { if (!IsDisposed) app.Changed(); };
                t.TopMost = TopMost;
                t.ShowDialog(this);
            }
            catch (Exception ex) { Program.Log("UI: tutorial: " + ex); }
            finally
            {
                tutorial = null;
                t.Dispose();
            }
            if (IsDisposed) return;
            RefreshValues();          // the tutorial edited the active profile
            PaintHandRows();
            RefreshTutorialButton();  // highlight off once G.TutorialDone
            app.UpdateTray();
            app.Changed();
        }

        // ---------------- key capture (single key, mouse button or Ctrl/Shift/Alt combo) ----------------

        private static int ModsOf(Keys keyData)
        {
            int m = 0;
            if ((keyData & Keys.Control) == Keys.Control) m |= KeyBindings.ModCtrl;
            if ((keyData & Keys.Shift) == Keys.Shift) m |= KeyBindings.ModShift;
            if ((keyData & Keys.Alt) == Keys.Alt) m |= KeyBindings.ModAlt;
            return m;
        }

        private KeyButton KeyBtn(int action)
        {
            KeyButton b = new KeyButton();
            b.Action = action;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FixUp(delegate { b.MinimumSize = new Size(Math.Max(F(150), TextRenderer.MeasureText("鼠标侧键1（XButton1）", Font).Width + S(18)), Font.Height + S(10)); });
            lineControls.Add(b); // FitControlHeights then sets the one control height (UIALIGN41 #4: was a fixed 28 px)
            b.Anchor = AnchorStyles.Left;
            b.Margin = P(3, 3, 8, 3);
            b.Click += delegate
            {
                if (suppressNextClick) { suppressNextClick = false; return; }
                if (capturing != null && capturing != b) CancelCapture();
                capturing = b;
                capture.Start();
                b.Text = capture.Shown;
                b.ForeColor = Theme.ColAccent;
            };
            b.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (capturing != b) return;
                int vk = 0;
                if (e.Button == MouseButtons.Left) vk = 1;
                else if (e.Button == MouseButtons.Right) vk = 2;
                else if (e.Button == MouseButtons.Middle) vk = 4;
                else if (e.Button == MouseButtons.XButton1) vk = 5;
                else if (e.Button == MouseButtons.XButton2) vk = 6;
                if (vk == 0) return;
                if (e.Button == MouseButtons.Left) suppressNextClick = true;
                if (capture.Mouse(vk, ModsOf(Control.ModifierKeys)) == KeyCapture.Result.Done) FinishCapture();
            };
            b.Leave += delegate { if (capturing == b) CancelCapture(); };
            refreshers.Add(delegate { if (capturing != b) b.Text = KeyText(b.Action); });
            return b;
        }

        /// <summary>The key of an action for a key button: 「未设置」 when it has none.</summary>
        private string KeyText(int action)
        {
            if (action >= FilterKeyBase) return FilterKeyText(action - FilterKeyBase); // v4.2 滤镜 key buttons
            return G.Keys.Get(action) <= 0 ? "未设置" : G.Keys.ComboOf(action);
        }

        private void CancelCapture()
        {
            KeyButton b = capturing;
            capturing = null;
            if (b != null)
            {
                b.Text = KeyText(b.Action);
                b.ForeColor = Theme.ColText;
            }
        }

        /// <summary>Applies capture.Vk / capture.Mods to the capturing button's action (asks first if another action has it).</summary>
        private void FinishCapture()
        {
            KeyButton b = capturing;
            capturing = null;
            if (b == null) return;
            b.ForeColor = Theme.ColText;
            int vk = capture.Vk, mods = capture.Mods;
            if (b.Action >= FilterKeyBase) { FinishFilterCapture(b, vk, mods); return; } // v4.2 滤镜 key (PageFilter)
            string filterUse = CrosshairKeyFilterClash(vk); // v4.2: a crosshair key never uses a 滤镜 key's base key
            if (filterUse != null)
            {
                b.Text = KeyText(b.Action);
                SetStatus(filterUse, true);
                return;
            }
            int other = KeyConflict.Find(G.Keys, vk, mods, b.Action);
            if (other >= 0)
            {
                bool yes = Ask(KeyConflict.Question(G.Keys, b.Action, vk, mods, other));
                suppressNextClick = false; // the button never sees the click that started this capture once a dialog was up
                if (!yes)
                {
                    b.Text = KeyText(b.Action);
                    return;
                }
            }
            // 是 moves the key: the other action takes this action's old key (never two actions on one key)
            string done = KeyConflict.Apply(G.Keys, b.Action, vk, mods, other);
            RefreshValues(); // the other action's key button (and pages that show key names) follow
            app.UpdateTray();
            app.Changed();
            SetStatus(done, false);
        }

        private bool TryCaptureKey(Keys keyData)
        {
            if (capturing == null) return false;
            KeyCapture.Result r = capture.KeyDown((int)(keyData & Keys.KeyCode), ModsOf(keyData));
            if (r == KeyCapture.Result.Cancelled) CancelCapture();
            else if (r == KeyCapture.Result.Done) FinishCapture();
            else if (capturing != null) capturing.Text = capture.Shown;
            return true;
        }

        private const int WmKeyUp = 0x0101, WmSysKeyUp = 0x0105;

        /// <summary>Key up while capturing: a modifier pressed and released on its own binds that modifier.</summary>
        protected override bool ProcessKeyPreview(ref Message m)
        {
            if (capturing != null && (m.Msg == WmKeyUp || m.Msg == WmSysKeyUp))
            {
                int vk = (int)(m.WParam.ToInt64() & 0xFF);
                KeyCapture.Result r = capture.KeyUp(vk, KeyBindings.ModsDown(Native.IsDown));
                if (r == KeyCapture.Result.Done) { FinishCapture(); return true; }
                if (r == KeyCapture.Result.Waiting) { capturing.Text = capture.Shown; return true; }
            }
            return base.ProcessKeyPreview(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TryCaptureKey(keyData)) return true;
            Keys code = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control, shift = (keyData & Keys.Shift) == Keys.Shift;
            if (ctrl && (code == Keys.Tab || code == Keys.PageDown || code == Keys.PageUp))
            {
                bool back = code == Keys.PageUp || (code == Keys.Tab && shift);
                List<int> order = NavOrder(); // v5.1: menu order
                int n = order.Count, at = Math.Max(0, order.IndexOf(current < 0 ? 0 : current));
                ShowPage(order[(at + (back ? n - 1 : 1)) % n]);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (TryCaptureKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }

        // ---------------- top bar (one line) ----------------

        private void BuildTopBar()
        {
            topBar = new TableLayoutPanel();
            topBar.Dock = DockStyle.Fill;
            topBar.AutoSize = true;
            topBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            topBar.Margin = new Padding(0);
            topBar.Padding = P(12, 8, 12, 8);
            topBar.RowCount = 1;
            topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            topBar.ColumnCount = 8;
            for (int i = 0; i < 5; i++) topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // spacer
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label lp = Lbl("配置");
            Tip(lp, "每把枪可以存一套配置（一般一套就够）");
            cbProfile = new ComboBox();
            cbProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            FixUp(delegate { cbProfile.Width = F(190); cbProfile.DropDownWidth = F(280); });
            cbProfile.Anchor = AnchorStyles.Left;
            cbProfile.Margin = P(0, 3, 3, 3);
            WheelScrollsPage(cbProfile); // a stray wheel never switches the profile
            cbProfile.SelectedIndexChanged += delegate
            {
                if (loading || cbProfile.SelectedItem == null) return;
                CommitEdits(); // a typed value belongs to the profile it was typed into
                G.ActiveProfile = (string)cbProfile.SelectedItem;
                RefreshAll();
                app.Changed();
            };

            profileMenu = new ContextMenuStrip();
            AddItem(profileMenu, "新建…", delegate { NewProfile(); });
            AddItem(profileMenu, "复制…", delegate { CopyProfile(); });
            AddItem(profileMenu, "重命名…", delegate { RenameProfile(); });
            AddItem(profileMenu, "删除", delegate { DeleteProfile(); });
            profileMenu.Items.Add(new ToolStripSeparator());
            AddItem(profileMenu, "导入…", delegate { ImportProfiles(); });
            AddItem(profileMenu, "导入旧版设置…", delegate { ImportLegacy(); });
            AddItem(profileMenu, "导出当前配置…", delegate { ExportProfile(); });
            StyleMenu(profileMenu);
            Button manage = null;
            manage = Btn("管理 ▾", delegate { profileMenu.Show(manage, new Point(0, manage.Height)); });
            Tip(manage, "新建 / 复制 / 重命名 / 删除 / 导入 / 导入旧版设置 / 导出");

            Button save = Btn("存为我的默认", delegate { SaveAsDefault(); });
            Tip(save, "把当前配置现在的数值存下来，以后点「恢复 ▾」就能回到这里");
            Button restore = null;
            restore = Btn("恢复 ▾", delegate { ShowRestoreMenu(restore); });
            Tip(restore, "恢复到你存的默认，或恢复到 Leo 推荐的数值");

            lblSave = new Label();
            lblSave.AutoSize = false;          // fills the spacer column: a long message is cut with … (full text in the tooltip)
            lblSave.Dock = DockStyle.Fill;
            lblSave.TextAlign = ContentAlignment.MiddleRight;
            lblSave.AutoEllipsis = true;
            lblSave.Margin = P(12, 3, 3, 3);
            lblSave.ForeColor = SystemColors.GrayText;

            chkAdvanced = new CheckBox();
            chkAdvanced.Text = "高级设置";
            chkAdvanced.AutoSize = true;
            chkAdvanced.Anchor = AnchorStyles.Right;
            chkAdvanced.Margin = P(12, 3, 3, 3);
            Tip(chkAdvanced, "打开后每一页会多出更细的选项（曲线、渐变、样式细调等）。一般不用开");
            chkAdvanced.CheckedChanged += delegate
            {
                if (loading) return;
                CommitEdits();
                UiPrefs.SetAdvanced(G, chkAdvanced.Checked);
                ToggleAdvanced(); // v4.3 PERF (W7): current page only; the others on first show (was the whole-window RefreshValues)
                if (UiPrefs.InSettings) app.Changed();
                SetStatus(chkAdvanced.Checked ? "已显示高级设置" : "已隐藏高级设置", false); // v4.3: short enough for 1280x720@150 (values never change; the tooltip says so)
            };

            chkEnabled = new CheckBox();
            chkEnabled.Text = "启用准星";
            chkEnabled.AutoSize = true;
            chkEnabled.Anchor = AnchorStyles.Right;
            chkEnabled.Margin = P(12, 3, 3, 3);
            chkEnabled.CheckedChanged += delegate
            {
                if (loading) return;
                app.SetEnabled(chkEnabled.Checked);
            };

            topBar.Controls.Add(lp, 0, 0);
            topBar.Controls.Add(cbProfile, 1, 0);
            topBar.Controls.Add(manage, 2, 0);
            topBar.Controls.Add(save, 3, 0);
            topBar.Controls.Add(restore, 4, 0);
            topBar.Controls.Add(lblSave, 5, 0);
            topBar.Controls.Add(chkAdvanced, 6, 0);
            topBar.Controls.Add(chkEnabled, 7, 0);
            topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 1: used only by the two-line layout
            root.Controls.Add(topBar, 0, 1);
        }

        /// <summary>Dark drop-down menus to match the window.</summary>
        private void StyleMenu(ContextMenuStrip m)
        {
            try
            {
                m.Font = Font; // readable size (11 pt)
                m.RenderMode = ToolStripRenderMode.System;
                m.BackColor = Theme.ColInput;
                m.ForeColor = Theme.ColText;
                m.ShowImageMargin = false;
            }
            catch (Exception) { }
        }

        private static void AddItem(ContextMenuStrip menu, string text, EventHandler onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += onClick;
            menu.Items.Add(it);
        }

        private void RefreshProfileList()
        {
            cbProfile.Items.Clear();
            foreach (Profile p in G.Profiles) cbProfile.Items.Add(p.Name);
            cbProfile.SelectedItem = Cur.Name;
        }

        public void RefreshEnabled()
        {
            bool was = loading;
            loading = true;
            chkEnabled.Checked = G.Enabled;
            loading = was;
        }

        private void NewProfile()
        {
            string n = NameDialog.Ask(this, "新建配置", "");
            if (n == null) return;
            Profile p = Profile.CreateBlank(G.UniqueName(n, null));
            G.Profiles.Add(p);
            G.ActiveProfile = p.Name;
            RefreshAll();
            app.Changed();
        }

        private void CopyProfile()
        {
            string n = NameDialog.Ask(this, "复制配置", Cur.Name);
            if (n == null) return;
            Profile p = Cur.Clone(); // keeps the saved-default snapshot
            p.Name = G.UniqueName(n, null);
            G.Profiles.Add(p);
            G.ActiveProfile = p.Name;
            RefreshAll();
            app.Changed();
        }

        private void RenameProfile()
        {
            Profile cur = Cur;
            string n = NameDialog.Ask(this, "重命名配置", cur.Name);
            if (n == null) return;
            cur.Name = G.UniqueName(n, cur);
            G.ActiveProfile = cur.Name;
            RefreshAll();
            app.Changed();
        }

        private void DeleteProfile()
        {
            if (G.Profiles.Count <= 1)
            {
                Say("至少要保留一个配置。");
                return;
            }
            Profile cur = Cur;
            if (!Ask("确定删除配置「" + cur.Name + "」？")) return;
            int idx = G.Profiles.IndexOf(cur);
            G.Profiles.Remove(cur);
            G.ActiveProfile = G.Profiles[Math.Max(0, Math.Min(idx, G.Profiles.Count - 1))].Name;
            RefreshAll();
            app.Changed();
        }

        private static string SafeFileName(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "配置" : r;
        }

        private void ExportProfile()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "导出配置";
                d.Filter = "准星配置 (*.json)|*.json";
                d.FileName = SafeFileName(Cur.Name) + ".json";
                d.OverwritePrompt = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, SettingsIO.ExportProfile(Cur), new UTF8Encoding(false));
                    SetStatus("已导出：" + Path.GetFileName(d.FileName), false);
                }
                catch (Exception ex)
                {
                    Say("导出失败：" + ex.Message);
                }
            }
        }

        private void ImportProfiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "导入配置（单个配置文件或整个 settings.json）";
                d.Filter = "准星配置 (*.json)|*.json|所有文件 (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string text, err;
                List<Profile> list;
                // v4.1 (SETTINGS41F, BRUTE40 P2-11): the file's real encoding (UTF-8 / BOMs / GBK / UTF-16), not always UTF-8
                try { text = SettingsIO.ReadSettingsText(d.FileName); }
                catch (Exception ex) { Say("读取失败：" + ex.Message); return; }
                if (!SettingsIO.TryParseImport(text, out list, out err))
                {
                    Say("这个文件不能导入：" + err);
                    return;
                }
                G.AddImported(list);
                RefreshAll();
                app.Changed();
                SetStatus("已导入 " + list.Count + " 个配置", false);
            }
        }

        /// <summary>First start (no settings.json before): 「用过旧版吗？」 welcome; 「选择旧的 settings.json」 opens the import.</summary>
        public void OfferLegacyImport()
        {
            if (IsDisposed) return;
            if (!Visible) app.ShowSettings();
            // GlobalSettings.MergeLegacy keeps this PC's 游戏分辨率 / 画面比例 / FOV and 调参基准 (v5.0.2, UI M4: no mouse setting since v4.3).
            if (ThemedDialog.Show(this, "欢迎使用" + Program.ChineseName + " " + Program.Version + "！\n\n用过旧版吗？可以导入你以前调好的设置：所有配置、按键和常规设置。"
                    + "游戏分辨率、画面比例、FOV 和调参基准保持现在的，「Leo默认」也会保留。",
                    "选择旧的 settings.json", "跳过"))
                ImportLegacy();
            if (G.WizardDone) OfferFirstSetup(); // v5.1: the 首次设置向导 (next) asks the same 3 values, so no second dialog
        }

        /// <summary>v5.0: first-run guidance (Leo 18 Nov 2026), right after the welcome/import dialog.
        /// v5.0.2 (UI L10): the page is called by its navigation name 「分辨率 · 常规」.</summary>
        public const string FirstSetupText = "先在「分辨率 · 常规」页设好和游戏里一样的 3 项：游戏分辨率、画面比例、FOV（建议 75）。然后进游戏实测，觉得有偏差再细调各项偏移。";
        public const string FirstSetupButton = "去「分辨率 · 常规」页";

        /// <summary>v5.0: the button jumps to the 分辨率 · 常规 page.</summary>
        private void OfferFirstSetup()
        {
            if (IsDisposed) return;
            if (ThemedDialog.Show(this, FirstSetupText, FirstSetupButton, "稍后"))
                JumpToPage("常规");
        }

        /// <summary>
        /// 「导入旧版设置…」: pick an old settings.json (or the old app folder); every old profile, the keys and the global
        /// settings come over (SettingsIO.ImportLegacy: tolerant load + migration, current file backed up first).
        /// </summary>
        private void ImportLegacy()
        {
            CommitEdits();
            CancelCapture();
            string picked = null;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择旧版的 settings.json（在旧版的程序文件夹里）";
                d.Filter = "旧版设置 settings.json|settings.json;*.json|所有文件 (*.*)|*.*";
                d.CheckFileExists = true;
                if (d.ShowDialog(this) == DialogResult.OK) picked = d.FileName;
            }
            if (picked == null)
            {
                // the user may want to pick the whole old folder instead
                using (FolderBrowserDialog f = new FolderBrowserDialog())
                {
                    f.Description = "或者选旧版的整个文件夹（里面有 settings.json）";
                    f.ShowNewFolderButton = false;
                    if (!Ask("没选文件。要改成选旧版的整个文件夹吗？")) return;
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    picked = f.SelectedPath;
                }
            }
            int n; string err;
            GlobalSettings merged = SettingsIO.ImportLegacy(G, picked, app.SettingsPath, out n, out err);
            if (merged == null) { Say(err ?? "导入失败。"); return; }
            string[] keyNotes = merged.KeyNotes ?? new string[0]; // key fixes of the old file + merge (runtime only, read before CopyFrom)
            G.CopyFrom(merged); // the same object stays (dashboard, 外观 and the overlay keep their reference)
            RefreshAll();
            PaintHandRows();
            app.UpdateTray();
            app.Changed();
            app.SaveNow();
            SetStatus("已导入 " + n + " 套配置", false);
            Say("已导入 " + n + " 套配置，现在用的是「" + G.ActiveProfile + "」。\n原来的设置已备份在设置文件夹（settings.before-import-….json）。"
                + (keyNotes.Length > 0 ? "\n" + SettingsIO.KeyNotesText(keyNotes) : ""));
        }

        private void SaveAsDefault()
        {
            Profile cur = Cur;
            if (!Ask("把配置“" + cur.Name + "”现在的全部数值存成你的默认？以后点「恢复 ▾」就能回到这里（会覆盖上一次存的）。")) return;
            cur.SaveAsDefault(DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            app.Publish();
            app.SaveNow(); // at once, not debounced
            SetStatus("已存为我的默认：" + cur.Name + "（" + cur.SavedDefaultAt + "）", false);
        }

        private void ShowRestoreMenu(Button anchor)
        {
            Profile cur = Cur;
            if (restoreMenu == null)
            {
                restoreMenu = new ContextMenuStrip();
                restoreSaved = new ToolStripMenuItem();
                restoreSaved.Click += delegate { RestoreSaved(); };
                ToolStripMenuItem builtIn = new ToolStripMenuItem("恢复到 Leo 推荐");
                builtIn.Click += delegate { RestoreBuiltIn(); };
                restoreMenu.Items.Add(restoreSaved);
                restoreMenu.Items.Add(builtIn);
                StyleMenu(restoreMenu);
            }
            bool has = cur.SavedDefault != null;
            restoreSaved.Text = has ? "恢复到我存的（" + cur.SavedDefaultAt + "）" : "恢复到我存的（还没存过）";
            restoreSaved.Enabled = has;
            restoreMenu.Show(anchor, new Point(0, anchor.Height));
        }

        private void RestoreSaved()
        {
            Profile cur = Cur;
            if (cur.SavedDefault == null) return;
            if (!Ask("把配置“" + cur.Name + "”的数值恢复成你存的默认（" + cur.SavedDefaultAt + "）？名字、按键和全局设置不变。")) return;
            G.RestoreSaved(cur);
            RefreshAll();
            app.Changed();
            SetStatus("已恢复到你存的：" + cur.Name, false);
        }

        /// <summary>Values = Profile.CreateLeoDefault; the name, the 「存为我的默认」 snapshot and 外观 (Style / Boost / 换手颜色) are kept, globals untouched.</summary>
        private void RestoreBuiltIn()
        {
            Profile cur = Cur;
            if (!Ask("把配置“" + cur.Name + "”的数值恢复成 Leo 推荐的数值？名字、按键、外观和全局设置不变。")) return;
            int idx = G.Profiles.IndexOf(cur);
            Profile n = Profile.CreateLeoDefault(cur.Name).CloneValues();
            n.Name = cur.Name;
            n.SavedDefault = cur.SavedDefault == null ? null : cur.SavedDefault.CloneValues();
            n.SavedDefaultAt = cur.SavedDefaultAt;
            // 外观 is the player's own choice: every 外观 value (look, 强化准星, 换手颜色, Style) stays as it is
            // (v5.0.2, W18: v5.0.1 copied only Style / Boost / 换手颜色, so the v3 look fields were replaced by Leo's)
            n.CopyAppearanceFrom(cur);
            n.Validate();
            if (idx >= 0) G.Profiles[idx] = n;
            G.ActiveProfile = n.Name;
            RefreshAll();
            app.Changed();
            SetStatus("已恢复到 Leo 推荐：" + cur.Name, false);
        }

        // ---------------- pages ----------------

        /// <summary>首页: the live dashboard (DashboardPanel, another worker's control) on its own MotionEngine.</summary>
        private Control BuildDashboard()
        {
            // v5.1 (W26): 首页 = status line + 必读 / 常见问题 / 设置向导 buttons + the dashboard (DashboardHome wraps DashboardPanel)
            try { dashboard = new DashboardHome(dashEngine, G, delegate(string p) { JumpToPage(p); }, delegate { RunSetupWizard(); }); }
            catch (Exception ex) { Program.Log("UI: DashboardPanel: " + ex); dashboard = null; }
            Control main = dashboard ?? Missing("首页面板");
            // v4.1 UI41-review #3: 「常调」 row under the dashboard (the dashboard itself is DASH41F's control)
            Panel host = new Panel();
            host.Margin = new Padding(0);
            host.Padding = new Padding(0);
            main.Dock = DockStyle.Fill;
            tuneStrip = BuildTuneStrip();
            tuneStrip.Dock = DockStyle.Bottom;
            host.Controls.Add(main);
            host.Controls.Add(tuneStrip);
            main.BringToFront(); // docked last: fills what the strip leaves
            tuneHost = host;
            host.Resize += delegate { FitTuneStrip(); };
            FixUp(FitTuneStrip);
            return host;
        }

        private Control tuneHost;

        /// <summary>v5.1: 常见问题 (W26's FaqPage, an AutoSize table that wraps to its parent's width).</summary>
        private Control BuildFaq()
        {
            try { return new FaqPage(G, Font, delegate(string p) { JumpToPage(p); }); }
            catch (Exception ex) { Program.Log("UI: FaqPage: " + ex); return Missing("「常见问题」页"); }
        }

        /// <summary>v5.1 (W26): the 首次设置向导 again (首页 button). Modal on this window.</summary>
        public void RunSetupWizard()
        {
            CommitEdits();
            CancelCapture();
            try { SetupWizard.Run(this, G, app.Changed, OpenTutorial); }
            catch (Exception ex) { Program.Log("UI: wizard: " + ex); }
            if (IsDisposed) return;
            RefreshValues();
            app.UpdateTray();
        }

        /// <summary>v5.1 (W26): the wizard's last page offers the tutorial.</summary>
        public void OpenTutorialFromWizard() { OpenTutorial(); }

        /// <summary>
        /// UIALIGN41 #5 (Leo 17 Nov, aligned UI): the 「常调」 strip spans exactly the dashboard's cards -- 「常调」 starts at the
        /// chart card's left edge and the last 「去调 →」 ends at the right-most card's right edge (DashLayout, DASH41F's own
        /// layout rule, asked for the same width and text size). When the items fit on one line the room left over is
        /// spread evenly between them; otherwise they wrap inside the same two edges.
        /// </summary>
        private void FitTuneStrip()
        {
            FlowLayoutPanel f = tuneStrip as FlowLayoutPanel;
            if (f == null || f.IsDisposed || tuneHost == null || f.Controls.Count < 2) return;
            int w = tuneHost.ClientSize.Width;
            if (w <= 0) return;
            int cardL, cardR;
            try
            {
                DashLayout lay = new DashLayout();
                lay.Arrange(w, Math.Max(1, tuneHost.ClientSize.Height - f.Height), k, 1.0, null); // v4.3.1: fixed text size
                cardL = lay.Chart.X;
                cardR = Math.Max(Math.Max(lay.Chart.Right, lay.Data.Right), Math.Max(lay.Term.Right, lay.CapsShown ? lay.Caps.Right : 0));
            }
            catch (Exception) { cardL = F(12); cardR = w - F(12); }
            if (cardR - cardL < S(100)) { cardL = 0; cardR = w; }
            Control head = f.Controls[0];
            Control last = f.Controls[f.Controls.Count - 1];
            TableLayoutPanel lastLine = last as TableLayoutPanel;
            Control lastBtn = lastLine != null && lastLine.Controls.Count > 0 ? lastLine.Controls[lastLine.Controls.Count - 1] : null;
            int trail = lastBtn != null ? lastBtn.Margin.Right : 0; // the visible end is the last button, not its margin
            int natural = 0; // the items' own left margin is 0 (AddTune); the spread below is what this sets
            foreach (Control c in f.Controls)
                if (c.Visible) natural += c.GetPreferredSize(Size.Empty).Width + c.Margin.Right;
            natural -= trail;
            int avail = cardR - cardL, gaps = f.Controls.Count - 1;
            bool oneLine = natural <= avail;
            int extra = oneLine && gaps > 0 ? (avail - natural) / gaps : 0, rest = oneLine && gaps > 0 ? (avail - natural) - extra * gaps : 0;
            f.SuspendLayout();
            try
            {
                for (int i = 1; i < f.Controls.Count; i++)
                {
                    Control c = f.Controls[i];
                    int l = extra + (i == f.Controls.Count - 1 ? rest : 0);
                    if (c.Margin.Left != l) c.Margin = new Padding(l, c.Margin.Top, c.Margin.Right, c.Margin.Bottom);
                }
                // one line: the content is exactly (cardR + trail) - (cardL - head margin) wide; 1 px slack against rounding
                Padding p = new Padding(Math.Max(0, cardL - head.Margin.Left), f.Padding.Top, Math.Max(0, w - cardR - trail - 1), f.Padding.Bottom);
                if (f.Padding != p) f.Padding = p;
            }
            finally { f.ResumeLayout(true); }
        }

        private Control tuneStrip;

        /// <summary>
        /// UI41-review #3: the values people actually tune -- 上抬偏移量, A 左, D 右 (百分比偏移) and the crosshair style -- each with
        /// 「去调 →」 to its page. One wrapping row; hidden in the compact (short window) layout.
        /// </summary>
        private Control BuildTuneStrip()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = true;
            f.FlowDirection = FlowDirection.LeftToRight;
            f.Margin = new Padding(0);
            f.Padding = P(2, 8, 2, 0);
            Label head = Lbl("常调");
            head.ForeColor = Theme.ColMuted;
            f.Controls.Add(head);
            AddTune(f, "recoil", delegate { return "上抬 " + OffsetUnits.Format(Cur.RecoilMaxPx, OffsetAxis.Y, G); });
            AddTune(f, "movement", delegate { return "A 左 " + OffsetUnits.Format(Cur.Dirs[2].OffX, OffsetAxis.X, G); });
            AddTune(f, "movement", delegate { return "D 右 " + OffsetUnits.Format(Cur.Dirs[3].OffX, OffsetAxis.X, G); });
            AddTune(f, "style", delegate
            {
                StyleTile t = null;
                try { t = StyleShapes.TileOf(Cur); } catch (Exception) { }
                return "样式 " + (t != null ? t.Label : "自定义");
            });
            AddReloadTunes(f); // v5.1 R2 (W23, SettingsForm.PageKeys.cs): 换弹 / 快速换弹 times, adjustable here
            refreshers.Add(FitTuneStrip); // new values = new item widths: spread again (after the items' own refresh)
            offsetRefreshers.Add(FitTuneStrip);
            return f;
        }

        /// <summary>One 「常调」 item: value + 「去调 →」 as ONE unit, so a narrow window wraps whole items.</summary>
        private void AddTune(FlowLayoutPanel f, string pageId, Func<string> text)
        {
            Label v = Lbl("");
            v.ForeColor = Theme.ColText;
            v.Margin = P(12, 3, 2, 3);
            Button go = Btn("去调 →", delegate
            {
                int i = Array.IndexOf(UiPages.Ids, pageId);
                if (i >= 0) ShowPage(i);
            });
            go.Margin = P(2, 2, 4, 2);
            Action refresh = delegate { try { v.Text = text(); } catch (Exception) { v.Text = ""; } };
            refreshers.Add(refresh);
            offsetRefreshers.Add(refresh);
            TableLayoutPanel item = Line(v, go);
            item.Margin = new Padding(0);
            f.Controls.Add(item);
        }

        /// <summary>侧身方式 (按住 / 点一下切换); shown on 按键 and 侧身 -- both stay in sync through RefreshValues.</summary>
        private ComboBox LeanModeChoice()
        {
            return Choice(new string[] { "按住", "点一下切换" }, new string[] { "hold", "toggle" },
                delegate { return G.LeanMode; }, delegate(string v) { G.LeanMode = v; RefreshValues(); });
        }

        /// <summary>「冲刺取消侧身」: only visible in 点一下切换 lean mode (SPEC v4).</summary>
        private CheckBox LeanSprintCheck()
        {
            CheckBox c = Check("冲刺（Shift+W）取消侧身", delegate { return G.LeanCancelSprint; },
                delegate(bool v) { G.LeanCancelSprint = v; RefreshValues(); });
            Tip(c, "侧身方式为「点一下切换」时：按住前进再按 Shift 冲刺，侧身自动回正。只按 Shift（屏息）不算");
            refreshers.Add(delegate { c.Visible = G.LeanMode == "toggle"; });
            return c;
        }

        // ---------------- refresh / sizing / closing ----------------

        /// <summary>Re-reads every field from the model, without rebuilding the profile list.</summary>
        public void RefreshValues()
        {
            bool was = loading;
            loading = true;
            try
            {
                foreach (Action r in refreshers) r();
                StylePanel sp = stylePanel as StylePanel;
                if (sp != null) sp.RefreshValues();
                RefreshAdvanced();
            }
            finally { loading = was; }
            RefreshTutorialButton();
        }

        public void RefreshAll()
        {
            bool was = loading;
            loading = true;
            try
            {
                RefreshProfileList();
                foreach (Action r in refreshers) r();
                StylePanel sp = stylePanel as StylePanel;
                if (sp != null) sp.RefreshValues();
                chkEnabled.Checked = G.Enabled;
                if (chkAdvanced != null) chkAdvanced.Checked = Advanced;
                TopMost = G.SettingsTopMost;
                RefreshAdvanced();
            }
            finally { loading = was; }
            PaintHandRows();
            RefreshTutorialButton();
        }

        // ---------------- v5.1 (W25, decision 8): pages built in the background, before the first open ----------------

        /// <summary>App.ChangeCount when the window was last hidden (or built): App.ShowSettings refreshes the values only when
        /// something changed while the window was hidden (hotkeys, tray, filter).</summary>
        internal int SeenVersion = -1;
        private PrebuildQueue prebuild;
        private bool tutorialWarmed, sized;
        private int warmNext;
        /// <summary>App.ChangeCount when 换手's 自动 rows were last refreshed (-1 = never).</summary>
        private int handSeen = -1;
        /// <summary>Probe / tests: ms each page took to build in the background (page id -> ms).</summary>
        internal readonly Dictionary<string, long> PrebuildMs = new Dictionary<string, long>();

        /// <summary>
        /// Builds ONE more page body (menu order) and creates its window handles, so its first show only lays out; returns
        /// false when there is nothing left. App calls it once per idle slot of the UI thread after start (never while the
        /// window is being dragged). Last step: outside 性能模式 one tutorial window is made and disposed at once (warms the
        /// code and fonts it needs; nothing of it is kept, so it cannot show old values).
        /// </summary>
        public bool PrebuildStep()
        {
            if (IsDisposed || frozen) return !IsDisposed;
            if (prebuild == null) prebuild = new PrebuildQueue(NavOrder().ToArray());
            int i = prebuild.Next(delegate(int x) { return pages[x].Built; });
            if (i < 0 && !Visible && warmNext < pages.Count)
            {
                // phase 2 (hidden): the window gets its real size, then every page is shown once at that size, so its
                // layout (wrap widths, card widths, scroll range) is done now and not at the user's first click
                System.Diagnostics.Stopwatch ws = System.Diagnostics.Stopwatch.StartNew();
                if (!sized) SizeToScreen();
                int w = NavOrder()[warmNext++];
                try { ShowPage(w); if (warmNext >= pages.Count) ShowPage(0); }
                catch (Exception ex) { Program.Log("UI: prebuild layout " + pages[w].Id + ": " + ex.Message); }
                PrebuildMs["layout:" + pages[w].Id] = ws.ElapsedMilliseconds;
                return true;
            }
            if (i < 0)
            {
                if (!tutorialWarmed)
                {
                    tutorialWarmed = true;
                    if (!UiPerf.On && tutorial == null)
                    {
                        System.Diagnostics.Stopwatch tw = System.Diagnostics.Stopwatch.StartNew();
                        try { using (TutorialForm t = new TutorialForm(G, delegate(string page) { })) { IntPtr th = t.Handle; GC.KeepAlive(th); } }
                        catch (Exception ex) { Program.Log("UI: tutorial warm-up: " + ex.Message); }
                        Program.Log("启动耗时 教程窗口后台预热: " + tw.ElapsedMilliseconds + " ms");
                        return true;
                    }
                }
                return false;
            }
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            Page pg = pages[i];
            EnsureBuilt(i);
            try { CreateHandles(pg.Host); } catch (Exception ex) { Program.Log("UI: prebuild handles " + pg.Id + ": " + ex.Message); }
            PrebuildMs[pg.Id] = sw.ElapsedMilliseconds;
            Program.Log("启动耗时 设置页「" + pg.Title + "」后台预先创建: " + sw.ElapsedMilliseconds + " ms");
            return true;
        }

        /// <summary>Window handles for a hidden page now (WinForms makes them at the first show otherwise).</summary>
        private static void CreateHandles(Control c)
        {
            IntPtr h = c.Handle;
            GC.KeepAlive(h);
            foreach (Control k in c.Controls) CreateHandles(k);
        }

        /// <summary>Probe / tests: true once every page body exists.</summary>
        internal bool AllBuilt { get { foreach (Page pg in pages) if (!pg.Built) return false; return true; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!sized) SizeToScreen(); // v5.1: a prebuilt window was already sized while hidden
        }

        /// <summary>
        /// Window size: the top bar on one line, room for the navigation, the work area's height, capped to the work area.
        /// v5.1 (W25): pages are no longer measured. Since v5.0.2 an unbuilt page counted as taller than any screen (so the
        /// height was always the work area's) and the width always came from the 1100 px floor (W17 measured), so the result
        /// is unchanged; measuring 12 prebuilt pages cost about 2.6 s on Mono at 200 %.
        /// </summary>
        private void SizeToScreen()
        {
            sized = true;
            int barsW = topBar.GetPreferredSize(Size.Empty).Width;
            int width = Math.Max(barsW, S(1100));
            int fixedH = header.Height + topBar.GetPreferredSize(new Size(width, 0)).Height;
            int navH = navItems.Count * S(36) + (navTutorialRow >= 0 ? (int)navBar.RowStyles[navTutorialRow].Height : S(56)) + navBar.Padding.Vertical + S(32);
            int height = Math.Max(fixedH + navH, S(740));
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            Size frame = Size - ClientSize;
            int maxW = wa.Width - frame.Width, maxH = wa.Height - frame.Height;
            height = Math.Max(height, maxH);
            ClientSize = new Size(Math.Min(width, maxW), Math.Min(height, maxH));
            // Never narrower than the top bar (no clipped buttons) and never shorter than the navigation; pages scroll.
            // SPEC v4: minimum window 960 x 640 logical (capped to the screen); below that everything scrolls, nothing is cut.
            Size minClient = new Size(Math.Min(Math.Max(barsW + S(8), S(960)), maxW), Math.Min(S(640), maxH));
            MinimumSize = minClient + frame;
            CenterToScreen();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { CommitEdits(); CancelCapture(); saveFlash.Stop(); }
            SyncPause();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                // v5.1 (W25, Leo decision 8): closing only HIDES the window (v4.1 - v5.0.2 disposed it), so the next open is
                // instant; while hidden every timer / animation of the window is paused (SyncPause). The crosshair keeps
                // running and the tray shows the same window again.
                e.Cancel = true;
                CommitEdits();
                CancelCapture();
                app.SaveNow();
                SeenVersion = app.ChangeCount;
                Hide();
                app.OnFormHidden();
                return;
            }
            if (!AllowClose)
            {
                // Windows shutdown, task manager, etc.: clean up without re-closing this form.
                AllowClose = true;
                app.ExitApp(false);
            }
            base.OnFormClosing(e);
        }

        private Action saveStatusHandler;

        protected override void Dispose(bool disposing)
        {
            Font uiFont = null;
            if (disposing)
            {
                if (saveStatusHandler != null) { app.SaveStatusChanged -= saveStatusHandler; saveStatusHandler = null; }
                if (foregroundHandler != null) { ForegroundState.Changed -= foregroundHandler; foregroundHandler = null; }
                uiFont = Font; // created here (Theme.Ui): ours to dispose
                saveFlash.Dispose();
                tips.Dispose();
                if (restoreMenu != null) restoreMenu.Dispose();
                if (profileMenu != null) profileMenu.Dispose();
                if (windowMenu != null) windowMenu.Dispose();
                if (tutorial != null && !tutorial.IsDisposed) tutorial.Dispose();
                foreach (Page pg in pages) if (pg.Heading.Parent == null) pg.Heading.Dispose(); // the dashboard's unused heading
            }
            base.Dispose(disposing);
            if (disposing) // after the controls that use them
            {
                if (monoFont != null) { try { monoFont.Dispose(); } catch (Exception) { } }
                if (guideBold != null) guideBold.Dispose();
                if (uiFont != null) { try { uiFont.Dispose(); } catch (Exception) { } }
            }
        }
    }

    /// <summary>
    /// v4.0 question / message box in the settings window's look: the owner's font (11 pt), dark theme,
    /// text wrapped at about 26 characters, 「是」 / 「否」 or one 「知道了」. Enter = 是 / 知道了, Esc = 否 / close.
    /// </summary>
    internal sealed class ThemedDialog : Form
    {
        private ThemedDialog(Form owner, string message, bool question) : this(owner, message, question, null, null) { }

        private ThemedDialog(Form owner, string message, bool question, string yesText, string noText)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font font = owner != null ? owner.Font : Theme.Ui(11f);
            Font = font;
            Text = owner != null ? owner.Text : Program.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            TopMost = owner != null && owner.TopMost;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int u = Math.Max(8, font.Height); // one text line: every spacing follows the text size

            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Padding = new Padding(u, u, u, u * 2 / 3);
            Label l = new Label();
            l.Text = message ?? "";
            l.AutoSize = true;
            l.MaximumSize = new Size(u * 26, 0);
            l.Margin = new Padding(0, 0, 0, u);
            t.Controls.Add(l);
            FlowLayoutPanel b = new FlowLayoutPanel();
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.FlowDirection = FlowDirection.RightToLeft;
            b.WrapContents = false;
            b.Anchor = AnchorStyles.Right;
            b.Margin = new Padding(0);
            Button ok = new Button();
            ok.Text = yesText ?? (question ? "是" : "知道了");
            ok.DialogResult = DialogResult.Yes;
            ok.AutoSize = true;
            ok.MinimumSize = new Size(u * 5, u + u / 2);
            ok.Tag = "theme:accent";
            ok.Margin = new Padding(3, 3, 0, 3); // UIALIGN41 #8: the right-most button ends at the text inset
            ok.Padding = new Padding(u / 2, 0, u / 2, 0); // v5.0.2 (UI L11): the text never touches the frame
            b.Controls.Add(ok);
            Button no = null;
            if (question)
            {
                no = new Button();
                no.Text = noText ?? "否";
                no.DialogResult = DialogResult.No;
                no.AutoSize = true;
                no.MinimumSize = ok.MinimumSize;
                no.Padding = ok.Padding;
                b.Controls.Add(no);
            }
            t.Controls.Add(b);
            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = no ?? ok;
            Theme.Apply(this);
        }

        /// <summary>Question with own button texts (e.g. 「选择旧的 settings.json」 / 「跳过」); true = the first button.</summary>
        public static bool Show(Form owner, string message, string yesText, string noText)
        {
            try
            {
                using (ThemedDialog d = new ThemedDialog(owner, message, true, yesText, noText))
                    return d.ShowDialog(owner) == DialogResult.Yes;
            }
            catch (Exception ex) { Program.Log("UI: dialog: " + ex); return false; }
        }

        /// <summary>question: true = 是 / 否 (returns true for 是); false = one 「知道了」 button (returns true).</summary>
        public static bool Show(Form owner, string message, bool question)
        {
            try
            {
                using (ThemedDialog d = new ThemedDialog(owner, message, question))
                    return d.ShowDialog(owner) == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                Program.Log("UI: dialog: " + ex);
                return MessageBox.Show(owner, message, owner != null ? owner.Text : Program.ProductName,
                    question ? MessageBoxButtons.YesNo : MessageBoxButtons.OK) == (question ? DialogResult.Yes : DialogResult.OK);
            }
        }
    }

    /// <summary>Profile name dialog (新建 / 复制 / 重命名), built like ThemedDialog: owner font, spacing from the text
    /// height, framed text box, 确定 (accent) / 取消 on the right.</summary>
    internal sealed class NameDialog : Form
    {
        private readonly TextBox box;
        private readonly Panel frame;

        private NameDialog(string title, string initial, Font font, bool topMost) : this(title, initial, font, topMost, null) { }

        /// <summary>v4.3: hintText null = the profile hint (偏移); the 滤镜 page passes its own.</summary>
        private NameDialog(string title, string initial, Font font, bool topMost, string hintText)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = font;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TopMost = topMost;
            int u = Math.Max(8, font.Height);

            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1; t.AutoSize = true; t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Padding = new Padding(u, u, u, u * 2 / 3);
            Label l = new Label(); l.Text = "名字："; l.AutoSize = true; l.Margin = new Padding(0, 0, 0, u / 4);
            t.Controls.Add(l);
            Label hint = new Label(); hint.Text = hintText ?? "给这套配置起个名字，例如 M4 / AK"; hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText; // Theme maps it to the readable muted colour
            hint.Margin = new Padding(0, 0, 0, u / 2);
            t.Controls.Add(hint);
            box = new TextBox();
            box.Width = u * 20;
            box.BorderStyle = BorderStyle.None;
            box.Margin = new Padding(0);
            box.Text = initial ?? "";
            frame = new Panel(); // 1 px border frame: the box stays visible on the dark background
            frame.Padding = new Padding(1);
            frame.Margin = new Padding(0, 0, 0, u);
            frame.BackColor = Theme.ColBorder;
            frame.Tag = "theme:keep-frame";
            frame.Size = new Size(box.Width + 2, box.PreferredHeight + 2);
            box.Dock = DockStyle.Fill;
            frame.Controls.Add(box);
            t.Controls.Add(frame);
            FlowLayoutPanel b = new FlowLayoutPanel();
            b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.FlowDirection = FlowDirection.RightToLeft; b.WrapContents = false;
            b.Anchor = AnchorStyles.Right; b.Margin = new Padding(0);
            Button ok = new Button(); ok.Text = "确定"; ok.DialogResult = DialogResult.OK; ok.AutoSize = true;
            ok.MinimumSize = new Size(u * 5, u + u / 2); ok.Tag = "theme:accent";
            ok.Margin = new Padding(3, 3, 0, 3); // UIALIGN41 #8: the right-most button ends at the text inset
            Button cancel = new Button(); cancel.Text = "取消"; cancel.DialogResult = DialogResult.Cancel; cancel.AutoSize = true;
            cancel.MinimumSize = ok.MinimumSize;
            b.Controls.Add(ok); b.Controls.Add(cancel);
            t.Controls.Add(b);
            Controls.Add(t);
            AcceptButton = ok; CancelButton = cancel;
            Theme.Apply(this);
            box.BorderStyle = BorderStyle.None; // the frame panel draws the border
            box.BackColor = Theme.ColInput; box.ForeColor = Theme.ColText;
            // brute-ui F6: size the frame AFTER the theme and the border change, from the box's real text height, so the
            // bottom of the typed name is never cut (it was sized from the pre-theme PreferredHeight)
            int textH = Math.Max(box.PreferredHeight, Math.Max(box.Height, TextRenderer.MeasureText("Mg名字", font).Height + 2));
            frame.Size = new Size(u * 20 + 2, textH + 2);
        }

        /// <summary>Returns the trimmed name, or null if cancelled/empty. Uniqueness is applied by the caller.</summary>
        public static string Ask(Form owner, string title, string initial) { return Ask(owner, title, initial, null); }

        /// <summary>v4.3: same, with the hint line under 「名字：」 (null = the profile hint).</summary>
        public static string Ask(Form owner, string title, string initial, string hint)
        {
            using (NameDialog d = new NameDialog(title, initial, owner.Font, owner.TopMost, hint))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                string n = (d.box.Text ?? "").Trim();
                return n.Length == 0 ? null : n;
            }
        }
    }
#endif
}
