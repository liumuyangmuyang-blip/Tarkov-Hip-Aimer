// V42-W3 tests for the 滤镜教程 model (src/FilterTutorialModel.cs) against the synced filter maths (src/Shade).
// Run: cd tests-v42-filtertut && dotnet run      (exit code = number of failures)
using System;
using System.Collections.Generic;
using System.IO;
using TarkovShade;

namespace TarkovCrosshair.FilterTutTests
{
    public static class Program
    {
        static int pass, fail;
        const string Src = "/home/dev/tarkov-crosshair-v4/src";

        static void T(bool ok, string name)
        {
            if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + name); }
        }

        static PilotStyle Neutral() { PilotStyle s = PilotStyle.CreateNeutral("n"); s.StrengthMode = PilotStyle.StrengthStandard; return s; }

        public static int Main(string[] args)
        {
            Lut(); Goals(); Saturation(); Flow(); Keys(); Texts(); NoScreenAccess();
            Console.WriteLine("filter tutorial tests: " + pass + " passed, " + fail + " failed");
            return fail;
        }

        static void Lut()
        {
            byte[] r = new byte[256], g = new byte[256], b = new byte[256];
            FilterTutorialModel.Lut(Neutral(), r, g, b);
            bool id = true;
            for (int i = 0; i < 256; i++) if (r[i] != i || g[i] != i || b[i] != i) id = false;
            T(id, "neutral style = identity LUT");
            FilterTutorialModel.Lut(null, r, g, b);
            T(r[37] == 37 && b[200] == 200, "null style = identity LUT");

            // the preview LUT is the engine's ramp (PilotCurve.Build, identity baseline, strength clamp) rounded to 8 bits
            foreach (PilotStyle s in PilotBuiltIns.Create())
            {
                FilterTutorialModel.Lut(s, r, g, b);
                ushort[] idc = PilotCurve.IdentityChannel();
                ShadeRamp ramp = PilotCurve.Build(s, idc, idc, idc, true);
                bool same = true;
                for (int i = 0; i < 256; i++)
                {
                    if (r[i] != (int)Math.Round(ramp.Red[i] / 257.0, MidpointRounding.AwayFromZero) ||
                        g[i] != (int)Math.Round(ramp.Green[i] / 257.0, MidpointRounding.AwayFromZero) ||
                        b[i] != (int)Math.Round(ramp.Blue[i] / 257.0, MidpointRounding.AwayFromZero)) same = false;
                }
                T(same, "preview LUT = PilotCurve ramp for " + s.Name);
                bool mono = true;
                for (int i = 1; i < 256; i++) if (g[i] < g[i - 1]) mono = false;
                T(mono, "LUT monotonic for " + s.Name);
            }
            bool threw = false;
            try { FilterTutorialModel.Lut(Neutral(), new byte[10], g, b); } catch (ArgumentException) { threw = true; }
            T(threw, "short LUT array rejected");
        }

        static void Goals()
        {
            PilotStyle s = Neutral();
            T(!FilterTutorialModel.EnemySeen(s), "enemy hidden without the filter");
            T(FilterTutorialModel.EnemyContrast(s) == FilterTutorialModel.EnemyLevel - FilterTutorialModel.CornerLevel, "neutral contrast = scene contrast");
            s.Shadows = 30; T(!FilterTutorialModel.EnemySeen(s), "enemy still hidden at 提亮暗部 30");
            s.Shadows = 105; T(FilterTutorialModel.EnemySeen(s), "enemy seen at 提亮暗部 105 (70 %)");
            s.Shadows = 150; T(FilterTutorialModel.EnemySeen(s), "enemy seen at 提亮暗部 max");
            foreach (PilotStyle p in PilotBuiltIns.Create()) T(FilterTutorialModel.EnemySeen(p), "enemy seen with preset " + p.Name);
            int prev = -1; bool grows = true;
            for (int v = 0; v <= 150; v += 10)
            {
                s.Shadows = v; int c = FilterTutorialModel.EnemyContrast(s);
                if (c < prev) grows = false; prev = c;
            }
            T(grows, "enemy contrast never drops as 提亮暗部 rises");
            PilotStyle h = Neutral();
            T(!FilterTutorialModel.SkyCalm(h), "sky glaring without the filter");
            h.Highlights = 60; T(FilterTutorialModel.SkyCalm(h), "sky calm at 压低高光 60");
            T(FilterTutorialModel.OutLevel(h, 255) < 255, "highlight compression lowers white");
        }

        static void Saturation()
        {
            PilotStyle s = Neutral();
            T(FilterTutorialModel.SaturationFactor(s) == 1.0, "saturation not applied = factor 1");
            T(FilterTutorialModel.SaturationFactor(null) == 1.0, "null style = factor 1");
            s.ApplySaturation = true; s.Saturation = 50; T(Math.Abs(FilterTutorialModel.SaturationFactor(s) - 1.0) < 1e-9, "50 = factor 1");
            s.Saturation = 100; T(Math.Abs(FilterTutorialModel.SaturationFactor(s) - 2.0) < 1e-9, "100 = factor 2 (ShadePilot software path)");
            s.Saturation = 0; T(FilterTutorialModel.SaturationFactor(s) == 0.0, "0 = factor 0");
            byte[] id = new byte[256]; for (int i = 0; i < 256; i++) id[i] = (byte)i;
            int px = unchecked((int)0xFF40703A);
            T(FilterTutorialModel.Apply(px, 1.0, id, id, id) == px, "factor 1 + identity LUT keeps the pixel");
            int grey = unchecked((int)0xFF808080);
            T(FilterTutorialModel.Apply(grey, 2.0, id, id, id) == grey, "grey stays grey at factor 2");
            int sat = FilterTutorialModel.Apply(px, 1.6, id, id, id);
            int g0 = (px >> 8) & 0xFF, r0 = (px >> 16) & 0xFF, g1 = (sat >> 8) & 0xFF, r1 = (sat >> 16) & 0xFF;
            T(g1 - r1 > g0 - r0, "more saturation spreads the channels");
            T(((sat >> 24) & 0xFF) == 0xFF, "alpha kept");
        }

        static void Flow()
        {
            FilterTutorialModel m = new FilterTutorialModel();
            T(m.Steps.Count == FilterTutorialModel.StepCount && m.Steps.Count == 7, "7 steps (v5.1: 调试滤镜和曲线)");
            T(m.Step == 0 && !m.Prev(), "starts at step 1, no previous");
            T(m.Presets.Count == 2 && m.Presets[0].Name == PilotBuiltIns.GeneralName && m.Presets[1].Name == PilotBuiltIns.NightName, "presets = 通用, 黑夜模式 (v5.1 R5)");
            T(m.FilteredStyle() != null && m.FilteredStyle().Name == PilotBuiltIns.GeneralName, "intro filters with 通用");
            T(m.ShowSplit, "intro shows the split");
            m.AutoSplit(20);
            T(m.Value(FilterTutorialModel.SlSplit) == 20 && !m.IsDone(0), "auto sweep moves the split but is not the user's goal");
            m.SetValue(FilterTutorialModel.SlSplit, 40);
            T(m.IsDone(0) && m.SplitTouched, "dragging the split finishes step 1");
            m.AutoSplit(80); T(m.Value(FilterTutorialModel.SlSplit) == 40, "auto sweep stops once touched");

            m.Next(); T(m.Step == FilterTutorialModel.StepShadows && !m.ShowSplit, "step 2 has no split");
            T(m.FilteredStyle().Shadows == 0, "step 2 starts neutral");
            m.SetValue(FilterTutorialModel.SlShadows, 999);
            T(m.Value(FilterTutorialModel.SlShadows) == PilotStyle.StrengthTonalMax(PilotStyle.StrengthStandard), "slider clamps to the 标准 max");
            T(m.IsDone(1), "step 2 done at max lift");
            m.SetValue(FilterTutorialModel.SlShadows, 0);
            T(m.IsDone(1), "a reached goal stays reached");
            m.SetValue(FilterTutorialModel.SlShadows, 105);
            m.Next(); T(m.FilteredStyle().Shadows == 105 && m.FilteredStyle().Highlights == 0, "step 3 keeps the lift, starts without compression");
            m.SetValue(FilterTutorialModel.SlHighlights, -5); T(m.Value(FilterTutorialModel.SlHighlights) == 0, "slider clamps at 0");
            m.SetValue(FilterTutorialModel.SlHighlights, 60); T(m.IsDone(2), "step 3 done");
            m.Next();
            T(!m.FilteredStyle().ApplySaturation, "saturation untouched at 50");
            m.SetValue(FilterTutorialModel.SlSaturation, 55); T(!m.IsDone(3), "a small nudge is not enough");
            m.SetValue(FilterTutorialModel.SlSaturation, 80); T(m.IsDone(3) && m.FilteredStyle().ApplySaturation, "step 4 done, saturation applied");
            m.Next();
            T(m.ShowSplit && m.Value(FilterTutorialModel.SlSplit) == 50 && !m.SplitTouched, "step 5 split reset");
            m.SelectPreset(1); m.SelectPreset(0); T(!m.IsDone(4), "presets alone are not enough");
            m.SetValue(FilterTutorialModel.SlSplit, 70); T(m.IsDone(4), "step 5 done");
            m.SelectPreset(99); T(m.PresetIndex == 0, "bad preset index ignored");
            // v5.1 (W24): 调试滤镜和曲线 step
            m.Next(); T(m.Step == FilterTutorialModel.StepTune && m.ShowSplit && !m.SplitTouched && m.Value(FilterTutorialModel.SlTune) == 0, "step 6 = tune, split reset, bar at 0");
            T(m.FilteredStyle().SameStyleValues(m.Presets[0]), "tune 0 = the preset exactly");
            m.SetValue(FilterTutorialModel.SlTune, 99); T(m.Value(FilterTutorialModel.SlTune) == FilterTune.Max && FilterTune.Max == 5, "bar clamps at +5");
            m.SetValue(FilterTutorialModel.SlTune, -99); T(m.Value(FilterTutorialModel.SlTune) == -5, "bar clamps at -5");
            m.SetValue(FilterTutorialModel.SlTune, 1); m.SetValue(FilterTutorialModel.SlSplit, 30); T(!m.IsDone(FilterTutorialModel.StepTune), "one step is not enough");
            m.SetValue(FilterTutorialModel.SlTune, 3); T(m.IsDone(FilterTutorialModel.StepTune), "step 6 done at +3 + split");
            T(m.FilteredStyle().Shadows > m.Presets[0].Shadows || m.FilteredStyle().Shadows == PilotStyle.StrengthTonalMax(PilotStyle.StrengthStandard), "+3 lifts the shadows");
            T(m.TuneTag().Contains("+3") && m.TuneTag().Contains("暗部") && m.TuneTag().Contains(PilotBuiltIns.GeneralName), "tune tag: 「通用 +3 · 暗部 64→…」");
            m.Next(); T(m.IsLast && !m.Next(), "step 7 is last");
            m.Go(-3); T(m.Step == 0, "Go clamps low");
            m.Go(99); T(m.Step == FilterTutorialModel.StepKeys, "Go clamps high");
            int v = m.Version; m.Go(1); T(m.Version != v && m.Visited(1), "Go bumps the version");
            T(m.Prompt == m.Current.DonePrompt, "done step shows the done prompt");
        }

        static void Keys()
        {
            FilterTutorialModel m = new FilterTutorialModel();
            T(!m.KeyDown(FilterTutorialKeys.VkF8, false), "hotkeys ignored before the keys step");
            m.Go(FilterTutorialModel.StepKeys);
            T(m.FilterOn && m.FilteredStyle() != null, "filter on at start");
            T(m.KeyDown(FilterTutorialKeys.VkF8, false) && !m.FilterOn && m.FilteredStyle() == null, "F8 turns it off");
            m.KeyDown(FilterTutorialKeys.VkF8, false); T(m.FilterOn, "F8 again turns it on");
            T(m.KeyDown(FilterTutorialKeys.VkF7, false) && m.HoldOriginal && m.FilteredStyle() == null, "hold F7 = original");
            T(m.KeyUp(FilterTutorialKeys.VkF7) && !m.HoldOriginal && m.FilteredStyle() != null, "release F7 = filter back");
            T(!m.IsDone(FilterTutorialModel.StepKeys), "not done before F9");
            m.KeyDown(FilterTutorialKeys.VkF9, false); T(m.PresetIndex == 1, "F9 next preset");
            T(m.IsDone(FilterTutorialModel.StepKeys), "keys step done after F8, F7, F9");
            m.KeyDown(FilterTutorialKeys.VkF9, false); T(m.PresetIndex == 0, "F9 wraps");
            T(!m.KeyDown(0x31, true) && !m.KeyDown(0x32, true) && m.PresetIndex == 0, "Ctrl+1 / Ctrl+2 do nothing (v4.3)");
            T(!m.KeyDown(0x75, true), "Ctrl+F6 does nothing (v4.3)");
            T(!m.KeyDown(FilterTutorialKeys.VkF8, true), "Ctrl+F8 ignored");
            T(m.KeyStatus().Contains("开") && m.KeyStatus().Contains(PilotBuiltIns.GeneralName), "status line");
            m.SetHold(true); T(m.KeyStatus().Contains("F7"), "status while holding");
            m.Go(0); T(!m.HoldOriginal, "leaving the step releases the hold");

            FilterTutorialKeys k = new FilterTutorialKeys();
            k.Toggle = " F10 "; k.HoldOriginal = ""; k.NextPreset = null;
            FilterTutorialModel c = new FilterTutorialModel(k);
            FilterTutStep st = c.Steps[FilterTutorialModel.StepKeys];
            T(c.Keys.Toggle == "F10" && c.Keys.HoldOriginal == "F7" && c.Keys.NextPreset == "F9", "custom key names cleaned, empty = default");
            T(st.Prompt.Contains("F10") && st.Prompt.Contains("F7") && st.Prompt.Contains("F9"), "prompt uses the key names");
        }

        static readonly string[] Banned = { "封号", "封禁", "风险", "保证", "作弊", "外挂", "TODO", "UNVERIFIED" };

        static void Texts()
        {
            FilterTutorialModel m = new FilterTutorialModel();
            List<string> all = new List<string>();
            foreach (FilterTutStep s in m.Steps)
            {
                T(!string.IsNullOrEmpty(s.Title) && !string.IsNullOrEmpty(s.Body) && !string.IsNullOrEmpty(s.Prompt) &&
                  !string.IsNullOrEmpty(s.DonePrompt) && !string.IsNullOrEmpty(s.Note) && !string.IsNullOrEmpty(s.NoteTitle), "texts present: " + s.Id);
                all.Add(s.Title); all.Add(s.Body); all.Add(s.Prompt); all.Add(s.DonePrompt); all.Add(s.Note); all.Add(s.NoteTitle);
                all.AddRange(s.Extra);
            }
            string joined = string.Join("\n", all.ToArray());
            foreach (string b in Banned) T(!joined.Contains(b), "no banned word " + b);
            FilterTutStep keys = m.Steps[FilterTutorialModel.StepKeys];
            string kt = keys.Prompt + string.Join(" ", keys.Extra);
            T(kt.Contains("F8") && kt.Contains("F7") && kt.Contains("F9"), "keys step names F8 / F7 / F9");
            T(!joined.Contains("Ctrl") && !joined.Contains("F6") && !joined.Contains("F12"), "no Ctrl+1 / Ctrl+2 / Ctrl+F6 / F12 anywhere (v4.3)");
            T(keys.Extra.Length == 0, "keys step: only F8 / F7 / F9");
            FilterTutStep pr = m.Steps[FilterTutorialModel.StepPresets];
            T(Array.IndexOf(pr.Extra, FilterTutorialModel.DebugBarLine) >= 0 && FilterTutorialModel.DebugBarLine.Contains("调试滤镜") &&
              FilterTutorialModel.DebugBarLine.Contains("往左更暗") && FilterTutorialModel.DebugBarLine.Contains("往右更亮"), "presets step: 调试滤镜 line");
            foreach (string f in Directory.GetFiles(Src, "FilterTutorial*.cs"))
            { string txt = File.ReadAllText(f); T(!txt.Contains("Ctrl+1") && !txt.Contains("Ctrl+2") && !txt.Contains("Ctrl+F6"), Path.GetFileName(f) + " has no Ctrl+1/2/F6"); }
            T(keys.Note.Contains("不截图") && keys.Note.Contains("不读游戏"), "safety note");
            T(m.SliderDefs[FilterTutorialModel.SlShadows].Label == PilotHelpText.Label(PilotHelpText.Shadows) &&
              m.SliderDefs[FilterTutorialModel.SlHighlights].Label == PilotHelpText.Label(PilotHelpText.Highlights) &&
              m.SliderDefs[FilterTutorialModel.SlSaturation].Label == PilotHelpText.Label(PilotHelpText.Saturation), "slider labels = the filter page's labels");
            T(m.Steps[1].Body.Contains(PilotHelpText.Label(PilotHelpText.Shadows)) && m.Steps[2].Body.Contains(PilotHelpText.Label(PilotHelpText.Highlights)), "bodies name the real sliders");
            T(joined.Contains(PilotBuiltIns.GeneralName) && joined.Contains(PilotBuiltIns.NightName), "preset names");
            foreach (string tn in PilotBuiltIns.RetiredV51Names) T(!joined.Contains(tn) || tn == "夜图", "no retired preset " + tn + " in the tutorial");
            FilterTutStep tu = m.Steps[FilterTutorialModel.StepTune];
            T(tu.Body.Contains("11 格") && tu.Body.Contains("-5") && tu.Body.Contains("+5"), "tune step: 11 steps -5..+5");
            foreach (string nm in FilterCurvePoints.Names) T(tu.Body.Contains(nm), "tune step names curve point " + nm);
            T(tu.Note.Contains("Windows 允许的最大值"), "tune step explains the clamp");
            T(!joined.Contains("直方图"), "no histogram mentioned as a feature");
        }

        static void NoScreenAccess()
        {
            string[] forbidden = { "SetDeviceGammaRamp", "GetDeviceGammaRamp", "ShadeEngine", "GammaRampController", "Win32Gamma",
                "CopyFromScreen", "GetDC(", "BitBlt", "Magnification", "VibranceController", "DdcCi", "Process.GetProcesses" };
            string[] files = Directory.GetFiles(Src, "FilterTutorial*.cs");
            T(files.Length == 4, "4 tutorial files (model, scene, view, form)");
            foreach (string f in files)
            {
                string txt = File.ReadAllText(f);
                foreach (string w in forbidden) T(!txt.Contains(w), Path.GetFileName(f) + " does not use " + w);
            }
        }
    }
}
