// 用途：构建按键设置页，编辑并保存多个取消开镜键。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>构建按键设置页，保留原有绑定和恢复默认入口。</summary>
        private Control BuildKeys()
        {
            TableLayoutPanel g = Grid(3);
            Row(g, "开关准星", KeyBtn(0), Blank());
            Row(g, "开火", KeyBtn(1), Blank());
            Row(g, "开镜", KeyBtn(2), Line(Lbl("开镜方式"), Choice(new string[] { "按住", "点一下切换" }, new string[] { "hold", "toggle" },
                delegate { return G.AdsMode; }, delegate(string v) { G.AdsMode = v; })));
            AddCancelAdsKeysRow(g);
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

        /// <summary>显示当前取消键，点击配置后沿用设置发布和延迟保存流程。</summary>
        private void AddCancelAdsKeysRow(TableLayoutPanel grid)
        {
            Label summary = Lbl("");
            summary.MaximumSize = new Size(S(260), 0);
            refreshers.Add(delegate
            {
                int[] keys = G.Keys.CancelAdsKeys;
                List<string> names = new List<string>();
                if (keys != null)
                    foreach (int key in keys) names.Add(KeyName(key));
                summary.Text = names.Count == 0 ? "未设置" : string.Join("、", names.ToArray());
            });
            Button edit = Btn("配置取消键…", delegate
            {
                using (CancelAdsKeysForm dialog = new CancelAdsKeysForm(G.Keys))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    G.Keys.CancelAdsKeys = dialog.SelectedKeys;
                    app.Changed();
                    RefreshValues();
                }
            });
            Row(grid, "取消开镜", edit, summary);
            Tip(edit, "仅用于点一下切换开镜。默认 Tab、Esc 清掉工具记录，不向游戏发送按键；背包内显隐不单独控制。");
        }
    }
}
