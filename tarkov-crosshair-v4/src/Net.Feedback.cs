// v4.4 (W9) NET: 反馈 -> a prefilled GitHub "new issue" page in the default browser. The app makes no network call itself.
// v5.1 R3 (W28): not used by the 反馈 page any more (Leo: users email him; Net.Mail.cs); kept for a possible GitHub-issue link.
// Body = the user's text + app version + Windows version + screen size / scale. Never the user name, machine name or a path.
// Length-capped so the whole URL stays well under browser / GitHub limits.
using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TarkovCrosshair
{
    internal enum FeedbackKind { Bug, Idea }

    internal sealed class FeedbackEnv
    {
        public string AppVersion = "";
        public string Windows = "";   // e.g. "Windows 11 23H2 (build 22631)"
        public string Screens = "";   // e.g. "2160x1440 @200%"
    }

    internal static class FeedbackUrl
    {
        /// <summary>Whole URL cap. Browsers take far more; GitHub's issue form starts failing somewhere above ~8 k.</summary>
        public const int MaxUrlLength = 6000;
        public const int MaxTitleChars = 80;

        public static string Label(FeedbackKind k) { return k == FeedbackKind.Bug ? "bug" : "idea"; }

        /// <summary>Title from the first line of the text (or a default), prefixed 「问题」/「建议」.</summary>
        public static string Title(FeedbackKind k, string text)
        {
            string first = (text ?? "").Replace("\r", "").Split('\n')[0].Trim();
            first = Scrub(first);
            if (first.Length == 0) first = k == FeedbackKind.Bug ? "程序问题" : "功能建议";
            string t = (k == FeedbackKind.Bug ? "[问题] " : "[建议] ") + first;
            return t.Length > MaxTitleChars ? t.Substring(0, MaxTitleChars - 1) + "…" : t;
        }

        public static string Body(string text, FeedbackEnv env)
        {
            StringBuilder b = new StringBuilder();
            b.Append(Scrub((text ?? "").Trim()));
            b.Append("\n\n---\n");
            b.Append("版本: ").Append(Clean(env != null ? env.AppVersion : "")).Append('\n');
            b.Append("Windows: ").Append(Clean(env != null ? env.Windows : "")).Append('\n');
            b.Append("屏幕: ").Append(Clean(env != null ? env.Screens : "")).Append('\n');
            return b.ToString();
        }

        /// <summary>The full URL. The user's text is cut (with 「…（太长，已截断）」) until the URL fits MaxUrlLength.</summary>
        public static string Build(FeedbackKind k, string text, FeedbackEnv env)
        {
            string title = Title(k, text);
            string t = (text ?? "").Trim();
            string url = Make(title, Body(t, env), k);
            if (url.Length <= MaxUrlLength) return url;
            const string cutMark = "\n…（太长，已截断）";
            int lo = 0, hi = t.Length; // binary search the longest prefix that fits
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Make(title, Body(SafeCut(t, mid) + cutMark, env), k).Length <= MaxUrlLength) lo = mid; else hi = mid - 1;
            }
            return Make(title, Body(SafeCut(t, lo) + cutMark, env), k);
        }

        private static string Make(string title, string body, FeedbackKind k)
        {
            return NetConfig.NewIssueUrl + "?labels=" + Uri.EscapeDataString(Label(k))
                + "&title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
        }

        private static string SafeCut(string s, int n)
        {
            if (n >= s.Length) return s;
            if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n);
        }

        private static string Clean(string s)
        {
            s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return s.Length > 120 ? s.Substring(0, 120) : s;
        }

        /// <summary>v5.1: paths, user / computer name, e-mail, IP removed (NetScrub, shared with the crash log).</summary>
        public static string Scrub(string s) { return NetScrub.Text(s); }

        /// <summary>Opens the URL in the default browser. False if Windows could not open it (caller shows the URL to copy).</summary>
        public static bool Open(string url)
        {
            if (url == null || !url.StartsWith(NetConfig.NewIssueUrl + "?", StringComparison.Ordinal)) return false;
            try { using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { } return true; }
            catch (Exception) { return false; }
        }
    }
}
