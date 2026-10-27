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
        public const string ProductName = "Tarkov Hip Aimer — by Leo";
        public const string ShortName = "Tarkov Hip Aimer";
        public const string Version = "v3.1";
        public const string MutexName = @"Local\TarkovHipAimer_Leo";
        public const string ShowEventName = @"Local\TarkovHipAimer_Leo_Show";
        private static readonly object logLock = new object();

        /// <summary>Command-line switch used by the 开机自动启动 logon task: tray only, no splash, no settings window.</summary>
        public const string AutostartArg = "--autostart";

        [STAThread]
        private static void Main(string[] args)
        {
            bool autostart = false;
            if (args != null)
                foreach (string a in args)
                    if (string.Equals((a ?? "").Trim(), AutostartArg, StringComparison.OrdinalIgnoreCase)) autostart = true;

            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                // Second instance: ask the first one to show its settings window, then exit.
                // A second --autostart launch (logon task while already running) stays silent.
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
                Exception ex = e.ExceptionObject as Exception;
                ReportCrash(ex != null ? ex : new Exception(Convert.ToString(e.ExceptionObject)));
            };

            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            try
            {
                App app = new App(showEvent, autostart);
                Application.Run(app);
            }
            finally
            {
                showEvent.Close();
                mutex.ReleaseMutex();
                mutex.Close();
            }
        }

        public static string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static void Log(string text)
        {
            try
            {
                lock (logLock)
                {
                    File.AppendAllText(Path.Combine(BaseDir, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n", Encoding.UTF8);
                }
            }
            catch (Exception) { }
        }

        private static void ReportCrash(Exception ex)
        {
            Log(ex.ToString());
            try
            {
                MessageBox.Show("出错了：" + ex.Message + "  详细信息在 crash.log，截图发给 Kiro。", ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }

    }

    /// <summary>Lifecycle: settings, overlay thread, tray, settings window.</summary>
    internal sealed class App : ApplicationContext
    {
        public GlobalSettings Settings;
        public readonly string SettingsPath;
        public readonly Overlay Overlay = new Overlay();
        public PreviewMode Preview = PreviewMode.Live;
        public double PreviewSpeed = 1.0;
        public string SaveStatus = "";
        public event Action SaveStatusChanged;
        public readonly Icon RingIcon;
        public readonly List<string> Weapons = new List<string>();

        private readonly SettingsForm form;
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

        public App(EventWaitHandle showEvent, bool autostart)
        {
            this.showEvent = showEvent;
            SettingsPath = SettingsIO.DefaultPath();
            string loadMsg;
            Settings = SettingsIO.Load(SettingsPath, out loadMsg);
            LoadWeapons();

            RingIcon = Theme.AppIcon();

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 500;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };

            form = new SettingsForm(this);
            form.FormClosed += delegate { if (exiting) ExitThread(); };
            IntPtr forceHandle = form.Handle; // so BeginInvoke works before the form is shown
            GC.KeepAlive(forceHandle);

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("打开设置");
            open.Click += delegate { ShowSettings(); };
            trayEnable = new ToolStripMenuItem("启用准星");
            trayEnable.Click += delegate { SetEnabled(!Settings.Enabled); };
            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { ExitApp(); };
            menu.Items.Add(open);
            menu.Items.Add(trayEnable);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);

            tray = new NotifyIcon();
            trayIcon = new Icon(RingIcon, SystemInformation.SmallIconSize); // crisp 16 px tray icon
            tray.Icon = trayIcon;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Visible = true;
            UpdateTray();

            Overlay.Log = Program.Log;
            Overlay.ToggleRequested = delegate(int seq, bool val)
            {
                try { form.BeginInvoke((Action)delegate { OnOverlayToggle(seq, val); }); } catch (Exception) { }
            };
            Overlay.Fatal = delegate(string msg)
            {
                try
                {
                    form.BeginInvoke((Action)delegate
                    {
                        ShowBalloon("准星绘制出错太多次，已停止。详细信息在 crash.log，截图发给 Kiro。", ToolTipIcon.Error);
                    });
                }
                catch (Exception) { }
            };
            Overlay.Start(BuildConfig());

            displayChanged = delegate
            {
                try { form.BeginInvoke((Action)delegate { form.RefreshMonitors(); Publish(); }); } catch (Exception) { }
            };
            SystemEvents.DisplaySettingsChanged += displayChanged;

            Thread waiter = new Thread(WaitForShowRequests);
            waiter.IsBackground = true;
            waiter.Name = "ShowRequestWaiter";
            waiter.Start();

            SyncAutostart(!autostart); // a logon-task start already runs the registered path: only read the state

            if (autostart)
            {
                // Logon task: overlay + tray only.
                if (loadMsg != null) ShowBalloon(loadMsg, ToolTipIcon.Warning);
                return;
            }
            string startMsg = loadMsg;
            Action afterSplash = delegate
            {
                if (exiting) return;
                if (Settings.ShowSettingsOnStart) ShowSettings();
                if (startMsg != null)
                    MessageBox.Show(form.Visible ? (IWin32Window)form : null, startMsg, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            bool splashShown = false;
            if (Splash.ShouldShow(Settings))
            {
                try
                {
                    Splash splash = new Splash(RingIcon);
                    splash.Done += afterSplash;
                    splash.Show(); // animates once the message loop runs; the overlay is already running underneath
                    splashShown = true;
                }
                catch (Exception ex) { Program.Log("splash: " + ex); }
            }
            if (!splashShown) afterSplash();
        }

        /// <summary>
        /// 开机自动启动 at start, in the background (schtasks takes a moment): if the logon task exists but points at another
        /// exe (the folder was moved), re-create it for this exe (Autostart.RefreshIfMoved); then mirror the real task state
        /// (Autostart.IsEnabled) into Settings.Autostart on the UI thread.
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
                    form.BeginInvoke((Action)delegate
                    {
                        if (exiting || Settings.Autostart == on) return;
                        Settings.Autostart = on;
                        form.RefreshValues();
                        Changed();
                    });
                }
                catch (Exception) { }
            });
            t.IsBackground = true;
            t.Name = "AutostartSync";
            t.Start();
        }

        private void LoadWeapons()
        {
            try
            {
                string p = Path.Combine(Program.BaseDir, "weapons.txt");
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    string t = line.Trim().TrimStart('\uFEFF');
                    if (t.Length > 0 && !Weapons.Contains(t)) Weapons.Add(t);
                }
            }
            catch (Exception ex) { Program.Log("weapons.txt: " + ex.Message); }
        }

        private void WaitForShowRequests()
        {
            while (!exiting)
            {
                try
                {
                    showEvent.WaitOne();
                    if (exiting) return;
                    form.BeginInvoke((Action)ShowSettings);
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
            return new RenderConfig(Settings.Clone(false), active.Clone(), cx, cy, Preview, PreviewSpeed, version, toggleAck);
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
            Publish();
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void SaveNow()
        {
            string err = SettingsIO.Save(Settings, SettingsPath);
            SaveStatus = err == null ? "已保存" : "保存失败：" + err;
            Action h = SaveStatusChanged;
            if (h != null) h();
        }

        private void OnOverlayToggle(int seq, bool val)
        {
            Settings.Enabled = val;
            toggleAck = seq;
            form.RefreshEnabled();
            UpdateTray();
            Changed();
        }

        public void SetEnabled(bool val)
        {
            Settings.Enabled = val;
            form.RefreshEnabled();
            UpdateTray();
            Changed();
        }

        public void UpdateTray()
        {
            trayEnable.Checked = Settings.Enabled;
            string t = Program.ShortName + " " + Program.Version + "（" + SettingsForm.KeyName(Settings.Keys.Toggle) + " 开/关）";
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
            form.TopMost = Settings.SettingsTopMost;
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        /// <summary>Called when the user closes the settings window: hide, and explain once.</summary>
        public void OnFormHidden()
        {
            if (!closeBalloonShown)
            {
                closeBalloonShown = true;
                ShowBalloon("准星还在后台运行，右下角托盘图标里可以打开设置或退出。", ToolTipIcon.Info);
            }
        }

        public bool Exiting { get { return exiting; } }

        public void ExitApp() { ExitApp(true); }

        /// <summary>closeForm=false when called from the form's own FormClosing (system shutdown etc.).</summary>
        public void ExitApp(bool closeForm)
        {
            if (exiting) return;
            exiting = true;
            try { SystemEvents.DisplaySettingsChanged -= displayChanged; } catch (Exception) { }
            Overlay.Stop();
            saveTimer.Stop();
            SaveNow();
            try { showEvent.Set(); } catch (Exception) { } // release the waiter thread
            tray.Visible = false;
            tray.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
            form.AllowClose = true;
            // The form's FormClosed handler ends the message loop (ExitThread), so the loop never ends
            // while the form is still inside its own FormClosing.
            if (closeForm && !form.IsDisposed && !form.Disposing) form.Close();
            else if (closeForm) ExitThread();
        }
    }
}
