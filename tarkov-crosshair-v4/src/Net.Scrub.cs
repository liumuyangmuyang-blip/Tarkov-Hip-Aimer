// v5.1 (W28) NET: removes personal details from text before it leaves the PC (feedback, crash logs). Pure.
//   paths      C:\Users\name\..., C:/..., \\server\share\... -> [路径]\Program.cs:line 12 when the path ends in a known
//              program / source file name, else [路径已隐藏] (a folder named after the user never survives)
//   names      the Windows user name and the computer name (whole word, any case) -> [用户名] / [电脑名]
//   e-mail     -> [邮箱已隐藏]      IPv4 -> [IP已隐藏]
// The worker scrubs paths / e-mail / IP a second time on its side.
using System;
using System.Text.RegularExpressions;

namespace TarkovCrosshair
{
    internal static class NetScrub
    {
        // Folders may contain single spaces (C:\Users\Zhang San\...) as long as a separator follows; the last part may not.
        // Full-width punctuation (，。；、：) ends a path, so a path can never run on into the sentence after it.
        private const string Seg = @"[^\s\\/""'<>|:*?，。；、：]+(?: [^\s\\/""'<>|:*?，。；、：]+)*[\\/]";
        private const string Last = @"[^\\/\s""'<>|:*?]*";
        private static readonly Regex WinPath = new Regex(@"[A-Za-z]:[\\/](?:" + Seg + ")*" + Last);
        private static readonly Regex UncPath = new Regex(@"\\\\(?:" + Seg + ")*" + Last);
        private static readonly Regex FileName = new Regex(@"^([A-Za-z0-9_.-]+\.(?:cs|dll|exe|json|txt|log|png|config))(?![A-Za-z0-9_])(.*)$", RegexOptions.IgnoreCase);
        private static readonly Regex Mail = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
        private static readonly Regex Ip4 = new Regex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b");

        public const string PathMark = "[路径已隐藏]";

        /// <summary>Scrub with this PC's user name and computer name.</summary>
        public static string Text(string s) { return Text(s, SafeEnv(true), SafeEnv(false)); }

        public static string Text(string s, string userName, string machineName)
        {
            s = s ?? "";
            s = WinPath.Replace(s, KeepFile);
            s = UncPath.Replace(s, KeepFile);
            s = Mail.Replace(s, "[邮箱已隐藏]");
            s = Ip4.Replace(s, "[IP已隐藏]");
            s = Word(s, machineName, "[电脑名]"); // first: a computer name often contains the user name (LEO-PC)
            s = Word(s, userName, "[用户名]");
            return s;
        }

        private static string KeepFile(Match m)
        {
            string v = m.Value;
            int cut = Math.Max(v.LastIndexOf('\\'), v.LastIndexOf('/'));
            string last = cut >= 0 ? v.Substring(cut + 1) : v;
            Match f = FileName.Match(last);
            // the file name stays; whatever follows it on the same "word" (，。 etc.) is ordinary text, not path
            return f.Success ? "[路径]\\" + f.Groups[1].Value + f.Groups[2].Value : PathMark;
        }

        /// <summary>Whole-word, case-insensitive (so a 2-letter name does not eat parts of other words).</summary>
        private static string Word(string s, string name, string mark)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length < 2) return s;
            string rx = @"(?<![A-Za-z0-9])" + Regex.Escape(name.Trim()) + @"(?![A-Za-z0-9])";
            return Regex.Replace(s, rx, mark, RegexOptions.IgnoreCase);
        }

        /// <summary>At most max chars, cut on a whole character (no half surrogate pair).</summary>
        public static string Cap(string s, int max)
        {
            s = s ?? "";
            if (s.Length <= max) return s;
            int n = max;
            if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n);
        }

        private static string SafeEnv(bool user)
        {
            try { return (user ? Environment.UserName : Environment.MachineName) ?? ""; } catch (Exception) { return ""; }
        }
    }
}
