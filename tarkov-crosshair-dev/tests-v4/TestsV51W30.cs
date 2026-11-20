// v5.1 (W30): decision 9 (text always fully visible: real metrics + margin, YaHei floor, no Chinese in mono, number boxes
// at 200 %, runtime check at 100/150/200 %) and decision 14 (flat style, one radius, theme colours only; hacker look only on
// the dashboard). Pure TextFitMath + Theme tokens, plus source checks of Theme.cs / TextFit.cs.
using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W30Init() { V51W30Tests(); }

        static string W30Src(string file)
        {
            return File.ReadAllText(Path.Combine("/home/dev/tarkov-crosshair-v4/src", file));
        }

        /// <summary>Text of a class body in a source file: from "class Name" to the next top-level "internal sealed class" / end.</summary>
        static string W30Class(string src, string name)
        {
            int i = src.IndexOf("class " + name, StringComparison.Ordinal);
            if (i < 0) return "";
            int j = src.IndexOf("\n    internal sealed class ", i + 10, StringComparison.Ordinal);
            int k = src.IndexOf("\n    private sealed class ", i + 10, StringComparison.Ordinal);
            int end = src.Length;
            if (j > 0) end = Math.Min(end, j);
            if (k > 0) end = Math.Min(end, k);
            return src.Substring(i, end - i);
        }

        static void V51W30Tests()
        {
            // ---------------------------------------------------------------- decision 9: pure rules
            Check("W30-1 CJK detection: 按键 / 。/ ＡＢ / かな are CJK; ASCII, 'é', '▸' are not",
                TextFitMath.HasCjk("按键") && TextFitMath.HasCjk("ok。") && TextFitMath.HasCjk("ＡＢ") && TextFitMath.HasCjk("かな") &&
                !TextFitMath.HasCjk("ADS 100% -5.17") && !TextFitMath.HasCjk("é▸") && !TextFitMath.HasCjk(null) && !TextFitMath.HasCjk(""), "");

            double emCjk = TextFitMath.LineEm("换手中"), emNum = TextFitMath.LineEm("100"), emMix = TextFitMath.TextEm("左右\n上下偏移 %");
            Check("W30-2 YaHei width floor: CJK 1 em each, digits 0.6 em, widest line of a multi-line text",
                Math.Abs(emCjk - 3.0) < 1e-9 && Math.Abs(emNum - 1.8) < 1e-9 && Math.Abs(emMix - (4.0 + 0.32 + 0.9)) < 1e-9,
                "cjk=" + emCjk + " num=" + emNum + " mix=" + emMix);

            double em12 = TextFitMath.EmPx(12, 1.0), em12x2 = TextFitMath.EmPx(12, 2.0);
            Check("W30-3 em px: 12 pt = 16 px at 100 %, 32 px at 200 %; bad input falls back", Math.Abs(em12 - 16) < 1e-9 && Math.Abs(em12x2 - 32) < 1e-9 &&
                TextFitMath.EmPx(double.NaN, double.NaN) > 0, em12 + " " + em12x2);

            // a measurement narrower than YaHei (Mono / Noto, or a wrong font) never wins; a wider one does; margin always added
            int narrow = TextFitMath.NeedWidth("后坐力", 30, 16), wide = TextFitMath.NeedWidth("后坐力", 80, 16);
            Check("W30-4 need width = max(measured, YaHei floor) + margin (0.4 em, >= 4 px)",
                narrow == 48 + 7 && wide == 80 + 7 && TextFitMath.MarginX(1) == 4 && TextFitMath.MarginY(1) == 2,
                "narrow=" + narrow + " wide=" + wide);

            Check("W30-5 need height: lines x 1.32 em floor + margin", TextFitMath.NeedHeight(2, 0, 16) == (int)Math.Ceiling(2 * 1.32 * 16) + 4 &&
                TextFitMath.NeedHeight(0, 40, 16) == 40 + 4, TextFitMath.NeedHeight(2, 0, 16).ToString());

            string w1 = TextFitMath.WidestNumber(-100m, 100m, 1, false), w2 = TextFitMath.WidestNumber(0m, 2000m, 0, true),
                   w3 = TextFitMath.WidestNumber(0m, 5m, 2, false);
            Check("W30-6 widest number text: sign, decimals, thousands, every digit as 8", w1 == "-888.8" && w2 == "8,888" && w3 == "8.88",
                w1 + " | " + w2 + " | " + w3);

            int nb1 = TextFitMath.NumberBoxWidth("-888.88", 0, TextFitMath.EmPx(11, 1), 1), nb2 = TextFitMath.NumberBoxWidth("-888.88", 0, TextFitMath.EmPx(11, 2), 2);
            Check("W30-7 number box width at 200 % >= 2 x the 100 % width - 2 (fits at 200 %, spin + frame scale too)", nb2 >= 2 * nb1 - 2 && nb1 > 40,
                "100%=" + nb1 + " 200%=" + nb2);

            Check("W30-8 fit / box scaling: a 100 % box scales linearly, floor-rounded", TextFitMath.Fits(10, 5, 10, 5) && !TextFitMath.Fits(11, 0, 10, 99) &&
                TextFitMath.Fits(0, 0, 0, 0) && TextFitMath.ScaleBox(101, 1, 1.5) == 151 && TextFitMath.ScaleBox(150, 1.5, 1) == 100 &&
                TextFitMath.ScaleBox(10, 0, double.NaN) == 10, "");

            Check("W30-9 the runtime check covers exactly 100 / 150 / 200 %", TextFitMath.Scales.Length == 3 && TextFitMath.Scales[0] == 1.0 &&
                TextFitMath.Scales[1] == 1.5 && TextFitMath.Scales[2] == 2.0, "");

            // ---------------------------------------------------------------- decision 14: tokens
            Check("W30-10 one radius (0), one border (1 px), one accent bar (3 px), spacing scale 4/8/12/16/24",
                Theme.Radius == 0 && Theme.BorderPx == 1 && Theme.AccentBarPx == 3 && Theme.FocusPx == 1 &&
                Theme.Sp(10, 1) == 8 && Theme.Sp(14, 1) == 12 && Theme.Sp(16, 2) == 32 && Theme.Sp(100, 1.5) == 36 && Theme.Sp(1, 0) == 4, "");

            double c1 = Theme.Contrast(Theme.ColOnAccent, Theme.ColAccent), c2 = Theme.Contrast(Theme.ColText, Theme.ColCard),
                   c3 = Theme.Contrast(Theme.ColMuted, Theme.ColCard), c4 = Theme.Contrast(Theme.ColText, Theme.ColAccentSoft),
                   c5 = Theme.Contrast(Theme.ColAccent, Theme.ColAccentSoft), c6 = Theme.Contrast(Theme.ColAccent2, Theme.ColCard);
            Check("W30-11 new tokens readable (>= 4.5:1): text on accent, text / muted / cyan on card, text / accent on the selected fill",
                c1 >= 4.5 && c2 >= 4.5 && c3 >= 4.5 && c4 >= 4.5 && c5 >= 4.5 && c6 >= 4.5,
                c1.ToString("0.0") + " " + c2.ToString("0.0") + " " + c3.ToString("0.0") + " " + c4.ToString("0.0") + " " + c5.ToString("0.0") + " " + c6.ToString("0.0"));

            // ---------------------------------------------------------------- source checks (Theme.cs / TextFit.cs)
            string th = W30Src("Theme.cs"), tf = W30Src("TextFit.cs");
            string header = W30Class(th, "HeaderPanel"), nav = W30Class(th, "NavItem"), title = W30Class(th, "PageTitle"),
                   fold = W30Class(th, "FoldHeader"), glow = W30Class(th, "GlowButton"), sect = W30Class(th, "Section");
            bool allFound = header.Length > 0 && nav.Length > 0 && title.Length > 0 && fold.Length > 0 && glow.Length > 0 && sect.Length > 0;
            string shell = header + nav + title + fold + glow + sect;
            bool noGradient = !shell.Contains("LinearGradientBrush") && !shell.Contains("DrawScanlines") && !shell.Contains("HackerScanlines") &&
                              !shell.Contains("DrawFadeLine") && !shell.Contains("HackerFadeLine");
            bool noDecode = !shell.Contains("Decode(");
            bool noTicks = !shell.Contains("corner ticks:") && !Regex.IsMatch(shell, @"g\.DrawLine\(a, ");
            bool noGlow = !glow.Contains("Math.Sin(") && !glow.Contains("fading frames");
            bool noDots = !shell.Contains("DashStyle.Dot");
            Check("W30-12 flat shell parts (header, nav, page title, fold, tutorial button, card): no gradient / scanline / fade line, no decode, no corner ticks, no glow pulse, no dotted focus",
                allFound && noGradient && noDecode && noTicks && noGlow && noDots,
                "found=" + allFound + " grad=" + noGradient + " decode=" + noDecode + " ticks=" + noTicks + " glow=" + noGlow + " dots=" + noDots);

            bool scanFlat = Regex.IsMatch(th, @"public static void DrawScanlines\(Graphics g, Rectangle r, int alpha\) \{ \}");
            int fade = th.IndexOf("public static void DrawFadeLine(", StringComparison.Ordinal);
            string fadeBody = fade < 0 ? "" : th.Substring(fade, Math.Min(400, th.Length - fade));
            bool fadeFlat = fadeBody.Contains("SolidBrush") && !fadeBody.Contains("Gradient");
            bool hackerKept = th.Contains("public static void HackerScanlines(") && th.Contains("public static void HackerFadeLine(");
            Check("W30-13 shared helpers flat for every caller outside the dashboard (scanlines draw nothing, fade line solid); Hacker* versions kept for the dashboard",
                scanFlat && fadeFlat && hackerKept, "scan=" + scanFlat + " fade=" + fadeFlat + " hacker=" + hackerKept);

            bool primary = th.Contains("b.BackColor = accent ? ColAccent : ColInput;") && th.Contains("b.ForeColor = accent ? ColOnAccent");
            bool tokensUsed = shell.Contains("Theme.AccentBarPx") && shell.Contains("Theme.BorderPx") && sect.Contains("Theme.ColCard");
            Check("W30-14 one button style (primary filled accent + dark text, secondary input fill + 1 px border); shell parts draw with the tokens",
                primary && tokensUsed, "primary=" + primary + " tokens=" + tokensUsed);

            bool monoGuard = th.Contains("try { HookMonoCjk(c); } catch { }") && th.Contains("TextFit.FixMonoCjk(") &&
                             th.Contains("c.TextChanged += OnMonoCjkChanged;") && th.Contains("c.FontChanged += OnMonoCjkChanged;");
            bool fontFor = th.Contains("return mono && !TarkovCrosshair.TextFitMath.HasCjk(text) ? Mono(pt, style) : Ui(pt, style);") &&
                           header.Contains("FontFor(title,") && title.Contains("Theme.FontFor(sub,");
            Check("W30-15 no Chinese in the monospace font: every themed control is switched to the UI font (now and on text / font change); owner-drawn header / page title pick via FontFor",
                monoGuard && fontFor, "guard=" + monoGuard + " fontFor=" + fontFor);

            int selfN = Regex.Matches(th, @": (Control|Control, IPausable), ITextFitSelf|: Control, IPausable, ITextFitSelf|: Control, ITextFitSelf").Count;
            Check("W30-16 owner-drawn shell controls report their own text need (ITextFitSelf: nav, page title, fold, tutorial button)",
                selfN >= 4 && nav.Contains("public Size TextNeed(") && title.Contains("public Size TextNeed(") && fold.Contains("public Size TextNeed(") &&
                glow.Contains("public Size TextNeed("), "n=" + selfN);

            bool checkAll = tf.Contains("internal static List<FitIssue> CheckAll(Control root)") && tf.Contains("foreach (double s in TextFitMath.Scales)");
            bool realMetrics = tf.Contains("new Font(f.FontFamily, (float)(f.SizeInPoints * fontScale)") && tf.Contains("TextRenderer.MeasureText(");
            bool numbers = tf.Contains("NumericUpDown nud = c as NumericUpDown;") && tf.Contains("NumberBoxWidth(nud.Minimum, nud.Maximum, nud.DecimalPlaces");
            bool safeFix = tf.Contains("if (c is TextBoxBase || c.AutoSize || c.Dock == DockStyle.Fill") && tf.Contains("Grow(c, wide ? 0 : fi.Need.Width, fi.Need.Height)") && !Regex.IsMatch(tf, @"\.Location\s*=");
            bool report = tf.Contains("internal const string ReportEnv = \"THA_TEXTFIT_REPORT\";");
            Check("W30-17 runtime check: real font metrics re-made at each scale, number boxes from their widest value, 100/150/200 %, fixes never move anything, report via THA_TEXTFIT_REPORT",
                checkAll && realMetrics && numbers && safeFix && report,
                "all=" + checkAll + " metrics=" + realMetrics + " numbers=" + numbers + " fix=" + safeFix + " report=" + report);

            bool cs5 = !Regex.IsMatch(tf, @"\$""|\?\.|=>|nameof\(") && !Regex.IsMatch(th, @"\$""|\?\.[A-Za-z]|nameof\(");
            Check("W30-18 TextFit.cs / Theme.cs stay C# 5 (no $\"\", ?., =>, nameof)", cs5, "");
        }
    }
}
