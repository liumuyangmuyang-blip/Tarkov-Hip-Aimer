using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    /// <summary>
    /// 启动动画 (v4.0): ~1.8 s borderless terminal-style splash. The ring sweeps in, 「TARKOV HIP AIMER」 decodes,
    /// three boot lines type out with OK, a scan line passes once, then the window fades out. A click or any key skips it.
    /// The caller decides whether to show it (ShouldShow); the overlay is already running underneath; Done runs once.
    /// </summary>
    internal sealed class Splash : Form
    {
        private const double SweepEndMs = 600, TitleStartMs = 120, TitleEndMs = 700;
        private const double LinesStartMs = 450, LineMs = 260, ScanStartMs = 250, ScanEndMs = 1150;
        private const double FadeStartMs = 1550, EndMs = 1800;

        private readonly Stopwatch clock = new Stopwatch();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly float k;
        private readonly string[] lines;
        private bool finished;
        public event Action Done;
        /// <summary>v5.0.2: the parts that never change (grid, frame, corner brackets, scanlines) are drawn once into this
        /// bitmap, and the fonts are made once, so each 15 ms frame is cheap and the UI thread (tray menu) stays free.</summary>
        private Bitmap backdrop;
        private Font titleFont, subFont, lineMono, lineUi;
        /// <summary>Frames painted (tests / log).</summary>
        internal int Frames;

        /// <summary>False when the user turned it off or Windows animations are off.</summary>
        public static bool ShouldShow(GlobalSettings g)
        {
            if (g == null || !g.SplashOnStart) return false;
            return Theme.AnimationsOn;
        }

        public Splash(Icon icon, string profileName)
        {
            float s = 1f;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f; } catch (Exception) { }
            k = (s >= 0.5f && s <= 5f) ? s : 1f;
            // v4.3.1: one fixed text size (no screen-size scaling); Windows display scaling only
            string prof = string.IsNullOrEmpty(profileName) ? GlobalSettings.DefaultProfileName : profileName;
            if (prof.Length > 14) prof = prof.Substring(0, 13) + "…";
            lines = new string[] { "载入配置「" + prof + "」", "准星窗口", "按键映射" };

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Text = Program.ProductName;
            if (icon != null) Icon = icon;
            BackColor = Theme.ColHeader;
            ClientSize = new Size(S(520), S(250));
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

        /// <summary>v5.0.2 (Leo: no animation while the game is in front): another program took the foreground -> the
        /// splash ends at once (its timer stops) instead of animating behind it.</summary>
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            try
            {
                BeginInvoke((Action)delegate { if (!finished && Native.ForegroundIsOurs() == 0) Finish(); });
            }
            catch (Exception) { }
        }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); Finish(); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { Finish(); return true; }

        private void Step()
        {
            double t = clock.Elapsed.TotalMilliseconds;
            if (t >= EndMs) { Finish(); return; }
            double o = t <= FadeStartMs ? 1.0 : 1.0 - (t - FadeStartMs) / (EndMs - FadeStartMs);
            // v5.1.1 (X2): Opacity only when it changes (each set is a layered-window update; 1.0 -> < 1 also changes the
            // window style), so the 1.5 s before the fade only repaint
            o = Math.Max(0.0, Math.Min(1.0, Math.Round(o, 2)));
            if (o != lastOpacity) { lastOpacity = o; try { Opacity = o; } catch (Exception) { } }
            Invalidate();
        }

        private double lastOpacity = 1.0;

        /// <summary>
        /// v5.1.1 (X2, Leo F6 「启动画面还是卡」): the splash runs on its OWN UI thread (own message loop), so nothing the main
        /// UI thread does meanwhile (building the settings window, the tray, the filter) can hold up a frame. done runs once
        /// on the splash thread when the splash ends (or could not be shown); the caller marshals it to its own thread.
        /// The icon is copied for the splash thread (GDI+ objects are not shared between threads).
        /// </summary>
        public static void ShowOnOwnThread(Icon icon, string profileName, Action done)
        {
            Icon copy = null;
            try { if (icon != null) copy = (Icon)icon.Clone(); } catch (Exception) { copy = null; }
            int once = 0;
            Action fire = delegate { if (Interlocked.Exchange(ref once, 1) == 0 && done != null) { try { done(); } catch (Exception ex) { Program.Log("splash done: " + ex); } } };
            Thread t = new Thread(delegate()
            {
                try
                {
                    using (Splash s = new Splash(copy, profileName))
                    {
                        s.Done += fire;
                        Application.Run(s);
                    }
                }
                catch (Exception ex) { Program.Log("splash thread: " + ex.Message); }
                finally
                {
                    fire(); // also when the splash could not run at all: the app goes on
                    if (copy != null) { try { copy.Dispose(); } catch (Exception) { } }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Name = "Splash";
            t.Start();
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

        private static double Unit(double t, double a, double b)
        {
            double x = (t - a) / (b - a);
            return x < 0 ? 0 : (x > 1 ? 1 : x);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                double t = clock.IsRunning ? clock.Elapsed.TotalMilliseconds : 0;
                Frames++;
                if (backdrop == null || backdrop.Width != w || backdrop.Height != h)
                {
                    if (backdrop != null) backdrop.Dispose();
                    backdrop = new Bitmap(Math.Max(1, w), Math.Max(1, h));
                    using (Graphics bg = Graphics.FromImage(backdrop)) DrawBackdrop(bg, w, h);
                }
                g.DrawImageUnscaled(backdrop, 0, 0);
                if (titleFont == null)
                {
                    titleFont = Theme.Mono(17f, FontStyle.Bold);
                    subFont = Theme.Mono(11f); // SPEC v4 readability >= 11 pt
                    lineMono = Theme.Mono(11f);
                    lineUi = Theme.Ui(11f);
                }

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Ring: black outline under green, swept clockwise from the top.
                float d = S(78);
                float cx = S(34) + d / 2f, cy = S(78);
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
                g.SmoothingMode = SmoothingMode.None;

                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                float tx = cx + d / 2f + S(22);
                int maxW = Math.Max(0, w - (int)tx - S(14));
                // Title decodes in.
                double tp = Unit(t, TitleStartMs, TitleEndMs);
                {
                    Font tf = titleFont, sf = subFont;
                    string title = Program.ShortName.ToUpperInvariant();
                    Size ts = TextRenderer.MeasureText(g, title, tf, new Size(w, h), flags);
                    int top = (int)Math.Round(cy - ts.Height * 0.95f);
                    if (tp > 0)
                        TextRenderer.DrawText(g, Theme.Decode(title, tp, 11), tf, new Rectangle((int)tx, top, maxW, ts.Height),
                            Theme.Blend(Theme.ColHeader, Theme.ColAccent, Math.Min(1.0, tp * 2)), flags | TextFormatFlags.EndEllipsis);
                    string sub = Program.Version + "  ·  " + Program.ChineseName + "  ·  by Leo";
                    TextRenderer.DrawText(g, Theme.Decode(sub, tp * 1.3 - 0.3, 5), sf, new Rectangle((int)tx, top + ts.Height + S(4), maxW, sf.Height + 2),
                        Theme.Blend(Theme.ColHeader, Theme.ColAccent2, tp), flags);
                }

                // Boot lines type out, each ending in OK.
                {
                    Font lf = lineMono, cf = lineUi;
                    int ly = S(146);
                    int lh = Math.Max(lf.Height, cf.Height) + S(4);
                    int lx = S(34);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        double lp = Unit(t, LinesStartMs + i * LineMs, LinesStartMs + i * LineMs + LineMs * 0.8);
                        if (lp <= 0) break;
                        string body = lines[i];
                        int chars = (int)Math.Ceiling(body.Length * Math.Min(1.0, lp / 0.7));
                        string typed = body.Substring(0, Math.Min(body.Length, chars));
                        int y = ly + i * lh;
                        TextRenderer.DrawText(g, ">", lf, new Point(lx, y), Theme.ColAccent, flags);
                        int x2 = lx + TextRenderer.MeasureText(g, "> ", lf, new Size(w, h), flags).Width;
                        TextRenderer.DrawText(g, typed, cf, new Point(x2, y), Theme.ColText, flags);
                        if (lp >= 1)
                        {
                            string ok = "[ 就绪 ]";
                            Size os = TextRenderer.MeasureText(g, ok, cf, new Size(w, h), flags);
                            int okX = w - S(34) - os.Width;
                            int dotsX = x2 + TextRenderer.MeasureText(g, body, cf, new Size(w, h), flags).Width + S(8);
                            if (okX - dotsX > S(10))
                                using (Pen dp = new Pen(Theme.ColBorder)) { dp.DashStyle = DashStyle.Dot; g.DrawLine(dp, dotsX, y + lh / 2 - S(2), okX - S(8), y + lh / 2 - S(2)); }
                            TextRenderer.DrawText(g, ok, cf, new Point(okX, y), Theme.ColAccent, flags);
                        }
                        else if (((int)(t / 120)) % 2 == 0)
                        {
                            int cx2 = x2 + TextRenderer.MeasureText(g, typed, cf, new Size(w, h), flags).Width + S(2);
                            using (SolidBrush cb = new SolidBrush(Theme.ColAccent)) g.FillRectangle(cb, cx2, y + S(2), S(7), lh - S(8));
                        }
                    }
                }

                // One scan line passes top -> bottom.
                double sp = Unit(t, ScanStartMs, ScanEndMs);
                if (sp > 0 && sp < 1)
                {
                    int sy = (int)Math.Round(sp * h);
                    // v5.1 flat (decision 14): one solid theme-colour line, no gradient band
                    using (Pen sp2 = new Pen(Theme.ColAccent2, Theme.BorderPx)) g.DrawLine(sp2, 0, sy, w, sy);
                }
                Theme.DrawScanlines(g, new Rectangle(1, 1, w - 2, h - 2), 55);
                Theme.DrawFadeLine(g, 1, h - Math.Max(2, S(2)) - 1, w - 2, Math.Max(2, S(2)), Theme.ColAccent);
            }
            catch (Exception) { }
        }

        /// <summary>The still parts: background, faint grid, frame and the two corner brackets (drawn once per size).</summary>
        private void DrawBackdrop(Graphics g, int w, int h)
        {
            g.Clear(Theme.ColHeader);
            using (Pen grid = new Pen(Theme.ColGrid))
            {
                int step = S(20);
                for (int x = step; x < w; x += step) g.DrawLine(grid, x, 0, x, h);
                for (int y = step; y < h; y += step) g.DrawLine(grid, 0, y, w, y);
            }
            using (Pen border = new Pen(Theme.ColBorder)) g.DrawRectangle(border, 0, 0, w - 1, h - 1);
            int c = S(14);
            using (Pen ap = new Pen(Theme.ColAccent, Math.Max(1f, k)))
            {
                g.DrawLines(ap, new Point[] { new Point(S(6), S(6) + c), new Point(S(6), S(6)), new Point(S(6) + c, S(6)) });
                g.DrawLines(ap, new Point[] { new Point(w - S(7) - c, h - S(7)), new Point(w - S(7), h - S(7)), new Point(w - S(7), h - S(7) - c) });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
                foreach (Font f in new Font[] { titleFont, subFont, lineMono, lineUi }) if (f != null) f.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
