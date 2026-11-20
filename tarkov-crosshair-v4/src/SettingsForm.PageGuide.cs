// SettingsForm.PageGuide.cs -- the 「指南」 page: the whole 说明 README.txt inside the app, laid out as a document -- by Leo.
// v4.1 GUIDE41 (UI41-review #4): a left 目录 built from the README headings (click = jump to that section), one text column
// capped at about 40 characters and centred, three heading levels, numbered steps drawn as steps, 「名 ……… 说明」 lines as
// tables, key names and numbers in the mono font, its own dark scroll bar. The content is only GuideText.Text (generated
// from the README); this file only decides how it looks.
using System;
using System.Collections.Generic;
#if !UI_PURE_CHECK
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
#endif

namespace TarkovCrosshair
{
    /// <summary>Splits the guide into display lines with a kind (pure: used by the page and the tests).</summary>
    internal static class GuideFormat
    {
        public const int Body = 0, Heading = 1, Rule = 2;

        /// <summary>A line of 5 or more '=' / '-' (or '─' '═') and nothing else.</summary>
        public static bool IsRule(string line)
        {
            string t = (line ?? "").Trim();
            if (t.Length < 5) return false;
            foreach (char c in t) if (c != '=' && c != '-' && c != '─' && c != '═') return false;
            return true;
        }

        /// <summary>Kind per line: Rule; Heading = a 【…】 line, a line between two rules, or the first non-empty line
        /// (the title); else Body.</summary>
        public static int[] Kinds(string[] lines)
        {
            int[] k = new int[lines.Length];
            bool titleSeen = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = (lines[i] ?? "").Trim();
                if (IsRule(t)) { k[i] = Rule; continue; }
                bool between = i > 0 && i + 1 < lines.Length && IsRule(lines[i - 1]) && IsRule(lines[i + 1]);
                bool bracket = t.StartsWith("【") && t.EndsWith("】");
                bool title = !titleSeen && t.Length > 0;
                if (t.Length > 0) titleSeen = true;
                k[i] = (t.Length > 0 && (between || bracket || title)) ? Heading : Body;
            }
            return k;
        }

        /// <summary>
        /// A 「名字 ……… 说明」 line (a run of 2+ '…' / '.' / '·' between two parts): the parts, the leading indent; false otherwise.
        /// UI41-review #4: shown as a 2-column table instead of dotted leaders.
        /// </summary>
        public static bool SplitLeader(string line, out string indent, out string left, out string right)
        {
            indent = left = right = null;
            string s = line ?? "";
            int start = 0;
            while (start < s.Length && (s[start] == ' ' || s[start] == '\u3000')) start++;
            if (start >= s.Length || IsLeaderChar(s[start])) return false; // nothing before the dots: not a table line
            for (int i = start + 1; i < s.Length; i++)
            {
                if (!IsLeaderChar(s[i])) continue;
                int j = i;
                while (j < s.Length && IsLeaderChar(s[j])) j++;
                bool dotsOnly = true; // '.' runs need 3+ (a file name like settings.json has one)
                for (int q = i; q < j; q++) if (s[q] != '.') dotsOnly = false;
                if (j - i < (dotsOnly ? 3 : 2)) { i = j; continue; }
                string l = s.Substring(start, i - start).Trim(), r = s.Substring(j).Trim();
                if (l.Length == 0 || r.Length == 0) return false;
                indent = s.Substring(0, start); left = l; right = r;
                return true;
            }
            return false;
        }

        internal static bool IsLeaderChar(char c) { return c == '…' || c == '.' || c == '·' || c == '⋯'; }

        /// <summary>Rough text width in em (CJK and full-width = 1, other = 0.55): sizes a column without a font.</summary>
        public static double Em(string s)
        {
            double w = 0;
            foreach (char c in s ?? "") w += (c >= 0x2E80 || (c >= 0xFF00 && c <= 0xFFEF)) ? 1.0 : 0.55;
            return w;
        }

        /// <summary>
        /// RTF of the guide (v4.1, UI41-review #4): rule lines (=====) are left out; headings bold in 'accent' with a gap
        /// above; 「名字 ……… 说明」 runs become a 2-column table (tab stop + hanging indent, column = widest name of the run);
        /// body in 'text'. 'rule' colours nothing now (kept for callers). Every non-ASCII char as \uN? so any code page works.
        /// v4.1 GUIDE41: the page draws the document itself; this RTF (black text) goes to the clipboard with 「复制全文」.
        /// </summary>
        public static string Rtf(string[] lines, int[] kinds, string fontName, float pt, System.Drawing.Color text,
            System.Drawing.Color accent, System.Drawing.Color rule)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int half = Math.Max(16, (int)Math.Round(pt * 2));
            sb.Append(@"{\rtf1\ansi\ansicpg936\deff0{\fonttbl{\f0\fnil\fcharset134 ");
            Escape(sb, string.IsNullOrEmpty(fontName) ? "Microsoft YaHei UI" : fontName);
            sb.Append(";}}{\\colortbl ;");
            foreach (System.Drawing.Color c in new System.Drawing.Color[] { text, accent, rule })
                sb.Append("\\red").Append(c.R).Append("\\green").Append(c.G).Append("\\blue").Append(c.B).Append(';');
            sb.Append("}\\f0\\fs").Append(half).Append(' ');
            int twipsPerEm = (int)Math.Round(pt * 20);
            bool first = true;
            for (int i = 0; i < lines.Length; i++)
            {
                int k = i < kinds.Length ? kinds[i] : Body;
                if (k == Rule) continue;
                if (!first) sb.Append("\\par\r\n");
                string ind, l, r;
                if (k == Heading)
                {
                    sb.Append(first ? "\\pard " : "\\pard\\sb180 ");
                    sb.Append("\\cf2\\b ");
                    Escape(sb, lines[i] ?? "");
                    sb.Append("\\b0");
                }
                else if (SplitLeader(lines[i], out ind, out l, out r))
                {
                    // column = widest name in this run of table lines (+ 1.5 em gap)
                    double col = 0;
                    for (int a = i; a < lines.Length && (a >= kinds.Length || kinds[a] == Body); a++)
                    {
                        string i2, l2, r2;
                        if (!SplitLeader(lines[a], out i2, out l2, out r2)) break;
                        col = Math.Max(col, Em(l2));
                    }
                    for (int a = i - 1; a >= 0 && (a >= kinds.Length || kinds[a] == Body); a--)
                    {
                        string i2, l2, r2;
                        if (!SplitLeader(lines[a], out i2, out l2, out r2)) break;
                        col = Math.Max(col, Em(l2));
                    }
                    int li = (int)Math.Round(Em(ind) * twipsPerEm), cw = (int)Math.Round((col + 1.5) * twipsPerEm);
                    sb.Append("\\pard\\li").Append(li + cw).Append("\\fi-").Append(cw).Append("\\tx").Append(li + cw).Append(' ');
                    sb.Append("\\cf1\\b0 ");
                    Escape(sb, l);
                    sb.Append("\\tab ");
                    Escape(sb, r);
                }
                else
                {
                    sb.Append("\\pard \\cf1\\b0 ");
                    Escape(sb, lines[i] ?? "");
                }
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void Escape(System.Text.StringBuilder sb, string s)
        {
            foreach (char c in s)
            {
                if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
                else if (c == '\t') sb.Append("\\tab ");
                else if (c < 0x20) continue;
                else if (c < 0x80) sb.Append(c);
                else sb.Append("\\u").Append((int)(short)c).Append('?');
            }
        }

        public static string[] Lines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
    }

    /// <summary>
    /// v4.1 GUIDE41 (UI41-review #4): GuideText.Text as a document (pure: the page and the tests use it).
    /// Parse: blocks -- the title, the ★ note, 【…】 sections and 「01 …」 pages (both in the 目录), lead-in lines (「开始前：」),
    /// paragraphs, 「- 」 bullets, 「1.」 steps, 「名 ……… 说明」 / 「名：说明」 / numbered lines as table rows, 问 / 答 -- with the
    /// hard-wrapped lines of the txt file joined again. Runs: key names (F12, W A S D, Ctrl+D …) and numbers (75, 4.17%,
    /// 2160×1440, v4.1 …) for the mono font. Wrap: lines for a width with Chinese line-breaking rules (no line starts with
    /// 。，、）」…, none ends with （「). Layout: where the 目录 and the text column go for a page width.
    /// </summary>
    internal static class GuideDoc
    {
        public const int Title = 0, Callout = 1, H1 = 2, H2 = 3, H3 = 4, Para = 5, Bullet = 6, Step = 7, Row = 8, Question = 9;
        /// <summary>Run kinds: body text, key name (mono), number (mono).</summary>
        public const int RText = 0, RKey = 1, RNum = 2;
        /// <summary>Text column cap / minimum next to the 目录, in characters of the body font; how far the column may move
        /// right of the 目录 to sit centred.</summary>
        public const int MaxColumnChars = 40, MinColumnChars = 24, CentreChars = 4;

        internal sealed class Block
        {
            public int Kind;
            /// <summary>0 = top level; 1 = inside a list item or an answer.</summary>
            public int Depth;
            /// <summary>Step / numbered row: "1".."10"; H2: its page code ("01").</summary>
            public string Num = "";
            /// <summary>Row: the left cell; Question: "问".</summary>
            public string Label = "";
            /// <summary>The text (the txt file's hard-wrapped lines joined); Row: the right cell (may be empty).</summary>
            public string Text = "";
            /// <summary>"答" on the first block of an answer (drawn in the gutter).</summary>
            public string Mark = "";
            /// <summary>Row: table id (rows of one table share the left column); -1 otherwise.</summary>
            public int Table = -1;
            /// <summary>Step: list id (steps of one list are joined by the step line); -1 otherwise.</summary>
            public int List = -1;
            /// <summary>First source line (0-based).</summary>
            public int Line;
            /// <summary>H1 / H2: its 目录 entry; -1 otherwise.</summary>
            public int Toc = -1;
            /// <summary>The 目录 entry this block belongs to (-1 above the first section).</summary>
            public int Section = -1;
        }

        internal sealed class Entry
        {
            /// <summary>1 = 【…】 section, 2 = 「01 首页」 page.</summary>
            public int Level;
            public string Num = "";
            /// <summary>The heading as written (without 【】 and the page code).</summary>
            public string Title = "";
            /// <summary>Short form for the 目录 (TocLabel): 「升级（从旧版本换到 v4.1）」 -> 「升级」.</summary>
            public string Label = "";
            public int Block;
        }

        /// <summary>目录 text of a heading: a trailing （…） and anything after the first '：' are left out (the heading
        /// itself shows them), so every entry fits on one line: 「第一次使用：跟着教程走」 -> 「第一次使用」.</summary>
        public static string TocLabel(string title)
        {
            string t = Squeeze(title);
            if (t.EndsWith("）"))
            {
                int open = t.LastIndexOf('（');
                if (open > 0) t = t.Substring(0, open).Trim();
            }
            int c = t.IndexOf('：');
            if (c > 0) t = t.Substring(0, c).Trim();
            return t.Length > 0 ? t : Squeeze(title);
        }

        internal sealed class Doc
        {
            public readonly List<Block> Blocks = new List<Block>();
            public readonly List<Entry> Toc = new List<Entry>();
        }

        internal struct Run { public int Start, Length, Kind; }

        /// <summary>One drawn piece of a line: text [Start, Start+Length) of one run kind at X (width W), line-relative.</summary>
        internal struct Piece { public int Start, Length, Kind, X, W; }

        /// <summary>Where things go for a page width (px): Wide = 目录 column at TocX (TocW wide) + text column; else the 目录
        /// is a 「目录 ▾」 menu. The document view starts at ViewX and reaches the right edge; the text column is ColX..ColX+ColW.</summary>
        internal struct Plan { public bool Wide; public int TocX, TocW, ViewX, ColX, ColW; }

        // ------------------------------------------------------------------ parse

        private sealed class Open
        {
            public Block B;          // null for an answer (its first block carries Mark "答")
            public int Marker;       // display column of the item's marker: a line at or left of it ends the item
            public bool Container;   // bullet / step / answer: deeper lines are its sub-items or its own text
            public bool Intro;       // text ends with '：' -> deeper 「名：说明」 / numbered lines are rows under it
            public bool Children;    // a sub-item was added after its text
            public int ChildDepth;
        }

