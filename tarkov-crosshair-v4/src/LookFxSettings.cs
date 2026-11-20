// v5.1.1 (X3, Leo round B): the stored settings of the 弹药提示 bar (look, 只在少于 X% 时显示, low-ammo warnings, reload
// animations for 普通 / 快速换弹) and of the 准星动态 effects (gap amounts, 开火波纹, 开火渐变色, 强度). Plain DataContract
// classes held by GlobalSettings (AmmoBar, AnimFx). Absent in an older file -> defaults. Only System + serialization.
using System;
using System.Runtime.Serialization;

namespace TarkovCrosshair
{
    internal static class FxClamp
    {
        public static int I(int v, int lo, int hi, int def) { return v < lo || v > hi ? def : v; }
        public static double D(double v, double lo, double hi, double def) { return double.IsNaN(v) || double.IsInfinity(v) || v < lo || v > hi ? def : v; }
        public static string Col(string v, string def) { int r, g, b; return ColorUtil.TryParse(v, out r, out g, out b) ? v.Trim().ToUpperInvariant() : def; }
        public static string One(string v, string[] ids, string def)
        {
            if (v != null) { string t = v.Trim().ToLowerInvariant(); foreach (string s in ids) if (s == t) return s; }
            return def;
        }
        public static int Rgb(string hex, int def) { int r, g, b; return ColorUtil.TryParse(hex, out r, out g, out b) ? (r << 16) | (g << 8) | b : def; }
    }

    /// <summary>One low-ammo warning level (警告 / 紧急警告).</summary>
    [DataContract]
    public sealed class AmmoWarnSettings
    {
        public static readonly string[] Styles = { "flash", "pulse", "colour", "glow", "blink", "steady" };
        public static readonly string[] StyleNames = { "闪烁", "呼吸", "变色", "发光", "闪几下后常亮", "常亮" };
        [DataMember(Name = "On")] public bool On;
        /// <summary>剩余少于这个百分比时警告 (1..100).</summary>
        [DataMember(Name = "Pct")] public int Pct;
        [DataMember(Name = "Style")] public string Style;
        /// <summary>闪烁 / 呼吸 / 变色 speed: one cycle in ms (80..2000).</summary>
        [DataMember(Name = "PeriodMs")] public int PeriodMs;
        [DataMember(Name = "Colour")] public string Colour;
        [DataMember(Name = "IntensityPct")] public int IntensityPct;
        /// <summary>闪几下后常亮: how many blinks (1..10).</summary>
        [DataMember(Name = "Blinks")] public int Blinks;

        public static AmmoWarnSettings Level1() { AmmoWarnSettings w = new AmmoWarnSettings(); w.On = true; w.Pct = 15; w.Style = "flash"; w.PeriodMs = 250; w.Colour = "#FF3B3B"; w.IntensityPct = 80; w.Blinks = 3; return w; }
        public static AmmoWarnSettings Level2() { AmmoWarnSettings w = new AmmoWarnSettings(); w.On = false; w.Pct = 5; w.Style = "pulse"; w.PeriodMs = 140; w.Colour = "#FF0000"; w.IntensityPct = 100; w.Blinks = 5; return w; }

        /// <summary>v5.1.1 (Leo): warning presets 默认 / 竞速 (fast hard flash in a hot violet, like a shift light at the limit).</summary>
        public static readonly string[] PresetIds = { "default", "race" };
        public static readonly string[] PresetNames = { "默认", "竞速" };
        public static void ApplyPreset(AmmoWarnSettings w, string id, bool urgent)
        {
            AmmoWarnSettings d = urgent ? Level2() : Level1();
            if (id == "race") { w.Style = urgent ? "flash" : "blink"; w.PeriodMs = urgent ? 90 : 120; w.Colour = urgent ? "#3C6CFF" : "#9B3CFF"; w.IntensityPct = 100; w.Blinks = 4; }
            else { w.Style = d.Style; w.PeriodMs = d.PeriodMs; w.Colour = d.Colour; w.IntensityPct = d.IntensityPct; w.Blinks = d.Blinks; }
        }

