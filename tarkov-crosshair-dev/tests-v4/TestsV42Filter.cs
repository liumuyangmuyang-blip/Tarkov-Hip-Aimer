// v4.2 (W2) 滤镜 in the crosshair exe: hotkeys (src/FilterHotkeys.cs), the two-way key clash rule, modifier conversion,
// filter-prefs.json, the module sync (src/Shade = tarkov-shade-module/src) and the module types the page relies on.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TarkovShade;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V42FilterInit() { V42FilterTests(); }

        static void V42FilterTests()
        {
            // F1 defaults (v4.3, Leo: only toggle / next / hold): F8, F9, F7, all valid, all different; an old 6-key file keeps 3.
            {
                FilterPrefs p = new FilterPrefs(); p.Normalize();
                bool ok = FilterKeys.ActionCount == 3 && p.Vk.Length == 3 && p.Vk[0] == 0x77 && p.Mods[0] == 0 && p.Vk[1] == 0x78 && p.Vk[2] == 0x76 && !p.WasOn;
                FilterPrefs old = FilterPrefs.FromJson("{\"Version\":1,\"Vk\":[119,120,118,49,50,117],\"Mods\":[0,0,0,1,1,1]}");
                ok = ok && old != null && old.Vk.Length == 3 && old.Vk[2] == 0x76;
                for (int a = 0; a < FilterKeys.ActionCount; a++) if (FilterKeys.Validate(p.Vk[a], p.Mods[a]).Length > 0) ok = false;
                for (int a = 0; a < FilterKeys.ActionCount; a++) if (FilterKeys.FilterClash(p, a, p.Vk[a], p.Mods[a]) >= 0) ok = false;
                Check("V42-F1 filter defaults F8 / F9 / F7 only (v4.3), valid, unique, filter off; old 6-key prefs -> 3", ok,
                    string.Join(",", p.Vk.Select(v => v.ToString("X"))));
            }

            // F2 no default filter key shares a base key with any crosshair default (both directions).
            {
                KeyBindings k = new KeyBindings();
                FilterPrefs p = new FilterPrefs();
                string d = "";
                for (int a = 0; a < FilterKeys.ActionCount; a++)
                {
                    int x = FilterKeys.CrosshairClash(k, p.Vk[a], p.Mods[a]);
                    if (x >= 0) d += FilterKeys.ActionNames[a] + "~" + KeyBindings.ActionNames[x] + " ";
                }
                for (int x = 0; x < KeyBindings.ActionCount; x++)
                    if (FilterKeys.FilterUsing(p, k.Get(x)) >= 0) d += "xh" + x + " ";
                Check("V42-F2 filter defaults vs crosshair defaults: no shared base key", d.Length == 0, d);
            }

            // F3 the clash rule: same base key = clash whatever the modifiers (Ctrl+F12 vs F12, F12 vs Ctrl+F12, F8 vs Ctrl+F8).
            {
                int C = KeyBindings.ModCtrl, S = KeyBindings.ModShift;
                bool ok = FilterKeys.ClashesWithCrosshair(0x7B, C, 0x7B, 0) && FilterKeys.ClashesWithCrosshair(0x7B, 0, 0x7B, C)
                    && FilterKeys.ClashesWithCrosshair(0x77, C | S, 0x77, S) && !FilterKeys.ClashesWithCrosshair(0x77, 0, 0x78, 0)
                    && !FilterKeys.ClashesWithCrosshair(0, 0, 0, 0) && !FilterKeys.ClashesWithCrosshair(0x77, 0, 0, 0);
                KeyBindings k = new KeyBindings();
                ok = ok && FilterKeys.CrosshairClash(k, 0x7B, C) == KeyBindings.ActionToggle && FilterKeys.CrosshairClash(k, 0x7A, KeyBindings.ModAlt) == -1 /* v4.3 (W10): F11 is free */
                    && FilterKeys.CrosshairClash(null, 0x7B, 0) == -1;
                FilterPrefs p = new FilterPrefs();
                ok = ok && FilterKeys.FilterUsing(p, 0x77) == FilterKeys.ActionToggle && FilterKeys.FilterUsing(p, 0x76) == FilterKeys.ActionHold
                    && FilterKeys.FilterUsing(p, 0x31) == -1 && FilterKeys.FilterUsing(p, 0x7B) == -1 && FilterKeys.FilterUsing(p, 0) == -1;
                Check("V42-F3 base-key clash rule both ways (Ctrl+F12 ~ F12; 按键 page refuses a filter base key)", ok, "");
            }

            // F4 key rules: F-keys or modifier combos; never plain letters / digits / space / Enter, mouse, modifier alone, Esc.
            {
                int C = KeyBindings.ModCtrl;
                bool ok = FilterKeys.Validate(0x70, 0) == "" && FilterKeys.Validate(0x87, 0) == "" && FilterKeys.Validate(0x41, C) == ""
                    && FilterKeys.Validate(0x31, KeyBindings.ModAlt) == "" && FilterKeys.Validate(0, 0) == ""
                    && FilterKeys.Validate(0x41, 0) != "" && FilterKeys.Validate(0x31, 0) != "" && FilterKeys.Validate(0x20, 0) != ""
                    && FilterKeys.Validate(0x0D, 0) != "" && FilterKeys.Validate(0x01, C) != "" && FilterKeys.Validate(0x05, 0) != ""
                    && FilterKeys.Validate(0x11, 0) != "" && FilterKeys.Validate(0xA2, C) != "" && FilterKeys.Validate(0x1B, C) != ""
                    && FilterKeys.Validate(0x5B, C) != "" && FilterKeys.Validate(300, 0) != "";
                Check("V42-F4 filter key rules (F1-F24 or Ctrl/Shift/Alt combos only)", ok, "");
            }

            // F5 modifier bitmask conversion, both ways, against the module's own constants.
            {
                bool ok = FilterKeys.ShadeAlt == ShadeSettings.ModAlt && FilterKeys.ShadeCtrl == ShadeSettings.ModCtrl && FilterKeys.ShadeShift == ShadeSettings.ModShift
                    && FilterKeys.ToShadeMods(KeyBindings.ModCtrl) == ShadeSettings.ModCtrl
                    && FilterKeys.ToShadeMods(KeyBindings.ModShift) == ShadeSettings.ModShift
                    && FilterKeys.ToShadeMods(KeyBindings.ModAlt) == ShadeSettings.ModAlt
                    && FilterKeys.FromShadeMods(ShadeSettings.ModCtrl) == KeyBindings.ModCtrl
                    && FilterKeys.FromShadeMods(ShadeSettings.ModAlt) == KeyBindings.ModAlt
                    && FilterKeys.ToShadeMods(0) == 0 && FilterKeys.FromShadeMods(0) == 0;
                for (int m = 0; m < 8; m++) if (FilterKeys.FromShadeMods(FilterKeys.ToShadeMods(m)) != m || FilterKeys.ToShadeMods(FilterKeys.FromShadeMods(m)) != m) ok = false;
                Check("V42-F5 modifier bits: crosshair Ctrl1/Shift2/Alt4 <-> filter Alt1/Ctrl2/Shift4 (all 8 round-trip)", ok, "");
            }

            // F6 exact-modifier matching + edge detection: F8 alone toggles, Ctrl+F8 does not; first poll never fires; hold up/down.
            {
                HashSet<int> down = new HashSet<int>();
                Func<int, bool> isDown = delegate(int vk) { return down.Contains(vk); };
                FilterPrefs p = new FilterPrefs();
                FilterKeyPoller poll = new FilterKeyPoller();
                List<string> ev = new List<string>();
                poll.Pressed += delegate(int a) { ev.Add("P" + a); };
                poll.HoldChanged += delegate(bool d) { ev.Add(d ? "H+" : "H-"); };
                down.Add(0x77); poll.Poll(p, isDown);                  // held at start: nothing
                down.Clear(); poll.Poll(p, isDown);
                down.Add(0x77); poll.Poll(p, isDown); poll.Poll(p, isDown); // one press
                down.Clear(); poll.Poll(p, isDown);
                down.Add(0x11); down.Add(0x77); poll.Poll(p, isDown);  // Ctrl+F8: not the toggle
                down.Clear(); poll.Poll(p, isDown);
                down.Add(0x11); down.Add(0x31); poll.Poll(p, isDown);  // Ctrl+1: no filter key any more (v4.3)
                down.Clear(); poll.Poll(p, isDown);
                down.Add(0x76); poll.Poll(p, isDown); poll.Poll(p, isDown); down.Clear(); poll.Poll(p, isDown); // hold F7
                down.Add(0x78); poll.Poll(p, isDown);                  // F9 next
                string got = string.Join(" ", ev);
                Check("V42-F6 poller: exact modifiers, one event per press, hold down/up", got == "P0 H+ H- P1", got);
            }

            // F7 filter-prefs.json: round trip, garbage -> null / defaults, Normalize drops duplicates and invalid keys.
            {
                FilterPrefs p = new FilterPrefs(); p.WasOn = true; p.KeysOnlyInGame = true; p.Vk[0] = 0x80; p.Mods[0] = 0;
                FilterPrefs q = FilterPrefs.FromJson(FilterPrefs.ToJson(p));
                bool ok = q != null && q.WasOn && q.KeysOnlyInGame && q.Vk[0] == 0x80 && q.Vk[2] == 0x76;
                ok = ok && FilterPrefs.FromJson("{nope") == null && FilterPrefs.FromJson(null) == null;
                FilterPrefs bad = new FilterPrefs(); bad.Vk = new int[] { 0x41, 0x78, 0x78 }; bad.Mods = new int[] { 0, 0, 0 };
                bad.Normalize();
                ok = ok && bad.Vk.Length == FilterKeys.ActionCount && bad.Vk[0] == 0 && bad.Vk[1] == 0x78 && bad.Vk[2] == 0;
                FilterPrefs shortP = new FilterPrefs(); shortP.Vk = new int[] { 0x80 }; shortP.Mods = new int[] { 0 }; shortP.Normalize();
                ok = ok && shortP.Vk[0] == 0x80 && shortP.Vk[2] == 0x76;
                FilterPrefs missing = FilterPrefs.FromJson("{\"Version\":1}");
                ok = ok && missing != null && missing.Vk[0] == 0x77 && !missing.WasOn;
                string dir = Path.Combine(Path.GetTempPath(), "w2prefs-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    FilterPrefs d0 = FilterPrefs.Load(dir);
                    ok = ok && !d0.WasOn && FilterPrefs.Save(p, dir) == "" && FilterPrefs.Save(p, dir) == "" && FilterPrefs.Load(dir).WasOn;
                    File.WriteAllText(FilterPrefs.PathIn(dir), "garbage");
                    ok = ok && !FilterPrefs.Load(dir).WasOn;
                }
                finally
                {
                    foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
                    Directory.Delete(dir);
                }
                Check("V42-F7 filter-prefs.json round trip, bad files -> defaults (filter off), Normalize fixes keys", ok, "");
            }

            // F8 SpareKeys (fallback for migrated crosshair keys) never hands out a default filter key.
            {
                FilterPrefs p = new FilterPrefs();
                string d = "";
                foreach (int s in GlobalSettings.SpareKeys) if (FilterKeys.FilterUsing(p, s) >= 0) d += s.ToString("X") + " ";
                Check("V42-F8 GlobalSettings.SpareKeys contains no filter default key (F9 removed)", d.Length == 0, d);
            }

            // F9 src/Shade is an exact copy of the module (sync-shade.sh), PilotShareCode excluded, nothing references it.
            {
                string shade = Path.Combine(UiSrcDirW2(), "Shade"), mod = "/home/dev/tarkov-shade-module/src";
                string d = "";
                if (!Directory.Exists(shade)) d = "no src/Shade";
                else
                {
                    if (File.Exists(Path.Combine(shade, "PilotShareCode.cs"))) d += "PilotShareCode copied ";
                    if (Directory.Exists(mod))
                        foreach (string f in Directory.GetFiles(mod, "*.cs"))
                        {
                            string n = Path.GetFileName(f);
                            if (n == "PilotShareCode.cs") continue;
                            string c = Path.Combine(shade, n);
                            if (!File.Exists(c)) d += "missing " + n + " ";
                            else if (!File.ReadAllBytes(c).SequenceEqual(File.ReadAllBytes(f))) d += "differs " + n + " ";
                        }
                    foreach (string f in Directory.GetFiles(UiSrcDirW2(), "*.cs").Concat(Directory.GetFiles(shade, "*.cs")))
                        if (File.ReadAllText(f).Contains("PilotShareCode")) d += "ref in " + Path.GetFileName(f) + " ";
                }
                Check("V42-F9 src/Shade = tarkov-shade-module/src (byte copy, no PilotShareCode)", d.Length == 0, d);
            }

            // F10 the page's module needs: the two built-ins, fixed strength, tooltips for every row and hotkey.
            {
                List<PilotStyle> b = PilotBuiltIns.Create();
                bool ok = b.Count == 2 && b[0].Name == PilotBuiltIns.GeneralName && b[1].Name == PilotBuiltIns.NightName && b[1].Untested
                    && PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode) == 150 && PilotStyle.StrengthSignedMax(PilotViews.FixedStrengthMode) == 70;
                string d = "";
                foreach (string k in PilotViews.BasicStyle.Concat(PilotViews.Advanced).Concat(new[] { PilotHelpText.Saturation, PilotHelpText.SaturationBackend }))
                    if (PilotHelpText.Tooltip(k).Length == 0) d += k + " ";
                foreach (string k in FilterKeys.HelpKeys) if (PilotHelpText.Tooltip(k).Length == 0) d += k + " ";
                Check("V42-F10 module: 通用 + 黑夜模式 (未实测), 标准 ranges 0..150 / -70..70, a tooltip for every row and hotkey", ok && d.Length == 0, d);
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V43FilterInit() { V43FilterTests(); }

        static void V43FilterTests()
        {
            // V43-F1 通用 = Leo sun2 style values + digital vibrance +5 (Saturation 55 through the driver), monitor untouched.
            {
                PilotStyle g = PilotBuiltIns.General();
                bool ok = string.Join(",", g.ToArray()) == "115,32,-21,70,14,18,0,0,65,0,53,-70,28" && g.Saturation == 55 && g.ApplySaturation
                    && g.SaturationBackend == PilotStyle.SaturationBackendDriver && g.MonitorBrightness == null && g.MonitorContrast == null && g.MonitorSharpness == null
                    && !PilotBuiltIns.Night().ApplySaturation;
                Check("V43-F1 通用: sun2 values, Saturation 55 via driver vibrance (Leo 09:24 UTC); 黑夜模式 leaves saturation alone", ok,
                    "sat=" + g.Saturation + " apply=" + g.ApplySaturation + " backend=" + g.SaturationBackend);
            }

            // V43-F2 share codes: TSHADE2 round trip keeps every value; the page's Chinese import reasons for old / foreign / bad codes.
            {
                PilotStyle g = PilotBuiltIns.General(); g.Name = "x"; g.MonitorContrast = 10;
                string code = ShareCode.Export(g), err;
                PilotStyle back = ShareCode.Import(code, out err);
                bool ok = code.StartsWith("TSHADE2:") && back != null && back.SameStyleValues(g) && back.Saturation == 55 && back.ApplySaturation
                    && back.MonitorContrast == 10 && !back.BuiltIn;
                PilotStyle bad = ShareCode.Import("SP3-c3VuMnwxMTV8MzJ8LTIxfDcwfDE0fDE4fDB8MHw2NXwwfDUzfC03MHwyOHw1MA-115A8824", out err);
                ok = ok && bad == null && FilterKeys.ImportReason(err).Contains("ShadePilot");
                bad = ShareCode.Import("TSHADE1:e30=", out err);
                ok = ok && bad == null && FilterKeys.ImportReason(err).Contains("TSHADE1");
                bad = ShareCode.Import("TSHADE2:@@@", out err);
                ok = ok && bad == null && FilterKeys.ImportReason(err).StartsWith("分享码不对");
                Check("V43-F2 TSHADE2 share code round trip (style + saturation + monitor); import refusals in Chinese", ok, code.Substring(0, Math.Min(16, code.Length)));
            }
        }

        // V43-T 「调试滤镜」 bar (src/FilterTune.cs)
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V43TuneInit() { V43TuneTests(); }

        static void V43TuneTests()
        {
            string d = "";
            int tonalMax = PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode), sMax = PilotStyle.StrengthSignedMax(PilotViews.FixedStrengthMode);
            List<PilotStyle> bases = PilotBuiltIns.Create();
            PilotStyle neutral = PilotStyle.CreateNeutral("n"); bases.Add(neutral);
            PilotStyle extreme = PilotStyle.CreateNeutral("x"); extreme.Shadows = 150; extreme.BlackPoint = 70; extreme.Midtones = -70; extreme.Highlights = 0;
            extreme.ShadowRange = 70; extreme.TransitionSoftness = -70; bases.Add(extreme);
            foreach (PilotStyle p in bases)
            {
                PilotStyle c = FilterTune.Apply(p, 0);
                if (!c.SameStyleValues(p) || c.Saturation != p.Saturation || c.ApplySaturation != p.ApplySaturation) d += p.Name + " centre; ";
                double lastMean = -1;
                for (int bar = -100; bar <= 100; bar += 5)
                {
                    PilotStyle s = FilterTune.Apply(p, bar);
                    if (s.Shadows < 0 || s.Shadows > tonalMax || s.Highlights < 0 || s.Highlights > tonalMax) d += p.Name + " tonal range " + bar + "; ";
                    foreach (int v in new[] { s.Midtones, s.BlackPoint, s.ShadowRange, s.TransitionSoftness })
                        if (v < -sMax || v > sMax) d += p.Name + " signed range " + bar + "; ";
                    if (s.Saturation != p.Saturation || s.Temperature != p.Temperature || s.Vibrance != p.Vibrance || s.WhitePoint != p.WhitePoint) d += p.Name + " kept fields " + bar + "; ";
                    ShadeRamp r = PilotCurve.Build(s);
                    double mean = 0;
                    foreach (ushort[] ch in new[] { r.Red, r.Green, r.Blue })
                        for (int i = 1; i < 256; i++) if (ch[i] < ch[i - 1]) { d += p.Name + " not monotonic at bar " + bar + " i " + i + "; "; break; }
                    for (int i = 0; i < 256; i++) mean += r.Green[i];
                    if (p != extreme && lastMean >= 0 && mean + 1e-6 < lastMean) d += p.Name + " darker at " + bar + "; "; // brightness order: real presets
                    lastMean = mean;
                }
                PilotStyle hi = FilterTune.Apply(p, 500), lo = FilterTune.Apply(p, -500);
                if (!hi.SameStyleValues(FilterTune.Apply(p, 100)) || !lo.SameStyleValues(FilterTune.Apply(p, -100))) d += p.Name + " ends clamp; ";
            }
            Check("V43-T1 调试滤镜: centre = preset exactly, every bar position monotonic + brighter to the right, values inside the slider ranges",
                d.Length == 0, d.Length > 600 ? d.Substring(0, 600) : d);
        }

        static string UiSrcDirW2()
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && d != null; i++)
            {
                string c = Path.Combine(d, "..", "tarkov-crosshair-v4", "src");
                if (Directory.Exists(c)) return Path.GetFullPath(c);
                d = Path.GetDirectoryName(d);
            }
            return "/home/dev/tarkov-crosshair-v4/src";
        }
    }
}
