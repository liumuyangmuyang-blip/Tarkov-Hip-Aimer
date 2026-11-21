// v5.0 (W12): the built-in default = profile 「Leo默认 (旧)」 of Leo's v5 settings.json; first-run guidance dialog; v5.0.
using System;
using System.Collections.Generic;
using System.IO;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V50Init() { V50Tests(); }

        static void V50Tests()
        {
            Profile want = V50Expected();
            // V50-1 fresh default profile values == the file's 「Leo默认 (旧)」 values (all fields, name aside)
            {
                Profile got = Profile.CreateLeoDefault("Leo默认 (旧)");
                List<string> d = new List<string>(); int n = 0;
                DeepEq(got, want, "$", d, ref n);
                Profile fresh = GlobalSettings.CreateDefault().Active;
                List<string> d2 = new List<string>(); int n2 = 0;
                Profile f2 = fresh.CloneValues(); f2.Name = want.Name;
                DeepEq(f2, want, "$", d2, ref n2);
                bool name = Profile.CreateLeoDefault("abc").Name == "abc" && fresh.Name == GlobalSettings.DefaultProfileName;
                Check("V50-1 fresh default profile = Leo's v5 file 「Leo默认 (旧)」 values (CreateLeoDefault + CreateDefault), name = the requested one",
                    d.Count == 0 && d2.Count == 0 && n > 80 && name, n + " values, diffs " + DiffStr(d) + " / " + DiffStr(d2) + " name=" + name);
            }
            // V50-2 the embedded text is exactly Leo's file; copies are independent; fallback exists
            {
                string file = File.ReadAllText(Fixture("leo-v50-1118-settings.v50")).TrimStart('\uFEFF').Replace("\r\n", "\n");
                // v5.1.1 (X5, Leo 21 Nov 2026): Leo's file except 「Leo默认 (旧)」's 上抬 / 站立 / Dirs / HandCustom (his 21 Nov values)
                System.Text.Json.Nodes.JsonNode jt = System.Text.Json.Nodes.JsonNode.Parse(LeoDefaultJson.Text), jf = System.Text.Json.Nodes.JsonNode.Parse(file);
                // Leo 21 Nov 2026 tuned default (X18): the whole 「Leo默认 (旧)」 profile is his tuned one (checked by V50-1 / X18-1)
                // and Autostart is off; the rest of the text is still his 18 Nov file
                foreach (System.Text.Json.Nodes.JsonNode j in new System.Text.Json.Nodes.JsonNode[] { jt, jf })
                {
                    j.AsObject().Remove("Autostart");
                    System.Text.Json.Nodes.JsonArray arr = j["Profiles"].AsArray();
                    for (int k = arr.Count - 1; k >= 0; k--) if ((string)arr[k]["Name"] == "Leo默认 (旧)") arr.RemoveAt(k);
                }
                bool same = System.Text.Json.Nodes.JsonNode.DeepEquals(jt, jf) && LeoDefaultJson.Text != file;
                Profile a = Profile.CreateLeoDefault("a"), b = Profile.CreateLeoDefault("b");
                a.Dirs[2].OffX = 1; a.Boost.Diameter = 99; a.Jump.TotalMs = 1234;
                bool indep = b.Dirs[2].OffX == 70.92 /* Leo 21 Nov 2026 tuned default: from the 站立 point 26.28 */ && b.Boost.Diameter == 15 && b.Jump.TotalMs == 800;
                Profile legacy = Profile.CreateLeoDefaultLegacy("l");
                bool leg = legacy.Dirs[2].OffX == 75 && legacy.Shape == "ring" && legacy.HandSwitchMs == 400;
                GlobalSettings g = GlobalSettings.CreateDefault();
                bool screen = g.ResAspect == "auto" && g.ResFov == 75 && !g.Autostart /* Leo 21 Nov 2026 tuned default: autostart off */ && g.ResBaseW == 2160 && g.ResBaseH == 1440 && g.ResBaseFov == 75
                    && g.MonitorIndex == -1 && g.GameWindowTitle == GlobalSettings.DefaultGameWindowTitle && !g.TutorialDone;
                Check("V50-2 LeoDefaultJson = Leo's file byte for byte; each call a deep copy; legacy fallback = v3.1 values; screen settings keep the first-run rules (画面比例 auto, FOV 75, autostart off, base 2160×1440 / 75)",
                    same && indep && leg && screen, "same=" + same + " indep=" + indep + " legacy=" + leg + " screen=" + screen);
            }
            // V50-3 first-run guidance dialog after the welcome, button to 常规; README + guide carry the 3 steps; Version v5.0
            {
                string sf = File.ReadAllText(Path.Combine(UiSrcDir(), "SettingsForm.cs"));
                string readme = File.ReadAllText(Path.Combine(UiSrcDir(), "..", "说明 README.txt"));
                string readme2 = File.ReadAllText(Path.Combine(UiSrcDir(), "..", "README.txt"));
                string guide = File.ReadAllText(Path.Combine(UiSrcDir(), "GuideText.cs"));
                bool dlg = sf.Contains("ImportLegacy();\n            OfferFirstSetup();") || sf.Contains("ImportLegacy();\r\n            OfferFirstSetup();")
                    || sf.Contains("ImportLegacy();\n            if (G.WizardDone) OfferFirstSetup();") || sf.Contains("ImportLegacy();\r\n            if (G.WizardDone) OfferFirstSetup();"); // v5.1 (W34): decision 13 -- the 首次设置向导 asks the same 3 values, so the dialog only follows the welcome once the wizard is done
                // v5.0.2 (W17, UI L10): the page is named as in the navigation, 「分辨率 · 常规」
                dlg = dlg && sf.Contains("ThemedDialog.Show(this, FirstSetupText, FirstSetupButton, \"稍后\")") && sf.Contains("FirstSetupButton = \"去「分辨率 · 常规」页\"") && sf.Contains("JumpToPage(\"常规\")")
                    && sf.Contains("先在「分辨率 · 常规」页设好和游戏里一样的 3 项：游戏分辨率、画面比例、FOV（建议 75）。然后进游戏实测，觉得有偏差再细调各项偏移。");
                bool rd = readme == readme2 && readme.Contains("第一次用，先做这 3 步：") && readme.IndexOf("第一次用，先做这 3 步") < readme.IndexOf("【这是什么】")
                    && guide.Contains("第一次用，先做这 3 步：") && readme.Contains("v5.0") && !readme.Contains("v4.3.1");
                bool ver = File.ReadAllText(Path.Combine(UiSrcDir(), "Program.cs")).Contains("public const string Version = \"v5.2\";"); // v5.1 (W25)
                Check("V50-3 first-run guidance dialog after the welcome (button 去「常规」页), README × 2 + 指南 start with the 3 steps, Version v5.0",
                    dlg && rd && ver, "dialog=" + dlg + " readme=" + rd + " version=" + ver);
            }
        }
    }
}
