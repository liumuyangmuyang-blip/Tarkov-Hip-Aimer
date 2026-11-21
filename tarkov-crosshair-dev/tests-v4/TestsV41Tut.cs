// TUT41F (v4.1): tutorial 百分比偏移 helpers (TutorialPct / TutorialPctLabel / TutorialPctVecLabel, src/TutorialModel.cs),
// tutorial layout rules (TutorialPlace: labels inside their view, off other labels, max 2 tags per view) and the guide text
// (说明 README.txt = README.txt = GuideText.Text: v4.1, 百分比偏移, ready-built exe, no bat / UAC / px; v4.3: no 鼠标滞后).
// TUT41G (T41-9..13): DOCCHECK41 fixes -- no English in tutorial user text, terminal 「完成」, step 9 / step 3 wording,
// 「设置教程」 on replay (TutorialTitle), README = code (page list + 指南, FOV sentence via OffsetUnits, exact labels, exe name).
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
        internal static void V41TutInit() { V41TutTests(); }

        static GlobalSettings TutG(int w, int h, double fov)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.ResGameW = w; g.ResGameH = h; g.ResFov = fov; g.ResBaseFov = 75;
            g.Validate();
            return g;
        }

        static bool Inside(double x, double y, double w, double h, double minX, double minY, double maxX, double maxY)
        {
            const double e = 1e-6;
            return x >= minX - e && y >= minY - e && x + w <= maxX + e && y + h <= maxY + e;
        }

        static void V41TutTests()
        {
            // T41-1 the tutorial's % strings = OffsetUnits (Leo's example: D X -30 px = 「← 4.17%」 at FOV 75)
            {
                GlobalSettings g = TutG(2560, 1440, 75);
                string a = TutorialPct.X(-30, g), b = TutorialPct.Y(10, g), c = TutorialPct.Vec(-30, 10, g), d = TutorialPct.Vec(0, 0, g);
                string e = TutorialPct.Mag(-30, g), f = TutorialPct.Y(-33, g);
                bool ok = a == "← 4.17%" && b == "↑ 1.39%" && c == "← 4.17%  ↑ 1.39%" && d == "0.00%" && e == "4.17%" && f == "↓ 4.58%";
                Check("T41-1 tutorial % strings (FOV 75)", ok, a + " | " + b + " | " + c + " | " + d + " | " + e + " | " + f);
            }

            // T41-2 FOV is converted by the SAME implementation as the settings pages (FOV 50 -> 「← 6.86%」)
            {
                bool ok = true; string d = "";
                int[][] res = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3440, 1440 } };
                foreach (int[] r in res)
                {
                    GlobalSettings g = TutG(r[0], r[1], 50);
                    string t = TutorialPct.X(-30, g), o = OffsetUnits.Format(-30, OffsetAxis.X, g);
                    if (t != o || t != "← 2.78%") /* v5.1.1 (X5, Leo 21 Nov 2026): D left x 50/75 */ { ok = false; d += r[0] + "x" + r[1] + "=" + t + "/" + o + " "; }
                }
                Check("T41-2 tutorial % at FOV 50 = OffsetUnits (4 resolutions)", ok, d.Length == 0 ? "← 2.78%" : d);
            }

            // T41-3 per-frame labels: rebuilt only when the shown value changes (same string object otherwise)
            {
                GlobalSettings g = TutG(1920, 1080, 75);
                TutorialPctLabel l = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);
                string s1 = l.Of(-30, g), s2 = l.Of(-30.001, g), s3 = l.Of(30, g), s4 = l.Of(0.001, g);
                TutorialPctLabel m = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Magnitude, "还差 ", "");
                string m1 = m.Of(-10, g), m2 = m.Of(double.NaN, g);
                TutorialPctVecLabel v = new TutorialPctVecLabel("当前：", "");
                string v1 = v.Of(-30, 10, g), v2 = v.Of(-30, 10, g);
                bool ok = s1 == "← 4.17%" && ReferenceEquals(s1, s2) && s3 == "→ 4.17%" && s4 == "0.00%" && m1 == "还差 1.39%" && m2 == "还差 0.00%"
                    && v1 == "当前：← 4.17%  ↑ 1.39%" && ReferenceEquals(v1, v2);
                Check("T41-3 TutorialPctLabel / TutorialPctVecLabel (cached, arrows, magnitude, NaN)", ok,
                    s1 + " " + s3 + " " + s4 + " | " + m1 + " | " + m2 + " | " + v1);
            }

            // T41-4 % input boxes: px -> shown % -> px keeps the shown value; clamped to the px range; limit never passes it
            {
                bool ok = true; string d = "";
                Random rnd = new Random(4141);
                int[] fovs = { 75, 50, 62 };
                foreach (int fov in fovs)
                {
                    GlobalSettings g = TutG(1920, 1080, fov);
                    for (int i = 0; i < 20000 && ok; i++)
                    {
                        double px = (rnd.NextDouble() * 2 - 1) * 500;
                        double pct = TutorialPct.FieldPct(px, g);
                        int sign = px < 0 ? -1 : 1;
                        double px2 = TutorialPct.FieldPx(pct, sign, -500, 500, g);
                        double pct2 = TutorialPct.FieldPct(px2, g);
                        if (pct2 != pct || Math.Abs(px2 - px) > 0.0051 * OffsetUnits.PxPerPct(g) || px2 < -500 || px2 > 500)
                        { ok = false; d = "fov " + fov + " px " + px + " -> " + pct + "% -> " + px2; }
                    }
                    double lim = TutorialPct.FieldLimit(-500, 500, g);
                    if (TutorialPct.FieldPx(lim, 1, -500, 500, g) > 500 || TutorialPct.FieldPx(lim + 0.01, 1, -500, 500, g) != 500
                        || Math.Abs(TutorialPct.FieldPx(lim, 1, double.MinValue, double.MaxValue, g) - 500) > OffsetUnits.PxPerPct(g) * 0.011)
                    { ok = false; d += " limit " + lim + " fov " + fov; }
                }
                GlobalSettings g75 = TutG(2560, 1440, 75);
                double l1 = TutorialPct.FieldLimit(-500, 500, g75), l2 = TutorialPct.FieldLimit(0, 1000, g75);
                double c1 = TutorialPct.FieldPx(1e9, 1, -500, 500, g75), c2 = TutorialPct.FieldPx(double.NaN, -1, -500, 500, g75);
                double c3 = TutorialPct.FieldPx(4.17, -1, -500, 500, g75);
                if (l1 != 69.44 || l2 != 138.88 || c1 != 500 || c2 != 0 || Math.Abs(c3 - (-30.024)) > 1e-9) { ok = false; d += " l1=" + l1 + " l2=" + l2 + " c1=" + c1 + " c2=" + c2 + " c3=" + c3; }
                Check("T41-4 % box <-> px (60000 values, 3 FOVs): round trip, clamp, limit", ok, d.Length == 0 ? "limit ±500 px = 69.44 %, 0..1000 px = 138.88 % (FOV 75)" : d);
            }

            // T41-5 TutorialPlace.Down: below every obstacle it touches, inside the bounds, false when there is no room / NaN
            {
                float[] one = { 0, 0, 100, 20 }, two = { 0, 0, 100, 20, 0, 24, 100, 20 };
                double ox, oy;
                bool a = TutorialPlace.Down(10, 5, 50, 10, 0, 0, 200, 200, one, 1, 4, out ox, out oy) && oy == 24 && ox == 10;
                bool b = TutorialPlace.Down(10, 5, 50, 10, 0, 0, 200, 200, two, 2, 4, out ox, out oy) && oy == 48;
                bool c = !TutorialPlace.Down(10, 5, 50, 10, 0, 0, 200, 30, one, 1, 4, out ox, out oy);
                bool e = !TutorialPlace.Down(double.NaN, 5, 50, 10, 0, 0, 200, 200, one, 1, 4, out ox, out oy);
                bool f = TutorialPlace.Down(190, 5, 50, 10, 0, 0, 200, 200, null, 0, 4, out ox, out oy) && ox == 150;
                bool brute = true; string d = "";
                Random rnd = new Random(77);
                float[] obs = new float[4 * 8];
                for (int i = 0; i < 20000 && brute; i++)
                {
                    int n = rnd.Next(0, 9);
                    for (int k = 0; k < n; k++)
                    {
                        obs[4 * k] = (float)(rnd.NextDouble() * 300); obs[4 * k + 1] = (float)(rnd.NextDouble() * 300);
                        obs[4 * k + 2] = (float)(5 + rnd.NextDouble() * 80); obs[4 * k + 3] = (float)(5 + rnd.NextDouble() * 40);
                    }
                    double w = 10 + rnd.NextDouble() * 60, h = 8 + rnd.NextDouble() * 20;
                    if (TutorialPlace.Down(rnd.NextDouble() * 300, rnd.NextDouble() * 300, w, h, 0, 0, 320, 320, obs, n, 3, out ox, out oy))
                    {
                        if (!Inside(ox, oy, w, h, 0, 0, 320, 320)) { brute = false; d = "outside"; }
                        for (int k = 0; k < n && brute; k++)
                            if (ox < obs[4 * k] + obs[4 * k + 2] && ox + w > obs[4 * k] && oy < obs[4 * k + 1] + obs[4 * k + 3] && oy + h > obs[4 * k + 1])
                            { brute = false; d = "overlaps obstacle " + k + " at try " + i; }
                    }
                }
                Check("T41-5 TutorialPlace.Down (wall label below the HUD tags; 20000 random layouts never overlap)", a && b && c && e && f && brute,
                    "a=" + a + " b=" + b + " c=" + c + " nan=" + e + " clampX=" + f + " " + d);
            }

            // T41-6 TutorialPlace.NameLabel: 「你」 at the map's bottom edge goes beside the figure, never outside the map
            {
                double x, y;
                TutorialPlace.NameLabel(100, 195, 20, 14, 16, 16, false, 0, 0, 200, 200, out x, out y);
                bool edge = Inside(x, y, 20, 14, 0, 0, 200, 200) && x >= 116 && y < 195;
                TutorialPlace.NameLabel(100, 50, 20, 14, 16, 16, false, 0, 0, 200, 200, out x, out y);
                bool below = x == 90 && y == 66;
                TutorialPlace.NameLabel(195, 195, 20, 14, 16, 16, false, 0, 0, 200, 200, out x, out y);
                bool corner = Inside(x, y, 20, 14, 0, 0, 200, 200) && x + 20 <= 195 - 16 + 1e-9;
                bool brute = true; string d = "";
                Random rnd = new Random(5);
                for (int i = 0; i < 20000 && brute; i++)
                {
                    double w = 8 + rnd.NextDouble() * 40, h = 8 + rnd.NextDouble() * 20;
                    double cx = rnd.NextDouble() * 160, cy = rnd.NextDouble() * 120;
                    TutorialPlace.NameLabel(cx, cy, w, h, 16, 14, rnd.Next(2) == 0, 0, 0, 160, 120, out x, out y);
                    if (!Inside(x, y, w, h, 0, 0, 160, 120)) { brute = false; d = "outside at " + cx + "," + cy; }
                }
                TutorialPlace.NameLabel(double.NaN, double.NaN, 20, 14, 16, 16, false, 0, 0, 200, 200, out x, out y);
                bool nan = Inside(x, y, 20, 14, 0, 0, 200, 200);
                Check("T41-6 TutorialPlace.NameLabel (whole label inside the view; 20000 random markers)", edge && below && corner && brute && nan,
                    "edge=" + edge + " below=" + below + " corner=" + corner + " nan=" + nan + " " + d);
            }

            // T41-7 per-view tag count (Leo: at most 2 tags per view): tags belong to the smallest view holding their centre
            {
                float[] views = { 0, 0, 100, 100, 200, 0, 100, 100, 210, 10, 40, 40 };
                float[] tags = { 10, 10, 30, 10, 10, 40, 30, 10, 250, 70, 30, 10, 220, 20, 10, 10, 120, 150, 30, 10, 500, 500, 10, 10 };
                int[] counts = new int[4];
                int m = TutorialPlace.MaxPerView(tags, 6, views, 3, 0, 0, 400, 300, counts);
                bool a = m == 2 && counts[0] == 2 && counts[1] == 1 && counts[2] == 1;
                float[] tags3 = { 10, 10, 30, 10, 10, 40, 30, 10, 10, 70, 30, 10 };
                bool b = TutorialPlace.MaxPerView(tags3, 3, views, 3, 0, 0, 400, 300, counts) == 3;
                bool c = TutorialPlace.MaxPerView(tags3, 3, null, 0, 0, 0, 400, 300, null) == 3;
                bool e = TutorialPlace.MaxPerView(null, 0, views, 3, 0, 0, 400, 300, counts) == 0;
                Check("T41-7 TutorialPlace.MaxPerView (views, scene, nested, outside not counted)", a && b && c && e,
                    "m=" + m + " a=" + a + " b=" + b + " c=" + c + " e=" + e);
            }

            // T41-8 guide text: 指南 = 说明 README.txt = README.txt; v4.2 content; no bat / UAC / px; one fallback 管理员 line
            {
                string t = GuideText.Text.Replace("\r", "");
                string d = "";
                string[] must = { "v5.2", "百分比偏移", "TarkovHipAimer.exe", // v5.1 (W25, decision 5): standing point = 站立静止偏移
                                  "FOV 75", "屏幕中心右下一点", "站立静止偏移", /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in stands right-down */ "计划任务", "以管理员身份运行" }; // v5.0.2 (W17): autostart = scheduled task, the app asks for admin
                foreach (string w in must) if (t.IndexOf(w, StringComparison.Ordinal) < 0) d += " missing「" + w + "」";
                string[] never = { "Start-HipAimer", ".bat", "bat ", "px", "UAC", "封号", "风险", "BattlEye", "站着偏移", "站立偏移", "鼠标滞后", "鼠标 DPI", "灵敏度", "校准键", "转一圈", "F11" }; // v4.3 (W10): 鼠标滞后 removed // v4.2: F8 is the 滤镜 toggle (Leo 08:35 UTC: F8 allowed)
                foreach (string w in never) if (t.IndexOf(w, StringComparison.Ordinal) >= 0) d += " has「" + w + "」";
                int adminLines = 0;
                foreach (string line in t.Split('\n')) if (line.IndexOf("管理员", StringComparison.Ordinal) >= 0) { adminLines++; if (line.IndexOf("以管理员身份运行", StringComparison.Ordinal) < 0) d += " admin line「" + line.Trim() + "」"; }
                if (adminLines != 1) d += " admin lines=" + adminLines;
                string root = "/home/dev/tarkov-crosshair-v4";
                string a = Path.Combine(root, "说明 README.txt"), b = Path.Combine(root, "README.txt");
                if (File.Exists(a))
                {
                    string sa = File.ReadAllText(a).Replace("\r", "").TrimStart('\uFEFF');
                    if (sa != t) d += " GuideText differs from 说明 README.txt (run gen-guide.py)";
                    if (File.Exists(b) && File.ReadAllText(b).Replace("\r", "").TrimStart('\uFEFF') != sa) d += " README.txt differs";
                }
                Check("T41-8 guide text (README = 指南): v4.1, 百分比偏移, exe, no 鼠标滞后 / F11 / bat/UAC/px/risk", d.Length == 0, d.Length == 0 ? t.Length + " chars" : d);
            }

            V41TutDocTests();
        }

        // ------------------------------------------------------------------ TUT41G (DOCCHECK41 fixes)

        /// <summary>src/ of the tree under test (a package snapshot finds its own copy first), else the live tree.</summary>
        static string TutSrcDir()
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

        /// <summary>Every string literal of a C# 5 file with its line number (comments and char literals skipped).</summary>
        static List<KeyValuePair<int, string>> TutLiterals(string code)
        {
            List<KeyValuePair<int, string>> r = new List<KeyValuePair<int, string>>();
            int i = 0, line = 1, n = code.Length;
            while (i < n)
            {
                char c = code[i];
                if (c == '\n') { line++; i++; continue; }
                if (c == '/' && i + 1 < n && code[i + 1] == '/') { while (i < n && code[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < n && code[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(code[i] == '*' && code[i + 1] == '/')) { if (code[i] == '\n') line++; i++; }
                    i += 2; continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < n && code[i] != '\'') { if (code[i] == '\\') i++; i++; }
                    i++; continue;
                }
                if (c == '@' && i + 1 < n && code[i + 1] == '"')
                {
                    int startLine = line; StringBuilder sb = new StringBuilder(); i += 2;
                    while (i < n)
                    {
                        if (code[i] == '"') { if (i + 1 < n && code[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; }
                        if (code[i] == '\n') line++;
                        sb.Append(code[i]); i++;
                    }
                    r.Add(new KeyValuePair<int, string>(startLine, sb.ToString())); continue;
                }
                if (c == '"')
                {
                    StringBuilder sb = new StringBuilder(); i++;
                    while (i < n && code[i] != '"' && code[i] != '\n')
                    {
                        if (code[i] == '\\' && i + 1 < n)
                        {
                            char e = code[i + 1];
                            if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'u' && i + 5 < n)
                            { sb.Append((char)Convert.ToInt32(code.Substring(i + 2, 4), 16)); i += 6; continue; }
                            else sb.Append(e);
                            i += 2; continue;
                        }
                        sb.Append(code[i]); i++;
                    }
                    i++;
                    r.Add(new KeyValuePair<int, string>(line, sb.ToString())); continue;
                }
                i++;
            }
            return r;
        }

        /// <summary>A literal the player reads: it holds Chinese (or full-width punctuation), it is a terminal line 「> …」, or it
        /// is a HUD caption built like 「后坐力 · Y」 (the v4.0 「RECOIL · Y」 had no Chinese at all).</summary>
        static bool TutIsUserText(string s)
        {
            if (s.StartsWith("> ", StringComparison.Ordinal) || s.IndexOf(" · ", StringComparison.Ordinal) >= 0) return true;
            foreach (char ch in s) if ((ch >= 0x4E00 && ch <= 0x9FFF) || (ch >= 0x3000 && ch <= 0x303F) || (ch >= 0xFF00 && ch <= 0xFFEF)) return true;
            return false;
        }

        static void V41TutDocTests()
        {
            string src = TutSrcDir();
            string[] tutFiles = Directory.Exists(src) ? Directory.GetFiles(src, "Tutorial*.cs") : new string[0];
            Array.Sort(tutFiles, StringComparer.Ordinal);

            // T41-9 (DOCCHECK41 T1-T3, T6) no English words in what the tutorial shows: only key names (WA WD SA SD, XButton1/2 as
            // the 按键 page writes them), Leo's WMX200 / PVP / IR note and the product name; distances in 米, never 「10m」
            {
                string[] allowed = { "WA", "WD", "SA", "SD", "XButton1", "XButton2", "WMX200", "PVP", "IR", "Tarkov", "Hip", "Aimer",
                    "F12" }; // v5.1 (W27): F12 = the 复位 key named on the 丢雷 / 强化 / 站立 pages
                string d = ""; int lits = 0, user = 0;
                foreach (string f in tutFiles)
                    foreach (KeyValuePair<int, string> lit in TutLiterals(File.ReadAllText(f)))
                    {
                        lits++;
                        string t = lit.Value;
                        if (!TutIsUserText(t)) continue;
                        user++;
                        foreach (Match m in Regex.Matches(t, "[A-Za-z][A-Za-z0-9]*"))
                            if (m.Value.Length >= 2 && Array.IndexOf(allowed, m.Value) < 0) d += " " + Path.GetFileName(f) + ":" + lit.Key + "「" + m.Value + "」";
                        if (Regex.IsMatch(t, @"\d\s?m\b")) d += " " + Path.GetFileName(f) + ":" + lit.Key + " metres as 「m」";
                    }
                Check("T41-9 tutorial user text: no English words (key names, WMX200/PVP/IR, product name only), distances in 米",
                    d.Length == 0 && tutFiles.Length >= 15 && user > 200, d.Length == 0 ? user + " user-text literals of " + lits + " in " + tutFiles.Length + " files" : d);
            }

            // T41-10 (DOCCHECK41 T1) terminal lines 「> …」: a finished line ends in 「完成」 (the 首页 terminal's word), never 「ok」
            {
                string d = ""; int lines = 0, done = 0;
                foreach (string f in tutFiles)
                    foreach (KeyValuePair<int, string> lit in TutLiterals(File.ReadAllText(f)))
                    {
                        if (!lit.Value.StartsWith("> ", StringComparison.Ordinal)) continue;
                        lines++;
                        if (Regex.IsMatch(lit.Value, @"\bok\b", RegexOptions.IgnoreCase)) d += " " + Path.GetFileName(f) + ":" + lit.Key + "「" + lit.Value + "」";
                        if (lit.Value.EndsWith("完成", StringComparison.Ordinal)) done++;
                    }
                Check("T41-10 tutorial terminal lines end in 「完成」, no 「ok」", d.Length == 0 && done >= 15, d.Length == 0 ? lines + " terminal lines, " + done + " end in 完成" : d);
            }

            // T41-11 (DOCCHECK41 T4, T5, R15) step 9 sends 换手检测 to the 按键 page (where the box is); no 「扫射教程」 (there is
            // none); the side buttons read 「鼠标侧键1」 as on the 按键 page (no space before the digit)
            {
                string d = "";
                string s9 = File.ReadAllText(Path.Combine(src, "TutorialStep9.cs")), keys = File.ReadAllText(Path.Combine(src, "SettingsForm.PageKeys.cs"));
                if (s9.IndexOf("换手检测未开启 · 到「按键」页打开", StringComparison.Ordinal) < 0) d += " step 9 tag";
                if (s9.IndexOf("到「换手」页打开", StringComparison.Ordinal) >= 0) d += " step 9 still says 换手 page";
                if (keys.IndexOf("Check(\"换手检测\"", StringComparison.Ordinal) < 0) d += " 按键 page lost its 换手检测 box";
                foreach (string f in tutFiles)
                {
                    string t = File.ReadAllText(f);
                    if (t.IndexOf("扫射教程", StringComparison.Ordinal) >= 0) d += " 扫射教程 in " + Path.GetFileName(f);
                    if (t.IndexOf("鼠标侧键 1", StringComparison.Ordinal) >= 0 || t.IndexOf("鼠标侧键 2", StringComparison.Ordinal) >= 0) d += " 「鼠标侧键 N」 in " + Path.GetFileName(f);
                }
                if (GuideText.Text.IndexOf("扫射教程", StringComparison.Ordinal) >= 0) d += " 扫射教程 in the guide";
                string s3 = File.ReadAllText(Path.Combine(src, "TutorialStep3.cs"));
                if (s3.IndexOf("左右先不用管，后面在移动里调。", StringComparison.Ordinal) < 0) d += " step 3 dev note"; // v5.0.2 (UI L10): 左右 / 上下 like the pages
                Check("T41-11 step 9 → 「按键」页 for 换手检测, no 扫射教程, 「鼠标侧键1」", d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // T41-12 (DOCCHECK41 T7) 「首次设置教程」 only the first time: replayed it is 「设置教程」 (the 设置 window's button rule,
            // README 「看完教程后变成「设置教程」」); the window title and the header both take it from TutorialTitle
            {
                string d = "";
                if (TutorialTitle.For(false) != "首次设置教程" || TutorialTitle.For(true) != "设置教程") d += " For";
                if (TutorialTitle.Window(false) != "首次设置教程 · Tarkov Hip Aimer" || TutorialTitle.Window(true) != "设置教程 · Tarkov Hip Aimer") d += " Window";
                foreach (string fn in new string[] { "TutorialForm.cs", "TutorialFrame.cs" })
                {
                    string code = File.ReadAllText(Path.Combine(src, fn));
                    foreach (KeyValuePair<int, string> lit in TutLiterals(code))
                        if (lit.Value.IndexOf("首次设置教程", StringComparison.Ordinal) >= 0) d += " hard-coded title " + fn + ":" + lit.Key;
                    if (code.IndexOf("TutorialTitle.", StringComparison.Ordinal) < 0) d += " " + fn + " does not use TutorialTitle";
                }
                if (GuideText.Text.IndexOf("看完教程后变成「" + TutorialTitle.Again + "」", StringComparison.Ordinal) < 0) d += " README replay name";
                Check("T41-12 tutorial title: 首次设置教程 the first time, 设置教程 when replayed", d.Length == 0, d.Length == 0 ? TutorialTitle.Window(false) + " / " + TutorialTitle.Window(true) : d);
            }

            // T41-13 (DOCCHECK41 R1-R10, R15) README / 指南 = what the code does
            {
                string t = GuideText.Text.Replace("\r", "");
                string d = "";
                // R1 the page list is the navigation (UiPages.Titles, 指南 included), one numbered section per page in that order
                // v5.1 (W25): the menu is 准星 (sub-menu NavModel.SubPages) + 滤镜, links below (NavModel.Links + 常见问题)
                {
                    List<string> sub = new List<string>();
                    foreach (string id in NavModel.SubPages) { int pi = UiPages.IndexOf(id); sub.Add(id == "general" ? "常规" : UiPages.Titles[pi]); }
                    if (t.IndexOf("准星 …… 点开后是准星的所有设置：" + string.Join("、", sub), StringComparison.Ordinal) < 0) d += " page list (准星 sub-menu)";
                    if (t.IndexOf("滤镜 …… 屏幕滤镜", StringComparison.Ordinal) < 0 || t.IndexOf("最下面是小链接：首页、指南、常见问题、反馈。", StringComparison.Ordinal) < 0) d += " page list (滤镜 / links)";
                }
                int pos = 0;
                for (int i = 0; i < UiPages.Titles.Length; i++)
                {
                    string head = "\n" + (i + 1).ToString("00") + " " + UiPages.Titles[i];
                    int p = t.IndexOf(head, pos, StringComparison.Ordinal);
                    if (p < 0) d += " no section「" + head.Trim() + "」 after the previous one"; else pos = p + 1;
                }
                if (t.IndexOf("点「打开说明文件」", StringComparison.Ordinal) >= 0
                    && File.ReadAllText(Path.Combine(src, "SettingsForm.PageGuide.cs")).IndexOf("\"打开说明文件\"", StringComparison.Ordinal) < 0) d += " 指南 button text";
                // R2 the FOV sentence = OffsetUnits: 1 % is half the picture height / 100 (no FOV in it); a smaller FOV raises the
                // 换算系数 and so the shown % (Leo's D X -30 px: 「← 4.17%」 at FOV 75, 「← 6.86%」 at FOV 50)
                // v5.1.1 (X5, Leo 21 Nov 2026): FOV is out of k; D (left, the other side) x gameFov / 75, A (gun side) unchanged
                GlobalSettings g75 = TutG(2560, 1440, 75), g60 = TutG(2560, 1440, 60), g50 = TutG(2560, 1440, 50);
                bool fovFree = Math.Abs(OffsetUnits.HalfHeight(g75) - OffsetUnits.HalfHeight(g50)) < 1e-9, kSame = ViewScale.Factor(g50) == ViewScale.Factor(g75);
                if (!fovFree || !kSame) d += " code: 1% FOV-free=" + fovFree + " k(50)==k(75)=" + kSame;
                string ex = "D 往左「" + OffsetUnits.Format(-30, OffsetAxis.X, g75) + "」，FOV 60 时显示「" + OffsetUnits.Format(-30, OffsetAxis.X, g60) + "」，FOV 50 时显示「" + OffsetUnits.Format(-30, OffsetAxis.X, g50) + "」；A 往右的数不变";
                if (t.IndexOf(ex, StringComparison.Ordinal) < 0) d += " FOV example (want " + ex + ")";
                if (t.IndexOf("和 FOV 无关", StringComparison.Ordinal) < 0 || t.IndexOf("往枪那一侧", StringComparison.Ordinal) < 0 || t.IndexOf("FOV 调小，换算系数变大", StringComparison.Ordinal) >= 0) d += " FOV sentence";
                if (t.IndexOf("同样的百分比在屏幕上会变大", StringComparison.Ordinal) >= 0) d += " old FOV sentence";
                // R3 / R4 / R5 field names verbatim as the pages write them; 「滞后强度」 is gone
                string gen = File.ReadAllText(Path.Combine(src, "SettingsForm.PageGeneral.cs")), mv = File.ReadAllText(Path.Combine(src, "SettingsForm.PageMove.cs"));
                // v4.2 (Leo 17 Nov 2026): 「FOV 类型」 is gone (always vertical) from the page and the README; the page shows the
                // game's 3 settings by their game names
                if (gen.IndexOf("\"FOV 类型\"", StringComparison.Ordinal) >= 0) d += " UI still has 「FOV 类型」";
                if (t.IndexOf("FOV 类型", StringComparison.Ordinal) >= 0) d += " README still has 「FOV 类型」";
                foreach (string lab in new string[] { "游戏分辨率", "画面比例", "FOV" })
                    if (gen.IndexOf("Row(gr, \"" + lab + "\"", StringComparison.Ordinal) < 0) d += " UI lost「" + lab + "」";
                // v4.3 (W10): 鼠标滞后 removed: none of its 移动 page labels is left
                foreach (string lab in new string[] { "鼠标 DPI（左右）", "鼠标 DPI（上下）", "游戏灵敏度", "甩动", "跟手滞后", "弹簧频率", "阻尼", "转一圈校准", "校准键", "开始校准" })
                    if (mv.IndexOf("\"" + lab, StringComparison.Ordinal) >= 0 || mv.IndexOf("鼠标滞后", StringComparison.Ordinal) >= 0) d += " UI still has「" + lab + "」";
                foreach (string gone in new string[] { "滞后强度", "滑后强度", "「FOV 类型（", "「鼠标 DPI」" })
                    if (t.IndexOf(gone, StringComparison.Ordinal) >= 0) d += " has「" + gone + "」";
                // R6 the import keeps this PC's screen, 调参基准 and mouse (GlobalSettings import): all named in one line
                int imp = t.IndexOf("导入时，这台电脑现在的这些设置保持不变", StringComparison.Ordinal);
                foreach (string w in new string[] { "游戏分辨率", "画面比例", "FOV", "调参基准" })
                {
                    int p = imp < 0 ? -1 : t.IndexOf(w, imp, StringComparison.Ordinal);
                    if (p < 0 || p > imp + 120) d += " import line lacks「" + w + "」";
                }
                // R7 / R8 自动分配 and the 校准键 row are 高级设置 items; R9 F12 x2 = MotionEngine's ring-on edge (hand, 强化, lean)
                if (t.IndexOf("打开「高级设置」后可以改每一段的时长、结束位置和曲线；「自动分配」", StringComparison.Ordinal) < 0) d += " 自动分配";
                if (t.IndexOf("按两下（关再开）＝回到正常手、退出强化准星、取消切换的侧身", StringComparison.Ordinal) < 0) d += " F12 x2";
                // R15 常用按键: the default keys and the names the 按键 page shows for them
                KeyBindings k = GlobalSettings.CreateDefault().Keys;
                string[,] rows = {
                    { "\n  " + k.ComboOf(KeyBindings.ActionToggle) + " ……", "" },
                    { "换手（点一下切换）；「按键」页显示「" + k.ComboOf(KeyBindings.ActionHandSwitch) + "」", "" },
                    { "跳跃；「按键」页显示「" + k.ComboOf(KeyBindings.ActionJump) + "」", "" },
                    { "再按一下恢复；「按键」页显示「" + k.ComboOf(KeyBindings.ActionBoost) + "」", "" } };
                for (int i = 0; i < rows.GetLength(0); i++)
                    if (t.IndexOf(rows[i, 0], StringComparison.Ordinal) < 0) d += " 常用按键「" + rows[i, 0].Trim() + "」";
                // the exe name = the one PACKAGE41 puts in the zip (package-v41.sh: EXE, or EXE_ZIP = "<prefix>" + EXE): first entry
                // of 【文件夹里有什么】, and the README never names the compiler output without that prefix
                string pkg = "/home/dev/tarkov-crosshair-dev/package-v41.sh";
                string exeZip = null;
                if (File.Exists(pkg))
                {
                    string ps = File.ReadAllText(pkg);
                    Match m = Regex.Match(ps, "\\nEXE = \"([^\"]+)\"");
                    Match mz = Regex.Match(ps, "\\nEXE_ZIP = \"([^\"]*)\" \\+ EXE");
                    if (!m.Success) d += " no EXE in package-v41.sh";
                    else
                    {
                        exeZip = (mz.Success ? mz.Groups[1].Value : "") + m.Groups[1].Value;
                        int f0 = t.IndexOf("【文件夹里有什么】", StringComparison.Ordinal);
                        int f1 = f0 < 0 ? -1 : t.IndexOf("=\n  " + exeZip + " ……", f0, StringComparison.Ordinal);
                        if (f1 < 0 || f1 > f0 + 80) d += " file list does not start with 「" + exeZip + "」";
                        string prefix = exeZip.Substring(0, exeZip.Length - m.Groups[1].Value.Length);
                        for (int at = t.IndexOf(m.Groups[1].Value, StringComparison.Ordinal); at >= 0; at = t.IndexOf(m.Groups[1].Value, at + 1, StringComparison.Ordinal))
                            if (at < prefix.Length || t.Substring(at - prefix.Length, prefix.Length) != prefix) { d += " bare 「" + m.Groups[1].Value + "」 at char " + at; break; }
                    }
                }
                Check("T41-13 README = code: page list + 指南, FOV sentence (OffsetUnits), exact labels, import, 高级设置 items, F12 x2, keys, exe",
                    d.Length == 0, d.Length == 0 ? ex + "; exe 「" + exeZip + "」" : d);
            }
        }
    }
}
