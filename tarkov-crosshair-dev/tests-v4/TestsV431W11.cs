// v4.3.1 (W11): one fixed text size (no window-size scaling), and Alt+Tab back to the window causes no relayout / no
// cache rebuild. Source-structure checks plus the pure helpers; the WinForms side runs only on Windows (UNVERIFIED here).
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V431W11Init() { V431W11Tests(); }

        static string W11Src(string f) { return File.ReadAllText(Path.Combine(UiSrcDir(), f)); }

        static void V431W11Tests()
        {
            string dir = UiSrcDir();

            // W11-1 no window-size text scaling left anywhere
            {
                string bad = "";
                foreach (string f in Directory.GetFiles(dir, "*.cs"))
                {
                    string s = File.ReadAllText(f);
                    foreach (string w in new[] { "UiScale.", "class UiScale", "ApplyUiScale", "SwapFonts", "PostUiScale", "UpdateUiScale",
                        "UiScaleSettle", "UiScaleGate", "StepCache", "fontZoom", "ZoomFor(", "ApplyScale(", "UpdateScale(", "scaleTimer" })
                        if (s.Contains(w)) bad += Path.GetFileName(f) + ":" + w + " ";
                }
                Check("V431-W11-1 no window-size text scaling left (UiScale / ApplyUiScale / SwapFonts / PostUiScale / font step caches / zoom / rescale timers)", bad == "", bad);
            }

            // W11-2 the fixed sizes: settings body 11 pt, tutorial body 13 pt via scale 1, dashboard 11 pt body, 外观 11 pt body
            {
                string sf = W11Src("SettingsForm.cs"), dp = W11Src("DashboardPanel.cs"), sp = W11Src("StylePanel.cs"),
                    tf = W11Src("TutorialForm.cs"), ft = W11Src("FilterTutorialForm.cs"), th = W11Src("Theme.cs");
                bool ok = sf.Contains("BaseUiPt = 11f, BaseMonoPt = 11f") && sf.Contains("Font = Theme.Ui(BaseUiPt);")
                    && dp.Contains("fBody = MakeFont(UiFamilies, 11f, FontStyle.Regular);")
                    && sp.Contains("new float[] { 15f, 12f, 10.5f, 11f, 11f }")
                    && tf.Contains("new TutorialFonts(uiFamily, monoFamily, 1f)") && ft.Contains("new TutorialFonts(uiFamily, monoFamily, 1f)")
                    && th.Contains("sf = Ui(11f), vf = Mono(11f)") && th.Contains("tf = FontFor(title, 13f, FontStyle.Bold, true);"); // v5.1 (W30): 13 pt title, UI font when Chinese
                // the dashboard layout at zoom 1: same zoom for every window size (fonts never depend on the size)
                DashLayout L = new DashLayout();
                bool same = true;
                foreach (int w in new[] { 400, 958, 1500, 2160, 3840 })
                    foreach (double s in new[] { 1.0, 1.5, 2.0 })
                    { L.Arrange(w, w * 2 / 3, s, 1.0, DashMetrics.Estimate(s)); if (L.Zoom != 1.0) same = false; }
                Check("V431-W11-2 one fixed text size for every window size: settings 11 pt, tutorials scale 1 (body 13 pt), dashboard body 11 pt, 外观 body 11 pt, header 13/11 pt; dashboard zoom 1 at 400..3840 px x 100/150/200 %",
                    ok && same, "src=" + ok + " zoom=" + same);
            }

            // W11-3 Alt+Tab: no WS_EX_COMPOSITED, no OnActivated work, no layout while minimised, dashboard keeps its pictures
            {
                string bad = "";
                foreach (string f in Directory.GetFiles(dir, "*.cs"))
                {
                    string s = File.ReadAllText(f);
                    if (s.Contains("0x02000000") || s.Contains("|= WS_EX_COMPOSITED")) bad += Path.GetFileName(f) + ":composited ";
                }
                string sf = W11Src("SettingsForm.cs"), dp = W11Src("DashboardPanel.cs"), pg = W11Src("SettingsForm.PageGuide.cs");
                if (sf.Contains("override void OnActivated")) bad += "SettingsForm.OnActivated ";
                if (!sf.Contains("protected override void OnLayout(LayoutEventArgs e)")) bad += "no-OnLayout-skip ";
                int sl = dp.IndexOf("private void Sleep()", StringComparison.Ordinal);
                int se = sl < 0 ? -1 : dp.IndexOf("\n        }\n", sl, StringComparison.Ordinal);
                if (sl < 0 || se < 0 || dp.Substring(sl, se - sl).Contains("ReleaseCaches")) bad += "Sleep-frees-caches ";
                if (Regex.IsMatch(pg, @"OnGotFocus\(EventArgs e\) \{ base\.OnGotFocus\(e\); Invalidate\(\); \}")) bad += "guide-full-invalidate ";
                bool skip = LayoutSkip.Minimised(true, 1000, 700) && LayoutSkip.Minimised(false, 0, 0) && LayoutSkip.Minimised(false, 100, 0)
                    && !LayoutSkip.Minimised(false, 1000, 700);
                Check("V431-W11-3 Alt+Tab back: no WS_EX_COMPOSITED (page-sized scroll host), no work in OnActivated, no layout pass while minimised (restore = repaint, no relayout), the dashboard keeps its frame + static pictures while asleep (no rebuild on wake), the 指南 repaints on focus only for a focus cue",
                    bad == "" && skip, bad + " skip=" + skip);
            }

            // W11-4 grids: every multi-column grid is filled at fixed cells (no flow that skips hidden cells)
            {
                string bad = "";
                foreach (string f in Directory.GetFiles(dir, "SettingsForm*.cs"))
                {
                    string s = File.ReadAllText(f);
                    foreach (Match m in Regex.Matches(s, @"(\w+)\s*=\s*Grid\((\d+)\)"))
                    {
                        if (int.Parse(m.Groups[2].Value) < 2) continue;
                        string v = m.Groups[1].Value;
                        int end = s.IndexOf("\n        }\n", m.Index, StringComparison.Ordinal);
                        string body = s.Substring(m.Index, (end < 0 ? s.Length : end) - m.Index);
                        foreach (Match a in Regex.Matches(body, @"\b" + v + @"\.Controls\.Add\(([^;]*)\);"))
                            if (a.Groups[1].Value.Split(',').Length < 3) bad += Path.GetFileName(f) + ":" + v + " ";
                    }
                }
                string sf = W11Src("SettingsForm.cs");
                bool cells = sf.Contains("g.Controls.Add(cs[i], i, r);") && sf.Contains("Cells(g, all);");
                Check("V431-W11-4 every multi-column settings grid is filled at fixed cells (Row / AdvRow / Cells / Wide): a hidden cell leaves its cell empty instead of sliding later cells into the wrong row (换手 grid on Windows 2160x1440 @200 %)",
                    bad == "" && cells, bad + " cells=" + cells);
            }
        }
    }
}
