// FilterTutorialModel.cs -- 滤镜教程 (v4.2, W3): steps, texts, slider state, hotkey demo and the preview maths.
// Pure logic (no System.Drawing / WinForms): the view (FilterTutorialView.cs) draws it, the form hosts it, tests check it.
// The preview uses the filter's real curve maths (TarkovShade.PilotCurve, synced into src/Shade by sync-shade.sh) on a
// procedurally drawn scene. Nothing here touches the screen: no gamma write, no screen capture, no game access.
using System;
using System.Collections.Generic;
using TarkovShade;

namespace TarkovCrosshair
{
    /// <summary>Key names shown in the hotkey step. Defaults = the filter's defaults (Leo, 17 Nov 2026: F8 toggle, F7 hold
    /// original, F9 next preset; v4.3: only these three are shown).
    /// The host may pass the user's own keys (names as the 按键 page shows them).</summary>
    public sealed class FilterTutorialKeys
    {
        public const int VkF7 = 0x76, VkF8 = 0x77, VkF9 = 0x78;
        public string Toggle = "F8", HoldOriginal = "F7", NextPreset = "F9";
        /// <summary>Virtual-key codes the tutorial window reacts to while it has focus (0 = only the on-screen chips).</summary>
        public int ToggleVk = VkF8, HoldVk = VkF7, NextVk = VkF9;

        public static FilterTutorialKeys Default() { return new FilterTutorialKeys(); }

        /// <summary>A copy with empty names replaced by the defaults.</summary>
        public FilterTutorialKeys Clean()
        {
            FilterTutorialKeys k = new FilterTutorialKeys();
            if (!string.IsNullOrEmpty(Toggle)) k.Toggle = Toggle.Trim();
            if (!string.IsNullOrEmpty(HoldOriginal)) k.HoldOriginal = HoldOriginal.Trim();
            if (!string.IsNullOrEmpty(NextPreset)) k.NextPreset = NextPreset.Trim();
            k.ToggleVk = ToggleVk; k.HoldVk = HoldVk; k.NextVk = NextVk;
            return k;
        }
    }

    /// <summary>One slider of the tutorial (values are the filter's own slider positions).</summary>
    public sealed class FilterTutSlider
    {
        public int Id;
        public string Label;
        /// <summary>PilotHelpText key (tooltip / label source), "" for the split divider.</summary>
        public string HelpKey;
        public int Min, Max, Neutral;
        public FilterTutSlider(int id, string label, string helpKey, int min, int max, int neutral)
        { Id = id; Label = label; HelpKey = helpKey; Min = min; Max = max; Neutral = neutral; }
    }

    /// <summary>Static texts of one step.</summary>
    public sealed class FilterTutStep
    {
        public string Id, Chapter, Title, Body, Prompt, DonePrompt, Note, NoteTitle;
        public int[] Sliders = new int[0];
        public bool Split, PresetChips, KeyChips;
        /// <summary>Extra lines under the chips (not interactive).</summary>
        public string[] Extra = new string[0];
    }

    public sealed class FilterTutorialModel
    {
        // ---------------------------------------------------------------- slider ids
        public const int SlShadows = 0, SlHighlights = 1, SlSaturation = 2, SlSplit = 3, SlTune = 4;
        // v5.1 (W24): StepTune = the 「调试滤镜」 bar (11 steps) and the 5-point curve of the 滤镜 page
        public const int StepIntro = 0, StepShadows = 1, StepHighlights = 2, StepSaturation = 3, StepPresets = 4, StepTune = 5, StepKeys = 6;
        public const int StepCount = 7;
        /// <summary>v4.3: the 「调试滤镜」 bar on the 滤镜 page, pointed to from the presets step (v5.1: the next step shows it).</summary>
        public const string DebugBarLine = "下一步：「滤镜」页的「调试滤镜」条，往左更暗，往右更亮，在预设的基础上再调。";
        /// <summary>v5.1: the tune step is done once the bar is this many steps from 0 and the split was dragged.</summary>
        public const int TuneMoved = 2;

        // ---------------------------------------------------------------- scene levels (0..255, framebuffer values)
        /// <summary>Wall next to the enemy in the dark corner.</summary>
        public const int CornerLevel = 8;
        /// <summary>The enemy's clothing in the dark corner (almost the same as the wall: hard to see without the filter).</summary>
        public const int EnemyLevel = 16;
        /// <summary>Bright sky in the window.</summary>
        public const int SkyLevel = 244;
        /// <summary>Thresholds for the step goals (output levels, 0..255).</summary>
        public const int EnemySeenContrast = 13, EnemySeenLevel = 28, SkyCalmLevel = 210, SaturationMoved = 12;

