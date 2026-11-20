// TarkovShade - ShadePilot-compatible picture style ("\u753b\u9762\u98ce\u683c") values.
// Field names, ranges, neutral values and the strength-mode ranges are ported from
// ShadePilot DisplayPreset.cs (commit 2bb77f6): StylePresetData (line 4311), AppSettingsStore
// StrengthMode / SaturationBackend (lines 4604, 4612), ApplyStrengthMode (line 871) and the WPF
// NormalizeStyleState (wpf/MainWindow.Full.cs line 127). Built-in ShadePilot presets: line 4562.
// ShadePilot: https://github.com/BYS-XSQ/ShadePilot, Copyright (c) 2026 BYS-XSQ, MIT License
// (see LICENSE-SHADEPILOT.txt). C# 5; all non-ASCII text uses \u escapes.
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TarkovShade
{
    /// <summary>
    /// One ShadePilot picture style. The 13 style ints are ShadePilot slider positions, passed
    /// unchanged to PilotCurve (which applies ShadePilot's ScalePositive / ScaleSigned).
    /// </summary>
    [DataContract]
    public sealed class PilotStyle
    {
        // ---- ranges (ShadePilot "\u6781\u9650" mode = full range) ----
        public const int TonalMin = 0, TonalMax = 200;          // Shadows, Highlights
        public const int SignedMin = -100, SignedMax = 100;     // the other 11 style sliders
        public const int SaturationMin = 0, SaturationMax = 100, SaturationNeutral = 50;
        public const int HardwareContrastMin = -50, HardwareContrastMax = 50; // WPF ToNeutralZero

        public const int StrengthSoft = 0;      // \u67d4\u548c
        public const int StrengthStandard = 1;  // \u6807\u51c6 (ShadePilot default, line 4672)
        public const int StrengthExtreme = 2;   // \u6781\u9650

        public const int SaturationBackendAuto = 0;     // DDC/CI 0x8A if the monitor has it, else Windows software
        public const int SaturationBackendHardware = 1; // DDC/CI VCP 0x8A only
        public const int SaturationBackendSoftware = 2; // Windows Magnification fullscreen colour matrix
        /// <summary>GPU driver digital vibrance (NVIDIA/AMD). Not in ShadePilot; TarkovShade default (Leo, 17 Nov 2026).</summary>
        public const int SaturationBackendDriver = 3;

        [DataMember(Order = 0)] public string Name { get; set; }

        /// <summary>\u63d0\u4eae\u6697\u90e8 0..200</summary>
        [DataMember(Order = 1)] public int Shadows { get; set; }
        /// <summary>\u538b\u4f4e\u9ad8\u5149 0..200</summary>
        [DataMember(Order = 2)] public int Highlights { get; set; }
        /// <summary>\u4e2d\u95f4\u8c03 -100..100</summary>
        [DataMember(Order = 3)] public int Midtones { get; set; }
        /// <summary>\u9ed1\u4f4d -100..100</summary>
        [DataMember(Order = 4)] public int BlackPoint { get; set; }
        /// <summary>\u767d\u4f4d -100..100</summary>
        [DataMember(Order = 5)] public int WhitePoint { get; set; }
        /// <summary>\u4e2d\u95f4\u5bf9\u6bd4 -100..100</summary>
        [DataMember(Order = 6)] public int ContrastPivot { get; set; }
        /// <summary>\u8272\u6e29 -100..100 (negative = cool/blue, positive = warm)</summary>
        [DataMember(Order = 7)] public int Temperature { get; set; }
        /// <summary>\u8272\u8c03 -100..100 (negative = green, positive = magenta)</summary>
        [DataMember(Order = 8)] public int Tint { get; set; }
        /// <summary>\u81ea\u7136\u8272\u5f69 -100..100 (inside the gamma curve, an approximation; see PILOT-NOTES.md)</summary>
        [DataMember(Order = 9)] public int Vibrance { get; set; }
        /// <summary>\u6574\u4f53\u66dd\u5149 -100..100</summary>
        [DataMember(Order = 10)] public int Exposure { get; set; }
        /// <summary>\u6697\u90e8\u8303\u56f4 -100..100</summary>
        [DataMember(Order = 11)] public int ShadowRange { get; set; }
        /// <summary>\u9ad8\u5149\u8303\u56f4 -100..100</summary>
        [DataMember(Order = 12)] public int HighlightRange { get; set; }
        /// <summary>\u8fc7\u6e21\u67d4\u548c -100..100</summary>
        [DataMember(Order = 13)] public int TransitionSoftness { get; set; }

        /// <summary>
        /// \u5f3a\u5ea6 0 \u67d4\u548c / 1 \u6807\u51c6 / 2 \u6781\u9650. Only limits the slider ranges (ShadePilot never scales
        /// values by strength). ShadePilot keeps this app-wide (AppSettingsStore.StrengthMode),
        /// not per preset; it is stored here for convenience and is NOT part of share codes.
        /// </summary>
        [DataMember(Order = 14)] public int StrengthMode { get; set; }

        /// <summary>
        /// \u9971\u548c\u5ea6 0..100, 50 = neutral, exactly as ShadePilot stores it (StylePresetData.Saturation,
        /// schema 8). Software backend: Windows matrix factor = value*2/100 (WPF ApplySoftware).
        /// Hardware backend: min + value/100*(max-min) on VCP 0x8A (WPF FromNeutral50).
        /// Only applied when ApplySaturation is true.
        /// </summary>
        [DataMember(Order = 15)] public int Saturation { get; set; }
        /// <summary>false = leave the monitor / Windows saturation alone ("do not change").</summary>
        [DataMember(Order = 16)] public bool ApplySaturation { get; set; }
        /// <summary>
        /// 0 Auto / 1 Hardware / 2 Software are ShadePilot's AppSettingsStore.SaturationBackend values;
        /// 3 = Driver vibrance (TarkovShade default). The BASIC view offers Driver vibrance and Windows.
        /// </summary>
        [DataMember(Order = 17)] public int SaturationBackend { get; set; }

        /// <summary>
        /// Hardware (DDC/CI) brightness in the monitor's own units, as ShadePilot's WPF slider
        /// shows it (raw BrightnessMin..BrightnessMax). Not part of ShadePilot presets or share codes.
        /// </summary>
        [DataMember(Order = 18)] public int HardwareBrightness { get; set; }
        [DataMember(Order = 19)] public bool ApplyHardwareBrightness { get; set; }
        /// <summary>
        /// Hardware (DDC/CI) contrast, -50..50 with 0 = middle of the monitor's range
        /// (ShadePilot WPF ToNeutralZero / FromNeutralZero). Not part of presets or share codes.
        /// </summary>
        [DataMember(Order = 20)] public int HardwareContrast { get; set; }
        [DataMember(Order = 21)] public bool ApplyHardwareContrast { get; set; }
        /// <summary>Hardware sharpness (DDC/CI VCP 0x87), raw monitor units. Not in ShadePilot.</summary>
        [DataMember(Order = 24)] public int HardwareSharpness { get; set; }
        [DataMember(Order = 25)] public bool ApplyHardwareSharpness { get; set; }

        /// <summary>True for the shipped presets (\u901a\u7528, \u9ed1\u591c\u6a21\u5f0f).</summary>
        [DataMember(Order = 22)] public bool BuiltIn { get; set; }
        /// <summary>True when the values were never checked in-game (\u9ed1\u591c\u6a21\u5f0f). Show a hint in the UI.</summary>
        [DataMember(Order = 23)] public bool Untested { get; set; }

        /// <summary>Monitor brightness in raw DDC units; null = leave the monitor unchanged. View over HardwareBrightness + ApplyHardwareBrightness.</summary>
        public int? MonitorBrightness
        {
            get { return ApplyHardwareBrightness ? (int?)HardwareBrightness : null; }
            set { ApplyHardwareBrightness = value.HasValue; if (value.HasValue) HardwareBrightness = value.Value; }
        }
        /// <summary>Monitor contrast -50..50 (0 = middle of the monitor's range); null = unchanged.</summary>
        public int? MonitorContrast
        {
            get { return ApplyHardwareContrast ? (int?)HardwareContrast : null; }
            set { ApplyHardwareContrast = value.HasValue; if (value.HasValue) HardwareContrast = value.Value; }
        }
        /// <summary>Monitor sharpness (VCP 0x87) in raw units; null = unchanged.</summary>
        public int? MonitorSharpness
        {
            get { return ApplyHardwareSharpness ? (int?)HardwareSharpness : null; }
            set { ApplyHardwareSharpness = value.HasValue; if (value.HasValue) HardwareSharpness = value.Value; }
        }

        public override string ToString() { return Name; }

        public PilotStyle()
        {
            SetDefaults();
        }

        // DataContractJsonSerializer does not run the constructor; missing fields keep these defaults.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) { SetDefaults(); }

        private void SetDefaults()
        {
            Name = "\u5f53\u524d"; // \u5f53\u524d
            Saturation = SaturationNeutral;
            StrengthMode = StrengthStandard;
            SaturationBackend = SaturationBackendDriver;
        }

        public static PilotStyle CreateNeutral(string name)
        {
            PilotStyle s = new PilotStyle();
            if (!string.IsNullOrEmpty(name)) s.Name = name;
            return s;
        }

        public static int StrengthTonalMax(int mode) { return mode == 0 ? 80 : mode == 1 ? 150 : 200; }
        public static int StrengthSignedMax(int mode) { return mode == 0 ? 40 : mode == 1 ? 70 : 100; }

        /// <summary>
        /// Clamp to the full ranges, like ShadePilot WPF NormalizeStyleState (every preset passes
        /// through it before the gamma ramp is built).
        /// </summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(Name) || Name.Trim().Length == 0) Name = "\u5f53\u524d";
            Shadows = C(Shadows, TonalMin, TonalMax);
            Highlights = C(Highlights, TonalMin, TonalMax);
            Midtones = C(Midtones, SignedMin, SignedMax);
            BlackPoint = C(BlackPoint, SignedMin, SignedMax);
            WhitePoint = C(WhitePoint, SignedMin, SignedMax);
            ContrastPivot = C(ContrastPivot, SignedMin, SignedMax);
            Temperature = C(Temperature, SignedMin, SignedMax);
            Tint = C(Tint, SignedMin, SignedMax);
            Vibrance = C(Vibrance, SignedMin, SignedMax);
            Exposure = C(Exposure, SignedMin, SignedMax);
            ShadowRange = C(ShadowRange, SignedMin, SignedMax);
            HighlightRange = C(HighlightRange, SignedMin, SignedMax);
            TransitionSoftness = C(TransitionSoftness, SignedMin, SignedMax);
            Saturation = C(Saturation, SaturationMin, SaturationMax);
            StrengthMode = C(StrengthMode, StrengthSoft, StrengthExtreme);
            SaturationBackend = C(SaturationBackend, SaturationBackendAuto, SaturationBackendDriver);
            HardwareContrast = C(HardwareContrast, HardwareContrastMin, HardwareContrastMax);
            if (HardwareBrightness < 0) HardwareBrightness = 0;
            if (HardwareSharpness < 0) HardwareSharpness = 0;
        }

        /// <summary>
        /// Clamp the 13 style values to the slider ranges of a strength mode, which is what a
        /// ShadePilot slider does when its range shrinks (ApplyStrengthMode, line 871).
        /// </summary>
        public void ClampToStrength(int mode)
        {
            int t = StrengthTonalMax(mode), s = StrengthSignedMax(mode);
            Shadows = C(Shadows, 0, t);
            Highlights = C(Highlights, 0, t);
            Midtones = C(Midtones, -s, s);
            BlackPoint = C(BlackPoint, -s, s);
            WhitePoint = C(WhitePoint, -s, s);
            ContrastPivot = C(ContrastPivot, -s, s);
            Temperature = C(Temperature, -s, s);
            Tint = C(Tint, -s, s);
            Vibrance = C(Vibrance, -s, s);
            Exposure = C(Exposure, -s, s);
            ShadowRange = C(ShadowRange, -s, s);
            HighlightRange = C(HighlightRange, -s, s);
            TransitionSoftness = C(TransitionSoftness, -s, s);
        }

        /// <summary>True when all 13 values already fit the given strength mode's slider ranges.</summary>
        public bool FitsStrength(int mode)
        {
            PilotStyle c = Clone();
            c.ClampToStrength(mode);
            return c.SameStyleValues(this);
        }

        public bool IsNeutralStyle
        {
            get { return SameStyleValues(CreateNeutral(null)); }
        }

        public bool SameStyleValues(PilotStyle o)
        {
            if (o == null) return false;
            return Shadows == o.Shadows && Highlights == o.Highlights && Midtones == o.Midtones &&
                BlackPoint == o.BlackPoint && WhitePoint == o.WhitePoint && ContrastPivot == o.ContrastPivot &&
                Temperature == o.Temperature && Tint == o.Tint && Vibrance == o.Vibrance &&
                Exposure == o.Exposure && ShadowRange == o.ShadowRange && HighlightRange == o.HighlightRange &&
                TransitionSoftness == o.TransitionSoftness;
        }

        public PilotStyle Clone()
        {
            return (PilotStyle)MemberwiseClone();
        }

        /// <summary>The 13 style values in ShadePilot's share-code / UI order.</summary>
        public int[] ToArray()
        {
            return new int[] { Shadows, Highlights, Midtones, BlackPoint, WhitePoint, ContrastPivot,
                Temperature, Tint, Vibrance, Exposure, ShadowRange, HighlightRange, TransitionSoftness };
        }

        internal static int C(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }

    /// <summary>
    /// BASIC / ADVANCED split of the settings (Leo, 17 Nov 2026). BASIC is shown by default and
    /// holds only what helps competitive visibility most; ADVANCED is behind a toggle that is off by
    /// default. Keys are PilotHelpText keys.
    /// </summary>
    public static class PilotViews
    {
        public const bool AdvancedVisibleByDefault = false;
        /// <summary>Strength is fixed to \u6807\u51c6 in TarkovShade (LEO-DECISIONS.md); the field stays for parity.</summary>
        public const int FixedStrengthMode = PilotStyle.StrengthStandard;

        /// <summary>Basic picture-style sliders, in display order.</summary>
        public static readonly string[] BasicStyle = {
            PilotHelpText.Shadows, PilotHelpText.ShadowRange, PilotHelpText.Highlights,
            PilotHelpText.BlackPoint, PilotHelpText.Midtones };
        /// <summary>Basic colour + monitor rows. HardwareSharpness only when the monitor supports VCP 0x87.</summary>
        public static readonly string[] BasicOther = {
            PilotHelpText.Saturation, PilotHelpText.SaturationBackend,
            PilotHelpText.HardwareBrightness, PilotHelpText.HardwareContrast, PilotHelpText.HardwareSharpness };
        /// <summary>Advanced (toggle, off by default).</summary>
        public static readonly string[] Advanced = {
            PilotHelpText.WhitePoint, PilotHelpText.ContrastPivot, PilotHelpText.Temperature,
            PilotHelpText.HighlightRange, PilotHelpText.TransitionSoftness, PilotHelpText.Vibrance };
        /// <summary>
        /// Removed from the UI and fixed at 0 / 0 / \u6807\u51c6 in TarkovShade presets (LEO-DECISIONS.md,
        /// no SP3 import in the app). Kept as data so the curve math equals ShadePilot. Legacy note: an imported
        /// non-zero Tint/Exposure is kept, applied, and shown read-only in Advanced with
        /// PilotHelpText.RemovedNote. StrengthMode is always FixedStrengthMode.
        /// </summary>
        public static readonly string[] RemovedFromUi = {
            PilotHelpText.Tint, PilotHelpText.Exposure, PilotHelpText.StrengthMode };

        public static bool IsBasic(string key)
        {
            return Array.IndexOf(BasicStyle, key) >= 0 || Array.IndexOf(BasicOther, key) >= 0;
        }

        /// <summary>Removed keys that must still be shown (read-only) because the style uses them.</summary>
        public static List<string> ReadOnlyShown(PilotStyle s)
        {
            List<string> list = new List<string>();
            if (s == null) return list;
            if (s.Tint != 0) list.Add(PilotHelpText.Tint);
            if (s.Exposure != 0) list.Add(PilotHelpText.Exposure);
            return list;
        }
    }

    /// <summary>Built-in ShadePilot-format styles: exactly two, 通用 + 黑夜模式 (Leo, 17 Nov 2026; again v5.1 round 2).</summary>
    public static class PilotBuiltIns
    {
        public const string GeneralName = "\u901a\u7528";        // \u901a\u7528
        public const string NightName = "\u9ed1\u591c\u6a21\u5f0f";  // \u9ed1\u591c\u6a21\u5f0f
        /// <summary>Kept for old callers; the sun2 values are now the \u901a\u7528 preset.</summary>
        public const string Sun2Name = GeneralName;

        /// <summary>
        /// \u901a\u7528 = Leo's ShadePilot preset "sun2" (strength \u6807\u51c6), values exactly as Leo gave them;
        /// confirmed by his share code SP3-c3VuMnwxMTV8...-115A8824 (\u9971\u548c\u5ea6 50 = neutral).
        /// Saturation 50 is neutral; hardware brightness/contrast/sharpness are not in share codes, so this
        /// preset leaves them and the saturation backend unchanged (ApplySaturation / ApplyHardware* = false).
        /// </summary>
        public static PilotStyle General()
        {
            PilotStyle s = Make(GeneralName, 115, 32, -21, 70, 14, 18, 0, 0, 65, 0, 53, -70, 28);
            s.Untested = false;
            // Leo 17 Nov 09:24 UTC: digital vibrance +5 = Saturation 55, applied through the GPU driver.
            s.Saturation = 55; s.ApplySaturation = true; s.SaturationBackend = PilotStyle.SaturationBackendDriver;
            return s;
        }

        /// <summary>Same as General() (sun2 values).</summary>
        public static PilotStyle Sun2() { return General(); }

        /// <summary>
        /// \u9ed1\u591c\u6a21\u5f0f: a starting point for night maps, values as Leo specified (all inside the
        /// \u6807\u51c6 slider ranges 0..150 / +-70, so no clamping was needed). UNTESTED in-game.
        /// Saturation / hardware values: do not change.
        /// </summary>
        public static PilotStyle Night()
        {
            PilotStyle s = Make(NightName, 135, 60, -10, 30, 10, 30, 0, 0, 65, 0, 50, -50, 35); // LABS-REPORT.md RECOMMENDED (17 Nov 2026)
            s.Untested = true;
            return s;
        }

        /// <summary>
        /// v5.1 round 2 (Leo, 20 Nov 2026, R5): the built-ins are only \u901a\u7528 and \u9ed1\u591c\u6a21\u5f0f again. An early v5.1 build
        /// also shipped \u767d\u5929 / \u9ec4\u660f / \u591c\u56fe / \u5ba4\u5185 / \u5b9e\u9a8c\u5ba4 as built-ins; a preset file it wrote marks them
        /// BuiltIn = true. PilotPresetStore.Load drops those entries (a user preset with the same name has BuiltIn = false and stays).
        /// </summary>
        public static readonly string[] RetiredV51Names = {
            "\u767d\u5929", "\u9ec4\u660f", "\u591c\u56fe", "\u5ba4\u5185", "\u5b9e\u9a8c\u5ba4" };

        public static bool IsRetiredV51(string name) { return name != null && Array.IndexOf(RetiredV51Names, name) >= 0; }

        /// <summary>The two built-ins: \u901a\u7528, \u9ed1\u591c\u6a21\u5f0f.</summary>
        public static List<PilotStyle> Create()
        {
            List<PilotStyle> list = new List<PilotStyle>();
            list.Add(General());
            list.Add(Night());
            return list;
        }

        /// <summary>
        /// ShadePilot's own six shipped presets (DisplayPreset.cs line 4562-4567). NOT built-ins here;
        /// kept for parity tests and as reference values.
        /// </summary>
        public static List<PilotStyle> ShadePilotShipped()
        {
            List<PilotStyle> list = new List<PilotStyle>();
            list.Add(Make("\u6697\u57df\u641c\u5bfb", 105, 35, 18, -8, -5, 8, -6, 0, 16, 8, 24, 0, 14));
            list.Add(Make("\u51b7\u8c03\u7535\u5f71", 28, 46, -4, -7, -9, 16, -18, 7, -5, -3, -7, 15, 11));
            list.Add(Make("\u80f6\u7247\u67d4\u548c", 24, 54, 7, 7, -13, -7, 10, 5, -3, 3, 7, 22, 26));
            list.Add(Make("\u591c\u666f\u7070\u84dd", 68, 42, 14, -5, -7, 11, -28, -4, 18, 6, 20, 7, 14));
            list.Add(Make("\u6696\u9633\u7167\u7247", 32, 26, 8, -3, 3, 7, 22, 6, 24, 4, 7, -3, 10));
            list.Add(Make("\u4e2d\u6027\u901a\u900f", 20, 28, 5, -4, 0, 10, 0, 0, 16, 3, 7, 3, 7));
            return list;
        }

        public static PilotStyle Find(string name)
        {
            foreach (PilotStyle s in Create())
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        private static PilotStyle Make(string name, int sh, int hi, int mid, int bp, int wp, int cp,
            int temp, int tint, int vib, int exp, int sr, int hr, int soft)
        {
            PilotStyle s = new PilotStyle();
            s.Name = name;
            s.BuiltIn = true;
            s.Shadows = sh; s.Highlights = hi; s.Midtones = mid; s.BlackPoint = bp; s.WhitePoint = wp;
            s.ContrastPivot = cp; s.Temperature = temp; s.Tint = tint; s.Vibrance = vib; s.Exposure = exp;
            s.ShadowRange = sr; s.HighlightRange = hr; s.TransitionSoftness = soft;
            s.StrengthMode = PilotStyle.StrengthStandard;
            s.Saturation = PilotStyle.SaturationNeutral; s.ApplySaturation = false;
            s.SaturationBackend = PilotStyle.SaturationBackendDriver;
            s.ApplyHardwareBrightness = false; s.ApplyHardwareContrast = false; s.ApplyHardwareSharpness = false;
            return s;
        }
    }
}
