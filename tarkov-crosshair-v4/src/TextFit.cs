// TextFit.cs -- v5.1 decision 9 (W30): every label / cell sized from real font metrics with a margin, Chinese never in the
// monospace font, number boxes wide enough at 200 %, and a runtime check that every control's text fits its box at
// 100 / 150 / 200 %. C# 5 / .NET Framework 4.8 / WinForms.
//
//   TextFit.FitPage(page)            -> after a page is built (and after a DPI change): fixes what it safely can, then
//                                       (only when THA_TEXTFIT_REPORT is set) appends the 100/150/200 % report to a file
//   TextFit.Fix(root)                -> mono -> UI font for Chinese text, grows fixed-size labels / buttons / number boxes /
//                                       drop-downs that are too small (never moves anything, never shrinks)
//   TextFit.Check(root, scale)       -> list of controls whose text does not fit at that screen scale (1.0, 1.5, 2.0)
//   TextFit.Report(root, name)       -> text report for all three scales ("TEXTFIT <name> scale=1.5 ok=N bad=M" + one line each)
//   TextFit.NeedSize(text, font, ...) / NumberBoxWidth(...) -> the sizes to build controls with in the first place
//   ITextFitSelf                     -> owner-drawn controls report what their own text needs
//
// Pure part (TextFitMath) compiles everywhere (Linux tests); the WinForms part is skipped under THEME_PURE_CHECK.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Text;
#if !THEME_PURE_CHECK
using System.IO;
using System.Reflection;
using System.Windows.Forms;
#endif

namespace TarkovCrosshair
{
    /// <summary>Pure text-fit rules (no WinForms): CJK detection, margins, a conservative Microsoft YaHei width floor.</summary>
    internal static class TextFitMath
    {
        /// <summary>The screen scales every control must fit at (decision 9).</summary>
        internal static readonly double[] Scales = { 1.0, 1.5, 2.0 };

        /// <summary>True for CJK ideographs, kana, hangul, CJK punctuation and full-width forms.</summary>
        internal static bool IsCjk(char c)
        {
            return (c >= 0x2E80 && c <= 0x2FDF) || (c >= 0x3000 && c <= 0x30FF) || (c >= 0x3100 && c <= 0x9FFF) ||
                   (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) ||
                   (c >= 0xFF00 && c <= 0xFFEF);
        }

        internal static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (IsCjk(s[i])) return true;
            return false;
        }

