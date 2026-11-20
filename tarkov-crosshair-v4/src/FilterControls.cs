using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TarkovShade;

// v4.2 (W2) 滤镜 page controls: FilterSlider (the 外观 page's HackSlider look, StylePanel.cs, copied: that one is private) and
// ToneCurveGraph (plot of the filter's real curve, PilotCurve.Build). Colours from Theme only.
// v5.1 (W24, Leo decision 10): Photoshop-style, 5 draggable points (FilterCurvePoints, FilterTune.cs); the page applies a drag.

namespace TarkovCrosshair
{
    /// <summary>Flat slider, drawn exactly like StylePanel's HackSlider (track, accent fill, block knob, glow on hover/focus).</summary>
    internal sealed class FilterSlider : Control
    {
        private readonly double min, max, step;
        private double value;
        private bool dragging, hover;
        public event EventHandler ValueChanged;
        /// <summary>Raised when a mouse drag ends (or after a key / wheel step): the page saves then.</summary>
        public event EventHandler Committed;

        public FilterSlider(double min, double max, double step)
        {
            this.min = min; this.max = Math.Max(max, min + 1e-9); this.step = step > 0 ? step : 1;
            value = min;
            Tag = "theme:keep";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public double Minimum { get { return min; } }
        public double Maximum { get { return max; } }
        public bool Dragging { get { return dragging; } }

        public double Value
        {
            get { return value; }
            set
            {
                double v = Clamp(value);
                if (v == this.value) return;
                this.value = v;
                Invalidate();
            }
        }

        private double Clamp(double v)
        {
            if (double.IsNaN(v)) v = min;
            return v < min ? min : (v > max ? max : v);
        }

        private void SetByUser(double v)
        {
            v = Clamp(Math.Round((v - min) / step) * step + min);
            v = Clamp(Math.Round(v, 6));
            if (v == value) return;
            value = v;
            Invalidate();
            EventHandler h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void Commit()
        {
            EventHandler h = Committed;
            if (h != null) h(this, EventArgs.Empty);
        }

        private int Pad { get { return Math.Max(6, Height / 3); } }

        private double FromX(int x)
        {
            int pad = Pad, w = Math.Max(1, Width - 2 * pad);
            return min + (max - min) * Math.Max(0, Math.Min(1, (x - pad) / (double)w));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true;
            Capture = true;
            SetByUser(FromX(e.X));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) SetByUser(FromX(e.X));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool was = dragging;
            dragging = false;
            Capture = false;
            if (was) Commit();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e)
        {
            bool was = dragging;
            dragging = false;
            Invalidate();
            base.OnLostFocus(e);
            if (was) Commit();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Focused) return; // never steal the page scroll from a slider the user is not using
            SetByUser(value + (e.Delta > 0 ? step : -step));
            Commit();
            HandledMouseEventArgs he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double big = Math.Max(step, (max - min) / 10.0);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: SetByUser(value - step); e.Handled = true; break;
                case Keys.Right: case Keys.Up: SetByUser(value + step); e.Handled = true; break;
                case Keys.PageDown: SetByUser(value - big); e.Handled = true; break;
                case Keys.PageUp: SetByUser(value + big); e.Handled = true; break;
                case Keys.Home: SetByUser(min); e.Handled = true; break;
                case Keys.End: SetByUser(max); e.Handled = true; break;
            }
            if (e.Handled) Commit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics gr = e.Graphics;
            gr.Clear(Parent != null ? Parent.BackColor : Theme.ColSurface);
            if (Parent != null && Parent.BackColor.A < 255) gr.Clear(Theme.ColSurface);
            int pad = Pad, cy = Height / 2, w = Math.Max(1, Width - 2 * pad);
            int th = Math.Max(3, Height / 6);
            float t = (float)((value - min) / (max - min));
            int x = pad + (int)Math.Round(w * t);
            Color acc = Enabled ? Theme.ColAccent : Theme.ColMuted;
            using (SolidBrush b = new SolidBrush(Theme.ColBorder)) gr.FillRectangle(b, pad, cy - th / 2, w, th);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(Enabled ? 200 : 90, acc))) gr.FillRectangle(b, pad, cy - th / 2, x - pad, th);
            int kw = Math.Max(6, Height / 3), kh = Math.Max(10, Height - 6);
            Rectangle knob = new Rectangle(x - kw / 2, cy - kh / 2, kw, kh);
            if (hover || Focused || dragging)
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(50, acc))) gr.FillRectangle(glow, Rectangle.Inflate(knob, 3, 2));
            using (SolidBrush b = new SolidBrush(dragging ? Theme.ColAccent2 : acc)) gr.FillRectangle(b, knob);
            using (Pen p = new Pen(Theme.ColBackground)) gr.DrawRectangle(p, knob.X, knob.Y, knob.Width - 1, knob.Height - 1);
            if (Focused)
                using (Pen fp = new Pen(Theme.ColAccent2) { DashStyle = DashStyle.Dot }) gr.DrawRectangle(fp, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// Read-only tone-curve graph: 4x4 grid (0 / 64 / 128 / 192 / 255), shadow / mid / highlight zone bands, dashed identity
    /// diagonal, the real R / G / B output of PilotCurve.Build (one curve in the text colour when all three are equal), axis
    /// labels 输入 / 输出, and a hover readout 「输入 47 → R 113 G 113 B 113」.
    /// v5.1: 5 round points (黑位 / 暗部 / 中间调 / 高光 / 白位) on the luminance curve. Mouse: press near a point's column and drag
    /// up / down. Keyboard (focused): Left / Right picks a point, Up / Down moves it 1 step, PageUp / PageDown 8 steps.
    /// PointDragged(i, wanted output 0..255) asks the page to move point i; PointReleased ends a drag. No timers.
    /// </summary>
    internal sealed class ToneCurveGraph : Control
    {
        public Func<PilotStyle> Style;
        public event Action<int, double> PointDragged;
        public event Action PointReleased;
        private ShadeRamp ramp;
        private PilotStyle shown;
        private double shadowEdge = 0.3, highlightEdge = 0.7;
        private int hoverX = -1, drag = -1, selected = FilterCurvePoints.Mid;

        public ToneCurveGraph()
        {
            Tag = "theme:keep";
            BackColor = Theme.ColSurface;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public bool Dragging { get { return drag >= 0; } }

        /// <summary>Re-read the style and rebuild the curve. v5.1.1 (P2): only marks the curve stale; the curve and the zone bands
        /// are computed on the next paint / key press, so a hidden or not-yet-shown graph costs nothing and several slider
        /// changes before one paint compute it once.</summary>
        public void Rebuild()
        {
            stale = true;
            Invalidate();
        }

        private bool stale = true;

        private void EnsureCurve()
        {
            if (!stale) return;
            stale = false;
            PilotStyle s = null;
            try { s = Style != null ? Style() : null; } catch (Exception) { s = null; }
            if (s == null) s = PilotStyle.CreateNeutral("");
            try
            {
                shown = s;
                ramp = PilotCurve.Build(s);
                ZoneEdges(s, out shadowEdge, out highlightEdge);
            }
            catch (Exception ex) { Program.Log("filter: curve graph: " + ex.Message); ramp = null; }
        }

        /// <summary>
        /// Band edges from the real curve math: the input where the shadow (highlight) slider alone, at 100, moves the output by
        /// less than half of its largest move. Pure; tests call it.
        /// </summary>
        public static void ZoneEdges(PilotStyle s, out double shadowEdge, out double highlightEdge)
        {
            PilotStyle sh = PilotStyle.CreateNeutral(""), hi = PilotStyle.CreateNeutral("");
            sh.Shadows = 100; sh.ShadowRange = s.ShadowRange; sh.TransitionSoftness = s.TransitionSoftness;
            hi.Highlights = 100; hi.HighlightRange = s.HighlightRange; hi.TransitionSoftness = s.TransitionSoftness;
            PilotStyle id = PilotStyle.CreateNeutral("");
            double[] ds = new double[256], dh = new double[256];
            double ms = 0, mh = 0;
            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0, y0 = PilotCurve.PreviewValue(id, x);
                ds[i] = Math.Abs(PilotCurve.PreviewValue(sh, x) - y0);
                dh[i] = Math.Abs(PilotCurve.PreviewValue(hi, x) - y0);
                ms = Math.Max(ms, ds[i]); mh = Math.Max(mh, dh[i]);
            }
            shadowEdge = 0; highlightEdge = 1;
            for (int i = 0; i < 256; i++) if (ms > 0 && ds[i] >= 0.5 * ms) shadowEdge = i / 255.0;
            for (int i = 255; i >= 0; i--) if (mh > 0 && dh[i] >= 0.5 * mh) highlightEdge = i / 255.0;
            if (highlightEdge < shadowEdge) { double m = (shadowEdge + highlightEdge) / 2; shadowEdge = highlightEdge = m; }
        }

        private Rectangle Plot()
        {
            int lab = Font.Height + 8;
            int side = Math.Max(10, Math.Min(Width - lab - 8, Height - lab - 8));
            return new Rectangle(lab, 4, side, side);
        }

        /// <summary>v5.1: the point whose input is nearest the mouse column (each point owns a column), like CurveEditor.</summary>
        internal static int NearestPoint(double input)
        {
            int best = 0;
            for (int i = 1; i < FilterCurvePoints.Count; i++)
                if (Math.Abs(FilterCurvePoints.Inputs[i] - input) < Math.Abs(FilterCurvePoints.Inputs[best] - input)) best = i;
            return best;
        }

        private double OutputAtY(int y)
        {
            Rectangle r = Plot();
            return Math.Max(0, Math.Min(255, (r.Bottom - y) * 255.0 / Math.Max(1, r.Height)));
        }

        private void Ask(int i, double target)
        {
            Action<int, double> h = PointDragged;
            if (h != null && Enabled) h(i, target);
        }

        private void Release()
        {
            Action h = PointReleased;
            if (h != null) h();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            Rectangle r = Plot();
            drag = selected = NearestPoint((e.X - r.Left) * 255.0 / Math.Max(1, r.Width));
            Capture = true;
            Ask(drag, OutputAtY(e.Y));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag >= 0) { Ask(drag, OutputAtY(e.Y)); return; }
            Rectangle r = Plot();
            int x = e.X >= r.Left && e.X <= r.Right ? (int)Math.Round((e.X - r.Left) * 255.0 / Math.Max(1, r.Width)) : -1;
            if (x != hoverX) { hoverX = x; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag < 0) return;
            drag = -1;
            Capture = false;
            Release();
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && drag >= 0) { drag = -1; Release(); Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverX = -1; Invalidate(); }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            EnsureCurve();
            if (shown == null || !Enabled) return;
            int step = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: selected = Math.Max(0, selected - 1); Invalidate(); e.Handled = true; return;
                case Keys.Right: selected = Math.Min(FilterCurvePoints.Count - 1, selected + 1); Invalidate(); e.Handled = true; return;
                case Keys.Up: step = 1; break;
                case Keys.Down: step = -1; break;
                case Keys.PageUp: step = 8; break;
                case Keys.PageDown: step = -8; break;
                default: return;
            }
            e.Handled = true;
            Ask(selected, Math.Round(FilterCurvePoints.Output(shown, selected)) + step);
            Release();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureCurve();
            Graphics g = e.Graphics;
            g.Clear(Theme.ColSurface);
            Rectangle r = Plot();
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush bg = new SolidBrush(Theme.ColBackground)) g.FillRectangle(bg, r);
            // zone bands: shadow (cyan), highlight (amber), mid between them
            int xs = r.Left + (int)Math.Round(r.Width * shadowEdge), xh = r.Left + (int)Math.Round(r.Width * highlightEdge);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(26, Theme.ColAccent2))) g.FillRectangle(b, r.Left, r.Top, xs - r.Left, r.Height);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(22, Theme.ColWarn))) g.FillRectangle(b, xh, r.Top, r.Right - xh, r.Height);
            using (Pen grid = new Pen(Theme.ColGrid))
                for (int i = 1; i < 4; i++)
                {
                    int x = r.Left + r.Width * i / 4, y = r.Top + r.Height * i / 4;
                    g.DrawLine(grid, x, r.Top, x, r.Bottom);
                    g.DrawLine(grid, r.Left, y, r.Right, y);
                }
            using (Pen border = new Pen(Theme.ColBorder)) g.DrawRectangle(border, r);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen diag = new Pen(Theme.ColMuted) { DashStyle = DashStyle.Dash }) g.DrawLine(diag, r.Left, r.Bottom, r.Right, r.Top);
            if (ramp != null)
            {
                bool same = true;
                for (int i = 0; i < 256 && same; i++) same = ramp.Red[i] == ramp.Green[i] && ramp.Green[i] == ramp.Blue[i];
                float w = Math.Max(1.5f, Font.Height / 10f);
                if (same) DrawChannel(g, r, ramp.Red, Theme.ColText, w);
                else
                {
                    DrawChannel(g, r, ramp.Red, Color.FromArgb(0xFF, 0x5A, 0x5A), w);
                    DrawChannel(g, r, ramp.Green, Theme.ColAccent, w);
                    DrawChannel(g, r, ramp.Blue, Color.FromArgb(0x5A, 0x9C, 0xFF), w);
                }
            }
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush tb = new SolidBrush(Theme.ColMuted))
            {
                g.DrawString("输入", Font, tb, r.Left + r.Width / 2 - g.MeasureString("输入", Font).Width / 2, r.Bottom + 2);
                // 输出 stacked (one character per line): rotated CJK text is drawn badly by some renderers
                float lx = 2, ly = r.Top + r.Height / 2 - Font.Height;
                g.DrawString("输", Font, tb, lx, ly);
                g.DrawString("出", Font, tb, lx, ly + Font.Height);
            }
            if (shown != null)
            {
                // v5.1: the 5 points (selected / dragged one filled with the accent colour, a ring when the graph has focus)
                float rad = Math.Max(4f, Font.Height / 4f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < FilterCurvePoints.Count; i++)
                {
                    float px = r.Left + r.Width * FilterCurvePoints.Inputs[i] / 255f;
                    float py = r.Bottom - r.Height * (float)(FilterCurvePoints.Output(shown, i) / 255.0);
                    bool hot = i == drag || (i == selected && Focused);
                    using (SolidBrush b = new SolidBrush(hot ? Theme.ColAccent : Theme.ColText)) g.FillEllipse(b, px - rad, py - rad, 2 * rad, 2 * rad);
                    using (Pen o = new Pen(Theme.ColBackground)) g.DrawEllipse(o, px - rad, py - rad, 2 * rad, 2 * rad);
                    if (i == selected && Focused)
                        using (Pen f = new Pen(Theme.ColAccent2)) g.DrawEllipse(f, px - rad - 3, py - rad - 3, 2 * rad + 6, 2 * rad + 6);
                }
                g.SmoothingMode = SmoothingMode.None;
            }
            if (hoverX >= 0 && hoverX <= 255 && ramp != null && drag < 0)
            {
                int x = r.Left + (int)Math.Round(r.Width * hoverX / 255.0);
                using (Pen cur = new Pen(Theme.ColAccent2)) g.DrawLine(cur, x, r.Top, x, r.Bottom);
                string t = "输入 " + hoverX + " → R " + Out(ramp.Red[hoverX]) + " G " + Out(ramp.Green[hoverX]) + " B " + Out(ramp.Blue[hoverX]);
                SizeF sz = g.MeasureString(t, Font);
                float tx = Math.Min(r.Right - sz.Width - 4, Math.Max(r.Left + 4, x + 6));
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(220, Theme.ColHeader))) g.FillRectangle(bb, tx - 2, r.Top + 4, sz.Width + 4, sz.Height);
                using (SolidBrush tb = new SolidBrush(Theme.ColText)) g.DrawString(t, Font, tb, tx, r.Top + 4);
            }
        }

        private static int Out(ushort v) { return (int)Math.Round(v / 257.0); }

        private static void DrawChannel(Graphics g, Rectangle r, ushort[] ch, Color c, float width)
        {
            PointF[] pts = new PointF[256];
            for (int i = 0; i < 256; i++)
                pts[i] = new PointF(r.Left + r.Width * i / 255f, r.Bottom - r.Height * (ch[i] / 65535f));
            using (Pen p = new Pen(c, width)) g.DrawLines(p, pts);
        }
    }
}
