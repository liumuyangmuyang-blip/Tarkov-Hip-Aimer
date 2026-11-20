using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TarkovCrosshair
{
    internal static class Program
    {
        public const string ProductName = "Tarkov Hip Aimer 塔科夫腰射准星 — by Leo";
        public const string ShortName = "Tarkov Hip Aimer";
        public const string Version = "v5.1";
        /// <summary>Chinese product name shown under the English brand (header, splash).</summary>
        public const string ChineseName = "塔科夫腰射准星";
        public const string MutexName = @"Local\TarkovHipAimer_Leo";
        public const string ShowEventName = @"Local\TarkovHipAimer_Leo_Show";
        private static readonly object logLock = new object();

        /// <summary>v4.2 滤镜: the running filter host, for the last-resort screen restore (fatal crash, Main finally).</summary>
        internal static FilterHost ActiveFilter;

        /// <summary>Last-resort: put the screen back (gamma, saturation, monitor values). Never throws; any thread.</summary>
        internal static void RestoreScreen()
        {
            FilterHost f = ActiveFilter;
            if (f == null) return;
            // v5.0.1: waits for the engine lock, which a slow driver call on the filter worker may hold: at most 3 s, on its own
            // thread, so a session end / crash on the UI thread can never hang here
            Thread t = new Thread(delegate() { try { f.StopNow(); } catch (Exception) { } });
            t.IsBackground = true;
            t.Name = "FilterRestoreNow";
            try { t.Start(); if (!t.Join(3000)) Log("filter: restore on exit did not finish within 3 s"); } catch (Exception) { }
        }

        /// <summary>v5.0.1: UI-thread watchdog (StartupWatch.cs); App points it at the UI thread once the invoker exists.</summary>
        internal static readonly UiWatchdog Watchdog = new UiWatchdog();

        /// <summary>Command-line switch used by the 开机自动启动 Run-key entry: tray only, no splash, no settings window.</summary>
        public const string AutostartArg = "--autostart";

        [STAThread]
        private static void Main(string[] args)
        {
            StartupClock.Mark("进程启动 → Main");
            // v5.0.2 (AUDIT F14): multicore JIT -- from the second start on, startup methods compile on another core.
            // One small file next to settings.json; never fails the start.
            try
            {
                System.Runtime.ProfileOptimization.SetProfileRoot(SettingsIO.UserDir());
                System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");
            }
            catch (Exception) { }
            // v5.0.2 (AUDIT F2): full collections run in the background while the crosshair draws
            try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; } catch (Exception) { }
            Watchdog.Start();
            bool autostart = false, updated = false;
            if (args != null)
                foreach (string a in args)
                {
                    if (string.Equals((a ?? "").Trim(), AutostartArg, StringComparison.OrdinalIgnoreCase)) autostart = true;
                    if (string.Equals((a ?? "").Trim(), UpdateInstaller.RestartArg, StringComparison.OrdinalIgnoreCase)) updated = true;
                }

            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            if (!created && updated)
            {
                // v5.1 (W28): started by the updater -- the old instance is still exiting; wait for it (max 15 s)
                try { created = mutex.WaitOne(15000); } catch (AbandonedMutexException) { created = true; }
            }
            StartupClock.Mark("单实例检查");
            if (!created)
            {
                // Second instance: ask the first one to show its settings window, then exit.
                // A second --autostart launch (sign-in start while already running) stays silent.
                if (autostart) { mutex.Close(); return; }
                try
                {
                    using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName)) ev.Set();
                }
                catch (Exception) { }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { ReportCrash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                RestoreScreen(); // v4.2: the process is going down -- 滤镜 off first (not in ThreadException: the app keeps running there)
                Exception ex = e.ExceptionObject as Exception;
                ReportCrash(ex != null ? ex : new Exception(Convert.ToString(e.ExceptionObject)));
            };

            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            try
            {
                App app = new App(showEvent, autostart);
                Application.Run(app);
                StartupClock.SetPhase("退出");
            }
            finally
            {
                // v4.3: the message loop has ended (no UI any more): wait for the filter worker to put the screen back
                // (ExitApp queued it); if the app never exited normally, restore directly.
                FilterHost fh = ActiveFilter;
                if (fh != null) { if (!fh.Join(5000)) RestoreScreen(); ActiveFilter = null; }
                showEvent.Close();
                mutex.ReleaseMutex();
                mutex.Close();
                Watchdog.Stop();
                FlushLog(1000);
            }
        }

        public static string BaseDir { get { return SettingsIO.UserDir(); } }

        /// <summary>v5.0.1: lines waiting for the log writer thread (a slow disk / antivirus scan of crash.log never blocks the
        /// UI or the overlay thread any more; v5.0 wrote the file under a lock on the calling thread).</summary>
        private static readonly Queue<string> logQueue = new Queue<string>();
        private static Thread logThread;
        private static bool logWriting;

        public static void Log(string text)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text + "\r\n";
                lock (logLock)
                {
                    if (logQueue.Count < 5000) logQueue.Enqueue(line);
                    if (logThread == null)
                    {
                        Thread t = new Thread(LogLoop);
                        t.IsBackground = true;
                        t.Name = "LogWriter";
                        logThread = t;
                        t.Start();
                    }
                    Monitor.PulseAll(logLock);
                }
            }
            catch (Exception) { }
        }

        private static void LogLoop()
        {
            // v5.0.2 (AUDIT F17): a crash.log over 1 MB becomes crash.old.log before the first line of this run
            LogRotation.RotateIfBig(Path.Combine(BaseDir, "crash.log"));
            while (true)
            {
                StringBuilder batch = new StringBuilder();
                lock (logLock)
                {
                    while (logQueue.Count == 0) { logWriting = false; Monitor.PulseAll(logLock); Monitor.Wait(logLock); }
                    logWriting = true;
                    while (logQueue.Count > 0) batch.Append(logQueue.Dequeue());
                }
                try { File.AppendAllText(Path.Combine(BaseDir, "crash.log"), batch.ToString(), Encoding.UTF8); }
                catch (Exception) { }
            }
        }

        /// <summary>Wait (max ms) until every queued log line is on disk (crash, exit).</summary>
        public static void FlushLog(int ms)
        {
            try
            {
                DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
                lock (logLock)
                {
                    while (logQueue.Count > 0 || logWriting)
                    {
                        int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                        if (left <= 0) return;
                        Monitor.Wait(logLock, left);
                    }
                }
            }
            catch (Exception) { }
        }

        private static void ReportCrash(Exception ex)
        {
            try { CrashStore.Save(SettingsIO.UserDir(), ex, Version); } catch (Exception) { } // v5.1 (W28): scrubbed copy, asked about later
            Log(ex.ToString());
            FlushLog(1000);
            try
            {
                MessageBox.Show("出错了：" + ex.Message + "\r\n下次打开时会问你要不要用邮件把错误记录发给作者。", ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }

    }

    /// <summary>
    /// v4.1 (Leo): after the settings window closed -- full GC (twice, finalizers in between, so disposed bitmaps / fonts are
    /// really freed). v4.3 (W7): the working-set trim (SetProcessWorkingSetSize -1 / -1) is gone: it paged out the overlay's
    /// own code and data too, and with the game using a lot of RAM those pages came back from disk on the next input, so
    /// the crosshair could start seconds late after alt-tab back (UNVERIFIED on Leo's PC). Windows trims on its own under
    /// memory pressure. Never throws.
    /// </summary>
    internal static class MemoryTrim
    {
        /// <summary>Private bytes and working set of this process (bytes; -1 when unknown).</summary>
        public static void Measure(out long privateBytes, out long workingSet)
        {
            privateBytes = -1; workingSet = -1;
            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
                {
                    p.Refresh();
                    privateBytes = p.PrivateMemorySize64;
                    workingSet = p.WorkingSet64;
                }
            }
            catch (Exception) { }
        }

        /// <summary>v5.0.2 (AUDIT F2): ONE background (non-blocking) gen-2 collection. v5.0.1 ran two blocking full GCs with a
        /// finalizer wait, which stopped every managed thread -- the crosshair's render thread too -- for 41-164 ms right after
        /// the settings window closed. Returns the ms this call took (the first run is logged once).</summary>
        public static long Run()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            try { GC.Collect(2, GCCollectionMode.Forced, false); }
            catch (Exception) { }
            return sw.ElapsedMilliseconds;
        }

        private static bool logged;

        /// <summary>Run + one crash.log line with the heap size (first trim of the session only).</summary>
        public static void RunAndLogOnce()
        {
            long before = GC.GetTotalMemory(false);
            long ms = Run();
            if (logged) return;
            logged = true;
            long pb, ws;
            Measure(out pb, out ws);
            Program.Log("内存: 设置窗口关闭后 后台回收 " + ms + " ms，托管堆 " + (before >> 20) + " MB -> " + (GC.GetTotalMemory(false) >> 20)
                + " MB，私有内存 " + (pb < 0 ? "?" : (pb >> 20).ToString()) + " MB");
        }
    }

    /// <summary>Lifecycle: settings, overlay thread, tray, settings window.</summary>
    internal sealed class App : ApplicationContext
    {
        public GlobalSettings Settings;
        /// <summary>v5.1 (W28): network (updates, stats, feedback, crash logs); null if it could not start.</summary>
        public NetHub net;
        /// <summary>True when this start created settings.json (first run): the settings window offers 「导入旧版设置」.</summary>
        public bool FirstRun;
        public readonly string SettingsPath;
        public readonly Overlay Overlay = new Overlay();
        public string SaveStatus = "";
        public event Action SaveStatusChanged;
        public readonly Icon RingIcon;
        /// <summary>v4.2 滤镜 (one engine in this process; data in the user folder next to settings.json).</summary>
        public readonly FilterHost Filter;
        /// <summary>Raised on the UI thread when the filter state changed (the settings window refreshes its 滤镜 page).</summary>
        public event Action FilterChanged;
        private readonly ToolStripMenuItem trayFilter;
        /// <summary>v5.0.2: the tray menu, built at start and its window handle created right after the message loop runs, so
        /// a right-click only shows an existing window (Leo: < 100 ms at any time).</summary>
        private readonly ContextMenuStrip trayMenu;

        /// <summary>v4.1 (Leo): the settings window exists only while it is open; closing it disposes it (and its caches),
        /// then GC (v4.3: no working-set trim), so in game only the overlay stays. null while closed.</summary>
        private SettingsForm form;
        /// <summary>Hidden control that marshals background-thread calls to the UI thread (the form may not exist).</summary>
        private readonly Control invoker;
        private readonly System.Windows.Forms.Timer trimTimer;
        private readonly Icon trayIcon;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem trayEnable;
        private readonly System.Windows.Forms.Timer saveTimer;
        private readonly EventWaitHandle showEvent;
        private int version;
        private int toggleAck;
        private bool exiting;
        private bool closeBalloonShown;
        private readonly EventHandler displayChanged;
        /// <summary>v4.3 (V43-PERF TOP 1 b): alt+tab out of a fullscreen game fires DisplaySettingsChanged 1-3 times; the work runs
        /// once, 500 ms after the last one. The filter part then runs on the filter worker (FilterCore), never on this thread.</summary>
        private readonly System.Windows.Forms.Timer displayTimer = new System.Windows.Forms.Timer();
        private readonly SessionEndedEventHandler sessionEnded;

        public App(EventWaitHandle showEvent, bool autostart)
        {
            this.showEvent = showEvent;
            SettingsPath = SettingsIO.DefaultPath();
            string loadMsg;
            SettingsIO.ScreenProbe = PrimaryScreenPixels; // first run: 游戏分辨率 = this screen's real pixels
            FirstRun = !File.Exists(SettingsPath); // no settings.json yet: offer 「导入旧版设置」 once the window is up
            Settings = SettingsIO.Load(SettingsPath, out loadMsg);
            UiPerf.Set(Settings.PerfMode);
            StartupClock.Mark("读取设置 settings.json" + (FirstRun ? "（第一次运行）" : ""));

            RingIcon = Theme.AppIcon();

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 500;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };

            invoker = new Control();
            IntPtr forceHandle = invoker.Handle; // so BeginInvoke works before (and without) a settings window
            GC.KeepAlive(forceHandle);
            Program.Watchdog.Post = delegate(Action a) { if (!invoker.IsDisposed) invoker.BeginInvoke(a); };
            // v5.1 (W28, decision 16): update check / stats / feedback / crash logs, all through NetHub (started in AfterUiUp)
            try
            {
                net = new NetHub(invoker, new DelegateNetSettings(
                        delegate { return Settings.NetUpdateCheck; }, delegate(bool v) { Settings.NetUpdateCheck = v; },
                        delegate { return Settings.NetStatsConsent; }, delegate(bool? v) { Settings.NetStatsConsent = v; },
                        delegate { return Settings.NetCrashAsk; }, delegate(bool v) { Settings.NetCrashAsk = v; },
                        delegate { Changed(); }),
                    new NetHttp(), Program.Version, Application.ExecutablePath, SettingsIO.UserDir(), Program.Log);
                net.Updates.RestartRequested += delegate { ExitApp(); };
            }
            catch (Exception ex) { Program.Log("net: init: " + ex.Message); net = null; }
            trimTimer = new System.Windows.Forms.Timer();
            trimTimer.Interval = 600;
            trimTimer.Tick += delegate
            {
                trimTimer.Stop();
                if ((form != null && !form.IsDisposed && form.Visible) || exiting) return;
                // Pictures only the settings previews needed (the overlay's own picture stays), then GC (v4.3 W7: no working-set trim).
                try { CustomCrosshair.TrimUiCaches(); } catch (Exception ex) { Program.Log("UI: trim caches: " + ex.Message); }
                MemoryTrim.RunAndLogOnce();
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("打开设置");
            open.Click += delegate { ShowSettings(); };
            trayEnable = new ToolStripMenuItem("启用准星");
            trayEnable.Click += delegate { SetEnabled(!Settings.Enabled); };
            trayFilter = new ToolStripMenuItem("启用滤镜");
            trayFilter.Click += delegate { if (Filter != null) Filter.Toggle(); };
            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { ExitApp(); };
            menu.Items.Add(open);
            menu.Items.Add(trayEnable);
            menu.Items.Add(trayFilter);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);

            tray = new NotifyIcon();
            trayIcon = new Icon(RingIcon, SystemInformation.SmallIconSize); // crisp 16 px tray icon
            tray.Icon = trayIcon;
            tray.ContextMenuStrip = menu;
            trayMenu = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Visible = true;
            UpdateTray();
            StartupClock.Mark("托盘图标");

            Overlay.Log = Program.Log;
            Overlay.ToggleRequested = delegate(int seq, bool val)
            {
                try { invoker.BeginInvoke((Action)delegate { OnOverlayToggle(seq, val); }); } catch (Exception) { }
            };
            Overlay.Fatal = delegate(string msg)
            {
                try
                {
                    invoker.BeginInvoke((Action)delegate
                    {
                        ShowBalloon("准星绘制连续出错，正在重试。详细信息在 crash.log，联系方式在设置的「反馈」页。", ToolTipIcon.Error);
                    });
                }
                catch (Exception) { }
            };
            Overlay.Start(BuildConfig());
            StartupClock.Mark("准星线程已启动");

            // v4.2 滤镜: same process, started after the overlay; off on first run (FilterPrefs.WasOn false), else as at last exit.
            Filter = new FilterHost(invoker, Program.BaseDir);
            Filter.CrosshairKeys = delegate { return Settings.Keys; };
            Filter.KeysAllowed = delegate
            {
                return !Filter.Prefs.KeysOnlyInGame || GameWindow.IsForeground(Settings.GameWindowTitle);
            };
            Filter.Notify = delegate(string text) { ShowBalloon(text, ToolTipIcon.Warning); };
            Filter.Changed += delegate
            {
                UpdateTray();
                Action h = FilterChanged;
                if (h != null) h();
            };
            Program.ActiveFilter = Filter;
            // v5.0.1: only the hotkey poll now; the engine (NVAPI / ADL, gamma, Magnification, DDC/CI) starts on a background
            // worker after the UI is up, and only if the filter was on at the last exit (or a crash left something to undo),
            // or on its first real use (tray 启用滤镜, filter hotkey, the 滤镜 page). See AfterUiUp.
            Filter.StartKeys();
            StartupClock.Mark("滤镜（推迟启动）");

            displayTimer.Interval = 500;
            displayTimer.Tick += delegate
            {
                displayTimer.Stop();
                if (exiting) return;
                if (form != null && !form.IsDisposed) form.DisplaysChanged(); // v5.0.2: only when 常规 has been built
                Publish();
                Filter.DisplaysChanged(); // queues only (FilterCore worker, debounced, max one re-apply per 5 s)
            };
            displayChanged = delegate
            {
                // may arrive on the SystemEvents thread: only (re)start the one-shot timer on the UI thread
                try { invoker.BeginInvoke((Action)delegate { if (exiting) return; displayTimer.Stop(); displayTimer.Start(); }); } catch (Exception) { }
            };
            SystemEvents.DisplaySettingsChanged += displayChanged;
            // Windows shutdown / sign-out while no settings window is open (v4.1: it is disposed when closed, so it can no
            // longer save on its own FormClosing): write the settings once. File write only -- this may run on another thread.
            sessionEnded = delegate
            {
                try { if (!exiting) { if (saver != null) saver.Flush(2000); SettingsIO.Save(Settings, SettingsPath); } } catch (Exception) { }
                Program.RestoreScreen(); // v4.2 滤镜: screen back before Windows ends the session (v5.0.1: max 3 s)
            };
            SystemEvents.SessionEnded += sessionEnded;
            StartupClock.Mark("系统事件");

            Thread waiter = new Thread(WaitForShowRequests);
            waiter.IsBackground = true;
            waiter.Name = "ShowRequestWaiter";
            waiter.Start();

            SyncAutostart(!autostart); // a sign-in start already runs the registered path: only read the state
            try { invoker.BeginInvoke((Action)AfterUiUp); } catch (Exception) { }

            if (autostart)
            {
                // Started at sign-in (Run key): overlay + tray only.
                if (loadMsg != null) ShowBalloon(loadMsg, ToolTipIcon.Warning);
                return;
            }
            string startMsg = loadMsg;
            Action afterSplash = delegate
            {
                if (exiting) return;
                if (Settings.ShowSettingsOnStart || FirstRun || !Settings.WizardDone) ShowSettings(); // v5.1: the wizard needs the window
                if (FirstRun && form != null && !form.IsDisposed)
                {
                    FirstRun = false;
                    try { form.OfferLegacyImport(); } catch (Exception ex) { Program.Log("UI: legacy import offer: " + ex); }
                }
                // v5.1 (W26): 首次设置向导 once (also for upgraded users: pre-filled with their values), after the import offer
                if (form != null && !form.IsDisposed && !Settings.WizardDone)
                {
                    SettingsForm wf = form;
                    try { SetupWizard.ShowIfNeeded(wf, Settings, Changed, delegate { wf.OpenTutorialFromWizard(); }); }
                    catch (Exception ex) { Program.Log("UI: wizard: " + ex); }
                    try { if (!wf.IsDisposed) wf.RefreshValues(); } catch (Exception) { }
                }
                if (startMsg != null)
                {
                    // v5.0.2 (UI L7): the window's own dialog look; a balloon when no settings window is open
                    if (form != null && !form.IsDisposed && form.Visible) ThemedDialog.Show(form, startMsg, false);
                    else ShowBalloon(startMsg, ToolTipIcon.Warning);
                }
            };
            bool splashShown = false;
            if (Splash.ShouldShow(Settings))
            {
                try
                {
                    // v5.1.1 (X2, Leo F6): the splash animates on its own UI thread, so the settings window can be built here
                    // meanwhile (StartPrebuild) without a single dropped splash frame; when it ends, afterSplash runs here.
                    splashRunning = true;
                    settingsAfterSplash = Settings.ShowSettingsOnStart || FirstRun || !Settings.WizardDone;
                    Splash.ShowOnOwnThread(RingIcon, Settings.ActiveProfile, delegate
                    {
                        try { invoker.BeginInvoke((Action)delegate { splashRunning = false; afterSplash(); }); }
                        catch (Exception ex) { Program.Log("splash done: " + ex.Message); }
                    });
                    splashShown = true;
                }
                catch (Exception ex) { splashRunning = false; Program.Log("splash: " + ex); }
            }
            if (!splashShown) afterSplash();
            StartupClock.Mark(splashShown ? "启动画面已显示" : "主窗口已显示");
        }

        /// <summary>v5.0.1: first message of the loop (the tray and the overlay already work). 1 s later the filter engine
        /// starts on its worker if it is wanted at all (FilterHost.StartIfWanted); nothing here waits for it.</summary>
        private void AfterUiUp()
        {
            StartupClock.Done("启动完成（消息循环在运行）");
            // v5.1.1 (X2): compile the settings window's code on a low-priority background thread (UiSpeed.cs PreJit)
            try { PreJit.Start(typeof(App).Assembly, UiPerf.On, Program.Log); } catch (Exception ex) { Program.Log("PreJit: " + ex.Message); }
            // v5.0.2: tray menu window + layout now (the first right-click used to create both)
            try
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                IntPtr mh = trayMenu.Handle;
                GC.KeepAlive(mh);
                trayMenu.PerformLayout();
                Program.Log("启动耗时 托盘菜单预先创建: " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex) { Program.Log("tray: prebuild: " + ex.Message); }
            // v5.0.2 (Leo: 0 wake-ups with the settings window closed): the UI watchdog sleeps until the window opens
            if (form == null || form.IsDisposed) Program.Watchdog.SetIdle(true);
            StartPrebuild();
            try
            {
                if (net != null)
                {
                    net.Start(); // background thread; first GitHub check 30 s later
                    // v5.1 (Leo 21 Nov 2026): no 统计 question -- anonymous stats are on by default, off in 常规
                }
            }
            catch (Exception ex) { Program.Log("net: start: " + ex.Message); }
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 1000;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                if (exiting || Filter == null) return;
                bool queued = Filter.StartIfWanted();
                Program.Log("启动耗时 滤镜引擎: " + (queued ? "已排到后台线程启动（上次退出时滤镜开着）" : "不启动（滤镜关着，用到时再启动）")
                    + "，启动后 " + StartupClock.Ms + " ms");
            };
            t.Start();
        }

        /// <summary>True while the start-up splash animates (the background build waits for it: no hitch in its frames).</summary>
        private bool splashRunning;
        /// <summary>v5.1.1: the settings window opens when the splash ends, so the background build starts at once (the splash
        /// has its own thread) instead of 1.5 s later.</summary>
        private bool settingsAfterSplash;
        private System.Windows.Forms.Timer prebuildTimer;
        private System.Diagnostics.Stopwatch prebuildClock;

        /// <summary>
        /// v5.1 (W25, decision 8 「设置秒开」): 1.5 s after the message loop runs (the crosshair is already drawing on its own
        /// thread), the settings window is created HIDDEN and its pages are built one per UI-thread slot (30 ms apart, so the
        /// tray and hotkeys stay responsive). A finite job: the timer is gone when every page is built. Waits while the splash
        /// animates or the window is being dragged. One crash.log line with the total.
        /// </summary>
        private void StartPrebuild()
        {
            prebuildTimer = new System.Windows.Forms.Timer();
            prebuildTimer.Interval = settingsAfterSplash ? 1 : 1500;
            prebuildTimer.Tick += delegate
            {
                prebuildTimer.Interval = 30;
                if (exiting) { StopPrebuild(null); return; }
                if (splashRunning && !settingsAfterSplash) return; // v5.1.1: the splash has its own thread now
                try
                {
                    if (form == null || form.IsDisposed)
                    {
                        prebuildClock = System.Diagnostics.Stopwatch.StartNew();
                        StartupClock.SetPhase("后台创建设置窗口");
                        SettingsForm f = new SettingsForm(this);
                        f.FormClosed += delegate { OnFormClosed(f); };
                        f.SeenVersion = changeCount;
                        form = f;
                        Program.Log("启动耗时 设置窗口后台创建: " + prebuildClock.ElapsedMilliseconds + " ms（启动后 " + StartupClock.Ms + " ms）");
                        StartupClock.SetPhase("空闲");
                        return;
                    }
                    if (prebuildClock == null) prebuildClock = System.Diagnostics.Stopwatch.StartNew();
                    if (!form.PrebuildStep()) StopPrebuild("全部设置页后台预先创建完成，共 " + prebuildClock.ElapsedMilliseconds + " ms（启动后 " + StartupClock.Ms + " ms）");
                }
                catch (Exception ex) { StopPrebuild("后台预先创建设置页出错（打开时再创建）: " + ex.Message); }
            };
            prebuildTimer.Start();
        }

        private void StopPrebuild(string logLine)
        {
            if (prebuildTimer == null) return;
            prebuildTimer.Stop();
            prebuildTimer.Dispose();
            prebuildTimer = null;
            if (logLine != null) Program.Log("启动耗时 " + logLine);
        }

        /// <summary>
        /// 开机自动启动 at start, in the background (moving an old v3.0 - v4.0 logon task over can take a moment): if the
        /// HKCU Run value points at another exe (the folder was moved), point it at this exe (Autostart.RefreshIfMoved); then
        /// mirror the real state (Autostart.IsEnabled) into Settings.Autostart on the UI thread.
        /// </summary>
        private void SyncAutostart(bool refresh)
        {
            string exe = Application.ExecutablePath;
            Thread t = new Thread(delegate()
            {
                bool on = false;
                try
                {
                    string err;
                    if (refresh && !Autostart.RefreshIfMoved(exe, out err)) Program.Log("autostart refresh: " + err);
                    on = Autostart.IsEnabled();
                }
                catch (Exception ex) { Program.Log("autostart: " + ex.Message); return; }
                try
                {
                    invoker.BeginInvoke((Action)delegate
                    {
                        if (exiting || Settings.Autostart == on) return;
                        Settings.Autostart = on;
                        if (form != null && !form.IsDisposed) form.RefreshValues();
                        Changed();
                    });
                }
                catch (Exception) { }
            });
            t.IsBackground = true;
            t.Name = "AutostartSync";
            t.Start();
        }

        private void WaitForShowRequests()
        {
            while (!exiting)
            {
                try
                {
                    showEvent.WaitOne();
                    if (exiting) return;
                    invoker.BeginInvoke((Action)ShowSettings);
                }
                catch (Exception) { return; }
            }
        }

        public void GetMonitorCenter(out double cx, out double cy)
        {
            Screen[] all = Screen.AllScreens;
            Screen s = (Settings.MonitorIndex >= 0 && Settings.MonitorIndex < all.Length) ? all[Settings.MonitorIndex] : Screen.PrimaryScreen;
            Rectangle b = s.Bounds;
            cx = b.Left + b.Width / 2.0;
            cy = b.Top + b.Height / 2.0;
        }

        private RenderConfig BuildConfig()
        {
            double cx, cy;
            GetMonitorCenter(out cx, out cy);
            version++;
            Profile active = Settings.Active;
            // v4.0: always live (no preview modes); the dashboard shows the live motion instead.
            return new RenderConfig(Settings.Clone(false), active.Clone(), cx, cy, PreviewMode.Live, 1.0, version, toggleAck);
        }

        /// <summary>Publish a new RenderConfig (no save).</summary>
        public void Publish()
        {
            if (exiting) return;
            Overlay.Publish(BuildConfig());
        }

        /// <summary>Model changed: publish now, save in 500 ms (debounced).</summary>
        public void Changed()
        {
            changeCount++;
            UiPerf.Set(Settings.PerfMode); // v5.1 性能模式 (常规 page): the UI follows at once, the crosshair is unaffected
            Publish();
            saveTimer.Stop();
            saveTimer.Start();
        }

        /// <summary>v5.0.2 (Leo: no file work on the UI thread): settings.json is written by a background thread. The UI
        /// thread only takes a snapshot (Clone with profiles); the status line follows when the file is on disk.</summary>
        private SettingsSaver<GlobalSettings> saver;

        public void SaveNow()
        {
            if (exiting) { SaveNowSync(); return; }
            if (saver == null) saver = new SettingsSaver<GlobalSettings>(delegate(GlobalSettings g) { return SettingsIO.Save(g, SettingsPath); });
            GlobalSettings snap;
            try { snap = Settings.Clone(true); }
            catch (Exception ex) { Program.Log("save: snapshot: " + ex.Message); SaveNowSync(); return; }
            saver.Request(snap, delegate(GlobalSettings done, string err)
            {
                try { invoker.BeginInvoke((Action)delegate { SaveDone(done, err); }); } catch (Exception) { }
            });
        }

        private void SaveDone(GlobalSettings snap, string err)
        {
            // the backup Load asked for was made by this save: later saves do not need it again (SettingsIO.Save rule)
            if (err == null && snap != null && snap.BackupBeforeSave == null) Settings.BackupBeforeSave = null;
            SaveStatus = err == null ? "已保存" : "保存失败：" + err;
            Action h = SaveStatusChanged;
            if (h != null) h();
        }

        /// <summary>Exit / Windows sign-out: wait for a background save in progress (max 2 s), then write now on this thread.</summary>
        public void SaveNowSync()
        {
            if (saver != null) { saver.DropPending(); saver.Flush(2000); }
            string err = SettingsIO.Save(Settings, SettingsPath);
            SaveStatus = err == null ? "已保存" : "保存失败：" + err;
            Action h = SaveStatusChanged;
            if (h != null) h();
        }

        private int changeCount;
        /// <summary>v5.1: bumped by every Changed() (any settings change); the hidden settings window refreshes on its next
        /// show only when this moved since it was hidden.</summary>
        public int ChangeCount { get { return changeCount; } }

        private void OnOverlayToggle(int seq, bool val)
        {
            Settings.Enabled = val;
            toggleAck = seq;
            if (form != null && !form.IsDisposed) form.RefreshEnabled();
            UpdateTray();
            Changed();
        }

        public void SetEnabled(bool val)
        {
            Settings.Enabled = val;
            if (form != null && !form.IsDisposed) form.RefreshEnabled();
            UpdateTray();
            Changed();
        }

        public void UpdateTray()
        {
            trayEnable.Checked = Settings.Enabled;
            if (trayFilter != null && Filter != null) trayFilter.Checked = Filter.IsOn;
            string t = Program.ShortName + " " + Program.Version + "（" + Settings.Keys.ComboOf(KeyBindings.ActionToggle) + " 开/关）";
            if (t.Length > 63) t = t.Substring(0, 63);
            tray.Text = t;
        }

        public void ShowBalloon(string text, ToolTipIcon icon)
        {
            if (tray == null || !tray.Visible) return;
            tray.ShowBalloonTip(5000, Program.ProductName, text, icon);
        }

        public void ShowSettings()
        {
            if (exiting) return;
            trimTimer.Stop();
            bool made = false;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            Program.Watchdog.SetIdle(false); // the window is UI work worth watching
            if (form == null || form.IsDisposed)
            {
                StartupClock.SetPhase("创建设置窗口");
                SettingsForm f = new SettingsForm(this);
                f.FormClosed += delegate { OnFormClosed(f); };
                form = f;
                made = true;
            }
            else if (!form.Visible && form.SeenVersion != changeCount)
            {
                // v5.1: the window was hidden (or prebuilt) and a hotkey / the tray / the filter changed something meanwhile
                try { form.RefreshValues(); } catch (Exception ex) { Program.Log("UI: refresh on show: " + ex.Message); }
            }
            long build = sw.ElapsedMilliseconds;
            StartupClock.SetPhase("显示设置窗口");
            form.TopMost = Settings.SettingsTopMost;
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            if (made) Program.Log("启动耗时 设置窗口: 创建 " + build + " ms，显示 " + (sw.ElapsedMilliseconds - build) + " ms（启动后 " + StartupClock.Ms + " ms）");
            else if (!shownOnce) Program.Log("启动耗时 设置窗口第一次显示（后台已创建）: " + sw.ElapsedMilliseconds + " ms（启动后 " + StartupClock.Ms + " ms）");
            shownOnce = true;
            StartupClock.SetPhase("空闲");
        }

        private bool shownOnce;

        /// <summary>The settings window closed (it disposes itself): forget it; a moment later GC + trim the working set.</summary>
        private void OnFormClosed(SettingsForm f)
        {
            if (form == f) form = null;
            if (exiting) { ExitThread(); return; }
            Program.Watchdog.SetIdle(true);
            trimTimer.Stop();
            trimTimer.Start();
        }

        /// <summary>Called when the user closes the settings window: explain once that the crosshair keeps running.</summary>
        public void OnFormHidden()
        {
            // v5.1: the window is only hidden now (decision 8): same idle state as after a v5.0.2 close
            Program.Watchdog.SetIdle(true);
            trimTimer.Stop();
            trimTimer.Start();
            if (!closeBalloonShown)
            {
                closeBalloonShown = true;
                ShowBalloon("准星还在后台运行，右下角托盘图标里可以打开设置或退出。", ToolTipIcon.Info);
            }
        }

        public bool Exiting { get { return exiting; } }

        /// <summary>
        /// First-run 游戏分辨率 probe: ScreenDetect (real pixels from Win32); if that finds nothing, Screen.PrimaryScreen.Bounds,
        /// which is in physical pixels too because the manifest makes the process DPI aware. null only if both fail
        /// (then Leo's 1920x1440 default stays).
        /// </summary>
        private static int[] PrimaryScreenPixels()
        {
            int[] px = null;
            try { px = ScreenDetect.PrimaryPixels(); } catch (Exception) { px = null; }
            if (px != null && px.Length >= 2 && px[0] > 0 && px[1] > 0) return px;
            try
            {
                Rectangle b = Screen.PrimaryScreen.Bounds;
                if (b.Width > 0 && b.Height > 0) return new int[] { b.Width, b.Height };
            }
            catch (Exception) { }
            return null;
        }

        public void ExitApp() { ExitApp(true); }

        /// <summary>closeForm=false when called from the form's own FormClosing (system shutdown etc.).</summary>
        public void ExitApp(bool closeForm)
        {
            if (exiting) return;
            exiting = true;
            try { SystemEvents.DisplaySettingsChanged -= displayChanged; } catch (Exception) { }
            displayTimer.Stop();
            try { SystemEvents.SessionEnded -= sessionEnded; } catch (Exception) { }
            try { if (form != null && !form.IsDisposed) form.CommitEdits(); } catch (Exception) { } // a number still being typed
            trimTimer.Stop();
            Overlay.Stop();
            try { Filter.Dispose(); } catch (Exception ex) { Program.Log("filter: exit: " + ex.Message); } // screen back, lock released
            // Program.ActiveFilter stays set: Main's finally joins the worker after the message loop (v4.3)
            saveTimer.Stop();
            SaveNowSync();
            try { showEvent.Set(); } catch (Exception) { } // release the waiter thread
            try { if (net != null) net.Dispose(); } catch (Exception) { } // v5.1 (W28): session-end heartbeat, max 1.3 s
            tray.Visible = false;
            tray.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
            SettingsForm f = form;
            if (f != null) f.AllowClose = true;
            // The form's FormClosed handler ends the message loop (ExitThread), so the loop never ends
            // while the form is still inside its own FormClosing. No window open: end it here.
            if (closeForm && f != null && !f.IsDisposed && !f.Disposing) f.Close();
            else if (closeForm) ExitThread();
        }
    }
}