        private sealed class Ids { public int Tables, Lists; }

        public static Doc Parse(string text)
        {
            Doc d = new Doc();
            string[] lines = GuideFormat.Lines(text);
            int[] kinds = GuideFormat.Kinds(lines);
            List<Open> st = new List<Open>();
            int[] lists = { -1, -1, -1, -1, -1, -1, -1, -1 };
            Ids ids = new Ids();
            bool titleSeen = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = (lines[i] ?? "").TrimEnd();
                string t = raw.Trim();
                if (kinds[i] == GuideFormat.Rule || t.Length == 0) { st.Clear(); Reset(lists); continue; }
                if (kinds[i] == GuideFormat.Heading)
                {
                    st.Clear();
                    Reset(lists);
                    Block h = New(H2, 0, i);
                    if (!titleSeen) { titleSeen = true; h.Kind = Title; h.Text = Squeeze(t); }
                    else if (t.Length > 2 && t[0] == '【' && t[t.Length - 1] == '】') { h.Kind = H1; h.Text = Squeeze(t.Substring(1, t.Length - 2)); }
                    else
                    {
                        h.Kind = i > 0 && IsMajorRule(lines[i - 1]) ? H1 : H2;
                        string code, rest;
                        if (h.Kind == H2 && SplitCode(t, out code, out rest)) { h.Num = code; h.Text = Squeeze(rest); }
                        else h.Text = Squeeze(t);
                    }
                    if (h.Kind == H1 || h.Kind == H2)
                    {
                        Entry e = new Entry();
                        e.Level = h.Kind == H1 ? 1 : 2;
                        e.Num = h.Num;
                        e.Title = h.Text;
                        e.Label = TocLabel(h.Text);
                        e.Block = d.Blocks.Count;
                        h.Toc = d.Toc.Count;
                        d.Toc.Add(e);
                    }
                    Add(d, h, lists);
                    continue;
                }
                int ind = Indent(raw);
                while (st.Count > 0 && st[st.Count - 1].Marker >= ind) st.RemoveAt(st.Count - 1);
                if (t[0] == '★')
                {
                    st.Clear();
                    Reset(lists);
                    Block n = New(Callout, 0, i);
                    n.Text = Squeeze(t.Trim('★', ' ', '\u3000'));
                    Add(d, n, lists);
                    continue;
                }
                string after;
                if (Marked(t, '问', out after))
                {
                    st.Clear();
                    Reset(lists);
                    Block q = New(Question, 0, i);
                    q.Label = "问";
                    q.Text = Squeeze(after);
                    Add(d, q, lists);
                    Open oq = new Open();
                    oq.B = q;
                    oq.Marker = ind;
                    st.Add(oq);
                    continue;
                }
                if (Marked(t, '答', out after))
                {
                    st.Clear();
                    Reset(lists);
                    Open oa = new Open();
                    oa.Marker = ind;
                    oa.Container = true;
                    oa.ChildDepth = 1;
                    st.Add(oa);
                    int col = ind + Columns(t.Substring(0, 2)) + Indent(after);
                    LineItem(d, st, lists, ids, lines, i, new string(' ', col) + after.Trim(), col, "答");
                    continue;
                }
                LineItem(d, st, lists, ids, lines, i, raw, ind, "");
            }
            return d;
        }

        /// <summary>One body line (raw = with its indent, ind = display columns of the indent) against the open items.</summary>
        private static void LineItem(Doc d, List<Open> st, int[] lists, Ids ids, string[] lines, int i, string raw, int ind, string mark)
        {
            string t = raw.Trim();
            if (t.Length == 0) return;
            Open owner = null;
            for (int s = st.Count - 1; s >= 0; s--) if (st[s].Container) { owner = st[s]; break; }
            Open top = st.Count > 0 ? st[st.Count - 1] : null;
            int depth = owner != null ? owner.ChildDepth : 0;
            bool intro = owner != null && owner.Intro;
            string num, rest, l, r, ix;
            Block b = null;
            bool container = false;
            if (t.StartsWith("- ") || t == "-")
            {
                b = New(Bullet, depth, i);
                b.Text = Squeeze(t.Substring(1));
                container = true;
            }
            else if (SplitStep(t, out num, out rest))
            {
                b = New(Step, depth, i);
                b.Num = num;
                b.Text = Squeeze(rest);
                container = true;
            }
            else if (intro && SplitNumRow(t, out num, out rest))
            {
                b = New(Row, depth, i);
                b.Num = num;
                if (GuideFormat.SplitLeader(rest, out ix, out l, out r)) { b.Label = Squeeze(l); b.Text = Squeeze(r); }
                else b.Label = Squeeze(rest);
            }
            else if (GuideFormat.SplitLeader(raw, out ix, out l, out r) || (ind >= 2 && LastIsRow(d, -1) && SplitSingleLeader(t, out l, out r)))
            {
                b = New(Row, depth, i);
                b.Label = Squeeze(l);
                b.Text = Squeeze(r);
            }
            else if (intro && SplitDef(t, out l, out r))
            {
                b = New(Row, depth, i);
                b.Label = Squeeze(l);
                b.Text = Squeeze(r);
            }
            if (b != null)
            {
                // a new item: whatever is open above its container (a paragraph, a row) ends here
                while (st.Count > 0 && st[st.Count - 1] != owner) st.RemoveAt(st.Count - 1);
                if (owner != null) owner.Children = true;
                b.Mark = mark ?? "";
                if (b.Kind == Row) b.Table = LastIsRow(d, depth) ? d.Blocks[d.Blocks.Count - 1].Table : ids.Tables++;
                Add(d, b, lists, ids);
                Open o = new Open();
                o.B = b;
                o.Marker = ind;
                o.Container = container;
                o.Intro = container && EndsIntro(b.Text);
                o.ChildDepth = depth + 1;
                st.Add(o);
                return;
            }
            if (top != null)
            {
                if (top.Container && (top.Children || top.B == null))
                {
                    // text below an item's sub-items (or an answer's first line): its own paragraph inside the item
                    Block p = New(Para, top.ChildDepth, i);
                    p.Text = Squeeze(t);
                    p.Mark = mark ?? "";
                    top.Children = true;
                    Add(d, p, lists);
                    Open o = new Open();
                    o.B = p;
                    o.Marker = top.Marker;
                    st.Add(o);
                    return;
                }
                top.B.Text = Join(top.B.Text, Squeeze(t)); // the rest of the item / paragraph / row on the next txt line
                if (top.Container) top.Intro = EndsIntro(top.B.Text);
                return;
            }
            Block nb = New(IsLeadIn(t, lines, i) ? H3 : Para, 0, i);
            nb.Text = Squeeze(t);
            nb.Mark = mark ?? "";
            Add(d, nb, lists);
            if (nb.Kind == Para)
            {
                Open o = new Open();
                o.B = nb;
                o.Marker = -1; // the next txt lines continue it until a blank line or an item
                st.Add(o);
            }
        }

        private static Block New(int kind, int depth, int line)
        {
            Block b = new Block();
            b.Kind = kind;
            b.Depth = Math.Max(0, Math.Min(depth, 6));
            b.Line = line;
            return b;
        }

        private static void Add(Doc d, Block b, int[] lists) { Add(d, b, lists, null); }

        private static void Add(Doc d, Block b, int[] lists, Ids ids)
        {
            int dep = Math.Min(b.Depth, lists.Length - 1);
            for (int k = dep + 1; k < lists.Length; k++) lists[k] = -1; // a shallower block ends the deeper lists
            if (b.Kind == Step && ids != null)
            {
                if (lists[dep] < 0) lists[dep] = ids.Lists++;
                b.List = lists[dep];
            }
            else lists[dep] = -1;
            b.Section = d.Toc.Count - 1;
            d.Blocks.Add(b);
        }

        private static void Reset(int[] lists) { for (int k = 0; k < lists.Length; k++) lists[k] = -1; }

        private static bool LastIsRow(Doc d, int depth)
        {
            if (d.Blocks.Count == 0) return false;
            Block p = d.Blocks[d.Blocks.Count - 1];
            return p.Kind == Row && (depth < 0 || p.Depth == depth);
        }

        private static bool IsMajorRule(string line)
        {
            return GuideFormat.IsRule(line) && ((line ?? "").IndexOf('=') >= 0 || (line ?? "").IndexOf('═') >= 0);
        }

        /// <summary>Leading whitespace in display columns (space 1, tab 4, ideographic space 2).</summary>
        internal static int Indent(string s)
        {
            int n = 0;
            foreach (char c in s ?? "")
            {
                if (c == ' ') n++;
                else if (c == '\t') n += 4;
                else if (c == '\u3000') n += 2;
                else break;
            }
            return n;
        }

        /// <summary>Display columns of a text (CJK and full-width = 2).</summary>
        internal static int Columns(string s)
        {
            int n = 0;
            foreach (char c in s ?? "") n += (c >= 0x2E80 || (c >= 0xFF00 && c <= 0xFFEF) || (c >= 0x1100 && c <= 0x115F)) ? 2 : 1;
            return n;
        }

