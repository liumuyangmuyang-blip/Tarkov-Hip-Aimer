using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace TarkovCrosshair
{
    /// <summary>
    /// v5.0.2 开机自动启动 (Leo 18 Nov 2026: the exe always runs as administrator, requireAdministrator in app.manifest, so it
    /// can read the keys while the game runs as administrator). A per-user Run key entry would start it at logon only through
    /// a UAC prompt (or not at all), so autostart is a Task Scheduler logon task again: 「TarkovHipAimer」, logon trigger for
    /// the current user, 「使用最高权限运行」 (RunLevel HighestAvailable), interactive token, no password stored. The app is
    /// elevated, so it creates and deletes the task itself (schtasks.exe /Create /XML, /Delete). The v4.1 - v5.0.1 HKCU Run
    /// value 「TarkovHipAimer」 is removed on every start (RefreshIfMoved) and by Enable / Disable; if it was there (autostart
    /// was on) the task is created in its place.
    /// Every method returns false + a Chinese error instead of throwing. schtasks runs as a process: callers on the UI thread
    /// use BeginSet. Windows behaviour is UNVERIFIED on the build host; the logic runs against the injectable Store / Run.
    /// </summary>
    public static class Autostart
    {
        /// <summary>Name of the logon task, and of the old v4.1 - v5.0.1 Run value.</summary>
        public const string TaskName = "TarkovHipAimer";
        public const string ValueName = TaskName;
        public const string Argument = "--autostart";
        /// <summary>Under HKEY_CURRENT_USER (old autostart, removed).</summary>
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>The Run-key value store. Get returns null when the value is missing; Set / Delete may throw.</summary>
        public interface IRunStore
        {
            string Get(string name);
            void Set(string name, string value);
            void Delete(string name);
        }

        private sealed class RegistryRunStore : IRunStore
        {
            public string Get(string name)
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    return k == null ? null : k.GetValue(name) as string;
            }

            public void Set(string name, string value)
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null) throw new IOException("Run key not available");
                    k.SetValue(name, value, RegistryValueKind.String);
                }
            }

            public void Delete(string name)
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    if (k != null) k.DeleteValue(name, false);
            }
        }

        /// <summary>Where the old Run value lives (HKCU Run key; tests put a fake here).</summary>
        public static IRunStore Store = new RegistryRunStore();

        /// <summary>Runs schtasks.exe with these arguments: exit code (-1 = it could not run) and its standard output.</summary>
        public delegate int SchtasksRunner(string args, out string stdout);

        /// <summary>schtasks.exe (tests replace it).</summary>
        public static SchtasksRunner Run = RunSchtasks;

        /// <summary>Writes the task XML to a temporary file and returns its path (tests replace it). UTF-16, as schtasks wants.</summary>
        public static Func<string, string> WriteTempXml = DefaultWriteTempXml;

        /// <summary>Kept for old callers: always false now (the task is ours to delete, the app is elevated).</summary>
        public static bool LegacyTaskLeft;

        private static readonly object sync = new object();
        private static int known = -1; // last known state: -1 unknown, 0 off, 1 on (IsEnabled answers from it)

        // ------------------------------------------------------------------ pure

        /// <summary>Pure: the old Run value for exePath: "&lt;exe&gt;" --autostart.</summary>
        public static string BuildRunValue(string exePath)
        {
            return "\"" + (exePath ?? "").Trim().Trim('"') + "\" " + Argument;
        }

        /// <summary>Pure: the exe path a Run value starts ("C:\a b\x.exe" --autostart, or unquoted C:\x.exe --autostart), or null.</summary>
        public static string ExeFromRunValue(string value)
        {
            if (value == null) return null;
            string s = value.Trim();
            if (s.Length == 0) return null;
            if (s[0] == '"')
            {
                int e = s.IndexOf('"', 1);
                string q = (e < 0 ? s.Substring(1) : s.Substring(1, e - 1)).Trim();
                return q.Length == 0 ? null : q;
            }
            int a = s.IndexOf(" " + Argument, StringComparison.OrdinalIgnoreCase);
            string p = (a >= 0 ? s.Substring(0, a) : s).Trim();
            return p.Length == 0 ? null : p;
        }

        /// <summary>Pure: same file path, case-insensitive, after GetFullPath (Windows paths).</summary>
        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            try { a = Path.GetFullPath(a.Trim().Trim('"')); b = Path.GetFullPath(b.Trim().Trim('"')); }
            catch (Exception) { }
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ the logon task

        /// <summary>Autostart on (the logon task exists). Answers from the last known state; asks schtasks only the first time.
        /// Never throws.</summary>
        public static bool IsEnabled()
        {
            lock (sync) { if (known >= 0) return known == 1; }
            return Query();
        }

        /// <summary>Asks schtasks whether the task exists (a process: not on the UI thread). Never throws.</summary>
        public static bool Query()
        {
            bool on = TaskExists();
            lock (sync) known = on ? 1 : 0;
            return on;
        }

        /// <summary>The exe path the logon task starts, or null (no task / error).</summary>
        public static string RegisteredPath()
        {
            try
            {
                string xml;
                if (Run("/Query /TN \"" + TaskName + "\" /XML", out xml) != 0) return null;
                return CommandFromXml(xml);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Creates (or replaces) the logon task for exePath and removes the old Run value. False with a Chinese error.</summary>
        public static bool Enable(string exePath, out string error)
        {
            error = null;
            LegacyTaskLeft = false;
            if (string.IsNullOrEmpty(exePath)) { error = "找不到程序路径。"; return false; }
            string full;
            try { full = Path.GetFullPath(exePath.Trim().Trim('"')); } catch (Exception) { full = exePath; }
            if (!CreateTask(full))
            {
                lock (sync) known = -1;
                error = "无法创建开机启动任务（可能被安全软件拦截）。";
                return false;
            }
            lock (sync) known = 1;
            RemoveRunValue();
            return true;
        }

        /// <summary>Deletes the logon task (missing = success) and the old Run value. False with a Chinese error.</summary>
        public static bool Disable(out string error)
        {
            error = null;
            LegacyTaskLeft = false;
            bool runOk = RemoveRunValue();
            bool gone = true;
            try
            {
                if (TaskExists())
                {
                    string o;
                    int rc = Run("/Delete /TN \"" + TaskName + "\" /F", out o);
                    gone = !TaskExists();
                    if (!gone) Program.Log("autostart: task \"" + TaskName + "\" could not be deleted (schtasks exit " + rc + ")");
                }
            }
            catch (Exception) { gone = false; }
            lock (sync) known = gone ? 0 : -1;
            if (!gone || !runOk) { error = "无法删除开机启动项（可能被安全软件拦截）。"; return false; }
            return true;
        }

        /// <summary>
        /// At start (background thread): removes the old Run value; if it was there (autostart was on in v4.1 - v5.0.1) the
        /// logon task is created instead. A task that starts another exe (the folder was moved) is pointed at this exe.
        /// Autostart off = nothing created. False with a Chinese error.
        /// </summary>
        public static bool RefreshIfMoved(string exePath, out string error)
        {
            error = null;
            try
            {
                string full = Path.GetFullPath(exePath.Trim().Trim('"'));
                string oldRun = null;
                try { oldRun = Store.Get(ValueName); } catch (Exception) { oldRun = null; }
                bool runWasOn = !string.IsNullOrEmpty(oldRun);
                bool runRemoved = RemoveRunValue();
                string xml;
                bool task = Run("/Query /TN \"" + TaskName + "\" /XML", out xml) == 0;
                bool ok = true;
                if (task)
                {
                    string cmd = CommandFromXml(xml);
                    if (cmd == null || !SamePath(cmd, full) || !IsOurTask(xml)) ok = CreateTask(full);
                }
                else if (runWasOn) ok = CreateTask(full);
                bool on = task || (runWasOn && ok);
                lock (sync) known = ok ? (on ? 1 : 0) : -1;
                if (!ok) { error = "无法更新开机启动项。"; return false; }
                if (!runRemoved) { error = "无法删除旧的开机启动项。"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                Program.Log("autostart refresh: " + ex);
                error = "无法更新开机启动项。";
                return false;
            }
        }

        /// <summary>v5.0.2: Enable / Disable on a background thread (schtasks is a process; the UI thread must not wait for
        /// it). done(ok, error, nowOn) runs on that background thread: marshal it to the UI thread (Control.BeginInvoke).</summary>
        public static void BeginSet(bool on, string exePath, Action<bool, string, bool> done)
        {
            Thread t = new Thread(delegate()
            {
                string err = null;
                bool ok;
                try { ok = on ? Enable(exePath, out err) : Disable(out err); }
                catch (Exception ex) { ok = false; err = ex.Message; }
                bool now;
                try { now = ok ? on : Query(); } catch (Exception) { now = false; }
                if (done != null) { try { done(ok, err, now); } catch (Exception) { } }
            });
            t.IsBackground = true;
            t.Name = "AutostartSet";
            t.Start();
        }

        /// <summary>The task exists (schtasks /Query exit code 0). Never throws.</summary>
        public static bool TaskExists()
        {
            try { string o; return Run("/Query /TN \"" + TaskName + "\"", out o) == 0; }
            catch (Exception) { return false; }
        }

        /// <summary>Kept for old callers (v4.1 name): the task exists.</summary>
        public static bool LegacyTaskExists() { return TaskExists(); }

        /// <summary>Pure: an XML that our BuildTaskXml made (highest privileges, logon trigger, our argument).</summary>
        public static bool IsOurTask(string xml)
        {
            if (xml == null) return false;
            return xml.IndexOf("<RunLevel>HighestAvailable</RunLevel>", StringComparison.OrdinalIgnoreCase) >= 0
                && xml.IndexOf("<LogonTrigger>", StringComparison.OrdinalIgnoreCase) >= 0
                && xml.IndexOf("<Arguments>" + Argument + "</Arguments>", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CreateTask(string exe)
        {
            string file = null;
            try
            {
                file = WriteTempXml(BuildTaskXml(exe, CurrentUserId()));
                string o;
                int rc = Run("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F", out o);
                if (rc != 0) { Program.Log("autostart: schtasks /Create exit " + rc); return false; }
                return true;
            }
            catch (Exception ex) { Program.Log("autostart create: " + ex.Message); return false; }
            finally
            {
                if (file != null) { try { File.Delete(file); } catch (Exception) { } }
            }
        }

        /// <summary>Deletes the old HKCU Run value (missing = success). Never throws.</summary>
        public static bool RemoveRunValue()
        {
            try { if (Store.Get(ValueName) != null) Store.Delete(ValueName); return true; }
            catch (Exception ex) { Program.Log("autostart: old Run value not removed: " + ex.Message); return false; }
        }

        private static string DefaultWriteTempXml(string xml)
        {
            string path = Path.Combine(Path.GetTempPath(), "TarkovHipAimer-task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(path, xml, Encoding.Unicode);
            return path;
        }

        private static string SchtasksPath()
        {
            string sys = Environment.SystemDirectory;
            string p = string.IsNullOrEmpty(sys) ? "schtasks.exe" : Path.Combine(sys, "schtasks.exe");
            return File.Exists(p) ? p : "schtasks.exe";
        }

        private static int RunSchtasks(string args, out string stdout)
        {
            stdout = null;
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(SchtasksPath(), args);
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                using (Process p = Process.Start(si))
                {
                    System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
                    stdout = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } return -1; }
                    try { err.Wait(1000); } catch (Exception) { }
                    return p.ExitCode;
                }
            }
            catch (Exception) { return -1; }
        }

        // ------------------------------------------------------------------ task XML

        /// <summary>DOMAIN\user of the current user (task trigger and principal).</summary>
        public static string CurrentUserId()
        {
            string d = Environment.UserDomainName, u = Environment.UserName;
            return string.IsNullOrEmpty(d) ? u : d + "\\" + u;
        }

        private static string X(string s)
        {
            if (s == null) return "";
            StringBuilder b = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': b.Append("&amp;"); break;
                    case '<': b.Append("&lt;"); break;
                    case '>': b.Append("&gt;"); break;
                    case '"': b.Append("&quot;"); break;
                    case '\'': b.Append("&apos;"); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }

        /// <summary>Pure: the Task Scheduler XML of the logon task: logon trigger for userId, interactive token (no password),
        /// highest privileges, no time limit, also on battery, one instance.</summary>
        public static string BuildTaskXml(string exePath, string userId)
        {
            string dir = Path.GetDirectoryName(exePath) ?? "";
            StringBuilder b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            b.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            b.Append("  <RegistrationInfo>\r\n");
            b.Append("    <Author>").Append(X(userId)).Append("</Author>\r\n");
            b.Append("    <Description>Tarkov Hip Aimer - by Leo: start in the tray at logon</Description>\r\n");
            b.Append("  </RegistrationInfo>\r\n");
            b.Append("  <Triggers>\r\n");
            b.Append("    <LogonTrigger>\r\n");
            b.Append("      <Enabled>true</Enabled>\r\n");
            b.Append("      <UserId>").Append(X(userId)).Append("</UserId>\r\n");
            b.Append("    </LogonTrigger>\r\n");
            b.Append("  </Triggers>\r\n");
            b.Append("  <Principals>\r\n");
            b.Append("    <Principal id=\"Author\">\r\n");
            b.Append("      <UserId>").Append(X(userId)).Append("</UserId>\r\n");
            b.Append("      <LogonType>InteractiveToken</LogonType>\r\n");
            b.Append("      <RunLevel>HighestAvailable</RunLevel>\r\n");
            b.Append("    </Principal>\r\n");
            b.Append("  </Principals>\r\n");
            b.Append("  <Settings>\r\n");
            b.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n");
            b.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n");
            b.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n");
            b.Append("    <AllowHardTerminate>true</AllowHardTerminate>\r\n");
            b.Append("    <StartWhenAvailable>false</StartWhenAvailable>\r\n");
            b.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n");
            b.Append("    <IdleSettings>\r\n");
            b.Append("      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n");
            b.Append("      <RestartOnIdle>false</RestartOnIdle>\r\n");
            b.Append("    </IdleSettings>\r\n");
            b.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n");
            b.Append("    <Enabled>true</Enabled>\r\n");
            b.Append("    <Hidden>false</Hidden>\r\n");
            b.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n");
            b.Append("    <WakeToRun>false</WakeToRun>\r\n");
            b.Append("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n");
            b.Append("    <Priority>7</Priority>\r\n");
            b.Append("  </Settings>\r\n");
            b.Append("  <Actions Context=\"Author\">\r\n");
            b.Append("    <Exec>\r\n");
            b.Append("      <Command>\"").Append(X(exePath)).Append("\"</Command>\r\n");
            b.Append("      <Arguments>").Append(Argument).Append("</Arguments>\r\n");
            b.Append("      <WorkingDirectory>").Append(X(dir)).Append("</WorkingDirectory>\r\n");
            b.Append("    </Exec>\r\n");
            b.Append("  </Actions>\r\n");
            b.Append("</Task>\r\n");
            return b.ToString();
        }

        /// <summary>Pure: the exe path inside a task XML's &lt;Command&gt; (quotes and XML escapes removed), or null.</summary>
        public static string CommandFromXml(string xml)
        {
            if (xml == null) return null;
            int a = xml.IndexOf("<Command>", StringComparison.OrdinalIgnoreCase);
            if (a < 0) return null;
            a += "<Command>".Length;
            int b = xml.IndexOf("</Command>", a, StringComparison.OrdinalIgnoreCase);
            if (b < 0) return null;
            string c = xml.Substring(a, b - a).Trim();
            c = c.Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
            return c.Trim().Trim('"');
        }
    }
}
