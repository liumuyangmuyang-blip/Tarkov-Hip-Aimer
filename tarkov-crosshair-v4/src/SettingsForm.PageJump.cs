using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 跳跃 page (PAGE-C). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>跳跃: ☑ enable, 跳跃键 summary, 总时长, a read-only path plot; 高级设置: 3 phase rows + 自动分配.</summary>
        private Control BuildJump()
        {
            Func<JumpSettings> J = delegate { return Cur.Jump; };
            JumpPlot plot = new JumpPlot();
            plot.Jump = J;
            plot.ValueText = delegate(double v) { return OffsetUnits.Format(v, OffsetAxis.Y, G); }; // v4.1 百分比偏移
            offsetRefreshers.Add(plot.Invalidate);
            plot.Size = new Size(F(420), F(130)); // v4.3.1: one fixed font, sized once (no font-scale fixup)
            fitWidth.Add(new KeyValuePair<Control, int>(plot, 420)); // narrower on a narrow page (no sideways scrolling)
            plot.Margin = P(3, 8, 3, 3);
            refreshers.Add(plot.Invalidate);
            List<Control> parts = new List<Control>();

            NumericUpDown total = Num(JumpSettings.MinTotalMs, JumpSettings.MaxTotalMs, 10, 0, delegate { return J().TotalMs; },
                delegate(double v) { JumpSplit.SetTotal(J(), (int)Math.Round(v)); RefreshValues(); });
            Button auto = Btn("自动分配", delegate { JumpSplit.Auto(J()); RefreshValues(); app.Changed(); });
            Tip(total, "三段加起来永远等于总时长，改总时长三段按比例一起变");
            Tip(auto, "三段时长按 30% / 40% / 30% 分");
            parts.Add(total); parts.Add(auto);

            TableLayoutPanel g = Grid(4);
            Label he = Lbl("结束位置（↑ 上 / ↓ 下）");
            Tip(he, "百分比：屏幕中心到上边缘 = 100%。点箭头换方向");
            Cells(g, Lbl("阶段"), Lbl("时长 ms"), he, Lbl("曲线"));
            for (int i = 0; i < JumpSettings.PhaseCount; i++)
            {
                int idx = i;
                Label phase = Lbl((i + 1) + ". " + JumpSettings.PhaseNames[i]);
                Control endCell;
                NumericUpDown ms = Num(0, JumpSettings.MaxTotalMs, 10, 0, delegate { return J().Phases[idx].Ms; },
                    delegate(double v) { JumpSplit.SetPhase(J(), idx, (int)Math.Round(v)); RefreshValues(); });
                parts.Add(ms);
                if (i < JumpSettings.PhaseCount - 1)
                {
                    OffsetField end = Offset(OffsetAxis.Y, OffsetKind.Toggle, JumpSettings.LimitPx, delegate { return J().Phases[idx].EndPx; },
                        delegate(double v) { J().Phases[idx].EndPx = v; plot.Invalidate(); });
                    endCell = end.Line;
                    parts.Add(end.Line);
                }
                else endCell = Unit("0（固定）");
                ComboBox cv = Choice(JumpSettings.CurveLabels, JumpSettings.Curves, delegate { return J().Phases[idx].Curve; },
                    delegate(string v) { J().Phases[idx].Curve = v; plot.Invalidate(); });
                Cells(g, phase, ms, endCell, cv); // one row per phase at fixed cells (v4.3.1)
                parts.Add(cv);
            }

            CheckBox on = Check("跳跃时准星跟着动", delegate { return J().Enabled; },
                delegate(bool v) { J().Enabled = v; foreach (Control c in parts) c.Enabled = v; });
            refreshers.Add(delegate { foreach (Control c in parts) c.Enabled = J().Enabled; });
            Label key = StatusLbl();
            refreshers.Add(delegate { key.Text = G.Keys.ComboOf(KeyBindings.ActionJump); });

            TableLayoutPanel box = Grid(1);
            Wide(box, on);
            Wide(box, Line(Lbl("跳跃键"), key, KeysPageLink("去「按键」页改 →")));
            Wide(box, Note("按一下跳跃键，准星按下面这条路线走一遍（跳的过程中再按不算）。", 520));
            Wide(box, Line(Lbl("总时长"), total, Unit("ms"), AdvOnly(auto)));
            Wide(box, plot);
            Wide(box, Hint("竖着看：准星上下位置（百分比）；横着看：时间。", 520));
            Wide(box, Note("默认就是开发者调好的路线，一般不用改；要改的话，一般只改总时长。", 520));
            Wide(box, AdvOnly(g)); // per-phase table: 高级设置 only
            Wide(box, AdvOnly(Note("三段加起来永远等于总时长：改总时长，三段按比例一起变；「自动分配」＝ 30% / 40% / 30%。结束位置：↑ 往上，↓ 往下。", 520)));
            return Stack(Group("跳跃", box));
        }
    }
}
#endif