        /// <summary>Whitespace runs -> one space, trimmed.</summary>
        internal static string Squeeze(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool sp = false;
            foreach (char c in (s ?? "").Trim())
            {
                if (c == ' ' || c == '\t' || c == '\u3000') { sp = true; continue; }
                if (sp && sb.Length > 0) sb.Append(' ');
                sp = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The next txt line of the same paragraph: no space between Chinese text, one between two Latin words.</summary>
        internal static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            char x = a[a.Length - 1], y = b[0];
            bool latin = x < 0x80 && x != ' ' && y < 0x80 && (Alnum(y) || y == '(');
            return latin ? a + " " + b : a + b;
        }

        private static bool Marked(string t, char mark, out string after)
        {
            after = null;
            if (t.Length < 2 || t[0] != mark || (t[1] != '：' && t[1] != ':')) return false;
            after = t.Substring(2);
            return true;
        }

        /// <summary>「1. 文字」 / 「1．文字」 / 「1、文字」 (not 「4.17%」).</summary>
        internal static bool SplitStep(string t, out string num, out string rest)
        {
            num = rest = null;
            int n = 0;
            while (n < t.Length && n < 3 && Digit(t[n])) n++;
            if (n == 0 || n > 2 || n >= t.Length) return false;
            char p = t[n];
            if (p != '.' && p != '．' && p != '、') return false;
            int j = n + 1;
            if (j < t.Length && Digit(t[j])) return false;
            while (j < t.Length && (t[j] == ' ' || t[j] == '\u3000')) j++;
            if (j >= t.Length) return false;
            num = t.Substring(0, n);
            rest = t.Substring(j);
            return true;
        }

        /// <summary>「3  文字」 (a number, spaces, text) -- the tutorial's step list under an intro line.</summary>
        private static bool SplitNumRow(string t, out string num, out string rest)
        {
            num = rest = null;
            int n = 0;
            while (n < t.Length && n < 3 && Digit(t[n])) n++;
            if (n == 0 || n > 2 || n >= t.Length || t[n] != ' ') return false;
            int j = n;
            while (j < t.Length && t[j] == ' ') j++;
            if (j >= t.Length) return false;
            num = t.Substring(0, n);
            rest = t.Substring(j);
            return true;
        }

        /// <summary>「01 首页」 -> "01" + "首页".</summary>
        private static bool SplitCode(string t, out string code, out string rest)
        {
            code = rest = null;
            if (t.Length < 4 || !Digit(t[0]) || !Digit(t[1]) || t[2] != ' ') return false;
            code = t.Substring(0, 2);
            rest = t.Substring(3).Trim();
            return rest.Length > 0;
        }

        /// <summary>A table line with a single 「…」 as the leader (「「存为我的默认」… 把当前…」), only right after table rows.</summary>
        private static bool SplitSingleLeader(string t, out string left, out string right)
        {
            left = right = null;
            for (int i = 1; i + 1 < t.Length; i++)
            {
                if (t[i] != '…') continue;
                if (GuideFormat.IsLeaderChar(t[i - 1]) || GuideFormat.IsLeaderChar(t[i + 1])) continue;
                if (t[i + 1] != ' ' && t[i + 1] != '\u3000') continue;
                left = t.Substring(0, i).Trim();
                right = t.Substring(i + 1).Trim();
                return left.Length > 0 && right.Length > 0;
            }
            return false;
        }

        /// <summary>「经典：经典圆环、…」 under an intro line: a short name (no sentence marks), '：', the rest.</summary>
        private static bool SplitDef(string t, out string left, out string right)
        {
            left = right = null;
            int c = t.IndexOf('：');
            if (c < 1 || c + 1 >= t.Length) return false;
            string a = t.Substring(0, c).Trim();
            if (a.Length == 0 || GuideFormat.Em(a) > 10) return false;
            foreach (char ch in a) if ("，。；、「」（）".IndexOf(ch) >= 0) return false;
            left = a;
            right = t.Substring(c + 1).Trim();
            return right.Length > 0;
        }

        private static bool EndsIntro(string s)
        {
            string t = (s ?? "").Trim();
            return t.Length > 0 && (t[t.Length - 1] == '：' || t[t.Length - 1] == ':');
        }

        /// <summary>A lead-in line (「开始前：」, 「偏移量的单位：百分比偏移」): directly followed by a list, ends with '：' or is
        /// short and not a sentence.</summary>
        private static bool IsLeadIn(string t, string[] lines, int i)
        {
            if (t.Length == 0 || t.Length > 40 || i + 1 >= lines.Length) return false;
            char last = t[t.Length - 1];
            if ("。！？.!?".IndexOf(last) >= 0) return false;
            string n = (lines[i + 1] ?? "").Trim();
            string a, b;
            if (!(n.StartsWith("- ") || SplitStep(n, out a, out b))) return false;
            return last == '：' || last == ':' || GuideFormat.Em(t) <= 20;
        }

        // ------------------------------------------------------------------ runs (mono for keys and numbers)

        private static readonly string[] KeyNames = { "Ctrl", "Shift", "Alt", "Tab", "Esc", "Enter", "Space", "Win" };
        private static readonly string[] Units = { "ms", "px", "Hz", "fps" };

        /// <summary>
        /// Text / key / number runs covering s: keys = F1..F24, Ctrl / Shift / Alt / Tab / Esc, W A S D Q E and WA WD SA SD on
        /// their own, joined by '+' (Ctrl+D) and listed with ' ' or ' / ' (W / S / A / D); numbers = 75, 4.17%, 50–100,
        /// 2160×1440, 10/11, 1080p, 380 ms, v4.1. Not: letters or digits inside words (WMX200, D:\, 侧键1, X / Y axes).
        /// </summary>
        public static List<Run> Runs(string s)
        {
            List<Run> r = new List<Run>();
            if (string.IsNullOrEmpty(s)) return r;
            int i = 0, text = 0;
            while (i < s.Length)
            {
                int n = KeySeq(s, i), kind = RKey;
                if (n == 0) { n = Number(s, i); kind = RNum; }
                if (n == 0) { i++; continue; }
                if (i > text) Emit(r, text, i - text, RText);
                Emit(r, i, n, kind);
                i += n;
                text = i;
            }
            if (s.Length > text) Emit(r, text, s.Length - text, RText);
            return r;
        }

        private static void Emit(List<Run> r, int start, int len, int kind)
        {
            if (len <= 0) return;
            if (r.Count > 0)
            {
                Run last = r[r.Count - 1];
                if (last.Kind == kind && last.Start + last.Length == start) { last.Length += len; r[r.Count - 1] = last; return; }
            }
            Run x;
            x.Start = start;
            x.Length = len;
            x.Kind = kind;
            r.Add(x);
        }

        private static int KeySeq(string s, int i)
        {
            if (i > 0 && Wordish(s[i - 1])) return 0;
            int n = KeyCombo(s, i);
            if (n == 0) return 0;
            int end = i + n;
            while (end < s.Length)
            {
                int j = end;
                if (At(s, j, " / ")) j += 3;
                else if (At(s, j, "/") || At(s, j, " ")) j += 1;
                else break;
                int m = KeyCombo(s, j);
                if (m == 0) break;
                end = j + m;
            }
            return end - i;
        }

        private static int KeyCombo(string s, int i)
        {
            int n = KeyAtom(s, i);
            if (n == 0) return 0;
            int end = i + n;
            while (end + 1 < s.Length && s[end] == '+')
            {
                int m = KeyAtom(s, end + 1);
                if (m == 0) break;
                end += 1 + m;
            }
            return WordEnd(s, end) ? end - i : 0;
        }

        private static int KeyAtom(string s, int i)
        {
            if (i >= s.Length) return 0;
            char c = s[i];
            if (c == 'F' && Digit(Ch(s, i + 1)))
            {
                int j = i + 1, v = 0;
                while (j < s.Length && Digit(s[j]) && j - i <= 2) { v = v * 10 + (s[j] - '0'); j++; }
                return v >= 1 && v <= 24 && !Alnum(Ch(s, j)) ? j - i : 0;
            }
            foreach (string k in KeyNames)
                if (i + k.Length <= s.Length && string.CompareOrdinal(s, i, k, 0, k.Length) == 0 && !Alnum(Ch(s, i + k.Length))) return k.Length;
            if ("WASDQE".IndexOf(c) < 0) return 0;
            char n = Ch(s, i + 1);
            if ((c == 'W' || c == 'S') && (n == 'A' || n == 'D') && !Alnum(Ch(s, i + 2))) return 2; // WA / WD / SA / SD
            return Alnum(n) ? 0 : 1;
        }

        private static int Number(string s, int i)
        {
            char c = s[i];
            int j = i;
            if (c == 'v' && Digit(Ch(s, i + 1))) j++;          // v4.1
            else if (!Digit(c)) return 0;
            if (i > 0)
            {
                char p = s[i - 1];
                if (Alnum(p) || p == '_' || p == '.' || p == '\\' || p == '/' || p == '-' || Ideograph(p)) return 0; // WMX200, 侧键1
            }
            j = Suffix(s, Digits(s, j));
            while (j + 1 < s.Length && (s[j] == '–' || s[j] == '-' || s[j] == '~' || s[j] == '×' || s[j] == '/') && Digit(s[j + 1]))
                j = Suffix(s, Digits(s, j + 1));
            foreach (string u in Units)
            {
                int k = j;
                if (At(s, k, " ")) k++;
                if (At(s, k, u) && !Alnum(Ch(s, k + u.Length))) { j = k + u.Length; break; }
            }
            return Alnum(Ch(s, j)) ? 0 : j - i;
        }

        private static int Digits(string s, int j)
        {
            while (j < s.Length && Digit(s[j])) j++;
            // v5.0.2 (UI M5): every 「.digits」 group, so a 3-part version (v5.0.2) is one number run, not 「v5.0」 + text 「.2」
            while (j + 1 < s.Length && s[j] == '.' && Digit(s[j + 1])) { j++; while (j < s.Length && Digit(s[j])) j++; }
            return j;
        }

        private static int Suffix(string s, int j)
        {
            if (j < s.Length && s[j] == '%') return j + 1;
            if (j < s.Length && (s[j] == 'p' || s[j] == 'K' || s[j] == 'k') && !Alnum(Ch(s, j + 1))) return j + 1;
            return j;
        }

        private static bool Wordish(char c) { return Alnum(c) || c == '_' || c == '\\' || c == '/' || c == '.' || c == '@' || c == '-' || c == ':'; }

        private static bool WordEnd(string s, int j)
        {
            if (j >= s.Length) return true;
            char c = s[j];
            if (Alnum(c) || c == '_' || c == '\\' || c == ':' || c == '@') return false;
            if ((c == '.' || c == '-') && Alnum(Ch(s, j + 1))) return false;
            return true;
        }

        private static char Ch(string s, int i) { return i >= 0 && i < s.Length ? s[i] : '\0'; }
        private static bool At(string s, int i, string w) { return i >= 0 && i + w.Length <= s.Length && string.CompareOrdinal(s, i, w, 0, w.Length) == 0; }
        internal static bool Digit(char c) { return c >= '0' && c <= '9'; }
        internal static bool Alnum(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
        internal static bool Ideograph(char c) { return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF); }

        // ------------------------------------------------------------------ line breaking

        private struct Atom { public int Start, Length, Kind, W; public bool Space, Glue; }

        /// <summary>Never at the start of a line (closing marks).</summary>
        internal static bool NoStart(char c) { return "，。、；：？！）」』】》〉〕］｝％’”…～·ー々,.;:!?)]}%".IndexOf(c) >= 0; }

        /// <summary>Never at the end of a line (opening marks).</summary>
        internal static bool NoEnd(char c) { return "（「『【《〈〔［｛‘“([{".IndexOf(c) >= 0; }

        /// <summary>
        /// Lines of s for maxW (any unit). Chinese breaks between any two characters except before a closing mark or after an
        /// opening one, and never inside —— / ……; Latin words, key names and numbers stay whole (split only when one is
        /// wider than a whole line: URLs after / - . @ first, then anywhere). Whitespace runs are one gap of spaceW and
        /// vanish at a line break. width(text, kind) = advance of the text in that run kind's font. Nothing is lost: the
        /// pieces of all lines are s in order, minus the whitespace.
        /// </summary>
        public static List<List<Piece>> Wrap(string s, List<Run> runs, Func<string, int, int> width, int spaceW, int maxW)
        {
            List<List<Piece>> lines = new List<List<Piece>>();
            if (string.IsNullOrEmpty(s) || width == null) return lines;
            if (runs == null) runs = Runs(s);
            if (maxW < 1) maxW = 1;
            if (spaceW < 0) spaceW = 0;
            List<Atom> atoms = Atoms(s, runs, width);
            List<Piece> cur = new List<Piece>();
            int x = 0, a = 0;
            while (a < atoms.Count)
            {
                int b = a + 1;
                while (b < atoms.Count && atoms[b].Glue) b++; // a cluster: atoms a..b-1, no break inside
                int w = 0;
                for (int q = a; q < b; q++) w += atoms[q].W + (q > a && atoms[q].Space ? spaceW : 0);
                int gap = atoms[a].Space && cur.Count > 0 ? spaceW : 0;
                if (cur.Count > 0 && x + gap + w > maxW) { lines.Add(Merge(s, cur, spaceW)); cur = new List<Piece>(); x = 0; gap = 0; }
                if (x + gap + w <= maxW)
                {
                    x += gap;
                    for (int q = a; q < b; q++)
                    {
                        if (q > a && atoms[q].Space) x += spaceW; // 「分辨率 · 常规」 stays together, with its spaces
                        Place(cur, atoms[q], x);
                        x += atoms[q].W;
                    }
                }
                else
                {
                    // wider than a whole line: break inside it
                    List<Atom> parts = new List<Atom>();
                    for (int q = a; q < b; q++) SplitAtom(s, atoms[q], width, maxW, parts);
                    for (int q = 0; q < parts.Count; q++)
                    {
                        int g = parts[q].Space && cur.Count > 0 ? spaceW : 0;
                        if (cur.Count > 0 && x + g + parts[q].W > maxW) { lines.Add(Merge(s, cur, spaceW)); cur = new List<Piece>(); x = 0; g = 0; }
                        x += g;
                        Place(cur, parts[q], x);
                        x += parts[q].W;
                    }
                }
                a = b;
            }
            if (cur.Count > 0) lines.Add(Merge(s, cur, spaceW));
            return lines;
        }

        /// <summary>Width of s on one line (no wrapping), with the same spacing rules as Wrap.</summary>
        public static int LineWidth(string s, List<Run> runs, Func<string, int, int> width, int spaceW)
        {
            List<List<Piece>> l = Wrap(s, runs, width, spaceW, int.MaxValue / 4);
            if (l.Count == 0 || l[0].Count == 0) return 0;
            Piece p = l[0][l[0].Count - 1];
            return p.X + p.W;
        }

        private static List<Atom> Atoms(string s, List<Run> runs, Func<string, int, int> width)
        {
            List<Atom> a = new List<Atom>();
            bool space = false;
            foreach (Run r in runs)
            {
                int p = Math.Max(0, r.Start), end = Math.Min(s.Length, r.Start + r.Length);
                if (r.Kind != RText)
                {
                    if (end > p) { AddAtom(a, s, p, end - p, r.Kind, space, width); space = false; }
                    continue;
                }
                while (p < end)
                {
                    char c = s[p];
                    if (c == ' ' || c == '\t' || c == '\u3000' || c == '\n' || c == '\r') { space = true; p++; continue; }
                    int q = p + 1;
                    if (Latin(c)) { while (q < end && Latin(s[q])) q++; }
                    else if (char.IsHighSurrogate(c) && q < end && char.IsLowSurrogate(s[q])) q++;
                    AddAtom(a, s, p, q - p, RText, space, width);
                    space = false;
                    p = q;
                }
            }
            for (int i = 1; i < a.Count; i++)
            {
                Atom x = a[i];
                char first = s[x.Start], last = s[a[i - 1].Start + a[i - 1].Length - 1];
                bool glue;
                if (x.Space) glue = NoStart(first) || first == '▾' || first == '▸' || last == '·'; // 「括号 [ ]」, 「分辨率 · 常规」, 「管理 ▾」
                else glue = NoStart(first) || NoEnd(last) || (first == last && (first == '—' || first == '…' || first == '⋯'));
                if (glue)
                {
                    x.Glue = true;
                    a[i] = x;
                }
            }
            return a;
        }

        private static bool Latin(char c) { return c < 0x2000 && c != ' ' && c != '\t' && c != '\n' && c != '\r'; }

        private static void AddAtom(List<Atom> a, string s, int start, int len, int kind, bool space, Func<string, int, int> width)
        {
            Atom x = new Atom();
            x.Start = start;
            x.Length = len;
            x.Kind = kind;
            x.Space = space;
            x.W = Math.Max(0, width(s.Substring(start, len), kind));
            a.Add(x);
        }

        /// <summary>An atom wider than maxW: split after / - . @ _ first, then at every character.</summary>
        private static void SplitAtom(string s, Atom at, Func<string, int, int> width, int maxW, List<Atom> parts)
        {
            if (at.W <= maxW || at.Length <= 1) { parts.Add(at); return; }
            int p = at.Start, end = at.Start + at.Length;
            bool first = true;
            while (p < end)
            {
                int q = p;
                while (q < end && "/-.@_".IndexOf(s[q]) < 0) q++;
                q = Math.Min(end, q + 1);
                Atom seg = new Atom();
                seg.Start = p;
                seg.Length = q - p;
                seg.Kind = at.Kind;
                seg.Space = first && at.Space;
                seg.W = width(s.Substring(p, q - p), at.Kind);
                if (seg.W <= maxW || seg.Length <= 1) parts.Add(seg);
                else
                {
                    for (int c = p; c < q; )
                    {
                        int n = char.IsHighSurrogate(s[c]) && c + 1 < q ? 2 : 1;
                        Atom ch = new Atom();
                        ch.Start = c;
                        ch.Length = n;
                        ch.Kind = at.Kind;
                        ch.Space = first && at.Space && c == p;
                        ch.W = width(s.Substring(c, n), at.Kind);
                        parts.Add(ch);
                        c += n;
                    }
                }
                first = false;
                p = q;
            }
        }

        private static void Place(List<Piece> line, Atom a, int x)
        {
            Piece p = new Piece();
            p.Start = a.Start;
            p.Length = a.Length;
            p.Kind = a.Kind;
            p.X = x;
            p.W = a.W;
            line.Add(p);
        }

        /// <summary>Joins neighbours of the same kind (no gap, or exactly one space) into one piece: one DrawText per run.</summary>
        private static List<Piece> Merge(string s, List<Piece> line, int spaceW)
        {
            List<Piece> m = new List<Piece>();
            foreach (Piece p in line)
            {
                if (m.Count > 0)
                {
                    Piece last = m[m.Count - 1];
                    int end = last.Start + last.Length;
                    bool touch = last.Kind == p.Kind && end == p.Start && last.X + last.W == p.X;
                    bool oneSpace = last.Kind == p.Kind && p.Start == end + 1 && s[end] == ' ' && last.X + last.W + spaceW == p.X;
                    if (touch || oneSpace)
                    {
                        last.Length = p.Start + p.Length - last.Start;
                        last.W = p.X + p.W - last.X;
                        m[m.Count - 1] = last;
                        continue;
                    }
                }
                m.Add(p);
            }
            return m;
        }

        // ------------------------------------------------------------------ page layout

        /// <summary>
        /// 目录 + text column for a page 'width' px (chW = one Chinese character of the body font, sbW = the scroll bar). Wide
        /// when a 目录 of 9.5..13 characters, a 1.5-character gap and a text column of at least MinColumnChars fit: the
        /// 目录 sits at the page's left edge (= the page title's left edge, like every other page), the column is capped at
        /// MaxColumnChars and centred in the reading area right of the 目录, at most CentreChars further right. Otherwise the
        /// 目录 becomes a menu and the capped column is centred in the page (a full-width one starts at the left edge).
        /// Never overlaps: TocX+TocW &lt; ViewX &lt;= ColX, ColX+ColW &lt;= width-sbW.
        /// </summary>
        public static Plan Layout(int width, int chW, int sbW)
        {
            Plan p = new Plan();
            if (chW < 1) chW = 1;
            if (sbW < 0) sbW = 0;
            if (width < 1) width = 1;
            int pad = Math.Max(2, chW / 2), gap = Math.Max(2, chW * 3 / 2);
            int maxCol = chW * MaxColumnChars, minCol = chW * MinColumnChars;
            int toc = Math.Max(chW * 19 / 2, Math.Min(chW * 13, width * 22 / 100));
            int room = width - sbW - pad;
            if (room >= toc + gap + minCol)
            {
                p.Wide = true;
                p.TocX = 0;
                p.TocW = toc;
                p.ColW = Math.Min(maxCol, room - toc - gap);
                // centred in the reading area, but never more than CentreChars away from the 目录 (a very wide window
                // would leave the 目录 far from the text)
                p.ColX = toc + gap + Math.Min(chW * CentreChars, Math.Max(0, (room - toc - gap - p.ColW) / 2));
                p.ViewX = toc + gap / 2;
                return p;
            }
            p.ColW = Math.Max(1, Math.Min(maxCol, room));
            p.ColX = Math.Max(0, (room - p.ColW) / 2); // a full-width column starts at the page title's left edge
            if (p.ColX + p.ColW > width - sbW) p.ColW = Math.Max(1, width - sbW - p.ColX);
            return p;
        }
    }

#if !UI_PURE_CHECK
    internal sealed partial class SettingsForm
    {
        public const string GuideFileName = "说明 README.txt";
        /// <summary>v4.0 RichTextBox bold font, still disposed by SettingsForm.Dispose; the v4.1 page owns its fonts
        /// (GuideFonts), so this stays null.</summary>
        private Font guideBold;

