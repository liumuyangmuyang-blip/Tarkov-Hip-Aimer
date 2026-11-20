using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 反馈 page + 外观 host (StylePanel) (PAGE-D). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>外观: STYLE worker's StylePanel (all crosshair looks, custom PNG upload).</summary>
        private Control BuildStyle()
        {
            Action changed = delegate { if (!loading) app.Changed(); };
            try { stylePanel = new StylePanel(G, changed); }
            catch (Exception ex) { Program.Log("UI: StylePanel: " + ex); stylePanel = null; }
            // v5.1.1 (X7, Leo 21 Nov): the 弹药提示 setup (弹匣容量 / 射速 / 换弹键 / 换弹时间 …) is the first group 「先设置」 of
            // 外观 -> 弹药提示; built here by the settings window (one builder, same fields, 常调 stays in sync)
            Control ammoSetup = BuildAmmoBox();
            StylePanel sp = stylePanel as StylePanel;
            ReleaseHeldOnShow(ammoSetup); // v5.1.1 (P3): BuildAmmoBox's grids, let go when the box first shows
            if (sp != null) { sp.SetAmmoSetup(ammoSetup); return sp; }
            return Stack(Missing("外观面板"), Group("弹药提示 · 先设置", ammoSetup));
        }

        // ---------------- 反馈 (last page) ----------------

        /// <summary>The douyin web search for the account (douyin has no public profile URL by handle).</summary>
        private const string DouyinSearchUrl = "https://www.douyin.com/search/" + Douyin;

        private Font fbHeroFont, fbCardFont;
        private Label fbHero, fbLead, fbBannerText;
        private TableLayoutPanel fbCardGrid;
        private readonly List<FeedbackCard> fbCards = new List<FeedbackCard>();
        private Section fbContact;
        private TableLayoutPanel fbContactGrid;
        /// <summary>Contact rows as {name, value, buttons}; laid out in 3 columns, or buttons under the value when narrow.</summary>
        private readonly List<Control[]> fbContactRows = new List<Control[]>();
        private int fbContactMode = -1;
        private FeedbackBanner fbBanner;
        private int fbCols = -1;

        /// <summary>
        /// 反馈 (last page): a friendly headline, the contact sentence word for word (SPEC item 12), three cards
        /// (功能建议 / 反馈创意 / Bug 反馈), GitHub / 邮箱 / 抖音号 with 复制 + 打开, the update line, version + by Leo.
        /// Cards sit 3 in a row on a normal window and stack on a narrow one (FbLayout, re-run on resize and text size).
        /// </summary>
        private Control BuildFeedback()
        {
            fbHero = Keep(Lbl("有想法？遇到问题？告诉我！"), Theme.ColAccent);
            fbHero.Margin = P(3, 0, 6, 6);
            fbLead = Keep(Lbl(FeedbackText), Theme.ColText);
            fbLead.Margin = P(3, 0, 6, 16);

            fbCards.Add(new FeedbackCard(0, Theme.ColAccent, k));
            fbCards.Add(new FeedbackCard(1, Theme.ColAccent2, k));
            fbCards.Add(new FeedbackCard(2, Theme.ColWarn, k));
            FillCard(fbCards[0], "功能建议", "想要的新功能、想改进的地方，都可以说。", "例如：想加一个按键、想要一个开关。");
            FillCard(fbCards[1], "反馈创意", "新的准星样式、更顺手的设置方法、教程里没讲清楚的地方。", "例如：画一个你想要的准星发给我。");
            FillCard(fbCards[2], "Bug 反馈", "说清楚做了什么、出了什么问题，最好附上截图。", "附上 crash.log 和版本信息会更好查：",
                Btn("打开 crash.log 文件夹", delegate { OpenLogFolder(); }),
                Btn("复制版本信息", delegate { CopyVersionInfo(); }));
            Tip(fbCards[2].Buttons[0], "打开设置文件夹并选中 crash.log（出过错才会有这个文件）");
            Tip(fbCards[2].Buttons[1], "复制版本号、Windows 版本、屏幕分辨率和缩放，粘贴到反馈里");

            fbCardGrid = new TableLayoutPanel();
            fbCardGrid.AutoSize = true;
            fbCardGrid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // Leo 17 Nov (aligned UI): cards start at the page's left edge like every other page's cards, 12 px apart
            fbCardGrid.Margin = new Padding(0);
            fbCardGrid.Padding = new Padding(0);

            fbContact = Group("联系方式", BuildContactRows());
            fbContact.Margin = P(0, 0, 0, 12);

            fbBannerText = Keep(Lbl("更新会发布在 GitHub 和抖音群内。"), Theme.ColText);
            fbBannerText.Margin = P(0, 4, 14, 4);
            Button upd = Btn("去 GitHub 看看", delegate { OpenUrl(GitHubUrl, GitHubUrl, "GitHub 地址"); });
            Tip(upd, "在浏览器里打开 " + GitHubUrl);
            fbBanner = new FeedbackBanner(k);
            fbBanner.Controls.Add(fbBannerText, 0, 0);
            if (app.net != null)
            {
                // v5.1 (W28): 检查更新 now (the hub answers in words on the banner line)
                Button chk = null;
                chk = Btn("检查更新", delegate
                {
                    chk.Enabled = false;
                    try
                    {
                        app.net.Updates.CheckNow(delegate(bool asked, string msg)
                        {
                            if (IsDisposed) return;
                            chk.Enabled = true;
                            if (!string.IsNullOrEmpty(msg)) fbBannerText.Text = msg;
                        });
                    }
                    catch (Exception ex) { chk.Enabled = true; Program.Log("net: check now: " + ex.Message); }
                });
                Tip(chk, "现在去 GitHub 看一下有没有新版本");
                fbBanner.Controls.Add(Line(chk, upd), 1, 0);
            }
            else fbBanner.Controls.Add(upd, 1, 0);
            fbBanner.Margin = P(0, 0, 0, 12);

            Label ver = TagLabel(Program.ChineseName + "  ·  " + Program.ShortName + "  " + Program.Version + "  ·  by Leo", Theme.ColMuted);
            ver.Margin = P(3, 2, 6, 4);

            FixUp(delegate { FbFonts(); });
            // v5.1 round 3 (Leo): no in-app send box; feedback goes by the user's own email (联系方式 → 「打开邮箱」)
            TableLayoutPanel root = HeldStack(fbHero, fbLead, fbCardGrid, fbContact, fbBanner, ver);
            ReleaseHeldOnShow(root); // v5.1.1 (P3): held through the first fill, one layout pass on the first show
            fbRoot = root;
            // v5.1.1 (P3): a resize while 反馈 is not shown is laid out when the page shows (VisibleChanged), not at once
            pageHost.Resize += delegate { if (fbRoot != null && !fbRoot.Visible) fbDirty = true; else FbLayout(); };
            root.VisibleChanged += delegate { if (fbDirty && root.Visible) { fbDirty = false; FbLayout(); } };
            // first layout at build, as before (same first look: FitControlHeights then gives the buttons the line height);
            // v5.1.1 (P3): it lays out the page body only (was the whole window)
            FixUp(delegate { FbLayout(); });
            Disposed += delegate
            {
                if (fbHeroFont != null) fbHeroFont.Dispose();
                if (fbCardFont != null) fbCardFont.Dispose();
            };
            return root;
        }

        /// <summary>v5.1.1 (P3): the 反馈 page body (FbLayout lays out only this, not the whole window).</summary>
        private TableLayoutPanel fbRoot;
        /// <summary>v5.1.1 (P3): the page host was resized while 反馈 was hidden: FbLayout on its next show.</summary>
        private bool fbDirty;

        private TableLayoutPanel BuildContactRows()
        {
            TableLayoutPanel g = HeldGrid(3);
            LinkLabel gh = new LinkLabel();
            // v5.1 (W30 text fit): the full address was wider than its card at 1280x720 @200 %; 复制 copies the full address
            gh.Text = "github.com/…/Tarkov-Hip-Aimer";
            gh.AutoSize = true;
            gh.Anchor = AnchorStyles.Left;
            gh.Margin = P(3, 6, 14, 6);
            gh.Font = monoFont;
            gh.LinkBehavior = LinkBehavior.HoverUnderline;
            gh.LinkClicked += delegate { OpenUrl(GitHubUrl, GitHubUrl, "GitHub 地址"); };
            Tip(gh, "点一下在浏览器里打开 " + GitHubUrl);

            Button ghOpen = Btn("打开", delegate { OpenUrl(GitHubUrl, GitHubUrl, "GitHub 地址"); });
            Tip(ghOpen, "在浏览器里打开");
            Button mail = Btn("打开邮箱", delegate
            {
                // v5.1 round 3 (W28): a mail to Leo with version / Windows / screen already written
                bool opened = false;
                try { opened = NetMail.Open(NetMail.FeedbackLink(FeedbackEnvProbe.Collect())); } catch (Exception ex) { Program.Log("UI: mail: " + ex.Message); }
                if (!opened) CopyText(Email, "这台电脑没有设置邮件软件，邮箱已复制，请在网页邮箱里发");
            });
            Tip(mail, "用电脑上的邮件软件新建一封发给作者的邮件，版本信息已写好");
            Button dy = Btn("打开", delegate { OpenUrl(DouyinSearchUrl, Douyin, "抖音号"); });
            Tip(dy, "在浏览器里搜索抖音号 " + Douyin + "；手机抖音里直接搜 " + Douyin + " 更方便");

            // UIALIGN41 #10: the last-column buttons (打开 / 写邮件 / 打开) share one width, so that column has one right edge
            Button[] lastCol = { ghOpen, mail, dy };
            FixUp(delegate
            {
                int w = 0;
                // v5.0.2 (UI L2): measured with the window's font (the buttons have no parent yet, so they still had the
                // default control font and came out too narrow)
                foreach (Button b in lastCol) { b.Font = Font; b.MinimumSize = new Size(0, b.MinimumSize.Height); w = Math.Max(w, b.GetPreferredSize(Size.Empty).Width); }
                foreach (Button b in lastCol) b.MinimumSize = new Size(w, b.MinimumSize.Height);
            });
            fbContactRows.Add(new Control[] { ContactName("GitHub"), gh, Line(CopyBtn(GitHubUrl, "GitHub 地址"), ghOpen) });
            fbContactRows.Add(new Control[] { ContactName("邮箱"), Value(Email), Line(CopyBtn(Email, "邮箱"), mail) });
            fbContactRows.Add(new Control[] { ContactName("抖音号"), Line(Value(Douyin), Unit("（主页有交流群）")), Line(CopyBtn(Douyin, "抖音号"), dy) });
            fbContactGrid = g;
            FbContactMode(0);
            return g;
        }

        /// <summary>mode 0 = name | value | buttons; mode 1 = name | value, buttons on the next line under the value.</summary>
        private void FbContactMode(int mode)
        {
            if (mode == fbContactMode || fbContactGrid == null) return;
            fbContactMode = mode;
            TableLayoutPanel g = fbContactGrid;
            g.SuspendLayout();
            g.Controls.Clear();
            g.ColumnStyles.Clear();
            g.RowStyles.Clear();
            int cols = mode == 0 ? 3 : (mode == 1 ? 2 : 1);
            g.ColumnCount = cols;
            for (int i = 0; i < cols; i++) g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row = 0;
            foreach (Control[] r in fbContactRows)
            {
                if (mode == 2)
                {
                    // very narrow: name, value, buttons under one another
                    g.Controls.Add(r[0], 0, row++);
                    g.Controls.Add(r[1], 0, row++);
                    g.Controls.Add(r[2], 0, row++);
                    continue;
                }
                g.Controls.Add(r[0], 0, row);
                g.Controls.Add(r[1], 1, row);
                if (mode == 0) g.Controls.Add(r[2], 2, row);
                else { row++; g.Controls.Add(r[2], 1, row); }
                row++;
            }
            g.RowCount = row;
            for (int i = 0; i < row; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FbContactMargins(mode);
            g.ResumeLayout(true);
        }

        /// <summary>Narrow mode: a little gap after each contact's buttons so the three contacts read as groups.</summary>
        private void FbContactMargins(int mode)
        {
            foreach (Control[] r in fbContactRows) r[2].Margin = mode == 0 ? new Padding(0) : new Padding(0, 0, 0, S(8));
        }

        /// <summary>Widest cell of one contact column (0 name, 1 value, 2 buttons).</summary>
        private int FbContactCol(int c)
        {
            int m = 0;
            foreach (Control[] r in fbContactRows)
                m = Math.Max(m, r[c].GetPreferredSize(Size.Empty).Width + r[c].Margin.Horizontal);
            return m;
        }

        /// <summary>The widest contact layout that fits inner px: 0 = 3 columns, 1 = buttons under the value, 2 = all stacked.</summary>
        private int FbContactFit(int inner)
        {
            int n = FbContactCol(0), v = FbContactCol(1), b = FbContactCol(2);
            if (n + v + b <= inner) return 0;
            if (n + Math.Max(v, b) <= inner) return 1;
            return 2;
        }

        private Label ContactName(string text)
        {
            Label l = Lbl(text);
            l.Margin = P(3, 6, 12, 6);
            return l;
        }

        /// <summary>A label whose colours Theme.Apply leaves alone (transparent on the cards / banner).</summary>
        private static Label Keep(Label l, Color fore)
        {
            l.ForeColor = fore;
            l.BackColor = Color.Transparent;
            l.Tag = "theme:keep-fbtext";
            return l;
        }

        private void FillCard(FeedbackCard c, string title, string text, string example, params Button[] buttons)
        {
            c.Title = Keep(Lbl(title), c.Accent);
            c.Title.Margin = new Padding(0, 0, 0, S(6));
            c.Text1 = Keep(Lbl(text), Theme.ColText);
            c.Text1.Margin = new Padding(0, 0, 0, S(4));
            c.Text2 = Keep(Lbl(example), Theme.ColMuted);
            c.Text2.Margin = new Padding(0, 0, 0, S(2));
            c.Buttons = buttons ?? new Button[0];
            c.Controls.Add(c.Title, 0, 0);
            c.Controls.Add(c.Text1, 0, 1);
            c.Controls.Add(c.Text2, 0, 2);
            if (c.Buttons.Length > 0)
            {
                // one button per line: a long label never pushes the card wider than its column
                TableLayoutPanel bl = HeldGrid(1);
                bl.Margin = new Padding(0, S(6), 0, 0);
                bl.BackColor = Color.Transparent;
                bl.Tag = "theme:keep-fbbtns";
                foreach (Button b in c.Buttons) { b.Margin = new Padding(0, S(2), 0, S(2)); bl.Controls.Add(b); }
                c.Controls.Add(bl, 0, 3);
            }
            // rows hug their content; the last row takes the extra height when a card is stretched to its row's height
            c.RowCount = 5;
            for (int i = 0; i < 4; i++) c.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        }

        /// <summary>Headline 16 pt bold (page title size) and card titles 13 pt bold (v4.3.1: fixed size).</summary>
        private void FbFonts()
        {
            Font oldHero = fbHeroFont, oldCard = fbCardFont;
            fbHeroFont = Theme.Ui(16f, FontStyle.Bold);
            fbCardFont = Theme.Ui(13f, FontStyle.Bold);
            if (fbHero != null) fbHero.Font = fbHeroFont;
            foreach (FeedbackCard c in fbCards)
            {
                if (c.Title != null) c.Title.Font = fbCardFont;
            }
            if (oldHero != null) oldHero.Dispose();
            if (oldCard != null) oldCard.Dispose();
        }

        /// <summary>
        /// Width of the page content = the same edges as every other page (FitWidths: the page padding to min(visible page,
        /// the 840 logical px 「内容宽度」); Leo 17 Nov, aligned UI); 3 cards in a row when each gets at least 205 px x text
        /// scale, otherwise one under another (full width). Equal card heights in a row; wrap widths follow the cards.
        /// </summary>
        private void FbLayout()
        {
            if (fbCardGrid == null || pageHost == null || fbCards.Count == 0) return;
            int right = Math.Min(pageHost.ClientSize.Width - S(10) - SystemInformation.VerticalScrollBarWidth - S(12 + 4), S(22) + F(ContentWidthPx));
            int w = Math.Max(S(280), right - S(22));
            int gap = S(12);
            int cols = (w - 2 * gap) / 3 >= F(205) ? 3 : 1;
            int cw = cols == 3 ? (w - 2 * gap) / 3 : w;

            // v5.1.1 (P3): the page body's layout only (was the whole window's: SuspendLayout / ResumeLayout(true) on the form)
            Control lay = fbRoot != null && !fbRoot.IsDisposed ? (Control)fbRoot : this;
            lay.SuspendLayout();
            try
            {
                if (cols != fbCols)
                {
                    fbCols = cols;
                    fbCardGrid.SuspendLayout();
                    fbCardGrid.Controls.Clear();
                    fbCardGrid.ColumnStyles.Clear();
                    fbCardGrid.RowStyles.Clear();
                    fbCardGrid.ColumnCount = cols;
                    fbCardGrid.RowCount = (fbCards.Count + cols - 1) / cols;
                    for (int i = 0; i < cols; i++) fbCardGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    for (int i = 0; i < fbCardGrid.RowCount; i++) fbCardGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    for (int i = 0; i < fbCards.Count; i++)
                    {
                        bool lastCol = i % cols == cols - 1;
                        fbCards[i].Margin = new Padding(0, 0, lastCol ? 0 : gap, gap);
                        fbCardGrid.Controls.Add(fbCards[i], i % cols, i / cols);
                    }
                    fbCardGrid.ResumeLayout(false);
                }
                else
                {
                    for (int i = 0; i < fbCards.Count; i++)
                        fbCards[i].Margin = new Padding(0, 0, i % cols == cols - 1 ? 0 : gap, gap);
                }

                int maxH = 0;
                foreach (FeedbackCard c in fbCards)
                {
                    int inner = Math.Max(S(80), cw - c.Padding.Horizontal);
                    c.Title.MaximumSize = new Size(Math.Max(S(60), inner - c.IconRoom), 0);
                    c.Text1.MaximumSize = new Size(inner, 0);
                    c.Text2.MaximumSize = new Size(inner, 0);
                    c.MinimumSize = new Size(cw, 0);
                    c.MaximumSize = new Size(cw, 0);
                    maxH = Math.Max(maxH, c.GetPreferredSize(new Size(cw, 0)).Height);
                }
                foreach (FeedbackCard c in fbCards) c.MinimumSize = new Size(cw, cols > 1 ? maxH : 0);
                int bmax = 0;
                int ch = ControlH(); // keep the one control height (FitControlHeights) while the widths are re-measured
                foreach (FeedbackCard c in fbCards)
                    foreach (Button b in c.Buttons) { b.MinimumSize = new Size(0, ch); bmax = Math.Max(bmax, b.GetPreferredSize(Size.Empty).Width); }
                foreach (FeedbackCard c in fbCards)
                    foreach (Button b in c.Buttons) b.MinimumSize = new Size(Math.Min(bmax, cw - c.Padding.Horizontal), ch);
                // the blocks below line up with the right edge of the card row
                if (cols == 3)
                {
                    int row = fbCardGrid.GetPreferredSize(Size.Empty).Width;
                    w = row > 0 ? row : 3 * cw + 2 * gap;
                }

                // text blocks share the cards' left edge and never reach past the card row's right edge
                // (the widest block sets the page column width, and some layouts stretch every block to it)
                fbHero.MaximumSize = new Size(Math.Max(S(120), w - fbHero.Margin.Right), 0);
                fbLead.MaximumSize = new Size(Math.Max(S(120), w - fbLead.Margin.Right), 0);
                FbContactMode(FbContactFit(w - fbContact.Padding.Horizontal - S(4)));
                FitBlockWidth(fbContact, w);
                Control btn = fbBanner.Controls.Count > 1 ? fbBanner.Controls[1] : null;
                int bw = btn == null ? 0 : btn.GetPreferredSize(Size.Empty).Width + btn.Margin.Horizontal;
                fbBannerText.MaximumSize = new Size(Math.Max(S(120), w - fbBanner.Padding.Horizontal - bw - fbBannerText.Margin.Horizontal), 0);
                FitBlockWidth(fbBanner, w);
            }
            finally { lay.ResumeLayout(true); }
            // second pass on the real bounds: the contact box and the update strip end where the card row ends
            int rowRight = cols == 3 ? fbCards[2].Right : 0; // the real right edge of the card row (inside the grid)
            if (rowRight > 0)
            {
                foreach (Control c in new Control[] { fbContact, fbBanner })
                {
                    int extra = c.Width - rowRight;
                    if (extra > 0 && extra < S(40) && c.MinimumSize.Width > 0)
                        c.MinimumSize = new Size(Math.Max(0, c.MinimumSize.Width - extra), 0);
                }
            }
            foreach (FeedbackCard c in fbCards) c.Invalidate();
            fbBanner.Invalidate();
        }

        /// <summary>Makes an auto-size block exactly w wide (never narrower than its content): some layout engines add
        /// the padding on top of MinimumSize, so the result is measured and corrected once.</summary>
        private static void FitBlockWidth(Control c, int w)
        {
            c.MinimumSize = Size.Empty;
            int natural = c.GetPreferredSize(Size.Empty).Width;
            if (natural >= w) return;
            c.MinimumSize = new Size(w, 0);
            int got = c.GetPreferredSize(Size.Empty).Width;
            if (got > w) c.MinimumSize = new Size(Math.Max(natural, w - (got - w)), 0);
        }

        private Label Value(string text)
        {
            Label l = Lbl(text);
            l.Font = monoFont;
            l.ForeColor = Theme.ColAccent2;
            l.Tag = "theme:keep-value";
            l.BackColor = Color.Transparent;
            l.Margin = P(3, 6, 10, 6);
            return l;
        }

        private Button CopyBtn(string value, string what)
        {
            Button b = null;
            Timer back = new Timer();
            back.Interval = 1500;
            back.Tick += delegate { back.Stop(); if (!b.IsDisposed) b.Text = "复制"; };
            b = Btn("复制", delegate
            {
                if (CopyText(value, "已复制" + what))
                {
                    b.Text = "已复制 ✓";
                    back.Stop();
                    back.Start();
                }
            });
            b.Disposed += delegate { back.Dispose(); };
            Tip(b, "复制" + what + "：" + value);
            FixUp(delegate { b.MinimumSize = new Size(TextW("已复制 ✓", Font) + S(20), 0); });
            return b;
        }

        private bool CopyText(string value, string okStatus)
        {
            try
            {
                Clipboard.SetText(value);
                SetStatus(okStatus, false);
                return true;
            }
            catch (Exception ex) { SetStatus("复制失败：" + ex.Message, true); return false; }
        }

        /// <summary>Opens a link (browser / mail program); if Windows cannot, copies the plain value instead and says so.</summary>
        private void OpenUrl(string url, string fallback, string what)
        {
            try { Process.Start(url); }
            catch (Exception ex)
            {
                try { Clipboard.SetText(fallback); } catch (Exception) { }
                Say("没有打开（" + ex.Message + "）。" + what + "已复制：" + fallback); // v5.0.2 (UI L7): the window's own dialog
            }
        }

        /// <summary>Opens the settings folder with crash.log selected (or just the folder when there is no log yet).</summary>
        private void OpenLogFolder()
        {
            string dir = null;
            try
            {
                dir = Program.BaseDir;
                string log = Path.Combine(dir, "crash.log");
                if (File.Exists(log))
                {
                    Process.Start("explorer.exe", "/select,\"" + log + "\"");
                    SetStatus("已打开设置文件夹（选中了 crash.log）", false);
                }
                else
                {
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                    SetStatus("还没有 crash.log，已打开设置文件夹", false);
                }
            }
            catch (Exception ex) { SetStatus("打不开文件夹：" + ex.Message + (dir != null ? "（" + dir + "）" : ""), true); }
        }

        /// <summary>One line for bug reports: version, Windows, primary screen in real pixels, Windows scale.</summary>
        private string VersionInfo()
        {
            string scr = "";
            try
            {
                int[] px = ScreenDetect.PrimaryPixels();
                if (px != null && px.Length >= 2 && px[0] > 0 && px[1] > 0) scr = px[0] + "x" + px[1];
            }
            catch (Exception) { }
            if (scr.Length == 0)
            {
                try { Rectangle b = Screen.PrimaryScreen.Bounds; scr = b.Width + "x" + b.Height; } catch (Exception) { scr = "?"; }
            }
            string os;
            try { os = Environment.OSVersion.Version.ToString(); } catch (Exception) { os = "?"; }
            return Program.ShortName + " " + Program.Version + " | Windows " + os + " | 屏幕 " + scr + " | 缩放 " + (int)Math.Round(k * 100) + "%";
        }

        private void CopyVersionInfo() { CopyText(VersionInfo(), "已复制版本信息，粘贴到反馈里即可"); }

        // ---------------- drawn containers ----------------

        /// <summary>A feedback card: raised surface, hairline frame, accent stripe on top and a small line icon top-right.</summary>
        private sealed class FeedbackCard : TableLayoutPanel
        {
            public readonly int Kind;
            public readonly Color Accent;
            public Label Title, Text1, Text2;
            public Button[] Buttons = new Button[0];
            private readonly float k;

            public FeedbackCard(int kind, Color accent, float scale)
            {
                Kind = kind;
                Accent = accent;
                k = scale;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                ColumnCount = 1;
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Padding = new Padding(Px(16), Px(16), Px(16), Px(14));
                BackColor = Theme.ColCard; // v5.1 flat (decision 14): the card colour token
                Tag = "theme:keep-fbcard";
            }

            /// <summary>Space kept free right of the title for the icon.</summary>
            public int IconRoom { get { return IconSize + Px(10); } }
            private int IconSize { get { return (int)Math.Round(24 * k); } }
            private int Px(int v) { return (int)Math.Round(v * k); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                try
                {
                    Graphics g = e.Graphics;
                    int w = ClientSize.Width, h = ClientSize.Height;
                    if (w < 8 || h < 8) return;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // v5.1 flat (decision 14): 1 px border + one accent bar, no corner ticks, no fade line
                    using (Pen p = new Pen(Theme.ColBorder, Theme.BorderPx)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);
                    using (SolidBrush b = new SolidBrush(Accent)) g.FillRectangle(b, 0, 0, w, Math.Max(2, Px(Theme.AccentBarPx)));
                    int s = IconSize;
                    Rectangle r = new Rectangle(w - Padding.Right - s, Padding.Top - Px(2), s, s);
                    using (Pen p = new Pen(Accent, Math.Max(1.5f, 2f * k)))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        p.LineJoin = LineJoin.Round;
                        DrawIcon(g, p, r);
                    }
                }
                catch { }
            }

            private void DrawIcon(Graphics g, Pen p, Rectangle r)
            {
                float x = r.X, y = r.Y, s = r.Width;
                if (Kind == 0)
                {
                    // light bulb: round glass, neck, two base lines
                    g.DrawArc(p, x + s * 0.18f, y + s * 0.02f, s * 0.64f, s * 0.64f, 140, 260);
                    g.DrawLine(p, x + s * 0.32f, y + s * 0.60f, x + s * 0.36f, y + s * 0.74f);
                    g.DrawLine(p, x + s * 0.68f, y + s * 0.60f, x + s * 0.64f, y + s * 0.74f);
                    g.DrawLine(p, x + s * 0.36f, y + s * 0.80f, x + s * 0.64f, y + s * 0.80f);
                    g.DrawLine(p, x + s * 0.40f, y + s * 0.94f, x + s * 0.60f, y + s * 0.94f);
                }
                else if (Kind == 1)
                {
                    // sparkle: a 4-point star + a small one
                    float cx = x + s * 0.42f, cy = y + s * 0.55f, R = s * 0.42f, q = s * 0.10f;
                    PointF[] star = {
                        new PointF(cx, cy - R), new PointF(cx + q, cy - q), new PointF(cx + R, cy), new PointF(cx + q, cy + q),
                        new PointF(cx, cy + R), new PointF(cx - q, cy + q), new PointF(cx - R, cy), new PointF(cx - q, cy - q) };
                    g.DrawPolygon(p, star);
                    float sx = x + s * 0.86f, sy = y + s * 0.14f, sr = s * 0.13f;
                    g.DrawLine(p, sx, sy - sr, sx, sy + sr);
                    g.DrawLine(p, sx - sr, sy, sx + sr, sy);
                }
                else
                {
                    // bug: body, head, middle line, three legs a side, antennae
                    g.DrawEllipse(p, x + s * 0.28f, y + s * 0.30f, s * 0.44f, s * 0.62f);
                    g.DrawArc(p, x + s * 0.36f, y + s * 0.14f, s * 0.28f, s * 0.26f, 180, 180);
                    g.DrawLine(p, x + s * 0.50f, y + s * 0.42f, x + s * 0.50f, y + s * 0.88f);
                    float[] ly = { 0.46f, 0.62f, 0.78f };
                    foreach (float f in ly)
                    {
                        g.DrawLine(p, x + s * 0.28f, y + s * f, x + s * 0.08f, y + s * (f - 0.06f));
                        g.DrawLine(p, x + s * 0.72f, y + s * f, x + s * 0.92f, y + s * (f - 0.06f));
                    }
                    g.DrawLine(p, x + s * 0.42f, y + s * 0.17f, x + s * 0.32f, y + s * 0.03f);
                    g.DrawLine(p, x + s * 0.58f, y + s * 0.17f, x + s * 0.68f, y + s * 0.03f);
                }
            }
        }

        /// <summary>The update line: a one-row strip with a neon bar on the left.</summary>
        private sealed class FeedbackBanner : TableLayoutPanel
        {
            private readonly float k;

            public FeedbackBanner(float scale)
            {
                k = scale;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                ColumnCount = 2;
                RowCount = 1;
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                RowStyles.Add(new RowStyle(SizeType.AutoSize));
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Padding = new Padding(Px(18), Px(10), Px(12), Px(10));
                BackColor = Theme.ColCard; // v5.1 flat
                Tag = "theme:keep-fbbanner";
            }

            private int Px(int v) { return (int)Math.Round(v * k); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                try
                {
                    Graphics g = e.Graphics;
                    int w = ClientSize.Width, h = ClientSize.Height;
                    if (w < 4 || h < 4) return;
                    using (Pen p = new Pen(Theme.ColBorder, Theme.BorderPx)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);
                    using (SolidBrush b = new SolidBrush(Theme.ColAccent)) g.FillRectangle(b, 0, 0, Math.Max(2, Px(Theme.AccentBarPx)), h);
                }
                catch { }
            }
        }
    }
}
#endif