        public void Validate(AmmoWarnSettings d)
        {
            Pct = FxClamp.I(Pct, 1, 100, d.Pct); Style = FxClamp.One(Style, Styles, d.Style);
            PeriodMs = FxClamp.I(PeriodMs, 80, 2000, d.PeriodMs); Colour = FxClamp.Col(Colour, d.Colour);
            IntensityPct = FxClamp.I(IntensityPct, 10, 100, d.IntensityPct); Blinks = FxClamp.I(Blinks, 1, 10, d.Blinks);
        }

        public AmmoWarnSettings Clone() { return (AmmoWarnSettings)MemberwiseClone(); }

        public bool Same(AmmoWarnSettings o)
        {
            return o != null && On == o.On && Pct == o.Pct && Style == o.Style && PeriodMs == o.PeriodMs && Colour == o.Colour && IntensityPct == o.IntensityPct && Blinks == o.Blinks;
        }
    }

    /// <summary>The animation of one reload kind (普通换弹 R / 快速换弹 R R). Its length is the reload time setting.</summary>
    [DataContract]
    public sealed class ReloadAnimSettings
    {
        public static readonly string[] Types = { "slide", "drain", "fade", "drop", "flash", "none", "steps", "drs" };
        public static readonly string[] TypeNames = { "滑出 + 滑入", "清空 + 填满", "淡出 + 淡入", "掉落 + 弹入", "闪烁填满", "无动画", "逐格点亮", "闪光速换" };
        public static readonly string[] Eases = { "linear", "smooth", "out", "in", "back" };
        public static readonly string[] EaseNames = { "匀速", "平滑", "先快后慢", "先慢后快", "回弹" };
        public static readonly string[] Markers = { "none", "dots", "progress", "icon" };
        public static readonly string[] MarkerNames = { "无", "三个点", "进度线", "转圈图标" };
        [DataMember(Name = "Type")] public string Type;
        /// <summary>false = 正向 (old mag leaves toward the far end, new fills from the start), true = 反向.</summary>
        [DataMember(Name = "ReverseDir")] public bool Reverse;
        /// <summary>Share of the reload time the old mag takes to leave / the new one to come in, % (5..90).</summary>
        [DataMember(Name = "OutPct")] public int OutPct;
        [DataMember(Name = "InPct")] public int InPct;
        [DataMember(Name = "Ease")] public string Ease;
        [DataMember(Name = "ColourOn")] public bool ColourOn;
        [DataMember(Name = "Colour")] public string Colour;
        [DataMember(Name = "Marker")] public string Marker;
        /// <summary>旧弹匣掉落: the old mag falls away and fades while it leaves.</summary>
        [DataMember(Name = "Drop")] public bool Drop;

        public static ReloadAnimSettings Normal()
        {
            ReloadAnimSettings r = new ReloadAnimSettings();
            r.Type = "slide"; r.Reverse = false; r.OutPct = 30; r.InPct = 40; r.Ease = "smooth"; r.ColourOn = false; r.Colour = "#5CF2FF"; r.Marker = "progress"; r.Drop = false;
            return r;
        }

        public static ReloadAnimSettings Quick()
        {
            ReloadAnimSettings r = new ReloadAnimSettings();
            r.Type = "drop"; r.Reverse = false; r.OutPct = 30; r.InPct = 65; r.Ease = "out"; r.ColourOn = false; r.Colour = "#5CF2FF"; r.Marker = "dots"; r.Drop = true;
            return r;
        }

        /// <summary>v5.1.1 (Leo): 竞速 = crisp snaps, no soft fades: 普通 lights the LEDs one by one, 快速 = a hard double flash
        /// (闪光速换), then the new mag snaps in.</summary>
        public static void ApplyRace(ReloadAnimSettings r, bool quick)
        {
            r.Type = quick ? "drs" : "steps"; r.Reverse = false; r.OutPct = 10; r.InPct = quick ? 70 : 75; r.Ease = "linear";
            r.ColourOn = quick; r.Colour = "#9B3CFF"; r.Marker = quick ? "none" : "progress"; r.Drop = false;
        }

        public void Validate(ReloadAnimSettings d)
        {
            Type = FxClamp.One(Type, Types, d.Type); Ease = FxClamp.One(Ease, Eases, d.Ease); Marker = FxClamp.One(Marker, Markers, d.Marker);
            OutPct = FxClamp.I(OutPct, 5, 90, d.OutPct); InPct = FxClamp.I(InPct, 5, 90, d.InPct); Colour = FxClamp.Col(Colour, d.Colour);
        }

