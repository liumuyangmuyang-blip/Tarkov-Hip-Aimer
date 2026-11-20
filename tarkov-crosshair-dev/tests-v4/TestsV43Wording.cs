// v4.3 Leo rule: user-facing text never carries disclaimers (「未实测」「起点设置」「只是起点」「估计值，需要你在游戏里实测」「示意」
// 「未核实」「还没在游戏里实测」). Scans every string literal (comments skipped) of src/*.cs and src/Shade/PilotHelpText.cs, and both
// README files. Allowed: 「回到起点」 (jump phase), the developer's own 「我实测 / 开发者实测」. Kept-but-never-shown data:
// PilotHelpText's UntestedPreset entry (the 滤镜 page no longer shows it).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TarkovShade;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V43WordingInit() { V43WordingTests(); }

        static readonly string[] BannedDisclaimers = { "未实测", "还没在游戏里实测", "还没有在游戏里实测", "起点设置", "只是起点",
            "估计值，需要你在游戏里实测", "需要你在游戏里实测", "示意", "未核实" };

        /// <summary>String literals of C# source (regular, verbatim and \uXXXX escapes decoded); comments skipped.</summary>
        internal static List<string> CsLiterals(string src)
        {
            List<string> r = new List<string>();
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/') { while (i < n && src[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < n && src[i + 1] == '*') { int e = src.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? n : e + 2; continue; }
                if (c == '\'') { i++; while (i < n && src[i] != '\'') { if (src[i] == '\\') i++; i++; } i++; continue; }
                bool verbatim = c == '@' && i + 1 < n && src[i + 1] == '"';
                if (verbatim || c == '"')
                {
                    i += verbatim ? 2 : 1;
                    StringBuilder sb = new StringBuilder();
                    while (i < n)
                    {
                        char d = src[i];
                        if (verbatim) { if (d == '"') { if (i + 1 < n && src[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; } sb.Append(d); i++; continue; }
                        if (d == '\\' && i + 1 < n)
                        {
                            char e = src[i + 1];
                            if (e == 'u' && i + 5 < n) { sb.Append((char)Convert.ToInt32(src.Substring(i + 2, 4), 16)); i += 6; continue; }
                            sb.Append(e == 'n' ? '\n' : e); i += 2; continue;
                        }
                        if (d == '"') { i++; break; }
                        if (d == '\n') break;
                        sb.Append(d); i++;
                    }
                    r.Add(sb.ToString());
                    continue;
                }
                i++;
            }
            return r;
        }

        static void V43WordingTests()
        {
            string src = UiSrcDirW2();
            string root = Path.GetDirectoryName(src);
            List<string> hits = new List<string>();
            List<string> files = new List<string>(Directory.GetFiles(src, "*.cs"));
            files.Add(Path.Combine(src, "Shade", "PilotHelpText.cs"));
            foreach (string f in files)
            {
                if (!File.Exists(f) || Path.GetFileName(f) == "GuideText.cs") continue; // GuideText = README, checked below
                bool helpText = Path.GetFileName(f) == "PilotHelpText.cs";
                foreach (string lit in CsLiterals(File.ReadAllText(f, Encoding.UTF8)))
                    foreach (string b in BannedDisclaimers)
                        if (lit.Contains(b) && !(helpText && UntestedEntry(lit))) hits.Add(Path.GetFileName(f) + ":「" + Short(lit) + "」");
            }
            foreach (string rd in new[] { "说明 README.txt", "README.txt" })
            {
                string p = Path.Combine(root, rd);
                if (!File.Exists(p)) continue;
                string[] lines = File.ReadAllText(p, Encoding.UTF8).Replace("\r", "").Split('\n');
                for (int k = 0; k < lines.Length; k++)
                    foreach (string b in BannedDisclaimers)
                        if (lines[k].Contains(b)) hits.Add(rd + ":" + (k + 1));
            }
            foreach (string b in BannedDisclaimers) if (GuideText.Text.Contains(b)) hits.Add("GuideText「" + b + "」");
            Check("V43-W1 no disclaimer wording (未实测 / 起点设置 / 示意 / 未核实 ...) in user-facing strings or the README",
                hits.Count == 0, hits.Count + " hit(s): " + string.Join("; ", hits.GetRange(0, Math.Min(hits.Count, 40)).ToArray()));
        }

        /// <summary>PilotHelpText's UntestedPreset label / help / plain text: kept as module data, never shown since v4.3.</summary>
        static bool UntestedEntry(string lit)
        {
            string l = PilotHelpText.Label(PilotHelpText.UntestedPreset), h = PilotHelpText.Help(PilotHelpText.UntestedPreset),
                p = PilotHelpText.Plain(PilotHelpText.UntestedPreset);
            return lit == l || lit == h || lit == p;
        }

        static string Short(string s) { s = s.Replace("\n", " "); return s.Length > 40 ? s.Substring(0, 40) + "…" : s; }
    }
}
