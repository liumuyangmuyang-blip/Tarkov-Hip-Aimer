// v5.1 (W28) NET: the pending crash log file (pure: no WinForms, no network). Program.ReportCrash calls CrashStore.Save;
// NetHub asks the user on the next start (CrashPrompt, Net.Crash.cs) and deletes the file after the answer.
// v5.1 R3: nothing is uploaded. On 打开邮箱 the hub first scrubs crash.log in place (ScrubLogFile), then opens the user's mail
// program and the folder with crash.log selected, so the user attaches the file to the email by hand.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TarkovCrosshair
{
    internal enum CrashChoice { OpenMail, DontSend, NeverAsk }

    internal static class CrashStore
    {
        public const string FileName = "crash-pending.txt";
        public const int MaxChars = 16000;

        public static string PathIn(string userDir) { return Path.Combine(userDir ?? "", FileName); }

        /// <summary>The text that would be sent: time (UTC), version, the exception, scrubbed and capped.</summary>
        public static string Build(Exception ex, string version, DateTime utc, string userName, string machineName)
        {
            StringBuilder b = new StringBuilder();
            b.Append(utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" UTC  ").Append(NetVersion.Normal(version)).Append('\n');
            b.Append(ex != null ? ex.ToString() : "(no exception)");
            string s = NetScrub.Text(b.ToString().Replace("\r\n", "\n"), userName, machineName);
            if (s.Length > MaxChars) s = NetScrub.Cap(s, MaxChars - 20) + "\n…（太长，已截断）";
            return s;
        }

        public static bool Save(string userDir, Exception ex, string version)
        {
            try
            {
                string text = Build(ex, version, DateTime.UtcNow, Environment.UserName, Environment.MachineName);
                File.WriteAllText(PathIn(userDir), text, new UTF8Encoding(false));
                return true;
            }
            catch (Exception) { return false; }
        }

        public static string Load(string userDir)
        {
            try
            {
                string p = PathIn(userDir);
                if (!File.Exists(p)) return null;
                string s = File.ReadAllText(p, Encoding.UTF8);
                return NetScrub.Cap(s, MaxChars + 40);
            }
            catch (Exception) { return null; }
        }

        public static void Delete(string userDir)
        {
            try { string p = PathIn(userDir); if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
        }

        public const string LogName = "crash.log";

        /// <summary>Rewrites crash.log (and crash.old.log) with user name, computer name, paths, e-mail and IP removed (NetScrub),
        /// via a temp file. Returns the path of crash.log when it exists afterwards, else null. Never throws.</summary>
        public static string ScrubLogFile(string userDir, string userName, string machineName)
        {
            string main = null;
            foreach (string name in new[] { LogName, "crash.old.log" })
            {
                try
                {
                    string p = Path.Combine(userDir ?? "", name);
                    if (!File.Exists(p)) continue;
                    string raw = File.ReadAllText(p, Encoding.UTF8);
                    string clean = NetScrub.Text(raw, userName, machineName);
                    if (clean != raw)
                    {
                        string tmp = p + ".tmp";
                        File.WriteAllText(tmp, clean, new UTF8Encoding(false));
                        File.Copy(tmp, p, true);
                        File.Delete(tmp);
                    }
                    if (name == LogName) main = p;
                }
                catch (Exception) { }
            }
            return main;
        }
    }

}