        public ReloadAnimSettings Clone() { return (ReloadAnimSettings)MemberwiseClone(); }

        public bool Same(ReloadAnimSettings o)
        {
            return o != null && Type == o.Type && Reverse == o.Reverse && OutPct == o.OutPct && InPct == o.InPct && Ease == o.Ease
                && ColourOn == o.ColourOn && Colour == o.Colour && Marker == o.Marker && Drop == o.Drop;
        }
    }

    /// <summary>弹药提示: ONE bar (default a small bar below the crosshair). Presets fill the look values; each stays editable.</summary>
    [DataContract]
    public sealed class AmmoBarSettings
    {
        public static readonly string[] Presets = { "simple", "cf", "scifi", "dots", "bold", "capsule", "race" };
        public static readonly string[] PresetNames = { "简洁", "CF 风格", "科幻", "点阵", "粗条", "胶囊", "竞速" };
        public static readonly string[] Positions = { "below", "above", "left", "right" };
        public static readonly string[] PositionNames = { "准星下方", "准星上方", "准星左侧", "准星右侧" };
        public const int BandCount = 5;

        [DataMember(Name = "Preset")] public string Preset;
        [DataMember(Name = "Pos")] public string Pos;
        /// <summary>长度 px (6..160), 粗细 px (1..12), 离准星距离 px (0..60).</summary>
        [DataMember(Name = "LenPx")] public double LenPx;
        [DataMember(Name = "ThickPx")] public double ThickPx;
        [DataMember(Name = "GapPx")] public double GapPx;
        /// <summary>分段数 (0 = 整条, 2..20), gap between segments px (0.5..6).</summary>
        [DataMember(Name = "Segments")] public int Segments;
        [DataMember(Name = "SegGapPx")] public double SegGapPx;
        [DataMember(Name = "Round")] public bool Round;
        [DataMember(Name = "Glow")] public bool Glow;
        [DataMember(Name = "GlowPx")] public double GlowPx;
        [DataMember(Name = "Track")] public bool Track;
        [DataMember(Name = "TrackPct")] public int TrackPct;
        [DataMember(Name = "Frame")] public bool Frame;
        /// <summary>v5.1.1 竞速 look: 斜切 (slanted segments), 按位置上色 (LEDs green -> yellow -> red along the bar, like shift
        /// lights), 黑色底板 (a black plate behind the bar with speed stripes at the far end).</summary>
        [DataMember(Name = "Slant")] public bool Slant;
        [DataMember(Name = "Led")] public bool Led;
        [DataMember(Name = "Backing")] public bool Backing;
        /// <summary>斜切角度 (0..60°), 换挡灯三段颜色 (前 / 中 / 后), 速度条纹 on/off and its 强调色.</summary>
        [DataMember(Name = "SlantDeg")] public double SlantDeg;
        public string[] LedColours;
        [DataMember(Name = "LedColours")] private string LedColoursText { get { return JoinCols(LedColours); } set { LedColours = SplitCols(value); } }
        private static string JoinCols(string[] a) { return a == null ? null : string.Join(",", a); }
        private static string[] SplitCols(string v) { return v == null ? null : v.Split(','); }
        [DataMember(Name = "Stripes")] public bool Stripes;
        [DataMember(Name = "AccentColour")] public string AccentColour;
        public static readonly string[] DefaultLedColours = { "#19E650", "#FFD21A", "#FF2A2A" };
        [DataMember(Name = "OpacityPct")] public int OpacityPct;
        /// <summary>按剩余变色 off: this colour; on: BandColours[0..4] = 满, 大半, 一半, 少, 快空.</summary>
        [DataMember(Name = "Colour")] public string Colour;
        /// <summary>Stored as one "#RRGGBB,#RRGGBB,..." string (a list of strings would be repaired element by element).</summary>
        public string[] BandColours;
        [DataMember(Name = "BandColours")] private string BandColoursText { get { return JoinCols(BandColours); } set { BandColours = SplitCols(value); } }
        /// <summary>出现 / 消失的淡入淡出 ms (0..1000).</summary>
        [DataMember(Name = "FadeInMs")] public int FadeInMs;
        /// <summary>只在剩余少于 ShowBelowPct % 时显示.</summary>
        [DataMember(Name = "ShowBelowOn")] public bool ShowBelowOn;
        [DataMember(Name = "ShowBelowPct")] public int ShowBelowPct;
        [DataMember(Name = "Warn1")] public AmmoWarnSettings Warn1;
        [DataMember(Name = "Warn2")] public AmmoWarnSettings Warn2;
        [DataMember(Name = "ReloadNormal")] public ReloadAnimSettings ReloadNormal;
        [DataMember(Name = "ReloadQuick")] public ReloadAnimSettings ReloadQuick;

