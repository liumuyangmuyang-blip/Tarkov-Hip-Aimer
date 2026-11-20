using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 按键 page (PAGE-A). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>按键: the 9 rebindable actions (W/A/S/D are fixed), combos, and the ADS / lean / hand options.</summary>
        private IEnumerable<Control> BuildKeysSteps()
        {
            TableLayoutPanel g = HeldGrid(3);
            Label toggleL = Lbl("开关准星");
            Tip(toggleL, "按两下（关再开）＝回到正常手、退出强化准星");
            Label toggleHint = Hint("连按两下＝全部复位", 220);
            Tip(toggleHint, "按两下（关再开）＝回到正常手、退出强化准星、取消切换的侧身");
            Row(g, toggleL, KeyBtn(0), toggleHint);
            Row(g, "开火", KeyBtn(1), Blank());
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            CheckBox adsTab = Check("Tab / Esc 取消开镜", delegate { return G.AdsCancelTabEsc; }, delegate(bool v) { G.AdsCancelTabEsc = v; });
            CheckBox adsSprint = Check("冲刺（Shift+W）取消开镜", delegate { return G.AdsCancelSprint; }, delegate(bool v) { G.AdsCancelSprint = v; });
            Tip(adsSprint, "只按 Shift（屏息）不算");
            Action adsToggleOnly = delegate { bool t = G.AdsMode == "toggle"; adsTab.Visible = t && Advanced; adsSprint.Visible = t; };
            refreshers.Add(adsToggleOnly);
            Row(g, "开镜", KeyBtn(2), Line(Lbl("开镜方式"), Choice(new string[] { "按住", "点一下切换" }, new string[] { "hold", "toggle" },
                delegate { return G.AdsMode; }, delegate(string v) { G.AdsMode = v; adsToggleOnly(); })));
            // one row that always has a visible item, so hiding the 切换-only boxes never leaves an empty row
            // stacked, not side by side: three boxes in one line made the page scroll sideways at small windows
            Row(g, Blank(), Blank(), Stack(
                AdvOnly(Check("Alt / Ctrl + 右键不算开镜", delegate { return G.AdsIgnoreAltCtrl; }, delegate(bool v) { G.AdsIgnoreAltCtrl = v; })),
                adsTab, adsSprint));

            CheckBox leanSprint = LeanSprintCheck();
            Row(g, "左侧身", KeyBtn(5), Line(Lbl("侧身方式"), LeanModeChoice()));
            Row(g, "右侧身", KeyBtn(6), Blank());
            Row(g, Blank(), Blank(), leanSprint); // own row: it applies to both lean keys
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            CheckBox hsOn = Check("换手检测", delegate { return G.HandEnabled; }, delegate(bool v) { G.HandEnabled = v; RefreshValues(); });
            Tip(hsOn, "程序只能按按键判断你用哪只手；对不上时按两下开关准星键（关再开）回到正常手");
            KeyButton handKey = KeyBtn(4);
            Label handModeL = Lbl("换手方式");
            ComboBox handMode = Choice(new string[] { "按住", "点一下切换" }, new string[] { "hold", "toggle" },
                delegate { return G.HandMode; }, delegate(string v) { G.HandMode = v; });
            EnableWhen(delegate { return G.HandEnabled; }, handKey, handModeL, handMode);
            // 高级设置 only -- but while 换手检测 is off it stays visible, so the greyed key can always be switched back on
            refreshers.Add(delegate { hsOn.Visible = Advanced || !G.HandEnabled; });
            Row(g, "换手", handKey, Line(handModeL, handMode, hsOn));
            Row(g, "跳跃", KeyBtn(11), Blank());
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            // W/A/S/D are fixed (SPEC v4 req 2): shown read-only so nobody looks for them
            Label wasd = Lbl("W  A  S  D");
            wasd.Font = monoFont;
            wasd.ForeColor = SystemColors.GrayText;
            wasd.TextAlign = ContentAlignment.MiddleCenter;
            wasd.Margin = P(3, 3, 8, 3);
            // same width as the key boxes above, so the column lines up
            wasd.MinimumSize = new Size(Math.Max(S(150), TextW("鼠标侧键1（XButton1）", Font) + S(18)), Font.Height + S(6));
            Row(g, "前后左右", wasd, Hint("固定，不用改", 220));
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            AdvRow(g, "自由视角（转头）", KeyBtn(3),
                Check("按住时隐藏准星", delegate { return G.HideOnFreeLook; }, delegate(bool v) { G.HideOnFreeLook = v; }));
            // v5.1 (decision 3, corrected by Leo): 强化 key = the game's 切换战术设备模式 key (v5.1.1 default 鼠标侧键2); 切换 (default) / 按住
            CheckBox boostOn = Check("启用强化准星", delegate { return G.BoostEnabled; }, delegate(bool v) { G.BoostEnabled = v; });
            Label boostL = Lbl("切换战术设备模式键（红外/手电）");
            Tip(boostL, "和游戏里「切换战术设备模式」用同一个键（默认鼠标侧键2：游戏里也把这个动作绑到鼠标侧键2，或在这里改成游戏里的键）：切到手电＝强化准星开，切回红外＝关。开关准星（关再开）和换武器也会关掉强化");
            KeyButton boostKey = KeyBtn(12);
            Label boostModeL = Lbl("强化方式");
            ComboBox boostMode = Choice(new string[] { "点一下切换", "按住" }, new string[] { "toggle", "hold" },
                delegate { return G.BoostToggle ? "toggle" : "hold"; }, delegate(string v) { G.BoostToggle = v == "toggle"; });
            EnableWhen(delegate { return G.BoostEnabled; }, boostKey, boostModeL, boostMode);
            Row(g, boostL, boostKey, Line(boostModeL, boostMode, boostOn, HelpMark.For("boost", Font, delegate { return G; })));
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            TableLayoutPanel keyBox = HeldGrid(1);
            keyBox.Controls.Add(Note("改键：点一下按键框，再按键盘或鼠标键（Esc 取消）。组合键：先按住 Ctrl / Shift / Alt 再按键，例如 Ctrl+D。", 600));
            keyBox.Controls.Add(g);
            keyBox.Controls.Add(Note("开镜方式、侧身方式、换手方式要和游戏里的设置一样。", 600));
            keyBox.Controls.Add(Note("按组合键时只算组合键：例如右侧身设成 Ctrl+D，按 Ctrl+D 只会侧身，准星不会再当成按了 D 去移动。", 600));
            Section gb = Group("按键映射", keyBox);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            Section gGrenade = Group("丢雷隐藏", BuildGrenadeBox());
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            // v5.1.1 (X7, Leo 21 Nov): the 弹药提示 setup lives on 外观 -> 弹药提示 (group 先设置); here only a pointer
            Label ammoPointer = Note("弹药提示的设置在「外观」→「弹药提示」。", 600);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            Button reset = Btn("按键恢复初始", delegate
            {
                if (!Ask("把这一页的按键和方式全部恢复成第一次安装时的样子？")) return;
                G.ResetKeys();
                RefreshValues();
                app.UpdateTray();
                app.Changed();
            });
            TableLayoutPanel root = HeldStack(gb, gGrenade, ammoPointer, reset);
            ReleaseHeldOnShow(root); // v5.1.1 (P3): held through the first fill, one layout pass on the first show
            yield return root;
        }

        // ---------------- v5.1: 丢雷隐藏 (decision 2) ----------------

        private Control BuildGrenadeBox()
        {
            TableLayoutPanel t = HeldGrid(2);
            Button key = VkBtn(delegate { return G.GrenadeKey; }, delegate(int vk) { G.GrenadeKey = vk; });
            CheckBox on = Check("启用丢雷隐藏", delegate { return G.GrenadeHideOn; }, delegate(bool v) { G.GrenadeHideOn = v; });
            EnableWhen(delegate { return G.GrenadeHideOn; }, key);
            Row(t, "丢雷键", Line(key, on, HelpMark.For("grenade", Font, delegate { return G; })));
            TableLayoutPanel box = HeldGrid(1);
            box.Controls.Add(t);
            box.Controls.Add(Note("按一下丢雷键：准星隐藏；再按一下：显示。不会自己回来；开关准星连按两下也会让它回来。", 600));
            return box;
        }

        // ---------------- v5.1: 弹药提示 settings (decision 1) ----------------
        // v5.1.1 (X7, Leo 21 Nov): shown as the first group 「先设置」 of 外观 -> 弹药提示 (BuildStyle -> StylePanel.SetAmmoSetup),
        // above the bar look; the 按键 page only points there. Same settings fields; ReloadSecBox keeps 常调 in sync.

        private Control BuildAmmoBox()
        {
            TableLayoutPanel t = HeldGrid(2);
            CheckBox on = Check("显示弹药提示（只算主武器）", delegate { return G.AmmoOn; }, delegate(bool v) { G.AmmoOn = v; RefreshValues(); });
            Wide(t, Line(on, HelpMark.For("ammo", Font, delegate { return G; })));
            NumericUpDown mag = Num(1, 200, 1, 0, delegate { return G.AmmoMagSize; }, delegate(double v) { G.AmmoMagSize = (int)Math.Round(v); });
            NumericUpDown rpm = Num(60, 2000, 10, 0, delegate { return G.AmmoRpm; }, delegate(double v) { G.AmmoRpm = (int)Math.Round(v); });
            ComboBox mode = Choice(new string[] { "全自动", "半自动" }, new string[] { "auto", "semi" },
                delegate { return G.AmmoSemi ? "semi" : "auto"; }, delegate(string v) { G.AmmoSemi = v == "semi"; });
            Button reload = VkBtn(delegate { return G.AmmoReloadKey; }, delegate(int vk) { G.AmmoReloadKey = vk; });
            NumericUpDown tap = Num(100, 800, 10, 0, delegate { return G.AmmoQuickTapMs; }, delegate(double v) { G.AmmoQuickTapMs = (int)Math.Round(v); });
            NumericUpDown rTime = ReloadSecBox(false), qTime = ReloadSecBox(true);
            Row(t, "弹匣容量", Line(mag, Unit("发")));
            Row(t, "射速", Line(rpm, Unit("发/分钟"), mode));
            Row(t, "换弹键", Line(reload, Hint("按一下＝普通换弹，连按两下＝快速换弹", 300), HelpMark.For("reload", Font, delegate { return G; })));
            Row(t, "连按两下判定", Line(tap, Unit("ms")));
            Row(t, "换弹时间", Line(rTime, Unit("秒"), Hint("按一下换弹键：这么久后换上一个满弹匣", 300)));
            Row(t, "快速换弹时间", Line(qTime, Unit("秒"), Hint("连按两下：旧弹匣丢掉，这么久后换上满弹匣", 300)));
            EnableWhen(delegate { return G.AmmoOn; }, mag, rpm, mode, reload, tap, rTime, qTime);
            TableLayoutPanel box = HeldGrid(1);
            box.Controls.Add(t);
            box.Controls.Add(Note("按住开火的时间 × 射速 = 打出去的子弹（半自动：点一下算一发）。只显示大概还剩多少，不显示数字。换弹（普通或快速）都是换上一个新的满弹匣。换弹时间在首页「常调」里也能改。提示的样子在下面改。", 600));
            return box;
        }

        /// <summary>A key box for a plain VK setting (no combo): click, then press a key (Esc cancels). Keyboard keys, the
        /// middle button and the mouse side buttons (v5.1.1 F3: also read directly while waiting, see KeyBoxMouse.cs).</summary>
        private Button VkBtn(Func<int> get, Action<int> set)
        {
            Button b = new Button();
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(Math.Max(S(150), TextW("鼠标侧键1（XButton1）", Font) + S(18)), Font.Height + S(10));
            lineControls.Add(b);
            b.Anchor = AnchorStyles.Left;
            b.Margin = P(3, 3, 8, 3);
            bool waiting = false;
            Action show = delegate { b.Text = get() <= 0 ? "未设置" : KeyBindings.KeyName(get()); b.ForeColor = Theme.ColText; };
            Action<int> done = delegate(int vk)
            {
                waiting = false;
                mousePoll.Stop();
                if (vk >= 1 && vk <= 254) { set(vk); app.Changed(); SetStatus("已设成 " + KeyBindings.KeyName(vk), false); }
                show();
            };
            b.Click += delegate
            {
                if (capturing != null) CancelCapture();
                waiting = true; b.Text = "请按一个键…"; b.ForeColor = Theme.ColAccent;
                mousePoll.Start(delegate(int pvk) { if (waiting) done(pvk); });
            };
            b.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e) { if (waiting) e.IsInputKey = true; };
            b.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!waiting) return;
                e.Handled = true; e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { waiting = false; mousePoll.Stop(); show(); return; }
                done((int)e.KeyCode);
            };
            b.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (!waiting) return;
                if (e.Button == MouseButtons.XButton1) done(KeyBindings.VkXButton1);
                else if (e.Button == MouseButtons.XButton2) done(KeyBindings.VkXButton2);
                else if (e.Button == MouseButtons.Middle) done(0x04);
            };
            b.Leave += delegate { if (waiting) { waiting = false; mousePoll.Stop(); show(); } };
            refreshers.Add(delegate { if (!waiting) show(); });
            return b;
        }