        public readonly FilterTutorialKeys Keys;
        public readonly List<FilterTutStep> Steps;
        public readonly FilterTutSlider[] SliderDefs;
        public readonly List<PilotStyle> Presets;

        private int step;
        private readonly int[] value = new int[5];
        private readonly bool[] done = new bool[StepCount];
        private readonly bool[] visited = new bool[StepCount];
        private int presetIndex;
        private bool filterOn = true, holdOriginal;
        private bool splitTouched, sawGeneral, sawNight;
        private bool keyToggled, keyHeld, keyNext;
        private int version;

        public FilterTutorialModel() : this(null) { }

        public FilterTutorialModel(FilterTutorialKeys keys)
        {
            Keys = (keys ?? FilterTutorialKeys.Default()).Clean();
            Presets = PilotBuiltIns.Create();
            int tonal = PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode); // 标准: 0..150
            SliderDefs = new FilterTutSlider[] {
                new FilterTutSlider(SlShadows, PilotHelpText.Label(PilotHelpText.Shadows), PilotHelpText.Shadows, 0, tonal, 0),
                new FilterTutSlider(SlHighlights, PilotHelpText.Label(PilotHelpText.Highlights), PilotHelpText.Highlights, 0, tonal, 0),
                new FilterTutSlider(SlSaturation, PilotHelpText.Label(PilotHelpText.Saturation), PilotHelpText.Saturation,
                    PilotStyle.SaturationMin, PilotStyle.SaturationMax, PilotStyle.SaturationNeutral),
                new FilterTutSlider(SlSplit, "对比线", "", 0, 100, 50),
                new FilterTutSlider(SlTune, "调试滤镜", "", FilterTune.Min, FilterTune.Max, 0) };
            for (int i = 0; i < SliderDefs.Length; i++) value[i] = SliderDefs[i].Neutral;
            Steps = BuildSteps(Keys);
            visited[0] = true;
        }

