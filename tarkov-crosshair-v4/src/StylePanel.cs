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
                foreach (FoldHeader h in folds) h.Open = value; // 高级设置 on = the 可选 sections open (like the other pages)
                ApplyVisibility();
                Relayout();
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
            thumbTimer.Tick += delegate { thumbTimer.Stop(); RenderThumbs(); };

            page = new PagePanel();
            page.Dock = DockStyle.Top;
            page.Margin = new Padding(0);
            page.Padding = new Padding(0);
            page.BackColor = Theme.ColSurface;
            page.Height = S(600);
            page.DoLayoutNow = DoLayout;
            page.LocationChanged += delegate { PinRight(); };
            Controls.Add(page);

            BuildHeader();
            BuildTiles();
            BuildRight();
            ApplySizes();
            ResumeLayout(false);
            RefreshValues();
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
            return t;
        }

        private void Changed()
        {
            if (loading) return;
            if (onChanged != null) { try { onChanged(); } catch (Exception) { } }
            if (preview != null) preview.Invalidate();
            ScheduleThumbs();
        }

        private void ScheduleThumbs()
        {
            thumbTimer.Stop();
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
                numW = Math.Max(numW, TextRenderer.MeasureText(p.Value, mono).Width);
            numW += SystemInformation.VerticalScrollBarWidth + U(14);
            foreach (KeyValuePair<NumericUpDown, string> p in nums)
                if (p.Key.Width != numW) p.Key.Width = numW;
            gridLabels.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            unitLabels.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            SameWidth(gridLabels);
            SameWidth(unitLabels);
            int sh = Math.Max(S(22), body.Height + S(4));
            foreach (Control c in sliders) { c.MinimumSize = new Size(U(90), sh); c.Height = sh; }
            foreach (Control c in swatches) c.Size = new Size(TextRenderer.MeasureText("#888888", mono).Width + U(60), Math.Max(S(24), mono.Height + S(8)));
            foreach (FoldHeader h in folds)
            {
                h.Font = body;
                int hh = h.GetPreferredSize(Size.Empty).Height;
                h.MinimumSize = new Size(0, hh);
                h.Height = hh;
            }
        }

        /// <summary>Gives every label of the list the width of the widest (hidden rows included, so nothing jumps).</summary>
        private static void SameWidth(List<Label> labels)
        {
            int w = 0;
            foreach (Label l in labels) w = Math.Max(w, TextRenderer.MeasureText(l.Text ?? "", l.Font).Width + l.Padding.Horizontal);
            Size want = new Size(w, 0);
            foreach (Label l in labels) if (l.MinimumSize != want) l.MinimumSize = want;
        }

        // ================================================================ layout

        private void Relayout()
        {
            if (page != null && !disposedAll) page.PerformLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PinRight();
        }

        /// <summary>Places every child of the page for its current width and sets the page height.</summary>
        private void DoLayout()
        {
            if (inLayout || disposedAll || page == null || lblHead == null || preview == null || stack == null) return;
            inLayout = true;
            try
            {
                int W = page.ClientSize.Width;
                if (W <= 0) return;
                // Leo 17 Nov (aligned UI): the content edges of every other settings page (SettingsForm.FitWidths): from the
                // page's left padding to min(the page minus the scroll-bar room, the 840 logical px 「内容宽度」)
                int padL = 0, gap = U(18);
                int edge = Math.Min(Math.Min(W, Width - SystemInformation.VerticalScrollBarWidth) - S(16), U(840));
                int inner = Math.Max(S(200), edge - padL);
                twoCol = inner >= S(600);
                int rightW = twoCol ? Math.Max(S(280), Math.Min(S(640), (int)(inner * 0.44))) : inner;
                int leftW = twoCol ? inner - rightW - gap : inner;
                int x0 = padL, y = S(2);

                // header across the whole width
                lblHead.Location = new Point(x0, y);
                lblHead.Size = lblHead.GetPreferredSize(Size.Empty);
                y = lblHead.Bottom + S(2);
                // v5.1 (W30 text fit): the height of the wrapped line from real font metrics + margin (YaHei is taller)
                lblSub.Bounds = new Rectangle(x0, y, inner, Math.Max(WrappedHeight(lblSub, inner), TextFit.NeedSize(lblSub.Text ?? "", lblSub.Font, inner, TextFit.ScaleOf(lblSub)).Height));
                y = lblSub.Bottom + U(8);
                int top = y;

                int rx = twoCol ? x0 + leftW + gap : x0;
                int pvH = twoCol ? Clamp((int)(rightW * 0.64), S(230), U(360)) : Clamp((int)(inner * 0.46), S(210), U(320));
                int stackH = LayoutStack(rightW);
                int rightH = pvH + S(8) + stackH;

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
                preview.Size = new Size(rightW, pvH);
                stack.Size = new Size(rightW, stackH);
                int h = Math.Max(tilesBottom, rightBox.Bottom) + S(12);
                if (page.Height != h) page.Height = h;
                PlaceRight();
            }
            finally { inLayout = false; }
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
            foreach (Label l in wraps) l.MaximumSize = wrap;
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
            int widest = 0;
            foreach (TileButton b in tiles) widest = Math.Max(widest, TextRenderer.MeasureText(b.Tile.Label, fonts[RSmall]).Width);
            int minW = Math.Max(U(80), widest + U(8)), maxW = Math.Max(minW, U(150)), target = Math.Max(minW, U(112));
            int best = -1; double bestScore = double.MaxValue;
            for (int c = 2; c <= 12; c++)
            {
                int tw = (w - (c - 1) * g2) / c;
                if (tw < minW || tw > maxW) continue;
                int waste = 0;
                for (int grp = 0; grp < groupOfLabel.Count; grp++)
                {
                    int n = 0;
                    foreach (TileButton b in tiles) if (b.Tile.Group == groupOfLabel[grp]) n++;
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

        internal int TileLabelHeight { get { return fonts[RSmall].Height + U(10); } }

        /// <summary>Preview + settings at their natural place, or kept in view while the tiles scroll (two columns,
        /// only when they fit the visible height).</summary>
        private void PlaceRight()
        {
            if (preview == null || stack == null) return;
            int off = 0;
            if (twoCol && page != null)
            {
                int scrolled = -page.Top; // page.Top = AutoScrollPosition.Y (<= 0)
                int view = ClientSize.Height;
                int room = page.Height - S(12) - rightBox.Bottom;
                if (rightBox.Height + S(8) <= view && scrolled > rightBox.Top - S(4))
                    off = Math.Max(0, Math.Min(room, scrolled - rightBox.Top + S(4)));
            }
            Point pp = new Point(rightBox.X, rightBox.Y + off);
            if (preview.Location != pp) preview.Location = pp;
            Point sp = new Point(rightBox.X, pp.Y + preview.Height + S(8));
            if (stack.Location != sp) stack.Location = sp;
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
            foreach (TileButton b in tiles) b.SetThumb(Thumb(b.Tile, b.ImageSide));
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
            // v5.1 (W22, decisions 1 / 6): always shown (not 高级设置 only), folded
            stack.Controls.Add(new Section(this, "准星动态（CS 风格）", BuildAnim(), false));
            stack.Controls.Add(new Section(this, "弹药提示", BuildAmmo(), false));
            stack.Layout += delegate { if (!inLayout) Relayout(); };
            page.Controls.Add(stack);
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

        /// <summary>v5.0.2 (Leo, decision 4): the 强化准星 section. The look itself is edited with the page's own controls
        /// (「改强化准星的样子」 switches the whole page to it, SetEditBoost); here: the switch, 「和正常准星一样」 and
        /// 「跟随当前手的颜色」.</summary>
        // ---------------- v5.1 (W22): 准星动态 (decision 6) and 弹药提示 (decision 1) ----------------

        private Control BuildAnim()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            Label note = Lbl("准星跟着你的动作变化：移动时张开、开火时跳一下。不增加延迟，中心始终留空，开镜时照样完全隐藏。", RSmall, Theme.ColMuted);
            note.Margin = P(4, 2, 4, 4);
            wraps.Add(note);
            AddWide(t, note);
            FlowLayoutPanel line = new FlowLayoutPanel();
            line.AutoSize = true;
            line.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            line.WrapContents = true;
            line.Margin = P(2, 2, 2, 2);
            line.BackColor = Color.Transparent;
            string[] tipsP = new string[] {
                "准星永远不动（职业选手常用）",
                "移动时张开，开火时每一发跳一下，停下后收回",
                "移动时不变，只在开火时跳一下" };
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
                tips.SetToolTip(rb, tipsP[i]);
                radios.Add(rb);
                line.Controls.Add(rb);
            }
            Label custom = Lbl("（已自定义）", RSmall, Theme.ColMuted);
            custom.Margin = P(4, 6, 4, 4);
            line.Controls.Add(custom);
            try { line.Controls.Add(HelpMark.For("anim", SmallFont, delegate { return g; })); } catch (Exception) { }
            AddWide(t, line);
            refreshers.Add(delegate
            {
                string now = CrosshairAnim.PresetOf(CrosshairAnim.From(g));
                for (int i = 0; i < radios.Count; i++) radios[i].Checked = CrosshairAnim.Presets[i] == now;
                custom.Visible = now == null;
            });
            AddCheck(t, refreshers, "移动时准星张开", delegate { return g.AnimDynGap; }, delegate(bool v) { g.AnimDynGap = v; AnimPresetFollow(); },
                "走得越快张得越开，跳起来再张开一点；停下后跟着移动的手感收回");
            AddCheck(t, refreshers, "开火时每一发跳一下", delegate { return g.AnimFirePulse; }, delegate(bool v) { g.AnimFirePulse = v; AnimPresetFollow(); },
                "按住左键时按射速每发张开一点，松开后约 0.2 秒收回");
            AddCheck(t, refreshers, "侧身时跟着倾斜", delegate { return g.AnimLeanRotate; }, delegate(bool v) { g.AnimLeanRotate = v; AnimPresetFollow(); },
                "按 Q / E 侧身时准星最多转 " + CrosshairAnim.LeanDeg.ToString("0") + " 度");
            AddCheck(t, refreshers, "移动 / 开火时变亮", delegate { return g.AnimStateColour; }, delegate(bool v) { g.AnimStateColour = v; AnimPresetFollow(); },
                "颜色向白色靠一点；换手颜色照常");
            AddCheck(t, refreshers, "开火时轻微抖动", delegate { return g.AnimShake; }, delegate(bool v) { g.AnimShake = v; AnimPresetFollow(); },
                "开火时准星随机抖动最多 " + CrosshairAnim.ShakePx.ToString("0.#") + " 像素");
            return t;
        }

        /// <summary>An option ticked by hand: AnimPreset follows when the options equal a preset (else it keeps the last one).</summary>
        private void AnimPresetFollow()
        {
            string p = CrosshairAnim.PresetOf(CrosshairAnim.From(g));
            if (p != null) g.AnimPreset = p;
        }

        private Control BuildAmmo()
        {
            TableLayoutPanel t = Grid(4);
            ParamColumns(t);
            Label note = Lbl("在准星旁边显示主武器还剩多少子弹（只显示范围）：满 / 大半 / 一半 / 少 / 快空，不显示具体数字。按 R 换弹时慢慢填满，双击 R 快速换弹时先清空再快速填满。切到其他武器时不显示，开镜、丢雷时和准星一起隐藏。", RSmall, Theme.ColMuted);
            note.Margin = P(4, 2, 4, 4);
            wraps.Add(note);
            AddWide(t, note);
            AddCheck(t, refreshers, "显示弹药提示", delegate { return g.AmmoOn; }, delegate(bool v) { g.AmmoOn = v; },
                "默认关。弹匣容量、射速和换弹键在设置里填");
            string[] labels = new string[] { "两侧括号 + 填充条", "底部细条", "按剩余变色（绿 → 黄 → 红）", "越少越淡", "快空时闪烁" };
            string[] tipsA = new string[] {
                "准星左右各一个括号，里面的条按剩余多少填充",
                "准星下方一条细线，按剩余多少从左往右填充",
                "满是绿色，一半是黄色，快空是红色（准星本身的颜色不变）",
                "子弹越少，提示越淡",
                "快空时提示一闪一闪" };
            int[] flags = new int[] { AmmoDisplay.Brackets, AmmoDisplay.BottomBar, AmmoDisplay.Colour, AmmoDisplay.Fade, AmmoDisplay.Flash };
            for (int i = 0; i < flags.Length; i++)
            {
                int f = flags[i];
                AddCheck(t, refreshers, labels[i], delegate { return (AmmoDisplay.ParseStyle(g.AmmoStyle) & f) != 0; },
                    delegate(bool v)
                    {
                        int cur = AmmoDisplay.ParseStyle(g.AmmoStyle);
                        cur = v ? cur | f : cur & ~f;
                        g.AmmoStyle = AmmoDisplay.FormatStyle(cur); // at least one of 括号 / 细条 stays on
                    }, tipsA[i]);
            }
            return t;
        }

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
            paramGrid.SuspendLayout();
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
            ApplySizes();
            paramGrid.ResumeLayout(true);
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
                        AddValue(paramGrid, R, label, "°", min, max, 1, 0, delegate { return StyleShapes.Of(Cur).Spread; },
                            delegate(double v) { StyleShapes.Of(Cur).Spread = v; }, tip);
                        break;
                    }
                case StyleShapes.ParamCount:
                    {
                        bool dots = id == "dotring";
                        AddValue(paramGrid, R, dots ? "点数" : "段数", dots ? "个" : "段", dots ? 3 : 2, dots ? 24 : 8, 1, 0,
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
                foreach (Action a in refreshers) { try { a(); } catch (Exception) { } }
                foreach (TileButton b in tiles) b.Selected = t != null && b.Tile.Id == t.Id;
                UpdatePngStatus();
                ApplyVisibility();
                ResumeLayout(false);
            }
            finally { loading = was; }
            Relayout();
            if (preview != null) preview.Invalidate();
            ScheduleThumbs();
        }

        /// <summary>高级设置 on/off: per-style fine-tuning rows, colour effect rows and the three fold sections.</summary>
        private void ApplyVisibility()
        {
            foreach (Control c in advSections)
            {
                bool on = advanced && !(editBoost && handSections.Contains(c)); // 换手颜色 belongs to the normal look
                if (c.Visible != on) c.Visible = on;
            }
            foreach (Control[] row in advParamRows) Show(row, advanced);
            bool was = loading;
            loading = true;
            try { foreach (Action a in refreshers) { try { a(); } catch (Exception) { } } }
            finally { loading = was; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
            RenderThumbs();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // v4.3.1: nothing is rebuilt when the page is shown again (fixed text size)
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
                            previewExtras.Frame(st, new AnimOptions(), new AnimInput(), true, av, owner.g.AmmoStyle, 0, 0);
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
                for (int i = 0; i < 3; i++) bw = Math.Max(bw, TextRenderer.MeasureText(ModeNames[i], btnF).Width + owner.UI(18));
                int x0 = r.Right - pad - 3 * bw - 2 * gapB;
                // v4.1 (REBRUTE41 P2-2): a look nobody can see (RingStyle.Hidden / no visible pixel) says so, in amber, no ✓
                bool invisible = st != null && (st.Hidden || (cache != null && cacheInvisible));
                string hole = "";
                int holeW = Math.Max(0, x0 - r.Left - 2 * pad);
                if (st != null)
                    foreach (string c in StyleFooter.Candidates(st.Hole, invisible))
                    {
                        hole = c;
                        if (TextRenderer.MeasureText(c, capF).Width <= holeW) break;
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
