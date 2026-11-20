// v5.1.1 (X6): button height comes from the measured text height (Leo's 滤镜 photo: every button cut in half on Windows);
// notes / the curve readout wrap inside their card. tests-v4 has no WinForms, so this checks the shared helpers' source.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51X6Init() { V51X6Tests(); }

        static string X6Body(string src, string sig)
        {
            int i = src.IndexOf(sig, StringComparison.Ordinal);
            if (i < 0) return "";
            int b = src.IndexOf('{', i), depth = 0;
            for (int j = b; j < src.Length; j++)
            {
                if (src[j] == '{') depth++;
                else if (src[j] == '}' && --depth == 0) return src.Substring(i, j - i + 1);
            }
            return "";
        }

        static void V51X6Tests()
        {
            // X6-1 the shared button helpers: height = measured text (TextRenderer) + padding + borders + >= 4 px slack, applied
            // to every themed Button and never capped by the number-box line height; no fixed button height in the helpers
            {
                string d = "";
                string th = X1Src("Theme.cs"), sf = X1Src("SettingsForm.cs"), sp = X1Src("StylePanel.cs");
                string need = X6Body(th, "internal static int ButtonNeedH(");
                if (!(need.Contains("TextRenderer.MeasureText") || need.Contains("TextWidthCache.SingleLineHeight(")) || !need.Contains("Padding.Vertical") || !Regex.IsMatch(need, @"\+ 4;")) d += "ButtonNeedH not measured + padding + 4; ";
                string fit = X6Body(th, "internal static void FitButtonHeight(");
                if (!fit.Contains("MinimumSize") || !fit.Contains("MaximumSize.Height < need")) d += "FitButtonHeight does not lift min/max; ";
                string sb = X6Body(th, "private static void StyleButton(");
                if (!sb.Contains("FitButtonHeight(b)") || !sb.Contains("TextChanged") || !sb.Contains("FontChanged")) d += "StyleButton does not fit / re-fit; ";
                string fch = X6Body(sf, "private void FitControlHeights(");
                if (!fch.Contains("Theme.ButtonNeedH")) d += "FitControlHeights caps buttons to the line height; ";
                string btn = X6Body(sf, "private Button Btn(") + X6Body(sp, "private Button SmallButton(");
                if (btn.Length == 0 || Regex.IsMatch(btn, @"\.(Height|Size)\s*=")) d += "fixed height in Btn / SmallButton; ";
                // X6-2 wrapping notes stop at the nearest fixed-width parent (card), not only at the page edge
                string fw = X6Body(sf, "private void FitWrapping(");
                if (!fw.Contains("ClipRight(kv.Key)")) d += "FitWrapping ignores the card edge; ";
                if (!X6Body(sf, "private int ClipRight(").Contains("Padding.Right")) d += "ClipRight without padding; ";
                // arithmetic of the rule at 100 / 150 / 200 %: YaHei 9 pt line ~ 17 / 25 / 33 px
                foreach (int text in new[] { 17, 25, 33 })
                {
                    int h = text + 2 /*padding 1+1*/ + 2 /*border*/ + 2 + 4;
                    if (h < text + 4) d += "rule < text + 4 at " + text + "; ";
                }
                Check("X6-1 button height from measured text (+ padding + borders + 4 px) in Theme.StyleButton / FitControlHeights, no fixed height in Btn / SmallButton; notes wrap at the card edge (ClipRight)", d.Length == 0, d);
            }
        }
    }
}
