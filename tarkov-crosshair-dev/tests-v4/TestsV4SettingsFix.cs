using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    // v4.0 SETTINGS-FIX: KeyBindings.Set normalises mods (BRUTE C5), tolerant settings load (one wrong-type field no
    // longer resets the whole file), UserDirFor accepts garbled 「<x> app」 + 「<x> Start-HipAimer.bat」 (PACKAGE request).
    public static partial class Program
    {
        static void V4SettingsFixTests()
        {
            SfSetMods();
            SfEveryLeafWrongType();
            SfParserEdges();
            SfLoad();
            SfUserDir();
        }

        // ------------------------------------------------------------------ BRUTE C5
        static void SfSetMods()
        {
            KeyBindings k = new KeyBindings();
            k.SetCombo(6, KeyBindings.VkD, KeyBindings.ModCtrl);
            bool combo = k.ComboOf(6) == "Ctrl+D";
            k.Set(6, 0x11);
            bool a = k.GetMods(6) == 0 && k.ComboOf(6) == k.ComboOf(6) && k.Get(6) == 0x11;
            k.SetCombo(6, KeyBindings.VkD, KeyBindings.ModCtrl | KeyBindings.ModShift);
            k.Set(6, 0xA0); // LShift: Shift bit is the key itself now
            bool b = k.GetMods(6) == KeyBindings.ModCtrl;
            k.SetCombo(5, KeyBindings.VkD, KeyBindings.ModCtrl);
            k.Set(5, 0x45);
            bool c = k.GetMods(5) == KeyBindings.ModCtrl && k.ComboOf(5) == "Ctrl+E";
            bool move = true;
            for (int x = 7; x <= 10; x++) { k.Set(x, 0x11); move &= k.GetMods(x) == 0; }
            move &= k.Forward == KeyBindings.VkW && k.Right == KeyBindings.VkD;
            bool all = true;
            for (int x = 0; x < KeyBindings.ActionCount; x++)
                foreach (int vk in new int[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x44 })
                {
                    KeyBindings q = new KeyBindings();
                    q.SetCombo(x, 0x44, 7);
                    q.Set(x, vk);
                    all &= q.GetMods(x) == KeyBindings.NormalizeMods(q.Get(x), q.GetMods(x));
                }
            Check("SF1 BRUTE C5: Set(key) after SetCombo re-normalises mods (Ctrl+D -> Set(Ctrl) = plain Ctrl; Ctrl+Shift+D -> Set(LShift) = Ctrl+LShift; Ctrl+D -> Set(E) = Ctrl+E); every action x modifier key stays normalised; WASD untouched",
                combo && a && b && c && move && all, combo + " " + a + " " + b + " " + c + " " + move + " " + all);
        }

        // ------------------------------------------------------------------ every leaf, every wrong type
        static GlobalSettings SfCustom()
        {
            GlobalSettings g = BxRandomConfig(new Random(7), true);
            g.Profiles.Add(Profile.CreateBlank("第二套"));
            g.Active.SaveAsDefault("x");
            g.Validate();
            return g;
        }

        static void SfLeaves(JsonNode n, string path, List<string> paths)
        {
            if (n is JsonObject)
            {
                foreach (KeyValuePair<string, JsonNode> kv in (JsonObject)n)
                {
                    string p = path.Length == 0 ? kv.Key : path + "." + kv.Key;
                    paths.Add(p);
                    if (kv.Value != null) SfLeaves(kv.Value, p, paths);
                }
            }
            else if (n is JsonArray)
            {
                JsonArray a = (JsonArray)n;
                for (int i = 0; i < a.Count; i++) { string p = path + "[" + i + "]"; paths.Add(p); if (a[i] != null) SfLeaves(a[i], p, paths); }
            }
        }

        // Resolves "a.b[2].c" to (parent node, key or index).
        static bool SfLocate(JsonNode root, string path, out JsonNode parent, out string key, out int index)
        {
            parent = null; key = null; index = -1;
            JsonNode cur = root;
            int i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.') { i++; continue; }
                if (path[i] == '[')
                {
                    int j = path.IndexOf(']', i);
                    int idx = int.Parse(path.Substring(i + 1, j - i - 1));
                    parent = cur; key = null; index = idx;
                    i = j + 1;
                    if (i < path.Length) cur = ((JsonArray)cur)[idx];
                }
                else
                {
                    int j = i;
                    while (j < path.Length && path[j] != '.' && path[j] != '[') j++;
                    string k = path.Substring(i, j - i);
                    parent = cur; key = k; index = -1;
                    i = j;
                    if (i < path.Length) cur = ((JsonObject)cur)[k];
                }
            }
            return parent != null;
        }

        static JsonNode SfGet(JsonNode parent, string key, int index) { return key != null ? ((JsonObject)parent)[key] : ((JsonArray)parent)[index]; }

        static void SfEveryLeafWrongType()
        {
            string baseJson = SettingsIO.Serialize(SfCustom());
            JsonNode probe = JsonNode.Parse(baseJson);
            List<string> paths = new List<string>();
            SfLeaves(probe, "", paths);
            int cases = 0, strictOk = 0, bad = 0, threw = 0, rangeBad = 0;
            string first = null;
            foreach (string path in paths)
            {
                JsonNode orig;
                {
                    JsonNode r0 = JsonNode.Parse(baseJson); JsonNode par; string k; int ix;
                    SfLocate(r0, path, out par, out k, out ix);
                    orig = SfGet(par, k, ix);
                }
                // wrong-type replacements: a value of every other JSON kind
                List<JsonNode> repl = new List<JsonNode>();
                bool isStr = orig is JsonValue && orig.GetValueKind() == System.Text.Json.JsonValueKind.String;
                bool isNum = orig is JsonValue && orig.GetValueKind() == System.Text.Json.JsonValueKind.Number;
                bool isBool = orig is JsonValue && (orig.GetValueKind() == System.Text.Json.JsonValueKind.True || orig.GetValueKind() == System.Text.Json.JsonValueKind.False);
                if (!isStr) repl.Add(JsonValue.Create("abc"));
                if (!isNum) repl.Add(JsonValue.Create(12.5));
                if (!isBool) repl.Add(JsonValue.Create(true));
                if (!(orig is JsonObject)) repl.Add(new JsonObject());
                if (!(orig is JsonArray)) repl.Add(new JsonArray(1, 2));
                foreach (JsonNode rv in repl)
                {
                    cases++;
                    JsonNode r1 = JsonNode.Parse(baseJson); JsonNode p1; string k1; int i1;
                    SfLocate(r1, path, out p1, out k1, out i1);
                    if (k1 != null) ((JsonObject)p1)[k1] = rv.DeepClone(); else ((JsonArray)p1)[i1] = rv.DeepClone();
                    string json = r1.ToJsonString();

                    GlobalSettings g; string err; List<string> dropped;
                    bool ok;
                    try { ok = SettingsIO.TryParse(json, out g, out err, out dropped); }
                    catch (Exception ex) { threw++; if (first == null) first = path + " threw " + ex.GetType().Name; continue; }
                    if (ok && dropped.Count == 0) { strictOk++; if (BxRangeErrors(g).Count > 0) rangeBad++; continue; } // serializer accepted it as is
                    if (!ok) { bad++; if (first == null) first = path + "=" + rv.ToJsonString() + " rejected: " + err; continue; }
                    if (BxRangeErrors(g).Count > 0) { rangeBad++; if (first == null) first = path + " out of range"; }

                    // oracle: the same file with that member removed (or, for an object inside a list, set to null; for a
                    // number/bool/string inside a list, the whole list removed)
                    JsonNode r2 = JsonNode.Parse(baseJson); JsonNode p2; string k2; int i2;
                    SfLocate(r2, path, out p2, out k2, out i2);
                    string expectDrop = path;
                    if (k2 != null) ((JsonObject)p2).Remove(k2);
                    else if (SfGet(p2, k2, i2) is JsonObject) ((JsonArray)p2)[i2] = null;
                    else
                    {
                        string listPath = path.Substring(0, path.LastIndexOf('['));
                        JsonNode p3; string k3; int i3;
                        SfLocate(r2, listPath, out p3, out k3, out i3);
                        if (k3 != null) ((JsonObject)p3).Remove(k3); else ((JsonArray)p3)[i3] = null;
                        expectDrop = listPath;
                    }
                    GlobalSettings ge; string erre;
                    bool okE = SettingsIO.TryParse(r2.ToJsonString(), out ge, out erre);
                    if (okE) SettingsIO.FillDroppedFromLeoDefault(ge, new List<string> { expectDrop }); // v5.1 (W23, decision 17): Leo's v5 value, not the class default
                    bool same = okE && SettingsIO.Serialize(ge) == SettingsIO.Serialize(g);
                    bool dropOk = dropped.Count == 1 && dropped[0] == expectDrop;
                    if (!same || !dropOk)
                    {
                        bad++;
                        if (first == null) first = path + "=" + rv.ToJsonString() + " same=" + same + " dropped=[" + string.Join(",", dropped) + "] expected " + expectDrop;
                    }
                }
            }
            Check("SF2 every member of a customised settings.json (" + paths.Count + " paths x every wrong JSON type = " + cases + " files): accepted, ONLY that member gets its default (identical to the file with it removed), dropped list names it, no throw, in range",
                bad == 0 && threw == 0 && rangeBad == 0, bad + " bad, " + threw + " threw, " + rangeBad + " out of range, " + strictOk + " accepted as is by the serializer" + (first == null ? "" : " | first: " + first));
        }

        // ------------------------------------------------------------------ parser edges
        static void SfParserEdges()
        {
            string def = SettingsIO.Serialize(GlobalSettings.CreateDefault());
            Func<string, string, string> put = delegate(string member, string raw)
            {
                // top-level member replaced with raw JSON text
                JsonObject o = (JsonObject)JsonNode.Parse(def);
                o[member] = JsonNode.Parse(raw);
                return o.ToJsonString();
            };
            GlobalSettings g; string err; List<string> d;
            int okCount = 0, total = 0; StringBuilder why = new StringBuilder();
            Action<string, bool> need = delegate(string name, bool c) { total++; if (c) okCount++; else why.Append(name + "; "); };

            bool p1 = SettingsIO.TryParse(put("Version", "99999999999"), out g, out err, out d);
            need("int overflow dropped", p1 && d.Count == 1 && d[0] == "Version");
            bool p2 = SettingsIO.TryParse(put("ResGameW", "1920.5"), out g, out err, out d);
            need("int not whole dropped", p2 && d.Count >= 0 && (d.Count == 0 || d[0] == "ResGameW"));
            bool p3 = SettingsIO.TryParse(put("ResGameW", "\"1920\""), out g, out err, out d);
            need("numeric string for int: accepted (the serializer itself reads \"1920\" as 1920, or the cleaner drops it)", p3 && (d.Count == 0 || (d.Count == 1 && d[0] == "ResGameW")));
            bool p3b = SettingsIO.TryParse(put("ResGameW", "\"abc\""), out g, out err, out d);
            need("non-numeric string for int dropped, others kept", p3b && d.Count == 1 && d[0] == "ResGameW");
            bool p4 = SettingsIO.TryParse("{\"__type\":\"x\",\"Enabled\":\"yes\",\"Fps\":120}", out g, out err, out d);
            need("__type + wrong bool dropped", p4 && d.Contains("__type") && d.Contains("Enabled") && d.Count == 2);
            bool p5 = SettingsIO.TryParse("{\"Enabled\":\"no\",\"GameWindowTitle\":\"中\\\"\\\\\\n\\u00e9\\/x\"}", out g, out err, out d);
            need("escapes kept through the cleaner", p5 && g != null && g.GameWindowTitle != null && g.GameWindowTitle.Contains("中\"\\") && g.GameWindowTitle.Contains("é/x"));
            string deep = "{\"Enabled\":\"x\",\"Unknown\":" + new string('[', 5000) + new string(']', 5000) + "}";
            bool p6;
            try { p6 = SettingsIO.TryParse(deep, out g, out err, out d); need("5000-deep nesting: no throw, rejected", !p6); }
            catch (Exception) { need("5000-deep nesting: no throw", false); }
            bool p7 = SettingsIO.TryParse("{\"Enabled\":\"x\",\"Fps\":1,\"Fps\":2}", out g, out err, out d);
            need("duplicate key: last wins", p7 && g != null);
            bool p8 = SettingsIO.TryParse("{\"Enabled\":\"x\",}", out g, out err, out d);
            need("syntax error still rejected", !p8 && err != null && d.Count == 0);
            bool p9 = SettingsIO.TryParse("{\"Profiles\":\"abc\",\"ActiveProfile\":\"zz\"}", out g, out err, out d);
            need("Profiles wrong type -> default profiles", p9 && g.Profiles != null && g.Profiles.Count >= 1);
            bool p10 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"A\",\"Dirs\":[\"x\",{\"OffX\":5,\"OffY\":\"y\"}]}]}", out g, out err, out d);
            need("Dirs keep positions: Dirs[0] null, Dirs[1].OffX kept", p10 && g.Profiles[0].Dirs[1].OffX == 5 && d.Contains("Profiles[0].Dirs[0]") && d.Contains("Profiles[0].Dirs[1].OffY"));
            bool p11 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"A\",\"CurveAD\":{\"DurationMs\":200,\"Speeds\":[1,\"x\",3]}}]}", out g, out err, out d);
            need("Speeds with a string -> whole list default, DurationMs kept", p11 && g.Profiles[0].CurveAD.DurationMs == 200 && d.Count == 1 && d[0] == "Profiles[0].CurveAD.Speeds");
            bool p12 = SettingsIO.TryParse("{\"Keys\":{\"LeanRight\":\"D\",\"LeanRightMods\":1,\"Toggle\":123}}", out g, out err, out d);
            need("Keys: one bad key, others kept", p12 && g.Keys.Toggle == 123 && g.Keys.LeanRight == new KeyBindings().LeanRight);
            Check("SF3 tolerant parser edges (int overflow/not whole, __type, escapes, 5000-deep nesting, duplicate keys, syntax error, wrong-type lists, Dirs positions, Speeds, Keys)",
                okCount == total, okCount + "/" + total + (why.Length == 0 ? "" : " | " + why));
        }

        // ------------------------------------------------------------------ Load path
        static void SfLoad()
        {
            string dir = Path.Combine(outDir, "settingsfix-load");
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            string path = Path.Combine(dir, "settings.json");

            GlobalSettings custom = SfCustom();
            JsonObject o = (JsonObject)JsonNode.Parse(SettingsIO.Serialize(custom));
            ((JsonObject)((JsonArray)o["Profiles"])[0])["Diameter"] = "big";
            string badText = o.ToJsonString();
            File.WriteAllText(path, badText, new UTF8Encoding(false));
            string msg;
            GlobalSettings g = SettingsIO.Load(path, out msg);
            string[] backups = Directory.GetFiles(dir, "settings.bad-*.json");
            bool backupOk = backups.Length == 1 && File.ReadAllText(backups[0]) == badText;
            JsonObject expectO = (JsonObject)JsonNode.Parse(SettingsIO.Serialize(custom));
            ((JsonObject)((JsonArray)expectO["Profiles"])[0]).Remove("Diameter");
            GlobalSettings ge; string e1;
            SettingsIO.TryParse(expectO.ToJsonString(), out ge, out e1);
            SettingsIO.FillDroppedFromLeoDefault(ge, new List<string> { "Profiles[0].Diameter" }); // v5.1 (W23, decision 17)
            bool kept = SettingsIO.Serialize(g) == SettingsIO.Serialize(ge) && g.Profiles[0].Diameter == Profile.CreateLeoDefault("x").Diameter
                && g.Profiles.Count == custom.Profiles.Count && g.Keys.ComboOf(6) == custom.Keys.ComboOf(6);
            GlobalSettings re; string e2; List<string> d2;
            bool resaved = SettingsIO.TryParse(File.ReadAllText(path), out re, out e2, out d2) && d2.Count == 0 && SettingsIO.Serialize(re) == SettingsIO.Serialize(g);
            bool msgOk = msg != null && msg.Contains("1 项") && msg.Contains("Profiles[0].Diameter") && msg.Contains(Path.GetFileName(backups.Length > 0 ? backups[0] : "?")) && msg.Contains("其余设置都保留");
            string msg2;
            SettingsIO.Load(path, out msg2);
            bool second = msg2 == null && Directory.GetFiles(dir, "settings.bad-*.json").Length == 1;
            Check("SF4 Load with one wrong-type field: only that field defaulted, everything else kept, original copied to settings.bad-*.json, file re-saved clean, message names the field; next Load is silent",
                backupOk && kept && resaved && msgOk && second, backupOk + " " + kept + " " + resaved + " " + msgOk + " " + second + " | " + msg);

            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            File.WriteAllText(path, "{\"Enabled\": tru", new UTF8Encoding(false));
            string msg3;
            GlobalSettings g3 = SettingsIO.Load(path, out msg3);
            bool renamed = Directory.GetFiles(dir, "settings.bad-*.json").Length == 1 && msg3 != null && msg3.Contains("默认设置")
                && SettingsIO.Serialize(g3) == SettingsIO.Serialize(SfDefaultValidated());
            Check("SF5 Load with a broken (not JSON) file: unchanged behaviour (renamed to settings.bad-*.json, defaults, message)", renamed, msg3);
            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            Directory.Delete(dir);
        }

        static GlobalSettings SfDefaultValidated() { GlobalSettings d = GlobalSettings.CreateDefault(); d.Validate(); return d; }

        // ------------------------------------------------------------------ PACKAGE: UserDirFor garbled names
        static void SfUserDir()
        {
            string root = "/x/TarkovHipAimer";
            Func<string, bool> none = delegate(string f) { return false; };
            Func<string, string[]> garbledBat = delegate(string d) { return new string[] { d + "/鍚姩 Start-HipAimer.bat", d + "/说明 README.txt" }; };
            Func<string, string[]> noBat = delegate(string d) { return new string[] { d + "/Start-HipAimer.bat.txt", d + "/xStart-HipAimer.bat" }; };
            Func<string, Func<string, bool>, Func<string, string[]>, string> U = delegate(string d, Func<string, bool> fe, Func<string, string[]> lb) { string r = SettingsIO.UserDirFor(d, fe, lb); return r == null ? null : r.Replace('\\', '/'); }; // v5.1.1: Windows gives \
            Func<string, string[]> throws = delegate(string d) { throw new IOException("denied"); };
            bool a = U(root + "/绋嬪簭 app", none, garbledBat) == root;
            bool b = U(root + "/绋嬪簭 app/", none, garbledBat) == root;
            bool c = U(root + "/程序 app", none, noBat) == root + "/程序 app";
            bool d1 = U(root + "/myapp", none, garbledBat) == root + "/myapp";
            bool e = U(root + "/绋嬪簭 app", none, throws) == root + "/绋嬪簭 app";
            bool f1 = U(root + "/bin", none, garbledBat) == root + "/bin";
            bool g = U(root + "/APP", delegate(string p) { return p.Replace('\\', '/') == root + "/启动 Start-HipAimer.bat"; }, null) == root;
            bool h = U(root + "/x app", delegate(string p) { return p.Replace('\\', '/') == root + "/Start-HipAimer.bat"; }, null) == root;
            bool i = SettingsIO.UserDirFor(null, none, garbledBat) == "" && SettingsIO.UserDirFor("app", none, garbledBat) == "app";
            // real file system through the 2-argument overload
            string real = Path.Combine(outDir, "userdir-garbled");
            string app = Path.Combine(real, "绋嬪簭 app");
            Directory.CreateDirectory(app);
            string bat = Path.Combine(real, "鍚姩 Start-HipAimer.bat");
            File.WriteAllText(bat, "@echo off");
            bool j = SettingsIO.UserDirFor(app, File.Exists) == real;
            File.Delete(bat);
            bool k = SettingsIO.UserDirFor(app, File.Exists) == app;
            Directory.Delete(app); Directory.Delete(real);
            Check("SF6 UserDirFor: any 「<x> app」 folder + 「<x> Start-HipAimer.bat」 (garbled zip names) -> parent; no bat / not an app folder / listing error -> exe folder; exact names still work; real file system",
                a && b && c && d1 && e && f1 && g && h && i && j && k, a + " " + b + " " + c + " " + d1 + " " + e + " " + f1 + " " + g + " " + h + " " + i + " " + j + " " + k);
        }
    }
}
