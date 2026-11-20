using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 侧身 + 换手 pages (PAGE-C). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>「去「按键」页改 →」: jumps to the 按键 page (key bindings + 侧身方式 / 换手方式 live there).</summary>
        private Button KeysPageLink(string text)
        {
            Button b = Btn(text, delegate
            {
                int i = Array.IndexOf(UiPages.Ids, "keys");
                if (i >= 0) ShowPage(i);
            });
            return b;
        }

        /// <summary>A label whose text (and colour) a refresher rewrites; starts in the accent colour.</summary>
        private Label StatusLbl()
        {
            Label l = Lbl("");
            l.ForeColor = Theme.ColAccent;
            return l;
        }

        // ---------------- 侧身 page ----------------

        private Control BuildLean()
        {
            // --- 侧身方式: read-only summary (the setting itself is on the 按键 page, one place to change it)
            Label mode = StatusLbl();
            Label sprint = Lbl("");
            refreshers.Add(delegate
            {
                bool toggle = G.LeanMode == "toggle";
                mode.Text = toggle ? "点一下切换（按一下侧身，再按一下回正）" : "按住（按住侧身，松开回正）";
                sprint.Text = "冲刺（Shift+W）取消侧身：" + (G.LeanCancelSprint ? "开" : "关");
                sprint.Visible = toggle;
            });
            TableLayoutPanel m = HeldGrid(2);
            Row(m, "侧身方式", mode);
            Row(m, Blank(), sprint);
            TableLayoutPanel modeBox = HeldGrid(1);
            Wide(modeBox, m);
            Wide(modeBox, Note("要和游戏里的侧身方式一样。侧身方式和侧身按键都在「按键」页改。", 520));
            Wide(modeBox, KeysPageLink("去「按键」页改 →"));
            Section gm = Group("侧身方式", modeBox);

            // --- 侧身偏移
            // v4.1 百分比偏移: [arrow] + positive % (storage stays px)
            TableLayoutPanel g = HeldGrid(3);
            Cells(g, Blank(), Lbl("左右"), Lbl("上下"));
            Label lq = Lbl("左侧身"), le = Lbl("右侧身");
            refreshers.Add(delegate
            {
                lq.Text = "左侧身（" + G.Keys.ComboOf(KeyBindings.ActionLeanLeft) + "）";
                le.Text = "右侧身（" + G.Keys.ComboOf(KeyBindings.ActionLeanRight) + "）";
            });
            // v5.1.1 (X8, Leo 21 Nov option A): lean counts from the SCREEN CENTRE (the engine fades the 站立 point out while
            // leaning), so its X box shows the on-screen distance from the centre (FOV side; Origin null = 0)
            OffsetField qx = Offset(OffsetAxis.X, OffsetKind.Toggle, 500, delegate { return Cur.LeanQX; }, delegate(double v) { Cur.LeanQX = v; });
            OffsetField ex = Offset(OffsetAxis.X, OffsetKind.Toggle, 500, delegate { return Cur.LeanEX; }, delegate(double v) { Cur.LeanEX = v; });
            Row(g, lq,
                qx.Line,
                Offset(OffsetAxis.Y, OffsetKind.Toggle, 500, delegate { return Cur.LeanQY; }, delegate(double v) { Cur.LeanQY = v; }).Line);
            Row(g, le,
                ex.Line,
                Offset(OffsetAxis.Y, OffsetKind.Toggle, 500, delegate { return Cur.LeanEY; }, delegate(double v) { Cur.LeanEY = v; }).Line);
            AdvRow(g, "过渡时间", Line(ArrowSpacer(), Num(0, 2000, 10, 0, delegate { return Cur.LeanMs; }, delegate(double v) { Cur.LeanMs = v; }), Unit("ms")), Blank());
            TableLayoutPanel box = HeldGrid(1);
            Wide(box, g);
            Wide(box, Note("怎么调：站着不动，侧身开枪，把偏移调到准星差不多盖住弹孔就行。", 520));
            Wide(box, Note("侧身时再移动：侧身偏移和移动偏移直接相加，不用另外调。", 520));
            Section gb = Group("侧身偏移", box);
            TableLayoutPanel root = HeldStack(gb, gm);
            ReleaseHeldOnShow(root); // v5.1.1 (P3): held through the first fill, one layout pass on the first show
            return root;
        }

        // ---------------- 换手 page (v3.1 换手自定义) ----------------

        /// <summary>Re-read the 换手 boxes (the 自动 rows follow the normal hand, which other pages may have changed).</summary>
        private readonly List<Action> handRefreshers = new List<Action>();
        /// <summary>The 正常手 column (header + one label per row): hidden together on a narrow page.</summary>
        private readonly List<Label> handNormal = new List<Label>();
        /// <summary>Repaint the 换手 rows: 正常手 column, grey = 自动, 「恢复自动」 on custom rows.</summary>
        private readonly List<Action> handPainters = new List<Action>();

        private void PaintHandRows() { foreach (Action a in handPainters) a(); }

        private IEnumerable<Control> BuildHandSteps()
        {
            // --- 换手键 summary (key, 换手方式 and 换手检测 are on the 按键 page)
            Label state = StatusLbl();
            refreshers.Add(delegate
            {
                if (!G.HandEnabled)
                {
                    state.Text = "换手检测已关：下面的数现在不会用到。";
                    state.ForeColor = Theme.ColWarn;
                }
                else
                {
                    state.Text = G.Keys.ComboOf(KeyBindings.ActionHandSwitch) + " · " + (G.HandMode == "hold" ? "按住" : "点一下切换");
                    state.ForeColor = Theme.ColAccent;
                }
            });
            TableLayoutPanel k1 = HeldGrid(2);
            Row(k1, "换手键", state);
            TableLayoutPanel keyBox = HeldGrid(1);
            Wide(keyBox, k1);
            Wide(keyBox, Note("要和游戏里的换手方式一样。程序只能按按键判断你用哪只手，对不上时按两下开关准星键（关再开）回到正常手。", 520));
            Wide(keyBox, KeysPageLink("去「按键」页改 →"));
            Section gk = Group("换手键", keyBox);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            // --- 换手后的偏移
            TableLayoutPanel g = HeldGrid(5);
            Label normalHead = Lbl("正常手 左右 / 上下");
            handNormal.Add(normalHead);
            // column heads start where their cells start (Row's FlushLeft rule, UIALIGN41 #7)
            Label headX = Lbl("换手后 左右"), headY = Lbl("换手后 上下");
            foreach (Label h in new Label[] { normalHead, headX, headY }) FlushLeft(h);
            Cells(g, Blank(), normalHead, headX, headY, Blank()); // header row: fixed cells (v4.3.1)
            foreach (int i in UiPages.HandRows()) { AddHandRow(g, i); yield return null; } // never the standing-still item (v5.1: 站立静止偏移 lives on 移动; 换手 mirrors it automatically)
            // F2 (narrow page, e.g. 1280x720 at 200 %): the 正常手 column is left out instead of the table scrolling sideways
            int hiddenW = 0;
            narrowFixes.Add(delegate(int right)
            {
                int x = LeftInPageHost(g);
                if (x < 0 || g.IsDisposed) return false;
                bool shown = handNormal.Count > 0 && handNormal[0].Visible;
                int avail = right - x - S(14 + 12 + 4); // the card's own padding + margin; v5.0.2 (UI-C L15): 4 px slack
                if (shown && g.PreferredSize.Width > avail)
                {
                    hiddenW = 0;
                    foreach (Label l in handNormal) hiddenW = Math.Max(hiddenW, l.PreferredSize.Width + l.Margin.Horizontal);
                    foreach (Label l in handNormal) l.Visible = false;
                    return true;
                }
                if (!shown && hiddenW > 0 && g.PreferredSize.Width + hiddenW <= avail - S(16))
                {
                    foreach (Label l in handNormal) l.Visible = true;
                    return true;
                }
                return false;
            });
            Button all = Btn("全部恢复自动", delegate
            {
                if (Cur.AnyHandCustom() && !Ask("换手的所有格子都恢复成自动（正常手左右镜像）？")) return;
                Cur.AllHandAuto();
                RefreshValues();
                PaintHandRows();
                app.Changed();
            });
            TableLayoutPanel box = HeldGrid(1);
            Wide(box, Note("灰色＝自动：用正常手的数左右镜像（X 取反，A↔D、Q↔E 对调）。", 560));
            Wide(box, g);
            Wide(box, all);
            Wide(box, Note("换手不是绝对镜像，换手后偏移会有点变化。换手后把 W S A D 和侧身都实测一遍，对不上就直接改格子（改过的变白）；点「恢复自动」回到镜像。", 560));
            Section gb = Group("换手后的偏移", box);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)

            Label hsMs = Lbl("换手过渡时间");
            Tip(hsMs, "跟着当前配置存");
            TableLayoutPanel t = HeldGrid(3);
            Row(t, hsMs, Num(0, 2000, 10, 0, delegate { return Cur.HandSwitchMs; }, delegate(double v) { Cur.HandSwitchMs = v; }), Unit("ms"));
            Section gt = Group("换手过渡", t);
            yield return null; // v5.1.1 (X5): one background-build chunk ends here (PageChunks)
            AdvOnly(gt);
            TableLayoutPanel root = HeldStack(gb, gk, gt);
            ReleaseHeldOnShow(root); // v5.1.1 (P3): held through the first fill, one layout pass on the first show
            yield return root;
        }

        private void AddHandRow(TableLayoutPanel g, int item)
        {
            bool hasY = item != Profile.HandDrift;
            double lim = item == Profile.HandStand ? 200 : 500;
            OffsetField fx = null, fy = null;
            Button reset = null;
            Label normal = Unit("");
            normal.Font = monoFont;
            handNormal.Add(normal);
            Label auto = Unit("自动");
            Func<int, double> get = delegate(int axis)
            {
                double x, y;
                Cur.HandValue(item, out x, out y);
                return axis == 0 ? x : y;
            };
            Action paint = delegate
            {
                Profile p = Cur;
                if (p == null || fx == null || reset == null) return;
                bool custom = p.IsHandCustom(item);
                normal.Text = NormalHandText(p, item, G);
                Color fore = custom ? Theme.ColText : Theme.ColMuted;
                fx.Box.ForeColor = fore;
                if (fx.Arrow != null) fx.Arrow.ForeColor = fore;
                if (fy != null) { fy.Box.ForeColor = fore; if (fy.Arrow != null) fy.Arrow.ForeColor = fore; }
                auto.Visible = !custom;
                reset.Visible = custom;
            };
            // v4.1 百分比偏移. Setting one axis makes the row custom and keeps the other axis at its current (shown) px.
            fx = Offset(OffsetAxis.X, OffsetKind.Toggle, lim, delegate { return get(0); },
                delegate(double v) { Cur.SetHandCustom(item, v, hasY ? get(1) : 0); paint(); });
            if (hasY)
                fy = Offset(OffsetAxis.Y, OffsetKind.Toggle, lim, delegate { return get(1); },
                    delegate(double v) { Cur.SetHandCustom(item, get(0), v); paint(); });
            fx.Switched = true; // v5.1.1 (X5): 换手 values -> the FOV sides swap
            if (item < Profile.HandStand) fx.Origin = delegate { return OffsetUnits.StandOriginX(Cur, true); }; // W/S/A/D: from the 换手 站立 point; Q/E/到顶时水平偏移 from the centre (X8)
            if (fy != null) fy.Switched = true;
            reset = Btn("恢复自动", delegate
            {
                Cur.SetHandAuto(item);
                RefreshValues();
                PaintHandRows();
                app.Changed();
            });
            Tip(reset, "回到自动（正常手左右镜像）");
            refreshers.Add(paint);
            offsetRefreshers.Add(paint); // the 正常手 column is in % too
            handRefreshers.Add(delegate { RefreshOffset(fx); if (fy != null) RefreshOffset(fy); });
            handPainters.Add(paint);
            Label name = Lbl(Profile.HandItemNames[item]);
            if (item == Profile.HandDrift)
            {
                // 到顶时水平偏移 belongs to the recoil extras (folded on 后坐力): 高级设置 only, like there.
                // Short label: the long 「到顶时水平偏移（后坐力）」 made the whole table scroll sideways at 1280x720 / 150 %.
                name.Text = "后坐力水平偏移";
                Tip(name, "后坐力页的「到顶时水平偏移」");
                AdvRow(g, name, normal, fx.Line, Blank(), Line(auto, reset));
            }
            else
            {
                int lean = item == Profile.HandQ ? KeyBindings.ActionLeanLeft : item == Profile.HandE ? KeyBindings.ActionLeanRight : -1;
                if (lean >= 0) // the user may have rebound 侧身 (e.g. Ctrl+D): show the real key
                    refreshers.Add(delegate { name.Text = (lean == KeyBindings.ActionLeanLeft ? "左侧身（" : "右侧身（") + G.Keys.ComboOf(lean) + "）"; });
                Row(g, name, normal, fx.Line, hasY ? (Control)fy.Line : Blank(), Line(auto, reset));
            }
        }

        /// <summary>「← 4.17% / ↑ 1.39%」 of the normal hand for a 换手 item (到顶时水平偏移: just 左右).</summary>
        private static string NormalHandText(Profile p, int item, GlobalSettings g)
        {
            double x, y;
            switch (item)
            {
                case 0: case 1: case 2: case 3:
                    x = p.Dirs[item].OffX; y = p.Dirs[item].OffY;
                    return OffsetUnits.FormatAt(x, OffsetUnits.StandOriginX(p, false), OffsetAxis.X, false, g) + " / " + OffsetUnits.Format(y, OffsetAxis.Y, g);
                case Profile.HandStand: x = p.StandOffsetX; y = p.StandOffsetY; break;
                // v5.1.1 (X8): lean and recoil count from the screen centre (origin 0)
                case Profile.HandQ: x = p.LeanQX; y = p.LeanQY;
                    return OffsetUnits.FormatAt(x, 0, OffsetAxis.X, false, g) + " / " + OffsetUnits.Format(y, OffsetAxis.Y, g);
                case Profile.HandE: x = p.LeanEX; y = p.LeanEY;
                    return OffsetUnits.FormatAt(x, 0, OffsetAxis.X, false, g) + " / " + OffsetUnits.Format(y, OffsetAxis.Y, g);
                default: return OffsetUnits.FormatAt(p.RecoilDriftX, 0, OffsetAxis.X, false, g);
            }
            return OffsetUnits.Format(x, OffsetAxis.X, g) + " / " + OffsetUnits.Format(y, OffsetAxis.Y, g);
        }
    }
}
#endif
