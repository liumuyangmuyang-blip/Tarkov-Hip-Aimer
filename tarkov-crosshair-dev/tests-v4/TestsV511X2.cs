// v5.1.1 (X2, Leo F6): background pre-compile (PreJit), the splash on its own UI thread, the background page build in
// slices, the CI layout probe's PowerShell name clash, and the 5 tests that failed only on Windows (CI run 44729023467).
// Pure parts + source checks (WinForms timing itself is measured by the Windows CI: probe modes timing / timingjit).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X2Init() { V511X2Tests(); }

        static string X2Dir(string sub)
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string c = Path.Combine(d, sub);
                if (Directory.Exists(c)) return c;
                d = Path.GetDirectoryName(d.TrimEnd('/', '\\'));
            }
            return null;
        }

        static string X2Src(string file)
        {
            string c = X2Dir(Path.Combine("tarkov-crosshair-v4", "src"));
            return File.ReadAllText(Path.Combine(c ?? "/home/dev/tarkov-crosshair-v4/src", file));
        }

        static string X2Between(string s, string from, string to)
        {
            int a = s.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return "";
            int b = s.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? s.Substring(a) : s.Substring(a, b - a);
        }

        static void V511X2Tests()
        {
            // X2-1 PreJit order: the settings window first, its pages next, tutorials last; lambda classes go with their owner
            {
                bool order = PreJit.Rank("TarkovCrosshair.SettingsForm") == 0 && PreJit.Rank("TarkovCrosshair.SettingsForm+<>c__DisplayClass12") == 0
                    && PreJit.Rank("TarkovCrosshair.Theme") == 0 && PreJit.Rank("TarkovCrosshair.StylePanel") == 1
                    && PreJit.Rank("TarkovCrosshair.DashboardPanel") == 1 && PreJit.Rank("TarkovCrosshair.FilterControls") == 2
                    && PreJit.Rank("TarkovCrosshair.WizardForm") == 2 && PreJit.Rank("TarkovCrosshair.Settings") == 3
                    && PreJit.Rank("TarkovCrosshair.TutorialForm") == 4 && PreJit.Rank("TarkovCrosshair.FilterTutorialForm") == 4
                    && PreJit.Rank(null) == 3 && PreJit.IsTutorial("TarkovCrosshair.TutorialStep3") && !PreJit.IsTutorial("TarkovCrosshair.FilterControls");
                Check("X2-1 PreJit order: settings window + Theme + TextFit first, pages next, other code, tutorials last; nested lambda classes ranked with their owner",
                    order, "settings=" + PreJit.Rank("TarkovCrosshair.SettingsForm") + " style=" + PreJit.Rank("TarkovCrosshair.StylePanel") + " tut=" + PreJit.Rank("TarkovCrosshair.TutorialForm"));
            }

            // X2-2 PreJit.Run compiles real methods without running any of them, never throws, skips tutorials on request
            {
                int before = V511X2Ran;
                List<string> lines = new List<string>();
                PreJit.Run(typeof(PreJit).Assembly, true, delegate(string s) { lines.Add(s); });
                bool ran = V511X2Ran == before; // the marker method below was compiled, not called
                bool ok = PreJit.Compiled > 500 && PreJit.Ms >= 0 && lines.Count == 1 && lines[0].StartsWith("启动耗时 后台预编译设置窗口代码: ")
                    && PreJit.Failed * 20 < PreJit.Compiled;
                bool nullSafe = true;
                try { PreJit.Start(null, false, null); } catch (Exception) { nullSafe = false; }
                Check("X2-2 PreJit.Run: " + PreJit.Compiled + " methods compiled in " + PreJit.Ms + " ms (" + PreJit.Failed + " skipped), nothing executed, one crash.log line, a null assembly is ignored",
                    ok && ran && nullSafe, "compiled=" + PreJit.Compiled + " failed=" + PreJit.Failed + " lines=" + lines.Count + " ran=" + ran + " line=" + (lines.Count > 0 ? lines[0] : ""));
            }

            // X2-3 source: PreJit started once the message loop runs; the splash on its own thread; the build starts at once
            {
                string p = X2Src("Program.cs"), sp = X2Src("Splash.cs"), sf = X2Src("SettingsForm.cs");
                string d = "";
                if (!X2Between(p, "private void AfterUiUp()", "StartPrebuild();").Contains("PreJit.Start(typeof(App).Assembly, UiPerf.On, Program.Log)")) d += " prejit";
                if (!p.Contains("Splash.ShowOnOwnThread(RingIcon, Settings.ActiveProfile,") || p.Contains("splash.Show();")) d += " thread";
                if (!X2Between(p, "Splash.ShowOnOwnThread(", "splashShown = true;").Contains("invoker.BeginInvoke(")) d += " marshal";
                if (!p.Contains("prebuildTimer.Interval = settingsAfterSplash ? 1 : 1500;")) d += " early";
                if (!sp.Contains("t.SetApartmentState(ApartmentState.STA);") || !sp.Contains("Application.Run(s);") || !sp.Contains("(Icon)icon.Clone()")) d += " sta";
                if (!X2Between(sp, "private void Step()", "private double lastOpacity").Contains("if (o != lastOpacity)")) d += " opacity";
                if (!X2Between(sp, "public static void ShowOnOwnThread(", "t.Start();").Contains("fire(); // also when")) d += " once";
                if (!sf.Contains("internal const int HandleSliceMs = 25;") || !X2Between(sf, "private void HandleSlice()", "MaxHandleSliceMs = sw.ElapsedMilliseconds;").Contains("sw.ElapsedMilliseconds < HandleSliceMs")) d += " slice";
                if (X2Between(sf, "public bool PrebuildStep()", "internal const int HandleSliceMs").Contains("CreateHandles(pg.Host)")) d += " whole-page-handles";
                Check("X2-3 F6: settings code pre-compiled in the background; splash on its own STA thread (icon copied, Done once, marshalled back), settings window built during the splash; splash sets Opacity only on change; page handles in 25 ms slices",
                    d == "", d);
            }

            // X2-4 CI scripts: no PowerShell variable reuses a param name in another case (names ignore case: probe.ps1:27
            // assigned a path to the validated -Shots parameter -> MetadataError, the layout probe never ran)
            {
                string ci = X2Dir(Path.Combine("tarkov-crosshair-dev", "ci")) ?? "/home/dev/tarkov-crosshair-dev/ci";
                string bad = ""; int files = 0;
                foreach (string f in Directory.GetFiles(ci, "*.ps1"))
                {
                    files++;
                    string s = File.ReadAllText(f);
                    Match m = Regex.Match(s, @"param\((.*?)\n\)", RegexOptions.Singleline);
                    if (!m.Success) continue;
                    string body = s.Substring(m.Index + m.Length);
                    foreach (Match v in Regex.Matches(m.Groups[1].Value, @"\$(\w+)"))
                    {
                        string n = v.Groups[1].Value;
                        foreach (Match a in Regex.Matches(body, @"\$(" + n + @")\s*=[^=]", RegexOptions.IgnoreCase))
                            if (a.Groups[1].Value != n) bad += " " + Path.GetFileName(f) + ":$" + a.Groups[1].Value;
                    }
                }
                string probe = File.ReadAllText(Path.Combine(ci, "probe.ps1"));
                Check("X2-4 CI scripts (" + files + "): no variable assigned under a parameter's name in another case (probe.ps1 $shots vs -Shots crashed the layout probe); probe runs timing + timingjit",
                    files > 3 && bad == "" && probe.Contains("$shotDir = Join-Path $Out 'shots'") && probe.Contains("'timing,timingjit,"), bad);
            }

            // X2-5 the 5 tests that failed on Windows only are written for both systems now (source check of the fixes)
            {
                string d = "";
                string dir = X2Dir("tests-v4") ?? "/home/dev/tarkov-crosshair-dev/tests-v4";
                string perf = File.ReadAllText(Path.Combine(dir, "TestsV4Perf.cs")), st = File.ReadAllText(Path.Combine(dir, "TestsV41Settings.cs"));
                string w14 = File.ReadAllText(Path.Combine(dir, "TestsV501W14.cs")), u = File.ReadAllText(Path.Combine(dir, "TestsV40.cs")), sf6 = File.ReadAllText(Path.Combine(dir, "TestsV4SettingsFix.cs"));
                if (!perf.Contains("return Math.Round(d, 6).ToString(\"R\"")) d += " perf";
                if (!st.Contains("OperatingSystem.IsWindows() ? \"C:\\\\Tarkov Hip")) d += " st19";
                if (!w14.Contains("W14TimeBeginPeriod(1)")) d += " w14";
                if (!u.Contains("(SettingsIO.UserDirFor(d, fe) ?? \"\").Replace('\\\\', '/')")) d += " u1";
                if (!sf6.Contains("r.Replace('\\\\', '/')")) d += " sf6";
                Check("X2-5 Windows-only failures fixed in the tests: PERF golden hash on 6-decimal values (same on Linux / Windows), ST19 Windows exe path, W14-4 1 ms timer like the app, U1 / SF6 compare with /",
                    d == "", d);
            }
        }

        static int V511X2Ran;
        static void V511X2Marker() { V511X2Ran++; }
    }
}
