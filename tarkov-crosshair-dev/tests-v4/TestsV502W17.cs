// v5.0.2 (W17): UI speed + idle cost (decision 5 + Leo's 11:25 steering) and the UI review items in W17's files.
// Pure logic (UiSpeed.cs, StartupWatch.cs, DashboardLogic.cs, GuideDoc) + source checks; Windows timings are measured
// separately with the Mono probe (V502-W17.md) and are UNVERIFIED on Windows.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V502W17Init() { V502W17Tests(); }

        static string W17Src(string name) { return File.ReadAllText(Path.Combine(UiSrcDir(), name)); }

        static string W17Between(string s, string from, string to)
        {
            int a = s.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return "";
            int b = s.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            return b > a ? s.Substring(a, b - a) : s.Substring(a);
        }

        /// <summary>Called from Main (threads must not run inside a module initializer: they would wait for it).</summary>
        static void V502W17ThreadTests()
        {
            // W17-1 settings.json off the UI thread: newest snapshot wins, the writer is another thread, Flush waits
            {
                object gate = new object();
                List<string> written = new List<string>();
                int callerThread = Thread.CurrentThread.ManagedThreadId, writerThread = -1;
                ManualResetEvent hold = new ManualResetEvent(false);
                SettingsSaver<string> sv = new SettingsSaver<string>(delegate(string v)
                {
                    hold.WaitOne(2000);
                    lock (gate) { written.Add(v); writerThread = Thread.CurrentThread.ManagedThreadId; }
                    return v == "bad" ? "disk full" : null;
                });
                string lastErr = "unset";
                sv.Request("a", delegate(string v, string e) { lastErr = e; });
                Thread.Sleep(50);                              // "a" is being written (held)
                sv.Request("b", null); sv.Request("c", null); sv.Request("d", delegate(string v, string e) { lastErr = "d:" + e; });
                bool notIdle = !sv.Idle;
                hold.Set();
                bool flushed = sv.Flush(3000);
                string got; lock (gate) got = string.Join(",", written);
                sv.Request("bad", delegate(string v, string e) { lastErr = e; });
                sv.Flush(3000);
                Check("W17-1 SettingsSaver: writes on its own thread, a newer snapshot replaces a queued one (a,d), Flush waits, errors reach Done",
                    notIdle && flushed && got == "a,d" && writerThread != callerThread && lastErr == "disk full" && sv.Idle,
                    "written=" + got + " flushed=" + flushed + " err=" + lastErr);
            }

            // W17-4 UI watchdog sleeps (0 wake-ups) once startup is done and no settings window is open; startup always watched
            {
                int posts = 0;
                bool done = false;
                UiWatchdog w = new UiWatchdog();
                w.IntervalMs = 10; w.ThresholdMs = 100000;
                w.StartupFinished = delegate { return done; };
                w.Post = delegate(Action a) { Interlocked.Increment(ref posts); a(); };
                w.Log = delegate(string s) { };
                System.Diagnostics.Stopwatch wc = System.Diagnostics.Stopwatch.StartNew();
                w.Now = delegate { return wc.ElapsedMilliseconds; };       // no StartupClock (module initializer: no static init on the thread)
                w.CurrentPhase = delegate { return "test"; };
                w.CurrentPhaseAt = delegate { return 0L; };
                w.SetIdle(true);                         // before startup is done: refused
                bool refused = !w.Idle;
                w.Start();
                Thread.Sleep(150);
                int running = posts;
                done = true;
                w.SetIdle(true);
                Thread.Sleep(40);
                int p0 = posts;
                Thread.Sleep(300);
                int idlePosts = posts - p0;
                w.SetIdle(false);
                Thread.Sleep(150);
                int resumed = posts - p0 - idlePosts;
                w.Stop();
                string prog = W17Src("Program.cs");
                bool wired = prog.Contains("Program.Watchdog.SetIdle(true);") && prog.Contains("Program.Watchdog.SetIdle(false);")
                    && W17Between(prog, "private void OnFormClosed(SettingsForm f)", "public void OnFormHidden()").Contains("Program.Watchdog.SetIdle(true);");
                Check("W17-4 watchdog: pings while starting / window open, none while idle (300 ms: 0 posts), resumes on SetIdle(false); App wires it",
                    refused && running > 3 && idlePosts == 0 && resumed > 3 && wired, "running=" + running + " idle=" + idlePosts + " resumed=" + resumed + " wired=" + wired);
            }

        }

        static void V502W17Tests()
        {
            Console.WriteLine("-- v5.0.2 UI speed / idle / UI review (W17)");

            // W17-2 ForegroundState: events only on a change; -1 (unknown) keeps the state
            {
                ForegroundState.Reset();
                List<bool> ev = new List<bool>();
                ForegroundState.Changed += delegate(bool o) { ev.Add(o); };
                ForegroundState.Set(1); ForegroundState.Set(0); ForegroundState.Set(0); ForegroundState.Set(-1);
                bool afterUnknown = !ForegroundState.Ours;
                ForegroundState.Set(1);
                string seq = string.Join(",", ev);
                ForegroundState.Reset();
                Check("W17-2 ForegroundState: Set(1) when already in front = no event; 0 -> one event; -1 keeps it; 1 -> back", seq == "False,True" && afterUnknown, seq);
            }

            // W17-3 crash.log rotation at 1 MB (AUDIT F17)
            {
                string dir = Path.Combine(Path.GetTempPath(), "w17rot-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string log = Path.Combine(dir, "crash.log"), old = Path.Combine(dir, LogRotation.OldName);
                File.WriteAllText(log, "small");
                bool small = !LogRotation.RotateIfBig(log) && File.Exists(log) && !File.Exists(old);
                File.WriteAllText(old, "older");
                File.WriteAllBytes(log, new byte[LogRotation.MaxBytes + 10]);
                bool big = LogRotation.RotateIfBig(log) && !File.Exists(log) && new FileInfo(old).Length == LogRotation.MaxBytes + 10;
                bool missing = !LogRotation.RotateIfBig(log);
                try { Directory.Delete(dir, true); } catch (Exception) { }
                string prog = W17Src("Program.cs");
                bool wired = W17Between(prog, "private static void LogLoop()", "while (true)").Contains("LogRotation.RotateIfBig(");
                Check("W17-3 crash.log over 1 MB -> crash.old.log (replacing the older one) at the log writer's start; small / missing file untouched",
                    small && big && missing && wired, "small=" + small + " big=" + big + " wired=" + wired);
            }

            // W17-5 Program.cs: background GC only (AUDIT F2), multicore JIT (F14), balloon text (UI M3), tray menu prebuilt,
            // background save, exit writes synchronously, start message without MessageBox (UI L7)
            {
                string p = W17Src("Program.cs");
                string trim = W17Between(p, "internal static class MemoryTrim", "internal sealed class App");
                string d = "";
                if (trim.Contains("GC.Collect();") || trim.Contains("WaitForPendingFinalizers") || !trim.Contains("GC.Collect(2, GCCollectionMode.Forced, false)")) d += " F2";
                if (!p.Contains("ProfileOptimization.SetProfileRoot(SettingsIO.UserDir())") || !p.Contains("StartProfile(\"startup.jitprofile\")")) d += " F14";
                if (!p.Contains("GCLatencyMode.SustainedLowLatency")) d += " latency";
                if (p.Contains("已停止") || !p.Contains("准星绘制连续出错，正在重试。详细信息在 crash.log，联系方式在设置的「反馈」页。")) d += " M3";
                if (!W17Between(p, "private void AfterUiUp()", "t.Start();").Contains("trayMenu.Handle")) d += " tray";
                string save = W17Between(p, "public void SaveNow()", "private void SaveDone(");
                if (!save.Contains("saver.Request(") || !save.Contains("Settings.Clone(true)") || save.Contains("SettingsIO.Save(Settings")) d += " bgsave";
                if (!W17Between(p, "public void ExitApp(bool closeForm)", "tray.Visible = false;").Contains("SaveNowSync();")) d += " exitsave";
                if (W17Between(p, "string startMsg = loadMsg;", "bool splashShown").Contains("MessageBox.Show")) d += " L7";
                if (!p.Contains("form.DisplaysChanged()") || p.Contains("form.RefreshMonitors()")) d += " monitors";
                if (!p.Contains("public const string Version = \"v5.2\";")) d += " version"; // v5.1 (W25)
                Check("W17-5 Program.cs: one background GC, multicore JIT, 正在重试 balloon, tray menu prebuilt, settings saved off the UI thread (exit: sync), themed start message, v5.0.2",
                    d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // W17-6 SettingsForm: page bodies are built on first visit (decision 5 / AUDIT F13), measured, themed and filled then
            {
                string s = W17Src("SettingsForm.cs");
                string ctor = W17Between(s, "public SettingsForm(App app)", "private void EnsureBuilt(int index)");
                string d = "";
                if (Regex.IsMatch(ctor, @"AddPage\(\d+, Build\w+\(\)")) d += " ctor calls a page builder";
                foreach (string b in new string[] { "BuildDashboard", "BuildKeys", "BuildGeneral", "BuildRecoil", "BuildMovement", "BuildLean", "BuildHand", "BuildJump", "BuildStyle", "BuildFilter", "BuildGuide", "BuildFeedback" })
                    if (!ctor.Contains(", " + b + ", ")) d += " no lazy " + b;
                if (!ctor.Contains("EnsureBuilt(0);")) d += " dashboard";
                string eb = W17Between(s, "private void EnsureBuilt(int index)", "private bool InAnyPage(");
                foreach (string w in new string[] { "pg.Build()", "PageBuildTimes.Add(", "Program.Log(PageBuildTimes.Line(", "ApplyAdvancedPage(index)", "FitControlHeights()", "FitComboHeights(pg.Host)", "PaintHandRows()" })
                    if (!eb.Contains(w)) d += " EnsureBuilt lacks " + w;
                string sp = W17Between(s, "private void ShowPage(int index)", "public void JumpToPage(");
                if (!sp.Contains("EnsureBuilt(index);")) d += " ShowPage";
                string tog = W17Between(s, "private void ToggleAdvanced()", "private void ApplyAdvancedPage(");
                if (!tog.Contains("!InAnyPage(i)") || tog.Contains("pages[pages.Count - 1].RefEnd")) d += " toggle ranges";
                if (!W17Between(s, "private void SizeToScreen()", "protected override void OnVisibleChanged").Contains("height = Math.Max(height, maxH);")) d += " OnLoad"; // v5.1 (W25): SizeToScreen
                if (!s.Contains("public void DisplaysChanged()")) d += " DisplaysChanged";
                Check("W17-6 settings window: only shells + 首页 in the constructor; every other page built on first ShowPage, timed (crash.log), themed and filled",
                    d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // W17-7 nothing ticks while another program is in front / the window is closed (Leo 11:25): pause reasons combined,
            // tutorial timer follows ForegroundState, splash ends on deactivate, no polling timer added
            {
                string s = W17Src("SettingsForm.cs"), t = W17Src("TutorialForm.cs"), sp = W17Src("Splash.cs"), th = W17Src("Theme.cs");
                string d = "";
                string sync = W17Between(s, "private void SyncPause()", "private void ForegroundTracking()");
                if (!sync.Contains("frozen || wheelPaused || background") || !sync.Contains("if (want == treePaused")) d += " SyncPause";
                if (W14Count(s, "Theme.PauseTree(") != 1) d += " PauseTree calls=" + W14Count(s, "Theme.PauseTree(");
                if (!W17Between(s, "private void ForegroundTracking()", "private Action<bool> foregroundHandler;").Contains("saveFlash.Stop()")) d += " saveFlash";
                if (!s.Contains("ForegroundState.Changed -= foregroundHandler")) d += " unsubscribe";
                string ts = W17Between(t, "private void SyncTimer()", "internal bool TimerRunning");
                if (!ts.Contains("ForegroundState.Ours") || !ts.Contains("timer.Stop()")) d += " tutorial";
                if (W17Between(t, "protected override void OnShown(EventArgs e)", "private Action<bool> foregroundHandler;").Contains("timer.Start();")) d += " tutorial starts timer directly";
                if (!t.Contains("ForegroundState.Changed -= foregroundHandler")) d += " tutorial unsubscribe";
                if (!W17Between(sp, "protected override void OnDeactivate(EventArgs e)", "private void Step()").Contains("Finish()")) d += " splash";
                if (!sp.Contains("DrawImageUnscaled(backdrop, 0, 0)") || sp.Contains("using (Font tf = Theme.Mono(17f")) d += " splash frame cost";
                string tf = W17Between(th, "public static void TrackForeground(Form f)", "/// <summary>Header bar");
                if (tf.Contains("Timer") || !tf.Contains("f.Deactivate +=") || !tf.Contains("ForegroundIsOurs()")) d += " TrackForeground";
                Check("W17-7 no timers / repaints with the game in front: window pause (drag | wheel | background), tutorial timer, splash, event-driven foreground",
                    d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // W17-8 UI review items in W17's files (M4, M5, M7, L2, L3, L5, L7, L10, L11, M2 hook)
            {
                string s = W17Src("SettingsForm.cs"), fb = W17Src("SettingsForm.PageFeedback.cs"), th = W17Src("Theme.cs");
                string d = "";
                if (s.Contains("鼠标设置保持") || !s.Contains("游戏分辨率、画面比例、FOV 和调参基准保持现在的，「Leo默认」也会保留。")) d += " M4";
                if (s.Contains("给这套偏移起个名字") || !s.Contains("给这套配置起个名字，例如 M4 / AK")) d += " L3";
                if (!s.Contains("ok.Padding = new Padding(u / 2, 0, u / 2, 0);") || !s.Contains("no.Padding = ok.Padding;")) d += " L11";
                if (W17Between(s, "private Label TagLabel(string text, Color c)", "private void Tip(").Contains("monoFont")) d += " L5";
                string ch = W17Between(s, "private int ControlH()", "private int lineH;");
                if (!ch.Contains("probe.PreferredHeight") || !s.Contains("Theme.MatchComboHeight(cb, h)") || !th.Contains("public static bool MatchComboHeight(ComboBox cb, int height)")) d += " M7";
                if (!W17Between(s, "private void RefreshAdvanced()", "private void ToggleAdvanced()").Contains("!manualFolds.Contains(f)")
                    || !W17Between(s, "private void ApplyAdvancedPage(int index)", "private bool refitPosted;").Contains("!manualFolds.Contains(folds[i])")) d += " M2";
                if (!fb.Contains("b.Font = Font; b.MinimumSize")) d += " L2";
                if (fb.Contains("MessageBox.Show")) d += " L7";
                // M5: a 3-part version is one number run
                List<GuideDoc.Run> runs = GuideDoc.Runs("升级（从旧版本换到 v5.0.2）");
                string num = "";
                foreach (GuideDoc.Run r in runs) if (r.Kind == GuideDoc.RNum) num += "[" + "升级（从旧版本换到 v5.0.2）".Substring(r.Start, r.Length) + "]";
                if (num != "[5.0.2]" && num != "[v5.0.2]") d += " M5 runs=" + num;
                List<GuideDoc.Run> r2 = GuideDoc.Runs("v4.1 和 380 ms");
                string n2 = ""; foreach (GuideDoc.Run r in r2) if (r.Kind == GuideDoc.RNum) n2 += "[" + "v4.1 和 380 ms".Substring(r.Start, r.Length) + "]";
                if (n2 != "[v4.1][380 ms]" && n2 != "[4.1][380 ms]") d += " M5 two-part=" + n2;
                Check("W17-8 UI review: welcome text (M4), 3-part version one number (M5), line heights (M7), feedback buttons (L2), 配置 (L3), UI font tags (L5), dialogs (L7, L11), manual folds (M2)",
                    d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // W17-9 dashboard mouse names (UI L4) and the short header hint (UI L16)
            {
                KeyBindings k = new KeyBindings();
                bool a1, a2;
                string l = DashKeyNames.Cap(k, 7, out a1), r = DashKeyNames.Cap(k, 8, out a2);
                string full = DashKeyNames.HintLine(k), shortH = DashKeyNames.HintLineShort(k);
                string logs = string.Join(",", DashKeyNames.LogNames(k));
                bool ok = l == "左键" && !a1 && r == "右键" && !a2 && logs.Contains("左键,右键")
                    && shortH == "W A S D 移动 · Q / E 侧身 · Space 跳跃 · 左键 开火 · 右键 开镜" && shortH.Length < full.Length && !logs.Contains("LMB");
                // every mouse binding + modifiers: log names never longer than MaxLogName
                bool lenOk = true;
                for (int vk = 1; vk <= 6; vk++)
                    for (int m = 0; m < 8; m++)
                    {
                        KeyBindings kb = new KeyBindings();
                        kb.Set(KeyBindings.ActionFire, vk); kb.SetMods(KeyBindings.ActionFire, m);
                        foreach (string n in DashKeyNames.LogNames(kb)) if (n.Length > DashKeyNames.MaxLogName) lenOk = false;
                    }
                Check("W17-9 dashboard: 左键 / 右键 / 侧键 on caps, terminal and try lines (L4); a short header hint for narrow windows (L16); log names <= 12",
                    ok && lenOk, "caps=" + l + "/" + r + " logs=" + logs + " short=" + shortH + " len=" + lenOk);
            }

            // W17-10 tutorial wording and fields (UI L1, L8, L10)
            {
                string d = "";
                foreach (string f in Directory.GetFiles(UiSrcDir(), "Tutorial*.cs"))
                {
                    string t = File.ReadAllText(f);
                    foreach (string bad in new string[] { "X 轴", "Y 轴", "：X\"", "：Y\"", "X 不推荐改", "Y 不推荐改", "画一个圈" })
                        if (t.Contains(bad)) d += " " + Path.GetFileName(f) + ":" + bad;
                }
                string tf = W17Src("TutorialForm.cs");
                if (!W17Between(tf, "private void PlaceFields()", "private void BuildFields(").Contains("nb.Top = (int)Math.Round(r.Y + (r.Height - nb.Height) / 2f);")) d += " L8";
                if (!W17Src("TutorialStep6.cs").Contains("\"D：左右\"") || !W17Src("TutorialStep7.cs").Contains("\"W：上下\"")) d += " fields";
                Check("W17-10 tutorial says 左右 / 上下 like the pages (L10), 准星 not 圈 (L1), number boxes centred in their slot (L8)", d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // W17-11 README = 说明 README = 指南: v5.0.2, one admin line, 显示器硬件 closed by default, crash.old.log, 430 ms, no ring default
            {
                string root = Path.Combine(UiSrcDir(), "..");
                string a = File.ReadAllText(Path.Combine(root, "README.txt")), b = File.ReadAllText(Path.Combine(root, "说明 README.txt"));
                string g = GuideText.Text.Replace("\r", "");
                string d = "";
                if (a != b) d += " README differs";
                if (g != a.Replace("\r", "").TrimStart('\uFEFF')) d += " GuideText differs (gen-guide.py)";
                string first = a.TrimStart('\uFEFF').Split('\n')[0];
                if (!first.Contains("v5.2")) d += " title"; // v5.1 (W25)
                if (a.Contains("v5.0.1")) d += " v5.0.1 left";
                int admin = 0; foreach (string line in a.Split('\n')) if (line.Contains("管理员")) admin++;
                if (admin != 1) d += " admin lines=" + admin;
                foreach (string must in new string[] { "「显示器硬件」（默认收起，点开后才检测显示器）", "crash.old.log", "推荐是左右、前后都用 430 ms（和内置「Leo默认」一样）", "计划任务", "1. 双击「准星 TarkovHipAimer.exe」，会先出现「首次设置向导」" })
                    if (!a.Contains(must)) d += " missing「" + must + "」";
                foreach (string bad in new string[] { "经典圆环（默认）", "画一个圈", "圈就抬", "让圈", "圈会变成", "只有出错时才有", "380 ms「慢起」" })
                    if (a.Contains(bad)) d += " has「" + bad + "」";
                Check("W17-11 README × 2 = 指南: v5.0.2, one admin line, 显示器硬件 默认收起, crash.old.log, 430 ms 推荐, scheduled-task autostart, 准星 wording",
                    d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // W17-12 page-build log line format
            {
                string line = PageBuildTimes.Line("外观", 12, 3);
                PageBuildTimes.Add("style", 15);
                bool has = false; foreach (KeyValuePair<string, long> kv in PageBuildTimes.Snapshot()) if (kv.Key == "style" && kv.Value == 15) has = true;
                Check("W17-12 crash.log line per page: 「启动耗时 设置页「外观」: 第一次打开时创建 12 ms，填值和排版 3 ms」", line == "启动耗时 设置页「外观」: 第一次打开时创建 12 ms，填值和排版 3 ms" && has, line);
            }
        }
    }
}
