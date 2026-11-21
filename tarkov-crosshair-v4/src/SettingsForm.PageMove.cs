using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// 移动 page + optional curve editor (PAGE-B). Part of the settings window: partial class SettingsForm (shell, shared helpers, 高级设置 switch: SettingsForm.cs).
// Page rule: use the shell helpers (Grid / Line / Stack / Row / AdvRow / AdvOnly / Lbl / Note / Unit / Num / Choice / Check /
// Btn / Group / Fold / FixUp / F / S / P / Tip / EnableWhen) and read `Advanced`; never change another page's file.

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    internal sealed partial class SettingsForm
    {
        /// <summary>移动: W/S/A/D offsets; the curve editor sits under 「可选：移动曲线（高级）」 with 推荐 marked.</summary>
        private Control BuildMovement()
        {
            // v4.1 百分比偏移: [arrow] + positive % per axis (storage stays px; OffsetUnits / ViewScale do the conversion)
            TableLayoutPanel g = HeldGrid(3);
            Cells(g, Blank(), Lbl("左右"), Lbl("上下"));
            string[] names = { "W 前", "S 后", "A 左", "D 右" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                OffsetField fx = Offset(OffsetAxis.X, OffsetKind.Toggle, 500 + Profile.StandLimitPx, delegate { return Cur.Dirs[idx].OffX; }, delegate(double v) { Cur.Dirs[idx].OffX = v; Profile.RecomputeDiagonals(Cur); });
                fx.Origin = delegate { return OffsetUnits.StandOriginX(Cur, false); }; // v5.1.1 (X5): FOV side of the final X
                Row(g, names[i],
                    fx.Line,
                    Offset(OffsetAxis.Y, OffsetKind.Toggle, 500 + Profile.StandLimitPx, delegate { return Cur.Dirs[idx].OffY; }, delegate(double v) { Cur.Dirs[idx].OffY = v; Profile.RecomputeDiagonals(Cur); }).Line);
            }
            TableLayoutPanel dirBox = HeldGrid(1);
            Wide(dirBox, g);
            Wide(dirBox, Note("按住这个方向走路时，准星往箭头方向移多少。A / D 调左右，W / S 调上下，另一格一般不用动。", 480));
            Wide(dirBox, Note("单位是百分比：屏幕中心到上边缘 = 100%，换分辨率不用重调。点箭头换方向。", 480));
            Wide(dirBox, Note("斜向（WA / WD / SA / SD）自动算好；侧身时再加上侧身的偏移。", 480));
            Section gDir = Group("移动时准星偏移", dirBox);
            Section gStand = Group("站立静止偏移（可选）", BuildStandBox());

            // 高级设置 off: say where the curve is instead of showing it.
            Label hint = Note("移动快慢（可选）在顶部「高级设置」里；默认就是推荐方案。", 480);
            refreshers.Add(delegate { hint.Visible = !Advanced; });

            // 移动速率曲线 (optional)
            Label state = TagLabel("", Theme.ColAccent);
            state.Font = Font; // v5.0.2 (UI-C L5): Chinese words in the UI font, not the mono number font
            Action stateOn = delegate
            {
                bool rec = Cur.CurveAD.IsRecommended && Cur.CurveWS.IsRecommended;
                state.Text = rec ? "● 当前为推荐" : "○ 已自定义";
                state.ForeColor = rec ? Theme.ColAccent : Theme.ColMuted;
            };
            refreshers.Add(stateOn);
            // v5.0.2 (UI-C H1, Leo): 推荐 = the curve of the built-in 「Leo默认」 (Profile.RecommendedCurveMs / RecommendedSpeeds)
            Button recommend = AccentBtn("恢复推荐（" + Profile.RecommendedCurveMs + " ms）", delegate
            {
                Cur.ResetCurvesToRecommended();
                RefreshValues();
                app.Changed();
            });
            Tip(recommend, "Leo 的方案：前后、左右都用 " + Profile.RecommendedCurveMs + " ms（和内置「Leo默认」一样）");
            TableLayoutPanel curves = HeldGrid(2);
            Control blockAD = CurveBlock("左右 A/D", delegate { return Cur.CurveAD; }, stateOn);
            Control blockWS = CurveBlock("前后 W/S", delegate { return Cur.CurveWS; }, stateOn);
            curves.Controls.Add(blockAD, 0, 0);
            curves.Controls.Add(blockWS, 1, 0);
            // Two curves side by side when the page is wide enough, else one under the other (no sideways scrolling).
            Action placeCurves = delegate
            {
                if (pageHost == null) return;
                int need = blockAD.PreferredSize.Width + blockAD.Margin.Horizontal + blockWS.PreferredSize.Width + blockWS.Margin.Horizontal
                    + S(22 + 10 + 40 + 14 + 30) + SystemInformation.VerticalScrollBarWidth; // page + section + fold indent + slack
                bool side = pageHost.ClientSize.Width >= need;
                TableLayoutPanelCellPosition want = side ? new TableLayoutPanelCellPosition(1, 0) : new TableLayoutPanelCellPosition(0, 1);
                TableLayoutPanelCellPosition now = curves.GetPositionFromControl(blockWS);
                if (now.Column != want.Column || now.Row != want.Row)
                {
                    curves.SetCellPosition(blockWS, want);
                    PostRefit(); // the card got wider / narrower: every card of the page follows (one right edge)
                }
            };
            if (pageHost != null) pageHost.Resize += delegate { placeCurves(); };
            placeCurves();
            Button copy = Btn("把左右的总时长复制到前后", delegate
            {
                if (!Ask("把前后 W/S 的总时长改成和左右 A/D 一样？")) return;
                Cur.CurveWS = Cur.CurveAD.Clone();
                RefreshValues();
                app.Changed();
            });
            TableLayoutPanel curveBox = HeldGrid(1);
            Wide(curveBox, Line(recommend, state));
            Wide(curveBox, curves);
            Wide(curveBox, copy);
            Wide(curveBox, Note("推荐＝Leo 的方案，一般不用改。准星先慢后快地移过去，总时长到了刚好到位。按下、松开、换方向都用这条曲线；短按只移一点就回来，A 换 D 时从当前位置先减速再反向加速。", 560));
            Section gCurve = Group("移动快慢", curveBox);
            Section gTap = Group("短按和换向", BuildTapBox());
            TableLayoutPanel root = HeldStack(gStand, gDir, hint, AdvOnly(Fold("可选：移动快慢（推荐＝Leo 的方案）", HeldStack(gCurve, gTap))));
            ReleaseHeldOnShow(root); // v5.1.1 (P3): held through the first fill, one layout pass on the first show
            return root;
        }

        // ---------------- v5.1 R4 (Leo round 2): 短按 / 换向 tunables ----------------

        private Control BuildTapBox()
        {
            // X13 (Leo 21 Nov 2026): two columns, 左右（A/D）and 前后（W/S）; W/S defaults are a bit faster.
            TableLayoutPanel t = HeldGrid(4);
            Row(t, Lbl(""), Unit("左右（A/D）"), Unit("前后（W/S）"), Unit(""));
            Row(t, Line(Lbl("短按阈值"), HelpMark.For("tap", Font, delegate { return G; })),
                Line(Num(GlobalSettings.TapThresholdMsMin, GlobalSettings.TapThresholdMsMax, 10, 0, delegate { return G.TapThresholdMs; }, delegate(double v) { G.TapThresholdMs = v; }), Unit("ms")),
                Line(Num(GlobalSettings.TapThresholdMsMin, GlobalSettings.TapThresholdMsMax, 10, 0, delegate { return G.WsTapThresholdMs; }, delegate(double v) { G.WsTapThresholdMs = v; }), Unit("ms")),
                Hint("按住比这短算短按（默认 A/D 200、W/S 160）", 260));
            Row(t, Line(Lbl("换向最短时间"), HelpMark.For("taps", Font, delegate { return G; })),
                Line(Num(GlobalSettings.ReverseMinMsMin, GlobalSettings.ReverseMinMsMax, 10, 0, delegate { return G.ReverseMinMs; }, delegate(double v) { G.ReverseMinMs = v; }), Unit("ms")),
                Line(Num(GlobalSettings.ReverseMinMsMin, GlobalSettings.ReverseMinMsMax, 10, 0, delegate { return G.WsReverseMinMs; }, delegate(double v) { G.WsReverseMinMs = v; }), Unit("ms")),
                Hint("A 换 D、W 换 S：先回去再出去，最少这么久（默认 A/D 250、W/S 180）", 260));
            Row(t, Lbl("回程时间"),
                Line(Num(GlobalSettings.ReturnTimePctMin, GlobalSettings.ReturnTimePctMax, 5, 0, delegate { return G.ReturnTimePct; }, delegate(double v) { G.ReturnTimePct = v; }), Unit("%")),
                Line(Num(GlobalSettings.ReturnTimePctMin, GlobalSettings.ReturnTimePctMax, 5, 0, delegate { return G.WsReturnTimePct; }, delegate(double v) { G.WsReturnTimePct = v; }), Unit("%")),
                Hint("回去用出去时间的多少（默认 A/D 100、W/S 80）", 260));
            Row(t, Lbl("换向停顿"),
                Line(Num(GlobalSettings.ReversePauseMsMin, GlobalSettings.ReversePauseMsMax, 10, 0, delegate { return G.ReversePauseMs; }, delegate(double v) { G.ReversePauseMs = v; }), Unit("ms")),
                Line(Num(GlobalSettings.ReversePauseMsMin, GlobalSettings.ReversePauseMsMax, 10, 0, delegate { return G.WsReversePauseMs; }, delegate(double v) { G.WsReversePauseMs = v; }), Unit("ms")),
                Hint("换向时在中间停多久（默认 0）", 260));
            Button def = Btn("恢复默认", delegate
            {
                GlobalSettings d = new GlobalSettings();
                G.TapThresholdMs = d.TapThresholdMs; G.ReverseMinMs = d.ReverseMinMs; G.ReturnTimePct = d.ReturnTimePct; G.ReversePauseMs = d.ReversePauseMs;
                G.WsTapThresholdMs = d.WsTapThresholdMs; G.WsReverseMinMs = d.WsReverseMinMs; G.WsReturnTimePct = d.WsReturnTimePct; G.WsReversePauseMs = d.WsReversePauseMs;
                RefreshValues();
                app.Changed();
            });
            TableLayoutPanel box = HeldGrid(1);
            Wide(box, t);
            Wide(box, def);
            Wide(box, Note("改完马上生效，可以直接试：开着首页的面板（或进游戏），轻点一下 A 或 W，准星只动一点就回来；按住 A 再换 D（或 W 换 S），看换向快不快。W/S 默认比 A/D 快一点。", 480));
            return box;
        }

        // ---------------- v5.1 站立静止偏移 (decision 5) ----------------

        /// <summary>The standing point as the new zero. Changing it from 0 asks first (StandConfirmText), then converts every
        /// direction (Profile.SetStandPoint) so they stay where they are on the screen.</summary>
        public const string StandConfirmText = "这会把零点从屏幕中心改到站立点：站着不动时准星停在这里，开关准星连按两下也回到这里。其他方向的数值会自动换算，在屏幕上的位置不变。继续吗？";

        private Control BuildStandBox()
        {
            TableLayoutPanel t = HeldGrid(3);
            Cells(t, Blank(), Lbl("左右"), Lbl("上下"));
            Row(t, Line(Lbl("站立点"), HelpMark.For("stand", Font, delegate { return G; })),
                Offset(OffsetAxis.X, OffsetKind.Toggle, Profile.StandLimitPx, delegate { return Cur.StandOffsetX; }, delegate(double v) { SetStand(v, Cur.StandOffsetY); }).Line,
                Offset(OffsetAxis.Y, OffsetKind.Toggle, Profile.StandLimitPx, delegate { return Cur.StandOffsetY; }, delegate(double v) { SetStand(Cur.StandOffsetX, v); }).Line);
            Button zero = Btn("改回屏幕中心", delegate
            {
                if (!Cur.StandAsZero) return;
                Cur.SetStandPoint(0, 0);
                RefreshValues();
                app.Changed();
            });
            TableLayoutPanel box = HeldGrid(1);
            Wide(box, t);
            Wide(box, zero);
            Wide(box, Note("0 = 站着不动时准星在屏幕中心（内置「Leo默认」是 → 3.40%  ↓ 3.50%）。填了以后，站立点就是新的零点，上面各方向的数值从站立点算起，会自动换算，屏幕上的位置不变。侧身照旧从屏幕中心算起；后坐力从准星当前的位置往上抬（站着就是从站立点往上）。", 480));
            return box;
        }

        private void SetStand(double x, double y)
        {
            if (!Cur.StandAsZero && (x != 0 || y != 0) && !Ask(StandConfirmText))
            {
                BeginInvoke((Action)RefreshValues); // 取消: the box goes back to 0
                return;
            }
            Cur.SetStandPoint(x, y);
            app.Changed();
            BeginInvoke((Action)RefreshValues); // W/S/A/D were converted
        }

        /// <summary>One 移动速率曲线 block (v5.1, decision 7): the axis and its 总时长 only. The curve shape is always the 推荐 slow
        /// start (Profile.RecommendedSpeeds); no presets, no point editing.</summary>
        private Control CurveBlock(string title, Func<MoveCurve> get, Action changed)
        {
            TableLayoutPanel b = HeldGrid(1);
            b.Margin = P(0, 0, 16, 0);
            Wide(b, Line(Lbl(title), Lbl("总时长"),
                Num(0, 2000, 10, 0, delegate { return get().DurationMs; }, delegate(double v) { get().DurationMs = v; changed(); }),
                Unit("ms")));
            CurveEditor ed = new CurveEditor(); // a picture of the shape only (not editable)
            ed.Curve = get;
            ed.Enabled = false;
            ed.Size = new Size(F(300), F(120));
            ed.Margin = P(3, 4, 3, 4);
            Wide(b, ed);
            refreshers.Add(ed.Invalidate);
            return b;
        }

        // ---------------- v5.1.1 (P3): page grids built with their layout held ----------------
        // Leo's PC (21 Nov): 按键 2415 ms, 换手 2095 ms, 常规 1674 ms, 反馈 1229 ms to build. Every Controls.Add into an
        // auto-size grid re-ran that grid's layout (and measured every label in it) while the page was still detached, with
        // the default font. The grids of the P3 pages (按键 / 常规 / 移动 / 侧身 / 换手 / 反馈) are now held (SuspendLayout)
        // while they are filled, stay held through the first fill (theme, heights, values, 高级设置 rows, TextFit) and are let
        // go on the page's first show, deepest first, one layout pass each (ReleaseHeldOnShow). Same final layout (Mono
        // bounds dump of every visible control, 1280x720 @100 % and 2560x1440 @180 %, identical).

        private List<Control> heldLayouts;

        /// <summary>Grid(cols) with its layout held until the page first shows (ReleaseHeldOnShow).</summary>
        private TableLayoutPanel HeldGrid(int cols)
        {
            TableLayoutPanel t = Grid(cols);
            Hold(t);
            return t;
        }

        private void Hold(Control c)
        {
            c.SuspendLayout();
            if (heldLayouts == null) heldLayouts = new List<Control>();
            heldLayouts.Add(c);
        }

        /// <summary>Stack(items) built with its layout held (same anchors and order as Stack).</summary>
        private TableLayoutPanel HeldStack(params Control[] items)
        {
            TableLayoutPanel t = Stack();
            Hold(t);
            foreach (Control c in items)
            {
                c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                t.Controls.Add(c);
            }
            return t;
        }

        /// <summary>v5.1.1 (P3, round 2): the held grids of this page stay held through the whole first fill (attach + theme,
        /// FitControlHeights, values, 高级设置 rows, TextFit: on Linux / Mono those were 60-80 % of a page's first open,
        /// each size change re-running the layout of every grid above it) and are let go on the page's first show, deepest
        /// first, each with one layout pass. Nothing reads a position on a page before it shows (the fit runs on show).</summary>
        private void ReleaseHeldOnShow(Control root)
        {
            List<Control> l = heldLayouts;
            heldLayouts = null;
            if (l == null || root == null) return;
            EventHandler h = null;
            h = delegate
            {
                if (!root.Visible) return; // Visible = really shown (every parent visible)
                root.VisibleChanged -= h;
                ReleaseNow(l);
            };
            root.VisibleChanged += h;
            root.Disposed += delegate { root.VisibleChanged -= h; };
        }

        /// <summary>Deepest grid first: its size is final before the grid around it lays out (one pass per grid).</summary>
        private static void ReleaseNow(List<Control> l)
        {
            List<KeyValuePair<int, Control>> byDepth = new List<KeyValuePair<int, Control>>(l.Count);
            foreach (Control c in l)
            {
                int d = 0;
                for (Control p = c.Parent; p != null; p = p.Parent) d++;
                byDepth.Add(new KeyValuePair<int, Control>(d, c));
            }
            byDepth.Sort(delegate(KeyValuePair<int, Control> a, KeyValuePair<int, Control> b) { return b.Key.CompareTo(a.Key); });
            foreach (KeyValuePair<int, Control> kv in byDepth)
            {
                try { if (!kv.Value.IsDisposed) kv.Value.ResumeLayout(true); } catch (Exception) { }
            }
        }

        /// <summary>A text width measured once per (text, font) (TextWidthCache), for the key-box minimum widths.</summary>
        private static int TextW(string text, Font font) { return TextWidthCache.Width(text, font); }
    }
}
#endif
