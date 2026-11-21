// UI41F (v4.1): settings window pure logic -- number-box clamp (brute-ui F4), text-size settle (F1), 指南 format (UI41-review #4),
// 换手 rows without 站着偏移, and source scans of the settings-window files (no admin / 任务计划 wording, version v4.1, no 站着偏移 UI).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V41UiInit() { V41UiTests(); }

        static string UiSrcDir()
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string p = Path.Combine(d, "..", "tarkov-crosshair-v4", "src");
                if (Directory.Exists(p)) return Path.GetFullPath(p);
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return "/home/dev/tarkov-crosshair-v4/src";
        }

        /// <summary>Every string literal of a C# file (no comments), roughly: enough for wording scans.</summary>
        static List<string> UiLiterals(string code)
        {
            List<string> r = new List<string>();
            int i = 0;
            while (i < code.Length)
            {
                char c = code[i];
                if (c == '/' && i + 1 < code.Length && code[i + 1] == '/') { while (i < code.Length && code[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < code.Length && code[i + 1] == '*') { int e = code.IndexOf("*/", i + 2); i = e < 0 ? code.Length : e + 2; continue; }
                if (c == '\'') { int e = i + 1; while (e < code.Length && code[e] != '\'') { if (code[e] == '\\') e++; e++; } i = e + 1; continue; }
                if (c == '"')
                {
                    bool verbatim = i > 0 && code[i - 1] == '@';
                    StringBuilder sb = new StringBuilder();
                    int e = i + 1;
                    while (e < code.Length)
                    {
                        if (verbatim && code[e] == '"' && e + 1 < code.Length && code[e + 1] == '"') { sb.Append('"'); e += 2; continue; }
                        if (!verbatim && code[e] == '\\') { sb.Append(code[e]); if (e + 1 < code.Length) sb.Append(code[e + 1]); e += 2; continue; }
                        if (code[e] == '"') break;
                        sb.Append(code[e]); e++;
                    }
                    r.Add(sb.ToString());
                    i = e + 1;
                    continue;
                }
                i++;
            }
            return r;
        }

        static readonly string[] UiFiles = {
            "SettingsForm.cs", "SettingsForm.PageFeedback.cs", "SettingsForm.PageGeneral.cs", "SettingsForm.PageGuide.cs",
            "SettingsForm.PageJump.cs", "SettingsForm.PageKeys.cs", "SettingsForm.PageLeanHand.cs", "SettingsForm.PageMove.cs",
            "SettingsForm.PageRecoil.cs", "StylePanel.cs", "CurveEditor.cs", "Theme.cs", "UiPrefs.cs", "Splash.cs", "Program.cs", "OffsetUnits.cs",
            "SettingsForm.PageFilter.cs", "FilterHost.cs", "FilterControls.cs", "FilterHotkeys.cs" }; // v4.2 滤镜 (W2)

        static void V41UiTests()
        {
            // UI1 brute-ui F4: model double -> decimal never throws, clamps BEFORE the cast, rounds to the box decimals.
            {
                bool ok = true; string d = "";
                double[] vs = { 1e300, -1e300, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 7.9e28, -8e28, 12.345, -12.345, 0.005 };
                decimal[] want = { 500m, -500m, 0m, 0m, 0m, 500m, -500m, 12.35m, -12.35m, 0.01m };
                for (int i = 0; i < vs.Length; i++)
                {
                    decimal got;
                    try { got = UiNum.ToDec(vs[i], -500m, 500m, 2); }
                    catch (Exception ex) { ok = false; d += vs[i] + " threw " + ex.GetType().Name + " "; continue; }
                    if (got != want[i]) { ok = false; d += vs[i] + "->" + got + " "; }
                }
                try { if (UiNum.ToDec(3.3, 0m, 10m, 40) != 3.3m) { ok = false; d += "dec40 "; } } catch (Exception) { ok = false; d += "dec40 threw "; }
                if (UiNum.ToDec(-7, 0m, 100m, 0) != 0m || UiNum.ToDec(1e30, 10m, 5000m, 0) != 5000m) { ok = false; d += "range "; }
                Check("UI1 UiNum.ToDec: ±1e300 / ±Inf / NaN / 8e28 clamp before the decimal cast, no throw", ok, d);
            }

            // UI2 百分比偏移 box maximum: floor to 0.01 %, so the typed maximum never exceeds the px range.
            {
                GlobalSettings g = UnitsG(2560, 1440, 75, 75);
                decimal m500 = UiNum.MaxPct(500, g), m1000 = UiNum.MaxPct(1000, g);
                GlobalSettings g50 = UnitsG(2560, 1440, 50, 75);
                decimal m50 = UiNum.MaxPct(500, g50);
                // v5.1.1 (X5, Leo 21 Nov 2026): FOV 50 shrinks only the other side, so the box maximum is the gun side's (= FOV 75)
                bool ok = m500 == 69.44m && m1000 == 138.88m
                    && OffsetUnits.ToPx((double)m500, g) <= 500 && OffsetUnits.ToPx((double)m50, OffsetAxis.X, g50) <= 500 && m50 == m500;
                Check("UI2 UiNum.MaxPct: 500 px = 69.44 % (Leo默认 FOV 75), never past the px range", ok, m500 + " " + m1000 + " fov50 " + m50);
            }

            // UI4 指南 (UI41-review #4): no ===== rule lines, every heading bold accent with a gap, 「名 ……… 说明」 as a 2-column table.
            {
                string[] gl = GuideFormat.Lines(GuideText.Text);
                int[] gk = GuideFormat.Kinds(gl);
                string rtf = GuideFormat.Rtf(gl, gk, "Microsoft YaHei UI", 11f, System.Drawing.Color.White, System.Drawing.Color.Lime, System.Drawing.Color.Gray);
                int heads = 0, leaders = 0;
                for (int i = 0; i < gl.Length; i++)
                {
                    if (gk[i] == GuideFormat.Heading) heads++;
                    string a, b, c;
                    if (gk[i] == GuideFormat.Body && GuideFormat.SplitLeader(gl[i], out a, out b, out c)) leaders++;
                }
                int bolds = 0, tabs = 0;
                for (int ix = rtf.IndexOf("\\cf2\\b "); ix >= 0; ix = rtf.IndexOf("\\cf2\\b ", ix + 1)) bolds++;
                for (int ix = rtf.IndexOf("\\tab "); ix >= 0; ix = rtf.IndexOf("\\tab ", ix + 1)) tabs++;
                bool ok = !rtf.Contains("=====") && bolds == heads && heads > 3 && leaders >= 5 && tabs >= leaders
                    && !rtf.Contains("\\u8230?\\u8230?") /* "……" leaders gone */ && rtf.Contains("\\sb180") && rtf.Contains("\\tx");
                string i1, l1, r1;
                bool sp = GuideFormat.SplitLeader("  settings.json …………………… 你的全部设置", out i1, out l1, out r1) && l1 == "settings.json" && r1 == "你的全部设置" && i1 == "  ";
                string i2, l2, r2;
                sp &= !GuideFormat.SplitLeader("  settings.json 是设置文件", out i2, out l2, out r2);           // one '.' is a file name
                sp &= GuideFormat.SplitLeader("「管理 ▾」 ……… 新建", out i2, out l2, out r2) && l2 == "「管理 ▾」";
                sp &= !GuideFormat.SplitLeader("……… 只有点", out i2, out l2, out r2) && !GuideFormat.SplitLeader("名字 ………", out i2, out l2, out r2);
                int depth = 0; for (int i = 0; i < rtf.Length; i++) { if (rtf[i] == '\\') { i++; continue; } if (rtf[i] == '{') depth++; if (rtf[i] == '}') depth--; }
                Check("UI4 指南 RTF: rules dropped, " + heads + " headings bold+gap, " + leaders + " leader lines -> tab table", ok && sp && depth == 0,
                    "bolds=" + bolds + " tabs=" + tabs + " split=" + sp + " depth=" + depth);
            }

            // UI5 Leo: the standing-still position is always the screen centre -> the 换手 page has no 站着偏移 row.
            {
                int[] rows = UiPages.HandRows();
                bool ok = Array.IndexOf(rows, Profile.HandStand) < 0 && rows.Length == Profile.HandItemCount - 1;
                foreach (int r in rows) if (Profile.HandItemNames[r].Contains("站着") || Profile.HandItemNames[r].Contains("静止")) ok = false;
                Check("UI5 换手 rows: " + rows.Length + " items, never HandStand / 站着 / 静止", ok, string.Join(",", rows));
            }

            // UI6 source scans of the settings-window files: no 站着偏移 / 静止位置 UI text, no admin / 任务计划 / UAC wording,
            // the product version is v4.1 and no user text hard-codes v4.0.
            {
                string dir = UiSrcDir();
                bool ok = true; string d = ""; int files = 0;
                string[] banned = { "站着偏移", "静止位置", "管理员", "任务计划", "UAC", "以管理员", "requireAdministrator", "v4.0" };
                foreach (string f in UiFiles)
                {
                    string p = Path.Combine(dir, f);
                    if (!File.Exists(p)) { ok = false; d += "missing " + f + " "; continue; }
                    files++;
                    foreach (string lit in UiLiterals(File.ReadAllText(p, Encoding.UTF8)))
                        foreach (string b in banned)
                            if (lit.Contains(b)) { ok = false; d += f + ":[" + b + "] "; }
                }
                string prog = File.ReadAllText(Path.Combine(dir, "Program.cs"), Encoding.UTF8);
                if (!prog.Contains("public const string Version = \"v5.2\";")) { ok = false; d += "Version "; } // v5.1 (W25)
                Check("UI6 settings-window files (" + files + "): no 站着偏移 / admin / 任务计划 / v4.0 user text, Version v5.0", ok, d);
            }

            // UI9 (REBRUTE41 P2-2) 外观 preview footer: a look the 中心留空 hides says so in amber with no ✓ (RingStyle.Hidden or a
            // raster without one visible pixel); a visible look keeps 「▣ 中心留空 x px ✓」.
            {
                bool ok = true; string d = "";
                Profile big = R41Tile("chevron"); StyleShapes.Of(big).Hole = 40;
                RingStyle bigs = RingStyle.From(big, 0, false);
                int n = RingRaster.CanvasSize(bigs);
                if (!StyleFooter.Invisible(bigs, RingRaster.Render(bigs, n, n / 2.0, n / 2.0))) { ok = false; d += "chevron+hole40 not flagged "; }
                Profile vis = R41Tile("chevron");
                RingStyle vs = RingStyle.From(vis, 0, false);
                int vn = RingRaster.CanvasSize(vs);
                if (StyleFooter.Invisible(vs, RingRaster.Render(vs, vn, vn / 2.0, vn / 2.0))) { ok = false; d += "default chevron flagged "; }
                RingStyle plain = new RingStyle();
                if (!StyleFooter.Invisible(plain, new int[16])) { ok = false; d += "blank raster "; }
                if (StyleFooter.Invisible(plain, null) || StyleFooter.Invisible(null, new int[4])) { ok = false; d += "null "; }
                string[] hc = StyleFooter.Candidates(40, true);
                foreach (string c in hc)
                    if (c.Contains("✓") || !c.StartsWith("▲")) { ok = false; d += "hidden[" + c + "] "; }
                if (!hc[0].Contains("40 px") || !hc[0].Contains("看不见")) { ok = false; d += "text[" + hc[0] + "] "; }
                string[] vc = StyleFooter.Candidates(3, false);
                if (vc[0] != "▣ 中心留空 3 px ✓" || vc[vc.Length - 1] != "✓") { ok = false; d += "visible[" + vc[0] + "] "; }
                if (StyleFooter.Candidates(double.NaN, false)[0] != "▣ 中心留空 0 px ✓") { ok = false; d += "nan "; }
                Check("UI9 外观 footer: a hidden look (宽距三角 + 中心留空 40) says 看不见 with no ✓; a visible one keeps ✓", ok, d);
            }

            // UI10 (DOCCHECK41 U1, U2, U4) no English page codes (「首页 // SYS.LIVE」) and no stale wording left in the
            // settings-window files; UiPages.Codes stays one entry per page (12 in v4.2), all empty (PageTitle draws nothing for an empty one).
            {
                bool ok = UiPages.Codes.Length == UiPages.Ids.Length && UiPages.Ids.Length == 13; string d = ""; // v4.2: + 滤镜; v5.1 (W34): + 常见问题 (W26, decision 13)
                foreach (string c in UiPages.Codes) if (c.Length != 0) { ok = false; d += "code[" + c + "] "; }
                string[] old = { "SYS.LIVE", "INPUT.MAP", "DISPLAY.CFG", "RECOIL.CFG", "MOTION.CFG", "LEAN.CFG", "HAND.CFG", "JUMP.CFG",
                                 "STYLE.CFG", "README.TXT", "CONTACT", "正数往上", "按参考代码", "100％",
                                 "鼠标滞后", "转一圈校准", "校准键", "开始校准", "游戏灵敏度", "鼠标 DPI" }; // v4.3 (W10): 鼠标滞后 removed
                string dir = UiSrcDir();
                foreach (string f in UiFiles)
                {
                    string p = Path.Combine(dir, f);
                    if (!File.Exists(p)) continue;
                    foreach (string lit in UiLiterals(File.ReadAllText(p, Encoding.UTF8)))
                        foreach (string o in old)
                            if (lit.Contains(o)) { ok = false; d += f + ":[" + o + "] "; }
                }
                Check("UI10 no English page codes / stale 跳跃 wording / no 鼠标滞后 (DPI, 灵敏度, 校准) text left / 「100％」 in the settings-window files", ok, d);
            }
        }
    }
}