        public static readonly string[] DefaultBandColours = { "#46DC5A", "#9ADC46", "#EAD23C", "#F08C32", "#E63C3C" };

        public static AmmoBarSettings Defaults()
        {
            AmmoBarSettings s = new AmmoBarSettings();
            s.Pos = "below"; s.Colour = "#E6E6E6"; s.BandColours = (string[])DefaultBandColours.Clone();
            s.SlantDeg = 31; s.LedColours = (string[])DefaultLedColours.Clone(); s.AccentColour = "#FF2A2A";
            s.OpacityPct = 85; s.FadeInMs = 150; s.ShowBelowOn = false; s.ShowBelowPct = 35;
            ApplyPreset(s, "simple");
            s.Warn1 = AmmoWarnSettings.Level1(); s.Warn2 = AmmoWarnSettings.Level2();
            s.ReloadNormal = ReloadAnimSettings.Normal(); s.ReloadQuick = ReloadAnimSettings.Quick();
            return s;
        }

        [OnDeserializing] private void OnDes(StreamingContext c) { CopyFrom(Defaults()); }

        private void CopyFrom(AmmoBarSettings d)
        {
            Preset = d.Preset; Pos = d.Pos; LenPx = d.LenPx; ThickPx = d.ThickPx; GapPx = d.GapPx; Segments = d.Segments; SegGapPx = d.SegGapPx;
            Round = d.Round; Glow = d.Glow; GlowPx = d.GlowPx; Track = d.Track; TrackPct = d.TrackPct; Frame = d.Frame; Slant = d.Slant; Led = d.Led; Backing = d.Backing; SlantDeg = d.SlantDeg; LedColours = d.LedColours; Stripes = d.Stripes; AccentColour = d.AccentColour; OpacityPct = d.OpacityPct;
            Colour = d.Colour; BandColours = d.BandColours; FadeInMs = d.FadeInMs; ShowBelowOn = d.ShowBelowOn; ShowBelowPct = d.ShowBelowPct;
            Warn1 = d.Warn1; Warn2 = d.Warn2; ReloadNormal = d.ReloadNormal; ReloadQuick = d.ReloadQuick;
        }

        /// <summary>The look values of a preset (position, colours, warnings and reload animations are left alone).</summary>
        public static void ApplyPreset(AmmoBarSettings s, string id)
        {
            s.Slant = false; s.Led = false; s.Backing = false; s.Stripes = false;
            switch (id)
            {
                case "race": s.LenPx = 44; s.ThickPx = 4; s.GapPx = 6; s.Segments = 10; s.SegGapPx = 1.5; s.Round = false; s.Glow = false; s.GlowPx = 3; s.Track = true; s.TrackPct = 25; s.Frame = false; s.Slant = true; s.Led = true; s.Backing = true; s.Stripes = true; break;
                case "cf": s.LenPx = 44; s.ThickPx = 4; s.GapPx = 5; s.Segments = 10; s.SegGapPx = 1; s.Round = false; s.Glow = false; s.GlowPx = 3; s.Track = true; s.TrackPct = 18; s.Frame = true; break;
                case "scifi": s.LenPx = 40; s.ThickPx = 2; s.GapPx = 7; s.Segments = 6; s.SegGapPx = 2.5; s.Round = false; s.Glow = true; s.GlowPx = 3.5; s.Track = true; s.TrackPct = 12; s.Frame = false; break;
                case "dots": s.LenPx = 30; s.ThickPx = 3.5; s.GapPx = 5; s.Segments = 6; s.SegGapPx = 2.5; s.Round = true; s.Glow = false; s.GlowPx = 3; s.Track = true; s.TrackPct = 20; s.Frame = false; break;
                case "bold": s.LenPx = 34; s.ThickPx = 5; s.GapPx = 5; s.Segments = 0; s.SegGapPx = 1; s.Round = true; s.Glow = true; s.GlowPx = 2.5; s.Track = true; s.TrackPct = 28; s.Frame = false; break;
                case "capsule": s.LenPx = 20; s.ThickPx = 3; s.GapPx = 4; s.Segments = 0; s.SegGapPx = 1; s.Round = true; s.Glow = false; s.GlowPx = 3; s.Track = true; s.TrackPct = 30; s.Frame = false; break;
                default: id = "simple"; s.LenPx = 24; s.ThickPx = 2; s.GapPx = 4; s.Segments = 0; s.SegGapPx = 1; s.Round = false; s.Glow = false; s.GlowPx = 3; s.Track = true; s.TrackPct = 22; s.Frame = false; break;
            }
            s.Preset = id;
        }

