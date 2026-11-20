// WizardForm.cs -- v5.1 (W26, decision 13): the first-run 设置向导 window (WinForms). Model and rules: WizardModel.cs.
//
//   SetupWizard.ShowIfNeeded(owner, app.Settings, app.Changed, offerTutorial)   // first launch (G.WizardDone == false)
//   SetupWizard.Run(owner, app.Settings, app.Changed, offerTutorial)            // 「重新运行设置向导」
//
// One step per screen, big readable text, 上一步 / 下一步 at the bottom right, 跳过向导 at the bottom left. Every label is AutoSize
// with a wrap width, every button AutoSize, so text is measured with the real font (Microsoft YaHei UI) and never clipped at
// 100 / 150 / 200 %. The body scrolls when the screen is small (1280×720 @ 200 %). Finishing applies the draft (WizardModel.Apply),
// starts/stops the logon task off the UI thread, then offers the tutorial (skippable).
#if !UI_PURE_CHECK
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal static class SetupWizard
    {
        /// <summary>Runs the wizard when it has not been done yet; true when it ran (finished or skipped).</summary>
        public static bool ShowIfNeeded(Form owner, GlobalSettings g, Action changed, Action offerTutorial)
        {
            if (g == null || g.WizardDone) return false;
            Run(owner, g, changed, offerTutorial);
            return true;
        }

        /// <summary>Shows the wizard (modal). Finish: applies + offers the tutorial. Skip: keeps values, must-read still shown once.</summary>
        public static void Run(Form owner, GlobalSettings g, Action changed, Action offerTutorial)
        {
            if (g == null) return;
            WizardForm f = null;
            try
            {
                bool autostartBefore = g.Autostart;
                f = new WizardForm(g);
                DialogResult r = f.ShowDialog(owner);
                if (r == DialogResult.OK)
                {
                    f.Model.Apply(g);
                    if (g.Autostart != autostartBefore) SetAutostart(owner, g, changed);
                    if (changed != null) changed();
                    if (offerTutorial != null && ThemedDialog.Show(owner, "要不要看一遍新手教程？大约几分钟，随时可以退出。", "看教程", "以后再说"))
                        offerTutorial();
                }
                else
                {
                    WizardModel.Skip(g);
                    if (!g.MustReadAck) MustReadDialog.ShowCard(owner, g, null);
                    if (changed != null) changed();
                }
            }
            catch (Exception ex) { Program.Log("UI: setup wizard: " + ex); }
            finally { if (f != null) f.Dispose(); }
        }

        private static void SetAutostart(Form owner, GlobalSettings g, Action changed)
        {
            bool on = g.Autostart;
            try
            {
                Autostart.BeginSet(on, Application.ExecutablePath, delegate(bool ok, string err, bool now)
                {
                    Action done = delegate
                    {
                        g.Autostart = ok ? on : now; // a failure keeps the real task state
                        if (changed != null) changed();
                        if (!ok && err != null) Program.Log("UI: wizard autostart: " + err);
                    };
                    try
                    {
                        if (owner != null && !owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(done);
                        else done();
                    }
                    catch (Exception) { }
                });
            }
            catch (Exception ex) { Program.Log("UI: wizard autostart: " + ex); }
        }
    }

    internal sealed class WizardForm : Form
    {
        public readonly WizardModel Model;
        private readonly GlobalSettings live;
        private readonly int u;               // one text line in px: every spacing follows it
        private readonly Font titleFont, boldFont;
        private readonly Label lblProgress, lblTitle, lblLead, lblBlock;
        private readonly Panel body;          // scrolls on small screens
        private readonly Button btnBack, btnNext, btnSkip;
        private readonly StepDots dots;
        private readonly List<Label> wraps = new List<Label>();

        // key capture (same rules as the 按键 page: KeyCapture)
        private readonly KeyCapture capture = new KeyCapture();
        private Button capturing;
        private bool suppressClick;
        private readonly MouseButtonPoll mousePoll = new MouseButtonPoll(); // v5.1.1 (F3): side / middle buttons anywhere

        public WizardForm(GlobalSettings g)
        {
            live = g;
            Model = new WizardModel(g);
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Ui(12f);
            u = Math.Max(10, Font.Height);
            titleFont = Theme.Ui(17f, FontStyle.Bold);
            boldFont = Theme.Ui(12f, FontStyle.Bold);
            Text = "设置向导 · " + Program.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(u + u / 2, u, u + u / 2, u);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // dots + progress
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // title
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // lead
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // body
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // blocker
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
            Controls.Add(root);

            FlowLayoutPanel top = new FlowLayoutPanel();
            top.AutoSize = true; top.WrapContents = false; top.Margin = new Padding(0, 0, 0, u / 2);
            dots = new StepDots(Model.Count, u / 2);
            dots.Margin = new Padding(0, u / 4, u / 2, 0);
            top.Controls.Add(dots);
            lblProgress = NewLabel("", Theme.ColMuted);
            top.Controls.Add(lblProgress);
            root.Controls.Add(top, 0, 0);

            lblTitle = NewLabel("", Theme.ColAccent);
            lblTitle.Font = titleFont;
            lblTitle.Margin = new Padding(0, 0, 0, u / 3);
            root.Controls.Add(lblTitle, 0, 1);

            lblLead = NewLabel("", Theme.ColText);
            lblLead.Margin = new Padding(0, 0, 0, u * 2 / 3);
            wraps.Add(lblLead);
            root.Controls.Add(lblLead, 0, 2);

            body = new Panel();
            body.Dock = DockStyle.Fill;
            body.AutoScroll = true;
            body.Margin = new Padding(0);
            root.Controls.Add(body, 0, 3);

            lblBlock = NewLabel("", Theme.ColWarn);
            lblBlock.Margin = new Padding(0, u / 3, 0, u / 3);
            wraps.Add(lblBlock);
            root.Controls.Add(lblBlock, 0, 4);

            TableLayoutPanel bar = new TableLayoutPanel();
            bar.ColumnCount = 4; bar.RowCount = 1;
            bar.AutoSize = true; bar.Dock = DockStyle.Fill; bar.Margin = new Padding(0);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnSkip = NewButton("跳过向导", false);
            btnSkip.Click += delegate { OnSkip(); };
            btnBack = NewButton("上一步", false);
            btnBack.Click += delegate { CancelCapture(); if (Model.Back()) ShowStep(); };
            btnNext = NewButton("下一步", true);
            btnNext.Click += delegate { OnNext(); };
            bar.Controls.Add(btnSkip, 0, 0);
            bar.Controls.Add(btnBack, 2, 0);
            bar.Controls.Add(btnNext, 3, 0);
            root.Controls.Add(bar, 0, 5);

            AcceptButton = null; // Enter must not skip steps while a key is being typed
            Size want = new Size(u * 40, u * 30);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(want.Width, work.Width - u * 2), Math.Min(want.Height, work.Height - u * 3));
            SetWraps();
            ShowStep();
            Theme.Apply(this);
        }

        private Label NewLabel(string text, Color fore)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = fore;
            l.Margin = new Padding(0, 0, 0, u / 3);
            return l;
        }

        private Button NewButton(string text, bool accent)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(u * 5, u + u * 2 / 3);
            b.Padding = new Padding(u / 2, 0, u / 2, 0);
            b.Margin = new Padding(u / 3, 0, 0, 0);
            if (accent) b.Tag = "theme:accent";
            return b;
        }

        private int WrapWidth { get { return Math.Max(u * 10, ClientSize.Width - u * 4 - SystemInformation.VerticalScrollBarWidth); } }

        private void SetWraps()
        {
            foreach (Label l in wraps) if (!l.IsDisposed) l.MaximumSize = new Size(WrapWidth, 0);
        }

        // ---------------------------------------------------------------- navigation

        private void OnNext()
        {
            CancelCapture();
            if (Model.IsLast) { DialogResult = DialogResult.OK; Close(); return; }
            string why = Model.Blocker();
            if (why != null) { lblBlock.Text = why; return; }
            Model.Next();
            ShowStep();
        }

        private void OnSkip()
        {
            CancelCapture();
            if (ThemedDialog.Show(this, "跳过向导？现在的设置保持不变，之后可以在首页「重新运行设置向导」。", "跳过", "继续设置"))
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void ShowStep()
        {
            SuspendLayout();
            body.SuspendLayout();
            for (int i = body.Controls.Count - 1; i >= 0; i--)
            {
                Control c = body.Controls[i];
                body.Controls.RemoveAt(i);
                RemoveWraps(c);
                c.Dispose();
            }
            lblProgress.Text = Model.Progress;
            dots.SetIndex(Model.Index);
            lblTitle.Text = Model.Title;
            lblLead.Text = Model.Lead;
            lblBlock.Text = "";
            Control content = BuildStep(Model.Current);
            content.Location = new Point(0, 0);
            body.Controls.Add(content);
            body.AutoScrollPosition = new Point(0, 0);
            btnBack.Visible = !Model.IsFirst;
            btnSkip.Visible = !Model.IsLast;
            btnNext.Text = Model.IsLast ? "完成" : Model.Current == WizardModel.Step.Welcome ? "开始" : "下一步";
            SetWraps();
            Theme.Apply(this);
            body.ResumeLayout(true);
            ResumeLayout(true);
            UpdateBlocker();
        }

        private void RemoveWraps(Control c)
        {
            Label l = c as Label;
            if (l != null) wraps.Remove(l);
            foreach (Control k in c.Controls) RemoveWraps(k);
        }

        /// <summary>Greys 下一步 only on the 必读 step (the reason is shown when the user clicks anyway elsewhere).</summary>
        private void UpdateBlocker()
        {
            if (Model.Current == WizardModel.Step.MustRead) btnNext.Enabled = Model.Draft.MustReadAck;
            else btnNext.Enabled = true;
            if (Model.Blocker() == null) lblBlock.Text = "";
        }

        // ---------------------------------------------------------------- steps

        private FlowLayoutPanel Column()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.TopDown;
            f.WrapContents = false;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Margin = new Padding(0);
            f.Padding = new Padding(0, 0, 0, u / 2);
            return f;
        }

        private FlowLayoutPanel Row()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = false;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Margin = new Padding(0, 0, 0, u / 2);
            return f;
        }

        private Label Note(string text)
        {
            Label l = NewLabel(text, Theme.ColMuted);
            wraps.Add(l);
            l.MaximumSize = new Size(WrapWidth, 0);
            return l;
        }

        private Label Text1(string text)
        {
            Label l = NewLabel(text, Theme.ColText);
            wraps.Add(l);
            l.MaximumSize = new Size(WrapWidth, 0);
            return l;
        }

        private Control BuildStep(WizardModel.Step s)
        {
            switch (s)
            {
                case WizardModel.Step.Welcome: return StepWelcome();
                case WizardModel.Step.Resolution: return StepResolution();
                case WizardModel.Step.Aspect: return StepAspect();
                case WizardModel.Step.Fov: return StepFov();
                case WizardModel.Step.Ads: return StepAds();
                case WizardModel.Step.GameKeys: return StepGameKeys();
                case WizardModel.Step.Hotkeys: return StepHotkeys();
                case WizardModel.Step.Autostart: return StepAutostart();
                case WizardModel.Step.MustRead: return StepMustRead();
                default: return StepDone();
            }
        }

        private Control StepWelcome()
        {
            FlowLayoutPanel c = Column();
            c.Controls.Add(Text1("这个程序在游戏画面上画一个腰射准星，并跟着你的移动、侧身、开火自动偏移。"));
            c.Controls.Add(Text1("接下来会问你游戏里的几项设置。照着游戏里的设置选就行，每一项旁边的「?」有说明。"));
            c.Controls.Add(Note("程序不会读取游戏文件，按键需要你自己填。"));
            return c;
        }

        private Control StepResolution()
        {
            FlowLayoutPanel c = Column();
            GlobalSettings d = Model.Draft;
            FlowLayoutPanel r1 = Row();
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            int sel = -1;
            for (int i = 0; i < WizardModel.ResPresets.Length; i++)
            {
                int[] p = WizardModel.ResPresets[i];
                cb.Items.Add(p[0] + " × " + p[1]);
                if (p[0] == (int)d.ResGameW && p[1] == (int)d.ResGameH) sel = i;
            }
            cb.Items.Add("自定义…");
            cb.Width = TextRenderer.MeasureText("3840 × 2160", Font).Width + u * 3;
            r1.Controls.Add(cb);
            Button detect = NewButton("自动检测", false);
            r1.Controls.Add(detect);
            r1.Controls.Add(HelpMark.For("resolution", Font, delegate { return live; }));
            c.Controls.Add(r1);

            FlowLayoutPanel r2 = Row();
            NumericUpDown nw = Num(WizardModel.MinW, WizardModel.MaxW, d.ResGameW, "7680");
            NumericUpDown nh = Num(WizardModel.MinH, WizardModel.MaxH, d.ResGameH, "4320");
            r2.Controls.Add(Text1("宽"));
            r2.Controls.Add(nw);
            r2.Controls.Add(Text1("高"));
            r2.Controls.Add(nh);
            c.Controls.Add(r2);
            Label now = Note("");
            c.Controls.Add(now);

            Action show = delegate { now.Text = "当前：" + (int)d.ResGameW + " × " + (int)d.ResGameH; };
            bool busy = false;
            Action fromBoxes = delegate
            {
                if (busy) return;
                d.ResGameW = (double)nw.Value; d.ResGameH = (double)nh.Value;
                busy = true;
                int idx = cb.Items.Count - 1;
                for (int i = 0; i < WizardModel.ResPresets.Length; i++)
                    if (WizardModel.ResPresets[i][0] == (int)d.ResGameW && WizardModel.ResPresets[i][1] == (int)d.ResGameH) idx = i;
                cb.SelectedIndex = idx;
                busy = false;
                show();
            };
            nw.ValueChanged += delegate { fromBoxes(); };
            nh.ValueChanged += delegate { fromBoxes(); };
            cb.SelectedIndexChanged += delegate
            {
                if (busy) return;
                int i = cb.SelectedIndex;
                if (i < 0 || i >= WizardModel.ResPresets.Length) { nw.Focus(); return; }
                busy = true;
                nw.Value = WizardModel.ResPresets[i][0]; nh.Value = WizardModel.ResPresets[i][1];
                busy = false;
                fromBoxes();
            };
            detect.Click += delegate
            {
                bool ok = false;
                try
                {
                    ok = d.DetectGameResolution(delegate
                    {
                        Rectangle b = Screen.FromControl(this).Bounds;
                        return new int[] { b.Width, b.Height };
                    });
                }
                catch (Exception) { ok = false; }
                if (!ok) { lblBlock.Text = "没有检测到分辨率，请手动选择。"; return; }
                busy = true;
                nw.Value = UiNum.ToDec(d.ResGameW, nw.Minimum, nw.Maximum, 0);
                nh.Value = UiNum.ToDec(d.ResGameH, nh.Minimum, nh.Maximum, 0);
                busy = false;
                fromBoxes();
            };
            busy = true;
            cb.SelectedIndex = sel >= 0 ? sel : cb.Items.Count - 1;
            busy = false;
            show();
            return c;
        }

        private NumericUpDown Num(int min, int max, double value, string widest)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max; n.DecimalPlaces = 0;
            n.Value = UiNum.ToDec(value, min, max, 0);
            n.Width = TextRenderer.MeasureText(widest, Font).Width + SystemInformation.VerticalScrollBarWidth + u;
            n.Margin = new Padding(u / 4, 0, u / 2, 0);
            return n;
        }

        private Control StepAspect()
        {
            FlowLayoutPanel c = Column();
            GlobalSettings d = Model.Draft;
            string[] ids = WizardModel.AspectChoices();
            FlowLayoutPanel r1 = Row();
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            string shown = ViewScale.DisplayAspect(d.ResAspect, d.ResGameW, d.ResGameH);
            int sel = ids.Length - 1;
            for (int i = 0; i < ids.Length; i++)
            {
                cb.Items.Add(ids[i] == WizardModel.CustomAspect ? WizardModel.CustomAspectLabel : ids[i]);
                if (ids[i] == shown) sel = i;
            }
            cb.Width = TextRenderer.MeasureText(WizardModel.CustomAspectLabel, Font).Width + u * 3;
            r1.Controls.Add(cb);
            TextBox custom = new TextBox();
            custom.Width = TextRenderer.MeasureText("12345:12345", Font).Width + u;
            custom.Text = sel == ids.Length - 1 ? d.ResAspect : (d.ResAspectCustom ?? "");
            custom.Margin = new Padding(u / 2, 0, 0, 0);
            r1.Controls.Add(custom);
            r1.Controls.Add(HelpMark.For("aspect", Font, delegate { return live; }));
            c.Controls.Add(r1);
            Label stretch = NewLabel("", Theme.ColAccent2);
            wraps.Add(stretch);
            c.Controls.Add(stretch);

            // v5.1.1 (F1, Leo): no 「画面有没有被拉宽」 question. A ratio that differs from the resolution means the game
            // stretches the picture (that is why players change it); 有黑边 is only in 常规 (高级设置).
            Action refresh = delegate
            {
                custom.Visible = cb.SelectedIndex == ids.Length - 1;
                stretch.Text = Model.AspectLine;
            };
            cb.SelectedIndexChanged += delegate
            {
                string id = ids[Math.Max(0, cb.SelectedIndex)];
                if (id == WizardModel.CustomAspect)
                {
                    if (!Model.SetCustomAspect(custom.Text)) d.ResAspect = WizardModel.CustomAspect;
                }
                else d.ResAspect = id;
                refresh();
            };
            custom.TextChanged += delegate
            {
                if (cb.SelectedIndex != ids.Length - 1) return;
                if (!Model.SetCustomAspect(custom.Text)) d.ResAspect = WizardModel.CustomAspect;
                refresh();
            };
            cb.SelectedIndex = sel;
            if (ids[sel] != WizardModel.CustomAspect) d.ResAspect = ids[sel];
            refresh();
            return c;
        }

        private RadioButton Radio(string text)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.Margin = new Padding(0, 0, 0, u / 3);
            return r;
        }

        private Control StepFov()
        {
            FlowLayoutPanel c = Column();
            GlobalSettings d = Model.Draft;
            FlowLayoutPanel r = Row();
            NumericUpDown n = Num(WizardModel.FovMin, WizardModel.FovMax, d.ResFov, "75");
            r.Controls.Add(Text1("FOV"));
            r.Controls.Add(n);
            r.Controls.Add(HelpMark.For("fov", Font, delegate { return live; }));
            c.Controls.Add(r);
            Label hint = Note("范围 50–75，和游戏里填的一样。");
            c.Controls.Add(hint);
            n.ValueChanged += delegate { d.ResFov = (double)n.Value; };
            n.KeyUp += delegate
            {
                double typed;
                string h = double.TryParse(n.Text, out typed) ? WizardModel.FovHint(typed) : "";
                hint.Text = h.Length > 0 ? h : "范围 50–75，和游戏里填的一样。";
                hint.ForeColor = h.Length > 0 ? Theme.ColWarn : Theme.ColMuted;
            };
            return c;
        }

        private Control StepAds()
        {
            FlowLayoutPanel c = Column();
            GlobalSettings d = Model.Draft;
            RadioButton hold = Radio("按住右键开镜，松开退出（按住）");
            RadioButton tog = Radio("点一下右键开镜，再点一下退出（切换）");
            hold.Checked = d.AdsMode != "toggle";
            tog.Checked = d.AdsMode == "toggle";
            // v5.1.1 (F2): one column for both choices + an explicit group, so picking one always clears the other
            Theme.Exclusive(hold, tog);
            hold.CheckedChanged += delegate { if (hold.Checked) d.AdsMode = "hold"; };
            tog.CheckedChanged += delegate { if (tog.Checked) d.AdsMode = "toggle"; };
            FlowLayoutPanel opts = Column();
            opts.Padding = new Padding(0);
            opts.Controls.Add(hold);
            opts.Controls.Add(tog);
            FlowLayoutPanel r = Row();
            r.Controls.Add(opts);
            r.Controls.Add(HelpMark.For("ads", Font, delegate { return live; }));
            c.Controls.Add(r);
            c.Controls.Add(Note("选错了的话，开镜时还会看到准星。"));
            return c;
        }

        private Control StepGameKeys()
        {
            TableLayoutPanel t = KeyTable();
            // v5.1.1 (F3, Leo): 开火 / 开镜 / 跳跃 are not asked (left button / right button / Space; 「按键」 page can change them)
            AddActionRow(t, "左侧身", KeyBindings.ActionLeanLeft, null);
            AddActionRow(t, "右侧身", KeyBindings.ActionLeanRight, null);
            AddActionRow(t, "自由视角（转头）", KeyBindings.ActionFreeLook, null);
            AddActionRow(t, "切换战术装置模式（= 强化键）", KeyBindings.ActionBoost, "boost");
            AddSingleRow(t, "丢雷", delegate { return Model.Draft.GrenadeKey; }, delegate(int vk) { Model.Draft.GrenadeKey = vk; }, "grenade");
            AddSingleRow(t, "换弹", delegate { return Model.Draft.AmmoReloadKey; }, delegate(int vk) { Model.Draft.AmmoReloadKey = vk; }, "ammo");
            FlowLayoutPanel c = Column();
            c.Controls.Add(t);
            c.Controls.Add(Note("没改过游戏按键的，直接点「下一步」。开火 = 鼠标左键，开镜 = 鼠标右键，跳跃 = 空格，不用填（要改去「按键」页）。"
                + "战术装置的开关（T）也不用填：它只负责开关装置，不影响准星。鼠标侧键、中键都可以用：点一下按钮，再按那个鼠标键。"));
            return c;
        }

        private Control StepHotkeys()
        {
            GlobalSettings d = Model.Draft;
            TableLayoutPanel t = KeyTable();
            AddActionRow(t, "开关准星（按两下 = 复位）", KeyBindings.ActionToggle, "enable");
            AddActionRow(t, "换手", KeyBindings.ActionHandSwitch, "hand");
            FlowLayoutPanel c = Column();
            c.Controls.Add(t);

            // v5.1.1 (F3): both 强化方式 choices always shown (label line, then the two options one under the other)
            c.Controls.Add(Text1("强化方式"));
            RadioButton tog = Radio("切换（按一下开，再按一下关）");
            RadioButton hold = Radio("按住（按住时开，松开就关）");
            Theme.Exclusive(tog, hold);
            tog.Checked = d.BoostToggle; hold.Checked = !d.BoostToggle;
            tog.CheckedChanged += delegate { if (tog.Checked) d.BoostToggle = true; };
            hold.CheckedChanged += delegate { if (hold.Checked) d.BoostToggle = false; };
            FlowLayoutPanel radios = Column();
            radios.Padding = new Padding(0);
            radios.Controls.Add(tog); radios.Controls.Add(hold);
            FlowLayoutPanel rb = Row();
            rb.Controls.Add(radios);
            rb.Controls.Add(HelpMark.For("boost", Font, delegate { return live; }));
            c.Controls.Add(rb);

            CheckBox gren = new CheckBox();
            gren.Text = "按丢雷键时隐藏准星（再按一次显示）";
            gren.AutoSize = true;
            gren.Checked = d.GrenadeHideOn;
            gren.CheckedChanged += delegate { d.GrenadeHideOn = gren.Checked; };
            FlowLayoutPanel rg = Row();
            rg.Controls.Add(gren);
            rg.Controls.Add(HelpMark.For("grenade", Font, delegate { return live; }));
            c.Controls.Add(rg);
            return c;
        }

        private Control StepAutostart()
        {
            FlowLayoutPanel c = Column();
            GlobalSettings d = Model.Draft;
            CheckBox cb = new CheckBox();
            cb.Text = "开机自动启动（在后台运行，右下角托盘图标）";
            cb.AutoSize = true;
            cb.Checked = d.Autostart;
            cb.CheckedChanged += delegate { d.Autostart = cb.Checked; };
            FlowLayoutPanel r = Row();
            r.Controls.Add(cb);
            r.Controls.Add(HelpMark.For("autostart", Font, delegate { return live; }));
            c.Controls.Add(r);
            return c;
        }

        private Control StepMustRead()
        {
            FlowLayoutPanel c = Column();
            MustReadView v = new MustReadView(Model.Draft, Font);
            v.SetWrapWidth(WrapWidth - u * 2);
            c.Controls.Add(v);
            Button ack = NewButton(MustRead.AckText, true);
            ack.Margin = new Padding(0, u / 2, 0, 0);
            Label done = NewLabel("", Theme.ColAccent);
            Action show = delegate
            {
                ack.Enabled = !Model.Draft.MustReadAck;
                done.Text = Model.Draft.MustReadAck ? "✓ 已确认，可以点「下一步」" : "";
            };
            ack.Click += delegate { Model.Draft.MustReadAck = true; show(); UpdateBlocker(); };
            FlowLayoutPanel r = Row();
            r.Controls.Add(ack);
            done.Margin = new Padding(u / 2, u / 2 + u / 4, 0, 0);
            r.Controls.Add(done);
            c.Controls.Add(r);
            show();
            return c;
        }

        private Control StepDone()
        {
            FlowLayoutPanel c = Column();
            GlobalSettings d = Model.Draft;
            c.Controls.Add(Text1("分辨率 " + (int)d.ResGameW + " × " + (int)d.ResGameH + " · 画面比例 "
                + ViewScale.DisplayAspect(d.ResAspect, d.ResGameW, d.ResGameH)
                + (Model.StretchText.Length > 0 ? "（" + Model.StretchText + "）" : "") + " · FOV " + (int)d.ResFov));
            c.Controls.Add(Text1("开镜：" + (d.AdsMode == "toggle" ? "切换" : "按住") + " · 强化键 " + d.Keys.ComboOf(KeyBindings.ActionBoost)
                + "（" + (d.BoostToggle ? "切换" : "按住") + "） · 开关准星 " + d.Keys.ComboOf(KeyBindings.ActionToggle)));
            // v5.1 (Leo 21 Nov 2026): no 统计 line in the wizard (stats on by default; off in 常规 → 高级设置)
            c.Controls.Add(Note("点「完成」保存。首页顶部的状态栏会一直告诉你准星现在的状态；看不懂时去「常见问题」。"));
            return c;
        }

        // ---------------------------------------------------------------- key rows

        private TableLayoutPanel KeyTable()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 3;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.Margin = new Padding(0, 0, 0, u / 2);
            return t;
        }

        private Button KeyButtonFor(Func<string> text)
        {
            Button b = new Button();
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(TextRenderer.MeasureText("鼠标侧键1（XButton1）", Font).Width + u, u + u / 2);
            b.Padding = new Padding(u / 3, 0, u / 3, 0);
            b.Margin = new Padding(u / 2, 2, u / 4, 2);
            b.Text = text();
            b.Tag = text;
            return b;
        }

        private void AddLabelCell(TableLayoutPanel t, string name, int row)
        {
            Label l = Text1(name);
            l.Anchor = AnchorStyles.Left;
            l.MaximumSize = new Size(u * 14, 0);
            t.Controls.Add(l, 0, row);
        }

        private void AddActionRow(TableLayoutPanel t, string name, int action, string help)
        {
            int row = t.Controls.Count / 3;
            t.RowCount = row + 1;
            AddLabelCell(t, name, row);
            KeyBindings k = Model.Draft.Keys;
            Button b = KeyButtonFor(delegate { return k.Get(action) <= 0 ? "未设置" : k.ComboOf(action); });
            WireCapture(b, delegate(int vk, int mods)
            {
                int other = KeyConflict.Find(k, vk, mods, action);
                if (other >= 0 && !ThemedDialog.Show(this, KeyConflict.Question(k, action, vk, mods, other), true)) return;
                KeyConflict.Apply(k, action, vk, mods, other);
                RefreshKeyButtons();
            }, true);
            t.Controls.Add(b, 1, row);
            if (help != null) t.Controls.Add(HelpMark.For(help, Font, delegate { return live; }), 2, row);
            else t.Controls.Add(new Label { AutoSize = true, Text = "" }, 2, row);
        }

        private void AddSingleRow(TableLayoutPanel t, string name, Func<int> get, Action<int> set, string help)
        {
            int row = t.Controls.Count / 3;
            t.RowCount = row + 1;
            AddLabelCell(t, name, row);
            Button b = KeyButtonFor(delegate { int v = get(); return v <= 0 ? "未设置" : KeyBindings.KeyName(v); });
            WireCapture(b, delegate(int vk, int mods) { set(vk); RefreshKeyButtons(); }, false);
            t.Controls.Add(b, 1, row);
            if (help != null) t.Controls.Add(HelpMark.For(help, Font, delegate { return live; }), 2, row);
            else t.Controls.Add(new Label { AutoSize = true, Text = "" }, 2, row);
        }

        private void RefreshKeyButtons() { Refresh(body); UpdateBlocker(); }

        private void Refresh(Control c)
        {
            Button b = c as Button;
            Func<string> f = b != null ? b.Tag as Func<string> : null;
            if (f != null && b != capturing) { b.Text = f(); b.ForeColor = Theme.ColText; }
            foreach (Control k in c.Controls) Refresh(k);
        }

        private Action<int, int> onCaptured;

        private void WireCapture(Button b, Action<int, int> done, bool allowMods)
        {
            b.Click += delegate
            {
                if (suppressClick) { suppressClick = false; return; }
                CancelCapture();
                capturing = b;
                onCaptured = delegate(int vk, int mods) { done(vk, allowMods ? mods : 0); };
                capture.Start();
                b.Text = capture.Shown;
                b.ForeColor = Theme.ColAccent;
                mousePoll.Start(delegate(int pvk)
                {
                    if (capturing == b && capture.Mouse(pvk, ModsOf(Control.ModifierKeys)) == KeyCapture.Result.Done) FinishCapture();
                });
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
                if (e.Button == MouseButtons.Left) suppressClick = true;
                if (capture.Mouse(vk, ModsOf(Control.ModifierKeys)) == KeyCapture.Result.Done) FinishCapture();
            };
            b.Leave += delegate { if (capturing == b) CancelCapture(); };
        }

        private static int ModsOf(Keys keyData)
        {
            int m = 0;
            if ((keyData & Keys.Control) == Keys.Control) m |= KeyBindings.ModCtrl;
            if ((keyData & Keys.Shift) == Keys.Shift) m |= KeyBindings.ModShift;
            if ((keyData & Keys.Alt) == Keys.Alt) m |= KeyBindings.ModAlt;
            return m;
        }

        private void CancelCapture()
        {
            mousePoll.Stop();
            Button b = capturing;
            capturing = null;
            onCaptured = null;
            if (b != null && !b.IsDisposed)
            {
                Func<string> f = b.Tag as Func<string>;
                if (f != null) b.Text = f();
                b.ForeColor = Theme.ColText;
            }
        }

        private void FinishCapture()
        {
            mousePoll.Stop();
            Button b = capturing;
            Action<int, int> done = onCaptured;
            capturing = null;
            onCaptured = null;
            if (b == null || done == null) return;
            done(capture.Vk, capture.Mods);
            suppressClick = false;
            RefreshKeyButtons();
        }

        private const int WmKeyUp = 0x0101, WmSysKeyUp = 0x0105;

        protected override bool ProcessKeyPreview(ref Message m)
        {
            if (capturing != null && (m.Msg == WmKeyUp || m.Msg == WmSysKeyUp))
            {
                int vk = (int)(m.WParam.ToInt64() & 0xFF);
                KeyCapture.Result r = capture.KeyUp(vk, KeyBindings.ModsDown(Native.IsDown));
                if (r == KeyCapture.Result.Done) { FinishCapture(); return true; }
                if (r == KeyCapture.Result.Waiting) { capturing.Text = capture.Shown; return true; }
            }
            return base.ProcessKeyPreview(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing != null)
            {
                KeyCapture.Result r = capture.KeyDown((int)(keyData & Keys.KeyCode), ModsOf(keyData));
                if (r == KeyCapture.Result.Cancelled) CancelCapture();
                else if (r == KeyCapture.Result.Done) FinishCapture();
                else if (capturing != null) capturing.Text = capture.Shown;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // the title-bar X = 跳过 (asks first; must-read is still shown once by SetupWizard.Run)
            if (DialogResult != DialogResult.OK && DialogResult != DialogResult.Cancel && e.CloseReason == CloseReason.UserClosing)
            {
                if (!ThemedDialog.Show(this, "跳过向导？现在的设置保持不变，之后可以在首页「重新运行设置向导」。", "跳过", "继续设置"))
                {
                    e.Cancel = true;
                    return;
                }
                DialogResult = DialogResult.Cancel;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                mousePoll.Dispose();
                if (titleFont != null) titleFont.Dispose();
                if (boldFont != null) boldFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Step progress: one dot per step, done = accent, current = ring, later = muted.</summary>
    internal sealed class StepDots : Control
    {
        private readonly int count, d;
        private int index;

        public StepDots(int count, int dot)
        {
            this.count = count;
            d = Math.Max(6, dot);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(count * d * 2, d + 2);
        }

        public void SetIndex(int i) { index = i; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush done = new SolidBrush(Theme.ColAccent))
            using (SolidBrush later = new SolidBrush(Theme.ColBorder))
            using (Pen ring = new Pen(Theme.ColAccent, 2f))
                for (int i = 0; i < count; i++)
                {
                    Rectangle r = new Rectangle(i * d * 2 + 1, 1, d - 1, d - 1);
                    if (i < index) g.FillEllipse(done, r);
                    else if (i == index) g.DrawEllipse(ring, r);
                    else g.FillEllipse(later, r);
                }
        }
    }

    /// <summary>「画面有没有被拉宽」 picture: the same screen twice -- a round circle (正常) and the same circle stretched wide
    /// (被拉宽) -- plus a third with black bars on both sides (有黑边). Drawn, no image files.</summary>
    internal sealed class StretchPicture : Control
    {
        private static readonly string[] Captions = { "正常（圆是圆的）", "被拉宽（圆变扁）", "有黑边" };

        public StretchPicture(int u, Font font)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = font;
            int cap = 0;
            foreach (string c in Captions) cap = Math.Max(cap, TextRenderer.MeasureText(c, font).Width);
            int w = Math.Max(u * 7, cap + u / 2);
            Size = new Size(w * 3 + u * 2, u * 4 + font.Height + u / 2);
            Margin = new Padding(0, u / 3, 0, u / 2);
            Tag = w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.ColBackground);
            int w = (int)Tag, gap = (Width - w * 3) / 2, h = Height - Font.Height - 4;
            for (int i = 0; i < 3; i++)
            {
                Rectangle scr = new Rectangle(i * (w + gap), 0, w - 1, h - 1);
                using (SolidBrush bg = new SolidBrush(Theme.ColInput)) g.FillRectangle(bg, scr);
                int cy = scr.Y + scr.Height / 2, r = scr.Height / 3;
                Rectangle circle;
                if (i == 1) circle = new Rectangle(scr.X + scr.Width / 2 - r * 3 / 2, cy - r, r * 3, r * 2);
                else circle = new Rectangle(scr.X + scr.Width / 2 - r, cy - r, r * 2, r * 2);
                if (i == 2)
                {
                    int bar = scr.Width / 6;
                    using (SolidBrush black = new SolidBrush(Color.Black))
                    {
                        g.FillRectangle(black, scr.X, scr.Y, bar, scr.Height);
                        g.FillRectangle(black, scr.Right - bar, scr.Y, bar + 1, scr.Height);
                    }
                }
                using (Pen p = new Pen(i == 1 ? Theme.ColWarn : Theme.ColAccent, 2f)) g.DrawEllipse(p, circle);
                using (Pen f = new Pen(Theme.ColBorder)) g.DrawRectangle(f, scr);
                TextRenderer.DrawText(g, Captions[i], Font, new Rectangle(scr.X, h + 2, w, Font.Height + 2), Theme.ColMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
#endif