        private static List<FilterTutStep> BuildSteps(FilterTutorialKeys k)
        {
            List<FilterTutStep> s = new List<FilterTutStep>();
            FilterTutStep a = new FilterTutStep();
            a.Id = "what"; a.Chapter = "作用"; a.Title = "滤镜是做什么的";
            a.Body = "暗处的敌人和墙几乎一样黑，很难看出来。\n滤镜把暗部调亮、把太亮的地方压暗，让人更容易被看见。";
            a.Prompt = "拖动画面中间的对比线"; a.DonePrompt = "左边开了滤镜，右边是原画";
            a.NoteTitle = "说明"; a.Note = "教程里的画面是画出来的，不会改你的屏幕。";
            a.Split = true;
            s.Add(a);

            FilterTutStep b = new FilterTutStep();
            b.Id = "shadows"; b.Chapter = "暗部"; b.Title = "提亮暗部";
            b.Body = "左边的暗处站着一个人。\n把「提亮暗部」往右拖，看他慢慢出现。";
            b.Prompt = "拖动「提亮暗部」，直到看见敌人"; b.DonePrompt = "看见了：暗处的人已经分得出来";
            b.NoteTitle = "提示"; b.Note = "纯黑和亮处基本不变，只有暗部变亮。";
            b.Sliders = new int[] { SlShadows };
            s.Add(b);

            FilterTutStep c = new FilterTutStep();
            c.Id = "highlights"; c.Chapter = "高光"; c.Title = "压低高光";
            c.Body = "窗外的天空和阳光太亮会刺眼。\n把「压低高光」往右拖，亮处会收一点。";
            c.Prompt = "拖动「压低高光」，让窗外不刺眼"; c.DonePrompt = "窗外亮度已经压下来了";
            c.NoteTitle = "提示"; c.Note = "暗部的提亮会保留，两个可以一起用。";
            c.Sliders = new int[] { SlHighlights };
            s.Add(c);

            FilterTutStep d = new FilterTutStep();
            d.Id = "saturation"; d.Chapter = "颜色"; d.Title = "饱和度";
            d.Body = "提亮后颜色会显得发灰。\n「饱和度」50 是不变，往右更浓，往左更淡。";
            d.Prompt = "拖动「饱和度」，看箱子和油桶的颜色"; d.DonePrompt = "颜色浓淡已经变了";
            d.NoteTitle = "提示"; d.Note = "实际颜色效果看显卡和显示器。";
            d.Sliders = new int[] { SlSaturation };
            s.Add(d);

            FilterTutStep e = new FilterTutStep();
            e.Id = "presets"; e.Chapter = "预设"; e.Title = "通用 和 黑夜模式";
            e.Body = "不想自己调，就直接用预设。\n「通用」是开发者自己用的设置，「黑夜模式」暗部提得更多。";
            e.Prompt = "点两个预设，再拖动对比线"; e.DonePrompt = "左边是选中的预设，右边是原画";
            e.NoteTitle = "提示"; e.Note = "「黑夜模式」暗部提得更多，适合夜图。";
            e.Split = true; e.PresetChips = true;
            e.Extra = new string[] { DebugBarLine };
            s.Add(e);

            FilterTutStep t = new FilterTutStep();
            t.Id = "tune"; t.Chapter = "调试"; t.Title = "调试滤镜和曲线";
            t.Body = "选好预设后用「调试滤镜」一格一格调：一共 11 格（-5 到 +5），往右更亮，往左更暗，0 就是预设本来的样子。\n" +
                "「滤镜」页的曲线上有 5 个点：黑位、暗部、中间调、高光、白位，可以直接上下拖，下面的滑块会跟着变。";
            t.Prompt = "把「调试滤镜」拖到 +2 以上或 -2 以下，再拖动对比线"; t.DonePrompt = "左边是调过的预设，右边是原画";
            t.NoteTitle = "提示"; t.Note = "曲线下面实时显示每个点「原来 → 现在」的亮度。拖不动了 = 已到 Windows 允许的最大值，再拉 Windows 会拒绝这条曲线。";
            t.Split = true; t.Sliders = new int[] { SlTune };
            s.Add(t);

            FilterTutStep f = new FilterTutStep();
            f.Id = "keys"; f.Chapter = "按键"; f.Title = "快捷键";
            f.Body = "玩的时候用快捷键，不用切出游戏。";
            f.Prompt = "按 " + k.Toggle + "、按住 " + k.HoldOriginal + "、按 " + k.NextPreset + "（或点右边的按键）";
            f.DonePrompt = "都试过了，可以点「完成」";
            f.NoteTitle = "安全"; f.Note = "只改显示器颜色曲线，不截图、不读游戏。";
            f.KeyChips = true;
            s.Add(f);
            return s;
        }

        // ---------------------------------------------------------------- state
        public int Step { get { return step; } }
        public FilterTutStep Current { get { return Steps[step]; } }
        /// <summary>Changes whenever anything that affects the picture or texts changes (view caches key on it).</summary>
        public int Version { get { return version; } }
        public bool IsDone(int i) { return i >= 0 && i < StepCount && done[i]; }
        public bool Visited(int i) { return i >= 0 && i < StepCount && visited[i]; }
        public bool IsLast { get { return step == StepCount - 1; } }
        public int PresetIndex { get { return presetIndex; } }
        public bool FilterOn { get { return filterOn; } }
        public bool HoldOriginal { get { return holdOriginal; } }
        public bool SplitTouched { get { return splitTouched; } }
        public bool KeyToggled { get { return keyToggled; } }
        public bool KeyHeld { get { return keyHeld; } }
        public bool KeyNext { get { return keyNext; } }

        public int Value(int slider) { return slider >= 0 && slider < value.Length ? value[slider] : 0; }

        public void SetValue(int slider, int v)
        {
            if (slider < 0 || slider >= value.Length) return;
            FilterTutSlider d = SliderDefs[slider];
            if (v < d.Min) v = d.Min;
            if (v > d.Max) v = d.Max;
            if (slider == SlSplit) splitTouched = true;
            if (value[slider] == v) { Evaluate(); return; }
            value[slider] = v;
            version++;
            Evaluate();
        }

        /// <summary>Split position for an animated sweep before the user touches it (does not count as touched).</summary>
        public void AutoSplit(int v)
        {
            if (splitTouched) return;
            if (v < 0) v = 0; if (v > 100) v = 100;
            if (value[SlSplit] != v) { value[SlSplit] = v; version++; }
        }