        /// <summary>指南 (v4.1 GUIDE41): GuidePage = 目录 + the document view of GuideText.Text + 「打开说明文件」.</summary>
        private Control BuildGuide()
        {
            guideBold = null;
            GuidePage page = null;
            Button open = Btn("打开说明文件", delegate { OpenGuideFile(); });
            Tip(open, "用记事本打开启动程序旁边的「" + GuideFileName + "」（和这一页内容一样）");
            Button toc = null;
            toc = Btn("目录 ▾", delegate { if (page != null) page.ShowTocMenu(toc); });
            Tip(toc, "跳到某一节");
            Label note = Lbl("和文件夹里的「" + GuideFileName + "」内容一样。");
            note.ForeColor = SystemColors.GrayText;
            page = new GuidePage(GuideText.Text, k, open, toc, note);
            return page;
        }

        /// <summary>Opens 「说明 README.txt」 next to the launcher (SettingsIO.UserDir); README.txt as a fallback name.</summary>
        private void OpenGuideFile()
        {
            string dir;
            // v5.0.2 (W19): 说明 README.txt is next to the exe, not in 数据 Data
            try { dir = SettingsIO.TopDir(); } catch (Exception) { dir = AppDomain.CurrentDomain.BaseDirectory; }
            string path = null;
            foreach (string n in new string[] { GuideFileName, "README.txt" })
            {
                try { string p = Path.Combine(dir, n); if (File.Exists(p)) { path = p; break; } }
                catch (Exception) { }
            }
            if (path == null)
            {
                Say("没找到「" + GuideFileName + "」（应该在启动程序旁边）。这一页就是同样的内容。");
                return;
            }
            try { Process.Start(path); }
            catch (Exception)
            {
                try { Process.Start("notepad.exe", "\"" + path + "\""); }
                catch (Exception ex) { Say("打不开说明文件：" + ex.Message); }
            }
        }
    }

    /// <summary>The 指南 page's fonts (from the window font = 11 pt, fixed) with cached text widths and line metrics.</summary>
    internal sealed class GuideFonts : IDisposable
    {
        public readonly Font Body, Bold, Mono, MonoBold, H1, H1Mono, H2, H2Mono, Title, TitleMono;
        /// <summary>One Chinese character of Body; line pitch of body text and of the 目录.</summary>
        public readonly int ChW, Pitch, TocPitch;
        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private readonly Dictionary<Font, Dictionary<string, int>> widths = new Dictionary<Font, Dictionary<string, int>>();
        private readonly Dictionary<Font, int[]> metrics = new Dictionary<Font, int[]>();

        public GuideFonts(Font basis)
        {
            float pt = Math.Max(11f, basis != null ? basis.SizeInPoints : 11f); // SPEC v4: body >= 11 pt
            Body = Theme.Ui(pt);
            Bold = Theme.Ui(pt, FontStyle.Bold);
            Mono = Theme.Mono(pt);
            MonoBold = Theme.Mono(pt, FontStyle.Bold);
            H1 = Theme.Ui(pt * 1.25f, FontStyle.Bold);
            H1Mono = Theme.Mono(pt * 1.25f, FontStyle.Bold);
            H2 = Theme.Ui(pt * 1.1f, FontStyle.Bold);
            H2Mono = Theme.Mono(pt * 1.1f, FontStyle.Bold);
            Title = Theme.Ui(pt * 1.4f, FontStyle.Bold);
            TitleMono = Theme.Mono(pt * 1.1f);
            ChW = Math.Max(4, Width(Body, "中"));
            Pitch = Math.Max(Body.Height + 2, (int)Math.Round(Body.Height * 1.32));
            TocPitch = Math.Max(Body.Height + 1, (int)Math.Round(Body.Height * 1.12));
        }

        /// <summary>Advance width of s in f (TextRenderer, no padding); cached, at most 20000 strings per font.</summary>
        public int Width(Font f, string s)
        {
            if (f == null || string.IsNullOrEmpty(s)) return 0;
            Dictionary<string, int> m;
            if (!widths.TryGetValue(f, out m)) { m = new Dictionary<string, int>(); widths[f] = m; }
            int w;
            if (m.TryGetValue(s, out w)) return w;
            try { w = TextRenderer.MeasureText(s, f, new Size(100000, 100000), Flags).Width; }
            catch (Exception) { w = (int)Math.Round(f.Height * 0.8 * s.Length); }
            if (m.Count < 20000) m[s] = w;
            return w;
        }

        /// <summary>Width of one space (measured between two letters: a lone space measures 0 on some systems).</summary>
        public int Space(Font f) { return Math.Max(1, Width(f, "a a") - Width(f, "aa")); }

        public int Ascent(Font f) { return Metric(f)[0]; }
        public int Descent(Font f) { return Metric(f)[1]; }

        private int[] Metric(Font f)
        {
            int[] r;
            if (metrics.TryGetValue(f, out r)) return r;
            r = new int[2];
            try
            {
                FontFamily ff = f.FontFamily;
                double line = ff.GetLineSpacing(f.Style);
                r[0] = (int)Math.Round(f.Height * ff.GetCellAscent(f.Style) / line);
                r[1] = (int)Math.Round(f.Height * ff.GetCellDescent(f.Style) / line);
            }
            catch (Exception) { r[0] = 0; }
            if (r[0] <= 0 || r[0] > f.Height) { r[0] = (int)Math.Round(f.Height * 0.78); r[1] = Math.Max(0, f.Height - r[0]); }
            metrics[f] = r;
            return r;
        }

        public void Dispose()
        {
            foreach (Font f in new Font[] { Body, Bold, Mono, MonoBold, H1, H1Mono, H2, H2Mono, Title, TitleMono })
                if (f != null) { try { f.Dispose(); } catch (Exception) { } }
            widths.Clear();
            metrics.Clear();
        }
    }

