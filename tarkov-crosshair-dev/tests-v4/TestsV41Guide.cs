// GUIDE41 (v4.1): the 指南 page as a document (UI41-review #4) -- src/SettingsForm.PageGuide.cs GuideDoc (pure):
// README -> blocks + 目录 (nothing lost, txt line wraps joined), key names / numbers for the mono font, Chinese line
// breaking, and the 目录 + text-column plan for every page width (no overlap, capped and centred).
using System;
using System.Collections.Generic;
using System.Text;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V41GuideInit() { V41GuideTests(); }

        /// <summary>Letters, digits and Chinese characters only: what must survive the parse in order.</summary>
        static string GuideKeep(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "")
                if (GuideDoc.Alnum(c) || GuideDoc.Ideograph(c)) sb.Append(c);
            return sb.ToString();
        }

        static string GuideNoSpace(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "") if (c != ' ' && c != '\t' && c != '\u3000') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Fake font: Chinese / full-width 10, other 6, mono runs 7 per char.</summary>
        static int GuideFakeWidth(string s, int kind)
        {
            int w = 0;
            foreach (char c in s ?? "") w += kind != GuideDoc.RText ? 7 : ((c >= 0x2E80 || (c >= 0xFF00 && c <= 0xFFEF)) ? 10 : 6);
            return w;
        }

        static string GuideMono(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (GuideDoc.Run r in GuideDoc.Runs(s))
                if (r.Kind != GuideDoc.RText) sb.Append(r.Kind == GuideDoc.RKey ? "K[" : "N[").Append(s.Substring(r.Start, r.Length)).Append(']');
            return sb.ToString();
        }

        static int GuideFind(GuideDoc.Doc d, int kind, string textStart, int from)
        {
            for (int i = Math.Max(0, from); i < d.Blocks.Count; i++)
                if (d.Blocks[i].Kind == kind && (d.Blocks[i].Text.StartsWith(textStart) || d.Blocks[i].Label.StartsWith(textStart))) return i;
            return -1;
        }

        static void V41GuideTests()
        {
            string src = GuideText.Text;
            GuideDoc.Doc doc = GuideDoc.Parse(src);
            string[] lines = GuideFormat.Lines(src);
            int[] kinds = GuideFormat.Kinds(lines);

            // GD1 nothing lost, nothing reordered: every letter / digit / Chinese character of the README (rules left out) is in
            // the blocks, in the same order (答 / 问 marks, step numbers, row cells included); no rule or leader run is left.
            {
                StringBuilder want = new StringBuilder(), got = new StringBuilder();
                for (int i = 0; i < lines.Length; i++) if (kinds[i] != GuideFormat.Rule) want.Append(GuideKeep(lines[i]));
                bool ok = true; string d = "";
                foreach (GuideDoc.Block b in doc.Blocks)
                {
                    got.Append(GuideKeep(b.Mark)).Append(GuideKeep(b.Num)).Append(GuideKeep(b.Label)).Append(GuideKeep(b.Text));
                    string all = b.Label + " " + b.Text;
                    if (all.Contains("=====") || all.Contains("-----") || all.Contains("……") || all.Contains("...") || all.Contains("\n"))
                    { ok = false; if (d.Length < 200) d += "junk@" + b.Line + " "; }
                }
                string w = want.ToString(), g = got.ToString();
                if (w != g)
                {
                    int at = 0; while (at < w.Length && at < g.Length && w[at] == g[at]) at++;
                    ok = false;
                    d += "differs at " + at + ": want …" + w.Substring(Math.Max(0, at - 10), Math.Min(30, w.Length - Math.Max(0, at - 10))) +
                         " got …" + g.Substring(Math.Max(0, at - 10), Math.Min(30, g.Length - Math.Max(0, at - 10)));
                }
                Check("GD1 指南 parse keeps every character of the README in order (" + w.Length + " chars, " + doc.Blocks.Count + " blocks), no rules / leaders left", ok, d);
            }

            // GD2 目录 = the README headings: every 【…】 section and every 「01 …」 page, in order, brackets dropped; each entry points
            // at its heading block; every block's Section is the last entry at or above it.
            {
                bool ok = true; string d = "";
                List<string> want = new List<string>();
                for (int i = 0; i < lines.Length; i++)
                {
                    if (kinds[i] != GuideFormat.Heading) continue;
                    string t = lines[i].Trim();
                    if (t.StartsWith("【") && t.EndsWith("】")) want.Add("1:" + t.Substring(1, t.Length - 2).Trim());
                    else if (i > 0 && i + 1 < lines.Length && GuideFormat.IsRule(lines[i - 1]) && lines[i - 1].Contains("-")) want.Add("2:" + t);
                }
                List<string> got = new List<string>();
                for (int e = 0; e < doc.Toc.Count; e++)
                {
                    GuideDoc.Entry en = doc.Toc[e];
                    got.Add(en.Level + ":" + (en.Num.Length > 0 ? en.Num + " " : "") + en.Title);
                    GuideDoc.Block hb = doc.Blocks[en.Block];
                    if (hb.Toc != e || (en.Level == 1 ? hb.Kind != GuideDoc.H1 : hb.Kind != GuideDoc.H2) || en.Title.Contains("【")) { ok = false; d += "entry" + e + " "; }
                }
                if (string.Join("|", want) != string.Join("|", got)) { ok = false; d += "want " + string.Join("|", want) + " got " + string.Join("|", got); }
                int sec = -1;
                for (int i = 0; i < doc.Blocks.Count; i++)
                {
                    if (doc.Blocks[i].Toc >= 0) sec = doc.Blocks[i].Toc;
                    if (doc.Blocks[i].Section != sec) { ok = false; d += "section@" + i + " "; break; }
                }
                int l1 = 0, l2 = 0;
                foreach (GuideDoc.Entry en in doc.Toc)
                {
                    if (en.Level == 1) l1++; else l2++;
                    if (en.Label.Length == 0 || !en.Title.StartsWith(en.Label)) { ok = false; d += "label[" + en.Title + "->" + en.Label + "] "; }
                }
                // short 目录 labels: a trailing （…） and the part after '：' are left out; other titles unchanged
                string[][] lab = { new[] { "升级（从旧版本换到 v4.1）", "升级" }, new[] { "第一次使用：跟着教程走", "第一次使用" },
                    new[] { "移动（W 前 / S 后 / A 左 / D 右）", "移动" }, new[] { "每把枪要不要单独调？", "每把枪要不要单独调？" },
                    new[] { "反馈 / 功能建议 / Bug", "反馈 / 功能建议 / Bug" }, new[] { "（全是括号）", "（全是括号）" }, new[] { "首页", "首页" } };
                foreach (string[] c in lab) if (GuideDoc.TocLabel(c[0]) != c[1]) { ok = false; d += "toclabel[" + c[0] + "]=" + GuideDoc.TocLabel(c[0]) + " "; }
                ok &= l1 >= 10 && l2 >= 10 && doc.Blocks[0].Kind == GuideDoc.Title && doc.Blocks[1].Kind == GuideDoc.Callout;
                Check("GD2 目录 = README headings: " + l1 + " sections + " + l2 + " pages in order, entries -> heading blocks, sections", ok, d);
            }

            // GD3 structure of known README parts: steps, the tutorial's numbered list, 「名：说明」 rows, the calibration walkthrough,
            // 问 / 答 with steps, lead-in lines, single-「…」 table lines, joined paragraphs.
            {
                bool ok = true; string d = "";
                // 安装 / 运行: steps 1..4 at the top level, one list
                int s1 = GuideFind(doc, GuideDoc.Step, "把压缩包整个解压", 0);
                if (s1 < 0 || doc.Blocks[s1].Num != "1" || doc.Blocks[s1].Depth != 0 || !doc.Blocks[s1].Text.Contains("不要在压缩包里直接双击运行")) { ok = false; d += "install1 "; }
                else for (int n = 2; n <= 4; n++)
                    {
                        GuideDoc.Block b = doc.Blocks[s1 + n - 1];
                        if (b.Kind != GuideDoc.Step || b.Num != n.ToString() || b.List != doc.Blocks[s1].List) { ok = false; d += "install" + n + " "; }
                    }
                // 第一次使用: step 3 -> 10 numbered rows (one table, depth 1), row 2 joined with its next txt line, then step 4 continues the list
                int s3 = GuideFind(doc, GuideDoc.Step, "教程一共", 0);
                if (s3 < 0) { if (GuideText.Text.IndexOf("向导做完会问要不要看教程", StringComparison.Ordinal) < 0) { ok = false; d += "tut3 "; } } // v5.1 (W25): wizard + 16-page tutorial list
                else
                {
                    // the rows inside step 3 (numbered 1..10 in order; an extra 「名：说明」 row such as 开发者的话 may sit between)
                    int j = s3 + 1, want = 1, table = doc.Blocks[s3 + 1].Table;
                    for (; j < doc.Blocks.Count && doc.Blocks[j].Depth == 1 && doc.Blocks[j].Kind == GuideDoc.Row; j++)
                    {
                        GuideDoc.Block r = doc.Blocks[j];
                        if (r.Table != table) { ok = false; d += "tuttable@" + j + " "; }
                        if (r.Num.Length == 0) continue;
                        if (r.Num != want.ToString()) { ok = false; d += "tutrow" + want + "=" + r.Num + " "; }
                        if (want == 2 && (!r.Text.Contains("近战") || r.Label != "落点在右下")) { ok = false; d += "tutrow2text "; }
                        want++;
                    }
                    if (want != 11) { ok = false; d += "tutrows=" + (want - 1) + " "; }
                    GuideDoc.Block s4 = j < doc.Blocks.Count ? doc.Blocks[j] : null;
                    if (s4 == null || s4.Kind != GuideDoc.Step || s4.Num != "4" || s4.Depth != 0 || s4.List != doc.Blocks[s3].List) { ok = false; d += "tut4 "; }
                }
                // 外观: 「推荐 · 中心留空：…」 row joined with its deeper next line, then 经典 / 自定义 in the same table
                int st = GuideFind(doc, GuideDoc.Row, "推荐 · 中心留空", 0);
                if (st < 0 || !doc.Blocks[st].Text.EndsWith("空心三角") || doc.Blocks[st].Depth != 1
                    || doc.Blocks[st + 1].Label != "经典" || doc.Blocks[st + 2].Label != "自定义" || doc.Blocks[st + 2].Table != doc.Blocks[st].Table) { ok = false; d += "style "; }
                // v4.3 (W10): 鼠标滞后 and its 「怎么校准…：」 steps are gone from the guide
                if (GuideFind(doc, GuideDoc.Bullet, "怎么校准", 0) >= 0) { ok = false; d += "calib still there "; }
                // 常见问题: first Q -> 答 on step 1, steps 2..4 inside the answer, step 4 joined with its next txt line
                int q = GuideFind(doc, GuideDoc.Question, "准星不显示", 0);
                if (q < 0 || doc.Blocks[q].Label != "问") { ok = false; d += "q "; }
                else
                {
                    GuideDoc.Block a1 = doc.Blocks[q + 1];
                    if (a1.Kind != GuideDoc.Step || a1.Mark != "答" || a1.Num != "1" || a1.Depth != 1) { ok = false; d += "a1 "; }
                    for (int n = 2; n <= 4; n++)
                        if (doc.Blocks[q + n].Kind != GuideDoc.Step || doc.Blocks[q + n].Num != n.ToString() || doc.Blocks[q + n].List != a1.List || doc.Blocks[q + n].Mark != "") { ok = false; d += "a" + n + " "; }
                    if (!doc.Blocks[q + 4].Text.Contains("还是不出来")) { ok = false; d += "a4join "; }
                }
                int questions = 0, qLines = 0;
                foreach (GuideDoc.Block b in doc.Blocks) if (b.Kind == GuideDoc.Question) questions++;
                foreach (string l in lines) if (l.Trim().StartsWith("问：")) qLines++;
                if (questions != qLines || questions < 5) { ok = false; d += "questions " + questions + "/" + qLines + " "; }
                // a one-line answer is a paragraph with the 答 mark
                int a2 = GuideFind(doc, GuideDoc.Para, "不用。", 0);
                if (a2 < 0 || doc.Blocks[a2].Mark != "答" || doc.Blocks[a2].Depth != 1) { ok = false; d += "answerpara "; }
                // lead-in lines
                foreach (string h in new string[] { "开始前：", "偏移量的单位：百分比偏移", "组合键：", "读旧设置时会自动处理：" })
                    if (GuideFind(doc, GuideDoc.H3, h, 0) < 0) { ok = false; d += "h3[" + h + "] "; }
                // table lines with one 「…」
                if (GuideFind(doc, GuideDoc.Row, "「存为我的默认」", 0) < 0 || GuideFind(doc, GuideDoc.Row, "分辨率 · 常规", 0) < 0) { ok = false; d += "single… "; }
                // a 3-line txt paragraph is one paragraph again
                int p3 = GuideFind(doc, GuideDoc.Para, "塔科夫腰射用的屏幕准星", 0);
                if (p3 < 0 || !doc.Blocks[p3].Text.Contains("屏幕上画一个准星") || !doc.Blocks[p3].Text.Contains("告诉你不开镜时")) { ok = false; d += "para3 "; }
                // the 文件夹 table: 5 rows, one table, top level
                int f1 = -1;
                for (int i = 0; i < doc.Blocks.Count && f1 < 0; i++)
                    if (doc.Blocks[i].Kind == GuideDoc.Row && doc.Blocks[i].Label.EndsWith("TarkovHipAimer.exe")) f1 = i;
                // v5.0.2 (W17): the table grew (其他 Other, 数据 Data): a crash.log / 数据 row within the next 8 rows of the same table
                bool logRow = false;
                for (int j = f1 + 1; f1 >= 0 && j < doc.Blocks.Count && j <= f1 + 8; j++)
                    if (doc.Blocks[j].Kind == GuideDoc.Row && doc.Blocks[j].Table == doc.Blocks[f1].Table
                        && (doc.Blocks[j].Label.Contains("crash.log") || doc.Blocks[j].Label.Contains("数据"))) logRow = true;
                if (f1 < 0 || doc.Blocks[f1].Depth != 0 || !logRow) { ok = false; d += "files "; }
                int steps = 0, bullets = 0, rows = 0, h3 = 0;
                foreach (GuideDoc.Block b in doc.Blocks)
                {
                    if (b.Kind == GuideDoc.Step) steps++;
                    if (b.Kind == GuideDoc.Bullet) bullets++;
                    if (b.Kind == GuideDoc.Row) rows++;
                    if (b.Kind == GuideDoc.H3) h3++;
                }
                ok &= steps >= 20 && bullets >= 40 && rows >= 30 && h3 >= 5;
                Check("GD3 指南 structure: " + steps + " steps, " + bullets + " bullets, " + rows + " table rows, " + h3 + " lead-ins, " + questions + " 问/答", ok, d);
            }

            // GD4 mono runs: key names and numbers, not letters / digits inside words.
            {
                bool ok = true; string d = "";
                string[][] cases = {
                    new[] { "按 F12 开关", "K[F12]" }, new[] { "按 W A S D、侧身", "K[W A S D]" }, new[] { "固定为 W / S / A / D。", "K[W / S / A / D]" },
                    new[] { "按住 Q+A、E+D 开枪", "K[Q+A]K[E+D]" }, new[] { "右侧身设成 Ctrl+D，", "K[Ctrl+D]" }, new[] { "按住 W 再按 Shift 冲刺", "K[W]K[Shift]" },
                    new[] { "「Tab / Esc 取消开镜」", "K[Tab / Esc]" }, new[] { "斜着走（WA / WD / SA / SD）", "K[WA / WD / SA / SD]" }, new[] { "Ctrl+Tab 也能", "K[Ctrl+Tab]" },
                    new[] { "「D 右」的 X", "K[D]" }, new[] { "A / D 的 Y 一般不改", "K[A / D]" },
                    new[] { "2160×1440、FOV 75", "N[2160×1440]N[75]" }, new[] { "后坐力 50–100 的枪", "N[50–100]" }, new[] { "（100%–200%）", "N[100%–200%]" },
                    new[] { "「← 4.17%」", "N[4.17%]" }, new[] { "每次改 0.10%，", "N[0.10%]" }, new[] { "都用 380 ms「慢起」", "N[380 ms]" }, new[] { "换到 v4.1", "N[v4.1]" },
                    new[] { "1080p、1440p、4K、带鱼屏", "N[1080p]N[1440p]N[4K]" }, new[] { "Windows 10/11（或", "N[10/11]" }, new[] { "30% / 40% / 30%", "N[30%]N[40%]N[30%]" },
                    new[] { "推荐 WMX200，", "" }, new[] { "比如 D:\\TarkovHipAimer。", "" }, new[] { "双击 TarkovHipAimer.exe", "" }, new[] { "（侧键1）", "" },
                    new[] { "X 偏移、Y 偏移", "" }, new[] { "平时开 IR，", "" }, new[] { "上传 PNG", "" }, new[] { "T 形（无上）", "" }, new[] { "鼠标 DPI", "" },
                    new[] { "settings.json", "" }, new[] { "GitHub：https://github.com/a-b/Tarkov-Hip-Aimer", "" }, new[] { "Tarkov Hip Aimer — by Leo", "" },
                };
                foreach (string[] c in cases)
                {
                    string g = GuideMono(c[0]);
                    if (g != c[1]) { ok = false; d += "[" + c[0] + "] -> " + g + " (want " + c[1] + ") "; }
                }
                // whole guide: runs tile every text exactly; key runs are ASCII key grammar; number runs have a digit
                int keys = 0, nums = 0;
                foreach (GuideDoc.Block b in doc.Blocks)
                    foreach (string s in new string[] { b.Text, b.Label })
                    {
                        int pos = 0;
                        foreach (GuideDoc.Run r in GuideDoc.Runs(s))
                        {
                            if (r.Start != pos || r.Length <= 0) { ok = false; d += "tile@" + b.Line + " "; break; }
                            string t = s.Substring(r.Start, r.Length);
                            if (r.Kind == GuideDoc.RKey)
                            {
                                keys++;
                                foreach (char ch in t) if (!(GuideDoc.Alnum(ch) || ch == '+' || ch == '/' || ch == ' ')) { ok = false; d += "key[" + t + "] "; break; }
                            }
                            if (r.Kind == GuideDoc.RNum)
                            {
                                nums++;
                                bool dig = false; foreach (char ch in t) if (GuideDoc.Digit(ch)) dig = true;
                                if (!dig) { ok = false; d += "num[" + t + "] "; }
                            }
                            pos += r.Length;
                        }
                        if (pos != s.Length) { ok = false; d += "cover@" + b.Line + " "; }
                    }
                ok &= keys >= 30 && nums >= 30;
                Check("GD4 mono runs: " + cases.Length + " cases (keys / numbers / words left alone), guide " + keys + " key + " + nums + " number runs tile the text", ok, d);
            }

            // GD5 line breaking on every guide text at 7 widths (fake font): nothing lost, every line fits, key names / numbers
            // never split, no line starts with 。，、）」… or ends with （「 (wide enough widths), deterministic.
            {
                bool ok = true; string d = "";
                int[] widths = { 40, 61, 97, 150, 233, 400, 1000 };
                int checkedLines = 0;
                foreach (int maxW in widths)
                    foreach (GuideDoc.Block b in doc.Blocks)
                        foreach (string s in new string[] { b.Text, b.Label })
                        {
                            if (s.Length == 0) continue;
                            List<GuideDoc.Run> runs = GuideDoc.Runs(s);
                            List<List<GuideDoc.Piece>> ls = GuideDoc.Wrap(s, runs, GuideFakeWidth, 4, maxW);
                            StringBuilder joined = new StringBuilder();
                            int[] lineOf = new int[s.Length];
                            for (int q = 0; q < lineOf.Length; q++) lineOf[q] = -1;
                            for (int l = 0; l < ls.Count; l++)
                            {
                                checkedLines++;
                                List<GuideDoc.Piece> line = ls[l];
                                if (line.Count == 0) { ok = false; d += "emptyline "; continue; }
                                GuideDoc.Piece last = line[line.Count - 1];
                                if (last.X + last.W > maxW) { ok = false; if (d.Length < 300) d += "wide@" + maxW + "/" + b.Line + " "; }
                                int px = -1;
                                foreach (GuideDoc.Piece p in line)
                                {
                                    if (p.X < px) { ok = false; d += "order "; }
                                    px = p.X + p.W;
                                    joined.Append(s.Substring(p.Start, p.Length));
                                    for (int q = p.Start; q < p.Start + p.Length; q++) lineOf[q] = l;
                                }
                                bool url = s.Contains("://") || s.Contains("@");
                                if (maxW >= 150 && !url)
                                {
                                    char first = s[line[0].Start], end = s[last.Start + last.Length - 1];
                                    if (l > 0 && GuideDoc.NoStart(first)) { ok = false; if (d.Length < 300) d += "starts[" + first + "]@" + b.Line + " "; }
                                    if (l < ls.Count - 1 && GuideDoc.NoEnd(end)) { ok = false; if (d.Length < 300) d += "ends[" + end + "]@" + b.Line + " "; }
                                }
                            }
                            if (GuideNoSpace(joined.ToString()) != GuideNoSpace(s)) { ok = false; if (d.Length < 300) d += "lost@" + maxW + "/" + b.Line + " "; }
                            foreach (GuideDoc.Run r in runs)
                            {
                                if (r.Kind == GuideDoc.RText || GuideFakeWidth(s.Substring(r.Start, r.Length), r.Kind) > maxW) continue;
                                for (int q = r.Start; q < r.Start + r.Length; q++)
                                    if (lineOf[q] != lineOf[r.Start] && s[q] != ' ') { ok = false; if (d.Length < 300) d += "split[" + s.Substring(r.Start, r.Length) + "] "; break; }
                            }
                            if (maxW == 97)
                            {
                                List<List<GuideDoc.Piece>> again = GuideDoc.Wrap(s, runs, GuideFakeWidth, 4, maxW);
                                if (again.Count != ls.Count) { ok = false; d += "nondet "; }
                            }
                        }
                // synthetic: closing marks stay on the line before; a URL breaks inside; spaces collapse; junk input
                string ui = "「打开设置」「启用准星」「退出」。";
                for (int w = 30; w <= 200; w += 7)
                {
                    List<List<GuideDoc.Piece>> ls = GuideDoc.Wrap(ui, null, GuideFakeWidth, 4, w);
                    for (int l = 1; l < ls.Count; l++)
                        if (GuideDoc.NoStart(ui[ls[l][0].Start])) { ok = false; d += "ui@" + w + " "; break; }
                }
                string url2 = "GitHub：https://github.com/liumuyangmuyang-blip/Tarkov-Hip-Aimer";
                List<List<GuideDoc.Piece>> lu = GuideDoc.Wrap(url2, null, GuideFakeWidth, 4, 100);
                StringBuilder ju = new StringBuilder();
                foreach (List<GuideDoc.Piece> line in lu) { foreach (GuideDoc.Piece p in line) ju.Append(url2.Substring(p.Start, p.Length)); if (line[line.Count - 1].X + line[line.Count - 1].W > 100) ok = false; }
                if (ju.ToString() != url2 || lu.Count < 3) { ok = false; d += "url "; }
                // a run of spaces is one gap (two pieces: drawing "ab   cd" in one go would show 3 spaces); one space merges
                List<List<GuideDoc.Piece>> sp = GuideDoc.Wrap("ab   cd", null, GuideFakeWidth, 4, 1000);
                if (sp.Count != 1 || sp[0].Count != 2 || sp[0][1].X != 12 + 4 || sp[0][1].Start != 5) { ok = false; d += "spaces3 "; }
                List<List<GuideDoc.Piece>> sp1 = GuideDoc.Wrap("ab cd", null, GuideFakeWidth, 4, 1000);
                if (sp1.Count != 1 || sp1[0].Count != 1 || sp1[0][0].W != 12 + 4 + 12 || sp1[0][0].Length != 5) { ok = false; d += "spaces1 "; }
                // 「分辨率 · 常规」 / 「管理 ▾」: the dot and the arrow never start or end a line
                foreach (int w in new int[] { 40, 50, 60, 70, 90 }) // >= the widest cluster (「率 · 常」 = 34): below that a split is forced
                    foreach (string t in new string[] { "分辨率 · 常规", "顶部「管理 ▾」→「新建…」", "括号 [ ]、点阵圆环、分段圆弧" })
                    {
                        List<List<GuideDoc.Piece>> ls = GuideDoc.Wrap(t, null, GuideFakeWidth, 4, w);
                        for (int l = 0; l < ls.Count; l++)
                        {
                            char f0 = t[ls[l][0].Start], fe = t[ls[l][ls[l].Count - 1].Start + ls[l][ls[l].Count - 1].Length - 1];
                            if ((l > 0 && (f0 == '▾' || GuideDoc.NoStart(f0))) || (l < ls.Count - 1 && (fe == '·' || GuideDoc.NoEnd(fe)))) { ok = false; d += "mark[" + t + "]@" + w + " "; }
                        }
                    }
                try
                {
                    if (GuideDoc.Wrap("", null, GuideFakeWidth, 4, 100).Count != 0 || GuideDoc.Wrap(null, null, GuideFakeWidth, 4, 100).Count != 0) { ok = false; d += "empty "; }
                    GuideDoc.Wrap("中文 text", null, GuideFakeWidth, -5, 0);
                    GuideDoc.Wrap("中文", null, null, 4, 100);
                }
                catch (Exception ex) { ok = false; d += "threw " + ex.GetType().Name; }
                Check("GD5 指南 line breaking: " + checkedLines + " lines at 7 widths fit, keep every character, keys / numbers whole, Chinese closing marks never start a line", ok, d);
            }

            // GD6 page plan for every width 1..6000 px x 10 text sizes x 5 scroll bar widths: 目录 left of the text, never overlapping;
            // text column <= 40 characters (>= 24 next to the 目录), inside the page, centred; once wide, wider pages stay wide.
            {
                bool ok = true; string d = "";
                int[] chs = { 8, 12, 15, 20, 22, 26, 29, 40, 53, 64 };
                int[] sbs = { 0, 8, 11, 17, 22 };
                int plans = 0, wide = 0;
                foreach (int ch in chs)
                    foreach (int sb in sbs)
                    {
                        bool wasWide = false;
                        for (int w = 1; w <= 6000; w += (w < 1200 ? 1 : 3))
                        {
                            GuideDoc.Plan p = GuideDoc.Layout(w, ch, sb);
                            plans++;
                            string bad = null;
                            int pad = Math.Max(2, ch / 2);
                            if (p.ColW < 1 || p.ColX < 0) bad = "neg";
                            else if (w >= sb + 2 && p.ColX + p.ColW > w - sb) bad = "past-sb";
                            else if (p.ColW > ch * GuideDoc.MaxColumnChars) bad = "too-wide";
                            else if (p.Wide)
                            {
                                wide++;
                                int gap = Math.Max(2, ch * 3 / 2);
                                int left = p.ColX - (p.TocW + gap), right = w - sb - pad - (p.ColX + p.ColW);
                                if (p.TocX != 0 || p.TocX + p.TocW >= p.ViewX || p.ViewX > p.ColX) bad = "overlap";
                                else if (p.ColW < ch * GuideDoc.MinColumnChars) bad = "narrow-col";
                                else if (p.TocW < ch * 19 / 2 || p.TocW > ch * 13) bad = "toc-width";
                                else if (left < 0 || left > ch * GuideDoc.CentreChars) bad = "too-far";
                                else if (left < ch * GuideDoc.CentreChars && Math.Abs(left - right) > 1) bad = "not-centred";
                            }
                            else
                            {
                                int room = w - sb - pad;
                                if (p.TocW != 0 || p.ViewX != 0) bad = "toc-in-narrow";
                                else if (room >= 1 && Math.Abs(p.ColX - (room - p.ColX - p.ColW)) > 1) bad = "not-centred-narrow";
                                else if (room >= 1 && p.ColW < Math.Min(room, ch * GuideDoc.MaxColumnChars)) bad = "narrow-not-full";
                                else if (wasWide) bad = "wide-then-narrow";
                            }
                            if (p.Wide) wasWide = true;
                            if (bad != null) { ok = false; if (d.Length < 300) d += bad + "@w" + w + "/ch" + ch + "/sb" + sb + " "; }
                        }
                    }
                // the sizes this page really gets (page width px, one Chinese character px, scroll bar px; Mono renders)
                GuideDoc.Plan p1080 = GuideDoc.Layout(860, 15, 11), p720x150 = GuideDoc.Layout(1000, 22, 17), p720x200 = GuideDoc.Layout(880, 29, 22);
                ok &= p1080.Wide && p720x150.Wide && !p720x200.Wide;
                Check("GD6 指南 page plan: " + plans + " widths (" + wide + " with the 目录 column): no overlap, text <= 40 chars, inside, centred, monotone", ok,
                    d + " 1080p=" + p1080.Wide + " 720p@150=" + p720x150.Wide + " 720p@200=" + p720x200.Wide);
            }

            // GD7 helpers: steps vs numbers, joining txt lines, whitespace.
            {
                string n, r;
                bool ok = GuideDoc.SplitStep("1. 把压缩包", out n, out r) && n == "1" && r == "把压缩包";
                ok &= GuideDoc.SplitStep("10．文字", out n, out r) && n == "10";
                ok &= !GuideDoc.SplitStep("4.17% 是", out n, out r) && !GuideDoc.SplitStep("10 步", out n, out r) && !GuideDoc.SplitStep("123. x", out n, out r) && !GuideDoc.SplitStep("1.", out n, out r);
                ok &= GuideDoc.Join("屏幕上画一个圈，", "告诉你") == "屏幕上画一个圈，告诉你" && GuideDoc.Join("the", "next") == "the next"
                    && GuideDoc.Join("D:\\TarkovHipAimer。", "不要") == "D:\\TarkovHipAimer。不要" && GuideDoc.Join("", "x") == "x";
                ok &= GuideDoc.Squeeze("  Leo   v4.1 ") == "Leo v4.1" && GuideDoc.Indent("   x") == 3 && GuideDoc.Indent("\u3000x") == 2 && GuideDoc.Columns("答：") == 4;
                GuideDoc.Doc e = GuideDoc.Parse("");
                GuideDoc.Doc j = GuideDoc.Parse("T\n=====\n【A】\n=====\n- x\n  y\n1. a\n   b\n\n  名字 ……… 说明\n");
                ok &= e.Blocks.Count == 0 && e.Toc.Count == 0 && GuideDoc.Parse(null).Blocks.Count == 0;
                ok &= j.Blocks.Count == 5 && j.Blocks[2].Text == "x y" && j.Blocks[3].Text == "a b" && j.Blocks[4].Kind == GuideDoc.Row && j.Toc.Count == 1;
                string det = j.Blocks.Count + " blocks";
                foreach (GuideDoc.Block b in j.Blocks) det += " [" + b.Kind + ":" + b.Label + "|" + b.Text + "]";
                Check("GD7 指南 helpers: 「1.」 steps (not 4.17% / 10 步), txt lines joined, whitespace, empty and tiny guides", ok, ok ? "" : det);
            }
        }
    }
}