/// <summary>v5.1 R2: 换弹时间 / 快速换弹时间 in seconds (0.1 s steps; stored in ms).</summary>
        private NumericUpDown ReloadSecBox(bool quick)
        {
            double min = (quick ? GlobalSettings.AmmoQuickReloadMsMin : GlobalSettings.AmmoReloadMsMin) / 1000.0;
            double max = (quick ? GlobalSettings.AmmoQuickReloadMsMax : GlobalSettings.AmmoReloadMsMax) / 1000.0;
            return Num(min, max, 0.1, 1,
                delegate { return (quick ? G.AmmoQuickReloadMs : G.AmmoReloadMs) / 1000.0; },
                delegate(double v)
                {
                    double ms = Math.Round(v * 1000.0);
                    if (quick) G.AmmoQuickReloadMs = ms; else G.AmmoReloadMs = ms;
                    RefreshReloadBoxes(quick);
                });
        }

        /// <summary>The other boxes for the same reload time (按键 page / 常调 strip) follow without a full refresh.</summary>
        private readonly List<Action<bool>> reloadBoxRefreshers = new List<Action<bool>>();
        private void RefreshReloadBoxes(bool quick)
        {
            bool was = loading;
            loading = true;
            try { foreach (Action<bool> a in reloadBoxRefreshers) a(quick); }
            finally { loading = was; }
        }

        /// <summary>v5.1 R2 (Leo round 2): 换弹 / 快换 times right in the 首页 「常调」 strip (局内可调), shown while 弹药提示 is on.
        /// Called by BuildTuneStrip.</summary>
        private void AddReloadTunes(Control f)
        {
            for (int q = 0; q < 2; q++)
            {
                bool quick = q == 1;
                NumericUpDown n = ReloadSecBox(quick);
                n.Margin = P(2, 3, 2, 3);
                reloadBoxRefreshers.Add(delegate(bool which)
                {
                    if (which != quick) return;
                    double ms = quick ? G.AmmoQuickReloadMs : G.AmmoReloadMs;
                    decimal v = Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)Math.Round(ms / 1000.0, 1)));
                    if (n.Value != v) n.Value = v;
                });
                Label l = Lbl(quick ? "快速换弹" : "换弹");
                l.Margin = P(12, 3, 2, 3);
                TableLayoutPanel item = Line(l, n, Unit("秒"));
                item.Margin = new Padding(0);
                refreshers.Add(delegate { item.Visible = G.AmmoOn; });
                f.Controls.Add(item);
            }
        }
    }
}
#endif