    /// <summary>
    /// The 指南 page (v4.1 GUIDE41): 目录 (GuideToc) + document (GuideView) + 「打开说明文件」. Wide pages: the 目录 is a column
    /// left of the capped text column, the pair centred, the button under the 目录. Narrow pages: one row 「目录 ▾」
    /// 「打开说明文件」 (+ the note when it fits) above the centred column. No timers; while the window is dragged
    /// (IPausable) nothing is laid out; on release one layout.
    /// </summary>
    internal sealed class GuidePage : Control, IPausable
    {
        private readonly GuideDoc.Doc doc;
        private readonly string source;
        private readonly GuideView view;
        private readonly GuideToc toc;
        private readonly Button open, tocButton;
        private readonly Label note;
        private readonly ContextMenuStrip tocMenu, copyMenu;
        private readonly float k;
        private GuideFonts fonts;
        private bool paused, pendingLayout, pendingFonts;

        public GuidePage(string text, float scale, Button open, Button tocButton, Label note)
        {
            k = scale > 0 ? scale : 1f;
            source = text ?? "";
            doc = GuideDoc.Parse(source);
            this.open = open;
            this.tocButton = tocButton;
            this.note = note;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.ColSurface;
            Tag = "theme:keep-guide";
            TabStop = false;
            view = new GuideView(doc, source, k);
            toc = new GuideToc(doc.Toc, k);
            toc.WheelTarget = view;
            toc.Jump += delegate(int i) { view.JumpTo(i); };
            view.SectionChanged += delegate(int i) { toc.Current = i; };
            // the buttons keep the window's own AutoSize height (same as every other button); the 目录 width comes from
            // MinimumSize / MaximumSize. Placed by hand at a column edge: no margin, anchored top-left.
            open.Margin = tocButton.Margin = note.Margin = new Padding(0);
            open.Anchor = tocButton.Anchor = note.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            note.AutoSize = true;
            toc.TabIndex = 0;
            view.TabIndex = 1;
            tocButton.TabIndex = 2;
            open.TabIndex = 3;
            tocMenu = Menu();
            copyMenu = Menu();
            ToolStripMenuItem copy = new ToolStripMenuItem("复制全文");
            copy.Click += delegate { view.CopyAll(); };
            copyMenu.Items.Add(copy);
            view.ContextMenuStrip = copyMenu;
            Controls.Add(view);
            Controls.Add(toc);
            Controls.Add(open);
            Controls.Add(tocButton);
            Controls.Add(note);
            RebuildFonts();
        }

        private ContextMenuStrip Menu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            try
            {
                m.RenderMode = ToolStripRenderMode.System; // same look as the window's other menus (SettingsForm.StyleMenu)
                m.BackColor = Theme.ColInput;
                m.ForeColor = Theme.ColText;
                m.ShowImageMargin = false;
            }
            catch (Exception) { }
            return m;
        }

        /// <summary>The 「目录 ▾」 menu (narrow pages): every section, pages indented, the current one checked.</summary>
        public void ShowTocMenu(Control anchor)
        {
            try
            {
                tocMenu.Items.Clear();
                tocMenu.ShowCheckMargin = true;
                for (int i = 0; i < doc.Toc.Count; i++)
                {
                    GuideDoc.Entry e = doc.Toc[i];
                    string t = e.Level == 2 ? "　　" + (e.Num.Length > 0 ? e.Num + "  " : "") + e.Label : e.Label;
                    ToolStripMenuItem it = new ToolStripMenuItem(t);
                    it.Checked = i == view.Current;
                    int idx = i;
                    it.Click += delegate { view.JumpTo(idx); view.Focus(); };
                    tocMenu.Items.Add(it);
                }
                Control a = anchor ?? this;
                tocMenu.Show(a, new Point(0, a.Height));
            }
            catch (Exception ex) { Program.Log("UI: guide menu: " + ex.Message); }
        }

        public void Pause() { paused = true; }

        public void Resume()
        {
            if (!paused) return;
            paused = false;
            if (IsDisposed) return;
            if (pendingFonts) RebuildFonts();
            else if (pendingLayout) DoLayout();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (paused) { pendingFonts = true; return; }
            RebuildFonts();
        }

        private void RebuildFonts()
        {
            pendingFonts = false;
            GuideFonts old = fonts;
            fonts = new GuideFonts(Font);
            view.SetFonts(fonts);
            toc.SetFonts(fonts);
            try { tocMenu.Font = Font; copyMenu.Font = Font; } catch (Exception) { }
            if (old != null) old.Dispose(); // after view / 目录 switched to the new set
            DoLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            DoLayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            DoLayout();
        }

        private bool inLayout;

        private void DoLayout()
        {
            if (paused) { pendingLayout = true; return; }
            if (inLayout) return; // moving a child raises another layout pass: one is enough
            pendingLayout = false;
            if (fonts == null || IsDisposed) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            inLayout = true;
            try { Place(w, h); }
            finally { inLayout = false; }
        }

