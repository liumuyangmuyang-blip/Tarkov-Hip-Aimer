// v4.3.1 (G2): the 按键 / 分辨率 · 常规 / 后坐力 / 滤镜 pages build every label + control group as FIXED cells.
// Leo's 2160x1440 @200 % screenshot showed the 换手 grid scrambled: a flowed TableLayoutPanel skips hidden children, so a
// hidden cell slid every later cell one place left and labels wrapped away from their controls. Source rules checked here
// (pure: no WinForms on this box). The rendered check (rows, order, overlap, clipping at min/default/max x 100/150/200 %)
// is layout-g2/run.sh (Mono + Xvfb).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V431G2Init() { V431G2Tests(); }

        static readonly string[] G2Files = { "SettingsForm.PageKeys.cs", "SettingsForm.PageGeneral.cs", "SettingsForm.PageRecoil.cs",
            "SettingsForm.PageFilter.cs", "FilterControls.cs" };

        static void V431G2Tests()
        {
            string dir = UiSrcDir();
            foreach (string name in G2Files)
            {
                string p = Path.Combine(dir, name);
                if (!File.Exists(p)) { Check("V431-G2 " + name + " exists", false, p); continue; }
                string src = StripComments(File.ReadAllText(p, Encoding.UTF8));

                // 1. no flow layout anywhere: nothing can wrap a label away from its control
                bool noFlow = src.IndexOf("FlowLayoutPanel", StringComparison.Ordinal) < 0 && src.IndexOf("WrapContents", StringComparison.Ordinal) < 0;
                Check("V431-G2-1 " + name + ": no FlowLayoutPanel / WrapContents", noFlow, "");

                // 2. no font-scale fix-ups (W11: one fixed font; F() == S())
                bool noScale = !Regex.IsMatch(src, @"\bFixUp\s*\(") && !Regex.IsMatch(src, @"(?<![\w.])F\s*\(\s*\d") &&
                    src.IndexOf("uiScale", StringComparison.Ordinal) < 0;
                Check("V431-G2-2 " + name + ": no FixUp / F(px) / uiScale (fonts are fixed)", noScale, "");

                // 3. a multi-column Grid(n>=2) is only ever filled through Row / AdvRow / Cells / Wide (fixed cells)
                List<string> bad = new List<string>();
                foreach (Match m in Regex.Matches(src, @"TableLayoutPanel\s+(\w+)\s*=\s*Grid\(\s*(\d+)\s*\)"))
                {
                    if (int.Parse(m.Groups[2].Value) < 2) continue;
                    string v = m.Groups[1].Value;
                    // the variable's scope: from its declaration to the end of the enclosing method (next "private " at class level)
                    int from = m.Index, to = src.IndexOf("\n        private ", from, StringComparison.Ordinal);
                    if (to < 0) to = src.Length;
                    string scope = src.Substring(from, to - from);
                    foreach (Match a in Regex.Matches(scope, @"\b" + Regex.Escape(v) + @"\.Controls\.Add\(([^;]*)\);"))
                    {
                        // Add(control, col, row) is a fixed cell too
                        if (!Regex.IsMatch(a.Groups[1].Value, @",\s*[^,]+,\s*[^,]+$")) bad.Add(v + ": " + a.Value);
                    }
                }
                Check("V431-G2-3 " + name + ": multi-column grids use fixed cells only (Row / AdvRow / Cells / Wide)", bad.Count == 0,
                    string.Join(" | ", bad.ToArray()));
            }

            // 4. every label + control group on these pages sits in one grid row (spot checks of the groups Leo can see)
            {
                string keys = StripComments(File.ReadAllText(Path.Combine(dir, "SettingsForm.PageKeys.cs"), Encoding.UTF8));
                string gen = StripComments(File.ReadAllText(Path.Combine(dir, "SettingsForm.PageGeneral.cs"), Encoding.UTF8));
                string rec = StripComments(File.ReadAllText(Path.Combine(dir, "SettingsForm.PageRecoil.cs"), Encoding.UTF8));
                string fil = StripComments(File.ReadAllText(Path.Combine(dir, "SettingsForm.PageFilter.cs"), Encoding.UTF8));
                string[][] want =
                {
                    new string[] { "按键 换手 row", keys, @"Row\(g, ""换手"", handKey, Line\(handModeL, handMode, hsOn\)\)" },
                    new string[] { "按键 开镜 row", keys, @"Row\(g, ""开镜"", KeyBtn\(2\), Line\(Lbl\(""开镜方式""\)" },
                    new string[] { "常规 游戏分辨率 row", gen, @"Row\(gr, ""游戏分辨率"", Line\(" },
                    // v5.1 (W34, decisions 12 + 13): the row is still one fixed 1-row grid, now Line(aspectBox, custom W:H box, "?")
                    new string[] { "常规 画面比例 row", gen, @"Row\(gr, ""画面比例"", (aspectBox\)|Line\(aspectBox, )" },
                    new string[] { "常规 FOV row", gen, @"Row\(gr, ""FOV"", Line\(" },
                    new string[] { "后坐力 上抬偏移量 row", rec, @"Row\(g, rise, Offset\(" },
                    new string[] { "后坐力 到顶时间 row", rec, @"Row\(a, ""到顶时间"", Line\(" },
                    new string[] { "滤镜 slider rows", fil, @"else Row\(g, l, sl, pill\);" },
                    new string[] { "滤镜 显示器硬件 rows", fil, @"Row\(g, l, use, sl, pill\);" },
                    new string[] { "滤镜 预设 row", fil, @"Line\(Lbl\(""预设""\), preset\)" },
                    new string[] { "滤镜 分享码 row", fil, @"Line\(Lbl\(""粘贴分享码""\), frame, import\)" },
                    new string[] { "滤镜 调试滤镜 bar", fil, @"Line\(dark, tuneBar, bright, tunePill\)" },
                };
                List<string> miss = new List<string>();
                foreach (string[] w in want) if (!Regex.IsMatch(w[1], w[2])) miss.Add(w[0]);
                Check("V431-G2-4 label + control groups are single fixed rows (Row / Line = 1-row grid): 换手, 开镜, 分辨率 / 比例 / FOV, 后坐力, 滤镜 sliders, 显示器硬件, 预设, 分享码, 调试滤镜",
                    miss.Count == 0, string.Join(", ", miss.ToArray()));
            }

            // 5. the share-code box never outgrows its frame (V43-OVERLAP #3): the frame decides the height
            {
                string fil = File.ReadAllText(Path.Combine(dir, "SettingsForm.PageFilter.cs"), Encoding.UTF8);
                Check("V431-G2-5 分享码 box: AutoSize off inside its frame (no 3 px clip)", fil.Contains("box.AutoSize = false;"), "");
            }
        }

        static string StripComments(string s)
        {
            // line comments outside strings only (strings here never contain "//" except URLs, which these files do not have)
            StringBuilder b = new StringBuilder(s.Length);
            foreach (string line in s.Split('\n'))
            {
                int cut = -1;
                bool inStr = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '"' && (i == 0 || line[i - 1] != '\\')) { inStr = !inStr; }
                    else if (!inStr && c == '/' && i + 1 < line.Length && line[i + 1] == '/') { cut = i; break; }
                }
                b.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }
            return b.ToString();
        }
    }
}