        /// <summary>
        /// Conservative width of one line in em (1 em = the font size in px) for Microsoft YaHei UI: CJK / full-width 1.0,
        /// digits and most Latin 0.6, wide Latin (M W m w @ %) 0.9, narrow (i l j . , : ; ' | ! space) 0.32.
        /// A floor only: the real measurement wins when it is wider. UNVERIFIED against Windows (no Windows here):
        /// the per-class values are rounded up from published YaHei advance widths (CJK 1 em, digits ~0.55 em).
        /// </summary>
        internal static double LineEm(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            double w = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (char.IsSurrogate(c)) { w += 0.5; continue; }
                if (IsCjk(c) || c > 0x2000 && c < 0x2E80 && !char.IsWhiteSpace(c)) w += 1.0; // symbols like ▸ → ✓: assume square
                else if ("MWmw@%".IndexOf(c) >= 0) w += 0.9;
                else if ("iljI.,:;'|! \t".IndexOf(c) >= 0) w += 0.32;
                else w += 0.6;
            }
            return w;
        }

        /// <summary>Widest line (split on \n) in em, see LineEm.</summary>
        internal static double TextEm(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double best = 0;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++) best = Math.Max(best, LineEm(lines[i]));
            return best;
        }

        internal static int LineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int n = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') n++;
            return n;
        }

        /// <summary>Font size in px for a point size at a screen scale (1.0 = 96 dpi).</summary>
        internal static double EmPx(double pt, double scale)
        {
            if (double.IsNaN(pt) || pt <= 0) pt = 9;
            if (double.IsNaN(scale) || scale <= 0) scale = 1;
            return pt * 96.0 * scale / 72.0;
        }

        /// <summary>Horizontal margin (both sides together) added to a measured text width: 0.4 em, at least 4 px.</summary>
        internal static int MarginX(double emPx) { return Math.Max(4, (int)Math.Ceiling(0.4 * emPx)); }

        /// <summary>Vertical margin (top + bottom together) added to a measured line height: 0.2 em, at least 2 px.</summary>
        internal static int MarginY(double emPx) { return Math.Max(2, (int)Math.Ceiling(0.2 * emPx)); }

        /// <summary>Needed width = max(measured, YaHei floor) + margin. measuredPx may be 0 (not measured).</summary>
        internal static int NeedWidth(string text, int measuredPx, double emPx)
        {
            int floor = (int)Math.Ceiling(TextEm(text) * emPx);
            return Math.Max(measuredPx, floor) + MarginX(emPx);
        }

        /// <summary>Needed height for lines of text: max(measured, lines x 1.32 em (YaHei line height)) + margin.</summary>
        internal static int NeedHeight(int lines, int measuredPx, double emPx)
        {
            if (lines < 1) lines = 1;
            int floor = (int)Math.Ceiling(lines * 1.32 * emPx);
            return Math.Max(measuredPx, floor) + MarginY(emPx);
        }

        /// <summary>Formats a number box value the way NumericUpDown shows it (invariant digits, fixed decimals).</summary>
        internal static string NumberText(decimal v, int decimals, bool thousands)
        {
            if (decimals < 0) decimals = 0;
            if (decimals > 10) decimals = 10;
            string fmt = (thousands ? "N" : "F") + decimals.ToString(CultureInfo.InvariantCulture);
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        /// <summary>The widest text a number box can show: the longer of min and max (and of "-" + |max| for negative ranges).</summary>
        internal static string WidestNumber(decimal min, decimal max, int decimals, bool thousands)
        {
            string a = NumberText(min, decimals, thousands), b = NumberText(max, decimals, thousands);
            string w = a.Length >= b.Length ? a : b;
            // digits are equal width; a box showing 8.88 needs the same room as 0.00 -- use '8' for every digit
            StringBuilder sb = new StringBuilder(w.Length);
            for (int i = 0; i < w.Length; i++) sb.Append(char.IsDigit(w[i]) ? '8' : w[i]);
            return sb.ToString();
        }

        /// <summary>
        /// Width a number box needs: the widest value (digits 0.6 em each) + spin buttons (17 px x scale) + frame (4 px x
        /// scale) + margin. textPx = a real measurement of WidestNumber (0 = not measured).
        /// </summary>
        internal static int NumberBoxWidth(string widest, int textPx, double emPx, double scale)
        {
            if (double.IsNaN(scale) || scale <= 0) scale = 1;
            int spin = (int)Math.Ceiling(17 * scale), frame = (int)Math.Ceiling(4 * scale);
            return NeedWidth(widest, textPx, emPx) + spin + frame;
        }

        /// <summary>A box of haveW x haveH holds needW x needH (0 = that side not checked).</summary>
        internal static bool Fits(int needW, int needH, int haveW, int haveH)
        {
            return (needW <= 0 || haveW >= needW) && (needH <= 0 || haveH >= needH);
        }

        /// <summary>A box sized at scale 'from' scaled to scale 'to' (WinForms scales layout linearly, rounded).</summary>
        internal static int ScaleBox(int px, double from, double to)
        {
            if (from <= 0 || double.IsNaN(from)) from = 1;
            if (to <= 0 || double.IsNaN(to)) to = 1;
            return (int)Math.Floor(px * to / from + 1e-6);
        }
    }

    /// <summary>
    /// An owner-drawn control that knows what its own text needs. TextFit.Check asks it instead of guessing:
    /// return the size the text needs (margin included) when drawn with fonts scaled by fontScale (1 = as now).
    /// </summary>
    internal interface ITextFitSelf
    {
        Size TextNeed(float fontScale);
    }

