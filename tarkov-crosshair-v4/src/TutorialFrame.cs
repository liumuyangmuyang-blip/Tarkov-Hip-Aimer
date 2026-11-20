// TutorialFrame.cs -- 首次设置教程 (v4.0): the window chrome (header + progress dots, scene frame, terminal, typed text
// panel, input rows, footer buttons) drawn on one canvas. System.Drawing only (no WinForms): TutorialForm hosts it and
// places real input controls at FieldRect(i); the offscreen harness draws it to PNG.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TarkovCrosshair
{
    /// <summary>Wrapped text that can be typed in without allocating per frame (prefixes are built at layout time).</summary>
    public sealed class TutorialTextBlock
    {
        public string[] Lines = new string[0];
        public string[][] Prefix = new string[0][];
        public float[][] Cum = new float[0][];   // cumulative x per char boundary
        public float LineH, Height, Width;
        public int TotalChars;
        public float ParaGap;
        private bool[] paraEnd = new bool[0];

        private static bool NoLineStart(char ch)
        {
            return "，。、；：！？）」』》〉,.;:!?)]%·".IndexOf(ch) >= 0;
        }

        private static bool IsAsciiWord(char ch)
        {
            return ch < 0x2E80 && ch != ' ' && ch != '\n';
        }

        /// <summary>
        /// Lays out text in 'width' px. '\n' starts a new paragraph (extra gap = paraGap x line height). Lines are filled
        /// to width minus one punctuation cell, so closing punctuation that may not start a line hangs inside the width
        /// (never past the box edge), and a paragraph does not end on a one-character line when the line above can give
        /// two characters away (no 「调。」 widows).
        /// </summary>
        public void Layout(Graphics g, string text, Font f, float width, float lineSpacing, float paraGap, StringFormat fmt)
        {
            List<string> lines = new List<string>();
            List<bool> pend = new List<bool>();
            Dictionary<char, float> w = new Dictionary<char, float>();
            BreakLines(g, text, f, width, fmt, w, lines, pend);
            Lines = lines.ToArray();
            paraEnd = pend.ToArray();
            Prefix = new string[Lines.Length][];
            Cum = new float[Lines.Length][];
            TotalChars = 0;
            float maxW = 0;
            for (int l = 0; l < Lines.Length; l++)
            {
                string s = Lines[l];
                Prefix[l] = new string[s.Length + 1];
                Cum[l] = new float[s.Length + 1];
                float x = 0;
                for (int c = 0; c <= s.Length; c++)
                {
                    Prefix[l][c] = s.Substring(0, c);
                    Cum[l][c] = x;
                    if (c < s.Length) x += CharW(g, f, s[c], w, fmt);
                }
                if (x > maxW) maxW = x;
                TotalChars += s.Length;
            }
            LineH = f.GetHeight(g) * lineSpacing;
            ParaGap = LineH * paraGap;
            Width = maxW;
            Height = 0;
            for (int l = 0; l < Lines.Length; l++) Height += LineH + (paraEnd[l] && l < Lines.Length - 1 ? ParaGap : 0);
        }

        /// <summary>
        /// v4.1 (UIALIGN41 #3): the Height that Layout would give this text (same line breaking), without building lines,
        /// prefixes or widths -- the frame measures every step this way to pick one layout for all of them. 'cache' holds
        /// the character widths of this font and is shared between calls.
        /// </summary>
        public static float MeasureHeight(Graphics g, string text, Font f, float width, float lineSpacing, float paraGap, StringFormat fmt,
            Dictionary<char, float> cache)
        {
            List<string> lines = new List<string>();
            List<bool> pend = new List<bool>();
            BreakLines(g, text, f, width, fmt, cache ?? new Dictionary<char, float>(), lines, pend);
            float lh = f.GetHeight(g) * lineSpacing, gap = lh * paraGap, h = 0;
            for (int l = 0; l < lines.Count; l++) h += lh + (pend[l] && l < lines.Count - 1 ? gap : 0);
            return h;
        }

        /// <summary>Line breaking shared by Layout and MeasureHeight: lines and, per line, whether it ends a paragraph.</summary>
        private static void BreakLines(Graphics g, string text, Font f, float width, StringFormat fmt, Dictionary<char, float> w,
            List<string> lines, List<bool> pend)
        {
            if (text == null) text = "";
            float hang = CharW(g, f, '，', w, fmt);
            float limit = Math.Max(width * 0.5f, width - hang);
            string[] paras = text.Split('\n');
            List<string> pl = new List<string>();
            for (int pi = 0; pi < paras.Length; pi++)
            {
                string p = paras[pi];
                // units: one CJK char, or an ASCII run without spaces, or a space
                List<string> units = new List<string>();
                int i = 0;
                while (i < p.Length)
                {
                    char ch = p[i];
                    if (IsAsciiWord(ch))
                    {
                        int j = i;
                        while (j < p.Length && IsAsciiWord(p[j])) j++;
                        units.Add(p.Substring(i, j - i));
                        i = j;
                    }
                    else { units.Add(p.Substring(i, 1)); i++; }
                }
                pl.Clear();
                string cur = "";
                float curW = 0;
                for (int u = 0; u < units.Count; u++)
                {
                    string un = units[u];
                    float uw = 0;
                    foreach (char ch in un) uw += CharW(g, f, ch, w, fmt);
                    bool fits = curW + uw <= limit || cur.Length == 0 || (un.Length == 1 && NoLineStart(un[0]));
                    if (!fits)
                    {
                        pl.Add(cur.TrimEnd());
                        cur = un == " " ? "" : un;
                        curW = un == " " ? 0 : uw;
                    }
                    else { cur += un; curW += uw; }
                }
                pl.Add(cur.TrimEnd());
                FixWidow(pl, g, f, w, fmt, limit);
                for (int l = 0; l < pl.Count; l++) { lines.Add(pl[l]); pend.Add(l == pl.Count - 1); }
            }
        }

        /// <summary>Characters of s that are not closing punctuation or spaces.</summary>
        private static int Visible(string s)
        {
            int n = 0;
            foreach (char ch in s) if (ch != ' ' && !NoLineStart(ch)) n++;
            return n;
        }

        /// <summary>A paragraph's last line with fewer than 2 visible characters takes characters from the end of the line
        /// above until it has 3 (never splitting an ASCII word, never starting with closing punctuation, never making the
        /// line above shorter than 6 characters). Unchanged when that is not possible.</summary>
        private static void FixWidow(List<string> pl, Graphics g, Font f, Dictionary<char, float> w, StringFormat fmt, float limit)
        {
            int n = pl.Count;
            if (n < 2) return;
            string last = pl[n - 1], prev = pl[n - 2];
            if (last.Length == 0 || Visible(last) >= 2) return;
            for (int guard = 0; guard < 6 && (Visible(last) < 3 || NoLineStart(last[0])) && prev.Length > 6; guard++)
            {
                char c = prev[prev.Length - 1];
                if (c == ' ') { prev = prev.Substring(0, prev.Length - 1); continue; }
                if (IsAsciiWord(c) && prev.Length >= 2 && IsAsciiWord(prev[prev.Length - 2])) return; // inside an ASCII word
                prev = prev.Substring(0, prev.Length - 1);
                last = c + last;
            }
            if (NoLineStart(last[0]) || Visible(last) < 2) return;
            float lw = 0;
            foreach (char ch in last) lw += CharW(g, f, ch, w, fmt);
            if (lw > limit) return;
            pl[n - 2] = prev.TrimEnd();
            pl[n - 1] = last;
        }

        private static float CharW(Graphics g, Font f, char ch, Dictionary<char, float> cache, StringFormat fmt)
        {
            float v;
            if (cache.TryGetValue(ch, out v)) return v;
            if (ch == ' ') v = g.MeasureString("a a", f, PointF.Empty, fmt).Width - g.MeasureString("aa", f, PointF.Empty, fmt).Width;
            else v = g.MeasureString(ch.ToString(), f, PointF.Empty, fmt).Width;
            if (v <= 0) v = f.Size * 0.5f;
            cache[ch] = v;
            return v;
        }

        /// <summary>Draws the first 'chars' characters (all when chars &lt; 0). Returns the cursor position.</summary>
        /// <summary>Characters in the whole lines that end at or above limit when drawn at y (-1 = every line fits);
        /// pass it to Draw to show only whole lines.</summary>
        public int CharsFitting(float y, float limit)
        {
            int n = 0;
            float yy = y;
            for (int l = 0; l < Lines.Length; l++)
            {
                if (yy + LineH > limit) return n;
                n += Lines[l].Length;
                yy += LineH + (paraEnd[l] && l < Lines.Length - 1 ? ParaGap : 0);
            }
            return -1;
        }

        public PointF Draw(Graphics g, Font f, Brush b, float x, float y, int chars, StringFormat fmt)
        {
            int left = chars < 0 ? int.MaxValue : chars;
            float yy = y;
            PointF cursor = new PointF(x, y);
            for (int l = 0; l < Lines.Length; l++)
            {
                string s = Lines[l];
                if (left <= 0) break;
                int n = Math.Min(left, s.Length);
                if (n > 0) g.DrawString(Prefix[l][n], f, b, x, yy, fmt);
                cursor = new PointF(x + Cum[l][n], yy);
                left -= n;
                if (n < s.Length) break;
                yy += LineH + (paraEnd[l] && l < Lines.Length - 1 ? ParaGap : 0);
                if (left <= 0) cursor = new PointF(x, yy);
            }
            return cursor;
        }
    }

    /// <summary>What a point on the canvas hits.</summary>
    public enum TutorialHit { None, Prev, Next, Skip, Exit, Dot, SettingsLink, Expander, Text, Scene, DevNote, FieldArrow }

    /// <summary>
    /// The tutorial window's canvas renderer and state machine (steps, typing, hover, panel scrolling). Host-independent.
    /// Layout scale = DPI scale (v4.3.1: fixed text size); fonts come from gx.F (made once by the host).
    /// The text panel never shrinks text below the readability minimums: on short windows it uses compact input rows, folds
    /// 「开发者的话」 to one line (click = card), and finally scrolls (mouse wheel; input controls follow).
    /// </summary>
    public sealed class TutorialFrame : IDisposable
    {
        public readonly TutorialContext C;
        public readonly TutorialGfx Gx;
        public readonly List<TutorialStep> Steps;
        public int Index { get { return index; } }
        /// <summary>The current step (null before the first Go).</summary>
        public TutorialStep Current { get { return index >= 0 && index < Steps.Count ? Steps[index] : null; } }
        /// <summary>Raised when the input rows moved (step change, expander, scroll, resize): re-place the controls.</summary>
        public event Action LayoutChanged;
        /// <summary>Harness only: draw placeholder boxes where the form puts real input controls.</summary>
        public bool DrawFieldGhosts;

        private int index = -1;
        private readonly bool[] visited, done, skipped;
        private readonly bool[] expanded;
        private double typeMs;           // ms since the step's text started
        private bool textInstant;
        private float k = 1f;
        private SizeF client;

        // layout
        private RectangleF rHeader, rScene, rTerm, rPanel, rFooter, rPrompt;
        private RectangleF bPrev, bNext, bSkip, bExit, rLink, rExpander, rTextArea, rDevToggle, rDevCard;
        private RectangleF[] dotRects = new RectangleF[0];
        private RectangleF[] fieldLabel = new RectangleF[0], fieldCtl = new RectangleF[0], fieldArrow = new RectangleF[0];
        private bool[] fieldShown = new bool[0];
        private bool[] zeroNeg = new bool[0];   // 百分比偏移 fields at 0: the direction the arrow shows (true = ← / ↓)
        private float panelTextBottom, fieldsTop, contentBottom;
        private bool panelOverflow, compact, devCollapsed, devOpen, scrollable;
        private float scroll;            // panel scroll (px) when scrollable

        // text
        private readonly TutorialTextBlock tbTitle = new TutorialTextBlock(), tbBody = new TutorialTextBlock(),
            tbNote = new TutorialTextBlock(), tbDev = new TutorialTextBlock(), tbFieldNote = new TutorialTextBlock(), tbPrompt = new TutorialTextBlock();
        private string sTitle, sBody, sNote, sDev, sFieldNote, sPrompt, sLink, sStepCount, sChapter;
        private int promptShown = int.MinValue;
        // Text is measured on this bitmap at the DPI of the surface it is drawn on (fonts are in points, so a 96-DPI measure
        // of text drawn on a 144-DPI screen would lay out lines 1.5x too close: overlapping text at 150 %).
        private readonly Bitmap measureBmp = new Bitmap(4, 4);
        private Graphics measureG;
        private float measureDpi;
        private float devTitleH;         // 「开发者的话」 heading height (layout and drawing use the same value)
        private readonly TutorialRng rng = new TutorialRng(777);
        private static readonly string[] Glyphs = { "0", "1", "7", "#", "%", "@", "&", "*", "/", "\\", "+", "=", "?", "$", "Σ", "Δ", "∴", "Ж", "Ψ", "ロ" };
        private const string PromptLead = "现在：";
        private const string LExit = "退出教程", LSkip = "跳过本步", LPrev = "上一步", LNext = "下一步", LNextReady = "下一步 ▶", LFinish = "完成，去精调";

        // pointer
        public PointF Mouse = new PointF(-1, -1);
        public bool MouseDown;
        private double clockMs;

        public const float TypeCps = 42f; // body chars per second

        /// <summary>v4.1: 「首次设置教程」 or, when the tutorial was finished before this window opened, 「设置教程」.</summary>
        private readonly string headerTitle;

        public TutorialFrame(TutorialContext c, TutorialGfx gx, List<TutorialStep> steps)
        {
            C = c; Gx = gx; Steps = steps;
            headerTitle = TutorialTitle.For(c != null && c.G != null && c.G.TutorialDone);
            visited = new bool[steps.Count]; done = new bool[steps.Count]; skipped = new bool[steps.Count];
            expanded = new bool[steps.Count];
            MakeMeasureGraphics(96f);
        }

        private void MakeMeasureGraphics(float dpi)
        {
            if (measureG != null) measureG.Dispose();
            measureBmp.SetResolution(dpi, dpi);
            measureG = Graphics.FromImage(measureBmp);
            measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            measureDpi = dpi;
        }

        /// <summary>The DPI of the surface the frame is drawn on (TutorialForm: the screen DPI). Text is measured at this DPI.
        /// Draw() also follows the paint Graphics' DPI by itself, so this only avoids one wrong first layout.</summary>
        public void SetMeasureDpi(float dpi)
        {
            if (!(dpi >= 48f && dpi <= 960f)) dpi = 96f;
            if (Math.Abs(dpi - measureDpi) < 0.5f) return;
            MakeMeasureGraphics(dpi);
            planOk = false;
            if (client.Width > 0) { float keep = scroll; Relayout(); scroll = keep; ClampScroll(); }
        }

        public void Dispose()
        {
            TutorialStep s = Current;
            if (s != null) { try { s.Leave(C); } catch (Exception) { } }
            if (measureG != null) measureG.Dispose();
            measureBmp.Dispose();
            identity.Dispose();
        }

        public bool Visited(int i) { return i >= 0 && i < visited.Length && visited[i]; }
        public bool IsLast { get { return index == Steps.Count - 1; } }
        /// <summary>True when the panel content is taller than the panel (it scrolls).</summary>
        public bool Scrollable { get { return scrollable; } }
        /// <summary>True when the panel content had to scroll (tests: should only happen on very small windows).</summary>
        public bool PanelOverflow { get { return panelOverflow; } }
        public bool DevNoteFolded { get { return devCollapsed; } }

        // ------------------------------------------------------------------ navigation
        public void Go(int i)
        {
            if (i < 0 || i >= Steps.Count) return;
            TutorialStep old = Current;
            if (old != null)
            {
                if (old.Done) done[index] = true;
                try { old.Leave(C); } catch (Exception) { }
            }
            index = i;
            C.Revisit = visited[i];
            visited[i] = true;
            C.StepMs = 0;
            C.Arbiter.Reset();
            C.Term.Clear();
            TutorialStep s = Steps[i];
            s.PromptIndex = 0;
            s.Done = false;
            try { s.Enter(C); } catch (Exception) { }
            if (s.Terminal != null) foreach (string line in s.Terminal) C.Term.Push(C.Fill(line));
            typeMs = 0;
            textInstant = C.Revisit;
            scroll = 0;
            devOpen = false;
            int nfz = s.Fields == null ? 0 : s.Fields.Length;
            zeroNeg = new bool[nfz];
            for (int z = 0; z < nfz; z++) zeroNeg[z] = s.Fields[z].ZeroSign < 0;
            Relayout();
        }

        public void Next(bool skip)
        {
            TutorialStep s = Current;
            if (s != null)
            {
                if (!skip && s.DoneOnNext) s.Done = true; // a read-and-continue step: 下一步 is its task
                if (skip && !s.Done) skipped[index] = true;
                if (s.Done) { done[index] = true; skipped[index] = false; }
            }
            if (index < Steps.Count - 1) Go(index + 1);
        }

        public void Prev() { if (index > 0) Go(index - 1); }

        /// <summary>Completes the typing at once (click on the text panel).</summary>
        public void FinishTyping() { textInstant = true; }

        public void ToggleExpander()
        {
            if (index < 0) return;
            expanded[index] = !expanded[index];
            LayoutPanel();
            Raise();
        }

        public bool Expanded { get { return index >= 0 && expanded[index]; } }

        /// <summary>Opens / closes the folded 开发者的话 card.</summary>
        public void ToggleDevNote()
        {
            if (!devCollapsed) return;
            devOpen = !devOpen;
            LayoutPanel();
            Raise();
        }

        /// <summary>Mouse wheel over the panel scrolls it (when it is taller than the panel). Returns true when used.</summary>
        public bool Wheel(PointF p, int delta)
        {
            if (!scrollable || !rPanel.Contains(p)) return false;
            float before = scroll;
            scroll -= delta / 120f * S(48);
            ClampScroll();
            if (scroll != before) Raise();
            return true;
        }

        private void ClampScroll()
        {
            float max = Math.Max(0, contentBottom + S(18) - rPanel.Bottom);
            if (scroll > max) scroll = max;
            if (scroll < 0 || !scrollable) scroll = 0;
        }

        private void Raise() { Action h = LayoutChanged; if (h != null) h(); }

        // ------------------------------------------------------------------ layout
        /// <summary>Client size in px and the layout scale (DPI scale). Fonts must already match (gx.F).</summary>
        public void SetClient(SizeF size, float scale)
        {
            // non-finite input (a broken host value) must not poison the whole layout with NaN (v4.1)
            if (!TutorialGfx.Fin(size.Width, size.Height) || size.Width < 0 || size.Height < 0) size = SizeF.Empty;
            client = size;
            k = (scale > 0 && scale < 100f) ? scale : 1f;
            Gx.K = k;
            C.K = k;
            planOk = false; // new size / scale / fonts: pick the one layout for all steps again
            Relayout();
        }

        private float S(float v) { return v * k; }

        private void Relayout()
        {
            TutorialStep s = Current;
            if (s != null)
            {
                sTitle = C.Fill(s.Title ?? "");
                sBody = C.Fill(s.Body ?? "");
                sNote = string.IsNullOrEmpty(s.Note) ? null : C.Fill(s.Note);
                sDev = string.IsNullOrEmpty(s.DevNote) ? null : C.Fill(s.DevNote);
                sFieldNote = string.IsNullOrEmpty(s.FieldNote) ? null : C.Fill(s.FieldNote);
                sLink = string.IsNullOrEmpty(s.SettingsPage) ? null : "去「" + s.SettingsPage + "」页 →";
                // the chapter lives in the header next to the only step counter; a chapter equal to the title is not repeated
                sChapter = (string.IsNullOrEmpty(s.Chapter) || s.Chapter == s.Title) ? "" : s.Chapter;
                sStepCount = (sChapter.Length > 0 ? sChapter + " · " : "") + "步骤 " + (index + 1) + " / " + Steps.Count;
            }
            // the footer prompt is laid out again for this step at the real width (also after 上一步 / a resize)
            promptShown = int.MinValue;
            sPrompt = null;
            promptLaidW = -1;
            if (client.Width <= 0 || client.Height <= 0 || s == null)
            {
                panelFrac = PlanFrac[0]; panelCap = PlanCap[0]; termMin = 60f; compact = false;
                ComputeRects(); Raise(); return;
            }
            // fit (audit P1-1, v4.1 UIALIGN41 #3): one split / row size / terminal height for ALL steps (MakePlan), so the panel
            // keeps its place on 下一步. Only this step's own extras (optional rows opened, a longer 「当前：」 note) can still
            // need compact rows or a scrolling panel. 「开发者的话」 stays visible; it is folded to one line only on a panel
            // shorter than FoldBelowPanelH design px (0 = never).
            if (!planOk) MakePlan();
            panelFrac = planFrac; panelCap = planCap; termMin = planTermMin;
            compact = planCompact; devCollapsed = false;
            Refit();
            if (panelOverflow && !compact) { compact = true; LayoutPanel(); }
            if (panelOverflow && sDev != null && rPanel.Height / k < FoldBelowPanelH) { devCollapsed = true; LayoutPanel(); }
            Raise();
        }

        /// <summary>
        /// v4.1 (UIALIGN41 #3): picks the layout every step uses at this window size -- the narrowest panel split (then compact
        /// input rows) at which no step has to scroll in its normal view, else the one where the worst step scrolls least --
        /// and the tallest footer prompt of any step. Cost: one text measure per step and candidate, on resize only.
        /// </summary>
        private void MakePlan()
        {
            planOk = true;
            planPromptH = 0;
            float promptW = PromptWidth();
            Dictionary<char, float> bodyW = new Dictionary<char, float>();
            foreach (TutorialStep st in Steps)
            {
                if (st == null) continue;
                if (st.Prompts != null) foreach (string pr in st.Prompts) planPromptH = Math.Max(planPromptH, PromptHeight(pr, promptW, bodyW));
                planPromptH = Math.Max(planPromptH, PromptHeight(st.Hint, promptW, bodyW));
            }
            Dictionary<Font, Dictionary<char, float>> widths = new Dictionary<Font, Dictionary<char, float>>();
            int best = 0; bool bestCompact = false; float bestOver = float.MaxValue;
            for (int c = 0; c < PlanFrac.Length && bestOver > 0; c++)
                for (int cm = 0; cm < 2 && bestOver > 0; cm++)
                {
                    panelFrac = PlanFrac[c]; panelCap = PlanCap[c]; termMin = 60f;
                    ComputeRects();
                    float over = float.MinValue;
                    foreach (TutorialStep st in Steps) if (st != null) over = Math.Max(over, ProbeOverflow(st, cm == 1, widths));
                    if (over < bestOver - S(0.5f)) { bestOver = over; best = c; bestCompact = cm == 1; }
                }
            planFrac = PlanFrac[best]; planCap = PlanCap[best]; planCompact = bestCompact;
            planTermMin = bestOver > 0 ? 44f : 60f; // the window is too small for some step: give the scene the terminal's spare rows
        }

        /// <summary>Height of one footer prompt as UpdatePrompt lays it out (PromptLead + filled text).</summary>
        private float PromptHeight(string prompt, float promptW, Dictionary<char, float> cache)
        {
            if (string.IsNullOrEmpty(prompt)) return 0;
            string t = C.Fill(prompt);
            if (string.IsNullOrEmpty(t)) return 0;
            return TutorialTextBlock.MeasureHeight(measureG, PromptLead + t, Gx.F.Body, Math.Max(S(60), promptW - S(26)), 1.15f, 0, Gx.Near, cache);
        }

        /// <summary>How far step st's panel content would run past the panel (px; negative = room left) at the current rects,
        /// in its normal view (optional rows folded, 开发者的话 open). Same numbers as LayoutText + LayoutPanel.</summary>
        private float ProbeOverflow(TutorialStep st, bool compactRows, Dictionary<Font, Dictionary<char, float>> widths)
        {
            float pad = S(18);
            float innerW = Math.Max(S(120), rPanel.Width - 2 * pad - S(8));
            float titleH = ProbeH(st.Title, Gx.F.Title, innerW, 1.05f, 0, widths);
            float bodyH = ProbeH(st.Body, Gx.F.Body, innerW, 1.2f, 0.1f, widths);
            float noteH = string.IsNullOrEmpty(st.Note) ? -1 : ProbeH(st.Note, Gx.F.Note, innerW, 1.2f, 0.2f, widths);
            float devH = string.IsNullOrEmpty(st.DevNote) ? -1 : ProbeH(st.DevNote, Gx.F.Body, innerW - S(16), 1.2f, 0.3f, widths);
            string fn = st.LayoutFieldNote(C);
            float fnH = string.IsNullOrEmpty(fn) ? -1 : ProbeH(fn, Gx.F.Note, innerW, 1.15f, 0, widths);
            float textBottom = TextBottom(titleH, bodyH, noteH, devH, false);
            float block = FieldBlockH(st.Fields, false, compactRows, fnH, !string.IsNullOrEmpty(st.SettingsPage));
            return (textBottom + S(8)) - (rPanel.Bottom - pad - block);
        }

        private float ProbeH(string text, Font f, float width, float lineSpacing, float paraGap, Dictionary<Font, Dictionary<char, float>> widths)
        {
            Dictionary<char, float> w;
            if (!widths.TryGetValue(f, out w)) { w = new Dictionary<char, float>(); widths[f] = w; }
            return TutorialTextBlock.MeasureHeight(measureG, C.Fill(text ?? ""), f, width, lineSpacing, paraGap, Gx.Near, w);
        }

        /// <summary>Bottom of the panel's text part (title, body, note, 开发者的话) for these heights (-1 = no note / no dev
        /// note). LayoutPanel and the plan probe use the same numbers.</summary>
        private float TextBottom(float titleH, float bodyH, float noteH, float devH, bool devFolded)
        {
            float y = rPanel.Top + S(18);                // (the chapter is shown in the header, next to 步骤 N / M)
            y += titleH + S(8);
            y += bodyH;
            if (noteH >= 0) y += S(8) + noteH;
            if (devH >= 0)
            {
                if (devFolded) y += S(12) + TagLineH() + S(16);
                else y += DevGapAbove() + DevBoxH(TagLineH(), devH);
            }
            return y;
        }

        private float TagLineH() { return Gx.F.Tag.GetHeight(measureG); }

        // input-row geometry (normal / compact); LayoutPanel and the plan probe share it
        private float CtlH() { return Math.Max(S(24), Gx.F.Small.GetHeight(measureG) + S(8)); }
        private float RowH(bool compactRows) { return Math.Max(CtlH() + S(compactRows ? 3 : 8), Gx.F.Small.GetHeight(measureG) * (compactRows ? 1.45f : 1.75f)); }
        private float FieldsHeadH(bool compactRows) { return Gx.F.Tag.GetHeight(measureG) + S(compactRows ? 8 : 14); }
        private float ExpanderH() { return Gx.F.Small.GetHeight(measureG) + S(10); }
        private float LinkH() { return Gx.F.Note.GetHeight(measureG) + S(8); }

        /// <summary>Height of the bottom block: 你的数值 head, shown rows, 可选 expander, field note (fieldNoteH -1 = none), link.</summary>
        private float FieldBlockH(TutorialField[] f, bool exp, bool compactRows, float fieldNoteH, bool hasLink)
        {
            int nf = f == null ? 0 : f.Length, rows = 0;
            bool anyOptional = false;
            for (int i = 0; i < nf; i++) { if (f[i].Optional) anyOptional = true; if ((!f[i].Optional || exp) && !devOpen) rows++; }
            return (nf > 0 ? FieldsHeadH(compactRows) : 0) + rows * RowH(compactRows) + (anyOptional ? ExpanderH() : 0)
                + (fieldNoteH >= 0 ? fieldNoteH + S(6) : 0) + (hasLink ? LinkH() : 0);
        }

        private void Refit() { ComputeRects(); LayoutText(); LayoutPanel(); }

        // panel width = W x panelFrac (cap panelCap design px); terminal min height termMin design px (see Relayout's fit)
        private float panelFrac = 0.36f, panelCap = 470f, termMin = 60f;

        // v4.1 (UIALIGN41 #3): ONE layout for all steps at a window size, so 下一步 never moves the panel, the scene, the
        // terminal or the footer. MakePlan picks it on SetClient / SetMeasureDpi (resize, DPI, fonts), never on a step change.
        private bool planOk;
        private float planFrac = 0.36f, planCap = 470f, planTermMin = 60f;
        private bool planCompact;
        private float planPromptH;       // the tallest footer prompt of any step at this width (the footer never changes height)
        private static readonly float[] PlanFrac = { 0.36f, 0.42f, 0.48f }, PlanCap = { 470f, 560f, 560f };

        /// <summary>Gap between the scene and the text panel, and between the scene and the terminal (UIALIGN41 #9: one
        /// gutter both ways; the outer margin stays 14).</summary>
        private const float Gutter = 12f, Margin = 14f;

        /// <summary>「开发者的话」 is folded (click to open) only when the text panel is shorter than this (design px). 0 = never
        /// (Leo / review: keep Leo's explanation visible at every size; a panel that is too short scrolls instead).</summary>
        public const float FoldBelowPanelH = 0f;
        private float promptLaidW = -1;

        private float ButtonW(string text, float min)
        {
            SizeF sz = measureG.MeasureString(text, Gx.F.Button, PointF.Empty, Gx.Near);
            return Math.Max(S(min), sz.Width + S(30));
        }

        // v4.1 (UIALIGN41 #3): each footer button keeps one width on every step -- 下一步 as wide as its widest label
        // (「完成，去精调」 on the last step), the three others one shared width -- so no button moves on 下一步.
        private float NextButtonW() { return Math.Max(ButtonW(LNextReady, 112), Math.Max(ButtonW(LNext, 112), ButtonW(LFinish, 112))); }
        private float SideButtonW() { return Math.Max(ButtonW(LPrev, 96), Math.Max(ButtonW(LSkip, 96), ButtonW(LExit, 96))); }
        private float PromptWidth()
        {
            return Math.Max(S(120), client.Width - 2 * S(Margin) - (NextButtonW() + 3 * SideButtonW() + 3 * S(10)) - S(24));
        }

        private void ComputeRects()
        {
            float W = client.Width, H = client.Height;
            float m = S(Margin), gut = S(Gutter);
            rHeader = new RectangleF(0, 0, W, S(54));
            // footer: fixed button widths, the prompt gets the rest (may wrap to 2-3 lines); the footer is as tall as the
            // tallest prompt of any step (planPromptH), so it keeps its height on every step
            float wNext = NextButtonW(), wSide = SideButtonW();
            float gap = S(10);
            float promptW = PromptWidth();
            UpdatePrompt(promptW);
            float footH = Math.Max(S(62), Math.Max(planPromptH, tbPrompt.Height) + S(20));
            rFooter = new RectangleF(0, H - footH, W, footH);
            float bh = S(36), by = rFooter.Top + (rFooter.Height - bh) / 2;
            float x = W - m - wNext;
            bNext = new RectangleF(x, by, wNext, bh); x -= gap + wSide;
            bPrev = new RectangleF(x, by, wSide, bh); x -= gap + wSide;
            bSkip = new RectangleF(x, by, wSide, bh); x -= gap + wSide;
            bExit = new RectangleF(x, by, wSide, bh);
            rPrompt = new RectangleF(m, rFooter.Top, promptW, rFooter.Height);

            // outer margin m around everything; ONE gutter between the scene and the panel and between the scene and the
            // terminal (UIALIGN41 #9: it was 14 across and 8 down)
            float top = rHeader.Bottom + m, bottom = rFooter.Top - m;
            float panelW = Math.Max(S(320), Math.Min(S(panelCap), W * panelFrac));
            float sceneW = W - panelW - 2 * m - gut;
            float termH = Math.Min(S(96), Math.Max(S(termMin), (bottom - top) * 0.17f));
            rScene = new RectangleF(m, top, sceneW, Math.Max(S(120), bottom - top - termH - gut));
            rTerm = new RectangleF(m, rScene.Bottom + gut, sceneW, Math.Max(S(40), bottom - rScene.Bottom - gut));
            rPanel = new RectangleF(rScene.Right + gut, top, panelW, bottom - top);
            C.SceneW = rScene.Width / k;
            C.SceneH = rScene.Height / k;
            C.Compact = C.SceneW < TutorialContext.CompactW || C.SceneH < TutorialContext.CompactH;

            // dots in the header centre
            int n = Steps.Count;
            if (dotRects.Length != n) dotRects = new RectangleF[n];
            float dw = S(16), dgap = S(6);
            float total = n * dw + (n - 1) * dgap;
            float dx = (W - total) / 2, dy = rHeader.Top + (rHeader.Height - S(16)) / 2;
            for (int i = 0; i < n; i++) dotRects[i] = new RectangleF(dx + i * (dw + dgap), dy, dw, S(16));
        }

        private void LayoutText()
        {
            float pad = S(18);
            float innerW = Math.Max(S(120), rPanel.Width - 2 * pad - S(8));
            // line height = the font's own line spacing (already ~1.3 em for CJK fonts) x a small factor
            tbTitle.Layout(measureG, sTitle, Gx.F.Title, innerW, 1.05f, 0, Gx.Near);
            tbBody.Layout(measureG, sBody, Gx.F.Body, innerW, 1.2f, 0.1f, Gx.Near);
            tbNote.Layout(measureG, sNote ?? "", Gx.F.Note, innerW, 1.2f, 0.2f, Gx.Near);
            tbDev.Layout(measureG, sDev ?? "", Gx.F.Body, innerW - S(16), 1.2f, 0.3f, Gx.Near);
            tbFieldNote.Layout(measureG, sFieldNote ?? "", Gx.F.Note, innerW, 1.15f, 0, Gx.Near);
        }

        private void LayoutPanel()
        {
            TutorialStep s = Current;
            float pad = S(18);
            float innerW = rPanel.Width - 2 * pad;
            rDevToggle = RectangleF.Empty;
            devTitleH = TagLineH();
            float y = TextBottom(tbTitle.Height, tbBody.Height, sNote != null ? tbNote.Height : -1, sDev != null ? tbDev.Height : -1,
                devCollapsed);
            if (sDev != null && devCollapsed) rDevToggle = new RectangleF(rPanel.Left + pad, y - devTitleH - S(16), innerW, devTitleH + S(16));
            panelTextBottom = y;
            rTextArea = new RectangleF(rPanel.Left, rPanel.Top, rPanel.Width, y - rPanel.Top);

            // fields: bottom-aligned block (or right after the text when the panel scrolls)
            TutorialField[] f = s == null ? null : s.Fields;
            int nf = f == null ? 0 : f.Length;
            if (fieldLabel.Length != nf) { fieldLabel = new RectangleF[nf]; fieldCtl = new RectangleF[nf]; fieldArrow = new RectangleF[nf]; fieldShown = new bool[nf]; }
            if (zeroNeg.Length != nf) { zeroNeg = new bool[nf]; for (int z = 0; z < nf; z++) zeroNeg[z] = f[z].ZeroSign < 0; }
            bool anyOptional = false;
            for (int i = 0; i < nf; i++) if (f[i].Optional) anyOptional = true;
            bool exp = index >= 0 && expanded[index];
            int rows = 0;
            for (int i = 0; i < nf; i++) { fieldShown[i] = (!f[i].Optional || exp) && !devOpen; if (fieldShown[i]) rows++; }
            float ctlH = CtlH();
            float rowH = RowH(compact);
            float head = FieldsHeadH(compact);
            float expH = ExpanderH();
            float linkH = LinkH();
            float block = FieldBlockH(f, exp, compact, sFieldNote != null ? tbFieldNote.Height : -1, sLink != null);
            float fy = rPanel.Bottom - pad - block;
            panelOverflow = fy < panelTextBottom + S(8);
            scrollable = panelOverflow;
            if (panelOverflow) fy = panelTextBottom + S(8);
            fieldsTop = fy;
            float yy = fy + (nf > 0 ? head : 0);
            float ctlW = Math.Max(S(92), Math.Max(measureG.MeasureString("-0000", Gx.F.Small, PointF.Empty, Gx.Near).Width,
                measureG.MeasureString("000.00", Gx.F.Small, PointF.Empty, Gx.Near).Width) + S(36));
            float unitW = S(30);
            // 百分比偏移 fields: an arrow (direction toggle) left of the number box
            float arrowW = Math.Max(ctlH, measureG.MeasureString(OffsetUnits.Left, Gx.F.Button, PointF.Empty, Gx.Near).Width + S(14));
            for (int i = 0; i < nf; i++)
            {
                if (!fieldShown[i]) { fieldLabel[i] = RectangleF.Empty; fieldCtl[i] = RectangleF.Empty; fieldArrow[i] = RectangleF.Empty; continue; }
                bool pct = f[i].Pct != TutorialField.PctNone && f[i].Kind == TutorialField.KindNumber;
                float aw = pct ? arrowW + S(4) : 0;
                // labels sit FieldInset inside the content column, like the text in the 开发者的话 card (UIALIGN41 #16)
                fieldLabel[i] = new RectangleF(rPanel.Left + pad + S(FieldInset), yy, innerW - ctlW - unitW - S(6) - aw - S(FieldInset), rowH);
                float cw = f[i].Kind == TutorialField.KindCheck ? S(22) : ctlW;
                fieldCtl[i] = new RectangleF(rPanel.Right - pad - unitW - cw, yy + (rowH - ctlH) / 2, cw, ctlH);
                fieldArrow[i] = pct ? new RectangleF(fieldCtl[i].Left - S(4) - arrowW, fieldCtl[i].Top, arrowW, ctlH) : RectangleF.Empty;
                yy += rowH;
            }
            // the 可选 expander's triangle starts on the label column
            rExpander = anyOptional ? new RectangleF(rPanel.Left + pad + S(FieldInset - 1), yy + S(2), innerW - S(FieldInset - 1), expH - S(4)) : RectangleF.Empty;
            if (anyOptional) yy += expH;
            if (sFieldNote != null) yy += tbFieldNote.Height + S(6);
            rLink = sLink != null ? new RectangleF(rPanel.Left + pad, yy, Math.Min(innerW, measureG.MeasureString(sLink, Gx.F.Note, PointF.Empty, Gx.Near).Width + S(8)), linkH) : RectangleF.Empty;
            yy += sLink != null ? linkH : 0;
            contentBottom = Math.Max(yy, panelTextBottom);
            float cardTop = rPanel.Top + pad + tbTitle.Height;
            rDevCard = devOpen ? new RectangleF(rPanel.Left + S(8), cardTop, rPanel.Width - S(16),
                Math.Min(rPanel.Bottom - S(8) - cardTop, devTitleH + S(30) + tbDev.Height + S(16))) : RectangleF.Empty;
            ClampScroll();
        }

        // 「开发者的话」 box geometry (layout and drawing use the same numbers)
        private float DevGapAbove() { return S(14); }
        private float DevBoxH(float titleH, float textH) { return S(6) + titleH + S(6) + textH + S(8); }
        private float DevTextTop(float titleH) { return S(6) + titleH + S(6); }
        /// <summary>Field labels (and the 可选 expander) start this far inside the content column, the same inset as the text
        /// in the 开发者的话 card; the main field's highlight strip spans exactly the column (UIALIGN41 #16).</summary>
        private const float FieldInset = 12f;

        /// <summary>Re-fills the step's text (placeholders such as {当前上抬} after a field edit) and lays it out again,
        /// keeping the typing progress and the panel scroll.</summary>
        public void RefreshText()
        {
            if (Current == null) return;
            float keep = scroll;
            Relayout();
            scroll = keep;
            ClampScroll();
        }

        /// <summary>Where the host puts the control of field i (Empty = hidden, also when scrolled out of the panel).</summary>
        public RectangleF FieldRect(int i)
        {
            if (i < 0 || i >= fieldCtl.Length || !fieldShown[i]) return RectangleF.Empty;
            RectangleF r = fieldCtl[i];
            if (!InView(r.Top, r.Bottom)) return RectangleF.Empty;
            if (scrollable) r.Y -= scroll;
            return r;
        }

        /// <summary>True when content rows top..bottom (panel content coordinates) are fully visible: inside the scrolled panel
        /// and above the bottom fade. A row that is not is neither drawn nor given its control (no half-visible rows).</summary>
        private bool InView(float top, float bottom)
        {
            if (!scrollable) return true;
            return top >= rPanel.Top + S(2) + scroll && bottom <= ViewBottom() + scroll;
        }

        /// <summary>chars (-1 = all) limited to the whole lines above the visible bottom while the panel scrolls, so the fade
        /// never cuts through a text line.</summary>
        private int WholeLines(TutorialTextBlock tb, float y, int chars)
        {
            if (!scrollable) return chars;
            int fit = tb.CharsFitting(y, ViewBottom() + scroll);
            if (fit < 0) return chars;
            return chars < 0 ? fit : Math.Min(chars, fit);
        }

        /// <summary>Bottom of the visible panel area in screen coordinates (above the fade while there is more below).</summary>
        private float ViewBottom()
        {
            float max = Math.Max(0, contentBottom + S(18) - rPanel.Bottom);
            return scroll < max - 1 ? rPanel.Bottom - S(15) : rPanel.Bottom - S(2);
        }

        public RectangleF SceneRect { get { return rScene; } }

        private void UpdatePrompt(float width)
        {
            TutorialStep s = Current;
            if (s == null) return;
            int pi = (s.Prompts != null && s.Prompts.Length > 0) ? Math.Max(0, Math.Min(s.PromptIndex, s.Prompts.Length - 1)) : -1;
            if (pi == promptShown && sPrompt != null && width == promptLaidW) return; // same prompt at the same width
            promptShown = pi;
            promptLaidW = width;
            sPrompt = pi >= 0 ? C.Fill(s.Prompts[pi]) : C.Fill(s.Hint ?? "");
            if (!string.IsNullOrEmpty(sPrompt)) sPrompt = PromptLead + sPrompt;   // v4.1: every step says what to do now
            tbPrompt.Layout(measureG, sPrompt ?? "", Gx.F.Body, Math.Max(S(60), width - S(26)), 1.15f, 0, Gx.Near);
        }

        // ------------------------------------------------------------------ frame
        public void Update(double dtMs)
        {
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            if (dtMs > 100) dtMs = 100;
            clockMs += dtMs;
            Gx.TimeMs = clockMs;
            typeMs += dtMs;
            C.StepMs += dtMs;
            C.Term.Update(dtMs);
            C.Arbiter.Update(dtMs, C.LiveMask != 0);
            TutorialStep s = Current;
            if (s == null) return;
            try { s.Update(C, dtMs); } catch (Exception) { }
            if (s.Done) done[index] = true;
            int pi = (s.Prompts != null && s.Prompts.Length > 0) ? Math.Max(0, Math.Min(s.PromptIndex, s.Prompts.Length - 1)) : -1;
            if (pi != promptShown)
            {
                float oldH = tbPrompt.Height;
                UpdatePrompt(rPrompt.Width);
                if (tbPrompt.Height != oldH) { float keep = scroll; Relayout(); scroll = keep; ClampScroll(); } // footer height changed
            }
        }

        private int CharsShown(TutorialTextBlock tb, double startMs)
        {
            if (textInstant) return -1;
            double t = typeMs - startMs;
            if (t <= 0) return 0;
            int n = (int)(t * TypeCps / 1000.0);
            return n >= tb.TotalChars ? -1 : n;
        }

        /// <summary>Harness: the most tags (pills) one view of the scene showed in the last Draw (Leo: at most
        /// TutorialPlace.MaxTagsPerView), and which tags the scene drew (filled only when RecordTagList is set).</summary>
        public int LastMaxTagsPerView;
        public string LastSceneTags;
        public bool RecordTagList;

        private readonly System.Drawing.Drawing2D.Matrix identity = new System.Drawing.Drawing2D.Matrix();

        /// <summary>Resets clip THEN transform (v4.1, brute-ui F5: libgdiplus fails ResetTransform while a step's clip is
        /// invalid, e.g. built from NaN geometry); falls back to setting the identity matrix. Never throws.</summary>
        private void SafeReset(Graphics g)
        {
            try { g.ResetClip(); } catch (Exception) { }
            try { g.ResetTransform(); }
            catch (Exception) { try { g.Transform = identity; } catch (Exception) { } }
        }

        public void Draw(Graphics g)
        {
            // v4.1 (brute-ui F5): nothing a step draws may stop the window from painting (an exception in OnPaint shows the
            // red-X / crash dialog on Windows), so every part is guarded and the Graphics state is reset between parts
            try
            {
                // the layout must be measured at the DPI this frame is drawn at (once, when it differs)
                if (Math.Abs(g.DpiY - measureDpi) >= 0.5f) SetMeasureDpi(g.DpiY);
                Gx.BeginFrame(g);
                Gx.Mouse = Mouse;
                TutorialPalette p = Gx.Pal;
                g.Clear(p.Bg);
                TutorialStep s = Current;
                if (s == null) return;
                Gx.TextBg = p.Panel;
                try { DrawHeader(g); } catch (Exception) { SafeReset(g); }
                Gx.TextBg = p.SceneBg;
                Gx.BeginScene();
                try { s.Draw(Gx, C, rScene); } catch (Exception) { }
                SafeReset(g);
                try { Gx.FlushLate(rScene); } catch (Exception) { SafeReset(g); }
                LastMaxTagsPerView = Gx.MaxTagsInAView(rScene);
                if (RecordTagList) LastSceneTags = Gx.SceneTagList();
                Gx.EndScene();
                try { Gx.HudFrame(new RectangleF(rScene.X - S(3), rScene.Y - S(3), rScene.Width + S(6), rScene.Height + S(6)), TutorialGfx.A(p.Neon, 160), 16); }
                catch (Exception) { SafeReset(g); }
                Gx.TextBg = Color.FromArgb(255, 8, 10, 12);
                try { DrawTerminal(g); } catch (Exception) { SafeReset(g); }
                Gx.TextBg = p.Panel;
                try { DrawPanel(g); } catch (Exception) { SafeReset(g); }
                try { DrawFooter(g); } catch (Exception) { SafeReset(g); }
            }
            catch (Exception) { SafeReset(g); }
        }

        private void DrawHeader(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            g.FillRectangle(Gx.B(p.Panel), rHeader);
            g.DrawLine(Gx.P(p.Border, 1f), rHeader.Left, rHeader.Bottom - 0.5f, rHeader.Right, rHeader.Bottom - 0.5f);
            float frac = (index + 1) / (float)Steps.Count;
            g.FillRectangle(Gx.B(TutorialGfx.A(p.Neon, 60)), rHeader.Left, rHeader.Bottom - S(3), rHeader.Width * frac, S(3));
            g.FillRectangle(Gx.B(p.Neon), rHeader.Left, rHeader.Bottom - S(1.5f), rHeader.Width * frac, S(1.5f));
            float lx = S(16), ly = rHeader.Top + rHeader.Height / 2;
            Gx.Ring(lx + S(9), ly, S(15), p.Neon, Color.Black, S(2));
            Gx.TextMid(headerTitle, Gx.F.Big, p.Text, lx + S(26), ly, 0);
            float tw = Gx.Measure(headerTitle, Gx.F.Big).Width;
            float subX = lx + S(36) + tw;
            if (dotRects.Length > 0 && subX + Gx.Measure("TARKOV HIP AIMER", Gx.F.MonoSmall).Width < dotRects[0].Left - S(16))
                Gx.TextMid("TARKOV HIP AIMER", Gx.F.MonoSmall, p.Muted, subX, ly, 0);
            for (int i = 0; i < dotRects.Length; i++)
            {
                RectangleF r = dotRects[i];
                float cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
                bool cur = i == index, hover = r.Contains(Mouse);
                Color c = done[i] ? p.Neon : (skipped[i] ? p.Accent : (visited[i] ? p.Text : p.Muted));
                if (cur)
                {
                    double pulse = 0.5 + 0.5 * Math.Sin(clockMs / 260.0);
                    Gx.Dot(TutorialGfx.A(p.Neon, (int)(40 + 50 * pulse)), cx, cy, S(8));
                    Gx.Dot(p.Neon, cx, cy, S(4.2f));
                }
                else if (done[i] || visited[i] || skipped[i]) Gx.Dot(TutorialGfx.A(c, hover ? 255 : 200), cx, cy, S(3.4f));
                else g.DrawEllipse(Gx.P(TutorialGfx.A(c, hover ? 255 : 170), 1.2f), cx - S(3.4f), cy - S(3.4f), S(6.8f), S(6.8f));
            }
            Gx.TextMid(sStepCount, Gx.F.Small, p.Text, rHeader.Right - S(18), ly, 2);
            for (int i = 0; i < dotRects.Length; i++)
            {
                if (!dotRects[i].Contains(Mouse)) continue;
                string t = (i + 1) + " · " + Steps[i].Title; // hover only (allocates while hovering a dot)
                SizeF sz = Gx.Measure(t, Gx.F.Small);
                float tx = Math.Min(Math.Max(S(4), dotRects[i].Left + dotRects[i].Width / 2 - sz.Width / 2 - S(6)), client.Width - sz.Width - S(16));
                RectangleF tip = new RectangleF(tx, dotRects[i].Bottom + S(6), sz.Width + S(12), sz.Height + S(6));
                g.FillRectangle(Gx.B(p.Surface), tip);
                g.DrawRectangle(Gx.P(p.Border, 1f), tip.X, tip.Y, tip.Width, tip.Height);
                Gx.TextBg = p.Surface;
                Gx.Text(t, Gx.F.Small, p.Text, tip.Left + S(6), tip.Top + S(3), 0);
                Gx.TextBg = p.Panel;
            }
        }

        private void DrawTerminal(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            RectangleF r = rTerm;
            g.FillRectangle(Gx.B(Color.FromArgb(255, 8, 10, 12)), r);
            g.DrawRectangle(Gx.P(TutorialGfx.A(p.Neon, 70), 1f), r.X, r.Y, r.Width, r.Height);
            g.SetClip(r);
            float lh = Gx.F.Mono.GetHeight(g) * 1.12f;
            int fit = Math.Max(1, (int)((r.Height - S(8)) / lh));
            TutorialTerminal t = C.Term;
            int first = Math.Max(0, t.Count - fit);
            float y = r.Top + S(4);
            for (int i = first; i < t.Count; i++)
            {
                string s = t.Visible(i);
                bool last = i == t.Count - 1;
                Gx.Text(s, Gx.F.Mono, last ? p.Neon : TutorialGfx.Mix(p.Neon, p.Bg, 0.25), r.Left + S(10), y, 0);
                if (t.Typing(i) || (last && t.Idle && ((int)(clockMs / 450)) % 2 == 0))
                {
                    SizeF sz = Gx.Measure(s, Gx.F.Mono);
                    g.FillRectangle(Gx.B(p.Neon), r.Left + S(10) + sz.Width + S(2), y + S(2), S(8), lh - S(6));
                }
                y += lh;
            }
            g.ResetClip();
        }

        private PointF PanelMouse()
        {
            PointF m = Mouse;
            if (scrollable && rPanel.Contains(m)) m.Y += scroll;
            return m;
        }

        private void DrawPanel(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            TutorialStep s = Current;
            RectangleF r = rPanel;
            g.FillRectangle(Gx.B(p.Panel), r);
            g.DrawRectangle(Gx.P(p.Border, 1f), r.X, r.Y, r.Width, r.Height);
            g.FillRectangle(Gx.B(p.Neon), r.Left, r.Top, S(3), S(40));
            g.SetClip(new RectangleF(r.Left + 1, r.Top + 1, r.Width - 2, r.Height - 2));
            if (scrollable) g.TranslateTransform(0, -scroll);
            PointF pm = PanelMouse();
            float pad = S(18), x = r.Left + pad, y = r.Top + pad;
            double tt = typeMs;
            if (!textInstant && tt < 520 && tbTitle.Lines.Length > 0)
            {
                string line = tbTitle.Lines[0];
                int settled = (int)(line.Length * tt / 520.0);
                tbTitle.Draw(g, Gx.F.Title, Gx.B(p.Text), x, y, settled, Gx.Near);
                float cx = x + tbTitle.Cum[0][Math.Min(settled, line.Length)];
                for (int i = settled; i < line.Length && i < settled + 6; i++)
                {
                    string gl = Glyphs[(int)(rng.Next() % (uint)Glyphs.Length)];
                    Gx.Text(gl, Gx.F.Title, TutorialGfx.A(p.Neon, Math.Max(0, 115 - (i - settled) * 15)), cx, y, 0);
                    cx += tbTitle.Cum[0][i + 1] - tbTitle.Cum[0][i];
                }
            }
            else tbTitle.Draw(g, Gx.F.Title, Gx.B(p.Text), x, y, -1, Gx.Near);
            y += tbTitle.Height + S(8);
            double bodyStart = 450;
            int nb = CharsShown(tbBody, bodyStart);
            PointF cur = tbBody.Draw(g, Gx.F.Body, Gx.B(p.Text), x, y, WholeLines(tbBody, y, nb), Gx.Near);
            if (nb >= 0 && typeMs > bodyStart) g.FillRectangle(Gx.B(p.Neon), cur.X + S(1), cur.Y + S(3), S(9), tbBody.LineH * 0.62f);
            y += tbBody.Height;
            double after = bodyStart + tbBody.TotalChars * 1000.0 / TypeCps + 200;
            bool bodyDone = nb < 0;
            if (sNote != null)
            {
                y += S(8);
                if (bodyDone) tbNote.Draw(g, Gx.F.Note, Gx.B(Gx.Readable(p.Muted)), x, y, -1, Gx.Near);
                y += tbNote.Height;
            }
            if (sDev != null && devCollapsed)
            {
                RectangleF t = rDevToggle;
                bool hov = t.Contains(pm);
                g.FillRectangle(Gx.B(TutorialGfx.A(p.Accent, hov ? 36 : 24)), t);
                g.FillRectangle(Gx.B(p.Accent), t.Left, t.Top, S(3), t.Height);
                Gx.TextMid("开发者的话", Gx.F.Tag, p.Accent, t.Left + S(12), t.Top + t.Height / 2, 0);
                Gx.TextMid(devOpen ? "收起" : "点开看", Gx.F.Small, hov ? p.Text : p.Muted, t.Right - S(24), t.Top + t.Height / 2, 2);
                Tri(t.Right - S(12), t.Top + t.Height / 2, S(4.5f), devOpen, hov ? p.Text : p.Muted);
            }
            else if (sDev != null)
            {
                y += DevGapAbove();
                RectangleF box = new RectangleF(x, y, r.Width - 2 * pad, DevBoxH(devTitleH, tbDev.Height));
                if (bodyDone || textInstant)
                {
                    Color devBg = TutorialGfx.Mix(p.Panel, p.Accent, 0.09);
                    g.FillRectangle(Gx.B(devBg), box);
                    g.FillRectangle(Gx.B(p.Accent), box.Left, box.Top, S(3), box.Height);
                    Gx.TextBg = devBg;
                    Gx.Text("开发者的话", Gx.F.Tag, p.Accent, box.Left + S(12), box.Top + S(6), 0);
                    int nd = CharsShown(tbDev, after);
                    PointF dc = tbDev.Draw(g, Gx.F.Body, Gx.B(Gx.Readable(TutorialGfx.Mix(p.Text, p.Muted, 0.2))), box.Left + S(12), box.Top + DevTextTop(devTitleH),
                        WholeLines(tbDev, box.Top + DevTextTop(devTitleH), nd), Gx.Near);
                    if (nd >= 0 && typeMs > after) g.FillRectangle(Gx.B(p.Accent), dc.X + S(1), dc.Y + S(3), S(8), tbDev.LineH * 0.6f);
                    Gx.TextBg = p.Panel;
                }
            }
            // fields
            TutorialField[] f = s.Fields;
            int nf = f == null ? 0 : f.Length;
            if (nf > 0 && !devOpen)
            {
                float fy = fieldsTop;
                g.DrawLine(Gx.P(p.Border, 1f), x, fy + S(4), r.Right - pad, fy + S(4));
                Gx.Text("你的数值", Gx.F.Tag, p.Muted, x, fy + S(compact ? 6 : 10), 0);
                Profile prof = C.P;
                for (int i = 0; i < nf; i++)
                {
                    if (!fieldShown[i]) continue;
                    if (!InView(fieldCtl[i].Top, fieldCtl[i].Bottom)) continue; // its control is hidden too (FieldRect)
                    RectangleF lr = fieldLabel[i];
                    TutorialField fd = f[i];
                    // main field: a strip exactly as wide as the content column (= the 开发者的话 card and the rule above)
                    if (fd.Main) g.FillRectangle(Gx.B(TutorialGfx.A(p.Neon, 18)), x, lr.Top + S(2), r.Width - 2 * pad, lr.Height - S(4));
                    SizeF ls = Gx.Measure(fd.Label, Gx.F.Small);
                    Gx.TextMid(fd.Label, Gx.F.Small, fd.Main ? p.Text : p.Muted, lr.Left, lr.Top + lr.Height / 2, 0);
                    if (!string.IsNullOrEmpty(fd.Tag) && ls.Width + Gx.Measure(fd.Tag, Gx.F.Tag).Width + S(18) < lr.Width)
                    {
                        Color tc = fd.Tag == "推荐改" ? p.Neon : (fd.Tag == "不建议改" || fd.Tag.IndexOf("不推荐") >= 0 ? p.Accent : p.Muted);
                        float th = Gx.F.Tag.GetHeight(g) + S(5);
                        Gx.Tag(fd.Tag, lr.Left + ls.Width + S(6), lr.Top + (lr.Height - th) / 2, tc, 0);
                    }
                    RectangleF cr = fieldCtl[i];
                    if (!string.IsNullOrEmpty(fd.Unit)) Gx.TextMid(fd.Unit, Gx.F.Small, p.Muted, cr.Right + S(6), cr.Top + cr.Height / 2, 0);
                    if (!fieldArrow[i].IsEmpty) DrawFieldArrow(g, i, fd, prof, pm);
                    if (DrawFieldGhosts && prof != null)
                    {
                        g.FillRectangle(Gx.B(Color.FromArgb(0x2F, 0x33, 0x38)), cr);
                        g.DrawRectangle(Gx.P(p.Border, 1f), cr.X, cr.Y, cr.Width, cr.Height);
                        string v = fd.Kind == TutorialField.KindCheck ? ((fd.GetCheck != null && fd.GetCheck(prof)) ? "✓" : "")
                            : (fd.Get == null ? "" : (fd.Pct != TutorialField.PctNone ? OffsetUnits.Number(OffsetUnits.ToPct(fd.Get(prof), C.G))
                            : TutorialNum.Int(fd.Get(prof))));
                        Gx.TextBg = Color.FromArgb(0x2F, 0x33, 0x38);
                        Gx.TextMid(v, Gx.F.Small, p.Text, cr.Left + S(6), cr.Top + cr.Height / 2, 0); // left, like the real box
                        Gx.TextBg = p.Panel;
                    }
                }
                if (!rExpander.IsEmpty && InView(rExpander.Top, rExpander.Bottom))
                {
                    bool hov = rExpander.Contains(pm);
                    Color ec = hov ? p.Text : p.Muted;
                    Tri(rExpander.Left + S(5), rExpander.Top + rExpander.Height / 2, S(4.5f), Expanded, ec);
                    Gx.TextMid("可选（默认不改）", Gx.F.Small, ec, rExpander.Left + S(16), rExpander.Top + rExpander.Height / 2, 0);
                }
            }
            if (sFieldNote != null && !devOpen)
            {
                float ny = (rLink.IsEmpty ? contentBottom : rLink.Top) - S(4) - tbFieldNote.Height;
                int fit = scrollable ? tbFieldNote.CharsFitting(ny, ViewBottom() + scroll) : -1; // whole lines only
                tbFieldNote.Draw(g, Gx.F.Note, Gx.B(Gx.Readable(p.Muted)), x, ny, fit, Gx.Near);
            }
            if (sLink != null && !devOpen && InView(rLink.Top, rLink.Bottom))
            {
                bool hov = rLink.Contains(pm);
                Gx.TextMid(sLink, Gx.F.Note, hov ? p.Text : p.Accent, rLink.Left, rLink.Top + rLink.Height / 2, 0);
                if (hov)
                {
                    SizeF sz = Gx.Measure(sLink, Gx.F.Note);
                    g.DrawLine(Gx.P(p.Text, 1f), rLink.Left, rLink.Top + rLink.Height / 2 + sz.Height / 2, rLink.Left + sz.Width, rLink.Top + rLink.Height / 2 + sz.Height / 2);
                }
            }
            if (devOpen && !rDevCard.IsEmpty) DrawDevCard(g);
            g.ResetTransform();
            g.ResetClip();
            if (scrollable)
            {
                float viewH = r.Height, contentH = contentBottom + S(18) - r.Top;
                float trackX = r.Right - S(6), th = Math.Max(S(30), viewH * viewH / Math.Max(viewH, contentH));
                float maxScroll = Math.Max(1, contentH - viewH);
                float ty = r.Top + (viewH - th) * (scroll / maxScroll);
                g.FillRectangle(Gx.B(TutorialGfx.A(p.Border, 200)), trackX, r.Top + S(4), S(3), viewH - S(8));
                g.FillRectangle(Gx.B(p.Accent), trackX - S(1), Math.Max(r.Top + S(4), ty), S(5), Math.Min(th, viewH - S(8)));
                if (scroll < maxScroll - 1)
                {
                    // fade at the bottom edge shows there is more below
                    g.FillRectangle(Gx.B(TutorialGfx.A(p.Panel, 200)), r.Left + 1, r.Bottom - S(14), r.Width - S(10), S(13));
                    Tri(r.Left + r.Width / 2, r.Bottom - S(8), S(5), true, p.Muted);
                }
            }
        }

        /// <summary>Direction of a 百分比偏移 field: the value's sign, or (value 0) the direction its arrow was set to.</summary>
        public int FieldSign(int i)
        {
            TutorialStep s = Current;
            TutorialField[] f = s == null ? null : s.Fields;
            if (f == null || i < 0 || i >= f.Length || f[i].Pct == TutorialField.PctNone) return 1;
            if (!f[i].PctSigned) return f[i].Min >= 0 ? 1 : -1;
            Profile prof = C.P;
            double v = (prof != null && f[i].Get != null) ? f[i].Get(prof) : 0;
            if (v > 0) return 1;
            if (v < 0) return -1;
            return (i < zeroNeg.Length && zeroNeg[i]) ? -1 : 1;
        }

        /// <summary>The arrow of a field at 0 was clicked: only the shown direction flips (nothing to store yet).</summary>
        public void ToggleZeroSign(int i)
        {
            if (i >= 0 && i < zeroNeg.Length) zeroNeg[i] = !zeroNeg[i];
        }

        /// <summary>The direction arrow left of a 百分比偏移 box: a small toggle button (signed fields) or a plain arrow.</summary>
        private void DrawFieldArrow(Graphics g, int i, TutorialField fd, Profile prof, PointF pm)
        {
            TutorialPalette p = Gx.Pal;
            RectangleF a = fieldArrow[i];
            string glyph = OffsetUnits.Arrow(fd.Axis, FieldSign(i) > 0);
            bool en = prof != null && (fd.Enabled == null || fd.Enabled(prof));
            if (!fd.PctSigned)
            {
                g.DrawString(glyph, Gx.F.Button, Gx.B(Gx.Readable(en ? p.Text : p.Muted)), a, Gx.CenterMid);
                return;
            }
            bool hov = en && a.Contains(pm);
            g.FillRectangle(Gx.B(hov ? Color.FromArgb(0x38, 0x3D, 0x43) : Color.FromArgb(0x2F, 0x33, 0x38)), a);
            g.DrawRectangle(Gx.P(hov ? p.Accent : p.Border, 1f), a.X, a.Y, a.Width, a.Height);
            g.DrawString(glyph, Gx.F.Button, Gx.B(Gx.Readable(en ? p.Text : p.Muted)), a, Gx.CenterMid);
        }

        /// <summary>Small triangle: pointing right (closed) or down (open).</summary>
        private void Tri(float x, float y, float r, bool down, Color c)
        {
            PointF[] t = Gx.Pts(3);
            if (down) { t[0] = new PointF(x - r, y - r * 0.55f); t[1] = new PointF(x + r, y - r * 0.55f); t[2] = new PointF(x, y + r * 0.75f); }
            else { t[0] = new PointF(x - r * 0.55f, y - r); t[1] = new PointF(x + r * 0.75f, y); t[2] = new PointF(x - r * 0.55f, y + r); }
            Gx.G.FillPolygon(Gx.B(c), t);
        }

        /// <summary>The folded 开发者的话 as a card over the panel (input rows are hidden while it is open).</summary>
        private void DrawDevCard(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            RectangleF r = rDevCard;
            g.FillRectangle(Gx.B(TutorialGfx.A(Color.Black, 120)), r.X + S(3), r.Y + S(4), r.Width, r.Height);
            Color bg = TutorialGfx.Mix(p.Panel, p.Accent, 0.08);
            g.FillRectangle(Gx.B(bg), r);
            g.DrawRectangle(Gx.P(p.Accent, 1.2f), r.X, r.Y, r.Width, r.Height);
            g.FillRectangle(Gx.B(p.Accent), r.Left, r.Top, S(3), r.Height);
            Gx.TextBg = bg;
            Gx.Text("开发者的话", Gx.F.Tag, p.Accent, r.Left + S(14), r.Top + S(10), 0);
            Gx.Text("点这里收起", Gx.F.Small, p.Muted, r.Right - S(12), r.Top + S(9), 2);
            tbDev.Draw(g, Gx.F.Body, Gx.B(Gx.Readable(TutorialGfx.Mix(p.Text, p.Muted, 0.15))), r.Left + S(14), r.Top + devTitleH + S(20), -1, Gx.Near);
            Gx.TextBg = p.Panel;
        }

        private void DrawFooter(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            g.FillRectangle(Gx.B(p.Panel), rFooter);
            g.DrawLine(Gx.P(p.Border, 1f), rFooter.Left, rFooter.Top + 0.5f, rFooter.Right, rFooter.Top + 0.5f);
            double pulse = 0.5 + 0.5 * Math.Sin(clockMs / 300.0);
            float px = rPrompt.Left + S(4), py = rPrompt.Top + rPrompt.Height / 2;
            PointF[] tri = Gx.Pts(3);
            tri[0] = new PointF(px, py - S(6)); tri[1] = new PointF(px + S(9), py); tri[2] = new PointF(px, py + S(6));
            g.FillPolygon(Gx.B(TutorialGfx.A(p.Neon, (int)(150 + 105 * pulse))), tri);
            tbPrompt.Draw(g, Gx.F.Body, Gx.B(p.Text), px + S(20), py - tbPrompt.Height / 2, -1, Gx.Near);
            Button(g, bExit, LExit, false, false);
            Button(g, bSkip, LSkip, false, IsLast);
            Button(g, bPrev, LPrev, false, index == 0);
            bool ready = Current.Done;
            Button(g, bNext, IsLast ? LFinish : (ready ? LNextReady : LNext), true, false);
            if (ready && !IsLast)
            {
                RectangleF r = bNext;
                g.DrawRectangle(Gx.P(TutorialGfx.A(p.Neon, (int)(60 + 90 * pulse)), 5f), r.X, r.Y, r.Width, r.Height);
            }
        }

        private void Button(Graphics g, RectangleF r, string text, bool primary, bool disabled)
        {
            TutorialPalette p = Gx.Pal;
            bool hover = !disabled && r.Contains(Mouse);
            bool press = hover && MouseDown;
            Color fill = primary ? (press ? TutorialGfx.Mix(p.Neon, Color.Black, 0.35) : (hover ? TutorialGfx.Mix(p.Neon, Color.White, 0.15) : p.Neon))
                : (press ? p.Border : (hover ? Color.FromArgb(0x38, 0x3D, 0x43) : p.Surface));
            if (disabled) fill = p.Surface;
            g.FillRectangle(Gx.B(fill), r);
            g.DrawRectangle(Gx.P(disabled ? TutorialGfx.A(p.Border, 120) : (primary ? p.Neon : (hover ? p.Accent : p.Border)), 1.2f), r.X, r.Y, r.Width, r.Height);
            Color tc = disabled ? TutorialGfx.Mix(p.Muted, p.Surface, 0.45) : (primary ? Color.Black : p.Text);
            g.DrawString(text, Gx.F.Button, Gx.B(tc), r, Gx.CenterMid);
        }

        // ------------------------------------------------------------------ hit testing
        public TutorialHit HitTest(PointF pt, out int dot)
        {
            dot = -1;
            if (Current == null) return TutorialHit.None;
            if (bNext.Contains(pt)) return TutorialHit.Next;
            if (bPrev.Contains(pt)) return index > 0 ? TutorialHit.Prev : TutorialHit.None;
            if (bSkip.Contains(pt)) return IsLast ? TutorialHit.None : TutorialHit.Skip;
            if (bExit.Contains(pt)) return TutorialHit.Exit;
            for (int i = 0; i < dotRects.Length; i++) if (dotRects[i].Contains(pt)) { dot = i; return TutorialHit.Dot; }
            if (rPanel.Contains(pt))
            {
                PointF q = pt;
                if (scrollable) q.Y += scroll;
                if (devOpen && rDevCard.Contains(q)) return TutorialHit.DevNote;
                if (!rDevToggle.IsEmpty && rDevToggle.Contains(q)) return TutorialHit.DevNote;
                if (!devOpen && !rLink.IsEmpty && rLink.Contains(q) && InView(rLink.Top, rLink.Bottom)) return TutorialHit.SettingsLink;
                if (!devOpen && !rExpander.IsEmpty && rExpander.Contains(q) && InView(rExpander.Top, rExpander.Bottom)) return TutorialHit.Expander;
                TutorialField[] ff = Current.Fields;
                for (int i = 0; !devOpen && ff != null && i < fieldArrow.Length && i < ff.Length; i++)
                {
                    if (!fieldShown[i] || fieldArrow[i].IsEmpty || !ff[i].PctSigned || !fieldArrow[i].Contains(q)) continue;
                    if (!InView(fieldArrow[i].Top, fieldArrow[i].Bottom)) continue;
                    Profile prof = C.P;
                    if (prof == null || (ff[i].Enabled != null && !ff[i].Enabled(prof))) continue;
                    dot = i;
                    return TutorialHit.FieldArrow;
                }
                if (rTextArea.Contains(q)) return TutorialHit.Text;
                return TutorialHit.None;
            }
            if (rScene.Contains(pt)) return TutorialHit.Scene;
            return TutorialHit.None;
        }

        /// <summary>True when the pointer is over something clickable (hand cursor).</summary>
        public bool Clickable(PointF pt)
        {
            int d;
            TutorialHit h = HitTest(pt, out d);
            if (h == TutorialHit.Scene) return Gx.HitChip(pt) >= 0;
            return h != TutorialHit.None && h != TutorialHit.Text;
        }
    }
}
