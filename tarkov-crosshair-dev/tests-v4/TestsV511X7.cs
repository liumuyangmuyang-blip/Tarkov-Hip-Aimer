// v5.1.1 (X7, Leo 21 Nov): the 弹药提示 setup (开关, 弹匣容量, 射速, 全自动/半自动, 换弹键, 双击间隔, 两种换弹时间) moved from
// the 按键 page to the top of 外观 -> 弹药提示 (group 先设置, before the bar look / warnings / reload animation). One builder
// (SettingsForm.BuildAmmoBox) and the same settings fields; the 按键 page keeps only a pointer. tests-v4 has no WinForms,
// so this checks the source and the shipped texts.
using System;
using System.IO;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X7Init() { V511X7Tests(); }

        static void V511X7Tests()
        {
            string d = "";
            string pk = X1Src("SettingsForm.PageKeys.cs"), pf = X1Src("SettingsForm.PageFeedback.cs"), sp = X1Src("StylePanel.cs");
            // 按键 page: no ammo group any more, only the pointer
            if (pk.Contains("Group(\"弹药提示\", BuildAmmoBox())")) d += "按键 still has the ammo group; ";
            if (!pk.Contains("弹药提示的设置在「外观」→「弹药提示」")) d += "no pointer on 按键; ";
            string keysPage = X6Body(pk, "private IEnumerable<Control>");
            if (keysPage.Contains("BuildAmmoBox(")) d += "按键 page builds the ammo box; ";
            // the one builder keeps every field and the 常调 sync
            string box = X6Body(pk, "private Control BuildAmmoBox(");
            foreach (string w in new string[] { "G.AmmoOn", "G.AmmoMagSize", "G.AmmoRpm", "G.AmmoSemi", "G.AmmoReloadKey", "G.AmmoQuickTapMs",
                "ReloadSecBox(false)", "ReloadSecBox(true)", "\"弹匣容量\"", "\"射速\"", "\"换弹键\"", "\"连按两下判定\"", "\"换弹时间\"", "\"快速换弹时间\"" })
                if (!box.Contains(w)) d += "box lost " + w + "; ";
            if (!X6Body(pk, "private NumericUpDown ReloadSecBox(").Contains("RefreshReloadBoxes(quick)")) d += "reload boxes not synced; ";
            // 外观 host hands it to StylePanel; StylePanel shows it first
            string bs = X6Body(pf, "private Control BuildStyle(");
            if (!bs.Contains("BuildAmmoBox()") || !bs.Contains("sp.SetAmmoSetup(")) d += "BuildStyle does not hand the setup over; ";
            string ba = X6Body(sp, "private Control BuildAmmo(");
            int first = ba.IndexOf("Head(t, \"先设置\")", StringComparison.Ordinal), host = ba.IndexOf("AddWide(t, ammoSetupHost)", StringComparison.Ordinal);
            int[] later = { ba.IndexOf("Head(t, \"样式预设\")", StringComparison.Ordinal), ba.IndexOf("Head(t, \"条的样子\")", StringComparison.Ordinal),
                ba.IndexOf("BuildWarn(t, \"低弹警告\"", StringComparison.Ordinal), ba.IndexOf("Head(t, \"换弹动画\")", StringComparison.Ordinal) };
            if (first < 0 || host < first) d += "先设置 group missing; ";
            foreach (int l in later) if (l < 0 || l < host) d += "先设置 not before the look groups; ";
            if (ba.Contains("\"显示弹药提示\"")) d += "second 显示弹药提示 switch on 外观; ";
            if (!X6Body(sp, "public void SetAmmoSetup(").Contains("ammoSetupHost.Controls.Add(")) d += "SetAmmoSetup; ";
            // texts point to the new place
            string guide = X1Src("GuideText.cs"), faq = X1Src("FaqText.cs"), tut = X1Src("TutorialStepsV51.cs");
            string readme = File.ReadAllText("/home/dev/tarkov-crosshair-v4/README.txt");
            foreach (string t in new string[] { guide, readme })
            {
                if (t.Contains("在「按键」页勾上「显示弹药提示")) d += "guide/README says 按键 page; ";
                if (!t.Contains("「外观」→「弹药提示」，最上面一组「先设置」")) d += "guide/README lacks the new place; ";
            }
            if (!faq.Contains("「外观」→「弹药提示」的「先设置」")) d += "FAQ; ";
            if (!tut.Contains("「外观」→「弹药提示」最上面的「先设置」")) d += "tutorial; ";
            V511X7Pin(sp);
            Check("V511-X7-1 弹药提示 setup: first group 「先设置」 on 外观 -> 弹药提示 (before preset / bar / warnings / reload anim), one builder with every field + 常调 sync, 按键 page only a pointer, guide / README / FAQ / tutorial say where",
                d.Length == 0, d.Length == 0 ? "ok" : d);
        }

        // V511-X7-2 (Leo 21 Nov): the live preview stays pinned at the top of 外观 (all 3 sub-pages) while the settings scroll
        static void V511X7Pin(string sp)
        {
            string d = "";
            string anim = X6Body(sp, "private Control BuildAnim("), ammo = X6Body(sp, "private Control BuildAmmo(");
            if (!anim.Contains("animPin = PinBox(live, pb)") || anim.Contains("AddWide(t, live)") || anim.Contains("AddWide(t, pb)")) d += "准星动态 preview not pinned; ";
            if (!ammo.Contains("ammoPin = PinBox(live, pb)") || ammo.Contains("AddWide(t, live)") || ammo.Contains("AddWide(t, pb)")) d += "弹药提示 preview not pinned; ";
            foreach (string b in new string[] { "预览普通换弹", "预览快速换弹", "预览低弹警告", "预览紧急警告" })
                if (ammo.IndexOf(b, StringComparison.Ordinal) > ammo.IndexOf("ammoPin = PinBox", StringComparison.Ordinal)) d += b + " outside the pinned block; ";
            string pin = X6Body(sp, "private void PlacePinned(");
            if (!pin.Contains("-page.Top") || !pin.Contains("pinBack.Bounds") || !pin.Contains("subTabs.Top") || !pin.Contains("pinY + scrolled")) d += "PlacePinned does not follow the scroll; ";
            if (!X6Body(sp, "private void PlaceRight(").Contains("PlacePinned();")) d += "scroll does not re-pin; ";
            string order = X6Body(sp, "private void PinOrder(");
            int back = order.IndexOf("pinBack.BringToFront()", StringComparison.Ordinal), pv = order.IndexOf("preview.BringToFront()", StringComparison.Ordinal);
            if (back < 0 || pv < back) d += "backing not under the preview; ";
            string h = X6Body(sp, "private int PinPreviewH(");
            if (!h.Contains("0.30") || !h.Contains("S(190)") || !h.Contains("S(90)")) d += "preview height not capped; ";
            // the cap at 1280x720 / 200 %: visible height ~ 560 px -> 168 px (30 %), never above 380 px (190 logical x 2)
            int view = 560, cap = Math.Max(180, Math.Min(380, (int)(view * 0.30)));
            if (cap > view / 2) d += "cap eats half the page; ";
            Check("V511-X7-2 外观 live preview pinned at the top (准星样式 / 准星动态 / 弹药提示 incl. reload preview buttons); settings scroll under it; height <= 30 % of the view, 90..190 px scaled",
                d.Length == 0, d.Length == 0 ? "ok" : d);
        }
    }
}
