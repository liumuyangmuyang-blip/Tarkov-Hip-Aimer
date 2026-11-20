// v4.4 (W9) NET: shared pieces for 检查更新 / 反馈 / 在线统计. Pure (no WinForms, no real network here):
//   IHttp      every network call goes through this, so tests use a fake; the real one is NetHttp (Net.Http.cs)
//   NetVersion "v4.4" / "v4.4.1" parse + compare
//   NetHash    SHA-256 of a file, "sha256: <hex>" line in a release body
//   NetPrefs   the small net-prefs.json next to settings.json (自动检查更新, skipped version, last check, 统计 opt-in + id)
// Accounts: only Leo's PERSONAL GitHub repo and his PERSONAL worker URL. No token, key or secret anywhere.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TarkovCrosshair
{
    /// <summary>Result of one HTTP call. Status 0 = no answer (offline, timeout, TLS error); Error says why.</summary>
    internal sealed class HttpResult
    {
        public int Status;
        public string Body;
        public string Error;
        public bool Ok { get { return Status >= 200 && Status < 300; } }
        public static HttpResult Fail(string why) { HttpResult r = new HttpResult(); r.Status = 0; r.Body = null; r.Error = why; return r; }
        public static HttpResult Of(int status, string body) { HttpResult r = new HttpResult(); r.Status = status; r.Body = body; return r; }
    }

    /// <summary>All network access. Implementations never throw; failures come back as HttpResult.Fail / false.</summary>
    internal interface IHttp
    {
        HttpResult Get(string url, int timeoutMs);
        HttpResult PostJson(string url, string json, int timeoutMs);
        /// <summary>Download to path (overwrites). False on any failure or when more than maxBytes arrive (the file is deleted).</summary>
        bool Download(string url, string path, int timeoutMs, long maxBytes, out string error);
    }

    internal static class NetConfig
    {
        /// <summary>Leo's PERSONAL GitHub repo (owner/name).</summary>
        public const string Repo = "liumuyangmuyang-blip/Tarkov-Hip-Aimer";
        public const string ApiBase = "https://api.github.com/repos/" + Repo;
        public const string LatestReleaseUrl = ApiBase + "/releases/latest";
        public const string ReleasesUrl = ApiBase + "/releases?per_page=100";
        public const string NewIssueUrl = "https://github.com/" + Repo + "/issues/new";
        public const string UserAgent = "TarkovHipAimer-Updater";
        public const int TimeoutMs = 5000;
        /// <summary>Release zips are about 1 MB; anything far bigger is not ours.</summary>
        public const long MaxZipBytes = 64L * 1024 * 1024;
        public const int DownloadTimeoutMs = 60000;
    }

    /// <summary>Version tags: "v4.4", "v4.4.1", "4.4" (leading v optional). Missing parts count as 0.</summary>
    internal static class NetVersion
    {
        private static readonly Regex Rx = new Regex(@"^\s*[vV]?(\d{1,4})\.(\d{1,4})(?:\.(\d{1,4}))?\s*$");

        public static bool TryParse(string tag, out int[] parts)
        {
            parts = null;
            if (tag == null) return false;
            Match m = Rx.Match(tag);
            if (!m.Success) return false;
            parts = new int[3];
            for (int k = 0; k < 3; k++)
                parts[k] = m.Groups[k + 1].Success ? int.Parse(m.Groups[k + 1].Value, CultureInfo.InvariantCulture) : 0;
            return true;
        }

        /// <summary>&lt;0 a older, 0 same, &gt;0 a newer. Unparseable sorts below everything (never offered as an update).</summary>
        public static int Compare(string a, string b)
        {
            int[] x, y;
            bool ox = TryParse(a, out x), oy = TryParse(b, out y);
            if (!ox && !oy) return 0;
            if (!ox) return -1;
            if (!oy) return 1;
            for (int k = 0; k < 3; k++) if (x[k] != y[k]) return x[k] < y[k] ? -1 : 1;
            return 0;
        }

        public static bool IsNewer(string candidate, string running) { return TryParse(candidate, out _dummy) && Compare(candidate, running) > 0; }
        private static int[] _dummy;

        /// <summary>"v4.4.0" -> "v4.4"; "4.4.1" -> "v4.4.1". Unparseable comes back trimmed.</summary>
        public static string Normal(string tag)
        {
            int[] p;
            if (!TryParse(tag, out p)) return (tag ?? "").Trim();
            return p[2] == 0 ? "v" + p[0] + "." + p[1] : "v" + p[0] + "." + p[1] + "." + p[2];
        }
    }

    internal static class NetHash
    {
        private static readonly Regex ShaLine = new Regex(@"(?im)^[ \t>*`-]*sha-?256[ \t]*[:=][ \t]*`?([0-9a-f]{64})`?[ \t]*$");

        /// <summary>The 64-hex sha256 from a release body line "sha256: &lt;hex&gt;" (case-insensitive), lower case; null if none
        /// or if two different hashes are listed (ambiguous = refuse).</summary>
        public static string FromReleaseBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            string found = null;
            foreach (Match m in ShaLine.Matches(body))
            {
                string h = m.Groups[1].Value.ToLowerInvariant();
                if (found != null && found != h) return null;
                found = h;
            }
            return found;
        }

        public static string OfBytes(byte[] data)
        {
            using (SHA256 s = SHA256.Create()) return Hex(s.ComputeHash(data));
        }

        public static string OfFile(string path)
        {
            using (SHA256 s = SHA256.Create())
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Hex(s.ComputeHash(f));
        }

        /// <summary>True only when the file exists and its sha256 equals expectedHex (any case). Never throws.</summary>
        public static bool Verify(string path, string expectedHex)
        {
            if (string.IsNullOrEmpty(expectedHex) || expectedHex.Length != 64) return false;
            try { return string.Equals(OfFile(path), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        private static string Hex(byte[] h)
        {
            StringBuilder b = new StringBuilder(h.Length * 2);
            foreach (byte x in h) b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return b.ToString();
        }
    }

    /// <summary>net-prefs.json next to settings.json. Kept out of settings.json so the settings format does not change.
    /// Unreadable / missing file = defaults (自动检查更新 on, 统计 not asked, off).</summary>
    internal sealed class NetPrefs
    {
        public const string FileName = "net-prefs.json";

        public bool AutoCheck = true;
        public string SkipVersion = "";
        public long LastCheckUtcTicks;
        public bool UsageAsked;
        public bool UsageOptIn;
        public string UsageId = "";

        public string Serialize()
        {
            return "{\n"
                + "  \"autoCheck\": " + (AutoCheck ? "true" : "false") + ",\n"
                + "  \"skipVersion\": " + MiniJson.Quote(SkipVersion ?? "") + ",\n"
                + "  \"lastCheckUtcTicks\": " + LastCheckUtcTicks.ToString(CultureInfo.InvariantCulture) + ",\n"
                + "  \"usageAsked\": " + (UsageAsked ? "true" : "false") + ",\n"
                + "  \"usageOptIn\": " + (UsageOptIn ? "true" : "false") + ",\n"
                + "  \"usageId\": " + MiniJson.Quote(UsageId ?? "") + "\n}\n";
        }

        public static NetPrefs Parse(string json)
        {
            NetPrefs p = new NetPrefs();
            object o = MiniJson.TryParse(json);
            if (MiniJson.Obj(o) == null) return p;
            p.AutoCheck = MiniJson.Bool(o, "autoCheck", true);
            p.SkipVersion = MiniJson.Str(o, "skipVersion") ?? "";
            object t = MiniJson.Get(o, "lastCheckUtcTicks");
            if (t is double) { double d = (double)t; if (d >= 0 && d <= DateTime.MaxValue.Ticks) p.LastCheckUtcTicks = (long)d; }
            p.UsageAsked = MiniJson.Bool(o, "usageAsked", false);
            p.UsageOptIn = MiniJson.Bool(o, "usageOptIn", false) && p.UsageAsked; // opt-in only counts after the question
            string id = MiniJson.Str(o, "usageId") ?? "";
            Guid g;
            p.UsageId = Guid.TryParse(id, out g) ? g.ToString("D") : "";
            return p;
        }

        public static string PathIn(string userDir) { return Path.Combine(userDir, FileName); }

        public static NetPrefs Load(string path)
        {
            try { return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new NetPrefs(); }
            catch (Exception) { return new NetPrefs(); }
        }

        /// <summary>Write via a temp file + replace. False on failure (prefs simply stay in memory).</summary>
        public bool Save(string path)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Serialize(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The random 统计 id, created once (only when the user said yes).</summary>
        public string EnsureUsageId()
        {
            Guid g;
            if (!Guid.TryParse(UsageId ?? "", out g)) UsageId = Guid.NewGuid().ToString("D");
            return UsageId;
        }
    }
}
