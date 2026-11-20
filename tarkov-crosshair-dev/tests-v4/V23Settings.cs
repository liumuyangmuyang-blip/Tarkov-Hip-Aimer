// TEST ONLY: verbatim v2.3 Settings.cs (namespace renamed) — reference to prove what remains behaves exactly like v2.3.
// PURE file: only System, System.Collections.Generic, System.IO, System.Text,
// System.Runtime.Serialization(.Json). Compiles under net48 (C# 5) and net8.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TarkovCrosshair.V23
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
        [DataMember(Name = "Crouch")] public int Crouch;     // 蹲下 (default C)
        [DataMember(Name = "Sprint")] public int Sprint;     // 冲刺 (default left Shift); see GlobalSettings.SprintMode
        [DataMember(Name = "Prone")] public int Prone;       // 趴下 (default Z); a press = not crouched

        public KeyBindings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        public void SetDefaults()
        {
            Forward = 0x57; Back = 0x53; Left = 0x41; Right = 0x44;
            LeanLeft = 0x51; LeanRight = 0x45;
            Fire = 0x01; Ads = 0x02; Toggle = 0x24; FreeLook = 0x04; Crouch = 0x43; Sprint = 0xA0; Prone = 0x5A;
        }

        public KeyBindings Clone() { return (KeyBindings)MemberwiseClone(); }

        public void Validate()
        {
            KeyBindings d = new KeyBindings();
            Forward = Vk(Forward, d.Forward); Back = Vk(Back, d.Back);
            Left = Vk(Left, d.Left); Right = Vk(Right, d.Right);
            LeanLeft = Vk(LeanLeft, d.LeanLeft); LeanRight = Vk(LeanRight, d.LeanRight);
            Fire = Vk(Fire, d.Fire); Ads = Vk(Ads, d.Ads); Toggle = Vk(Toggle, d.Toggle);
            FreeLook = Vk(FreeLook, d.FreeLook); Crouch = Vk(Crouch, d.Crouch); Sprint = Vk(Sprint, d.Sprint); Prone = Vk(Prone, d.Prone);
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

    /// <summary>
    /// Lean+move combo with its own TOTAL offset (move + lean, UI coords). Enabled = false keeps it additive (Dirs[dir] + lean).
    /// Fixed order: QA, QWA, QAS, ED, WED, EDS.
    /// </summary>
    [DataContract]
    public sealed class ComboSetting
    {
        [DataMember(Name = "Key")] public string Key;
        [DataMember(Name = "Enabled")] public bool Enabled;
        [DataMember(Name = "OffX")] public double OffX;
        [DataMember(Name = "OffY")] public double OffY;

        public ComboSetting() { }

        public ComboSetting(string key) { Key = key; }

        public ComboSetting Clone() { return (ComboSetting)MemberwiseClone(); }
    }

    /// <summary>
    /// 反向惯性 for one axis (左右 A↔D or 前后 W↔S), v2.1: when the key on that axis flips to the opposite side the
    /// ring eases to the middle (DecelMs), waits (PauseMs), then eases into the new direction (AccelMs).
    /// Invariant after Validate: DecelMs + PauseMs + AccelMs == TotalMs (whole ms). Enabled = false = plain 跟随.
    /// v2.0 files carry ReturnMs/PauseMs instead; Profile.Validate migrates them (LegacyReturnMs).
    /// </summary>
    [DataContract]
    public sealed class ReverseAxis
    {
        public static readonly string[] Shapes = new string[] { "smooth", "in", "out", "linear" }; // 慢起慢停 / 慢起 / 慢停 / 匀速
        public const double MaxTotalMs = 2000;

        [DataMember(Name = "Enabled")] public bool Enabled;
        [DataMember(Name = "TotalMs")] public double TotalMs;
        [DataMember(Name = "DecelMs")] public double DecelMs;
        [DataMember(Name = "PauseMs")] public double PauseMs;
        [DataMember(Name = "AccelMs")] public double AccelMs;
        [DataMember(Name = "DecelShape")] public string DecelShape;
        [DataMember(Name = "DecelStrength")] public double DecelStrength;
        [DataMember(Name = "AccelShape")] public string AccelShape;
        [DataMember(Name = "AccelStrength")] public double AccelStrength;
        /// <summary>v2.0 "回正" time; only present in files written by v2.0. Never written back (null).</summary>
        [DataMember(Name = "ReturnMs", EmitDefaultValue = false)] public double? LegacyReturnMs;

        public ReverseAxis() { SetDefaults(); }

        public ReverseAxis(bool enabled, double decelMs, double pauseMs, double accelMs)
        {
            SetDefaults();
            Enabled = enabled; DecelMs = decelMs; PauseMs = pauseMs; AccelMs = accelMs; TotalMs = decelMs + pauseMs + accelMs;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        /// <summary>Off, 400 = 100 / 200 / 100, both curves 慢起慢停 100 %.</summary>
        public void SetDefaults()
        {
            Enabled = false; TotalMs = 400; DecelMs = 100; PauseMs = 200; AccelMs = 100;
            DecelShape = "smooth"; DecelStrength = 100; AccelShape = "smooth"; AccelStrength = 100;
            LegacyReturnMs = null;
        }

        public ReverseAxis Clone() { return (ReverseAxis)MemberwiseClone(); }

        /// <summary>Clamp + migrate. followMs is the profile's 跟随时间 (v2.0 accel = 跟随).</summary>
        public void Validate(double followMs)
        {
            if (LegacyReturnMs.HasValue)
            {
                // v2.0 -> v2.1: Decel = ReturnMs, Pause = PauseMs, Accel = the profile's FollowMs, curves default.
                DecelMs = Clamp.D(LegacyReturnMs.Value, 0, 1000, 100);
                PauseMs = Clamp.D(PauseMs, 0, 1000, 200);
                AccelMs = Clamp.D(followMs, 0, 2000, 200);
                DecelShape = "smooth"; DecelStrength = 100; AccelShape = "smooth"; AccelStrength = 100;
                LegacyReturnMs = null;
                TotalMs = Math.Round(DecelMs) + Math.Round(PauseMs) + Math.Round(AccelMs);
            }
            DecelShape = Clamp.OneOf(DecelShape, Shapes);
            AccelShape = Clamp.OneOf(AccelShape, Shapes);
            DecelStrength = Math.Round(Clamp.D(DecelStrength, 0, 100, 100));
            AccelStrength = Math.Round(Clamp.D(AccelStrength, 0, 100, 100));
            DecelMs = Math.Round(Clamp.D(DecelMs, 0, MaxTotalMs, 100));
            PauseMs = Math.Round(Clamp.D(PauseMs, 0, MaxTotalMs, 200));
            AccelMs = Math.Round(Clamp.D(AccelMs, 0, MaxTotalMs, 100));
            TotalMs = Math.Round(Clamp.D(TotalMs, 0, MaxTotalMs, 400));
            double sum = DecelMs + PauseMs + AccelMs;
            if (sum != TotalMs)
            {
                // Inconsistent file: the phases win; too long -> scaled down to the maximum total.
                TotalMs = sum;
                if (TotalMs > MaxTotalMs) ReverseSplit.SetTotal(this, (int)MaxTotalMs);
            }
        }
    }

    /// <summary>
    /// 反向惯性 editing rules (pure): the three phases always add up to TotalMs exactly, nothing goes negative.
    /// </summary>
    public static class ReverseSplit
    {
        /// <summary>自动分配 ratios (from Leo's 100 / 200 / 100).</summary>
        public static readonly int[] AutoPct = new int[] { 25, 50, 25 };

        public static int[] Get(ReverseAxis r) { return new int[] { (int)r.DecelMs, (int)r.PauseMs, (int)r.AccelMs }; }

        private static void Put(ReverseAxis r, int[] ph, int total)
        {
            r.DecelMs = ph[0]; r.PauseMs = ph[1]; r.AccelMs = ph[2]; r.TotalMs = total;
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
            if (ph[big] < 0) // cannot happen for |diff| <= 2, but never leave a negative phase
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

        /// <summary>Change the total: all three scale by T'/T (auto ratios if T was 0).</summary>
        public static void SetTotal(ReverseAxis r, int newTotal)
        {
            if (newTotal < 0) newTotal = 0;
            if (newTotal > (int)ReverseAxis.MaxTotalMs) newTotal = (int)ReverseAxis.MaxTotalMs;
            int[] old = Get(r);
            int t = old[0] + old[1] + old[2];
            int[] ph;
            if (t <= 0) ph = AutoPhases(newTotal);
            else
            {
                ph = new int[3];
                for (int i = 0; i < 3; i++) ph[i] = RoundI(old[i] * (double)newTotal / t);
                FixSum(ph, newTotal);
            }
            Put(r, ph, newTotal);
        }

        /// <summary>Change one phase (0 减速, 1 停顿, 2 加速) to v (clamped 0..T); the other two share T - v in their old proportion.</summary>
        public static void SetPhase(ReverseAxis r, int index, int v)
        {
            int[] ph = Get(r);
            int t = (int)r.TotalMs;
            if (v < 0) v = 0;
            if (v > t) v = t;
            int a = (index + 1) % 3, b = (index + 2) % 3;
            int rest = t - v;
            int na;
            if (ph[a] + ph[b] <= 0) na = RoundI(rest / 2.0);
            else na = RoundI(rest * (double)ph[a] / (ph[a] + ph[b]));
            if (na > rest) na = rest;
            ph[index] = v; ph[a] = na; ph[b] = rest - na;
            Put(r, ph, t);
        }

        /// <summary>自动分配: T x 25 / 50 / 25, both curves 慢起慢停 100 %.</summary>
        public static void Auto(ReverseAxis r)
        {
            int t = (int)r.TotalMs;
            Put(r, AutoPhases(t), t);
            r.DecelShape = "smooth"; r.DecelStrength = 100; r.AccelShape = "smooth"; r.AccelStrength = 100;
        }

        /// <summary>s(u) = (1-k) u + k f(u), k = strength/100; f = 3u²-2u³ (smooth), u² (in), 1-(1-u)² (out), u (linear).</summary>
        public static double Ease(string shape, double strengthPct, double u)
        {
            if (u <= 0) return 0;
            if (u >= 1) return 1;
            double f;
            switch (shape)
            {
                case "in": f = u * u; break;
                case "out": f = 1 - (1 - u) * (1 - u); break;
                case "linear": f = u; break;
                default: f = u * u * (3 - 2 * u); break;
            }
            double k = Clamp.D(strengthPct, 0, 100, 100) / 100.0;
            double s = (1 - k) * u + k * f;
            if (s < 0) s = 0; if (s > 1) s = 1;
            return s;
        }
    }

    [DataContract]
    public sealed class Profile
    {
        public const int DirCount = 8;
        // Order: W, S, A, D, WA, WD, SA, SD
        public static readonly string[] DirNames = new string[] { "W 前", "S 后", "A 左", "D 右", "WA 左前", "WD 右前", "SA 左后", "SD 右后" };
        // Combos: key, lean side (-1 Q / +1 E), dir index, Chinese label. Same order as Combos.
        public const int ComboCount = 6;
        public static readonly string[] ComboKeys = new string[] { "QA", "QWA", "QAS", "ED", "WED", "EDS" };
        public static readonly int[] ComboLean = new int[] { -1, -1, -1, 1, 1, 1 };
        public static readonly int[] ComboDir = new int[] { 2, 4, 6, 3, 5, 7 };
        public static readonly string[] ComboLabels = new string[] { "左侧身+左移", "左侧身+左前", "左侧身+左后", "右侧身+右移", "右侧身+右前", "右侧身+右后" };

        /// <summary>Combo index 0..5 for this exact (lean target, dir) pair, else -1.</summary>
        public static int ComboIndex(int lean, int dir)
        {
            for (int i = 0; i < ComboCount; i++) if (ComboLean[i] == lean && ComboDir[i] == dir) return i;
            return -1;
        }

        public static readonly string[] Gradients = new string[] { "none", "radial", "sweep", "twotone" };

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

        /// <summary>跟随时间: ms for the ring to get ~95 % of the way to a new target (critically damped, no overshoot). 0 = instant.</summary>
        [DataMember(Name = "FollowMs")] public double FollowMs;

        [DataMember(Name = "Dirs")] public List<DirSetting> Dirs;

        /// <summary>侧身+移动（单独设置）: always 6 items in ComboKeys order.</summary>
        [DataMember(Name = "Combos")] public List<ComboSetting> Combos;

        /// <summary>反向惯性 左右 A↔D.</summary>
        [DataMember(Name = "ReverseLR")] public ReverseAxis ReverseLR;
        /// <summary>反向惯性 前后 W↔S.</summary>
        [DataMember(Name = "ReverseFB")] public ReverseAxis ReverseFB;

        [DataMember(Name = "RecoilMaxPx")] public double RecoilMaxPx;
        [DataMember(Name = "RecoilRiseMs")] public double RecoilRiseMs;
        [DataMember(Name = "RecoilClickKickPx")] public double RecoilClickKickPx;
        [DataMember(Name = "RecoilFallMs")] public double RecoilFallMs;
        [DataMember(Name = "RecoilDriftX")] public double RecoilDriftX;
        /// <summary>ms after the Fire press before this shot's click kick and the hold rise start (the bullet leaves before the kick).</summary>
        [DataMember(Name = "RecoilDelayMs")] public double RecoilDelayMs;

        // 蹲下（C）: while the tracker thinks you are crouched, recoil MAGNITUDES scale by a percentage (times unchanged).
        [DataMember(Name = "CrouchEnabled")] public bool CrouchEnabled;     // 蹲下时后坐力按百分比变化
        [DataMember(Name = "CrouchPct")] public double CrouchPct;           // 倍率 % (0..300), all three magnitudes
        [DataMember(Name = "CrouchSplit")] public bool CrouchSplit;         // 分开设置
        [DataMember(Name = "CrouchMaxPct")] public double CrouchMaxPct;     // 最高上抬 %
        [DataMember(Name = "CrouchKickPct")] public double CrouchKickPct;   // 额外上跳 %
        [DataMember(Name = "CrouchDriftPct")] public double CrouchDriftPct; // 水平偏移 %
        [DataMember(Name = "CrouchColorEnabled")] public bool CrouchColorEnabled; // 蹲下时圈换颜色
        [DataMember(Name = "CrouchColor")] public string CrouchColor;
        [DataMember(Name = "CrouchMoveEnabled")] public bool CrouchMoveEnabled; // 蹲着走时移动偏移按百分比
        [DataMember(Name = "CrouchMovePct")] public double CrouchMovePct;       // 0..300

        // 冲刺后恢复: dim the ring while sprinting and for SprintRecoveryMs after (opacity x SprintDimPct %).
        [DataMember(Name = "SprintDimEnabled")] public bool SprintDimEnabled;   // 冲刺时和刚停下时圈变暗
        [DataMember(Name = "SprintRecoveryMs")] public double SprintRecoveryMs; // 恢复开枪时间 0..2000
        [DataMember(Name = "SprintDimPct")] public double SprintDimPct;         // 变暗后透明度 0..100 (0 = hidden)

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

        public Profile() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); StandPresent = false; }

        public void SetDefaults()
        {
            standOffsetX = 0; standOffsetY = 0; StandPresent = true;
            Name = "配置";
            Diameter = 18; Thickness = 2; Feather = 1.0; OpacityPct = 60;
            Color = "#78FF78"; Color2 = "#FFFFFF"; Gradient = "none";
            CenterDot = false; CenterDotSize = 2; OuterThickness = 1.5;
            FollowMs = 200;
            Dirs = NewZeroDirs();
            Combos = NewDefaultCombos();
            ReverseLR = new ReverseAxis();
            ReverseFB = new ReverseAxis();
            RecoilMaxPx = 40; RecoilRiseMs = 300; RecoilClickKickPx = 8; RecoilFallMs = 250; RecoilDriftX = 0; RecoilDelayMs = 60;
            CrouchEnabled = false; CrouchPct = 100; CrouchSplit = false; CrouchMaxPct = 100; CrouchKickPct = 100; CrouchDriftPct = 100;
            CrouchColorEnabled = false; CrouchColor = "#FFD24A";
            CrouchMoveEnabled = false; CrouchMovePct = 100;
            SprintDimEnabled = false; SprintRecoveryMs = 300; SprintDimPct = 30;
            LeanQX = -30; LeanQY = -5; LeanEX = 30; LeanEY = -5; LeanMs = 150;
        }

        public static List<ComboSetting> NewDefaultCombos()
        {
            List<ComboSetting> l = new List<ComboSetting>();
            for (int i = 0; i < ComboCount; i++) l.Add(new ComboSetting(ComboKeys[i]));
            return l;
        }

        /// <summary>Full lean offset of one side (-1 = Q, +1 = E), UI coords.</summary>
        public void FullLean(int side, out double x, out double y)
        {
            if (side < 0) { x = LeanQX; y = LeanQY; }
            else if (side > 0) { x = LeanEX; y = LeanEY; }
            else { x = 0; y = 0; }
        }

        /// <summary>The additive total for a combo: Dirs[dir] + full lean of its side.</summary>
        public void AdditiveCombo(int combo, out double x, out double y)
        {
            double lx, ly;
            FullLean(ComboLean[combo], out lx, out ly);
            DirSetting d = Dirs[ComboDir[combo]];
            x = d.OffX + lx; y = d.OffY + ly;
        }

        /// <summary>"按叠加填入": every combo = Dirs + lean, and enabled.</summary>
        public static void FillCombosFromAdditive(Profile p)
        {
            for (int i = 0; i < ComboCount; i++)
            {
                double x, y;
                p.AdditiveCombo(i, out x, out y);
                p.Combos[i].OffX = Clamp.D(x, -500, 500, 0);
                p.Combos[i].OffY = Clamp.D(y, -500, 500, 0);
                p.Combos[i].Enabled = true;
            }
        }

        public static List<DirSetting> NewZeroDirs()
        {
            List<DirSetting> l = new List<DirSetting>();
            for (int i = 0; i < DirCount; i++) l.Add(new DirSetting());
            return l;
        }

        public Profile Clone()
        {
            Profile p = (Profile)MemberwiseClone();
            p.Dirs = new List<DirSetting>();
            if (Dirs != null)
                foreach (DirSetting d in Dirs) p.Dirs.Add(d == null ? new DirSetting() : d.Clone());
            p.Combos = new List<ComboSetting>();
            if (Combos != null)
                foreach (ComboSetting c in Combos) p.Combos.Add(c == null ? null : c.Clone());
            p.ReverseLR = ReverseLR == null ? new ReverseAxis() : ReverseLR.Clone();
            p.ReverseFB = ReverseFB == null ? new ReverseAxis() : ReverseFB.Clone();
            return p;
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

        /// <summary>Built-in default: Leo's hand-tuned modded M4 (values from his screenshots, 9 Oct 2026).</summary>
        public static Profile CreateLeoM4(string name)
        {
            Profile p = new Profile();
            p.Name = name;
            // 外观
            p.Diameter = 13; p.Thickness = 1.2; p.Feather = 1.0; p.OpacityPct = 60;
            p.Color = "#00DF00"; p.Color2 = "#000000"; p.Gradient = "twotone"; p.OuterThickness = 1.5;
            p.CenterDot = false; p.CenterDotSize = 2;
            // 移动
            p.Dirs[0] = new DirSetting(0, -20);       // W
            p.Dirs[1] = new DirSetting(0, 1);         // S
            p.Dirs[2] = new DirSetting(55, 1);        // A
            p.Dirs[3] = new DirSetting(-35, 1);       // D
            p.Dirs[4] = new DirSetting(38.9, -13.4);  // WA
            p.Dirs[5] = new DirSetting(-24.7, -13.4); // WD
            p.Dirs[6] = new DirSetting(38.9, 1.4);    // SA
            p.Dirs[7] = new DirSetting(-24.7, 1.4);   // SD
            p.Combos = NewDefaultCombos();
            p.FollowMs = 100;
            p.StandOffsetX = 0; p.StandOffsetY = 0;
            p.ReverseLR = new ReverseAxis(true, 100, 200, 100); // 400 = 100 / 200 / 100, 慢起慢停 100 %
            p.ReverseFB = new ReverseAxis(true, 100, 200, 100);
            // 后坐力
            p.RecoilMaxPx = 40; p.RecoilRiseMs = 260; p.RecoilClickKickPx = 8; p.RecoilFallMs = 230;
            p.RecoilDelayMs = 40; p.RecoilDriftX = 0;
            // 侧身
            p.LeanQX = -2; p.LeanQY = -8; p.LeanEX = 25; p.LeanEY = -5; p.LeanMs = 200;
            return p;
        }

        /// <summary>"斜向自动合成": Off = 0.7071*(a+b).</summary>
        public static void AutoDiagonal(Profile p)
        {
            p.Dirs[4] = Combine(p.Dirs[0], p.Dirs[2]); // WA = W + A
            p.Dirs[5] = Combine(p.Dirs[0], p.Dirs[3]); // WD = W + D
            p.Dirs[6] = Combine(p.Dirs[1], p.Dirs[2]); // SA = S + A
            p.Dirs[7] = Combine(p.Dirs[1], p.Dirs[3]); // SD = S + D
        }

        private static DirSetting Combine(DirSetting a, DirSetting b)
        {
            const double k = 0.7071;
            DirSetting r = new DirSetting();
            r.OffX = k * (a.OffX + b.OffX); r.OffY = k * (a.OffY + b.OffY);
            return r;
        }

        /// <summary>"左右镜像（A→D）": A→D, WA→WD, SA→SD; negate OffX.</summary>
        public static void MirrorLeftToRight(Profile p)
        {
            p.Dirs[3] = Mirror(p.Dirs[2]);
            p.Dirs[5] = Mirror(p.Dirs[4]);
            p.Dirs[7] = Mirror(p.Dirs[6]);
        }

        private static DirSetting Mirror(DirSetting s)
        {
            DirSetting r = s.Clone();
            r.OffX = -s.OffX;
            return r;
        }

        public static void ClearDirs(Profile p) { p.Dirs = NewZeroDirs(); }

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
            FollowMs = Clamp.D(FollowMs, 0, 2000, 200);
            if (Dirs == null) Dirs = NewZeroDirs();
            for (int i = 0; i < Dirs.Count; i++) if (Dirs[i] == null) Dirs[i] = new DirSetting();
            while (Dirs.Count < DirCount) Dirs.Add(new DirSetting());
            if (Dirs.Count > DirCount) Dirs.RemoveRange(DirCount, Dirs.Count - DirCount);
            foreach (DirSetting d in Dirs) d.Validate();
            // Combos: exactly 6, in key order, matched by Key (case-insensitive); unknown/duplicate entries dropped.
            List<ComboSetting> fixedCombos = NewDefaultCombos();
            if (Combos != null)
            {
                bool[] taken = new bool[ComboCount];
                foreach (ComboSetting c in Combos)
                {
                    if (c == null || c.Key == null) continue;
                    int idx = Array.IndexOf(ComboKeys, c.Key.Trim().ToUpperInvariant());
                    if (idx < 0 || taken[idx]) continue;
                    taken[idx] = true;
                    fixedCombos[idx].Enabled = c.Enabled;
                    fixedCombos[idx].OffX = Clamp.D(c.OffX, -500, 500, 0);
                    fixedCombos[idx].OffY = Clamp.D(c.OffY, -500, 500, 0);
                }
            }
            Combos = fixedCombos;
            if (ReverseLR == null) ReverseLR = new ReverseAxis();
            if (ReverseFB == null) ReverseFB = new ReverseAxis();
            ReverseLR.Validate(FollowMs);
            ReverseFB.Validate(FollowMs);
            RecoilMaxPx = Clamp.D(RecoilMaxPx, 0, 1000, 40);
            RecoilRiseMs = Clamp.D(RecoilRiseMs, 10, 5000, 300);
            RecoilClickKickPx = Clamp.D(RecoilClickKickPx, 0, 300, 8);
            RecoilFallMs = Clamp.D(RecoilFallMs, 10, 5000, 250);
            RecoilDriftX = Clamp.D(RecoilDriftX, -500, 500, 0);
            RecoilDelayMs = Clamp.D(RecoilDelayMs, 0, 300, 60);
            CrouchPct = Clamp.D(CrouchPct, 0, 300, 100);
            CrouchMaxPct = Clamp.D(CrouchMaxPct, 0, 300, 100);
            CrouchKickPct = Clamp.D(CrouchKickPct, 0, 300, 100);
            CrouchDriftPct = Clamp.D(CrouchDriftPct, 0, 300, 100);
            CrouchColor = ColorUtil.Normalize(CrouchColor, "#FFD24A");
            CrouchMovePct = Clamp.D(CrouchMovePct, 0, 300, 100);
            SprintRecoveryMs = Clamp.D(SprintRecoveryMs, 0, 2000, 300);
            SprintDimPct = Clamp.D(SprintDimPct, 0, 100, 30);
            LeanQX = Clamp.D(LeanQX, -500, 500, -30); LeanQY = Clamp.D(LeanQY, -500, 500, -5);
            LeanEX = Clamp.D(LeanEX, -500, 500, 30); LeanEY = Clamp.D(LeanEY, -500, 500, -5);
            LeanMs = Clamp.D(LeanMs, 0, 2000, 150);
            standOffsetX = Clamp.D(standOffsetX, -200, 200, 0);
            standOffsetY = Clamp.D(standOffsetY, -200, 200, 0);
        }

        /// <summary>A string that changes whenever anything affecting the raster changes.</summary>
        public string AppearanceKey()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                Diameter, Thickness, Feather, Color, Color2, Gradient, CenterDot, CenterDotSize, OuterThickness);
        }
    }

    [DataContract]
    public sealed class GlobalSettings
    {
        public const string DefaultProfileName = "M4（Leo 改装）";

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
        /// <summary>冲刺方式: "toggle" (切换, default) or "hold" (按住 = v2.2 behaviour).</summary>
        [DataMember(Name = "SprintMode")] public string SprintMode;
        /// <summary>分辨率换算: offsets were tuned at ResBase; scale s = ResGameH / ResBaseH on both axes.</summary>
        [DataMember(Name = "ResBaseW")] public double ResBaseW;
        [DataMember(Name = "ResBaseH")] public double ResBaseH;
        [DataMember(Name = "ResGameW")] public double ResGameW;
        [DataMember(Name = "ResGameH")] public double ResGameH;

        /// <summary>s = 游戏高度 / 调参高度 (1 when equal).</summary>
        public double ResolutionScale { get { return (ResBaseH > 0 && ResGameH > 0) ? ResGameH / ResBaseH : 1.0; } }
        [DataMember(Name = "Profiles")] public List<Profile> Profiles;

        // Runtime only (not saved).
        public PreviewMode PreviewMode;
        public double PreviewSpeed;

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
            ResBaseW = 2160; ResBaseH = 1440; ResGameW = 2160; ResGameH = 1440;
            SprintMode = "toggle";
            Profiles = new List<Profile>();
            PreviewMode = PreviewMode.Live; PreviewSpeed = 1.0;
        }

        /// <summary>Fresh install (no settings.json): ready to use with Leo's M4 setup as the only, active profile.</summary>
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
            g.SettingsTopMost = true; g.ShowSettingsOnStart = true; g.HideOnFreeLook = true;
            g.Profiles.Add(Profile.CreateLeoM4(DefaultProfileName));
            g.ActiveProfile = DefaultProfileName;
            return g;
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

        /// <summary>Adds validated copies of the profiles (clashing names get " (2)"), returns the first added name.</summary>
        /// <summary>恢复默认 M4: replace p's values with Profile.CreateLeoM4, keep its name; globals untouched. Returns the new profile.</summary>
        public Profile RestoreLeoM4(Profile p)
        {
            int idx = Profiles.IndexOf(p);
            Profile n = Profile.CreateLeoM4(p.Name);
            n.Validate();
            if (idx >= 0) Profiles[idx] = n;
            return n;
        }

        public string AddImported(List<Profile> imported)
        {
            string first = null;
            foreach (Profile src in imported)
            {
                Profile p = src.Clone();
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
            Keys.Validate();
            AdsMode = Clamp.OneOf(AdsMode, new string[] { "hold", "toggle" });
            LeanMode = Clamp.OneOf(LeanMode, new string[] { "hold", "toggle" });
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
            SprintMode = Clamp.OneOf(SprintMode, new string[] { "toggle", "hold" });
            ResBaseW = Math.Round(Clamp.D(ResBaseW, 640, 7680, 2160)); ResBaseH = Math.Round(Clamp.D(ResBaseH, 480, 4320, 1440));
            ResGameW = Math.Round(Clamp.D(ResGameW, 640, 7680, 2160)); ResGameH = Math.Round(Clamp.D(ResGameH, 480, 4320, 1440));
            PreviewSpeed = Clamp.D(PreviewSpeed, 0.1, 1.0, 1.0);

            if (Profiles == null) Profiles = new List<Profile>();
            Profiles.RemoveAll(delegate(Profile p) { return p == null; });
            if (Profiles.Count == 0)
            {
                GlobalSettings d = CreateDefault();
                Profiles.AddRange(d.Profiles);
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

        /// <summary>Pretty JSON of one profile (the 导出 file).</summary>
        public static string ExportProfile(Profile p)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Profile));
                using (System.Xml.XmlDictionaryWriter w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    ser.WriteObject(w, p);
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
                foreach (Profile p in list) p.Validate();
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
