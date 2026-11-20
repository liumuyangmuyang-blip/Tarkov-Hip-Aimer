// v5.1 (W28) NET: scrubbing, feedback / crash payloads to the worker, sessions, consent rules, source checks.
// Pure parts only (no WinForms, no real network): Net.Scrub.cs, Net.Mail.cs, Net.CrashStore.cs, Net.HubLogic.cs + the v4.4 files.
// v5.1 R3: feedback and crash logs by the user's own email (Net.Send.cs removed).
// FakeHttp lives in TestsV44Net.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51NetInit() { V51NetTests(); }

        static string NetSrcDir()
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string p = Path.Combine(d, "..", "tarkov-crosshair-v4", "src");
                if (Directory.Exists(p)) return Path.GetFullPath(p);
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return "/home/dev/tarkov-crosshair-v4/src";
        }

        static void V51NetTests()
        {
            const string url = "https://thaim.example.workers.dev";

            // ---- X1 scrub
            {
                string raw = "在 C:\\Users\\Zhang San\\Desktop\\x.log 和 C:\\Users\\zhangs\\AppData\\Local 出错，D:/Games/zhangs/a.txt， "
                    + "\\\\NAS\\zhangs\\share，邮箱 zs@qq.com，IP 192.168.1.20，at TarkovCrosshair.App.Tick() in C:\\build\\src\\Program.cs:line 12，"
                    + "电脑 ZHANG-PC，用户 ZhangS，version 4.0.30319.42000，单词 zhangsan 不动";
                string s = NetScrub.Text(raw, "zhangs", "ZHANG-PC");
                bool paths = !s.Contains("Users") && !s.Contains("NAS") && !s.Contains("Games") && s.Contains("[路径]\\x.log")
                    && s.Contains("[路径]\\a.txt") && s.Contains("[路径]\\Program.cs:line 12") && s.Contains(NetScrub.PathMark);
                bool names = !Regex.IsMatch(s, @"(?i)\bzhangs\b") && s.Contains("用户 [用户名]") && s.Contains("电脑 [电脑名]") && s.Contains("zhangsan 不动");
                bool other = s.Contains("[邮箱已隐藏]") && !s.Contains("qq.com") && s.Contains("[IP已隐藏]") && s.Contains("4.0.30319.42000");
                bool shortName = NetScrub.Text("a ab abc", "ab", "") == "a [用户名] abc" && NetScrub.Text("x", "a", "") == "x";
                bool cap = NetScrub.Cap("ab😀", 3) == "ab" && NetScrub.Cap("abc", 5) == "abc";
                Check("V51-X1 scrub: Windows / forward-slash / UNC paths hidden (program file name + line kept, folders never), user + computer name whole-word, e-mail, IPv4 (not .NET version numbers); cap never splits an emoji",
                    paths && names && other && shortName && cap, "paths=" + paths + " names=" + names + " other=" + other + " short=" + shortName + " cap=" + cap + " | " + s);
            }

            // ---- X2 (v5.1 R3) feedback by the user's own email: mailto link to Leo with subject + version / Windows / screen
            {
                FeedbackEnv env = new FeedbackEnv(); env.AppVersion = "v5.1.0"; env.Windows = "Windows 11 23H2 (build 22631)"; env.Screens = "2560x1440 @150%";
                string link = NetMail.FeedbackLink(env);
                string dec = Uri.UnescapeDataString(link);
                bool head = link.StartsWith("mailto:liumuyangmuyang@gmail.com?subject=", StringComparison.Ordinal) && dec.Contains("subject=Tarkov Hip Aimer v5.1 反馈&body=")
                    && NetMail.Address == "liumuyangmuyang@gmail.com";
                bool body = dec.Contains("版本：v5.1\r\n") && dec.Contains("Windows：Windows 11 23H2 (build 22631)") && dec.Contains("屏幕：2560x1440 @150%")
                    && !link.Contains(" ") && !link.Contains("\n");
                FeedbackEnv bad = new FeedbackEnv(); bad.AppVersion = "v5.1"; bad.Windows = "C:\\Users\\someone\\x " + new string('w', 3000); bad.Screens = "x";
                string bl = NetMail.FeedbackLink(bad);
                bool capped = bl.Length <= NetMail.MaxLink && !Uri.UnescapeDataString(bl).Contains("someone");
                string big = NetMail.Link("s", new string('长', 3000));
                bool cut = big.Length <= NetMail.MaxLink && big.Contains("&body=");
                string crash = Uri.UnescapeDataString(NetMail.CrashLink(env));
                bool cr = crash.Contains("subject=Tarkov Hip Aimer 崩溃记录 v5.1&") && crash.Contains("crash.log");
                bool openGuard = !NetMail.Open("mailto:someone@else.com") && !NetMail.Open(null) && !NetMail.Open("https://x");
                Check("V51-X2 反馈 by email (R3): mailto:liumuyangmuyang@gmail.com, subject 「Tarkov Hip Aimer v5.1 反馈」, body template with version / Windows / screen (CRLF, encoded), env scrubbed, link <= 1800; crash mail subject 「Tarkov Hip Aimer 崩溃记录 v5.1」; Open only for Leo's address",
                    head && body && capped && cut && cr && openGuard,
                    "head=" + head + " body=" + body + " cap=" + capped + " cut=" + cut + " crash=" + cr + " guard=" + openGuard);
            }

            // ---- X3 crash log file + payload
            {
                string dir = Path.Combine(Path.GetTempPath(), "v51x3-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                Exception ex;
                try { throw new InvalidOperationException("打不开 C:\\Users\\leo\\AppData\\settings.json"); } catch (Exception e) { ex = e; }
                string built = CrashStore.Build(ex, "v5.1", new DateTime(2026, 11, 20, 1, 2, 3, DateTimeKind.Utc), "leo", "LEO-PC");
                bool text = built.StartsWith("2026-11-20 01:02:03 UTC  v5.1\n", StringComparison.Ordinal) && built.Contains("InvalidOperationException")
                    && built.Contains("[路径]\\settings.json") && !Regex.IsMatch(built, @"(?i)\bleo\b");
                bool missing = CrashStore.Load(dir) == null;
                bool saved = CrashStore.Save(dir, ex, "v5.1") && CrashStore.Load(dir) != null && CrashStore.Load(dir).Contains("InvalidOperationException");
                CrashStore.Delete(dir);
                bool deleted = CrashStore.Load(dir) == null && !File.Exists(CrashStore.PathIn(dir));
                string huge = CrashStore.Build(new Exception(new string('x', 40000)), "v5.1", DateTime.UtcNow, "", "");
                bool capped = huge.Length <= CrashStore.MaxChars + 20 && huge.EndsWith("（太长，已截断）", StringComparison.Ordinal);
                // R3: crash.log scrubbed in place before the user attaches it
                File.WriteAllText(Path.Combine(dir, "crash.log"), "12:00 boom at C:\\Users\\leo\\Desktop\\TarkovHipAimer\\Program.cs:line 3 on LEO-PC\n", Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, "crash.old.log"), "old leo\n", Encoding.UTF8);
                string lp = CrashStore.ScrubLogFile(dir, "leo", "LEO-PC");
                string after = File.ReadAllText(Path.Combine(dir, "crash.log"));
                bool inPlace = lp == Path.Combine(dir, "crash.log") && after.StartsWith("12:00 boom at [路径]\\Program.cs:line 3 on [电脑名]", StringComparison.Ordinal)
                    && File.ReadAllText(Path.Combine(dir, "crash.old.log")) == "old [用户名]\n" && !File.Exists(lp + ".tmp");
                File.Delete(lp);
                bool noLog = CrashStore.ScrubLogFile(dir, "leo", "") == null;
                bool badDir = !CrashStore.Save(Path.Combine(dir, "no", "such"), ex, "v5.1"); // never throws
                Directory.Delete(dir, true);
                Check("V51-X3 错误记录: crash-pending.txt = time + version + exception, scrubbed (user name, paths), capped with a mark; load / delete; R3: crash.log + crash.old.log scrubbed in place (user, computer, paths) before the user attaches it; no log = null; a bad folder never throws",
                    text && missing && saved && deleted && capped && inPlace && noLog && badDir,
                    "text=" + text + " missing=" + missing + " saved=" + saved + " deleted=" + deleted + " cap=" + capped + " inPlace=" + inPlace + " noLog=" + noLog + " badDir=" + badDir);
            }

            // ---- X4 sessions
            {
                DateTime t0 = new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc);
                NetPrefs yes = new NetPrefs(); yes.UsageAsked = true; yes.UsageOptIn = true;
                FakeHttp h = new FakeHttp();
                UsageHeartbeat hb = new UsageHeartbeat(h, yes, url, "v5.1");
                bool noEndYet = !hb.SendEnd() && h.Calls.Count == 0;
                hb.Tick(t0); hb.Tick(t0.AddMinutes(15));
                string s1 = MiniJson.Str(MiniJson.Parse(h.Posts[0]), "s"), s2 = MiniJson.Str(MiniJson.Parse(h.Posts[1]), "s");
                bool same = s1 == s2 && s1 == hb.SessionId && MiniJson.Str(MiniJson.Parse(h.Posts[0]), "id") == yes.UsageId && s1 != yes.UsageId;
                hb.Tick(t0.AddMinutes(15 + 90)); // PC slept 90 min
                string s3 = MiniJson.Str(MiniJson.Parse(h.Posts[2]), "s");
                bool slept = s3 != s1;
                h.PostStatuses.Enqueue(409);
                hb.Tick(t0.AddMinutes(15 + 90 + 15));
                string s4 = MiniJson.Str(MiniJson.Parse(h.Posts[3]), "s"), s5 = MiniJson.Str(MiniJson.Parse(h.Posts[4]), "s");
                bool renewed = h.Posts.Count == 5 && s4 == s3 && s5 != s3 && hb.SessionId == s5;
                bool end = hb.SendEnd();
                object ej = MiniJson.Parse(h.Posts[5]);
                bool endShape = end && MiniJson.Obj(ej).Count == 4 && MiniJson.Bool(ej, "end", false) && MiniJson.Str(ej, "s") == s5;
                NetPrefs no = new NetPrefs(); no.UsageAsked = true;
                FakeHttp h2 = new FakeHttp();
                UsageHeartbeat off = new UsageHeartbeat(h2, no, url, "v5.1");
                off.Tick(t0);
                bool silent = !off.SendEnd() && h2.Calls.Count == 0;
                Check("V51-X4 使用时长: one session id per run (not the install id), kept across heartbeats; new session after a 90-min sleep or a 409 from the worker (resent at once); exit sends {id, s, v, end:true} once, only after a heartbeat went out; opted out = nothing",
                    noEndYet && same && slept && renewed && endShape && silent,
                    "noEnd=" + noEndYet + " same=" + same + " slept=" + slept + " renewed=" + renewed + " end=" + endShape + " silent=" + silent);
            }

            // ---- X5 consent rules + stats parse
            {
                DelegateNetSettings st = new DelegateNetSettings();
                NetPrefs p = new NetPrefs();
                NetRules.Mirror(st, p);
                bool fresh = st.UpdateCheck && st.CrashAsk && !st.StatsConsent.HasValue && !p.UsageAsked && !p.UsageOptIn && p.AutoCheck
                    && NetRules.NeedsStatsQuestion(st, url) && !NetRules.WantHeartbeat(st, url) && !NetRules.NeedsStatsQuestion(st, "");
                st.StatsConsent = false; NetRules.Mirror(st, p);
                bool no = p.UsageAsked && !p.UsageOptIn && !NetRules.WantHeartbeat(st, url) && !NetRules.NeedsStatsQuestion(st, url);
                st.StatsConsent = true; st.UpdateCheck = false; NetRules.Mirror(st, p);
                bool yes = p.UsageAsked && p.UsageOptIn && !p.AutoCheck && NetRules.WantHeartbeat(st, url) && !NetRules.WantHeartbeat(st, "");
                bool crash = NetRules.MayAskCrash(st);   // R3: no worker needed (nothing is uploaded)
                st.CrashAsk = false;
                crash &= !NetRules.MayAskCrash(st);
                bool? backing = null; bool upd = true, cr = true; int saves = 0;
                DelegateNetSettings bridged = new DelegateNetSettings(delegate { return upd; }, delegate(bool v) { upd = v; },
                    delegate { return backing; }, delegate(bool? v) { backing = v; }, delegate { return cr; }, delegate(bool v) { cr = v; }, delegate { saves++; });
                bridged.StatsConsent = true; bridged.CrashAsk = false; bridged.Save();
                bool bridge = backing == true && !cr && saves == 1 && bridged.Saves == 1;
                OnlineStats o = OnlineStats.Parse("{\"online_now\":3,\"online_24h\":9,\"users_total\":120,\"sessions_7d\":40,\"avg_session_min_7d\":52.5,\"total_hours\":1234.5,\"by_version\":{\"v5.1\":9}}");
                bool parse = o != null && o.UsersTotal == 120 && o.Sessions7d == 40 && Math.Abs(o.AvgSessionMin7d - 52.5) < 1e-9 && Math.Abs(o.TotalHours - 1234.5) < 1e-9;
                OnlineStats old = OnlineStats.Parse("{\"online_now\":1,\"online_24h\":2,\"total_hours\":-5}");
                parse &= old != null && old.UsersTotal == 0 && old.TotalHours == 0;
                bool texts = UsageConfig.AskText.Contains("随机编号") && UsageConfig.AskNo == "不用" && UsageConfig.SafetyLine.StartsWith("只在检查更新时联网；你同意后，还会发送匿名统计", StringComparison.Ordinal)
                    && !UsageConfig.SafetyLine.Contains("发送匿名统计、");
                Check("V51-X5 consent: not asked = question pending and nothing sent; 不用 = nothing; 可以 = heartbeat; no worker = never asked; crash question whenever the switch is on (R3: no worker needed); settings -> net-prefs mirror; bridge writes the settings fields; /stats v5.1 fields parsed (old reply still reads, negatives = 0)",
                    fresh && no && yes && crash && bridge && parse && texts,
                    "fresh=" + fresh + " no=" + no + " yes=" + yes + " crash=" + crash + " bridge=" + bridge + " parse=" + parse + " texts=" + texts);
            }

            // ---- X6 source checks: no credential, no TLS bypass, worker URL constant, no disclaimers, consent default
            {
                string dir = NetSrcDir();
                StringBuilder all = new StringBuilder();
                foreach (string f in Directory.GetFiles(dir, "*.cs")) all.Append(File.ReadAllText(f));
                string src = all.ToString();
                StringBuilder net = new StringBuilder();
                foreach (string f in Directory.GetFiles(dir, "Net.*.cs")) net.Append(File.ReadAllText(f));
                string n = net.ToString();
                bool noSecret = !Regex.IsMatch(src, @"re_[A-Za-z0-9]{16,}") && !src.Contains("RESEND_API_KEY") && !Regex.IsMatch(src, @"Bearer\s+[A-Za-z0-9]")
                    && !Regex.IsMatch(src, @"(?i)(api[_-]?key|password|secret)\s*=\s*""[^""]{6,}""");
                bool noBypass = !src.Contains("ServerCertificateValidationCallback") && !src.Contains("CheckCertificateRevocationList = false");
                bool consts = Regex.IsMatch(n, @"public const string WorkerUrl = ""[^""]*"";") && n.Contains("MessageBoxDefaultButton.Button2");
                bool words = !Regex.IsMatch(n, @"""[^""\n]*(未实测|示意|估计|起点|UNVERIFIED)[^""\n]*""");
                bool noTarkovFiles = !Regex.IsMatch(n, @"(?i)escape from tarkov|Battlestate|shared\.ini|local\.ini");
                bool noUpload = !n.Contains("/feedback\"") && !n.Contains("/crash\"") && !File.Exists(Path.Combine(dir, "Net.Send.cs"));
                Check("V51-X6 net source: no API key / bearer token / password in any src file, no TLS check bypass, one WorkerUrl constant, 统计 dialog defaults to 不用, no disclaimer words in net strings, never reads Tarkov's files, R3: no /feedback or /crash upload code",
                    noSecret && noBypass && consts && words && noTarkovFiles && noUpload,
                    "secret=" + noSecret + " bypass=" + noBypass + " consts=" + consts + " words=" + words + " tarkov=" + noTarkovFiles + " noUpload=" + noUpload);
            }
        }
    }
}
