// v5.0.2 (W18): settings + appearance. Leo's decisions 3 (推荐 / every 默认 = his v5 file) and 4 (强化准星 = a full look of
// its own), BRUTE P2-2 (one typo no longer drops later profiles), BRUTE P3-3 (one bad byte no longer turns a UTF-8 file into
// GBK), UI-C H1 / M6 / L5 / L7 / L12 / L15 and the UI-thread rules of decision 5 for my files (source checks).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V502W18Init() { V502W18Tests(); }

        static string W18Src(string f)
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string p = Path.Combine(d, "..", "tarkov-crosshair-v4", "src", f);
                if (File.Exists(p)) return File.ReadAllText(p);
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return File.ReadAllText(Path.Combine("/home/dev/tarkov-crosshair-v4/src", f));
        }

        static string W18Dir(string tag)
        {
            string scratch = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            if (string.IsNullOrEmpty(scratch)) scratch = Path.GetTempPath();
            string dir = Path.Combine(scratch, "w18-" + tag + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void W18Clear(string dir) { foreach (string f in Directory.GetFiles(dir)) File.Delete(f); }

        static void W18Rm(string dir)
        {
            try { W18Clear(dir); Directory.Delete(dir); } catch (Exception) { }
        }

        static GlobalSettings W18Load(string dir, byte[] b, out string msg)
        {
            W18Clear(dir);
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllBytes(path, b);
            return SettingsIO.Load(path, out msg);
        }

        /// <summary>The profile 「Leo默认 (旧)」 of Leo's own file as JSON (the source of every 推荐 / 默认).</summary>
        static JsonObject W18LeoJson()
        {
            JsonObject root = (JsonObject)JsonNode.Parse(LeoDefaultJson.Text);
            foreach (JsonNode n in (JsonArray)root["Profiles"])
                if ((string)n["Name"] == LeoDefaultJson.ProfileName) return (JsonObject)n;
            return null;
        }

        /// <summary>Every member of want (recursively) is in got with the same value (numbers within 1e-9). Lists the misses.</summary>
        static void W18Same(JsonNode want, JsonNode got, string path, List<string> miss)
        {
            if (want == null) { if (got != null) miss.Add(path + " (want null)"); return; }
            if (got == null) { miss.Add(path + " (missing)"); return; }
            JsonObject wo = want as JsonObject;
            if (wo != null)
            {
                JsonObject go = got as JsonObject;
                if (go == null) { miss.Add(path + " (not an object)"); return; }
                foreach (KeyValuePair<string, JsonNode> kv in wo) W18Same(kv.Value, go[kv.Key], path + "." + kv.Key, miss);
                return;
            }
            JsonArray wa = want as JsonArray;
            if (wa != null)
            {
                JsonArray ga = got as JsonArray;
                if (ga == null || ga.Count != wa.Count) { miss.Add(path + " (list)"); return; }
                for (int i = 0; i < wa.Count; i++) W18Same(wa[i], ga[i], path + "[" + i + "]", miss);
                return;
            }
            JsonValue wv = (JsonValue)want, gv = got as JsonValue;
            if (gv == null) { miss.Add(path); return; }
            double a, b;
            if (wv.TryGetValue(out a) && gv.TryGetValue(out b)) { if (Math.Abs(a - b) > 1e-9) miss.Add(path + " " + a + "≠" + b); return; }
            if (wv.ToJsonString() != gv.ToJsonString()) miss.Add(path + " " + wv.ToJsonString() + "≠" + gv.ToJsonString());
        }

        static List<string> W18Diff(Profile p, JsonObject want, params string[] skip)
        {
            JsonObject got = (JsonObject)JsonNode.Parse(SettingsIO.ExportProfile(p));
            JsonObject w = (JsonObject)want.DeepClone();
            foreach (string s in skip) w.Remove(s);
            List<string> miss = new List<string>();
            W18Same(w, got, "", miss);
            return miss;
        }

        static bool W18SamePixels(RingStyle a, RingStyle b, out string why)
        {
            why = "";
            int na = RingRaster.CanvasSize(a), nb = RingRaster.CanvasSize(b);
            if (na != nb) { why = "canvas " + na + "/" + nb; return false; }
            int[] pa = RingRaster.Render(a, na, na / 2.0 + 0.3, na / 2.0 + 0.7), pb = RingRaster.Render(b, nb, nb / 2.0 + 0.3, nb / 2.0 + 0.7);
            for (int i = 0; i < pa.Length; i++) if (pa[i] != pb[i]) { why = "pixel " + i; return false; }
            if (a.OpacityPct != b.OpacityPct) { why = "opacity"; return false; }
            return true;
        }

        /// <summary>A random but valid look on p (tile, colours, outline, glow, feather, rotation, 中心留空, 强化 values).</summary>
        static void W18RandomLook(Profile p, Random r)
        {
            StyleTile t = StyleShapes.Tiles[r.Next(StyleShapes.Tiles.Length)];
            if (t.Id == "custom") t = StyleShapes.Tiles[0];
            StyleShapes.ApplyTile(p, t.Id);
            string[] grads = Profile.Gradients;
            p.Gradient = grads[r.Next(grads.Length)];
            p.Color = ColorUtil.Format(r.Next(256), r.Next(256), r.Next(256));
            p.Color2 = ColorUtil.Format(r.Next(256), r.Next(256), r.Next(256));
            p.HandColorOn = r.Next(2) == 0;
            p.HandColor = ColorUtil.Format(r.Next(256), r.Next(256), r.Next(256));
            p.OuterThickness = 0.5 + r.Next(15) * 0.5;
            p.GlowPx = r.Next(3) == 0 ? 0 : r.Next(1, 16);
            p.GlowOpacityPct = r.Next(101);
            p.Feather = r.Next(0, 6) * 0.5;
            p.RotationDeg = r.Next(-180, 181);
            p.Diameter = Math.Max(4, Math.Min(40, p.Diameter * (0.6 + r.NextDouble())));
            p.Thickness = 0.5 + r.Next(10) * 0.3;
            CrosshairStyle cs = StyleShapes.Of(p);
            cs.Hole = CrosshairStyle.MinHole + r.Next(0, 12) * 0.5;
            p.Boost.Diameter = Math.Max(4, Math.Min(60, p.Diameter * (0.7 + r.NextDouble() * 2)));
            p.Boost.Thickness = 0.5 + r.Next(12) * 0.4;
            p.Boost.OuterThickness = r.Next(0, 10) * 0.5;
            p.Boost.OpacityPct = 5 + r.Next(96);
            p.Boost.GlowPx = r.Next(0, 12);
            p.Boost.GlowOpacityPct = r.Next(101);
            p.Boost.FollowHandColor = r.Next(2) == 0;
            p.Boost.Color = ColorUtil.Format(r.Next(256), r.Next(256), r.Next(256));
            p.Boost.Color2 = ColorUtil.Format(r.Next(256), r.Next(256), r.Next(256));
            p.Validate();
        }

        static void V502W18Tests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            JsonObject leo = W18LeoJson();

            // ---- W18-1 decision 3: 推荐 = the curve in Leo's file; fresh install shows 「推荐」; 恢复推荐 = exactly his values
            {
                JsonObject ad = (JsonObject)leo["CurveAD"], ws = (JsonObject)leo["CurveWS"];
                bool constOk = (double)ad["DurationMs"] == Profile.RecommendedCurveMs && (double)ws["DurationMs"] == Profile.RecommendedCurveMs;
                for (int i = 0; i < MotionCurve.Points; i++)
                    constOk &= (double)ad["Speeds"][i] == Profile.RecommendedSpeeds[i] && (double)ws["Speeds"][i] == Profile.RecommendedSpeeds[i];
                GlobalSettings d = GlobalSettings.CreateDefault(); d.Validate();
                bool fresh = d.Active.CurveAD.IsRecommended && d.Active.CurveWS.IsRecommended;
                Profile p = d.Active.Clone();
                p.CurveAD.DurationMs = 120; p.CurveAD.SetPreset(3); p.CurveWS.SetPreset(1);
                bool changed = !p.CurveAD.IsRecommended && !p.CurveWS.IsRecommended;
                p.ResetCurvesToRecommended();
                bool back = p.CurveAD.DurationMs == 430 && p.CurveWS.DurationMs == 430 && string.Join(",", p.CurveAD.Speeds) == "0,15,45,75,100"
                    && string.Join(",", p.CurveWS.Speeds) == "0,15,45,75,100" && p.CurveAD.IsRecommended;
                Profile blank = Profile.CreateBlank("b");
                bool blankRec = blank.CurveAD.IsRecommended && blank.CurveWS.IsRecommended;
                MoveCurve noSpeeds = new MoveCurve(250, MotionCurve.Presets[2]); noSpeeds.Speeds = null; noSpeeds.Validate();
                bool fallback = string.Join(",", noSpeeds.Speeds) == "0,15,45,75,100";
                string file = "/home/dev/tarkov-crosshair-dev/v4-requests/LEO-DEFAULT-v5-settings.json";
                bool src = !File.Exists(file) || JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(file)), JsonNode.Parse(LeoDefaultJson.Text));
                Check("W18-1 推荐 curve = Leo's file (「Leo默认 (旧)」 430 ms 0/15/45/75/100 on both axes); fresh install is 「推荐」; 恢复推荐 returns exactly to it; 新建配置 and a file curve without speeds get it; LeoDefaultJson = LEO-DEFAULT-v5-settings.json",
                    constOk && fresh && changed && back && blankRec && fallback && src,
                    "const=" + constOk + " fresh=" + fresh + " changed=" + changed + " back=" + back + " blank=" + blankRec + " fallback=" + fallback + " src=" + src);
            }

            // ---- W18-2 every 推荐 / 默认 (move curve, recoil, lean, jump, appearance, 强化, 换手) = Leo's file
            {
                GlobalSettings d = GlobalSettings.CreateDefault(); d.Validate();
                List<string> fresh = W18Diff(d.Active, leo, "Name", "SavedDefault", "SavedDefaultAt");
                Profile edited = d.Active;
                edited.RecoilMaxPx = 3; edited.LeanMs = 900; edited.Jump.TotalMs = 1500; edited.Diameter = 40; edited.Boost.GlowPx = 0; edited.HandSwitchMs = 1;
                edited.Dirs[0].OffY = 77; edited.CurveAD.DurationMs = 5;
                Profile restored = d.RestoreLeoDefault(edited);
                List<string> rest = W18Diff(restored, leo, "Name", "SavedDefault", "SavedDefaultAt");
                // 新建配置: Leo's 外观 / 强化 / 换手颜色 / times; amounts 0, 跳跃 off, 换手 all 自动
                Profile blank = Profile.CreateBlank("b");
                Profile leoP = Profile.CreateLeoDefault("x");
                bool look = blank.AppearanceKey() == leoP.AppearanceKey();
                bool times = blank.RecoilRiseMs == leoP.RecoilRiseMs && blank.RecoilFallMs == leoP.RecoilFallMs && blank.RecoilDelayMs == leoP.RecoilDelayMs
                    && blank.LeanMs == leoP.LeanMs && blank.HandSwitchMs == leoP.HandSwitchMs && blank.Jump.TotalMs == leoP.Jump.TotalMs;
                for (int i = 0; i < JumpSettings.PhaseCount; i++)
                    times &= blank.Jump.Phases[i].Ms == leoP.Jump.Phases[i].Ms && blank.Jump.Phases[i].Curve == leoP.Jump.Phases[i].Curve && blank.Jump.Phases[i].EndPx == 0;
                bool zero = !blank.Jump.Enabled && blank.RecoilMaxPx == 0 && blank.RecoilClickKickPx == 0 && blank.RecoilDriftX == 0
                    && blank.LeanQX == 0 && blank.LeanQY == 0 && blank.LeanEX == 0 && blank.LeanEY == 0 && !blank.AnyHandCustom() && blank.SavedDefault == null;
                foreach (DirSetting ds in blank.Dirs) zero &= ds.OffX == 0 && ds.OffY == 0;
                Check("W18-2 every 推荐 / 默认 = Leo's file: fresh install and 「恢复到 Leo 推荐」 equal 「Leo默认 (旧)」 in every member (move, curves, recoil, lean, 换手 450 ms + 换手自定义, 跳跃, 外观, 强化准星); 新建配置 = his 外观 / 强化 / 换手颜色 / times with amounts 0",
                    fresh.Count == 0 && rest.Count == 0 && look && times && zero,
                    "fresh=" + string.Join(";", fresh) + " restored=" + string.Join(";", rest) + " blankLook=" + look + " times=" + times + " zero=" + zero);
            }

            // ---- W18-3 BRUTE P2-2: one typo inside profile #1 keeps every later profile (400 typos, BRUTE's probe, seed 5)
            {
                string t = LeoDefaultJson.Text;
                GlobalSettings g0; string e0;
                SettingsIO.TryParse(t, out g0, out e0);
                List<string> names0 = new List<string>();
                foreach (Profile p in g0.Profiles) names0.Add(p.Name);
                int ps = t.IndexOf("\"Profiles\"", StringComparison.Ordinal);
                int p0 = t.IndexOf('{', ps), p1 = -1, depth = 0;
                bool inStr = false;
                for (int i = p0; i < t.Length; i++)
                {
                    char c = t[i];
                    if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                    if (c == '"') inStr = true; else if (c == '{') depth++; else if (c == '}') { depth--; if (depth == 0) { p1 = i; break; } }
                }
                string dir = W18Dir("typo");
                int cases = 0, keptAll = 0, threw = 0, lied = 0, firstKept = 0;
                Dictionary<string, int[]> byKind = new Dictionary<string, int[]>();
                string worst = null;
                try
                {
                    Random r = new Random(5);
                    for (int k = 0; k < 400; k++)
                    {
                        int at = p0 + 1 + r.Next(p1 - p0 - 1);
                        string kind, m;
                        switch (r.Next(5))
                        {
                            case 0: kind = "delete"; m = t.Remove(at, 1); break;
                            case 1: kind = "comma"; m = t.Insert(at, ","); break;
                            case 2: kind = "letter"; m = t.Insert(at, "x"); break;
                            case 3: kind = "quote"; m = t.Insert(at, "\""); break;
                            default: kind = "nocomma"; { int c = t.IndexOf(',', at); if (c < 0 || c > p1) { k--; continue; } m = t.Remove(c, 1); } break;
                        }
                        cases++;
                        string msg;
                        GlobalSettings g;
                        try { g = W18Load(dir, new UTF8Encoding(false).GetBytes(m), out msg); }
                        catch (Exception ex) { threw++; if (worst == null) worst = kind + "@" + at + " threw " + ex.Message; continue; }
                        bool all = true;
                        for (int i = 1; i < names0.Count; i++) if (g.FindProfile(names0[i]) == null) all = false;
                        if (g.Profiles.Count >= names0.Count) firstKept++;
                        int[] st; if (!byKind.TryGetValue(kind, out st)) { st = new int[2]; byKind[kind] = st; }
                        st[1]++;
                        if (all) { keptAll++; st[0]++; }
                        else if (worst == null) worst = kind + "@" + at + " ctx=" + m.Substring(Math.Max(0, at - 50), Math.Min(90, m.Length - Math.Max(0, at - 50))).Replace("\n", "⏎") + ": [" + string.Join(" | ", g.Profiles.ConvertAll(delegate(Profile q) { return q.Name; })) + "]";
                        if (msg != null && msg.Contains("配置没能读出来")) lied++;
                    }
                    // anywhere in the file (top-level settings, Keys, both profiles): both profiles stay, whatever was hit
                    Random r2 = new Random(18);
                    int anyCases = 0, anyBoth = 0;
                    string anyBad = null;
                    for (int k = 0; k < 1500; k++)
                    {
                        int at = 1 + r2.Next(t.Length - 2);
                        string m;
                        switch (r2.Next(4))
                        {
                            case 0: m = t.Remove(at, 1); break;
                            case 1: m = t.Insert(at, ","); break;
                            case 2: m = t.Insert(at, "x"); break;
                            default: m = t.Insert(at, "\""); break;
                        }
                        string msg;
                        GlobalSettings g;
                        try { g = W18Load(dir, new UTF8Encoding(false).GetBytes(m), out msg); }
                        catch (Exception) { threw++; continue; }
                        anyCases++;
                        if (g.Profiles.Count == names0.Count) anyBoth++;
                        else if (anyBad == null) anyBad = "@" + at + " ctx=" + m.Substring(Math.Max(0, at - 40), Math.Min(80, m.Length - Math.Max(0, at - 40))).Replace("\n", "⏎") + " -> " + g.Profiles.Count;
                    }
                    // the repro file of the report: one '"' inserted before "OffY" in profile #1
                    int q = t.IndexOf("\"OffY\": -33", StringComparison.Ordinal);
                    string rep = q < 0 ? t : t.Insert(q, "\"");
                    string rmsg;
                    GlobalSettings rg = W18Load(dir, new UTF8Encoding(false).GetBytes(rep), out rmsg);
                    bool repro = q > 0 && rg.FindProfile("Leo默认") != null && rg.FindProfile("Leo默认 (旧)") != null && rmsg != null && rmsg.Contains("2 套配置");
                    // the old cut-off behaviour stays: a file cut inside profile #2 keeps profile #1 and drops the cut one
                    string cut = t.Substring(0, t.LastIndexOf("\"HandSwitchMs\"", StringComparison.Ordinal));
                    string cmsg;
                    GlobalSettings cg = W18Load(dir, new UTF8Encoding(false).GetBytes(cut), out cmsg);
                    bool cutOk = cg.FindProfile("Leo默认") != null && cg.FindProfile("Leo默认 (旧)") == null;
                    StringBuilder sb = new StringBuilder();
                    foreach (KeyValuePair<string, int[]> kv in byKind) sb.Append(kv.Key).Append(' ').Append(kv.Value[0]).Append('/').Append(kv.Value[1]).Append(' ');
                    Check("W18-3 BRUTE P2-2: a typo in profile #1 skips only the damaged entry: profile #2 survives in all 400 typos (was 237 of 400; stray quote 0 of 80); 1,500 typos anywhere in the file keep both profiles; the report's repro file keeps both profiles, a cut-off file still drops only the unfinished profile, never a throw",
                        keptAll == cases && threw == 0 && repro && cutOk && lied == 0 && anyBoth == anyCases,
                        "anywhere: both kept " + anyBoth + "/" + anyCases + (anyBad == null ? "" : " e.g. " + anyBad) + "; kept " + keptAll + "/" + cases + " (" + sb + ") profile#1 kept " + firstKept + " threw=" + threw + " repro=" + repro + " cut=" + cutOk + " lied=" + lied + (worst == null ? "" : " e.g. " + worst));
                }
                finally { W18Rm(dir); }
            }

            // ---- W18-4 BRUTE P3-3: one bad byte in a BOM-less UTF-8 file keeps every Chinese name; GBK files still read as GBK
            {
                string dir = W18Dir("enc");
                try
                {
                    int cases = 0, garbled = 0, gbkWrong = 0, gbkCases = 0, noMsg = 0;
                    string eg = null;
                    foreach (string src in new string[] { "leo-v30-1027-settings.json", null })
                    {
                        byte[] orig = src == null ? new UTF8Encoding(false).GetBytes(LeoDefaultJson.Text) : File.ReadAllBytes(Fixture(src));
                        if (orig.Length >= 3 && orig[0] == 0xEF && orig[1] == 0xBB && orig[2] == 0xBF) { byte[] o2 = new byte[orig.Length - 3]; Array.Copy(orig, 3, o2, 0, o2.Length); orig = o2; }
                        bool oe;
                        string text0 = SettingsIO.DecodeSettingsBytes(orig, out oe);
                        HashSet<char> chars0 = new HashSet<char>(text0.ToCharArray());
                        foreach (char c in GlobalSettings.DefaultProfileName + "配置旧") chars0.Add(c);
                        List<byte[]> copies = new List<byte[]>();
                        for (int k = 0; k < 150; k++) { byte[] b = (byte[])orig.Clone(); b[(int)((long)k * orig.Length / 150)] = 0xFF; copies.Add(b); }
                        for (int i = 1, n = 0; i < orig.Length && n < 60; i++)
                            if ((orig[i] & 0xC0) == 0x80) { byte[] b = new byte[i]; Array.Copy(orig, b, i); copies.Add(b); n++; }
                        foreach (byte[] b in copies)
                        {
                            bool old, dmg;
                            string s = SettingsIO.DecodeSettingsBytes(b, out old, out dmg);
                            cases++;
                            bool bad = old;
                            foreach (char c in s) if (c > 127 && c != '\uFFFD' && !chars0.Contains(c)) bad = true;
                            if (bad) { garbled++; if (eg == null) eg = (src ?? "LeoDefault") + " old=" + old; }
                        }
                    }
                    // the report's repro: fixture byte 44 = 0xFF -> 「Leo默认」 kept, message + backup
                    byte[] fx = File.ReadAllBytes(Fixture("leo-v30-1027-settings.json"));
                    fx[44] = 0xFF;
                    string msg;
                    GlobalSettings g = W18Load(dir, fx, out msg);
                    bool repro = g.FindProfile("Leo默认") != null && msg != null && msg.Contains("读不出来") && msg.Contains("备份");
                    if (!repro) noMsg++;
                    // GB18030 / GBK files (no UTF-8 at all) still decode as the old Chinese encoding
                    Random r = new Random(9);
                    Encoding gb = Encoding.GetEncoding(54936);
                    for (int k = 0; k < 3000; k++)
                    {
                        StringBuilder names = new StringBuilder();
                        int np = 1 + r.Next(4);
                        for (int j = 0; j < np; j++)
                        {
                            if (j > 0) names.Append(',');
                            names.Append("{\"Name\":\"").Append(k % 3 == 0 ? "M4 " : "");
                            int len = 1 + r.Next(8);
                            for (int c = 0; c < len; c++) names.Append((char)(0x4E00 + r.Next(0x9FA5 - 0x4E00)));
                            names.Append("\"}");
                        }
                        string json = "{\"Profiles\":[" + names + "]}";
                        byte[] b = gb.GetBytes(json);
                        bool strict = true;
                        try { new UTF8Encoding(false, true).GetString(b); } catch (ArgumentException) { strict = false; }
                        if (strict) continue;
                        gbkCases++;
                        bool old, dmg;
                        string s = SettingsIO.DecodeSettingsBytes(b, out old, out dmg);
                        if (!old || s != json) gbkWrong++;
                    }
                    Check("W18-4 BRUTE P3-3: one 0xFF byte or a cut inside a character of a BOM-less UTF-8 settings file never garbles a name (was 97 / 228 of 312 / 322 on Mono); the repro file keeps 「Leo默认」 with a message and a backup; GB18030 files are still read as the old Chinese encoding",
                        garbled == 0 && repro && gbkWrong <= gbkCases / 1000,
                        "copies=" + cases + " garbled=" + garbled + (eg == null ? "" : " e.g. " + eg) + " repro=" + repro + " msg=" + msg + " gbk wrong " + gbkWrong + "/" + gbkCases);
                }
                finally { W18Rm(dir); }
            }

            // ---- W18-5 decision 4: an old 强化 look turned into a look of its own draws exactly the same (1000 random looks)
            {
                Random r = new Random(18);
                int n = 0, same = 0, roundTrip = 0, saved = 0;
                string why0 = null;
                for (int k = 0; k < 500; k++)
                {
                    Profile p = Profile.CreateLeoDefault("x");
                    W18RandomLook(p, r);
                    foreach (double hand in new double[] { 0, 1 })
                    {
                        Profile q = p.Clone();
                        RingStyle before = RingStyle.From(q, hand, true);
                        RingStyle rs0 = RingStyle.From(q, 0, true);
                        q.BeginOwnBoost(rs0.SGap, rs0.SLength, rs0.Hole);
                        Profile v = new Profile();
                        q.FillBoostView(v);
                        RingStyle after = RingStyle.From(v, hand, false);
                        n++;
                        string why;
                        if (W18SamePixels(before, after, out why)) same++; else if (why0 == null) why0 = "look " + k + " hand " + hand + ": " + why + " key " + before.Key() + " vs " + after.Key();
                        // StoreBoostView(FillBoostView) changes nothing; the file keeps the own look
                        string key = q.AppearanceKey();
                        q.StoreBoostView(v);
                        if (q.AppearanceKey() == key) roundTrip++;
                        GlobalSettings g = GlobalSettings.CreateDefault();
                        g.Profiles[0] = q; q.Name = g.ActiveProfile;
                        GlobalSettings back; string e;
                        if (SettingsIO.TryParse(SettingsIO.Serialize(g), out back, out e) && back.Active.Boost.OwnLook && back.Active.AppearanceKey() == q.AppearanceKey()) saved++;
                    }
                }
                GlobalSettings oldFile; string err;
                bool oldOff = SettingsIO.TryParse(LeoDefaultJson.Text, out oldFile, out err) && !oldFile.Active.Boost.OwnLook;
                Check("W18-5 decision 4: turning a pre-v5.0.2 强化 look into a look of its own (BeginOwnBoost) draws pixel for pixel the same (both hands); Fill/Store round trip and save / load keep it; old files load with OwnLook off",
                    same == n && roundTrip == n && saved == n && oldOff,
                    "same " + same + "/" + n + " roundTrip=" + roundTrip + " saved=" + saved + " oldOff=" + oldOff + (why0 == null ? "" : " first diff: " + why0));
            }

            // ---- W18-6 decision 4: every 外观 option of the normal look is a 强化 option (data), and the overlay draws it
            {
                // every 外观 member of Profile has a BoostStyle member of the same name (HandColor* are the normal look's 换手颜色)
                List<string> missing = new List<string>();
                string[] look = { "Diameter", "Thickness", "Feather", "OpacityPct", "Color", "Color2", "Gradient", "CenterDot", "CenterDotSize",
                    "OuterThickness", "Shape", "CrossLength", "CrossThickness", "CrossGap", "CrossTop", "Dashes", "DashGapPct", "GlowPx",
                    "GlowOpacityPct", "RotationDeg", "Style" };
                foreach (string m in look) if (typeof(BoostStyle).GetField(m) == null) missing.Add(m);
                // an own 强化 look with another style than the normal one: the boosted overlay frame is that look
                Random r = new Random(6);
                int n = 0, ok = 0;
                string why0 = null;
                for (int k = 0; k < 200; k++)
                {
                    Profile p = Profile.CreateLeoDefault("x");
                    W18RandomLook(p, r);
                    Profile v = new Profile();
                    RingStyle rs0 = RingStyle.From(p, 0, true);
                    p.BeginOwnBoost(rs0.SGap, rs0.SLength, rs0.Hole);
                    p.FillBoostView(v);
                    StyleTile t = StyleShapes.Tiles[r.Next(StyleShapes.Tiles.Length)];
                    if (t.Id != "custom") StyleShapes.ApplyTile(v, t.Id);
                    v.Gradient = Profile.Gradients[r.Next(Profile.Gradients.Length)];
                    v.Feather = r.Next(0, 4); v.RotationDeg = r.Next(-90, 90);
                    p.StoreBoostView(v);
                    p.Validate();
                    Profile v2 = new Profile();
                    p.FillBoostView(v2);
                    n++;
                    string why;
                    if (W18SamePixels(RingStyle.From(p, 0, true), RingStyle.From(v2, 0, false), out why)) ok++;
                    else if (why0 == null) why0 = why;
                }
                Check("W18-6 decision 4: BoostStyle has every 外观 member of the normal look; the boosted overlay frame (RingStyle.From(p, hand, true)) draws the own 强化 look (needs W16: RingStyle.Set uses Profile.FillBoostView when Boost.OwnLook)",
                    missing.Count == 0 && ok == n, "missing=" + string.Join(",", missing) + " drawn " + ok + "/" + n + (why0 == null ? "" : " first: " + why0));
            }

            // ---- W18-7 「和正常准星一样」 / 「恢复到 Leo 推荐」 keeps 外观 (CopyAppearanceFrom)
            {
                Random r = new Random(7);
                int n = 0, ok = 0, copyOk = 0;
                for (int k = 0; k < 150; k++)
                {
                    Profile p = Profile.CreateLeoDefault("x");
                    W18RandomLook(p, r);
                    p.Boost.FollowHandColor = true;
                    p.CopyNormalToBoost();
                    Profile v = new Profile();
                    p.FillBoostView(v);
                    n++;
                    string why;
                    if (p.Boost.OwnLook && W18SamePixels(RingStyle.From(p, 0, false), RingStyle.From(v, 0, false), out why)) ok++;
                    Profile c = Profile.CreateLeoDefault("y");
                    c.CopyAppearanceFrom(p);
                    if (c.AppearanceKey() == p.AppearanceKey() && c.RecoilMaxPx == 28.8) copyOk++; // v5.1.1 (X5, Leo 21 Nov 2026): 上抬 4.00 %
                }
                Check("W18-7 「和正常准星一样」 makes the 强化 look draw exactly like the normal one; CopyAppearanceFrom copies every 外观 value (incl. 强化 and style) and nothing else",
                    ok == n && copyOk == n, "copy " + ok + "/" + n + " appearance " + copyOk + "/" + n);
            }

            // ---- W18-8 source: UI-C H1 / M6 / L5 / L7 / L12 / L15, decision 5 in my files
            {
                string move = W18Src("SettingsForm.PageMove.cs"), style = W18Src("StylePanel.cs"), gen = W18Src("SettingsForm.PageGeneral.cs"),
                    lean = W18Src("SettingsForm.PageLeanHand.cs");
                bool h1 = !move.Contains("380") && !move.Contains("慢起 ·") && move.Contains("\"恢复推荐（\" + Profile.RecommendedCurveMs + \" ms）\"")
                    && !move.Contains("MotionCurve.RecommendedPreset ? AccentBtn");
                bool m6 = style.Contains("\"描边（推荐）\"") && !style.Contains("描边（推荐，");
                bool l7 = true;
                foreach (string f in new string[] { "SettingsForm.PageMove.cs", "SettingsForm.PageRecoil.cs", "SettingsForm.PageLeanHand.cs", "SettingsForm.PageJump.cs",
                    "SettingsForm.PageGeneral.cs", "SettingsForm.PageKeys.cs", "StylePanel.cs" })
                    if (W18Src(f).Contains("MessageBox.")) l7 = false;
                bool l12 = style.Contains("lblColorHead = Lbl(\"颜色 · 透明度\", RBody");
                bool l5 = !gen.Contains("factor.Font = monoFont") && move.Contains("state.Font = Font;");
                bool l15 = lean.Contains("S(14 + 12 + 4)");
                bool ui = gen.Contains("Autostart.BeginSet(") && !gen.Contains("Autostart.Enable(") && !gen.Contains("Autostart.Disable(")
                    && style.Contains("CustomCrosshair.BeginImport(") && !style.Contains("CustomCrosshair.Import(");
                bool boostUi = style.Contains("改强化准星的样子") && style.Contains("改回正常准星") && style.Contains("和正常准星一样") && style.Contains("FillBoostView")
                    && !style.Contains("StyleShapes.ApplyTile(Cur") && style.Contains("ApplyTileKeepBoost(Cur, t.Id)"); // an own 强化 look keeps its size on a tile change
                Check("W18-8 source: 移动 「恢复推荐（430 ms）」 from the constant, no 380 / 慢起 accent (UI-C H1); 「描边（推荐）」 (M6); no stock MessageBox in my pages (L7); colour title in the body font (L12); no mono font on Chinese labels (L5); 换手 4 px slack (L15); autostart and PNG upload off the UI thread (decision 5, BRUTE P3-5); 强化 editor buttons (decision 4)",
                    h1 && m6 && l7 && l12 && l5 && l15 && ui && boostUi,
                    "h1=" + h1 + " m6=" + m6 + " l7=" + l7 + " l12=" + l12 + " l5=" + l5 + " l15=" + l15 + " ui=" + ui + " boostUi=" + boostUi);
            }
        }
    }
}
