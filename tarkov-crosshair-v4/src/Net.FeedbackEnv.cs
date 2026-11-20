// v4.4 (W9) NET: what 反馈 attaches about the PC: app version, Windows version, screen sizes + scale. Nothing else.
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TarkovCrosshair
{
    internal static class FeedbackEnvProbe
    {
        public static FeedbackEnv Collect()
        {
            FeedbackEnv e = new FeedbackEnv();
            e.AppVersion = Program.Version;
            e.Windows = WindowsVersion();
            e.Screens = ScreenText();
            return e;
        }

        /// <summary>"Windows 11 Pro 23H2 (build 22631)". Environment.OSVersion lies on .NET Framework, so read the registry.</summary>
        public static string WindowsVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k == null) return Environment.OSVersion.VersionString;
                    string name = Convert.ToString(k.GetValue("ProductName", "Windows"));
                    string disp = Convert.ToString(k.GetValue("DisplayVersion", k.GetValue("ReleaseId", "")));
                    string build = Convert.ToString(k.GetValue("CurrentBuild", ""));
                    int b;
                    // Windows 11 still says "Windows 10" in ProductName; build 22000+ is Windows 11.
                    if (int.TryParse(build, out b) && b >= 22000 && name.StartsWith("Windows 10", StringComparison.Ordinal))
                        name = "Windows 11" + name.Substring("Windows 10".Length);
                    return (name + " " + disp).Trim() + (build.Length > 0 ? " (build " + build + ")" : "")
                        + (Environment.Is64BitOperatingSystem ? " x64" : " x86");
                }
            }
            catch (Exception) { return "Windows"; }
        }

        public static string ScreenText()
        {
            StringBuilder b = new StringBuilder();
            try
            {
                int pct = 100;
                try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) pct = (int)Math.Round(g.DpiX * 100.0 / 96.0); } catch (Exception) { }
                foreach (Screen s in Screen.AllScreens)
                {
                    if (b.Length > 0) b.Append(", ");
                    b.Append(s.Bounds.Width).Append('x').Append(s.Bounds.Height);
                    if (s.Primary) b.Append(" @").Append(pct).Append('%');
                }
            }
            catch (Exception) { }
            return b.ToString();
        }
    }
}
