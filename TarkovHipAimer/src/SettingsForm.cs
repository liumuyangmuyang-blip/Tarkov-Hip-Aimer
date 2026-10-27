using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    /// <summary>Button that captures a key or mouse button for one action of the 按键 page.</summary>
    internal sealed class KeyButton : Button
    {
        public int Action; // KeyBindings action index
    }

    /// <summary>
    /// Settings window (v3.0). Layout rules, so nothing wraps or overlaps at 100–200 % scaling:
    /// every grid is a TableLayoutPanel (no wrapping FlowLayoutPanel anywhere), every fixed pixel size goes
    /// through S() (system DPI / 96, AutoScaleMode None so nothing is scaled twice), fonts are in points,
    /// labels are vertically centred by Anchor = Left, the top bar is one TableLayoutPanel row, tab pages
    /// scroll (AutoScroll) and the window has a minimum size that keeps the top and bottom bars on one line.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        /// <summary>Layout scale (system DPI / 96). Settable before construction for layout tests only.</summary>
        internal static float ScaleOverride = 0f;

        private readonly App app;
        private readonly float k;
        private bool loading;
        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<KeyButton> keyButtons = new List<KeyButton>();
        private readonly ToolTip tips = new ToolTip();

        private ComboBox cbProfile;
        private CheckBox chkEnabled;
        private CheckBox chkAutostart;
        private ComboBox cbMonitor;
        private ComboBox cbPreview;
        private ContextMenuStrip profileMenu;
        private ContextMenuStrip restoreMenu;
        private ContextMenuStrip windowMenu;
        private ToolStripMenuItem restoreSaved;
        private Label lblStatus;
        private Label lblSave;
        private TabControl tabs;
        private TableLayoutPanel root;
        private Control header;
        private TableLayoutPanel topBar;
        private TableLayoutPanel bottomBar;
        private readonly List<Control> pageContents = new List<Control>();
        private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();

        private KeyButton capturing;
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
            Font = PickFont();
            Text = Program.ProductName + "  " + Program.Version;
            if (app.RingIcon != null) Icon = app.RingIcon;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = G.SettingsTopMost;
            tips.AutoPopDelay = 15000;
            tips.InitialDelay = 400;

            root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // header
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // top bar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // tabs
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // preview + status
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            header = Theme.Header(Program.ShortName, "by Leo", Program.Version);
            header.Dock = DockStyle.Top;
            header.Margin = new Padding(0);
            root.Controls.Add(header, 0, 0);
            BuildTopBar();
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Margin = P(6, 4, 6, 0);
            tabs.Multiline = false;
            root.Controls.Add(tabs, 0, 2);
            AddPage("外观", BuildAppearance());
            AddPage("移动", BuildMovement());
            AddPage("后坐力", BuildRecoil());
            AddPage("侧身", BuildLean());
            AddPage("跳跃", BuildJump());
            AddPage("按键", BuildKeys());
            AddPage("其他", BuildOther());
            BuildBottomBar();

            statusTimer.Interval = 100;
            statusTimer.Tick += delegate { UpdateStatus(); CheckPreviewFocus(); };
            statusTimer.Start();
            app.SaveStatusChanged += delegate { lblSave.Text = app.SaveStatus; };

            RefreshAll();
            ResumeLayout(false);
            PerformLayout();
            Theme.Apply(this);
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

        private static Font PickFont()
        {
            try
            {
                Font f = new Font("Microsoft YaHei UI", 9f);
                if (f.Name == "Microsoft YaHei UI") return f;
                f.Dispose();
            }
            catch (Exception) { }
            return SystemFonts.MessageBoxFont;
        }

        // ---------------- layout helpers ----------------

        /// <summary>Scales a 96-DPI pixel length.</summary>
        private int S(int px) { return (int)Math.Round(px * k); }
        private Size Sz(int w, int h) { return new Size(S(w), S(h)); }
        private Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        private void AddPage(string title, Control content)
        {
            TabPage page = new TabPage(title);
            page.AutoScroll = true;
            page.Padding = new Padding(0);
            content.Location = new Point(S(8), S(8));
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
            pageContents.Add(content);
        }

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
            Label l = new Label();
            l.AutoSize = true;
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

        private void Tip(Control c, string text) { tips.SetToolTip(c, text); }

        private static void Row(TableLayoutPanel g, Control label, params Control[] cells)
        {
            g.Controls.Add(label);
            foreach (Control c in cells)
            {
                if (c.Anchor == (AnchorStyles.Top | AnchorStyles.Left)) c.Anchor = AnchorStyles.Left;
                g.Controls.Add(c);
            }
        }

        private void Row(TableLayoutPanel g, string label, params Control[] cells) { Row(g, Lbl(label), cells); }

        private GroupBox Group(string title, Control content)
        {
            GroupBox gb = new GroupBox();
            gb.Text = title;
            gb.AutoSize = true;
            gb.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            gb.Padding = P(8, 6, 8, 8);
            gb.Margin = P(0, 0, 10, 10);
            gb.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            content.Dock = DockStyle.Fill;
            gb.Controls.Add(content);
            return gb;
        }

        private static decimal ToDec(double v, NumericUpDown n)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            decimal d = (decimal)Math.Round(v, n.DecimalPlaces);
            if (d < n.Minimum) d = n.Minimum;
            if (d > n.Maximum) d = n.Maximum;
            return d;
        }

        private bool Ask(string question)
        {
            return MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>Width that fits the widest value of a number box at this font and DPI.</summary>
        private int NumWidth(double min, double max, int dec)
        {
            string wide = (min < 0 ? "-" : "") + new string('8', Math.Max(1, ((long)Math.Max(Math.Abs(min), Math.Abs(max))).ToString().Length))
                + (dec > 0 ? "." + new string('8', dec) : "");
            return TextRenderer.MeasureText(wide, Font).Width + SystemInformation.VerticalScrollBarWidth + S(14);
        }

        /// <summary>NumericUpDown bound to a model value. ValueChanged writes the model, publishes and schedules a save.</summary>
        private NumericUpDown Num(double min, double max, double inc, int dec, Func<double> get, Action<double> set)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = (decimal)min;
            n.Maximum = (decimal)max;
            n.Increment = (decimal)inc;
            n.DecimalPlaces = dec;
            n.Width = NumWidth(min, max, dec);
            n.Margin = P(3, 3, 3, 3);
            n.Anchor = AnchorStyles.Left;
            n.TextAlign = HorizontalAlignment.Right;
            n.ValueChanged += delegate
            {
                if (loading) return;
                set((double)n.Value);
                app.Changed();
            };
            refreshers.Add(delegate { n.Value = ToDec(get(), n); });
            return n;
        }

        private ComboBox Choice(string[] labels, string[] values, Func<string> get, Action<string> set)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(labels);
            int w = S(70);
            foreach (string l in labels) w = Math.Max(w, TextRenderer.MeasureText(l, Font).Width + SystemInformation.VerticalScrollBarWidth + S(16));
            c.Width = w;
            c.Margin = P(3, 3, 3, 3);
            c.Anchor = AnchorStyles.Left;
            c.SelectedIndexChanged += delegate
            {
                if (loading || c.SelectedIndex < 0) return;
                set(values[c.SelectedIndex]);
                app.Changed();
            };
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
            c.Margin = P(3, 3, 6, 3);
            c.CheckedChanged += delegate
            {
                if (loading) return;
                set(c.Checked);
                app.Changed();
            };
            refreshers.Add(delegate { c.Checked = get(); });
            return c;
        }

        private Button ColorButton(string text, Func<string> get, Action<string> set)
        {
            Button b = new Button();
            b.Text = text;
            b.Tag = "theme:keep"; // colour swatch: the theme must not restyle it
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Anchor = AnchorStyles.Left;
            b.Margin = P(3, 2, 3, 2);
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
            b.ImageAlign = ContentAlignment.MiddleLeft;
            int sw = Math.Max(12, S(16));
            Action paint = delegate
            {
                int r, g, bl;
                if (!ColorUtil.TryParse(get(), out r, out g, out bl)) { r = g = bl = 255; }
                Bitmap bmp = new Bitmap(sw, sw);
                using (Graphics gr = Graphics.FromImage(bmp))
                {
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(r, g, bl))) gr.FillRectangle(br, 0, 0, sw, sw);
                    gr.DrawRectangle(Pens.Gray, 0, 0, sw - 1, sw - 1);
                }
                Image old = b.Image;
                b.Image = bmp;
                if (old != null) old.Dispose();
            };
            b.Click += delegate
            {
                int r, g, bl;
                using (ColorDialog dlg = new ColorDialog())
                {
                    dlg.FullOpen = true;
                    if (ColorUtil.TryParse(get(), out r, out g, out bl)) dlg.Color = Color.FromArgb(r, g, bl);
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    set(ColorUtil.Format(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                }
                paint();
                app.Changed();
            };
            refreshers.Add(paint);
            return b;
        }

        private Button Btn(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Anchor = AnchorStyles.Left;
            b.Margin = P(3, 2, 3, 2);
            b.Padding = P(4, 0, 4, 0);
            b.Click += onClick;
            return b;
        }

        /// <summary>Enables a set of controls while cond() holds (refreshed with the values and on demand).</summary>
        private Action EnableWhen(Func<bool> cond, params Control[] targets)
        {
            Action a = delegate { bool on = cond(); foreach (Control c in targets) c.Enabled = on; };
            refreshers.Add(a);
            return a;
        }

        // ---------------- key capture ----------------

        public static string KeyName(int vk)
        {
            switch (vk)
            {
                case 1: return "鼠标左键";
                case 2: return "鼠标右键";
                case 4: return "鼠标中键";
                case 5: return "鼠标侧键1（XButton1）";
                case 6: return "鼠标侧键2（XButton2）";
                default: return ((Keys)vk).ToString();
            }
        }

        private KeyButton KeyBtn(int action)
        {
            KeyButton b = new KeyButton();
            b.Action = action;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(Math.Max(S(150), TextRenderer.MeasureText("鼠标侧键1（XButton1）", Font).Width + S(16)), 0);
            b.Anchor = AnchorStyles.Left;
            b.Margin = P(3, 2, 6, 2);
            b.Click += delegate
            {
                if (suppressNextClick) { suppressNextClick = false; return; }
                if (capturing != null && capturing != b) CancelCapture();
                capturing = b;
                b.Text = "请按键…（Esc 取消）";
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
                FinishCapture(vk);
            };
            b.Leave += delegate { if (capturing == b) CancelCapture(); };
            keyButtons.Add(b);
            refreshers.Add(delegate { b.Text = KeyName(G.Keys.Get(b.Action)); });
            return b;
        }

        private void CancelCapture()
        {
            KeyButton b = capturing;
            capturing = null;
            if (b != null) b.Text = KeyName(G.Keys.Get(b.Action));
        }

        private void FinishCapture(int vk)
        {
            KeyButton b = capturing;
            capturing = null;
            if (b == null) return;
            int other = G.Keys.FindOther(vk, b.Action);
            if (other >= 0)
            {
                bool yes = Ask("这个键已经用于「" + KeyBindings.ActionNames[other] + "」，还要用吗？");
                suppressNextClick = false; // the button never sees the click that started this capture once a dialog was up
                if (!yes)
                {
                    b.Text = KeyName(G.Keys.Get(b.Action));
                    return;
                }
            }
            G.Keys.Set(b.Action, vk);
            b.Text = KeyName(vk);
            app.UpdateTray();
            app.Changed();
        }

        private bool TryCaptureKey(Keys keyData)
        {
            if (capturing == null) return false;
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Escape) { CancelCapture(); return true; }
            int vk = (int)code;
            if (vk <= 0 || vk > 254) return true;
            FinishCapture(vk);
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TryCaptureKey(keyData)) return true;
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
            topBar.Padding = P(6, 6, 6, 0);
            topBar.RowCount = 1;
            topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            topBar.ColumnCount = 7;
            for (int i = 0; i < 5; i++) topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // spacer
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label lp = Lbl("配置");
            Tip(lp, "每把枪可以存一套配置");
            cbProfile = new ComboBox();
            cbProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            cbProfile.Width = S(190);
            cbProfile.DropDownWidth = S(280);
            cbProfile.Anchor = AnchorStyles.Left;
            cbProfile.Margin = P(0, 3, 3, 3);
            cbProfile.SelectedIndexChanged += delegate
            {
                if (loading || cbProfile.SelectedItem == null) return;
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
            AddItem(profileMenu, "导出当前配置…", delegate { ExportProfile(); });
            Button manage = null;
            manage = Btn("配置 ▾", delegate { profileMenu.Show(manage, new Point(0, manage.Height)); });

            Button save = Btn("保存为默认", delegate { SaveAsDefault(); });
            Tip(save, "把当前配置现在的数值存下来，以后可以「恢复默认」回到这里");
            Button restore = null;
            restore = Btn("恢复默认 ▾", delegate { ShowRestoreMenu(restore); });

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
            topBar.Controls.Add(chkEnabled, 6, 0);
            root.Controls.Add(topBar, 0, 1);
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
            string n = NameDialog.Ask(this, "新建配置", "", app.Weapons);
            if (n == null) return;
            Profile p = Profile.CreateBlank(G.UniqueName(n, null));
            G.Profiles.Add(p);
            G.ActiveProfile = p.Name;
            RefreshAll();
            app.Changed();
        }

        private void CopyProfile()
        {
            string n = NameDialog.Ask(this, "复制配置", Cur.Name, app.Weapons);
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
            string n = NameDialog.Ask(this, "重命名配置", cur.Name, app.Weapons);
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
                MessageBox.Show(this, "至少要保留一个配置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    lblSave.Text = "已导出：" + Path.GetFileName(d.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                try { text = File.ReadAllText(d.FileName, Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, "读取失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                if (!SettingsIO.TryParseImport(text, out list, out err))
                {
                    MessageBox.Show(this, "这个文件不能导入：" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                G.AddImported(list);
                RefreshAll();
                app.Changed();
                lblSave.Text = "已导入 " + list.Count + " 个配置";
            }
        }

        private void SaveAsDefault()
        {
            Profile cur = Cur;
            if (!Ask("把配置“" + cur.Name + "”现在的全部数值存成它的默认？以后点「恢复默认」就能回到这里（会覆盖上一次存的）。")) return;
            cur.SaveAsDefault(DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            app.Publish();
            app.SaveNow(); // at once, not debounced
            lblSave.Text = "已存为默认：" + cur.Name + "（" + cur.SavedDefaultAt + "）";
        }

        private void ShowRestoreMenu(Button anchor)
        {
            Profile cur = Cur;
            if (restoreMenu == null)
            {
                restoreMenu = new ContextMenuStrip();
                restoreSaved = new ToolStripMenuItem();
                restoreSaved.Click += delegate { RestoreSaved(); };
                ToolStripMenuItem builtIn = new ToolStripMenuItem("恢复到内置默认（" + GlobalSettings.DefaultProfileName + "）");
                builtIn.Click += delegate { RestoreBuiltIn(); };
                restoreMenu.Items.Add(restoreSaved);
                restoreMenu.Items.Add(builtIn);
            }
            bool has = cur.SavedDefault != null;
            restoreSaved.Text = has ? "恢复到我存的默认（" + cur.SavedDefaultAt + "）" : "恢复到我存的默认（还没存过）";
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
            lblSave.Text = "已恢复到你存的默认：" + cur.Name;
        }

        /// <summary>Values = Profile.CreateLeoDefault; the name and the 「保存为默认」 snapshot are kept, globals untouched.</summary>
        private void RestoreBuiltIn()
        {
            Profile cur = Cur;
            if (!Ask("把配置“" + cur.Name + "”的数值恢复成内置默认（" + GlobalSettings.DefaultProfileName + "）？名字、按键和全局设置不变。")) return;
            int idx = G.Profiles.IndexOf(cur);
            Profile n = Profile.CreateLeoDefault(cur.Name).CloneValues();
            n.Name = cur.Name;
            n.SavedDefault = cur.SavedDefault == null ? null : cur.SavedDefault.CloneValues();
            n.SavedDefaultAt = cur.SavedDefaultAt;
            n.Validate();
            if (idx >= 0) G.Profiles[idx] = n;
            G.ActiveProfile = n.Name;
            RefreshAll();
            app.Changed();
            lblSave.Text = "已恢复到内置默认：" + cur.Name;
        }

        // ---------------- tabs ----------------

        private static readonly string[] ShapeLabels = new string[] { "圆圈", "十字", "圆圈+十字", "方框", "实心点" };
        private static readonly string[] ShapeValues = new string[] { "ring", "cross", "ringcross", "square", "dot" };

        private Control BuildAppearance()
        {
            // 形状和尺寸 (label | control | unit)
            TableLayoutPanel g = Grid(3);
            Action crossOn = null;
            ComboBox shape = Choice(ShapeLabels, ShapeValues, delegate { return Cur.Shape; },
                delegate(string v) { Cur.Shape = v; if (crossOn != null) crossOn(); });
            Tip(shape, "实心点会盖住屏幕中心（游戏自己的小白点）");
            Row(g, "形状", shape, Blank());
            Row(g, "直径", Num(4, 300, 1, 0, delegate { return Cur.Diameter; }, delegate(double v) { Cur.Diameter = v; }), Unit("px"));
            Row(g, "线宽", Num(0.5, 30, 0.5, 1, delegate { return Cur.Thickness; }, delegate(double v) { Cur.Thickness = v; }), Unit("px"));
            Row(g, "边缘羽化", Num(0, 30, 0.5, 1, delegate { return Cur.Feather; }, delegate(double v) { Cur.Feather = v; }), Unit("px"));
            Row(g, "透明度", Num(5, 100, 1, 0, delegate { return Cur.OpacityPct; }, delegate(double v) { Cur.OpacityPct = v; }), Unit("%"));
            TableLayoutPanel colors = Line(ColorButton("颜色", delegate { return Cur.Color; }, delegate(string v) { Cur.Color = v; }),
                ColorButton("描边颜色", delegate { return Cur.Color2; }, delegate(string v) { Cur.Color2 = v; }));
            g.Controls.Add(Lbl("颜色"));
            g.Controls.Add(colors);
            g.SetColumnSpan(colors, 2);
            NumericUpDown outerNum = Num(0.5, 20, 0.5, 1, delegate { return Cur.OuterThickness; }, delegate(double v) { Cur.OuterThickness = v; });
            Action outerOn = EnableWhen(delegate { return Cur.Gradient == "twotone"; }, outerNum);
            ComboBox grad = Choice(new string[] { "无", "由内到外", "环绕", "内外双色" }, Profile.Gradients,
                delegate { return Cur.Gradient; }, delegate(string v) { Cur.Gradient = v; outerOn(); });
            Tip(grad, "「内外双色」＝里圈用颜色，外圈用描边颜色");
            Row(g, "渐变", grad, Blank());
            Row(g, "外圈线宽", outerNum, Unit("px"));
            NumericUpDown dotSize = Num(1, 10, 1, 0, delegate { return Cur.CenterDotSize; }, delegate(double v) { Cur.CenterDotSize = v; });
            Action dotOn = EnableWhen(delegate { return Cur.CenterDot; }, dotSize);
            TableLayoutPanel dot = Line(Check("显示", delegate { return Cur.CenterDot; }, delegate(bool v) { Cur.CenterDot = v; dotOn(); }), dotSize, Unit("px"));
            g.Controls.Add(Lbl("中心点"));
            g.Controls.Add(dot);
            g.SetColumnSpan(dot, 2);

            // 十字 / 虚线 / 发光 / 旋转
            TableLayoutPanel x = Grid(3);
            NumericUpDown cl = Num(0, 200, 0.5, 1, delegate { return Cur.CrossLength; }, delegate(double v) { Cur.CrossLength = v; });
            NumericUpDown ct = Num(0.5, 30, 0.5, 1, delegate { return Cur.CrossThickness; }, delegate(double v) { Cur.CrossThickness = v; });
            NumericUpDown cg = Num(0, 200, 0.5, 1, delegate { return Cur.CrossGap; }, delegate(double v) { Cur.CrossGap = v; });
            CheckBox ctop = Check("有上面一笔", delegate { return Cur.CrossTop; }, delegate(bool v) { Cur.CrossTop = v; });
            Tip(ctop, "不勾＝T 形（没有上面那一笔）");
            Tip(cg, "十字离中心空出多少");
            Row(x, "十字长度", cl, Unit("px"));
            Row(x, "十字线宽", ct, Unit("px"));
            Row(x, "十字间隙", cg, Unit("px"));
            Row(x, "十字", ctop, Blank());
            crossOn = EnableWhen(delegate { return Cur.Shape == "cross" || Cur.Shape == "ringcross"; }, cl, ct, cg, ctop);
            NumericUpDown dashes = Num(0, 24, 1, 0, delegate { return Cur.Dashes; }, delegate(double v) { Cur.Dashes = (int)Math.Round(v); });
            Tip(dashes, "0＝实线，2–24＝分成几段");
            NumericUpDown dgap = Num(0, 90, 1, 0, delegate { return Cur.DashGapPct; }, delegate(double v) { Cur.DashGapPct = v; });
            Action dashOn = EnableWhen(delegate { return Cur.Dashes >= 2; }, dgap); // 1 = solid, like 0
            dashes.ValueChanged += delegate { if (!loading) dashOn(); };
            Row(x, "虚线段数", dashes, Unit("段"));
            Row(x, "虚线间隙", dgap, Unit("%"));
            Row(x, "发光", Num(0, 40, 0.5, 1, delegate { return Cur.GlowPx; }, delegate(double v) { Cur.GlowPx = v; }), Unit("px"));
            Row(x, "发光透明度", Num(0, 100, 1, 0, delegate { return Cur.GlowOpacityPct; }, delegate(double v) { Cur.GlowOpacityPct = v; }), Unit("%"));
            Row(x, "旋转", Num(-180, 180, 1, 0, delegate { return Cur.RotationDeg; }, delegate(double v) { Cur.RotationDeg = v; }), Unit("°"));
            // 换手颜色
            TableLayoutPanel hc = Grid(2);
            Button hc1 = ColorButton("换手颜色", delegate { return Cur.HandColor; }, delegate(string v) { Cur.HandColor = v; });
            Button hc2 = ColorButton("换手描边", delegate { return Cur.HandColor2; }, delegate(string v) { Cur.HandColor2 = v; });
            Action hcOn = EnableWhen(delegate { return Cur.HandColorOn; }, hc1, hc2);
            CheckBox hcChk = Check("换手后换颜色", delegate { return Cur.HandColorOn; }, delegate(bool v) { Cur.HandColorOn = v; hcOn(); });
            Tip(hcChk, "换手后准星变成这个颜色，过渡时间跟「换手过渡时间」一样");
            hc.Controls.Add(hcChk);
            hc.SetColumnSpan(hcChk, 2);
            hc.Controls.Add(hc1);
            hc.Controls.Add(hc2);

            // 强化准星外观
            TableLayoutPanel bs = Grid(3);
            Row(bs, "直径", Num(4, 300, 1, 0, delegate { return Cur.Boost.Diameter; }, delegate(double v) { Cur.Boost.Diameter = v; }), Unit("px"));
            Row(bs, "线宽", Num(0.5, 30, 0.1, 1, delegate { return Cur.Boost.Thickness; }, delegate(double v) { Cur.Boost.Thickness = v; }), Unit("px"));
            Row(bs, "描边线宽", Num(0, 20, 0.5, 1, delegate { return Cur.Boost.OuterThickness; }, delegate(double v) { Cur.Boost.OuterThickness = v; }), Unit("px"));
            Row(bs, "透明度", Num(5, 100, 1, 0, delegate { return Cur.Boost.OpacityPct; }, delegate(double v) { Cur.Boost.OpacityPct = v; }), Unit("%"));
            Row(bs, "发光", Num(0, 40, 0.5, 1, delegate { return Cur.Boost.GlowPx; }, delegate(double v) { Cur.Boost.GlowPx = v; }), Unit("px"));
            Row(bs, "发光透明度", Num(0, 100, 1, 0, delegate { return Cur.Boost.GlowOpacityPct; }, delegate(double v) { Cur.Boost.GlowOpacityPct = v; }), Unit("%"));
            Button bc1 = ColorButton("颜色", delegate { return Cur.Boost.Color; }, delegate(string v) { Cur.Boost.Color = v; });
            Button bc2 = ColorButton("描边颜色", delegate { return Cur.Boost.Color2; }, delegate(string v) { Cur.Boost.Color2 = v; });
            Action bcOn = EnableWhen(delegate { return !Cur.Boost.FollowHandColor; }, bc1, bc2);
            CheckBox follow = Check("跟随当前手的颜色", delegate { return Cur.Boost.FollowHandColor; }, delegate(bool v) { Cur.Boost.FollowHandColor = v; bcOn(); });
            Tip(follow, "勾上＝用正常/换手时的颜色；不勾＝用下面的颜色");
            bs.Controls.Add(follow);
            bs.SetColumnSpan(follow, 3);
            Label bcl = Lbl("颜色");
            bs.Controls.Add(bcl);
            TableLayoutPanel bcLine = Line(bc1, bc2);
            bs.Controls.Add(bcLine);
            bs.SetColumnSpan(bcLine, 2);

            GroupBox gShape = Group("形状和尺寸", g);
            GroupBox gMore = Group("十字 · 虚线 · 发光 · 旋转", x);
            GroupBox gHand = Group("换手颜色", hc);
            GroupBox gBoost = Group("强化准星（按强化键后）", bs);
            Tip(gBoost, "形状和其他尺寸跟正常准星一样");

            TableLayoutPanel page = Grid(2);
            page.Controls.Add(Stack(gShape, gHand), 0, 0);
            page.Controls.Add(Stack(gMore, gBoost), 1, 0);
            return page;
        }

        private Control BuildMovement()
        {
            // 方向 (W/S/A/D) + 站着偏移: one grid; the diagonals are always computed.
            TableLayoutPanel g = Grid(3);
            Label h0 = Blank();
            Label hx = Lbl("X（右为正）");
            Label hy = Lbl("Y（上为正）");
            g.Controls.Add(h0);
            g.Controls.Add(hx);
            g.Controls.Add(hy);
            string[] names = { "W 前", "S 后", "A 左", "D 右" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Row(g, names[i],
                    Num(-500, 500, 1, 1, delegate { return Cur.Dirs[idx].OffX; }, delegate(double v) { Cur.Dirs[idx].OffX = v; Profile.RecomputeDiagonals(Cur); }),
                    Num(-500, 500, 1, 1, delegate { return Cur.Dirs[idx].OffY; }, delegate(double v) { Cur.Dirs[idx].OffY = v; Profile.RecomputeDiagonals(Cur); }));
            }
            Label st = Lbl("站着偏移");
            Tip(st, "不按移动键时准星的位置");
            Row(g, st,
                Num(-200, 200, 1, 0, delegate { return Cur.StandOffsetX; }, delegate(double v) { Cur.StandOffsetX = v; }),
                Num(-200, 200, 1, 0, delegate { return Cur.StandOffsetY; }, delegate(double v) { Cur.StandOffsetY = v; }));
            GroupBox gDir = Group("移动时准星偏移（px）", g);
            Tip(gDir, "按住这个方向走路时子弹落点离屏幕中心多远。斜向（WA/WD/SA/SD）自动算＝两个方向相加 × 0.7071；侧身+移动直接相加");

            // 移动速率曲线
            TableLayoutPanel curves = Grid(2);
            curves.Controls.Add(CurveBlock("左右 A/D", delegate { return Cur.CurveAD; }), 0, 0);
            curves.Controls.Add(CurveBlock("前后 W/S", delegate { return Cur.CurveWS; }), 1, 0);
            Button copy = Btn("把左右的曲线复制到前后", delegate
            {
                if (!Ask("把前后 W/S 的曲线（总时长和 5 个速度）改成和左右 A/D 一样？")) return;
                Cur.CurveWS = Cur.CurveAD.Clone();
                RefreshValues();
                app.Changed();
            });
            curves.Controls.Add(copy, 0, 1);
            curves.SetColumnSpan(copy, 2);
            GroupBox gCurve = Group("移动速率曲线", curves);
            Tip(gCurve, "点越高，那一段时间动得越快；总会在总时长内刚好到位。按下、松开、换方向都用这条曲线");

            return Stack(gDir, gCurve);
        }

        /// <summary>One 移动速率曲线 block: 总时长, the curve editor, 5 number boxes (synced both ways), presets.</summary>
        private Control CurveBlock(string title, Func<MoveCurve> get)
        {
            TableLayoutPanel b = Grid(1);
            b.Margin = P(0, 0, 14, 0);
            CurveEditor ed = new CurveEditor();
            ed.Curve = get;
            ed.Size = Sz(300, 120);
            b.Controls.Add(Line(Lbl(title), Lbl("总时长"),
                Num(0, 2000, 10, 0, delegate { return get().DurationMs; }, delegate(double v) { get().DurationMs = v; ed.Invalidate(); }),
                Unit("ms")));
            b.Controls.Add(ed);
            TableLayoutPanel pts = Grid(MotionCurve.Points);
            NumericUpDown[] boxes = new NumericUpDown[MotionCurve.Points];
            for (int i = 0; i < MotionCurve.Points; i++)
            {
                Label l = Lbl((i * 25) + "%");
                l.Anchor = AnchorStyles.None;
                l.Margin = P(3, 0, 3, 0);
                l.ForeColor = SystemColors.GrayText;
                pts.Controls.Add(l, i, 0);
            }
            for (int i = 0; i < MotionCurve.Points; i++)
            {
                int idx = i;
                boxes[i] = Num(0, 100, 1, 0, delegate { return get().Speeds[idx]; }, delegate(double v) { get().Speeds[idx] = v; ed.Invalidate(); });
                boxes[i].Margin = P(1, 2, 1, 2);
                pts.Controls.Add(boxes[i], i, 1);
            }
            b.Controls.Add(pts);
            ed.PointChanged += delegate(int i)
            {
                bool was = loading;
                loading = true;
                try { boxes[i].Value = ToDec(get().Speeds[i], boxes[i]); }
                finally { loading = was; }
                app.Changed();
            };
            refreshers.Add(ed.Invalidate);
            Control[] presets = new Control[MotionCurve.PresetNames.Length];
            for (int i = 0; i < MotionCurve.PresetNames.Length; i++)
            {
                int idx = i;
                presets[i] = Btn(MotionCurve.PresetNames[i], delegate { get().SetPreset(idx); RefreshValues(); app.Changed(); });
            }
            b.Controls.Add(Line(presets));
            return b;
        }

        private Control BuildRecoil()
        {
            TableLayoutPanel g = Grid(3);
            Row(g, "最高上抬", Num(0, 1000, 1, 0, delegate { return Cur.RecoilMaxPx; }, delegate(double v) { Cur.RecoilMaxPx = v; }), Unit("px"));
            Row(g, "到顶时间", Num(10, 5000, 10, 0, delegate { return Cur.RecoilRiseMs; }, delegate(double v) { Cur.RecoilRiseMs = v; }), Unit("ms"));
            Row(g, "每次按下额外上跳", Num(0, 300, 1, 0, delegate { return Cur.RecoilClickKickPx; }, delegate(double v) { Cur.RecoilClickKickPx = v; }), Unit("px"));
            Row(g, "松开回落时间", Num(10, 5000, 10, 0, delegate { return Cur.RecoilFallMs; }, delegate(double v) { Cur.RecoilFallMs = v; }), Unit("ms"));
            Label delay = Lbl("开枪后多久开始上跳");
            Tip(delay, "这一发子弹在上跳之前就已经出膛了");
            Row(g, delay, Num(0, 300, 10, 0, delegate { return Cur.RecoilDelayMs; }, delegate(double v) { Cur.RecoilDelayMs = v; }), Unit("ms"));
            Row(g, "到顶时水平偏移", Num(-500, 500, 1, 0, delegate { return Cur.RecoilDriftX; }, delegate(double v) { Cur.RecoilDriftX = v; }), Unit("px"));
            GroupBox gb = Group("后坐力（按住左键）", g);
            Tip(gb, "按住左键准星上抬到「最高上抬」；点射每次额外上跳一点；开镜时不算");
            return Stack(gb);
        }

        private Control BuildLean()
        {
            TableLayoutPanel g = Grid(4);
            g.Controls.Add(Blank());
            g.Controls.Add(Lbl("X（右为正）"));
            g.Controls.Add(Lbl("Y（上为正）"));
            g.Controls.Add(Blank());
            Row(g, "Q 左侧身",
                Num(-500, 500, 1, 0, delegate { return Cur.LeanQX; }, delegate(double v) { Cur.LeanQX = v; }),
                Num(-500, 500, 1, 0, delegate { return Cur.LeanQY; }, delegate(double v) { Cur.LeanQY = v; }), Unit("px"));
            Row(g, "E 右侧身",
                Num(-500, 500, 1, 0, delegate { return Cur.LeanEX; }, delegate(double v) { Cur.LeanEX = v; }),
                Num(-500, 500, 1, 0, delegate { return Cur.LeanEY; }, delegate(double v) { Cur.LeanEY = v; }), Unit("px"));
            Row(g, "过渡时间", Num(0, 2000, 10, 0, delegate { return Cur.LeanMs; }, delegate(double v) { Cur.LeanMs = v; }), Unit("ms"), Blank());
            GroupBox gb = Group("侧身偏移", g);
            Tip(gb, "侧身+移动＝两个偏移直接相加。侧身方式（按住/切换）在「按键」页");
            return Stack(gb);
        }

        /// <summary>跳跃 tab: ☑ enable, 总时长, 3 phase rows, 自动分配, a read-only path plot.</summary>
        private Control BuildJump()
        {
            Func<JumpSettings> J = delegate { return Cur.Jump; };
            JumpPlot plot = new JumpPlot();
            plot.Jump = J;
            plot.Size = Sz(420, 140);
            refreshers.Add(plot.Invalidate);
            List<Control> parts = new List<Control>();

            NumericUpDown total = Num(JumpSettings.MinTotalMs, JumpSettings.MaxTotalMs, 10, 0, delegate { return J().TotalMs; },
                delegate(double v) { JumpSplit.SetTotal(J(), (int)Math.Round(v)); RefreshValues(); });
            Button auto = Btn("自动分配", delegate { JumpSplit.Auto(J()); RefreshValues(); app.Changed(); });
            Tip(total, "三段加起来永远等于总时长，改总时长三段按比例一起变");
            Tip(auto, "30% / 40% / 30%");
            parts.Add(total); parts.Add(auto);

            TableLayoutPanel g = Grid(4);
            g.Controls.Add(Lbl("阶段"));
            g.Controls.Add(Lbl("时长 ms"));
            Label he = Lbl("结束位置 px");
            Tip(he, "正数往上");
            g.Controls.Add(he);
            g.Controls.Add(Lbl("曲线"));
            for (int i = 0; i < JumpSettings.PhaseCount; i++)
            {
                int idx = i;
                g.Controls.Add(Lbl((i + 1) + ". " + JumpSettings.PhaseNames[i]));
                NumericUpDown ms = Num(0, JumpSettings.MaxTotalMs, 10, 0, delegate { return J().Phases[idx].Ms; },
                    delegate(double v) { JumpSplit.SetPhase(J(), idx, (int)Math.Round(v)); RefreshValues(); });
                g.Controls.Add(ms);
                parts.Add(ms);
                if (i < JumpSettings.PhaseCount - 1)
                {
                    NumericUpDown end = Num(-JumpSettings.LimitPx, JumpSettings.LimitPx, 1, 1, delegate { return J().Phases[idx].EndPx; },
                        delegate(double v) { J().Phases[idx].EndPx = v; plot.Invalidate(); });
                    g.Controls.Add(end);
                    parts.Add(end);
                }
                else g.Controls.Add(Unit("0（固定）"));
                ComboBox cv = Choice(JumpSettings.CurveLabels, JumpSettings.Curves, delegate { return J().Phases[idx].Curve; },
                    delegate(string v) { J().Phases[idx].Curve = v; plot.Invalidate(); });
                g.Controls.Add(cv);
                parts.Add(cv);
            }

            CheckBox on = Check("跳跃时准星跟着动", delegate { return J().Enabled; },
                delegate(bool v) { J().Enabled = v; foreach (Control c in parts) c.Enabled = v; });
            Tip(on, "按一下跳跃键（在「按键」页改）跳一次，跳的过程中再按不算。可以在预览里选「跳跃（站着）」看形状");
            refreshers.Add(delegate { foreach (Control c in parts) c.Enabled = J().Enabled; });

            TableLayoutPanel box = Grid(1);
            box.Controls.Add(on);
            box.Controls.Add(Line(Lbl("总时长"), total, Unit("ms"), auto));
            box.Controls.Add(g);
            box.Controls.Add(plot);
            return Stack(Group("跳跃", box));
        }

        private Control BuildKeys()
        {
            TableLayoutPanel g = Grid(3);
            Row(g, "开关准星", KeyBtn(0), Blank());
            Row(g, "开火", KeyBtn(1), Blank());
            Row(g, "开镜", KeyBtn(2), Line(Lbl("开镜方式"), Choice(new string[] { "按住", "点一下切换" }, new string[] { "hold", "toggle" },
                delegate { return G.AdsMode; }, delegate(string v) { G.AdsMode = v; })));
            Row(g, "自由视角（转头）", KeyBtn(3),
                Check("按住时隐藏准星", delegate { return G.HideOnFreeLook; }, delegate(bool v) { G.HideOnFreeLook = v; }));
            CheckBox hsOn = Check("换手检测", delegate { return G.HandEnabled; }, delegate(bool v) { G.HandEnabled = v; });
            Tip(hsOn, "程序只能按按键猜你用哪只手；对不上时按两下开关准星键（关再开）回到正常手");
            Row(g, "换手", KeyBtn(4), Line(Lbl("换手方式"), Choice(new string[] { "切换", "按住" }, new string[] { "toggle", "hold" },
                delegate { return G.HandMode; }, delegate(string v) { G.HandMode = v; }), hsOn));
            Label hsMs = Lbl("换手过渡时间");
            Tip(hsMs, "跟着当前配置存");
            Row(g, hsMs, Line(Num(0, 2000, 10, 0, delegate { return Cur.HandSwitchMs; }, delegate(double v) { Cur.HandSwitchMs = v; }), Unit("ms")), Blank());
            CheckBox boostOn = Check("启用", delegate { return G.BoostEnabled; }, delegate(bool v) { G.BoostEnabled = v; });
            Label boostL = Lbl("强化准星");
            Tip(boostL, "按一下变成更显眼的准星，再按一下恢复；开关准星（关再开）也会恢复");
            Row(g, boostL, KeyBtn(12), boostOn);
            Row(g, "左侧身", KeyBtn(5), Line(Lbl("侧身方式"), Choice(new string[] { "按住", "点一下切换" }, new string[] { "hold", "toggle" },
                delegate { return G.LeanMode; }, delegate(string v) { G.LeanMode = v; })));
            Row(g, "右侧身", KeyBtn(6), Blank());
            Row(g, "前进", KeyBtn(7), Blank());
            Row(g, "后退", KeyBtn(8), Blank());
            Row(g, "左移", KeyBtn(9), Blank());
            Row(g, "右移", KeyBtn(10), Blank());
            Row(g, "跳跃", KeyBtn(11), Blank());
            GroupBox gb = Group("按键（点按钮再按新键）", g);
            Tip(gb, "开镜方式、侧身方式要和游戏里一样");
            Button reset = Btn("按键恢复默认", delegate
            {
                if (!Ask("把这一页的按键和方式全部恢复成第一次安装时的样子？")) return;
                G.ResetKeys();
                RefreshValues();
                app.UpdateTray();
                app.Changed();
            });
            return Stack(gb, reset);
        }

        private Control BuildOther()
        {
            TableLayoutPanel o = Grid(2);
            cbMonitor = new ComboBox();
            cbMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMonitor.Width = S(240);
            cbMonitor.Anchor = AnchorStyles.Left;
            cbMonitor.SelectedIndexChanged += delegate
            {
                if (loading || cbMonitor.SelectedIndex < 0) return;
                G.MonitorIndex = cbMonitor.SelectedIndex - 1; // item 0 = primary (-1)
                app.Changed();
            };
            refreshers.Add(RefreshMonitors);
            Row(o, "显示器", cbMonitor);
            Row(o, "帧率", Line(Choice(new string[] { "跟随显示器", "固定FPS" }, new string[] { "vsync", "fixed" },
                delegate { return G.FrameMode; }, delegate(string v) { G.FrameMode = v; }),
                Num(30, 360, 1, 0, delegate { return G.Fps; }, delegate(double v) { G.Fps = v; }), Unit("fps")));
            Row(o, "出现渐变", Line(Num(0, 1000, 10, 0, delegate { return G.ShowFadeMs; }, delegate(double v) { G.ShowFadeMs = v; }), Unit("ms")));
            Row(o, "消失渐变", Line(Num(0, 1000, 10, 0, delegate { return G.HideFadeMs; }, delegate(double v) { G.HideFadeMs = v; }), Unit("ms")));
            Row(o, "窗口", Line(
                Check("设置窗口置顶", delegate { return G.SettingsTopMost; }, delegate(bool v) { G.SettingsTopMost = v; TopMost = v; }),
                Check("启动时打开设置", delegate { return G.ShowSettingsOnStart; }, delegate(bool v) { G.ShowSettingsOnStart = v; }),
                Check("启动动画", delegate { return G.SplashOnStart; }, delegate(bool v) { G.SplashOnStart = v; })));

            chkAutostart = new CheckBox();
            chkAutostart.Text = "开机自动启动";
            chkAutostart.AutoSize = true;
            chkAutostart.Anchor = AnchorStyles.Left;
            chkAutostart.Margin = P(3, 3, 6, 3);
            Tip(chkAutostart, "登录 Windows 后自动在托盘里启动（用任务计划，不弹管理员确认）");
            chkAutostart.CheckedChanged += delegate { if (!loading) SetAutostart(chkAutostart.Checked); };
            refreshers.Add(delegate { chkAutostart.Checked = G.Autostart; });
            Row(o, "开机", Line(chkAutostart, Btn("打开设置文件夹", delegate
            {
                try { Process.Start("explorer.exe", "\"" + Program.BaseDir.TrimEnd('\\') + "\""); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            })));
            GroupBox gGen = Group("常规", o);

            // 只在塔科夫前台时显示
            TableLayoutPanel f = Grid(2);
            TextBox title = new TextBox();
            title.Width = S(220);
            title.Anchor = AnchorStyles.Left;
            title.Margin = P(3, 3, 3, 3);
            title.TextChanged += delegate
            {
                if (loading) return;
                G.GameWindowTitle = title.Text.Trim();
                app.Changed();
            };
            refreshers.Add(delegate { title.Text = G.GameWindowTitle ?? ""; });
            Tip(title, "前台窗口标题里包含这段文字就算塔科夫在前台（不分大小写）");
            Button pick = null;
            pick = Btn("从打开的窗口里选 ▾", delegate { ShowWindowMenu(pick, title); });
            Action titleOn = EnableWhen(delegate { return G.OnlyWhenGameFocused; }, title, pick);
            CheckBox only = Check("只在塔科夫前台时显示", delegate { return G.OnlyWhenGameFocused; }, delegate(bool v) { G.OnlyWhenGameFocused = v; titleOn(); });
            Tip(only, "切出游戏时准星自动隐藏，也不读按键；这个设置窗口在前台时照常显示");
            f.Controls.Add(only);
            f.SetColumnSpan(only, 2);
            Row(f, "窗口标题", Line(title, pick));
            GroupBox gGame = Group("游戏窗口", f);

            // 分辨率换算
            TableLayoutPanel gr = Grid(2);
            Row(gr, "调参分辨率", Line(
                Num(640, 7680, 1, 0, delegate { return G.ResBaseW; }, delegate(double v) { G.ResBaseW = v; }), Unit("×"),
                Num(480, 4320, 1, 0, delegate { return G.ResBaseH; }, delegate(double v) { G.ResBaseH = v; })));
            Row(gr, "游戏分辨率", Line(
                Num(640, 7680, 1, 0, delegate { return G.ResGameW; }, delegate(double v) { G.ResGameW = v; }), Unit("×"),
                Num(480, 4320, 1, 0, delegate { return G.ResGameH; }, delegate(double v) { G.ResGameH = v; }),
                Btn("用当前显示器", delegate
                {
                    Screen[] all = Screen.AllScreens;
                    Screen sc = (G.MonitorIndex >= 0 && G.MonitorIndex < all.Length) ? all[G.MonitorIndex] : Screen.PrimaryScreen;
                    G.ResGameW = Clamp.D(sc.Bounds.Width, 640, 7680, 2160);
                    G.ResGameH = Clamp.D(sc.Bounds.Height, 480, 4320, 1440);
                    RefreshValues();
                    app.Changed();
                })));
            GroupBox gRes = Group("分辨率换算", gr);
            Tip(gRes, "数值按「调参分辨率」填；换了游戏分辨率只改「游戏分辨率」，偏移自动按比例换算（视野 FOV 不会自动换算）");
            return Stack(gGen, gGame, gRes);
        }

        private void ShowWindowMenu(Button anchor, TextBox target)
        {
            if (windowMenu == null) windowMenu = new ContextMenuStrip();
            windowMenu.Items.Clear();
            List<string> titles = null;
            try { titles = GameWindow.VisibleTitles(); }
            catch (Exception ex) { Program.Log("VisibleTitles: " + ex.Message); }
            if (titles == null || titles.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("（没有找到打开的窗口）");
                none.Enabled = false;
                windowMenu.Items.Add(none);
            }
            else
            {
                foreach (string t in titles)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    string pickText = t;
                    string shown = t.Length > 70 ? t.Substring(0, 67) + "…" : t;
                    ToolStripMenuItem it = new ToolStripMenuItem(shown);
                    it.Click += delegate { target.Text = pickText; }; // TextChanged saves it
                    windowMenu.Items.Add(it);
                }
            }
            windowMenu.Show(anchor, new Point(0, anchor.Height));
        }

        private void SetAutostart(bool on)
        {
            string err = null;
            bool ok;
            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try { ok = on ? Autostart.Enable(Application.ExecutablePath, out err) : Autostart.Disable(out err); }
            catch (Exception ex) { ok = false; err = ex.Message; }
            finally { Cursor.Current = old; }
            if (ok) G.Autostart = on;
            else
            {
                MessageBox.Show(this, (on ? "设置开机自动启动失败：" : "取消开机自动启动失败：") + (err ?? "未知错误"), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                try { G.Autostart = Autostart.IsEnabled(); } catch (Exception) { } // show the real task state
            }
            bool was = loading;
            loading = true;
            chkAutostart.Checked = G.Autostart;
            loading = was;
            app.Changed();
        }

        public void RefreshMonitors()
        {
            bool was = loading;
            loading = true;
            cbMonitor.Items.Clear();
            cbMonitor.Items.Add("主显示器");
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
            {
                Rectangle b = all[i].Bounds;
                cbMonitor.Items.Add("显示器 " + (i + 1) + "（" + b.Width + "x" + b.Height + "）" + (all[i].Primary ? " 主" : ""));
            }
            int idx = G.MonitorIndex + 1;
            cbMonitor.SelectedIndex = (idx >= 0 && idx < cbMonitor.Items.Count) ? idx : 0;
            loading = was;
        }

        // ---------------- bottom bar ----------------

        /// <summary>Preview list: item order == PreviewMode order (0 Live … 16 JumpStand, 17 BoostStand).</summary>
        private static readonly string[] PreviewItems = new string[]
        {
            "实时（按键控制）", "静止",
            "W 前", "S 后", "A 左", "D 右", "WA 左前", "WD 右前", "SA 左后", "SD 右后",
            "后坐力最高", "Q 左侧身", "E 右侧身", "A→D 换向", "W→S 换向", "换手来回切（站着）", "跳跃（站着）",
            "强化准星（站着）"
        };

        private void BuildBottomBar()
        {
            bottomBar = new TableLayoutPanel();
            bottomBar.Dock = DockStyle.Fill;
            bottomBar.AutoSize = true;
            bottomBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bottomBar.Margin = new Padding(0);
            bottomBar.Padding = P(6, 4, 6, 4);
            bottomBar.ColumnCount = 7;
            for (int i = 0; i < 5; i++) bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomBar.RowCount = 2;
            bottomBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottomBar.RowStyles.Add(new RowStyle(SizeType.Absolute, Font.Height + S(8)));

            cbPreview = new ComboBox();
            cbPreview.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPreview.Items.AddRange(PreviewItems);
            int w = S(120);
            foreach (string s in PreviewItems) w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width + SystemInformation.VerticalScrollBarWidth + S(16));
            cbPreview.Width = w;
            cbPreview.Anchor = AnchorStyles.Left;
            cbPreview.SelectedIndex = 0;
            cbPreview.SelectedIndexChanged += delegate
            {
                if (cbPreview.SelectedIndex < 0) return;
                app.Preview = (PreviewMode)cbPreview.SelectedIndex; // item order == enum order
                app.Publish();
            };
            NumericUpDown speed = new NumericUpDown();
            speed.Minimum = 0.1m; speed.Maximum = 1.0m; speed.Increment = 0.1m; speed.DecimalPlaces = 1;
            speed.Value = 1.0m;
            speed.Width = NumWidth(0, 1, 1);
            speed.Anchor = AnchorStyles.Left;
            speed.ValueChanged += delegate { app.PreviewSpeed = (double)speed.Value; app.Publish(); };
            Label slow = Lbl("慢放");
            Tip(slow, "预览动画的速度（1.0＝原速）");

            lblSave = Lbl("");
            lblSave.Anchor = AnchorStyles.Right;
            lblSave.ForeColor = SystemColors.GrayText;

            bottomBar.Controls.Add(Lbl("预览"), 0, 0);
            bottomBar.Controls.Add(cbPreview, 1, 0);
            bottomBar.Controls.Add(slow, 2, 0);
            bottomBar.Controls.Add(speed, 3, 0);
            bottomBar.Controls.Add(Btn("重播", delegate { app.Overlay.RequestReset(); }), 4, 0);
            bottomBar.Controls.Add(lblSave, 6, 0);

            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.AutoEllipsis = true;
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Margin = P(3, 0, 3, 0);
            lblStatus.Text = "状态：—";
            lblStatus.Size = new Size(S(100), Font.Height);
            bottomBar.Controls.Add(lblStatus, 0, 1);
            bottomBar.SetColumnSpan(lblStatus, 7);
            root.Controls.Add(bottomBar, 0, 3);
        }

        private static string Signed(double v)
        {
            int r = (int)Math.Round(v);
            return (r >= 0 ? "+" : "") + r.ToString();
        }

        private void UpdateStatus()
        {
            if (!Visible) return;
            DebugInfo d = app.Overlay.Debug;
            if (d == null || d.Motion == null) { lblStatus.Text = "状态：—"; return; }
            MotionDebug m = d.Motion;
            lblStatus.Text = "状态：" + m.DirName + (m.Jumping ? " | 跳跃中" : "") + " | " + m.HandName + (m.Boosted ? " | 强化" : "") +
                " | 侧身 " + m.Lean.ToString("0.00") + " | 后坐力 " + Math.Round(m.Recoil).ToString() +
                "px | 偏移 X" + Signed(m.UiOffsetX) + " Y" + Signed(m.UiOffsetY) + " | " + Math.Round(d.Fps).ToString() + "fps";
        }

        // ---------------- refresh / sizing / closing ----------------

        /// <summary>Re-reads every field from the model, without rebuilding the profile list.</summary>
        public void RefreshValues()
        {
            bool was = loading;
            loading = true;
            try { foreach (Action r in refreshers) r(); }
            finally { loading = was; }
        }

        public void RefreshAll()
        {
            bool was = loading;
            loading = true;
            try
            {
                RefreshProfileList();
                foreach (Action r in refreshers) r();
                chkEnabled.Checked = G.Enabled;
                TopMost = G.SettingsTopMost;
            }
            finally { loading = was; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Content-driven size: the largest tab page, and the top / bottom bars on one line; capped to the work area.
            int pw = 0, ph = 0;
            foreach (Control c in pageContents)
            {
                Size ps = c.GetPreferredSize(Size.Empty);
                pw = Math.Max(pw, ps.Width + c.Left * 2);
                ph = Math.Max(ph, ps.Height + c.Top * 2);
            }
            Size chrome = tabs.Size - tabs.DisplayRectangle.Size;
            int scroll = SystemInformation.VerticalScrollBarWidth;
            int barsW = Math.Max(topBar.GetPreferredSize(Size.Empty).Width, bottomBar.GetPreferredSize(Size.Empty).Width);
            int tabsW = pw + chrome.Width + tabs.Margin.Horizontal + scroll;
            int width = Math.Max(barsW, tabsW);
            int fixedH = header.Height + topBar.GetPreferredSize(new Size(width, 0)).Height
                + bottomBar.GetPreferredSize(new Size(width, 0)).Height + chrome.Height + tabs.Margin.Vertical;
            int height = fixedH + ph + scroll;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            Size frame = Size - ClientSize;
            int maxW = wa.Width - frame.Width, maxH = wa.Height - frame.Height;
            ClientSize = new Size(Math.Min(width, maxW), Math.Min(height, maxH));
            // Never narrower than the bars (no clipped buttons), never shorter than a usable tab area; pages scroll.
            Size minClient = new Size(Math.Min(barsW + S(8), maxW), Math.Min(fixedH + S(220), maxH));
            MinimumSize = minClient + frame;
            CenterToScreen();
        }

        // ---------------- preview back to 实时 when Leo leaves the window (v2.4.1) ----------------

        private void PreviewToLive()
        {
            if (cbPreview != null && cbPreview.SelectedIndex != 0) cbPreview.SelectedIndex = 0;
        }

        /// <summary>Another process in the foreground, or this window minimised / hidden -> 实时. Our own dialogs
        /// (colour picker, message boxes, file dialogs) belong to this process, so they do not reset it.</summary>
        private void CheckPreviewFocus()
        {
            if (cbPreview == null || cbPreview.SelectedIndex == 0) return;
            if (!Visible || WindowState == FormWindowState.Minimized || Native.ForegroundIsOurs() == 0) PreviewToLive();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // After Deactivate the new foreground window is known; check it on the next message.
            try { BeginInvoke((Action)CheckPreviewFocus); } catch (Exception) { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) PreviewToLive();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) PreviewToLive();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                CancelCapture();
                // Never leave a preview running in the raid once the window is closed.
                if (cbPreview.SelectedIndex != 0) cbPreview.SelectedIndex = 0;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                statusTimer.Dispose();
                tips.Dispose();
                if (restoreMenu != null) restoreMenu.Dispose();
                if (profileMenu != null) profileMenu.Dispose();
                if (windowMenu != null) windowMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Profile name dialog: editable ComboBox with AutoComplete from weapons.txt.</summary>
    internal sealed class NameDialog : Form
    {
        private readonly ComboBox box;

        private NameDialog(string title, string initial, List<string> weapons, Font font, bool topMost)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = font;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TopMost = topMost;

            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1; t.AutoSize = true; t.AutoSizeMode = AutoSizeMode.GrowAndShrink; t.Padding = new Padding(10);
            Label l = new Label(); l.Text = "名字（可以用枪名）："; l.AutoSize = true;
            t.Controls.Add(l);
            box = new ComboBox();
            box.DropDownStyle = ComboBoxStyle.DropDown;
            box.Width = 300;
            if (weapons != null && weapons.Count > 0)
            {
                box.Items.AddRange(weapons.ToArray());
                AutoCompleteStringCollection ac = new AutoCompleteStringCollection();
                ac.AddRange(weapons.ToArray());
                box.AutoCompleteCustomSource = ac;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            }
            box.Text = initial ?? "";
            t.Controls.Add(box);
            TableLayoutPanel b = new TableLayoutPanel();
            b.AutoSize = true; b.ColumnCount = 2; b.Anchor = AnchorStyles.Right;
            Button ok = new Button(); ok.Text = "确定"; ok.DialogResult = DialogResult.OK; ok.AutoSize = true;
            Button cancel = new Button(); cancel.Text = "取消"; cancel.DialogResult = DialogResult.Cancel; cancel.AutoSize = true;
            b.Controls.Add(ok, 0, 0); b.Controls.Add(cancel, 1, 0);
            t.Controls.Add(b);
            Controls.Add(t);
            AcceptButton = ok; CancelButton = cancel;
            Theme.Apply(this);
        }

        /// <summary>Returns the trimmed name, or null if cancelled/empty. Uniqueness is applied by the caller.</summary>
        public static string Ask(Form owner, string title, string initial, List<string> weapons)
        {
            using (NameDialog d = new NameDialog(title, initial, weapons, owner.Font, owner.TopMost))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                string n = (d.box.Text ?? "").Trim();
                return n.Length == 0 ? null : n;
            }
        }
    }
}
