using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>
    /// v3.0 开机自动启动 via Task Scheduler (schtasks.exe): a Run-key entry of a requireAdministrator app is blocked at logon,
    /// a logon task with RunLevel HighestAvailable is not. Task "TarkovHipAimer": logon trigger for the current user, no time
    /// limit (PT0S), no battery limits, IgnoreNew, action = this exe with --autostart, working dir = the exe folder.
    /// Needs the app to run elevated (it does: app.manifest). Windows behaviour not verifiable on the build host (UNVERIFIED
    /// until tried on Windows); every method returns false + error instead of throwing.
    /// </summary>
    public static class Autostart
    {
        public const string TaskName = "TarkovHipAimer";
        public const string Argument = "--autostart";

        private static string Schtasks()
        {
            string sys = Environment.SystemDirectory;
            string p = string.IsNullOrEmpty(sys) ? "schtasks.exe" : Path.Combine(sys, "schtasks.exe");
            return File.Exists(p) ? p : "schtasks.exe";
        }

        /// <summary>DOMAIN\user of the current user (the logon trigger and principal).</summary>
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

        /// <summary>Pure: the Task Scheduler XML (UTF-16 declaration; write it with Encoding.Unicode).</summary>
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

        private static int Run(string args, out string output)
        {
            output = "";
            ProcessStartInfo si = new ProcessStartInfo(Schtasks(), args);
            si.UseShellExecute = false;
            si.CreateNoWindow = true;
            si.RedirectStandardOutput = true;
            si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } output = "schtasks timeout"; return -1; }
                output = (o + "\n" + e).Trim();
                return p.ExitCode;
            }
        }

        /// <summary>True when the task exists (schtasks /Query exit code 0). Never throws.</summary>
        public static bool IsEnabled()
        {
            try { string o; return Run("/Query /TN \"" + TaskName + "\"", out o) == 0; }
            catch (Exception) { return false; }
        }

        /// <summary>The exe path the registered task starts, or null (no task / error).</summary>
        public static string RegisteredPath()
        {
            try
            {
                string o;
                if (Run("/Query /TN \"" + TaskName + "\" /XML", out o) != 0) return null;
                return CommandFromXml(o);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Creates (or replaces, /F) the logon task for exePath. Returns false with the error text.</summary>
        public static bool Enable(string exePath, out string error)
        {
            error = null;
            string tmp = null;
            try
            {
                if (string.IsNullOrEmpty(exePath)) { error = "exe path empty"; return false; }
                exePath = Path.GetFullPath(exePath);
                tmp = Path.Combine(Path.GetTempPath(), "TarkovHipAimer-task-" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(tmp, BuildTaskXml(exePath, CurrentUserId()), Encoding.Unicode);
                string o;
                int rc = Run("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F", out o);
                if (rc != 0) { error = string.IsNullOrEmpty(o) ? "schtasks exit " + rc : o; return false; }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                if (tmp != null) { try { File.Delete(tmp); } catch (Exception) { } }
            }
        }

        /// <summary>Deletes the task. Not registered = success. Returns false with the error text.</summary>
        public static bool Disable(out string error)
        {
            error = null;
            try
            {
                if (!IsEnabled()) return true;
                string o;
                int rc = Run("/Delete /TN \"" + TaskName + "\" /F", out o);
                if (rc != 0) { error = string.IsNullOrEmpty(o) ? "schtasks exit " + rc : o; return false; }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>At start: if the task exists but starts another exe path (the folder was moved), re-create it for exePath.
        /// No task = nothing to do (true). Returns false with the error text.</summary>
        public static bool RefreshIfMoved(string exePath, out string error)
        {
            error = null;
            try
            {
                string reg = RegisteredPath();
                if (reg == null) return true;
                if (SamePath(reg, exePath)) return true;
                return Enable(exePath, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Pure: same file path, case-insensitive, after GetFullPath (Windows paths).</summary>
        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            try { a = Path.GetFullPath(a.Trim().Trim('"')); b = Path.GetFullPath(b.Trim().Trim('"')); }
            catch (Exception) { }
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
