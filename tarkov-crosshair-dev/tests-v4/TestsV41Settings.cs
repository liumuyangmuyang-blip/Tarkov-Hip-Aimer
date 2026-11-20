// SETTINGS41F (v4.1): brute-settings P1/P2 fixes in Settings.cs, v4.3 (W10) old mouse-sway files + F11 free, 站着偏移
// always 0, Autostart via the HKCU Run key, TestsV4Brute scratch folder. Runs from a module initializer (like TestsV41Core).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V41SettingsInit() { V41SettingsTests(); }

        static void V41SettingsTests()
        {
            StImportCheck();
            StKeyClashes();
            StPresence();
            StActiveAndNames();
            StBackups();
            StDamaged();
            StEncodings();
            StF11Free();
            StStandZero();
            StMouseLagGone();
            StAutostart();
            StBruteScratch();
            StImportKeepsBase();
        }

        // ------------------------------------------------------------------ helpers

        static string StDir(string tag)
        {
            string s = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            if (string.IsNullOrEmpty(s)) s = Path.GetTempPath();
            string d = Path.Combine(s, "st41-" + tag + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>Removes a folder this test made (StDir), with what it holds.</summary>
        static void StRm(string d)
        {
            try
            {
                foreach (string x in Directory.GetDirectories(d)) StRm(x);
                foreach (string f in Directory.GetFiles(d)) File.Delete(f);
                Directory.Delete(d);
            }
            catch (Exception) { }
        }

        static void StClear(string d) { foreach (string f in Directory.GetFiles(d)) File.Delete(f); }

        static JsonObject StDefaultJson() { return (JsonObject)JsonNode.Parse(SettingsIO.Serialize(GlobalSettings.CreateDefault())); }

        static string StKeysJson(Action<JsonObject> edit)
        {
            JsonObject o = StDefaultJson();
            edit((JsonObject)o["Keys"]);
            return o.ToJsonString();
        }

        /// <summary>null when every action that is on has its own key + 组合键 and none is a plain W/A/S/D.</summary>
        static string StClash(GlobalSettings g)
        {
            List<int> acts = new List<int>();
            foreach (int a in KeyBindings.RebindableActions) if (g.ActionOn(a)) acts.Add(a);
            foreach (int a in acts)
            {
                int vk = g.Keys.Get(a), m = g.Keys.GetMods(a);
                if (m == 0 && (vk == 0x57 || vk == 0x41 || vk == 0x53 || vk == 0x44)) return KeyBindings.ActionNames[a] + " on W/A/S/D";
                foreach (int b in acts) if (b != a && g.Keys.Get(b) == vk && g.Keys.GetMods(b) == m) return KeyBindings.ActionNames[a] + " = " + KeyBindings.ActionNames[b];
            }
            return null;
        }

        static bool StStable(GlobalSettings g)
        {
            string s1 = SettingsIO.Serialize(g);
            GlobalSettings b; string e;
            return SettingsIO.TryParse(s1, out b, out e) && b.KeyNotes.Length == 0 && SettingsIO.Serialize(b) == s1;
        }

        static string StNames(GlobalSettings g) { return string.Join(",", g.Profiles.ConvertAll(delegate(Profile p) { return p.Name; })); }

        // ------------------------------------------------------------------ P1-2 / P2-8 导入旧版设置 only takes our settings.json

        static void StImportCheck()
        {
            bool fx = true; string bad = "";
            foreach (string f in new string[] { "leo-v30-1027-settings.json", "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json", "settings-v2.0.json",
                "settings-v2.1.json", "settings-v2.2.json", "settings-v2.3.json", "settings-v2.3-leo1011.json", "settings-v2.4.5.json" })
                if (!SettingsIO.LooksLikeCrosshairSettings(File.ReadAllText(Fixture(f)))) { fx = false; bad += " " + f; }
            string[] foreign = { "{}", "{\"Profiles\":[]}", "{\"Profiles\":[{\"Name\":\"x\"}]}", "{\"editor.fontSize\":14,\"Profiles\":[{\"name\":\"x\",\"diameter\":3}]}",
                "{\"Profiles\":\"Dirs\",\"Dirs\":[1]}", "[{\"Profiles\":[{\"Dirs\":[]}]}]", "not json", null, "" };
            bool rej = true;
            foreach (string s in foreign) if (SettingsIO.LooksLikeCrosshairSettings(s)) { rej = false; bad += " accepted:" + s; }
            bool lenient = SettingsIO.LooksLikeCrosshairSettings("// old file\n{\"Profiles\":[{\"Name\":\"a\",\"Diameter\":20,},],}");

            string d = StDir("imp");
            try
            {
                string target = Path.Combine(d, "cur", "settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate(); SettingsIO.Save(cur, target);
                string before = File.ReadAllText(target);
                // a folder with only another program's settings.json: nothing picked, clear message
                string other = Path.Combine(d, "other"); Directory.CreateDirectory(Path.Combine(other, "sub"));
                File.WriteAllText(Path.Combine(other, "settings.json"), "{\"editor.fontSize\":14}");
                int n; string e1, e2, e3, e4;
                bool r1 = SettingsIO.FindLegacyFile(other) == null && SettingsIO.ImportLegacy(cur, other, target, out n, out e1) == null && e1.Contains("不是准星");
                // the same folder with ours one level down: ours is picked
                File.Copy(Fixture("settings-v2.4.5.json"), Path.Combine(other, "sub", "settings.json"));
                bool r2 = SettingsIO.FindLegacyFile(other) == Path.Combine(other, "sub", "settings.json");
                // the settings.json in use (picked as the file, or as its folder): refused
                bool r3 = SettingsIO.ImportLegacy(cur, target, target, out n, out e2) == null && e2.Contains("正在用")
                    && SettingsIO.ImportLegacy(cur, Path.GetDirectoryName(target), target, out n, out e3) == null && e3.Contains("正在用");
                // {} picked directly: refused
                string empty = Path.Combine(d, "empty.json"); File.WriteAllText(empty, "{}");
                bool r4 = SettingsIO.ImportLegacy(cur, empty, target, out n, out e4) == null && e4 != null;
                bool untouched = File.ReadAllText(target) == before && Directory.GetFiles(Path.GetDirectoryName(target), "settings.before-import-*").Length == 0;
                Check("V41-ST1 导入旧版设置 only takes a crosshair settings.json (BRUTE40 P1-2 / P2-8): all 10 fixtures recognised (comments / trailing commas ok); {} / other programs / name-only lists rejected; a folder never picks another program's file; the file in use is refused; nothing written",
                    fx && rej && lenient && r1 && r2 && r3 && r4 && untouched, "fixtures=" + fx + " rejected=" + rej + " lenient=" + lenient + " other=" + r1 + " sub=" + r2 + " inUse=" + r3 + " {}=" + r4 + " untouched=" + untouched + bad);
            }
            finally { StRm(d); }
        }

        // ------------------------------------------------------------------ P1-3 no two actions on one key after a load

        static void StKeyClashes()
        {
            List<string> why = new List<string>();
            Func<string, Func<GlobalSettings, bool>, string, bool> one = delegate(string json, Func<GlobalSettings, bool> want, string name)
            {
                GlobalSettings g; string e;
                bool ok = SettingsIO.TryParse(json, out g, out e) && StClash(g) == null && want(g) && g.KeyNotes.Length > 0 && StStable(g);
                if (!ok) why.Add(name + (g == null ? " " + e : " clash=" + StClash(g) + " notes=" + string.Join("|", g.KeyNotes)));
                return ok;
            };
            bool a = one(StKeysJson(delegate(JsonObject k) { k["Forward"] = 38; k["LeanLeft"] = 0x57; k["Jump"] = 0x51; }),
                delegate(GlobalSettings g) { return g.Keys.Jump == 0x51 && g.Keys.LeanLeft == GlobalSettings.SpareKeys[0] && string.Join("", g.KeyNotes).Contains("左侧身"); }, "a");
            bool b = one(StKeysJson(delegate(JsonObject k) { k["Right"] = 39; k["LeanRight"] = 0x44; k["FreeLook"] = 0x45; }),
                delegate(GlobalSettings g) { return g.Keys.FreeLook == 0x45 && g.Keys.LeanRight == GlobalSettings.SpareKeys[0]; }, "b");
            bool c = one(StKeysJson(delegate(JsonObject k) { k["Toggle"] = 0x57; k["Fire"] = 0x7B; }),
                delegate(GlobalSettings g) { return g.Keys.Fire == 0x7B && g.Keys.Toggle == GlobalSettings.SpareKeys[0]; }, "c");
            bool dd = one(StKeysJson(delegate(JsonObject k) { k["Fire"] = 1; k["Ads"] = 1; }),
                delegate(GlobalSettings g) { return g.Keys.Fire == 1 && g.Keys.Ads == 2; }, "d");
            bool e5 = one(StKeysJson(delegate(JsonObject k) { k["LeanLeft"] = 0x44; k["LeanLeftMods"] = 1; k["LeanRight"] = 0x44; k["LeanRightMods"] = 1; }),
                delegate(GlobalSettings g) { return g.Keys.ComboOf(KeyBindings.ActionLeanLeft) == "Ctrl+D" && g.Keys.LeanRight == 0x45 && g.Keys.LeanRightMods == 0; }, "e");
            // a switched-off 换手 may keep a shared key (v2.1: 自由视角 = 侧键1); nothing is moved, no note
            GlobalSettings v21; string err;
            bool off = SettingsIO.TryParse(File.ReadAllText(Fixture("settings-v2.1.json")), out v21, out err) && !v21.HandEnabled && v21.Keys.HandSwitch == 5
                && v21.Keys.FreeLook == 5 && v21.KeyNotes.Length == 0;
            // random key sets (incl. 组合键, 换手 / 强化 on or off, an old file's removed "Calibrate360" key): never a clash, stable
            Random r = new Random(41);
            int[] pool = { 0x57, 0x41, 0x53, 0x44, 0x20, 0x51, 0x45, 0x01, 0x02, 0x04, 0x05, 0x06, 0x7A, 0x7B, 0x24, 0x23, 0x43, 0x46 };
            string[] names = { "Toggle", "Fire", "Ads", "FreeLook", "HandSwitch", "LeanLeft", "LeanRight", "Jump", "Boost", "Calibrate360" };
            string baseText = StDefaultJson().ToJsonString();
            int clashes = 0, unstable = 0; string first = null;
            for (int i = 0; i < 3000; i++)
            {
                JsonObject o = (JsonObject)JsonNode.Parse(baseText);
                JsonObject k = (JsonObject)o["Keys"];
                foreach (string nm in names) { k[nm] = pool[r.Next(pool.Length)]; k[nm + "Mods"] = r.Next(6) == 0 ? r.Next(8) : 0; }
                o["HandEnabled"] = r.Next(2) == 0; o["BoostEnabled"] = r.Next(2) == 0;
                GlobalSettings g;
                if (!SettingsIO.TryParse(o.ToJsonString(), out g, out err)) { clashes++; continue; }
                string cl = StClash(g);
                if (cl != null) { clashes++; if (first == null) first = cl; }
                if (!StStable(g)) unstable++;
            }
            // Load: the fixes are listed in the message and saved (the next start is silent); Leo's file and every fixture load silently, untouched
            string d = StDir("keys");
            bool msgOk, quiet = true;
            try
            {
                string p = Path.Combine(d, "settings.json");
                File.WriteAllText(p, StKeysJson(delegate(JsonObject k) { k["LeanLeft"] = 0x57; k["Jump"] = 0x51; }));
                string m1, m2;
                SettingsIO.Load(p, out m1);
                SettingsIO.Load(p, out m2);
                msgOk = m1 != null && m1.Contains("已自动改好") && m1.Contains("左侧身") && m2 == null && Directory.GetFiles(d, "settings.bad-*.json").Length == 1;
                foreach (string f in new string[] { "leo-v30-1027-settings.json", "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json", "settings-v2.0.json",
                    "settings-v2.1.json", "settings-v2.2.json", "settings-v2.3.json", "settings-v2.3-leo1011.json", "settings-v2.4.5.json" })
                {
                    StClear(d);
                    byte[] raw = File.ReadAllBytes(Fixture(f));
                    File.WriteAllBytes(p, raw);
                    string m;
                    SettingsIO.Load(p, out m);
                    byte[] after = File.ReadAllBytes(p);
                    bool same = after.Length == raw.Length;
                    for (int i = 0; same && i < raw.Length; i++) same = raw[i] == after[i];
                    if (m != null || !same || Directory.GetFiles(d).Length != 1) { quiet = false; why.Add(f + " msg=" + m + " same=" + same); }
                }
            }
            finally { StRm(d); }
            Check("V41-ST2 load resolves key clashes (BRUTE40 P1-3): ↑ + 左侧身 W + 跳跃 Q / → + 右侧身 D + 自由视角 E / 开关 W + 开火 F12 / 开火 = 开镜 / two Ctrl+D -> the key the file set wins, the other gets its default or a spare key, never W/A/S/D; 3000 random sets no clash + stable; switched-off 换手 untouched; Load names the fixes once; Leo's file and every fixture load silently and untouched",
                a && b && c && dd && e5 && off && clashes == 0 && unstable == 0 && msgOk && quiet,
                a + " " + b + " " + c + " " + dd + " " + e5 + " off=" + off + " random clash=" + clashes + " unstable=" + unstable + (first ?? "") + " msg=" + msgOk + " quiet=" + quiet + " | " + string.Join("; ", why));
        }

        // ------------------------------------------------------------------ P2-1 / P2-2 / P2-12 / P2-4

        static void StPresence()
        {
            string err; List<string> dropped;
            Func<Action<JsonObject>, GlobalSettings> parse = delegate(Action<JsonObject> edit)
            {
                JsonObject o = StDefaultJson();
                o["HandMode"] = "hold"; o["HandEnabled"] = false; o["BoostEnabled"] = false;
                edit(o);
                GlobalSettings g; string e; List<string> d;
                return SettingsIO.TryParse(o.ToJsonString(), out g, out e, out d) ? g : null;
            };
            // v5.1 (W23): an unreadable 强化 key gets the v5.1 default Ctrl+T (decision 3 / 17); a Keys object without Boost still
            // gets the old migration key 侧键2 (6). Either way 换手方式 / ☑ 换手检测 / ☑ 强化准星 are kept.
            Func<GlobalSettings, bool> kept = delegate(GlobalSettings g) { return g != null && g.HandMode == "hold" && !g.HandEnabled && !g.BoostEnabled && g.Keys.HandSwitch == 5; };
            // v5.1.1 (X5, Leo 21 Nov 2026): the default 强化 key is 鼠标侧键2 (6, no modifier)
            Func<GlobalSettings, bool> ctrlT = delegate(GlobalSettings g) { return g != null && g.Keys.Boost == 6 && g.Keys.BoostMods == 0; };
            GlobalSettings q1 = parse(delegate(JsonObject o) { o["Keys"]["HandSwitch"] = "XButton1"; o["Keys"]["Boost"] = "XButton2"; });
            GlobalSettings q2 = parse(delegate(JsonObject o) { o["Keys"] = new JsonArray(); });
            GlobalSettings q3 = parse(delegate(JsonObject o) { o["Keys"] = new JsonObject(); });
            bool p1 = kept(q1) && ctrlT(q1) && kept(q2) && ctrlT(q2) && kept(q3) && q3.Keys.Boost == 6;
            // a real pre-v3.0 file still migrates (v2.3: no BoostEnabled, 侧键2 used by 自由视角 -> 强化 off)
            GlobalSettings v23;
            bool mig = SettingsIO.TryParse(File.ReadAllText(Fixture("settings-v2.3.json")), out v23, out err) && v23.Keys.Boost == 6 && !v23.BoostEnabled && v23.HandMode == "toggle";
            Check("V41-ST3 an unreadable 换手 / 强化 key (or \"Keys\": [] / {}) no longer resets 换手方式 / ☑ 换手检测 / ☑ 强化准星 (BRUTE40 P2-1); real old files still migrate",
                p1 && mig, "kept=" + p1 + " oldFile=" + mig);

            double[] rec = Profile.RecommendedSpeeds; // v5.0.2 (W18): 推荐 = Leo's curve
            JsonObject c = StDefaultJson();
            c["Profiles"][0]["CurveAD"] = JsonNode.Parse("{\"DurationMs\":250,\"Speeds\":[5,\"fast\",60,80,95]}");
            c["Profiles"][0]["CurveWS"] = JsonNode.Parse("{\"DurationMs\":200}");
            GlobalSettings gc;
            bool p2 = SettingsIO.TryParse(c.ToJsonString(), out gc, out err, out dropped);
            if (p2)
            {
                MoveCurve ad = gc.Profiles[0].CurveAD, ws = gc.Profiles[0].CurveWS;
                p2 = ad.DurationMs == 250 && ws.DurationMs == 200;
                for (int i = 0; i < MotionCurve.Points; i++) p2 &= ad.Speeds[i] == Math.Round(rec[i]) && ws.Speeds[i] == Math.Round(rec[i]);
            }
            Check("V41-ST4 one bad Speeds value or a missing Speeds list gives the 推荐 speeds (not the deleted v2.3 feel); 总时长 kept (BRUTE40 P2-2)",
                p2, gc == null ? err : string.Join(",", gc.Profiles[0].CurveAD.Speeds) + " / " + string.Join(",", gc.Profiles[0].CurveWS.Speeds));

            string d = StDir("empty");
            try
            {
                string p = Path.Combine(d, "settings.json");
                SettingsIO.ScreenProbe = delegate { return new int[] { 1920, 1080 }; };
                bool all = true; string det = "";
                foreach (string t in new string[] { "{}", "{\"Version\":1}", " \uFEFF{ }" })
                {
                    StClear(d); File.WriteAllText(p, t);
                    string m; GlobalSettings g = SettingsIO.Load(p, out m);
                    bool ok = m == null && g.Fps == 360 && g.ShowFadeMs == 50 && g.ResGameW == 1920 && g.ResGameH == 1080 && g.Active.Name == GlobalSettings.DefaultProfileName;
                    all &= ok; if (!ok) det += t + ": msg=" + m + " fps=" + g.Fps + " ";
                }
                StClear(d); File.WriteAllText(p, "{\"editor.fontSize\":14}");
                string mf; GlobalSettings gf = SettingsIO.Load(p, out mf);
                bool foreign = mf != null && gf.Fps == 360 && Directory.GetFiles(d, "settings.bad-*.json").Length == 1;
                Check("V41-ST5 settings.json with no crosshair settings ({} / only Version) = first run (Fps 360, fade 50, detected 1920x1080), silent; another program's JSON: same defaults + message + backup (BRUTE40 P2-12)",
                    all && foreign, det + " foreign=" + foreign);
            }
            finally { SettingsIO.ScreenProbe = null; StRm(d); }

            GlobalSettings rn = GlobalSettings.CreateDefault();
            rn.Profiles[0].Name = GlobalSettings.LegacyDefaultProfileName; rn.ActiveProfile = rn.Profiles[0].Name; rn.Validate();
            GlobalSettings back;
            bool keep = SettingsIO.TryParse(SettingsIO.Serialize(rn), out back, out err) && back.Profiles[0].Name == GlobalSettings.LegacyDefaultProfileName
                && back.ActiveProfile == GlobalSettings.LegacyDefaultProfileName;
            JsonObject old = (JsonObject)JsonNode.Parse(SettingsIO.Serialize(rn)); old.Remove("BoostEnabled");
            GlobalSettings oldG;
            bool oldRen = SettingsIO.TryParse(old.ToJsonString(), out oldG, out err) && oldG.Profiles[0].Name == GlobalSettings.DefaultProfileName && oldG.ActiveProfile == GlobalSettings.DefaultProfileName;
            Check("V41-ST6 a profile the user renamed to 「M4（Leo 改装）」 keeps its name on every start; a pre-v3.0 file is still renamed to 「Leo默认」 (BRUTE40 P2-4)",
                keep && oldRen, "v4 file=" + keep + " pre-v3.0=" + oldRen);
        }

        // ------------------------------------------------------------------ P2-3 / P2-13

        static void StActiveAndNames()
        {
            GlobalSettings a, b; string err;
            bool pa = SettingsIO.TryParse("{\"ActiveProfile\":\" a \",\"Profiles\":[{\"Name\":\"x\",\"Diameter\":5},{\"Name\":\" a \",\"Diameter\":77}]}", out a, out err)
                && a.Active.Diameter == 77 && a.ActiveProfile == "a";
            bool pb = SettingsIO.TryParse("{\"ActiveProfile\":\"a (2)\",\"Profiles\":[{\"Name\":\"a\",\"Diameter\":5},{\"Name\":\"a\",\"Diameter\":6},{\"Name\":\"a (2)\",\"Diameter\":77}]}", out b, out err)
                && b.Active.Diameter == 77 && StNames(b) == "a,a (3),a (2)";
            GlobalSettings c;
            bool pc = SettingsIO.TryParse("{\"ActiveProfile\":\"a\",\"Profiles\":[{\"Name\":\"x\",\"Diameter\":5},{\"Name\":\" a\",\"Diameter\":66}]}", out c, out err) && c.Active.Diameter == 66;
            Check("V41-ST7 the active profile stays the same profile through name trimming and de-duplication (BRUTE40 P2-3)", pa && pb && pc,
                (a == null ? "-" : a.ActiveProfile + "/" + a.Active.Diameter) + " " + (b == null ? "-" : StNames(b) + " active " + b.ActiveProfile) + " " + pc);

            StringBuilder sb = new StringBuilder("{\"Profiles\":[");
            for (int i = 0; i < 2000; i++) sb.Append(i == 0 ? "" : ",").Append("{\"Name\":\"a\"}");
            sb.Append("]}");
            Stopwatch w = Stopwatch.StartNew();
            GlobalSettings many;
            bool ok = SettingsIO.TryParse(sb.ToString(), out many, out err);
            w.Stop();
            HashSet<string> uniq = new HashSet<string>(many == null ? new List<string>() : many.Profiles.ConvertAll(delegate(Profile p) { return p.Name; }));
            Check("V41-ST8 2000 profiles with one name: unique names in one pass (BRUTE40 P2-13)", ok && uniq.Count == 2000 && many.Profiles[0].Name == "a" && w.ElapsedMilliseconds < 3000,
                w.ElapsedMilliseconds + " ms, " + uniq.Count + " unique");
        }

        // ------------------------------------------------------------------ P2-5 / P2-6 / P2-7

        static void StBackups()
        {
            string d = StDir("bak");
            Action<string, string> keepCopy = SettingsIO.CopyFile;
            try
            {
                string p = Path.Combine(d, "settings.json");
                // P2-5 a file from a newer version: an exact copy first
                JsonObject nv = StDefaultJson(); nv["Version"] = 7; nv["FutureThing"] = 1;
                string newer = nv.ToJsonString();
                File.WriteAllText(p, newer);
                string m; GlobalSettings g = SettingsIO.Load(p, out m);
                string[] nc = Directory.GetFiles(d, "settings.newer-*.json");
                GlobalSettings re; string err;
                bool p5 = nc.Length == 1 && File.ReadAllText(nc[0]) == newer && m != null && m.Contains("更新的版本")
                    && SettingsIO.TryParse(File.ReadAllText(p), out re, out err) && re.FileVersion == GlobalSettings.FormatVersion;
                Check("V41-ST9 a settings.json from a newer version is copied to settings.newer-*.json before it is saved over, with a message; v4.1 writes format 2 (BRUTE40 P2-5)",
                    p5, "copies=" + nc.Length + " msg=" + m);

                // P2-6 the backup cannot be written -> the file is left exactly as it is, and Save refuses until the backup works
                StClear(d);
                JsonObject one = StDefaultJson(); one["Profiles"][0]["Diameter"] = "big";
                string bad = one.ToJsonString();
                File.WriteAllText(p, bad);
                SettingsIO.CopyFile = delegate(string a, string b) { throw new IOException("locked"); };
                string m1; GlobalSettings g1 = SettingsIO.Load(p, out m1);
                bool left = File.ReadAllText(p) == bad && m1 != null && m1.Contains("没能备份") && g1.BackupBeforeSave != null;
                string se = SettingsIO.Save(g1, p);
                bool refused = se != null && File.ReadAllText(p) == bad;
                string p2 = Path.Combine(d, "garbage", "settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(p2));
                File.WriteAllText(p2, "{ not json at all");
                string m2; GlobalSettings g2 = SettingsIO.Load(p2, out m2);
                bool garbage = File.ReadAllText(p2) == "{ not json at all" && m2 != null && g2.Profiles.Count == 1 && Directory.GetFiles(Path.GetDirectoryName(p2)).Length == 1;
                SettingsIO.CopyFile = keepCopy;
                string se2 = SettingsIO.Save(g1, p);
                string[] bk = Directory.GetFiles(d, "settings.bad-*.json");
                List<string> dr; GlobalSettings g3;
                bool after = se2 == null && g1.BackupBeforeSave == null && bk.Length == 1 && File.ReadAllText(bk[0]) == bad
                    && SettingsIO.TryParse(File.ReadAllText(p), out g3, out err, out dr) && dr.Count == 0;
                Check("V41-ST10 backup copy fails: settings.json (one bad member, or not JSON) is left exactly as it is, the message says so, Save refuses; once the copy works Save backs up the original first (BRUTE40 P2-6)",
                    left && refused && garbage && after, "left=" + left + " refused=" + refused + " garbage=" + garbage + " after=" + after + " | " + m1);

                // P2-7 backups in the same second never overwrite each other
                StClear(d);
                string src = Path.Combine(d, "src.json");
                List<string> made = new List<string>(); bool distinct = true;
                for (int i = 0; i < 3; i++)
                {
                    File.WriteAllText(src, "v" + i);
                    string e; string c = SettingsIO.BackupCopy(src, "x", out e);
                    if (c == null || made.Contains(c)) distinct = false; else made.Add(c);
                }
                for (int i = 0; distinct && i < made.Count; i++) distinct = File.ReadAllText(made[i]) == "v" + i;
                // two 导入旧版设置 in a row: two before-import backups, the first holds the original
                StClear(d);
                string target = Path.Combine(d, "settings.json");
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate(); SettingsIO.Save(cur, target);
                string orig = File.ReadAllText(target);
                string oldDir = Path.Combine(d, "old"); Directory.CreateDirectory(oldDir);
                File.Copy(Fixture("settings-v2.4.5.json"), Path.Combine(oldDir, "settings.json"));
                int n; string ie;
                GlobalSettings m1g = SettingsIO.ImportLegacy(cur, oldDir, target, out n, out ie);
                string afterFirst = File.ReadAllText(target);
                GlobalSettings m2g = m1g == null ? null : SettingsIO.ImportLegacy(m1g, oldDir, target, out n, out ie);
                string[] bi = Directory.GetFiles(d, "settings.before-import-*.json");
                Array.Sort(bi, StringComparer.Ordinal);
                bool two = m2g != null && bi.Length == 2 && Array.Exists(bi, delegate(string f) { return File.ReadAllText(f) == orig; })
                    && Array.Exists(bi, delegate(string f) { return File.ReadAllText(f) == afterFirst; });
                Check("V41-ST11 backups made in the same second get their own names and never overwrite each other (BRUTE40 P2-7)", distinct && two,
                    "distinct=" + distinct + " imports=" + two + " (" + bi.Length + ") " + ie);
            }
            finally { SettingsIO.CopyFile = keepCopy; StRm(d); }
        }

        // ------------------------------------------------------------------ P2-9 / P2-10 damaged files

        static void StDamaged()
        {
            string err;
            JsonObject ex = (JsonObject)JsonNode.Parse(SettingsIO.ExportProfile(Profile.CreateLeoDefault("导出")));
            ex["Diameter"] = "22px";
            List<Profile> imp; string ie, ie2; List<Profile> imp2;
            bool p9 = SettingsIO.TryParseImport(ex.ToJsonString(), out imp, out ie) && imp.Count == 1 && imp[0].Diameter == new Profile().Diameter && imp[0].Thickness == 1.2
                && imp[0].Name == "导出";
            bool cut9 = !SettingsIO.TryParseImport("{\"Name\":\"a\"", out imp2, out ie2) && ie2 == SettingsIO.ImportBadError;
            Check("V41-ST12 「导入…」 is as tolerant as settings.json: a wrong-typed member gets its default, the rest is kept; a cut-off file is refused (BRUTE40 P2-9)",
                p9 && cut9, "tolerant=" + p9 + " (" + ie + ") cut=" + cut9 + " " + ie2);

            // a rich file, cut at many points
            GlobalSettings rich = GlobalSettings.CreateDefault();
            Profile pb = Profile.CreateBlank("B"); pb.Diameter = 33; pb.Dirs[3] = new DirSetting(-12, 4); Profile.RecomputeDiagonals(pb); rich.Profiles.Add(pb);
            Profile pc = Profile.CreateBlank("C"); pc.LeanQX = -9; rich.Profiles.Add(pc);
            rich.Fps = 240; rich.Validate();
            string text = SettingsIO.Serialize(rich);
            string p0Export = SettingsIO.ExportProfile(rich.Profiles[0]);
            int iB = text.IndexOf("\"B\"", StringComparison.Ordinal), depth = 0, startB = -1;
            for (int i = iB; i >= 0 && startB < 0; i--)
            {
                if (text[i] == '}') depth++;
                else if (text[i] == '{') { if (depth == 0) startB = i; else depth--; }
            }
            int lastClose = text.LastIndexOf('}'), fpsAfter = text.IndexOf("\"FrameMode\"", StringComparison.Ordinal);
            List<int> cuts = new List<int>();
            for (int i = 1; i < lastClose; i += 41) cuts.Add(i);
            for (int i = 0, k = 0; i < lastClose && k < 40; i++) if (text[i] == ',') { cuts.Add(i + 1); k++; } // right after a comma (the serializer took some of these)
            cuts.Add(startB); cuts.Add(lastClose);
            string d = StDir("cut");
            int accepted = 0, threw = 0, silent = 0, range = 0, noCopy = 0, lostP0 = 0, lostFps = 0; string first = null;
            try
            {
                string p = Path.Combine(d, "settings.json");
                foreach (int cut in cuts)
                {
                    string t = text.Substring(0, cut);
                    GlobalSettings gt;
                    if (SettingsIO.TryParse(t, out gt, out err)) { accepted++; if (first == null) first = "accepted at " + cut; }
                    StClear(d); File.WriteAllText(p, t);
                    try
                    {
                        string m; GlobalSettings g = SettingsIO.Load(p, out m);
                        if (m == null) silent++;
                        if (BxRangeErrors(g).Count > 0) { range++; if (first == null) first = "range at " + cut + ": " + string.Join(";", BxRangeErrors(g)); }
                        string[] bk = Directory.GetFiles(d, "settings.bad-*.json");
                        if (bk.Length != 1 || File.ReadAllText(bk[0]) != t) noCopy++;
                        if (cut >= startB && SettingsIO.ExportProfile(g.Profiles[0]) != p0Export) { lostP0++; if (first == null) first = "profile 0 lost at " + cut; }
                        if (cut > fpsAfter && g.Fps != 240) lostFps++;
                    }
                    catch (Exception e) { threw++; if (first == null) first = cut + ": " + e.GetType().Name; }
                }
                // tolerated damage: a comment, a missing comma, text after the end -> every profile kept, with a message; NUL padding -> silent
                string noComma = text.Remove(text.IndexOf(',', text.IndexOf("\"ActiveProfile\"", StringComparison.Ordinal)), 1);
                bool keepAll = true; string kd = "";
                foreach (string t in new string[] { "// my settings\n" + text, noComma, text + "\nxyz", text + "\0\0\0\0" })
                {
                    StClear(d); File.WriteAllText(p, t);
                    string m; GlobalSettings g = SettingsIO.Load(p, out m);
                    bool ok = g.Profiles.Count == 3 && g.Fps == 240 && (t.EndsWith("\0", StringComparison.Ordinal) ? m == null : m != null);
                    string diff = "";
                    for (int i = 0; ok && i < 3; i++)
                    {
                        string ga = SettingsIO.ExportProfile(g.Profiles[i]), wa = SettingsIO.ExportProfile(rich.Profiles[i]);
                        ok = ga == wa;
                        if (!ok) { int q = 0; while (q < ga.Length && q < wa.Length && ga[q] == wa[q]) q++; diff = " profile " + i + " at " + q + ": [" + ga.Substring(Math.Max(0, q - 40), Math.Min(80, ga.Length - Math.Max(0, q - 40))).Replace("\n", " ") + "]"; }
                    }
                    keepAll &= ok; if (!ok) kd += " [" + t.Substring(0, 12).Replace("\n", " ") + "] n=" + g.Profiles.Count + " fps=" + g.Fps + " msg=" + (m != null) + diff;
                }
                Check("V41-ST13 a cut-off settings.json is never accepted silently: " + cuts.Count + " cut points -> never parsed as complete, Load never throws, in range, always a message + an exact backup, complete profiles and settings before the cut kept; comments / a missing comma / text after the end keep everything (BRUTE40 P2-10)",
                    accepted == 0 && threw == 0 && silent == 0 && range == 0 && noCopy == 0 && lostP0 == 0 && lostFps == 0 && keepAll,
                    "accepted=" + accepted + " threw=" + threw + " silent=" + silent + " range=" + range + " noCopy=" + noCopy + " lostP0=" + lostP0 + " lostFps=" + lostFps + " tolerated=" + keepAll + kd + " " + (first ?? ""));
            }
            finally { StRm(d); }
        }

        // ------------------------------------------------------------------ P2-11 encodings

        static void StEncodings()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string text = File.ReadAllText(Fixture("leo-v30-1027-settings.json"), Encoding.UTF8);
            GlobalSettings want; string err;
            SettingsIO.TryParse(text, out want, out err);
            string names = StNames(want);
            Func<Encoding, bool, byte[]> enc = delegate(Encoding e, bool bom)
            {
                byte[] body = e.GetBytes(text), pre = bom ? e.GetPreamble() : new byte[0];
                byte[] all = new byte[pre.Length + body.Length];
                Array.Copy(pre, all, pre.Length); Array.Copy(body, 0, all, pre.Length, body.Length);
                return all;
            };
            object[][] cases = {
                new object[] { "GB18030", enc(Encoding.GetEncoding(54936), false), true },
                new object[] { "UTF-16LE no BOM", enc(new UnicodeEncoding(false, false), false), true },
                new object[] { "UTF-16BE no BOM", enc(new UnicodeEncoding(true, false), false), true },
                new object[] { "UTF-8", enc(new UTF8Encoding(false), false), false },
                new object[] { "UTF-8 BOM", enc(new UTF8Encoding(true), true), false },
                new object[] { "UTF-16LE BOM", enc(new UnicodeEncoding(false, true), true), false },
                new object[] { "UTF-32LE BOM", enc(new UTF32Encoding(false, true), true), false },
            };
            string d = StDir("enc");
            bool all = true; string det = "";
            try
            {
                string p = Path.Combine(d, "settings.json");
                foreach (object[] c in cases)
                {
                    StClear(d); File.WriteAllBytes(p, (byte[])c[1]);
                    string m, m2; GlobalSettings g = SettingsIO.Load(p, out m);
                    SettingsIO.Load(p, out m2);
                    bool expectMsg = (bool)c[2];
                    bool ok = StNames(g) == names && (expectMsg ? m != null && m.Contains("编码") : m == null) && m2 == null;
                    all &= ok; if (!ok) det += c[0] + ": names=" + StNames(g) + " msg=" + m + " second=" + m2 + "; ";
                }
                bool noThrow = true;
                Random r = new Random(11);
                for (int i = 0; i < 200; i++)
                {
                    byte[] b = new byte[r.Next(0, 64)]; r.NextBytes(b);
                    try { bool old; SettingsIO.DecodeSettingsBytes(b, out old); } catch (Exception) { noThrow = false; }
                }
                Check("V41-ST14 Leo's file saved as GB18030 / UTF-16 without BOM keeps 「Leo默认」 (decoded, message, re-saved as UTF-8); UTF-8 with or without BOM and UTF-16 / UTF-32 with BOM load silently; random bytes never throw (BRUTE40 P2-11)",
                    all && noThrow, det + " noThrow=" + noThrow);
            }
            finally { StRm(d); }
        }

        // ------------------------------------------------------------------ v4.3 (W10): F11 is free (the old turn key is gone)

        static void StF11Free()
        {
            KeyBindings k = new KeyBindings();
            bool table = KeyBindings.ActionCount == 13 && KeyBindings.ActionNames.Length == 13
                && Array.IndexOf(KeyBindings.ActionNames, "转一圈校准") < 0 && KeyBindings.ActionNames[12] == "强化准星";
            bool noDefault = true;
            for (int a = 0; a < KeyBindings.ActionCount; a++) if (k.Get(a) == 0x7A) noDefault = false;
            // an old file whose turn key is F11 and whose 跳跃 is F11 too: 跳跃 keeps F11, nothing is moved, no note
            JsonObject jo = StDefaultJson(); jo["Keys"]["Jump"] = 0x7A; jo["Keys"]["Calibrate360"] = 0x7A; jo["Keys"]["Calibrate360Mods"] = 0;
            GlobalSettings j; string err;
            bool keep = SettingsIO.TryParse(jo.ToJsonString(), out j, out err) && j.Keys.Jump == 0x7A && j.KeyNotes.Length == 0 && StClash(j) == null
                && !SettingsIO.Serialize(j).Contains("Calibrate360");
            // any action can take F11 / Ctrl+F11 without a clash; the 滤镜 keys see no crosshair key on it
            KeyBindings kb = new KeyBindings();
            kb.SetCombo(KeyBindings.ActionToggle, 0x7A, KeyBindings.ModCtrl);
            bool bind = kb.ComboOf(KeyBindings.ActionToggle) == "Ctrl+F11" && kb.FindOtherCombo(0x7A, KeyBindings.ModCtrl, KeyBindings.ActionJump) == KeyBindings.ActionToggle
                && new KeyBindings().FindOtherCombo(0x7A, 0, KeyBindings.ActionJump) < 0
                && FilterKeys.CrosshairClash(new KeyBindings(), 0x7A, 0) == -1 && FilterKeys.CrosshairClash(new KeyBindings(), 0x7A, KeyBindings.ModAlt) == -1;
            // F11 held is no input at all with the default keys
            InputState none = MotionEngine.ReadInput(new KeyBindings(), delegate(int vk) { return vk == 0x7A; });
            bool noInput = !none.Fwd && !none.Back && !none.Left && !none.Right && !none.LeanL && !none.LeanR && !none.Fire && !none.Ads && !none.FreeLook
                && !none.HandSwitch && !none.Jump && !none.Boost;
            // the v1.2 fixture (its own old "Calibrate": F8) still loads
            GlobalSettings v12;
            bool v12ok = SettingsIO.TryParse(File.ReadAllText(Fixture("settings-v1.2.json")), out v12, out err);
            Check("V43-W10-1 F11 is free: 13 actions (no 转一圈校准), no default on F11, an old file's Calibrate360 is ignored and never written, any action and the 滤镜 keys may use F11, F11 alone is no input, v1.2 still loads",
                table && noDefault && keep && bind && noInput && v12ok,
                "table=" + table + " noDefault=" + noDefault + " keep=" + keep + " bind=" + bind + " noInput=" + noInput + " v1.2=" + v12ok);
        }

        // ------------------------------------------------------------------ 站着偏移 always 0 (Leo 17 Nov 2026)

        static bool StStand(Profile p)
        {
            HandView v = new HandView();
            p.FillView(1, v);
            HandView v0 = new HandView();
            p.FillView(0, v0);
            return p.StandOffsetX == 0 && p.StandOffsetY == 0 && !p.IsHandCustom(Profile.HandStand) && v.SX == 0 && v.SY == 0 && v0.SX == 0 && v0.SY == 0;
        }

        static void StStandZero()
        {
            string err; List<string> bad = new List<string>();
            JsonObject o = StDefaultJson();
            o["Profiles"][0]["StandAsZero"] = false; /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in has a 站立 point now; this case measures around the centre */
            o["Profiles"][0]["StandOffsetX"] = 7; o["Profiles"][0]["StandOffsetY"] = -27;
            o["Profiles"][0]["HandCustom"][Profile.HandStand] = JsonNode.Parse("{\"Custom\":true,\"X\":5,\"Y\":5}");
            string d = StDir("stand");
            try
            {
                string p = Path.Combine(d, "settings.json");
                File.WriteAllText(p, o.ToJsonString());
                string m; GlobalSettings g = SettingsIO.Load(p, out m);
                if (!StStand(g.Profiles[0])) bad.Add("load");
                // 导入… a single exported profile carrying 站着偏移
                JsonObject ex = (JsonObject)JsonNode.Parse(SettingsIO.ExportProfile(Profile.CreateLeoDefault("x")));
                ex.Remove("StandAsZero"); ex["StandOffsetX"] = 7; ex["StandOffsetY"] = -27; ex["HandCustom"][Profile.HandStand] = JsonNode.Parse("{\"Custom\":true,\"X\":5,\"Y\":5}");
                List<Profile> imp;
                if (!SettingsIO.TryParseImport(ex.ToJsonString(), out imp, out err) || !StStand(imp[0])) bad.Add("导入");
                Profile mem = Profile.CreateLeoDefault("mem"); mem.SetStandPoint(0, 0); mem.StandOffsetX = 9; mem.StandOffsetY = 3; mem.SetHandCustom(Profile.HandStand, 4, 4);
                g.AddImported(new List<Profile> { mem });
                if (!StStand(g.Active)) bad.Add("AddImported");
                // 导入旧版设置 of old files with 站着偏移 (v2.3: per profile; v1.x: global 中心校正)
                foreach (string f in new string[] { "settings-v2.3.json", "settings-v1.2.json", "settings-v2.4.5.json" })
                {
                    string od = Path.Combine(d, "old-" + f); Directory.CreateDirectory(od);
                    File.Copy(Fixture(f), Path.Combine(od, "settings.json"));
                    int n; string ie;
                    GlobalSettings mg = SettingsIO.ImportLegacy(g, od, p, out n, out ie);
                    if (mg == null) { bad.Add(f + " " + ie); continue; }
                    foreach (Profile q in mg.Profiles) if (!StStand(q)) bad.Add(f + ":" + q.Name);
                }
                bool hasStand = false; // the test is only meaningful while the old fixture really carries a 站着偏移
                foreach (JsonNode pr in JsonNode.Parse(File.ReadAllText(Fixture("settings-v2.3.json")))["Profiles"].AsArray())
                    if (pr["StandOffsetX"] != null && pr["StandOffsetX"].GetValue<double>() != 0) hasStand = true;
                if (!hasStand) bad.Add("fixture v2.3 has no non-zero StandOffsetX");
            }
            finally { StRm(d); }
            Profile blank = Profile.CreateBlank("新");
            if (!StStand(blank)) bad.Add("新建");
            Profile s = Profile.CreateLeoDefault("s"); s.SetStandPoint(0, 0); s.SaveAsDefault("x"); s.SavedDefault.StandOffsetX = 6; s.SavedDefault.SetHandCustom(Profile.HandStand, 2, 2);
            GlobalSettings h = GlobalSettings.CreateDefault(); h.Profiles.Add(s);
            Profile restored = h.RestoreSaved(s);
            if (!StStand(restored)) bad.Add("恢复");
            // engine: standing still = screen centre, on both hands
            GlobalSettings eg = GlobalSettings.CreateDefault(); eg.ShowFadeMs = 0; eg.HideFadeMs = 0; eg.OnlyWhenGameFocused = false; eg.Validate();
            Profile ep = eg.Active; ep.SetStandPoint(0, 0); ep.StandOffsetX = 8; ep.StandOffsetY = -6; ep.Validate();
            Sim sim = new Sim(ep, eg, 144);
            sim.Run(1.0, In(""));
            bool c0 = Math.Abs(sim.E.OffsetX) < 1e-9 && Math.Abs(sim.E.OffsetY) < 1e-9;
            sim.Step(In("K")); sim.Run(1.5, In(""));
            bool c1 = Math.Abs(sim.E.OffsetX) < 1e-9 && Math.Abs(sim.E.OffsetY) < 1e-9;
            if (!c0 || !c1) bad.Add("engine " + sim.E.OffsetX + "," + sim.E.OffsetY);
            Check("V41-ST16 站着偏移 is 0 on every path (Leo): load, 导入 (exported profile with 7 / -27 + a custom 换手 item), AddImported, 导入旧版设置 of v1.2 / v2.3 / v2.4.5 files, 新建, 恢复到我存的默认; 换手 自动 = 0; standing still = screen centre on both hands",
                bad.Count == 0, bad.Count == 0 ? "" : string.Join("; ", bad));
        }

        // ------------------------------------------------------------------ v4.3 (W10): 鼠标滞后 removed, old files still load

        /// <summary>A v4.1 / v4.2 settings.json with 鼠标滞后 ON in every profile (and its 保存为默认 snapshot), the global mouse
        /// fields (DPI X / Y, the SIM41 single DPI, 灵敏度, a 3-turn calibration) and the turn key on F11.</summary>
        static string StOldMouseLagJson(GlobalSettings from)
        {
            JsonObject o = (JsonObject)JsonNode.Parse(SettingsIO.Serialize(from));
            o["MouseDpiX"] = 1600; o["MouseDpiY"] = 800; o["MouseDpi"] = 1600; o["MouseSens"] = 0.3; o["MouseCountsPer360"] = 9000;
            o["MouseCalibTurns"] = new JsonArray(9000, 9010, 8990);
            o["Keys"]["Calibrate360"] = 0x7A; o["Keys"]["Calibrate360Mods"] = 0;
            foreach (JsonNode pr in o["Profiles"].AsArray())
            {
                List<JsonObject> objs = new List<JsonObject> { pr.AsObject() };
                if (pr["SavedDefault"] is JsonObject sd) objs.Add(sd);
                foreach (JsonObject po in objs)
                {
                    po["MouseLagOn"] = true; po["MouseLagFollow"] = 5; po["MouseLagKick"] = 1.2; po["MouseLagHz"] = 4; po["MouseLagDamping"] = 0.5;
                    po["MouseLagMaxPx"] = 150; po["MouseLagRev"] = 2; po["MouseLagGain"] = 3.3;
                }
            }
            return o.ToJsonString();
        }

        static void StMouseLagGone()
        {
            string[] gone = { "MouseLag", "MouseDpi", "MouseSens", "MouseCountsPer360", "MouseCalibTurns", "Calibrate360" };
            GlobalSettings src = GlobalSettings.CreateDefault();
            src.Active.SavedDefault = src.Active.Clone(); src.Active.SavedDefault.SavedDefault = null; src.Active.SavedDefaultAt = "2026-11-17 09:00";
            src.Validate();
            string plainText = SettingsIO.Serialize(src);
            string old = StOldMouseLagJson(src);
            GlobalSettings g, plain; string err, err2;
            bool parsed = SettingsIO.TryParse(old, out g, out err) && g.KeyNotes.Length == 0;
            bool plainOk = SettingsIO.TryParse(plainText, out plain, out err2);
            // the old values are skipped: the loaded file saves exactly like the same file without them, with none of the old keys
            string written = parsed ? SettingsIO.Serialize(g) : "";
            bool notWritten = parsed;
            foreach (string f in gone) if (written.Contains("\"" + f)) notWritten = false;
            bool same = parsed && plainOk && written == SettingsIO.Serialize(plain);
            // it does nothing: the same keys (and frames) through the engine give bit-identical ring offsets
            double worst = 0;
            if (parsed && plainOk)
                foreach (string c in new string[] { "", "D", "WQ", "K", "L", "SA" })
                {
                    Sim a = new Sim(g.Active, g, 144), b = new Sim(plain.Active, plain, 144);
                    for (int f = 0; f < 200; f++)
                    {
                        a.Step(In(c)); b.Step(In(c));
                        worst = Math.Max(worst, Math.Abs(a.E.OffsetX - b.E.OffsetX) + Math.Abs(a.E.OffsetY - b.E.OffsetY));
                    }
                }
            // through Load (a real file on disk): no message, no damaged-file copy
            string d = StDir("mouselag");
            bool load = false;
            try
            {
                string p = Path.Combine(d, "settings.json");
                File.WriteAllText(p, old);
                string m;
                GlobalSettings l = SettingsIO.Load(p, out m);
                load = l != null && m == null && Directory.GetFiles(d, "settings.bad-*.json").Length == 0 && l.Profiles.Count == src.Profiles.Count;
            }
            finally { StRm(d); }
            // 导入旧版设置 of that file: every old profile comes in, nothing of the old feature is kept
            int n = 0;
            GlobalSettings merged = parsed ? SettingsIO.MergeLegacy(GlobalSettings.CreateDefault(), g, out n) : null;
            string mt = merged == null ? "" : SettingsIO.Serialize(merged);
            bool import = merged != null && n == g.Profiles.Count && merged.Profiles.Count == 2 * src.Profiles.Count;
            foreach (string f in gone) if (mt.Contains("\"" + f)) import = false;
            Check("V43-W10-2 a v4.1 / v4.2 file with 鼠标滞后 ON (+ DPI, 灵敏度, 转一圈校准, F11 key) loads without a message, does nothing (ring bit-identical over 6 key sets), saves without the old keys, imports",
                parsed && notWritten && same && worst == 0 && load && import,
                "parsed=" + parsed + " (" + err + ") notWritten=" + notWritten + " same=" + same + " worst=" + worst + " load=" + load + " import=" + import + " n=" + n);
        }

        // ------------------------------------------------------------------ Autostart via the HKCU Run key

        sealed class StFakeRun : Autostart.IRunStore
        {
            public Dictionary<string, string> V = new Dictionary<string, string>();
            public bool Throw;
            public string Get(string name) { if (Throw) throw new IOException("denied"); string v; return V.TryGetValue(name, out v) ? v : null; }
            public void Set(string name, string value) { if (Throw) throw new IOException("denied"); V[name] = value; }
            public void Delete(string name) { if (Throw) throw new IOException("denied"); V.Remove(name); }
        }

        static void StAutostart()
        {
            // v5.0.2 (W16, Leo decision 2): autostart = a Task Scheduler logon task with highest privileges (the exe is
            // requireAdministrator); the v4.1 - v5.0.1 HKCU Run value is removed and, when it was there, turned into the task.
            Autostart.IRunStore keepStore = Autostart.Store;
            Autostart.SchtasksRunner keepRun = Autostart.Run;
            Func<string, string> keepXml = Autostart.WriteTempXml;
            try
            {
                StFakeRun st = new StFakeRun();
                string taskXml = null; bool createFails = false, deletable = true; List<string> calls = new List<string>();
                Dictionary<string, string> files = new Dictionary<string, string>();
                Autostart.Store = st;
                Autostart.WriteTempXml = delegate(string x) { string f = "tmp" + files.Count + ".xml"; files[f] = x; return f; };
                Autostart.Run = delegate(string a, out string o)
                {
                    calls.Add(a); o = "";
                    if (a.StartsWith("/Query")) { if (taskXml == null) return 1; if (a.Contains("/XML")) o = taskXml; return 0; }
                    if (a.StartsWith("/Delete")) { if (deletable) { taskXml = null; return 0; } return 1; }
                    if (a.StartsWith("/Create"))
                    {
                        if (createFails) return 1;
                        int q = a.IndexOf("/XML \"", StringComparison.Ordinal) + 6;
                        taskXml = files[a.Substring(q, a.IndexOf('"', q) - q)];
                        return 0;
                    }
                    return 1;
                };
                // v5.1.1 (X2): a path of the OS the tests run on (Autostart compares after Path.GetFullPath, which turns /opt/... into
                // D:\opt\... on Windows, so the Linux path never matched there; CI run 44729023467)
                string exe = OperatingSystem.IsWindows() ? "C:\\Tarkov Hip\\app\\TarkovHipAimer.exe" : "/opt/Tarkov Hip/app/TarkovHipAimer.exe", e;
                st.V[Autostart.ValueName] = "\"" + exe + "\" --autostart";
                bool en = Autostart.Enable(exe, out e) && taskXml != null && Autostart.IsOurTask(taskXml) && taskXml.Contains("<RunLevel>HighestAvailable</RunLevel>")
                    && taskXml.Contains("<LogonType>InteractiveToken</LogonType>") && !taskXml.Contains("Password") && Autostart.IsEnabled()
                    && Autostart.RegisteredPath() == exe && !st.V.ContainsKey(Autostart.ValueName) && calls.Exists(x => x.StartsWith("/Create")) && !Autostart.LegacyTaskLeft;
                bool tempGone = files.Count == 1; // written once (the fake does not delete; the real one deletes in finally)
                bool dis = Autostart.Disable(out e) && taskXml == null && !Autostart.IsEnabled() && Autostart.Disable(out e);
                // migrate: an old Run value (v4.1 - v5.0.1 autostart on) becomes the task, the value is removed
                st.V[Autostart.ValueName] = "\"/old/TarkovHipAimer.exe\" --autostart"; calls.Clear();
                bool migrate = Autostart.RefreshIfMoved(exe, out e) && taskXml != null && Autostart.RegisteredPath() == exe && !st.V.ContainsKey(Autostart.ValueName) && Autostart.IsEnabled();
                // moved exe: the task is pointed at this exe
                taskXml = Autostart.BuildTaskXml("/old/place/TarkovHipAimer.exe", "PC\\Leo");
                bool moved = Autostart.RefreshIfMoved(exe, out e) && Autostart.RegisteredPath() == exe;
                // up to date: nothing created
                calls.Clear();
                bool same = Autostart.RefreshIfMoved(exe, out e) && !calls.Exists(x => x.StartsWith("/Create"));
                // off: no task, no Run value = nothing created
                taskXml = null; st.V.Clear(); calls.Clear();
                bool off = Autostart.RefreshIfMoved(exe, out e) && taskXml == null && !calls.Exists(x => x.StartsWith("/Create")) && !Autostart.IsEnabled();
                // failures: Chinese text, no throw
                createFails = true; string e1, e2, e3;
                bool f1 = !Autostart.Enable(exe, out e1) && e1.Contains("开机");
                createFails = false; Autostart.Enable(exe, out e); deletable = false;
                bool f2 = !Autostart.Disable(out e2) && e2.Contains("开机");
                deletable = true; Autostart.Disable(out e);
                st.Throw = true;
                st.V.Clear();
                bool f3 = !Autostart.RefreshIfMoved(exe, out e3) && e3.Contains("开机");
                st.Throw = false;
                bool parse = Autostart.ExeFromRunValue("\"C:\\a b\\x.exe\" --autostart") == "C:\\a b\\x.exe" && Autostart.ExeFromRunValue("C:\\x.exe --autostart") == "C:\\x.exe"
                    && Autostart.ExeFromRunValue("") == null && Autostart.ExeFromRunValue(null) == null
                    && Autostart.RunKeyPath == "Software\\Microsoft\\Windows\\CurrentVersion\\Run" && Autostart.ValueName == "TarkovHipAimer" && Autostart.TaskName == "TarkovHipAimer";
                Check("V502-ST19 开机自动启动 = logon task 「TarkovHipAimer」 (logon trigger, current user, highest privileges, interactive token, no password) made / deleted by the app; the old HKCU Run value is removed and migrated into the task; a moved exe is re-pointed; autostart off = nothing created; errors -> false + Chinese text, never throws",
                    en && tempGone && dis && migrate && moved && same && off && f1 && f2 && f3 && parse,
                    "enable=" + en + " temp=" + tempGone + " disable=" + dis + " migrate=" + migrate + " moved=" + moved + " same=" + same + " off=" + off + " f1=" + f1 + " f2=" + f2 + " f3=" + f3 + " parse=" + parse);
            }
            finally { Autostart.Store = keepStore; Autostart.Run = keepRun; Autostart.WriteTempXml = keepXml; Autostart.LegacyTaskLeft = false; }
        }

        // ------------------------------------------------------------------ REBRUTE41 P2-1: 导入旧版设置 keeps the 调参基准

        static void StImportKeepsBase()
        {
            string err; int n = 0;
            GlobalSettings old;
            bool parsed = SettingsIO.TryParse(File.ReadAllText(Fixture("leo-v30-1027-settings.json")), out old, out err);
            // the finding's repro: 游戏分辨率 2560x1440, 调参 FOV 60, everything else default -> 「Leo默认」 D (X -30 px) = 3.14 % left
            GlobalSettings cur = GlobalSettings.CreateDefault(); cur.ResGameW = 2560; cur.ResGameH = 1440; cur.ResBaseFov = 60; cur.Validate();
            Profile leo = cur.FindProfile(GlobalSettings.DefaultProfileName);
            leo.Dirs[3] = new DirSetting(-30, 3); Profile.RecomputeDiagonals(leo); // v5.0: the old built-in D (-30, 3) as the probe value
            string before = OffsetUnits.Format(leo.Dirs[3].OffX, OffsetAxis.X, cur);
            double kBefore = cur.ResolutionScale;
            GlobalSettings m = parsed ? SettingsIO.MergeLegacy(cur, old, out n) : null;
            Profile mleo = m == null ? null : m.FindProfile(GlobalSettings.DefaultProfileName);
            string after = mleo == null ? "" : OffsetUnits.Format(mleo.Dirs[3].OffX, OffsetAxis.X, m);
            // v5.1.1 (X5, Leo 21 Nov 2026): k has no FOV part (1 here); D (left, other side) x 75/60 = 5.21 %
            bool fov = mleo != null && m.ResBaseFov == 60 && m.ResolutionScale == kBefore && kBefore == 1.0 && mleo.Dirs[3].OffX == -30
                && before == after && after.Contains("5.21") && Math.Abs(OffsetUnits.ToPct(mleo.Dirs[3].OffX, OffsetAxis.X, m) + 5.2083) < 0.001
                && SettingsIO.ExportProfile(mleo) == SettingsIO.ExportProfile(leo);
            // the imported old profiles keep their raw px values
            bool raw = m != null && n == old.Profiles.Count;
            for (int i = 0; raw && i < n; i++)
            {
                Profile got = m.Profiles[m.Profiles.Count - n + i].CloneValues();
                got.Name = old.Profiles[i].Name;
                raw = SettingsIO.ExportProfile(got) == SettingsIO.ExportProfile(old.Profiles[i]);
            }
            // 调参分辨率 / 调参比例 / 调参 FOV / FOV 类型 kept for random screens and bases, merged with every old fixture: the scale never changes
            Random r = new Random(4121);
            string[] fx = { "leo-v30-1027-settings.json", "settings-v1.json", "settings-v1.2.json", "settings-v2.1.json", "settings-v2.3.json", "settings-v2.4.5.json" };
            List<GlobalSettings> olds = new List<GlobalSettings>();
            foreach (string f in fx) { GlobalSettings o; if (SettingsIO.TryParse(File.ReadAllText(Fixture(f)), out o, out err)) olds.Add(o); }
            int merges = 0, moved = 0; string first = null;
            for (int i = 0; i < 300; i++)
            {
                GlobalSettings c = GlobalSettings.CreateDefault();
                c.ResGameW = 640 + r.Next(7041); c.ResGameH = 480 + r.Next(3841); c.ResAspect = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)];
                c.ResFov = 50 + r.Next(26); c.ResBaseW = 640 + r.Next(7041); c.ResBaseH = 480 + r.Next(3841);
                c.ResBaseAspect = ViewScale.AspectIds[r.Next(ViewScale.AspectIds.Length)]; c.ResBaseFov = 50 + r.Next(26); c.ResFovType = r.Next(2) == 0 ? "v" : "h";
                c.Validate();
                GlobalSettings mg = SettingsIO.MergeLegacy(c, olds[i % olds.Count], out n);
                merges++;
                bool same = mg.ResBaseW == c.ResBaseW && mg.ResBaseH == c.ResBaseH && mg.ResBaseAspect == c.ResBaseAspect && mg.ResBaseFov == c.ResBaseFov
                    && mg.ResGameW == c.ResGameW && mg.ResGameH == c.ResGameH && mg.ResAspect == c.ResAspect && mg.ResFov == c.ResFov && mg.ResFovType == c.ResFovType
                    && mg.ResolutionScale == c.ResolutionScale;
                if (!same) { moved++; if (first == null) first = fx[i % olds.Count] + ": base " + c.ResBaseW + "x" + c.ResBaseH + " " + c.ResBaseAspect + " " + c.ResBaseFov + " -> " + mg.ResBaseW + "x" + mg.ResBaseH + " " + mg.ResBaseAspect + " " + mg.ResBaseFov; }
            }
            Check("V41-ST21 导入旧版设置 keeps this PC's 调参分辨率 / 调参比例 / 调参 FOV like the 游戏分辨率 (REBRUTE41 P2-1): 调参 FOV 60 + Leo's v3.0 file -> 「Leo默认」 D still " + before + " (k 0.7524 kept), its values unchanged; the imported old profiles keep their raw px; 300 random screens / bases x 6 old files: scale never changes",
                parsed && fov && raw && olds.Count == fx.Length && moved == 0,
                "parsed=" + parsed + " fov=" + fov + " (" + before + " -> " + after + ", k " + kBefore.ToString("0.####") + " -> " + (m == null ? "-" : m.ResolutionScale.ToString("0.####")) + ") raw=" + raw
                + " olds=" + olds.Count + " moved=" + moved + "/" + merges + " " + (first ?? ""));
        }

        // ------------------------------------------------------------------ TestsV4Brute scratch (BRUTE40 P2-14)

        static void StBruteScratch()
        {
            string a = BxScratch(), b = BxScratch();
            string parent = Path.GetDirectoryName(a);
            string sentinel = Path.Combine(parent, "st41-sentinel-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(sentinel, "keep");
            File.WriteAllText(Path.Combine(a, "settings.json"), "x");
            BxCleanScratch(a); BxCleanScratch(b); BxCleanScratch(parent);
            bool ok = a != b && Path.GetFileName(parent) == "brute-settings" && File.Exists(sentinel) && !Directory.Exists(a) && !Directory.Exists(b);
            File.Delete(sentinel);
            Check("V41-ST20 TestsV4Brute uses a folder of its own per run (brute-settings/run-<guid>) and deletes only that folder's files: other files in brute-settings survive (BRUTE40 P2-14)",
                ok, a);
        }
    }
}