        /// <summary>The preset whose look values these are, or null (自定义).</summary>
        public string PresetOf()
        {
            foreach (string id in Presets)
            {
                AmmoBarSettings p = new AmmoBarSettings(); ApplyPreset(p, id);
                if (p.LenPx == LenPx && p.ThickPx == ThickPx && p.GapPx == GapPx && p.Segments == Segments && p.SegGapPx == SegGapPx && p.Round == Round
                    && p.Glow == Glow && p.GlowPx == GlowPx && p.Track == Track && p.TrackPct == TrackPct && p.Frame == Frame
                    && p.Slant == Slant && p.Led == Led && p.Backing == Backing && p.Stripes == Stripes) return id;
            }
            return null;
        }

        public void Validate()
        {
            AmmoBarSettings d = Defaults();
            Preset = FxClamp.One(Preset, Presets, d.Preset); Pos = FxClamp.One(Pos, Positions, d.Pos);
            LenPx = FxClamp.D(LenPx, 6, 160, d.LenPx); ThickPx = FxClamp.D(ThickPx, 1, 12, d.ThickPx); GapPx = FxClamp.D(GapPx, 0, 60, d.GapPx);
            if (Segments != 0) Segments = FxClamp.I(Segments, 2, 20, 0);
            SegGapPx = FxClamp.D(SegGapPx, 0.5, 6, d.SegGapPx); GlowPx = FxClamp.D(GlowPx, 1, 10, d.GlowPx);
            TrackPct = FxClamp.I(TrackPct, 0, 100, d.TrackPct); OpacityPct = FxClamp.I(OpacityPct, 10, 100, d.OpacityPct);
            Colour = FxClamp.Col(Colour, d.Colour);
            string[] bc = new string[BandCount];
            for (int i = 0; i < BandCount; i++) bc[i] = FxClamp.Col(BandColours != null && i < BandColours.Length ? BandColours[i] : null, DefaultBandColours[i]);
            BandColours = bc;
            SlantDeg = FxClamp.D(SlantDeg, 0, 60, d.SlantDeg); AccentColour = FxClamp.Col(AccentColour, d.AccentColour);
            string[] lc = new string[3];
            for (int i = 0; i < 3; i++) lc[i] = FxClamp.Col(LedColours != null && i < LedColours.Length ? LedColours[i] : null, DefaultLedColours[i]);
            LedColours = lc;
            FadeInMs = FxClamp.I(FadeInMs, 0, 1000, d.FadeInMs); ShowBelowPct = FxClamp.I(ShowBelowPct, 5, 100, d.ShowBelowPct);
            if (Warn1 == null) Warn1 = d.Warn1; else Warn1.Validate(d.Warn1);
            if (Warn2 == null) Warn2 = d.Warn2; else Warn2.Validate(d.Warn2);
            if (ReloadNormal == null) ReloadNormal = d.ReloadNormal; else ReloadNormal.Validate(d.ReloadNormal);
            if (ReloadQuick == null) ReloadQuick = d.ReloadQuick; else ReloadQuick.Validate(d.ReloadQuick);
        }

