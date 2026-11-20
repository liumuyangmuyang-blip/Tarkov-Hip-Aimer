using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 分辨率 · 常规 page (PAGE-A). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>v5.1.1 (X5): the FOV line of 常规 in plain words, e.g. 「FOV 换算：枪那一侧 ×1.00，另一侧 ×0.80，上下不变」.</summary>
        internal static string FovLineText(GlobalSettings g)
        {
            return FovText.Line(g);
        }

        /// <summary>常规: 分辨率换算 first (SPEC v4 page 2), then display, game window, window / start-up.</summary>
        private IEnumerable<Control> BuildGeneralSteps()
        {
            // 分辨率换算
            TableLayoutPanel gr = HeldGrid(2);
            // v4.1 画面比例 + FOV: every offset is converted through the focal length (ViewScale); v4.2: a stretched 画面比例 adds an X factor.
            Label factor = Lbl(""); // v5.0.2 (UI-C L5): Chinese text in the UI font (mono has no CJK glyphs)
            factor.ForeColor = Theme.ColAccent;
            Label stretch = Note("", 560);
            Label fovLine = Lbl(""); // v5.1.1 (X5): 「FOV 换算：枪那一侧 ×1.00，另一侧 ×0.80，上下不变」
            fovLine.ForeColor = Theme.ColAccent;
            ComboBox aspectBox = null;
            Control stretchRow = null;
            TextBox customBox = null;
            // v5.1.1 (X1, Leo): 「自定义」 chosen -- the W:H box stays open even while the typed ratio is also a list ratio
            bool customMode = AspectChoiceOf(G.ResAspect) == "custom";
            Action showFactor = delegate
            {
                System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
                double kf = G.ResolutionScale, kx = G.ResolutionScaleX;
                string ks = kf.ToString("0.000", inv);
                // v4.1 百分比偏移: what 1 % is on this screen (half the picture height / 100)
                string onePct = (OffsetUnits.HalfHeight(G) / 100.0).ToString("0.00", inv);
                // v4.2: a stretched 画面比例 gives X its own factor
                // v4.3 (W6 asks): stretched = the X / Y factor ratio (same as DashStretch.Ratio); both axes then get their own 1 %
                double ratio = kf > 0 ? kx / kf : 1.0;
                bool stretched = Math.Abs(ratio - 1.0) > 1e-9;
                string onePctX = (OffsetUnits.HalfHeight(G) / 100.0 * ratio).ToString("0.00", inv);
                factor.Text = (kx == kf ? "换算系数 ×" + ks : "换算系数 左右 ×" + kx.ToString("0.000", inv) + "  上下 ×" + ks)
                    + (stretched ? "    上下 1% = " + onePct + " 像素 · 左右 1% = " + onePctX + " 像素" : "    1% = " + onePct + " 像素");
                // v5.1 (decision 11): the horizontal factor is always shown in words: 「横向 ×1.20（拉伸）」 / 「横向 ×1.00（没拉伸）」
                bool nonNative = Math.Abs(ViewScale.StretchX(G.ResGameW, G.ResGameH, G.ResAspect) - 1.0) > 1e-9;
                string bars = G.ResBars == GlobalSettings.BarsTopBottom ? "（没拉伸，上下有黑边）" : G.ResBars == GlobalSettings.BarsLeftRight ? "（没拉伸，左右有黑边）" : "（没拉伸，有黑边）";
                stretch.Text = "横向 ×" + ratio.ToString("0.00", inv) + (stretched ? "（拉伸）" : nonNative ? bars : "（没拉伸）")
                    + (stretched ? "：" + ViewScale.DisplayAspect(G.ResAspect, G.ResGameW, G.ResGameH) + " 拉宽到 "
                    + ViewScale.DisplayAspect("auto", G.ResGameW, G.ResGameH) + " 屏，左右偏移自动 ×" + ratio.ToString("0.00", inv) + "（设置里的 % 不用改）" : "");
                stretch.ForeColor = stretched ? Theme.ColWarn : Theme.ColMuted;
                fovLine.Text = FovLineText(G);
                // v5.1.1 (F1, Leo): 拉宽了 is the default; the 有黑边 choice is only shown in 高级设置
                if (stretchRow != null) stretchRow.Visible = nonNative && Advanced;
                if (customBox != null) customBox.Visible = customMode || AspectChoiceOf(G.ResAspect) == "custom";
                // "auto" (not chosen yet) shows the resolution's ratio: follow a typed resolution without storing a choice
                if (aspectBox != null && G.ResAspect == "auto")
                {
                    int i = Array.IndexOf(AspectChoiceIds, ViewScale.DisplayAspect(G.ResAspect, G.ResGameW, G.ResGameH));
                    if (i >= 0 && aspectBox.SelectedIndex != i) { bool was = loading; loading = true; aspectBox.SelectedIndex = i; loading = was; }
                }
            };
            refreshers.Add(showFactor);
            // v4.2 (Leo 17 Nov 2026): exactly the game's 3 view settings, with the game's names
            Row(gr, "游戏分辨率", Line(
                Num(640, 7680, 1, 0, delegate { return G.ResGameW; }, delegate(double v) { G.ResGameW = v; showFactor(); }), Unit("×"),
                Num(480, 4320, 1, 0, delegate { return G.ResGameH; }, delegate(double v) { G.ResGameH = v; showFactor(); }),
                AutoDetectBtn(), ResPresetBtn(), HelpMark.For("resolution", Font, delegate { return G; })));
            // v5.1 (decision 12): the game's ratios + 1:1 + 自定义 (any W:H, e.g. 123:111)
            customBox = new TextBox();
            customBox.Width = Math.Max(S(90), TextW("12345:12345", monoFont) + S(12));
            customBox.Font = monoFont;
            customBox.Anchor = AnchorStyles.Left;
            customBox.Margin = P(3, 3, 3, 3);
            Tip(customBox, "任意宽:高，例如 123:111");
            // v5.1.1 (X1): typed W:H applied as soon as it reads (red while it does not), checked again on leave / Enter
            Action<bool> takeCustom = delegate(bool final)
            {
                int cw, ch;
                string t = (customBox.Text ?? "").Trim().Replace('：', ':');
                bool okRatio = ViewScale.TryParseRatio(t, out cw, out ch) && ViewScale.NormalizeAspect(cw + ":" + ch) != "auto";
                customBox.ForeColor = okRatio ? Theme.ColText : Theme.ColWarn;
                if (!okRatio)
                {
                    if (final) { SetStatus("画面比例要写成 宽:高，例如 123:111", true); customBox.Text = G.ResAspectCustom; customBox.ForeColor = Theme.ColText; }
                    return;
                }
                string n = cw + ":" + ch;
                if (G.ResAspectCustom == n && G.ResAspect == n) { if (final) customBox.Text = n; return; }
                G.ResAspectCustom = n; G.ResAspect = n;
                if (final) customBox.Text = n;
                showFactor(); app.Changed();
            };
            customBox.TextChanged += delegate { if (!loading && customBox.Focused) takeCustom(false); };
            customBox.Leave += delegate { takeCustom(true); };
            customBox.KeyDown += delegate(object s2, KeyEventArgs e2) { if (e2.KeyCode == Keys.Enter) { e2.SuppressKeyPress = true; takeCustom(true); } };
            refreshers.Add(delegate
            {
                customMode = AspectChoiceOf(G.ResAspect) == "custom" || (customMode && G.ResAspect == G.ResAspectCustom);
                if (!customBox.Focused) customBox.Text = G.ResAspectCustom ?? "";
            });
            aspectBox = Choice(AspectChoiceLabels, AspectChoiceIds,
                delegate { return customMode ? "custom" : AspectChoiceOf(ViewScale.DisplayAspect(G.ResAspect, G.ResGameW, G.ResGameH)); },
                delegate(string v)
                {
                    customMode = v == "custom";
                    if (customMode)
                    {
                        int cw, ch;
                        if (!ViewScale.TryParseRatio(G.ResAspectCustom, out cw, out ch)) { cw = 16; ch = 9; }
                        G.ResAspectCustom = cw + ":" + ch; G.ResAspect = G.ResAspectCustom;
                        customBox.Text = G.ResAspectCustom;
                    }
                    else G.ResAspect = v;
                    showFactor();
                    if (customMode) { PostRefit(); customBox.Focus(); customBox.SelectAll(); }
                });
            Row(gr, "画面比例", Line(aspectBox, customBox, HelpMark.For("aspect", Font, delegate { return G; })));
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            // v5.1 (decision 11): 拉宽了 (stretched, default) / 有黑边 -- only when the ratio is not the screen's own; v5.1.1: 高级设置 only
            // v5.1.1 (X1, Leo): 3 choices, stored in ResBars (none / topbottom / leftright; ResStretched kept in step)
            ComboBox stretchBox = Choice(new string[] { "拉宽了，铺满屏幕", "没拉宽，上下有黑边", "没拉宽，左右有黑边" }, GlobalSettings.BarsIds,
                delegate { return G.ResBars ?? GlobalSettings.BarsNone; },
                delegate(string v) { G.SetBars(v); showFactor(); });
            stretchRow = Line(stretchBox, HelpMark.For("stretch", Font, delegate { return G; }));
            Label stretchL = Lbl("画面有没有被拉宽");
            Row(gr, stretchL, stretchRow);
            refreshers.Add(delegate { stretchL.Visible = stretchRow.Visible; });
            Row(gr, "FOV", Line(Num(ViewScale.MinFov, ViewScale.MaxFov, 1, 0, delegate { return G.ResFov; }, delegate(double v) { G.ResFov = v; showFactor(); }),
                Hint("°（游戏里能设 50–75）", 200), HelpMark.For("fov", Font, delegate { return G; })));
            TableLayoutPanel resBox = HeldGrid(1);
            resBox.Controls.Add(gr);
            resBox.Controls.Add(factor);
            resBox.Controls.Add(fovLine);
            resBox.Controls.Add(stretch);
            resBox.Controls.Add(Note("照游戏设置里填这 3 项。偏移量会自动换算到你的画面；第一次打开时已经自动填好主显示器的分辨率。画面比例和屏幕不一样时，按游戏把画面拉宽铺满来换算；有黑条的话，打开高级设置选「没拉宽，上下有黑边」或「没拉宽，左右有黑边」。", 560));

            // v5.1.1 (B5, Leo): the 调参基准 rows (调参分辨率 / 调参比例 / 调参 FOV) are gone from the UI. The stored fields
            // ResBaseW / ResBaseH / ResBaseAspect / ResBaseFov stay (defaults 2160×1440 / 跟随 / 75), so files and imports still convert.
            Section gRes = Group("分辨率 · 画面", resBox);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            foreach (Control c in BuildGeneralRestSteps(gRes)) yield return c;
        }

        /// <summary>v5.1 (decision 12): 画面比例 choices: the game's list (incl. 1:1) + 自定义.</summary>
        private static readonly string[] AspectChoiceIds = AspectIdsWithCustom();
        private static readonly string[] AspectChoiceLabels = AspectLabelsWithCustom();

        private static string[] AspectIdsWithCustom()
        {
            List<string> l = new List<string>(ViewScale.GameAspectIds);
            l.Add("custom");
            return l.ToArray();
        }

        private static string[] AspectLabelsWithCustom()
        {
            List<string> l = new List<string>(ViewScale.GameAspectIds);
            l.Add("自定义");
            return l.ToArray();
        }

        /// <summary>The choice that shows a stored ratio: a listed id, else 自定义.</summary>
        private static string AspectChoiceOf(string id)
        {
            return Array.IndexOf(ViewScale.GameAspectIds, id) >= 0 ? id : "custom";
        }

        /// <summary>v5.1 (decision 12): common resolutions.</summary>
        private static readonly int[,] ResPresets = new int[,]
        {
            { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 }, { 1920, 1200 }, { 2560, 1600 }, { 2160, 1440 }, { 1680, 1050 },
            { 1600, 900 }, { 1366, 768 }, { 1440, 1080 }, { 1280, 1024 }, { 2560, 1080 }, { 3440, 1440 }, { 5120, 1440 }
        };
        private ContextMenuStrip resMenu;

        private Button ResPresetBtn()
        {
            Button b = null;
            b = Btn("常用 ▾", delegate
            {
                if (resMenu == null)
                {
                    resMenu = new ContextMenuStrip();
                    StyleMenu(resMenu);
                    for (int i = 0; i < ResPresets.GetLength(0); i++)
                    {
                        int w = ResPresets[i, 0], h = ResPresets[i, 1];
                        ToolStripMenuItem it = new ToolStripMenuItem(w + " × " + h);
                        it.Click += delegate
                        {
                            G.ResGameW = w; G.ResGameH = h;
                            RefreshValues();
                            app.Changed();
                            SetStatus("游戏分辨率：" + w + " × " + h, false);
                        };
                        resMenu.Items.Add(it);
                    }
                }
                resMenu.Show(b, new Point(0, b.Height));
            });
            Tip(b, "常见分辨率；别的分辨率直接在左边填数字");
            return b;
        }

        private Button AutoDetectBtn()
        {
            Button b = Btn("自动检测", delegate
                {
                    Screen[] all = Screen.AllScreens;
                    Screen sc = (G.MonitorIndex >= 0 && G.MonitorIndex < all.Length) ? all[G.MonitorIndex] : Screen.PrimaryScreen;
                    // primary screen: its real pixels (not scaled by Windows display scaling); another screen: its bounds
                    if (!sc.Primary || !G.DetectGameResolution(ScreenDetect.PrimaryPixels))
                    {
                        G.ResGameW = Clamp.D(sc.Bounds.Width, 640, 7680, 2160);
                        G.ResGameH = Clamp.D(sc.Bounds.Height, 480, 4320, 1440);
                    }
                    RefreshValues();
                    app.Changed();
                    SetStatus("游戏分辨率：" + Math.Round(G.ResGameW) + " × " + Math.Round(G.ResGameH), false);
                });
            Tip(b, "填成显示器的真实分辨率（不受 Windows 缩放影响）");
            return b;
        }

        /// <summary>常规 after 分辨率: display (monitor shown when there is more than one), game window, window / start-up.</summary>
        private IEnumerable<Control> BuildGeneralRestSteps(Section gRes)
        {
            // 显示器 / 渐变
            TableLayoutPanel o = HeldGrid(2);
            cbMonitor = new ComboBox();
            cbMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMonitor.Width = S(250);
            cbMonitor.Anchor = AnchorStyles.Left;
            cbMonitor.Margin = P(3, 3, 3, 3);
            cbMonitor.SelectedIndexChanged += delegate
            {
                if (loading || cbMonitor.SelectedIndex < 0) return;
                G.MonitorIndex = cbMonitor.SelectedIndex - 1; // item 0 = primary (-1)
                app.Changed();
            };
            WheelScrollsPage(cbMonitor);
            refreshers.Add(RefreshMonitors);
            Row(o, "显示器", cbMonitor);
            // v4.0: no frame-rate option -- the crosshair always runs at the monitor's refresh rate
            AdvRow(o, "出现渐变", Line(Num(0, 1000, 10, 0, delegate { return G.ShowFadeMs; }, delegate(double v) { G.ShowFadeMs = v; }), Unit("ms")));
            AdvRow(o, "消失渐变", Line(Num(0, 1000, 10, 0, delegate { return G.HideFadeMs; }, delegate(double v) { G.HideFadeMs = v; }), Unit("ms")));
            Section gDisp = Group("显示", o);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            // one screen: nothing to choose, so the group is 高级设置 only; several screens: the 显示器 choice is always shown
            refreshers.Add(delegate { gDisp.Visible = Advanced || ScreenCount() > 1; });

            // 只在塔科夫前台时显示
            TableLayoutPanel f = HeldGrid(2);
            TextBox title = new TextBox();
            title.Width = S(230);
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
            Wide(f, only);
            Label onlyNote = Note("切出游戏（例如 Alt+Tab）时准星自动隐藏；这个设置窗口在前台时照常显示。", 560);
            onlyNote.Margin = P(22, 0, 6, 6);
            Wide(f, onlyNote);
            AdvRow(f, "窗口标题", Line(title, pick));
            Section gGame = Group("游戏窗口", f);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            // 窗口 / 开机
            TableLayoutPanel w = HeldGrid(1);
            w.Controls.Add(AdvOnly(Line(
                Check("设置窗口置顶", delegate { return G.SettingsTopMost; }, delegate(bool v) { G.SettingsTopMost = v; TopMost = v; if (tutorial != null && !tutorial.IsDisposed) tutorial.TopMost = v; }),
                Check("启动时打开设置", delegate { return G.ShowSettingsOnStart; }, delegate(bool v) { G.ShowSettingsOnStart = v; }),
                Check("启动动画", delegate { return G.SplashOnStart; }, delegate(bool v) { G.SplashOnStart = v; }))));
            chkAutostart = new CheckBox();
            chkAutostart.Text = "开机自动启动";
            chkAutostart.AutoSize = true;
            chkAutostart.Anchor = AnchorStyles.Left;
            chkAutostart.Margin = P(3, 3, 8, 3);
            Tip(chkAutostart, "登录 Windows 后自动在托盘里启动");
            chkAutostart.CheckedChanged += delegate { if (!loading) SetAutostart(chkAutostart.Checked); };
            refreshers.Add(delegate { chkAutostart.Checked = G.Autostart; });
            w.Controls.Add(Line(chkAutostart, AdvOnly(Btn("打开设置文件夹", delegate
            {
                try { Process.Start("explorer.exe", "\"" + Program.BaseDir.TrimEnd('\\') + "\""); }
                catch (Exception ex) { ThemedDialog.Show(this, "没能打开设置文件夹：" + ex.Message, false); } // v5.0.2 (UI-C L7)
            }))));
            // v5.1 (decision 8, W25): 性能模式
            CheckBox perf = Check("性能模式", delegate { return G.PerfMode; }, delegate(bool v) { G.PerfMode = v; });
            Tip(perf, "关掉设置窗口里的动画和发光，界面每秒最多 30 帧；准星不受影响");
            w.Controls.Add(Line(perf, HelpMark.For("perf", Font, delegate { return G; })));
            Section gWin = Group("窗口和开机", w);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            // v5.1 (decision 16, W28): 联网
            TableLayoutPanel n = HeldGrid(1);
            CheckBox upd = Check("自动检查更新", delegate { return G.NetUpdateCheck; }, delegate(bool v) { G.NetUpdateCheck = v; NetChanged(); });
            n.Controls.Add(Line(upd, HelpMark.For("update", Font, delegate { return G; })));
            if (UsageConfig.Enabled)
            {
                // v5.1 (Leo 21 Nov 2026): on by default (null = on); unticking stores false and stops the heartbeat at once.
                // Leo follow-up 21 Nov: the checkbox + notice live in 高级设置 only (hidden on the normal page)
                CheckBox stats = Check(UsageConfig.SettingLabel, delegate { return G.NetStatsConsent != false; }, delegate(bool v) { G.NetStatsConsent = v; NetChanged(); });
                n.Controls.Add(AdvOnly(Line(stats, HelpMark.For("stats", Font, delegate { return G; }))));
                n.Controls.Add(AdvOnly(Note(UsageConfig.Notice, 560)));
            }
            CheckBox crash = Check(UsageConfig.CrashSettingLabel, delegate { return G.NetCrashAsk; }, delegate(bool v) { G.NetCrashAsk = v; NetChanged(); });
            n.Controls.Add(Line(crash, HelpMark.For("crash", Font, delegate { return G; })));
            Section gNet = Group("联网", n);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            // v5.1 (W33): 排查贴图变糊, all off by default
            TableLayoutPanel d = HeldGrid(1);
            CheckBox realHide = Check("开镜/丢雷时真正隐藏准星窗口", delegate { return G.AdsRealHide; }, delegate(bool v) { G.AdsRealHide = v; });
            Tip(realHide, "开镜或丢雷时把准星窗口整个关掉，而不是只变透明。用来排查“开着准星游戏贴图变糊”。关掉再打开窗口会多花一点点时间。");
            CheckBox dcomp = Check("新的显示方式（DirectComposition）", delegate { return G.OverlayDCompMode; }, delegate(bool v) { G.OverlayDCompMode = v; });
            Tip(dcomp, "用另一种方式把准星交给 Windows 显示，可能不会让游戏贴图变糊。如果你的电脑不支持，会自动换回原来的方式。");
            CheckBox gpuLog = Check("显存诊断日志", delegate { return G.GpuDiagLog; }, delegate(bool v) { G.GpuDiagLog = v; });
            Tip(gpuLog, "每秒把游戏用的显存写进 Data/crash.log，方便对比开 / 关准星时贴图是否掉档。不用时请关掉。");
            d.Controls.Add(realHide);
            d.Controls.Add(Note("开镜或丢雷时把准星窗口整个关掉，而不是只变透明。用来排查“开着准星游戏贴图变糊”。关掉再打开窗口会多花一点点时间。", 560));
            d.Controls.Add(dcomp);
            d.Controls.Add(Note("用另一种方式把准星交给 Windows 显示，可能不会让游戏贴图变糊。如果你的电脑不支持，会自动换回原来的方式。", 560));
            d.Controls.Add(gpuLog);
            d.Controls.Add(Note("每秒把游戏用的显存写进 Data/crash.log，方便对比开 / 关准星时贴图是否掉档。不用时请关掉。", 560));
            Control gDiag = AdvOnly(Fold("排查贴图变糊（高级）", d));
            TableLayoutPanel root = HeldStack(gRes, gDisp, gGame, gWin, gNet, gDiag);
            ReleaseHeldOnShow(root); // v5.1.1 (P3): held through the first fill, one layout pass on the first show
            yield return root;
        }

        /// <summary>v5.1 (W28): a 联网 setting changed: save and tell the network part (it may not exist when its start failed).</summary>
        private void NetChanged()
        {
            try { if (app.net != null) app.net.SettingsChanged(); }
            catch (Exception ex) { Program.Log("net: settings changed: " + ex.Message); }
        }

        private static int ScreenCount()
        {
            try { return Screen.AllScreens.Length; }
            catch (Exception) { return 1; }
        }

        private void ShowWindowMenu(Button anchor, TextBox target)
        {
            if (windowMenu == null) { windowMenu = new ContextMenuStrip(); StyleMenu(windowMenu); }
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

        /// <summary>True while the logon task is being created / deleted on the background thread.</summary>
        private bool autostartBusy;

        /// <summary>v5.0.2 (Leo, decision 5: no scheduled-task work on the UI thread): ☑ 开机自动启动 creates / deletes the
        /// logon task on a background thread (Autostart.BeginSet); the box is greyed until the answer is back.</summary>
        private void SetAutostart(bool on)
        {
            if (autostartBusy) return;
            autostartBusy = true;
            chkAutostart.Enabled = false;
            Autostart.BeginSet(on, Application.ExecutablePath, delegate(bool ok, string err, bool now)
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)delegate { AutostartDone(on, ok, err, now); });
                }
                catch (Exception) { }
            });
        }

        private void AutostartDone(bool on, bool ok, string err, bool now)
        {
            autostartBusy = false;
            G.Autostart = ok ? on : now; // a failure shows the real task state
            if (chkAutostart == null || chkAutostart.IsDisposed) { app.Changed(); return; }
            chkAutostart.Enabled = true;
            bool was = loading;
            loading = true;
            chkAutostart.Checked = G.Autostart;
            loading = was;
            app.Changed();
            if (!ok) ThemedDialog.Show(this, (on ? "设置开机自动启动失败：" : "取消开机自动启动失败：") + (err ?? "未知错误"), false); // v5.0.2 (UI-C L7)
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
    }
}
#endif
