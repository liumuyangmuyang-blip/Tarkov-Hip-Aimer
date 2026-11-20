// v5.1.1 (X5): FOV conversion by Leo's in-game measurements (21 Nov 2026; Leo 推荐, 1920x1440 4:3, tuned at FOV 75):
// gun side (screen right for the normal hand) x1.00, other side x gameFov / baseFov, mirrored on 换手, applied to the FINAL
// on-screen X; Y has no FOV term (Leo's final call). Plus Leo's new built-in default of 21 Nov 2026 (上抬 4.00 %, 站立静止偏移
// → 3.40 % ↓ 3.50 % with every screen position unchanged, 强化 = 鼠标侧键2).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X5Init() { V511X5Tests(); }

        static GlobalSettings X5G(double fov)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.ResGameW = 1920; g.ResGameH = 1440; g.ResAspect = "4:3"; g.ResFov = fov;
            g.Validate();
            return g;
        }

        static string X5F(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        /// <summary>Screen X % (+ right) after holding dir (W S A D WA WD SA SD = 0..7, -1 none) and lean (-1 Q, 1 E).</summary>
        static double X5Screen(Profile p, GlobalSettings g, bool hand1, int dir, int lean, out double yPct)
        {
            MotionEngine e = W23Hold(p, g, hand1, W23Keys(dir, lean));
            yPct = -OffsetUnits.ScreenToPct(e.OffsetY, g);
            return OffsetUnits.ScreenToPct(e.OffsetX, g);
        }

        // the 「Leo默认」 values before 21 Nov 2026 (LeoDefaultJson profile 「Leo默认 (旧)」, no 站立静止偏移)
        static readonly double[][] X5OldDirs = {
            new[] { 0, -14.4 }, new[] { 0, 14.4 }, new[] { 97.2, 0 }, new[] { -37.440000000000005, 0 },
            new[] { 68.73012, -10.18224 }, new[] { -26.473824, -10.18224 }, new[] { 68.73012, 10.18224 }, new[] { -26.473824, 10.18224 } };
        static readonly double[][] X5OldHand = {   // Custom (1/0), X, Y
            new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 0 }, new[] { 1.0, 28.8, 1.08 }, new[] { 1.0, -72, 0 },
            new[] { 0.0, 0, 0 }, new[] { 1.0, -25.92, 5.7600000000000007 }, new[] { 1.0, 10.8, -4.32 }, new[] { 0.0, 0, 0 } };

        static Profile X5OldDefault()
        {
            Profile p = Profile.CreateLeoDefault("old");
            p.StandAsZero = false; p.StandOffsetX = 0; p.StandOffsetY = 0;
            for (int i = 0; i < 8; i++) p.Dirs[i] = new DirSetting(X5OldDirs[i][0], X5OldDirs[i][1]);
            for (int i = 0; i < 8; i++) { p.HandCustom[i].Custom = X5OldHand[i][0] == 1.0; p.HandCustom[i].X = X5OldHand[i][1]; p.HandCustom[i].Y = X5OldHand[i][2]; }
            p.RecoilMaxPx = 24;
            return p;
        }

        static void V511X5Tests()
        {
            // X5-1 Leo's numbers, normal hand: A right 13.5 % at every FOV; D left 5.2 % x FOV / 75 (Leo: FOV 60 4.0-4.04,
            // FOV 50 3.5); standing = the 站立 point (gun side, unscaled); Y unchanged by FOV.
            {
                string d = "";
                Profile p = Profile.CreateLeoDefault("t"); p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0;
                double y75 = 0;
                foreach (double fov in new double[] { 75, 60, 50 })
                {
                    GlobalSettings g = X5G(fov);
                    double ya, yd, ys;
                    double a = X5Screen(p, g, false, 2, 0, out ya), dd = X5Screen(p, g, false, 3, 0, out yd), st = X5Screen(p, g, false, -1, 0, out ys);
                    double wantD = -5.2 * fov / 75.0;
                    if (Math.Abs(a - 13.5) > 0.01) d += "FOV" + fov + " A " + X5F(a) + "; ";
                    if (Math.Abs(dd - wantD) > 0.01) d += "FOV" + fov + " D " + X5F(dd) + " want " + X5F(wantD) + "; ";
                    if (Math.Abs(st - 3.40) > 0.01 || Math.Abs(ys + 3.50) > 0.01) d += "FOV" + fov + " stand " + X5F(st) + "," + X5F(ys) + "; ";
                    if (fov == 75) y75 = yd; else if (Math.Abs(yd - y75) > 1e-9) d += "FOV" + fov + " D Y moved; ";
                    // the boxes show the on-screen distance from the 站立 point
                    string boxD = OffsetUnits.FormatAt(p.Dirs[3].OffX, OffsetUnits.StandOriginX(p, false), OffsetAxis.X, false, g);
                    string want = OffsetUnits.FormatPct(dd - st, OffsetAxis.X);
                    if (boxD != want) d += "FOV" + fov + " D box " + boxD + " vs screen " + want + "; ";
                    string boxA = OffsetUnits.FormatAt(p.Dirs[2].OffX, OffsetUnits.StandOriginX(p, false), OffsetAxis.X, false, g);
                    if (boxA != OffsetUnits.FormatPct(a - st, OffsetAxis.X)) d += "FOV" + fov + " A box " + boxA + "; ";
                }
                Check("X5-1 Leo's measurements (1920x1440 4:3, tuned FOV 75): A → 13.50 % at FOV 75/60/50, D ← 5.20/4.16/3.47 %, standing → 3.40 % ↓ 3.50 % unscaled, Y unchanged; box = screen distance from the 站立 point", d.Length == 0, d);
            }

            // X5-2 every direction x lean x both hands x FOV 50/55/60/65/70: the engine X = FovX(k·sum, r, hand) (换手 mirrors:
            // left unscaled, right scaled), Y = k·sum exactly as FOV 75; equal FOV bit-identical to the plain sum.
            {
                string d = "";
                Profile p = Profile.CreateLeoDefault("t"); p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0;
                GlobalSettings g75 = X5G(75);
                foreach (double fov in new double[] { 50, 55, 60, 65, 70, 75 })
                {
                    GlobalSettings g = X5G(fov);
                    double r = fov / 75.0, kx = g.ResolutionScaleX, k = g.ResolutionScale;
                    for (int hand = 0; hand < 2; hand++)
                    {
                        HandView v = new HandView(); p.FillView(hand, v);
                        for (int dir = -1; dir < 8; dir++)
                            for (int lean = -1; lean <= 1; lean++)
                            {
                                MotionEngine e = W23Hold(p, g, hand == 1, W23Keys(dir, lean));
                                double lx = lean < 0 ? v.QX : lean > 0 ? v.EX : 0, ly = lean < 0 ? v.QY : lean > 0 ? v.EY : 0;
                                double st = dir < 0 && lean != 0 ? 0 : 1; // X8 (Leo 21 Nov option A): standing + lean counts from the centre
                                double sumX = kx * ((dir < 0 ? 0 : v.DX[dir]) + lx + st * v.SX), sumY = -k * ((dir < 0 ? 0 : v.DY[dir]) + ly + st * v.SY);
                                double gunSide = hand == 0 ? 1 : -1;
                                double ex = sumX * gunSide > 0 ? sumX : sumX * r;
                                if (Math.Abs(e.OffsetX - ex) > 0.06 || Math.Abs(e.OffsetY - sumY) > 0.06)
                                    d += "FOV" + fov + " hand" + hand + " dir" + dir + " lean" + lean + " got " + X5F(e.OffsetX) + "," + X5F(e.OffsetY) + " want " + X5F(ex) + "," + X5F(sumY) + "; ";
                                if (fov == 75)
                                {
                                    MotionEngine e2 = W23Hold(p, g75, hand == 1, W23Keys(dir, lean));
                                    if (e2.OffsetX != e.OffsetX) d += "75 not deterministic; ";
                                }
                            }
                    }
                }
                Random rnd = new Random(5115);
                for (int i = 0; i < 20000; i++)
                {
                    double x = (rnd.NextDouble() - 0.5) * 2000, h = rnd.NextDouble();
                    if (ViewScale.FovX(x, 1.0, h) != x) { d += "FovX r=1 changed " + x + "; "; break; }
                }
                Check("X5-2 8 directions + standing x Q/E/none x both hands x FOV 50..75: X gun side x1, other side x FOV/75 (换手 mirrored), Y unchanged; FOV 75 = plain sum bit for bit", d.Length == 0, d);
            }

            // X5-3 equal FOVs are bit-identical to v5.1: Factor = half span ratio (= old formula with tan ratio 1), the axis
            // % helpers = the plain ones, an old settings file loads to the same k and the same engine output.
            {
                string d = "";
                Random r = new Random(5113);
                for (int i = 0; i < 5000; i++)
                {
                    double gw = 640 + r.Next(7041), gh = 480 + r.Next(3841), bw = 640 + r.Next(7041), bh = 480 + r.Next(3841);
                    string ga = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)], ba = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)];
                    double f = 50 + r.Next(26);
                    double k = ViewScale.Factor(gw, gh, ga, f, bw, bh, ba, f, "v");
                    double old = (ViewScale.HalfSpan(gw, gh, ga, "v") / ViewScale.HalfSpan(bw, bh, ba, "v")) * 1.0;
                    if (k != old) { d += "k " + gw + "x" + gh + "; "; break; }
                    GlobalSettings g = GlobalSettings.CreateDefault(); g.ResGameW = (int)gw; g.ResGameH = (int)gh; g.ResFov = f; g.ResBaseFov = f; g.Validate();
                    double px = (r.NextDouble() - 0.5) * 1000, o = (r.NextDouble() - 0.5) * 200;
                    if (OffsetUnits.ToPct(px, OffsetAxis.X, g) != OffsetUnits.ToPct(px, g) || OffsetUnits.ToPctAt(px, o, OffsetAxis.X, true, g) != OffsetUnits.ToPct(px, g)
                        || OffsetUnits.ToPx(px, OffsetAxis.X, g) != OffsetUnits.ToPx(px, g) || OffsetUnits.ToPxAt(px, o, OffsetAxis.X, false, g) != OffsetUnits.ToPx(px, g))
                    { d += "pct helpers differ at equal FOV; "; break; }
                }
                string err; GlobalSettings old1;
                if (!SettingsIO.TryParse(File.ReadAllText(Fixture("leo-v30-1027-settings.json")), out old1, out err)) d += "fixture " + err + "; ";
                else
                {
                    if (ViewScale.FovRatio(old1) != 1.0 || old1.ResolutionScale != old1.ResGameH / 1440.0) d += "old file k " + old1.ResolutionScale + "; ";
                    Profile p = old1.Active;
                    HandView v = new HandView(); p.FillView(0, v);
                    MotionEngine e = W23Hold(p, old1, false, W23Keys(3, 0));
                    double want = old1.ResolutionScaleX * (v.DX[3] + v.SX);
                    if (Math.Abs(e.OffsetX - want) > 0.06) d += "old file D " + e.OffsetX + " vs " + want + "; ";
                }
                Check("X5-3 equal FOV = v5.1 bit for bit: k = old formula (5000 screens), % helpers identical, Leo's v3.0 file loads with the same k and the same D offset", d.Length == 0, d);
            }

            // X5-4 % ⇄ px round trips (FOV side and origin) and the 常规 / dashboard lines.
            {
                string d = "";
                GlobalSettings g = X5G(60);
                for (int i = -7000; i <= 7000; i += 7)
                    foreach (double o in new double[] { 0, 24.48, -24.48, 80 })
                        foreach (bool sw in new bool[] { false, true })
                        {
                            double pct = i / 100.0;
                            double px = OffsetUnits.ToPxAt(pct, o, OffsetAxis.X, sw, g);
                            if (Math.Abs(OffsetUnits.ToPctAt(px, o, OffsetAxis.X, sw, g) - pct) > 1e-9) { d += "rt " + pct + " o" + o + " sw" + sw + "; "; break; }
                        }
                string line = FovText.Line(g), sh = FovText.Short(ViewScale.FovRatio(g));
                if (line != "FOV 换算：枪那一侧 ×1.00，另一侧 ×0.80，上下不变") d += "line " + line + "; ";
                if (sh != "视野换算 枪侧×1.00 另侧×0.80" || sh.Length > 25) d += "short " + sh + "; ";
                if (FovText.Line(X5G(75)) != "FOV 换算：枪那一侧 ×1.00，另一侧 ×1.00，上下不变") d += "line75; ";
                string gen = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/SettingsForm.PageGeneral.cs");
                if (gen.IndexOf("FovLineText(G)", StringComparison.Ordinal) < 0) d += "常规 not wired; ";
                string dash = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/DashboardLogic.cs");
                if (dash.IndexOf("\"τ=tan\"", StringComparison.Ordinal) >= 0) d += "old τ line still drawn; ";
                Check("X5-4 % ⇄ px exact with FOV side + 站立 origin (both hands); 常规 「FOV 换算：枪那一侧 ×1.00，另一侧 ×0.80，上下不变」, dashboard 「视野换算 枪侧×1.00 另侧×0.80」, old τ line gone", d.Length == 0, d);
            }

            // X5-5 Leo's new built-in default (21 Nov 2026): 上抬 28.8 px = 4.00 %, 站立静止偏移 24.48 / -25.2 with StandAsZero,
            // every direction and 换手 custom item at the SAME screen place as the old default (0 px error), FOV 75.
            {
                string d = "";
                Profile n = Profile.CreateLeoDefault("n"), o = X5OldDefault();
                GlobalSettings g = GlobalSettings.CreateDefault();
                if (n.RecoilMaxPx != 28.8 || OffsetUnits.Format(n.RecoilMaxPx, OffsetAxis.Y, g) != "↑ 4.00%") d += "recoil " + n.RecoilMaxPx + "; ";
                if (!n.StandAsZero || n.StandOffsetX != 24.48 || n.StandOffsetY != -25.2) d += "stand " + n.StandAsZero + " " + n.StandOffsetX + "," + n.StandOffsetY + "; ";
                if (g.ResFov != 75 || g.ResBaseFov != 75) d += "fov " + g.ResFov + "; ";
                double worst = 0;
                for (int hand = 0; hand < 2; hand++)
                {
                    HandView vn = new HandView(), vo = new HandView(); n.FillView(hand, vn); o.FillView(hand, vo);
                    for (int i = 0; i < 8; i++)
                    {
                        worst = Math.Max(worst, Math.Abs(vn.DX[i] + vn.SX - (vo.DX[i] + vo.SX)));
                        worst = Math.Max(worst, Math.Abs(vn.DY[i] + vn.SY - (vo.DY[i] + vo.SY)));
                    }
                    worst = Math.Max(worst, Math.Abs(vn.QX + vn.SX - (vo.QX + vo.SX + vn.SX)));   // lean adds on top of the 站立 point
                    worst = Math.Max(worst, Math.Abs(vn.EX + vn.SX - (vo.EX + vo.SX + vn.SX)));
                }
                if (worst > 1e-9) d += "positions moved " + worst.ToString("E2") + " px; ";
                // engine: every direction (both hands) lands where the old default landed (recoil off)
                n.RecoilMaxPx = 0; o.RecoilMaxPx = 0; n.RecoilClickKickPx = 0; o.RecoilClickKickPx = 0;
                double we = 0;
                for (int hand = 0; hand < 2; hand++)
                    for (int dir = 0; dir < 8; dir++)
                    {
                        MotionEngine en = W23Hold(n, g, hand == 1, W23Keys(dir, 0)), eo = W23Hold(o, g, hand == 1, W23Keys(dir, 0));
                        we = Math.Max(we, Math.Abs(en.OffsetX - eo.OffsetX) + Math.Abs(en.OffsetY - eo.OffsetY));
                    }
                if (we > 0.06) d += "engine moved " + we + "; ";
                // only these values changed (everything else = the old default)
                Profile o2 = X5OldDefault(), n2 = Profile.CreateLeoDefault("old");
                n2.RecoilMaxPx = o2.RecoilMaxPx; n2.StandAsZero = o2.StandAsZero; n2.StandOffsetX = o2.StandOffsetX; n2.StandOffsetY = o2.StandOffsetY;
                for (int i = 0; i < 8; i++) { n2.Dirs[i] = o2.Dirs[i]; n2.HandCustom[i] = o2.HandCustom[i]; }
                if (SettingsIO.ExportProfile(n2) != SettingsIO.ExportProfile(o2)) d += "another value changed; ";
                Check("X5-5 new 「Leo默认」 (Leo 21 Nov 2026): 上抬 ↑ 4.00 %, 站立 24.48 / -25.2 (StandAsZero), FOV 75, all 8 directions + 换手 at the old screen place (0 px), nothing else changed", d.Length == 0, d);
            }

            // X5-6 强化 default = 鼠标侧键2 (XButton2, no modifier), 换手 stays 侧键1; texts tell the player to bind the game's
            // 切换战术装置模式 to the same button.
            {
                string d = "";
                KeyBindings k = new KeyBindings();
                if (k.Boost != 6 || k.BoostMods != 0 || k.HandSwitch != 5 || k.HandSwitchMods != 0) d += "keys " + k.Boost + "/" + k.BoostMods + " " + k.HandSwitch + "; ";
                GlobalSettings g = GlobalSettings.CreateDefault();
                if (g.Keys.ComboOf(KeyBindings.ActionBoost) != "鼠标侧键2（XButton2）") d += "fresh " + g.Keys.ComboOf(KeyBindings.ActionBoost) + "; ";
                string mr = MustRead.PlainText(g);
                if (mr.IndexOf("鼠标侧键2", StringComparison.Ordinal) < 0 || mr.IndexOf("切换战术装置模式", StringComparison.Ordinal) < 0 || mr.IndexOf("Ctrl+T", StringComparison.Ordinal) >= 0) d += "must-read; ";
                string readme = File.ReadAllText("/home/dev/tarkov-crosshair-v4/说明 README.txt");
                if (readme.IndexOf("默认鼠标侧键2", StringComparison.Ordinal) < 0 || System.Text.RegularExpressions.Regex.IsMatch(readme, "Ctrl\\+T(?!ab)")) d += "README; ";
                if (GuideText.Text.Replace("\r", "") != readme.Replace("\r", "").TrimStart('\uFEFF')) d += "GuideText stale; ";
                Check("X5-6 强化 default 鼠标侧键2 (6, no mods), 换手 侧键1; fresh install shows 「鼠标侧键2（XButton2）」; must-read + README say bind the game's 切换战术装置模式 to it, no Ctrl+T left", d.Length == 0, d);
            }

            // X5-8 统计 opt-out (Leo 21 Nov 2026): fresh install / null = on, 不用 (false) stays off, turning it off stops the
            // heartbeat at once and is saved; no question; the 常规 / wizard notice. Fake http only, no real network.
            {
                string d = "";
                string url = "https://example.invalid";
                GlobalSettings g = GlobalSettings.CreateDefault();
                if (g.NetStatsConsent != null) d += "fresh not null; ";
                bool? stored = g.NetStatsConsent; int saves = 0;
                DelegateNetSettings st = new DelegateNetSettings(delegate { return true; }, delegate(bool v) { }, delegate { return stored; },
                    delegate(bool? v) { stored = v; }, delegate { return true; }, delegate(bool v) { }, delegate { saves++; });
                NetPrefs prefs = new NetPrefs();
                NetRules.Mirror(st, prefs);
                if (!NetRules.WantHeartbeat(st, url) || NetRules.NeedsStatsQuestion(st, url) || !prefs.UsageOptIn) d += "null not on; ";
                FakeHttp h = new FakeHttp();
                UsageHeartbeat hb = new UsageHeartbeat(h, prefs, url, "v5.1");
                DateTime t0 = new DateTime(2026, 11, 21, 1, 0, 0, DateTimeKind.Utc);
                if (!hb.Tick(t0) || h.Calls.Count != 1) d += "no first heartbeat; ";
                if (h.Posts.Count > 0 && (h.Posts[0].Contains("user") || MiniJson.Str(MiniJson.Parse(h.Posts[0]), "v") == null)) d += "payload; ";
                stored = false; st.Save(); NetRules.Mirror(st, prefs);
                int before = h.Calls.Count;
                hb.Tick(t0.AddMinutes(30)); hb.SendEnd();
                if (h.Calls.Count != before || NetRules.WantHeartbeat(st, url) || saves != 1) d += "off still sends; ";
                // a file that said 不用 stays off; a file without the field (or null) is on
                GlobalSettings f; string err;
                string j = SettingsIO.Serialize(GlobalSettings.CreateDefault());
                if (!SettingsIO.TryParse(System.Text.RegularExpressions.Regex.Replace(j, "\"NetStatsConsent\"\\s*:\\s*null", "\"NetStatsConsent\": false"), out f, out err) || f.NetStatsConsent != false) d += "false not kept; ";
                if (!SettingsIO.TryParse(j, out f, out err) || f.NetStatsConsent == false) d += "null read as off; ";
                string gen = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/SettingsForm.PageGeneral.cs"), wiz = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/WizardForm.cs");
                string prog = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/Program.cs");
                // Leo follow-up 21 Nov: the checkbox + notice only in 常规 高级设置; nothing about stats in the wizard
                if (!gen.Contains("G.NetStatsConsent != false") || !gen.Contains("AdvOnly(Line(stats") || !gen.Contains("AdvOnly(Note(UsageConfig.Notice")
                    || wiz.Contains("UsageConfig") || wiz.Contains("NetStatsConsent") || File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/WizardModel.cs").Contains("NetStatsConsent")) d += "notice / checkbox placement; ";
                if (prog.Contains("AskStatsIfNeeded(")) d += "question still asked; ";
                string readme = File.ReadAllText("/home/dev/tarkov-crosshair-v4/说明 README.txt");
                if (!readme.Contains("默认发送匿名统计") || readme.Contains("默认不用")) d += "README; ";
                bool faq = false; foreach (FaqText.Entry e in FaqText.Items) if (e.A.Contains("默认发送匿名统计")) faq = true;
                if (!faq) d += "FAQ; ";
                GlobalSettings w = GlobalSettings.CreateDefault(); w.NetStatsConsent = false; WizardModel m = new WizardModel(w); m.Apply(w);
                if (w.NetStatsConsent != false) d += "wizard changed the stats choice; "; // the wizard never touches it
                Check("X5-8 统计 opt-out (Leo 21 Nov 2026): fresh / null = on and sends (fake http), no question, 不用 file stays off, unticking stops the heartbeat at once + saved, wizard leaves it alone, checkbox + notice only in 常规 高级设置 (none in the wizard), README / FAQ disclosure kept", d.Length == 0, d);
            }

            // X5-9 (Leo's Windows log 21 Nov): the background build never drops a page -- the pending handle slices are
            // finished before the next page is taken from the queue (before: every other page was skipped and built on open)
            {
                string sf = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/SettingsForm.cs");
                int st = sf.IndexOf("public bool PrebuildStep()", StringComparison.Ordinal);
                int h = sf.IndexOf("if (handleQueue != null && handleQueue.Count > 0) { HandleSlice(); return true; }", st, StringComparison.Ordinal);
                int n = sf.IndexOf("prebuild.Next(", st, StringComparison.Ordinal);
                PrebuildQueue q = new PrebuildQueue(new int[] { 0, 1, 2, 3, 4 });
                bool[] built = new bool[5]; built[0] = true; int got = 0;
                for (int i = q.Next(delegate(int x) { return built[x]; }); i >= 0; i = q.Next(delegate(int x) { return built[x]; })) { built[i] = true; got++; }
                Check("X5-9 prebuild: handle slices finish before the next page is dequeued (no page skipped); queue visits every unbuilt page",
                    st > 0 && h > st && n > h && got == 4, "st=" + st + " h=" + h + " next=" + n + " got=" + got);
            }

            // X5-7 predicted box values (normal hand, 1920x1440 4:3, new default): A, D, Q, E as on-screen % (absolute) and as
            // the box shows them (distance from the 站立 point). Printed for V511-X5.md.
            {
                Profile p = Profile.CreateLeoDefault("t"); p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0;
                List<string> rows = new List<string>();
                foreach (double fov in new double[] { 50, 55, 60, 65, 70, 75 })
                {
                    GlobalSettings g = X5G(fov);
                    double o = OffsetUnits.StandOriginX(p, false);
                    double y;
                    double sa = X5Screen(p, g, false, 2, 0, out y), sd = X5Screen(p, g, false, 3, 0, out y), sq = X5Screen(p, g, false, -1, -1, out y), se = X5Screen(p, g, false, -1, 1, out y);
                    rows.Add("| " + fov + " | " + OffsetUnits.FormatAt(p.Dirs[2].OffX, o, OffsetAxis.X, false, g) + " (" + OffsetUnits.FormatPct(sa, OffsetAxis.X) + ") | "
                        + OffsetUnits.FormatAt(p.Dirs[3].OffX, o, OffsetAxis.X, false, g) + " (" + OffsetUnits.FormatPct(sd, OffsetAxis.X) + ") | "
                        + OffsetUnits.FormatAt(p.LeanQX, o, OffsetAxis.X, false, g) + " (" + OffsetUnits.FormatPct(sq, OffsetAxis.X) + ") | "
                        + OffsetUnits.FormatAt(p.LeanEX, o, OffsetAxis.X, false, g) + " (" + OffsetUnits.FormatPct(se, OffsetAxis.X) + ") |");
                }
                Console.WriteLine("X5-TABLE | FOV | A box (screen) | D box (screen) | Q box (screen) | E box (screen) |");
                foreach (string s in rows) Console.WriteLine("X5-TABLE " + s);
                Check("X5-7 prediction table printed (6 FOVs)", rows.Count == 6, "");
            }
        }
    }
}
