// v4.4 (W9) NET: runs 检查更新 off the UI thread and asks the user on the UI thread. Not wired yet (phase 2: Program/App).
//   UpdateService.Start(ui, ...)   at app start: cleans up after an update, then one check on a background thread; a
//                                  timer looks again every hour and checks only when 24 h have passed since the last check.
//   The question: 「有新版 vX，更新吗？」 是 / 以后再说 / 跳过这个版本. 是 = download + verify + swap on a background thread,
//   then restart with --updated (Program must wait for the old instance's mutex when it sees that flag).
// The overlay thread is never involved. Offline / any error = silent (log only).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class UpdateService : IDisposable
    {
        private readonly Control ui;
        private readonly IHttp http;
        private readonly string runningVersion, exePath, userDir, prefsPath;
        private readonly Action<string> log;
        private readonly NetPrefs prefs;
        private System.Threading.Timer timer;
        private int busy;          // 1 while a check / install runs or the question is open
        private volatile bool disposed;

        /// <summary>Raised on the UI thread before the app exits for the restart (phase 2: ExitApp so the filter/overlay stop cleanly).</summary>
        public event EventHandler RestartRequested;

        public UpdateService(Control ui, IHttp http, string runningVersion, string exePath, string userDir, Action<string> log)
        {
            this.ui = ui; this.http = http; this.runningVersion = runningVersion; this.exePath = exePath; this.userDir = userDir;
            this.log = log ?? delegate(string s) { };
            prefsPath = NetPrefs.PathIn(userDir);
            prefs = NetPrefs.Load(prefsPath);
        }

        public NetPrefs Prefs { get { return prefs; } }

        /// <summary>自动检查更新 switch (settings page). Saved at once.</summary>
        public bool AutoCheck
        {
            get { return prefs.AutoCheck; }
            set { prefs.AutoCheck = value; prefs.Save(prefsPath); }
        }

        /// <summary>v5.1: 「检查更新」 button. Ignores the 6 h rule and the 自动检查更新 switch; same question when there is one.
        /// done(true) = a question was shown or is coming, done(false) = up to date / offline (called on the UI thread).</summary>
        public void CheckNow(Action<bool, string> done)
        {
            if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) { if (done != null) done(true, "正在检查…"); return; }
            Thread t = new Thread(delegate()
            {
                bool handedOff = false;
                string msg = "已经是最新版本。";
                try
                {
                    UpdateCheckResult r = UpdateChecker.Check(http, runningVersion, prefs, DateTime.UtcNow);
                    prefs.Save(prefsPath);
                    if (r.Decision == UpdateDecision.Offer || r.Decision == UpdateDecision.Skipped)
                    {
                        msg = "有新版 " + r.Release.Tag + "。";
                        handedOff = OnUi(delegate { if (done != null) done(true, msg); Ask(r.Release); });
                    }
                    else if (r.Decision == UpdateDecision.Failed) msg = "暂时连不上 GitHub，稍后再试。";
                    else if (r.Decision == UpdateDecision.Unusable) msg = "新版还没准备好，稍后再试。";
                }
                catch (Exception) { msg = "暂时连不上 GitHub，稍后再试。"; }
                finally
                {
                    if (!handedOff)
                    {
                        Interlocked.Exchange(ref busy, 0);
                        string m2 = msg;
                        OnUi(delegate { if (done != null) done(false, m2); });
                    }
                }
            });
            t.IsBackground = true; t.Name = "update-check-now"; t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        public void Start()
        {
            int removed = UpdateInstaller.CleanupAfterUpdate(exePath, userDir);
            if (removed > 0) log("update: removed " + removed + " leftover update file(s)");
            // v5.1 (W28): first check 30 s after start (startup stays quiet), then the hourly look (checks only after 6 h)
            int first = 1;
            timer = new System.Threading.Timer(delegate { Kick(Interlocked.Exchange(ref first, 0) == 1); }, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
        }

        private void Kick(bool startup)
        {
            if (disposed || !UpdateChecker.ShouldCheck(prefs, DateTime.UtcNow, startup)) return;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            Thread t = new Thread(delegate() { CheckBody(); });
            t.IsBackground = true;
            t.Name = "update-check";
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        private void CheckBody()
        {
            bool handedOff = false;
            try
            {
                UpdateCheckResult r = UpdateChecker.Check(http, runningVersion, prefs, DateTime.UtcNow);
                prefs.Save(prefsPath);
                log("update: " + r.Decision + (r.Release != null ? " " + r.Release.Tag : "") + (r.Error != null ? " (" + r.Error + ")" : ""));
                if (r.Decision == UpdateDecision.Offer && !disposed) handedOff = OnUi(delegate { Ask(r.Release); });
            }
            catch (Exception e) { log("update: check failed " + e.GetType().Name); }
            finally { if (!handedOff) Interlocked.Exchange(ref busy, 0); }
        }

        private bool OnUi(MethodInvoker m)
        {
            try
            {
                if (ui == null || ui.IsDisposed || !ui.IsHandleCreated) return false;
                ui.BeginInvoke(m);
                return true;
            }
            catch (Exception) { return false; }
        }

        private void Ask(ReleaseInfo rel)
        {
            UpdateChoice c = UpdateChoice.Later;
            try { c = UpdatePrompt.Show(ui.FindForm(), rel.Tag); } catch (Exception) { }
            UpdateChecker.Apply(prefs, rel, c);
            prefs.Save(prefsPath);
            if (c != UpdateChoice.Update) { Interlocked.Exchange(ref busy, 0); return; }
            Thread t = new Thread(delegate()
            {
                InstallResult res;
                try { res = UpdateInstaller.Install(http, rel, exePath, userDir); }
                catch (Exception e) { res = new InstallResult(); res.Step = InstallStep.Swap; res.Detail = e.GetType().Name; }
                log("update: install " + rel.Tag + " -> " + res.Step + (res.Detail != null ? " (" + res.Detail + ")" : ""));
                if (!OnUi(delegate { Finish(res); })) Interlocked.Exchange(ref busy, 0);
            });
            t.IsBackground = true; t.Name = "update-install"; t.Start();
        }

        private void Finish(InstallResult res)
        {
            Interlocked.Exchange(ref busy, 0);
            if (!res.Ok)
            {
                MessageBox.Show(ui.FindForm(), res.UserMessage, Program.ShortName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exePath, UpdateInstaller.RestartArg) { UseShellExecute = false });
            }
            catch (Exception e)
            {
                log("update: restart failed " + e.GetType().Name);
                MessageBox.Show(ui.FindForm(), "更新好了，请手动重新打开程序。", Program.ShortName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            EventHandler h = RestartRequested;
            if (h != null) h(this, EventArgs.Empty); else Application.Exit();
        }

        public void Dispose()
        {
            disposed = true;
            System.Threading.Timer t = timer; timer = null;
            if (t != null) t.Dispose();
        }
    }

    /// <summary>「有新版 vX，更新吗？」 with three buttons, in the app's dark theme. Esc / close = 以后再说.</summary>
    internal static class UpdatePrompt
    {
        public static UpdateChoice Show(IWin32Window owner, string tag)
        {
            using (Form f = new Form())
            {
                float s = 1f;
                try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = Math.Max(1f, g.DpiX / 96f); } catch (Exception) { }
                f.Text = Program.ShortName;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = owner == null;
                f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                f.BackColor = Theme.ColSurface; f.ForeColor = Theme.ColText;
                f.AutoScaleMode = AutoScaleMode.None;
                f.Font = Theme.Ui(10f);
                int pad = (int)(16 * s), bw = (int)(120 * s), bh = (int)(34 * s), gap = (int)(10 * s);
                Label l = new Label();
                l.Text = UpdateChecker.PromptText(tag);
                l.Font = Theme.Ui(12f, FontStyle.Bold);
                l.ForeColor = Theme.ColAccent;
                l.AutoSize = false;
                l.SetBounds(pad, pad, 3 * bw + 2 * gap, (int)(44 * s));
                l.TextAlign = ContentAlignment.MiddleLeft;
                f.Controls.Add(l);
                UpdateChoice result = UpdateChoice.Later;
                string[] texts = { UpdateChecker.BtnYes, UpdateChecker.BtnLater, UpdateChecker.BtnSkip };
                UpdateChoice[] vals = { UpdateChoice.Update, UpdateChoice.Later, UpdateChoice.SkipThisVersion };
                for (int i = 0; i < 3; i++)
                {
                    Button b = new Button();
                    b.Text = texts[i];
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = i == 0 ? Theme.ColAccent : Theme.ColBorder;
                    b.BackColor = Theme.ColInput; b.ForeColor = i == 0 ? Theme.ColAccent : Theme.ColText;
                    b.SetBounds(pad + i * (bw + gap), pad + (int)(56 * s), bw, bh);
                    UpdateChoice v = vals[i];
                    b.Click += delegate { result = v; f.Close(); };
                    f.Controls.Add(b);
                    if (i == 0) f.AcceptButton = b;
                    if (i == 1) f.CancelButton = b;
                }
                f.ClientSize = new Size(2 * pad + 3 * bw + 2 * gap, pad + (int)(56 * s) + bh + pad);
                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
                return result;
            }
        }
    }
}
