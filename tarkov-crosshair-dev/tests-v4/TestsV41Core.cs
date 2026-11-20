// CORE41 (v4.1): F2 (v4.3: F1, the removed mouse sway, had its tests removed with it) 游戏分辨率 / 画面比例 / FOV conversion (ViewScale).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V41CoreInit() { V41CoreTests(); }

        static double Tand(double deg) { return Math.Tan(deg * Math.PI / 180.0); }

        static GlobalSettings V41G()
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.ResGameW = 2160; g.ResGameH = 1440;
            g.ShowFadeMs = 0; g.HideFadeMs = 0;
            g.Validate();
            return g;
        }

        static void V41CoreTests()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            ViewTests();
            Brute40FixTests();
        }

        // ------------------------------------------------------------------ BRUTE40 fixes in CORE41 files

        static void Brute40FixTests()
        {
            // B1 新建空白配置 gets the 推荐 curve, not the deleted v2.3 one.
            {
                Profile b = Profile.CreateBlank("x"); b.Validate();
                Check("V41-B1 CreateBlank: both curves = 推荐 (430 ms, Leo默认)", b.CurveAD.IsRecommended && b.CurveWS.IsRecommended,
                    b.CurveAD.DurationMs + " ms " + string.Join(",", b.CurveAD.Speeds));
            }
            // B2 导入旧版设置 rejects {} / another app's settings.json; a folder prefers OUR settings.json over another app's.
            {
                string dir = Path.Combine(Path.GetTempPath(), "v41b2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "current.json");
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate();
                SettingsIO.Save(cur, target);
                int imp; string e1, e2, e3;
                string empty = Path.Combine(dir, "empty.json"); File.WriteAllText(empty, "{}");
                string other = Path.Combine(dir, "vscode.json"); File.WriteAllText(other, "{\"editor.fontSize\":14,\"Profiles\":[{\"name\":\"x\"}],\"Keys\":{}}");
                bool r1 = SettingsIO.ImportLegacy(cur, empty, target, out imp, out e1) == null && e1 != null;
                bool r2 = SettingsIO.ImportLegacy(cur, other, target, out imp, out e2) == null && e2 != null;
                string pkg = Path.Combine(dir, "pkg"); Directory.CreateDirectory(Path.Combine(pkg, "aaa-other"));
                Directory.CreateDirectory(Path.Combine(pkg, "zzz app"));
                File.WriteAllText(Path.Combine(pkg, "aaa-other", "settings.json"), "{\"theme\":\"dark\"}");
                File.WriteAllText(Path.Combine(pkg, "zzz app", "settings.json"), File.ReadAllText(Fixture("leo-v30-1027-settings.json")));
                string found = SettingsIO.FindLegacyFile(pkg);
                bool r3 = found != null && found.Contains("zzz app");
                bool ok4 = SettingsIO.LooksLikeCrosshairSettings(File.ReadAllText(Fixture("settings-v1.json"))) && !SettingsIO.LooksLikeCrosshairSettings("{\"Profiles\":[]}")
                    && !SettingsIO.LooksLikeCrosshairSettings(null) && SettingsIO.ImportLegacy(cur, pkg, target, out imp, out e3) != null && imp >= 1;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) File.Delete(f);
                foreach (string d in new string[] { Path.Combine(pkg, "aaa-other"), Path.Combine(pkg, "zzz app"), pkg, dir }) Directory.Delete(d);
                Check("V41-B2 导入旧版设置: {} and another app's settings.json rejected; a folder picks our settings.json over another app's; v1 fixture recognised",
                    r1 && r2 && r3 && ok4, "{}=" + r1 + " other=" + r2 + " folder=" + (found ?? "null") + " rest=" + ok4);
            }
            // B3 after the W/A/S/D migration no two actions share a key: the moved one takes the first free spare key.
            {
                Random r = new Random(3); int clashes = 0, n = 0, changedUntouched = 0; string first = null;
                int[] pool = { 0x57, 0x41, 0x53, 0x44, 0x20, 0x51, 0x45, 0x01, 0x02, 0x05, 0x06, 0x12, 0x43, 0x7B, 0x24 };
                for (int i = 0; i < 3000; i++)
                {
                    GlobalSettings g = GlobalSettings.CreateDefault();
                    foreach (int a in KeyBindings.RebindableActions) g.Keys.SetCombo(a, pool[r.Next(pool.Length)], 0);
                    // make the non-W/A/S/D bindings distinct (a file v4.0 could save); W/A/S/D ones get reset by the migration
                    List<int> seen = new List<int>(); bool distinct = true;
                    foreach (int a in KeyBindings.RebindableActions)
                    {
                        int vk = g.Keys.Get(a);
                        if (vk == 0x57 || vk == 0x41 || vk == 0x53 || vk == 0x44) continue;
                        if (seen.Contains(vk)) distinct = false; seen.Add(vk);
                    }
                    if (!distinct) continue;
                    Dictionary<int, int> before = new Dictionary<int, int>();
                    foreach (int a in KeyBindings.RebindableActions) before[a] = g.Keys.Get(a);
                    GlobalSettings b; string err;
                    if (!SettingsIO.TryParse(SettingsIO.Serialize(g), out b, out err)) continue;
                    n++;
                    foreach (int a in KeyBindings.RebindableActions)
                    {
                        if (GlobalSettings.ClashesRebindable(b.Keys, a)) { clashes++; if (first == null) first = "a=" + a + " vk=" + b.Keys.Get(a); break; }
                        int ov = before[a];
                        bool wasWasd = ov == 0x57 || ov == 0x41 || ov == 0x53 || ov == 0x44;
                        if (!wasWasd && b.Keys.Get(a) != ov) changedUntouched++;
                    }
                }
                Check("V41-B3 load after the W/A/S/D migration: no two actions on one key (" + n + " random key sets); keys the player chose are never changed",
                    n > 200 && clashes == 0 && changedUntouched == 0, clashes + " clash, " + changedUntouched + " changed " + (first ?? ""));
            }
            // B4 导入旧版设置 keeps this screen's 游戏分辨率 / 画面比例 / FOV.
            {
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.ResGameW = 3440; cur.ResGameH = 1440; cur.ResAspect = "21:9"; cur.ResFov = 62; cur.Validate();
                GlobalSettings old; string err;
                SettingsIO.TryParse(File.ReadAllText(Fixture("leo-v30-1027-settings.json")), out old, out err);
                int imp;
                GlobalSettings m = SettingsIO.MergeLegacy(cur, old, out imp);
                Check("V41-B4 导入旧版设置 keeps the current 游戏分辨率 3440x1440 / 21:9 / FOV 62 (old file had " + old.ResGameW + "x" + old.ResGameH + ")",
                    m.ResGameW == 3440 && m.ResGameH == 1440 && m.ResAspect == "21:9" && m.ResFov == 62 && imp >= 1, m.ResGameW + "x" + m.ResGameH + " " + m.ResAspect + " " + m.ResFov);
            }
            // B5 开关键 twice (ring off -> on) snaps to 正常手 and no lean at once (no 换手 / 侧身 slide).
            {
                GlobalSettings g = V41G(); g.LeanMode = "toggle"; Profile p = g.Active;
                Sim s = new Sim(p, g, 144);
                s.Step(In("K")); s.Run(1.0, In("")); s.Step(In("E")); s.Run(1.0, In(""));
                bool setUp = s.E.Hand == 1 && s.E.HandBlend == 1 && Math.Abs(s.E.Lean - 1) < 0.01;
                g.Enabled = false; s.Step(In("")); g.Enabled = true; s.Step(In(""));
                bool snap = s.E.HandBlend == 0 && s.E.Lean == 0 && s.E.Hand == 0;
                Check("V41-B5 开关键 x2 reset snaps: 正常手 (blend 0) and lean 0 on the first frame back on", setUp && snap,
                    "setUp=" + setUp + " blend=" + s.E.HandBlend + " lean=" + s.E.Lean);
            }
        }

        // ------------------------------------------------------------------ F2 ViewScale

        static void ViewTests()
        {
            // R1 identity: defaults (跟随, FOV 75 both, 垂直) -> k == 游戏高度 / 1440 bit for bit (the v4.0 rule), game == base -> 1.
            {
                int bad = 0, n = 0; string first = null;
                Random r = new Random(41);
                for (int i = 0; i < 4000; i++)
                {
                    GlobalSettings g = V41G();
                    g.ResGameW = 640 + r.Next(7041); g.ResGameH = 480 + r.Next(3841);
                    if (i == 0) { g.ResGameW = 2160; g.ResGameH = 1440; }
                    double want = g.ResGameH / 1440.0, old = ResolutionDetect.ScaleFor(g.ResGameH);
                    n++;
                    if (g.ResolutionScale != want || g.ResolutionScale != old) { bad++; if (first == null) first = g.ResGameW + "x" + g.ResGameH + " k=" + g.ResolutionScale.ToString("R"); }
                }
                GlobalSettings same = V41G();
                Check("V41-R1 default view (跟随 / FOV 75 / 垂直): k == 游戏高度 / 1440 bit for bit (" + n + " resolutions), game == base -> k == 1",
                    bad == 0 && same.ResolutionScale == 1.0, bad + " differ " + (first ?? ""));
            }
            // R1b game == base in every field (any resolution, aspect, FOV, type) -> k == 1 exactly, so nothing moves.
            {
                Random r = new Random(11); int bad = 0;
                for (int i = 0; i < 5000; i++)
                {
                    double w = 640 + r.Next(7041), h = 480 + r.Next(3841);
                    string a = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)];
                    double f = 50 + r.Next(26); string t = r.Next(2) == 0 ? "v" : "h";
                    if (ViewScale.Factor(w, h, a, f, w, h, a, f, t) != 1.0) bad++;
                }
                Check("V41-R1b game == base (5000 random resolutions / aspects / FOVs / types): k == 1 exactly", bad == 0, bad + " not 1");
            }
            // R2 FOV 75 -> 50 enlarges by tan(37.5°)/tan(25°); smaller FOV always bigger k.
            {
                GlobalSettings g = V41G(); g.ResFov = 50;
                double want = Tand(37.5) / Tand(25);
                bool mono = true; double prev = double.MaxValue;
                for (int f = 50; f <= 75; f++) { g.ResFov = f; double k = g.ResolutionScale; if (!(k < prev)) mono = false; prev = k; }
                g.ResFov = 50;
                bool ok = Math.Abs(g.ResolutionScale - want) < 1e-12 && want > 1.6 && prev == 1.0 && mono;
                Check("V41-R2 FOV 75 -> 50 at 2160x1440: k = tan(37.5°)/tan(25°) = " + want.ToString("0.0000") + " (> 1), k falls with FOV, FOV 75 -> 1",
                    ok, "k50=" + g.ResolutionScale.ToString("0.000000") + " mono=" + mono);
            }
            // R3 angle math == linear factor (px_new = f_new·tan(atan(px/f_base))) for random screens, aspects, FOVs, both FOV types.
            {
                Random r = new Random(7);
                double worst = 0; int n = 0;
                for (int i = 0; i < 20000; i++)
                {
                    double gw = 640 + r.Next(7041), gh = 480 + r.Next(3841), bw = 640 + r.Next(7041), bh = 480 + r.Next(3841);
                    string ga = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)], ba = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)];
                    double gf = 50 + r.Next(26), bf = 50 + r.Next(26);
                    string t = r.Next(2) == 0 ? "v" : "h";
                    double fg = ViewScale.FocalPx(gw, gh, ga, gf, t), fb = ViewScale.FocalPx(bw, bh, ba, bf, t);
                    double k = ViewScale.Factor(gw, gh, ga, gf, bw, bh, ba, bf, t);
                    double px = -300 + r.NextDouble() * 600;
                    double viaAngle = fg * Math.Tan(Math.Atan(px / fb));
                    worst = Math.Max(worst, Math.Abs(viaAngle - px * k) / Math.Max(1.0, Math.Abs(px * k)));
                    n++;
                }
                Check("V41-R3 angle conversion f_new·tan(atan(px/f_base)) == px·k (" + n + " random screens / aspects / FOVs / types)", worst < 1e-9, "worst rel " + worst.ToString("E1"));
            }
            // R4 (v4.2 stretch model, Leo 17 Nov 2026): the picture always fills the screen. Y keeps the full height; X gets
            // 分辨率比例 / 画面比例. Cases: 16:9 native, 4:3 native, 4:3 stretched on 16:9, 21:9 (native + chosen on 16:9).
            {
                Func<double, double, string, GlobalSettings> G = delegate(double w, double h, string a)
                { GlobalSettings x = V41G(); x.ResGameW = w; x.ResGameH = h; x.ResAspect = a; return x; };
                string d = "";
                Func<double, double, bool> eq = delegate(double a, double b) { return Math.Abs(a - b) < 1e-12; };
                // 16:9 native (chosen 16:9 on 1920x1080 == 跟随): kx == ky == 1080/1440
                GlobalSettings n169 = G(1920, 1080, "16:9"), a169 = G(1920, 1080, "auto");
                if (!(n169.ResolutionScale == 0.75 && n169.ResolutionScaleX == 0.75 && a169.ResolutionScaleX == 0.75 && ViewScale.StretchX(1920, 1080, "16:9") == 1.0)) d += " 16:9";
                // 4:3 native (1600x1200): no stretch, k = 1200/1440
                GlobalSettings n43 = G(1600, 1200, "4:3");
                if (!(eq(n43.ResolutionScale, 1200 / 1440.0) && n43.ResolutionScaleX == n43.ResolutionScale)) d += " 4:3 native";
                // 4:3 stretched on 1920x1080: Y 0.75, X 0.75 · (16/9)/(4/3) = 1.0
                GlobalSettings s43 = G(1920, 1080, "4:3");
                double sx43 = ViewScale.StretchX(1920, 1080, "4:3");
                if (!(eq(sx43, 4.0 / 3.0) && s43.ResolutionScale == 0.75 && eq(s43.ResolutionScaleX, 1.0))) d += " 4:3 stretched sx=" + sx43 + " kx=" + s43.ResolutionScaleX;
                // 21:9: 3440x1440 and 2560x1080 (43:18 / 64:27) with 21:9 chosen are native (within 3 %); 21:9 on 1920x1080 = squeezed X
                GlobalSettings u1 = G(3440, 1440, "21:9"), u2 = G(2560, 1080, "21:9"), s219 = G(1920, 1080, "21:9");
                if (!(u1.ResolutionScale == 1.0 && u1.ResolutionScaleX == 1.0 && u2.ResolutionScaleX == u2.ResolutionScale)) d += " 21:9 native";
                double sx219 = ViewScale.StretchX(1920, 1080, "21:9");
                if (!(eq(sx219, (16.0 / 9.0) / (21.0 / 9.0)) && s219.ResolutionScale == 0.75 && eq(s219.ResolutionScaleX, 0.75 * sx219))) d += " 21:9 on 16:9";
                // the picture keeps the full screen height (no letterbox), so 1 % = 游戏高度 / 200 for every ratio
                foreach (string a in ViewScale.AspectIds) if (OffsetUnits.HalfHeight(G(1920, 1080, a)) != 540) d += " halfH " + a;
                double rw, rh; ViewScale.RenderRect(1920, 1080, "4:3", out rw, out rh);
                if (!(eq(rw, 1440) && rh == 1080)) d += " render " + rw + "x" + rh;
                // FOV 类型 is ignored: a stored "h" reads back "v" and changes nothing
                GlobalSettings fh = G(1920, 1080, "4:3"); fh.ResFovType = "h";
                if (fh.ResFovType != "v" || fh.ResolutionScale != s43.ResolutionScale) d += " fovtype";
                // the 画面比例 box: shows the resolution's ratio for "auto", the game's list only
                if (ViewScale.DisplayAspect("auto", 1920, 1080) != "16:9" || ViewScale.DisplayAspect("auto", 2160, 1440) != "3:2"
                    || ViewScale.DisplayAspect("auto", 3440, 1440) != "21:9" || ViewScale.DisplayAspect("auto", 1280, 1024) != "5:4"
                    || ViewScale.DisplayAspect("4:3", 1920, 1080) != "4:3" || Array.IndexOf(ViewScale.GameAspectIds, "auto") >= 0) d += " display";
                Check("V42-A1 画面比例 stretch: 16:9 native k 0.75 both, 4:3 native, 4:3 on 1920x1080 X ×1.333 (kx 1.0, ky 0.75), 21:9 native (3440x1440, 2560x1080) and 21:9 on 16:9 (X ×0.762); full height; FOV 类型 ignored",
                    d.Length == 0, d);
            }
            // R4b old letterbox maths is gone, X and Y factors only differ when the ratio is not native (random fuzz).
            {
                Random r = new Random(42); int bad = 0;
                for (int i = 0; i < 20000; i++)
                {
                    double w = 640 + r.Next(7041), h = 480 + r.Next(3841);
                    string a = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)];
                    GlobalSettings g = V41G(); g.ResGameW = w; g.ResGameH = h; g.ResAspect = a; g.ResFov = 50 + r.Next(26);
                    double ky = g.ResolutionScale, kx = g.ResolutionScaleX, sx = ViewScale.StretchX(w, h, a);
                    if (!(kx > 0) || double.IsInfinity(kx) || Math.Abs(kx - ky * sx) > 1e-12 * Math.Max(1, kx)) bad++;
                    if (ky != ViewScale.Factor(w, h, "auto", g.ResFov, 2160, 1440, "auto", 75, "v")) bad++;
                }
                Check("V42-A2 20000 random screens / ratios / FOVs: Y factor ignores 画面比例, X factor = Y × StretchX", bad == 0, bad + " bad");
            }
            // R5 horizontal and vertical FOV give the same focal length when they describe the same picture.
            {
                Random r = new Random(5); double worst = 0;
                for (int i = 0; i < 5000; i++)
                {
                    double w = 640 + r.Next(7041), h = 480 + r.Next(3841);
                    string a = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)];
                    double rw, rh; ViewScale.RenderRect(w, h, a, out rw, out rh);
                    double hf = 60 + r.NextDouble() * 60;
                    double vf = ViewScale.HorizontalToVertical(hf, rw / rh);
                    double fh = ViewScale.FocalPx(w, h, a, hf, "h"), fv = ViewScale.FocalPx(w, h, a, vf, "v");
                    worst = Math.Max(worst, Math.Abs(fh - fv) / fv);
                }
                double v90 = ViewScale.HorizontalToVertical(90, 16.0 / 9.0); // 2·atan(9/16) = 58.72°
                Check("V41-R5 水平 FOV and the matching 垂直 FOV give one focal length (5000 cases); 90° 水平 at 16:9 = 58.72° 垂直",
                    worst < 1e-12 && Math.Abs(v90 - 58.7155) < 1e-3, "worst rel " + worst.ToString("E1") + " v90=" + v90.ToString("0.0000"));
            }
            // R6 every offset uses the same k on X and Y: movement, lean, recoil, jump, hand, mouse lag.
            {
                double worst = 0; string where = "";
                string[] combos = { "D", "A", "WD", "SA", "Q", "E", "L", "DL", "J", "WJ", "QL", "K", "KD" };
                foreach (string c in combos)
                {
                    GlobalSettings b = V41G(); Profile p = b.Active;
                    GlobalSettings g = V41G(); g.ResGameW = 1920; g.ResGameH = 1080; g.ResFov = 60; g.ResAspect = "21:9";
                    double k = g.ResolutionScale, kx = g.ResolutionScaleX;
                    Sim sb = new Sim(p, b, 144), sg = new Sim(p, g, 144);
                    for (int f = 0; f < 100; f++)
                    {
                        string keys = c.Replace("J", "").Replace("K", f < 3 ? "K" : "");
                        InputState i = In(keys); i.Jump = c.Contains("J") && f < 3;
                        sb.Step(i); sg.Step(i);
                        double e = Math.Abs(sg.E.OffsetX - kx * sb.E.OffsetX) + Math.Abs(sg.E.OffsetY - k * sb.E.OffsetY);
                        if (e > worst) { worst = e; where = c + "@" + f; }
                    }
                }
                Check("V41-R6 1920x1080 / 21:9 / FOV 60: every offset (move, lean, recoil, jump, hand) = kx × / ky × the 2160x1440 offset on X / Y (v4.2 stretch)",
                    worst < 1e-9, "worst " + worst.ToString("E1") + " " + where);
            }
            // R7 migration: Leo's v3.0 file and a v4.0-shaped file (no v4.1 keys) -> base 2160x1440, FOV 75 both, 跟随, 垂直; k == h / 1440.
            {
                List<string> files = new List<string>();
                files.Add(File.ReadAllText(Fixture("leo-v30-1027-settings.json")));
                GlobalSettings v40 = GlobalSettings.CreateDefault(); v40.ResGameW = 2560; v40.ResGameH = 1600; v40.Validate();
                JsonNode n = JsonNode.Parse(SettingsIO.Serialize(v40));
                foreach (string f in new string[] { "ResAspect", "ResFov", "ResBaseAspect", "ResBaseFov", "ResFovType", "ResV41" }) n.AsObject().Remove(f);
                n["ResBaseW"] = 2560; n["ResBaseH"] = 1600; // a pre-v3.4 style base: locked back to 2160x1440 like v4.0 did
                files.Add(n.ToJsonString());
                bool ok = true; string d = "";
                foreach (string json in files)
                {
                    GlobalSettings g; string err;
                    bool parsed = SettingsIO.TryParse(json, out g, out err);
                    bool one = parsed && g.ResV41Present && g.ResBaseW == 2160 && g.ResBaseH == 1440 && g.ResFov == 75 && g.ResBaseFov == 75
                        && g.ResAspect == "auto" && g.ResBaseAspect == "auto" && g.ResFovType == "v" && g.ResolutionScale == g.ResGameH / 1440.0;
                    if (!one) ok = false;
                    d += (parsed ? g.ResGameW + "x" + g.ResGameH + " base " + g.ResBaseW + "x" + g.ResBaseH + " k=" + g.ResolutionScale.ToString("0.####") : err) + "; ";
                }
                Check("V41-R7 old files (Leo v3.0, v4.0-shaped): base 2160x1440, FOV 75/75, 跟随, 垂直, k == 游戏高度/1440", ok, d);
            }
            // R8 v4.1 fields: saved + reloaded; clamped (FOV whole 50..75, unknown aspect -> 跟随, unknown type -> 垂直); base editable.
            {
                GlobalSettings g = V41G();
                g.ResAspect = "16:10"; g.ResFov = 62; g.ResBaseAspect = "3:2"; g.ResBaseFov = 70; g.ResFovType = "h"; g.ResBaseW = 2560; g.ResBaseH = 1600;
                GlobalSettings b; string err;
                bool rt = SettingsIO.TryParse(SettingsIO.Serialize(g), out b, out err) && b.ResAspect == "16:10" && b.ResFov == 62 && b.ResBaseAspect == "3:2"
                    && b.ResBaseFov == 70 && b.ResFovType == "v" && b.ResBaseW == 2560 && b.ResBaseH == 1600 && b.ResolutionScale == g.ResolutionScale;
                GlobalSettings c = V41G();
                c.ResFov = 30; c.ResBaseFov = 100.0; c.ResAspect = "17:x"; /* v5.1 (W34): decision 12 = any W:H is valid, so the bad aspect is now unparsable text */ c.ResBaseAspect = null; c.ResFovType = "x"; c.ResBaseW = 99999; c.ResBaseH = -3; c.Validate();
                GlobalSettings c2 = V41G(); c2.ResAspect = " 17:9 "; c2.ResBaseAspect = "1:1"; c2.Validate();
                bool cl = c2.ResAspect == "17:9" && c2.ResBaseAspect == "1:1" && c.ResFov == 50 && c.ResBaseFov == 75 && c.ResAspect == "auto" && c.ResBaseAspect == "auto" && c.ResFovType == "v" && c.ResBaseW == 7680 && c.ResBaseH == 480;
                GlobalSettings d = V41G(); d.ResFov = 62.4; d.ResBaseFov = double.NaN; d.Validate();
                bool rnd = d.ResFov == 62 && d.ResBaseFov == 75;
                GlobalSettings e = GlobalSettings.CreateDefault();
                bool defs = e.ResAspect == "auto" && e.ResFov == 75 && e.ResBaseAspect == "auto" && e.ResBaseFov == 75 && e.ResFovType == "v" && e.ResBaseW == 2160 && e.ResBaseH == 1440;
                Check("V41-R8 view fields: defaults 跟随/75/跟随/75/垂直/2160x1440, saved + reloaded, clamped (FOV 50..75 whole, bad aspect/type -> default; v5.1 any W:H + 1:1 kept), 调参分辨率 editable",
                    rt && cl && rnd && defs, "rt=" + rt + " clamp=" + cl + " round=" + rnd + " defaults=" + defs);
            }
        }
    }
}
