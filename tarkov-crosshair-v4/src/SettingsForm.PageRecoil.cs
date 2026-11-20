using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 后坐力 page (PAGE-B). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>后坐力: only 上抬偏移量 up front (+ the 50–100 note); the rest under 「可选 / 高级」.</summary>
        private Control BuildRecoil()
        {
            // Always visible: the one number a player needs + the developer's note.
            TableLayoutPanel g = Grid(2);
            Label rise = Lbl("上抬偏移量");
            Tip(rise, "按住开火键时，准星最多往上抬多少（百分比：屏幕中心到上边缘 = 100%）");
            rise.ForeColor = Theme.ColAccent; // UI41-review #3: the one value people tune here stands out
            Row(g, rise, Offset(OffsetAxis.Y, OffsetKind.AlwaysPositive, 1000, delegate { return Cur.RecoilMaxPx; }, delegate(double v) { Cur.RecoilMaxPx = v; }).Line);
            TableLayoutPanel box = Grid(1);
            box.Controls.Add(g);
            box.Controls.Add(Note("按住开火键连射时，准星最多往上抬多少。填 0 就不抬；开镜时不算。", 480));
            box.Controls.Add(Note("开发者实测：后坐力 50–100 的枪用同一套数值就够，近距离差别不大。想每把枪单独调也可以：顶部「管理 ▾」→ 新建一套配置。", 480));
            Section gb = Group("后坐力（按住开火键）", box);

            // 高级设置 off: a visible pointer instead of the extra fields.
            Label hint = Note("到顶时间、回落时间等更多参数：打开顶部「高级设置」。一般不用改。", 480);
            refreshers.Add(delegate { hint.Visible = !Advanced; });

            // 高级设置 on: the rest, folded; each row says what it does (no hidden tooltips).
            // v4.1: offsets in 百分比偏移 ([arrow] + % ); time rows get an arrow-wide spacer so every number box lines up
            TableLayoutPanel a = Grid(3);
            List<Control> advNums = new List<Control>();
            NumericUpDown rise1 = Num(10, 5000, 10, 0, delegate { return Cur.RecoilRiseMs; }, delegate(double v) { Cur.RecoilRiseMs = v; });
            NumericUpDown fall = Num(10, 5000, 10, 0, delegate { return Cur.RecoilFallMs; }, delegate(double v) { Cur.RecoilFallMs = v; });
            NumericUpDown delay = Num(0, 300, 10, 0, delegate { return Cur.RecoilDelayMs; }, delegate(double v) { Cur.RecoilDelayMs = v; });
            OffsetField kick = Offset(OffsetAxis.Y, OffsetKind.AlwaysPositive, 300, delegate { return Cur.RecoilClickKickPx; }, delegate(double v) { Cur.RecoilClickKickPx = v; });
            OffsetField drift = Offset(OffsetAxis.X, OffsetKind.Toggle, 500, delegate { return Cur.RecoilDriftX; }, delegate(double v) { Cur.RecoilDriftX = v; });
            // v5.1.1 (X8, Leo 21 Nov option A): recoil counts from the screen centre, so no 站立 origin (Origin null = 0)
            advNums.Add(rise1); advNums.Add(fall); advNums.Add(delay); advNums.Add(kick.Box); advNums.Add(drift.Box);
            Row(a, "到顶时间", Line(ArrowSpacer(), rise1, Unit("ms")), Hint("按住后大约多久抬到最高", 260));
            Row(a, "每次按下额外上跳", kick.Line, Hint("每按一下开火多跳一点（点射）", 260));
            Row(a, "松开回落时间", Line(ArrowSpacer(), fall, Unit("ms")), Hint("松开后大约多久回到原位", 260));
            Row(a, "开枪后多久开始上跳", Line(ArrowSpacer(), delay, Unit("ms")), Hint("第一发在上跳前就已出膛", 260));
            Row(a, "到顶时水平偏移", drift.Line, Hint("一般填 0", 260));
            // one box width for the whole column (v4.3.1: fixed font, so measured once here)
            int boxW = 0;
            foreach (Control c in advNums) boxW = Math.Max(boxW, c.Width);
            foreach (Control c in advNums) c.Width = boxW;
            return Stack(gb, hint, AdvOnly(Fold("可选：更多后坐力参数", a)));
        }
    }
}
#endif