        public AmmoBarSettings Clone()
        {
            AmmoBarSettings c = (AmmoBarSettings)MemberwiseClone();
            c.BandColours = BandColours == null ? null : (string[])BandColours.Clone();
            c.LedColours = LedColours == null ? null : (string[])LedColours.Clone();
            c.Warn1 = Warn1 == null ? null : Warn1.Clone(); c.Warn2 = Warn2 == null ? null : Warn2.Clone();
            c.ReloadNormal = ReloadNormal == null ? null : ReloadNormal.Clone(); c.ReloadQuick = ReloadQuick == null ? null : ReloadQuick.Clone();
            return c;
        }

        /// <summary>Same values (no allocation; the render thread compares every frame).</summary>
        public bool Same(AmmoBarSettings o)
        {
            if (o == null) return false;
            if (!(Pos == o.Pos && LenPx == o.LenPx && ThickPx == o.ThickPx && GapPx == o.GapPx && Segments == o.Segments && SegGapPx == o.SegGapPx
                && Round == o.Round && Glow == o.Glow && GlowPx == o.GlowPx && Track == o.Track && TrackPct == o.TrackPct && Frame == o.Frame
                && Slant == o.Slant && Led == o.Led && Backing == o.Backing && SlantDeg == o.SlantDeg && Stripes == o.Stripes && AccentColour == o.AccentColour
                && OpacityPct == o.OpacityPct && Colour == o.Colour && FadeInMs == o.FadeInMs && ShowBelowOn == o.ShowBelowOn && ShowBelowPct == o.ShowBelowPct)) return false;
            int n = BandColours == null ? 0 : BandColours.Length, m = o.BandColours == null ? 0 : o.BandColours.Length;
            if (n != m) return false;
            for (int i = 0; i < n; i++) if (BandColours[i] != o.BandColours[i]) return false;
            int a = LedColours == null ? 0 : LedColours.Length, b = o.LedColours == null ? 0 : o.LedColours.Length;
            if (a != b) return false;
            for (int i = 0; i < a; i++) if (LedColours[i] != o.LedColours[i]) return false;
            return SameW(Warn1, o.Warn1) && SameW(Warn2, o.Warn2) && SameR(ReloadNormal, o.ReloadNormal) && SameR(ReloadQuick, o.ReloadQuick);
        }

        private static bool SameW(AmmoWarnSettings a, AmmoWarnSettings b) { return a == null ? b == null : a.Same(b); }
        private static bool SameR(ReloadAnimSettings a, ReloadAnimSettings b) { return a == null ? b == null : a.Same(b); }
    }

    /// <summary>准星动态 amounts and the two firing effects (开火波纹, 开火渐变色). The on/off switches of 移动时张开 / 开火时张开 /
    /// 状态变色 stay in GlobalSettings (AnimDynGap / AnimFirePulse / AnimStateColour).</summary>
    [DataContract]
    public sealed class AnimFxSettings
    {
        [DataMember(Name = "MoveGapPct")] public int MoveGapPct;     // 移动张开幅度 % (0..300)
        [DataMember(Name = "FireGapPct")] public int FireGapPct;     // 开火张开幅度 % (0..300)
        [DataMember(Name = "FireOpenMs")] public int FireOpenMs;     // 张开速度: time to open, ms (0 = at once .. 200)
        [DataMember(Name = "FireRecoverMs")] public int FireRecoverMs; // 恢复时间 ms (50..1000)
        [DataMember(Name = "IntensityPct")] public int IntensityPct; // 开火效果强度 % (10..200)
        [DataMember(Name = "Ripple")] public bool Ripple;
        [DataMember(Name = "RippleColour")] public string RippleColour;
        [DataMember(Name = "RippleSpeed")] public int RippleSpeed;   // px/s (50..2000)
        [DataMember(Name = "RippleWidth")] public double RippleWidth; // px (0.5..8)
        [DataMember(Name = "RippleRange")] public int RippleRange;   // px it travels (4..80)
        [DataMember(Name = "RippleFadeMs")] public int RippleFadeMs; // ms (80..1500)
        [DataMember(Name = "Grad")] public bool Grad;
        [DataMember(Name = "GradFrom")] public string GradFrom;     // 中心颜色
        [DataMember(Name = "GradTo")] public string GradTo;         // 外圈颜色
        [DataMember(Name = "GradSpreadMs")] public int GradSpreadMs; // 扩散时间 ms (10..600)
        [DataMember(Name = "GradFadeMs")] public int GradFadeMs;     // 褪色时间 ms (50..1500)

