// C# 5 / .NET Framework 4.8 / WinForms + System.Drawing only.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    /// <summary>
    /// 移动速率曲线 editor (v2.4): x = time 0 → 100 % of 总时长, y = relative speed 0 → 100.
    /// Draws the smooth MotionCurve.SpeedAt curve and 5 round handles; a handle is dragged vertically (integers 0..100).
    /// </summary>
    internal sealed class CurveEditor : Control
    {
        /// <summary>The curve being edited (read every paint, so it always shows the model).</summary>
        public Func<MoveCurve> Curve;
        /// <summary>Raised after a drag changed point i in the model.</summary>
        public event Action<int> PointChanged;

        private int drag = -1;

        public CurveEditor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            Tag = "theme:keep"; // painted entirely here with the Theme colours
            Size = new Size(280, 120);
            Margin = new Padding(3, 3, 3, 3);
            BackColor = Theme.ColSurface;
            Cursor = Cursors.Hand;
        }

        private float K { get { return Math.Max(0.75f, ClientSize.Height / 120f); } }

        private RectangleF Plot
        {
            get
            {
                float k = K;
                float left = 26 * k, right = 10 * k, top = 8 * k, bottom = 18 * k;
                return new RectangleF(left, top, Math.Max(10, ClientSize.Width - left - right), Math.Max(10, ClientSize.Height - top - bottom));
            }
        }

        private double[] Speeds
        {
            get
            {
                MoveCurve c = Curve == null ? null : Curve();
                return c == null ? null : c.Speeds;
            }
        }

        private static PointF At(RectangleF r, double u, double v)
        {
            return new PointF(r.Left + (float)(u * r.Width), r.Bottom - (float)(v / 100.0 * r.Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ColInput);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = Plot;
            MoveCurve c = Curve == null ? null : Curve();
            double[] s = c == null ? null : c.Speeds;
            float k = K;
            using (Pen grid = new Pen(Theme.ColBorder, 1f))
            using (Brush muted = new SolidBrush(Theme.ColMuted))
            using (Font small = new Font(Font.FontFamily, Math.Max(6f, Font.Size * 0.85f)))
            {
                for (int i = 0; i <= 4; i++)
                {
                    float x = r.Left + r.Width * i / 4f;
                    g.DrawLine(grid, x, r.Top, x, r.Bottom);
                    string t = (i * 25) + "%";
                    SizeF ts = g.MeasureString(t, small);
                    float tx = Math.Min(Math.Max(0, x - ts.Width / 2), ClientSize.Width - ts.Width);
                    g.DrawString(t, small, muted, tx, r.Bottom + 1);
                }
                for (int i = 0; i <= 2; i++)
                {
                    float y = r.Bottom - r.Height * i / 2f;
                    g.DrawLine(grid, r.Left, y, r.Right, y);
                    string t = (i * 50).ToString();
                    SizeF ts = g.MeasureString(t, small);
                    g.DrawString(t, small, muted, r.Left - ts.Width - 2, y - ts.Height / 2);
                }
                if (c != null)
                {
                    string ms = "总时长 " + Math.Round(c.DurationMs) + " ms";
                    SizeF ts = g.MeasureString(ms, small);
                    g.DrawString(ms, small, muted, r.Right - ts.Width, r.Top + 1);
                }
            }
            if (s == null) return;
            PointF[] line = new PointF[121];
            for (int i = 0; i < line.Length; i++)
            {
                double u = i / (double)(line.Length - 1);
                line[i] = At(r, u, MotionCurve.SpeedAt(s, u));
            }
            using (Pen p = new Pen(Theme.ColAccent, 2f * k)) g.DrawLines(p, line);
            float rad = 5f * k;
            for (int i = 0; i < MotionCurve.Points; i++)
            {
                PointF h = At(r, i / 4.0, i < s.Length ? s[i] : 0);
                using (Brush b = new SolidBrush(i == drag ? Theme.ColAccent : Theme.ColText))
                    g.FillEllipse(b, h.X - rad, h.Y - rad, 2 * rad, 2 * rad);
                using (Pen o = new Pen(Theme.ColBackground, 1f)) g.DrawEllipse(o, h.X - rad, h.Y - rad, 2 * rad, 2 * rad);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled || Speeds == null) return;
            Focus();
            RectangleF r = Plot;
            // nearest point by x (each point owns a column), so a click anywhere above/below a handle grabs it
            int best = (int)Math.Round((e.X - r.Left) / r.Width * 4);
            if (best < 0) best = 0;
            if (best > 4) best = 4;
            drag = best;
            Capture = true;
            SetFromMouse(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag >= 0) SetFromMouse(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag >= 0) { drag = -1; Capture = false; Invalidate(); }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && drag >= 0) { drag = -1; Invalidate(); }
        }

        private void SetFromMouse(int y)
        {
            double[] s = Speeds;
            if (s == null || drag < 0 || drag >= s.Length) return;
            RectangleF r = Plot;
            double v = Math.Round((r.Bottom - y) / r.Height * 100.0);
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            if (s[drag] != v)
            {
                s[drag] = v;
                Action<int> h = PointChanged;
                if (h != null) h(drag);
            }
            Invalidate();
        }
    }

    /// <summary>跳跃 path plot (v2.4.3, read-only): height (+ = up) over 0 → 总时长, phase boundaries marked. Redraw with Invalidate.</summary>
    internal sealed class JumpPlot : Control
    {
        public Func<JumpSettings> Jump;

        public JumpPlot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Tag = "theme:keep"; // painted entirely here with the Theme colours
            Size = new Size(300, 120);
            Margin = new Padding(3, 3, 3, 3);
            BackColor = Theme.ColSurface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ColInput);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            JumpSettings j = Jump == null ? null : Jump();
            if (j == null || j.Phases == null || j.Phases.Count < JumpSettings.PhaseCount) return;
            float k = Math.Max(0.75f, ClientSize.Height / 120f);
            RectangleF r = new RectangleF(30 * k, 8 * k, Math.Max(10, ClientSize.Width - 40 * k), Math.Max(10, ClientSize.Height - 26 * k));
            double total = Math.Max(1, j.TotalMs);
            int n = 160;
            double lo = 0, hi = 0;
            double[] ys = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                ys[i] = JumpPath.Offset(j, total * i / n);
                lo = Math.Min(lo, ys[i]); hi = Math.Max(hi, ys[i]);
            }
            for (int i = 0; i < JumpSettings.PhaseCount - 1; i++) { lo = Math.Min(lo, j.Phases[i].EndPx); hi = Math.Max(hi, j.Phases[i].EndPx); }
            if (hi - lo < 1) { hi += 0.5; lo -= 0.5; }
            double pad = (hi - lo) * 0.08;
            hi += pad; lo -= pad;
            Func<double, float> Y = delegate(double v) { return r.Bottom - (float)((v - lo) / (hi - lo) * r.Height); };
            using (Pen grid = new Pen(Theme.ColBorder, 1f))
            using (Pen zero = new Pen(Theme.ColMuted, 1f))
            using (Brush muted = new SolidBrush(Theme.ColMuted))
            using (Font small = new Font(Font.FontFamily, Math.Max(6f, Font.Size * 0.85f)))
            {
                double[] b = JumpPath.Boundaries(j);
                for (int i = 0; i < b.Length; i++)
                {
                    float x = r.Left + (float)(b[i] / total * r.Width);
                    g.DrawLine(grid, x, r.Top, x, r.Bottom);
                    if (i < b.Length - 1)
                    {
                        float x2 = r.Left + (float)(b[i + 1] / total * r.Width);
                        string t = (i + 1).ToString();
                        SizeF ts = g.MeasureString(t, small);
                        if (x2 - x > ts.Width) g.DrawString(t, small, muted, (x + x2 - ts.Width) / 2, r.Bottom + 1);
                    }
                }
                zero.DashStyle = DashStyle.Dash;
                g.DrawLine(zero, r.Left, Y(0), r.Right, Y(0));
                string top = Math.Round(hi).ToString(), bot = Math.Round(lo).ToString();
                g.DrawString(top, small, muted, r.Left - g.MeasureString(top, small).Width - 2, r.Top - 2);
                g.DrawString(bot, small, muted, r.Left - g.MeasureString(bot, small).Width - 2, r.Bottom - g.MeasureString(bot, small).Height + 2);
                string ms = Math.Round(total) + " ms";
                SizeF mss = g.MeasureString(ms, small);
                g.DrawString(ms, small, muted, r.Right - mss.Width, r.Bottom + 1);
            }
            PointF[] line = new PointF[n + 1];
            for (int i = 0; i <= n; i++) line[i] = new PointF(r.Left + r.Width * i / n, Y(ys[i]));
            using (Pen p = new Pen(Theme.ColAccent, 2f * k)) g.DrawLines(p, line);
        }
    }
}
