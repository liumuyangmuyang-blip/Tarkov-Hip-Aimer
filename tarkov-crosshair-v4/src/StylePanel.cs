// v4.0 外观 / 准星样式 page content: a grid of style tiles (thumbnails), a big live preview and only the settings that
// matter for the chosen style. Hosted by SettingsForm: new StylePanel(g, onChanged); the UI calls RefreshValues()
// when the active profile changes and sets Advanced from 「高级设置」. Every style keeps the centre empty (RingRaster
// cuts a hole of 中心留空 ≥ 3 px).
//
// Layout (v4.0 STYLE-POLISH): ONE scroll area (this control). Inside it a single page whose children are placed by
// DoLayout: header across the top, then two columns (tiles left, preview + settings right) when the page is wide
// enough, else one column (preview, settings, tiles). Tiles wrap to the column width (never a horizontal bar). In two
// columns the preview + settings column stays in view while the tiles scroll, if it fits the window height.
// v4.3.1: ONE fixed text size (body 11 pt, labels 10.5 pt, numbers 11 pt, titles 15 / 12 pt); Windows display scaling
// still applies. A window resize only re-lays out.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    public sealed class StylePanel : UserControl, IPausable
    {
        private readonly GlobalSettings g;
        private readonly Action onChanged;
        private readonly float k;    // system DPI scale (1 = 100 %)
        private bool loading;
        private bool disposedAll;
        // v4.1 drag freeze (IPausable): no thumbnail render while the window is moved / resized
        private bool paused, thumbsPending;

        public void Pause()
        {
            if (paused || disposedAll) return;
            paused = true;
            if (thumbTimer.Enabled) { thumbTimer.Stop(); thumbsPending = true; }
        }

        public void Resume()
        {
            if (!paused) return;
            paused = false;
            if (disposedAll) return;
            if (thumbsPending) { thumbsPending = false; thumbTimer.Start(); }
        }

        // font roles: one fixed point size each (v4.3.1; Windows DPI scaling applies on top)
        private const int RTitle = 0, RHead = 1, RSmall = 2, RBody = 3, RMono = 4;
        private static readonly float[] BasePt = new float[] { 15f, 12f, 10.5f, 11f, 11f };
        private static readonly bool[] RoleUi = new bool[] { true, true, true, true, false };
        private static readonly FontStyle[] RoleStyle = new FontStyle[] { FontStyle.Bold, FontStyle.Bold, FontStyle.Regular, FontStyle.Regular, FontStyle.Regular };
        private Font[] fonts = new Font[5];
        private readonly List<KeyValuePair<Control, int>> fontUsers = new List<KeyValuePair<Control, int>>();
        private readonly List<KeyValuePair<NumericUpDown, string>> nums = new List<KeyValuePair<NumericUpDown, string>>();
        private readonly List<Control> sliders = new List<Control>();
        private readonly List<Control> swatches = new List<Control>();
        private readonly List<FoldHeader> folds = new List<FoldHeader>();
        private readonly List<Label> wraps = new List<Label>(); // labels that wrap to the settings column width
        /// <summary>v4.1 (Leo 17 Nov, aligned UI): first-column labels and unit labels of every value grid. ApplySizes gives
        /// all of them one width (and every number box one width), so the sliders and boxes of every group line up.</summary>
        private readonly List<Label> gridLabels = new List<Label>(), unitLabels = new List<Label>();

        private readonly ToolTip tips = new ToolTip();
        private readonly List<Action> refreshers = new List<Action>();     // persistent controls
        private readonly List<Action> paramRefreshers = new List<Action>(); // the per-style rows
        private readonly List<TileButton> tiles = new List<TileButton>();
        private readonly Dictionary<string, Geo> tunedNormal = new Dictionary<string, Geo>(); // per-style values this session
        private readonly Dictionary<string, Geo> tunedBoost = new Dictionary<string, Geo>();  // the same for the 强化 look
        private Dictionary<string, Geo> tuned { get { return editBoost ? tunedBoost : tunedNormal; } }
        private readonly Timer thumbTimer = new Timer();

        private PagePanel page;
        private Label lblHead, lblSub;
        private readonly List<Label> groupLabels = new List<Label>();
        private readonly List<int> groupOfLabel = new List<int>();
        private PreviewBox preview;
        private Label lblTitle, lblHint, lblStatus, lblColorHead;
        private TableLayoutPanel stack;
        private TableLayoutPanel paramGrid;
        private TableLayoutPanel colorGrid;
        private Control[] rowOutline, rowColor2, rowColor, rowMode, rowOpacity;
        private Label lblColor2;
        // v5.0.2 (Leo, decision 4): 强化准星 has every 外观 option of the normal crosshair. editBoost = the whole page (tiles,
        // preview, size, colour, outline, glow, ...) edits boostEdit, a Profile holding the 强化 look (Profile.FillBoostView);
        // every change is written back to the active profile's Boost (SyncBoost, Profile.StoreBoostView).
        private bool editBoost;
        private Profile boostEdit, boostEditOf;
        private Button btnBoostEdit, btnBoostCopy;
        private Label lblBoostNote;
        private readonly List<Control> handSections = new List<Control>(); // 换手颜色: the normal look only
        private string shownTile; // tile whose rows paramGrid shows ("" = none)
        private readonly List<Control[]> advParamRows = new List<Control[]>(); // per-style rows shown only with 高级设置
        private readonly List<Control> advSections = new List<Control>();
        private bool advanced;

        // layout state
        private bool twoCol, inLayout;
        private Rectangle rightBox;  // natural place of preview + settings (page coordinates, before pinning)
        private int tileSide = -1;

        /// <summary>「高级设置」 (set by the settings window). Off = style tiles, preview, size, colour and opacity only;
        /// on = every fine-tuning value (per-style geometry, 中心留空, 描边, 发光 · 羽化 · 旋转, 换手颜色, 强化准星).</summary>
        public bool Advanced
        {
            get { return advanced; }
            set
            {
                if (advanced == value) return;
                advanced = value;
                if (disposedAll) return;
                if (!value && editBoost) SetEditBoost(false); // the way back lives in the 强化 section (高级设置 only)
                BeginHold(); // v5.1.1 (P1): one layout for the whole toggle
                try
                {
                    foreach (FoldHeader h in folds) h.Open = value; // 高级设置 on = the 可选 sections open (like the other pages)
                    ApplyVisibility();
                }
                finally { EndHold(); } // = Relayout
            }
        }

        /// <summary>The look the page edits: the active profile, or its 强化 look while editBoost (v5.0.2).</summary>
        private Profile Cur { get { return editBoost && boostEdit != null ? boostEdit : g.Active; } }

        public StylePanel(GlobalSettings g, Action onChanged)
        {
            this.g = g;
            // v5.0.2: the 强化 editor's values reach the profile before anyone reads it
            this.onChanged = onChanged == null ? (Action)SyncBoost : (Action)delegate { SyncBoost(); onChanged(); };
            k = DpiScale();
            CustomCrosshair.Install();
            SuspendLayout();
            DoubleBuffered = true;
            BackColor = Theme.ColSurface;
            ForeColor = Theme.ColText;
            Dock = DockStyle.Fill;
            Padding = new Padding(0);
            AutoScroll = true;
            MakeFonts();
            Font = fonts[RBody];
            tips.AutoPopDelay = 15000;
            tips.InitialDelay = 300;
            thumbTimer.Interval = 150;
            thumbTimer.Tick += delegate { thumbTimer.Stop(); if (thumbNext >= 0) RenderThumbSlice(); else RenderThumbs(); };

            page = new PagePanel();
            page.Dock = DockStyle.Top;
            page.Margin = new Padding(0);
            page.Padding = new Padding(0);
            page.BackColor = Theme.ColSurface;
            page.Height = S(600);
            page.DoLayoutNow = DoLayout;
            page.LocationChanged += delegate { PinRight(); };
            Controls.Add(page);

            // v5.1.1 (P1 perf): only 准星样式 (the sub-page shown first) is built here; 准星动态 / 弹药提示 are built on their
            // first switch (EnsureSubPage) or earlier in idle slices (PrebuildNextSubPage). Layout is held for the whole build.
            BeginHold();
            try
            {
                BuildHeader();
                BuildTiles();
                BuildRight();
                BuildSubPages();
                ApplySizes();
                RefreshValues();
            }
            finally
            {
                ResumeLayout(false);
                EndHold();
            }
            try { Program.Log("UI: 外观 built: " + DoLayoutCount + " page layouts, " + layoutBoxes.Count + " grids (准星动态 / 弹药提示 built on first open)"); } catch (Exception) { }
        }

        // ================================================================ helpers

        private static float DpiScale()
        {
            try { using (Graphics gr = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, gr.DpiX / 96f); }
            catch (Exception) { return 1f; }
        }

        /// <summary>System-DPI pixels (fixed sizes).</summary>
        private int S(double v) { return (int)Math.Round(v * k); }
        /// <summary>Things sized from the text (v4.3.1: fixed text size, so the DPI scale only).</summary>
        private int U(double v) { return (int)Math.Round(v * k); }
        private Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        private static Font SafeFont(bool isUi, float pt, FontStyle st)
        {
            try { return isUi ? Theme.Ui(pt, st) : Theme.Mono(pt, st); }
            catch (Exception) { return new Font(FontFamily.GenericSansSerif, pt, st); }
        }

        /// <summary>Creates the five fonts once (v4.3.1: one fixed size per role).</summary>
        private void MakeFonts()
        {
            for (int i = 0; i < 5; i++) fonts[i] = SafeFont(RoleUi[i], BasePt[i], RoleStyle[i]);
        }

        /// <summary>v4.3.1 test hook: the body font's point size (constant for every window size).</summary>
        internal float BodyPt { get { return fonts[RBody] != null ? fonts[RBody].SizeInPoints : 0f; } }

        private Font FontOf(int role) { return fonts[role]; }

        private void UseFont(Control c, int role)
        {
            c.Font = fonts[role];
            fontUsers.Add(new KeyValuePair<Control, int>(c, role));
        }

        private Label Lbl(string text, int role, Color c)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            if (role >= 0) UseFont(l, role);
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
            l.Margin = P(3, 4, 3, 2);
            l.Anchor = AnchorStyles.Left;
            l.Tag = "theme:keep";
            return l;
        }

        private TableLayoutPanel Grid(int cols)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = cols;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Dock = DockStyle.Top;
            t.Margin = new Padding(0);
            t.Padding = P(4, 2, 4, 2);
            t.BackColor = Color.Transparent;
            Hold(t);
            return t;
        }

        // ---- v5.1.1 (P1 perf, Leo's PC: 外观 built in 6674 ms + 1332 ms fill): every grid / row container this panel builds
        // is registered here. While a build or a refresh runs (BeginHold .. EndHold) all of them and the page have their
        // layout suspended: adding a row or showing / hiding one no longer re-lays the whole grid (and its AutoSize parents)
        // each time. EndHold resumes them inner-first (each container lays out once, with all its rows), then one page layout.
        private readonly List<Control> layoutBoxes = new List<Control>();
        private int holdDepth;

        private void Hold(Control c)
        {
            if (c == null) return;
            layoutBoxes.Add(c);
            if (holdDepth > 0) c.SuspendLayout();
        }

        private void BeginHold()
        {
            if (holdDepth++ > 0) return;
            layoutBoxes.RemoveAll(delegate(Control c) { return c.IsDisposed; });
            if (page != null) page.SuspendLayout();
            foreach (Control c in layoutBoxes) c.SuspendLayout();
        }

        private void EndHold() { EndHold(true); }

        /// <summary>relayout false: the grids resume, no page layout (a hidden sub-page built in an idle slice).</summary>
        private void EndHold(bool relayout)
        {
            if (holdDepth <= 0) return;
            if (holdDepth > 1) { holdDepth--; return; }
            // still held (holdDepth 1) while the grids resume, so their Layout events do not each re-lay the page
            for (int i = layoutBoxes.Count - 1; i >= 0; i--)
            {
                try { if (!layoutBoxes[i].IsDisposed) layoutBoxes[i].ResumeLayout(true); } catch (Exception) { }
            }
            holdDepth = 0;
            if (page != null) page.ResumeLayout(false);
            if (relayout) Relayout();
        }

        private void Changed()
        {
            if (loading) return;
            if (onChanged != null) { try { onChanged(); } catch (Exception) { } }
            if (preview != null) preview.Invalidate();
            if (subTab == 0) ScheduleThumbs(); // v5.1.1 (P1): 准星动态 / 弹药提示 values do not change the tile pictures
        }

        private void ScheduleThumbs()
        {
            thumbTimer.Stop();
            thumbNext = -1; // a new render starts over (after the delay)
            thumbTimer.Interval = 150;
            if (paused) { thumbsPending = true; return; }
            thumbTimer.Start();
        }

        private static string Num(double v, int dec)
        {
            return v.ToString(dec <= 0 ? "0" : "0." + new string('0', dec), CultureInfo.InvariantCulture);
        }

        /// <summary>Widest text a number box must show: "-888.8" style, digits of the largest magnitude.</summary>
        private static string WideNum(double min, double max, int dec)
        {
            int digits = ((long)Math.Max(Math.Abs(min), Math.Abs(max))).ToString(CultureInfo.InvariantCulture).Length;
            return (min < 0 ? "-" : "") + new string('8', Math.Max(1, digits)) + (dec > 0 ? "." + new string('8', dec) : "");
        }

        /// <summary>Sizes that follow the fonts: number boxes, sliders, colour buttons, fold headers.</summary>
        private void ApplySizes()
        {
            nums.RemoveAll(delegate(KeyValuePair<NumericUpDown, string> p) { return p.Key.IsDisposed; });
            sliders.RemoveAll(delegate(Control c) { return c.IsDisposed; });
            swatches.RemoveAll(delegate(Control c) { return c.IsDisposed; });
            Font mono = fonts[RMono], body = fonts[RBody];
            // Leo 17 Nov (aligned UI): every number box as wide as the widest one needs, so the box column (and the slider
            // column before it) has one x in every group
            int numW = 0;
            foreach (KeyValuePair<NumericUpDown, string> p in nums)
                numW = Math.Max(numW, TextWidthCache.Width(p.Value, mono)); // v5.1.1 (P1): measured once per text
            numW += SystemInformation.VerticalScrollBarWidth + U(14);
            // v5.1.1 (P1, measured at 2560x1440 / 180 %): the settings window's text fit (TextFit.NumberBoxWidth, real font
            // metrics) wanted 107-114 px where this gave 102, so every ApplySizes shrank 7 boxes back (clipped digits, and a
            // full grid layout each time, 60-120 ms per call). The column width is now the larger of the two, so both agree.
            foreach (KeyValuePair<NumericUpDown, string> p in nums) numW = Math.Max(numW, FitBoxWidth(p.Key));
            foreach (KeyValuePair<NumericUpDown, string> p in nums)
                if (p.Key.Width != numW) p.Key.Width = numW;
            gridLabels.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            unitLabels.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            SameWidth(gridLabels);
            SameWidth(unitLabels);
            int sh = Math.Max(S(22), body.Height + S(4));
            Size smin = new Size(U(90), sh);
            foreach (Control c in sliders) { if (c.MinimumSize != smin) c.MinimumSize = smin; if (c.Height != sh) c.Height = sh; }
            Size swz = new Size(TextWidthCache.Width("#888888", mono) + U(60), Math.Max(S(24), mono.Height + S(8)));
            foreach (Control c in swatches) if (c.Size != swz) c.Size = swz;
            foreach (FoldHeader h in folds)
            {
                if (h.Font != body) h.Font = body;
                int hh = h.GetPreferredSize(Size.Empty).Height;
                Size hm = new Size(0, hh);
                if (h.MinimumSize != hm) h.MinimumSize = hm;
                if (h.Height != hh) h.Height = hh;
            }
        }

        private readonly Dictionary<string, int> boxFit = new Dictionary<string, int>();

        /// <summary>TextFit.NumberBoxWidth of a number box, measured once per range / font / scale.</summary>
        private int FitBoxWidth(NumericUpDown n)
        {
            try
            {
                double sc = TextFit.ScaleOf(n);
                string key = n.Minimum + "|" + n.Maximum + "|" + n.DecimalPlaces + "|" + n.Font.Name + "|" + n.Font.Size + "|" + sc;
                int w;
                if (!boxFit.TryGetValue(key, out w)) { w = TextFit.NumberBoxWidth(n); boxFit[key] = w; }
                return w;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>Gives every label of the list the width of the widest (hidden rows included, so nothing jumps).</summary>
        private static void SameWidth(List<Label> labels)
        {
            int w = 0;
            foreach (Label l in labels) w = Math.Max(w, TextWidthCache.Width(l.Text ?? "", l.Font) + l.Padding.Horizontal); // v5.1.1 (P1): cached
            Size want = new Size(w, 0);
            foreach (Label l in labels) if (l.MinimumSize != want) l.MinimumSize = want;
        }

        // ================================================================ layout

        private void Relayout()
        {
            if (holdDepth > 0) return; // v5.1.1 (P1, R1 #1): building / refreshing: EndHold lays the page out once
            if (page != null && !disposedAll) page.PerformLayout();
        }

        /// <summary>v5.1.1 (P1): full page layouts so far (logged once after construction: Leo's log shows the count).</summary>
        internal int DoLayoutCount;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PinRight();
        }

        /// <summary>Places every child of the page for its current width and sets the page height.</summary>
        private void DoLayout()
        {
            if (inLayout || disposedAll || page == null || lblHead == null || preview == null || stack == null || holdDepth > 0) return;
            DoLayoutCount++;
            inLayout = true;
            try
            {
                int W = page.ClientSize.Width;
                if (W <= 0) return;
                // Leo 17 Nov (aligned UI): the content edges of every other settings page (SettingsForm.FitWidths): from the
                // page's left padding to min(the page minus the scroll-bar room, the 840 logical px 「内容宽度」)
                int padL = 0, gap = U(18);
                int inner = InnerWidth(W);
                twoCol = inner >= S(600);
                int rightW = twoCol ? Math.Max(S(280), Math.Min(S(640), (int)(inner * 0.44))) : inner;
                int leftW = twoCol ? inner - rightW - gap : inner;
                int x0 = padL, y = S(2);
                int pvPin = PinPreviewH();
                tabsY = y;
                if (subTabs != null)
                {
                    subTabs.Bounds = new Rectangle(x0, y, Math.Min(inner, 3 * (subTabs.TabW + S(4))), subTabs.PreferredH);
                    y = subTabs.Bottom + U(6);
                    if (subTab != 0)
                    {
                        // v5.1.1 (X7): pinned header = tabs + this sub-page's live preview and its buttons
                        TableLayoutPanel pin = subTab == 1 ? animPin : ammoPin;
                        pinY = y;
                        if (pin != null)
                        {
                            if (pin.Controls.Count > 0 && pin.Controls[0].Height != pvPin) pin.Controls[0].Height = pvPin;
                            y = LayoutSub(pin, x0, y, inner) + U(8);
                        }
                        pinH = y;
                        int bot = LayoutSub(subTab == 1 ? subAnim : subAmmo, x0, y, inner);
                        int ph = bot + S(12);
                        if (page.Height != ph) page.Height = ph;
                        PlaceRight();
                        return;
                    }
                }
                // v5.1.1 (X7): 准星样式 header = tabs + the big preview across the content width (capped height)
                pinY = y;
                preview.Bounds = new Rectangle(x0, y, inner, pvPin);
                y = preview.Bottom + U(8);
                pinH = y;

                // header across the whole width
                lblHead.Location = new Point(x0, y);
                lblHead.Size = lblHead.GetPreferredSize(Size.Empty);
                y = lblHead.Bottom + S(2);
                // v5.1 (W30 text fit): the height of the wrapped line from real font metrics + margin (YaHei is taller)
                // v5.1.1 (P1, R1 #5): measured again only when its text or the width changes
                string subKey = (lblSub.Text ?? "") + "|" + inner;
                if (subKey != lblSubKey)
                {
                    lblSubKey = subKey;
                    lblSubH = Math.Max(WrappedHeight(lblSub, inner), TextFit.NeedSize(lblSub.Text ?? "", lblSub.Font, inner, TextFit.ScaleOf(lblSub)).Height);
                }
                lblSub.Bounds = new Rectangle(x0, y, inner, lblSubH);
                y = lblSub.Bottom + U(8);
                int top = y;

                int rx = twoCol ? x0 + leftW + gap : x0;
                int stackH = LayoutStack(rightW);
                int rightH = stackH; // v5.1.1 (X7): the preview is in the pinned header, not above the settings

                int tilesTop, tilesBottom;
                if (twoCol)
                {
                    tilesTop = top;
                    tilesBottom = LayoutTiles(x0, tilesTop, leftW);
                    rightBox = new Rectangle(rx, top, rightW, rightH);
                }
                else
                {
                    rightBox = new Rectangle(rx, top, rightW, rightH);
                    tilesTop = top + rightH + U(10);
                    tilesBottom = LayoutTiles(x0, tilesTop, leftW);
                }
                stack.Size = new Size(rightW, stackH);
                int h = Math.Max(tilesBottom, rightBox.Bottom) + S(12);
                if (page.Height != h) page.Height = h;
                PlaceRight();
            }
            finally { inLayout = false; }
        }

        /// <summary>Content width for a page width W: the other settings pages' edges (SettingsForm.FitWidths), from the
        /// page's left edge to min(the page minus the scroll-bar room, the 840 logical px 「内容宽度」).</summary>
        private string lblSubKey;
        private int lblSubH;

        private int InnerWidth(int W)
        {
            int edge = Math.Min(Math.Min(W, Width - SystemInformation.VerticalScrollBarWidth) - S(16), U(840));
            return Math.Max(S(200), edge);
        }

        /// <summary>Height of a word-wrapped label at width w (fixed-size label: wraps on every runtime, never clipped).</summary>
        private static int WrappedHeight(Label l, int w)
        {
            Size sz = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(Math.Max(1, w), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return sz.Height + 2;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? Math.Max(lo, hi) : v); }

        /// <summary>Sizes the settings stack for a width and returns its height.</summary>
        private int LayoutStack(int w)
        {
            Size wrap = new Size(Math.Max(S(160), w - S(16)), 0);
            wraps.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            foreach (Label l in wraps) if (l.MaximumSize != wrap) l.MaximumSize = wrap; // v5.1.1 (P1): only on a change
            int hp = stack.GetPreferredSize(new Size(w, 0)).Height;
            stack.Size = new Size(w, Math.Max(S(20), hp));
            stack.PerformLayout();
            int bottom = 0;
            foreach (Control c in stack.Controls) if (c.Visible) bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            return Math.Max(hp, bottom + stack.Padding.Bottom);
        }

        /// <summary>Group labels + tiles, wrapped to width w. Tiles fill the row exactly. Returns the bottom.</summary>
        private int LayoutTiles(int x0, int y, int w)
        {
            int g2 = U(8);
            int cols = TileColumns(w, g2);
            int tw = (w - (cols - 1) * g2) / cols;
            int labelH = TileLabelHeight;
            int th = tw - U(14) + labelH;
            int side = Math.Max(16, tw - U(14));
            bool sideChanged = side != tileSide;
            tileSide = side;
            int gh = fonts[RHead].Height + U(6);
            for (int grp = 0; grp < groupLabels.Count; grp++)
            {
                Label gl = groupLabels[grp];
                y += grp == 0 ? 0 : U(12);
                gl.Bounds = new Rectangle(x0, y, w, gh);
                y += gh + U(4);
                int col = 0;
                foreach (TileButton b in tiles)
                {
                    if (b.Tile.Group != groupOfLabel[grp]) continue;
                    if (col == cols) { col = 0; y += th + g2; }
                    b.Bounds = new Rectangle(x0 + col * (tw + g2), y, tw, th);
                    col++;
                }
                if (col > 0) y += th;
            }
            if (sideChanged) ScheduleThumbs();
            return y;
        }

        /// <summary>Columns for the tile grid: tiles between ~96 and ~150 px (logical x text scale), preferring a count
        /// that leaves no half-empty last row in the groups that span several rows, then the one closest to ~112 px.</summary>
        private int TileColumns(int w, int g2)
        {
            if (widestTile < 0)
            {
                // v5.1.1 (P1, R1 #5): the tile labels and group sizes never change after the fonts are built: measured once
                widestTile = 0;
                foreach (TileButton b in tiles) widestTile = Math.Max(widestTile, TextWidthCache.Width(b.Tile.Label, fonts[RSmall]));
                groupCounts = new int[groupOfLabel.Count];
                for (int grp = 0; grp < groupOfLabel.Count; grp++)
                    foreach (TileButton b in tiles) if (b.Tile.Group == groupOfLabel[grp]) groupCounts[grp]++;
            }
            int widest = widestTile;
            int minW = Math.Max(U(80), widest + U(8)), maxW = Math.Max(minW, U(150)), target = Math.Max(minW, U(112));
            int best = -1; double bestScore = double.MaxValue;
            for (int c = 2; c <= 12; c++)
            {
                int tw = (w - (c - 1) * g2) / c;
                if (tw < minW || tw > maxW) continue;
                int waste = 0;
                for (int grp = 0; grp < groupCounts.Length; grp++)
                {
                    int n = groupCounts[grp];
                    if (n > c && n % c != 0) waste += c - n % c;
                }
                double score = waste * 1000.0 + Math.Abs(tw - target);
                if (score < bestScore) { bestScore = score; best = c; }
            }
            if (best > 0) return best;
            int fit = Math.Max(2, (w + g2) / (target + g2));
            while (fit > 2 && (w - (fit - 1) * g2) / fit < minW) fit--;
            while ((w - (fit - 1) * g2) / fit > maxW) fit++;
            return fit;
        }

        private int widestTile = -1;
        private int[] groupCounts;

        internal int TileLabelHeight { get { return fonts[RSmall].Height + U(10); } }

        /// <summary>Preview + settings at their natural place, or kept in view while the tiles scroll (two columns,
        /// only when they fit the visible height).</summary>
        private void PlaceRight()
        {
            if (preview == null || stack == null) return;
            if (subTab == 0)
            {
                Point sp = new Point(rightBox.X, rightBox.Y);
                if (stack.Location != sp) stack.Location = sp;
            }
            PlacePinned();
        }

        /// <summary>v5.1.1 (X7, Leo 21 Nov): keeps the header (tabs + the open sub-page's preview) at the top of the visible
        /// area while the page scrolls; the settings pass under the opaque backing.</summary>
        private void PlacePinned()
        {
            if (page == null || pinBack == null) return;
            int scrolled = Math.Max(0, Math.Min(-page.Top, Math.Max(0, page.Height - pinH))); // page.Top = AutoScrollPosition.Y (<= 0)
            Control pv = subTab == 0 ? (Control)preview : subTab == 1 ? (Control)animPin : ammoPin;
            Rectangle back = new Rectangle(0, scrolled, page.Width, pinH);
            if (pinBack.Bounds != back) pinBack.Bounds = back;
            if (subTabs != null && subTabs.Top != tabsY + scrolled) subTabs.Top = tabsY + scrolled;
            if (pv != null && pv.Top != pinY + scrolled) pv.Top = pinY + scrolled;
        }

        private void PinRight()
        {
            if (inLayout || disposedAll) return;
            inLayout = true;
            try { PlaceRight(); } finally { inLayout = false; }
        }

        /// <summary>v4.3 PERF (W7): set by the settings window; true while the wheel scrolls (animations / timers stop),
        /// false 150 ms after the last notch.</summary>
        public Action<bool> ScrollPause
        {
            set { Wheel.Pause = value; }
        }

        private WheelScroller wheel;
        private WheelScroller Wheel { get { if (wheel == null) wheel = new WheelScroller(ScrollUp); return wheel; } }

        /// <summary>v4.3 PERF (W7): the wheel over the page is coalesced (one scroll per frame, same distance as WinForms'
        /// own AutoScroll wheel: e.Delta px), and the sticky preview is moved once per scroll (was on every notch AND on the
        /// page's LocationChanged, so it was painted twice per notch).</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!VScroll || disposedAll) { base.OnMouseWheel(e); return; }
            HandledMouseEventArgs he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            Wheel.Queue(e.Delta);
        }

        /// <summary>One scroll: the page moves (LocationChanged does not re-pin meanwhile), then the preview + settings
        /// stack are placed once, in one layout batch.</summary>
        private void ScrollUp(int px)
        {
            if (disposedAll) return;
            Point cur = AutoScrollPosition; // negative
            bool was = inLayout;
            inLayout = true;
            SuspendLayout();
            try { AutoScrollPosition = new Point(0, WheelCoalescer.Target(-cur.Y, px)); }
            finally { inLayout = was; }
            try { PinRight(); }
            finally { ResumeLayout(false); }
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            PinRight();
        }

        /// <summary>The wheel over a number box / combo scrolls the page instead of changing the value.</summary>
        private void WheelScrollsPage(Control c)
        {
            c.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                ComboBox cb = s as ComboBox;
                if (cb != null && cb.DroppedDown) return;
                HandledMouseEventArgs he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
                ScrollBy(e.Delta);
            };
        }

        internal void ScrollBy(int delta)
        {
            int step = Math.Max(S(40), SystemInformation.MouseWheelScrollLines * Font.Height);
            if (disposedAll) return;
            Wheel.Queue(Math.Sign(delta) * step); // v4.3: coalesced like the page wheel
        }

        // ================================================================ header + tiles

        private void BuildHeader()
        {
            lblHead = Lbl("准星样式", RBody, Theme.ColAccent2); // v4.1 UI41-review #5: same look as the other pages' group titles
            lblHead.Margin = new Padding(0);
            page.Controls.Add(lblHead);
            lblSub = Lbl("中间一定留空，游戏的中心小白点始终看得见。点一下就换上，预览马上变。", RSmall, Theme.ColMuted);
            lblSub.Margin = new Padding(0);
            lblSub.AutoSize = false;
            page.Controls.Add(lblSub);
        }

        private void BuildTiles()
        {
            for (int grp = 0; grp < StyleShapes.GroupNames.Length; grp++)
            {
                bool any = false;
                foreach (StyleTile t in StyleShapes.Tiles) if (t.Group == grp) any = true;
                if (!any) continue;
                Label gl = Lbl(StyleShapes.GroupNames[grp], RBody, Theme.ColAccent2); // v4.1: group-title style (no big // heads)
                gl.AutoSize = false;
                gl.TextAlign = ContentAlignment.BottomLeft;
                gl.Margin = new Padding(0);
                gl.AutoEllipsis = true;
                page.Controls.Add(gl);
                groupLabels.Add(gl);
                groupOfLabel.Add(grp);
                foreach (StyleTile t in StyleShapes.Tiles)
                {
                    if (t.Group != grp) continue;
                    TileButton b = new TileButton(this, t);
                    b.Size = new Size(S(100), S(120));
                    b.Margin = new Padding(0);
                    tips.SetToolTip(b, t.Label + "\n" + t.Hint);
                    page.Controls.Add(b);
                    tiles.Add(b);
                }
            }
        }

        internal void OnTileClick(StyleTile t)
        {
            if (t == null) return;
            StyleTile now = StyleShapes.TileOf(Cur);
            if (now != null && now.Id == t.Id && t.Id != "custom") return;
            if (now != null) tuned[now.Id] = Geo.Read(Cur);
            ApplyTileKeepBoost(Cur, t.Id);
            Geo saved;
            if (tuned.TryGetValue(t.Id, out saved)) saved.Write(Cur);
            if (t.Id == "custom")
            {
                CrosshairStyle cs = StyleShapes.Of(Cur);
                if (string.IsNullOrEmpty(cs.PngFile) || StyleShapes.Png(cs.PngFile) == null)
                {
                    List<string> have = CustomCrosshair.List();
                    if (have.Count > 0) cs.PngFile = have[0];
                }
            }
            RefreshValues();
            if (onChanged != null) { try { onChanged(); } catch (Exception) { } }
            if (t.Id == "custom" && StyleShapes.Png(StyleShapes.Of(Cur).PngFile) == null) BeginInvokeSafe(Upload);
        }

        /// <summary>StyleShapes.ApplyTile, but a 强化 look of its own (Boost.OwnLook, v5.0.2) keeps its size: ApplyTile scales
        /// the 强化 diameter with the normal one (the v3.0 rule for a 强化 look that borrows the normal shape).</summary>
        private static void ApplyTileKeepBoost(Profile p, string id)
        {
            bool own = p != null && p.Boost != null && p.Boost.OwnLook;
            double keep = own ? p.Boost.Diameter : 0;
            StyleShapes.ApplyTile(p, id);
            if (own) p.Boost.Diameter = keep;
        }

        private void BeginInvokeSafe(Action a)
        {
            try { if (IsHandleCreated) BeginInvoke(a); else a(); } catch (Exception) { }
        }

        /// <summary>Thumbnail of a tile in the current colours, with the game's centre dot, fitted to side × side.</summary>
        internal Bitmap Thumb(StyleTile t, int side)
        {
            try
            {
                Profile c = StyleShapes.CloneWithStyle(Cur);
                StyleTile now = StyleShapes.TileOf(Cur);
                if (now == null || now.Id != t.Id)
                {
                    StyleShapes.ApplyTile(c, t.Id);
                    Geo saved;
                    if (tuned.TryGetValue(t.Id, out saved)) saved.Write(c);
                }
                if (t.Id == "custom" && StyleShapes.Png(StyleShapes.Of(c).PngFile) == null) return null;
                RingStyle st = RingStyle.From(c, 0, false);
                st.Feather = Math.Min(st.Feather, 1.0);
                int n0 = RingRaster.CanvasSize(st);
                double f = side * 0.80 / Math.Max(1, n0); // every style fills its tile about the same
                if (f > 8) f = 8;
                if (f < 0.25) f = 0.25;
                RingStyle big = StyleShapes.Scaled(st, f);
                int n = RingRaster.CanvasSize(big);
                int[] px = RingRaster.Render(big, n, n / 2.0, n / 2.0);
                // fit the drawn shape (not its padded canvas) to ~76 % of the tile, centred on the crosshair centre
                double reach = Reach(px, n);
                if (reach > 0.5)
                {
                    double f2 = Math.Max(0.25, Math.Min(12.0, f * side * 0.38 / reach));
                    if (Math.Abs(f2 / f - 1) > 0.08)
                    {
                        big = StyleShapes.Scaled(st, f2);
                        n = RingRaster.CanvasSize(big);
                        px = RingRaster.Render(big, n, n / 2.0, n / 2.0);
                    }
                }
                Bitmap bmp = new Bitmap(side, side, PixelFormat.Format32bppPArgb);
                using (Graphics gr = Graphics.FromImage(bmp))
                using (Bitmap src = CustomCrosshair.ToBitmap(px, n, n))
                {
                    gr.Clear(Color.Transparent);
                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    double reachNow = Reach(px, n) + 1;
                    float scale = reachNow * 2 > side ? (float)(side / (reachNow * 2 + 2)) : 1f; // only shrink when the shape itself is too big
                    gr.SetClip(new Rectangle(0, 0, side, side));
                    float w = n * scale;
                    gr.DrawImage(src, new RectangleF((side - w) / 2f, (side - w) / 2f, w, w));
                    float d = Math.Max(2f, k * 2f);
                    using (SolidBrush wb = new SolidBrush(Color.White)) gr.FillEllipse(wb, side / 2f - d / 2f, side / 2f - d / 2f, d, d);
                }
                return bmp;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Largest distance (px, Chebyshev) from the canvas centre to a visible pixel.</summary>
        private static double Reach(int[] px, int n)
        {
            double c = n / 2.0, r = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (((uint)px[y * n + x] >> 24) < 24) continue;
                    double d = Math.Max(Math.Abs(x + 0.5 - c), Math.Abs(y + 0.5 - c));
                    if (d > r) r = d;
                }
            return r;
        }

        private void RenderThumbs()
        {
            if (disposedAll) return;
            // v5.1.1 (P1 perf): the page prebuilt in the background (hidden) renders no thumbnail; they are drawn when it is shown
            if (!Visible || subTab != 0) { thumbsOnShow = true; return; }
            thumbsOnShow = false;
            thumbNext = 0;
            RenderThumbSlice();
        }
        private bool thumbsOnShow;
        /// <summary>v5.1.1 (P1, measured 34 ms at 1280x720 and 129 ms at 2560x1440 / 180 % for all 21 tiles in one go): the
        /// tiles are drawn a few at a time, at most ThumbSliceMs per timer tick, so the window never stalls on them.</summary>
        private int thumbNext = -1;
        internal const int ThumbSliceMs = 12;

        private void RenderThumbSlice()
        {
            if (disposedAll || thumbNext < 0) return;
            if (!Visible || subTab != 0) { thumbNext = -1; thumbsOnShow = true; return; }
            Stopwatch sw = Stopwatch.StartNew();
            while (thumbNext < tiles.Count)
            {
                TileButton b = tiles[thumbNext++];
                b.SetThumb(Thumb(b.Tile, b.ImageSide));
                if (sw.ElapsedMilliseconds >= ThumbSliceMs) break;
            }
            if (thumbNext >= tiles.Count) { thumbNext = -1; return; }
            thumbTimer.Interval = 1;
            if (paused) { thumbsPending = true; return; }
            thumbTimer.Start();
        }

        // ================================================================ right: preview + settings

        private void BuildRight()
        {
            preview = new PreviewBox(this);
            preview.Height = S(240);
            preview.Margin = new Padding(0);
            page.Controls.Add(preview);

            stack = new TableLayoutPanel();
            stack.ColumnCount = 1;
            stack.AutoSize = false;
            stack.Margin = new Padding(0);
            stack.Padding = new Padding(0);
            stack.BackColor = Color.Transparent;
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Hold(stack);

            lblTitle = Lbl("", RHead, Theme.ColAccent);
            lblTitle.Margin = P(4, 2, 4, 0);
            stack.Controls.Add(lblTitle);
            lblHint = Lbl("", RSmall, Theme.ColMuted);
            lblHint.Margin = P(4, 0, 4, 4);
            wraps.Add(lblHint);
            stack.Controls.Add(lblHint);

            paramGrid = Grid(4);
            ParamColumns(paramGrid);
            stack.Controls.Add(paramGrid);
            lblStatus = Lbl("", RSmall, Theme.ColWarn);
            lblStatus.Visible = false;
            wraps.Add(lblStatus);
            stack.Controls.Add(lblStatus);

            stack.Controls.Add(BuildColorSection());
            Section hand = new Section(this, "换手颜色", BuildHand(), false);
            handSections.Add(hand);
            Control[] adv = new Control[] {
                new Section(this, "发光 · 羽化 · 旋转", BuildAdvanced(), false),
                hand,
                new Section(this, "强化准星（按强化键后）", BuildBoost(), false) };
            foreach (Control c in adv) { c.Visible = advanced; advSections.Add(c); stack.Controls.Add(c); }
            // v5.1.1 (X3, Leo B1): 准星动态 and 弹药提示 are their own sub-pages of 外观 (SubTabs at the top), not sections here
            stack.Layout += delegate { if (!inLayout) Relayout(); };
            page.Controls.Add(stack);
        }

        // ---- v5.1.1 (X3, Leo B1): 外观 sub-pages ----
        private SubTabs subTabs;
        private TableLayoutPanel subAnim, subAmmo;
        /// <summary>v5.1.1 (X7): holds the 弹药提示 「先设置」 rows the settings window builds (SetAmmoSetup).</summary>
        private TableLayoutPanel ammoSetupHost;
        private int subTab;

        // ---- v5.1.1 (X7, Leo 21 Nov): the live preview stays in view. The sub-tabs and the preview of the open sub-page
        // (准星样式: PreviewBox, also for the 强化 look / 换手颜色; 准星动态 / 弹药提示: FxLivePreview + its preview buttons)
        // form a header pinned at the top of the visible area; only the settings below scroll (they pass under pinBack).
        private TableLayoutPanel animPin, ammoPin;
        private Panel pinBack;
        private int tabsY, pinY, pinH; // natural places in page coordinates (scroll position 0)

        /// <summary>Preview + its buttons as one pinned block (not part of the scrolling settings grid).</summary>
        private TableLayoutPanel PinBox(Control live, Control buttons)
        {
            TableLayoutPanel b = Grid(1);
            b.Padding = new Padding(0);
            b.Dock = DockStyle.None;
            b.AutoSize = false;
            b.BackColor = Theme.ColSurface;
            live.Dock = DockStyle.Top;
            b.Controls.Add(live, 0, 0);
            b.Controls.Add(buttons, 0, 1);
            return b;
        }

        /// <summary>Preview height: sensible at 1280x720 / 200 % -- at most ~30 % of the visible height, 90..190 px (scaled).</summary>
        private int PinPreviewH()
        {
            int view = ClientSize.Height > 0 ? ClientSize.Height : S(600);
            return Clamp((int)(view * 0.30), S(90), S(190));
        }

        /// <summary>v5.1.1 (X7, Leo 21 Nov): the 弹药提示 setup (开关、弹匣容量、射速、换弹键、换弹时间 …) built by SettingsForm
        /// (BuildAmmoBox, the same settings fields) goes into the first group 「先设置」 of the 弹药提示 sub-page.</summary>
        public void SetAmmoSetup(Control setup)
        {
            if (setup == null || disposedAll) return;
            if (ammoSetupHost == null)
            {
                // v5.1.1 (P1): 弹药提示 is not built yet. The rows wait on the page (hidden), so the settings window still
                // fits their heights and text with the rest of the page; they move into 「先设置」 when it is built.
                pendingAmmoSetup = setup;
                setup.Visible = false;
                page.Controls.Add(setup);
                return;
            }
            if (setup.Parent == page) { page.Controls.Remove(setup); setup.Visible = true; }
            ammoSetupHost.Controls.Clear();
            setup.Dock = DockStyle.Top;
            ammoSetupHost.Controls.Add(setup, 0, 0);
        }
        private readonly List<Control> mainHidden = new List<Control>();

        /// <summary>The open sub-page: 0 准星样式, 1 准星动态, 2 弹药提示.</summary>
        public int SubPage { get { return subTab; } set { if (subTabs != null) subTabs.Pick(value); } }

        private void BuildSubPages()
        {
            subTabs = new SubTabs(this);
            subTabs.Picked += SetSubTab;
            page.Controls.Add(subTabs);
            pinBack = new Panel();
            pinBack.Tag = "theme:keep";
            pinBack.BackColor = Theme.ColSurface;
            pinBack.Margin = new Padding(0);
            page.Controls.Add(pinBack);
            PinOrder();
        }

        // ---- v5.1.1 (P1 perf): 准星动态 / 弹药提示 are built on demand ----
        private Control pendingAmmoSetup;
        /// <summary>Refresher ranges [start, end) of the two sub-pages (-1 = not built). RefreshValues runs a sub-page's
        /// refreshers only while it is the open sub-page (SetSubTab refreshes on every switch).</summary>
        private int animRef0 = -1, animRef1 = -1, ammoRef0 = -1, ammoRef1 = -1;
        /// <summary>Raised after a sub-page was built and added to the page (the settings window can fit combo heights / text
        /// of the new controls, like it does for every page it builds).</summary>
        public event Action<Control> SubPageBuilt;

        /// <summary>True when the sub-page (1 准星动态, 2 弹药提示) exists; 0 准星样式 always does.</summary>
        public bool IsSubPageBuilt(int i) { return i == 1 ? subAnim != null : i == 2 ? subAmmo != null : true; }

        /// <summary>One small step of building the sub-pages that are not open (准星动态, then 弹药提示 in parts, then their
        /// window handles a few at a time) for an idle slice of the settings window. Returns true while work is left.</summary>
        public bool PrebuildNextSubPage()
        {
            if (disposedAll) return false;
            if (finPending != null) { FinishValues(); return true; }
            if (preLayoutDue != null)
            {
                TableLayoutPanel pl = preLayoutDue;
                preLayoutDue = null;
                Stopwatch sw0 = Stopwatch.StartNew();
                if (pl != subAnim || subTab != 1) if (pl != subAmmo || subTab != 2) PreLayout(pl, pl == subAnim ? animPin : ammoPin);
                if (sw0.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw0.ElapsedMilliseconds; MaxSubStepWhat = "layout"; }
                return true;
            }
            if (subAnim == null) { BuildAnimIdle(); return true; }
            if (subAmmo == null) { AmmoSliceStep(); return true; }
            if (HandleStep()) return true;
            if (ammoPicker != null && !pickerWarm)
            {
                // the 5 preset pictures, drawn now instead of at the first paint of 弹药提示 (the first switch)
                pickerWarm = true;
                Stopwatch sw = Stopwatch.StartNew();
                ammoPicker.Warm();
                if (sw.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw.ElapsedMilliseconds; MaxSubStepWhat = "picker"; }
            }
            return false;
        }
        private AmmoPresetPicker ammoPicker;
        private bool pickerWarm, handlesWaiting;

        // ---- 弹药提示 built part by part (BuildAmmo's parts list) ----
        private bool ammoSliced;
        private List<Action> ammoParts;
        private int ammoPart;
        private TableLayoutPanel ammoT; // 弹药提示 grid while its parts are being built (not on the page yet)
        private readonly List<Label> animWraps = new List<Label>(), ammoWraps = new List<Label>();
        private List<Label> wrapTarget;
        /// <summary>Probe / log: longest single build step of the idle prebuild (ms).</summary>
        internal long MaxSubStepMs;
        internal string MaxSubStepWhat;

        private void AmmoSliceStep()
        {
            if (subAmmo != null) return;
            Stopwatch sw = Stopwatch.StartNew();
            if (ammoT == null)
            {
                ammoRef0 = refreshers.Count;
                ammoSliced = true;
                wrapTarget = ammoWraps;
                try { ammoT = (TableLayoutPanel)BuildAmmo(); }
                finally { ammoSliced = false; wrapTarget = null; }
                ammoT.SuspendLayout(); // kept until the last part: the parts add rows, the grid lays out once
                ammoPart = 0;
            }
            else if (ammoPart >= ammoParts.Count) { FinishAdd(2, ammoT, ammoRef0); return; } // its own step (FinishValues next)
            else
            {
                BeginHold();
                wrapTarget = ammoWraps;
                try { ammoParts[ammoPart++](); }
                finally { wrapTarget = null; EndHold(false); }
            }
            if (sw.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw.ElapsedMilliseconds; MaxSubStepWhat = "ammo part " + ammoPart; }
        }

        /// <summary>Builds sub-page i (1 准星动态, 2 弹药提示) if it does not exist yet. Hidden until SetSubTab shows it.</summary>
        public void EnsureSubPage(int i)
        {
            if (finPending != null && !disposedAll) FinishValues(); // an idle build stopped between its last two steps
            if (disposedAll || (i == 1 && subAnim != null) || (i == 2 && subAmmo != null) || (i != 1 && i != 2)) return;
            Stopwatch sw = Stopwatch.StartNew();
            if (i == 2 && ammoT != null)
            {
                // the idle build had started: the rest of its parts now
                BeginHold();
                wrapTarget = ammoWraps;
                try { while (ammoPart < ammoParts.Count) ammoParts[ammoPart++](); }
                finally { wrapTarget = null; EndHold(false); }
                FinishSub(2, ammoT, ammoRef0);
                return;
            }
            int r0 = refreshers.Count;
            TableLayoutPanel sp;
            BeginHold();
            try
            {
                if (i == 1)
                {
                    wrapTarget = animWraps;
                    try { subAnim = (TableLayoutPanel)BuildAnim(); }
                    finally { wrapTarget = null; }
                    sp = subAnim;
                    subAnim = null; // FinishSub sets it
                }
                else
                {
                    wrapTarget = ammoWraps;
                    try { subAmmo = (TableLayoutPanel)BuildAmmo(); }
                    finally { wrapTarget = null; }
                    sp = subAmmo;
                    subAmmo = null;
                }
            }
            finally { EndHold(false); }
            FinishSub(i, sp, r0);
        }

        /// <summary>Idle step: 准星动态 built (one step), joined to the page in the next ones.</summary>
        private void BuildAnimIdle()
        {
            Stopwatch sw = Stopwatch.StartNew();
            int r0 = refreshers.Count;
            TableLayoutPanel sp;
            BeginHold();
            try
            {
                wrapTarget = animWraps;
                try { sp = (TableLayoutPanel)BuildAnim(); }
                finally { wrapTarget = null; }
            }
            finally { EndHold(false); }
            FinishAdd(1, sp, r0);
            if (sw.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw.ElapsedMilliseconds; MaxSubStepWhat = "build 1"; }
        }

        /// <summary>A built sub-page joins the page (hidden) and gets its sizes and values (FinishAdd, then FinishValues).
        /// The idle build runs the two as separate steps and lays the hidden sub-page out in a third (PreLayout).</summary>
        private void FinishSub(int i, TableLayoutPanel sp, int r0)
        {
            FinishAdd(i, sp, r0);
            FinishValues();
        }

        private TableLayoutPanel finPending, preLayoutDue; // FinishValues / PreLayout still to run (idle build)
        private int finI;
        private Stopwatch finSw;

        private void FinishAdd(int i, TableLayoutPanel sp, int r0)
        {
            finSw = Stopwatch.StartNew();
            Stopwatch sw = Stopwatch.StartNew();
            BeginHold();
            try
            {
                if (i == 1) { subAnim = sp; animRef0 = r0; animRef1 = refreshers.Count; }
                else { subAmmo = sp; ammoRef0 = r0; ammoRef1 = refreshers.Count; }
                TableLayoutPanel pin = i == 1 ? animPin : ammoPin;
                if (sp == ammoT) { ammoT = null; ammoParts = null; sp.ResumeLayout(false); }
                sp.Dock = DockStyle.None;
                sp.AutoSize = false;
                sp.Visible = false;
                sp.Layout += delegate { if (!inLayout) Relayout(); };
                page.Controls.Add(sp);
                if (pin != null) { pin.Visible = false; page.Controls.Add(pin); }
                if (i == 2 && pendingAmmoSetup != null) { Control s = pendingAmmoSetup; pendingAmmoSetup = null; SetAmmoSetup(s); }
                PinOrder();
            }
            finally { EndHold(false); }
            finPending = sp;
            finI = i;
            if (sw.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw.ElapsedMilliseconds; MaxSubStepWhat = "add " + i; }
        }

        private void FinishValues()
        {
            TableLayoutPanel sp = finPending;
            if (sp == null || disposedAll) return;
            finPending = null;
            int i = finI;
            Stopwatch sw = Stopwatch.StartNew();
            BeginHold();
            try
            {
                ApplySizes();
                MatchCombos(sp);
                // the values now (the switch refreshes again; then only what changed meanwhile moves)
                int r0 = i == 1 ? animRef0 : ammoRef0, r1 = i == 1 ? animRef1 : ammoRef1;
                bool was = loading;
                loading = true;
                try { for (int k2 = r0; k2 < r1 && k2 < refreshers.Count; k2++) { try { refreshers[k2](); } catch (Exception) { } } }
                finally { loading = was; }
            }
            finally { EndHold(false); }
            if (subTab != i) preLayoutDue = sp;
            handlesDue = true;
            if (sw.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw.ElapsedMilliseconds; MaxSubStepWhat = "values " + i; }
            try { Program.Log("UI: 外观 sub-page " + i + " built: " + (finSw != null ? finSw.ElapsedMilliseconds : 0) + " ms since its parts were done"); } catch (Exception) { }
            Action<Control> h = SubPageBuilt;
            if (h != null) { try { h(sp); } catch (Exception) { } }
        }

        /// <summary>Lays a hidden sub-page out at the width it will be shown at (DoLayout does the same when it is shown).</summary>
        private void PreLayout(TableLayoutPanel sp, TableLayoutPanel pin)
        {
            if (disposedAll || page == null || page.ClientSize.Width <= 0 || inLayout) return;
            int inner = InnerWidth(page.ClientSize.Width);
            inLayout = true;
            try
            {
                if (pin != null)
                {
                    int pv = PinPreviewH();
                    if (pin.Controls.Count > 0 && pin.Controls[0].Height != pv) pin.Controls[0].Height = pv;
                    LayoutSub(pin, 0, 0, inner);
                }
                LayoutSub(sp, 0, 0, inner);
            }
            catch (Exception) { }
            finally { inLayout = false; }
        }

        // ---- window handles of the hidden sub-pages, a few per step (WinForms makes them at the first show otherwise)
        private bool handlesDue;
        private Queue<Control> handleQ;
        internal const int HandleStepMs = 12;

        private bool HandleStep()
        {
            if (!handlesDue && (handleQ == null || handleQ.Count == 0)) return false;
            if (!IsHandleCreated) { handlesWaiting = true; return false; } // queued again when the handle exists (OnHandleCreated)
            if (handleQ == null || handleQ.Count == 0)
            {
                handlesDue = false;
                handleQ = new Queue<Control>();
                foreach (Control c in new Control[] { animPin, subAnim, ammoPin, subAmmo }) if (c != null && !c.IsDisposed) handleQ.Enqueue(c);
            }
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                while (handleQ.Count > 0 && sw.ElapsedMilliseconds < HandleStepMs)
                {
                    Control c = handleQ.Dequeue();
                    if (c == null || c.IsDisposed) continue;
                    if (!c.IsHandleCreated) { IntPtr hw = c.Handle; GC.KeepAlive(hw); }
                    foreach (Control k2 in c.Controls) handleQ.Enqueue(k2);
                }
            }
            catch (Exception) { handleQ.Clear(); }
            if (sw.ElapsedMilliseconds > MaxSubStepMs) { MaxSubStepMs = sw.ElapsedMilliseconds; MaxSubStepWhat = "handles"; }
            return handleQ.Count > 0;
        }

        /// <summary>The settings window that hosts this panel queues the idle build itself (SettingsForm.QueueIdleSteps, page
        /// "none": never forced by a page switch; a switch to the sub-tab builds what is missing at once).</summary>
        private bool idleQueued;

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            QueuePrebuild();
        }

        private void QueuePrebuild()
        {
            if (idleQueued || disposedAll) return;
            SettingsForm f = FindForm() as SettingsForm;
            if (f == null) return;
            idleQueued = true;
            try { f.QueueIdleSteps(null, PrebuildNextSubPage); } catch (Exception) { idleQueued = false; }
        }

        /// <summary>Drop-downs of a sub-page built after the settings window fitted the page: the same height as the
        /// 准星样式 drop-downs (Theme.MatchComboHeight, what SettingsForm.FitComboHeights does for the rest).</summary>
        private void MatchCombos(Control root)
        {
            if (refCombo == null || refCombo.IsDisposed || root == null) return;
            int h = refCombo.Height;
            Stack<Control> todo = new Stack<Control>();
            todo.Push(root);
            while (todo.Count > 0)
            {
                Control c = todo.Pop();
                ComboBox cb = c as ComboBox;
                if (cb != null && cb != refCombo) Theme.MatchComboHeight(cb, h);
                foreach (Control ch in c.Controls) todo.Push(ch);
            }
        }
        private ComboBox refCombo;

        private bool SkipRefresher(int idx)
        {
            if (animRef0 >= 0 && idx >= animRef0 && idx < animRef1) return subTab != 1;
            if (ammoRef0 >= 0 && idx >= ammoRef0 && idx < ammoRef1) return subTab != 2;
            return false;
        }

        private void RunRefreshers()
        {
            for (int i = 0; i < refreshers.Count; i++)
            {
                if (SkipRefresher(i)) continue;
                try { refreshers[i](); } catch (Exception) { }
            }
        }

        /// <summary>Header above the scrolling settings: backing first, then the previews and the tabs on top.</summary>
        private void PinOrder()
        {
            if (pinBack == null) return;
            pinBack.BringToFront();
            if (preview != null) preview.BringToFront();
            if (animPin != null) animPin.BringToFront();
            if (ammoPin != null) ammoPin.BringToFront();
            if (subTabs != null) subTabs.BringToFront();
        }

        private bool IsPinned(Control c)
        {
            return c == subTabs || c == subAnim || c == subAmmo || c == animPin || c == ammoPin || c == pinBack || (c != null && c == pendingAmmoSetup);
        }

        private void SetSubTab(int i)
        {
            if (i == subTab || disposedAll) return;
            EnsureSubPage(i); // v5.1.1 (P1): built on the first switch to it
            BeginHold();
            bool wasMain = subTab == 0;
            subTab = i;
            SuspendLayout();
            try
            {
                if (wasMain)
                {
                    mainHidden.Clear();
                    foreach (Control c in page.Controls)
                    {
                        if (IsPinned(c)) continue;
                        if (!c.Visible) mainHidden.Add(c);
                        c.Visible = false;
                    }
                }
                else if (i == 0)
                {
                    foreach (Control c in page.Controls)
                        if (!IsPinned(c)) c.Visible = !mainHidden.Contains(c);
                }
                if (subAnim != null) subAnim.Visible = i == 1;
                if (subAmmo != null) subAmmo.Visible = i == 2;
                if (animPin != null) animPin.Visible = i == 1;
                if (ammoPin != null) ammoPin.Visible = i == 2;
            }
            finally { ResumeLayout(false); }
            AutoScrollPosition = new Point(0, 0);
            switchingTab = true; // a switch changes no value: the tile pictures stay (RenderThumbs on the way back if due)
            try { RefreshValues(); }
            finally { switchingTab = false; EndHold(); } // one page layout for the switch (BeginHold in the first lines)
            if (i == 0 && thumbsOnShow) BeginInvokeSafe(RenderThumbs);
        }

        /// <summary>Places one sub-page at (x, y), inner wide; returns its bottom.</summary>
        private int LayoutSub(TableLayoutPanel sp, int x, int y, int w)
        {
            Size wrap = new Size(Math.Max(S(160), w - S(16)), 0);
            // v5.1.1 (P1): only this sub-page's wrapping labels (each of them a layout of its grid when it changes)
            List<Label> own = sp == subAnim ? animWraps : sp == subAmmo || sp == ammoT ? ammoWraps : null;
            if (own != null) foreach (Label l in own) if (!l.IsDisposed && l.MaximumSize != wrap) l.MaximumSize = wrap;
            sp.Location = new Point(x, y);
            sp.Width = w;
            int hp = sp.GetPreferredSize(new Size(w, 0)).Height;
            sp.Size = new Size(w, Math.Max(S(20), hp));
            sp.PerformLayout();
            int bottom = 0;
            foreach (Control c in sp.Controls) if (c.Visible) bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            int h = Math.Max(hp, bottom + sp.Padding.Bottom);
            if (sp.Height != h) sp.Height = h;
            return sp.Bottom;
        }

        private void ParamColumns(TableLayoutPanel t)
        {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // label
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // slider
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // number
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // unit
        }

        /// <summary>label | slider | number | unit, bound to a model value. Returns the four controls.</summary>
        private Control[] AddValue(TableLayoutPanel t, List<Action> refs, string label, string unit, double min, double max,
            double inc, int dec, Func<double> get, Action<double> set, string tip)
        {
            Label l = Lbl(label, -1, Theme.ColText);
            HackSlider sl = new HackSlider(min, max, inc);
            sl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            sl.Margin = P(4, 3, 4, 3);
            sliders.Add(sl);
            NumericUpDown n = new NumericUpDown();
            n.Minimum = (decimal)min;
            n.Maximum = (decimal)max;
            n.Increment = (decimal)inc;
            n.DecimalPlaces = dec;
            n.TextAlign = HorizontalAlignment.Left; // same as the settings pages (right-aligned text is lost in narrow boxes)
            UseFont(n, RMono);
            n.Margin = P(2, 3, 2, 3);
            n.Anchor = AnchorStyles.Left;
            nums.Add(new KeyValuePair<NumericUpDown, string>(n, WideNum(min, max, dec)));
            WheelScrollsPage(n);
            Label u = Lbl(unit, RSmall, Theme.ColMuted);
            bool sync = false;
            sl.ValueChanged += delegate
            {
                if (loading || sync) return;
                sync = true;
                try { n.Value = Dec(sl.Value, n); } finally { sync = false; }
                set((double)n.Value);
                Changed();
            };
            n.ValueChanged += delegate
            {
                if (loading || sync) return;
                sync = true;
                try { sl.Value = (double)n.Value; } finally { sync = false; }
                set((double)n.Value);
                Changed();
            };
            // a typed value counts at once (not only when the box loses focus): clicking the left navigation or 退出
            // right after typing must not lose it. The box text itself is left alone while typing.
            n.TextChanged += delegate
            {
                if (loading || sync) return;
                decimal d;
                string s = (n.Text ?? "").Trim();
                if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out d) &&
                    !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return;
                if (d < n.Minimum || d > n.Maximum) return;
                d = Math.Round(d, n.DecimalPlaces);
                if ((double)d == get()) return;
                sync = true;
                try { sl.Value = (double)d; } finally { sync = false; }
                set((double)d);
                Changed();
            };
            refs.Add(delegate
            {
                double v = get();
                n.Value = Dec(v, n);
                sl.Value = (double)n.Value;
            });
            if (!string.IsNullOrEmpty(tip)) { tips.SetToolTip(l, tip); tips.SetToolTip(sl, tip); tips.SetToolTip(n, tip); }
            gridLabels.Add(l);
            unitLabels.Add(u);
            Put(t, l, sl, n, u);
            return new Control[] { l, sl, n, u };
        }

        /// <summary>Model double -> box value, clamped BEFORE the decimal cast (brute-ui F4; shared rule UiNum.ToDec).</summary>
        private static decimal Dec(double v, NumericUpDown n)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = (double)n.Minimum;
            return UiNum.ToDec(v, n.Minimum, n.Maximum, n.DecimalPlaces);
        }

        private void AddWide(TableLayoutPanel t, Control c)
        {
            int r = GridRows.Next(t);
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, Math.Max(1, t.ColumnCount));
        }

        /// <summary>v4.3.1: one grid row at FIXED cells (label, then its controls; the last one spans the rest). A flowed
        /// TableLayoutPanel skips hidden children, so hiding one row's cell used to slide later cells into the wrong rows.</summary>
        private static void Put(TableLayoutPanel t, params Control[] cs)
        {
            int r = GridRows.Next(t);
            for (int i = 0; i < cs.Length; i++) t.Controls.Add(cs[i], i, r);
            if (cs.Length > 0 && cs.Length < t.ColumnCount) t.SetColumnSpan(cs[cs.Length - 1], t.ColumnCount - cs.Length + 1);
        }

        private Control[] AddSwatch(TableLayoutPanel t, List<Action> refs, string label, Func<string> get, Action<string> set, string tip)
        {
            Label l = Lbl(label, -1, Theme.ColText);
            Swatch sw = new Swatch(this);
            sw.Size = new Size(S(150), S(26));
            sw.Margin = P(4, 3, 4, 3);
            sw.Anchor = AnchorStyles.Left;
            sw.Get = get;
            swatches.Add(sw);
            sw.Click += delegate
            {
                using (ColorDialog dlg = new ColorDialog())
                {
                    int r, gg, b;
                    dlg.FullOpen = true;
                    if (ColorUtil.TryParse(get(), out r, out gg, out b)) dlg.Color = Color.FromArgb(r, gg, b);
                    if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                    set(ColorUtil.Format(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                }
                sw.Invalidate();
                Changed();
            };
            refs.Add(delegate { sw.Invalidate(); });
            if (!string.IsNullOrEmpty(tip)) { tips.SetToolTip(l, tip); tips.SetToolTip(sw, tip); }
            gridLabels.Add(l);
            Put(t, l, sw); // sw spans the other 3 columns
            return new Control[] { l, sw };
        }

        private CheckBox AddCheck(TableLayoutPanel t, List<Action> refs, string text, Func<bool> get, Action<bool> set, string tip)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Margin = P(4, 4, 4, 4);
            c.Anchor = AnchorStyles.Left;
            c.CheckedChanged += delegate { if (loading) return; set(c.Checked); RefreshValues(); Changed(); };
            refs.Add(delegate { c.Checked = get(); });
            if (!string.IsNullOrEmpty(tip)) tips.SetToolTip(c, tip);
            AddWide(t, c);
            return c;
        }

        private static void Show(Control[] row, bool on)
        {
            if (row == null) return;
            foreach (Control c in row) if (c.Visible != on) c.Visible = on;
        }

        // ---------------- colour / outline / opacity (all styles) ----------------

        private static readonly string[] ColorModes = new string[] { "twotone", "none", "radial", "sweep" };
        private static readonly string[] ColorModeLabels = new string[] { "描边（推荐）", "单色", "由内到外渐变", "环绕渐变" };

        private Control BuildColorSection()
        {
            colorGrid = Grid(4);
            ParamColumns(colorGrid);
            lblColorHead = Lbl("颜色 · 透明度", RBody, Theme.ColAccent2); // v5.0.2 (UI-C L12): the other pages' group-title look
            lblColorHead.Margin = P(2, 12, 2, 4);
            AddWide(colorGrid, lblColorHead);
            rowColor = AddSwatch(colorGrid, refreshers, "颜色", delegate { return Cur.Color; }, delegate(string v) { Cur.Color = v; }, "准星的主颜色");

            Label lm = Lbl("效果", -1, Theme.ColText);
            gridLabels.Add(lm);
            ComboBox mode = new ComboBox();
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Items.AddRange(ColorModeLabels);
            mode.Margin = P(4, 3, 4, 3);
            mode.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            refCombo = mode; // v5.1.1 (P1): height reference for drop-downs of sub-pages built later (MatchCombos)
            WheelScrollsPage(mode);
            mode.SelectedIndexChanged += delegate
            {
                if (loading || mode.SelectedIndex < 0) return;
                Cur.Gradient = ColorModes[mode.SelectedIndex];
                RefreshValues();
                Changed();
            };
            refreshers.Add(delegate { int i = Array.IndexOf(ColorModes, Cur.Gradient); mode.SelectedIndex = i < 0 ? 1 : i; });
            tips.SetToolTip(mode, "描边＝外面包一圈第二颜色（默认黑色），亮的地方暗的地方都看得清");
            Put(colorGrid, lm, mode); // mode spans 3
            rowMode = new Control[] { lm, mode };

            rowColor2 = AddSwatch(colorGrid, refreshers, "描边颜色", delegate { return Cur.Color2; }, delegate(string v) { Cur.Color2 = v; }, null);
            lblColor2 = (Label)rowColor2[0];
            rowOutline = AddValue(colorGrid, refreshers, "描边粗细", "px", 0.5, 8, 0.1, 1,
                delegate { return Cur.OuterThickness; }, delegate(double v) { Cur.OuterThickness = v; }, "描边一圈有多宽");
            rowOpacity = AddValue(colorGrid, refreshers, "透明度", "%", 5, 100, 1, 0,
                delegate { return Cur.OpacityPct; }, delegate(double v) { Cur.OpacityPct = v; }, "100%＝完全不透明");
            refreshers.Add(delegate
            {
                bool png = IsCustom() && !StyleShapes.Of(Cur).PngTint;
                // v5.0.2: a 强化 look that follows the hand colours has no colours of its own to set here
                bool follow = editBoost && g.Active != null && g.Active.Boost != null && g.Active.Boost.FollowHandColor;
                Show(rowColor, !png && !follow);
                Show(rowMode, advanced && !png && !IsCustom());
                bool two = Cur.Gradient == "twotone", grad = Cur.Gradient == "radial" || Cur.Gradient == "sweep";
                Show(rowColor2, advanced && !IsCustom() && !follow && (two || grad));
                lblColor2.Text = two ? "描边颜色" : "第二颜色";
                Show(rowOutline, advanced && !IsCustom() && two);
                lblColorHead.Text = advanced ? "颜色 · 描边 · 透明度" : "颜色 · 透明度";
            });
            return colorGrid;
        }
        private bool IsCustom()
        {
            StyleTile t = StyleShapes.TileOf(Cur);
            return t != null && t.Id == "custom";
        }

        private Control BuildAdvanced()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            AddValue(t, refreshers, "发光", "px", 0, 40, 0.5, 1, delegate { return Cur.GlowPx; }, delegate(double v) { Cur.GlowPx = v; }, "准星外面一圈柔光，0＝不发光");
            AddValue(t, refreshers, "发光透明度", "%", 0, 100, 1, 0, delegate { return Cur.GlowOpacityPct; }, delegate(double v) { Cur.GlowOpacityPct = v; }, null);
            AddValue(t, refreshers, "边缘羽化", "px", 0, 10, 0.5, 1, delegate { return Cur.Feather; }, delegate(double v) { Cur.Feather = v; }, "边缘柔和程度，1＝清晰");
            AddValue(t, refreshers, "旋转", "°", -180, 180, 1, 0, delegate { return Cur.RotationDeg; }, delegate(double v) { Cur.RotationDeg = v; }, "顺时针为正");
            return t;
        }

        private Control BuildHand()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            AddCheck(t, refreshers, "换手后换颜色", delegate { return Cur.HandColorOn; }, delegate(bool v) { Cur.HandColorOn = v; },
                "换手后准星变成这个颜色，过渡时间跟「换手过渡时间」一样");
            Control[] c1 = AddSwatch(t, refreshers, "换手颜色", delegate { return Cur.HandColor; }, delegate(string v) { Cur.HandColor = v; }, null);
            Control[] c2 = AddSwatch(t, refreshers, "换手描边", delegate { return Cur.HandColor2; }, delegate(string v) { Cur.HandColor2 = v; }, null);
            refreshers.Add(delegate { Show(c1, Cur.HandColorOn); Show(c2, Cur.HandColorOn); });
            return t;
        }

        // ---------------- v5.1.1 (X3, Leo round B): 准星动态 and 弹药提示, each on its own sub-page of 外观 ----------------

        private AmmoBarSettings Bar { get { if (g.AmmoBar == null) g.AmmoBar = AmmoBarSettings.Defaults(); return g.AmmoBar; } }
        private AnimFxSettings Fx { get { if (g.AnimFx == null) g.AnimFx = AnimFxSettings.Defaults(); return g.AnimFx; } }

        /// <summary>A group title inside a sub-page.</summary>
        private void Head(TableLayoutPanel t, string text)
        {
            Label l = Lbl(text, RBody, Theme.ColAccent2);
            l.Margin = P(2, 12, 2, 4);
            AddWide(t, l);
        }

        private Label Note(TableLayoutPanel t, string text)
        {
            Label n = Lbl(text, RSmall, Theme.ColMuted);
            n.Margin = P(4, 2, 4, 4);
            (wrapTarget ?? wraps).Add(n); // v5.1.1 (P1): a sub-page's notes wrap with that sub-page only
            AddWide(t, n);
            return n;
        }

        /// <summary>label | drop-down (spans the rest), bound to an index.</summary>
        private Control[] AddCombo(TableLayoutPanel t, List<Action> refs, string label, string[] names, Func<int> get, Action<int> set, string tip)
        {
            Label l = Lbl(label, -1, Theme.ColText);
            gridLabels.Add(l);
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(names);
            c.Margin = P(4, 3, 4, 3);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            WheelScrollsPage(c);
            c.SelectedIndexChanged += delegate { if (loading || c.SelectedIndex < 0) return; set(c.SelectedIndex); RefreshValues(); Changed(); };
            refs.Add(delegate { int i = get(); c.SelectedIndex = i < 0 || i >= names.Length ? 0 : i; });
            if (!string.IsNullOrEmpty(tip)) { tips.SetToolTip(l, tip); tips.SetToolTip(c, tip); }
            Put(t, l, c);
            return new Control[] { l, c };
        }

        private static Control[] Join(params Control[][] rows)
        {
            List<Control> l = new List<Control>();
            foreach (Control[] r in rows) if (r != null) l.AddRange(r);
            return l.ToArray();
        }

        private FlowLayoutPanel ButtonRow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = true;
            f.Margin = P(2, 2, 2, 6);
            f.BackColor = Color.Transparent;
            Hold(f);
            return f;
        }

        private Button SmallButton(string text, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Margin = P(2, 2, 6, 2);
            b.Click += click;
            return b;
        }

        // ---- 准星动态 ----

        private Control BuildAnim()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            Head(t, "准星动态");
            Note(t, "准星跟着你的动作变化：移动时张开、开火时张开，还可以加开火波纹和开火渐变色。不增加延迟，中心始终留空，开镜、丢雷时照样完全看不到。下面的预览会循环演示：静止 → 开火 → 移动 → 边走边开火。");
            FxLivePreview live = new FxLivePreview(this, false);
            live.Margin = P(2, 2, 2, 4);
            live.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            FlowLayoutPanel pb = ButtonRow();
            pb.Controls.Add(SmallButton("预览开火（3 发）", delegate { live.Burst(3); }));
            pb.Controls.Add(SmallButton("预览连续开火", delegate { live.Burst(12); }));
            animPin = PinBox(live, pb); // v5.1.1 (X7): pinned at the top of the page, the settings scroll below
            refreshers.Add(delegate { live.Invalidate(); });

            FlowLayoutPanel line = ButtonRow();
            string[] tipsP = new string[] {
                "准星永远不动（职业选手常用）",
                "移动时张开，开火时每一发张开一下，停下后收回",
                "移动时不变，只在开火时张开一下",
                "开火时张开，同时出现波纹和渐变色",
                "像赛车换挡灯：开火时一格一格亮起来，斜切形状" };
            List<RadioButton> radios = new List<RadioButton>();
            for (int i = 0; i < CrosshairAnim.Presets.Length; i++)
            {
                string id = CrosshairAnim.Presets[i];
                RadioButton rb = new RadioButton();
                rb.Text = CrosshairAnim.PresetNames[i];
                rb.AutoSize = true;
                rb.Margin = P(4, 4, 12, 4);
                rb.CheckedChanged += delegate
                {
                    if (loading || !rb.Checked) return;
                    CrosshairAnim.ApplyPreset(id, g);
                    RefreshValues();
                    Changed();
                };
                if (i < tipsP.Length) tips.SetToolTip(rb, tipsP[i]); // v5.1: 竞速 was added without a tip (外观 crashed on Windows)
                radios.Add(rb);
                line.Controls.Add(rb);
            }
            Label custom = Lbl("（已自定义）", RSmall, Theme.ColMuted);
            custom.Margin = P(4, 6, 4, 4);
            line.Controls.Add(custom);
            try { line.Controls.Add(HelpMark.For("anim", SmallFont, delegate { return g; })); } catch (Exception) { }
            Head(t, "预设");
            AddWide(t, line);
            refreshers.Add(delegate
            {
                string now = CrosshairAnim.PresetOf(CrosshairAnim.From(g));
                for (int i = 0; i < radios.Count; i++) radios[i].Checked = CrosshairAnim.Presets[i] == now;
                custom.Visible = now == null;
            });

            Head(t, "移动");
            AddCheck(t, refreshers, "移动时准星张开", delegate { return g.AnimDynGap; }, delegate(bool v) { g.AnimDynGap = v; AnimPresetFollow(); },
                "走得越快张得越开，跳起来再张开一点；停下后跟着移动的手感收回");
            Control[] mv = AddValue(t, refreshers, "张开幅度", "%", 0, 300, 5, 0, delegate { return Fx.MoveGapPct; }, delegate(double v) { Fx.MoveGapPct = (int)Math.Round(v); }, "100%＝默认");
            refreshers.Add(delegate { Show(mv, g.AnimDynGap); });

            Head(t, "开火张开");
            AddCheck(t, refreshers, "开火时每一发张开一下", delegate { return g.AnimFirePulse; }, delegate(bool v) { g.AnimFirePulse = v; AnimPresetFollow(); },
                "按住左键时按射速每发张开一点，松开后收回");
            Control[] fg = Join(
                AddValue(t, refreshers, "张开幅度", "%", 0, 300, 5, 0, delegate { return Fx.FireGapPct; }, delegate(double v) { Fx.FireGapPct = (int)Math.Round(v); }, "每一发张开多少，100%＝默认"),
                AddValue(t, refreshers, "张开速度", "ms", 0, 200, 5, 0, delegate { return Fx.FireOpenMs; }, delegate(double v) { Fx.FireOpenMs = (int)Math.Round(v); }, "0＝开枪的同一帧立刻张开"),
                AddValue(t, refreshers, "恢复时间", "ms", 50, 1000, 10, 0, delegate { return Fx.FireRecoverMs; }, delegate(double v) { Fx.FireRecoverMs = (int)Math.Round(v); }, "停火后多久收回原样"));
            refreshers.Add(delegate { Show(fg, g.AnimFirePulse); });

            Head(t, "开火波纹");
            AddCheck(t, refreshers, "开火时出现波纹", delegate { return Fx.Ripple; }, delegate(bool v) { Fx.Ripple = v; AnimPresetFollow(); },
                "每开一枪，一圈细线从准星外沿向外扩散，一看就知道在开火");
            Control[] rp = Join(
                AddSwatch(t, refreshers, "波纹颜色", delegate { return Fx.RippleColour; }, delegate(string v) { Fx.RippleColour = v; }, null),
                AddValue(t, refreshers, "扩散速度", "px/s", 50, 2000, 10, 0, delegate { return Fx.RippleSpeed; }, delegate(double v) { Fx.RippleSpeed = (int)Math.Round(v); }, null),
                AddValue(t, refreshers, "波纹宽度", "px", 0.5, 8, 0.1, 1, delegate { return Fx.RippleWidth; }, delegate(double v) { Fx.RippleWidth = v; }, null),
                AddValue(t, refreshers, "扩散距离", "px", 4, 80, 1, 0, delegate { return Fx.RippleRange; }, delegate(double v) { Fx.RippleRange = (int)Math.Round(v); }, "波纹最远跑出准星多少像素"),
                AddValue(t, refreshers, "褪色时间", "ms", 80, 1500, 10, 0, delegate { return Fx.RippleFadeMs; }, delegate(double v) { Fx.RippleFadeMs = (int)Math.Round(v); }, null));
            refreshers.Add(delegate { Show(rp, Fx.Ripple); });

            Head(t, "开火渐变色");
            AddCheck(t, refreshers, "开火时准星从中心向外变色", delegate { return Fx.Grad; }, delegate(bool v) { Fx.Grad = v; AnimPresetFollow(); },
                "开火时，一层渐变色从中心向外铺满准星，停火后慢慢褪掉");
            Control[] gr = Join(
                AddSwatch(t, refreshers, "中心颜色", delegate { return Fx.GradFrom; }, delegate(string v) { Fx.GradFrom = v; }, null),
                AddSwatch(t, refreshers, "外圈颜色", delegate { return Fx.GradTo; }, delegate(string v) { Fx.GradTo = v; }, null),
                AddValue(t, refreshers, "扩散时间", "ms", 10, 600, 5, 0, delegate { return Fx.GradSpreadMs; }, delegate(double v) { Fx.GradSpreadMs = (int)Math.Round(v); }, "从中心铺到外圈要多久"),
                AddValue(t, refreshers, "褪色时间", "ms", 50, 1500, 10, 0, delegate { return Fx.GradFadeMs; }, delegate(double v) { Fx.GradFadeMs = (int)Math.Round(v); }, "停火后多久褪回原色"));
            refreshers.Add(delegate { Show(gr, Fx.Grad); });

            Head(t, "其他");
            AddCheck(t, refreshers, "按状态变色", delegate { return g.AnimStateColour; }, delegate(bool v) { g.AnimStateColour = v; AnimPresetFollow(); },
                "移动、跳跃、开火时准星颜色向白色靠一点；换手颜色照常");
            AddValue(t, refreshers, "开火效果强度", "%", 10, 200, 5, 0, delegate { return Fx.IntensityPct; }, delegate(double v) { Fx.IntensityPct = (int)Math.Round(v); },
                "同时调大或调小开火张开、波纹和渐变色");
            return t;
        }

        /// <summary>An option ticked by hand: AnimPreset follows when the switches equal a preset (else it keeps the last one).</summary>
        private void AnimPresetFollow()
        {
            string p = CrosshairAnim.PresetOf(CrosshairAnim.From(g));
            if (p != null) g.AnimPreset = p;
        }

        // ---- 弹药提示 ----

        private Control BuildAmmo()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            // v5.1.1 (P1 perf): built in parts, so the settings window can build this sub-page in small idle slices
            // (AmmoSliceStep); a direct call runs every part at once.
            List<Action> parts = new List<Action>();
            parts.Add(delegate
            {
                // v5.1.1 (X7, Leo 21 Nov): 先设置 first -- 开关、弹匣容量、射速、全自动/半自动、换弹键、双击间隔、两种换弹时间.
                // The rows come from the settings window (SetAmmoSetup: same fields as before, the 常调 strip stays in sync).
                // The live preview + its buttons are pinned at the top of the page (PinBox), so they stay in view while scrolling.
                Note(t, "在准星下方显示主武器还剩多少子弹（只显示范围）：满 / 大半 / 一半 / 少 / 快空，不显示具体数字。按 R 换弹、双击 R 快速换弹时换上一个新的满弹匣。中心始终留空，开镜、丢雷时和准星一起完全隐藏。");
                Head(t, "先设置");
                Note(t, "先打开弹药提示，填好主武器的弹匣容量和射速，换弹键和换弹时间要和游戏里一样。下面再改提示的样子。");
                ammoSetupHost = Grid(1);
                ammoSetupHost.Padding = new Padding(0);
                AddWide(t, ammoSetupHost);
                FxLivePreview live = new FxLivePreview(this, true);
                live.Margin = P(2, 2, 2, 4);
                live.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                FlowLayoutPanel pb = ButtonRow();
                pb.Controls.Add(SmallButton("预览普通换弹", delegate { live.Reload(1); }));
                pb.Controls.Add(SmallButton("预览快速换弹", delegate { live.Reload(2); }));
                pb.Controls.Add(SmallButton("预览低弹警告", delegate { live.Warn(1); }));
                pb.Controls.Add(SmallButton("预览紧急警告", delegate { live.Warn(2); }));
                ammoPin = PinBox(live, pb);
                refreshers.Add(delegate { live.Invalidate(); });
            });
            parts.Add(delegate
            {
                Head(t, "样式预设");
                AmmoPresetPicker picker = new AmmoPresetPicker(this);
                ammoPicker = picker;
                picker.Margin = P(2, 2, 2, 4);
                picker.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                picker.Picked += delegate(int i) { AmmoBarSettings.ApplyPreset(Bar, AmmoBarSettings.Presets[i]); RefreshValues(); Changed(); };
                AddWide(t, picker);
                Label custom = Note(t, "");
                refreshers.Add(delegate
                {
                    picker.Invalidate();
                    string p = Bar.PresetOf();
                    if (p != null) Bar.Preset = p;
                    custom.Text = p == null ? "（已自定义：下面的数值和预设不一样）" : "预设只改条的样子；位置、颜色、警告和换弹动画保持你的设置";
                });
            });
            parts.Add(delegate
            {
                Head(t, "条的样子");
                AddCombo(t, refreshers, "位置", AmmoBarSettings.PositionNames, delegate { return Array.IndexOf(AmmoBarSettings.Positions, Bar.Pos); },
                    delegate(int i) { Bar.Pos = AmmoBarSettings.Positions[i]; }, "默认在准星下方");
                AddValue(t, refreshers, "长度", "px", 6, 160, 1, 0, delegate { return Bar.LenPx; }, delegate(double v) { Bar.LenPx = v; }, null);
                AddValue(t, refreshers, "粗细", "px", 1, 12, 0.5, 1, delegate { return Bar.ThickPx; }, delegate(double v) { Bar.ThickPx = v; }, null);
                AddValue(t, refreshers, "离准星距离", "px", 0, 60, 1, 0, delegate { return Bar.GapPx; }, delegate(double v) { Bar.GapPx = v; }, "0＝紧贴准星外沿（中心一直留空）");
                AddValue(t, refreshers, "分段数", "段", 0, 20, 1, 0, delegate { return Bar.Segments; },
                    delegate(double v) { int n = (int)Math.Round(v); Bar.Segments = n == 1 ? 2 : n; }, "0＝一整条；2 段以上＝一格一格（CF 风格是 10 格）");
                Control[] seg = AddValue(t, refreshers, "格子间隔", "px", 0.5, 6, 0.5, 1, delegate { return Bar.SegGapPx; }, delegate(double v) { Bar.SegGapPx = v; }, null);
                refreshers.Add(delegate { Show(seg, Bar.Segments >= 2); });
                AddCheck(t, refreshers, "圆角", delegate { return Bar.Round; }, delegate(bool v) { Bar.Round = v; }, "条和格子的两头变圆");
                AddCheck(t, refreshers, "发光", delegate { return Bar.Glow; }, delegate(bool v) { Bar.Glow = v; }, "亮着的部分外面一圈柔光");
                Control[] gl = AddValue(t, refreshers, "发光大小", "px", 1, 10, 0.5, 1, delegate { return Bar.GlowPx; }, delegate(double v) { Bar.GlowPx = v; }, null);
                refreshers.Add(delegate { Show(gl, Bar.Glow); });
                AddCheck(t, refreshers, "显示背景槽", delegate { return Bar.Track; }, delegate(bool v) { Bar.Track = v; }, "空的部分也淡淡显示出来，一眼看出还剩多少");
                Control[] tr = AddValue(t, refreshers, "背景槽浓度", "%", 0, 100, 1, 0, delegate { return Bar.TrackPct; }, delegate(double v) { Bar.TrackPct = (int)Math.Round(v); }, null);
                refreshers.Add(delegate { Show(tr, Bar.Track); });
                AddCheck(t, refreshers, "外框", delegate { return Bar.Frame; }, delegate(bool v) { Bar.Frame = v; }, "条的外面加一圈细框（CF 风格）");
                AddCheck(t, refreshers, "斜切格子", delegate { return Bar.Slant; }, delegate(bool v) { Bar.Slant = v; }, "格子斜着切，看起来更快更锐利（竞速）");
                Control[] sl = AddValue(t, refreshers, "斜切角度", "°", 0, 60, 1, 0, delegate { return Bar.SlantDeg; }, delegate(double v) { Bar.SlantDeg = v; }, null);
                refreshers.Add(delegate { Show(sl, Bar.Slant); });
            });
            parts.Add(delegate
            {
                AddCheck(t, refreshers, "按位置上色（换挡灯）", delegate { return Bar.Led; }, delegate(bool v) { Bar.Led = v; }, "像换挡灯一样，前段、中段、后段各一种颜色；警告时用警告色");
                string[] ledNames = { "前段颜色", "中段颜色", "后段颜色" };
                List<Control[]> ledRows = new List<Control[]>();
                for (int i = 0; i < 3; i++)
                {
                    int k = i;
                    ledRows.Add(AddSwatch(t, refreshers, ledNames[i], delegate { return Bar.LedColours[k]; }, delegate(string v) { Bar.LedColours[k] = v; }, null));
                }
                refreshers.Add(delegate { foreach (Control[] r in ledRows) Show(r, Bar.Led); });
                AddCheck(t, refreshers, "黑色底板", delegate { return Bar.Backing; }, delegate(bool v) { Bar.Backing = v; }, "条后面垫一块黑色底板，任何背景都看得清");
                AddCheck(t, refreshers, "速度条纹", delegate { return Bar.Stripes; }, delegate(bool v) { Bar.Stripes = v; }, "条的尾部加三道条纹");
                Control[] ac = AddSwatch(t, refreshers, "条纹颜色", delegate { return Bar.AccentColour; }, delegate(string v) { Bar.AccentColour = v; }, null);
                refreshers.Add(delegate { Show(ac, Bar.Stripes); });
                AddValue(t, refreshers, "不透明度", "%", 10, 100, 1, 0, delegate { return Bar.OpacityPct; }, delegate(double v) { Bar.OpacityPct = (int)Math.Round(v); }, null);
            });
            parts.Add(delegate
            {
                Head(t, "颜色");
                AddCheck(t, refreshers, "按剩余变色", delegate { return (AmmoDisplay.ParseStyle(g.AmmoStyle) & AmmoDisplay.Colour) != 0; },
                    delegate(bool v) { int c = AmmoDisplay.ParseStyle(g.AmmoStyle); g.AmmoStyle = AmmoDisplay.FormatStyle(v ? c | AmmoDisplay.Colour : c & ~AmmoDisplay.Colour); },
                    "每一档用自己的颜色（准星本身的颜色不变）");
                List<Control[]> bandRows = new List<Control[]>();
                string[] bandLabels = { "满", "大半", "一半", "少", "快空" };
                for (int i = 0; i < AmmoBarSettings.BandCount; i++)
                {
                    int k = i;
                    bandRows.Add(AddSwatch(t, refreshers, bandLabels[i] + "的颜色", delegate { return Bar.BandColours[k]; }, delegate(string v) { Bar.BandColours[k] = v; }, null));
                }
                Control[] single = AddSwatch(t, refreshers, "颜色", delegate { return Bar.Colour; }, delegate(string v) { Bar.Colour = v; }, null);
                refreshers.Add(delegate
                {
                    bool band = (AmmoDisplay.ParseStyle(g.AmmoStyle) & AmmoDisplay.Colour) != 0;
                    foreach (Control[] r in bandRows) Show(r, band);
                    Show(single, !band);
                });
                AddCheck(t, refreshers, "越少越淡", delegate { return (AmmoDisplay.ParseStyle(g.AmmoStyle) & AmmoDisplay.Fade) != 0; },
                    delegate(bool v) { int c = AmmoDisplay.ParseStyle(g.AmmoStyle); g.AmmoStyle = AmmoDisplay.FormatStyle(v ? c | AmmoDisplay.Fade : c & ~AmmoDisplay.Fade); }, "子弹越少，条越淡");
            });
            parts.Add(delegate
            {
                Head(t, "什么时候显示");
                AddCheck(t, refreshers, "只在剩余少于下面的值时显示", delegate { return Bar.ShowBelowOn; }, delegate(bool v) { Bar.ShowBelowOn = v; },
                    "平时不显示，子弹快用完了才出现；换弹时一直显示");
                Control[] sb = AddValue(t, refreshers, "剩余少于", "%", 5, 100, 1, 0, delegate { return Bar.ShowBelowPct; }, delegate(double v) { Bar.ShowBelowPct = (int)Math.Round(v); }, null);
                refreshers.Add(delegate { Show(sb, Bar.ShowBelowOn); });
                AddValue(t, refreshers, "出现 / 消失淡入", "ms", 0, 1000, 10, 0, delegate { return Bar.FadeInMs; }, delegate(double v) { Bar.FadeInMs = (int)Math.Round(v); }, "0＝立刻出现");
            });
            parts.Add(delegate
            {
                BuildWarn(t, "低弹警告", delegate { return Bar.Warn1; }, "子弹少于这个比例时，条用下面的方式提醒你");
            });
            parts.Add(delegate
            {
                BuildWarn(t, "紧急警告（快打空时）", delegate { return Bar.Warn2; }, "更少时换成更急的提醒（会盖过上面的低弹警告）");
            });
            parts.Add(delegate
            {
                Head(t, "换弹动画");
                Note(t, "普通换弹（按 R）和快速换弹（双击 R）各自设置。动画的总长度就是上面「先设置」里的换弹时间。");
                FlowLayoutPanel rp = ButtonRow();
                Label rl = Lbl("预设：", RSmall, Theme.ColMuted);
                rl.Margin = P(4, 8, 2, 2);
                rp.Controls.Add(rl);
                rp.Controls.Add(SmallButton("默认", delegate { Bar.ReloadNormal = ReloadAnimSettings.Normal(); Bar.ReloadQuick = ReloadAnimSettings.Quick(); RefreshValues(); Changed(); }));
                rp.Controls.Add(SmallButton("竞速", delegate { ReloadAnimSettings.ApplyRace(Bar.ReloadNormal, false); ReloadAnimSettings.ApplyRace(Bar.ReloadQuick, true); RefreshValues(); Changed(); }));
                AddWide(t, rp);
                AddWide(t, BuildReloadGrid());
            });
            if (ammoSliced) { ammoParts = parts; return t; }
            foreach (Action a in parts) a();
            return t;
        }

        private void BuildWarn(TableLayoutPanel t, string title, Func<AmmoWarnSettings> w, string tip)
        {
            Head(t, title);
            AddCheck(t, refreshers, "开启" + title.Split('（')[0], delegate { return w().On; }, delegate(bool v) { w().On = v; }, tip);
            bool urgent = title.StartsWith("紧急");
            FlowLayoutPanel pr = ButtonRow();
            Label pl = Lbl("预设：", RSmall, Theme.ColMuted);
            pl.Margin = P(4, 8, 2, 2);
            pr.Controls.Add(pl);
            for (int i = 0; i < AmmoWarnSettings.PresetIds.Length; i++)
            {
                string id = AmmoWarnSettings.PresetIds[i];
                pr.Controls.Add(SmallButton(AmmoWarnSettings.PresetNames[i], delegate { AmmoWarnSettings.ApplyPreset(w(), id, urgent); RefreshValues(); Changed(); }));
            }
            AddWide(t, pr);
            Control[] rows = Join(
                AddValue(t, refreshers, "剩余少于", "%", 1, 100, 1, 0, delegate { return w().Pct; }, delegate(double v) { w().Pct = (int)Math.Round(v); }, null),
                AddCombo(t, refreshers, "提醒方式", AmmoWarnSettings.StyleNames, delegate { return Array.IndexOf(AmmoWarnSettings.Styles, w().Style); },
                    delegate(int i) { w().Style = AmmoWarnSettings.Styles[i]; }, "闪烁＝一亮一暗；呼吸＝慢慢变亮变暗；变色＝在条的颜色和警告色之间来回；发光＝外面一圈光一闪一闪"),
                AddValue(t, refreshers, "速度（一次）", "ms", 80, 2000, 10, 0, delegate { return w().PeriodMs; }, delegate(double v) { w().PeriodMs = (int)Math.Round(v); }, "数字越小闪得越快"),
                AddSwatch(t, refreshers, "警告颜色", delegate { return w().Colour; }, delegate(string v) { w().Colour = v; }, null),
                AddValue(t, refreshers, "强度", "%", 10, 100, 1, 0, delegate { return w().IntensityPct; }, delegate(double v) { w().IntensityPct = (int)Math.Round(v); }, null));
            Control[] blinks = AddValue(t, refreshers, "闪几下", "次", 1, 10, 1, 0, delegate { return w().Blinks; }, delegate(double v) { w().Blinks = (int)Math.Round(v); }, null);
            refreshers.Add(delegate { Show(rows, w().On); Show(blinks, w().On && w().Style == "blink"); pr.Visible = w().On; });
        }

        /// <summary>换弹动画: label | 普通换弹 | 快速换弹.</summary>
        private Control BuildReloadGrid()
        {
            TableLayoutPanel rg = new TableLayoutPanel();
            rg.ColumnCount = 3;
            rg.AutoSize = true;
            rg.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rg.Dock = DockStyle.Top;
            rg.Margin = new Padding(0);
            rg.Padding = P(2, 2, 2, 2);
            rg.BackColor = Color.Transparent;
            rg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            Hold(rg);
            Func<ReloadAnimSettings>[] col = new Func<ReloadAnimSettings>[] {
                delegate { return Bar.ReloadNormal; }, delegate { return Bar.ReloadQuick; } };
            Label h0 = Lbl("", -1, Theme.ColText), h1 = Lbl("普通换弹（R）", RBody, Theme.ColAccent), h2 = Lbl("快速换弹（R R）", RBody, Theme.ColAccent);
            ReloadRow(rg, h0, h1, h2);
            Func<ReloadAnimSettings, int> gT = delegate(ReloadAnimSettings r) { return Array.IndexOf(ReloadAnimSettings.Types, r.Type); };
            ReloadCombo(rg, col, "动画", ReloadAnimSettings.TypeNames, gT, delegate(ReloadAnimSettings r, int i) { r.Type = ReloadAnimSettings.Types[i]; });
            ReloadCombo(rg, col, "方向", new string[] { "正向（从起点填满）", "反向（从末端填满）" }, delegate(ReloadAnimSettings r) { return r.Reverse ? 1 : 0; },
                delegate(ReloadAnimSettings r, int i) { r.Reverse = i == 1; });
            ReloadNum(rg, col, "旧弹匣离开用时 %", 5, 90, delegate(ReloadAnimSettings r) { return r.OutPct; }, delegate(ReloadAnimSettings r, int v) { r.OutPct = v; });
            ReloadNum(rg, col, "新弹匣装入用时 %", 5, 90, delegate(ReloadAnimSettings r) { return r.InPct; }, delegate(ReloadAnimSettings r, int v) { r.InPct = v; });
            ReloadCombo(rg, col, "快慢曲线", ReloadAnimSettings.EaseNames, delegate(ReloadAnimSettings r) { return Array.IndexOf(ReloadAnimSettings.Eases, r.Ease); },
                delegate(ReloadAnimSettings r, int i) { r.Ease = ReloadAnimSettings.Eases[i]; });
            ReloadCombo(rg, col, "换弹标记", ReloadAnimSettings.MarkerNames, delegate(ReloadAnimSettings r) { return Array.IndexOf(ReloadAnimSettings.Markers, r.Marker); },
                delegate(ReloadAnimSettings r, int i) { r.Marker = ReloadAnimSettings.Markers[i]; });
            ReloadCheck(rg, col, "旧弹匣掉落", delegate(ReloadAnimSettings r) { return r.Drop; }, delegate(ReloadAnimSettings r, bool v) { r.Drop = v; });
            ReloadColour(rg, col);
            return rg;
        }

        private void ReloadRow(TableLayoutPanel rg, Control a, Control b, Control c)
        {
            int r = GridRows.Next(rg);
            rg.Controls.Add(a, 0, r); rg.Controls.Add(b, 1, r); rg.Controls.Add(c, 2, r);
        }

        private Label ReloadLabel(string text)
        {
            Label l = Lbl(text, -1, Theme.ColText);
            return l;
        }

        private void ReloadCombo(TableLayoutPanel rg, Func<ReloadAnimSettings>[] col, string label, string[] names, Func<ReloadAnimSettings, int> get, Action<ReloadAnimSettings, int> set)
        {
            Control[] cells = new Control[2];
            for (int k = 0; k < 2; k++)
            {
                Func<ReloadAnimSettings> f = col[k];
                ComboBox c = new ComboBox();
                c.DropDownStyle = ComboBoxStyle.DropDownList;
                c.Items.AddRange(names);
                c.Margin = P(4, 3, 4, 3);
                c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                WheelScrollsPage(c);
                c.SelectedIndexChanged += delegate { if (loading || c.SelectedIndex < 0) return; set(f(), c.SelectedIndex); RefreshValues(); Changed(); };
                refreshers.Add(delegate { int i = get(f()); c.SelectedIndex = i < 0 || i >= names.Length ? 0 : i; });
                cells[k] = c;
            }
            ReloadRow(rg, ReloadLabel(label), cells[0], cells[1]);
        }

        private void ReloadNum(TableLayoutPanel rg, Func<ReloadAnimSettings>[] col, string label, int min, int max, Func<ReloadAnimSettings, int> get, Action<ReloadAnimSettings, int> set)
        {
            Control[] cells = new Control[2];
            for (int k = 0; k < 2; k++)
            {
                Func<ReloadAnimSettings> f = col[k];
                NumericUpDown n = new NumericUpDown();
                n.Minimum = min; n.Maximum = max; n.Increment = 5; n.DecimalPlaces = 0;
                n.TextAlign = HorizontalAlignment.Left;
                UseFont(n, RMono);
                n.Margin = P(4, 3, 4, 3);
                n.Anchor = AnchorStyles.Left;
                nums.Add(new KeyValuePair<NumericUpDown, string>(n, WideNum(min, max, 0)));
                WheelScrollsPage(n);
                n.ValueChanged += delegate { if (loading) return; set(f(), (int)n.Value); Changed(); };
                refreshers.Add(delegate { n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, get(f()))); });
                cells[k] = n;
            }
            ReloadRow(rg, ReloadLabel(label), cells[0], cells[1]);
        }

        private void ReloadCheck(TableLayoutPanel rg, Func<ReloadAnimSettings>[] col, string label, Func<ReloadAnimSettings, bool> get, Action<ReloadAnimSettings, bool> set)
        {
            Control[] cells = new Control[2];
            for (int k = 0; k < 2; k++)
            {
                Func<ReloadAnimSettings> f = col[k];
                CheckBox c = new CheckBox();
                c.Text = "开";
                c.AutoSize = true;
                c.Margin = P(4, 4, 4, 4);
                c.Anchor = AnchorStyles.Left;
                c.CheckedChanged += delegate { if (loading) return; set(f(), c.Checked); RefreshValues(); Changed(); };
                refreshers.Add(delegate { c.Checked = get(f()); });
                cells[k] = c;
            }
            ReloadRow(rg, ReloadLabel(label), cells[0], cells[1]);
        }

        private void ReloadColour(TableLayoutPanel rg, Func<ReloadAnimSettings>[] col)
        {
            Control[] cells = new Control[2];
            for (int k = 0; k < 2; k++)
            {
                Func<ReloadAnimSettings> f = col[k];
                FlowLayoutPanel cell = new FlowLayoutPanel();
                cell.AutoSize = true; cell.AutoSizeMode = AutoSizeMode.GrowAndShrink; cell.WrapContents = true; cell.Margin = new Padding(0); cell.BackColor = Color.Transparent;
                CheckBox c = new CheckBox();
                c.Text = "开";
                c.AutoSize = true;
                c.Margin = P(4, 4, 4, 4);
                c.CheckedChanged += delegate { if (loading) return; f().ColourOn = c.Checked; RefreshValues(); Changed(); };
                Swatch sw = new Swatch(this);
                sw.Margin = P(4, 3, 4, 3);
                sw.Get = delegate { return f().Colour; };
                swatches.Add(sw);
                sw.Click += delegate
                {
                    using (ColorDialog dlg = new ColorDialog())
                    {
                        int r, gg, b;
                        dlg.FullOpen = true;
                        if (ColorUtil.TryParse(f().Colour, out r, out gg, out b)) dlg.Color = Color.FromArgb(r, gg, b);
                        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                        f().Colour = ColorUtil.Format(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    }
                    sw.Invalidate();
                    Changed();
                };
                refreshers.Add(delegate { c.Checked = f().ColourOn; sw.Visible = f().ColourOn; sw.Invalidate(); });
                cell.Controls.Add(c); cell.Controls.Add(sw);
                cells[k] = cell;
            }
            ReloadRow(rg, ReloadLabel("换弹时用这个颜色"), cells[0], cells[1]);
        }

        /// <summary>v5.0.2 (Leo, decision 4): the 强化准星 section. The look itself is edited with the page's own controls
        /// (「改强化准星的样子」 switches the whole page to it, SetEditBoost); here: the switch, 「和正常准星一样」 and
        /// 「跟随当前手的颜色」.</summary>
        private Control BuildBoost()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            lblBoostNote = Lbl("", RSmall, Theme.ColMuted);
            lblBoostNote.Margin = P(4, 2, 4, 4);
            wraps.Add(lblBoostNote);
            AddWide(t, lblBoostNote);
            FlowLayoutPanel line = new FlowLayoutPanel();
            line.AutoSize = true;
            line.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            line.WrapContents = true;
            line.Margin = P(2, 2, 2, 2);
            line.BackColor = Color.Transparent;
            btnBoostEdit = new Button();
            btnBoostEdit.Text = "改强化准星的样子";
            btnBoostEdit.Tag = "theme:accent";
            btnBoostEdit.AutoSize = true;
            btnBoostEdit.Margin = P(2, 2, 6, 2);
            btnBoostEdit.Click += delegate { SetEditBoost(!editBoost); };
            btnBoostCopy = new Button();
            btnBoostCopy.Text = "和正常准星一样";
            btnBoostCopy.AutoSize = true;
            btnBoostCopy.Margin = P(2, 2, 2, 2);
            btnBoostCopy.Click += delegate { CopyNormalToBoost(); };
            tips.SetToolTip(btnBoostCopy, "强化准星的样式、大小、颜色、描边、发光全部改成和正常准星一样");
            line.Controls.Add(btnBoostEdit);
            line.Controls.Add(btnBoostCopy);
            AddWide(t, line);
            AddCheck(t, refreshers, "跟随当前手的颜色", delegate { return g.Active.Boost.FollowHandColor; },
                delegate(bool v) { g.Active.Boost.FollowHandColor = v; },
                "勾上＝用正常 / 换手时的颜色；不勾＝强化准星用自己的颜色（在上面的「颜色」里改）");
            refreshers.Add(delegate
            {
                btnBoostEdit.Text = editBoost ? "改回正常准星" : "改强化准星的样子";
                lblBoostNote.Text = editBoost
                    ? "现在整页改的是强化准星（按强化键后的样子）。改完点「改回正常准星」。"
                    : "按强化键后换成另一套外观：样式、大小、线宽、颜色、描边、发光、中心留空、自定义图片都能单独设。点「改强化准星的样子」，整页就改强化准星。";
            });
            return t;
        }

        /// <summary>v5.0.2: switches the whole page between the normal look and the 强化 look. A pre-v5.0.2 强化 look (shape
        /// from the normal look) first becomes a look of its own that draws exactly as before (Profile.BeginOwnBoost).</summary>
        internal void SetEditBoost(bool on)
        {
            if (disposedAll) return;
            if (on)
            {
                Profile a = g.Active;
                if (a == null) return;
                if (a.Boost == null) a.Boost = new BoostStyle();
                bool converted = false;
                if (!a.Boost.OwnLook)
                {
                    RingStyle rs = RingStyle.From(a, 0, true); // what the v3.0 rule draws now
                    a.BeginOwnBoost(rs.SGap, rs.SLength, rs.Hole);
                    converted = true;
                }
                boostEdit = new Profile();
                a.FillBoostView(boostEdit);
                boostEditOf = a;
                editBoost = true;
                if (converted && onChanged != null) { try { onChanged(); } catch (Exception) { } }
            }
            else
            {
                SyncBoost();
                editBoost = false;
                boostEdit = null;
                boostEditOf = null;
            }
            shownTile = null; // rebuild the per-style rows (the 强化 size goes up to 300 px)
            if (preview != null) preview.SetMode(on ? 2 : 0);
            RefreshValues();
        }

        /// <summary>v5.0.2: the page is editing the 强化 look.</summary>
        internal bool EditingBoost { get { return editBoost; } }

        /// <summary>v5.0.2: writes the 强化 editor's values back to the profile they came from (no-op otherwise).</summary>
        private void SyncBoost()
        {
            if (!editBoost || boostEdit == null || boostEditOf == null || boostEditOf != g.Active) return;
            boostEditOf.StoreBoostView(boostEdit);
        }

        /// <summary>v5.0.2: 「和正常准星一样」.</summary>
        private void CopyNormalToBoost()
        {
            Profile a = g.Active;
            if (a == null) return;
            if (!ThemedDialog.Show(FindForm(), "把强化准星的样式、大小、颜色、描边、发光全部改成和正常准星一样？", true)) return;
            a.CopyNormalToBoost();
            if (editBoost) { boostEdit = new Profile(); a.FillBoostView(boostEdit); boostEditOf = a; }
            shownTile = null;
            RefreshValues();
            if (onChanged != null) { try { onChanged(); } catch (Exception) { } }
        }

        private Profile previewView;

        /// <summary>v5.0.2: the 强化 look of p as a plain Profile for the preview's 「强化」 button (reused object).</summary>
        internal Profile BoostPreviewOf(Profile p)
        {
            if (previewView == null) previewView = new Profile();
            p.FillBoostView(previewView);
            return previewView;
        }

        // ---------------- per-style rows ----------------

        private void RebuildParams(StyleTile t)
        {
            string id = t == null ? "" : t.Id;
            if (id == shownTile) return;
            shownTile = id;
            BeginHold(); // v5.1.1 (P1, R1 #4): one layout for the new rows (paramGrid is held with the rest)
            try
            {
            List<Control> old = new List<Control>();
            foreach (Control c in paramGrid.Controls) old.Add(c);
            paramGrid.Controls.Clear();
            foreach (Control c in old) c.Dispose();
            GridRows.Reset(paramGrid); // v4.3.1: rows are fixed cells, numbered again from 0
            paramRefreshers.Clear();
            advParamRows.Clear();
            List<Action> R = paramRefreshers;
            if (t == null)
            {
                AddValue(paramGrid, R, "大小", "px", 4, editBoost ? 300 : 120, 1, 0, delegate { return Cur.Diameter; }, delegate(double v) { Cur.Diameter = v; }, null);
                advParamRows.Add(AddValue(paramGrid, R, "线宽", "px", 0.5, 12, 0.1, 1, delegate { return Cur.Thickness; }, delegate(double v) { Cur.Thickness = v; }, null));
            }
            else
            {
                string main = MainSizeParam(t);
                foreach (string prm in t.Params)
                {
                    int before = paramGrid.Controls.Count;
                    AddParam(t, prm);
                    if (prm == main || prm == StyleShapes.ParamPng) continue; // shown without 高级设置
                    List<Control> row = new List<Control>();
                    for (int i = before; i < paramGrid.Controls.Count; i++) row.Add(paramGrid.Controls[i]);
                    advParamRows.Add(row.ToArray());
                }
            }
            advParamRows.Add(AddValue(paramGrid, R, "中心留空", "px", CrosshairStyle.MinHole, CrosshairStyle.MaxHole, 0.5, 1,
                delegate { return StyleShapes.Of(Cur).Hole; }, delegate(double v) { StyleShapes.Of(Cur).Hole = Math.Max(CrosshairStyle.MinHole, v); },
                "这个半径以内一定完全透明（最少 3 px），游戏中心的小白点永远看得见"));
            foreach (Control[] row in advParamRows) Show(row, advanced);
            ApplySizes(); // cached widths: only the new rows' sizes actually change
            }
            finally { EndHold(); }
        }

        /// <summary>The one value shown without 高级设置: the style's overall size (直径 / 大小, else 边长 / 十字长度 / 离中心).</summary>
        private static string MainSizeParam(StyleTile t)
        {
            string[] order = new string[] { StyleShapes.ParamSize, StyleShapes.ParamLen, StyleShapes.ParamCrossLen, StyleShapes.ParamGap };
            foreach (string o in order) if (t.Has(o)) return o;
            return null;
        }

        private void AddParam(StyleTile t, string prm)
        {
            List<Action> R = paramRefreshers;
            string id = t.Id;
            switch (prm)
            {
                case StyleShapes.ParamSize:
                    {
                        bool custom = id == "custom";
                        string label = id == "ring" || id == "dashring" || id == "ringcross" || id == "ringticks" || id == "dotring" || id == "arcs" || id == "smile" ? "直径" : "大小";
                        AddValue(paramGrid, R, label, "px", custom ? 8 : 4, custom || editBoost ? 300 : 120, 1, 0, delegate { return Cur.Diameter; },
                            delegate(double v)
                            {
                                double old = Cur.Diameter;
                                Cur.Diameter = v;
                                // v5.0.2: a 强化 look of its own keeps its own size
                                if (old > 0 && !editBoost && !string.IsNullOrEmpty(StyleShapes.Of(Cur).Kind) && Cur.Boost != null && !Cur.Boost.OwnLook)
                                    Cur.Boost.Diameter = Math.Max(4, Math.Min(300, Cur.Boost.Diameter * v / old)); // keep the 强化 ratio
                            }, custom ? "图片较长的一边缩放到这么大" : null);
                        break;
                    }
                case StyleShapes.ParamThick:
                    {
                        bool dots = id == "dotring" || id == "fourdots";
                        AddValue(paramGrid, R, dots ? "点大小" : "线宽", "px", 0.5, 12, 0.1, 1, delegate { return Cur.Thickness; },
                            delegate(double v) { Cur.Thickness = v; }, null);
                        break;
                    }
                case StyleShapes.ParamGap:
                    {
                        bool chev = id == "chevron" || id == "chevron2";
                        AddValue(paramGrid, R, chev ? "下沉距离" : "离中心", "px", 0, 60, 0.5, 1, delegate { return StyleShapes.Of(Cur).Gap; },
                            delegate(double v) { StyleShapes.Of(Cur).Gap = v; },
                            chev ? "箭头尖离中心多远（往下沉），越大中间越空" : "离中心多远开始画");
                        break;
                    }
                case StyleShapes.ParamLen:
                    {
                        string label = id == "chevron" || id == "chevron2" ? "边长" : (id == "corners" ? "角长" : (id == "brackets" ? "括号沿" : (id == "ringticks" ? "刻度长" : "长度")));
                        AddValue(paramGrid, R, label, "px", 1, 60, 0.5, 1, delegate { return StyleShapes.Of(Cur).Length; },
                            delegate(double v) { StyleShapes.Of(Cur).Length = v; }, null);
                        break;
                    }
                case StyleShapes.ParamSpread:
                    {
                        double min = 30, max = 170; string label = "张开角度", tip = "两条边之间的角度，越大越平";
                        if (id == "arcs") { min = 10; max = 170; label = "每段弧度"; tip = "每一段圆弧有多长（段数多时会自动缩短）"; }
                        if (id == "smile") { min = 30; max = 330; label = "弧度"; tip = "弧线有多长"; }
                        if (id == "race") { min = 0; max = 60; label = "斜切角度"; tip = "格子斜多少度，0＝竖直"; }
                        AddValue(paramGrid, R, label, "°", min, max, 1, 0, delegate { return StyleShapes.Of(Cur).Spread; },
                            delegate(double v) { StyleShapes.Of(Cur).Spread = v; }, tip);
                        break;
                    }
                case StyleShapes.ParamCount:
                    {
                        bool dots = id == "dotring";
                        AddValue(paramGrid, R, dots ? "点数" : (id == "race" ? "每边格数" : "段数"), dots ? "个" : "段", dots ? 3 : 2, dots ? 24 : 8, 1, 0,
                            delegate { return StyleShapes.Of(Cur).Count; }, delegate(double v) { StyleShapes.Of(Cur).Count = (int)Math.Round(v); }, null);
                        break;
                    }
                case StyleShapes.ParamCrossGap:
                    AddValue(paramGrid, R, "十字间隙", "px", 0, 60, 0.5, 1, delegate { return Cur.CrossGap; }, delegate(double v) { Cur.CrossGap = v; }, "十字离中心空出多少");
                    break;
                case StyleShapes.ParamCrossLen:
                    AddValue(paramGrid, R, "十字长度", "px", 1, 60, 0.5, 1, delegate { return Cur.CrossLength; }, delegate(double v) { Cur.CrossLength = v; }, null);
                    break;
                case StyleShapes.ParamCrossThick:
                    AddValue(paramGrid, R, "十字线宽", "px", 0.5, 12, 0.1, 1, delegate { return Cur.CrossThickness; }, delegate(double v) { Cur.CrossThickness = v; }, null);
                    break;
                case StyleShapes.ParamDashes:
                    AddValue(paramGrid, R, "段数", "段", 2, 24, 1, 0, delegate { return Math.Max(2, Cur.Dashes); }, delegate(double v) { Cur.Dashes = (int)Math.Round(v); }, null);
                    break;
                case StyleShapes.ParamDashGap:
                    AddValue(paramGrid, R, "段间空隙", "%", 5, 90, 1, 0, delegate { return Cur.DashGapPct; }, delegate(double v) { Cur.DashGapPct = v; }, null);
                    break;
                case StyleShapes.ParamPng:
                    AddPngRows();
                    break;
            }
        }

        private void AddPngRows()
        {
            List<Action> R = paramRefreshers;
            FlowLayoutPanel line = new FlowLayoutPanel();
            line.AutoSize = true;
            line.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            line.WrapContents = true;
            line.Margin = P(2, 4, 2, 2);
            line.BackColor = Color.Transparent;
            Button up = new Button();
            up.Text = "上传 PNG…";
            up.Tag = "theme:accent";
            up.AutoSize = true;
            up.Margin = P(2, 2, 6, 2);
            up.Click += delegate { Upload(); };
            Button open = new Button();
            open.Text = "打开「" + SettingsIO.CrosshairFolderName + "」文件夹";
            open.AutoSize = true;
            open.Margin = P(2, 2, 2, 2);
            open.Click += delegate { OpenFolder(); };
            line.Controls.Add(up);
            line.Controls.Add(open);
            AddWide(paramGrid, line);

            Label lp = Lbl("图片", -1, Theme.ColText);
            gridLabels.Add(lp);
            ComboBox pick = new ComboBox();
            pick.DropDownStyle = ComboBoxStyle.DropDownList;
            pick.Margin = P(4, 3, 4, 3);
            WheelScrollsPage(pick);
            pick.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            pick.DropDown += delegate
            {
                if (loading) return;
                CustomCrosshair.Invalidate();
                FillPick(pick);
            };
            pick.SelectedIndexChanged += delegate
            {
                if (loading || pick.SelectedIndex < 0) return;
                string name = pick.SelectedItem as string;
                if (string.IsNullOrEmpty(name) || name == StyleShapes.Of(Cur).PngFile) return;
                StyleShapes.Of(Cur).PngFile = name;
                UpdatePngStatus();
                Changed();
            };
            R.Add(delegate { FillPick(pick); });
            Put(paramGrid, lp, pick); // pick spans 3

            CheckBox tint = new CheckBox();
            tint.Text = "用准星颜色给图片上色（换手颜色也会生效）";
            tint.AutoSize = true;
            tint.Margin = P(4, 4, 4, 4);
            tint.CheckedChanged += delegate
            {
                if (loading) return;
                StyleShapes.Of(Cur).PngTint = tint.Checked;
                RefreshValues();
                Changed();
            };
            R.Add(delegate { tint.Checked = StyleShapes.Of(Cur).PngTint; });
            AddWide(paramGrid, tint);
            Label note = Lbl("透明背景的 PNG 效果最好。图片中间会自动挖空（至少 3 px），游戏的小白点不会被挡住。", RSmall, Theme.ColMuted);
            wraps.Add(note);
            AddWide(paramGrid, note);
        }

        private void FillPick(ComboBox pick)
        {
            bool was = loading;
            loading = true;
            try
            {
                pick.BeginUpdate();
                pick.Items.Clear();
                List<string> files = CustomCrosshair.List();
                string cur = StyleShapes.Of(Cur).PngFile;
                if (!string.IsNullOrEmpty(cur) && files.IndexOf(cur) < 0) files.Insert(0, cur);
                foreach (string f in files) pick.Items.Add(f);
                pick.SelectedIndex = string.IsNullOrEmpty(cur) ? -1 : pick.Items.IndexOf(cur);
                pick.EndUpdate();
            }
            finally { loading = was; }
        }

        private void Upload()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择准星图片（PNG）";
                dlg.Filter = "PNG 图片 (*.png)|*.png";
                dlg.CheckFileExists = true;
                dlg.Multiselect = false;
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (importing) return;
                // v5.0.2 (BRUTE P3-5): the picture is checked and copied on a background thread (an 8192 x 8192 PNG took
                // 5-7 s on the UI thread); the page says so meanwhile and applies the result when it is back.
                importing = true;
                UpdatePngStatus();
                Profile target = g.Active;
                bool forBoost = editBoost;
                CustomCrosshair.BeginImport(dlg.FileName, delegate(string name, string err)
                {
                    try
                    {
                        if (disposedAll || !IsHandleCreated) return;
                        BeginInvoke((Action)delegate { UploadDone(name, err, target, forBoost); });
                    }
                    catch (Exception) { }
                });
            }
        }

        /// <summary>True while an upload is being read on the background thread.</summary>
        private bool importing;

        /// <summary>v5.0.2: the upload's result, on the UI thread. The picture is used only when the page still shows the
        /// profile (and look) it was uploaded for; otherwise it is just in the list.</summary>
        private void UploadDone(string name, string err, Profile target, bool forBoost)
        {
            importing = false;
            if (disposedAll) return;
            if (name == null)
            {
                UpdatePngStatus();
                ThemedDialog.Show(FindForm(), err ?? "上传失败。", false);
                return;
            }
            if (target != g.Active || forBoost != editBoost)
            {
                shownTile = null;
                RefreshValues();
                return;
            }
            StyleTile now = StyleShapes.TileOf(Cur);
            if (now == null || now.Id != "custom")
            {
                if (now != null) tuned[now.Id] = Geo.Read(Cur);
                ApplyTileKeepBoost(Cur, "custom");
            }
            StyleShapes.Of(Cur).PngFile = name;
            shownTile = null; // rebuild the rows (picture list)
            RefreshValues();
            if (onChanged != null) { try { onChanged(); } catch (Exception) { } }
        }

        private void OpenFolder()
        {
            string err;
            if (!CustomCrosshair.EnsureFolder(out err))
            {
                ThemedDialog.Show(FindForm(), "无法创建文件夹：" + err, false); // v5.0.2 (UI-C L7): the app's own dialog
                return;
            }
            try { Process.Start("explorer.exe", "\"" + CustomCrosshair.Folder() + "\""); }
            catch (Exception ex) { ThemedDialog.Show(FindForm(), "没能打开文件夹：" + ex.Message, false); }
        }

        private void UpdatePngStatus()
        {
            string msg = null;
            if (importing) msg = "正在读取图片…";
            else if (IsCustom())
            {
                CrosshairStyle cs = StyleShapes.Of(Cur);
                if (string.IsNullOrEmpty(cs.PngFile)) msg = "还没有图片：点「上传 PNG…」选一张。没有图片时显示经典圆环。";
                else if (StyleShapes.Png(cs.PngFile) == null) msg = "找不到图片「" + cs.PngFile + "」（可能被删了），现在显示经典圆环。请重新上传。";
            }
            else if (StyleShapes.TileOf(Cur) == null)
                msg = "这是旧版的「实心点」样式：v4 起中心一定留空，建议在左边换一个样式。";
            lblStatus.Text = msg ?? "";
            lblStatus.Visible = msg != null;
        }

        // ================================================================ refresh

        /// <summary>Re-reads every control from g.Active (the UI calls this when the active profile changes).</summary>
        public void RefreshValues()
        {
            if (disposedAll || g.Active == null) return;
            if (editBoost)
            {
                // v5.0.2: another profile (or a restored one) -> its own 强化 look; else store the edits and read them back
                // (a change of 「跟随当前手的颜色」 shows the right colours at once)
                Profile a = g.Active;
                if (boostEditOf != a)
                {
                    if (a.Boost == null) a.Boost = new BoostStyle();
                    if (!a.Boost.OwnLook) { RingStyle rs = RingStyle.From(a, 0, true); a.BeginOwnBoost(rs.SGap, rs.SLength, rs.Hole); }
                    boostEdit = new Profile();
                    boostEditOf = a;
                    shownTile = null;
                }
                else a.StoreBoostView(boostEdit);
                a.FillBoostView(boostEdit);
            }
            bool was = loading;
            loading = true;
            BeginHold(); // v5.1.1 (P1): rows shown / hidden by the refreshers cost one layout in total
            try
            {
                lblHead.Text = editBoost ? "强化准星的样式（按强化键后）" : "准星样式";
                lblHead.ForeColor = editBoost ? Theme.ColWarn : Theme.ColAccent2;
                lblSub.Text = editBoost
                    ? "现在改的是强化准星：这一页的样式、大小、颜色、描边、发光都只改按强化键后的样子。中间一定留空。"
                    : "中间一定留空，游戏的中心小白点始终看得见。点一下就换上，预览马上变。";
                StyleTile t = StyleShapes.TileOf(Cur);
                SuspendLayout();
                RebuildParams(t);
                lblTitle.Text = t == null ? "旧版样式" : t.Label;
                lblHint.Text = t == null ? "" : t.Hint;
                foreach (Action a in paramRefreshers) { try { a(); } catch (Exception) { } }
                RunRefreshers(); // v5.1.1 (P1): a closed sub-page's rows are refreshed when it is opened
                foreach (TileButton b in tiles) b.Selected = t != null && b.Tile.Id == t.Id;
                UpdatePngStatus();
                ApplyVisibility(false);
                ResumeLayout(false);
            }
            finally { loading = was; EndHold(); } // EndHold = the one Relayout
            if (preview != null) preview.Invalidate();
            if (!switchingTab) ScheduleThumbs();
        }
        private bool switchingTab;

        /// <summary>高级设置 on/off: per-style fine-tuning rows, colour effect rows and the three fold sections.</summary>
        private void ApplyVisibility() { ApplyVisibility(true); }

        /// <summary>v5.1.1 (P1, R1 #2): RefreshValues has just run every refresher, so it skips the second pass.</summary>
        private void ApplyVisibility(bool runRefreshers)
        {
            foreach (Control c in advSections)
            {
                bool on = advanced && !(editBoost && handSections.Contains(c)); // 换手颜色 belongs to the normal look
                if (c.Visible != on) c.Visible = on;
            }
            foreach (Control[] row in advParamRows) Show(row, advanced);
            if (!runRefreshers) return;
            bool was = loading;
            loading = true;
            try { RunRefreshers(); }
            finally { loading = was; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (handlesWaiting) { handlesWaiting = false; idleQueued = false; QueuePrebuild(); }
            Relayout();
            RenderThumbs();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // v4.3.1: nothing is rebuilt when the page is shown again (fixed text size)
            // v5.1.1 (P1): thumbnails skipped while hidden are drawn right after the page shows (it paints first)
            if (Visible && thumbsOnShow && !disposedAll) { thumbsOnShow = false; BeginInvokeSafe(RenderThumbs); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposedAll)
            {
                disposedAll = true;
                if (wheel != null) { wheel.Dispose(); wheel = null; }
                thumbTimer.Stop();
                thumbTimer.Dispose();
                tips.Dispose();
                foreach (TileButton b in tiles) b.SetThumb(null);
                if (preview != null) preview.DropCache();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font f in fonts) if (f != null) f.Dispose();
            }
        }

        internal Font MonoFont { get { return fonts[RMono]; } }
        internal Font MonoSmallFont { get { return fonts[RMono]; } }
        internal Font SmallFont { get { return fonts[RSmall]; } }
        internal Font BodyFont { get { return fonts[RBody]; } }
        internal float K { get { return k; } }
        internal int UI(double v) { return U(v); }
        internal Profile Active { get { return Cur; } }

        // ================================================================ per-style memory (this session)

        private sealed class Geo
        {
            private double dia, thick, xgap, xlen, xthick, dashGap, gap, len, spread, boostDia;
            private int dashes, count;

            public static Geo Read(Profile p)
            {
                CrosshairStyle s = StyleShapes.Of(p);
                Geo q = new Geo();
                q.dia = p.Diameter; q.thick = p.Thickness; q.xgap = p.CrossGap; q.xlen = p.CrossLength; q.xthick = p.CrossThickness;
                q.dashes = p.Dashes; q.dashGap = p.DashGapPct; q.gap = s.Gap; q.len = s.Length; q.spread = s.Spread; q.count = s.Count;
                q.boostDia = p.Boost != null ? p.Boost.Diameter : 0;
                return q;
            }

            public void Write(Profile p)
            {
                CrosshairStyle s = StyleShapes.Of(p);
                p.Diameter = dia; p.Thickness = thick; p.CrossGap = xgap; p.CrossLength = xlen; p.CrossThickness = xthick;
                p.Dashes = dashes; p.DashGapPct = dashGap; s.Gap = gap; s.Length = len; s.Spread = spread; s.Count = count;
                if (p.Boost != null && boostDia > 0 && !p.Boost.OwnLook) p.Boost.Diameter = boostDia; // v5.0.2: an own 强化 look keeps its size
            }
        }

        // ================================================================ controls

        /// <summary>One style tile: thumbnail + name; accent frame when chosen.</summary>
        private sealed class TileButton : Control
        {
            private readonly StylePanel owner;
            public readonly StyleTile Tile;
            private Bitmap thumb;
            private bool hover, selected;

            public TileButton(StylePanel owner, StyleTile t)
            {
                this.owner = owner;
                Tile = t;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                AccessibleName = t.Label;
                AccessibleRole = AccessibleRole.PushButton;
            }

            /// <summary>Square thumbnail area: the tile width minus a small frame, above the name line.</summary>
            public int ImageSide { get { return Math.Max(16, Math.Min(Width - owner.UI(14), Height - owner.TileLabelHeight - owner.UI(7))); } }

            public bool Selected
            {
                get { return selected; }
                set { if (selected != value) { selected = value; Invalidate(); } }
            }

            public void SetThumb(Bitmap b)
            {
                Bitmap old = thumb;
                thumb = b;
                if (old != null) old.Dispose();
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) { Focus(); owner.OnTileClick(Tile); }
            }

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { owner.OnTileClick(Tile); e.Handled = true; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = ClientRectangle;
                Color bg = selected ? Theme.Blend(Theme.ColInput, Theme.ColAccent, 0.10) : (hover ? Theme.ColHover : Theme.ColInput);
                using (SolidBrush b = new SolidBrush(bg)) gr.FillRectangle(b, r);
                int side = ImageSide;
                Rectangle img = new Rectangle((r.Width - side) / 2, owner.UI(7), side, side);
                using (SolidBrush b = new SolidBrush(Theme.ColCard)) gr.FillRectangle(b, img); // v5.1 flat: theme colour
                using (Pen grid = new Pen(Theme.ColGrid))
                {
                    gr.DrawLine(grid, img.Left, img.Top + side / 2, img.Right - 1, img.Top + side / 2);
                    gr.DrawLine(grid, img.Left + side / 2, img.Top, img.Left + side / 2, img.Bottom - 1);
                }
                if (thumb != null) gr.DrawImage(thumb, img.Left, img.Top, side, side);
                else if (Tile.Id == "custom")
                {
                    using (SolidBrush b = new SolidBrush(Theme.ColAccent2))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                        gr.DrawString("+ PNG", owner.MonoFont, b, img, sf);
                    }
                }
                Rectangle txt = new Rectangle(r.Left + 2, img.Bottom, r.Width - 4, r.Bottom - img.Bottom);
                TextRenderer.DrawText(gr, Tile.Label, owner.SmallFont, txt, selected ? Theme.ColAccent : Theme.ColText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Color frame = selected ? Theme.ColAccent : (Focused ? Theme.ColAccent2 : Theme.ColBorder);
                using (Pen p = new Pen(frame, Theme.BorderPx)) // v5.1 flat: one border width everywhere
                {
                    p.Alignment = PenAlignment.Inset;
                    gr.SmoothingMode = SmoothingMode.None;
                    gr.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
                }
                if (selected)
                {
                    using (SolidBrush b = new SolidBrush(Theme.ColAccent))
                        gr.FillPolygon(b, new Point[] { new Point(r.Right - owner.S(14), 0), new Point(r.Right, 0), new Point(r.Right, owner.S(14)) });
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && thumb != null) { thumb.Dispose(); thumb = null; }
                base.Dispose(disposing);
            }
        }

        /// <summary>Live preview: the crosshair magnified (pixel-exact) and at real size over a game-like wall.</summary>
        private sealed class PreviewBox : Control
        {
            private readonly StylePanel owner;
            private int mode; // 0 正常, 1 换手, 2 强化
            private Bitmap cache;
            private string cacheKey;
            private int cacheN;
            /// <summary>v4.1 (REBRUTE41 P2-2): the cached raster cannot be seen (StyleFooter.Invisible).</summary>
            private bool cacheInvisible;
            private readonly Rectangle[] modeRects = new Rectangle[3];
            private readonly LookExtras previewExtras = new LookExtras(); // v5.1 (W22): ammo hint geometry for the preview
            private bool previewAmmo;   // the cached bitmap holds the hint (opacity already in its pixels)
            private static readonly string[] ModeNames = new string[] { "正常", "换手", "强化" };

            public PreviewBox(StylePanel owner)
            {
                this.owner = owner;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public void DropCache()
            {
                if (cache != null) { cache.Dispose(); cache = null; }
                cacheKey = null;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                for (int i = 0; i < 3; i++)
                    if (modeRects[i].Contains(e.Location)) { mode = i; Invalidate(); }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                bool over = false;
                for (int i = 0; i < 3; i++) if (modeRects[i].Contains(e.Location)) over = true;
                Cursor = over ? Cursors.Hand : Cursors.Default;
            }

            /// <summary>v5.0.2: 0 正常, 1 换手, 2 强化 (the page selects 强化 while it edits the 强化 look).</summary>
            public void SetMode(int m) { mode = Math.Max(0, Math.Min(2, m)); Invalidate(); }

            private RingStyle CurrentStyle()
            {
                Profile p = owner.Active;
                if (p == null) return null;
                // v5.0.2: while the page edits the 强化 look, p already IS that look (drawn unboosted); a 强化 look of its own
                // (OwnLook) is drawn from Profile.FillBoostView, the same way the overlay draws it
                if (owner.EditingBoost) return RingStyle.From(p, mode == 1 ? 1.0 : 0.0, false);
                if (mode == 2 && p.Boost != null && p.Boost.OwnLook) return RingStyle.From(owner.BoostPreviewOf(p), 0, false);
                return RingStyle.From(p, mode == 1 ? 1.0 : 0.0, mode == 2);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics gr = e.Graphics;
                Rectangle r = ClientRectangle;
                // a dim "wall": vertical gradient + grid + scanlines
                using (SolidBrush wall = new SolidBrush(Theme.ColCard)) gr.FillRectangle(wall, r); // v5.1 flat: solid theme colour
                using (Pen grid = new Pen(Color.FromArgb(40, Theme.ColAccent)))
                {
                    int step = owner.S(24);
                    for (int x = r.Left; x < r.Right; x += step) gr.DrawLine(grid, x, r.Top, x, r.Bottom);
                    for (int y = r.Top; y < r.Bottom; y += step) gr.DrawLine(grid, r.Left, y, r.Right, y);
                }
                try { Theme.DrawScanlines(gr, r, 18); } catch (Exception) { }

                RingStyle st = null;
                int[] px = null;
                int n = 0;
                try
                {
                    st = CurrentStyle();
                    if (st != null)
                    {
                        // v5.1 (W22): with 弹药提示 on, the preview shows the hint (大半) in its real place and colours; the
                        // crosshair's 不透明度 is then put into its own pixels so the hint keeps its own opacity
                        bool ammo = owner.g.AmmoOn;
                        n = RingRaster.CanvasSize(st);
                        if (ammo)
                        {
                            AmmoView av = new AmmoView(); av.Active = true; av.Band = 3; av.Progress = double.NaN;
                            previewExtras.Frame(st, new AnimOptions(), new AnimInput(), true, av, owner.g.AmmoStyle, previewExtras.BarFrom(owner.g), 0, 0);
                            n = previewExtras.Canvas;
                        }
                        string key = st.Key() + "|" + n + (ammo ? "|a" + previewExtras.Ammo.Key + "|" + st.OpacityPct.ToString(CultureInfo.InvariantCulture) : "");
                        if (key != cacheKey || cache == null)
                        {
                            px = RingRaster.Render(st, n, n / 2.0, n / 2.0);
                            bool invisibleLook = StyleFooter.Invisible(st, px); // the crosshair alone (not the hint)
                            if (ammo)
                            {
                                FadePixels.Scale(px, px, n * n, FrameVisibility.Level(st.OpacityPct, 1.0));
                                previewExtras.Ammo.Draw(px, n, n / 2.0, n / 2.0, previewExtras.Inner, previewExtras.AmmoStyle, 255);
                            }
                            previewAmmo = ammo;
                            DropCache();
                            cache = CustomCrosshair.ToBitmap(px, n, n);
                            cacheKey = key;
                            cacheN = n;
                            cacheInvisible = invisibleLook;
                        }
                    }
                }
                catch (Exception) { DropCache(); }

                Font capF = owner.SmallFont, btnF = owner.SmallFont; // v5.1 (decision 9): Chinese captions never in the mono font
                int bottom = Math.Max(owner.S(26), Math.Max(capF.Height, btnF.Height) + owner.UI(12));
                int pad = owner.UI(8);
                Rectangle body = new Rectangle(r.Left, r.Top, r.Width, r.Height - bottom);
                int realW = Math.Max(owner.UI(90), (int)(body.Width * 0.30));
                Rectangle zoomArea = new Rectangle(body.Left + pad, body.Top + pad, body.Width - realW - 3 * pad, body.Height - 2 * pad);
                Rectangle realArea = new Rectangle(zoomArea.Right + 2 * pad, zoomArea.Top, body.Right - zoomArea.Right - 3 * pad, zoomArea.Height);
                float opacity = st == null || previewAmmo ? 1f : (float)Math.Max(0.05, Math.Min(1.0, st.OpacityPct / 100.0));
                using (Pen sep = new Pen(Theme.ColBorder))
                    gr.DrawLine(sep, zoomArea.Right + pad, body.Top + pad, zoomArea.Right + pad, body.Bottom - pad);
                if (cache != null)
                {
                    int z = Math.Max(1, Math.Min(12, Math.Min(zoomArea.Width, zoomArea.Height - 2 * capF.Height) / Math.Max(1, cacheN)));
                    DrawAt(gr, zoomArea, z, opacity);
                    DrawAt(gr, realArea, 1, opacity);
                    Caption(gr, "放大 ×" + z, capF, zoomArea.Left, zoomArea.Top);
                    Caption(gr, "实际大小", capF, realArea.Left, realArea.Top);
                }

                // footer: centre status + 正常 / 换手 / 强化
                int fy = r.Bottom - bottom;
                using (SolidBrush fb = new SolidBrush(Theme.ColHeader)) gr.FillRectangle(fb, r.Left, fy, r.Width, bottom);
                using (Pen sep = new Pen(Theme.ColBorder)) gr.DrawLine(sep, r.Left, fy, r.Right, fy);
                int gapB = owner.UI(4), bh = btnF.Height + owner.UI(6), bw = 0;
                for (int i = 0; i < 3; i++) bw = Math.Max(bw, TextWidthCache.Width(ModeNames[i], btnF) + owner.UI(18)); // v5.1.1 (P1): cached
                int x0 = r.Right - pad - 3 * bw - 2 * gapB;
                // v4.1 (REBRUTE41 P2-2): a look nobody can see (RingStyle.Hidden / no visible pixel) says so, in amber, no ✓
                bool invisible = st != null && (st.Hidden || (cache != null && cacheInvisible));
                string hole = "";
                int holeW = Math.Max(0, x0 - r.Left - 2 * pad);
                if (st != null)
                    foreach (string c in StyleFooter.Candidates(st.Hole, invisible))
                    {
                        hole = c;
                        if (TextWidthCache.Width(c, capF) <= holeW) break;
                    }
                TextRenderer.DrawText(gr, hole, capF, new Rectangle(r.Left + pad, fy, holeW, bottom), invisible ? Theme.ColWarn : Theme.ColAccent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                for (int i = 0; i < 3; i++)
                {
                    Rectangle br = new Rectangle(x0 + i * (bw + gapB), fy + (bottom - bh) / 2, bw, bh);
                    modeRects[i] = br;
                    bool on = mode == i;
                    using (SolidBrush b = new SolidBrush(on ? Theme.Blend(Theme.ColInput, Theme.ColAccent, 0.3) : Theme.ColInput)) gr.FillRectangle(b, br);
                    using (Pen p = new Pen(on ? Theme.ColAccent : Theme.ColBorder)) gr.DrawRectangle(p, br.X, br.Y, br.Width - 1, br.Height - 1);
                    TextRenderer.DrawText(gr, ModeNames[i], btnF, br, on ? Theme.ColAccent : Theme.ColText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                using (Pen fr = new Pen(Theme.ColBorder)) gr.DrawRectangle(fr, 0, 0, r.Width - 1, r.Height - 1);
            }

            /// <summary>Small caption on a dark backing so it reads on the wall (contrast ≥ 4.5:1).</summary>
            private void Caption(Graphics gr, string text, Font f, int x, int y)
            {
                Size sz = TextRenderer.MeasureText(text, f);
                Rectangle bg = new Rectangle(x, y, sz.Width + owner.UI(4), sz.Height + owner.UI(2));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, Theme.ColHeader))) gr.FillRectangle(b, bg);
                TextRenderer.DrawText(gr, text, f, bg, Theme.ColText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            /// <summary>Crosshair magnified z × (nearest neighbour = the real pixels) + the game's 2 px centre dot.</summary>
            private void DrawAt(Graphics gr, Rectangle area, int z, float opacity)
            {
                int w = cacheN * z;
                // pixel-exact: the crosshair centre (cacheN / 2 in the raster) lands on the area centre
                int cxp = area.Left + area.Width / 2, cyp = area.Top + area.Height / 2;
                int x = cxp - (cacheN / 2) * z, y = cyp - (cacheN / 2) * z;
                // the game's own centre dot (white, ~2 px) -- always visible through the hole
                using (SolidBrush wb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    gr.FillRectangle(wb, cxp - z, cyp - z, 2 * z, 2 * z);
                InterpolationMode im = gr.InterpolationMode;
                PixelOffsetMode pm = gr.PixelOffsetMode;
                gr.InterpolationMode = InterpolationMode.NearestNeighbor;
                gr.PixelOffsetMode = PixelOffsetMode.Half;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = opacity;
                    ia.SetColorMatrix(cm);
                    gr.DrawImage(cache, new Rectangle(x, y, w, w), 0, 0, cacheN, cacheN, GraphicsUnit.Pixel, ia);
                }
                gr.InterpolationMode = im;
                gr.PixelOffsetMode = pm;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) DropCache();
                base.Dispose(disposing);
            }
        }

        /// <summary>Flat neon slider (mouse drag, wheel, arrow keys / PageUp / PageDown / Home / End).</summary>
        private sealed class HackSlider : Control
        {
            private readonly double min, max, step;
            private double value;
            private bool dragging, hover;
            public event EventHandler ValueChanged;

            public HackSlider(double min, double max, double step)
            {
                this.min = min; this.max = Math.Max(max, min + 1e-9); this.step = step > 0 ? step : 1;
                value = min;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
            }

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
                dragging = false;
                Capture = false;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { dragging = false; Invalidate(); base.OnLostFocus(e); }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (!Focused) return; // never steal the page scroll from a slider the user is not using
                SetByUser(value + (e.Delta > 0 ? step : -step));
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
                using (SolidBrush b = new SolidBrush(dragging || hover ? Theme.ColAccent2 : acc)) gr.FillRectangle(b, knob);
                using (Pen p = new Pen(Theme.ColBackground)) gr.DrawRectangle(p, knob.X, knob.Y, knob.Width - 1, knob.Height - 1);
                if (Focused)
                    using (Pen fp = new Pen(Theme.ColAccent2, 1f)) gr.DrawRectangle(fp, 0, 0, Width - 1, Height - 1); // v5.1 flat: solid 1 px
            }
        }

        /// <summary>Colour button: swatch + hex.</summary>
        /// <summary>v5.1.1 (X3, Leo B1): the 外观 sub-page bar: 准星样式 / 准星动态 / 弹药提示.</summary>
        internal sealed class SubTabs : Control
        {
            private readonly StylePanel owner;
            public static readonly string[] Names = { "准星样式", "准星动态", "弹药提示" };
            public event Action<int> Picked;
            public int Selected;
            private int hover = -1;

            public SubTabs(StylePanel owner)
            {
                this.owner = owner;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            /// <summary>Width of one tab from the real font metrics (+ margin), so no text is clipped.</summary>
            public int TabW
            {
                get
                {
                    if (tabW < 0)
                    {
                        int w = 0; // v5.1.1 (P1, R1 #5): fixed text size: measured once
                        foreach (string n in Names) w = Math.Max(w, TextRenderer.MeasureText(n, owner.BodyFont).Width);
                        tabW = w + owner.S(36);
                    }
                    return tabW;
                }
            }
            private int tabW = -1, prefH = -1;

            public int PreferredH { get { if (prefH < 0) prefH = TextRenderer.MeasureText("准星", owner.BodyFont).Height + owner.S(14); return prefH; } }

            public Rectangle TabRect(int i) { return new Rectangle(i * (TabW + owner.S(4)), 0, TabW, Height - 1); }

            private int Hit(Point p) { for (int i = 0; i < Names.Length; i++) if (TabRect(i).Contains(p)) return i; return -1; }

            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
            protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); int h = Hit(e.Location); if (h >= 0) Pick(h); }
            protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Left) { Pick((Selected + Names.Length - 1) % Names.Length); e.Handled = true; }
                else if (e.KeyCode == Keys.Right) { Pick((Selected + 1) % Names.Length); e.Handled = true; }
            }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            public void Pick(int i)
            {
                if (i < 0 || i >= Names.Length) return;
                Selected = i;
                Invalidate();
                Action<int> p = Picked;
                if (p != null) p(i);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics gr = e.Graphics;
                using (SolidBrush bg = new SolidBrush(Theme.ColSurface)) gr.FillRectangle(bg, ClientRectangle);
                for (int i = 0; i < Names.Length; i++)
                {
                    Rectangle r = TabRect(i);
                    bool on = i == Selected;
                    using (SolidBrush b = new SolidBrush(on ? Theme.ColPressed : (hover == i ? Theme.ColHover : Theme.ColInput))) gr.FillRectangle(b, r);
                    TextRenderer.DrawText(gr, Names[i], owner.BodyFont, r, on ? Theme.ColAccent : Theme.ColText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    using (Pen p = new Pen(on ? Theme.ColAccent : Theme.ColBorder, on ? Math.Max(2, owner.S(2)) : 1))
                    {
                        p.Alignment = PenAlignment.Inset;
                        gr.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    }
                    if (on && Focused) using (Pen p = new Pen(Theme.ColAccent2)) gr.DrawRectangle(p, r.X + 3, r.Y + 3, r.Width - 7, r.Height - 7);
                }
            }
        }

        /// <summary>v5.1.1 (X3, Leo B3): the bar presets as tiles drawn with the real bar pixels (大半, the user's colours).</summary>
        private sealed class AmmoPresetPicker : Control
        {
            private readonly StylePanel owner;
            public event Action<int> Picked;
            private readonly Bitmap[] thumbs = new Bitmap[AmmoBarSettings.Presets.Length];
            private readonly int[] thumbN = new int[AmmoBarSettings.Presets.Length];
            private AmmoBarSettings thumbSrc;
            private string thumbStyle;
            private int hover = -1;
            public const double ThumbInner = 10;

            public AmmoPresetPicker(StylePanel owner)
            {
                this.owner = owner;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                Height = TileH;
            }

            private int Count { get { return AmmoBarSettings.Presets.Length; } }
            // v5.1.1 (P1, R1 #5): fixed text size: measured once (these are read in every hit test and paint)
            private int tileW = -1, labelH = -1;
            private int TileW { get { if (tileW < 0) tileW = Math.Max(owner.S(96), TextRenderer.MeasureText("CF 风格", owner.SmallFont).Width + owner.S(24)); return tileW; } }
            private int LabelH { get { if (labelH < 0) labelH = TextRenderer.MeasureText("CF 风格", owner.SmallFont).Height + owner.S(6); return labelH; } }
            private int TileH { get { return owner.S(64) + LabelH; } }
            private int Gap { get { return owner.S(6); } }
            private int Cols { get { return Math.Max(1, Math.Min(Count, (Width + Gap) / (TileW + Gap))); } }
            public int Selected { get { return Array.IndexOf(AmmoBarSettings.Presets, owner.Bar.PresetOf()); } }

            public int HeightFor(int width)
            {
                int cols = Math.Max(1, Math.Min(Count, (width + Gap) / (TileW + Gap)));
                int rows = (Count + cols - 1) / cols;
                return rows * TileH + (rows - 1) * Gap;
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); int h = HeightFor(Width); if (Height != h) Height = h; }

            public Rectangle TileRect(int i) { int c = Cols; return new Rectangle((i % c) * (TileW + Gap), (i / c) * (TileH + Gap), TileW, TileH); }
            private int Hit(Point p) { for (int i = 0; i < Count; i++) if (TileRect(i).Contains(p)) return i; return -1; }

            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
            protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); int h = Hit(e.Location); if (h >= 0) Pick(h); }
            protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int s = Math.Max(0, Selected);
                if (e.KeyCode == Keys.Left) { Pick((s + Count - 1) % Count); e.Handled = true; }
                else if (e.KeyCode == Keys.Right) { Pick((s + 1) % Count); e.Handled = true; }
            }

            private void Pick(int i) { Action<int> p = Picked; if (p != null) p(i); Invalidate(); }

            /// <summary>v5.1.1 (P1): draws the preset pictures now (an idle step), so the first paint only copies them.</summary>
            public void Warm() { try { Build(); } catch (Exception) { Drop(); } }

            private void Build()
            {
                AmmoBarSettings b = owner.Bar;
                if (thumbSrc != null && thumbs[0] != null && thumbStyle == owner.g.AmmoStyle && SameColours(b, thumbSrc)) return;
                Drop();
                thumbSrc = b.Clone(); thumbStyle = owner.g.AmmoStyle;
                int style = AmmoDisplay.ParseStyle(owner.g.AmmoStyle);
                for (int i = 0; i < Count; i++)
                {
                    AmmoBarSettings s = b.Clone();
                    AmmoBarSettings.ApplyPreset(s, AmmoBarSettings.Presets[i]);
                    s.Pos = "below"; s.ShowBelowOn = false; s.Warn1.On = false; s.Warn2.On = false;
                    AmmoBarCfg c = AmmoBarCfg.From(s);
                    AmmoDisplay d = new AmmoDisplay();
                    AmmoView v = new AmmoView(); v.Active = true; v.Band = 3; v.Progress = double.NaN; v.Share = 0.7;
                    d.Update(v, 0, style, c);
                    int n = AmmoDisplay.CanvasNeed(ThumbInner, style, c);
                    int[] px = new int[n * n];
                    d.Draw(px, n, n / 2.0, n / 2.0, ThumbInner, style, 255);
                    thumbs[i] = CustomCrosshair.ToBitmap(px, n, n);
                    thumbN[i] = n;
                }
            }

            private static bool SameColours(AmmoBarSettings a, AmmoBarSettings b)
            {
                if (a.Colour != b.Colour || a.OpacityPct != b.OpacityPct) return false;
                for (int i = 0; i < AmmoBarSettings.BandCount; i++) if (a.BandColours[i] != b.BandColours[i]) return false;
                return true;
            }

            private void Drop() { for (int i = 0; i < thumbs.Length; i++) if (thumbs[i] != null) { thumbs[i].Dispose(); thumbs[i] = null; } thumbSrc = null; }

            protected override void Dispose(bool disposing) { if (disposing) Drop(); base.Dispose(disposing); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics gr = e.Graphics;
                using (SolidBrush bg = new SolidBrush(Theme.ColCard)) gr.FillRectangle(bg, ClientRectangle);
                try { Build(); } catch (Exception) { Drop(); }
                int sel = Selected, lh = LabelH;
                Font f = owner.SmallFont;
                for (int i = 0; i < Count; i++)
                {
                    Rectangle r = TileRect(i);
                    bool on = i == sel;
                    using (SolidBrush b = new SolidBrush(on ? Theme.ColPressed : (hover == i ? Theme.ColHover : Theme.ColInput))) gr.FillRectangle(b, r);
                    Rectangle img = new Rectangle(r.X + owner.S(4), r.Y + owner.S(4), r.Width - owner.S(8), r.Height - lh - owner.S(6));
                    Bitmap bm = thumbs[i];
                    if (bm != null && img.Width > 0 && img.Height > 0)
                    {
                        int n = thumbN[i];
                        float sc = Math.Min(Math.Min(img.Width / (float)n, img.Height / (float)n) * 1.3f, 2f * owner.k);
                        float w = n * sc, cxp = img.X + img.Width / 2f, cyp = img.Y + img.Height / 2f - w * 0.12f;
                        GraphicsState gs = gr.Save();
                        gr.SetClip(img);
                        gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        gr.DrawImage(bm, cxp - w / 2, cyp - w / 2, w, w);
                        float rr = (float)(ThumbInner - 4) * sc;
                        gr.SmoothingMode = SmoothingMode.AntiAlias;
                        using (Pen p = new Pen(Theme.ColText, Math.Max(1f, 1.3f * sc))) gr.DrawEllipse(p, cxp - rr, cyp - rr, 2 * rr, 2 * rr);
                        gr.Restore(gs);
                    }
                    Rectangle lr = new Rectangle(r.X, r.Bottom - lh - owner.S(2), r.Width, lh);
                    TextRenderer.DrawText(gr, AmmoBarSettings.PresetNames[i], f, lr, on ? Theme.ColAccent : Theme.ColText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    using (Pen p = new Pen(on ? Theme.ColAccent : Theme.ColBorder, on ? Math.Max(2, owner.S(2)) : 1))
                    {
                        p.Alignment = PenAlignment.Inset;
                        gr.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    }
                }
            }
        }

        /// <summary>
        /// v5.1.1 (X3, Leo B2 / B3): live preview around the current crosshair, drawn with the overlay's own code (LookExtras).
        /// Anim mode loops 静止 → 开火 → 移动 → 边走边开火 (buttons fire a burst). Ammo mode drains a mag, reloads, and the
        /// buttons play 普通换弹 / 快速换弹 / 低弹警告 / 紧急警告 with the real reload times. Ticks only while it is on screen.
        /// </summary>
        internal sealed class FxLivePreview : Control
        {
            private readonly StylePanel owner;
            private readonly bool ammoMode;
            private readonly Timer timer = new Timer();
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly LookExtras ex = new LookExtras();
            private Bitmap bmp;
            private int bmpN, lastKey = int.MinValue;
            private int[] work;
            private string caption = "", lastCap;
            private double lastS;
            // ammo sim
            private double share = 1, holdUntil, reloadStart = -1, reloadMs;
            private int reloadSeq, reloadKind, warnDemo;
            // anim sim
            private double burstUntil = -1;
            public const double AnimCycleS = 9;

            public FxLivePreview(StylePanel owner, bool ammo)
            {
                this.owner = owner;
                ammoMode = ammo;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Height = owner.S(150);
                timer.Interval = 33;
                timer.Tick += delegate { Tick(); };
            }

            private double Now { get { return clock.Elapsed.TotalSeconds; } }

            public void Reload(int kind)
            {
                reloadKind = kind; reloadSeq++; reloadStart = Now; warnDemo = 0;
                reloadMs = kind == 2 ? owner.g.AmmoQuickReloadMs : owner.g.AmmoReloadMs;
                if (!(reloadMs > 0)) reloadMs = kind == 2 ? AmmoDisplay.QuickReloadMs : AmmoDisplay.NormalReloadMs;
                Sync(); Tick();
            }

            public void Warn(int level)
            {
                AmmoWarnSettings w = level == 2 ? owner.Bar.Warn2 : owner.Bar.Warn1;
                reloadStart = -1;
                share = Math.Max(0.005, w.Pct / 100.0 - 0.02);
                holdUntil = Now + 3.5; warnDemo = w.On ? level : -level;
                Sync(); Tick();
            }

            public void Burst(int rounds)
            {
                double per = 60.0 / Math.Max(60, owner.g.AmmoRpm);
                burstUntil = Now + rounds * per;
                Sync(); Tick();
            }

            /// <summary>Anim mode script at t seconds: firing, moving and the caption (pure; tests use it).</summary>
            internal static void AnimScript(double tS, out bool firing, out bool moving, out string cap)
            {
                double t = tS % AnimCycleS; if (t < 0) t += AnimCycleS;
                firing = (t >= 1.2 && t < 3.2) || (t >= 6.0 && t < 7.6);
                moving = t >= 4.4 && t < 7.6;
                cap = firing && moving ? "边走边开火" : (firing ? "开火" : (moving ? "移动" : "静止"));
            }

            private bool Live()
            {
                if (!Visible || !IsHandleCreated || Width < 8 || Height < 8) return false;
                Form f = FindForm();
                return f != null && f.Visible && f.WindowState != FormWindowState.Minimized;
            }

            private void Sync() { bool on = Live(); if (on && !timer.Enabled) { lastS = Now; timer.Start(); } else if (!on && timer.Enabled) timer.Stop(); }

            protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Sync(); }
            protected override void OnParentVisibleChanged(EventArgs e) { base.OnParentVisibleChanged(e); Sync(); }
            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Sync(); }
            protected override void OnHandleDestroyed(EventArgs e) { timer.Stop(); base.OnHandleDestroyed(e); }

            private void Tick()
            {
                if (!Live()) { timer.Stop(); return; }
                timer.Interval = owner.g.PerfMode ? 100 : 33; // 性能模式: the preview at 10 fps
                try { Render(); } catch (Exception) { lastKey = int.MinValue; }
            }

            private void Render()
            {
                Profile p = owner.Active;
                if (p == null) return;
                double now = Now, dt = Math.Max(0, Math.Min(0.1, now - lastS));
                lastS = now;
                RingStyle st = RingStyle.From(p, 0, false);
                AnimInput ai = new AnimInput(); ai.MoveLevel = double.NaN; ai.ShotSeq = -1; ai.Rpm = owner.g.AmmoRpm; ai.Semi = owner.g.AmmoSemi;
                AmmoView av = new AmmoView(); av.Progress = double.NaN;
                AnimOptions o = new AnimOptions();
                string cap;
                if (ammoMode)
                {
                    av.Active = true;
                    if (reloadStart >= 0)
                    {
                        double el = (now - reloadStart) * 1000;
                        if (el >= reloadMs) { reloadStart = -1; share = 1; holdUntil = now + 1.2; }
                        else { av.Reloading = true; av.Progress = el / reloadMs; }
                    }
                    if (reloadStart < 0 && now >= holdUntil)
                    {
                        share -= dt / 6.0; // a mag in ~6 s
                        if (share <= 0.02) { share = 0.02; Reload(reloadSeq % 2 == 0 ? 1 : 2); return; }
                    }
                    av.Share = share;
                    av.Band = AmmoModel.BandOf((int)Math.Round(share * 1000), 1000);
                    av.ReloadSeq = reloadSeq; av.ReloadKind = reloadKind; av.ReloadMs = reloadMs;
                    if (reloadStart >= 0) cap = reloadKind == 2 ? "快速换弹中" : "普通换弹中";
                    else if (warnDemo < 0) cap = (warnDemo == -2 ? "紧急警告" : "低弹警告") + "没有开启";
                    else
                    {
                        cap = AmmoDisplay.BandNames[Math.Max(0, Math.Min(4, av.Band))];
                        if (now < holdUntil && warnDemo > 0) cap += warnDemo == 2 ? " · 紧急警告" : " · 低弹警告";
                    }
                }
                else
                {
                    bool firing, moving;
                    AnimScript(now, out firing, out moving, out cap);
                    if (now < burstUntil) { firing = true; moving = false; cap = "开火"; }
                    ai.Firing = firing; ai.Moving = moving;
                    o = ex.AnimFrom(owner.g);
                }
                ex.Frame(st, o, ai, ammoMode, av, owner.g.AmmoStyle, ex.BarFrom(owner.g), dt, now * 1000.0);
                int n = ex.Canvas;
                int key;
                unchecked { key = st.Key().GetHashCode() * 31 + n; key = key * 31 + ex.Anim.Key; key = key * 31 + (ex.FxActive ? ex.Fx.Key : 0); key = key * 31 + (ex.Ammo.Visible ? ex.Ammo.Key : 0); }
                if (key == lastKey && cap == lastCap && bmp != null) return;
                lastKey = key; lastCap = cap; caption = cap;
                int[] px = RingRaster.Render(st, n, n / 2.0, n / 2.0);
                FadePixels.Scale(px, px, n * n, FrameVisibility.Level(st.OpacityPct, 1.0));
                if (ex.FxActive) ex.Fx.Draw(px, n, n / 2.0, n / 2.0, ex.Reach, 255);
                if (ammoMode) ex.Ammo.Draw(px, n, n / 2.0, n / 2.0, ex.Inner, ex.AmmoStyle, 255);
                work = px;
                if (bmp == null || bmpN != n) { if (bmp != null) bmp.Dispose(); bmp = new Bitmap(n, n, PixelFormat.Format32bppPArgb); bmpN = n; }
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < n; y++)
                        System.Runtime.InteropServices.Marshal.Copy(work, y * n, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), n);
                }
                finally { bmp.UnlockBits(d); }
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { timer.Stop(); timer.Dispose(); if (bmp != null) { bmp.Dispose(); bmp = null; } }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics gr = e.Graphics;
                Rectangle r = ClientRectangle;
                using (SolidBrush b = new SolidBrush(Theme.ColInput)) gr.FillRectangle(b, r);
                if (bmp == null) { try { Render(); } catch (Exception) { } }
                if (bmp != null)
                {
                    int pad = owner.S(6);
                    float sc = Math.Min((r.Height - 2 * pad) / (float)bmpN, 3f * owner.k);
                    if (sc > 0)
                    {
                        float w = bmpN * sc;
                        gr.InterpolationMode = sc >= 1.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                        gr.PixelOffsetMode = PixelOffsetMode.Half;
                        gr.DrawImage(bmp, r.X + r.Width / 2f - w / 2, r.Y + r.Height / 2f - w / 2, w, w);
                    }
                }
                Font f = owner.SmallFont;
                string text = "预览：" + caption;
                Size ts = TextRenderer.MeasureText(text, f);
                TextRenderer.DrawText(gr, text, f, new Rectangle(r.X + owner.S(6), r.Y + owner.S(4), Math.Max(ts.Width + 4, r.Width - owner.S(12)), ts.Height + 2),
                    Theme.ColMuted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                using (Pen p = new Pen(Theme.ColBorder)) gr.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        private sealed class Swatch : Control
        {
            private readonly StylePanel owner;
            public Func<string> Get;
            private bool hover;

            public Swatch(StylePanel owner)
            {
                this.owner = owner;
                Tag = "theme:keep";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics gr = e.Graphics;
                Rectangle r = ClientRectangle;
                using (SolidBrush b = new SolidBrush(hover ? Theme.ColHover : Theme.ColInput)) gr.FillRectangle(b, r);
                string hex = Get == null ? "" : Get();
                int cr, cg, cb;
                if (!ColorUtil.TryParse(hex, out cr, out cg, out cb)) { cr = cg = cb = 255; }
                int s = r.Height - 8;
                Rectangle sw = new Rectangle(4, 4, s * 2, s);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(cr, cg, cb))) gr.FillRectangle(b, sw);
                using (Pen p = new Pen(Theme.ColMuted)) gr.DrawRectangle(p, sw.X, sw.Y, sw.Width - 1, sw.Height - 1);
                TextRenderer.DrawText(gr, (hex ?? "").ToUpperInvariant(), owner.MonoFont, new Rectangle(sw.Right + 6, 0, r.Width - sw.Right - 6, r.Height),
                    Theme.ColText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                using (Pen p = new Pen(Focused ? Theme.ColAccent2 : (hover ? Theme.ColAccent : Theme.ColBorder))) gr.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        /// <summary>The one scrolled page: children are placed by StylePanel.DoLayout (no docking, no inner scrollers).</summary>
        private sealed class PagePanel : Panel
        {
            public Action DoLayoutNow;

            public PagePanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Tag = "theme:keep";
            }

            protected override void OnLayout(LayoutEventArgs levent)
            {
                Action a = DoLayoutNow;
                if (a != null) a();
            }
        }

        /// <summary>Collapsible section: a "▸ title" header line that shows / hides its body.</summary>
        private sealed class Section : TableLayoutPanel
        {
            public Section(StylePanel owner, string title, Control body, bool open)
            {
                owner.Hold(this); // v5.1.1 (P1): layout held while the page is built
                ColumnCount = 1;
                RowCount = 2;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Dock = DockStyle.Top;
                Margin = new Padding(0, owner.S(6), 0, 0);
                Padding = new Padding(0);
                BackColor = Color.Transparent;
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                // same look and wording as the settings pages' folds: 「▸ 可选：名字 ------」
                FoldHeader head = new FoldHeader("可选：" + title, owner.k);
                head.Font = owner.BodyFont;
                head.Height = head.GetPreferredSize(Size.Empty).Height;
                // v4.3 (V43-OVERLAP): at least as wide as its own text (was 365 px for 420 px of text at 200 %)
                Action fit = delegate
                {
                    int tw = TextRenderer.MeasureText(head.Text, head.Font).Width + (int)Math.Round(60 * owner.k);
                    head.MinimumSize = new Size(tw, head.Height);
                };
                fit();
                head.FontChanged += delegate { head.Height = head.GetPreferredSize(Size.Empty).Height; fit(); };
                head.Dock = DockStyle.Top;
                head.Margin = new Padding(0);
                owner.folds.Add(head);
                head.Toggled += delegate { body.Visible = head.Open; owner.Relayout(); };
                body.Dock = DockStyle.Top;
                body.Visible = open;
                Controls.Add(head, 0, 0);
                Controls.Add(body, 0, 1);
                if (open) head.Open = true;
            }
        }
    }
}
