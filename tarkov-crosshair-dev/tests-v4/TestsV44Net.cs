// v4.4 (W9) NET: 检查更新 / 反馈 / 统计 pure logic. No real network: every call goes to FakeHttp.
// v5.1 (W28): moved from v44-staging; heartbeat now {id, s, v}, update interval 6 h. Compile items in Tests.csproj (v5.1 block): Net.Json.cs, Net.Core.cs, Net.Update.cs, Net.UpdateInstall.cs,
// Net.Feedback.cs, Net.Usage.cs, TestsV44Net.cs. The Windows side (real HTTP, exe rename while running, restart) is UNVERIFIED.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        sealed class FakeHttp : IHttp
        {
            public readonly Dictionary<string, HttpResult> Gets = new Dictionary<string, HttpResult>();
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Posts = new List<string>();
            public byte[] DownloadBytes;
            public bool Offline;
            /// <summary>v5.1: status codes for the next POSTs (then 204).</summary>
            public readonly Queue<int> PostStatuses = new Queue<int>();

            public HttpResult Get(string url, int timeoutMs)
            {
                Calls.Add("GET " + url);
                if (Offline) return HttpResult.Fail("ConnectFailure");
                HttpResult r;
                return Gets.TryGetValue(url, out r) ? r : HttpResult.Of(404, "{}");
            }

            public HttpResult PostJson(string url, string json, int timeoutMs)
            {
                Calls.Add("POST " + url); Posts.Add(json);
                if (Offline) return HttpResult.Fail("ConnectFailure");
                return HttpResult.Of(PostStatuses.Count > 0 ? PostStatuses.Dequeue() : 204, "");
            }

            public bool Download(string url, string path, int timeoutMs, long maxBytes, out string error)
            {
                Calls.Add("DL " + url);
                error = null;
                if (Offline || DownloadBytes == null) { error = "ConnectFailure"; return false; }
                File.WriteAllBytes(path, DownloadBytes);
                return true;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V44NetInit() { V44NetTests(); }

        static string RelJson(string tag, string body, string assetName, bool draft)
        {
            return "{\"tag_name\":" + MiniJson.Quote(tag) + ",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":false,"
                + "\"html_url\":\"https://github.com/" + NetConfig.Repo + "/releases/tag/" + tag + "\","
                + "\"body\":" + MiniJson.Quote(body) + ",\"assets\":[{\"name\":" + MiniJson.Quote(assetName) + ",\"size\":1000,\"download_count\":7,"
                + "\"browser_download_url\":\"https://github.com/" + NetConfig.Repo + "/releases/download/" + tag + "/" + assetName + "\"}]}";
        }

        static byte[] MakeZip(byte[] exe, string exeName)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive z = new ZipArchive(ms, ZipArchiveMode.Create, true, Encoding.UTF8))
                {
                    ZipArchiveEntry r = z.CreateEntry("TarkovHipAimer/说明 README.txt");
                    using (Stream s = r.Open()) { byte[] t = Encoding.UTF8.GetBytes("readme"); s.Write(t, 0, t.Length); }
                    if (exe != null)
                    {
                        ZipArchiveEntry e = z.CreateEntry("TarkovHipAimer/" + exeName, CompressionLevel.Optimal);
                        using (Stream s = e.Open()) s.Write(exe, 0, exe.Length);
                    }
                }
                return ms.ToArray();
            }
        }

        static byte[] FakeExe(byte fill)
        {
            byte[] b = new byte[50000];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(fill + i % 7);
            b[0] = (byte)'M'; b[1] = (byte)'Z';
            return b;
        }

        static void V44NetTests()
        {
            // ---- N1 version compare
            {
                bool ok = NetVersion.Compare("v4.4", "v4.3") > 0 && NetVersion.Compare("v4.4", "4.4.0") == 0
                    && NetVersion.Compare("v4.10", "v4.9") > 0 && NetVersion.Compare("v4.4.1", "v4.4") > 0
                    && NetVersion.Compare("v4.3", "v4.4") < 0 && !NetVersion.IsNewer("latest", "v4.3")
                    && !NetVersion.IsNewer("v4.3", "v4.3") && NetVersion.Normal("4.4.0") == "v4.4" && NetVersion.Normal("V4.4.2") == "v4.4.2";
                Check("V44-N1 version tags: v4.10 > v4.9, v4.4.1 > v4.4, v4.4 = 4.4.0, a non-version tag is never newer", ok, "");
            }

            // ---- N2 sha256 line + verify good / bad
            {
                string dir = Path.Combine(Path.GetTempPath(), "v44n2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, "a.zip");
                File.WriteAllBytes(f, Encoding.ASCII.GetBytes("abc"));
                const string abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
                string body = "新版说明\n\nsha256: " + abc.ToUpperInvariant() + "\n";
                bool parse = NetHash.FromReleaseBody(body) == abc && NetHash.FromReleaseBody("- SHA256: `" + abc + "`") == abc
                    && NetHash.FromReleaseBody("no hash here") == null
                    && NetHash.FromReleaseBody("sha256: " + abc + "\nsha256: " + abc.Replace('b', 'c')) == null; // two different = refuse
                bool good = NetHash.Verify(f, abc);
                bool bad = !NetHash.Verify(f, abc.Replace('a', 'b')) && !NetHash.Verify(f, "") && !NetHash.Verify(Path.Combine(dir, "missing"), abc);
                File.Delete(f); Directory.Delete(dir);
                Check("V44-N2 sha256: the 'sha256: <hex>' line is read (any case, backticks ok, two different hashes = refused); right hash verifies, wrong/empty/missing file fails",
                    parse && good && bad, "parse=" + parse + " good=" + good + " bad=" + bad);
            }

            // ---- N3 decide: offer / up to date / skipped / unusable / offline; 24 h throttle
            {
                string sha = new string('a', 64);
                NetPrefs p = new NetPrefs();
                FakeHttp h = new FakeHttp();
                h.Gets[NetConfig.LatestReleaseUrl] = HttpResult.Of(200, RelJson("v4.4", "sha256: " + sha, "TarkovHipAimer-v4.4.zip", false));
                DateTime now = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
                UpdateCheckResult r1 = UpdateChecker.Check(h, "v4.3", p, now);
                bool offer = r1.Decision == UpdateDecision.Offer && r1.Release.Tag == "v4.4" && r1.Release.Sha256 == sha
                    && r1.Release.ZipUrl.EndsWith("/v4.4/TarkovHipAimer-v4.4.zip") && p.LastCheckUtcTicks == now.Ticks;
                bool same = UpdateChecker.Check(h, "v4.4", p, now).Decision == UpdateDecision.UpToDate;
                UpdateChecker.Apply(p, r1.Release, UpdateChoice.Later);
                bool later = p.SkipVersion == "" && UpdateChecker.Check(h, "v4.3", p, now).Decision == UpdateDecision.Offer;
                UpdateChecker.Apply(p, r1.Release, UpdateChoice.SkipThisVersion);
                bool skipped = UpdateChecker.Check(h, "v4.3", p, now).Decision == UpdateDecision.Skipped;
                h.Gets[NetConfig.LatestReleaseUrl] = HttpResult.Of(200, RelJson("v4.5", "sha256: " + sha, "TarkovHipAimer-v4.5.zip", false));
                bool nextOffered = UpdateChecker.Check(h, "v4.3", p, now).Decision == UpdateDecision.Offer; // skip is per version
                h.Gets[NetConfig.LatestReleaseUrl] = HttpResult.Of(200, RelJson("v4.6", "no hash", "TarkovHipAimer-v4.6.zip", false));
                bool noSha = UpdateChecker.Check(h, "v4.3", p, now).Decision == UpdateDecision.Unusable;
                h.Gets[NetConfig.LatestReleaseUrl] = HttpResult.Of(200, RelJson("v4.6", "sha256: " + sha, "other.zip", false).Replace("/releases/download/", "/evil/"));
                bool foreignUrl = UpdateChecker.Check(h, "v4.3", p, now).Decision == UpdateDecision.Unusable;
                h.Gets[NetConfig.LatestReleaseUrl] = HttpResult.Of(200, "not json");
                bool badJson = UpdateChecker.Check(h, "v4.3", p, now).Decision == UpdateDecision.Failed;
                h.Offline = true;
                long before = p.LastCheckUtcTicks;
                bool offline = UpdateChecker.Check(h, "v4.3", p, now.AddDays(3)).Decision == UpdateDecision.Failed && p.LastCheckUtcTicks == before;
                NetPrefs q = new NetPrefs(); q.LastCheckUtcTicks = now.Ticks;
                bool thr = UpdateChecker.ShouldCheck(q, now.AddHours(1), true) && !UpdateChecker.ShouldCheck(q, now.AddHours(5.9), false)
                    && UpdateChecker.ShouldCheck(q, now.AddHours(6), false);
                q.AutoCheck = false;
                bool off = !UpdateChecker.ShouldCheck(q, now.AddDays(5), true);
                Check("V44-N3 update decision: newer + zip + sha256 = offer; same = up to date; 以后再说 asks again, 跳过这个版本 skips only that tag; no sha / foreign download URL = not offered; bad JSON / offline = silent; start + every 6 h (v5.1); 自动检查更新 off = no check",
                    offer && same && later && skipped && nextOffered && noSha && foreignUrl && badJson && offline && thr && off,
                    "offer=" + offer + " same=" + same + " later=" + later + " skip=" + skipped + " next=" + nextOffered + " noSha=" + noSha + " url=" + foreignUrl + " json=" + badJson + " offline=" + offline + " thr=" + thr + " off=" + off);
            }

            // ---- N4 install: verified zip swaps the exe, keeps the old one as .old, never touches settings / 准星样式; cleanup next start
            {
                string root = Path.Combine(Path.GetTempPath(), "v44n4-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                string exe = Path.Combine(root, "准星 TarkovHipAimer.exe");
                string settings = Path.Combine(root, "settings.json");
                string xh = Path.Combine(root, "准星样式 Crosshairs");
                Directory.CreateDirectory(xh);
                File.WriteAllText(Path.Combine(xh, "dot.png"), "png");
                File.WriteAllText(settings, "{\"keep\":1}");
                byte[] oldExe = FakeExe(1), newExe = FakeExe(9);
                File.WriteAllBytes(exe, oldExe);
                byte[] zip = MakeZip(newExe, "准星 TarkovHipAimer.exe");
                ReleaseInfo rel = new ReleaseInfo();
                rel.Tag = "v4.4"; rel.ZipUrl = "https://github.com/" + NetConfig.Repo + "/releases/download/v4.4/x.zip"; rel.Sha256 = NetHash.OfBytes(zip);
                FakeHttp h = new FakeHttp(); h.DownloadBytes = zip;

                // bad hash first: nothing changes
                ReleaseInfo badRel = new ReleaseInfo(); badRel.Tag = rel.Tag; badRel.ZipUrl = rel.ZipUrl; badRel.Sha256 = new string('0', 64);
                InstallResult bad = UpdateInstaller.Install(h, badRel, exe, root);
                bool badKept = bad.Step == InstallStep.Hash && Same(File.ReadAllBytes(exe), oldExe) && !File.Exists(exe + ".old") && !File.Exists(exe + ".new")
                    && bad.UserMessage.IndexOf("旧版本照常能用", StringComparison.Ordinal) >= 0;

                // zip without the exe: nothing changes
                byte[] noExe = MakeZip(null, "");
                FakeHttp h2 = new FakeHttp(); h2.DownloadBytes = noExe;
                ReleaseInfo r2 = new ReleaseInfo(); r2.Tag = "v4.4"; r2.ZipUrl = rel.ZipUrl; r2.Sha256 = NetHash.OfBytes(noExe);
                bool zipKept = UpdateInstaller.Install(h2, r2, exe, root).Step == InstallStep.Zip && Same(File.ReadAllBytes(exe), oldExe);

                // offline: nothing changes
                FakeHttp h3 = new FakeHttp(); h3.Offline = true;
                bool dlKept = UpdateInstaller.Install(h3, rel, exe, root).Step == InstallStep.Download && Same(File.ReadAllBytes(exe), oldExe);

                InstallResult ok = UpdateInstaller.Install(h, rel, exe, root);
                bool swapped = ok.Ok && Same(File.ReadAllBytes(exe), newExe) && Same(File.ReadAllBytes(exe + ".old"), oldExe) && !File.Exists(exe + ".new");
                bool untouched = File.ReadAllText(settings) == "{\"keep\":1}" && File.ReadAllText(Path.Combine(xh, "dot.png")) == "png";
                int removed = UpdateInstaller.CleanupAfterUpdate(exe, root);
                bool cleaned = removed >= 2 && !File.Exists(exe + ".old") && !Directory.Exists(Path.Combine(root, UpdateInstaller.DownloadFolderName))
                    && File.Exists(settings) && File.Exists(Path.Combine(xh, "dot.png"));

                // swap failure (the new file vanished): the old exe is put back
                File.WriteAllBytes(exe, oldExe);
                InstallResult sf = UpdateInstaller.Swap(exe, exe + ".new-missing");
                bool restored = sf.Step == InstallStep.Swap && Same(File.ReadAllBytes(exe), oldExe);

                foreach (string f in Directory.GetFiles(xh)) File.Delete(f);
                Directory.Delete(xh);
                foreach (string f in Directory.GetFiles(root)) File.Delete(f);
                Directory.Delete(root);
                Check("V44-N4 install: wrong sha256 / zip without the exe / offline keep the old exe untouched; a verified zip swaps in the new exe (old kept as .old), settings.json + 准星样式 untouched; next start removes .old + the download; a failed swap puts the old exe back",
                    badKept && zipKept && dlKept && swapped && untouched && cleaned && restored,
                    "bad=" + badKept + " zip=" + zipKept + " dl=" + dlKept + " swap=" + swapped + " untouched=" + untouched + " cleaned=" + cleaned + "(" + removed + ") restored=" + restored);
            }

            // ---- N5 zip reader: stored + deflate entries, CRC checked, single exe only
            {
                byte[] zip = MakeZip(FakeExe(3), "准星 TarkovHipAimer.exe");
                ZipMini z = new ZipMini(zip);
                ZipMini.Entry e = UpdateInstaller.FindExe(z);
                bool read = e != null && e.Name == "TarkovHipAimer/准星 TarkovHipAimer.exe" && Same(z.Read(e), FakeExe(3));
                bool crc = false;
                try
                {
                    // flip a byte inside the exe's compressed data
                    byte[] b2 = (byte[])zip.Clone();
                    long lh = e.LocalOffset;
                    int start = (int)(lh + 30 + (b2[lh + 26] | b2[lh + 27] << 8) + (b2[lh + 28] | b2[lh + 29] << 8));
                    b2[start + 10] ^= 0x55;
                    ZipMini z2 = new ZipMini(b2);
                    z2.Read(UpdateInstaller.FindExe(z2));
                }
                catch (Exception) { crc = true; }
                bool notZip = false;
                try { new ZipMini(Encoding.ASCII.GetBytes("hello world, definitely not a zip file at all")); } catch (Exception) { notZip = true; }
                Check("V44-N5 zip reader: reads the exe entry (UTF-8 name, deflate) byte-exact; a damaged entry fails its CRC; non-zip input is refused",
                    read && crc && notZip, "read=" + read + " crc=" + crc + " notZip=" + notZip);
            }

            // ---- N6 feedback URL: label, no paths / user name, capped
            {
                FeedbackEnv env = new FeedbackEnv(); env.AppVersion = "v4.4"; env.Windows = "Windows 11 Pro 23H2 (build 22631) x64"; env.Screens = "2160x1440 @200%";
                string shortUrl = FeedbackUrl.Build(FeedbackKind.Bug, "准星不显示\n打开 C:\\Users\\someone\\Desktop\\x.log 后", env);
                string dec = Uri.UnescapeDataString(shortUrl);
                bool basic = shortUrl.StartsWith(NetConfig.NewIssueUrl + "?labels=bug&title=", StringComparison.Ordinal)
                    && dec.Contains("[问题] 准星不显示") && dec.Contains("版本: v4.4") && dec.Contains("2160x1440 @200%")
                    && !dec.Contains("someone") && !dec.Contains("C:\\") && dec.Contains("[路径]\\x.log"); // v5.1: file name kept, folders hidden
                StringBuilder big = new StringBuilder();
                for (int i = 0; i < 4000; i++) big.Append("很长的建议😀");
                string longUrl = FeedbackUrl.Build(FeedbackKind.Idea, big.ToString(), env);
                string ld = Uri.UnescapeDataString(longUrl);
                bool capped = longUrl.Length <= FeedbackUrl.MaxUrlLength && longUrl.Length > FeedbackUrl.MaxUrlLength - 300
                    && ld.Contains("太长，已截断") && ld.Contains("版本: v4.4") && longUrl.Contains("labels=idea");
                string t = FeedbackUrl.Title(FeedbackKind.Idea, new string('长', 300));
                bool title = t.Length <= FeedbackUrl.MaxTitleChars && t.StartsWith("[建议] ", StringComparison.Ordinal)
                    && FeedbackUrl.Title(FeedbackKind.Bug, "   ") == "[问题] 程序问题";
                bool noOpenForeign = !FeedbackUrl.Open("https://example.com/") && !FeedbackUrl.Open(null);
                Check("V44-N6 反馈 URL: GitHub new-issue page with label bug/idea, version + Windows + screen in the body, pasted paths hidden; long text cut with a mark so the URL stays <= 6000 chars; titles capped; only our issue URL can be opened",
                    basic && capped && title && noOpenForeign, "basic=" + basic + " capped=" + capped + "(" + longUrl.Length + ") title=" + title + " open=" + noOpenForeign);
            }

            // ---- N7 stats JSON parse
            {
                string rels = "[" + RelJson("v4.3", "", "TarkovHipAimer-v4.3.zip", false).Replace("\"download_count\":7", "\"download_count\":120")
                    + "," + RelJson("v4.4", "", "TarkovHipAimer-v4.4.zip", false).Replace("\"download_count\":7", "\"download_count\":35")
                    + "," + RelJson("v4.5", "", "TarkovHipAimer-v4.5.zip", true) + "]";
                DownloadStats d = DownloadStats.Parse(rels);
                bool dl = d != null && d.Total == 155 && d.PerRelease.Count == 2 && d.PerRelease[0].Version == "v4.4" && d.PerRelease[1].Count == 120 && d.Latest == "v4.4";
                OnlineStats o = OnlineStats.Parse("{\"online_now\":12,\"online_24h\":80,\"by_version\":{\"v4.3\":30,\"v4.4\":50}}");
                bool on = o != null && o.OnlineNow == 12 && o.Online24h == 80 && o.ByVersion.Count == 2 && o.ByVersion[0].Version == "v4.4";
                bool bad = OnlineStats.Parse("{}") == null && OnlineStats.Parse("<html>") == null && DownloadStats.Parse("{\"message\":\"rate limited\"}") == null;
                bool deep = MiniJson.TryParse(new string('[', 200) + new string(']', 200)) == null
                    && (string)MiniJson.Parse("\"a\\u00e9\\n\\\"\"") == "a\u00e9\n\"";
                Check("V44-N7 stats: GitHub releases -> total + per release (drafts skipped, newest first); worker /stats -> online now / 24 h / by version; bad replies -> null; JSON nesting capped",
                    dl && on && bad && deep, "dl=" + dl + " on=" + on + " bad=" + bad + " deep=" + deep);
            }

            // ---- N8 heartbeat: opt-out (or not asked, or no worker URL) sends nothing; opted in = id + version every 15 min
            {
                DateTime t0 = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
                string url = "https://thaim-usage.example.workers.dev";
                NetPrefs no = new NetPrefs(); no.UsageAsked = true; no.UsageOptIn = false;
                FakeHttp h1 = new FakeHttp();
                UsageHeartbeat hb1 = new UsageHeartbeat(h1, no, url, "v4.4");
                for (int i = 0; i < 100; i++) hb1.Tick(t0.AddMinutes(i * 5));
                NetPrefs notAsked = NetPrefs.Parse("{\"usageOptIn\":true}"); // opt-in without the question never counts
                FakeHttp h2 = new FakeHttp();
                new UsageHeartbeat(h2, notAsked, url, "v4.4").Tick(t0);
                NetPrefs yes = new NetPrefs(); yes.UsageAsked = true; yes.UsageOptIn = true;
                FakeHttp h3 = new FakeHttp();
                new UsageHeartbeat(h3, yes, "", "v4.4").Tick(t0);                 // worker URL empty = feature hidden
                new UsageHeartbeat(h3, yes, "http://insecure.example", "v4.4").Tick(t0);
                bool silent = h1.Calls.Count == 0 && h2.Calls.Count == 0 && h3.Calls.Count == 0 && no.UsageId == "" && !notAsked.UsageOptIn;

                FakeHttp h4 = new FakeHttp();
                UsageHeartbeat hb = new UsageHeartbeat(h4, yes, url, "v4.4.0");
                bool first = hb.Tick(t0);
                bool early = !hb.Tick(t0.AddMinutes(14));
                bool again = hb.Tick(t0.AddMinutes(15));
                h4.Offline = true;
                bool offlineOk = hb.Tick(t0.AddMinutes(30)) && !hb.Tick(t0.AddMinutes(31)); // a failure waits 15 min too
                object pj = MiniJson.Parse(h4.Posts[0]);
                Guid g;
                bool payload = MiniJson.Obj(pj).Count == 3 && Guid.TryParse(MiniJson.Str(pj, "id"), out g) && Guid.TryParse(MiniJson.Str(pj, "s"), out g) && MiniJson.Str(pj, "v") == "v4.4"
                    && h4.Calls[0] == "POST " + url + "/hb" && h4.Posts[1] == h4.Posts[0];
                Check("V44-N8 在线统计: opted out, not asked yet, or worker URL empty/non-https = no request at all (and no id made); opted in = POST /hb {id, s, v} only (v5.1: + session), first at start then every 15 min, failures wait 15 min",
                    silent && first && early && again && offlineOk && payload, "silent=" + silent + " first=" + first + " early=" + early + " again=" + again + " offline=" + offlineOk + " payload=" + payload);
            }

            // ---- N9 prefs round trip + monitor source offline
            {
                NetPrefs p = new NetPrefs(); p.AutoCheck = false; p.SkipVersion = "v4.5"; p.LastCheckUtcTicks = 123456789; p.UsageAsked = true; p.UsageOptIn = true;
                p.EnsureUsageId();
                NetPrefs q = NetPrefs.Parse(p.Serialize());
                bool rt = !q.AutoCheck && q.SkipVersion == "v4.5" && q.LastCheckUtcTicks == 123456789 && q.UsageAsked && q.UsageOptIn && q.UsageId == p.UsageId;
                NetPrefs def = NetPrefs.Parse("garbage");
                bool defaults = def.AutoCheck && !def.UsageAsked && !def.UsageOptIn && def.UsageId == "" && NetPrefs.Parse("{\"usageId\":\"../x\"}").UsageId == "";
                FakeHttp h = new FakeHttp(); h.Offline = true;
                MonitorSnapshot s = new MonitorSource(h, "v4.4", "").Fetch();
                bool mon = s.Downloads == null && s.DownloadsError == "offline" && !s.OnlineConfigured && h.Calls.Count == 1;
                Check("V44-N9 net-prefs.json round trip; garbage = defaults (自动检查更新 on, 统计 off); monitor offline shows errors, and without a worker URL asks GitHub only",
                    rt && defaults && mon, "rt=" + rt + " def=" + defaults + " mon=" + mon);
            }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
