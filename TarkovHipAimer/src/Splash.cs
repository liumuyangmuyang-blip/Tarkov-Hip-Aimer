using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    /// <summary>
    /// 启动动画 (v3.0): ~1.2 s borderless centred splash. The two-tone ring draws itself as a sweeping arc,
    /// 「Tarkov Hip Aimer」 + 「by Leo」 fade in, then the whole window fades out. A click or any key skips it.
    /// The caller decides whether to show it (SplashOnStart, Windows animations, --autostart); see ShouldShow.
    /// The overlay is already running underneath; Done runs once when the splash is gone.
    /// </summary>
    internal sealed class Splash : Form
    {
        private const double SweepEndMs = 650, TextStartMs = 250, TextEndMs = 750, FadeStartMs = 1000, EndMs = 1200;

        private readonly Stopwatch clock = new Stopwatch();
        private readonly Timer timer = new Timer();
        private readonly float k;
        private bool finished;
        public event Action Done;

        /// <summary>False when the user turned it off or Windows animations are off.</summary>
        public static bool ShouldShow(GlobalSettings g)
        {
            if (g == null || !g.SplashOnStart) return false;
            try { if (!SystemInformation.UIEffectsEnabled) return false; } catch (Exception) { }
            return true;
        }

        public Splash(Icon icon)
        {
            float s = 1f;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f; } catch (Exception) { }
            k = (s >= 0.5f && s <= 5f) ? s : 1f;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Text = Program.ProductName;
            if (icon != null) Icon = icon;
            BackColor = Theme.ColHeader;
            ClientSize = new Size(S(380), S(190));
            Opacity = 1.0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timer.Interval = 15;
            timer.Tick += delegate { Step(); };
        }

        private int S(int px) { return (int)Math.Round(px * k); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            clock.Start();
            timer.Start();
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Finish(); }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); Finish(); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { Finish(); return true; }

        private void Step()
        {
            double t = clock.Elapsed.TotalMilliseconds;
            if (t >= EndMs) { Finish(); return; }
            double o = t <= FadeStartMs ? 1.0 : 1.0 - (t - FadeStartMs) / (EndMs - FadeStartMs);
            try { Opacity = Math.Max(0.0, Math.Min(1.0, o)); } catch (Exception) { }
            Invalidate();
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            try { Hide(); } catch (Exception) { }
            Action h = Done;
            if (h != null)
            {
                try { h(); } catch (Exception ex) { Program.Log("splash done: " + ex); }
            }
            try { BeginInvoke((Action)Close); } catch (Exception) { }
        }

        private static double EaseOut(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return 1 - (1 - x) * (1 - x) * (1 - x);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                g.Clear(Theme.ColHeader);
                using (Pen border = new Pen(Theme.ColBorder)) g.DrawRectangle(border, 0, 0, w - 1, h - 1);
                using (SolidBrush accent = new SolidBrush(Theme.ColAccent)) g.FillRectangle(accent, 0, h - Math.Max(2, S(2)), w, Math.Max(2, S(2)));

                double t = clock.IsRunning ? clock.Elapsed.TotalMilliseconds : 0;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Ring: black outline under green, swept clockwise from the top.
                float d = S(84);
                float cx = S(30) + d / 2f, cy = h / 2f;
                float inner = Math.Max(2f, d * 0.12f);
                float outer = inner + 2f * Math.Max(1f, d / 28f);
                RectangleF circle = new RectangleF(cx - d / 2f + outer / 2f, cy - d / 2f + outer / 2f, d - outer, d - outer);
                float sweep = (float)(360.0 * EaseOut(t / SweepEndMs));
                if (sweep > 0.5f)
                {
                    using (Pen po = new Pen(Theme.ColRingOuter, outer))
                    using (Pen pi = new Pen(Theme.ColRingInner, inner))
                    {
                        if (sweep >= 359.5f)
                        {
                            g.DrawEllipse(po, circle);
                            g.DrawEllipse(pi, circle);
                        }
                        else
                        {
                            po.StartCap = LineCap.Round; po.EndCap = LineCap.Round;
                            pi.StartCap = LineCap.Round; pi.EndCap = LineCap.Round;
                            g.DrawArc(po, circle, -90f, sweep);
                            g.DrawArc(pi, circle, -90f, sweep);
                        }
                    }
                }

                // Text fades in.
                double a = (t - TextStartMs) / (TextEndMs - TextStartMs);
                a = a < 0 ? 0 : (a > 1 ? 1 : a);
                if (a > 0)
                {
                    float tx = cx + d / 2f + S(22);
                    TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                    using (Font tf = new Font("Microsoft YaHei UI", 22f * k, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Font sf = new Font("Microsoft YaHei UI", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        Color tc = Theme.Blend(Theme.ColHeader, Theme.ColText, a);
                        Color sc = Theme.Blend(Theme.ColHeader, Theme.ColAccent, a);
                        Size ts = TextRenderer.MeasureText(g, Program.ShortName, tf, new Size(w, h), flags);
                        Size ss = TextRenderer.MeasureText(g, "by Leo", sf, new Size(w, h), flags);
                        int gap = S(6);
                        int top = (int)Math.Round(cy - (ts.Height + gap + ss.Height) / 2f);
                        int maxW = Math.Max(0, w - (int)tx - S(12));
                        TextRenderer.DrawText(g, Program.ShortName, tf, new Rectangle((int)tx, top, maxW, ts.Height), tc, flags | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(g, "by Leo", sf, new Rectangle((int)tx, top + ts.Height + gap, maxW, ss.Height), sc, flags);
                    }
                }
            }
            catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
