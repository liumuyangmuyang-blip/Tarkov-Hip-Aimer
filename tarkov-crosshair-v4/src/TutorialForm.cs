// TutorialForm.cs -- 首次设置教程 window (v4.0). Hosts TutorialFrame on one double-buffered canvas at ~60 fps, reads the
// keys like the overlay (GetAsyncKeyState via MotionEngine.ReadInput + LiveInputFilter: 组合键, last pressed wins),
// places real input controls for the step's fields, and saves settings.json (SettingsIO) after edits and on 完成.
//
// Use (UI worker):  using (TutorialForm t = new TutorialForm(app.Settings, delegate(string page) { ShowPage(page); }))
//                   { t.ValuesChanged += delegate { app.Changed(); }; t.ShowDialog(this); }  then RefreshValues().
// Page keys passed to jumpToPage: TutorialPages (首页 按键 常规 后坐力 移动 侧身 换手 跳跃 外观 反馈).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    public sealed class TutorialForm : Form
    {
        /// <summary>Raised on the UI thread after the tutorial changed a profile value or set TutorialDone (publish + refresh).</summary>
        public event Action ValuesChanged;

        private readonly GlobalSettings g;
        private readonly Action<string> jumpToPage;
        private readonly TutorialContext ctx;
        private readonly FontFamily uiFamily, monoFamily;
        private readonly TutorialGfx gx;
        private readonly TutorialFrame frame;
        private readonly Canvas canvas;
        private readonly Timer timer;
        private readonly Timer saveTimer;
        private bool layoutReady;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastTicks;
        private readonly LiveInputFilter filter = new LiveInputFilter();
        private readonly Func<int, bool> isDown = Native.IsDown;
        private readonly float k;
        private Font ctlFont;
        private bool mouseFire, dirty, finished, closing;
        private TutorialHit downHit = TutorialHit.None;
        private int downDot = -1, downChip = -1;
        private readonly List<Control> fieldCtl = new List<Control>();
        private TutorialField[] fieldsShown;
        private bool updatingFields;
        private int frameSkip;
        private double paintMs;
        // v4.1 window drag + memory (Leo, 17 Nov 2026): while the window is moved / resized nothing animates, computes or
        // relayouts; the last frame is shown frozen from a bitmap (no stutter, no ghosting). One relayout on release, then resume.
        private bool sizing, sizedDuringDrag;
        private Bitmap frozen;

        /// <summary>Where the tutorial was left in this app session (reopening resumes there).</summary>
        private static int lastIndex;

        public TutorialForm(GlobalSettings g, Action<string> jumpToPage)
        {
            if (g == null) throw new ArgumentNullException("g");
            this.g = g;
            this.jumpToPage = jumpToPage;
            k = SystemScale();

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = TutorialTitle.Window(g.TutorialDone); // 「首次设置教程」 the first time, 「设置教程」 when replayed
            ShowInTaskbar = false;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = true;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; } catch (Exception) { }
            Size want = new Size((int)(1180 * k), (int)(760 * k));
            Size max = new Size((int)(wa.Width * 0.96), (int)(wa.Height * 0.94));
            Size min = new Size(Math.Min((int)(960 * k), max.Width), Math.Min((int)(620 * k), max.Height));
            ClientSize = new Size(Math.Min(want.Width, max.Width), Math.Min(want.Height, max.Height));
            MinimumSize = SizeFromClientSize(min);

            TutorialPalette pal = TutorialPalette.FromTheme(); // same colours as the settings window (and the offscreen renders)
            BackColor = pal.Bg;
            ForeColor = pal.Text;

            uiFamily = PickFamily(new string[] { "Microsoft YaHei UI", "Microsoft YaHei" }, SystemFonts.MessageBoxFont.FontFamily);
            monoFamily = PickFamily(new string[] { "Consolas", "Cascadia Mono", "Courier New" }, FontFamily.GenericMonospace);
            // v4.3.1 (Leo): ONE fixed text size (body 13 pt, numbers 11 pt), whatever the window size; Windows DPI applies
            ctlFont = new Font(uiFamily, TutorialFonts.NumberPt); // numbers 11 pt (SPEC)
            gx = new TutorialGfx(pal, new TutorialFonts(uiFamily, monoFamily, 1f));
            gx.K = k;

            ctx = new TutorialContext(g);
            ctx.K = k;
            ctx.ReducedMotion = AnimationsOff();
            ctx.ChangedHook = delegate { AfterEdit(false); };
            ctx.FinishHook = delegate { Finish(); };
            ctx.CloseAndJumpHook = delegate(string page) { Finish(); CloseAndOpen(page); };

            List<TutorialStep> steps = new List<TutorialStep>();
            steps.AddRange(TutorialStepsA.Create());
            steps.AddRange(TutorialStepsB.Create());
            frame = new TutorialFrame(ctx, gx, steps);
            frame.SetMeasureDpi(96f * k); // text is laid out at the screen DPI it is painted at
            frame.LayoutChanged += PlaceFields;

            canvas = new Canvas();
            canvas.Dock = DockStyle.Fill;
            canvas.BackColor = pal.Bg;
            canvas.Paint += delegate(object s, PaintEventArgs e)
            {
                if (sizing && frozen != null)
                {
                    // moving / resizing: the frozen last frame; area the window grew by = plain background (never old pixels)
                    try
                    {
                        e.Graphics.Clear(BackColor);
                        e.Graphics.DrawImage(frozen, new Rectangle(0, 0, frozen.Width, frozen.Height));
                    }
                    catch (Exception) { }
                    return;
                }
                long t0 = clock.ElapsedTicks;
                try { frame.Draw(e.Graphics); } catch (Exception) { } // never a red-X window (frame.Draw guards its parts too)
                double ms = (clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
                paintMs = paintMs * 0.9 + ms * 0.1; // smoothed paint cost
            };
            canvas.Resize += delegate { OnCanvasResize(); };
            canvas.MouseWheel += delegate(object s, MouseEventArgs e) { if (frame.Wheel(new PointF(e.X, e.Y), e.Delta)) { } };
            canvas.MouseMove += OnCanvasMove;
            canvas.MouseDown += OnCanvasDown;
            canvas.MouseUp += OnCanvasUp;
            canvas.MouseLeave += delegate { frame.Mouse = new PointF(-1, -1); frame.MouseDown = false; };
            Controls.Add(canvas);

            timer = new Timer();
            timer.Interval = 15; // fires every system tick (~64 Hz at the default 15.6 ms resolution)
            timer.Tick += OnTick;
            saveTimer = new Timer();
            saveTimer.Interval = 600;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };

            // select the first page BEFORE any layout (fields need a current step), then lay out at the real size
            int start = g.TutorialDone ? 0 : lastIndex;
            if (start < 0 || start >= steps.Count) start = 0;
            frame.Go(start);
            ResumeLayout(true);
            layoutReady = true;
            frame.SetClient(LayoutSize(), k);
        }

        /// <summary>The canvas size, or the form's client size before the canvas is docked.</summary>
        private Size LayoutSize()
        {
            Size s = canvas.ClientSize;
            if (s.Width <= 0 || s.Height <= 0) s = ClientSize;
            return s;
        }

        private void OnCanvasResize()
        {
            if (!layoutReady) return;
            if (sizing) { sizedDuringDrag = true; canvas.Invalidate(); return; }   // no relayout per mouse move: once on release
            Size sz = LayoutSize();
            if (sz.Width <= 0 || sz.Height <= 0) return;
            frame.SetClient(sz, k);                    // relayout only: the text size is fixed (v4.3.1)
        }

        // ------------------------------------------------------------------ setup helpers
        private static float SystemScale()
        {
            try
            {
                using (Graphics gr = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float s = gr.DpiX / 96f;
                    if (s >= 0.5f && s <= 5f) return s;
                }
            }
            catch (Exception) { }
            return 1f;
        }

        private static FontFamily PickFamily(string[] names, FontFamily fallback)
        {
            foreach (string n in names)
            {
                try { FontFamily f = new FontFamily(n); return f; }
                catch (Exception) { }
            }
            return fallback;
        }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfoBool(int action, int param, ref int value, int winIni);

        /// <summary>v4.1: Windows 设置 → 辅助功能 → 「动画效果」 off (SPI_GETCLIENTAREAANIMATION). Any failure = animations on.</summary>
        private static bool AnimationsOff()
        {
            try
            {
                int on = 1;
                if (SystemParametersInfoBool(0x1042, 0, ref on, 0)) return on == 0;
            }
            catch (Exception) { }
            return false;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); // dark title bar
            }
            catch (Exception) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { if (Owner != null && Owner.Icon != null) Icon = Owner.Icon; } catch (Exception) { }
            lastTicks = clock.ElapsedTicks;
            // v5.0.2 (AUDIT F8, Leo: no timer ticks while the game is in front): the frame timer stops when another
            // program takes the foreground and starts again when one of our windows is in front (events, no polling)
            Theme.TrackForeground(this);
            foregroundHandler = delegate(bool ours) { if (!IsDisposed && !closing) SyncTimer(); };
            ForegroundState.Changed += foregroundHandler;
            SyncTimer();
            canvas.Focus();
        }

        private Action<bool> foregroundHandler;

        /// <summary>v5.0.2: the frame timer runs only while one of our windows is in front and the tutorial is not closing.</summary>
        private void SyncTimer()
        {
            bool run = ForegroundState.Ours && !closing && !IsDisposed;
            if (run == timer.Enabled) return;
            if (run)
            {
                lastTicks = clock.ElapsedTicks; // the stopped time is not one long frame
                timer.Start();
                canvas.Invalidate();
            }
            else timer.Stop();
        }

        /// <summary>Tests / probe: whether the frame timer is running.</summary>
        internal bool TimerRunning { get { return timer.Enabled; } }

        // ------------------------------------------------------------------ move / resize freeze (v4.1)
        protected override void OnResizeBegin(EventArgs e) { base.OnResizeBegin(e); BeginSizing(); }

        protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); EndSizing(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0231) BeginSizing();        // WM_ENTERSIZEMOVE (also raises OnResizeBegin; both calls are fine)
            base.WndProc(ref m);
            if (m.Msg == 0x0232) EndSizing();          // WM_EXITSIZEMOVE
        }

        /// <summary>The move / size loop started: stop every timer-driven update and show the last frame frozen.</summary>
        private void BeginSizing()
        {
            if (sizing || closing || IsDisposed || !layoutReady) return;
            sizing = true;
            sizedDuringDrag = false;
            mouseFire = false;
            CaptureFrozen();
            try { Theme.PauseTree(this, true); } catch (Exception) { }
        }

        /// <summary>The move / size loop ended: one relayout at the final size (fixed text size), then resume.</summary>
        private void EndSizing()
        {
            if (!sizing) return;
            sizing = false;
            DisposeFrozen();
            if (closing || IsDisposed) return;
            if (sizedDuringDrag)
            {
                sizedDuringDrag = false;
                Size sz = LayoutSize();
                if (sz.Width > 0 && sz.Height > 0)
                {
                    frame.SetClient(sz, k);                             // one relayout (fixed text size)
                }
            }
            try { Theme.PauseTree(this, false); } catch (Exception) { }
            lastTicks = clock.ElapsedTicks;                            // no jump in the animations
            canvas.Invalidate();
        }

        /// <summary>Draws the current frame into a bitmap (canvas size, at the screen DPI so the layout is not re-measured).</summary>
        private void CaptureFrozen()
        {
            DisposeFrozen();
            Size s = canvas.ClientSize;
            if (s.Width <= 0 || s.Height <= 0) return;
            Bitmap b = null;
            try
            {
                b = new Bitmap(s.Width, s.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                b.SetResolution(96f * k, 96f * k);
                using (Graphics gr = Graphics.FromImage(b)) frame.Draw(gr);
                frozen = b;
                b = null;
            }
            catch (Exception) { }
            finally { if (b != null) b.Dispose(); }
        }

        private void DisposeFrozen()
        {
            Bitmap b = frozen;
            frozen = null;
            if (b != null) b.Dispose();
        }

        // ------------------------------------------------------------------ frame loop
        private void OnTick(object sender, EventArgs e)
        {
            if (closing || IsDisposed) return;
            long now = clock.ElapsedTicks;
            double dtMs = (now - lastTicks) * 1000.0 / Stopwatch.Frequency;
            lastTicks = now;
            if (sizing) return;                        // being moved / resized: frozen (no input, no animation, no paint)
            if (WindowState == FormWindowState.Minimized) return;
            bool active = Form.ActiveForm == this;
            ReadLive(active);
            frame.Update(dtMs);
            // inactive (e.g. testing in the game on another screen): ~20 fps is plenty and saves CPU; a slow paint (very
            // large window on a slow PC) drops to ~32 fps so the window stays responsive
            frameSkip++;
            if (!active && (frameSkip % 3) != 0) return;
            if (active && paintMs > 11 && (frameSkip % 2) != 0) return;
            canvas.Invalidate();
        }

        private void ReadLive(bool active)
        {
            InputState raw = active ? MotionEngine.ReadInput(g.Keys, isDown) : new InputState();
            InputState live = filter.Next(raw, true, true); // like the overlay: opposite keys, last pressed wins
            bool keyFire = active && g.Keys != null && g.Keys.Fire > 6 && raw.Fire; // 开火 bound to a keyboard key
            live.Fire = false;                                                    // mouse buttons: only clicks on the scene count
            bool fire = (mouseFire && active) || keyFire;
            ctx.Live = live;
            ctx.LiveFire = fire;
            ctx.LiveMask = TKey.Of(live, fire);
        }

        // ------------------------------------------------------------------ mouse
        private PointF P(MouseEventArgs e) { return new PointF(e.X, e.Y); }

        private void OnCanvasMove(object sender, MouseEventArgs e)
        {
            frame.Mouse = P(e);
            Cursor c = frame.Clickable(P(e)) ? Cursors.Hand : Cursors.Default;
            if (canvas.Cursor != c) canvas.Cursor = c;
        }

        private static int Vk(MouseButtons b)
        {
            switch (b)
            {
                case MouseButtons.Left: return 1;
                case MouseButtons.Right: return 2;
                case MouseButtons.Middle: return 4;
                case MouseButtons.XButton1: return 5;
                case MouseButtons.XButton2: return 6;
                default: return 0;
            }
        }

        private void OnCanvasDown(object sender, MouseEventArgs e)
        {
            canvas.Focus(); // commits a number box being typed in
            PointF p = P(e);
            frame.Mouse = p;
            frame.MouseDown = e.Button == MouseButtons.Left;
            downHit = frame.HitTest(p, out downDot);
            downChip = downHit == TutorialHit.Scene ? gx.HitChip(p) : -1;
            bool fireButton = e.Button == MouseButtons.Left || (g.Keys != null && Vk(e.Button) == g.Keys.Fire);
            if (downHit == TutorialHit.Scene && downChip < 0 && fireButton && frame.Current != null && frame.Current.ClickFires) mouseFire = true;
        }

        private void OnCanvasUp(object sender, MouseEventArgs e)
        {
            PointF p = P(e);
            mouseFire = false;
            frame.MouseDown = false;
            if (e.Button != MouseButtons.Left) return;
            int dot;
            TutorialHit up = frame.HitTest(p, out dot);
            if (up != downHit) return;
            switch (up)
            {
                case TutorialHit.Next:
                    if (frame.IsLast) { Finish(); CloseAndOpen(TutorialPages.Recoil); }
                    else frame.Next(false);
                    break;
                case TutorialHit.Skip: frame.Next(true); break;
                case TutorialHit.Prev: frame.Prev(); break;
                case TutorialHit.Exit: Close(); break;
                case TutorialHit.Dot: if (dot == downDot) frame.Go(dot); break;
                case TutorialHit.SettingsLink:
                    if (!string.IsNullOrEmpty(frame.Current.SettingsPage)) CloseAndOpen(frame.Current.SettingsPage);
                    break;
                case TutorialHit.Expander: frame.ToggleExpander(); break;
                case TutorialHit.FieldArrow: if (dot == downDot) FlipField(dot); break;
                case TutorialHit.DevNote: frame.ToggleDevNote(); break;
                case TutorialHit.Text: frame.FinishTyping(); break;
                case TutorialHit.Scene:
                    int chip = gx.HitChip(p);
                    if (chip >= 0 && chip == downChip) { try { frame.Current.OnChip(ctx, chip); } catch (Exception) { } }
                    break;
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            mouseFire = false;
            frame.MouseDown = false;
        }

        // ------------------------------------------------------------------ keys
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool inBox = ActiveControl is NumericUpDown || (ActiveControl != null && ActiveControl.Parent is NumericUpDown);
            if (inBox)
            {
                // the demo keys (W A S D Q E Space ...) must not beep or type into a number box
                if ((e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) || e.KeyCode == Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)
                {
                    // 百分比偏移 boxes hold a positive size: '-' flips the direction arrow instead of typing a minus
                    int fi = FieldIndexOf(ActiveControl is NumericUpDown ? ActiveControl : ActiveControl.Parent);
                    if (fi >= 0 && fieldsShown != null && fi < fieldsShown.Length && fieldsShown[fi].Pct != TutorialField.PctNone)
                    {
                        e.Handled = true; e.SuppressKeyPress = true;
                        FlipField(fi);
                    }
                }
                if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; canvas.Focus(); }
                return;
            }
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }   // v4.1: Esc = 退出教程 (same as the button)
            if (e.KeyCode == Keys.Enter && ActiveControl == canvas)
            {
                e.Handled = true;
                if (frame.IsLast) { Finish(); CloseAndOpen(TutorialPages.Recoil); } else frame.Next(false);
            }
        }

        // ------------------------------------------------------------------ fields
        private int FieldIndexOf(Control c)
        {
            for (int i = 0; i < fieldCtl.Count; i++) if (fieldCtl[i] == c) return i;
            return -1;
        }

        private void PlaceFields()
        {
            if (frame == null || frame.Current == null) return; // layout before the first step
            TutorialField[] f = frame.Current.Fields;
            if (f != fieldsShown) BuildFields(f);
            for (int i = 0; i < fieldCtl.Count; i++)
            {
                RectangleF r = frame.FieldRect(i);
                Control c = fieldCtl[i];
                if (r.IsEmpty) { c.Visible = false; continue; }
                c.Bounds = new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
                // v5.0.2 (UI L8): Windows keeps a number box at its own fixed height, shorter than the painted slot: centre it
                // in the slot so it lines up with the drawn arrow next to it
                NumericUpDown nb = c as NumericUpDown;
                if (nb != null && nb.Height < r.Height - 1)
                    nb.Top = (int)Math.Round(r.Y + (r.Height - nb.Height) / 2f);
                if (!c.Visible) c.Visible = true;
            }
            RefreshFields();
        }

        private void BuildFields(TutorialField[] f)
        {
            canvas.SuspendLayout();
            foreach (Control c in fieldCtl) { canvas.Controls.Remove(c); c.Dispose(); }
            fieldCtl.Clear();
            fieldsShown = f;
            int n = f == null ? 0 : f.Length;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                TutorialField fd = f[i];
                if (fd.Kind == TutorialField.KindCheck)
                {
                    CheckBox cb = new CheckBox();
                    cb.Text = "";
                    cb.AutoSize = false;
                    cb.Size = new Size((int)(22 * k), (int)(22 * k));
                    cb.BackColor = gx.Pal.Panel;
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.FlatAppearance.BorderColor = gx.Pal.Border;
                    cb.CheckedChanged += delegate { OnCheck(idx); };
                    fieldCtl.Add(cb);
                    canvas.Controls.Add(cb);
                }
                else
                {
                    NumericUpDown nud = new NumericUpDown();
                    nud.Font = ctlFont;
                    nud.BorderStyle = BorderStyle.FixedSingle;
                    nud.BackColor = Color.FromArgb(0x2F, 0x33, 0x38);
                    nud.ForeColor = gx.Pal.Text;
                    nud.DecimalPlaces = Math.Max(0, Math.Min(2, fd.Decimals));
                    if (fd.Pct != TutorialField.PctNone)
                    {
                        // v4.1 百分比偏移: the box holds the positive size in % (the arrow left of it is the direction)
                        nud.Minimum = 0;
                        nud.Maximum = PctLimit(fd);
                    }
                    else
                    {
                        nud.Minimum = (decimal)fd.Min;
                        nud.Maximum = (decimal)fd.Max;
                    }
                    nud.Increment = (decimal)(fd.Increment > 0 ? fd.Increment : 1);
                    nud.TextAlign = HorizontalAlignment.Left; // like the settings pages (SettingsForm number boxes)
                    nud.ValueChanged += delegate { OnNumber(idx); };
                    nud.MouseWheel += delegate(object ws, MouseEventArgs we)
                    {
                        // the wheel scrolls the panel unless the pointer is on this (focused) box: no accidental value changes
                        Point cp = canvas.PointToClient(Cursor.Position);
                        if (nud.Focused && nud.Bounds.Contains(cp)) return;
                        HandledMouseEventArgs h = we as HandledMouseEventArgs;
                        if (h != null) h.Handled = true;
                        frame.Wheel(new PointF(cp.X, cp.Y), we.Delta);
                    };
                    fieldCtl.Add(nud);
                    canvas.Controls.Add(nud);
                }
            }
            canvas.ResumeLayout(false);
        }

        /// <summary>Largest % a 百分比偏移 box accepts: the field's px range at the user's real settings (2 decimals, rounded
        /// down so the px limit is never passed).</summary>
        private decimal PctLimit(TutorialField fd)
        {
            return Dec(TutorialPct.FieldLimit(fd.Min, fd.Max, g), 0, 100000);
        }

        /// <summary>A 百分比偏移 box changed (or its arrow flipped): % x direction -> PROFILE px (double, not rounded), clamped
        /// to the field's px range.</summary>
        private double PctToFieldPx(TutorialField fd, double pct, int sign)
        {
            return TutorialPct.FieldPx(pct, sign, fd.Min, fd.Max, g);
        }

        /// <summary>The arrow of field i was clicked (or '-' typed in its box): the direction flips. A value of 0 only flips
        /// the arrow (shown by the frame) so the next typed number goes that way.</summary>
        private void FlipField(int i)
        {
            if (fieldsShown == null || i < 0 || i >= fieldsShown.Length) return;
            TutorialField fd = fieldsShown[i];
            Profile p = ctx.P;
            if (p == null || fd.Pct == TutorialField.PctNone || !fd.PctSigned || fd.Get == null || fd.Set == null) return;
            if (fd.Enabled != null && !fd.Enabled(p)) return;
            double v = fd.Get(p);
            if (v == 0) { frame.ToggleZeroSign(i); canvas.Invalidate(); return; }
            double nv = -v;
            if (nv < fd.Min) nv = fd.Min;
            if (nv > fd.Max) nv = fd.Max;
            fd.Set(p, nv);
            if (fd.After != null) fd.After(p);
            AfterEdit(true);
        }

        private static decimal Dec(double v, decimal min, decimal max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            decimal d;
            try { d = (decimal)v; } catch (OverflowException) { d = v > 0 ? max : min; }
            if (d < min) d = min;
            if (d > max) d = max;
            return d;
        }

        /// <summary>Re-reads every field from the profile (values, enabled state).</summary>
        private void RefreshFields()
        {
            TutorialField[] f = fieldsShown;
            Profile p = ctx.P;
            if (f == null || p == null) return;
            updatingFields = true;
            try
            {
                for (int i = 0; i < f.Length && i < fieldCtl.Count; i++)
                {
                    TutorialField fd = f[i];
                    bool en = fd.Enabled == null || fd.Enabled(p);
                    NumericUpDown nud = fieldCtl[i] as NumericUpDown;
                    if (nud != null && fd.Pct != TutorialField.PctNone)
                    {
                        decimal lim = PctLimit(fd);
                        if (nud.Maximum != lim) nud.Maximum = lim;
                        decimal v = Dec(TutorialPct.FieldPct(fd.Get(p), g), nud.Minimum, nud.Maximum);
                        if (nud.Value != v) nud.Value = v;
                        if (nud.Enabled != en) nud.Enabled = en;
                    }
                    else if (nud != null)
                    {
                        decimal v = Dec(fd.Get(p), nud.Minimum, nud.Maximum);
                        decimal r = Math.Round(v, nud.DecimalPlaces, MidpointRounding.AwayFromZero);
                        if (nud.Value != r) nud.Value = r;
                        if (nud.Enabled != en) nud.Enabled = en;
                    }
                    CheckBox cb = fieldCtl[i] as CheckBox;
                    if (cb != null)
                    {
                        bool v = fd.GetCheck != null && fd.GetCheck(p);
                        if (cb.Checked != v) cb.Checked = v;
                        if (cb.Enabled != en) cb.Enabled = en;
                    }
                }
            }
            finally { updatingFields = false; }
        }

        private void OnNumber(int i)
        {
            if (updatingFields || fieldsShown == null || i >= fieldsShown.Length) return;
            Profile p = ctx.P;
            NumericUpDown nud = fieldCtl[i] as NumericUpDown;
            if (p == null || nud == null) return;
            TutorialField fd = fieldsShown[i];
            if (fd.Set != null)
            {
                if (fd.Pct != TutorialField.PctNone) fd.Set(p, PctToFieldPx(fd, (double)nud.Value, frame.FieldSign(i)));
                else fd.Set(p, (double)nud.Value);
            }
            if (fd.After != null) fd.After(p);
            AfterEdit(true);
        }

        private void OnCheck(int i)
        {
            if (updatingFields || fieldsShown == null || i >= fieldsShown.Length) return;
            Profile p = ctx.P;
            CheckBox cb = fieldCtl[i] as CheckBox;
            if (p == null || cb == null) return;
            TutorialField fd = fieldsShown[i];
            if (fd.SetCheck != null) fd.SetCheck(p, cb.Checked);
            if (fd.After != null) fd.After(p);
            AfterEdit(true);
        }

        /// <summary>A value changed: save soon, tell the app, let the step refresh its demo, re-read dependent fields.</summary>
        private void AfterEdit(bool fromField)
        {
            dirty = true;
            saveTimer.Stop();
            saveTimer.Start();
            Action h = ValuesChanged;
            if (h != null) { try { h(); } catch (Exception) { } }
            TutorialStep cur = frame.Current;
            if (fromField && cur != null) { try { cur.OnFieldChanged(ctx); } catch (Exception) { } }
            // texts with placeholders (e.g. 「当前：{当前上抬}」) show the new value at once
            if (cur != null && HasPlaceholder(cur)) frame.RefreshText();
            RefreshFields();
        }

        private static bool HasPlaceholder(TutorialStep s)
        {
            if (Has(s.Title) || Has(s.Body) || Has(s.Note) || Has(s.DevNote) || Has(s.FieldNote) || Has(s.Hint)) return true;
            if (s.Prompts != null) foreach (string p in s.Prompts) if (Has(p)) return true;
            return false;
        }

        private static bool Has(string t) { return t != null && t.IndexOf('{') >= 0; }

        // ------------------------------------------------------------------ save / finish / close
        private void SaveNow()
        {
            string err = SettingsIO.Save(g, SettingsIO.DefaultPath());
            if (err == null) dirty = false;
            else ctx.Term.Push("> 保存失败：" + err);
        }

        private void Finish()
        {
            if (!g.TutorialDone) g.TutorialDone = true;
            finished = true;
            saveTimer.Stop();
            SaveNow();
            Action h = ValuesChanged;
            if (h != null) { try { h(); } catch (Exception) { } }
        }

        private void CloseAndOpen(string page)
        {
            Close();
            if (jumpToPage != null && !string.IsNullOrEmpty(page)) { try { jumpToPage(page); } catch (Exception) { } }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            closing = true;
            timer.Stop();
            saveTimer.Stop();
            if (dirty) SaveNow();
            lastIndex = finished ? 0 : Math.Max(0, frame.Index);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (foregroundHandler != null) { ForegroundState.Changed -= foregroundHandler; foregroundHandler = null; }
                timer.Stop(); timer.Dispose();
                saveTimer.Stop(); saveTimer.Dispose();

                foreach (Control c in fieldCtl) c.Dispose();
                fieldCtl.Clear();
                frame.Dispose();
                gx.Dispose(); // disposes its current fonts
                if (ctlFont != null) ctlFont.Dispose();
                DisposeFrozen();
                // v4.1 memory: the font families too (fonts first), so a closed tutorial keeps nothing but the static lastIndex
                try { if (uiFamily != null) uiFamily.Dispose(); } catch (Exception) { }
                try { if (monoFamily != null) monoFamily.Dispose(); } catch (Exception) { }
            }
            base.Dispose(disposing);
        }

        /// <summary>The canvas: one double-buffered, opaque, focusable surface.</summary>
        private sealed class Canvas : Control
        {
            public Canvas()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                DoubleBuffered = true;
            }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.Up || code == Keys.Down || code == Keys.Left || code == Keys.Right) return true;
                return base.IsInputKey(keyData);
            }
        }
    }
}