        private void Place(int w, int h)
        {
            GuideDoc.Plan p = GuideDoc.Layout(w, fonts.ChW, view.ScrollBarWidth);
            int gapV = Math.Max((int)Math.Round(6 * k), fonts.Body.Height / 2);
            int gapH = Math.Max((int)Math.Round(6 * k), fonts.ChW / 2);
            if (p.Wide)
            {
                tocButton.Visible = false;
                note.Visible = false;
                toc.Visible = true;
                Size fixedW = new Size(p.TocW, 0); // 「打开说明文件」 as wide as the 目录, flush with its left edge
                if (open.MinimumSize != fixedW || open.MaximumSize != fixedW)
                {
                    open.MinimumSize = Size.Empty;
                    open.MaximumSize = fixedW;
                    open.MinimumSize = fixedW;
                }
                int bh = open.GetPreferredSize(Size.Empty).Height;
                int tocH = Math.Min(toc.HeightFor(p.TocW), Math.Max(0, h - bh - gapV));
                toc.Bounds = new Rectangle(p.TocX, 0, p.TocW, tocH);
                open.Location = new Point(p.TocX, Math.Min(tocH + gapV, Math.Max(0, h - bh)));
                view.Bounds = new Rectangle(p.ViewX, 0, Math.Max(0, w - p.ViewX), h);
                view.SetColumn(p.ColX - p.ViewX, p.ColW);
            }
            else
            {
                toc.Visible = false;
                tocButton.Visible = true;
                open.MinimumSize = open.MaximumSize = Size.Empty;
                int bh = Math.Max(open.GetPreferredSize(Size.Empty).Height, tocButton.GetPreferredSize(Size.Empty).Height);
                int x = Math.Max(0, p.ColX); // the buttons start at the text column's left edge
                tocButton.Location = new Point(x, 0);
                open.Location = new Point(x + tocButton.GetPreferredSize(Size.Empty).Width + gapH, 0);
                Size ns = note.GetPreferredSize(Size.Empty);
                int nx = open.Left + open.GetPreferredSize(Size.Empty).Width + gapH;
                bool fits = nx + ns.Width <= w - view.ScrollBarWidth;
                note.Visible = fits;
                if (fits) note.Location = new Point(nx, Math.Max(0, (bh - ns.Height) / 2));
                int vy = bh + gapV;
                view.Bounds = new Rectangle(0, vy, w, Math.Max(0, h - vy));
                view.SetColumn(p.ColX, p.ColW);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            view.Wheel(e.Delta);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.ColSurface)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); // view / 目录 first: they draw with the fonts
            if (disposing)
            {
                try { tocMenu.Dispose(); copyMenu.Dispose(); } catch (Exception) { }
                if (fonts != null) { fonts.Dispose(); fonts = null; }
            }
        }
    }

    /// <summary>A text block placed for one width: the text, its style, where it starts, its lines.</summary>
    internal sealed class GuideArea
    {
        public string S;
        public Font Text, Mono;
        public Color Fore, Key, Num;
        public int Pitch, X, Y;
        public List<List<GuideDoc.Piece>> Lines;
    }

    /// <summary>
    /// The document (v4.1 GUIDE41): blocks of GuideDoc laid out for the text column and drawn by hand -- headings, the ★
    /// note, paragraphs, bullets, numbered steps joined by a thin line, tables with hairlines, 问 / 答; mono for keys and
    /// numbers. Scrolls by wheel, keys (↑ ↓ PageUp PageDown Home End Space), its own dark scroll bar; only the visible
    /// part is drawn. Raises SectionChanged with the 目录 entry at the top. Right click / Ctrl+C: 复制全文.
    /// </summary>
    internal sealed class GuideView : Control, IPausable
    {
        private sealed class Item
        {
            public int Y, H, Kind, X, W;
            public readonly List<GuideArea> Areas = new List<GuideArea>();
            public string Mark = "";
            public Font MarkFont;
            public Color MarkColor;
            public int MarkX, MarkBase;
            public string Num = "";
            public Font NumFont;
            public Color NumColor;
            public int NumRight, NumBase;
            public int DotX, DotY, DotS;
            public bool DotHollow, Box, LineTop, LineBottom, Rule;
            public int RuleY;
        }

        private struct Rail { public int X, Y1, Y2; }

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private readonly GuideDoc.Doc doc;
        private readonly string source;
        private readonly float k;
        private readonly List<GuideDoc.Run>[] textRuns, labelRuns;
        private readonly List<Item> items = new List<Item>();
        private readonly List<Rail> rails = new List<Rail>();
        private GuideFonts fonts;
        private int colX, colW, wantX, wantW, scrollY, contentH, current = -1, pinned = -1, pinnedScroll = -1;
        private bool paused, dirty, dragging, thumbHover;
        private int dragOffset;
        private readonly Color keyColor = Theme.Blend(Theme.ColText, Theme.ColAccent2, 0.5);

        public event Action<int> SectionChanged;

        public GuideView(GuideDoc.Doc doc, string source, float scale)
        {
            this.doc = doc;
            this.source = source ?? "";
            k = scale > 0 ? scale : 1f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            Tag = "theme:keep-guide-view";
            TabStop = true;
            AccessibleRole = AccessibleRole.Document;
            AccessibleName = "指南";
            textRuns = new List<GuideDoc.Run>[doc.Blocks.Count];
            labelRuns = new List<GuideDoc.Run>[doc.Blocks.Count];
            for (int i = 0; i < doc.Blocks.Count; i++)
            {
                textRuns[i] = GuideDoc.Runs(doc.Blocks[i].Text);
                labelRuns[i] = GuideDoc.Runs(doc.Blocks[i].Label);
            }
        }

        public int ScrollBarWidth { get { return Math.Max(8, (int)Math.Round(11 * k)); } }

        /// <summary>The 目录 entry of the section at the top (-1 above the first one).</summary>
        public int Current { get { return current; } }

        public void SetFonts(GuideFonts f)
        {
            fonts = f;
            dirty = true;
            if (!paused) { Relayout(); Invalidate(); }
        }

        /// <summary>The text column inside this control (x, width).</summary>
        public void SetColumn(int x, int w)
        {
            wantX = Math.Max(0, x);
            wantW = Math.Max(1, w);
            if (wantX == colX && wantW == colW && !dirty) { ClampScroll(); Invalidate(); return; }
            dirty = true;
            if (!paused) { Relayout(); Invalidate(); }
        }

        public void Pause() { paused = true; dragging = false; }

        public void Resume()
        {
            if (!paused) return;
            paused = false;
            if (dirty) Relayout();
            ClampScroll();
            Invalidate();
        }

        // ---------------------------------------------------------------- layout

        private Func<string, int, int> Measure(Font text, Font mono)
        {
            GuideFonts f = fonts;
            return delegate(string s, int kind) { return f.Width(kind == GuideDoc.RText ? text : mono, s); };
        }

        private GuideArea Area(string s, List<GuideDoc.Run> runs, Font text, Font mono, Color fore, int pitch, int x, int y, int w)
        {
            GuideArea a = new GuideArea();
            a.S = s ?? "";
            a.Text = text;
            a.Mono = mono;
            a.Fore = fore;
            a.Key = fore == Theme.ColText ? keyColor : fore;
            a.Num = fore;
            a.Pitch = pitch;
            a.X = x;
            a.Y = y;
            a.Lines = GuideDoc.Wrap(a.S, runs, Measure(text, mono), fonts.Space(text), Math.Max(1, w));
            return a;
        }

        private int Base(GuideArea a) { return a.Y + (a.Pitch - (fonts.Ascent(a.Text) + fonts.Descent(a.Text))) / 2 + fonts.Ascent(a.Text); }

        private static int Lines(GuideArea a) { return Math.Max(1, a.Lines.Count); }

        private void Relayout()
        {
            dirty = false;
            // keep the reading position: the block at the top and how far into it
            int anchor = -1;
            double frac = 0;
            if (items.Count > 0 && pinned < 0 && scrollY > 0) // at the very top: stay at the top
            {
                anchor = ItemAt(scrollY);
                if (anchor >= 0 && items[anchor].H > 0) frac = Math.Max(0, Math.Min(1, (scrollY - items[anchor].Y) / (double)items[anchor].H));
            }
            colX = wantX;
            colW = Math.Max(1, wantW);
            items.Clear();
            rails.Clear();
            if (fonts == null) return;
            GuideFonts f = fonts;
            int chW = f.ChW, pitch = f.Pitch;
            int bulletGutter = (int)Math.Round(chW * 1.2);
            int numW = f.Width(f.MonoBold, "00");
            int stepGutter = numW + (int)Math.Round(chW * 0.7);
            int rowNumW = f.Width(f.Mono, "00") + (int)Math.Round(chW * 0.6);
            int qaGutter = f.Width(f.Bold, "答：") + Math.Max(2, chW / 4);
            int line1 = Math.Max(1, (int)Math.Round(k));
            int[] childX = new int[8];
            Dictionary<int, int> labelCol = new Dictionary<int, int>();
            int y = (int)Math.Round(pitch * 0.3);
            for (int i = 0; i < doc.Blocks.Count; i++)
            {
                GuideDoc.Block b = doc.Blocks[i];
                GuideDoc.Block prev = i > 0 ? doc.Blocks[i - 1] : null;
                int d = Math.Min(b.Depth, childX.Length - 1);
                int x0 = d == 0 ? 0 : childX[d];
                int w0 = Math.Max(chW * 4, colW - x0);
                y += Before(b, prev, pitch);
                Item it = new Item();
                it.Kind = b.Kind;
                it.Y = y;
                it.X = x0;
                it.W = w0;
                GuideArea a;
                switch (b.Kind)
                {
                    case GuideDoc.Title:
                        a = Area(b.Text, textRuns[i], f.Title, f.TitleMono, Theme.ColText, (int)Math.Round(f.Title.Height * 1.2), x0, y, w0);
                        a.Num = a.Key = Theme.ColMuted;
                        it.Areas.Add(a);
                        it.H = Lines(a) * a.Pitch;
                        break;
                    case GuideDoc.Callout:
                    {
                        int padV = (int)Math.Round(pitch * 0.45), padH = (int)Math.Round(chW * 0.8), bar = Math.Max(2, (int)Math.Round(3 * k));
                        int star = f.Width(f.Body, "★") + (int)Math.Round(chW * 0.5);
                        int tx = x0 + bar + padH + star;
                        a = Area(b.Text, textRuns[i], f.Body, f.Mono, Theme.ColText, pitch, tx, y + padV, x0 + w0 - padH - tx);
                        it.Areas.Add(a);
                        it.Box = true;
                        it.H = Lines(a) * pitch + 2 * padV;
                        it.Mark = "★";
                        it.MarkFont = f.Body;
                        it.MarkColor = Theme.ColAccent;
                        it.MarkX = x0 + bar + padH;
                        it.MarkBase = Base(a);
                        break;
                    }
                    case GuideDoc.H1:
                        a = Area(b.Text, textRuns[i], f.H1, f.H1Mono, Theme.ColAccent2, (int)Math.Round(f.H1.Height * 1.2), x0, y, w0);
                        it.Areas.Add(a);
                        it.Rule = true;
                        it.RuleY = y + Lines(a) * a.Pitch + (int)Math.Round(pitch * 0.12);
                        it.H = it.RuleY - y + line1 + (int)Math.Round(pitch * 0.42);
                        break;
                    case GuideDoc.H2:
                    {
                        int tx = x0;
                        int hp = (int)Math.Round(f.H2.Height * 1.2);
                        if (b.Num.Length > 0)
                        {
                            it.Num = b.Num;
                            it.NumFont = f.H2Mono;
                            it.NumColor = Theme.ColAccent;
                            tx = x0 + f.Width(f.H2Mono, b.Num) + (int)Math.Round(chW * 0.55);
                            it.NumRight = tx - (int)Math.Round(chW * 0.55);
                        }
                        a = Area(b.Text, textRuns[i], f.H2, f.H2Mono, Theme.ColText, hp, tx, y, x0 + w0 - tx);
                        it.Areas.Add(a);
                        it.NumBase = Base(a);
                        it.H = Lines(a) * hp + (int)Math.Round(pitch * 0.18);
                        break;
                    }
                    case GuideDoc.H3:
                        a = Area(b.Text, textRuns[i], f.Bold, f.MonoBold, Theme.ColText, pitch, x0, y, w0);
                        it.Areas.Add(a);
                        it.H = Lines(a) * pitch;
                        break;
                    case GuideDoc.Bullet:
                    {
                        int tx = x0 + bulletGutter;
                        a = Area(b.Text, textRuns[i], f.Body, f.Mono, Theme.ColText, pitch, tx, y, x0 + w0 - tx);
                        it.Areas.Add(a);
                        it.H = Lines(a) * pitch;
                        it.DotS = Math.Max(3, (int)Math.Round(chW * (d == 0 ? 0.27 : 0.24)));
                        it.DotHollow = d > 0;
                        it.DotX = x0 + (int)Math.Round(chW * 0.25);
                        it.DotY = Base(a) - (int)Math.Round(f.Ascent(f.Body) * 0.36) - it.DotS / 2;
                        childX[Math.Min(d + 1, childX.Length - 1)] = tx;
                        break;
                    }
                    case GuideDoc.Step:
                    {
                        int tx = x0 + stepGutter;
                        a = Area(b.Text, textRuns[i], f.Body, f.Mono, Theme.ColText, pitch, tx, y, x0 + w0 - tx);
                        it.Areas.Add(a);
                        it.H = Lines(a) * pitch;
                        it.Num = b.Num;
                        it.NumFont = f.MonoBold;
                        it.NumColor = Theme.ColAccent;
                        it.NumRight = x0 + numW;
                        it.NumBase = Base(a);
                        childX[Math.Min(d + 1, childX.Length - 1)] = tx;
                        break;
                    }
                    case GuideDoc.Row:
                    {
                        int padV = (int)Math.Round(pitch * 0.22), padH = (int)Math.Round(chW * 0.35);
                        int lx = x0 + padH + (b.Num.Length > 0 ? rowNumW : 0);
                        int lw;
                        if (!labelCol.TryGetValue(b.Table, out lw))
                        {
                            lw = 0;
                            for (int j = i; j < doc.Blocks.Count && doc.Blocks[j].Kind == GuideDoc.Row && doc.Blocks[j].Table == b.Table; j++)
                                lw = Math.Max(lw, GuideDoc.LineWidth(doc.Blocks[j].Label, labelRuns[j], Measure(f.Bold, f.MonoBold), f.Space(f.Bold)));
                            lw = Math.Max(chW * 3, Math.Min(lw, (int)Math.Round((x0 + w0 - lx) * 0.42)));
                            labelCol[b.Table] = lw;
                        }
                        bool hasText = b.Text.Length > 0;
                        GuideArea la = Area(b.Label, labelRuns[i], f.Bold, f.MonoBold, Theme.ColText, pitch, lx, y + padV,
                            hasText ? lw : x0 + w0 - padH - lx);
                        it.Areas.Add(la);
                        int nl = Lines(la);
                        if (hasText)
                        {
                            int tx = lx + lw + (int)Math.Round(chW * 0.9);
                            GuideArea ta = Area(b.Text, textRuns[i], f.Body, f.Mono, Theme.ColText, pitch, tx, y + padV, x0 + w0 - padH - tx);
                            it.Areas.Add(ta);
                            nl = Math.Max(nl, Lines(ta));
                        }
                        if (b.Num.Length > 0)
                        {
                            it.Num = b.Num;
                            it.NumFont = f.Mono;
                            it.NumColor = Theme.Blend(Theme.ColMuted, Theme.ColAccent, 0.55);
                            it.NumRight = x0 + padH + f.Width(f.Mono, "00");
                            it.NumBase = Base(la);
                        }
                        it.H = nl * pitch + 2 * padV;
                        it.LineTop = prev == null || prev.Kind != GuideDoc.Row || prev.Table != b.Table;
                        it.LineBottom = true;
                        break;
                    }
                    case GuideDoc.Question:
                    {
                        int tx = x0 + qaGutter;
                        a = Area(b.Text, textRuns[i], f.Bold, f.MonoBold, Theme.ColText, pitch, tx, y, x0 + w0 - tx);
                        it.Areas.Add(a);
                        it.H = Lines(a) * pitch;
                        it.Mark = b.Label + "：";
                        it.MarkFont = f.Bold;
                        it.MarkColor = Theme.ColAccent;
                        it.MarkX = x0;
                        it.MarkBase = Base(a);
                        childX[Math.Min(d + 1, childX.Length - 1)] = tx;
                        break;
                    }
                    default: // paragraph
                        a = Area(b.Text, textRuns[i], f.Body, f.Mono, Theme.ColText, pitch, x0, y, w0);
                        it.Areas.Add(a);
                        it.H = Lines(a) * pitch;
                        break;
                }
                if (b.Mark.Length > 0 && it.Areas.Count > 0)
                {
                    // 「答：」 in the gutter left of the answer, on its first line
                    it.Mark = b.Mark + "：";
                    it.MarkFont = f.Body;
                    it.MarkColor = Theme.ColMuted;
                    it.MarkX = Math.Max(0, x0 - qaGutter);
                    it.MarkBase = Base(it.Areas[0]);
                }
                items.Add(it);
                y = it.Y + it.H;
            }
            contentH = y + pitch * 2;
            // a thin line from each step number down to the next step of the same list
            for (int i = 0; i < doc.Blocks.Count; i++)
            {
                GuideDoc.Block b = doc.Blocks[i];
                if (b.Kind != GuideDoc.Step || b.List < 0) continue;
                for (int j = i + 1; j < doc.Blocks.Count; j++)
                {
                    GuideDoc.Block n = doc.Blocks[j];
                    if (n.Depth < b.Depth || (n.Depth == b.Depth && n.Kind != GuideDoc.Step)) break;
                    if (n.Kind != GuideDoc.Step || n.List != b.List) continue;
                    Item a = items[i], c = items[j];
                    Rail r;
                    r.X = a.NumRight - fonts.Width(fonts.MonoBold, "0") / 2;
                    r.Y1 = a.NumBase + fonts.Descent(fonts.MonoBold) + (int)Math.Round(pitch * 0.12);
                    r.Y2 = c.NumBase - fonts.Ascent(fonts.MonoBold) - (int)Math.Round(pitch * 0.08);
                    if (r.Y2 - r.Y1 > pitch / 4) rails.Add(r);
                    break;
                }
            }
            if (pinned >= 0 && pinned < doc.Toc.Count) scrollY = TargetOf(pinned);
            else if (anchor >= 0 && anchor < items.Count) scrollY = items[anchor].Y + (int)Math.Round(frac * items[anchor].H);
            ClampScroll();
            if (pinned >= 0) pinnedScroll = scrollY;
            UpdateCurrent();
        }

        /// <summary>Space above a block: sections far apart, items of one list close, rows of one table touching.</summary>
        private static int Before(GuideDoc.Block b, GuideDoc.Block p, int pitch)
        {
            if (p == null) return 0;
            double s;
            bool afterHead = p.Kind == GuideDoc.H1 || p.Kind == GuideDoc.H2 || p.Kind == GuideDoc.H3;
            switch (b.Kind)
            {
                case GuideDoc.Callout: s = 0.55; break;
                case GuideDoc.H1: s = p.Kind == GuideDoc.Title || p.Kind == GuideDoc.Callout ? 1.3 : 1.75; break;
                case GuideDoc.H2: s = p.Kind == GuideDoc.H1 ? 0.45 : 1.3; break;
                case GuideDoc.H3: s = afterHead ? 0.3 : 0.8; break;
                case GuideDoc.Question: s = afterHead ? 0.35 : 1.0; break;
                default:
                    if (afterHead) s = p.Kind == GuideDoc.H3 ? 0.12 : 0.3;
                    else if (b.Kind == GuideDoc.Row && p.Kind == GuideDoc.Row && p.Table == b.Table) s = 0;
                    else if (b.Depth > p.Depth) s = b.Kind == GuideDoc.Row ? 0.3 : 0.18;
                    else if (b.Depth < p.Depth) s = 0.3;
                    else if (p.Kind == GuideDoc.Question) s = 0.12;
                    else if (b.Kind == p.Kind && (b.Kind == GuideDoc.Bullet || b.Kind == GuideDoc.Step)) s = 0.2;
                    else s = 0.55;
                    break;
            }
            return (int)Math.Round(s * pitch);
        }

        // ---------------------------------------------------------------- scrolling

        private int ViewH { get { return Math.Max(1, ClientSize.Height); } }
        private int MaxScroll { get { return Math.Max(0, contentH - ViewH); } }

        private void ClampScroll()
        {
            int m = MaxScroll;
            if (scrollY > m) scrollY = m;
            if (scrollY < 0) scrollY = 0;
        }

        private int ItemAt(int y)
        {
            int lo = 0, hi = items.Count - 1, r = items.Count > 0 ? 0 : -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (items[mid].Y <= y) { r = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return r;
        }

        private int TargetOf(int entry)
        {
            int bi = doc.Toc[entry].Block;
            if (bi < 0 || bi >= items.Count) return 0;
            int y = items[bi].Y - (int)Math.Round(fonts != null ? fonts.Pitch * 0.35 : 0);
            return Math.Max(0, y);
        }

        public void ScrollTo(int y)
        {
            int old = scrollY;
            scrollY = y;
            ClampScroll();
            if (scrollY != old) Invalidate();
            if (pinned >= 0 && scrollY != pinnedScroll) pinned = -1; // the user scrolled away from the jump target
            UpdateCurrent();
        }

        /// <summary>Scrolls the section of 目录 entry i to the top (as far as the end of the document allows).</summary>
        public void JumpTo(int entry)
        {
            if (entry < 0 || entry >= doc.Toc.Count || items.Count == 0) return;
            pinned = -1;
            ScrollTo(TargetOf(entry));
            pinned = entry;
            pinnedScroll = scrollY;
            UpdateCurrent();
        }

        public void Wheel(int delta)
        {
            if (fonts == null) return;
            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines <= 0 ? Math.Max(fonts.Pitch, ViewH - fonts.Pitch) : lines * fonts.Pitch;
            ScrollTo(scrollY - (int)((long)delta * step / 120));
        }

        private void UpdateCurrent()
        {
            int cur = -1;
            if (pinned >= 0 && scrollY == pinnedScroll) cur = pinned;
            else if (fonts != null && items.Count > 0)
            {
                int probe = scrollY + (int)Math.Round(fonts.Pitch * 1.2);
                for (int e = 0; e < doc.Toc.Count; e++)
                {
                    int bi = doc.Toc[e].Block;
                    if (bi < items.Count && items[bi].Y <= probe) cur = e; else break;
                }
            }
            if (cur == current) return;
            current = cur;
            Action<int> h = SectionChanged;
            if (h != null) h(cur);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (paused) return; // the window is being dragged: keep the last frame, lay out on release
            ClampScroll();
            UpdateCurrent();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HandledMouseEventArgs he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            Wheel(e.Delta);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys c = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) != 0) return base.IsInputKey(keyData);
            if (c == Keys.Up || c == Keys.Down || c == Keys.PageUp || c == Keys.PageDown || c == Keys.Home || c == Keys.End || c == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (fonts == null || e.Handled) return;
            int page = Math.Max(fonts.Pitch, ViewH - fonts.Pitch * 2);
            if (e.Control && (e.KeyCode == Keys.C || e.KeyCode == Keys.A)) { if (e.KeyCode == Keys.C) CopyAll(); e.Handled = true; return; }
            if (e.Control || e.Alt) return;
            switch (e.KeyCode)
            {
                case Keys.Up: ScrollTo(scrollY - fonts.Pitch * 2); break;
                case Keys.Down: ScrollTo(scrollY + fonts.Pitch * 2); break;
                case Keys.PageUp: ScrollTo(scrollY - page); break;
                case Keys.PageDown: ScrollTo(scrollY + page); break;
                case Keys.Space: ScrollTo(scrollY + (e.Shift ? -page : page)); break;
                case Keys.Home: ScrollTo(0); break;
                case Keys.End: ScrollTo(MaxScroll); break;
                default: return;
            }
            e.Handled = true;
        }

        private Rectangle Track() { int sw = ScrollBarWidth; return new Rectangle(ClientSize.Width - sw, 0, sw, ClientSize.Height); }

        private Rectangle Thumb()
        {
            int vh = ViewH;
            if (contentH <= vh) return Rectangle.Empty;
            Rectangle t = Track();
            int th = Math.Max(Math.Max(16, (int)Math.Round(28 * k)), (int)((long)vh * vh / contentH));
            th = Math.Min(th, vh);
            int max = MaxScroll, ty = max > 0 ? (int)((long)scrollY * (vh - th) / max) : 0;
            int tw = Math.Max(3, (int)Math.Round(6 * k));
            return new Rectangle(t.X + (t.Width - tw) / 2, ty, tw, th);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Focused) Focus();
            if (e.Button != MouseButtons.Left || contentH <= ViewH) return;
            Rectangle tr = Track();
            if (e.X < tr.X) return;
            Rectangle th = Thumb();
            if (e.Y >= th.Top && e.Y < th.Bottom) { dragging = true; dragOffset = e.Y - th.Top; return; }
            int page = Math.Max(fonts != null ? fonts.Pitch : 20, ViewH - (fonts != null ? fonts.Pitch * 2 : 40));
            ScrollTo(scrollY + (e.Y < th.Top ? -page : page));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Rectangle th = Thumb();
                int range = ViewH - th.Height;
                if (range > 0) ScrollTo((int)((long)(e.Y - dragOffset) * MaxScroll / range));
                return;
            }
            bool over = e.X >= Track().X && contentH > ViewH;
            if (over != thumbHover) { thumbHover = over; Invalidate(Track()); }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (thumbHover && !dragging) { thumbHover = false; Invalidate(Track()); }
        }

        // v4.3.1 (Alt+Tab lag): focus coming back with the window repaints the whole guide only when a focus cue is drawn
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (ShowFocusCues) Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); dragging = false; if (ShowFocusCues) Invalidate(); }

        /// <summary>Plain text + RTF (black text) of the whole guide on the clipboard.</summary>
        public void CopyAll()
        {
            try
            {
                DataObject o = new DataObject();
                o.SetData(DataFormats.UnicodeText, source);
                string[] ls = GuideFormat.Lines(source);
                o.SetData(DataFormats.Rtf, GuideFormat.Rtf(ls, GuideFormat.Kinds(ls), "Microsoft YaHei UI", 11f,
                    Color.Black, Color.FromArgb(0x00, 0x6E, 0x3C), Color.Gray));
                Clipboard.SetDataObject(o, true);
            }
            catch (Exception ex) { Program.Log("UI: guide copy: " + ex.Message); }
        }

        // ---------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            using (SolidBrush b = new SolidBrush(Theme.ColSurface)) g.FillRectangle(b, clip);
            if (fonts == null) return;
            try
            {
                int top = scrollY + Math.Max(0, clip.Top), bottom = scrollY + clip.Bottom;
                int lw = Math.Max(1, (int)Math.Round(k));
                using (Pen rail = new Pen(Theme.Blend(Theme.ColBorder, Theme.ColAccent, 0.22), lw))
                    foreach (Rail r in rails)
                        if (r.Y2 >= top && r.Y1 <= bottom) g.DrawLine(rail, colX + r.X, r.Y1 - scrollY, colX + r.X, r.Y2 - scrollY);
                for (int i = Math.Max(0, ItemAt(top)); i < items.Count && items[i].Y <= bottom; i++)
                    if (items[i].Y + items[i].H >= top) DrawItem(g, items[i]);
                DrawScrollBar(g);
                if (Focused && ShowFocusCues)
                    using (Pen fp = new Pen(Theme.Blend(Theme.ColSurface, Theme.ColAccent2, 0.55)))
                    {
                        fp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                        g.DrawRectangle(fp, 0, 0, Math.Max(1, ClientSize.Width - ScrollBarWidth - 2), Math.Max(1, ClientSize.Height - 1));
                    }
            }
            catch (Exception ex) { Program.Log("UI: guide paint: " + ex.Message); }
        }

        private void DrawItem(Graphics g, Item it)
        {
            int ox = colX, oy = -scrollY;
            int lw = Math.Max(1, (int)Math.Round(k));
            if (it.Box)
            {
                Rectangle r = new Rectangle(ox + it.X, it.Y + oy, it.W, it.H);
                using (SolidBrush b = new SolidBrush(Theme.Blend(Theme.ColSurface, Theme.ColAccent, 0.06))) g.FillRectangle(b, r);
                using (SolidBrush b = new SolidBrush(Theme.ColAccent)) g.FillRectangle(b, r.X, r.Y, Math.Max(2, (int)Math.Round(3 * k)), r.Height);
            }
            if (it.LineTop || it.LineBottom)
                using (Pen p = new Pen(Theme.ColBorder, lw))
                {
                    if (it.LineTop) g.DrawLine(p, ox + it.X, it.Y + oy, ox + it.X + it.W - 1, it.Y + oy);
                    if (it.LineBottom) g.DrawLine(p, ox + it.X, it.Y + it.H - lw + oy, ox + it.X + it.W - 1, it.Y + it.H - lw + oy);
                }
            if (it.Rule) Theme.DrawFadeLine(g, ox + it.X, it.RuleY + oy, it.W, lw, Theme.Blend(Theme.ColSurface, Theme.ColAccent2, 0.55));
            if (it.DotS > 0)
            {
                Color dc = Theme.Blend(Theme.ColMuted, Theme.ColAccent2, 0.45);
                if (it.DotHollow)
                    using (Pen p = new Pen(dc, lw)) g.DrawRectangle(p, ox + it.DotX, it.DotY + oy, it.DotS - lw, it.DotS - lw);
                else
                    using (SolidBrush b = new SolidBrush(dc)) g.FillRectangle(b, ox + it.DotX, it.DotY + oy, it.DotS, it.DotS);
            }
            if (it.Num.Length > 0 && it.NumFont != null)
            {
                int w = fonts.Width(it.NumFont, it.Num);
                TextRenderer.DrawText(g, it.Num, it.NumFont, new Point(ox + it.NumRight - w, it.NumBase - fonts.Ascent(it.NumFont) + oy), it.NumColor, Flags);
            }
            if (it.Mark.Length > 0 && it.MarkFont != null)
                TextRenderer.DrawText(g, it.Mark, it.MarkFont, new Point(ox + it.MarkX, it.MarkBase - fonts.Ascent(it.MarkFont) + oy), it.MarkColor, Flags);
            foreach (GuideArea a in it.Areas) DrawArea(g, a, ox, oy);
        }

        private void DrawArea(Graphics g, GuideArea a, int ox, int oy)
        {
            int baseOff = Base(a) - a.Y, h = ClientSize.Height;
            for (int l = 0; l < a.Lines.Count; l++)
            {
                int baseY = a.Y + l * a.Pitch + baseOff + oy;
                if (baseY + a.Pitch < 0 || baseY - a.Pitch > h) continue;
                foreach (GuideDoc.Piece p in a.Lines[l])
                {
                    Font f = p.Kind == GuideDoc.RText ? a.Text : a.Mono;
                    Color c = p.Kind == GuideDoc.RKey ? a.Key : (p.Kind == GuideDoc.RNum ? a.Num : a.Fore);
                    TextRenderer.DrawText(g, a.S.Substring(p.Start, p.Length), f, new Point(ox + a.X + p.X, baseY - fonts.Ascent(f)), c, Flags);
                }
            }
        }

        private void DrawScrollBar(Graphics g)
        {
            if (contentH <= ViewH) return;
            Rectangle t = Track();
            using (SolidBrush b = new SolidBrush(Theme.Blend(Theme.ColSurface, Theme.ColBackground, 0.6))) g.FillRectangle(b, t);
            Rectangle th = Thumb();
            Color c = dragging ? Theme.Blend(Theme.ColBorder, Theme.ColAccent2, 0.6) : (thumbHover ? Theme.Blend(Theme.ColBorder, Theme.ColMuted, 0.6) : Theme.Blend(Theme.ColBorder, Theme.ColMuted, 0.25));
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, th);
        }

        /// <summary>Render-harness / test hook: layout problems (text past the column or the scroll bar, blocks overlapping,
        /// lines of one block overlapping); empty = none.</summary>
        internal string CheckLayout()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int right = colX + colW, sbX = ClientSize.Width - ScrollBarWidth;
            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i];
                if (i > 0 && it.Y < items[i - 1].Y + items[i - 1].H) sb.Append("overlap@" + i + " ");
                foreach (GuideArea a in it.Areas)
                    foreach (List<GuideDoc.Piece> line in a.Lines)
                        foreach (GuideDoc.Piece p in line)
                        {
                            int x2 = colX + a.X + p.X + p.W;
                            if (x2 > right + 1 && p.W <= colW) sb.Append("past-col@" + i + " ");
                            if (x2 > sbX) sb.Append("under-scrollbar@" + i + " ");
                            if (a.X + p.X < 0) sb.Append("left@" + i + " ");
                        }
                if (sb.Length > 400) break;
            }
            return sb.ToString();
        }

        /// <summary>Render-harness hook: content height, scroll position, laid-out column.</summary>
        internal string Describe() { return "content=" + contentH + " scroll=" + scrollY + " col=" + colX + "+" + colW + " items=" + items.Count + " current=" + current; }
    }

    /// <summary>
    /// The 目录 (v4.1 GUIDE41): 「目录」 then every 【…】 section and its 「01 …」 pages, the current one marked like the
    /// window's navigation (accent bar + tint). Click / Enter / ↑ ↓ jump (Jump event). Scrolls when it is taller than the
    /// page; the wheel goes to the document when it does not.
    /// </summary>
    internal sealed class GuideToc : Control, IPausable
    {
        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private readonly List<GuideDoc.Entry> entries;
        private readonly List<GuideDoc.Run>[] runs;
        private readonly float k;
        private readonly List<Rectangle> rects = new List<Rectangle>();
        private readonly List<GuideArea> areas = new List<GuideArea>();
        private GuideFonts fonts;
        private int current = -1, hover = -1, scrollY, contentH, headerH, laidW = -1;
        private bool paused, dirty;

        public event Action<int> Jump;
        public GuideView WheelTarget;

        public GuideToc(List<GuideDoc.Entry> entries, float scale)
        {
            this.entries = entries ?? new List<GuideDoc.Entry>();
            k = scale > 0 ? scale : 1f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            Tag = "theme:keep-guide-toc";
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "目录";
            Cursor = Cursors.Hand;
            runs = new List<GuideDoc.Run>[this.entries.Count];
            for (int i = 0; i < this.entries.Count; i++) runs[i] = GuideDoc.Runs(this.entries[i].Label);
        }

        public int Current
        {
            get { return current; }
            set
            {
                if (value == current) return;
                current = value;
                if (current < 0) { scrollY = 0; ClampScroll(); } // the document is at its top: so is the 目录
                else EnsureVisible(current);
                Invalidate();
            }
        }

        public void SetFonts(GuideFonts f)
        {
            fonts = f;
            dirty = true;
            if (!paused) { Relayout(ClientSize.Width); Invalidate(); }
        }

        public void Pause() { paused = true; }

        public void Resume()
        {
            if (!paused) return;
            paused = false;
            if (dirty || laidW != ClientSize.Width) Relayout(ClientSize.Width);
            Invalidate();
        }

        /// <summary>Content height for a width (lays out at that width).</summary>
        public int HeightFor(int width)
        {
            if (fonts == null) return 0;
            if (width != laidW || dirty) Relayout(width);
            return contentH;
        }

        private int Sb { get { return Math.Max(6, (int)Math.Round(8 * k)); } }

        /// <summary>Left inset of the 目录 text: 14 px x DPI, the same as the content inside every other page's cards.</summary>
        private int Inset { get { return (int)Math.Round(14 * k); } }

        private void Relayout(int width)
        {
            dirty = false;
            laidW = width;
            rects.Clear();
            areas.Clear();
            if (fonts == null || width <= 0) { contentH = 0; return; }
            GuideFonts f = fonts;
            int chW = f.ChW, pitch = f.TocPitch;
            int padV = (int)Math.Round(pitch * 0.2);
            int numW = f.Width(f.Mono, "00") + (int)Math.Round(chW * 0.45);
            int right = (int)Math.Round(chW * 0.6) + Sb;
            headerH = pitch + (int)Math.Round(pitch * 0.35);
            int y = headerH;
            Func<string, int, int> m = delegate(string s, int kind) { return f.Width(kind == GuideDoc.RText ? f.Body : f.Mono, s); };
            for (int i = 0; i < entries.Count; i++)
            {
                GuideDoc.Entry e = entries[i];
                if (e.Level == 1 && i > 0) y += (int)Math.Round(pitch * 0.1);
                padV = (int)Math.Round(pitch * (e.Level == 1 ? 0.2 : 0.12)); // pages sit a little closer under their section
                // text 14 px (x DPI) inside the column, like the content of every other page's cards (UIALIGN41 #11)
                int x = Inset + (e.Level == 2 ? (int)Math.Round(chW * 0.5) + (e.Num.Length > 0 ? numW : 0) : 0);
                GuideArea a = new GuideArea();
                a.S = e.Label;
                a.Text = f.Body;
                a.Mono = f.Mono;
                a.Pitch = pitch;
                a.X = x;
                a.Y = y + padV;
                a.Lines = GuideDoc.Wrap(e.Label, runs[i], m, f.Space(f.Body), Math.Max(chW * 2, width - x - right));
                int h = Math.Max(1, a.Lines.Count) * pitch + 2 * padV;
                rects.Add(new Rectangle(0, y, width, h));
                areas.Add(a);
                y += h;
            }
            contentH = y + padV;
            ClampScroll();
        }

        private void ClampScroll()
        {
            int m = Math.Max(0, contentH - ClientSize.Height);
            if (scrollY > m) scrollY = m;
            if (scrollY < 0) scrollY = 0;
        }

        private void EnsureVisible(int i)
        {
            if (i < 0 || i >= rects.Count) return;
            Rectangle r = rects[i];
            int h = ClientSize.Height;
            if (r.Top - scrollY < headerH) scrollY = Math.Max(0, r.Top - headerH);
            else if (r.Bottom - scrollY > h) scrollY = r.Bottom - h;
            ClampScroll();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (paused) { dirty = true; return; }
            if (laidW != ClientSize.Width) Relayout(ClientSize.Width);
            ClampScroll();
            Invalidate();
        }

        private int HitTest(Point p)
        {
            int y = p.Y + scrollY;
            for (int i = 0; i < rects.Count; i++) if (y >= rects[i].Top && y < rects[i].Bottom) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover >= 0) { hover = -1; Invalidate(); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitTest(e.Location);
            if (h < 0) return;
            if (!Focused) Focus();
            Go(h);
        }

        private void Go(int i)
        {
            if (i < 0 || i >= entries.Count) return;
            Action<int> j = Jump;
            if (j != null) j(i);
            Current = i;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HandledMouseEventArgs he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            if (contentH > ClientSize.Height && fonts != null)
            {
                int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
                scrollY -= (int)((long)e.Delta * lines * fonts.TocPitch / 120);
                ClampScroll();
                Invalidate();
            }
            else if (WheelTarget != null) WheelTarget.Wheel(e.Delta);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys c = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) == 0 && (c == Keys.Up || c == Keys.Down || c == Keys.Home || c == Keys.End)) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control || e.Alt || entries.Count == 0) return;
            int c = current < 0 ? 0 : current;
            switch (e.KeyCode)
            {
                case Keys.Up: Go(Math.Max(0, c - 1)); break;
                case Keys.Down: Go(current < 0 ? 0 : Math.Min(entries.Count - 1, c + 1)); break;
                case Keys.Home: Go(0); break;
                case Keys.End: Go(entries.Count - 1); break;
                case Keys.Enter:
                case Keys.Space: Go(c); break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (ShowFocusCues) Invalidate(); } // v4.3.1
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (ShowFocusCues) Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            using (SolidBrush b = new SolidBrush(Theme.ColSurface)) g.FillRectangle(b, e.ClipRectangle);
            if (fonts == null) return;
            try
            {
                GuideFonts f = fonts;
                int lw = Math.Max(1, (int)Math.Round(k));
                int bar = Math.Max(2, (int)Math.Round(3 * k));
                int numW = f.Width(f.Mono, "00");
                for (int i = 0; i < rects.Count; i++)
                {
                    Rectangle r = rects[i];
                    r.Y -= scrollY;
                    if (r.Bottom < headerH || r.Top > h) continue;
                    GuideDoc.Entry en = entries[i];
                    bool cur = i == current, hov = i == hover;
                    int rw = Math.Max(1, w - lw - (contentH > h ? Sb : 0));
                    if (cur || hov)
                        using (SolidBrush b = new SolidBrush(cur ? Theme.Blend(Theme.ColSurface, Theme.ColAccent, 0.09) : Theme.ColHover))
                            g.FillRectangle(b, 0, r.Y, rw, r.Height);
                    if (cur) using (SolidBrush b = new SolidBrush(Theme.ColAccent)) g.FillRectangle(b, 0, r.Y, bar, r.Height);
                    GuideArea a = areas[i];
                    Color tc = cur || hov ? Theme.ColText : (en.Level == 1 ? Theme.Blend(Theme.ColMuted, Theme.ColText, 0.45) : Theme.ColMuted);
                    int baseOff = (a.Pitch - (f.Ascent(f.Body) + f.Descent(f.Body))) / 2 + f.Ascent(f.Body);
                    for (int l = 0; l < a.Lines.Count; l++)
                    {
                        int baseY = a.Y - scrollY + l * a.Pitch + baseOff;
                        foreach (GuideDoc.Piece p in a.Lines[l])
                        {
                            Font pf = p.Kind == GuideDoc.RText ? f.Body : f.Mono;
                            TextRenderer.DrawText(g, a.S.Substring(p.Start, p.Length), pf, new Point(a.X + p.X, baseY - f.Ascent(pf)), tc, Flags);
                        }
                    }
                    if (en.Level == 2 && en.Num.Length > 0)
                    {
                        int nb = a.Y - scrollY + baseOff;
                        int nx = a.X - (int)Math.Round(f.ChW * 0.45) - numW;
                        TextRenderer.DrawText(g, en.Num, f.Mono, new Point(nx, nb - f.Ascent(f.Mono)), cur ? Theme.ColAccent : Theme.ColMuted, Flags);
                    }
                    if (cur && Focused && ShowFocusCues)
                        using (Pen fp = new Pen(Theme.Blend(Theme.ColSurface, Theme.ColAccent2, 0.7)))
                        {
                            fp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                            g.DrawRectangle(fp, 1, r.Y + 1, rw - 3, r.Height - 3);
                        }
                }
                // header 「目录」 over the list (the list scrolls under it)
                using (SolidBrush b = new SolidBrush(Theme.ColSurface)) g.FillRectangle(b, 0, 0, w, headerH);
                int hb = (f.TocPitch - (f.Ascent(f.Body) + f.Descent(f.Body))) / 2 + f.Ascent(f.Body);
                TextRenderer.DrawText(g, "目录", f.Body, new Point(Inset, hb - f.Ascent(f.Body)), Theme.ColMuted, Flags);
                Theme.DrawFadeLine(g, 0, headerH - (int)Math.Round(f.TocPitch * 0.18), Math.Max(1, w - lw), lw, Theme.Blend(Theme.ColSurface, Theme.ColAccent2, 0.4));
                if (contentH > h)
                {
                    int sb = Sb, tw = Math.Max(3, (int)Math.Round(5 * k));
                    int th = Math.Max(Math.Max(14, (int)Math.Round(24 * k)), (int)((long)h * h / contentH));
                    int max = contentH - h, ty = max > 0 ? (int)((long)scrollY * (h - th) / max) : 0;
                    using (SolidBrush b = new SolidBrush(Theme.Blend(Theme.ColBorder, Theme.ColMuted, 0.25)))
                        g.FillRectangle(b, w - lw - sb + (sb - tw) / 2, ty, tw, Math.Min(th, h));
                }
                using (Pen p = new Pen(Theme.ColBorder, lw)) g.DrawLine(p, w - lw, 0, w - lw, h); // hairline between 目录 and text
            }
            catch (Exception ex) { Program.Log("UI: guide toc paint: " + ex.Message); }
        }
    }
#endif
}
