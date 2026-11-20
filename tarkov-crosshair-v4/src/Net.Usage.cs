// v4.4 (W9) NET: 统计. v5.1 (W28): sessions (how long each run lasts), cumulative users, total hours.
//   downloads  GitHub releases API (public, no token): sum of asset download_count per release
//   online     v5.1 (Leo 21 Nov 2026): ON by default (opt-out in 常规 / wizard; no question). While running and on: POST {id, s, v} to Leo's
//              personal worker every 15 min (id = random GUID made once, s = random GUID made per app run, v = app
//              version) and {id, s, v, end:true} once at exit. Nothing else is sent; the worker stores no IP.
//              WorkerUrl empty = the whole online feature is hidden and nothing is ever sent.
// Pure except the timer; every network call goes through IHttp.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace TarkovCrosshair
{
    internal static class UsageConfig
    {
        /// <summary>Leo's PERSONAL Cloudflare worker, e.g. "https://thaim-usage.&lt;name&gt;.workers.dev" (no trailing slash).
        /// Empty = 在线统计 hidden: no question, no heartbeat, the monitor shows downloads only.</summary>
        public const string WorkerUrl = "https://thaim.dev.workers.dev"; // Leo deployed 21 Nov 2026

        public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(15);
        public const int TimeoutMs = 5000;

        public static bool Enabled { get { return IsValidWorkerUrl(WorkerUrl); } }

        public static bool IsValidWorkerUrl(string u)
        {
            return u != null && Regex.IsMatch(u, @"^https://[A-Za-z0-9.-]+(?::\d+)?$");
        }

        /// <summary>The one-time question (wizard or a small dialog). Default answer = AskNo.</summary>
        public const string AskTitle = "帮作者统计使用人数？";
        public const string AskText = "只发送：随机编号、版本号、每次用了多久。\r\n不发送用户名、电脑名、文件、IP 或游戏数据。以后可以在「常规」里改。";
        public const string AskYes = "可以";
        public const string AskNo = "不用";
        public const string SettingLabel = "发送匿名统计（随机编号、版本号、使用时长）";
        /// <summary>v5.1 (Leo 21 Nov 2026): the one-line notice next to the checkbox (常规 联网 and the wizard's last page).</summary>
        public const string Notice = "默认发送匿名统计（随机编号、版本号、使用时长），不含个人信息，可在这里关闭。";
        public const string CrashSettingLabel = "程序出错后，问我要不要把错误记录发给作者";
        /// <summary>Safety line for README / guide (W25): what the app sends and when.</summary>
        public const string SafetyLine = "只在检查更新和发送匿名统计（随机编号、版本号、使用时长，默认开，可在「常规」关闭）时联网。不上传个人信息，反馈和错误记录由你自己用邮件发给作者。";
    }

    /// <summary>The heartbeat. Tick is called once a minute by a pool timer (Start) or directly by tests. One session id per
    /// app run; a new one after a silence over 1 h (PC slept) or when the worker answers 409.</summary>
    internal sealed class UsageHeartbeat : IDisposable
    {
        public static readonly TimeSpan SleepGap = TimeSpan.FromHours(1);
        public const int EndTimeoutMs = 1200;

        private readonly IHttp http;
        private readonly NetPrefs prefs;
        private readonly string workerUrl, version;
        private long lastSentTicks = long.MinValue;
        private int running;
        private Timer timer;
        private string sessionId = Guid.NewGuid().ToString("D");
        public int Sent { get; private set; }
        public string SessionId { get { return sessionId; } }

        public UsageHeartbeat(IHttp http, NetPrefs prefs, string workerUrl, string version)
        {
            this.http = http; this.prefs = prefs; this.workerUrl = workerUrl; this.version = version;
        }

        public void Start()
        {
            if (timer != null) return;
            timer = new Timer(delegate { Tick(DateTime.UtcNow); }, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1));
        }

        private bool Allowed { get { return prefs != null && prefs.UsageAsked && prefs.UsageOptIn && UsageConfig.IsValidWorkerUrl(workerUrl); } }

        /// <summary>Sends when opted in, the worker URL is set and 15 min passed since the last send (first one right away).
        /// Returns true when a request was made. Opted out / no URL = nothing at all, not even a request.</summary>
        public bool Tick(DateTime nowUtc)
        {
            if (!Allowed) return false;
            if (lastSentTicks != long.MinValue && nowUtc.Ticks - lastSentTicks < UsageConfig.HeartbeatEvery.Ticks && nowUtc.Ticks >= lastSentTicks) return false;
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return false;
            try
            {
                if (lastSentTicks != long.MinValue && nowUtc.Ticks - lastSentTicks > SleepGap.Ticks) sessionId = Guid.NewGuid().ToString("D");
                lastSentTicks = nowUtc.Ticks; // also after a failure: no retry storm, next try in 15 min
                HttpResult r = http.PostJson(workerUrl + "/hb", Payload(prefs.EnsureUsageId(), sessionId, version, false), UsageConfig.TimeoutMs);
                Sent++;
                if (r != null && r.Status == 409)
                {
                    sessionId = Guid.NewGuid().ToString("D"); // the worker closed the old run (long silence): start a new one
                    http.PostJson(workerUrl + "/hb", Payload(prefs.EnsureUsageId(), sessionId, version, false), UsageConfig.TimeoutMs);
                    Sent++;
                }
                return true;
            }
            catch (Exception) { return true; }
            finally { Interlocked.Exchange(ref running, 0); }
        }

        /// <summary>At exit: one {end:true} so the run's length is exact. Only if a heartbeat went out this run. Blocks at most
        /// EndTimeoutMs (the caller runs it on a background thread and joins with a cap).</summary>
        public bool SendEnd()
        {
            if (!Allowed || lastSentTicks == long.MinValue) return false;
            try { http.PostJson(workerUrl + "/hb", Payload(prefs.EnsureUsageId(), sessionId, version, true), EndTimeoutMs); return true; }
            catch (Exception) { return false; }
        }

        public static string Payload(string id, string session, string version, bool end)
        {
            return "{\"id\":" + MiniJson.Quote(id) + ",\"s\":" + MiniJson.Quote(session) + ",\"v\":" + MiniJson.Quote(NetVersion.Normal(version))
                + (end ? ",\"end\":true}" : "}");
        }

        public void Dispose()
        {
            Timer t = timer; timer = null;
            if (t != null) t.Dispose();
        }
    }

    internal sealed class VersionCount
    {
        public string Version;
        public long Count;
        public VersionCount(string v, long c) { Version = v; Count = c; }
    }

    internal sealed class DownloadStats
    {
        public long Total;
        public readonly List<VersionCount> PerRelease = new List<VersionCount>(); // newest first
        public string Latest = "";

        /// <summary>From GET /releases (array). Drafts skipped; only .zip assets counted. Null on bad JSON.</summary>
        public static DownloadStats Parse(string json)
        {
            List<object> arr = MiniJson.Arr(MiniJson.TryParse(json));
            if (arr == null) return null;
            DownloadStats d = new DownloadStats();
            foreach (object rel in arr)
            {
                if (MiniJson.Obj(rel) == null || MiniJson.Bool(rel, "draft", false)) continue;
                string tag = NetVersion.Normal(MiniJson.Str(rel, "tag_name") ?? "?");
                long n = 0;
                List<object> assets = MiniJson.Arr(MiniJson.Get(rel, "assets"));
                if (assets != null)
                    foreach (object a in assets)
                        if ((MiniJson.Str(a, "name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            n += Math.Max(0, MiniJson.Num(a, "download_count", 0));
                d.PerRelease.Add(new VersionCount(tag, n));
                d.Total += n;
                if (!MiniJson.Bool(rel, "prerelease", false) && NetVersion.Compare(tag, d.Latest) > 0) d.Latest = tag;
            }
            d.PerRelease.Sort(delegate(VersionCount a, VersionCount b) { return NetVersion.Compare(b.Version, a.Version); });
            return d;
        }
    }

    internal sealed class OnlineStats
    {
        public long OnlineNow;     // seen in the last 20 min
        public long Online24h;     // seen in the last 24 h
        public long UsersTotal;    // installs that ever sent a heartbeat (said yes to 统计)
        public long Sessions7d;    // app runs started in the last 7 days
        public double AvgSessionMin7d;
        public double TotalHours;  // all runs ever
        public readonly List<VersionCount> ByVersion = new List<VersionCount>(); // last 24 h, most first

        /// <summary>From the worker's GET /stats. online_now / online_24h are required (a v4.4-style reply still reads; the
        /// v5.1 fields then stay 0). Null on bad JSON.</summary>
        public static OnlineStats Parse(string json)
        {
            object o = MiniJson.TryParse(json);
            if (MiniJson.Obj(o) == null) return null;
            if (!(MiniJson.Get(o, "online_now") is double) || !(MiniJson.Get(o, "online_24h") is double)) return null;
            OnlineStats s = new OnlineStats();
            s.OnlineNow = Math.Max(0, MiniJson.Num(o, "online_now", 0));
            s.Online24h = Math.Max(0, MiniJson.Num(o, "online_24h", 0));
            s.UsersTotal = Math.Max(0, MiniJson.Num(o, "users_total", 0));
            s.Sessions7d = Math.Max(0, MiniJson.Num(o, "sessions_7d", 0));
            s.AvgSessionMin7d = Dbl(o, "avg_session_min_7d");
            s.TotalHours = Dbl(o, "total_hours");
            Dictionary<string, object> bv = MiniJson.Obj(MiniJson.Get(o, "by_version"));
            if (bv != null)
                foreach (KeyValuePair<string, object> kv in bv)
                    if (kv.Value is double && kv.Key.Length <= 16) s.ByVersion.Add(new VersionCount(kv.Key, Math.Max(0, (long)(double)kv.Value)));
            s.ByVersion.Sort(delegate(VersionCount a, VersionCount b)
            {
                int c = b.Count.CompareTo(a.Count);
                return c != 0 ? c : NetVersion.Compare(b.Version, a.Version);
            });
            return s;
        }

        private static double Dbl(object o, string key)
        {
            object v = MiniJson.Get(o, key);
            if (!(v is double)) return 0;
            double d = (double)v;
            return double.IsNaN(d) || double.IsInfinity(d) || d < 0 ? 0 : Math.Min(d, 1e12);
        }
    }

    /// <summary>Everything the 统计 panel shows. Null parts = not available (offline, worker not set).</summary>
    internal sealed class MonitorSnapshot
    {
        public string Running = "";
        public DownloadStats Downloads;
        public OnlineStats Online;
        public bool OnlineConfigured;
        public DateTime FetchedLocal;
        public string DownloadsError, OnlineError;
    }

    internal interface IMonitorSource
    {
        /// <summary>Blocking; the panel calls it on a background thread.</summary>
        MonitorSnapshot Fetch();
    }

    internal sealed class MonitorSource : IMonitorSource
    {
        private readonly IHttp http;
        private readonly string running, workerUrl;

        public MonitorSource(IHttp http, string running, string workerUrl) { this.http = http; this.running = running; this.workerUrl = workerUrl; }

        public MonitorSnapshot Fetch()
        {
            MonitorSnapshot s = new MonitorSnapshot();
            s.Running = running;
            s.FetchedLocal = DateTime.Now;
            HttpResult r = http.Get(NetConfig.ReleasesUrl, NetConfig.TimeoutMs);
            if (r.Ok) { s.Downloads = DownloadStats.Parse(r.Body); if (s.Downloads == null) s.DownloadsError = "bad reply"; }
            else s.DownloadsError = r.Status == 0 ? "offline" : "HTTP " + r.Status;
            s.OnlineConfigured = UsageConfig.IsValidWorkerUrl(workerUrl);
            if (s.OnlineConfigured)
            {
                HttpResult o = http.Get(workerUrl + "/stats", UsageConfig.TimeoutMs);
                if (o.Ok) { s.Online = OnlineStats.Parse(o.Body); if (s.Online == null) s.OnlineError = "bad reply"; }
                else s.OnlineError = o.Status == 0 ? "offline" : "HTTP " + o.Status;
            }
            return s;
        }
    }
}
