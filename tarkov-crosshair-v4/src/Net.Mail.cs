// v5.1 R3 (W28, Leo: "我邮箱写在软件里，然后别人可以直接发我"): feedback and crash logs go by the USER'S OWN email program to
// Leo's address. The app sends nothing itself: it only opens a mailto: link (subject + a body template with version,
// Windows and screen) and, after a crash, the 数据 Data folder with crash.log selected so the user can attach it.
// Pure except Open (Process.Start of the mailto link).
using System;
using System.Diagnostics;
using System.Text;

namespace TarkovCrosshair
{
    internal static class NetMail
    {
        /// <summary>Leo's address, shown on the 反馈 page (same value as SettingsForm.Email).</summary>
        public const string Address = "liumuyangmuyang@gmail.com";

        /// <summary>Windows hands mailto: to the mail program through ShellExecute; keep the whole link well under ~2,000 chars.</summary>
        public const int MaxLink = 1800;

        /// <summary>Same text as Program.ShortName (this file is also compiled into the tests, without Program).</summary>
        public const string Product = "Tarkov Hip Aimer";

        public static string FeedbackSubject(string version) { return Product + " " + NetVersion.Normal(version) + " 反馈"; }
        public static string CrashSubject(string version) { return Product + " 崩溃记录 " + NetVersion.Normal(version); }

        public static string FeedbackBody(FeedbackEnv env)
        {
            return "（请在这里写遇到的问题或想要的功能，可以附上截图）\n\n\n" + EnvBlock(env);
        }

        public static string CrashBody(FeedbackEnv env)
        {
            return "程序出错了。请把 crash.log 拖进这封邮件当附件（文件夹已经打开，crash.log 已选中）。\n\n出错前在做什么（可以不填）：\n\n\n" + EnvBlock(env);
        }

        private static string EnvBlock(FeedbackEnv env)
        {
            StringBuilder b = new StringBuilder("---\n");
            b.Append("版本：").Append(Clean(env != null ? NetVersion.Normal(env.AppVersion) : "")).Append('\n');
            b.Append("Windows：").Append(Clean(env != null ? env.Windows : "")).Append('\n');
            b.Append("屏幕：").Append(Clean(env != null ? env.Screens : "")).Append('\n');
            return b.ToString();
        }

        private static string Clean(string s)
        {
            s = NetScrub.Text((s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim());
            return NetScrub.Cap(s, 120);
        }

        /// <summary>mailto:Address?subject=..&amp;body=.. (RFC 6068: CRLF line breaks, percent-encoded UTF-8). The body is cut
        /// line by line from the end of its first part if the link would be too long; subject and address always stay.</summary>
        public static string Link(string subject, string body)
        {
            string head = "mailto:" + Address + "?subject=" + Uri.EscapeDataString(subject ?? "");
            string b = (body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            string url = head + "&body=" + Uri.EscapeDataString(b);
            while (url.Length > MaxLink && b.Length > 0)
            {
                b = b.Substring(0, Math.Max(0, b.Length - Math.Max(20, b.Length / 8)));
                if (b.Length > 0 && char.IsHighSurrogate(b[b.Length - 1])) b = b.Substring(0, b.Length - 1);
                url = head + "&body=" + Uri.EscapeDataString(b);
            }
            return url.Length > MaxLink ? head : url;
        }

        public static string FeedbackLink(FeedbackEnv env) { return Link(FeedbackSubject(env != null ? env.AppVersion : ""), FeedbackBody(env)); }
        public static string CrashLink(FeedbackEnv env) { return Link(CrashSubject(env != null ? env.AppVersion : ""), CrashBody(env)); }

        /// <summary>Opens the user's mail program. False when Windows has none for mailto: (caller shows the address to copy).</summary>
        public static bool Open(string link)
        {
            if (link == null || !link.StartsWith("mailto:" + Address, StringComparison.Ordinal)) return false;
            try { using (Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })) { } return true; }
            catch (Exception) { return false; }
        }
    }
}
