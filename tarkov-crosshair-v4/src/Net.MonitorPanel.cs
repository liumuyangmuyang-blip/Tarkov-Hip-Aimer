// v4.4 (W9) NET: 统计 panel, terminal style (Theme colours, mono font), owner-drawn. v5.1 (W28): 累计用户 / 平均每次 / 累计时长;
// Chinese text always in the UI font (Microsoft YaHei), only digits / versions in the mono font. Not wired yet (phase 2: a 「统计」
// nav page or dashboard tab). Shows 当前版本 / 最新版本, 总下载 + per release, 在线（20 分钟内）/ 今天, 按版本分布, 上次刷新.
// Refreshes on the 刷新 button and every 60 s, only while the panel is visible; the fetch runs on a background thread.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class MonitorPanel : Control
    {
        public const int RefreshMs = 60000;

        private readonly IMonitorSource source;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private MonitorSnapshot snap;
        private volatile bool fetching;
        private Rectangle rButton;
        private bool hoverButton;
        private Font fMono, fMonoBold, fUi, fUiReg;
        private float fontDpi;

        public MonitorPanel(IMonitorSource source)
        {
            this.source = source;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.ColBackground;
            ForeColor = Theme.ColText;
            timer.Interval = RefreshMs;
            timer.Tick += delegate { RefreshNow(); };
        }

        /// <summary>For the render harness / tests: show this snapshot without fetching.</summary>
        public void ShowSnapshot(MonitorSnapshot s) { snap = s; Invalidate(); }

        public MonitorSnapshot Snapshot { get { return snap; } }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            SyncTimer();
            if (Visible && IsHandleCreated && snap == null) RefreshNow();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SyncTimer();
            if (Visible && snap == null && source != null) RefreshNow();
        }

        private void SyncTimer()
        {
            bool on = Visible && IsHandleCreated && source != null;
            if (on != timer.Enabled) timer.Enabled = on;
        }

        public void RefreshNow()
        {
            if (source == null || fetching || IsDisposed) return;
            fetching = true;
            Invalidate(rButton);
            Thread t = new Thread(delegate()
            {
                MonitorSnapshot s = null;
                try { s = source.Fetch(); } catch (Exception) { }
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { fetching = false; if (s != null) snap = s; Invalidate(); });
                    else fetching = false;
                }
                catch (Exception) { fetching = false; }
            });
            t.IsBackground = true; t.Name = "monitor-fetch"; t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        private void EnsureFonts(float dpi)
        {
            if (fMono != null && Math.Abs(dpi - fontDpi) < 0.5f) return;
            DisposeFonts();
            fontDpi = dpi;
            // Point sizes: GDI scales them by the display DPI itself.
            fMono = Theme.Mono(10.5f);
            fMonoBold = Theme.Mono(10.5f, FontStyle.Bold);
            fUi = Theme.Ui(10.5f, FontStyle.Bold);
            fUiReg = Theme.Ui(10.5f);
        }

        private void DisposeFonts()
        {
            if (fMono != null) fMono.Dispose();
            if (fMonoBold != null) fMonoBold.Dispose();
            if (fUi != null) fUi.Dispose();
            if (fUiReg != null) fUiReg.Dispose();
            fMono = fMonoBold = fUi = fUiReg = null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = rButton.Contains(e.Location);
            if (h != hoverButton) { hoverButton = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(rButton); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverButton) { hoverButton = false; Cursor = Cursors.Default; Invalidate(rButton); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && rButton.Contains(e.Location)) RefreshNow();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float dpi = g.DpiX > 0 ? g.DpiX : 96f;
            float s = dpi / 96f;
            EnsureFonts(dpi);
            g.Clear(Theme.ColBackground);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int pad = (int)(16 * s);
            int lineH = (int)Math.Ceiling(Math.Max(fMono.GetHeight(g), fUiReg.GetHeight(g)) * 1.35f);
            Rectangle box = new Rectangle(pad, pad, Math.Max(10, Width - 2 * pad), Math.Max(10, Height - 2 * pad));
            using (SolidBrush sb = new SolidBrush(Theme.ColSurface)) g.FillRectangle(sb, box);
            using (Pen p = new Pen(Theme.ColBorder, Math.Max(1f, s))) g.DrawRectangle(p, box);
            Theme.DrawScanlines(g, box, 10);

            int x = box.X + pad, y = box.Y + pad / 2;
            int right = box.Right - pad;

            // title row + 刷新 button
            string btn = fetching ? "刷新中…" : "刷新";
            Size bt = TextRenderer.MeasureText(g, btn, fUi);
            rButton = new Rectangle(right - bt.Width - (int)(20 * s), y, bt.Width + (int)(20 * s), lineH + (int)(4 * s));
            using (SolidBrush bb = new SolidBrush(hoverButton ? Theme.ColHover : Theme.ColInput)) g.FillRectangle(bb, rButton);
            using (Pen bp = new Pen(fetching ? Theme.ColMuted : Theme.ColAccent, Math.Max(1f, s))) g.DrawRectangle(bp, rButton);
            TextRenderer.DrawText(g, btn, fUi, rButton, fetching ? Theme.ColMuted : Theme.ColAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Text_(g, "> 统计 // USAGE MONITOR", fUi, Theme.ColAccent, x, y + (int)(2 * s));
            y += lineH + (int)(10 * s);
            Theme.DrawFadeLine(g, x, y, right - x, Math.Max(1, (int)s), Theme.ColAccent2);
            y += (int)(10 * s);

            MonitorSnapshot m = snap;
            // one value column for every row: the widest label (real font metrics) + a gap
            int labelW = (int)(150 * s);
            foreach (string k in new[] { "在线（20 分钟内）", "今天（24 小时）", "平均每次（7 天）", "累计用户", "累计时长", "当前版本", "最新版本", "上次刷新" })
                labelW = Math.Max(labelW, TextRenderer.MeasureText(g, k, fUiReg, Size.Empty, TextFormatFlags.NoPadding).Width + lineH);
            if (m == null)
            {
                Text_(g, "正在读取……", fUiReg, Theme.ColMuted, x, y);
                return;
            }

            DownloadStats d = m.Downloads;
            KeyValue(g, x, ref y, labelW, lineH, "当前版本", NetVersion.Normal(m.Running), Theme.ColText);
            string latest = d != null && d.Latest.Length > 0 ? d.Latest : "—";
            bool newer = d != null && NetVersion.IsNewer(d.Latest, m.Running);
            KeyValue(g, x, ref y, labelW, lineH, "最新版本", latest + (newer ? "   ← 有新版" : ""), newer ? Theme.ColWarn : Theme.ColText);
            y += lineH / 3;

            if (d == null) KeyValue(g, x, ref y, labelW, lineH, "总下载", Why(m.DownloadsError), Theme.ColMuted);
            else
            {
                KeyValue(g, x, ref y, labelW, lineH, "总下载", N(d.Total), Theme.ColAccent);
                long max = 1;
                foreach (VersionCount v in d.PerRelease) max = Math.Max(max, v.Count);
                int shown = 0;
                foreach (VersionCount v in d.PerRelease)
                {
                    if (shown++ >= 6) { Text_(g, "  …", fUiReg, Theme.ColMuted, x, y); y += lineH; break; }
                    Bar(g, x, ref y, labelW, right, lineH, "  " + v.Version, v.Count, max, Theme.ColAccent2, s);
                }
            }
            y += lineH / 3;

            if (!m.OnlineConfigured) KeyValue(g, x, ref y, labelW, lineH, "在线", "未开启", Theme.ColMuted);
            else if (m.Online == null) KeyValue(g, x, ref y, labelW, lineH, "在线", Why(m.OnlineError), Theme.ColMuted);
            else
            {
                OnlineStats o = m.Online;
                KeyValue(g, x, ref y, labelW, lineH, "在线（20 分钟内）", N(o.OnlineNow), Theme.ColAccent);
                KeyValue(g, x, ref y, labelW, lineH, "今天（24 小时）", N(o.Online24h), Theme.ColText);
                KeyValue(g, x, ref y, labelW, lineH, "累计用户", N(o.UsersTotal), Theme.ColText);
                KeyValue(g, x, ref y, labelW, lineH, "平均每次（7 天）", o.AvgSessionMin7d.ToString("0.0", CultureInfo.InvariantCulture) + " 分钟 · " + N(o.Sessions7d) + " 次", Theme.ColText);
                KeyValue(g, x, ref y, labelW, lineH, "累计时长", o.TotalHours.ToString("#,0.0", CultureInfo.InvariantCulture) + " 小时", Theme.ColText);
                Text_(g, "按版本分布（今天）", fUiReg, Theme.ColMuted, x, y); y += lineH;
                long max = 1;
                foreach (VersionCount v in o.ByVersion) max = Math.Max(max, v.Count);
                int shown = 0;
                foreach (VersionCount v in o.ByVersion)
                {
                    if (shown++ >= 6) { Text_(g, "  …", fUiReg, Theme.ColMuted, x, y); y += lineH; break; }
                    Bar(g, x, ref y, labelW, right, lineH, "  " + v.Version, v.Count, max, Theme.ColAccent, s);
                }
            }
            y += lineH / 3;
            KeyValue(g, x, ref y, labelW, lineH, "上次刷新", m.FetchedLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Theme.ColMuted);
            ContentHeight = y + 2 * pad;
        }

        private void Text_(Graphics g, string t, Font f, Color c, int x, int y)
        {
            TextRenderer.DrawText(g, t, f, new Point(x, y), c, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private void KeyValue(Graphics g, int x, ref int y, int labelW, int lineH, string k, string v, Color vc)
        {
            int lw = Math.Max(labelW, TextRenderer.MeasureText(g, k, fUiReg, Size.Empty, TextFormatFlags.NoPadding).Width + lineH);
            Text_(g, k, Pick(k, fMono), Theme.ColMuted, x, y);
            Text_(g, v, Pick(v, fMonoBold), vc, x + lw, y);
            y += lineH;
        }

        private void Bar(Graphics g, int x, ref int y, int labelW, int right, int lineH, string label, long n, long max, Color c, float s)
        {
            Text_(g, label, Pick(label, fMono), Theme.ColText, x, y);
            string num = N(n);
            int numW = TextRenderer.MeasureText(g, "0000000", fMono, Size.Empty, TextFormatFlags.NoPadding).Width;
            int bx = x + labelW, bw = Math.Max(10, right - bx - numW - (int)(12 * s));
            int bh = Math.Max(2, lineH / 2), by = y + (lineH - bh) / 2 - (int)(2 * s);
            using (SolidBrush gb = new SolidBrush(Theme.ColGrid)) g.FillRectangle(gb, bx, by, bw, bh);
            int fill = (int)Math.Round(bw * (double)n / Math.Max(1, max));
            if (n > 0) fill = Math.Max(fill, (int)(2 * s));
            using (SolidBrush fb = new SolidBrush(c)) g.FillRectangle(fb, bx, by, fill, bh);
            Text_(g, num, fMono, c, bx + bw + (int)(12 * s), y);
            y += lineH;
        }

        /// <summary>v5.1: any non-ASCII character (Chinese, ←, ·) -> the UI font; the mono font is for digits / versions only.</summary>
        private Font Pick(string t, Font mono)
        {
            foreach (char c in t ?? "") if (c > 0x7e) return mono == fMonoBold ? fUi : fUiReg;
            return mono;
        }

        /// <summary>Plain Chinese for a failed read (the technical reason stays in the snapshot for the log).</summary>
        private static string Why(string err) { return err == "offline" ? "暂时读不到（没联网）" : "暂时读不到"; }

        /// <summary>Height (px) the last paint needed; a scrolling host page sizes the panel to this (phase 2).</summary>
        public int ContentHeight { get; private set; }

        private static string N(long n) { return n.ToString("#,0", CultureInfo.InvariantCulture); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); DisposeFonts(); }
            base.Dispose(disposing);
        }
    }
}