        public void Go(int i)
        {
            if (i < 0) i = 0;
            if (i >= StepCount) i = StepCount - 1;
            if (i == step) return;
            step = i;
            visited[i] = true;
            if (Current.Split) { splitTouched = false; value[SlSplit] = 50; }
            holdOriginal = false;
            version++;
            Evaluate();
        }

        public bool Next() { if (step >= StepCount - 1) return false; Go(step + 1); return true; }
        public bool Prev() { if (step <= 0) return false; Go(step - 1); return true; }

        public void SelectPreset(int i)
        {
            if (i < 0 || i >= Presets.Count) return;
            presetIndex = i;
            if (i == 0) sawGeneral = true; else sawNight = true;
            version++;
            Evaluate();
        }

        /// <summary>Hotkey demo: toggle (F8).</summary>
        public void PressToggle() { filterOn = !filterOn; keyToggled = true; version++; Evaluate(); }
        /// <summary>Hotkey demo: next preset (F9).</summary>
        public void PressNext() { SelectPreset((presetIndex + 1) % Presets.Count); keyNext = true; Evaluate(); }
        /// <summary>Hotkey demo: hold to see the original (F7 down / up).</summary>
        public void SetHold(bool down)
        {
            if (holdOriginal == down) return;
            holdOriginal = down;
            if (down) keyHeld = true;
            version++;
            Evaluate();
        }

        /// <summary>Key handler for the tutorial window. Returns true when the key belongs to the demo.</summary>
        public bool KeyDown(int vk) { return KeyDown(vk, false); }

        /// <summary>ctrl = Ctrl is held: the demo keys are plain F-keys, so any Ctrl combo is ignored.</summary>
        public bool KeyDown(int vk, bool ctrl)
        {
            if (step != StepKeys || vk == 0) return false;
            if (ctrl) return false;
            if (vk == Keys.ToggleVk) { PressToggle(); return true; }
            if (vk == Keys.NextVk) { PressNext(); return true; }
            if (vk == Keys.HoldVk) { SetHold(true); return true; }
            return false;
        }

        public bool KeyUp(int vk)
        {
            if (step != StepKeys || vk == 0) return false;
            if (vk == Keys.HoldVk) { SetHold(false); return true; }
            return false;
        }

        // ---------------------------------------------------------------- what the preview shows
        /// <summary>The style the tutorial's own sliders make (neutral apart from the sliders).</summary>
        public PilotStyle SliderStyle()
        {
            PilotStyle s = PilotStyle.CreateNeutral("教程");
            s.StrengthMode = PilotViews.FixedStrengthMode;
            s.Shadows = value[SlShadows];
            if (step >= StepHighlights) s.Highlights = value[SlHighlights];
            if (step >= StepSaturation)
            {
                s.Saturation = value[SlSaturation];
                s.ApplySaturation = value[SlSaturation] != PilotStyle.SaturationNeutral;
                s.SaturationBackend = PilotStyle.SaturationBackendSoftware;
            }
            return s;
        }

        /// <summary>Style of the filtered picture for the current step; null = the original picture.</summary>
        public PilotStyle FilteredStyle()
        {
            switch (step)
            {
                case StepIntro: return Presets[0];
                case StepPresets: return Presets[presetIndex];
                case StepTune: return FilterTune.Apply(Presets[presetIndex], value[SlTune]);
                case StepKeys: return filterOn && !holdOriginal ? Presets[presetIndex] : null;
                default: return SliderStyle();
            }
        }

        /// <summary>True when the picture is split (left filtered, right original) at Value(SlSplit) percent.</summary>
        public bool ShowSplit { get { return Current.Split; } }

        // ---------------------------------------------------------------- preview maths
        /// <summary>256-entry lookup per channel from the filter's real ramp (PilotCurve.Build on an identity baseline).
        /// null style = identity.</summary>
        public static void Lut(PilotStyle s, byte[] r, byte[] g, byte[] b)
        {
            if (r == null || g == null || b == null || r.Length < 256 || g.Length < 256 || b.Length < 256)
                throw new ArgumentException("lut arrays need 256 entries");
            if (s == null)
            {
                for (int i = 0; i < 256; i++) { r[i] = (byte)i; g[i] = (byte)i; b[i] = (byte)i; }
                return;
            }
            ShadeRamp ramp = PilotCurve.Build(s, PilotCurve.IdentityChannel(), PilotCurve.IdentityChannel(),
                PilotCurve.IdentityChannel(), true);
            for (int i = 0; i < 256; i++)
            {
                r[i] = ToByte(ramp.Red[i]); g[i] = ToByte(ramp.Green[i]); b[i] = ToByte(ramp.Blue[i]);
            }
        }

