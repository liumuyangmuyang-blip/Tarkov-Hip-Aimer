// v5.1 (W26): onboarding / CX -- 「?」 help texts, 必读 card, 常见问题, home status line, setup wizard model (all pure parts).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W26Init() { V51W26Tests(); }

        static readonly string[] W26Banned = { "未实测", "示意", "估计", "起点", "UNVERIFIED", "TODO" };

        static List<string> W26AllTexts(GlobalSettings g)
        {
            List<string> all = new List<string>();
            foreach (HelpText.Entry e in HelpText.All) { all.Add(e.Title); all.Add(HelpText.Body(e.Id, g)); }
            for (int i = 0; i < MustRead.Items.Length; i++) { all.Add(MustRead.HeadOf(i, g)); all.Add(MustRead.BodyOf(i, g)); }
            for (int i = 0; i < FaqText.Items.Length; i++)
            {
                all.Add(FaqText.Question(i, g)); all.Add(FaqText.Answer(i, g));
                if (FaqText.Items[i].JumpText != null) all.Add(FaqText.Items[i].JumpText);
            }
            all.AddRange(WizardModel.Titles); all.AddRange(WizardModel.Leads);
            return all;
        }

        static string W26Src()
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string c = Path.Combine(d, "tarkov-crosshair-v4", "src");
                if (Directory.Exists(c)) return c;
                d = Path.GetDirectoryName(d.TrimEnd('/', '\\'));
            }
            return "/home/dev/tarkov-crosshair-v4/src";
        }

        static void V51W26Tests()
        {
            GlobalSettings g = G();

            // W26-1 help texts: unique ids, title + body, every placeholder filled from the user's keys
            {
                HashSet<string> ids = new HashSet<string>();
                bool ok = HelpText.Count >= 30;
                foreach (HelpText.Entry e in HelpText.All)
                {
                    if (!ids.Add(e.Id) || string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Body)) ok = false;
                    if (HelpText.Body(e.Id, g).Contains("{")) ok = false;
                    if (HelpText.Body(e.Id, null).Contains("{")) ok = false;
                }
                bool find = HelpText.Find("nope") == null && HelpText.Body("nope") == "" && HelpText.Title("grenade") == "丢雷隐藏";
                Check("W26-1 HelpText: unique ids, title + body, no unfilled {key} with or without settings", ok && find,
                    "count=" + HelpText.Count + " ok=" + ok + " find=" + find);
            }

            // W26-2 keys follow the settings (boost = the game's device mode-switch key, e.g. Ctrl+T)
            {
                GlobalSettings h = G();
                h.Keys.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkT, KeyBindings.ModCtrl);
                h.GrenadeKey = 0x47; h.AmmoReloadKey = 0x52;
                string b = HelpText.Body("boost", h), gr = HelpText.Body("grenade", h), am = HelpText.Body("ammo", h);
                bool ok = b.Contains("Ctrl+T") && b.Contains("手电") && b.Contains("红外") && gr.Contains("按 G") && am.Contains("按 R");
                bool generic = HelpText.Body("boost", null).Contains("强化键");
                Check("W26-2 help text names the user's keys (强化 = Ctrl+T 切换战术装置模式, 丢雷 G, 换弹 R); generic words without settings",
                    ok && generic, "boost=" + b.Replace("\n", " / "));
            }

            // W26-3 必读 card (decision 4 + Leo's boost correction)
            {
                GlobalSettings h = G();
                h.Keys.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkT, KeyBindings.ModCtrl);
                string all = MustRead.PlainText(h);
                bool ir = MustRead.HeadOf(0, h).Contains("红外") && all.Contains("WMX200") && MustRead.BodyOf(0, h).Contains("T 打开");
                bool laser = all.Contains("不要同时开准星和激光") || all.Contains("激光");
                bool boost = all.Contains("切换战术装置模式") && all.Contains("Ctrl+T") && all.Contains("手电") && all.Contains("切回红外");
                bool numbered = all.Contains("1. ") && all.Contains(MustRead.Items.Length + ". ");
                bool ack1 = !h.MustReadAck && MustRead.Acknowledge(h) && h.MustReadAck, ack2 = !MustRead.Acknowledge(h);
                // round 2 (R6): exactly Leo's 3 + 管理员 + 开镜方式, short sentences (head <= 22 chars, body <= 45 chars)
                bool admin = all.Contains("管理员"), adsMode = all.Contains("开镜方式") && all.Contains("按住") && all.Contains("切换");
                bool shortText = true;
                for (int i = 0; i < MustRead.Items.Length; i++)
                    if (MustRead.HeadOf(i, h).Length > 22 || MustRead.BodyOf(i, h).Length > 45) shortText = false;
                Check("W26-3 必读 (R6): 5 items -- IR mode (WMX200, T keeps it on), no laser together, boost = mode-switch key (手电 on / 红外 off), 管理员, 开镜方式; short; numbered, ack once",
                    MustRead.Items.Length == 5 && ir && laser && boost && admin && adsMode && shortText && numbered && ack1 && ack2 && !all.Contains("{"),
                    "ir=" + ir + " laser=" + laser + " boost=" + boost + " admin=" + admin + " ads=" + adsMode + " short=" + shortText + " num=" + numbered + " ack=" + ack1 + "/" + ack2);
            }

            // W26-4 常见问题: every jump target is a real page; the key questions from the brief are there
            {
                bool jumps = true; string bad = "";
                foreach (FaqText.Entry e in FaqText.Items)
                    if (e.Jump != null && UiPages.IndexOf(e.Jump) < 0) { jumps = false; bad += e.Jump + " "; }
                string all = string.Join("\n", W26AllTexts(g).ToArray());
                bool ads = false, yellow = false, still = false;
                for (int i = 0; i < FaqText.Items.Length; i++)
                {
                    string q = FaqText.Question(i, g), a = FaqText.Answer(i, g);
                    if (q.Contains("开镜") && a.Contains("开镜方式")) ads = true;
                    if (q.Contains("黄") && a.Contains("换手")) yellow = true;
                    if (q.Contains("不动") && a.Contains("管理员")) still = true;
                }
                Check("W26-4 常见问题: jump targets exist (UiPages), 开镜还有准星 -> 开镜方式, 变黄 -> 换手, 不动 -> 管理员",
                    jumps && ads && yellow && still && FaqText.Items.Length >= 8 && all.Length > 0, "bad=" + bad + " ads=" + ads + " yellow=" + yellow + " still=" + still);
            }

            // W26-5 status line words (decision 13 examples)
            {
                StatusLevel lv;
                StatusSnapshot s = new StatusSnapshot();
                s.Enabled = true;
                string shown = StatusLine.Compose(s, g, out lv);
                bool okShown = shown == "准星：显示中" && lv == StatusLevel.Ok;
                s.HandSwitched = true; s.BoostEnabled = true; s.BoostOn = true;
                string hb = StatusLine.Compose(s, g, out lv);
                bool okHb = hb == "准星：显示中 · 黄色 = 换手中 · 强化：开" && lv == StatusLevel.Info;
                s.BoostOn = false;
                bool okOff = StatusLine.Compose(s, g, out lv).EndsWith("强化：关");
                s.AdsHidden = true;
                bool okAds = StatusLine.Compose(s, g, out lv).StartsWith("已隐藏：开镜") && lv == StatusLevel.Hidden;
                GlobalSettings h = G(); h.GrenadeKey = 0x47;
                s.GrenadeHidden = true;
                string gr = StatusLine.Compose(s, h, out lv);
                bool okGr = gr.StartsWith("已隐藏：丢雷（再按 G 显示）") && lv == StatusLevel.Hidden; // 丢雷 wins over 开镜: it needs a key press
                StatusSnapshot off = new StatusSnapshot(); off.Enabled = false; off.AdsHidden = true; off.BoostOn = true;
                string o = StatusLine.Compose(off, g, out lv);
                bool okDis = o.StartsWith("准星：已关闭（按 " + g.Keys.ComboOf(KeyBindings.ActionToggle)) && !o.Contains("强化") && lv == StatusLevel.Off;
                StatusSnapshot fl = new StatusSnapshot(); fl.Enabled = true; fl.FreeLookHidden = true;
                bool okFl = StatusLine.Compose(fl, g, out lv) == "已隐藏：自由视角";
                fl.FreeLookHidden = false; fl.WaitingForGame = true; fl.AmmoOn = true;
                bool okWait = StatusLine.Compose(fl, g, out lv) == "已隐藏：游戏不在前台 · 弹药提示：开";
                GlobalSettings pm = G(); pm.PerfMode = true;
                StatusSnapshot ps = new StatusSnapshot(); ps.Enabled = true;
                okWait = okWait && StatusLine.Compose(ps, pm, out lv) == "准星：显示中 · 性能模式：开";
                Check("W26-5 status line: 显示中 / 黄色 = 换手中 / 强化：开·关 / 已隐藏：开镜 / 已隐藏：丢雷（再按 G 显示）/ 已关闭 / 自由视角 / 游戏不在前台",
                    okShown && okHb && okOff && okAds && okGr && okDis && okFl && okWait,
                    "shown=" + okShown + " hb=" + hb + " off=" + okOff + " ads=" + okAds + " gr=" + gr + " dis=" + o + " fl=" + okFl + " wait=" + okWait);
            }

            // W26-6 status line only reports a change when the words change (repaint gate)
            {
                StatusLine line = new StatusLine();
                StatusSnapshot s = new StatusSnapshot(); s.Enabled = true;
                bool first = line.Update(s, g), same = !line.Update(s, g);
                s.AdsHidden = true;
                bool changed = line.Update(s, g) && line.Text == "已隐藏：开镜", same2 = !line.Update(s, g);
                Check("W26-6 status line Update: true only when the words / level change", first && same && changed && same2,
                    "first=" + first + " same=" + same + " changed=" + changed + " same2=" + same2);
            }

            // W26-7 wizard flow: 10 steps, 必读 blocks 完成 until 我知道了
            {
                GlobalSettings h = G(); h.MustReadAck = false;
                WizardModel m = new WizardModel(h);
                int steps = 0;
                while (m.Current != WizardModel.Step.MustRead && m.Next()) steps++;
                bool atMust = m.Current == WizardModel.Step.MustRead && m.Blocker() != null && !m.Next();
                m.Draft.MustReadAck = true;
                bool through = m.Next() && m.IsLast && !m.Next();
                bool back = m.Back() && m.Current == WizardModel.Step.MustRead;
                bool liveUntouched = !h.MustReadAck && !h.WizardDone;
                Check("W26-7 wizard: 10 steps in order, 必读 must be acknowledged before 完成, the draft never touches the live settings",
                    m.Count == 10 && WizardModel.Titles.Length == WizardModel.Leads.Length && steps == 8 && atMust && through && back && liveUntouched,
                    "steps=" + steps + " atMust=" + atMust + " through=" + through + " back=" + back + " live=" + liveUntouched);
            }

            // W26-8 resolution input
            {
                int w, h;
                bool a = WizardModel.TryParseResolution("1920x1080", out w, out h) && w == 1920 && h == 1080;
                bool b = WizardModel.TryParseResolution(" 2560 × 1440 ", out w, out h) && w == 2560 && h == 1440;
                bool c = WizardModel.TryParseResolution("3440*1440", out w, out h) && WizardModel.TryParseResolution("1280 1024", out w, out h);
                bool bad = !WizardModel.TryParseResolution("100x100", out w, out h) && !WizardModel.TryParseResolution("abc", out w, out h)
                    && !WizardModel.TryParseResolution("1920x", out w, out h) && !WizardModel.TryParseResolution(null, out w, out h);
                bool presets = true;
                foreach (int[] p in WizardModel.ResPresets) if (WizardModel.ResolutionError(p[0], p[1]) != null) presets = false;
                GlobalSettings gg = G(); WizardModel m = new WizardModel(gg);
                m.GoTo(WizardModel.Step.Resolution);
                m.Draft.ResGameW = 100;
                bool blocked = m.Blocker() != null && !m.Next();
                Check("W26-8 wizard resolution: 1920x1080 / 2560 × 1440 / 3440*1440 / 1280 1024 read; out of range blocked; presets valid",
                    a && b && c && bad && presets && blocked, "a=" + a + " b=" + b + " c=" + c + " bad=" + bad + " presets=" + presets + " blocked=" + blocked);
            }

            // W26-9 画面比例: any W:H, 1:1 offered, stretch question + 「横向 ×1.20（拉伸）」 (16:10 screen at 4:3)
            {
                GlobalSettings gg = G(); WizardModel m = new WizardModel(gg);
                bool choices = Array.IndexOf(WizardModel.AspectChoices(), "1:1") >= 0 && Array.IndexOf(WizardModel.AspectChoices(), WizardModel.CustomAspect) >= 0;
                bool custom = m.SetCustomAspect("123:111") && m.Draft.ResAspect == "123:111" && m.SetCustomAspect("16：9");
                bool reject = !m.SetCustomAspect("abc") && !m.SetCustomAspect("0:5") && m.Draft.ResAspect == "16:9";
                m.Draft.ResGameW = 1680; m.Draft.ResGameH = 1050; m.Draft.ResAspect = "4:3";
                bool ask = m.NeedsStretchQuestion && m.StretchText == "横向 ×1.20（拉伸）";
                m.Draft.ResAspect = "16:10";
                bool native = !m.NeedsStretchQuestion && m.StretchText == "";
                m.GoTo(WizardModel.Step.Aspect); m.Draft.ResAspect = WizardModel.CustomAspect;
                bool blocked = m.Blocker() != null;
                Check("W26-9 wizard 画面比例: 123:111 / 16：9 accepted, junk rejected, 1:1 + 自定义 offered, 1680×1050 at 4:3 asks and shows 横向 ×1.20（拉伸）",
                    choices && custom && reject && ask && native && blocked, "choices=" + choices + " custom=" + custom + " reject=" + reject + " ask=" + ask + " native=" + native + " blocked=" + blocked);
            }

            // W26-10 Apply copies only the wizard's fields; profiles and other settings stay
            {
                GlobalSettings live = G();
                int profiles = live.Profiles.Count;
                string active = live.ActiveProfile;
                double shadeBefore = live.ShowFadeMs;
                bool splash = live.SplashOnStart;
                WizardModel m = new WizardModel(live);
                m.Draft.ResGameW = 2560; m.Draft.ResGameH = 1440; m.Draft.ResAspect = "16:9"; m.Draft.ResFov = 60;
                m.Draft.AdsMode = "toggle"; m.Draft.BoostToggle = false; m.Draft.GrenadeHideOn = false; m.Draft.GrenadeKey = 0x48;
                m.Draft.Keys.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkT, KeyBindings.ModCtrl);
                m.Draft.MustReadAck = true;
                m.Draft.ShowFadeMs = shadeBefore + 77; // not a wizard field: must not be copied
                m.Draft.SplashOnStart = !splash;
                m.Apply(live);
                bool copied = live.ResGameW == 2560 && live.ResGameH == 1440 && live.ResAspect == "16:9" && live.ResFov == 60 && live.AdsMode == "toggle"
                    && !live.BoostToggle && !live.GrenadeHideOn && live.GrenadeKey == 0x48 && live.Keys.ComboOf(KeyBindings.ActionBoost) == "Ctrl+T";
                bool flags = live.WizardDone && live.MustReadAck;
                bool kept = live.Profiles.Count == profiles && live.ActiveProfile == active && live.ShowFadeMs == shadeBefore && live.SplashOnStart == splash;
                GlobalSettings sk = G(); double w0 = sk.ResGameW; WizardModel.Skip(sk);
                bool skip = sk.WizardDone && sk.ResGameW == w0 && !sk.MustReadAck;
                Check("W26-10 wizard Apply: wizard fields copied (res, aspect, FOV, 开镜, 强化 Ctrl+T 按住, 丢雷), WizardDone + MustReadAck set, profiles / other fields kept; 跳过 only sets WizardDone",
                    copied && flags && kept && skip, "copied=" + copied + " flags=" + flags + " kept=" + kept + " skip=" + skip);
            }

            // W26-11 key clash blocks 下一步; FOV hint
            {
                GlobalSettings gg = G(); WizardModel m = new WizardModel(gg);
                m.GoTo(WizardModel.Step.GameKeys);
                bool clean = m.Blocker() == null;
                m.Draft.Keys.SetCombo(KeyBindings.ActionJump, m.Draft.Keys.Get(KeyBindings.ActionFire), m.Draft.Keys.GetMods(KeyBindings.ActionFire));
                bool clash = m.Blocker() != null && m.Blocker().Contains("跳跃") && !m.Next();
                GlobalSettings g2 = G(); WizardModel m2 = new WizardModel(g2);
                m2.Draft.GrenadeHideOn = true; m2.Draft.GrenadeKey = m2.Draft.Keys.Get(KeyBindings.ActionLeanLeft);
                bool gclash = m2.KeyClash() != null && m2.KeyClash().Contains("丢雷");
                bool fov = WizardModel.FovHint(40) != "" && WizardModel.FovHint(90) != "" && WizardModel.FovHint(60) == "";
                m.GoTo(WizardModel.Step.Fov); m.Draft.ResFov = 80;
                bool fovBlock = m.Blocker() != null;
                Check("W26-11 wizard: two actions on one key (or 丢雷 on a crosshair key) block 下一步; FOV outside 50–75 hinted + blocked",
                    clean && clash && gclash && fov && fovBlock, "clean=" + clean + " clash=" + clash + " gclash=" + gclash + " fov=" + fov + " fovBlock=" + fovBlock);
            }

            // W26-12 wording: no disclaimers, no guessed facts markers, every text Chinese (decision 13 / brief)
            {
                string bad = "";
                foreach (string t in W26AllTexts(g))
                    foreach (string w in W26Banned) if (t.Contains(w)) bad += w + "@" + t.Substring(0, Math.Min(12, t.Length)) + " ";
                bool cjk = true;
                foreach (string t in W26AllTexts(g)) if (!Regex.IsMatch(t, "[\u4e00-\u9fff]")) { cjk = false; bad += "noCJK:" + t + " "; }
                Check("W26-12 onboarding texts: no 未实测/示意/估计/起点/UNVERIFIED, all Chinese", bad.Length == 0 && cjk, bad);
            }

            // W26-13 source rules: WinForms parts guarded for the Linux tests, never read Tarkov's files, text never clipped
            {
                string src = W26Src();
                string[] ui = { "WizardForm.cs", "FaqPage.cs", "DashboardHome.cs" };
                bool guard = true, noGame = true, noEllipsis = true;
                foreach (string f in new string[] { "WizardForm.cs", "WizardModel.cs", "FaqPage.cs", "FaqText.cs", "DashboardHome.cs", "StatusLine.cs", "HelpText.cs", "MustRead.cs" })
                {
                    string p = Path.Combine(src, f);
                    if (!File.Exists(p)) { guard = false; continue; }
                    string s = File.ReadAllText(p);
                    if (Array.IndexOf(ui, f) >= 0 && !s.TrimStart('\uFEFF').Contains("#if !UI_PURE_CHECK")) guard = false;
                    if (Regex.IsMatch(s, "EscapeFromTarkov_Data|shared\\.ini|local\\.ini|Battlestate|AppData.*Tarkov", RegexOptions.IgnoreCase)) noGame = false;
                    if (s.Contains("EndEllipsis") || s.Contains("WordEllipsis")) noEllipsis = false;
                }
                Check("W26-13 source: WinForms parts behind UI_PURE_CHECK, no Tarkov file paths read, no ellipsis-clipped text", guard && noGame && noEllipsis,
                    "guard=" + guard + " noGame=" + noGame + " noEllipsis=" + noEllipsis + " src=" + Path.GetFullPath(src));
            }

            // W26-14 round 2 texts: R2 reload = new full mag + adjustable times (常调), R3 no weapon keys, R4 tunables, R5 two presets
            {
                string all = string.Join("\n", W26AllTexts(g).ToArray());
                string rl = HelpText.Body("reload", g);
                bool r2 = rl.Contains("新的满弹匣") && rl.Contains("2400") && rl.Contains("900") && rl.Contains("常调") && !all.Contains("补满");
                bool r3 = HelpText.Find("ammokeys") == null && !all.Contains("武器键") && !all.Contains("暂停计数");
                bool r4 = HelpText.Body("taps", g).Contains("短按阈值") && HelpText.Body("taps", g).Contains("换向最短时间")
                    && HelpText.Find("reversems") != null && all.Contains("120 毫秒") && all.Contains("150 毫秒");
                bool r5 = HelpText.Body("filtertier", g).Contains("通用") && HelpText.Body("filtertier", g).Contains("黑夜模式");
                foreach (string w in new string[] { "白天", "黄昏", "夜图预设", "室内", "实验室" }) if (all.Contains(w)) r5 = false;
                Check("W26-14 round 2 help/FAQ: reload = new full mag, times 2400 / 900 adjustable incl. 常调; no weapon keys; 短按阈值 / 换向最短时间; filter presets 通用 + 黑夜模式 only",
                    r2 && r3 && r4 && r5, "r2=" + r2 + " r3=" + r3 + " r4=" + r4 + " r5=" + r5);
            }
        }
    }
}