        public static AnimFxSettings Defaults()
        {
            AnimFxSettings s = new AnimFxSettings();
            s.MoveGapPct = 100; s.FireGapPct = 100; s.FireOpenMs = 0; s.FireRecoverMs = 180; s.IntensityPct = 100;
            s.Ripple = false; s.RippleColour = "#5CF2FF"; s.RippleSpeed = 420; s.RippleWidth = 1.5; s.RippleRange = 22; s.RippleFadeMs = 320;
            s.Grad = false; s.GradFrom = "#FFFFFF"; s.GradTo = "#5CF2FF"; s.GradSpreadMs = 90; s.GradFadeMs = 260;
            return s;
        }

        [OnDeserializing] private void OnDes(StreamingContext c) { AnimFxSettings d = Defaults(); foreach (System.Reflection.FieldInfo f in typeof(AnimFxSettings).GetFields()) f.SetValue(this, f.GetValue(d)); }

        public void Validate()
        {
            AnimFxSettings d = Defaults();
            MoveGapPct = FxClamp.I(MoveGapPct, 0, 300, d.MoveGapPct); FireGapPct = FxClamp.I(FireGapPct, 0, 300, d.FireGapPct);
            FireOpenMs = FxClamp.I(FireOpenMs, 0, 200, d.FireOpenMs); FireRecoverMs = FxClamp.I(FireRecoverMs, 50, 1000, d.FireRecoverMs);
            IntensityPct = FxClamp.I(IntensityPct, 10, 200, d.IntensityPct);
            RippleColour = FxClamp.Col(RippleColour, d.RippleColour); RippleSpeed = FxClamp.I(RippleSpeed, 50, 2000, d.RippleSpeed);
            RippleWidth = FxClamp.D(RippleWidth, 0.5, 8, d.RippleWidth); RippleRange = FxClamp.I(RippleRange, 4, 80, d.RippleRange);
            RippleFadeMs = FxClamp.I(RippleFadeMs, 80, 1500, d.RippleFadeMs);
            GradFrom = FxClamp.Col(GradFrom, d.GradFrom); GradTo = FxClamp.Col(GradTo, d.GradTo);
            GradSpreadMs = FxClamp.I(GradSpreadMs, 10, 600, d.GradSpreadMs); GradFadeMs = FxClamp.I(GradFadeMs, 50, 1500, d.GradFadeMs);
        }

        /// <summary>v5.1.1 (Leo): the 竞速 feel: a sharp snap open and fast recovery, a thin fast red ripple, no soft gradient.</summary>
        public static void ApplyRace(AnimFxSettings s)
        {
            s.FireGapPct = 120; s.FireOpenMs = 0; s.FireRecoverMs = 90; s.IntensityPct = 100;
            s.Ripple = true; s.RippleColour = "#FF2A2A"; s.RippleSpeed = 900; s.RippleWidth = 1.0; s.RippleRange = 16; s.RippleFadeMs = 150;
            s.Grad = false;
        }

        public AnimFxSettings Clone() { return (AnimFxSettings)MemberwiseClone(); }

        public bool Same(AnimFxSettings o)
        {
            return o != null && MoveGapPct == o.MoveGapPct && FireGapPct == o.FireGapPct && FireOpenMs == o.FireOpenMs && FireRecoverMs == o.FireRecoverMs
                && IntensityPct == o.IntensityPct && Ripple == o.Ripple && RippleColour == o.RippleColour && RippleSpeed == o.RippleSpeed
                && RippleWidth == o.RippleWidth && RippleRange == o.RippleRange && RippleFadeMs == o.RippleFadeMs && Grad == o.Grad
                && GradFrom == o.GradFrom && GradTo == o.GradTo && GradSpreadMs == o.GradSpreadMs && GradFadeMs == o.GradFadeMs;
        }
    }
}
