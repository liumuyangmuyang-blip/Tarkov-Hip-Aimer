// v5.1 (W28) NET: one object that runs all network features, so Program.cs / SettingsForm need only a few lines (W25 hooks):
//   App ctor (after invoker has a handle):  net = new NetHub(invoker, NetSettingsBridge, new NetHttp(), Program.Version,
//                                           Application.ExecutablePath, SettingsIO.UserDir(), Program.Log); net.Start();
//   ExitApp:                                net.Dispose();   (sends the session-end heartbeat, waits at most 1.2 s)
//   Program.ReportCrash:                    CrashStore.Save(SettingsIO.UserDir(), ex, Version);  (+ net.CheckPendingCrash()
//                                           when the app keeps running)
//   settings change (常规 checkboxes):        net.SettingsChanged();
//   反馈 page:                               net.CreateFeedbackPanel() (email box; v5.1 R3 no send box)
//   统计 view:                               new MonitorPanel(net.CreateMonitorSource())
//   统计 (v5.1, Leo 21 Nov 2026):           on by default; 常规 / wizard checkbox -> settings + net.SettingsChanged()
// Settings are the source of truth (GlobalSettings NetUpdateCheck / NetStatsConsent / NetCrashAsk via INetSettings);
// net-prefs.json keeps only machine state (install id, skipped version, last check time) plus a mirror of the two switches.
// Stats: opt-out. NetStatsConsent null / true = heartbeat, false = nothing (stops at once). Crash logs / feedback: own email.
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class NetHub : IDisposable
    {
        private readonly Control ui;
        private readonly INetSettings settings;
        private readonly IHttp http;
        private readonly string version, userDir, workerUrl;
        private readonly Action<string> log;
        private readonly UpdateService updates;
        private UsageHeartbeat heartbeat;
        private System.Windows.Forms.Timer crashTimer;
        private int crashAsking;
        private bool disposed;

        public NetHub(Control ui, INetSettings settings, IHttp http, string version, string exePath, string userDir, Action<string> log)
            : this(ui, settings, http, version, exePath, userDir, log, UsageConfig.WorkerUrl) { }

        /// <summary>workerUrl is a parameter only for tests; the app always uses UsageConfig.WorkerUrl.</summary>
        internal NetHub(Control ui, INetSettings settings, IHttp http, string version, string exePath, string userDir, Action<string> log, string workerUrl)
        {
            this.ui = ui; this.settings = settings ?? new DelegateNetSettings(); this.http = http; this.version = version;
            this.userDir = userDir; this.log = log ?? delegate(string s) { }; this.workerUrl = workerUrl;
            updates = new UpdateService(ui, http, version, exePath, userDir, this.log);
            Mirror();
        }

        public UpdateService Updates { get { return updates; } }
        public UsageHeartbeat Heartbeat { get { return heartbeat; } }
        public bool OnlineEnabled { get { return UsageConfig.IsValidWorkerUrl(workerUrl); } }

        /// <summary>True when the 统计 question still has to be asked (worker set, never answered).</summary>
        public bool NeedsStatsQuestion { get { return NetRules.NeedsStatsQuestion(settings, workerUrl); } }

        public void Start()
        {
            updates.Start();
            SyncHeartbeat();
            // pending crash from the last run: ask a little after start, never during the first frames
            if (ui != null && CrashStore.Load(userDir) != null)
            {
                crashTimer = new System.Windows.Forms.Timer();
                crashTimer.Interval = 8000;
                crashTimer.Tick += delegate { crashTimer.Stop(); CheckPendingCrash(); };
                crashTimer.Start();
            }
        }

        /// <summary>Call after the user changed 自动检查更新 / 统计 / 错误记录 in settings.</summary>
        public void SettingsChanged() { Mirror(); SyncHeartbeat(); }

        public void SetStatsConsent(bool yes)
        {
            settings.StatsConsent = yes;
            settings.Save();
            SettingsChanged();
        }

        /// <summary>v5.1 (Leo 21 Nov 2026): no question any more (stats on by default); always returns null, asks nothing.</summary>
        public bool? AskStatsIfNeeded(IWin32Window owner)
        {
            if (!NeedsStatsQuestion) return null;
            bool yes = false;
            try
            {
                yes = MessageBox.Show(owner, UsageConfig.AskText, UsageConfig.AskTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            catch (Exception) { }
            SetStatsConsent(yes);
            return yes;
        }

        private void Mirror()
        {
            NetRules.Mirror(settings, updates.Prefs);
            updates.Prefs.Save(NetPrefs.PathIn(userDir));
        }

        private void SyncHeartbeat()
        {
            bool want = !disposed && NetRules.WantHeartbeat(settings, workerUrl);
            if (want && heartbeat == null)
            {
                heartbeat = new UsageHeartbeat(http, updates.Prefs, workerUrl, version);
                heartbeat.Start();
                log("net: heartbeat on");
            }
            else if (!want && heartbeat != null)
            {
                heartbeat.Dispose();
                heartbeat = null;
                log("net: heartbeat off");
            }
        }

        /// <summary>If a crash log is waiting: with 「程序出错后，问我…」 on, ask (UI thread); 打开邮箱 = mail program + folder;
        /// otherwise just delete it. Safe to call from any thread, any number of times.</summary>
        public void CheckPendingCrash()
        {
            string text = CrashStore.Load(userDir);
            if (text == null) return;
            if (!NetRules.MayAskCrash(settings)) { CrashStore.Delete(userDir); return; }
            if (Interlocked.CompareExchange(ref crashAsking, 1, 0) != 0) return;
            MethodInvoker ask = delegate
            {
                CrashChoice c = CrashChoice.DontSend;
                try { c = CrashPrompt.Show(ui != null ? ui.FindForm() : null, text); } catch (Exception) { }
                ApplyCrashChoice(c, text);
            };
            try
            {
                if (ui != null && ui.IsHandleCreated && !ui.IsDisposed) { if (ui.InvokeRequired) ui.BeginInvoke(ask); else ask(); }
                else Interlocked.Exchange(ref crashAsking, 0);
            }
            catch (Exception) { Interlocked.Exchange(ref crashAsking, 0); }
        }

        /// <summary>The answer: pending file deleted in every case; 以后都不问 turns the switch off; 打开邮箱 (v5.1 R3) scrubs
        /// crash.log, opens a mail to Leo and the folder with crash.log selected. Nothing is uploaded. UI thread.</summary>
        internal void ApplyCrashChoice(CrashChoice c, string text)
        {
            CrashStore.Delete(userDir);
            log("net: crash log " + c);
            if (c == CrashChoice.NeverAsk) { settings.CrashAsk = false; settings.Save(); }
            if (c == CrashChoice.OpenMail) OpenCrashMail();
            Interlocked.Exchange(ref crashAsking, 0);
        }

        private void OpenCrashMail()
        {
            try { Program.FlushLog(500); } catch (Exception) { }
            string user = "", machine = "";
            try { user = Environment.UserName; machine = Environment.MachineName; } catch (Exception) { }
            string logPath = CrashStore.ScrubLogFile(userDir, user, machine);
            FeedbackEnv env;
            try { env = FeedbackEnvProbe.Collect(); } catch (Exception) { env = new FeedbackEnv(); env.AppVersion = version; }
            bool mail = NetMail.Open(NetMail.CrashLink(env));
            try
            {
                if (logPath != null) Process.Start("explorer.exe", "/select,\"" + logPath + "\"");
                else Process.Start("explorer.exe", "\"" + userDir + "\"");
            }
            catch (Exception ex) { log("net: open folder: " + ex.GetType().Name); }
            string msg = mail
                ? "已新建一封发给作者的邮件，也打开了放着 crash.log 的文件夹。\r\n请把选中的 crash.log 拖进邮件当附件，然后发送。"
                : "这台电脑没有设置邮件软件。请在网页邮箱里新建邮件发给 " + NetMail.Address + "，主题写「" + NetMail.CrashSubject(version)
                    + "」，把已经选中的 crash.log 当附件。";
            if (!mail) { try { Clipboard.SetText(NetMail.Address); msg += "\r\n（邮箱已复制）"; } catch (Exception) { } }
            try { MessageBox.Show(ui != null ? ui.FindForm() : null, msg, Program.ShortName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception) { }
        }

        /// <summary>The 反馈 email box (address + 复制 + 打开邮箱). v5.1 R3: no send box.</summary>
        public FeedbackPanel CreateFeedbackPanel()
        {
            return new FeedbackPanel(delegate { return FeedbackEnvProbe.Collect(); });
        }

        public IMonitorSource CreateMonitorSource() { return new MonitorSource(http, version, workerUrl); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (crashTimer != null) { crashTimer.Stop(); crashTimer.Dispose(); crashTimer = null; }
            try { updates.Dispose(); } catch (Exception) { }
            UsageHeartbeat h = heartbeat;
            heartbeat = null;
            if (h != null)
            {
                h.Dispose();
                // session end, so 使用时长 is exact; never holds the exit longer than EndTimeoutMs
                Thread t = new Thread(delegate() { try { h.SendEnd(); } catch (Exception) { } });
                t.IsBackground = true; t.Name = "hb-end";
                t.Start();
                t.Join(UsageHeartbeat.EndTimeoutMs + 100);
            }
        }
    }
}