        private static byte ToByte(ushort w) { int v = (w + 128) / 257; return (byte)(v > 255 ? 255 : v); }

        /// <summary>Output level of one grey input level through the style's ramp (green channel = luminance curve).</summary>
        public static int OutLevel(PilotStyle s, int level)
        {
            byte[] r = new byte[256], g = new byte[256], b = new byte[256];
            Lut(s, r, g, b);
            if (level < 0) level = 0; if (level > 255) level = 255;
            return g[level];
        }

        /// <summary>Saturation factor of the preview: the Windows software path (factor = value x 2 / 100, ShadePilot
        /// ApplySoftware). 1 when saturation is not applied.</summary>
        public static double SaturationFactor(PilotStyle s)
        {
            if (s == null || !s.ApplySaturation) return 1.0;
            int v = Math.Max(PilotStyle.SaturationMin, Math.Min(PilotStyle.SaturationMax, s.Saturation));
            return v * 2.0 / 100.0;
        }

        /// <summary>One pixel through saturation (Rec.709 luma kept, as the Windows colour matrix does) then the ramp.</summary>
        public static int Apply(int argb, double sat, byte[] lr, byte[] lg, byte[] lb)
        {
            int a = (argb >> 24) & 0xFF, r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
            if (sat != 1.0)
            {
                double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = Clamp255(y + (r - y) * sat);
                g = Clamp255(y + (g - y) * sat);
                b = Clamp255(y + (b - y) * sat);
            }
            return (a << 24) | (lr[r] << 16) | (lg[g] << 8) | lb[b];
        }

        private static int Clamp255(double v) { int i = (int)Math.Round(v); return i < 0 ? 0 : (i > 255 ? 255 : i); }

        /// <summary>Enemy against the wall after the filter, in output levels.</summary>
        public static int EnemyContrast(PilotStyle s) { return OutLevel(s, EnemyLevel) - OutLevel(s, CornerLevel); }

        public static bool EnemySeen(PilotStyle s)
        {
            return EnemyContrast(s) >= EnemySeenContrast && OutLevel(s, EnemyLevel) >= EnemySeenLevel;
        }

        public static bool SkyCalm(PilotStyle s) { return OutLevel(s, SkyLevel) <= SkyCalmLevel; }

        // ---------------------------------------------------------------- goals
        private void Evaluate()
        {
            if (done[step]) return; // a reached goal stays reached (the dot stays filled)
            bool now = false;
            switch (step)
            {
                case StepIntro: now = splitTouched; break;
                case StepShadows: now = EnemySeen(SliderStyle()); break;
                case StepHighlights: now = SkyCalm(SliderStyle()); break;
                case StepSaturation:
                    now = Math.Abs(value[SlSaturation] - PilotStyle.SaturationNeutral) >= SaturationMoved; break;
                case StepPresets: now = sawGeneral && sawNight && splitTouched; break;
                case StepTune: now = Math.Abs(value[SlTune]) >= TuneMoved && splitTouched; break;
                case StepKeys: now = keyToggled && keyHeld && keyNext; break;
            }
            if (now) { done[step] = true; version++; }
        }

        /// <summary>Footer prompt for the current step.</summary>
        public string Prompt { get { return done[step] ? Current.DonePrompt : Current.Prompt; } }

        /// <summary>v5.1: scene tag of the tune step, e.g. 「通用 +3 · 暗部 64→150」 (the curve point that moves most).</summary>
        public string TuneTag()
        {
            PilotStyle p = Presets[presetIndex];
            int v = value[SlTune];
            PilotStyle s = FilterTune.Apply(p, v);
            int i = FilterCurvePoints.Shadow;
            return p.Name + " " + (v > 0 ? "+" + v : v.ToString()) + " · " + FilterCurvePoints.Names[i] + " " + FilterCurvePoints.Inputs[i] + "→" +
                (int)Math.Round(FilterCurvePoints.Output(s, i));
        }

        /// <summary>Hotkey step status line, e.g. 「滤镜：开 · 通用」 / 「按住 F7：原画」.</summary>
        public string KeyStatus()
        {
            if (holdOriginal) return "按住 " + Keys.HoldOriginal + "：原画";
            return "滤镜：" + (filterOn ? "开" : "关") + " · " + Presets[presetIndex].Name;
        }
    }
}
