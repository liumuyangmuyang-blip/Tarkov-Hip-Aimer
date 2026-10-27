// PURE file: only System, System.Collections.Generic, System.IO, System.Text,
// System.Runtime.Serialization(.Json). Compiles under net48 (C# 5) and net8.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TarkovCrosshair
{
    [DataContract]
    public sealed class KeyBindings
    {
        [DataMember(Name = "Forward")] public int Forward;
        [DataMember(Name = "Back")] public int Back;
        [DataMember(Name = "Left")] public int Left;
        [DataMember(Name = "Right")] public int Right;
        [DataMember(Name = "LeanLeft")] public int LeanLeft;
        [DataMember(Name = "LeanRight")] public int LeanRight;
        [DataMember(Name = "Fire")] public int Fire;
        [DataMember(Name = "Ads")] public int Ads;
        [DataMember(Name = "Toggle")] public int Toggle;
        [DataMember(Name = "FreeLook")] public int FreeLook; // 自由视角 (default middle mouse)
        [DataMember(Name = "Jump")] public int Jump;         // 跳跃 (v2.4.3, default Space)

        // 换手 (v2.4, default mouse side button 1). A property so a load can tell "absent in the file" (older file -> migrate).
        private int handSwitch;
        /// <summary>False only for bindings deserialized from a file without HandSwitch (pre-v2.4).</summary>
        public bool HandSwitchPresent;
        [DataMember(Name = "HandSwitch")]
        public int HandSwitch { get { return handSwitch; } set { handSwitch = value; HandSwitchPresent = true; } }

        // 强化准星 (v3.0, default mouse side button 2 = 上侧键). A property for the same "absent in the file" migration.
        private int boost;
        /// <summary>False only for bindings deserialized from a file without Boost (pre-v3.0).</summary>
        public bool BoostPresent;
        [DataMember(Name = "Boost")]
        public int Boost { get { return boost; } set { boost = value; BoostPresent = true; } }

        public const int VkXButton1 = 0x05;
        public const int VkXButton2 = 0x06;
        public const int VkSpace = 0x20;

        // Action table for the 按键 page (order = page order) and the "already used" check.
        public const int ActionCount = 13;
        public const int ActionHandSwitch = 4, ActionBoost = 12;
        public static readonly string[] ActionNames = new string[]
        {
            "开关准星", "开火", "开镜", "自由视角（转头）", "换手", "左侧身", "右侧身", "前进", "后退", "左移", "右移", "跳跃", "强化准星"
        };

        public KeyBindings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); HandSwitchPresent = false; BoostPresent = false; }

        public void SetDefaults()
        {
            Forward = 0x57; Back = 0x53; Left = 0x41; Right = 0x44;
            LeanLeft = 0x51; LeanRight = 0x45;
            Fire = 0x01; Ads = 0x02; Toggle = 0x24; FreeLook = 0x04; Jump = VkSpace;
            handSwitch = VkXButton1; HandSwitchPresent = true;
            boost = VkXButton2; BoostPresent = true;
        }

        public KeyBindings Clone() { return (KeyBindings)MemberwiseClone(); }

        public int Get(int action)
        {
            switch (action)
            {
                case 0: return Toggle;
                case 1: return Fire;
                case 2: return Ads;
                case 3: return FreeLook;
                case 4: return HandSwitch;
                case 5: return LeanLeft;
                case 6: return LeanRight;
                case 7: return Forward;
                case 8: return Back;
                case 9: return Left;
                case 10: return Right;
                case 11: return Jump;
                case 12: return Boost;
                default: return 0;
            }
        }

        public void Set(int action, int vk)
        {
            switch (action)
            {
                case 0: Toggle = vk; break;
                case 1: Fire = vk; break;
                case 2: Ads = vk; break;
                case 3: FreeLook = vk; break;
                case 4: HandSwitch = vk; break;
                case 5: LeanLeft = vk; break;
                case 6: LeanRight = vk; break;
                case 7: Forward = vk; break;
                case 8: Back = vk; break;
                case 9: Left = vk; break;
                case 10: Right = vk; break;
                case 11: Jump = vk; break;
                case 12: Boost = vk; break;
            }
        }

        /// <summary>The first action other than 'except' bound to vk, or -1.</summary>
        public int FindOther(int vk, int except)
        {
            for (int a = 0; a < ActionCount; a++) if (a != except && Get(a) == vk) return a;
            return -1;
        }

        public void Validate()
        {
            KeyBindings d = new KeyBindings();
            Forward = Vk(Forward, d.Forward); Back = Vk(Back, d.Back);
            Left = Vk(Left, d.Left); Right = Vk(Right, d.Right);
            LeanLeft = Vk(LeanLeft, d.LeanLeft); LeanRight = Vk(LeanRight, d.LeanRight);
            Fire = Vk(Fire, d.Fire); Ads = Vk(Ads, d.Ads); Toggle = Vk(Toggle, d.Toggle);
            FreeLook = Vk(FreeLook, d.FreeLook); Jump = Vk(Jump, d.Jump);
            bool present = HandSwitchPresent;
            handSwitch = Vk(handSwitch, d.HandSwitch);
            HandSwitchPresent = present; // migration (GlobalSettings.Validate) decides; Validate itself never marks it present
            bool bp = BoostPresent;
            boost = Vk(boost, d.Boost);
            BoostPresent = bp;
        }

        private static int Vk(int v, int def) { return (v >= 1 && v <= 254) ? v : def; }
    }

    /// <summary>Per-direction target offset (UI coords). v1/v1.1 Kick*/Sway* members in old files are ignored on load.</summary>
    [DataContract]
    public sealed class DirSetting
    {
        [DataMember(Name = "OffX")] public double OffX;
        [DataMember(Name = "OffY")] public double OffY;

        public DirSetting() { }

        public DirSetting(double offX, double offY) { OffX = offX; OffY = offY; }

        public DirSetting Clone() { return (DirSetting)MemberwiseClone(); }

        public void Validate()
        {
            OffX = Clamp.D(OffX, -500, 500, 0); OffY = Clamp.D(OffY, -500, 500, 0);
        }
    }

    /// <summary>移动速率曲线 of one axis (v2.4): 总时长 + 5 relative speeds at 0/25/50/75/100 % (see MotionCurve).</summary>
    [DataContract]
    public sealed class MoveCurve
    {
        public const double DefaultMs = 100;

        [DataMember(Name = "DurationMs")] public double DurationMs;
        [DataMember(Name = "Speeds")] public double[] Speeds;

        public MoveCurve() { SetDefaults(); }

        public MoveCurve(double ms, double[] speeds) { DurationMs = ms; Speeds = (double[])speeds.Clone(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        /// <summary>100 ms, 「v2.3 手感」.</summary>
        public void SetDefaults() { DurationMs = DefaultMs; Speeds = MotionCurve.V23Feel(); }

        public MoveCurve Clone()
        {
            MoveCurve c = (MoveCurve)MemberwiseClone();
            c.Speeds = Speeds == null ? MotionCurve.V23Feel() : (double[])Speeds.Clone();
            return c;
        }

        public void SetPreset(int index) { Speeds = (double[])MotionCurve.Presets[index].Clone(); }

        public void Validate()
        {
            DurationMs = Clamp.D(DurationMs, 0, MotionCurve.MaxDurationMs, DefaultMs);
            double[] def = MotionCurve.V23Feel();
            double[] s = new double[MotionCurve.Points];
            for (int i = 0; i < s.Length; i++)
            {
                double v = (Speeds != null && i < Speeds.Length) ? Speeds[i] : def[i];
                s[i] = Math.Round(Clamp.D(v, 0, 100, def[i]));
            }
            Speeds = s;
        }
    }

    /// <summary>One 跳跃 phase: time, end position (UI px, + = up, same sign as 最高上抬 / 站着偏移) and curve.</summary>
    [DataContract]
    public sealed class JumpPhase
    {
        [DataMember(Name = "Ms")] public double Ms;
        [DataMember(Name = "EndPx")] public double EndPx;
        [DataMember(Name = "Curve")] public string Curve;

        public JumpPhase() { }

        public JumpPhase(double ms, double endPx, string curve) { Ms = ms; EndPx = endPx; Curve = curve; }

        public JumpPhase Clone() { return (JumpPhase)MemberwiseClone(); }
    }

    /// <summary>
    /// 跳跃 per profile (v2.4.3): on a Jump press the ring moves vertically in 3 phases (起跳上抬 → 回落 → 回到起点), then 0.
    /// Every default is a PLACEHOLDER with no source: how the weapon really moves in the game is UNVERIFIED.
    /// Invariant after Validate: the three phase times (whole ms, ≥ 0) add up to TotalMs exactly; phase 3 ends at 0.
    /// </summary>
    [DataContract]
    public sealed class JumpSettings
    {
        public const int PhaseCount = 3;
        public const double MinTotalMs = 100, MaxTotalMs = 3000, LimitPx = 500;
        /// <summary>Curve keys = MotionCurve preset indices 0..3 (慢起 / 慢停 / 慢起慢停 / 匀速).</summary>
        public static readonly string[] Curves = new string[] { "in", "out", "smooth", "linear" };
        public static readonly string[] CurveLabels = new string[] { "慢起", "慢停", "慢起慢停", "匀速" };
        public static readonly string[] PhaseNames = new string[] { "先往下", "再往上", "回到起点" };

        [DataMember(Name = "Enabled")] public bool Enabled;
        [DataMember(Name = "TotalMs")] public double TotalMs;
        [DataMember(Name = "Phases")] public List<JumpPhase> Phases;

        public JumpSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        /// <summary>Off, 700 = 210 / 280 / 210 ms, +30 (慢停) → −10 (慢起慢停) → 0 (慢停). Placeholders.</summary>
        public void SetDefaults()
        {
            Enabled = false;
            TotalMs = 700;
            Phases = NewDefaultPhases();
        }

        public static List<JumpPhase> NewDefaultPhases()
        {
            List<JumpPhase> l = new List<JumpPhase>();
            l.Add(new JumpPhase(210, -30, "out"));    // v2.4.5 (Leo): bullets go DOWN first …
            l.Add(new JumpPhase(280, 10, "smooth"));  // … then UP (above the start), then back to 0. Heights are placeholders.
            l.Add(new JumpPhase(210, 0, "out"));
            return l;
        }

        public JumpSettings Clone()
        {
            JumpSettings j = (JumpSettings)MemberwiseClone();
            j.Phases = new List<JumpPhase>();
            if (Phases != null) foreach (JumpPhase p in Phases) j.Phases.Add(p == null ? null : p.Clone());
            return j;
        }

        /// <summary>MotionCurve preset index of a curve key (unknown -> 慢起慢停).</summary>
        public static int CurvePreset(string curve)
        {
            int i = Array.IndexOf(Curves, curve);
            return i < 0 ? 2 : i;
        }

        public void Validate()
        {
            List<JumpPhase> d = NewDefaultPhases();
            if (Phases == null) Phases = new List<JumpPhase>();
            while (Phases.Count < PhaseCount) Phases.Add(d[Phases.Count].Clone());
            if (Phases.Count > PhaseCount) Phases.RemoveRange(PhaseCount, Phases.Count - PhaseCount);
            for (int i = 0; i < PhaseCount; i++)
            {
                JumpPhase p = Phases[i];
                if (p == null) { p = d[i].Clone(); Phases[i] = p; }
                p.Ms = Math.Round(Clamp.D(p.Ms, 0, MaxTotalMs, d[i].Ms));
                p.EndPx = Clamp.D(p.EndPx, -LimitPx, LimitPx, d[i].EndPx);
                string c = p.Curve == null ? null : p.Curve.Trim().ToLowerInvariant();
                p.Curve = Array.IndexOf(Curves, c) >= 0 ? c : d[i].Curve;
            }
            Phases[PhaseCount - 1].EndPx = 0; // 回到起点 always ends at 0
            TotalMs = Math.Round(Clamp.D(TotalMs, MinTotalMs, MaxTotalMs, 700));
            double sum = Phases[0].Ms + Phases[1].Ms + Phases[2].Ms;
            if (sum != TotalMs)
            {
                // Inconsistent file: the phases win when their sum is a valid total, else they are rescaled to TotalMs.
                if (sum >= MinTotalMs && sum <= MaxTotalMs) TotalMs = sum;
                else JumpSplit.SetTotal(this, (int)TotalMs);
            }
        }
    }

    /// <summary>
    /// 跳跃 editing rules (pure): the three phase times always add up to TotalMs exactly, nothing goes negative.
    /// 总时长 rescales all three; one phase keeps the total and rescales the other two (both 0 -> split evenly); 自动分配 = 30/40/30 %.
    /// </summary>
    public static class JumpSplit
    {
        public static readonly int[] AutoPct = new int[] { 30, 40, 30 };

        public static int[] Get(JumpSettings j) { return new int[] { (int)j.Phases[0].Ms, (int)j.Phases[1].Ms, (int)j.Phases[2].Ms }; }

        private static void Put(JumpSettings j, int[] ph, int total)
        {
            for (int i = 0; i < 3; i++) j.Phases[i].Ms = ph[i];
            j.TotalMs = total;
        }

        private static int RoundI(double v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }

        /// <summary>Puts the rounding error on the largest phase so the sum is exactly total.</summary>
        private static void FixSum(int[] ph, int total)
        {
            int diff = total - (ph[0] + ph[1] + ph[2]);
            if (diff == 0) return;
            int big = 0;
            for (int i = 1; i < 3; i++) if (ph[i] > ph[big]) big = i;
            ph[big] += diff;
            if (ph[big] < 0)
            {
                int rest = ph[big]; ph[big] = 0;
                for (int i = 0; i < 3 && rest < 0; i++) { int take = Math.Min(ph[i], -rest); ph[i] -= take; rest += take; }
            }
        }

        private static int[] AutoPhases(int total)
        {
            int[] ph = new int[3];
            for (int i = 0; i < 3; i++) ph[i] = RoundI(total * AutoPct[i] / 100.0);
            FixSum(ph, total);
            return ph;
        }

        private static int ClampTotal(int t)
        {
            if (t < (int)JumpSettings.MinTotalMs) return (int)JumpSettings.MinTotalMs;
            if (t > (int)JumpSettings.MaxTotalMs) return (int)JumpSettings.MaxTotalMs;
            return t;
        }

        /// <summary>Change 总时长 (clamped 100..3000): all three phases scale by T'/T (自动分配 ratios if they were all 0).</summary>
        public static void SetTotal(JumpSettings j, int newTotal)
        {
            newTotal = ClampTotal(newTotal);
            double[] old = { j.Phases[0].Ms, j.Phases[1].Ms, j.Phases[2].Ms };
            double t = old[0] + old[1] + old[2];
            int[] ph;
            if (t <= 0) ph = AutoPhases(newTotal);
            else
            {
                ph = new int[3];
                for (int i = 0; i < 3; i++) ph[i] = RoundI(old[i] * newTotal / t);
                FixSum(ph, newTotal);
            }
            Put(j, ph, newTotal);
        }

        /// <summary>Change one phase (0..2) to v (clamped 0..总时长); the other two share the rest in their old proportion (evenly if both were 0).</summary>
        public static void SetPhase(JumpSettings j, int index, int v)
        {
            int[] ph = Get(j);
            int t = (int)j.TotalMs;
            if (v < 0) v = 0;
            if (v > t) v = t;
            int a = (index + 1) % 3, b = (index + 2) % 3;
            int rest = t - v;
            int na;
            if (ph[a] + ph[b] <= 0) na = RoundI(rest / 2.0);
            else na = RoundI(rest * (double)ph[a] / (ph[a] + ph[b]));
            if (na > rest) na = rest;
            ph[index] = v; ph[a] = na; ph[b] = rest - na;
            Put(j, ph, t);
        }

        /// <summary>自动分配: 总时长 x 30 / 40 / 30 %.</summary>
        public static void Auto(JumpSettings j)
        {
            int t = ClampTotal((int)j.TotalMs);
            Put(j, AutoPhases(t), t);
        }
    }

    /// <summary>The 跳跃 offset (pure): vertical UI px (+ = up) at t ms after the press; exactly 0 from TotalMs on.</summary>
    public static class JumpPath
    {
        public static double Offset(JumpSettings j, double tMs)
        {
            if (j == null || j.Phases == null || j.Phases.Count < JumpSettings.PhaseCount || !(tMs > 0)) return 0;
            double start = 0, from = 0;
            for (int i = 0; i < JumpSettings.PhaseCount; i++)
            {
                JumpPhase p = j.Phases[i];
                double to = i == JumpSettings.PhaseCount - 1 ? 0 : p.EndPx;
                double end = start + p.Ms;
                if (tMs < end && p.Ms > 0)
                {
                    double u = (tMs - start) / p.Ms;
                    return from + (to - from) * MotionCurve.Progress(MotionCurve.Presets[JumpSettings.CurvePreset(p.Curve)], u);
                }
                start = end; from = to;
            }
            return 0;
        }

        /// <summary>Phase boundary times (ms): [0, end of 1, end of 2, total].</summary>
        public static double[] Boundaries(JumpSettings j)
        {
            double a = j.Phases[0].Ms, b = a + j.Phases[1].Ms, c = b + j.Phases[2].Ms;
            return new double[] { 0, a, b, c };
        }
    }

    /// <summary>
    /// 强化准星 look per profile (v3.0). Shape and every dimension not listed here come from the normal look.
    /// All defaults are PLACEHOLDERS (no source; Leo tunes them).
    /// </summary>
    [DataContract]
    public sealed class BoostStyle
    {
        [DataMember(Name = "Diameter")] public double Diameter;
        [DataMember(Name = "Thickness")] public double Thickness;
        [DataMember(Name = "OuterThickness")] public double OuterThickness;
        [DataMember(Name = "OpacityPct")] public double OpacityPct;
        [DataMember(Name = "GlowPx")] public double GlowPx;
        [DataMember(Name = "GlowOpacityPct")] public double GlowOpacityPct;
        /// <summary>true = keep the current hand's colours (incl. 换手颜色); false = Color / Color2 below.</summary>
        [DataMember(Name = "FollowHandColor")] public bool FollowHandColor;
        [DataMember(Name = "Color")] public string Color;
        [DataMember(Name = "Color2")] public string Color2;

        public BoostStyle() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        public void SetDefaults()
        {
            Diameter = 24; Thickness = 2.4; OuterThickness = 2.5; OpacityPct = 100;
            GlowPx = 6; GlowOpacityPct = 60; FollowHandColor = true;
            Color = "#00FF00"; Color2 = "#000000";
        }

        public BoostStyle Clone() { return (BoostStyle)MemberwiseClone(); }

        public void Validate()
        {
            Diameter = Clamp.D(Diameter, 4, 300, 24);
            Thickness = Clamp.D(Thickness, 0.5, 30, 2.4);
            OuterThickness = Clamp.D(OuterThickness, 0, 20, 2.5);
            OpacityPct = Clamp.D(OpacityPct, 5, 100, 100);
            GlowPx = Clamp.D(GlowPx, 0, 40, 6);
            GlowOpacityPct = Clamp.D(GlowOpacityPct, 0, 100, 60);
            Color = ColorUtil.Normalize(Color, "#00FF00");
            Color2 = ColorUtil.Normalize(Color2, "#000000");
        }

        public string Key()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                Diameter, Thickness, OuterThickness, OpacityPct, GlowPx, GlowOpacityPct, FollowHandColor, Color, Color2);
        }
    }

    /// <summary>All hand-specific values of one hand, resolved (engine + UI). Reused buffers, no allocation per frame.</summary>
    public sealed class HandView
    {
        public readonly double[] DX = new double[Profile.DirCount], DY = new double[Profile.DirCount];
        public double QX, QY, EX, EY, SX, SY, Drift;

        public void FullLean(int side, out double x, out double y)
        {
            if (side < 0) { x = QX; y = QY; }
            else if (side > 0) { x = EX; y = EY; }
            else { x = 0; y = 0; }
        }
    }

    [DataContract]
    public sealed class Profile
    {
        public const int DirCount = 8;
        // Order: W, S, A, D, WA, WD, SA, SD. v3.0: only W/S/A/D are user values; the diagonals are always
        // 0.7071 × (sum of the two cardinals), recomputed on load and after every edit (RecomputeDiagonals).
        public static readonly string[] DirNames = new string[] { "W 前", "S 后", "A 左", "D 右", "WA 左前", "WD 右前", "SA 左后", "SD 右后" };
        /// <summary>Left/right counterpart of each direction (換手 = 左右镜像).</summary>
        public static readonly int[] MirrorDir = new int[] { 0, 1, 3, 2, 5, 4, 7, 6 };

        /// <summary>换手自定义 (v3.1) items, fixed order; 到顶时水平偏移 has X only.</summary>
        public const int HandItemCount = 8;
        public const int HandStand = 4, HandQ = 5, HandE = 6, HandDrift = 7;
        public static readonly string[] HandItemNames = new string[] { "W 前", "S 后", "A 左", "D 右", "站着偏移", "Q 左侧身", "E 右侧身", "到顶时水平偏移" };

        public static readonly string[] Gradients = new string[] { "none", "radial", "sweep", "twotone" };
        /// <summary>准星形状 (v3.0). "dot" = a filled disc that covers the centre.</summary>
        public static readonly string[] Shapes = new string[] { "ring", "cross", "ringcross", "square", "dot" };
        public static readonly string[] ShapeLabels = new string[] { "圆环", "十字", "圆环+十字", "方框", "实心圆点" };

        [DataMember(Name = "Name")] public string Name;

        [DataMember(Name = "Diameter")] public double Diameter;
        [DataMember(Name = "Thickness")] public double Thickness;
        [DataMember(Name = "Feather")] public double Feather;
        [DataMember(Name = "OpacityPct")] public double OpacityPct;
        [DataMember(Name = "Color")] public string Color;
        [DataMember(Name = "Color2")] public string Color2;
        [DataMember(Name = "Gradient")] public string Gradient;
        [DataMember(Name = "CenterDot")] public bool CenterDot;
        [DataMember(Name = "CenterDotSize")] public double CenterDotSize;
        [DataMember(Name = "OuterThickness")] public double OuterThickness; // outer band width for "twotone"

        // ---- v3.0 ring customisation (defaults reproduce the v2.4.5 look exactly) ----
        [DataMember(Name = "Shape")] public string Shape;
        [DataMember(Name = "CrossLength")] public double CrossLength;
        [DataMember(Name = "CrossThickness")] public double CrossThickness;
        [DataMember(Name = "CrossGap")] public double CrossGap;
        /// <summary>false = no top arm (T 形).</summary>
        [DataMember(Name = "CrossTop")] public bool CrossTop;
        /// <summary>0 = solid ring, 2..24 = that many segments (ring / square part).</summary>
        [DataMember(Name = "Dashes")] public int Dashes;
        [DataMember(Name = "DashGapPct")] public double DashGapPct;
        [DataMember(Name = "GlowPx")] public double GlowPx;
        [DataMember(Name = "GlowOpacityPct")] public double GlowOpacityPct;
        [DataMember(Name = "RotationDeg")] public double RotationDeg;

        // ---- v3.0 换手颜色 (placeholders) ----
        [DataMember(Name = "HandColorOn")] public bool HandColorOn;
        [DataMember(Name = "HandColor")] public string HandColor;
        [DataMember(Name = "HandColor2")] public string HandColor2;

        /// <summary>强化准星 look (v3.0).</summary>
        [DataMember(Name = "Boost")] public BoostStyle Boost;

        /// <summary>v1.2–v2.3 跟随时间; only read from older files to set the curves' 总时长. Never written back (null).</summary>
        [DataMember(Name = "FollowMs", EmitDefaultValue = false)] public double? LegacyFollowMs;

        /// <summary>移动速率曲线 左右 A/D (X axis).</summary>
        [DataMember(Name = "CurveAD")] public MoveCurve CurveAD;
        /// <summary>移动速率曲线 前后 W/S (Y axis).</summary>
        [DataMember(Name = "CurveWS")] public MoveCurve CurveWS;

        /// <summary>8 directions; [4..7] are derived (RecomputeDiagonals), whatever a file says.</summary>
        [DataMember(Name = "Dirs")] public List<DirSetting> Dirs;

        [DataMember(Name = "RecoilMaxPx")] public double RecoilMaxPx;
        [DataMember(Name = "RecoilRiseMs")] public double RecoilRiseMs;
        [DataMember(Name = "RecoilClickKickPx")] public double RecoilClickKickPx;
        [DataMember(Name = "RecoilFallMs")] public double RecoilFallMs;
        [DataMember(Name = "RecoilDriftX")] public double RecoilDriftX;
        /// <summary>ms after the Fire press before this shot's click kick and the hold rise start (the bullet leaves before the kick).</summary>
        [DataMember(Name = "RecoilDelayMs")] public double RecoilDelayMs;

        // 站着偏移 (per gun, v1.3.1): where the ring sits with no movement/lean key held. UI coords, ±200.
        // Properties so a load can tell "absent in the file" (old format -> migrate) from "0".
        private double standOffsetX, standOffsetY;
        /// <summary>False only for a profile deserialized from a file without StandOffsetX/Y (pre-v1.3.1).</summary>
        public bool StandPresent;
        [DataMember(Name = "StandOffsetX")]
        public double StandOffsetX { get { return standOffsetX; } set { standOffsetX = value; StandPresent = true; } }
        [DataMember(Name = "StandOffsetY")]
        public double StandOffsetY { get { return standOffsetY; } set { standOffsetY = value; StandPresent = true; } }

        [DataMember(Name = "LeanQX")] public double LeanQX;
        [DataMember(Name = "LeanQY")] public double LeanQY;
        [DataMember(Name = "LeanEX")] public double LeanEX;
        [DataMember(Name = "LeanEY")] public double LeanEY;
        [DataMember(Name = "LeanMs")] public double LeanMs;

        /// <summary>换手过渡时间 ms (0..2000).</summary>
        [DataMember(Name = "HandSwitchMs")] public double HandSwitchMs;

        /// <summary>换手自定义 (v3.1): HandItemCount items in HandItemNames order. Custom false = 自动 = 左右镜像 of the
        /// normal hand (the v3.0 rule, see HandAuto). Older files have none, so everything stays 自动.</summary>
        [DataMember(Name = "HandCustom")] public List<HandCustomItem> HandCustom;

        /// <summary>跳跃 (v2.4.3). Shared by both hands.</summary>
        [DataMember(Name = "Jump")] public JumpSettings Jump;

        /// <summary>「保存为默认」 snapshot of this profile (its own SavedDefault is always null), or null.</summary>
        [DataMember(Name = "SavedDefault", EmitDefaultValue = false)] public Profile SavedDefault;
        /// <summary>Local time of the snapshot, "yyyy-MM-dd HH:mm".</summary>
        [DataMember(Name = "SavedDefaultAt", EmitDefaultValue = false)] public string SavedDefaultAt;

        public Profile() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c)
        {
            SetDefaults();
            StandPresent = false;
            CurveAD = null; CurveWS = null; // absent in the file -> Validate derives them from FollowMs
        }

        public void SetDefaults()
        {
            standOffsetX = 0; standOffsetY = 0; StandPresent = true;
            Name = "配置";
            Diameter = 18; Thickness = 2; Feather = 1.0; OpacityPct = 60;
            Color = "#78FF78"; Color2 = "#FFFFFF"; Gradient = "none";
            CenterDot = false; CenterDotSize = 2; OuterThickness = 1.5;
            Shape = "ring"; CrossLength = 6; CrossThickness = 1.5; CrossGap = 4; CrossTop = true;
            Dashes = 0; DashGapPct = 30; GlowPx = 0; GlowOpacityPct = 40; RotationDeg = 0;
            HandColorOn = true; HandColor = "#FF9500"; HandColor2 = "#000000";
            Boost = new BoostStyle();
            LegacyFollowMs = null;
            CurveAD = new MoveCurve();
            CurveWS = new MoveCurve();
            Dirs = NewZeroDirs();
            RecoilMaxPx = 40; RecoilRiseMs = 300; RecoilClickKickPx = 8; RecoilFallMs = 250; RecoilDriftX = 0; RecoilDelayMs = 60;
            LeanQX = -30; LeanQY = -5; LeanEX = 30; LeanEY = -5; LeanMs = 150;
            HandSwitchMs = 300;
            HandCustom = NewAutoHand();
            Jump = new JumpSettings();
            SavedDefault = null; SavedDefaultAt = null;
        }

        /// <summary>Full lean offset of one side (-1 = Q, +1 = E), UI coords (正常手).</summary>
        public void FullLean(int side, out double x, out double y)
        {
            if (side < 0) { x = LeanQX; y = LeanQY; }
            else if (side > 0) { x = LeanEX; y = LeanEY; }
            else { x = 0; y = 0; }
        }

        public static List<DirSetting> NewZeroDirs()
        {
            List<DirSetting> l = new List<DirSetting>();
            for (int i = 0; i < DirCount; i++) l.Add(new DirSetting());
            return l;
        }

        /// <summary>换手自定义 with every item 自动 (v3.1).</summary>
        public static List<HandCustomItem> NewAutoHand()
        {
            List<HandCustomItem> l = new List<HandCustomItem>();
            for (int i = 0; i < HandItemCount; i++) l.Add(new HandCustomItem());
            return l;
        }

        /// <summary>Deep copy padded/truncated to HandItemCount (missing items 自动).</summary>
        public static List<HandCustomItem> CloneHand(List<HandCustomItem> src)
        {
            List<HandCustomItem> l = new List<HandCustomItem>();
            for (int i = 0; i < HandItemCount; i++)
                l.Add(src != null && i < src.Count && src[i] != null ? src[i].Clone() : new HandCustomItem());
            return l;
        }

        /// <summary>Deep copy, including the saved-default snapshot.</summary>
        public Profile Clone()
        {
            Profile p = (Profile)MemberwiseClone();
            p.Dirs = new List<DirSetting>();
            if (Dirs != null)
                foreach (DirSetting d in Dirs) p.Dirs.Add(d == null ? new DirSetting() : d.Clone());
            p.CurveAD = CurveAD == null ? null : CurveAD.Clone();
            p.CurveWS = CurveWS == null ? null : CurveWS.Clone();
            p.Boost = Boost == null ? new BoostStyle() : Boost.Clone();
            p.Jump = Jump == null ? new JumpSettings() : Jump.Clone();
            p.HandCustom = CloneHand(HandCustom);
            p.SavedDefault = SavedDefault == null ? null : SavedDefault.Clone();
            return p;
        }

        /// <summary>Deep copy of the values only: no saved-default snapshot (used for the snapshot itself and for export).</summary>
        public Profile CloneValues()
        {
            Profile p = Clone();
            p.SavedDefault = null;
            p.SavedDefaultAt = null;
            return p;
        }

        /// <summary>「保存为默认」: snapshot every per-profile value (incl. curves, 跳跃, 换手颜色, 强化准星), never nested.</summary>
        public void SaveAsDefault(string at)
        {
            SavedDefault = CloneValues();
            SavedDefaultAt = at;
        }

        /// <summary>The "空白（全0）" profile: default appearance and timings, all dirs, lean and recoil amounts 0.</summary>
        public static Profile CreateBlank(string name)
        {
            Profile p = new Profile();
            p.Name = name;
            p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0; p.RecoilDriftX = 0;
            p.LeanQX = 0; p.LeanQY = 0; p.LeanEX = 0; p.LeanEY = 0;
            return p;
        }

        /// <summary>
        /// The only built-in profile 「Leo默认」 (v3.1): every value from Leo's best-tested v3.0 settings.json (27 Oct 2026);
        /// 换手自定义 all 自动. How these map to the game is UNVERIFIED.
        /// </summary>
        public static Profile CreateLeoDefault(string name)
        {
            Profile p = new Profile();
            p.Name = name;
            // 外观
            p.Diameter = 12; p.Thickness = 1.2; p.Feather = 1.0; p.OpacityPct = 65;
            p.Color = "#00DF00"; p.Color2 = "#000000"; p.Gradient = "twotone"; p.OuterThickness = 1.5;
            p.CenterDot = false; p.CenterDotSize = 2;
            // 强化准星
            p.Boost.Diameter = 15; p.Boost.Thickness = 1; p.Boost.OuterThickness = 2.5; p.Boost.OpacityPct = 90;
            p.Boost.GlowPx = 5; p.Boost.GlowOpacityPct = 45; p.Boost.FollowHandColor = true;
            p.Boost.Color = "#00FF00"; p.Boost.Color2 = "#000000";
            // 移动 (X right +, Y up +); diagonals automatic
            p.Dirs[0] = new DirSetting(0, -33);  // W
            p.Dirs[1] = new DirSetting(0, 11);   // S
            p.Dirs[2] = new DirSetting(75, 0);   // A
            p.Dirs[3] = new DirSetting(-30, 3);  // D
            RecomputeDiagonals(p);
            p.StandOffsetX = 0; p.StandOffsetY = 0;
            // 移动速率曲线: both axes 380 ms 慢起 (0 / 25 / 50 / 75 / 100)
            p.CurveAD = new MoveCurve(380, MotionCurve.Presets[0]);
            p.CurveWS = new MoveCurve(380, MotionCurve.Presets[0]);
            // 后坐力
            p.RecoilMaxPx = 24; p.RecoilRiseMs = 300; p.RecoilClickKickPx = 8; p.RecoilFallMs = 180;
            p.RecoilDelayMs = 30; p.RecoilDriftX = 0;
            // 侧身
            p.LeanQX = -5; p.LeanQY = -22; p.LeanEX = 12; p.LeanEY = -17; p.LeanMs = 280;
            // 换手 (换手自定义 all 自动 = 左右镜像)
            p.HandSwitchMs = 400;
            // 跳跃: on, 800 = 200 / 305 / 295 ms, −300 → 0 → 0, 慢起慢停 / 慢起慢停 / 慢起
            JumpSettings j = new JumpSettings();
            j.Enabled = true;
            j.TotalMs = 800;
            j.Phases = new List<JumpPhase>();
            j.Phases.Add(new JumpPhase(200, -300, "smooth"));
            j.Phases.Add(new JumpPhase(305, 0, "smooth"));
            j.Phases.Add(new JumpPhase(295, 0, "in"));
            p.Jump = j;
            return p;
        }

        /// <summary>v3.0: the diagonals are always 0.7071 × (sum of the two cardinals). Call after any cardinal edit.</summary>
        public static void RecomputeDiagonals(Profile p)
        {
            if (p == null || p.Dirs == null || p.Dirs.Count < DirCount) return;
            p.Dirs[4] = Combine(p.Dirs[0], p.Dirs[2]); // WA = W + A
            p.Dirs[5] = Combine(p.Dirs[0], p.Dirs[3]); // WD = W + D
            p.Dirs[6] = Combine(p.Dirs[1], p.Dirs[2]); // SA = S + A
            p.Dirs[7] = Combine(p.Dirs[1], p.Dirs[3]); // SD = S + D
        }

        /// <summary>Same as RecomputeDiagonals (the v1–v2.4 name, kept).</summary>
        public static void AutoDiagonal(Profile p) { RecomputeDiagonals(p); }

        private static DirSetting Combine(DirSetting a, DirSetting b)
        {
            const double k = 0.7071;
            DirSetting r = new DirSetting();
            r.OffX = k * (a.OffX + b.OffX); r.OffY = k * (a.OffY + b.OffY);
            return r;
        }

        /// <summary>D = A with X negated, diagonals recomputed (no UI in v3.0; kept for tools).</summary>
        public static void MirrorLeftToRight(Profile p)
        {
            p.Dirs[3] = Mirror(p.Dirs[2]);
            RecomputeDiagonals(p);
        }

        private static DirSetting Mirror(DirSetting s)
        {
            DirSetting r = s.Clone();
            r.OffX = Neg(s.OffX);
            return r;
        }

        /// <summary>All directions 0 (no UI in v3.0; kept for tools).</summary>
        public static void ClearDirs(Profile p) { p.Dirs = NewZeroDirs(); }

        private static double Neg(double x) { return x == 0 ? 0 : -x; } // no -0

        // ---------------- 换手 ----------------

        /// <summary>
        /// Fills v with every hand-specific value. hand 0 = 正常手. hand 1 = 换手: each 换手自定义 item is Leo's own value
        /// (Custom) or 自动 = 左右镜像 of the normal hand (the v3.0 rule: A↔D, Q↔E swap and every X is negated, also 站着偏移 X
        /// and 水平偏移); the hand-1 diagonals are 0.7071 × the sum of the hand-1 cardinals. With every item 自动 this equals
        /// v3.0. How the game really behaves after a hand switch is UNVERIFIED.
        /// </summary>
        public void FillView(int hand, HandView v)
        {
            if (hand != 1)
            {
                for (int i = 0; i < DirCount; i++)
                {
                    DirSetting d = Dirs[i];
                    v.DX[i] = d.OffX;
                    v.DY[i] = d.OffY;
                }
                v.SX = StandOffsetX; v.SY = StandOffsetY;
                v.QX = LeanQX; v.QY = LeanQY;
                v.EX = LeanEX; v.EY = LeanEY;
                v.Drift = RecoilDriftX;
                return;
            }
            for (int i = 0; i < 4; i++) HandValue(i, out v.DX[i], out v.DY[i]);
            const double k = 0.7071; // same as RecomputeDiagonals
            v.DX[4] = k * (v.DX[0] + v.DX[2]); v.DY[4] = k * (v.DY[0] + v.DY[2]); // WA = W + A
            v.DX[5] = k * (v.DX[0] + v.DX[3]); v.DY[5] = k * (v.DY[0] + v.DY[3]); // WD = W + D
            v.DX[6] = k * (v.DX[1] + v.DX[2]); v.DY[6] = k * (v.DY[1] + v.DY[2]); // SA = S + A
            v.DX[7] = k * (v.DX[1] + v.DX[3]); v.DY[7] = k * (v.DY[1] + v.DY[3]); // SD = S + D
            HandValue(HandStand, out v.SX, out v.SY);
            HandValue(HandQ, out v.QX, out v.QY);
            HandValue(HandE, out v.EX, out v.EY);
            double unused;
            HandValue(HandDrift, out v.Drift, out unused);
        }

        /// <summary>自动 value of a 换手 item: 左右镜像 of the CURRENT normal-hand values (the v3.0 rule).</summary>
        public void HandAuto(int item, out double x, out double y)
        {
            switch (item)
            {
                case 0: x = Neg(Dirs[0].OffX); y = Dirs[0].OffY; break;   // W
                case 1: x = Neg(Dirs[1].OffX); y = Dirs[1].OffY; break;   // S
                case 2: x = Neg(Dirs[3].OffX); y = Dirs[3].OffY; break;   // A <- D
                case 3: x = Neg(Dirs[2].OffX); y = Dirs[2].OffY; break;   // D <- A
                case HandStand: x = Neg(StandOffsetX); y = StandOffsetY; break;
                case HandQ: x = Neg(LeanEX); y = LeanEY; break;           // Q <- E
                case HandE: x = Neg(LeanQX); y = LeanQY; break;           // E <- Q
                case HandDrift: x = Neg(RecoilDriftX); y = 0; break;
                default: x = 0; y = 0; break;
            }
        }

        public bool IsHandCustom(int item)
        {
            List<HandCustomItem> l = HandCustom;
            return l != null && item >= 0 && item < l.Count && l[item] != null && l[item].Custom;
        }

        /// <summary>The value the switched hand uses: Leo's own numbers for a custom item, else 自动.</summary>
        public void HandValue(int item, out double x, out double y)
        {
            HandCustomItem h = IsHandCustom(item) ? HandCustom[item] : null;
            if (h != null)
            {
                x = h.X;
                y = item == HandDrift ? 0 : h.Y;
                return;
            }
            HandAuto(item, out x, out y);
        }

        public void SetHandCustom(int item, double x, double y)
        {
            if (item < 0 || item >= HandItemCount) return;
            EnsureHandList();
            HandCustomItem h = new HandCustomItem();
            h.Custom = true;
            h.X = x;
            h.Y = item == HandDrift ? 0 : y;
            HandCustom[item] = h;
        }

        public void SetHandAuto(int item)
        {
            if (item < 0 || item >= HandItemCount) return;
            EnsureHandList();
            HandCustom[item] = new HandCustomItem();
        }

        public void AllHandAuto() { HandCustom = NewAutoHand(); }

        public bool AnyHandCustom()
        {
            for (int i = 0; i < HandItemCount; i++) if (IsHandCustom(i)) return true;
            return false;
        }

        /// <summary>Exactly HandItemCount non-null items (missing ones 自动).</summary>
        private void EnsureHandList()
        {
            if (HandCustom == null) HandCustom = new List<HandCustomItem>();
            if (HandCustom.Count > HandItemCount) HandCustom.RemoveRange(HandItemCount, HandCustom.Count - HandItemCount);
            while (HandCustom.Count < HandItemCount) HandCustom.Add(new HandCustomItem());
            for (int i = 0; i < HandItemCount; i++) if (HandCustom[i] == null) HandCustom[i] = new HandCustomItem();
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Name) || Name.Trim().Length == 0) Name = "配置";
            Name = Name.Trim();
            Diameter = Clamp.D(Diameter, 4, 300, 18);
            Thickness = Clamp.D(Thickness, 0.5, 30, 2);
            Feather = Clamp.D(Feather, 0, 30, 1.0);
            OpacityPct = Clamp.D(OpacityPct, 5, 100, 60);
            Color = ColorUtil.Normalize(Color, "#78FF78");
            Color2 = ColorUtil.Normalize(Color2, "#FFFFFF");
            Gradient = Clamp.OneOf(Gradient, Gradients);
            CenterDotSize = Clamp.D(CenterDotSize, 1, 10, 2);
            OuterThickness = Clamp.D(OuterThickness, 0.5, 20, 1.5);
            Shape = Clamp.OneOf(Shape, Shapes);
            CrossLength = Clamp.D(CrossLength, 0, 200, 6);
            CrossThickness = Clamp.D(CrossThickness, 0.5, 30, 1.5);
            CrossGap = Clamp.D(CrossGap, 0, 200, 4);
            if (Dashes < 2) Dashes = 0;
            if (Dashes > 24) Dashes = 24;
            DashGapPct = Clamp.D(DashGapPct, 0, 90, 30);
            GlowPx = Clamp.D(GlowPx, 0, 40, 0);
            GlowOpacityPct = Clamp.D(GlowOpacityPct, 0, 100, 40);
            RotationDeg = Clamp.D(RotationDeg, -100000, 100000, 0) % 360.0; // same ranges as the UI: wrap to -180..180
            if (RotationDeg > 180) RotationDeg -= 360;
            if (RotationDeg < -180) RotationDeg += 360;
            HandColor = ColorUtil.Normalize(HandColor, "#FF9500");
            HandColor2 = ColorUtil.Normalize(HandColor2, "#000000");
            if (Boost == null) Boost = new BoostStyle();
            Boost.Validate();
            // v2.4 migration: a file from v1.x .. v2.3 has no curves; 总时长 = its 跟随时间 (absent -> 100), speeds = 「v2.3 手感」.
            double followMs = LegacyFollowMs.HasValue ? Clamp.D(LegacyFollowMs.Value, 0, MotionCurve.MaxDurationMs, MoveCurve.DefaultMs) : MoveCurve.DefaultMs;
            if (CurveAD == null) CurveAD = new MoveCurve(followMs, MotionCurve.V23Feel());
            if (CurveWS == null) CurveWS = new MoveCurve(followMs, MotionCurve.V23Feel());
            LegacyFollowMs = null;
            CurveAD.Validate();
            CurveWS.Validate();
            if (Dirs == null) Dirs = NewZeroDirs();
            for (int i = 0; i < Dirs.Count; i++) if (Dirs[i] == null) Dirs[i] = new DirSetting();
            while (Dirs.Count < DirCount) Dirs.Add(new DirSetting());
            if (Dirs.Count > DirCount) Dirs.RemoveRange(DirCount, Dirs.Count - DirCount);
            foreach (DirSetting d in Dirs) d.Validate();
            RecomputeDiagonals(this); // v3.0: diagonals always automatic, also for older files
            RecoilMaxPx = Clamp.D(RecoilMaxPx, 0, 1000, 40);
            RecoilRiseMs = Clamp.D(RecoilRiseMs, 10, 5000, 300);
            RecoilClickKickPx = Clamp.D(RecoilClickKickPx, 0, 300, 8);
            RecoilFallMs = Clamp.D(RecoilFallMs, 10, 5000, 250);
            RecoilDriftX = Clamp.D(RecoilDriftX, -500, 500, 0);
            RecoilDelayMs = Clamp.D(RecoilDelayMs, 0, 300, 60);
            LeanQX = Clamp.D(LeanQX, -500, 500, -30); LeanQY = Clamp.D(LeanQY, -500, 500, -5);
            LeanEX = Clamp.D(LeanEX, -500, 500, 30); LeanEY = Clamp.D(LeanEY, -500, 500, -5);
            LeanMs = Clamp.D(LeanMs, 0, 2000, 150);
            standOffsetX = Clamp.D(standOffsetX, -200, 200, 0);
            standOffsetY = Clamp.D(standOffsetY, -200, 200, 0);
            HandSwitchMs = Clamp.D(HandSwitchMs, 0, 2000, 300);
            EnsureHandList(); // v3.1 换手自定义: exactly 8 items; older files have none (all 自动)
            for (int i = 0; i < HandItemCount; i++)
            {
                HandCustomItem h = HandCustom[i];
                double lim = i == HandStand ? 200 : 500;
                h.X = Clamp.D(h.X, -lim, lim, 0);
                h.Y = i == HandDrift ? 0 : Clamp.D(h.Y, -lim, lim, 0);
            }
            if (Jump == null) Jump = new JumpSettings();
            Jump.Validate();
            if (SavedDefault != null)
            {
                SavedDefault.SavedDefault = null; // never nested
                SavedDefault.SavedDefaultAt = null;
                SavedDefault.Validate();
                if (SavedDefaultAt == null) SavedDefaultAt = "";
            }
            else SavedDefaultAt = null;
        }

        /// <summary>A string that changes whenever anything affecting the raster changes (both hands, both looks).</summary>
        public string AppearanceKey()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}",
                Diameter, Thickness, Feather, Color, Color2, Gradient, CenterDot, CenterDotSize, OuterThickness,
                Shape, CrossLength, CrossThickness, CrossGap, CrossTop, Dashes, DashGapPct, GlowPx, GlowOpacityPct, RotationDeg,
                HandColorOn, HandColor, HandColor2, Boost == null ? "" : Boost.Key());
        }
    }

    [DataContract]
    public sealed class GlobalSettings
    {
        /// <summary>The only built-in profile (v3.0).</summary>
        public const string DefaultProfileName = "Leo默认";
        /// <summary>The v2.4–v2.4.5 built-in profile name: renamed to DefaultProfileName on load (unless that name is taken).</summary>
        public const string LegacyDefaultProfileName = "M4（Leo 改装）";
        /// <summary>Default for GameWindowTitle. UNVERIFIED that this is the real title of the Tarkov game window.</summary>
        public const string DefaultGameWindowTitle = "EscapeFromTarkov";

        [DataMember(Name = "Version")] public int Version;
        [DataMember(Name = "ActiveProfile")] public string ActiveProfile;
        [DataMember(Name = "Enabled")] public bool Enabled;
        [DataMember(Name = "Keys")] public KeyBindings Keys;
        [DataMember(Name = "AdsMode")] public string AdsMode;
        [DataMember(Name = "LeanMode")] public string LeanMode;
        [DataMember(Name = "MonitorIndex")] public int MonitorIndex;
        // Legacy (<= v1.3) global 中心校正. Only read to migrate old files: Validate copies it into every profile
        // that has no StandOffsetX/Y yet, then sets it to 0. Not used by the engine any more.
        [DataMember(Name = "CenterOffsetX")] public double CenterOffsetX;
        [DataMember(Name = "CenterOffsetY")] public double CenterOffsetY;
        [DataMember(Name = "FrameMode")] public string FrameMode;
        [DataMember(Name = "Fps")] public double Fps;
        [DataMember(Name = "ShowFadeMs")] public double ShowFadeMs;
        [DataMember(Name = "HideFadeMs")] public double HideFadeMs;
        [DataMember(Name = "SettingsTopMost")] public bool SettingsTopMost;
        [DataMember(Name = "ShowSettingsOnStart")] public bool ShowSettingsOnStart;
        /// <summary>按住自由视角键时隐藏准星 (default on, also for older files without the field).</summary>
        [DataMember(Name = "HideOnFreeLook")] public bool HideOnFreeLook;
        /// <summary>☑ 换手检测 (v2.4, default on).</summary>
        [DataMember(Name = "HandEnabled")] public bool HandEnabled;
        /// <summary>换手方式: "toggle" (切换, default) or "hold" (按住).</summary>
        [DataMember(Name = "HandMode")] public string HandMode;
        /// <summary>分辨率换算: offsets were tuned at ResBase; scale s = ResGameH / ResBaseH on both axes.</summary>
        [DataMember(Name = "ResBaseW")] public double ResBaseW;
        [DataMember(Name = "ResBaseH")] public double ResBaseH;
        [DataMember(Name = "ResGameW")] public double ResGameW;
        [DataMember(Name = "ResGameH")] public double ResGameH;

        // ---- v3.0 ----
        /// <summary>只在塔科夫前台时显示 (default on): see FocusGate.</summary>
        [DataMember(Name = "OnlyWhenGameFocused")] public bool OnlyWhenGameFocused;
        /// <summary>Case-insensitive substring of the game window title (UNVERIFIED default, see DefaultGameWindowTitle).</summary>
        [DataMember(Name = "GameWindowTitle")] public string GameWindowTitle;
        /// <summary>开机自动启动 (default off). The real state lives in Task Scheduler (Autostart class); this mirrors it.</summary>
        [DataMember(Name = "Autostart")] public bool Autostart;
        /// <summary>启动动画 (default on).</summary>
        [DataMember(Name = "SplashOnStart")] public bool SplashOnStart;
        /// <summary>☑ 启用 强化准星 (default on), like 换手检测.</summary>
        [DataMember(Name = "BoostEnabled")] public bool BoostEnabled;

        /// <summary>s = 游戏高度 / 调参高度 (1 when equal).</summary>
        public double ResolutionScale { get { return (ResBaseH > 0 && ResGameH > 0) ? ResGameH / ResBaseH : 1.0; } }
        [DataMember(Name = "Profiles")] public List<Profile> Profiles;

        // Runtime only (not saved).
        public PreviewMode PreviewMode;
        public double PreviewSpeed;
        /// <summary>The hand shown in the settings window (0 正常手 / 1 换手); synthetic previews use it.</summary>
        public int EditHand;

        public GlobalSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        public void SetDefaults()
        {
            Version = 1; ActiveProfile = ""; Enabled = true;
            Keys = new KeyBindings();
            AdsMode = "hold"; LeanMode = "hold";
            MonitorIndex = -1; CenterOffsetX = 0; CenterOffsetY = 0;
            FrameMode = "vsync"; Fps = 144; ShowFadeMs = 60; HideFadeMs = 0;
            SettingsTopMost = true; ShowSettingsOnStart = true; HideOnFreeLook = true;
            HandEnabled = true; HandMode = "toggle";
            ResBaseW = 2160; ResBaseH = 1440; ResGameW = 2160; ResGameH = 1440;
            OnlyWhenGameFocused = true; GameWindowTitle = DefaultGameWindowTitle;
            Autostart = false; SplashOnStart = true; BoostEnabled = true;
            Profiles = new List<Profile>();
            PreviewMode = PreviewMode.Live; PreviewSpeed = 1.0; EditHand = 0;
        }

        /// <summary>Fresh install (no settings.json): exactly one profile 「Leo默认」, active.</summary>
        public static GlobalSettings CreateDefault()
        {
            GlobalSettings g = new GlobalSettings();
            g.Enabled = true;
            g.Keys = new KeyBindings();
            g.Keys.Toggle = 0x7B;          // F12
            g.AdsMode = "hold"; g.LeanMode = "hold";
            g.MonitorIndex = -1;           // 主显示器
            g.FrameMode = "vsync"; g.Fps = 360;
            g.ShowFadeMs = 50; g.HideFadeMs = 0;
            g.ResGameW = 1920; // Leo's file (27 Oct 2026); the scale uses height only, so nothing moves
            g.SettingsTopMost = true; g.ShowSettingsOnStart = true; g.HideOnFreeLook = true;
            g.HandEnabled = true; g.HandMode = "toggle";
            g.OnlyWhenGameFocused = true; g.GameWindowTitle = DefaultGameWindowTitle;
            g.Autostart = false; g.SplashOnStart = true; g.BoostEnabled = true;
            g.Profiles.Add(Profile.CreateLeoDefault(DefaultProfileName));
            g.ActiveProfile = DefaultProfileName;
            return g;
        }

        /// <summary>「按键恢复默认」: every binding + mode on the 按键 page back to the fresh-install values.</summary>
        public void ResetKeys()
        {
            GlobalSettings d = CreateDefault();
            Keys = d.Keys.Clone();
            AdsMode = d.AdsMode; LeanMode = d.LeanMode; HideOnFreeLook = d.HideOnFreeLook;
            HandMode = d.HandMode; HandEnabled = d.HandEnabled; BoostEnabled = d.BoostEnabled;
        }

        /// <summary>Copy of the global fields (Keys cloned). Profiles are deep-cloned only if withProfiles.</summary>
        public GlobalSettings Clone(bool withProfiles)
        {
            GlobalSettings g = (GlobalSettings)MemberwiseClone();
            g.Keys = Keys == null ? new KeyBindings() : Keys.Clone();
            g.Profiles = new List<Profile>();
            if (withProfiles && Profiles != null)
                foreach (Profile p in Profiles) g.Profiles.Add(p.Clone());
            return g;
        }

        public Profile FindProfile(string name)
        {
            if (Profiles == null) return null;
            foreach (Profile p in Profiles) if (p != null && p.Name == name) return p;
            return null;
        }

        public Profile Active
        {
            get
            {
                Profile p = FindProfile(ActiveProfile);
                if (p == null && Profiles != null && Profiles.Count > 0) p = Profiles[0];
                return p;
            }
        }

        /// <summary>Returns a name not used by any profile except 'except': clash appends " (2)", " (3)", ...</summary>
        public string UniqueName(string wanted, Profile except)
        {
            string baseName = string.IsNullOrEmpty(wanted) ? "配置" : wanted.Trim();
            if (baseName.Length == 0) baseName = "配置";
            string n = baseName;
            int k = 2;
            while (true)
            {
                Profile hit = FindProfile(n);
                if (hit == null || hit == except) return n;
                n = baseName + " (" + k + ")";
                k++;
            }
        }

        /// <summary>Replaces p by 'values' (a values-only profile): name and saved-default snapshot of p are kept. Returns the new profile.</summary>
        private Profile ReplaceValues(Profile p, Profile values)
        {
            int idx = Profiles.IndexOf(p);
            Profile n = values.CloneValues();
            n.Name = p.Name;
            n.SavedDefault = p.SavedDefault == null ? null : p.SavedDefault.CloneValues();
            n.SavedDefaultAt = p.SavedDefaultAt;
            n.Validate();
            if (idx >= 0) Profiles[idx] = n;
            return n;
        }

        /// <summary>恢复到内置默认: p's values = Profile.CreateLeoDefault, name + saved snapshot kept; globals untouched.</summary>
        public Profile RestoreLeoDefault(Profile p)
        {
            return ReplaceValues(p, Profile.CreateLeoDefault(p.Name));
        }

        /// <summary>恢复到我存的默认: p's values = its snapshot, name + snapshot kept. Returns p unchanged when there is none.</summary>
        public Profile RestoreSaved(Profile p)
        {
            if (p.SavedDefault == null) return p;
            return ReplaceValues(p, p.SavedDefault);
        }

        /// <summary>Adds validated copies of the profiles (clashing names get " (2)"), returns the first added name.</summary>
        public string AddImported(List<Profile> imported)
        {
            string first = null;
            foreach (Profile src in imported)
            {
                Profile p = src.CloneValues();
                p.Validate();
                p.Name = UniqueName(p.Name, null);
                Profiles.Add(p);
                if (first == null) first = p.Name;
            }
            if (first != null) ActiveProfile = first;
            return first;
        }

        public void Validate()
        {
            Version = 1;
            if (Keys == null) Keys = new KeyBindings();
            bool handMigrate = !Keys.HandSwitchPresent;
            Keys.Validate();
            if (handMigrate)
            {
                // v2.4 migration: 换手 = mouse side button 1, 切换, on — unless that button already does something else in this file.
                Keys.HandSwitch = KeyBindings.VkXButton1;
                HandMode = "toggle";
                HandEnabled = Keys.FindOther(KeyBindings.VkXButton1, 4) < 0;
            }
            Keys.HandSwitchPresent = true;
            if (!Keys.BoostPresent)
            {
                // v3.0 migration: 强化准星 = mouse side button 2, on — unless that button already does something else in this file.
                Keys.Boost = KeyBindings.VkXButton2;
                BoostEnabled = Keys.FindOther(KeyBindings.VkXButton2, KeyBindings.ActionBoost) < 0;
            }
            Keys.BoostPresent = true;
            GameWindowTitle = GameWindowTitle == null ? "" : GameWindowTitle.Trim();
            if (GameWindowTitle.Length == 0) GameWindowTitle = DefaultGameWindowTitle;
            if (GameWindowTitle.Length > 200) GameWindowTitle = GameWindowTitle.Substring(0, 200);
            AdsMode = Clamp.OneOf(AdsMode, new string[] { "hold", "toggle" });
            LeanMode = Clamp.OneOf(LeanMode, new string[] { "hold", "toggle" });
            HandMode = Clamp.OneOf(HandMode, new string[] { "toggle", "hold" });
            if (MonitorIndex < -1 || MonitorIndex > 15) MonitorIndex = -1;
            CenterOffsetX = Clamp.D(CenterOffsetX, -200, 200, 0);
            CenterOffsetY = Clamp.D(CenterOffsetY, -200, 200, 0);
            // v1.3.1 migration: old global 中心校正 -> 站着偏移 of every profile that does not have one yet (once).
            if (Profiles != null)
                foreach (Profile p in Profiles)
                    if (p != null && !p.StandPresent) { p.StandOffsetX = CenterOffsetX; p.StandOffsetY = CenterOffsetY; }
            CenterOffsetX = 0;
            CenterOffsetY = 0;
            FrameMode = Clamp.OneOf(FrameMode, new string[] { "vsync", "fixed" });
            Fps = Clamp.D(Fps, 30, 360, 144);
            ShowFadeMs = Clamp.D(ShowFadeMs, 0, 1000, 60);
            HideFadeMs = Clamp.D(HideFadeMs, 0, 1000, 0);
            ResBaseW = Math.Round(Clamp.D(ResBaseW, 640, 7680, 2160)); ResBaseH = Math.Round(Clamp.D(ResBaseH, 480, 4320, 1440));
            ResGameW = Math.Round(Clamp.D(ResGameW, 640, 7680, 2160)); ResGameH = Math.Round(Clamp.D(ResGameH, 480, 4320, 1440));
            PreviewSpeed = Clamp.D(PreviewSpeed, 0.1, 1.0, 1.0);
            if (EditHand != 1) EditHand = 0;

            if (Profiles == null) Profiles = new List<Profile>();
            Profiles.RemoveAll(delegate(Profile p) { return p == null; });
            if (Profiles.Count == 0)
            {
                GlobalSettings d = CreateDefault();
                Profiles.AddRange(d.Profiles);
            }
            // v3.0: the old built-in name 「M4（Leo 改装）」 becomes 「Leo默认」 (values kept), unless that name is already used.
            if (FindProfile(DefaultProfileName) == null)
            {
                Profile legacy = FindProfile(LegacyDefaultProfileName);
                if (legacy != null)
                {
                    legacy.Name = DefaultProfileName;
                    if (legacy.SavedDefault != null && legacy.SavedDefault.Name == LegacyDefaultProfileName) legacy.SavedDefault.Name = DefaultProfileName;
                    if (ActiveProfile == LegacyDefaultProfileName) ActiveProfile = DefaultProfileName;
                }
            }
            // Validate and make names unique.
            List<Profile> seen = new List<Profile>();
            foreach (Profile p in Profiles)
            {
                p.Validate();
                string n = p.Name;
                int k = 2;
                while (seen.Exists(delegate(Profile q) { return q.Name == n; }))
                {
                    n = p.Name + " (" + k + ")";
                    k++;
                }
                p.Name = n;
                seen.Add(p);
            }
            if (FindProfile(ActiveProfile) == null) ActiveProfile = Profiles[0].Name;
        }
    }

    /// <summary>One 换手自定义 item (v3.1). Custom false = 自动 (左右镜像 of the normal hand; X/Y ignored).</summary>
    [DataContract]
    public sealed class HandCustomItem
    {
        [DataMember(Name = "Custom")] public bool Custom;
        [DataMember(Name = "X")] public double X;
        [DataMember(Name = "Y")] public double Y;

        public HandCustomItem Clone() { return (HandCustomItem)MemberwiseClone(); }
    }

    public static class Clamp
    {
        public static double D(double v, double min, double max, double def)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return def;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static string OneOf(string v, string[] allowed)
        {
            if (v != null)
            {
                string t = v.Trim().ToLowerInvariant();
                foreach (string a in allowed) if (a == t) return a;
            }
            return allowed[0];
        }
    }

    public static class ColorUtil
    {
        /// <summary>Parses "#RRGGBB". Returns false on anything else.</summary>
        public static bool TryParse(string s, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (s == null) return false;
            s = s.Trim();
            if (s.Length != 7 || s[0] != '#') return false;
            int v = 0;
            for (int i = 1; i < 7; i++)
            {
                int h = Hex(s[i]);
                if (h < 0) return false;
                v = v * 16 + h;
            }
            r = (v >> 16) & 0xFF; g = (v >> 8) & 0xFF; b = v & 0xFF;
            return true;
        }

        public static string Normalize(string s, string def)
        {
            int r, g, b;
            if (!TryParse(s, out r, out g, out b)) return def;
            return Format(r, g, b);
        }

        public static string Format(int r, int g, int b)
        {
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }

    public static class SettingsIO
    {
        public static string DefaultPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        }

        public static string Serialize(GlobalSettings g)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                WriteTo(g, ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        // Pretty JSON; the writer owns (and closes) the stream, as in the spec.
        private static void WriteTo(GlobalSettings g, Stream s)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(GlobalSettings));
            using (System.Xml.XmlDictionaryWriter w = JsonReaderWriterFactory.CreateJsonWriter(s, Encoding.UTF8, true, true, "  "))
            {
                ser.WriteObject(w, g);
                w.Flush();
                FileStream fs = s as FileStream;
                if (fs != null) fs.Flush(true);
            }
        }

        /// <summary>Parses and validates. Never throws; returns false with an error message on bad input.</summary>
        public static bool TryParse(string json, out GlobalSettings result, out string error)
        {
            result = null; error = null;
            try
            {
                if (json == null) { error = "empty"; return false; }
                string trimmed = json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                if (trimmed.Length == 0 || trimmed[0] != '{') { error = "not a JSON object"; return false; }
                byte[] bytes = Encoding.UTF8.GetBytes(trimmed);
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(GlobalSettings));
                object o;
                using (MemoryStream ms = new MemoryStream(bytes)) o = ser.ReadObject(ms);
                GlobalSettings g = o as GlobalSettings;
                if (g == null) { error = "not a settings object"; return false; }
                g.Validate();
                result = g;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Pretty JSON of one profile (the 导出 file). The saved-default snapshot is not exported.</summary>
        public static string ExportProfile(Profile p)
        {
            Profile values = p.CloneValues();
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Profile));
                using (System.Xml.XmlDictionaryWriter w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    ser.WriteObject(w, values);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        [DataContract]
        private sealed class ImportProbe
        {
            [DataMember(Name = "Profiles")] public List<Profile> Profiles;
            [DataMember(Name = "Name")] public string Name;
            [DataMember(Name = "Dirs")] public List<DirSetting> Dirs;

            // Members are filled by the serializer; nulls mean "absent in the file".
            [OnDeserializing]
            private void OnDeserializing(StreamingContext c) { Profiles = null; Name = null; Dirs = null; }
        }

        /// <summary>
        /// 导入: accepts a single exported profile or a full settings.json. Never throws.
        /// Returns validated profiles (names not yet made unique against the target), or false with an error.
        /// </summary>
        public static bool TryParseImport(string json, out List<Profile> profiles, out string error)
        {
            profiles = null; error = null;
            try
            {
                if (json == null) { error = "空文件"; return false; }
                string t = json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                if (t.Length == 0 || t[0] != '{') { error = "不是 JSON 对象"; return false; }
                byte[] bytes = Encoding.UTF8.GetBytes(t);
                ImportProbe probe;
                using (MemoryStream ms = new MemoryStream(bytes))
                    probe = (ImportProbe)new DataContractJsonSerializer(typeof(ImportProbe)).ReadObject(ms);
                List<Profile> list = new List<Profile>();
                if (probe != null && probe.Profiles != null && probe.Profiles.Count > 0)
                {
                    foreach (Profile p in probe.Profiles) if (p != null) list.Add(p);
                }
                else if (probe != null && (probe.Name != null || probe.Dirs != null))
                {
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        Profile one = new DataContractJsonSerializer(typeof(Profile)).ReadObject(ms) as Profile;
                        if (one != null) list.Add(one);
                    }
                }
                if (list.Count == 0) { error = "文件里没有准星配置"; return false; }
                foreach (Profile p in list) { p.SavedDefault = null; p.SavedDefaultAt = null; p.Validate(); } // import never carries a snapshot
                profiles = list;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Loads settings. Missing file: writes and returns the defaults (message null).
        /// Unparsable file: renames it to settings.bad-yyyyMMdd-HHmmss.json, returns the defaults,
        /// and sets 'message' for the caller to show.
        /// </summary>
        public static GlobalSettings Load(string path, out string message)
        {
            message = null;
            if (!File.Exists(path))
            {
                GlobalSettings d = GlobalSettings.CreateDefault();
                d.Validate();
                Save(d, path);
                return d;
            }
            string text = null;
            string err;
            GlobalSettings g = null;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { err = ex.Message; message = "读取设置文件失败：" + err + "\n已使用默认设置。"; GlobalSettings d0 = GlobalSettings.CreateDefault(); d0.Validate(); return d0; }

            if (TryParse(text, out g, out err)) return g;

            string bad = Path.Combine(Path.GetDirectoryName(path),
                "settings.bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            try { if (File.Exists(bad)) File.Delete(bad); File.Move(path, bad); }
            catch (Exception) { }
            GlobalSettings def = GlobalSettings.CreateDefault();
            def.Validate();
            Save(def, path);
            message = "设置文件无法读取（" + err + "）。\n旧文件已改名为 " + Path.GetFileName(bad) + "，现在使用默认设置。";
            return def;
        }

        /// <summary>Writes settings.json.tmp, then replaces settings.json. Returns null on success, else the error.</summary>
        public static string Save(GlobalSettings g, string path)
        {
            string tmp = path + ".tmp";
            try
            {
                FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                try { WriteTo(g, fs); } // the writer closes fs
                finally { fs.Dispose(); }
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null); }
                    catch (PlatformNotSupportedException) { File.Delete(path); File.Move(tmp, path); }
                }
                else
                {
                    File.Move(tmp, path);
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
