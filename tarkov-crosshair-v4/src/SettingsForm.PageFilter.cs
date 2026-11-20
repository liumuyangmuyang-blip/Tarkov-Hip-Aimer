using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using TarkovShade;

// 滤镜 page (v4.2, W2). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch:
// SettingsForm.cs). Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit /
// Choice / Check / Btn / Group / Fold / FixUp / F / S / P / Tip) and read `Advanced`. Content per
// /home/dev/tarkov-shade-module/LEO-DECISIONS.md; tooltips from PilotHelpText; the filter itself is app.Filter (FilterHost).

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>KeyButton.Action values from here up are 滤镜 hotkeys (FilterKeys action = Action - FilterKeyBase).</summary>
        internal const int FilterKeyBase = 100;

        /// <summary>Refreshers of the 滤镜 page only (run on every filter change; the rest of the window is not touched).</summary>
        private readonly List<Action> filterRefreshers = new List<Action>();
        private ToneCurveGraph filterGraph;
        private PilotStyle slotA, slotB;
        private char liveSlot = ' ';
        private bool filterSelfChange;

        private FilterHost idleFilter;
        /// <summary>The app's filter host; a not-started stand-in when there is none (render harness builds App without its
        /// constructor), so the page always draws and every action is a safe no-op.</summary>
        private FilterHost Fh
        {
            get
            {
                if (app.Filter != null) return app.Filter;
                if (idleFilter == null) idleFilter = new FilterHost(null, Program.BaseDir);
                return idleFilter;
            }
        }

        /// <summary>滤镜: on/off + preset, tone curve, 色调 / 色彩 / 范围 sliders (advanced rows via 高级设置), A/B, hotkeys.</summary>
        private Control BuildFilter()
        {
            List<Control> blocks = new List<Control>();

            // ---------------------------------------------------------------- top: on/off, preset, buttons
            CheckBox on = Check("开启滤镜", delegate { return Fh.IsOn; }, delegate(bool v) { Fh.SetOn(v); });
            Tip(on, "打开后整块屏幕的颜色曲线会按下面的设置改变；关掉马上恢复原画面。快捷键见下面");
            Label onState = StatusLbl();
            FilterRefresh(delegate
            {
                on.Checked = Fh.IsOn;
                onState.Text = !Fh.Started ? "滤镜没有启动" : (Fh.Holding ? "正在看原画面" : (Fh.IsOn ? "已开启" : "已关闭"));
                onState.ForeColor = Fh.IsOn ? Theme.ColAccent : Theme.ColMuted;
            });

            ComboBox preset = PresetCombo();
            Tip(preset, PresetTip);
            Button saveAs = Btn("另存为…", delegate
            {
                string n = NameDialog.Ask(this, "另存为新预设", Fh.PresetName, "给这套滤镜设置起个名字，例如 夜图 / 雪地");
                if (n == null) return;
                Fh.SaveAs(n, delegate(string saved)
                {
                    if (IsDisposed) return;
                    if (saved == null) SetStatus("没存上，详细信息在 crash.log", true); else SetStatus("已存为预设「" + saved + "」", false);
                });
            });
            Tip(saveAs, "把现在的滑块数值存成一个新预设");
            Button saveCur = Btn("保存", delegate
            {
                string n = Fh.PresetName;
                if (Fh.PresetBuiltIn(n) && !Ask("把现在的数值存进自带预设「" + n + "」？以后可以点「恢复出厂」改回去。")) return;
                Fh.SaveToCurrent(delegate(bool ok) { if (!IsDisposed) SetStatus(ok ? "已保存到「" + n + "」" : "没存上，详细信息在 crash.log", !ok); });
            });
            Tip(saveCur, "把现在的滑块数值存进当前预设");
            Button rename = Btn("重命名…", delegate
            {
                string old = Fh.PresetName;
                string n = NameDialog.Ask(this, "重命名预设", old, "给这套滤镜设置起个新名字");
                if (n == null || n == old) return;
                Fh.Rename(old, n, delegate(bool ok) { if (!IsDisposed) SetStatus(ok ? "已改名为「" + n + "」" : "改不了：名字已被占用", !ok); });
            });
            Button delete = Btn("删除", delegate
            {
                string n = Fh.PresetName;
                if (!Ask("删除预设「" + n + "」？删除后不能恢复。")) return;
                Fh.Delete(n, delegate(bool ok) { if (!IsDisposed) SetStatus(ok ? "已删除「" + n + "」" : "删不了，详细信息在 crash.log", !ok); });
            });
            Button factory = Btn("恢复出厂", delegate
            {
                string n = Fh.PresetName;
                if (!Ask("把自带预设「" + n + "」恢复成出厂数值？")) return;
                Fh.ResetBuiltIn(n, delegate(bool ok) { if (!IsDisposed) SetStatus(ok ? "「" + n + "」已恢复出厂数值" : "没恢复成功", !ok); });
            });
            Tip(rename, "只能改你自己存的预设；自带的「通用」「黑夜模式」不能改名");
            Tip(delete, "只能删你自己存的预设；自带的不能删");
            FilterRefresh(delegate
            {
                bool builtIn = Fh.PresetBuiltIn(Fh.PresetName);
                rename.Visible = !builtIn; delete.Visible = !builtIn; factory.Visible = builtIn;
                foreach (Button b in new Button[] { saveAs, saveCur, rename, delete, factory }) b.Enabled = Fh.Started;
            });

            Button tut = AccentBtn("滤镜教程", delegate { OpenFilterTutorial(); });
            Tip(tut, "一步一步看滤镜每个设置的作用（不会改你的屏幕）");
            Button hold = Btn("按住看原画面", null);
            Tip(hold, PilotHelpText.Tooltip(PilotHelpText.HotkeyHoldOriginal));
            hold.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Fh.Hold(true); };
            hold.MouseUp += delegate { Fh.Hold(false); };
            hold.MouseCaptureChanged += delegate { if (Fh.Holding && !hold.Capture) Fh.Hold(false); };
            Button restore = Btn("全部恢复原样", delegate
            {
                Fh.RestoreAll();
                SetStatus("已恢复原画面（滤镜已关）", false);
            });
            Tip(restore, RestoreTip); // v5.0.2 (UI-C H3): no hotkey any more (the module text promised Ctrl+F12)
            Label problem = Hint("", 560);
            problem.ForeColor = Theme.ColWarn;
            FilterRefresh(delegate { problem.Text = Fh.Problem; problem.Visible = Fh.Problem.Length > 0; });

            TableLayoutPanel top = Grid(1);
            top.Controls.Add(Line(on, onState));
            top.Controls.Add(Line(Lbl("预设"), preset));
            top.Controls.Add(Line(saveAs, saveCur, rename, delete, factory));
            top.Controls.Add(Line(tut, hold, restore));
            top.Controls.Add(problem);
            top.Controls.Add(Note("滤镜只改显示器的颜色曲线（和显卡控制面板的伽马一样），不截图，也不读游戏。退出程序时自动恢复原画面。", 560));
            blocks.Add(Group("滤镜", top));

            // ---------------------------------------------------------------- tone curve + A/B
            filterGraph = new ToneCurveGraph();
            filterGraph.Style = delegate { return Fh.Style; };
            filterGraph.Font = Font;
            int side = S(280);
            filterGraph.Size = new Size(side + Font.Height + S(14), side + Font.Height + S(14));
            fitWidth.Add(new KeyValuePair<Control, int>(filterGraph, 310));
            filterGraph.Margin = P(3, 6, 3, 3);
            FilterRefresh(filterGraph.Rebuild);
            // v5.1 (W24, decision 10): drag the 5 points; readout + the reason a point stopped
            Label curveRead = Hint("", 560);
            curveRead.ForeColor = Theme.ColAccent2;
            Label curveWhy = Hint("", 560);
            curveWhy.ForeColor = Theme.ColWarn;
            curveWhy.Visible = false;
            PilotStyle dragBase = null;
            string whyPreset = null; // the reason stays until another preset is picked
            filterGraph.PointDragged += delegate(int i, double target)
            {
                if (!Fh.Started) return;
                if (dragBase == null) dragBase = Fh.Style;
                string why;
                PilotStyle s = FilterCurvePoints.Drag(dragBase, i, target, out why);
                curveWhy.Text = why;
                curveWhy.Visible = why.Length > 0;
                whyPreset = Fh.PresetName;
                SetFilterStyle(s);
                RunFilterRefreshers(); // sliders + readout follow at once (the graph is not yanked: it reads the same style)
            };
            filterGraph.PointReleased += delegate { dragBase = null; };
            FilterRefresh(delegate
            {
                curveRead.Text = FilterCurvePoints.Readout(Fh.Style);
                if (curveWhy.Visible && whyPreset != Fh.PresetName) curveWhy.Visible = false;
            });

            Button saveA = Btn("存为 A", delegate { slotA = Fh.Style; liveSlot = 'A'; RefreshFilterPage(); SetStatus("当前设置已存为 A", false); });
            Button saveB = Btn("存为 B", delegate { slotB = Fh.Style; liveSlot = 'B'; RefreshFilterPage(); SetStatus("当前设置已存为 B", false); });
            Button swap = Btn("A / B 切换", delegate { SwapAb(); });
            Label abState = StatusLbl();
            Tip(swap, "先把两套设置分别存为 A 和 B，再点这里来回切换，对比哪个更清楚");
            FilterRefresh(delegate
            {
                swap.Enabled = slotA != null && slotB != null;
                abState.Text = slotA == null && slotB == null ? "" : "现在是 " + (liveSlot == ' ' ? "改动后的设置" : liveSlot.ToString());
            });
            TableLayoutPanel curve = Grid(1);
            curve.Controls.Add(filterGraph);
            curve.Controls.Add(Hint(CurveHint, 380));
            curve.Controls.Add(curveRead);
            curve.Controls.Add(curveWhy);
            curve.Controls.Add(Line(saveA, saveB, swap, abState));
            blocks.Add(Group("色调曲线", curve));

            // ---------------------------------------------------------------- 调试滤镜 (one bar moves the curve values together)
            blocks.Add(Group("调试滤镜", BuildTune()));

            // ---------------------------------------------------------------- sliders
            TableLayoutPanel tone = Grid(3);
            StyleRow(tone, PilotHelpText.Shadows, false);
            StyleRow(tone, PilotHelpText.Highlights, false);
            StyleRow(tone, PilotHelpText.Midtones, false);
            StyleRow(tone, PilotHelpText.BlackPoint, false);
            StyleRow(tone, PilotHelpText.WhitePoint, true);
            StyleRow(tone, PilotHelpText.ContrastPivot, true);
            blocks.Add(Group("色调", tone));

            TableLayoutPanel colour = Grid(3);
            CheckBox satOn = Check("改饱和度", delegate { return Fh.Style.ApplySaturation; },
                delegate(bool v) { PilotStyle s = Fh.Style; s.ApplySaturation = v; SetFilterStyle(s); });
            Tip(satOn, SatTip); // v5.0.2 (UI-C M1): the app never uses DDC/CI saturation
            StyleRow(colour, PilotHelpText.Saturation, false);
            Row(colour, Blank(), satOn, Blank());
            ComboBox backend = Choice(new string[] { "显卡驱动（推荐）", "Windows 放大镜（会降低帧数）" },
                new string[] { PilotStyle.SaturationBackendDriver.ToString(), PilotStyle.SaturationBackendSoftware.ToString() },
                delegate { return Fh.Style.SaturationBackend.ToString(); },
                delegate(string v) { PilotStyle s = Fh.Style; s.SaturationBackend = int.Parse(v); SetFilterStyle(s); });
            Label backendL = Lbl(PilotHelpText.Label(PilotHelpText.SaturationBackend));
            Tip(backendL, PilotHelpText.Tooltip(PilotHelpText.SaturationBackend));
            Tip(backend, PilotHelpText.Tooltip(PilotHelpText.SaturationBackend));
            Row(colour, backendL, backend, Blank());
            // v5.0.2 (AUDIT F5, Leo: performance): the Magnification way costs every frame of the whole screen; say so where it is chosen
            Label magWarn = Hint(MagWarnText, 560);
            magWarn.ForeColor = Theme.ColWarn;
            FilterRefresh(delegate { PilotStyle st = Fh.Style; magWarn.Visible = st.ApplySaturation && st.SaturationBackend == PilotStyle.SaturationBackendSoftware; });
            Wide(colour, magWarn);
            StyleRow(colour, PilotHelpText.Temperature, true);
            StyleRow(colour, PilotHelpText.Vibrance, true);
            blocks.Add(Group("色彩", colour));

            TableLayoutPanel range = Grid(3);
            StyleRow(range, PilotHelpText.ShadowRange, false);
            StyleRow(range, PilotHelpText.HighlightRange, true);
            StyleRow(range, PilotHelpText.TransitionSoftness, true);
            blocks.Add(Group("范围", range));
            // v5.0.1 (W13): the monitor's DDC/CI is probed only once this card is opened (it can take seconds per monitor and
            // hang on some monitors / docks / laptops); the probe runs on the filter worker, every call time-limited.
            Control hwCard = Group("显示器硬件", BuildHardware());
            Control hwFold = Fold("显示器硬件（亮度 / 对比度 / 锐度，点开才检测）", hwCard);
            blocks.Add(hwFold);
            // v5.0.2 (UI-C M2): 高级设置 does not open it; only the user's click does, and only that probes DDC/CI
            ManualFold(hwFold, delegate { if (!IsDisposed) Fh.HardwareOpened(); });
            blocks.Add(Group("分享码", BuildShare()));
            Label more = Note("更多设置（白位、中间对比、色温、高光范围、过渡柔和、自然色彩）在顶部打开「高级设置」后显示。", 560);
            refreshers.Add(delegate { more.Visible = !Advanced; });
            blocks.Add(more);

            // ---------------------------------------------------------------- hotkeys
            TableLayoutPanel keys = Grid(2);
            for (int a = 0; a < FilterKeys.ActionCount; a++)
            {
                Label l = Lbl(FilterKeys.ActionNames[a]);
                Tip(l, PilotHelpText.Tooltip(FilterKeys.HelpKeys[a]));
                KeyButton kb = KeyBtn(FilterKeyBase + a);
                Tip(kb, PilotHelpText.Tooltip(FilterKeys.HelpKeys[a]));
                Row(keys, l, kb);
            }
            Label clash = Hint("", 560);
            clash.ForeColor = Theme.ColWarn;
            FilterRefresh(delegate
            {
                List<string> c = Fh.KeyClashes();
                clash.Text = string.Join("；", c.ToArray());
                clash.Visible = c.Count > 0;
            });
            CheckBox inGame = Check("只在游戏窗口在前面时响应快捷键", delegate { return Fh.Prefs.KeysOnlyInGame; },
                delegate(bool v) { Fh.Prefs.KeysOnlyInGame = v; Fh.SavePrefs(); });
            Tip(inGame, "开着时，游戏窗口不在最前面就不响应滤镜快捷键（游戏窗口标题在「分辨率 · 常规」页）");
            Button resetKeys = Btn("快捷键恢复初始", delegate
            {
                if (!Ask("把滤镜快捷键恢复成第一次安装时的样子？")) return;
                Fh.ResetKeys();
                RefreshValues();
            });
            TableLayoutPanel keyBox = Grid(1);
            keyBox.Controls.Add(Note("改键：点一下按键框，再按键（Esc 取消）。只能用 F1–F24 或带 Ctrl / Shift / Alt 的组合键，不能和准星的按键用同一个键。", 560));
            keyBox.Controls.Add(keys);
            keyBox.Controls.Add(clash);
            keyBox.Controls.Add(AdvOnly(inGame));
            keyBox.Controls.Add(resetKeys);
            blocks.Add(Group("快捷键", keyBox));

            FilterChangedHandler = delegate
            {
                if (IsDisposed) return;
                if (filterSelfChange) { if (filterGraph != null) filterGraph.Rebuild(); return; }
                RefreshFilterPage();
            };
            app.FilterChanged += FilterChangedHandler;
            FormClosed += delegate { app.FilterChanged -= FilterChangedHandler; };
            refreshers.Add(RunFilterRefreshers);
            TableLayoutPanel root = Stack(blocks.ToArray());
            filterPageRoot = root;
            root.VisibleChanged += delegate
            {
                if (!root.Visible) return;
                Fh.PageOpened(); // v5.0.1: the engine starts (background worker) the first time the 滤镜 page is shown
                if (filterPageStale) RunFilterRefreshers();
            };
            return root;
        }

        private Action FilterChangedHandler;
        private FilterSlider tuneBar;
        private Label tunePill, tuneWhy;
        private string tuneBase; // preset the bar position belongs to

        /// <summary>「调试滤镜」: left = darker, right = brighter, centre = the chosen preset exactly (FilterTune). The sliders below show
        /// the resulting values at once; 「另存为…」/「保存」 store them as a normal preset.</summary>
        private Control BuildTune()
        {
            tuneBar = new FilterSlider(FilterTune.Min, FilterTune.Max, 1);
            tuneBar.Value = 0;
            tuneBar.Anchor = AnchorStyles.Left;
            tuneBar.Margin = P(3, 3, 8, 3);
            tuneBar.Width = S(330);
            fitWidth.Add(new KeyValuePair<Control, int>(tuneBar, 360));
            lineControls.Add(tuneBar);
            Tip(tuneBar, "一共 11 格：往右整体更亮（暗部提得更多），往左更暗；中间 0 就是当前预设。下面的滑块会跟着一起变");
            tunePill = Lbl("0");
            tunePill.Font = monoFont;
            tunePill.Tag = "theme:keep-tag";
            tunePill.ForeColor = Theme.ColAccent2;
            tunePill.Cursor = Cursors.Hand;
            tunePill.TextAlign = ContentAlignment.MiddleRight;
            tunePill.MinimumSize = new Size(TextRenderer.MeasureText("+5", monoFont).Width + S(6), 0);
            Tip(tunePill, "点一下回到中间（当前预设本来的数值）");
            Label dark = Unit("更暗"), bright = Unit("更亮");
            tuneWhy = Hint("", 560);
            tuneWhy.ForeColor = Theme.ColWarn;
            tuneWhy.Visible = false;
            tuneBar.ValueChanged += delegate
            {
                if (loading) return;
                ApplyTune((int)Math.Round(tuneBar.Value));
            };
            tunePill.Click += delegate
            {
                bool was = loading; loading = true;
                try { tuneBar.Value = 0; } finally { loading = was; }
                ApplyTune(0);
            };
            FilterRefresh(delegate
            {
                if (tuneBar.Dragging) return;
                if (tuneBase != Fh.PresetName) { tuneBase = Fh.PresetName; tuneBar.Value = 0; tuneWhy.Visible = false; }
                int v = (int)Math.Round(tuneBar.Value);
                tunePill.Text = Signed(v);
            });
            TableLayoutPanel t = Grid(1);
            t.Controls.Add(Line(dark, tuneBar, bright, tunePill));
            t.Controls.Add(tuneWhy);
            t.Controls.Add(Note("一共 11 格（-5 到 +5），每格整体调亮或调暗一点：提亮暗部、黑位、中间调一起变，其他数值和饱和度不变。满意了点上面的「另存为…」或「保存」。", 560));
            return t;
        }

        private void ApplyTune(int bar)
        {
            tunePill.Text = Signed(bar);
            PilotStyle basePreset = Fh.SavedPreset(Fh.PresetName);
            if (basePreset == null) return;
            PilotStyle cur = Fh.Style;
            // v5.1: stop at the last step Windows still accepts (FilterLimit) and say why
            string why;
            int fit = FilterTune.Fit(basePreset, bar, out why);
            tuneWhy.Text = why;
            tuneWhy.Visible = why.Length > 0;
            if (fit != bar)
            {
                bar = fit;
                tunePill.Text = Signed(bar);
                if (!tuneBar.Dragging) { bool was = loading; loading = true; try { tuneBar.Value = bar; } finally { loading = was; } }
            }
            PilotStyle s = FilterTune.Apply(basePreset, bar);
            // saturation, monitor values and the name stay what the page shows now
            s.Name = cur.Name; s.Saturation = cur.Saturation; s.ApplySaturation = cur.ApplySaturation; s.SaturationBackend = cur.SaturationBackend;
            s.MonitorBrightness = cur.MonitorBrightness; s.MonitorContrast = cur.MonitorContrast; s.MonitorSharpness = cur.MonitorSharpness;
            tuneBase = Fh.PresetName;
            SetFilterStyle(s);
            RunFilterRefreshers(); // the other sliders show the new values now (the bar itself is skipped while dragged)
        }

        /// <summary>Preset picker whose list follows the engine (user presets are added / renamed / deleted on this page).</summary>
        private ComboBox PresetCombo()
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Margin = P(3, 3, 3, 3);
            c.Anchor = AnchorStyles.Left;
            Action fit = delegate
            {
                int w = S(120);
                foreach (object o in c.Items) w = Math.Max(w, TextRenderer.MeasureText(o.ToString(), Font).Width + SystemInformation.VerticalScrollBarWidth + S(16));
                c.Width = w;
            };
            fit();
            c.SelectedIndexChanged += delegate
            {
                if (loading || c.SelectedIndex < 0) return;
                string v = c.Items[c.SelectedIndex].ToString();
                if (v == Fh.PresetName) return;
                Fh.SelectPreset(v);
                RefreshFilterPage();
            };
            WheelScrollsPage(c);
            FilterRefresh(delegate
            {
                List<string> names = Fh.PresetNames();
                bool same = names.Count == c.Items.Count;
                for (int i = 0; same && i < names.Count; i++) same = c.Items[i].ToString() == names[i];
                bool was = loading;
                loading = true;
                try
                {
                    if (!same) { c.Items.Clear(); foreach (string n in names) c.Items.Add(n); fit(); }
                    int idx = names.IndexOf(Fh.PresetName);
                    c.SelectedIndex = idx < 0 ? (c.Items.Count > 0 ? 0 : -1) : idx;
                }
                finally { loading = was; }
            });
            return c;
        }

        /// <summary>显示器硬件: brightness / contrast / sharpness of the monitor (DDC/CI). Each row has a 「改」 switch: off = the monitor
        /// keeps its own value. Unsupported brightness / contrast rows are greyed with 「不支持」; unsupported sharpness is hidden.</summary>
        private Control BuildHardware()
        {
            MonitorHardware hw = Fh.Hardware();
            TableLayoutPanel g = Grid(4);
            HardwareRow(g, PilotHelpText.HardwareBrightness, "亮度", hw.Brightness, false,
                delegate(PilotStyle s) { return s.MonitorBrightness; }, delegate(PilotStyle s, int? v) { s.MonitorBrightness = v; },
                delegate { return (int)Fh.Hardware().Brightness.Current; });
            DdcValue cv = hw.Contrast;
            if (cv.Supported) { cv.Min = 0; cv.Max = 100; } // shown as -50..50 below (0 = middle of the monitor's range)
            HardwareRow(g, PilotHelpText.HardwareContrast, "对比度", cv, false,
                delegate(PilotStyle s) { return s.MonitorContrast; }, delegate(PilotStyle s, int? v) { s.MonitorContrast = v; },
                delegate { return Fh.HardwareContrastNow(); });
            HardwareRow(g, PilotHelpText.HardwareSharpness, "锐度", hw.Sharpness, true,
                delegate(PilotStyle s) { return s.MonitorSharpness; }, delegate(PilotStyle s, int? v) { s.MonitorSharpness = v; },
                delegate { return (int)Fh.Hardware().Sharpness.Current; });
            Label name = Hint("", 560);
            FilterRefresh(delegate
            {
                MonitorHardware h = Fh.Hardware();
                bool any = h.Brightness.Supported || h.Contrast.Supported || h.Sharpness.Supported;
                name.Text = !Fh.HardwareProbed ? "还没有检测显示器" : !Fh.Started ? "滤镜没有启动，显示器设置用不了" : any ? "显示器：" + (h.Name.Length > 0 ? h.Name : "主显示器")
                    : "这台显示器不支持用程序调亮度 / 对比度（需要显示器支持 DDC/CI，可以在显示器自己的菜单里打开）";
            });
            Button probe = Btn("重新检测显示器", delegate { Fh.RefreshMonitors(); RefreshFilterPage(); SetStatus("已重新检测显示器", false); });
            Tip(probe, "换了显示器或在显示器菜单里打开了 DDC/CI 后点这里");
            TableLayoutPanel box = Grid(1);
            box.Controls.Add(name);
            box.Controls.Add(g);
            box.Controls.Add(Note("勾上「改」才会改显示器本身的设置；只在滤镜开着时生效。关掉滤镜、按「全部恢复原样」或退出程序时，显示器会改回原来的值。拖动后显示器要等半秒左右才变。", 560));
            box.Controls.Add(probe);
            return box;
        }

        private void HardwareRow(TableLayoutPanel g, string key, string label, DdcValue v, bool hideWhenUnsupported,
            Func<PilotStyle, int?> get, Action<PilotStyle, int?> set, Func<int> monitorNow)
        {
            bool contrast = key == PilotHelpText.HardwareContrast;
            int min = v.Supported ? (contrast ? -50 : (int)v.Min) : 0, max = v.Supported ? (contrast ? 50 : (int)v.Max) : 100;
            if (max <= min) max = min + 1;
            Label l = Lbl(label);
            string tip = PilotHelpText.Tooltip(key);
            Tip(l, tip);
            CheckBox use = new CheckBox();
            use.Text = "改";
            use.AutoSize = true;
            use.Anchor = AnchorStyles.Left;
            use.Margin = P(3, 3, 8, 3);
            Tip(use, "勾上＝用下面的数值；不勾＝显示器保持自己的设置");
            FilterSlider sl = new FilterSlider(min, max, 1);
            sl.Anchor = AnchorStyles.Left;
            sl.Margin = P(3, 3, 8, 3);
            sl.Width = S(200);
            fitWidth.Add(new KeyValuePair<Control, int>(sl, 200));
            lineControls.Add(sl);
            Tip(sl, tip);
            Label pill = Lbl("");
            pill.Font = monoFont;
            pill.Tag = "theme:keep-tag";
            pill.ForeColor = Theme.ColAccent2;
            pill.TextAlign = ContentAlignment.MiddleRight;
            pill.MinimumSize = new Size(TextRenderer.MeasureText("不支持", Font).Width + S(6), 0);
            use.CheckedChanged += delegate
            {
                if (loading) return;
                PilotStyle s = Fh.Style;
                set(s, use.Checked ? (int?)(int)Math.Round(sl.Value) : null);
                sl.Enabled = use.Checked;
                SetFilterStyle(s);
            };
            sl.ValueChanged += delegate
            {
                if (loading) return;
                int x = (int)Math.Round(sl.Value);
                pill.Text = contrast ? Signed(x) : x.ToString();
                if (!use.Checked) return;
                PilotStyle s = Fh.Style;
                set(s, x);
                SetFilterStyle(s);
            };
            FilterRefresh(delegate
            {
                if (sl.Dragging) return;
                MonitorHardware h = Fh.Hardware();
                bool ok = key == PilotHelpText.HardwareBrightness ? h.Brightness.Supported : key == PilotHelpText.HardwareContrast ? h.Contrast.Supported : h.Sharpness.Supported;
                bool show = ok || !hideWhenUnsupported;
                l.Visible = use.Visible = sl.Visible = pill.Visible = show;
                int? val = get(Fh.Style);
                use.Checked = ok && val.HasValue;
                use.Enabled = ok;
                sl.Enabled = ok && val.HasValue;
                int now = 0;
                if (ok) { try { now = val.HasValue ? val.Value : monitorNow(); } catch (Exception) { now = 0; } }
                sl.Value = now;
                pill.Text = !ok ? DdcCiController.NotSupportedText : (contrast ? Signed((int)Math.Round(sl.Value)) : ((int)Math.Round(sl.Value)).ToString());
                pill.ForeColor = ok ? Theme.ColAccent2 : Theme.ColMuted;
                Font pf = ok ? monoFont : Font; // v5.0.2 (UI-C L5): Chinese text in the UI font, numbers in mono
                if (pill.Font != pf) pill.Font = pf;
            });
            Row(g, l, use, sl, pill);
        }

        /// <summary>分享码: copy the working values as a TSHADE2 code; paste a code to import it as a new preset.</summary>
        private Control BuildShare()
        {
            Button copy = Btn("复制分享码", delegate
            {
                string name = Fh.PresetName;
                Fh.ExportCode(delegate(string code)
                {
                    if (IsDisposed) return;
                    if (string.IsNullOrEmpty(code)) { SetStatus("滤镜没有启动，不能导出", true); return; }
                    CopyText(code, "已复制「" + name + "」的分享码，发给朋友，对方在这里粘贴导入");
                });
            });
            Tip(copy, ShareTip); // v5.0.2 (UI-C H2): TSHADE2, 分享码
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Margin = new Padding(0);
            box.Dock = DockStyle.Fill;
            Panel frame = new Panel();
            frame.Padding = new Padding(1);
            frame.Margin = P(3, 3, 8, 3);
            frame.BackColor = Theme.ColBorder;
            frame.Tag = "theme:keep-frame";
            frame.Controls.Add(box);
            frame.Anchor = AnchorStyles.Left;
            // v4.3 (V43-OVERLAP): height from the font + padding, so descenders are never clipped at any DPI.
            // v4.3.1: the frame decides the box height (AutoSize off), so the box is never taller than the frame's client.
            box.Font = Font;
            box.AutoSize = false;
            int textH = Math.Max(box.PreferredHeight, Math.Max(Font.Height, TextRenderer.MeasureText("Mg分享g", Font).Height));
            int pad = Math.Max(2, S(3));
            frame.Padding = new Padding(1 + pad, 1 + pad / 2, 1 + pad, 1 + pad / 2);
            frame.BackColor = Theme.ColBorder;
            frame.Size = new Size(S(320), textH + pad + 2); // >= textH + Padding.Vertical
            fitWidth.Add(new KeyValuePair<Control, int>(frame, 320));
            Button import = Btn("导入", delegate
            {
                Fh.ImportCode(box.Text, delegate(string n, string why)
                {
                    if (IsDisposed) return;
                    if (n == null) { SetStatus(why, true); return; }
                    box.Text = "";
                    SetStatus("已导入为新预设「" + n + "」（现在用的就是它）", false);
                });
            });
            Tip(import, "导入成一个新预设，不会覆盖你已有的预设");
            TableLayoutPanel t = Grid(1);
            t.Controls.Add(Line(copy));
            t.Controls.Add(Line(Lbl("粘贴分享码"), frame, import));
            t.Controls.Add(Note("分享码包含这套滤镜的全部数值（饱和度、显示器设置也在里面；对方显示器不支持的项会跳过）。只能导入本程序导出的分享码。", 560));
            return t;
        }

        private void FilterRefresh(Action a) { filterRefreshers.Add(a); }

        // v5.1 (W24): preset tip + the curve graph text (round 2: built-ins = 通用, 黑夜模式 only)
        internal const string PresetTip = "通用：开发者自己用的设置；黑夜模式：暗部提得更多，适合夜图；其他是你自己存的预设。选好再用「调试滤镜」调亮或调暗";
        internal const string CurveHint = "横着看：原来的亮度；竖着看：滤镜后的亮度。虚线 = 不改。拖曲线上的 5 个点（黑位、暗部、中间调、高光、白位）往上更亮、往下更暗，下面的滑块会跟着变；点一下曲线后也能用方向键调。";

        // v5.0.2 app-side tooltips (UI-C H2, H3, M1; the synced module keeps its own wording)
        internal const string ShareTip = "复制这套滤镜的分享码发给朋友；对方在这里粘贴、点「导入」就是同样的效果（TSHADE2 开头）。";
        internal const string RestoreTip = "画面出问题时点这里：颜色曲线、饱和度和显示器亮度 / 对比度 / 锐度全部回到打开程序前的样子，滤镜关掉。没有快捷键；退出程序时也会自动恢复。";
        internal const string SatTip = "整体颜色浓淡：50 不变，往上更浓，往下更淡。用显卡驱动还是 Windows 放大镜，在「饱和度方式」里选。";
        internal const string MagWarnText = "Windows 放大镜方式每一帧都要多算一遍整个屏幕，会降低游戏帧数，也可能和插帧（DLSS 帧生成、AFMF）冲突。显卡驱动支持时请用「显卡驱动（推荐）」。";

        /// <summary>Tooltip for a 滤镜 slider key (the app's own text where the module's is wrong for this app).</summary>
        internal static string StyleTip(string key) { return key == PilotHelpText.Saturation ? SatTip : PilotHelpText.Tooltip(key).Replace("Gamma 曲线", "颜色曲线"); } // UI-C L10

        /// <summary>v5.0.2 (UI-C M2): a fold that only the user opens. 高级设置 sets every fold's Open (SettingsForm.cs); for this
        /// fold an Open that does not come from the user's own click / Space / Enter is undone at once.</summary>
        internal static void ManualFold(Control fold, Action userOpened)
        {
            FoldHeader h = null;
            foreach (Control c in fold.Controls) { h = c as FoldHeader; if (h != null) break; }
            if (h == null) return;
            bool user = false;
            h.Click += delegate { user = true; };
            h.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) user = true; };
            h.Toggled += delegate
            {
                bool u = user;
                user = false;
                if (h.Open && !u) { h.Open = false; return; }
                if (h.Open && userOpened != null) userOpened();
            };
        }

        private bool filterPageStale;
        private Control filterPageRoot;

        private void RunFilterRefreshers()
        {
            bool was = loading;
            loading = true; // refreshers set values; no change handler may write back
            try
            {
                foreach (Action r in filterRefreshers)
                {
                    try { r(); } catch (Exception ex) { Program.Log("UI: filter refresh: " + ex.Message); }
                }
            }
            finally { loading = was; }
            filterPageStale = false;
        }

        /// <summary>v4.3 (V43-PERF TOP 1 a): a filter change refreshes the 滤镜 page only, and only while it is shown; a hidden page
        /// is marked stale and refreshed when it becomes visible (never the whole window).</summary>
        private void RefreshFilterPage()
        {
            if (IsDisposed) return;
            if (filterPageRoot != null && !filterPageRoot.Visible) { filterPageStale = true; return; }
            RunFilterRefreshers();
        }

        /// <summary>Slider / check edits: apply at once, without re-reading the whole window (only the graph follows).</summary>
        private void SetFilterStyle(PilotStyle s)
        {
            filterSelfChange = true;
            try { Fh.SetStyle(s); }
            finally { filterSelfChange = false; }
            liveSlot = ' ';
            if (filterGraph != null) filterGraph.Rebuild();
        }

        private void SwapAb()
        {
            if (slotA == null || slotB == null) return;
            liveSlot = liveSlot == 'A' ? 'B' : 'A';
            filterSelfChange = true;
            try { Fh.SetStyle(liveSlot == 'A' ? slotA : slotB); }
            finally { filterSelfChange = false; }
            RefreshFilterPage();
            SetStatus("现在是 " + liveSlot, false);
        }

        /// <summary>One slider row: label (tooltip) · slider · value pill (click = back to neutral). Advanced rows only in 高级设置.</summary>
        private void StyleRow(TableLayoutPanel g, string key, bool advanced)
        {
            int min, max, neutral;
            StyleRange(key, out min, out max, out neutral);
            Label l = Lbl(PilotHelpText.Label(key));
            string tip = StyleTip(key);
            Tip(l, tip);
            FilterSlider sl = new FilterSlider(min, max, 1);
            sl.Anchor = AnchorStyles.Left;
            sl.Margin = P(3, 3, 8, 3);
            sl.Width = S(240);
            fitWidth.Add(new KeyValuePair<Control, int>(sl, 240));
            lineControls.Add(sl);
            Tip(sl, tip);
            Label pill = Lbl("0");
            pill.Font = monoFont;
            pill.Tag = "theme:keep-tag";
            pill.ForeColor = Theme.ColAccent2;
            pill.Cursor = Cursors.Hand;
            pill.TextAlign = ContentAlignment.MiddleRight;
            pill.MinimumSize = new Size(TextRenderer.MeasureText("+150", monoFont).Width + S(6), 0);
            Tip(pill, "点一下回到 " + PillText(key, neutral));
            sl.ValueChanged += delegate
            {
                if (loading) return;
                int v = (int)Math.Round(sl.Value);
                pill.Text = PillText(key, v);
                PilotStyle s = Fh.Style;
                SetStyleValue(s, key, v);
                SetFilterStyle(s);
            };
            pill.Click += delegate
            {
                PilotStyle s = Fh.Style;
                SetStyleValue(s, key, neutral);
                SetFilterStyle(s);
                bool was = loading;
                loading = true;
                try { sl.Value = neutral; pill.Text = PillText(key, neutral); }
                finally { loading = was; }
            };
            FilterRefresh(delegate
            {
                if (sl.Dragging) return; // v4.3: values come back from the filter worker later; never yank a slider being dragged
                int v = GetStyleValue(Fh.Style, key);
                sl.Value = v;
                pill.Text = PillText(key, v);
            });
            if (advanced) AdvRow(g, l, sl, pill);
            else Row(g, l, sl, pill);
        }

        private static string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }

        /// <summary>Value pill: signed for the style sliders, plain for 饱和度 (0..100, 50 = 不变).</summary>
        private static string PillText(string key, int v) { return key == PilotHelpText.Saturation ? v.ToString() : Signed(v); }

        /// <summary>Slider ranges = 标准 (PILOT-INTEGRATION.md 6): 提亮暗部 / 压低高光 0..150, 饱和度 0..100 (50 = 不变), others -70..70.</summary>
        internal static void StyleRange(string key, out int min, out int max, out int neutral)
        {
            if (key == PilotHelpText.Shadows || key == PilotHelpText.Highlights)
            {
                min = 0; max = PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode); neutral = 0;
            }
            else if (key == PilotHelpText.Saturation)
            {
                min = PilotStyle.SaturationMin; max = PilotStyle.SaturationMax; neutral = PilotStyle.SaturationNeutral;
            }
            else
            {
                int m = PilotStyle.StrengthSignedMax(PilotViews.FixedStrengthMode);
                min = -m; max = m; neutral = 0;
            }
        }

        internal static int GetStyleValue(PilotStyle s, string key)
        {
            switch (key)
            {
                case PilotHelpText.Shadows: return s.Shadows;
                case PilotHelpText.Highlights: return s.Highlights;
                case PilotHelpText.Midtones: return s.Midtones;
                case PilotHelpText.BlackPoint: return s.BlackPoint;
                case PilotHelpText.WhitePoint: return s.WhitePoint;
                case PilotHelpText.ContrastPivot: return s.ContrastPivot;
                case PilotHelpText.Temperature: return s.Temperature;
                case PilotHelpText.Vibrance: return s.Vibrance;
                case PilotHelpText.ShadowRange: return s.ShadowRange;
                case PilotHelpText.HighlightRange: return s.HighlightRange;
                case PilotHelpText.TransitionSoftness: return s.TransitionSoftness;
                case PilotHelpText.Saturation: return s.Saturation;
            }
            return 0;
        }

        internal static void SetStyleValue(PilotStyle s, string key, int v)
        {
            switch (key)
            {
                case PilotHelpText.Shadows: s.Shadows = v; break;
                case PilotHelpText.Highlights: s.Highlights = v; break;
                case PilotHelpText.Midtones: s.Midtones = v; break;
                case PilotHelpText.BlackPoint: s.BlackPoint = v; break;
                case PilotHelpText.WhitePoint: s.WhitePoint = v; break;
                case PilotHelpText.ContrastPivot: s.ContrastPivot = v; break;
                case PilotHelpText.Temperature: s.Temperature = v; break;
                case PilotHelpText.Vibrance: s.Vibrance = v; break;
                case PilotHelpText.ShadowRange: s.ShadowRange = v; break;
                case PilotHelpText.HighlightRange: s.HighlightRange = v; break;
                case PilotHelpText.TransitionSoftness: s.TransitionSoftness = v; break;
                case PilotHelpText.Saturation: s.Saturation = v; break;
            }
        }

        // ---------------------------------------------------------------- hotkey buttons (hooks in SettingsForm.cs)

        private string FilterKeyText(int action)
        {
            if (action < 0 || action >= FilterKeys.ActionCount) return "未设置";
            return FilterKeys.ComboText(Fh.Prefs.Vk[action], Fh.Prefs.Mods[action]);
        }

        private void FinishFilterCapture(KeyButton b, int vk, int mods)
        {
            string why = Fh.SetKey(b.Action - FilterKeyBase, vk, mods);
            b.Text = KeyText(b.Action);
            if (why.Length > 0) SetStatus(why, true);
            else SetStatus(FilterKeys.ActionNames[b.Action - FilterKeyBase] + " = " + KeyText(b.Action), false);
            RunFilterRefreshers();
        }

        /// <summary>The 按键 page side of the two-way check: a crosshair key may not use a 滤镜 key's base key. null = free.</summary>
        private string CrosshairKeyFilterClash(int vk)
        {
            if (app.Filter == null) return null;
            int a = FilterKeys.FilterUsing(app.Filter.Prefs, vk);
            if (a < 0) return null;
            return "「" + KeyBindings.KeyName(vk) + "」已经用在滤镜的「" + FilterKeys.ActionNames[a] + "」（" + FilterKeyText(a) + "），去「滤镜」页先改掉它";
        }

        /// <summary>v5.0.2 (UI-C M8): the filter keys as the tutorial shows them (names with modifiers; vks for the keys it reacts to).</summary>
        internal static FilterTutorialKeys TutorialKeys(FilterPrefs p)
        {
            FilterTutorialKeys k = FilterTutorialKeys.Default();
            if (p == null) return k;
            k.Toggle = FilterKeys.ComboText(p.Vk[FilterKeys.ActionToggle], p.Mods[FilterKeys.ActionToggle]);
            k.NextPreset = FilterKeys.ComboText(p.Vk[FilterKeys.ActionNext], p.Mods[FilterKeys.ActionNext]);
            k.HoldOriginal = FilterKeys.ComboText(p.Vk[FilterKeys.ActionHold], p.Mods[FilterKeys.ActionHold]);
            k.ToggleVk = Math.Max(0, p.Vk[FilterKeys.ActionToggle]);
            k.NextVk = Math.Max(0, p.Vk[FilterKeys.ActionNext]);
            k.HoldVk = Math.Max(0, p.Vk[FilterKeys.ActionHold]);
            return k;
        }

        /// <summary>「滤镜教程」: W3's FilterTutorialForm.Show(owner), looked up at run time so this page builds before it exists.</summary>
        private void OpenFilterTutorial()
        {
            try
            {
                Type t = typeof(SettingsForm).Assembly.GetType("TarkovCrosshair.FilterTutorialForm");
                // v5.0.2 (UI-C M8): the tutorial shows the user's own filter keys
                MethodInfo m = t == null ? null : t.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(IWin32Window), typeof(FilterTutorialKeys) }, null);
                if (m == null) { Say("滤镜教程还没有做好。"); return; }
                m.Invoke(null, new object[] { this, TutorialKeys(Fh.Prefs) });
            }
            catch (Exception ex)
            {
                Program.Log("UI: filter tutorial: " + ex);
                Say("滤镜教程打不开，详细信息在 crash.log。");
            }
        }
    }
}
#endif