#if !THEME_PURE_CHECK
    /// <summary>One control whose text does not fit.</summary>
    internal sealed class FitIssue
    {
        public string Path, Text, Kind;
        public double Scale;
        public Size Need, Have;

        public override string ToString()
        {
            string t = Text ?? "";
            t = t.Replace("\r", "").Replace("\n", "⏎");
            if (t.Length > 40) t = t.Substring(0, 40) + "…";
            return "scale=" + Scale.ToString("0.##", CultureInfo.InvariantCulture) + " " + Kind + " " + Path + " \"" + t + "\" need=" +
                   Need.Width + "x" + Need.Height + " have=" + Have.Width + "x" + Have.Height;
        }
    }

    internal static class TextFit
    {
        private const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        /// <summary>Environment variable: a file path; FitPage appends its 100/150/200 % report there (CI / probes).</summary>
        internal const string ReportEnv = "THA_TEXTFIT_REPORT";

        // ------------------------------------------------------------------ sizes to build with
        /// <summary>Screen scale of a control (DeviceDpi / 96); 1 when unknown.</summary>
        internal static double ScaleOf(Control c)
        {
            int dpi = 96;
            try { if (c != null) dpi = c.DeviceDpi; } catch { }
            if (dpi <= 0) dpi = 96;
            return dpi / 96.0;
        }

        private static double EmPx(Font f, double scale)
        {
            return TextFitMath.EmPx(f == null ? 9 : f.SizeInPoints, scale);
        }

        /// <summary>
        /// Size a box needs to show text in font f (margin included). wrapWidth &gt; 0: wrapped at that width (height grows);
        /// 0: one line per \n. scale = the screen scale the font is used at (ScaleOf(control)).
        /// </summary>
        internal static Size NeedSize(string text, Font f, int wrapWidth, double scale)
        {
            if (f == null) return Size.Empty;
            text = text ?? "";
            double em = EmPx(f, scale);
            Size m;
            try
            {
                m = wrapWidth > 0
                    ? TextRenderer.MeasureText(text.Length == 0 ? " " : text, f, new Size(Math.Max(1, wrapWidth - TextFitMath.MarginX(em)), 0), Wrap)
                    : TextRenderer.MeasureText(text.Length == 0 ? " " : text, f, new Size(int.MaxValue / 4, 0), Line & ~TextFormatFlags.SingleLine);
            }
            catch { m = Size.Empty; }
            if (wrapWidth > 0)
            {
                int lines = Math.Max(1, (int)Math.Ceiling(TextFitMath.TextEm(text) * em / Math.Max(1.0, wrapWidth - TextFitMath.MarginX(em))));
                return new Size(wrapWidth, TextFitMath.NeedHeight(Math.Max(lines, TextFitMath.LineCount(text)), m.Height, em));
            }
            return new Size(TextFitMath.NeedWidth(text, m.Width, em), TextFitMath.NeedHeight(TextFitMath.LineCount(text), m.Height, em));
        }

        /// <summary>Width a NumericUpDown needs to show its widest value at its own scale (spin buttons and frame included).</summary>
        internal static int NumberBoxWidth(NumericUpDown n)
        {
            if (n == null) return 0;
            return NumberBoxWidth(n.Minimum, n.Maximum, n.DecimalPlaces, n.ThousandsSeparator, n.Font, ScaleOf(n));
        }

        /// <summary>Width a number box with this range needs in font f at a screen scale.</summary>
        internal static int NumberBoxWidth(decimal min, decimal max, int decimals, bool thousands, Font f, double scale)
        {
            if (f == null) return 0;
            string w = TextFitMath.WidestNumber(min, max, decimals, thousands);
            int px = 0;
            try { px = TextRenderer.MeasureText(w, f, new Size(int.MaxValue / 4, 0), Line).Width; } catch { }
            return TextFitMath.NumberBoxWidth(w, px, EmPx(f, scale), scale);
        }

        // ------------------------------------------------------------------ fonts
        /// <summary>True when the font is one of the monospace fonts Theme.Mono picks (or a generic monospace).</summary>
        internal static bool IsMonoFont(Font f)
        {
            if (f == null) return false;
            string n = f.Name ?? "";
            return n.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("Lucida Console", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// No Chinese in the monospace font: when c shows CJK text in a mono font, switch it to the UI font (same size and
        /// style). Fonts made here are cached per (size, style), never disposed (a handful per process). Returns true when changed.
        /// </summary>
        internal static bool FixMonoCjk(Control c)
        {
            if (c == null) return false;
            try
            {
                if (!IsMonoFont(c.Font) || !TextFitMath.HasCjk(ShownText(c))) return false;
                c.Font = UiLike(c.Font);
                return true;
            }
            catch { return false; }
        }

        private static readonly Dictionary<string, Font> uiCache = new Dictionary<string, Font>();

        internal static Font UiLike(Font f)
        {
            string key = f.SizeInPoints.ToString("0.###", CultureInfo.InvariantCulture) + "/" + (int)f.Style;
            lock (uiCache)
            {
                Font u;
                if (!uiCache.TryGetValue(key, out u)) { u = Theme.Ui(f.SizeInPoints, f.Style); uiCache[key] = u; }
                return u;
            }
        }

        private static string ShownText(Control c)
        {
            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                StringBuilder sb = new StringBuilder(cb.Text ?? "");
                try { foreach (object o in cb.Items) sb.Append('\n').Append(cb.GetItemText(o)); } catch { }
                return sb.ToString();
            }
            return c.Text ?? "";
        }

        // ------------------------------------------------------------------ check
        /// <summary>The control's own Visible flag (true even while a parent page is hidden); falls back to Visible.</summary>
        private static MethodInfo getState;
        private static bool getStateLooked;

        private static bool OwnVisible(Control c)
        {
            try
            {
                if (!getStateLooked)
                {
                    getStateLooked = true;
                    getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
                }
                if (getState != null) return (bool)getState.Invoke(c, new object[] { 0x2 }); // STATE_VISIBLE
            }
            catch { }
            return c.Visible;
        }

        /// <summary>Controls whose text does not fit at screen scale 'scale' (1.0 / 1.5 / 2.0). Never throws.</summary>
        internal static List<FitIssue> Check(Control root, double scale)
        {
            List<FitIssue> list = new List<FitIssue>();
            if (root == null) return list;
            try { Walk(root, root.Name ?? root.GetType().Name, scale, ScaleOf(root), list); } catch { }
            return list;
        }

        /// <summary>Check at 100, 150 and 200 %.</summary>
        internal static List<FitIssue> CheckAll(Control root)
        {
            List<FitIssue> all = new List<FitIssue>();
            foreach (double s in TextFitMath.Scales) all.AddRange(Check(root, s));
            return all;
        }

        private static void Walk(Control c, string path, double scale, double cur, List<FitIssue> list)
        {
            if (!OwnVisible(c)) return;
            try { CheckOne(c, path, scale, cur, list); } catch { }
            if (c is UpDownBase || c is TextBoxBase || c is ComboBox) return; // inner parts are the control's own business
            Control[] kids;
            try { kids = new Control[c.Controls.Count]; c.Controls.CopyTo(kids, 0); } catch { return; }
            for (int i = 0; i < kids.Length; i++)
            {
                Control k = kids[i];
                string nm = !string.IsNullOrEmpty(k.Name) ? k.Name : k.GetType().Name + "#" + i;
                Walk(k, path + "/" + nm, scale, cur, list);
            }
        }

        private static void Add(List<FitIssue> list, string kind, string path, string text, double scale, Size need, Size have)
        {
            FitIssue fi = new FitIssue();
            fi.Kind = kind; fi.Path = path; fi.Text = text; fi.Scale = scale; fi.Need = need; fi.Have = have;
            list.Add(fi);
        }

        private static readonly Dictionary<string, Font> scaledCache = new Dictionary<string, Font>();

        /// <summary>f at fontScale x its size (cached; fontScale 1 = f itself).</summary>
        private static Font Scaled(Font f, double fontScale)
        {
            if (f == null || Math.Abs(fontScale - 1) < 1e-6) return f;
            string key = f.Name + "/" + f.SizeInPoints.ToString("0.###", CultureInfo.InvariantCulture) + "/" + (int)f.Style + "/" +
                         fontScale.ToString("0.###", CultureInfo.InvariantCulture);
            lock (scaledCache)
            {
                Font s;
                if (!scaledCache.TryGetValue(key, out s))
                {
                    s = new Font(f.FontFamily, (float)(f.SizeInPoints * fontScale), f.Style, GraphicsUnit.Point);
                    scaledCache[key] = s;
                }
                return s;
            }
        }

        private static Size HaveAt(Control c, double cur, double scale)
        {
            Size s = c.ClientSize;
            if (c is ButtonBase || c is Label || c is ComboBox || c is UpDownBase) s = c.Size;
            return new Size(TextFitMath.ScaleBox(s.Width, cur, scale), TextFitMath.ScaleBox(s.Height, cur, scale));
        }

        /// <summary>
        /// One control: needed size of its text at 'scale' (real metrics: the font is re-made at size x scale / current scale
        /// and measured), compared with its box scaled the same way. AutoSize controls size themselves: for them only the
        /// parent's clipping is checked (the control must lie inside its parent unless the parent scrolls).
        /// </summary>
        private static void CheckOne(Control c, string path, double scale, double cur, List<FitIssue> list)
        {
            double fs = scale / cur;
            ITextFitSelf self = c as ITextFitSelf;
            if (self != null)
            {
                Size need = self.TextNeed((float)fs);
                Size have = HaveAt(c, cur, scale);
                if (!TextFitMath.Fits(need.Width, need.Height, have.Width, have.Height)) Add(list, "self", path, c.Text, scale, need, have);
                return;
            }
            string text = ShownText(c);
            bool cjkInMono = TextFitMath.HasCjk(text) && IsMonoFont(c.Font) && !(c is TextBoxBase);
            if (cjkInMono) Add(list, "mono-cjk", path, text, scale, Size.Empty, Size.Empty);

            NumericUpDown nud = c as NumericUpDown;
            if (nud != null)
            {
                Font f = Scaled(nud.Font, fs);
                int need = NumberBoxWidth(nud.Minimum, nud.Maximum, nud.DecimalPlaces, nud.ThousandsSeparator, f, scale);
                int needH = f.Height + 2;
                Size have = HaveAt(c, cur, scale);
                if (!TextFitMath.Fits(need, needH, have.Width, have.Height)) Add(list, "number", path, TextFitMath.WidestNumber(nud.Minimum, nud.Maximum, nud.DecimalPlaces, nud.ThousandsSeparator), scale, new Size(need, needH), have);
                return;
            }
            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                Font f = Scaled(cb.Font, fs);
                int widest = 0;
                string wt = "";
                try
                {
                    foreach (object o in cb.Items)
                    {
                        string t = cb.GetItemText(o);
                        int w = NeedSize(t, f, 0, scale).Width;
                        if (w > widest) { widest = w; wt = t; }
                    }
                }
                catch { }
                if (cb.DropDownStyle == ComboBoxStyle.DropDownList && widest > 0)
                {
                    int need = widest + (int)Math.Ceiling(20 * scale); // drop-down arrow + frame
                    Size have = HaveAt(c, cur, scale);
                    if (have.Width < need) Add(list, "combo", path, wt, scale, new Size(need, 0), have);
                }
                return;
            }
            if (text.Length == 0) return;
            if (c is TextBoxBase)
            {
                TextBoxBase tb = (TextBoxBase)c;
                if (tb.Multiline) return; // scrolls
                Font f = Scaled(tb.Font, fs);
                Size have = HaveAt(c, cur, scale);
                int needH = f.Height + 2;
                int needW = tb.ReadOnly ? NeedSize(text, f, 0, scale).Width : 0; // what the user types scrolls; fixed text must fit
                if (!TextFitMath.Fits(needW, needH, have.Width, have.Height)) Add(list, "textbox", path, text, scale, new Size(needW, needH), have);
                return;
            }
            bool isLabel = c is Label, isCheck = c is CheckBox || c is RadioButton, isButton = c is ButtonBase && !isCheck;
            if (!isLabel && !isCheck && !isButton) return; // custom / container controls: only via ITextFitSelf
            Font font = Scaled(c.Font, fs);
            if (c.AutoSize)
            {
                CheckClip(c, path, text, scale, cur, list);
                return;
            }
            Size box = HaveAt(c, cur, scale);
            int pad = (int)Math.Ceiling((c.Padding.Horizontal) * scale / cur);
            int extraW = pad, extraH = (int)Math.Ceiling(c.Padding.Vertical * scale / cur);
            if (isCheck) extraW += (int)Math.Ceiling(font.Height + 3 * scale); // the tick box + gap (flat style draws it at about the font height)
            if (isButton) { extraW += (int)Math.Ceiling(6 * scale); extraH += (int)Math.Ceiling(4 * scale); } // 1 px frame + inner pad each side
            Label lb = c as Label;
            if (lb != null && lb.BorderStyle != BorderStyle.None) { extraW += (int)Math.Ceiling(2 * scale); extraH += (int)Math.Ceiling(2 * scale); }
            bool wraps = isLabel && TextFitMath.LineCount(text) == 1 && box.Height >= 2 * font.Height;
            Size needT = wraps ? NeedSize(text, font, Math.Max(1, box.Width - extraW), scale) : NeedSize(text, font, 0, scale);
            Size needAll = new Size(wraps ? 0 : needT.Width + extraW, needT.Height + extraH);
            if (!TextFitMath.Fits(needAll.Width, needAll.Height, box.Width, box.Height))
                Add(list, isLabel ? "label" : (isCheck ? "check" : "button"), path, text, scale, needAll, box);
        }

        private static void CheckClip(Control c, string path, string text, double scale, double cur, List<FitIssue> list)
        {
            Control p = c.Parent;
            if (p == null || c.Dock == DockStyle.Fill) return;
            ScrollableControl sp = p as ScrollableControl;
            if (sp != null && sp.AutoScroll) return;
            Rectangle pc = p.ClientRectangle;
            Rectangle b = c.Bounds;
            // at another scale both grow about linearly; text grows slightly more, so check with the measured need at 'scale'
            Font f = Scaled(c.Font, scale / cur);
            // a label that wraps (MaximumSize.Width set) never gets wider than that width: only its own box counts
            int maxW = c.MaximumSize.Width > 0 ? TextFitMath.ScaleBox(c.MaximumSize.Width, cur, scale) : 0;
            int textW = maxW > 0 ? 0 : NeedSize(text, f, 0, scale).Width - TextFitMath.MarginX(TextFitMath.EmPx(c.Font.SizeInPoints, scale));
            int right = TextFitMath.ScaleBox(b.Left, cur, scale) + Math.Max(TextFitMath.ScaleBox(b.Width, cur, scale), textW);
            Size need = new Size(right, 0);
            int haveRight = TextFitMath.ScaleBox(pc.Right, cur, scale);
            if (right > haveRight) Add(list, "clipped", path, text, scale, need, new Size(haveRight, 0));
        }

        // ------------------------------------------------------------------ fix
        /// <summary>
        /// Fixes what it safely can, top-down: Chinese in a mono font -> UI font; fixed-size labels, buttons, check boxes,
        /// number boxes and drop-down lists that are too small (at the current scale) grow (Width / Height and MinimumSize,
        /// so a layout panel keeps the room); drop-down lists open wide enough for their widest item. Never moves or
        /// shrinks anything; Dock = Fill / anchored-both-sides controls only get the font fix. Returns how many changed.
        /// </summary>
        internal static int Fix(Control root)
        {
            if (root == null) return 0;
            int n = 0;
            try { n = FixTree(root); } catch { }
            return n;
        }

        private static int FixTree(Control c)
        {
            int n = 0;
            if (FixMonoCjk(c)) n++;
            try { if (FixSize(c)) n++; } catch { }
            if (c is UpDownBase || c is TextBoxBase || c is ComboBox) return n;
            Control[] kids;
            try { kids = new Control[c.Controls.Count]; c.Controls.CopyTo(kids, 0); } catch { return n; }
            for (int i = 0; i < kids.Length; i++) n += FixTree(kids[i]);
            return n;
        }

        private static bool Stretched(Control c)
        {
            if (c.Dock != DockStyle.None) return true;
            AnchorStyles a = c.Anchor;
            return (a & AnchorStyles.Left) != 0 && (a & AnchorStyles.Right) != 0;
        }

        private static bool Grow(Control c, int w, int h)
        {
            Size s = c.Size;
            int nw = Math.Max(s.Width, w), nh = Math.Max(s.Height, h);
            if (nw == s.Width && nh == s.Height) return false;
            try
            {
                Size min = c.MinimumSize;
                if (min.Width > 0 || min.Height > 0 || c.Parent is TableLayoutPanel || c.Parent is FlowLayoutPanel)
                    c.MinimumSize = new Size(Math.Max(min.Width, w > s.Width ? nw : 0), Math.Max(min.Height, h > s.Height ? nh : 0));
            }
            catch { }
            c.Size = new Size(nw, nh);
            return true;
        }

        private static bool FixSize(Control c)
        {
            if (c is ITextFitSelf) return false; // owner-drawn: its owner sizes it
            double cur = ScaleOf(c);
            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                int widest = 0;
                try { foreach (object o in cb.Items) widest = Math.Max(widest, NeedSize(cb.GetItemText(o), cb.Font, 0, cur).Width); } catch { }
                if (widest <= 0) return false;
                int need = widest + (int)Math.Ceiling(20 * cur);
                bool changed = false;
                try { if (cb.DropDownWidth < need) { cb.DropDownWidth = need; changed = true; } } catch { }
                if (cb.DropDownStyle == ComboBoxStyle.DropDownList && !Stretched(c) && cb.Width < need) changed |= Grow(c, need, 0);
                return changed;
            }
            NumericUpDown nud = c as NumericUpDown;
            if (nud != null)
            {
                if (Stretched(c)) return false;
                return Grow(c, NumberBoxWidth(nud), 0);
            }
            if (c is TextBoxBase || c.AutoSize || c.Dock == DockStyle.Fill || c.Dock == DockStyle.Left || c.Dock == DockStyle.Right) return false;
            bool wide = Stretched(c); // stretched sideways (Dock Top / Bottom, or anchored left + right): only the height may grow
            List<FitIssue> one = new List<FitIssue>();
            CheckOne(c, "", cur, cur, one);
            for (int i = 0; i < one.Count; i++)
            {
                FitIssue fi = one[i];
                if (fi.Kind == "label" || fi.Kind == "check" || fi.Kind == "button") return Grow(c, wide ? 0 : fi.Need.Width, fi.Need.Height);
            }
            return false;
        }

        // ------------------------------------------------------------------ report
        /// <summary>"TEXTFIT name scale=S ok=N bad=M" per scale, then one line per issue. ok = controls with text checked.</summary>
        internal static string Report(Control root, string name)
        {
            StringBuilder sb = new StringBuilder();
            int checkedN = CountText(root);
            foreach (double s in TextFitMath.Scales)
            {
                List<FitIssue> l = Check(root, s);
                sb.Append("TEXTFIT ").Append(name ?? "?").Append(" scale=").Append(s.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(" checked=").Append(checkedN).Append(" bad=").Append(l.Count).Append('\n');
                for (int i = 0; i < l.Count; i++) sb.Append("  ").Append(l[i].ToString()).Append('\n');
            }
            return sb.ToString();
        }

        private static int CountText(Control c)
        {
            if (c == null || !OwnVisible(c)) return 0;
            int n = (c.Text ?? "").Length > 0 || c is NumericUpDown || c is ComboBox ? 1 : 0;
            if (c is UpDownBase || c is TextBoxBase || c is ComboBox) return n;
            foreach (Control k in c.Controls) n += CountText(k);
            return n;
        }

        /// <summary>
        /// Call after a page is built and after a DPI change: Fix, then (only when THA_TEXTFIT_REPORT names a file)
        /// append the report. Cheap when the variable is unset (one tree walk). Never throws. Returns how many were fixed.
        /// </summary>
        internal static int FitPage(Control page, string name)
        {
            int n = Fix(page);
            try
            {
                string path = Environment.GetEnvironmentVariable(ReportEnv);
                if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, Report(page, name), Encoding.UTF8);
            }
            catch { }
            return n;
        }
    }
#endif
}
