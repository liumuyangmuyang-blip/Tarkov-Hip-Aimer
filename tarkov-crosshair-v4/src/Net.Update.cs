// v4.4 (W9) NET: 检查更新 logic. Pure: parses the GitHub "latest release" reply and decides whether to offer it.
// Network goes through IHttp (tests use a fake). Threading + dialog are in Net.UpdateUi.cs, install in Net.UpdateInstall.cs.
// Rules: checked at app start (30 s after it, v5.1) and again once 6 h have passed while running (v5.1; was 24 h), only when 自动检查更新 is on; offline / any
// error = silent; a release is offered only when it is newer, not skipped, and has a zip asset + a "sha256: <hex>" line.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair
{
    internal sealed class ReleaseInfo
    {
        public string Tag = "";       // normalised, e.g. "v4.4"
        public string RawTag = "";
        public string ZipName = "";
        public string ZipUrl = "";
        public long ZipSize;
        public string Sha256;         // lower-case hex or null
        public string PageUrl = "";   // the release page (html_url)
        public bool Usable { get { return ZipUrl.Length > 0 && Sha256 != null; } }

        /// <summary>From one release object of the GitHub API. Null for drafts, pre-releases and unparseable tags.</summary>
        public static ReleaseInfo FromJson(object rel)
        {
            if (MiniJson.Obj(rel) == null) return null;
            if (MiniJson.Bool(rel, "draft", false) || MiniJson.Bool(rel, "prerelease", false)) return null;
            string tag = MiniJson.Str(rel, "tag_name");
            int[] p;
            if (!NetVersion.TryParse(tag, out p)) return null;
            ReleaseInfo r = new ReleaseInfo();
            r.RawTag = tag;
            r.Tag = NetVersion.Normal(tag);
            r.Sha256 = NetHash.FromReleaseBody(MiniJson.Str(rel, "body"));
            string page = MiniJson.Str(rel, "html_url") ?? "";
            r.PageUrl = page.StartsWith("https://github.com/" + NetConfig.Repo + "/", StringComparison.OrdinalIgnoreCase) ? page : "";
            List<object> assets = MiniJson.Arr(MiniJson.Get(rel, "assets"));
            if (assets != null)
            {
                int best = -1;
                foreach (object a in assets)
                {
                    string name = MiniJson.Str(a, "name") ?? "";
                    string url = MiniJson.Str(a, "browser_download_url") ?? "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsOurDownloadUrl(url)) continue;
                    int score = name.IndexOf("TarkovHipAimer", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                    if (score <= best) continue;
                    best = score;
                    r.ZipName = name; r.ZipUrl = url; r.ZipSize = MiniJson.Num(a, "size", 0);
                }
            }
            return r;
        }

        /// <summary>Only https downloads from Leo's own repo (GitHub then redirects to its own storage host).</summary>
        public static bool IsOurDownloadUrl(string url)
        {
            return url != null && url.StartsWith("https://github.com/" + NetConfig.Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal enum UpdateDecision { NoCheck, UpToDate, Offer, Skipped, Unusable, Failed }

    internal sealed class UpdateCheckResult
    {
        public UpdateDecision Decision;
        public ReleaseInfo Release;
        public string Error;
    }

    internal enum UpdateChoice { Update, Later, SkipThisVersion }

    internal static class UpdateChecker
    {
        public static readonly TimeSpan Interval = TimeSpan.FromHours(6); // v5.1 (W28): 6 h so a pushed update reaches running apps the same day

        /// <summary>startup = the one check per app start; later calls need Interval (6 h) since the last check.</summary>
        public static bool ShouldCheck(NetPrefs p, DateTime nowUtc, bool startup)
        {
            if (p == null || !p.AutoCheck) return false;
            if (startup) return true;
            long elapsed = nowUtc.Ticks - p.LastCheckUtcTicks;
            return elapsed < 0 || elapsed >= Interval.Ticks;
        }

        /// <summary>One check (blocking; call on a background thread). Stamps p.LastCheckUtcTicks on any answer from GitHub.</summary>
        public static UpdateCheckResult Check(IHttp http, string runningVersion, NetPrefs p, DateTime nowUtc)
        {
            UpdateCheckResult res = new UpdateCheckResult();
            HttpResult h = http.Get(NetConfig.LatestReleaseUrl, NetConfig.TimeoutMs);
            if (h.Status != 0 && p != null) p.LastCheckUtcTicks = nowUtc.Ticks;
            if (!h.Ok) { res.Decision = UpdateDecision.Failed; res.Error = h.Status == 0 ? h.Error : "HTTP " + h.Status; return res; }
            return Decide(MiniJson.TryParse(h.Body), runningVersion, p);
        }

        public static UpdateCheckResult Decide(object latestJson, string runningVersion, NetPrefs p)
        {
            UpdateCheckResult res = new UpdateCheckResult();
            ReleaseInfo r = ReleaseInfo.FromJson(latestJson);
            res.Release = r;
            if (r == null) { res.Decision = UpdateDecision.Failed; res.Error = "no release"; return res; }
            if (!NetVersion.IsNewer(r.Tag, runningVersion)) { res.Decision = UpdateDecision.UpToDate; return res; }
            if (p != null && NetVersion.Compare(p.SkipVersion, r.Tag) == 0 && NetVersion.TryParse(p.SkipVersion, out dummy)) { res.Decision = UpdateDecision.Skipped; return res; }
            if (!r.Usable) { res.Decision = UpdateDecision.Unusable; res.Error = r.ZipUrl.Length == 0 ? "no zip asset" : "no sha256 line"; return res; }
            res.Decision = UpdateDecision.Offer;
            return res;
        }
        private static int[] dummy;

        /// <summary>Records the user's answer. Only 跳过这个版本 changes prefs.</summary>
        public static void Apply(NetPrefs p, ReleaseInfo r, UpdateChoice c)
        {
            if (p != null && r != null && c == UpdateChoice.SkipThisVersion) p.SkipVersion = r.Tag;
        }

        // ---- user-facing text (Chinese; no internal words)
        public static string PromptText(string tag) { return "有新版 " + tag + "，更新吗？"; }
        public const string BtnYes = "是";
        public const string BtnLater = "以后再说";
        public const string BtnSkip = "跳过这个版本";
        public const string AutoCheckLabel = "自动检查更新";
    }
}
