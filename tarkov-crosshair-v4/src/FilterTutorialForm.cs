// FilterTutorialForm.cs -- 滤镜教程 (v4.2, W3): the window that hosts FilterTutorialView.
// Open it from the 滤镜 page:  FilterTutorialForm.Show(this);   (or Show(this, keys) with the user's own hotkey names)
// Modal, sized and font-scaled like the crosshair tutorial (TutorialForm): 1180 x 760 at 100 %, one fixed text size
// (v4.3.1), same colours (TutorialPalette.FromTheme). The tutorial only draws a picture: it never changes the
// screen's colour curve, never captures the screen and never reads the game.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    public sealed class FilterTutorialForm : Form
    {
        private readonly FilterTutorialModel model;
        private readonly FilterTutorialView view;
        private readonly TutorialGfx gx;
        private readonly Canvas canvas;
        private readonly Timer timer;
        private readonly ToolTip tip;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly FontFamily uiFamily, monoFamily;
        private readonly float k;
        private bool sizing, sizedDuringDrag; // v4.3.1: one relayout at ResizeEnd, none per mouse move
        private string tipText = "";
        private bool closing;

        /// <summary>Opens the filter tutorial as a dialog over owner (null = no owner). Default hotkey names.</summary>
        public static new void Show(IWin32Window owner) { Show(owner, null); }

        /// <summary>Opens the filter tutorial with the user's own hotkey names (null = defaults F8 / F7 / F9).</summary>
        public static void Show(IWin32Window owner, FilterTutorialKeys keys)
        {
            using (FilterTutorialForm f = new FilterTutorialForm(keys))
            {
                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
            }
        }

        public FilterTutorialForm(FilterTutorialKeys keys)
        {
            k = SystemScale();
            model = new FilterTutorialModel(keys);

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "滤镜教程";
            ShowInTaskbar = false;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; } catch (Exception) { }
            Size want = new Size((int)(1180 * k), (int)(760 * k));
            Size max = new Size((int)(wa.Width * 0.96), (int)(wa.Height * 0.94));
            Size min = new Size(Math.Min((int)(960 * k), max.Width), Math.Min((int)(620 * k), max.Height));
            ClientSize = new Size(Math.Min(want.Width, max.Width), Math.Min(want.Height, max.Height));
            MinimumSize = SizeFromClientSize(min);

            TutorialPalette pal = TutorialPalette.FromTheme();
            BackColor = pal.Bg;
            ForeColor = pal.Text;
            uiFamily = PickFamily(new string[] { "Microsoft YaHei UI", "Microsoft YaHei" }, SystemFonts.MessageBoxFont.FontFamily);
            monoFamily = PickFamily(new string[] { "Consolas", "Cascadia Mono", "Courier New" }, FontFamily.GenericMonospace);
            // v4.3.1 (Leo): ONE fixed text size, whatever the window size; Windows DPI applies
            gx = new TutorialGfx(pal, new TutorialFonts(uiFamily, monoFamily, 1f));
            gx.K = k;
            view = new FilterTutorialView(model, gx);
            view.ExitRequested += delegate { CloseSoon(); };
            view.Finished += delegate { CloseSoon(); };

            canvas = new Canvas();
            canvas.Dock = DockStyle.Fill;
            canvas.BackColor = pal.Bg;
            canvas.Paint += delegate(object s, PaintEventArgs e)
            {
                try { view.Draw(e.Graphics, clock.Elapsed.TotalMilliseconds); } catch (Exception) { }
            };
            canvas.Resize += delegate { OnCanvasResize(); };
            canvas.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                canvas.Focus();
                canvas.Capture = true;
                if (view.OnMouseDown(new PointF(e.X, e.Y))) canvas.Invalidate();
            };
            canvas.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (view.OnMouseMove(new PointF(e.X, e.Y))) canvas.Invalidate();
                UpdateTip(new PointF(e.X, e.Y));
            };
            canvas.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                canvas.Capture = false;
                if (view.OnMouseUp(new PointF(e.X, e.Y))) canvas.Invalidate();
            };
            canvas.MouseLeave += delegate { view.OnMouseLeave(); canvas.Invalidate(); };
            Controls.Add(canvas);

            tip = new ToolTip();
            tip.InitialDelay = 400; tip.ReshowDelay = 200; tip.AutoPopDelay = 15000;

            timer = new Timer();
            timer.Interval = 31; // v4.3 (W7): 31 ms = 2 system ticks (~32 Hz); 33 rounded up to 3 ticks (~21 Hz). ~30 Hz is plenty: only the pulse and the intro sweep animate
            timer.Tick += delegate { if (!closing && Visible && WindowState != FormWindowState.Minimized) canvas.Invalidate(); };

            ResumeLayout(true);
            view.SetClient(LayoutSize(), k);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            canvas.Focus();
            timer.Start();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (view.OnKeyDown((int)e.KeyCode, e.Control, e.Shift)) { e.Handled = true; e.SuppressKeyPress = true; canvas.Invalidate(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (view.OnKeyUp((int)e.KeyCode)) { e.Handled = true; canvas.Invalidate(); return; }
            base.OnKeyUp(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // F10 / Alt would move focus to the (absent) menu; arrows / Enter must reach the canvas
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Escape) { CloseSoon(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            view.OnKeyUp(model.Keys.HoldVk); // a held F7 must not stick when the window loses focus
            canvas.Invalidate();
        }

        private void UpdateTip(PointF pt)
        {
            string t = view.TooltipAt(pt);
            if (t == tipText) return;
            tipText = t;
            tip.SetToolTip(canvas, t);
        }

        private void CloseSoon()
        {
            if (closing) return;
            closing = true;
            BeginInvoke(new MethodInvoker(Close));
        }

        private Size LayoutSize()
        {
            Size s = canvas.ClientSize;
            if (s.Width <= 0 || s.Height <= 0) s = ClientSize;
            return s;
        }

        private void OnCanvasResize()
        {
            Size sz = LayoutSize();
            if (sz.Width <= 0 || sz.Height <= 0 || view == null) return;
            if (sizing) { sizedDuringDrag = true; return; } // dragging an edge: one relayout when it is released
            view.SetClient(sz, k);
            canvas.Invalidate();
        }

        protected override void OnResizeBegin(EventArgs e) { base.OnResizeBegin(e); sizing = true; }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            sizing = false;
            if (!sizedDuringDrag || closing || IsDisposed) return;
            sizedDuringDrag = false;
            OnCanvasResize();
        }

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
                try { return new FontFamily(n); } catch (Exception) { }
            }
            return fallback;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                if (timer != null) { timer.Stop(); timer.Dispose(); }
                if (tip != null) tip.Dispose();
                if (view != null) view.Dispose();
                if (gx != null) gx.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>One double-buffered, opaque, focusable surface (arrows reach it).</summary>
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
