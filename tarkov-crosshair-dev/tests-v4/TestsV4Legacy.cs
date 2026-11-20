// UI-SHELL (v4.0) 「导入旧版设置」: SettingsIO.MergeLegacy / FindLegacyFile / ImportLegacy with Leo's v3.0 file and a
// v3.6-shaped file (old keys incl. moved W/A/S/D, no v4 fields, two profiles, one named Leo默认).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V4LegacyInit() { V4LegacyTests(); }

        static string V36Shaped()
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.Keys.Toggle = 0x75; g.Keys.Fire = 0x4C; g.Keys.LeanLeft = 0x31; g.Keys.LeanRight = 0x32; g.Keys.Jump = 0x33;
            g.AdsMode = "toggle"; g.LeanMode = "toggle"; g.ResGameW = 2560; g.ResGameH = 1600; g.OnlyWhenGameFocused = false;
            Profile p = g.Profiles[0];
            p.Dirs[0] = new DirSetting(1.5, -40); p.Dirs[2] = new DirSetting(70, 1); Profile.RecomputeDiagonals(p);
            p.RecoilMaxPx = 61; p.LeanQX = -7;
            Profile q = Profile.CreateBlank("AK 一套"); q.Validate(); q.RecoilMaxPx = 77; g.Profiles.Add(q); g.ActiveProfile = q.Name;
            g.Validate();
            JsonNode n = JsonNode.Parse(SettingsIO.Serialize(g));
            n.AsObject().Remove("TutorialDone"); n.AsObject().Remove("LeanCancelRun");
            foreach (string f in new string[] { "LeanLeftMods", "LeanRightMods", "FireMods", "AdsMods", "ToggleMods", "FreeLookMods", "JumpMods", "HandSwitchMods", "BoostMods" })
                n["Keys"].AsObject().Remove(f);
            n["Keys"]["Forward"] = 0x26; // an old file with W moved to ↑: v4 forces W/A/S/D
            n["Keys"]["Right"] = 0x27;
            return n.ToJsonString();
        }

        static void V4LegacyTests()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string v30 = File.ReadAllText(Fixture("leo-v30-1027-settings.json"));
            string v36 = V36Shaped();

            // LG1 v3.0 (one profile 「Leo默认」) into fresh v4 defaults
            {
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate();
                GlobalSettings old; string err;
                bool parsed = SettingsIO.TryParse(v30, out old, out err);
                int n = -1;
                GlobalSettings m = parsed ? SettingsIO.MergeLegacy(cur, old, out n) : null;
                bool ok = parsed && n == 1 && m.Profiles.Count == 2 && m.Profiles[0].Name == GlobalSettings.DefaultProfileName
                    && m.Profiles[1].Name == GlobalSettings.DefaultProfileName + SettingsIO.LegacySuffix
                    && m.ActiveProfile == m.Profiles[1].Name && m.Active == m.Profiles[1]
                    && m.Profiles[1].RecoilMaxPx == old.Profiles[0].RecoilMaxPx && m.Profiles[1].Dirs[0].OffY == old.Profiles[0].Dirs[0].OffY
                    && m.Keys.Get(KeyBindings.ActionToggle) == old.Keys.Get(KeyBindings.ActionToggle)
                    && cur.Profiles.Count == 1 && old.Profiles.Count == 1; // inputs unchanged
                Check("LG1 v3.0 file: 1 profile imported as 「Leo默认 (旧)」, active; 「Leo默认」 kept; keys from the old file", ok,
                    m == null ? err : string.Join(",", m.Profiles.ConvertAll(delegate(Profile p) { return p.Name; })) + " active=" + m.ActiveProfile);
            }

            // LG2 v3.6-shaped: 2 profiles, active = AK, keys + globals from old, W/A/S/D forced, no combos added
            {
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate(); cur.TutorialDone = true;
                Profile mine = Profile.CreateBlank("我的 v4"); mine.Validate(); cur.Profiles.Add(mine);
                double curW = cur.ResGameW, curH = cur.ResGameH; // v4.1 (BRUTE40 P1-4, Leo 17 Nov): the import keeps THIS screen
                GlobalSettings old; string err;
                bool parsed = SettingsIO.TryParse(v36, out old, out err);
                int n = -1;
                GlobalSettings m = parsed ? SettingsIO.MergeLegacy(cur, old, out n) : null;
                KeyBindings k = m == null ? null : m.Keys;
                bool ok = parsed && n == 2 && m.Profiles.Count == 4
                    && m.FindProfile(GlobalSettings.DefaultProfileName) != null && m.FindProfile("我的 v4") != null
                    && m.FindProfile(GlobalSettings.DefaultProfileName + SettingsIO.LegacySuffix) != null && m.FindProfile("AK 一套") != null
                    && m.ActiveProfile == "AK 一套" && m.Active.RecoilMaxPx == 77
                    && m.FindProfile(GlobalSettings.DefaultProfileName + SettingsIO.LegacySuffix).RecoilMaxPx == 61
                    && k.Get(KeyBindings.ActionForward) == 0x57 && k.Get(KeyBindings.ActionRight) == 0x44
                    && k.Get(KeyBindings.ActionLeanLeft) == 0x31 && k.Get(KeyBindings.ActionLeanRight) == 0x32 && k.GetMods(KeyBindings.ActionLeanRight) == 0
                    && m.AdsMode == "toggle" && m.LeanMode == "toggle" && m.ResGameW == curW && m.ResGameH == curH && old.ResGameW == 2560 && !m.OnlyWhenGameFocused
                    && m.TutorialDone; // finished the v4 tutorial already: stays done
                Check("LG2 v3.6-shaped file: 2 profiles imported, active AK, current profiles kept, W/A/S/D forced, keys + globals from old", ok,
                    m == null ? err : string.Join(",", m.Profiles.ConvertAll(delegate(Profile p) { return p.Name; })) + " active=" + m.ActiveProfile);
            }

            // LG3 combos in the old file are kept; importing twice gives unique names; null legacy = unchanged
            {
                GlobalSettings old = GlobalSettings.CreateDefault(); old.Keys.SetCombo(KeyBindings.ActionLeanRight, 0x44, KeyBindings.ModCtrl); old.Validate();
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate();
                int n1, n2, n3;
                GlobalSettings m1 = SettingsIO.MergeLegacy(cur, old, out n1);
                GlobalSettings m2 = SettingsIO.MergeLegacy(m1, old, out n2);
                GlobalSettings m3 = SettingsIO.MergeLegacy(cur, null, out n3);
                HashSet<string> names = new HashSet<string>(); bool unique = true;
                foreach (Profile p in m2.Profiles) unique &= names.Add(p.Name);
                bool ok = m1.Keys.ComboOf(KeyBindings.ActionLeanRight) == "Ctrl+D" && n1 == 1 && n2 == 1 && unique && m2.Profiles.Count == 3
                    && m2.Active == m2.Profiles[2] && m3 == cur && n3 == 0;
                Check("LG3 combos kept (Ctrl+D), second import gets a unique name, null old file = no change", ok,
                    string.Join(",", m2.Profiles.ConvertAll(delegate(Profile p) { return p.Name; })));
            }

            // LG4 FindLegacyFile: file, folder, 「* app」 sub-folder, one level deeper, nothing
            string root = Path.Combine(Path.GetTempPath(), "hipaimer-legacy-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "a"));
                Directory.CreateDirectory(Path.Combine(root, "b", "程序 app"));
                Directory.CreateDirectory(Path.Combine(root, "c", "TarkovHipAimer-v3.6", "app"));
                Directory.CreateDirectory(Path.Combine(root, "empty", "x"));
                string fa = Path.Combine(root, "a", "settings.json"), fb = Path.Combine(root, "b", "程序 app", "settings.json"),
                    fc = Path.Combine(root, "c", "TarkovHipAimer-v3.6", "app", "settings.json");
                File.WriteAllText(fa, v30); File.WriteAllText(fb, v36); File.WriteAllText(fc, v36);
                bool ok4 = SettingsIO.FindLegacyFile(fa) == fa && SettingsIO.FindLegacyFile(Path.Combine(root, "a")) == fa
                    && SettingsIO.FindLegacyFile(Path.Combine(root, "b")) == fb && SettingsIO.FindLegacyFile(Path.Combine(root, "c")) == fc
                    && SettingsIO.FindLegacyFile(Path.Combine(root, "empty")) == null && SettingsIO.FindLegacyFile(null) == null
                    && SettingsIO.FindLegacyFile(Path.Combine(root, "nope")) == null;
                Check("LG4 FindLegacyFile: settings.json, its folder, 「程序 app」 sub-folder, unzipped package, none", ok4, root);

                // LG5 ImportLegacy end to end: backup of the current file, saved result loads back the same
                string target = Path.Combine(root, "cur", "settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                GlobalSettings cur = GlobalSettings.CreateDefault(); cur.Validate(); SettingsIO.Save(cur, target);
                string before = File.ReadAllText(target);
                int n; string err;
                GlobalSettings m = SettingsIO.ImportLegacy(cur, Path.Combine(root, "b"), target, out n, out err);
                string[] baks = Directory.GetFiles(Path.GetDirectoryName(target), "settings.before-import-*.json");
                string msg;
                GlobalSettings back = SettingsIO.Load(target, out msg);
                bool ok5 = m != null && n == 2 && baks.Length == 1 && File.ReadAllText(baks[0]) == before && msg == null
                    && back.Profiles.Count == 3 && back.ActiveProfile == "AK 一套" && back.Keys.Get(KeyBindings.ActionLeanLeft) == 0x31;
                // bad inputs: a folder without settings.json, a non-settings file -> null + Chinese error, target untouched
                string after = File.ReadAllText(target);
                string junk = Path.Combine(root, "junk.json"); File.WriteAllText(junk, "not json");
                int nb; string e1, e2;
                GlobalSettings b1 = SettingsIO.ImportLegacy(back, Path.Combine(root, "empty"), target, out nb, out e1);
                GlobalSettings b2 = SettingsIO.ImportLegacy(back, junk, target, out nb, out e2);
                ok5 &= b1 == null && b2 == null && e1 != null && e2 != null && e1.Contains("settings.json") && !e2.Contains("Exception")
                    && File.ReadAllText(target) == after;
                Check("LG5 ImportLegacy: current file backed up (settings.before-import-*), merged file saved + reloads, bad input = Chinese error, nothing written",
                    ok5, (err ?? "") + " n=" + n + " baks=" + baks.Length + " " + (e1 ?? "") + " | " + (e2 ?? ""));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }
    }
}
