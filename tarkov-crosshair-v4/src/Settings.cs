// PURE file: only System, System.Collections.Generic, System.IO, System.Text,
// System.Runtime.Serialization(.Json) (+ mscorlib reflection/globalization). Compiles under net48 (C# 5) and net8.0.
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

        // v4.0 组合键: modifier bitmask per rebindable key (ModCtrl | ModShift | ModAlt), 0 = plain key (older files: absent = 0).
        [DataMember(Name = "LeanLeftMods")] public int LeanLeftMods;
        [DataMember(Name = "LeanRightMods")] public int LeanRightMods;
        [DataMember(Name = "FireMods")] public int FireMods;
        [DataMember(Name = "AdsMods")] public int AdsMods;
        [DataMember(Name = "ToggleMods")] public int ToggleMods;
        [DataMember(Name = "FreeLookMods")] public int FreeLookMods;
        [DataMember(Name = "JumpMods")] public int JumpMods;
        [DataMember(Name = "HandSwitchMods")] public int HandSwitchMods;
        [DataMember(Name = "BoostMods")] public int BoostMods;

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
        public const int VkT = 0x54;
        /// <summary>v4.0: 前后左右 are fixed to W / S / A / D (not rebindable; older files are forced on load).</summary>
        public const int VkW = 0x57, VkA = 0x41, VkS = 0x53, VkD = 0x44;
        /// <summary>v4.0 组合键 modifier bits (left and right keys both count).</summary>
        public const int ModCtrl = 1, ModShift = 2, ModAlt = 4, ModMask = 7;
        public const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

        // Action table for the 按键 page (order = page order) and the "already used" check.
        public const int ActionCount = 13; // v4.3: the old action 13 (the mouse-sway turn key) is gone; its old JSON key is skipped
        public const int ActionHandSwitch = 4, ActionBoost = 12;
        public const int ActionToggle = 0, ActionFire = 1, ActionAds = 2, ActionFreeLook = 3, ActionLeanLeft = 5, ActionLeanRight = 6;
        public const int ActionForward = 7, ActionBack = 8, ActionLeft = 9, ActionRight = 10, ActionJump = 11;
        /// <summary>v4.0: the actions shown on the 按键 page (page order). 前进/后退/左移/右移 are fixed W/S/A/D.</summary>
        public static readonly int[] RebindableActions = new int[] { 0, 1, 2, 3, 4, 5, 6, 11, 12 };
        public static readonly string[] ActionNames = new string[]
        {
            "开关准星", "开火", "开镜", "自由视角（转头）", "换手", "左侧身", "右侧身", "前进", "后退", "左移", "右移", "跳跃", "强化准星"
        };

        public KeyBindings() { SetDefaults(); }

        /// <summary>v4.1: while a file is read every key starts at 0 (= "not in the file"); Validate gives a 0 key its default,
        /// exactly as before, and GlobalSettings.Validate can tell a key the file really set from one that only got its default
        /// (BRUTE40 P1-3: on a clash the default gives way).</summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext c)
        {
            SetDefaults();
            Forward = Back = Left = Right = LeanLeft = LeanRight = Fire = Ads = Toggle = FreeLook = Jump = 0;
            handSwitch = 0; boost = 0;
            BoostMods = 0; // v5.1: a file without BoostMods (pre-v4.0) = plain key, not the Ctrl of the new default
            HandSwitchPresent = false; BoostPresent = false;
        }

        public void SetDefaults()
        {
            Forward = VkW; Back = VkS; Left = VkA; Right = VkD;
            LeanLeft = 0x51; LeanRight = 0x45;
            Fire = 0x01; Ads = 0x02; Toggle = 0x7B; FreeLook = 0x04; Jump = VkSpace;
            handSwitch = VkXButton1; HandSwitchPresent = true;
            // v5.1 (Leo, decision 3 corrected): 强化准星 default = the game's 切换战术设备模式 key Ctrl+T (V51-RESEARCH §2);
            // v3.0-v5.0.2: XButton2. Files that have a Boost key keep it.
            boost = VkT; BoostPresent = true;
            LeanLeftMods = LeanRightMods = FireMods = AdsMods = ToggleMods = FreeLookMods = JumpMods = HandSwitchMods = 0;
            BoostMods = ModCtrl;
        }

        /// <summary>v4.0: 前后左右 are always W / S / A / D.</summary>
        public void ForceWasd() { Forward = VkW; Back = VkS; Left = VkA; Right = VkD; }

        /// <summary>前进 / 后退 / 左移 / 右移 (fixed keys, no modifiers, not on the 按键 page).</summary>
        public static bool IsMovementAction(int action) { return action >= ActionForward && action <= ActionRight; }

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

        /// <summary>Binds a key. v4.0: 前进/后退/左移/右移 are fixed W/S/A/D, so those actions are ignored.</summary>
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
                case 11: Jump = vk; break;
                case 12: Boost = vk; break;
                default: return;
            }
            // v4.0: re-normalise the kept modifiers against the new key (Ctrl+D then Set(Ctrl) = plain Ctrl, not Ctrl+Ctrl).
            SetMods(action, GetMods(action));
        }

        /// <summary>v4.0 组合键: modifier bitmask of an action (0 = plain key; movement actions always 0).</summary>
        public int GetMods(int action)
        {
            switch (action)
            {
                case 0: return ToggleMods;
                case 1: return FireMods;
                case 2: return AdsMods;
                case 3: return FreeLookMods;
                case 4: return HandSwitchMods;
                case 5: return LeanLeftMods;
                case 6: return LeanRightMods;
                case 11: return JumpMods;
                case 12: return BoostMods;
                default: return 0;
            }
        }

        /// <summary>Sets an action's modifiers (normalised against its current key). Movement actions are ignored.</summary>
        public void SetMods(int action, int mods)
        {
            int m = NormalizeMods(Get(action), mods);
            switch (action)
            {
                case 0: ToggleMods = m; break;
                case 1: FireMods = m; break;
                case 2: AdsMods = m; break;
                case 3: FreeLookMods = m; break;
                case 4: HandSwitchMods = m; break;
                case 5: LeanLeftMods = m; break;
                case 6: LeanRightMods = m; break;
                case 11: JumpMods = m; break;
                case 12: BoostMods = m; break;
            }
        }

        /// <summary>Key capture result: key + modifiers in one call (e.g. 右侧身 = Ctrl+D). Movement actions are ignored.</summary>
        public void SetCombo(int action, int vk, int mods)
        {
            if (IsMovementAction(action)) return;
            Set(action, vk);
            SetMods(action, mods);
        }

        /// <summary>Display text of an action's binding, e.g. 「Ctrl+D」.</summary>
        public string ComboOf(int action) { return ComboName(Get(action), GetMods(action)); }

        /// <summary>The first action other than 'except' bound to vk with no modifiers, or -1.</summary>
        public int FindOther(int vk, int except) { return FindOtherCombo(vk, 0, except); }

        /// <summary>The first action other than 'except' bound to exactly the same key AND modifiers, or -1.
        /// Ctrl+D (右侧身) and D (右移) are different bindings: that is what 组合键 are for.</summary>
        public int FindOtherCombo(int vk, int mods, int except)
        {
            int m = NormalizeMods(vk, mods);
            for (int a = 0; a < ActionCount; a++) if (a != except && Get(a) == vk && GetMods(a) == m) return a;
            return -1;
        }

        /// <summary>Shift / Ctrl / Alt (generic or left/right virtual-key codes).</summary>
        public static bool IsModifierVk(int vk) { return ModBitOf(vk) != 0; }

        /// <summary>The Mod* bit of a modifier key, else 0.</summary>
        public static int ModBitOf(int vk)
        {
            switch (vk)
            {
                case 0x11: case 0xA2: case 0xA3: return ModCtrl;
                case 0x10: case 0xA0: case 0xA1: return ModShift;
                case 0x12: case 0xA4: case 0xA5: return ModAlt;
                default: return 0;
            }
        }

        /// <summary>mods limited to Ctrl/Shift/Alt, without the key's own bit (Ctrl+Ctrl = Ctrl).</summary>
        public static int NormalizeMods(int vk, int mods)
        {
            return mods & ModMask & ~ModBitOf(vk);
        }

        /// <summary>Bitmask of the modifiers held right now (key capture: hold Ctrl, press D -> ModCtrl).</summary>
        public static int ModsDown(Func<int, bool> isDown)
        {
            int m = 0;
            if (isDown(VkControl)) m |= ModCtrl;
            if (isDown(VkShift)) m |= ModShift;
            if (isDown(VkMenu)) m |= ModAlt;
            return m;
        }

        /// <summary>「Ctrl+Shift+Alt+D」 (that order); mods 0 = just the key name.</summary>
        public static string ComboName(int vk, int mods)
        {
            int m = NormalizeMods(vk, mods);
            StringBuilder sb = new StringBuilder();
            if ((m & ModCtrl) != 0) sb.Append("Ctrl+");
            if ((m & ModShift) != 0) sb.Append("Shift+");
            if ((m & ModAlt) != 0) sb.Append("Alt+");
            sb.Append(KeyName(vk));
            return sb.ToString();
        }

        /// <summary>Pure key name of a Windows virtual-key code (no WinForms).</summary>
        public static string KeyName(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();               // A..Z
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();               // 0..9
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);               // F1..F24
            if (vk >= 0x60 && vk <= 0x69) return "小键盘" + (vk - 0x60);               // NumPad0..9
            switch (vk)
            {
                case 0x01: return "鼠标左键";
                case 0x02: return "鼠标右键";
                case 0x04: return "鼠标中键";
                case 0x05: return "鼠标侧键1（XButton1）";
                case 0x06: return "鼠标侧键2（XButton2）";
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x6A: return "小键盘*";
                case 0x6B: return "小键盘+";
                case 0x6D: return "小键盘-";
                case 0x6E: return "小键盘.";
                case 0x6F: return "小键盘/";
                case 0x90: return "NumLock";
                case 0x91: return "ScrollLock";
                case 0xA0: return "左Shift";
                case 0xA1: return "右Shift";
                case 0xA2: return "左Ctrl";
                case 0xA3: return "右Ctrl";
                case 0xA4: return "左Alt";
                case 0xA5: return "右Alt";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
                default: return "键" + vk.ToString("X2");
            }
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
            if (boost < 1 || boost > 254) { boost = d.Boost; BoostMods = d.BoostMods; } // v5.1: default key WITH its Ctrl
            BoostPresent = bp;
            ForceWasd(); // v4.0: 前后左右 fixed
            LeanLeftMods = NormalizeMods(LeanLeft, LeanLeftMods); LeanRightMods = NormalizeMods(LeanRight, LeanRightMods);
            FireMods = NormalizeMods(Fire, FireMods); AdsMods = NormalizeMods(Ads, AdsMods);
            ToggleMods = NormalizeMods(Toggle, ToggleMods); FreeLookMods = NormalizeMods(FreeLook, FreeLookMods);
            JumpMods = NormalizeMods(Jump, JumpMods);
            HandSwitchMods = NormalizeMods(handSwitch, HandSwitchMods); BoostMods = NormalizeMods(boost, BoostMods);
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

        /// <summary>v4.1 (BRUTE40 P2-2): a curve read from a file without a usable Speeds list gets null here, so Validate gives
        /// it the 推荐 speeds instead of the deleted v2.3 feel (v1-v2.3 files have no curve at all: Profile.Validate migrates
        /// them from FollowMs and keeps their old feel).</summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); Speeds = null; }

        /// <summary>Class default: 100 ms with the old (pre-v2.4) speeds. Not offered in the UI any more (v4.0); new profiles use
        /// SetRecommended, and a curve read from a file without Speeds gets 推荐 (see OnDeserializing).</summary>
        public void SetDefaults() { DurationMs = DefaultMs; Speeds = MotionCurve.V23Feel(); }

        public MoveCurve Clone()
        {
            MoveCurve c = (MoveCurve)MemberwiseClone();
            c.Speeds = Speeds == null ? (double[])Profile.RecommendedSpeeds.Clone() : (double[])Speeds.Clone();
            return c;
        }

        public void SetPreset(int index) { Speeds = (double[])MotionCurve.Presets[index].Clone(); }

        /// <summary>推荐 = Leo's curve (v5.0.2: the one of the built-in 「Leo默认」, 430 ms, 0 / 15 / 45 / 75 / 100).</summary>
        public void SetRecommended()
        {
            DurationMs = Profile.RecommendedCurveMs;
            Speeds = (double[])Profile.RecommendedSpeeds.Clone();
        }

        /// <summary>True when this curve is exactly the 推荐 curve.</summary>
        public bool IsRecommended
        {
            get
            {
                if (DurationMs != Profile.RecommendedCurveMs || Speeds == null || Speeds.Length != MotionCurve.Points) return false;
                double[] r = Profile.RecommendedSpeeds;
                for (int i = 0; i < MotionCurve.Points; i++) if (Speeds[i] != r[i]) return false;
                return true;
            }
        }

        public void Validate()
        {
            DurationMs = Clamp.D(DurationMs, 0, MotionCurve.MaxDurationMs, DefaultMs);
            // v4.0: a dropped / short Speeds falls back to 推荐 (v2.3 手感 is deleted; v1-v2.3 files keep it via SetDefaults / FollowMs migration).
            double[] def = (Speeds == null || Speeds.Length < MotionCurve.Points)
                ? Profile.RecommendedSpeeds : MotionCurve.V23Feel();
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
    /// 强化准星 look per profile (v3.0). Until v5.0.1 (OwnLook false) the shape and every dimension not listed in v3.0 come from
    /// the normal look (the old files and the built-in 「Leo默认」 still work that way). v5.0.2 (Leo 18 Nov 2026, decision 4):
    /// OwnLook true = a complete look of its own, every 外观 option of the normal crosshair (style, size, thickness, gap,
    /// colour / gradient, outline, feather, glow, rotation, 中心留空, custom PNG); FollowHandColor still borrows the normal
    /// look's colours (incl. 换手颜色). Profile.FillBoostView turns it into a Profile for drawing / editing.
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

        // ---- v5.0.2: the rest of a full look (used only when OwnLook) ----
        /// <summary>v5.0.2: true = the members below are this look's own; false (files before v5.0.2) = the v3.0 rule.</summary>
        [DataMember(Name = "OwnLook")] public bool OwnLook;
        [DataMember(Name = "Feather")] public double Feather;
        [DataMember(Name = "Gradient")] public string Gradient;
        [DataMember(Name = "CenterDot")] public bool CenterDot;
        [DataMember(Name = "CenterDotSize")] public double CenterDotSize;
        [DataMember(Name = "Shape")] public string Shape;
        [DataMember(Name = "CrossLength")] public double CrossLength;
        [DataMember(Name = "CrossThickness")] public double CrossThickness;
        [DataMember(Name = "CrossGap")] public double CrossGap;
        [DataMember(Name = "CrossTop")] public bool CrossTop;
        [DataMember(Name = "Dashes")] public int Dashes;
        [DataMember(Name = "DashGapPct")] public double DashGapPct;
        [DataMember(Name = "RotationDeg")] public double RotationDeg;
        [DataMember(Name = "Style")] public CrosshairStyle Style;

        public BoostStyle() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        public void SetDefaults()
        {
            Diameter = 24; Thickness = 2.4; OuterThickness = 2.5; OpacityPct = 100;
            GlowPx = 6; GlowOpacityPct = 60; FollowHandColor = true;
            Color = "#00FF00"; Color2 = "#000000";
            OwnLook = false;
            Feather = 1.0; Gradient = "twotone"; CenterDot = false; CenterDotSize = 2;
            Shape = "ring"; CrossLength = 6; CrossThickness = 1.5; CrossGap = 4; CrossTop = true;
            Dashes = 0; DashGapPct = 30; RotationDeg = 0;
            Style = new CrosshairStyle();
        }

        public BoostStyle Clone()
        {
            BoostStyle b = (BoostStyle)MemberwiseClone();
            b.Style = Style == null ? new CrosshairStyle() : Style.Clone();
            return b;
        }

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
            // v5.0.2: the same ranges as the normal look (Profile.Validate)
            Feather = Clamp.D(Feather, 0, 30, 1.0);
            Gradient = Clamp.OneOf(Gradient, Profile.Gradients);
            CenterDotSize = Clamp.D(CenterDotSize, 1, 10, 2);
            Shape = Clamp.OneOf(Shape, Profile.Shapes);
            CrossLength = Clamp.D(CrossLength, 0, 200, 6);
            CrossThickness = Clamp.D(CrossThickness, 0.5, 30, 1.5);
            CrossGap = Clamp.D(CrossGap, 0, 200, 4);
            if (Dashes < 2) Dashes = 0;
            if (Dashes > 24) Dashes = 24;
            DashGapPct = Clamp.D(DashGapPct, 0, 90, 30);
            RotationDeg = Clamp.D(RotationDeg, -100000, 100000, 0) % 360.0;
            if (RotationDeg > 180) RotationDeg -= 360;
            if (RotationDeg < -180) RotationDeg += 360;
            if (Style == null) Style = new CrosshairStyle();
            Style.Validate();
        }

        public string Key()
        {
            string k = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                Diameter, Thickness, OuterThickness, OpacityPct, GlowPx, GlowOpacityPct, FollowHandColor, Color, Color2);
            if (!OwnLook) return k;
            return k + string.Format(System.Globalization.CultureInfo.InvariantCulture, "|own|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|",
                Feather, Gradient, CenterDot, CenterDotSize, Shape, CrossLength, CrossThickness, CrossGap, CrossTop, Dashes, DashGapPct, RotationDeg)
                + (Style == null ? "" : Style.Key());
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
        /// <summary>v4.0 准星样式 (StyleShapes.cs). Kind "" = the v3 shapes (Shape field) unchanged.</summary>
        [DataMember(Name = "Style")] public CrosshairStyle Style;

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
        // v5.1 (decision 5) 站立静止偏移: StandAsZero = on. Then StandOffsetX/Y (调参 px, UI coords like Dirs, ±200) is the new
        // zero: the engine adds it to every target (Motion: pos + lean + recoil + stand), and the 8 direction offsets are stored
        // RELATIVE to it (SetStandPoint converts them so every direction stays at the same place on screen). Off -> stand 0.
        private bool standAsZero;
        /// <summary>False only while reading a file without StandAsZero (v5.0.2 and older: stand is forced to 0 as before).</summary>
        public bool StandAsZeroPresent;
        [DataMember(Name = "StandAsZero")]
        public bool StandAsZero { get { return standAsZero; } set { standAsZero = value; StandAsZeroPresent = true; } }
        public const double StandLimitPx = 200;

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

        // v4.3 (Leo): the mouse-sway feature is removed. Old files (v4.1 / v4.2) still carry its 8 profile members: no
        // member has those names any more, so the reader skips them (nothing is applied) and a save no longer writes them.

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
            StandAsZeroPresent = false;
            CurveAD = null; CurveWS = null; // absent in the file -> Validate derives them from FollowMs
        }

        public void SetDefaults()
        {
            standOffsetX = 0; standOffsetY = 0; StandPresent = true;
            standAsZero = false; StandAsZeroPresent = true;
            Name = "配置";
            Diameter = 18; Thickness = 2; Feather = 1.0; OpacityPct = 60;
            Color = "#78FF78"; Color2 = "#FFFFFF"; Gradient = "none";
            CenterDot = false; CenterDotSize = 2; OuterThickness = 1.5;
            Shape = "ring"; CrossLength = 6; CrossThickness = 1.5; CrossGap = 4; CrossTop = true;
            Dashes = 0; DashGapPct = 30; GlowPx = 0; GlowOpacityPct = 40; RotationDeg = 0;
            HandColorOn = true; HandColor = "#FF9500"; HandColor2 = "#000000";
            Boost = new BoostStyle();
            Style = new CrosshairStyle();
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
            p.Style = Style == null ? new CrosshairStyle() : Style.Clone();
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

        /// <summary>The "空白（全0）" profile (「新建配置」): all dirs, lean and recoil amounts 0, 跳跃 off, 换手 all 自动.
        /// v5.0.2 (Leo, decision 3: every 默认 is his file): 外观, 强化准星, 换手颜色 and every time (曲线 = 推荐, 后坐力 / 侧身 /
        /// 换手 / 跳跃 times) are the built-in 「Leo默认」's; before v5.0.2 they were the class placeholders.</summary>
        public static Profile CreateBlank(string name)
        {
            Profile p = CreateLeoDefault(name);
            p.Dirs = NewZeroDirs();
            p.StandOffsetX = 0; p.StandOffsetY = 0;
            p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0; p.RecoilDriftX = 0;
            p.LeanQX = 0; p.LeanQY = 0; p.LeanEX = 0; p.LeanEY = 0;
            p.AllHandAuto();
            if (p.Jump == null) p.Jump = new JumpSettings();
            p.Jump.Enabled = false;
            if (p.Jump.Phases != null) foreach (JumpPhase ph in p.Jump.Phases) if (ph != null) ph.EndPx = 0;
            p.ResetCurvesToRecommended(); // v4.1 (BRUTE40 P1-1): not the deleted v2.3 curve (MoveCurve's class default)
            p.SavedDefault = null; p.SavedDefaultAt = null;
            return p;
        }

        /// <summary>
        /// The only built-in profile 「Leo默认」 (v3.1): every value from Leo's best-tested v3.0 settings.json (27 Oct 2026);
        /// 换手自定义 all 自动. How these map to the game is UNVERIFIED.
        /// </summary>
        /// <summary>推荐曲线 (Leo's), both axes. v5.0.2 (Leo 18 Nov 2026, UI-C H1): the curve of the built-in 「Leo默认」 (his
        /// LeoDefaultJson profile 「Leo默认 (旧)」): 430 ms, speeds 0 / 15 / 45 / 75 / 100. v4.0-v5.0.1: 380 ms 慢起.</summary>
        public const double RecommendedCurveMs = 430;
        /// <summary>The 推荐 speeds at 0 / 25 / 50 / 75 / 100 % (read-only by convention: callers clone).</summary>
        public static readonly double[] RecommendedSpeeds = new double[] { 0, 15, 45, 75, 100 };

        /// <summary>Both curves back to the 推荐 curve.</summary>
        public void ResetCurvesToRecommended()
        {
            if (CurveAD == null) CurveAD = new MoveCurve();
            if (CurveWS == null) CurveWS = new MoveCurve();
            CurveAD.SetRecommended();
            CurveWS.SetRecommended();
        }

        /// <summary>v5.0: true while CreateLeoDefault parses LeoDefaultJson (a parse that needs a default gets the old values).</summary>
        [ThreadStatic] private static bool leoDefaultParsing;
        /// <summary>v5.0: the parsed 「Leo默认 (旧)」 values (parsed once; callers always get a deep copy).</summary>
        private static volatile Profile leoDefaultCache;
        /// <summary>v5.0.1: the one parse of LeoDefaultJson failed; every later call returns the legacy values at once.</summary>
        private static volatile bool leoDefaultFailed;

        /// <summary>v5.0 (Leo 18 Nov 2026): the built-in default = the values of profile 「Leo默认 (旧)」 in Leo's own
        /// settings.json (LeoDefaultJson.Text, tuned at 2160×1440 / FOV 75), under the given name. Falls back to the v3.1–v4.3.1
        /// values (CreateLeoDefaultLegacy) if that text cannot be read.</summary>
        public static Profile CreateLeoDefault(string name)
        {
            Profile cached = leoDefaultCache;
            if (cached == null && !leoDefaultParsing && !leoDefaultFailed)
            {
                leoDefaultParsing = true;
                try
                {
                    GlobalSettings g; string err;
                    if (SettingsIO.TryParse(LeoDefaultJson.Text, out g, out err) && g != null)
                    {
                        Profile src = g.FindProfile(LeoDefaultJson.ProfileName) ?? g.FindProfile(g.ActiveProfile);
                        if (src != null) { cached = src.CloneValues(); leoDefaultCache = cached; }
                    }
                }
                catch (Exception) { cached = null; }
                finally { leoDefaultParsing = false; }
                if (cached == null) leoDefaultFailed = true; // v5.0.1: parse once even when it fails (no re-parse per call)
            }
            if (cached == null) return CreateLeoDefaultLegacy(name);
            Profile p = cached.CloneValues();
            p.Name = name;
            return p;
        }

        /// <summary>The v3.1–v4.3.1 built-in default values (before v5.0); used only as the fallback of CreateLeoDefault.</summary>
        public static Profile CreateLeoDefaultLegacy(string name)
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
            // 移动速率曲线: both axes 380 ms 慢起 (0 / 25 / 50 / 75 / 100), the v4.0-v5.0.1 推荐 (legacy values kept as they were)
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
            // v5.1: cardinals are summed FROM THE SCREEN CENTRE (stored + stand) and the result is stored relative to the
            // stand point, so a diagonal keeps its place when 站立静止偏移 changes. Stand 0 = exactly the v3.0 rule.
            double sx = p.standAsZero ? p.standOffsetX : 0, sy = p.standAsZero ? p.standOffsetY : 0;
            p.Dirs[4] = Combine(p.Dirs[0], p.Dirs[2], sx, sy); // WA = W + A
            p.Dirs[5] = Combine(p.Dirs[0], p.Dirs[3], sx, sy); // WD = W + D
            p.Dirs[6] = Combine(p.Dirs[1], p.Dirs[2], sx, sy); // SA = S + A
            p.Dirs[7] = Combine(p.Dirs[1], p.Dirs[3], sx, sy); // SD = S + D
        }

        /// <summary>Same as RecomputeDiagonals (the v1–v2.4 name, kept).</summary>
        public static void AutoDiagonal(Profile p) { RecomputeDiagonals(p); }

        private static DirSetting Combine(DirSetting a, DirSetting b, double sx, double sy)
        {
            const double k = 0.7071;
            DirSetting r = new DirSetting();
            if (sx == 0 && sy == 0) { r.OffX = k * (a.OffX + b.OffX); r.OffY = k * (a.OffY + b.OffY); return r; } // bit-identical v3.0
            r.OffX = k * (a.OffX + b.OffX + 2 * sx) - sx; r.OffY = k * (a.OffY + b.OffY + 2 * sy) - sy;
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
            HandValue(HandStand, out v.SX, out v.SY);
            // same rule as RecomputeDiagonals: 0.7071 × the sum of the two cardinals measured FROM THE SCREEN CENTRE, stored
            // relative to the stand point (v5.1; stand 0 = exactly the v3.0 sums)
            const double k = 0.7071;
            double sx = standAsZero ? v.SX : 0, sy = standAsZero ? v.SY : 0;
            v.DX[4] = k * (v.DX[0] + v.DX[2] + 2 * sx) - sx; v.DY[4] = k * (v.DY[0] + v.DY[2] + 2 * sy) - sy; // WA = W + A
            v.DX[5] = k * (v.DX[0] + v.DX[3] + 2 * sx) - sx; v.DY[5] = k * (v.DY[0] + v.DY[3] + 2 * sy) - sy; // WD = W + D
            v.DX[6] = k * (v.DX[1] + v.DX[2] + 2 * sx) - sx; v.DY[6] = k * (v.DY[1] + v.DY[2] + 2 * sy) - sy; // SA = S + A
            v.DX[7] = k * (v.DX[1] + v.DX[3] + 2 * sx) - sx; v.DY[7] = k * (v.DY[1] + v.DY[3] + 2 * sy) - sy; // SD = S + D
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
            if (Style == null) Style = new CrosshairStyle();
            Style.Validate();
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
            // v5.1 (decision 5): 站立静止偏移 is optional. Off, or a file from before v5.1 (no StandAsZero): standing still is the
            // screen centre (stand 0, the v4.1 rule; old v1.3-v3.3 values are not revived). On: clamped to ±StandLimitPx.
            if (!StandAsZeroPresent || !standAsZero) { standOffsetX = 0; standOffsetY = 0; standAsZero = false; }
            else
            {
                standOffsetX = Clamp.D(standOffsetX, -StandLimitPx, StandLimitPx, 0);
                standOffsetY = Clamp.D(standOffsetY, -StandLimitPx, StandLimitPx, 0);
            }
            StandAsZeroPresent = true; // (before RecomputeDiagonals: the diagonals use the stand point)
            // v5.1: W/S/A/D are stored relative to the stand point; their ±500 limit applies FROM THE SCREEN CENTRE, so a value
            // converted by SetStandPoint (up to ±700 stored) survives a save + reload unchanged.
            double vsx = standAsZero ? standOffsetX : 0, vsy = standAsZero ? standOffsetY : 0;
            for (int i = 0; i < Dirs.Count; i++)
            {
                DirSetting d = Dirs[i];
                if (i >= 4 || (vsx == 0 && vsy == 0)) { d.Validate(); continue; }
                d.OffX += vsx; d.OffY += vsy;
                d.Validate();
                d.OffX -= vsx; d.OffY -= vsy;
            }
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
            HandSwitchMs = Clamp.D(HandSwitchMs, 0, 2000, 300);
            EnsureHandList(); // v3.1 换手自定义: exactly 8 items; older files have none (all 自动)
            for (int i = 0; i < HandItemCount; i++)
            {
                HandCustomItem h = HandCustom[i];
                double lim = i == HandStand ? 200 : 500;
                h.X = Clamp.D(h.X, -lim, lim, 0);
                h.Y = i == HandDrift ? 0 : Clamp.D(h.Y, -lim, lim, 0);
            }
            HandCustom[HandStand] = new HandCustomItem(); // v4.1: the 换手 站着偏移 item is always 自动 (mirror of 0 = 0)
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

        /// <summary>
        /// v5.1 (decision 5): moves the zero to the standing point (x, y) (调参 px, UI coords, clamped ±StandLimitPx) and converts
        /// the 4 user directions W/S/A/D (normal hand, and 换手 items that are 自定义) by the change, so every direction keeps its
        /// on-screen position: new = old − (stand_new − stand_old). Diagonals are recomputed from the centre-based cardinals
        /// (RecomputeDiagonals), so they keep their place too. (0, 0) = off (StandAsZero false). Lean / recoil / jump are deltas added
        /// on top and are not converted. Returns false (nothing changed) when the values are not finite.
        /// </summary>
        public bool SetStandPoint(double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) return false;
            x = Math.Max(-StandLimitPx, Math.Min(StandLimitPx, x));
            y = Math.Max(-StandLimitPx, Math.Min(StandLimitPx, y));
            double dx = x - (standAsZero ? standOffsetX : 0), dy = y - (standAsZero ? standOffsetY : 0);
            EnsureHandList();
            if (Dirs != null)
                for (int i = 0; i < 4 && i < Dirs.Count; i++) { Dirs[i].OffX -= dx; Dirs[i].OffY -= dy; }
            // 换手 自定义 items: the hand is mirrored (x -> −x), so the mirrored stand point moves by (−dx, dy)
            for (int i = 0; i < 4; i++)
                if (HandCustom[i].Custom) { HandCustom[i].X -= -dx; HandCustom[i].Y -= dy; }
            standOffsetX = x; standOffsetY = y;
            standAsZero = x != 0 || y != 0;
            RecomputeDiagonals(this);
            return true;
        }

        /// <summary>v5.1: absolute (from the screen centre) position of direction i of the normal hand = stored + stand.</summary>
        public void DirFromCentre(int i, out double x, out double y)
        {
            x = Dirs[i].OffX + (standAsZero ? standOffsetX : 0);
            y = Dirs[i].OffY + (standAsZero ? standOffsetY : 0);
        }

        /// <summary>A string that changes whenever anything affecting the raster changes (both hands, both looks).</summary>
        public string AppearanceKey()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}",
                Diameter, Thickness, Feather, Color, Color2, Gradient, CenterDot, CenterDotSize, OuterThickness,
                Shape, CrossLength, CrossThickness, CrossGap, CrossTop, Dashes, DashGapPct, GlowPx, GlowOpacityPct, RotationDeg,
                HandColorOn, HandColor, HandColor2, Boost == null ? "" : Boost.Key()) + "|" + (Style == null ? "" : Style.Key());
        }

        // ---------------- v5.0.2 强化准星 with a look of its own (Leo, decision 4) ----------------

        /// <summary>
        /// v5.0.2: dst's 外观 = this profile's 强化 look as a plain (not boosted) Profile, for drawing (RingStyle.Set with
        /// boosted = false) and for the 外观 page's 强化 editor. dst.Style IS Boost.Style (shared, not copied). Colours:
        /// FollowHandColor = this profile's colours and 换手颜色, else Boost.Color / Color2 without 换手颜色. Only for
        /// Boost.OwnLook (a pre-v5.0.2 boost is drawn by the v3.0 rule). No allocation.
        /// </summary>
        public void FillBoostView(Profile dst)
        {
            BoostStyle b = Boost;
            if (dst == null || b == null) return;
            dst.Diameter = b.Diameter; dst.Thickness = b.Thickness; dst.Feather = b.Feather; dst.OpacityPct = b.OpacityPct;
            dst.Gradient = b.Gradient; dst.CenterDot = b.CenterDot; dst.CenterDotSize = b.CenterDotSize; dst.OuterThickness = b.OuterThickness;
            dst.Shape = b.Shape; dst.CrossLength = b.CrossLength; dst.CrossThickness = b.CrossThickness; dst.CrossGap = b.CrossGap;
            dst.CrossTop = b.CrossTop; dst.Dashes = b.Dashes; dst.DashGapPct = b.DashGapPct; dst.GlowPx = b.GlowPx;
            dst.GlowOpacityPct = b.GlowOpacityPct; dst.RotationDeg = b.RotationDeg;
            if (b.Style == null) b.Style = new CrosshairStyle();
            dst.Style = b.Style;
            if (b.FollowHandColor)
            {
                dst.Color = Color; dst.Color2 = Color2;
                dst.HandColorOn = HandColorOn; dst.HandColor = HandColor; dst.HandColor2 = HandColor2;
            }
            else
            {
                dst.Color = b.Color; dst.Color2 = b.Color2;
                dst.HandColorOn = false; dst.HandColor = b.Color; dst.HandColor2 = b.Color2;
            }
        }

        /// <summary>v5.0.2: the reverse of FillBoostView after src (the 强化 editor's Profile) was edited: Boost = src's 外观,
        /// OwnLook on. Colours are taken only when the boost does not follow the hand colours.</summary>
        public void StoreBoostView(Profile src)
        {
            if (src == null) return;
            if (Boost == null) Boost = new BoostStyle();
            BoostStyle b = Boost;
            b.Diameter = src.Diameter; b.Thickness = src.Thickness; b.Feather = src.Feather; b.OpacityPct = src.OpacityPct;
            b.Gradient = src.Gradient; b.CenterDot = src.CenterDot; b.CenterDotSize = src.CenterDotSize; b.OuterThickness = src.OuterThickness;
            b.Shape = src.Shape; b.CrossLength = src.CrossLength; b.CrossThickness = src.CrossThickness; b.CrossGap = src.CrossGap;
            b.CrossTop = src.CrossTop; b.Dashes = src.Dashes; b.DashGapPct = src.DashGapPct; b.GlowPx = src.GlowPx;
            b.GlowOpacityPct = src.GlowOpacityPct; b.RotationDeg = src.RotationDeg;
            b.Style = src.Style ?? new CrosshairStyle();
            if (!b.FollowHandColor) { b.Color = src.Color; b.Color2 = src.Color2; }
            b.OwnLook = true;
        }

        /// <summary>
        /// v5.0.2: turns a pre-v5.0.2 强化 look (OwnLook false: shape etc. from the normal look) into a look of its own that
        /// draws the same: the normal look's shape values + the boost's own size / thickness / outline / opacity / glow /
        /// colours. gap, length and hole = the style gap, length and 中心留空 the v3.0 rule drew (RingStyle.From(p, 0, true):
        /// SGap, SLength, Hole). Does nothing when OwnLook is already on.
        /// </summary>
        public void BeginOwnBoost(double gap, double length, double hole)
        {
            if (Boost == null) Boost = new BoostStyle();
            BoostStyle b = Boost;
            if (b.OwnLook) return;
            b.Feather = Feather; b.Gradient = Gradient; b.CenterDot = CenterDot; b.CenterDotSize = CenterDotSize;
            b.Shape = Shape; b.CrossLength = CrossLength; b.CrossThickness = CrossThickness; b.CrossGap = CrossGap; b.CrossTop = CrossTop;
            b.Dashes = Dashes; b.DashGapPct = DashGapPct; b.RotationDeg = RotationDeg;
            CrosshairStyle s = Style == null ? new CrosshairStyle() : Style.Clone();
            if (!double.IsNaN(gap) && !double.IsInfinity(gap)) s.Gap = gap;
            if (!double.IsNaN(length) && !double.IsInfinity(length)) s.Length = length;
            if (!double.IsNaN(hole) && !double.IsInfinity(hole)) s.Hole = hole;
            b.Style = s;
            b.OwnLook = true;
            b.Validate();
        }

        /// <summary>v5.0.2: the 强化 look = a copy of this profile's normal look (colours too unless it follows the hand
        /// colours), OwnLook on. The 外观 page's 「和正常准星一样」.</summary>
        public void CopyNormalToBoost()
        {
            if (Boost == null) Boost = new BoostStyle();
            Boost.OwnLook = false;
            BeginOwnBoost(StyleOrNew().Gap, StyleOrNew().Length, StyleOrNew().Hole);
            BoostStyle b = Boost;
            b.Diameter = Diameter; b.Thickness = Thickness; b.OuterThickness = OuterThickness; b.OpacityPct = OpacityPct;
            b.GlowPx = GlowPx; b.GlowOpacityPct = GlowOpacityPct;
            if (!b.FollowHandColor) { b.Color = Color; b.Color2 = Color2; }
            b.Validate();
        }

        private CrosshairStyle StyleOrNew() { if (Style == null) Style = new CrosshairStyle(); return Style; }

        /// <summary>v5.0.2: every 外观 value of src (the normal look, 换手颜色, 强化准星 and the style), nothing else; for
        /// 「恢复到 Leo 推荐」, which keeps the player's 外观 (SettingsForm.RestoreBuiltIn).</summary>
        public void CopyAppearanceFrom(Profile src)
        {
            if (src == null) return;
            Diameter = src.Diameter; Thickness = src.Thickness; Feather = src.Feather; OpacityPct = src.OpacityPct;
            Color = src.Color; Color2 = src.Color2; Gradient = src.Gradient; CenterDot = src.CenterDot; CenterDotSize = src.CenterDotSize;
            OuterThickness = src.OuterThickness; Shape = src.Shape; CrossLength = src.CrossLength; CrossThickness = src.CrossThickness;
            CrossGap = src.CrossGap; CrossTop = src.CrossTop; Dashes = src.Dashes; DashGapPct = src.DashGapPct; GlowPx = src.GlowPx;
            GlowOpacityPct = src.GlowOpacityPct; RotationDeg = src.RotationDeg;
            HandColorOn = src.HandColorOn; HandColor = src.HandColor; HandColor2 = src.HandColor2;
            Boost = src.Boost == null ? new BoostStyle() : src.Boost.Clone();
            Style = src.Style == null ? new CrosshairStyle() : src.Style.Clone();
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
        /// <summary>v4.1 (BRUTE40 P2-5): the settings.json format this version writes (v4.0 and older always wrote 1). A file
        /// with a higher number comes from a newer version: Load keeps a copy before saving over it.</summary>
        public const int FormatVersion = 2;

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
        // v4.1 (BRUTE40 P2-1): HandEnabled / HandMode / BoostEnabled are properties so a load can tell "in the file" from "absent":
        // the v2.4 / v3.0 key migrations only set them for a file that really is older (it lacks them), never for a newer file
        // whose 换手 / 强化 key just could not be read.
        private bool handEnabled, boostEnabled;
        private string handMode;
        /// <summary>False only while reading a file that has no HandEnabled / HandMode / BoostEnabled member.</summary>
        public bool HandEnabledPresent, HandModePresent, BoostEnabledPresent;
        /// <summary>☑ 换手检测 (v2.4, default on).</summary>
        [DataMember(Name = "HandEnabled")]
        public bool HandEnabled { get { return handEnabled; } set { handEnabled = value; HandEnabledPresent = true; } }
        /// <summary>换手方式: "toggle" (切换, default) or "hold" (按住).</summary>
        [DataMember(Name = "HandMode")]
        public string HandMode { get { return handMode; } set { handMode = value; HandModePresent = true; } }
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
        [DataMember(Name = "BoostEnabled")]
        public bool BoostEnabled { get { return boostEnabled; } set { boostEnabled = value; BoostEnabledPresent = true; } }
        // ---- v3.2 ----
        /// <summary>点一下切换开镜: Tab / Esc 取消开镜 (default on).</summary>
        [DataMember(Name = "AdsCancelTabEsc")] public bool AdsCancelTabEsc;
        /// <summary>开镜键是右键时，Alt/Ctrl+右键 不算开镜 (default on).</summary>
        [DataMember(Name = "AdsIgnoreAltCtrl")] public bool AdsIgnoreAltCtrl;
        /// <summary>v3.3 点一下切换开镜: Shift+W (冲刺) 取消开镜 (default on).</summary>
        [DataMember(Name = "AdsCancelRun")] public bool AdsCancelSprint;
        // ---- v4.0 ----
        /// <summary>侧身方式 = 切换 only: 冲刺 (Shift + W, rising edge) 取消侧身 (default on; older files -> on).</summary>
        [DataMember(Name = "LeanCancelRun")] public bool LeanCancelSprint; // JSON name like AdsCancelRun (old v2.3 files had a removed "Sprint*" field)
        /// <summary>首次设置教程 finished (default false: the tutorial button is highlighted until it is done).</summary>
        [DataMember(Name = "TutorialDone")] public bool TutorialDone;
        // ---- v4.1 游戏画面换算 (ViewScale): offsets are angles, converted through each screen's focal length ----
        /// <summary>画面比例 of the game: "auto" (跟随分辨率) or one of ViewScale.AspectIds ("16:9", ...).</summary>
        [DataMember(Name = "ResAspect")] public string ResAspect;
        /// <summary>游戏 FOV in degrees (whole number 50..75, default 75).</summary>
        [DataMember(Name = "ResFov")] public double ResFov;
        /// <summary>调参比例 (the aspect the offsets were tuned at), default "auto".</summary>
        [DataMember(Name = "ResBaseAspect")] public string ResBaseAspect;
        /// <summary>调参 FOV, default 75. UNVERIFIED: the FOV Leo tuned at (he will confirm).</summary>
        [DataMember(Name = "ResBaseFov")] public double ResBaseFov;
        /// <summary>FOV 类型: v4.2 always "v" (垂直; Leo 17 Nov 2026). Kept in the file for older versions; a stored "h" is
        /// read and ignored, and "v" is written.</summary>
        [DataMember(Name = "ResFovType")] public string ResFovType { get { return "v"; } set { } }
        /// <summary>The file has the v4.1 fields. Absent (older file) -> Validate sets base 2160×1440, both FOV 75, both
        /// aspects 跟随, vertical: the output is then exactly the old height-only scale.</summary>
        [DataMember(Name = "ResV41")] public bool ResV41Present;
        // v4.3 (Leo): the 6 global mouse members of the removed mouse-sway feature (mouse counts per inch, game
        // sensitivity, turn counts) are gone: an old file's values are skipped on read and never written back.

        // ---- v5.1 shared fields (W23; V51-BRIEF.md). Absent in older files -> SetDefaults values (OnDeserializing). ----
        /// <summary>弹药提示 (decision 1), default off.</summary>
        [DataMember(Name = "AmmoOn")] public bool AmmoOn;
        /// <summary>弹匣容量 (rounds, 1..200, default 30).</summary>
        [DataMember(Name = "AmmoMagSize")] public int AmmoMagSize;
        /// <summary>射速 RPM (60..2000, default 600).</summary>
        [DataMember(Name = "AmmoRpm")] public int AmmoRpm;
        /// <summary>半自动: 1 round per click (default false = 全自动, hold time × RPM).</summary>
        [DataMember(Name = "AmmoSemi")] public bool AmmoSemi;
        /// <summary>Comma-separated style tokens of AmmoStyleIds, any combination (W22 AmmoDisplay.ParseStyle); default
        /// "brackets,colour". Validate keeps known tokens in AmmoStyleIds order; empty -> "brackets".</summary>
        [DataMember(Name = "AmmoStyle")] public string AmmoStyle;
        public static readonly string[] AmmoStyleIds = new string[] { "brackets", "bottombar", "colour", "fade", "flash" };
        /// <summary>换弹键 (virtual-key code, default R = 0x52). Press = 普通换弹, double-tap within AmmoQuickTapMs = 快速换弹.</summary>
        [DataMember(Name = "AmmoReloadKey")] public int AmmoReloadKey;
        /// <summary>双击判定 window in ms (100..800, default 300).</summary>
        [DataMember(Name = "AmmoQuickTapMs")] public int AmmoQuickTapMs;
        /// <summary>v5.1 R2 (Leo round 2): 换弹时间 (R): a new full mag after this many ms (300..6000, default 2400).</summary>
        [DataMember(Name = "AmmoReloadMs")] public double AmmoReloadMs;
        /// <summary>v5.1 R2: 快速换弹时间 (R twice): the old mag is dropped, a new full mag after this many ms (200..4000, default 900).</summary>
        [DataMember(Name = "AmmoQuickReloadMs")] public double AmmoQuickReloadMs;
        /// <summary>v5.1 R4: 换向最短时间 in ms (A→D: back to 0 and out again takes at least this; 0..600, default 150 =
        /// MinDirectionBlendTime, V51-RESEARCH §1).</summary>
        [DataMember(Name = "ReverseMinMs")] public double ReverseMinMs;
        /// <summary>v5.1 R4: 回程时间 in % of the time it took to get out (taps, partial releases, first leg of A→D); 25..300, default 100.</summary>
        [DataMember(Name = "ReturnTimePct")] public double ReturnTimePct;
        /// <summary>v5.1 R4: 换向停顿 in ms: rest at 0 between the two legs of A→D; 0..300, default 0.</summary>
        [DataMember(Name = "ReversePauseMs")] public double ReversePauseMs;
        public const double AmmoReloadMsMin = 300, AmmoReloadMsMax = 6000, AmmoQuickReloadMsMin = 200, AmmoQuickReloadMsMax = 4000;
        public const double TapThresholdMsMin = 60, TapThresholdMsMax = 250, ReverseMinMsMin = 0, ReverseMinMsMax = 600;
        public const double ReturnTimePctMin = 25, ReturnTimePctMax = 300, ReversePauseMsMin = 0, ReversePauseMsMax = 300;
        // v5.1 R3 (Leo round 2): weapon-switch handling is removed. AmmoWeaponKeysMain / Other below are kept ONLY so older v5.1
        // files import and round-trip; no UI, no engine use.
        /// <summary>主武器 keys (VK list): pressing one = back on the main weapon (count resumes).</summary>
        [DataMember(Name = "AmmoWeaponKeysMain")] public int[] AmmoWeaponKeysMain;
        /// <summary>其他武器/物品 keys (VK list): pressing one = off the main weapon (count pauses).</summary>
        [DataMember(Name = "AmmoWeaponKeysOther")] public int[] AmmoWeaponKeysOther;
        /// <summary>丢雷隐藏 (decision 2, default on): GrenadeKey toggles the crosshair hidden / shown.</summary>
        [DataMember(Name = "GrenadeHideOn")] public bool GrenadeHideOn;
        /// <summary>丢雷键 (VK, default G = 0x47).</summary>
        [DataMember(Name = "GrenadeKey")] public int GrenadeKey;
        /// <summary>强化准星 key = Keys.Boost + Keys.BoostMods (one source of truth, saved in Keys; action ActionBoost).
        /// v5.1 default (fresh install / unreadable key) = Ctrl+T, Tarkov's default 切换战术设备模式（红外/手电）key
        /// (V51-RESEARCH §2). Not T alone (T = device on/off). Older files keep their own key.</summary>
        public int BoostKey { get { return Keys == null ? 0 : Keys.Boost; } set { if (Keys != null) Keys.Boost = value; } }
        public int BoostKeyMods { get { return Keys == null ? 0 : Keys.BoostMods; } set { if (Keys != null) Keys.SetMods(KeyBindings.ActionBoost, value); } }
        /// <summary>强化方式: true = 切换 (default), false = 按住.</summary>
        [DataMember(Name = "BoostToggle")] public bool BoostToggle;
        /// <summary>准星动态 preset: "static" (静态, default) / "cs" (CS 动态) / "classic" (经典).</summary>
        [DataMember(Name = "AnimPreset")] public string AnimPreset;
        [DataMember(Name = "AnimDynGap")] public bool AnimDynGap;
        [DataMember(Name = "AnimFirePulse")] public bool AnimFirePulse;
        [DataMember(Name = "AnimLeanRotate")] public bool AnimLeanRotate;
        [DataMember(Name = "AnimStateColour")] public bool AnimStateColour;
        [DataMember(Name = "AnimShake")] public bool AnimShake;
        /// <summary>性能模式 (decision 8, default off).</summary>
        [DataMember(Name = "PerfMode")] public bool PerfMode;
        /// <summary>自定义画面比例 "W:H" (e.g. "123:111"); used when ResAspect == "custom". Default "16:9".</summary>
        [DataMember(Name = "ResAspectCustom")] public string ResAspectCustom;
        /// <summary>v5.1 (decision 11): how the game shows a 画面比例 that is not the screen's own: stretched (拉伸到全屏, the v4.2
        /// model: X × screen ratio / chosen ratio; default, = v4.2-v5.0.2) or not (不拉伸: the picture keeps its shape inside
        /// the screen, black bars; no X factor). The wizard / 常规 page ask the user (V51-RESEARCH §3: exclusive
        /// full screen + GPU scaling stretches, borderless does not; Tarkov's own option in borderless is UNVERIFIED).</summary>
        /// v5.1 (W26 request): null = not asked / 不确定 (= stretched, the v4.2 behaviour), true = 拉宽了, false = 有黑边 / 没拉宽.</summary>
        [DataMember(Name = "ResStretched")] public bool? ResStretched;
        /// <summary>True when the game picture is stretched to the full screen (ResStretched null or true).</summary>
        public bool PictureStretched { get { return ResStretched != false; } }
        // ---- v5.1 W33 (overlay texture-drop diagnostics), all default false ----
        /// <summary>新的显示方式（DirectComposition）.</summary>
        [DataMember(Name = "OverlayDCompMode")] public bool OverlayDCompMode;
        /// <summary>开镜/丢雷时真正隐藏准星窗口 (instead of full transparency).</summary>
        [DataMember(Name = "AdsRealHide")] public bool AdsRealHide;
        /// <summary>显存诊断日志 (once a second into Data/crash.log).</summary>
        [DataMember(Name = "GpuDiagLog")] public bool GpuDiagLog;
        /// <summary>首次设置向导 finished (default false).</summary>
        [DataMember(Name = "WizardDone")] public bool WizardDone;
        /// <summary>必读 card acknowledged (我知道了), default false.</summary>
        [DataMember(Name = "MustReadAck")] public bool MustReadAck;
        [DataMember(Name = "NetUpdateCheck")] public bool NetUpdateCheck;
        /// <summary>匿名统计 consent: null = not asked yet (ask once), false = no, true = yes.</summary>
        [DataMember(Name = "NetStatsConsent")] public bool? NetStatsConsent;
        [DataMember(Name = "NetCrashAsk")] public bool NetCrashAsk;
        /// <summary>短按 threshold in ms (60..250, default 120; V51-RESEARCH §1).</summary>
        [DataMember(Name = "TapThresholdMs")] public double TapThresholdMs;
        /// <summary>A→D 反向: "slowstart" (only mode in v5.1: from the current position, decelerate then accelerate).</summary>
        [DataMember(Name = "ReverseMode")] public string ReverseMode;
        public static readonly string[] AnimPresetIds = new string[] { "static", "cs", "classic" };
        public static readonly string[] ReverseModeIds = new string[] { "slowstart" };
        /// <summary>Default weapon keys (V51-RESEARCH §2, Tarkov defaults): main = 2 (枪带上的主武器); other = 1 (手枪), 3 (背上的
        /// 第二把长枪), V (近战), 4-0 (快捷栏).</summary>
        public static int[] DefaultWeaponKeysMain() { return new int[] { 0x32 }; }
        public static int[] DefaultWeaponKeysOther() { return new int[] { 0x31, 0x33, 0x56, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 }; }

        /// <summary>v4.1: keys a migrated binding may fall back to when its default is taken (rarely bound in game; in this order).
        /// v4.2 (W2): F9 removed, it is the 滤镜
        /// 下一个预设 default (F6-F8 were never in the list).</summary>
        public static readonly int[] SpareKeys = new int[] { 0x24 /* Home */, 0x23 /* End */, 0x2D /* Insert */, 0x2E /* Delete */,
            0x21 /* PgUp */, 0x22 /* PgDn */, 0x13 /* Pause */, 0x91 /* ScrollLock */, 0x7B /* F12 */, 0x79 /* F10 */ };

        /// <summary>Another rebindable action or a fixed W/A/S/D has exactly a's key + 组合键.</summary>
        public static bool ClashesRebindable(KeyBindings k, int a)
        {
            return !ComboFree(k, a, k.Get(a), k.GetMods(a));
        }

        /// <summary>v4.0 自动检测: sets 游戏分辨率 to the screen size probe() returns ({width, height} in REAL pixels).
        /// Returns false (nothing changed) when probe is null, throws, or returns no usable size. Values are clamped like
        /// Validate (640..7680 × 480..4320). The offsets then scale by height: s = height / 1440 (调参分辨率 2160×1440).</summary>
        public bool DetectGameResolution(Func<int[]> probe)
        {
            int w, h;
            if (!ResolutionDetect.TryProbe(probe, out w, out h)) return false;
            ResGameW = w; ResGameH = h;
            return true;
        }

        /// <summary>v4.1: 换算系数 k = f_game / f_base (ViewScale.Factor), the Y factor. With equal FOV and native aspect
        /// this is exactly 游戏高度 / 调参高度 (the v3.x/v4.0 rule), and X uses the same k.</summary>
        public double ResolutionScale { get { return ViewScale.Factor(this); } }

        /// <summary>v4.2: the X 换算系数 = ResolutionScale × horizontal stretch (ViewScale.FactorX); equal to ResolutionScale
        /// unless 画面比例 differs from the resolution's own ratio on the game or 调参 screen.</summary>
        public double ResolutionScaleX { get { return ViewScale.FactorX(this); } }

        [DataMember(Name = "Profiles")] public List<Profile> Profiles;

        // Runtime only (not saved).
        public PreviewMode PreviewMode;
        public double PreviewSpeed;
        /// <summary>The hand shown in the settings window (0 正常手 / 1 换手); synthetic previews use it.</summary>
        public int EditHand;
        /// <summary>v4.1 (BRUTE40 P2-5): the Version member the last Validate found (v4.0 and older files: 1).</summary>
        public int FileVersion = FormatVersion;
        /// <summary>v4.1 (BRUTE40 P1-3): the key bindings the last Validate had to change, e.g. 「跳跃 Q → Space」 (empty: none).
        /// SettingsIO.Load / ImportLegacy put them in their message.</summary>
        public string[] KeyNotes = new string[0];
        /// <summary>v4.1 (BRUTE40 P2-6): the settings file that must be backed up before it is overwritten (Load could not back
        /// it up), else null. SettingsIO.Save tries the backup first and does not save while it fails.</summary>
        public string BackupBeforeSave;

        public GlobalSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c)
        {
            SetDefaults();
            ResV41Present = false;
            HandEnabledPresent = false; HandModePresent = false; BoostEnabledPresent = false;
        }

        public void SetDefaults()
        {
            Version = FormatVersion; ActiveProfile = ""; Enabled = true;
            FileVersion = FormatVersion; KeyNotes = new string[0]; BackupBeforeSave = null; // runtime (a file read skips initialisers)
            ResAspect = "auto"; ResFov = 75; ResBaseAspect = "auto"; ResBaseFov = 75; ResFovType = "v"; ResV41Present = true;
            Keys = new KeyBindings();
            AdsMode = "hold"; LeanMode = "hold";
            MonitorIndex = -1; CenterOffsetX = 0; CenterOffsetY = 0;
            FrameMode = "vsync"; Fps = 144; ShowFadeMs = 60; HideFadeMs = 0;
            SettingsTopMost = true; ShowSettingsOnStart = true; HideOnFreeLook = true;
            HandEnabled = true; HandMode = "toggle";
            ResBaseW = 2160; ResBaseH = 1440; ResGameW = 2160; ResGameH = 1440;
            OnlyWhenGameFocused = true; GameWindowTitle = DefaultGameWindowTitle;
            Autostart = false; SplashOnStart = true; BoostEnabled = true;
            AdsCancelTabEsc = true; AdsIgnoreAltCtrl = true; AdsCancelSprint = true;
            LeanCancelSprint = true; TutorialDone = false;
            SetV51Defaults();
            Profiles = new List<Profile>();
            PreviewMode = PreviewMode.Live; PreviewSpeed = 1.0; EditHand = 0;
        }

        /// <summary>v5.1 shared fields: defaults (also for an older file without them).</summary>
        public void SetV51Defaults()
        {
            AmmoOn = false; AmmoMagSize = 30; AmmoRpm = 600; AmmoSemi = false; AmmoStyle = "brackets,colour";
            AmmoReloadKey = 0x52; AmmoQuickTapMs = 300;
            AmmoWeaponKeysMain = DefaultWeaponKeysMain(); AmmoWeaponKeysOther = DefaultWeaponKeysOther();
            GrenadeHideOn = true; GrenadeKey = 0x47; BoostToggle = true;
            AnimPreset = "static"; AnimDynGap = false; AnimFirePulse = false; AnimLeanRotate = false; AnimStateColour = false; AnimShake = false;
            PerfMode = false; OverlayDCompMode = false; AdsRealHide = false; GpuDiagLog = false; ResAspectCustom = "16:9"; ResStretched = null; WizardDone = false; MustReadAck = false;
            NetUpdateCheck = true; NetStatsConsent = null; NetCrashAsk = true;
            TapThresholdMs = 120; ReverseMode = "slowstart";
            AmmoReloadMs = 2400; AmmoQuickReloadMs = 900; ReverseMinMs = 150; ReturnTimePct = 100; ReversePauseMs = 0;
        }

        /// <summary>v5.1: clamps the shared fields; a bad value gets its SetV51Defaults value.</summary>
        public void ValidateV51()
        {
            GlobalSettings d = new GlobalSettings();
            if (AmmoMagSize < 1 || AmmoMagSize > 200) AmmoMagSize = d.AmmoMagSize;
            if (AmmoRpm < 60 || AmmoRpm > 2000) AmmoRpm = d.AmmoRpm;
            AmmoStyle = NormalizeAmmoStyle(AmmoStyle);
            if (AmmoReloadKey < 1 || AmmoReloadKey > 254) AmmoReloadKey = d.AmmoReloadKey;
            if (AmmoQuickTapMs < 100 || AmmoQuickTapMs > 800) AmmoQuickTapMs = d.AmmoQuickTapMs;
            AmmoWeaponKeysMain = CleanVks(AmmoWeaponKeysMain, d.AmmoWeaponKeysMain);
            AmmoWeaponKeysOther = CleanVks(AmmoWeaponKeysOther, d.AmmoWeaponKeysOther);
            if (GrenadeKey < 1 || GrenadeKey > 254) GrenadeKey = d.GrenadeKey;
            AnimPreset = Clamp.OneOf(AnimPreset, AnimPresetIds);
            int cw, ch;
            if (!ViewScale.TryParseRatio(ResAspectCustom, out cw, out ch)) ResAspectCustom = d.ResAspectCustom;
            else ResAspectCustom = cw + ":" + ch;
            TapThresholdMs = Math.Round(Clamp.D(TapThresholdMs, TapThresholdMsMin, TapThresholdMsMax, d.TapThresholdMs));
            AmmoReloadMs = Math.Round(Clamp.D(AmmoReloadMs, AmmoReloadMsMin, AmmoReloadMsMax, d.AmmoReloadMs));
            AmmoQuickReloadMs = Math.Round(Clamp.D(AmmoQuickReloadMs, AmmoQuickReloadMsMin, AmmoQuickReloadMsMax, d.AmmoQuickReloadMs));
            ReverseMinMs = Math.Round(Clamp.D(ReverseMinMs, ReverseMinMsMin, ReverseMinMsMax, d.ReverseMinMs));
            ReturnTimePct = Math.Round(Clamp.D(ReturnTimePct, ReturnTimePctMin, ReturnTimePctMax, d.ReturnTimePct));
            ReversePauseMs = Math.Round(Clamp.D(ReversePauseMs, ReversePauseMsMin, ReversePauseMsMax, d.ReversePauseMs));
            ReverseMode = Clamp.OneOf(ReverseMode, ReverseModeIds);
        }

        /// <summary>Known AmmoStyleIds tokens of s in canonical order ("" or null -> "brackets").</summary>
        public static string NormalizeAmmoStyle(string s)
        {
            List<string> have = new List<string>();
            if (s != null) foreach (string t in s.Split(',', ' ', '|', ';')) { string u = t.Trim().ToLowerInvariant(); if (u == "color") u = "colour"; have.Add(u); }
            StringBuilder sb = new StringBuilder();
            foreach (string id in AmmoStyleIds) if (have.Contains(id)) { if (sb.Length > 0) sb.Append(','); sb.Append(id); }
            return sb.Length == 0 ? "brackets" : sb.ToString();
        }

        /// <summary>Distinct VKs in 1..254 (max 16); null -> def (a copy). An explicitly empty list stays empty.</summary>
        private static int[] CleanVks(int[] v, int[] def)
        {
            if (v == null) return (int[])def.Clone();
            List<int> r = new List<int>();
            foreach (int k in v) if (k >= 1 && k <= 254 && !r.Contains(k) && r.Count < 16) r.Add(k);
            return r.ToArray();
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
            AdsCancelTabEsc = d.AdsCancelTabEsc; AdsIgnoreAltCtrl = d.AdsIgnoreAltCtrl; AdsCancelSprint = d.AdsCancelSprint;
            LeanCancelSprint = d.LeanCancelSprint; // v4.0 (组合键 are part of Keys)
            // v5.1: the 按键 page also holds 强化方式, 丢雷隐藏 and the 弹药提示 keys
            BoostToggle = d.BoostToggle; GrenadeHideOn = d.GrenadeHideOn; GrenadeKey = d.GrenadeKey;
            AmmoReloadKey = d.AmmoReloadKey; AmmoQuickTapMs = d.AmmoQuickTapMs;
            AmmoReloadMs = d.AmmoReloadMs; AmmoQuickReloadMs = d.AmmoQuickReloadMs;
            AmmoWeaponKeysMain = (int[])d.AmmoWeaponKeysMain.Clone(); AmmoWeaponKeysOther = (int[])d.AmmoWeaponKeysOther.Clone();
        }

        /// <summary>Copy of the global fields (Keys cloned). Profiles are deep-cloned only if withProfiles.</summary>
        public GlobalSettings Clone(bool withProfiles)
        {
            GlobalSettings g = (GlobalSettings)MemberwiseClone();
            g.Keys = Keys == null ? new KeyBindings() : Keys.Clone();
            g.AmmoWeaponKeysMain = AmmoWeaponKeysMain == null ? null : (int[])AmmoWeaponKeysMain.Clone();
            g.AmmoWeaponKeysOther = AmmoWeaponKeysOther == null ? null : (int[])AmmoWeaponKeysOther.Clone();
            g.Profiles = new List<Profile>();
            if (withProfiles && Profiles != null)
                foreach (Profile p in Profiles) g.Profiles.Add(p.Clone());
            return g;
        }

        /// <summary>
        /// Copies every saved ([DataMember]) field of 'o' into this object, so an object other code holds (the dashboard,
        /// the 外观 panel) sees new settings without being replaced. Runtime-only fields stay. Used by 「导入旧版设置」.
        /// </summary>
        public void CopyFrom(GlobalSettings o)
        {
            if (o == null || o == this) return;
            System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (System.Reflection.FieldInfo f in typeof(GlobalSettings).GetFields(bf))
                if (f.IsDefined(typeof(DataMemberAttribute), false)) f.SetValue(this, f.GetValue(o));
            // v4.1: HandEnabled / HandMode / BoostEnabled are [DataMember] properties now
            foreach (System.Reflection.PropertyInfo p in typeof(GlobalSettings).GetProperties(bf))
                if (p.IsDefined(typeof(DataMemberAttribute), false) && p.CanRead && p.CanWrite) p.SetValue(this, p.GetValue(o, null), null);
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

        private static bool IsWasd(int vk)
        {
            return vk == KeyBindings.VkW || vk == KeyBindings.VkA || vk == KeyBindings.VkS || vk == KeyBindings.VkD;
        }

        /// <summary>v4.1: the action does something now. A switched-off 换手 (☐ 换手检测) or 强化准星 does not, so its key may
        /// stay shared with another action (it is never moved on load; the 按键 page decides when it is switched on).</summary>
        public bool ActionOn(int action)
        {
            if (action == KeyBindings.ActionHandSwitch) return HandEnabled;
            if (action == KeyBindings.ActionBoost) return BoostEnabled;
            return true;
        }

        /// <summary>v4.1: every action with a key the load checks: the 按键 page actions (v4.3: the mouse-lag key is gone).</summary>
        private static readonly int[] KeyActions = new int[] { 0, 1, 2, 3, 4, 5, 6, 11, 12 };

        /// <summary>vk + mods is not a fixed W/A/S/D and no action other than 'self' has it (switched-off ones count).</summary>
        private static bool ComboFree(KeyBindings k, int self, int vk, int mods)
        {
            int m = KeyBindings.NormalizeMods(vk, mods);
            if (m == 0 && IsWasd(vk)) return false;
            foreach (int b in KeyActions) if (b != self && k.Get(b) == vk && k.GetMods(b) == m) return false;
            return true;
        }

        /// <summary>
        /// v4.1 (BRUTE40 P1-3): after a load no two actions that are on share a key + 组合键 (one press must not lean AND
        /// jump). The keys the file really set win, in 按键 page order (the first keeps it); then come the actions that only
        /// got a default (missing / unusable in the file, was a plain W/A/S/D, new in this version). An action that must move takes its default when that is free, else the first free SpareKeys key;
        /// W/A/S/D are never given out. A switched-off 换手 / 强化准星 keeps its key. Returns one note per changed binding
        /// (「跳跃 Q → Space」); keys the file did not have and that simply got their default are not listed.
        /// </summary>
        private string[] ResolveKeyClashes(bool[] gotDefault, int[] fileVk, int[] fileMods)
        {
            KeyBindings k = Keys, kd = new KeyBindings();
            List<int> order = new List<int>();
            foreach (int a in KeyBindings.RebindableActions) if (ActionOn(a) && !gotDefault[a]) order.Add(a);
            foreach (int a in KeyBindings.RebindableActions) if (ActionOn(a) && gotDefault[a]) order.Add(a);
            List<int> placed = new List<int>();
            foreach (int a in order)
            {
                int vk = k.Get(a), m = k.GetMods(a);
                bool clash = false;
                if (gotDefault[a]) clash = !ComboFree(k, a, vk, m);
                else foreach (int b in placed) if (k.Get(b) == vk && k.GetMods(b) == m) { clash = true; break; }
                if (clash)
                {
                    if (ComboFree(k, a, kd.Get(a), kd.GetMods(a))) k.SetCombo(a, kd.Get(a), kd.GetMods(a));
                    else foreach (int s in SpareKeys) if (ComboFree(k, a, s, 0)) { k.SetCombo(a, s, 0); break; }
                }
                placed.Add(a);
            }

            List<string> notes = new List<string>();
            foreach (int a in KeyActions)
            {
                int vk = k.Get(a), m = k.GetMods(a), fv = fileVk[a];
                bool valid = fv >= 1 && fv <= 254;
                if (fv == 0) { if (vk == kd.Get(a) && m == kd.GetMods(a)) continue; } // not in the file: its default, as before
                else if (valid && vk == fv && m == KeyBindings.NormalizeMods(fv, fileMods[a])) continue;
                notes.Add(KeyBindings.ActionNames[a] + (valid ? " " + KeyBindings.ComboName(fv, fileMods[a]) : "") + " → " + k.ComboOf(a));
            }
            return notes.ToArray();
        }

        public void Validate()
        {
            FileVersion = Version;
            Version = FormatVersion;
            if (Keys == null) Keys = new KeyBindings();
            bool handMigrate = !Keys.HandSwitchPresent, boostMigrate = !Keys.BoostPresent;
            bool preV30 = !BoostEnabledPresent; // read before the migrations below set BoostEnabled
            // v4.1 (BRUTE40 P1-3): what the file said, before defaults are filled in (0 = not in the file).
            int[] fileVk = new int[KeyBindings.ActionCount], fileMods = new int[KeyBindings.ActionCount];
            bool[] gotDefault = new bool[KeyBindings.ActionCount];
            foreach (int a in KeyActions)
            {
                fileVk[a] = Keys.Get(a); fileMods[a] = Keys.GetMods(a);
                gotDefault[a] = fileVk[a] < 1 || fileVk[a] > 254;
            }
            Keys.Validate();
            KeyBindings kd = new KeyBindings();
            // v4.0: 前后左右 are forced to W/S/A/D, so a rebindable action holding a plain W/A/S/D goes back to its default.
            foreach (int a in KeyActions)
                if (Keys.GetMods(a) == 0 && IsWasd(Keys.Get(a))) { Keys.SetCombo(a, kd.Get(a), kd.GetMods(a)); gotDefault[a] = true; }
            if (handMigrate)
            {
                // v2.4 migration: 换手 = mouse side button 1, 切换, on — unless that button already does something else in this file.
                // v4.1 (BRUTE40 P2-1): 换手方式 / 换手检测 only for a file without them (really pre-v2.4); a newer file whose
                // 换手 key could not be read keeps its own.
                Keys.HandSwitch = KeyBindings.VkXButton1;
                gotDefault[KeyBindings.ActionHandSwitch] = true;
                if (!HandModePresent) HandMode = "toggle";
                if (!HandEnabledPresent) HandEnabled = Keys.FindOther(KeyBindings.VkXButton1, KeyBindings.ActionHandSwitch) < 0;
            }
            Keys.HandSwitchPresent = true;
            if (boostMigrate)
            {
                // v3.0 migration: 强化准星 = mouse side button 2, on — unless that button already does something else in this file.
                // v4.1 (BRUTE40 P2-1): ☑ 强化准星 only for a file without BoostEnabled (really pre-v3.0).
                // (v5.1: kept as before -- a pre-v3.0 file gets XButton2; only fresh installs / unreadable keys get Ctrl+T.)
                Keys.Boost = KeyBindings.VkXButton2; Keys.BoostMods = 0;
                gotDefault[KeyBindings.ActionBoost] = true;
                // v5.1: the migrated 侧键2 counts as the file's key for the notes (it is no longer the default Ctrl+T, but a real
                // old file must still load without a 「已自动改好」 message)
                fileVk[KeyBindings.ActionBoost] = KeyBindings.VkXButton2; fileMods[KeyBindings.ActionBoost] = 0;
                if (!BoostEnabledPresent) BoostEnabled = Keys.FindOther(KeyBindings.VkXButton2, KeyBindings.ActionBoost) < 0;
            }
            Keys.BoostPresent = true;
            KeyNotes = ResolveKeyClashes(gotDefault, fileVk, fileMods);
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
                    if (p != null && !p.StandAsZeroPresent) { p.StandOffsetX = 0; p.StandOffsetY = 0; p.SetHandAuto(Profile.HandStand); } // v3.4: 站着偏移 removed; v5.1: kept for a file with StandAsZero (站立静止偏移)
            CenterOffsetX = 0;
            CenterOffsetY = 0;
            FrameMode = Clamp.OneOf(FrameMode, new string[] { "vsync", "fixed" });
            Fps = Clamp.D(Fps, 30, 360, 144);
            ShowFadeMs = Clamp.D(ShowFadeMs, 0, 1000, 60);
            HideFadeMs = Clamp.D(HideFadeMs, 0, 1000, 0);
            if (!ResV41Present)
            {
                // v4.1 migration: an older file keeps the v3.4 lock (调参分辨率 2160×1440) and gets FOV 75 / 跟随 / 垂直 on
                // both sides, so ViewScale.Factor == 游戏高度 / 1440 exactly (nothing moves).
                ResBaseW = 2160; ResBaseH = 1440;
                ResAspect = "auto"; ResBaseAspect = "auto"; ResFov = 75; ResBaseFov = 75; ResFovType = "v";
            }
            ResV41Present = true;
            ResBaseW = Math.Round(Clamp.D(ResBaseW, 640, 7680, 2160)); ResBaseH = Math.Round(Clamp.D(ResBaseH, 480, 4320, 1440));
            ResGameW = Math.Round(Clamp.D(ResGameW, 640, 7680, 2160)); ResGameH = Math.Round(Clamp.D(ResGameH, 480, 4320, 1440));
            ResAspect = ViewScale.NormalizeAspect(ResAspect);      // v5.1: any W:H
            ResBaseAspect = ViewScale.NormalizeAspect(ResBaseAspect);
            ResFov = Math.Round(Clamp.D(ResFov, ViewScale.MinFov, ViewScale.MaxFov, ViewScale.DefaultFov));
            ResBaseFov = Math.Round(Clamp.D(ResBaseFov, ViewScale.MinFov, ViewScale.MaxFov, ViewScale.DefaultFov));
            // v4.2: FOV 类型 is always 垂直 (ResFovType returns "v"; nothing to clamp)
            PreviewSpeed = Clamp.D(PreviewSpeed, 0.1, 1.0, 1.0);
            if (EditHand != 1) EditHand = 0;
            ValidateV51();

            if (Profiles == null) Profiles = new List<Profile>();
            Profiles.RemoveAll(delegate(Profile p) { return p == null; });
            if (Profiles.Count == 0)
            {
                GlobalSettings d = CreateDefault();
                Profiles.AddRange(d.Profiles);
            }
            // v4.1 (BRUTE40 P2-3): find the active profile BEFORE names are trimmed, renamed or made unique, so it stays the
            // same profile (exact name first, else the same name without outer spaces).
            Profile active = string.IsNullOrEmpty(ActiveProfile) ? null : FindProfile(ActiveProfile);
            if (active == null && ActiveProfile != null && ActiveProfile.Trim().Length > 0)
            {
                string want = ActiveProfile.Trim();
                foreach (Profile p in Profiles) if (p.Name != null && p.Name.Trim() == want) { active = p; break; }
            }
            // v3.0: the old built-in name 「M4（Leo 改装）」 becomes 「Leo默认」 (values kept), unless that name is already used.
            // v4.1 (BRUTE40 P2-4): only for a file from before v3.0 (no BoostEnabled); a newer file keeps the name the user chose.
            if (preV30 && FindProfile(DefaultProfileName) == null)
            {
                Profile legacy = FindProfile(LegacyDefaultProfileName);
                if (legacy != null)
                {
                    legacy.Name = DefaultProfileName;
                    if (legacy.SavedDefault != null && legacy.SavedDefault.Name == LegacyDefaultProfileName) legacy.SavedDefault.Name = DefaultProfileName;
                }
            }
            foreach (Profile p in Profiles) p.Validate();
            MakeNamesUnique();
            ActiveProfile = active != null ? active.Name : Profiles[0].Name;
            // the object is complete now: a later Validate (e.g. 导入旧版设置 merging it) must not run the old-file migrations again
            HandEnabledPresent = true; HandModePresent = true; BoostEnabledPresent = true;
        }

        /// <summary>v4.1 (BRUTE40 P2-3 / P2-13): every profile name unique, in one pass: a name stays where it first appears; a
        /// later repeat gets " (2)", " (3)", ... (the first number no profile uses, so an existing 「a (2)」 keeps its name).</summary>
        private void MakeNamesUnique()
        {
            HashSet<string> all = new HashSet<string>(StringComparer.Ordinal), kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (Profile p in Profiles) all.Add(p.Name);
            Dictionary<string, int> next = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Profile p in Profiles)
            {
                if (kept.Add(p.Name)) continue;
                string b = p.Name;
                int k;
                if (!next.TryGetValue(b, out k)) k = 2;
                string n = b + " (" + k + ")";
                while (all.Contains(n)) { k++; n = b + " (" + k + ")"; }
                next[b] = k + 1;
                p.Name = n;
                all.Add(n);
                kept.Add(n);
            }
        }
    }

    /// <summary>v5.1 弹药提示 weapon keys as text (pure): 「1 3 V 4」 ⇄ VK codes. Letters, digits, F1-F12, separated by spaces or
    /// commas (、，, too). Empty text = no keys.</summary>
    public static class WeaponKeyText
    {
        /// <summary>The VK list, or null when a token is not a key this box accepts.</summary>
        public static int[] Parse(string text)
        {
            List<int> r = new List<int>();
            if (text == null) return r.ToArray();
            foreach (string raw in text.Replace('，', ' ').Replace('、', ' ').Replace(',', ' ').Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().ToUpperInvariant();
                int vk = 0;
                if (t.Length == 1 && ((t[0] >= 'A' && t[0] <= 'Z') || (t[0] >= '0' && t[0] <= '9'))) vk = t[0];
                else if (t.Length >= 2 && t[0] == 'F')
                {
                    int n;
                    if (int.TryParse(t.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 12) vk = 0x70 + n - 1;
                }
                if (vk == 0) return null;
                if (!r.Contains(vk) && r.Count < 16) r.Add(vk);
            }
            return r.ToArray();
        }

        public static string Format(int[] vks)
        {
            if (vks == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (int v in vks) { if (sb.Length > 0) sb.Append(' '); sb.Append(KeyBindings.KeyName(v)); }
            return sb.ToString();
        }
    }

    /// <summary>v4.0 游戏分辨率 auto-detect maths (pure).</summary>
    public static class ResolutionDetect
    {
        public const int MinW = 640, MaxW = 7680, MinH = 480, MaxH = 4320;

        /// <summary>Runs probe safely: true with a clamped size when it returns two positive numbers.</summary>
        public static bool TryProbe(Func<int[]> probe, out int w, out int h)
        {
            w = 0; h = 0;
            if (probe == null) return false;
            int[] r;
            try { r = probe(); }
            catch (Exception) { return false; }
            return TryUse(r, out w, out h);
        }

        /// <summary>A usable size: two positive values, clamped to the allowed range.</summary>
        public static bool TryUse(int[] r, out int w, out int h)
        {
            w = 0; h = 0;
            if (r == null || r.Length < 2 || r[0] <= 0 || r[1] <= 0) return false;
            w = Math.Min(MaxW, Math.Max(MinW, r[0]));
            h = Math.Min(MaxH, Math.Max(MinH, r[1]));
            return true;
        }

        /// <summary>The offset scale a screen of this height gets (offsets tuned at 1440 px high).</summary>
        public static double ScaleFor(double gameH) { return gameH / 1440.0; }
    }

    /// <summary>
    /// v4.1 游戏分辨率 / 画面比例 / FOV conversion (pure). An offset of px_base pixels at the tuning screen is an angle
    /// a = atan(px_base / f_base); on the game screen it is px_new = f_new · tan(a), with f = (renderH / 2) / tan(vFOV / 2)
    /// (or (renderW / 2) / tan(hFOV / 2) for a horizontal FOV). tan(atan(x)) = x, so this is exactly the linear factor
    /// k = f_new / f_base, the same for X and Y (one focal length per screen). renderW × renderH is the picture the game
    /// draws: the whole resolution for 跟随分辨率, else the centred letterbox / pillarbox rectangle of the chosen aspect
    /// (the ring centre stays at the screen centre, which is also the picture centre).
    /// v4.2 (Leo 17 Nov 2026): the FOV is always vertical (widening the aspect only adds width), and a 画面比例 that is not
    /// the resolution's own ratio is drawn STRETCHED to the full screen: the picture keeps the full height (Y factor
    /// unchanged) and X gets the extra factor res ratio / chosen ratio (FactorX). The stretch model is UNVERIFIED.
    /// </summary>
    public static class ViewScale
    {
        public const int MinFov = 50, MaxFov = 75, DefaultFov = 75;
        /// <summary>"auto" = 跟随分辨率 (the resolution's own aspect, no black bars).</summary>
        public static readonly string[] AspectIds = new string[] { "auto", "16:9", "16:10", "21:9", "32:9", "4:3", "5:4", "3:2", "1:1" };
        public static readonly string[] AspectLabels = new string[] { "跟随分辨率", "16:9", "16:10", "21:9", "32:9", "4:3", "5:4", "3:2", "1:1" };

        /// <summary>v5.1 (decision 12): parses any "W:H" (also "W／H", full-width colon, spaces) with whole W, H in 1..10000 and
        /// a ratio in 0.2..8. "123:111" -> 123, 111.</summary>
        public static bool TryParseRatio(string s, out int w, out int h)
        {
            w = 0; h = 0;
            if (s == null) return false;
            string t = s.Trim().Replace('：', ':').Replace('/', ':').Replace('／', ':').Replace('x', ':').Replace('X', ':').Replace('×', ':').Replace(" ", "");
            string[] parts = t.Split(':');
            if (parts.Length != 2) return false;
            int a, b;
            if (!int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out a)) return false;
            if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out b)) return false;
            if (a < 1 || b < 1 || a > 10000 || b > 10000) return false;
            double r = (double)a / b;
            if (r < 0.2 || r > 8) return false;
            w = a; h = b;
            return true;
        }

        /// <summary>v5.1: a stored 画面比例 is "auto", a listed id, or any valid "W:H" (normalised); anything else -> "auto".</summary>
        public static string NormalizeAspect(string id)
        {
            if (id != null)
            {
                string t = id.Trim().ToLowerInvariant();
                if (Array.IndexOf(AspectIds, t) >= 0) return t;
                int w, h;
                if (TryParseRatio(t, out w, out h)) return w + ":" + h;
            }
            return "auto";
        }

        /// <summary>Width / height of an aspect id; "auto" (or unknown) = w / h of the resolution.</summary>
        public static double AspectValue(string id, double w, double h)
        {
            switch (id)
            {
                case "16:9": return 16.0 / 9.0;
                case "16:10": return 16.0 / 10.0;
                case "21:9": return 21.0 / 9.0; // nominal; real "21:9" screens are 64:27 (2560×1080) or 43:18 (3440×1440)
                case "32:9": return 32.0 / 9.0;
                case "4:3": return 4.0 / 3.0;
                case "5:4": return 5.0 / 4.0;
                case "3:2": return 3.0 / 2.0;
                case "1:1": return 1.0;
                default:
                    int cw, ch;
                    if (id != null && id != "auto" && TryParseRatio(id, out cw, out ch)) return (double)cw / ch;
                    return (h > 0) ? w / h : 1.0;
            }
        }

        /// <summary>v4.2: the 画面比例 choices shown in the UI, in the order of the game's list (Leo 17 Nov 2026: 16:9, 16:10,
        /// 4:3, 5:4, 21:9 ...). "auto" stays a valid stored value (old files, first run) but is not shown: it is displayed as
        /// the ratio of the resolution (DisplayAspect). Which ratios Tarkov really lists (32:9? 3:2?) is UNVERIFIED.</summary>
        public static readonly string[] GameAspectIds = new string[] { "16:9", "16:10", "4:3", "5:4", "21:9", "32:9", "3:2", "1:1" };

        /// <summary>v4.2: a chosen ratio within 3 % of the resolution's own ratio counts as native (no stretch): real
        /// "21:9" screens are 64:27 (2560×1080, 1.6 %) or 43:18 (3440×1440, 2.4 %), 1366×768 is 0.05 % off 16:9. UNVERIFIED
        /// that the game treats these as native.</summary>
        public const double NativeTolerance = 0.03;

        /// <summary>v4.2: the ratio id the 画面比例 box shows: the stored id, or for "auto" the GameAspectIds entry closest to
        /// the resolution's ratio (by relative difference).</summary>
        public static string DisplayAspect(string id, double w, double h)
        {
            if (id != null && id != "auto" && NormalizeAspect(id) == id) return id;
            double sa = (w > 0 && h > 0) ? w / h : 16.0 / 9.0, best = double.MaxValue;
            string pick = GameAspectIds[0];
            foreach (string g in GameAspectIds)
            {
                double d = Math.Abs(AspectValue(g, w, h) / sa - 1.0);
                if (d < best - 1e-12) { best = d; pick = g; }
            }
            return pick;
        }

        /// <summary>The aspect the game's picture is drawn at: the chosen ratio, or the resolution's own for "auto" and for a
        /// ratio within NativeTolerance of it.</summary>
        public static double EffectiveAspect(string id, double w, double h)
        {
            double sa = (w > 0 && h > 0) ? w / h : 1.0;
            if (id == null || id == "auto") return sa;
            double a = AspectValue(id, w, h);
            return (a > 0 && Math.Abs(a / sa - 1.0) >= NativeTolerance) ? a : sa;
        }

        /// <summary>v4.2 stretch model (Leo 17 Nov 2026): the game draws its picture at 'aspect' and stretches it to fill the
        /// whole w × h screen (no black bars), so the picture always has the full screen height. rw × rh is that picture in
        /// screen-height pixels BEFORE the horizontal stretch: rh = h, rw = h · aspect (= w when native). UNVERIFIED that
        /// Tarkov stretches rather than letterboxes.</summary>
        public static void RenderRect(double w, double h, string aspect, out double rw, out double rh)
        {
            RenderRect(w, h, aspect, false, out rw, out rh);
        }

        /// <summary>v5.1 (decision 11): fit = the picture is NOT stretched: it keeps its aspect a inside the w × h screen, as big
        /// as fits (a narrower than the screen: full height, rw = h · a; wider: full width, rh = w / a, bars top and bottom).
        /// fit false = the stretch model above (rh = h always). Native ratio: rw × rh = w × h either way.</summary>
        public static void RenderRect(double w, double h, string aspect, bool fit, out double rw, out double rh)
        {
            rw = w; rh = h;
            if (w <= 0 || h <= 0) return;
            double a = EffectiveAspect(aspect, w, h);
            if (fit && a > w / h) { rw = w; rh = w / a; return; }
            rw = h * a;
        }

        /// <summary>v4.2: horizontal stretch of the game picture on the screen = resolution ratio / chosen ratio (1 when
        /// native). 1920×1080 at 4:3 → 1.3333 (things look wider); 1280×1024 (5:4) at 16:9 → 0.7031. UNVERIFIED (see
        /// RenderRect).</summary>
        public static double StretchX(double w, double h, string aspect, bool fit)
        {
            return fit ? 1.0 : StretchX(w, h, aspect);
        }

        public static double StretchX(double w, double h, string aspect)
        {
            if (!(w > 0) || !(h > 0)) return 1.0;
            double a = EffectiveAspect(aspect, w, h), sa = w / h;
            return (a > 0) ? sa / a : 1.0;
        }

        /// <summary>Half the picture size along the FOV's axis ("h" = width, else height), in px.</summary>
        public static double HalfSpan(double w, double h, string aspect, string fovType)
        {
            return HalfSpan(w, h, aspect, fovType, false);
        }

        public static double HalfSpan(double w, double h, string aspect, string fovType, bool fit)
        {
            double rw, rh;
            RenderRect(w, h, aspect, fit, out rw, out rh);
            return (fovType == "h" ? rw : rh) / 2.0;
        }

        /// <summary>Focal length in px: f = half span / tan(FOV / 2).</summary>
        public static double FocalPx(double w, double h, string aspect, double fovDeg, string fovType)
        {
            return HalfSpan(w, h, aspect, fovType) / Math.Tan(fovDeg * Math.PI / 360.0);
        }

        /// <summary>k = f_game / f_base, computed as (half span ratio) × (tan ratio) so that equal FOVs give a tan ratio of
        /// exactly 1 and 跟随 + 垂直 gives exactly 游戏高度 / 调参高度 (bit for bit the v4.0 scale). 1 when anything is unusable.</summary>
        public static double Factor(double gameW, double gameH, string gameAspect, double gameFov,
                                    double baseW, double baseH, string baseAspect, double baseFov, string fovType)
        {
            return Factor(gameW, gameH, gameAspect, gameFov, baseW, baseH, baseAspect, baseFov, fovType, false);
        }

        /// <summary>v5.1: Factor with the fit (不拉伸) model on both screens when fit is true.</summary>
        public static double Factor(double gameW, double gameH, string gameAspect, double gameFov,
                                    double baseW, double baseH, string baseAspect, double baseFov, string fovType, bool fit)
        {
            double hg = HalfSpan(gameW, gameH, gameAspect, fovType, fit), hb = HalfSpan(baseW, baseH, baseAspect, fovType, fit);
            if (!(hg > 0) || !(hb > 0) || !(gameFov > 0) || !(baseFov > 0) || gameFov >= 180 || baseFov >= 180) return 1.0;
            double tr = gameFov == baseFov ? 1.0 : Math.Tan(baseFov * Math.PI / 360.0) / Math.Tan(gameFov * Math.PI / 360.0);
            return (hg / hb) * tr;
        }

        public static double Factor(GlobalSettings g)
        {
            if (g == null) return 1.0;
            return Factor(g.ResGameW, g.ResGameH, g.ResAspect, g.ResFov, g.ResBaseW, g.ResBaseH, g.ResBaseAspect, g.ResBaseFov, "v", !g.PictureStretched);
        }

        /// <summary>v4.2: the X factor = Factor (the Y factor) × the game screen's horizontal stretch ÷ the 调参 screen's
        /// (StretchX). Native ratio on both sides → exactly Factor. UNVERIFIED stretch model (see RenderRect).</summary>
        public static double FactorX(double gameW, double gameH, string gameAspect, double gameFov,
                                     double baseW, double baseH, string baseAspect, double baseFov)
        {
            double k = Factor(gameW, gameH, gameAspect, gameFov, baseW, baseH, baseAspect, baseFov, "v");
            double sg = StretchX(gameW, gameH, gameAspect), sb = StretchX(baseW, baseH, baseAspect);
            if (sg == sb) return k;
            double kx = k * sg / sb;
            return (kx > 0 && !double.IsInfinity(kx) && !double.IsNaN(kx)) ? kx : k;
        }

        public static double FactorX(GlobalSettings g)
        {
            if (g == null) return 1.0;
            if (!g.PictureStretched) return Factor(g); // v5.1: 不拉伸 -> no X factor
            return FactorX(g.ResGameW, g.ResGameH, g.ResAspect, g.ResFov, g.ResBaseW, g.ResBaseH, g.ResBaseAspect, g.ResBaseFov);
        }

        /// <summary>Converts a horizontal FOV to the vertical FOV of a picture with aspect a (= w / h), in degrees.</summary>
        public static double HorizontalToVertical(double hFovDeg, double aspect)
        {
            return 2.0 * Math.Atan(Math.Tan(hFovDeg * Math.PI / 360.0) / aspect) * 360.0 / (2.0 * Math.PI);
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
        /// <summary>v4.0 package names (Chinese + English). The v3.6 names are still accepted.</summary>
        public static readonly string[] AppFolderNames = new string[] { "程序 app", "app" };
        public static readonly string[] BatNames = new string[] { "启动 Start-HipAimer.bat", "Start-HipAimer.bat" };
        /// <summary>User folder for uploaded 准星样式 PNGs, next to settings.json.</summary>
        public const string CrosshairFolderName = "准星样式 Crosshairs";

        /// <summary>v5.0.2 (W19, Leo): runtime files (settings.json, crash.log, shade-*.json, filter-prefs.json, backups, *.bad,
        /// *.tmp) live in this sub-folder next to the exe, never at the top level.</summary>
        public const string DataFolderName = "数据 Data";

        private static readonly object dataDirLock = new object();
        private static string dataDirCache;

        /// v3.6: the exe lives in <root>\app\; user files (settings.json, crash.log) live in <root> next to the bat.
        /// v4.0: the folder may be 「程序 app」 and the bat 「启动 Start-HipAimer.bat」.
        /// v5.0.2 (W19): runtime files live in 「数据 Data」 next to the exe (created and migrated on the first call, see
        /// DataDirFor); TopDir() is the old location (still used for 「准星样式 Crosshairs」 and README).
        public static string UserDir()
        {
            string d = dataDirCache;
            if (d != null) return d;
            string note = null;
            lock (dataDirLock)
            {
                if (dataDirCache == null)
                    dataDirCache = DataDirFor(AppDomain.CurrentDomain.BaseDirectory, TopDir(), out note);
                d = dataDirCache;
            }
            if (note != null) Program.Log(note);
            return d;
        }

        /// <summary>The pre-v5.0.2 user folder (exe folder, or its parent when the bat sits there): top-level files such as
        /// 「准星样式 Crosshairs」 and 说明 README.txt.</summary>
        public static string TopDir()
        {
            return UserDirFor(AppDomain.CurrentDomain.BaseDirectory, File.Exists);
        }

        /// <summary>Exact runtime file names moved into 「数据 Data」.</summary>
        public static readonly string[] RuntimeFileNames = new string[] {
            "settings.json", "crash.log", "crash.old.log", "shade-settings.json", "shade-recovery.json", "shade-engine.lock",
            "filter-prefs.json", "net-prefs.json", "ui-advanced.txt", "pilot-presets.json", "startup.jitprofile" };

        /// <summary>Name stems of our backups / damaged / temp files (settings.before-import-*.json, shade-recovery.bad-*.json,
        /// *.bak, *.bad, *.tmp of the files above). Unrelated files in the old folder never match.</summary>
        private static readonly string[] RuntimeStems = new string[] {
            "settings.", "crash.", "shade-settings.", "shade-recovery.", "shade-engine.", "filter-prefs.", "net-prefs.",
            "ui-advanced.", "pilot-presets." };

        /// <summary>Is 'name' (a file name) one of our runtime files? *.old (already migrated) never is.</summary>
        public static bool IsRuntimeFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) return false;
            int oi = name.LastIndexOf(".old-", StringComparison.OrdinalIgnoreCase);
            if (oi >= 0 && IsDigits(name.Substring(oi + 5))) return false; // <name>.old-2 ...
            foreach (string n in RuntimeFileNames) if (string.Equals(name, n, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string s in RuntimeStems)
            {
                if (!name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = name.Substring(s.Length);
                if (s == "settings." && rest.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true; // backups
                if (rest.EndsWith(".bad", StringComparison.OrdinalIgnoreCase) || rest.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || rest.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("bad-", StringComparison.OrdinalIgnoreCase)
                    || rest.IndexOf(".bad", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// v5.0.2 (W19): the data folder for an exe folder: &lt;exeDir&gt;\数据 Data (created). On the first run every runtime file
        /// in 'oldDir' (the folder UserDirFor gave before v5.0.2) that is not in 数据 Data yet is copied there and the old one is
        /// renamed to &lt;name&gt;.old. When 数据 Data cannot be created or written (read-only folder), 'oldDir' is returned and
        /// nothing moves. note = one crash.log line (null when nothing happened). Never throws.
        /// </summary>
        public static string DataDirFor(string exeDir, string oldDir, out string note)
        {
            note = null;
            string exe = (exeDir ?? "").TrimEnd('\\', '/');
            string old = string.IsNullOrEmpty(oldDir) ? exe : oldDir.TrimEnd('\\', '/');
            string data;
            try
            {
                data = Path.Combine(exe, DataFolderName);
                Directory.CreateDirectory(data);
                string probe = Path.Combine(data, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                note = "data folder: 「" + DataFolderName + "」 cannot be created or written (" + ex.GetType().Name + "), using " + old;
                return old;
            }
            try
            {
                if (SamePath(old, data) || !Directory.Exists(old)) return data;
                int moved = 0;
                List<string> failed = new List<string>();
                foreach (string src in Directory.GetFiles(old))
                {
                    string name = Path.GetFileName(src);
                    if (!IsRuntimeFile(name)) continue;
                    string dst = Path.Combine(data, name);
                    if (File.Exists(dst)) continue; // already there: the new copy wins, the old file is left alone
                    try
                    {
                        File.Copy(src, dst, false);
                        moved++;
                    }
                    catch (Exception) { failed.Add(name); continue; }
                    try
                    {
                        string bak = src + ".old";
                        for (int i = 2; (File.Exists(bak) || Directory.Exists(bak)) && i < 100; i++) bak = src + ".old-" + i;
                        File.Move(src, bak);
                    }
                    catch (Exception) { failed.Add(name + " (rename)"); }
                }
                if (moved > 0 || failed.Count > 0)
                    note = "data folder: moved " + moved + " file(s) from " + old + " to " + data
                        + (failed.Count > 0 ? "; not moved: " + string.Join(", ", failed.ToArray()) : "");
            }
            catch (Exception ex) { note = "data folder: migration from " + old + " failed: " + ex.GetType().Name; }
            return data;
        }

        /// <summary>Pure rule behind UserDir (testable): exeDir's parent when exeDir is an app folder and a bat sits in the parent.</summary>
        public static string UserDirFor(string exeDir, Func<string, bool> fileExists)
        {
            return UserDirFor(exeDir, fileExists, ListBatFiles);
        }

        /// <summary>Real directory listing behind UserDirFor: files named "* Start-HipAimer.bat" in dir.</summary>
        private static string[] ListBatFiles(string dir)
        {
            return Directory.GetFiles(dir, "* " + BatSuffix);
        }

        public const string BatSuffix = "Start-HipAimer.bat";

        /// <summary>
        /// v4.0 (PACKAGE request): also tolerant of zip tools that garble the Chinese names. The exe folder may be
        /// 「app」 or any 「&lt;x&gt; app」 (e.g. 「程序 app」 or a garbled 「绋嬪簭 app」); the parent must hold one of BatNames or
        /// any 「&lt;x&gt; Start-HipAimer.bat」. listBats(dir) lists candidate bat paths (injectable for tests; may throw).
        /// </summary>
        public static string UserDirFor(string exeDir, Func<string, bool> fileExists, Func<string, string[]> listBats)
        {
            string dir = (exeDir ?? "").TrimEnd('\\', '/');
            try
            {
                string parent = Path.GetDirectoryName(dir);
                string name = Path.GetFileName(dir) ?? "";
                bool appFolder = string.Equals(name, "app", StringComparison.OrdinalIgnoreCase)
                    || (name.Length > 4 && name.EndsWith(" app", StringComparison.OrdinalIgnoreCase));
                if (appFolder && !string.IsNullOrEmpty(parent))
                {
                    foreach (string b in BatNames) if (fileExists(Path.Combine(parent, b))) return parent;
                    if (listBats != null)
                    {
                        try
                        {
                            string[] found = listBats(parent);
                            if (found != null)
                                foreach (string f in found)
                                {
                                    string fn = Path.GetFileName(f ?? "") ?? "";
                                    if (fn.Length > BatSuffix.Length + 1 && fn.EndsWith(" " + BatSuffix, StringComparison.OrdinalIgnoreCase))
                                        return parent;
                                }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            return dir;
        }

        /// <summary>「准星样式 Crosshairs」 at the top level (TopDir, where it was before v5.0.2; never in 数据 Data; not created here).</summary>
        public static string CrosshairDir()
        {
            return Path.Combine(TopDir(), CrosshairFolderName);
        }

        /// <summary>v4.0: real-pixel size of the primary screen ({width, height}); set by Program before Load
        /// (ScreenDetect.PrimaryPixels on Windows). null = no detection (tests, other OS): the built-in default stays.</summary>
        public static Func<int[]> ScreenProbe;

        public static string DefaultPath()
        {
            return Path.Combine(UserDir(), "settings.json");
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

        /// <summary>Parses and validates. Never throws; returns false with an error message on bad input.
        /// v4.0: tolerant -- a member with the wrong JSON type (e.g. a string where a number belongs) is dropped and
        /// gets its default; every other value is kept (see the 4-argument overload for the list of dropped members).</summary>
        public static bool TryParse(string json, out GlobalSettings result, out string error)
        {
            List<string> dropped;
            return TryParse(json, out result, out error, out dropped);
        }

        /// <summary>
        /// v4.0 tolerant parse. Tries the file as is; if DataContractJsonSerializer rejects it, the JSON is walked against
        /// the settings classes, every member whose value has the wrong JSON type (or an int out of range / not whole, a
        /// non-finite number, a "__type" hint) is removed, and the cleaned JSON is parsed again. 'dropped' lists the removed
        /// members as paths, e.g. "Profiles[0].Diameter" (empty when the file was fine as is). Never throws.
        /// </summary>
        public static bool TryParse(string json, out GlobalSettings result, out string error, out List<string> dropped)
        {
            dropped = new List<string>();
            string head = json == null ? "" : json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (head.Length > 0 && head[0] == '{' && !TolerantJson.IsComplete(head))
            {
                // v4.1 (BRUTE40 P2-10): a cut-off file is never accepted as is (the serializer takes some of them and every
                // setting after the cut would silently become its default). Load salvages what it can instead.
                result = null; error = IncompleteError;
                return false;
            }
            // v5.1 (decision 17): "Keys": [] is accepted by the serializer as an empty key object (-> the old 侧键2 migration);
            // it is a wrong-type value, so it takes the tolerant path and gets Leo's v5 keys like any unreadable member.
            result = null; error = ImportBadError;
            if (!TopLevelArray(head, "Keys") && TryParseStrict(json, out result, out error)) return true;
            string strictError = error;
            try
            {
                string trimmed = json == null ? "" : json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                if (trimmed.Length == 0 || trimmed[0] != '{') return false;
                List<string> d = new List<string>();
                string cleaned;
                if (!TolerantJson.Clean(trimmed, typeof(GlobalSettings), d, out cleaned)) return false;
                GlobalSettings g; string e2;
                if (!TryParseStrict(cleaned, out g, out e2)) return false;
                FillDroppedFromLeoDefault(g, d); // v5.1 (decision 17)
                result = g; error = null; dropped = d;
                return true;
            }
            catch (Exception) { result = null; error = strictError; return false; }
        }

        /// <summary>True when the JSON object's member "name" (first match, outside strings) holds an array.</summary>
        private static bool TopLevelArray(string json, string name)
        {
            if (json == null) return false;
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;
            i += key.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != ':') return false;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            return i < json.Length && json[i] == '[';
        }

        private static bool TryParseStrict(string json, out GlobalSettings result, out string error)
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
                Program.Log("import: " + ex);
                error = "文件内容不完整或格式不对，不是准星配置文件。";
                return false;
            }
        }

        // ---------------- v5.1 (decision 17): an unreadable value gets Leo's v5 default ----------------

        /// <summary>v5.1: the reference for unreadable values: Leo's v5 settings (LeoDefaultJson), every profile path taken
        /// from its profile 「Leo默认 (旧)」, with the v5.1 decisions on top (强化 key = the new default Ctrl+T, decision 3).
        /// Null if the text cannot be read.</summary>
        internal static GlobalSettings LeoReference(out Profile profile)
        {
            profile = null;
            GlobalSettings r; string e;
            if (!TryParseStrict(LeoDefaultJson.Text, out r, out e) || r == null) return null;
            KeyBindings kd = new KeyBindings();
            r.Keys.Boost = kd.Boost; r.Keys.BoostMods = kd.BoostMods;
            profile = r.FindProfile(LeoDefaultJson.ProfileName);
            if (profile == null && r.Profiles.Count > 0) profile = r.Profiles[0];
            return r;
        }

        /// <summary>
        /// v5.1 (decision 17): every member the tolerant reader dropped (paths like "Fps", "Keys.Boost", "Profiles[1].Diameter",
        /// "Profiles[0].Dirs[3].OffX", "Profiles[0].Jump") is set to the value at the same path in LeoReference (any profile
        /// index -> Leo's profile), not to the class default; then g is validated again. A dropped whole list item
        /// ("Profiles[2]") or a path the reference does not have keeps the class default. Never throws.
        /// </summary>
        internal static int FillDroppedFromLeoDefault(GlobalSettings g, List<string> dropped)
        {
            if (g == null || dropped == null || dropped.Count == 0) return 0;
            int n = 0;
            try
            {
                Profile rp;
                GlobalSettings r = LeoReference(out rp);
                if (r == null) return 0;
                foreach (string path in dropped)
                {
                    if (path == "NetStatsConsent") continue; // W28: consent stays null (ask), never Leo's answer
                    try
                    {
                        if (!FillPath(g, r, rp, path)) continue;
                        n++;
                        // a key comes with its 组合键 (an unreadable 强化 key -> Ctrl+T, never plain T = 战术设备开关)
                        if (path.StartsWith("Keys.", StringComparison.Ordinal) && !path.EndsWith("Mods", StringComparison.Ordinal)
                            && DataMemberOf(typeof(KeyBindings), path.Substring(5) + "Mods") != null)
                            FillPath(g, r, rp, path + "Mods");
                    }
                    catch (Exception ex) { Program.Log("fill dropped " + path + ": " + ex.Message); }
                }
                if (n > 0)
                {
                    int fv = g.FileVersion; string[] notes = g.KeyNotes; string bak = g.BackupBeforeSave;
                    g.Validate();
                    g.FileVersion = fv; g.KeyNotes = notes; g.BackupBeforeSave = bak;
                }
            }
            catch (Exception ex) { Program.Log("fill dropped: " + ex.Message); }
            return n;
        }

        private static bool FillPath(GlobalSettings g, GlobalSettings r, Profile rp, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string[] segs = path.Split('.');
            object dst = g, src = r;
            for (int i = 0; i < segs.Length; i++)
            {
                string name = segs[i];
                int idx = -1;
                int br = name.IndexOf('[');
                if (br >= 0)
                {
                    int close = name.IndexOf(']', br);
                    if (close < 0 || !int.TryParse(name.Substring(br + 1, close - br - 1), out idx)) return false;
                    name = name.Substring(0, br);
                }
                bool last = i == segs.Length - 1;
                System.Reflection.MemberInfo m = DataMemberOf(dst.GetType(), name);
                if (m == null) return false;
                if (last && idx < 0)
                {
                    if (name == "Name" && dst is Profile) return false; // a profile keeps its own (default) name
                    SetMember(dst, m, DeepCopy(GetMember(src, m)));
                    return true;
                }
                object dv = GetMember(dst, m), sv = GetMember(src, m);
                if (idx >= 0)
                {
                    if (last) return false; // a whole list item: keep the class default
                    System.Collections.IList dl = dv as System.Collections.IList, sl = sv as System.Collections.IList;
                    if (dl == null || sl == null || idx >= dl.Count) return false;
                    dv = dl[idx];
                    sv = (name == "Profiles" && dst is GlobalSettings) ? (object)rp : (idx < sl.Count ? sl[idx] : null);
                }
                if (dv == null || sv == null) return false;
                dst = dv; src = sv;
            }
            return false;
        }

        private static System.Reflection.MemberInfo DataMemberOf(Type t, string jsonName)
        {
            System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (System.Reflection.MemberInfo m in t.GetMembers(bf))
            {
                if (!(m is System.Reflection.FieldInfo) && !(m is System.Reflection.PropertyInfo)) continue;
                object[] a = m.GetCustomAttributes(typeof(DataMemberAttribute), false);
                if (a.Length == 0) continue;
                string n = ((DataMemberAttribute)a[0]).Name ?? m.Name;
                if (n == jsonName) return m;
            }
            return null;
        }

        private static object GetMember(object o, System.Reflection.MemberInfo m)
        {
            System.Reflection.FieldInfo f = m as System.Reflection.FieldInfo;
            if (f != null) return f.GetValue(o);
            return ((System.Reflection.PropertyInfo)m).GetValue(o, null);
        }

        private static void SetMember(object o, System.Reflection.MemberInfo m, object v)
        {
            System.Reflection.FieldInfo f = m as System.Reflection.FieldInfo;
            if (f != null) { f.SetValue(o, v); return; }
            System.Reflection.PropertyInfo p = (System.Reflection.PropertyInfo)m;
            if (p.CanWrite) p.SetValue(o, v, null);
        }

        /// <summary>Strings, numbers, bools: as is; arrays and [DataContract] objects: a serializer round trip (no sharing).</summary>
        private static object DeepCopy(object v)
        {
            if (v == null || v is string || v.GetType().IsPrimitive || v is decimal) return v;
            Array a = v as Array;
            if (a != null && a.GetType().GetElementType().IsPrimitive) return a.Clone();
            DataContractJsonSerializer ser = new DataContractJsonSerializer(v.GetType());
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, v);
                ms.Position = 0;
                return ser.ReadObject(ms);
            }
        }

        /// <summary>TryParse error for a cut-off file.</summary>
        public const string IncompleteError = "文件不完整（可能没保存完）";
        /// <summary>The user-facing import error (Chinese, no exception text).</summary>
        public const string ImportBadError = "文件内容不完整或格式不对，不是准星配置文件。";

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
                if (!TolerantJson.IsComplete(t)) { error = ImportBadError; return false; } // v4.1 (BRUTE40 P2-10): a cut-off file is not imported
                List<Profile> list;
                int r = TryImportStrict(t, out list);
                if (r == 2)
                {
                    // v4.1 (BRUTE40 P2-9): as tolerant as settings.json: a wrong-typed member gets its default instead of
                    // refusing the whole file (a full settings.json is cleaned as settings, a single exported profile as one).
                    foreach (Type root in new Type[] { typeof(GlobalSettings), typeof(Profile) })
                    {
                        List<string> d = new List<string>();
                        string cleaned;
                        if (!TolerantJson.Clean(t, root, d, out cleaned)) break; // a syntax error: no use trying again
                        r = TryImportStrict(cleaned, out list);
                        if (r != 2) break;
                    }
                }
                if (r == 2) { error = ImportBadError; return false; }
                if (r == 1) { error = "文件里没有准星配置"; return false; }
                foreach (Profile p in list) { p.SavedDefault = null; p.SavedDefaultAt = null; p.Validate(); } // import never carries a snapshot
                profiles = list;
                return true;
            }
            catch (Exception ex)
            {
                Program.Log("import: " + ex);
                error = ImportBadError;
                return false;
            }
        }

        /// <summary>0 = profiles read, 1 = a JSON object without a profile, 2 = the serializer refused it. Never throws.</summary>
        private static int TryImportStrict(string t, out List<Profile> list)
        {
            list = new List<Profile>();
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(t);
                ImportProbe probe;
                using (MemoryStream ms = new MemoryStream(bytes))
                    probe = (ImportProbe)new DataContractJsonSerializer(typeof(ImportProbe)).ReadObject(ms);
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
                return list.Count == 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Program.Log("import: " + ex);
                list = new List<Profile>();
                return 2;
            }
        }

        /// <summary>Name suffix for an imported old profile whose name is already taken (「导入旧版设置」).</summary>
        public const string LegacySuffix = " (旧)";

        /// <summary>
        /// v4.0 「导入旧版设置」 (pure, never throws): the result has the OLD file's keys and global settings (already migrated
        /// by TryParse: W/A/S/D forced, 组合键 kept, old names renamed), ALL current profiles (so 「Leo默认」 stays) followed by
        /// ALL old profiles (a clashing name gets 「 (旧)」, then (2), (3)...), and the old file's active profile active.
        /// TutorialDone stays true if it was true in either. Neither input is changed. imported = number of old profiles.
        /// v4.1: this PC's screen (游戏分辨率 / 画面比例 / FOV / FOV 类型) and 调参基准 (调参分辨率 / 调参比例 / 调参 FOV) are kept
        /// from 'current'; profile values are copied as they are (raw px).
        /// </summary>
        public static GlobalSettings MergeLegacy(GlobalSettings current, GlobalSettings legacy, out int imported)
        {
            imported = 0;
            if (legacy == null) return current;
            GlobalSettings r = legacy.Clone(false);
            r.Profiles = new List<Profile>();
            if (current != null && current.Profiles != null)
                foreach (Profile p in current.Profiles) if (p != null) r.Profiles.Add(p.Clone());
            if (r.Profiles.Count == 0) r.Profiles.Add(Profile.CreateLeoDefault(GlobalSettings.DefaultProfileName));
            string active = null;
            string oldActive = legacy.ActiveProfile;
            if (legacy.Profiles != null)
                foreach (Profile p in legacy.Profiles)
                {
                    if (p == null) continue;
                    Profile c = p.Clone();
                    string want = string.IsNullOrEmpty(c.Name) ? "配置" : c.Name.Trim();
                    if (r.FindProfile(want) != null) want = want + LegacySuffix;
                    c.Name = r.UniqueName(want, null);
                    if (c.SavedDefault != null) c.SavedDefault.Name = c.Name;
                    r.Profiles.Add(c);
                    imported++;
                    if (active == null && p.Name == oldActive) active = c.Name;
                }
            if (active == null && imported > 0) active = r.Profiles[r.Profiles.Count - imported].Name;
            r.ActiveProfile = active ?? (current != null && current.FindProfile(current.ActiveProfile) != null ? current.ActiveProfile : r.Profiles[0].Name);
            r.TutorialDone = legacy.TutorialDone || (current != null && current.TutorialDone);
            if (current != null)
            {
                // v4.1 (BRUTE40 P1-4, Leo 17 Nov): the import keeps THIS screen (游戏分辨率 / 画面比例 / FOV / FOV 类型); an old
                // file's 2160×1440 is its 调参分辨率, not the player's screen.
                r.ResGameW = current.ResGameW; r.ResGameH = current.ResGameH;
                r.ResAspect = current.ResAspect; r.ResFov = current.ResFov; r.ResFovType = current.ResFovType;
                // v4.1 (REBRUTE41 P2-1, same rule): the 调参基准 (调参分辨率 / 调参比例 / 调参 FOV) is kept too, so the profiles that are
                // already there stay where they are on screen. It is one global value: the imported old profiles keep their raw
                // px and are shown at this 调参基准 (the same as their own for a user who never changed it: 2160×1440 / 跟随 / 75).
                r.ResBaseW = current.ResBaseW; r.ResBaseH = current.ResBaseH;
                r.ResBaseAspect = current.ResBaseAspect; r.ResBaseFov = current.ResBaseFov;
                r.ResV41Present = true; // these are real v4.1 values: Validate must not reset them to the pre-v4.1 lock
            }
            r.Validate();
            return r;
        }

        /// <summary>
        /// The settings.json to import from a picked path: the file itself, or inside a picked folder: settings.json in it, in
        /// any sub-folder (「app」 / 「* app」), or one more level down (an unzipped old package). null if none.
        /// </summary>
        public static string FindLegacyFile(string picked) { return FindLegacyFile(picked, null); }

        /// <summary>
        /// v4.1 (BRUTE40 P1-2 / P2-8): in a picked folder only a settings.json that really is ours counts (another program's
        /// settings.json is never picked), and 'exclude' (the settings.json in use) is skipped. A picked FILE is returned as it
        /// is (ImportLegacy checks it). null if none. Never throws.
        /// </summary>
        public static string FindLegacyFile(string picked, string exclude)
        {
            try
            {
                if (string.IsNullOrEmpty(picked)) return null;
                if (File.Exists(picked)) return picked;
                if (!Directory.Exists(picked)) return null;
                foreach (string f in LegacyCandidates(picked))
                {
                    if (!string.IsNullOrEmpty(exclude) && SamePath(f, exclude)) continue;
                    if (IsOurSettingsFile(f)) return f;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Existing settings.json files in a picked folder, its sub-folders and one level deeper (that order).</summary>
        private static List<string> LegacyCandidates(string folder)
        {
            List<string> cand = new List<string>(), found = new List<string>();
            try
            {
                cand.Add(Path.Combine(folder, "settings.json"));
                string[] subs = Directory.GetDirectories(folder);
                foreach (string d in subs) cand.Add(Path.Combine(d, "settings.json"));
                foreach (string d in subs)
                {
                    try
                    {
                        foreach (string d2 in Directory.GetDirectories(d))
                        {
                            cand.Add(Path.Combine(d2, "settings.json"));
                            cand.Add(Path.Combine(d2, DataFolderName, "settings.json")); // v5.0.2 (W19): <pkg>\程序 app\数据 Data
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            foreach (string f in cand) if (File.Exists(f)) found.Add(f);
            return found;
        }

        private static bool IsOurSettingsFile(string f)
        {
            try
            {
                FileInfo fi = new FileInfo(f);
                bool oldEncoding;
                return fi.Length <= 8 * 1024 * 1024 && LooksLikeCrosshairSettings(ReadSettingsText(f, out oldEncoding));
            }
            catch (Exception) { return false; }
        }

        /// <summary>Same file? (full paths; case-insensitive on Windows).</summary>
        public static bool SamePath(string a, string b)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
                StringComparison c = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), c);
            }
            catch (Exception) { return false; }
        }

        /// <summary>v4.1 (BRUTE40 P1-2): a crosshair settings.json is a JSON object whose "Profiles" list holds at least one object
        /// with a profile setting other than Name (Dirs, Diameter, FollowMs, ...). {} or another program's settings.json is
        /// rejected (TryParse alone would accept it: Validate adds a default profile). Comments and trailing commas are
        /// tolerated. Never throws.</summary>
        public static bool LooksLikeCrosshairSettings(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try { return TolerantJson.HasProfileObject(json); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 「导入旧版设置」 file step: reads 'legacyPath' (settings.json or a folder with one), merges it into 'current'
        /// (MergeLegacy), backs up 'targetPath' as settings.before-import-yyyyMMdd-HHmmss.json and saves the result there.
        /// Returns the merged settings or null with 'error' (Chinese, for the user).
        /// </summary>
        public static GlobalSettings ImportLegacy(GlobalSettings current, string legacyPath, string targetPath, out int imported, out string error)
        {
            imported = 0; error = null;
            string file = FindLegacyFile(legacyPath, targetPath);
            if (file == null) { error = LegacyNotFound(legacyPath, targetPath); return null; }
            if (SamePath(file, targetPath))
            {
                // v4.1 (BRUTE40 P2-8): importing the file in use would only copy every profile once more
                error = "这是现在正在用的设置文件，里面的配置已经都在了。请选旧版文件夹里的 settings.json。";
                return null;
            }
            string text;
            bool oldEncoding;
            try { text = ReadSettingsText(file, out oldEncoding); }
            catch (Exception ex) { Program.Log("import read: " + ex); error = "读取失败：文件打不开（可能被别的程序占用）。"; return null; }
            GlobalSettings legacy; string err;
            if (!LooksLikeCrosshairSettings(text) || !TryParse(text, out legacy, out err)) { error = "这个文件不是准星设置文件，或者已经损坏。"; return null; }
            GlobalSettings merged = MergeLegacy(current, legacy, out imported);
            if (imported == 0) { error = "这个文件里没有准星配置。"; return null; }
            merged.KeyNotes = ConcatNotes(legacy.KeyNotes, merged.KeyNotes); // v4.1: key fixes (old file + merge), for the import message
            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
            {
                string bakErr;
                if (BackupCopy(targetPath, "before-import", out bakErr) == null)
                {
                    error = "备份当前设置失败，所以没有导入（" + bakErr + "）。";
                    return null;
                }
            }
            string saveErr = Save(merged, targetPath);
            if (!string.IsNullOrEmpty(saveErr)) { error = "保存失败：" + saveErr; return null; }
            return merged;
        }

        /// <summary>Why FindLegacyFile found nothing (Chinese, for the user).</summary>
        private static string LegacyNotFound(string picked, string inUse)
        {
            try
            {
                if (!string.IsNullOrEmpty(picked) && Directory.Exists(picked))
                {
                    List<string> c = LegacyCandidates(picked);
                    bool onlyInUse = c.Count > 0 && c.TrueForAll(delegate(string f) { return SamePath(f, inUse) || !IsOurSettingsFile(f); })
                        && c.Exists(delegate(string f) { return SamePath(f, inUse); });
                    if (onlyInUse) return "这个文件夹里只有现在正在用的 settings.json，里面的配置已经都在了。请选旧版的文件夹。";
                    if (c.Count > 0) return "这个文件夹里的 settings.json 不是准星的设置文件。请选旧版准星的文件夹，或者直接选旧版的 settings.json。";
                }
            }
            catch (Exception) { }
            return "这里没找到 settings.json。请选旧版文件夹里的 settings.json，或者旧版的整个文件夹。";
        }

        /// <summary>
        /// Loads settings. Missing file: writes and returns the defaults (message null). Every other case returns usable
        /// settings and, when something had to change, a Chinese 'message' for the caller to show. v4.1: the file is only
        /// rewritten after an exact copy of it was made (settings.bad-*.json, settings.newer-*.json; names never reused);
        /// when no copy can be made it is left untouched and the next Save tries again (GlobalSettings.BackupBeforeSave).
        /// </summary>
        public static GlobalSettings Load(string path, out string message)
        {
            message = null;
            if (!File.Exists(path))
            {
                GlobalSettings d = FreshDefaults(); // v4.0 first run: 游戏分辨率 = this screen (real pixels)
                Save(d, path);
                return d;
            }
            string text;
            bool oldEncoding, damagedText;
            try { text = DecodeSettingsBytes(File.ReadAllBytes(path), out oldEncoding, out damagedText); }
            catch (Exception ex)
            {
                Program.Log("settings read: " + ex);
                message = "读取设置文件失败（文件可能被别的程序占用）。\n这次先使用默认设置；原文件没有改动，下次保存前会先备份它。";
                GlobalSettings d0 = FreshDefaults();
                d0.BackupBeforeSave = path;
                return d0;
            }

            GlobalSettings g; string err;
            List<string> dropped;
            if (TryParse(text, out g, out err, out dropped))
            {
                List<string> top = TolerantJson.TopLevelKeys(text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n'));
                if (top != null && !HasKnownMember(top))
                {
                    // v4.1 (BRUTE40 P2-12): {} (or a file with none of our settings) is a first run, not class defaults
                    GlobalSettings fresh = FreshDefaults();
                    bool foreign = top.Exists(delegate(string k) { return k != "Version"; });
                    if (!foreign) { Save(fresh, path); return fresh; }
                    message = Rewrite(path, fresh, "bad", "settings.json 里没有准星的设置，已换成默认设置。");
                    return fresh;
                }
                bool newer = g.FileVersion > GlobalSettings.FormatVersion;
                if (dropped.Count == 0 && g.KeyNotes.Length == 0 && !newer && !oldEncoding && !damagedText) return g;
                StringBuilder m = new StringBuilder();
                if (dropped.Count > 0) m.Append(DroppedMessage(dropped, null));
                if (g.KeyNotes.Length > 0) AddLine(m, KeyNotesText(g.KeyNotes));
                if (newer) AddLine(m, "这个设置文件来自更新的版本，这个版本不认识的设置不会保留。");
                if (oldEncoding) AddLine(m, "设置文件用的是旧的文字编码，已按中文读出，会改存成新的编码。如果配置名字显示成乱码，请用备份文件找回。");
                if (damagedText) AddLine(m, DamagedTextLine);
                message = Rewrite(path, g, newer ? "newer" : "bad", m.ToString());
                return g;
            }

            // v4.1 (BRUTE40 P2-10): a cut-off or damaged file: keep everything that can still be read
            GlobalSettings s; int profiles; bool hasRes;
            if (TrySalvage(text, out s, out profiles, out hasRes))
            {
                if (!hasRes) s.DetectGameResolution(ScreenProbe); // the cut was before 游戏分辨率: use this screen, like a first run
                StringBuilder m = new StringBuilder("设置文件不完整或有错误（可能没保存完）。能读出的设置都保留了");
                m.Append(profiles > 0 ? "（" + profiles + " 套配置）" : "（配置没能读出来，先用「" + GlobalSettings.DefaultProfileName + "」）");
                m.Append("，其余的恢复默认值。");
                if (s.KeyNotes.Length > 0) AddLine(m, KeyNotesText(s.KeyNotes));
                if (damagedText) AddLine(m, DamagedTextLine);
                message = Rewrite(path, s, "bad", m.ToString());
                return s;
            }

            Program.Log("settings parse: " + err);
            GlobalSettings def = FreshDefaults();
            message = Rewrite(path, def, "bad", "设置文件无法读取，现在使用默认设置。");
            return def;
        }

        /// <summary>Keeps an exact copy of 'path', then saves g over it. Returns 'why' plus where the copy is; if no copy could
        /// be made, the file is left as it is (and g.BackupBeforeSave makes the next Save try the copy first).</summary>
        private static string Rewrite(string path, GlobalSettings g, string kind, string why)
        {
            string err;
            string copy = BackupCopy(path, kind, out err);
            StringBuilder m = new StringBuilder(why);
            if (copy != null)
            {
                Save(g, path);
                AddLine(m, "原文件已备份为 " + Path.GetFileName(copy) + "。");
            }
            else
            {
                g.BackupBeforeSave = path;
                AddLine(m, "原文件暂时没能备份（" + err + "），所以先没有改动它，下次保存前会再试。");
            }
            return m.ToString();
        }

        private static void AddLine(StringBuilder m, string line)
        {
            if (m.Length > 0) m.Append('\n');
            m.Append(line);
        }

        private static string[] ConcatNotes(string[] a, string[] b)
        {
            List<string> l = new List<string>();
            if (a != null) l.AddRange(a);
            if (b != null) foreach (string x in b) if (!l.Contains(x)) l.Add(x);
            return l.ToArray();
        }

        /// <summary>The load / import line for GlobalSettings.KeyNotes.</summary>
        public static string KeyNotesText(string[] notes)
        {
            return "有按键重复或用不了，已自动改好：" + string.Join("、", notes) + "。需要的话可以在「按键」页再改。";
        }

        /// <summary>A first-run default: 「Leo默认」 and 游戏分辨率 = this screen (ScreenProbe).</summary>
        private static GlobalSettings FreshDefaults()
        {
            GlobalSettings d = GlobalSettings.CreateDefault();
            d.DetectGameResolution(ScreenProbe);
            d.Validate();
            return d;
        }

        /// <summary>A member of GlobalSettings other than Version.</summary>
        private static bool HasKnownMember(List<string> keys)
        {
            foreach (string k in keys) if (k != "Version" && TolerantJson.IsMember(typeof(GlobalSettings), k)) return true;
            return false;
        }

        private static bool TrySalvage(string text, out GlobalSettings g, out int profiles, out bool hasRes)
        {
            g = null; profiles = 0; hasRes = false;
            try
            {
                List<string> d = new List<string>(), top;
                string cleaned;
                if (!TolerantJson.Salvage(text, typeof(GlobalSettings), d, out cleaned, out top, out profiles)) return false;
                if (!HasKnownMember(top)) return false;
                GlobalSettings r; string e;
                if (!TryParseStrict(cleaned, out r, out e)) return false;
                FillDroppedFromLeoDefault(r, d); // v5.1 (decision 17)
                g = r;
                hasRes = top.Contains("ResGameW") && top.Contains("ResGameH");
                return true;
            }
            catch (Exception ex) { Program.Log("settings salvage: " + ex); return false; }
        }

        /// <summary>
        /// v4.1 (BRUTE40 P2-6 / P2-7): copies 'src' to settings.&lt;kind&gt;-yyyyMMdd-HHmmss.json next to it; if that name is
        /// taken (by a file or a folder) -2, -3, ... is added. Never overwrites. Returns the copy's path, or null with 'error'
        /// (Chinese).
        /// </summary>
        /// <summary>The copy step of BackupCopy (src, dst; must not overwrite). Tests replace it to simulate a failing copy.</summary>
        internal static Action<string, string> CopyFile = delegate(string src, string dst) { File.Copy(src, dst, false); };

        public static string BackupCopy(string src, string kind, out string error)
        {
            error = null;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(src));
                string stem = "settings." + kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                for (int i = 1; i <= 500; i++)
                {
                    string p = Path.Combine(dir, stem + (i == 1 ? "" : "-" + i) + ".json");
                    if (File.Exists(p) || Directory.Exists(p)) continue;
                    try { CopyFile(src, p); return p; }
                    catch (IOException) { if (File.Exists(p) || Directory.Exists(p)) continue; throw; }
                }
                error = "备份文件名都被占用了";
            }
            catch (Exception ex)
            {
                Program.Log("settings backup: " + ex);
                error = "文件可能被别的程序占用，或者文件夹不能写入";
            }
            return null;
        }

        /// <summary>v4.1 (BRUTE40 P2-11): reads a settings file in the encoding it really has (see DecodeSettingsBytes).</summary>
        public static string ReadSettingsText(string path)
        {
            bool oldEncoding;
            return ReadSettingsText(path, out oldEncoding);
        }

        /// <summary>oldEncoding = the file was not UTF-8 / had no BOM (GBK, ANSI, UTF-16 without BOM).</summary>
        public static string ReadSettingsText(string path, out bool oldEncoding)
        {
            return DecodeSettingsBytes(File.ReadAllBytes(path), out oldEncoding);
        }

        /// <summary>
        /// v4.1 (BRUTE40 P2-11): bytes -> text. A BOM decides (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE); without one, every other
        /// byte being 0 means UTF-16 (JSON is mostly ASCII); then strict UTF-8; else the old Windows encoding a Chinese
        /// Notepad saved ("ANSI": the system code page when it is a CJK one, else GB18030), never silently garbled UTF-8.
        /// Trailing NUL padding is dropped. oldEncoding = not UTF-8 / not marked by a BOM. Never throws.
        /// </summary>
        public static string DecodeSettingsBytes(byte[] b, out bool oldEncoding)
        {
            bool damaged;
            return DecodeSettingsBytes(b, out oldEncoding, out damaged);
        }

        /// <summary>The Load line for a UTF-8 file with a few bytes that are not UTF-8 (v5.0.2, BRUTE P3-3).</summary>
        public const string DamagedTextLine = "设置文件里有几个字读不出来（显示成 \uFFFD），其余的字都按原样读出了。配置名字里有 \uFFFD 的话，改一下名字就好。";

        /// <summary>
        /// v5.0.2 (BRUTE P3-3): damaged = a UTF-8 file (BOM, or BOM-less but MostlyUtf8) with bytes that are not UTF-8; those
        /// read as U+FFFD and everything else keeps its characters (before, one bad byte made the whole file decode as GBK).
        /// </summary>
        public static string DecodeSettingsBytes(byte[] b, out bool oldEncoding, out bool damaged)
        {
            oldEncoding = false;
            damaged = false;
            if (b == null || b.Length == 0) return "";
            string s;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            {
                s = null;
                try { s = new UTF8Encoding(false, true).GetString(b, 3, b.Length - 3); }
                catch (ArgumentException) { }
                if (s == null) { damaged = !OnlyCutTail(b, 3); s = new UTF8Encoding(false, false).GetString(b, 3, b.Length - 3); }
            }
            else if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) s = new UTF32Encoding(false, false).GetString(b, 4, (b.Length - 4) & ~3);
            else if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF) s = new UTF32Encoding(true, false).GetString(b, 4, (b.Length - 4) & ~3);
            else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) s = Encoding.Unicode.GetString(b, 2, (b.Length - 2) & ~1);
            else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) s = Encoding.BigEndianUnicode.GetString(b, 2, (b.Length - 2) & ~1);
            else
            {
                int n = Math.Min(b.Length, 512) & ~1, zEven = 0, zOdd = 0;
                for (int i = 0; i < n; i++) if (b[i] == 0) { if ((i & 1) == 0) zEven++; else zOdd++; }
                int half = n / 2;
                if (n >= 4 && zOdd * 3 > half && zEven * 20 < half) { oldEncoding = true; s = Encoding.Unicode.GetString(b, 0, b.Length & ~1); }
                else if (n >= 4 && zEven * 3 > half && zOdd * 20 < half) { oldEncoding = true; s = Encoding.BigEndianUnicode.GetString(b, 0, b.Length & ~1); }
                else
                {
                    s = null;
                    try { s = new UTF8Encoding(false, true).GetString(b); }
                    catch (ArgumentException) { } // DecoderFallbackException: not UTF-8
                    if (s == null && MostlyUtf8(b, 0))
                    {
                        damaged = !OnlyCutTail(b, 0); // a file cut inside its last character is not "damaged text"
                        s = new UTF8Encoding(false, false).GetString(b);
                    }
                    if (s == null)
                    {
                        oldEncoding = true;
                        foreach (Encoding e in LegacyEncodings())
                        {
                            try { s = e.GetString(b); break; }
                            catch (ArgumentException) { }
                        }
                        if (s == null) s = new UTF8Encoding(false, false).GetString(b);
                    }
                }
            }
            return s.TrimEnd('\0');
        }

        /// <summary>Scans b from 'start' as UTF-8: valid3 = valid sequences of 3 or 4 bytes (a Chinese character is 3),
        /// invalid = runs of bytes that are not UTF-8; a sequence cut short by the end of the data counts as neither (cutTail).</summary>
        private static void ScanUtf8(byte[] b, int start, out int valid3, out int invalid, out bool cutTail)
        {
            valid3 = 0; invalid = 0; cutTail = false;
            int n = b.Length, i = start;
            bool prevBad = false;
            while (i < n)
            {
                int c = b[i];
                if (c < 0x80) { i++; prevBad = false; continue; }
                int len = c >= 0xC2 && c <= 0xDF ? 2 : c >= 0xE0 && c <= 0xEF ? 3 : c >= 0xF0 && c <= 0xF4 ? 4 : 0;
                bool ok = len > 0 && i + len <= n;
                for (int k = 1; ok && k < len; k++) ok = b[i + k] >= 0x80 && b[i + k] <= 0xBF;
                if (ok && len >= 3)
                {
                    int d = b[i + 1];
                    if ((c == 0xE0 && d < 0xA0) || (c == 0xED && d > 0x9F) || (c == 0xF0 && d < 0x90) || (c == 0xF4 && d > 0x8F)) ok = false;
                }
                if (ok) { if (len >= 3) valid3++; i += len; prevBad = false; continue; }
                if (len > 0 && i + len > n)
                {
                    bool prefix = true;
                    for (int k = i + 1; k < n; k++) if (b[k] < 0x80 || b[k] > 0xBF) prefix = false;
                    if (prefix) { cutTail = true; return; }
                }
                if (!prevBad) invalid++;
                prevBad = true;
                i++;
            }
        }

        /// <summary>v5.0.2 (BRUTE P3-3): BOM-less bytes that fail strict UTF-8 are still read as UTF-8 when the only fault is a
        /// character cut by the end of the file, or when there are at least 2 valid 3-byte characters and at least twice as many
        /// of them as damaged spots. A GBK / ANSI file almost never passes (measured: 1 of 25,800 GB18030 test files, all
        /// with a single character); one bad byte in a real UTF-8 settings file (6+ Chinese characters) always does.</summary>
        internal static bool MostlyUtf8(byte[] b, int start)
        {
            int v3, inv; bool cut;
            ScanUtf8(b, start, out v3, out inv, out cut);
            return inv == 0 || (v3 >= 2 && v3 >= 2 * inv);
        }

        /// <summary>The only UTF-8 fault is a character cut short by the end of the data.</summary>
        private static bool OnlyCutTail(byte[] b, int start)
        {
            int v3, inv; bool cut;
            ScanUtf8(b, start, out v3, out inv, out cut);
            return inv == 0;
        }

        private static List<Encoding> legacyEncodings;
        private static readonly object legacyGate = new object();

        /// <summary>The system "ANSI" code page when it is Chinese / Japanese / Korean (.NET Framework on Windows), then GB18030;
        /// strict (a byte sequence that does not fit throws). .NET Core needs CodePagesEncodingProvider, found by name so this
        /// file keeps compiling for net48 (C# 5) without it.</summary>
        private static List<Encoding> LegacyEncodings()
        {
            lock (legacyGate)
            {
                if (legacyEncodings != null) return legacyEncodings;
                List<Encoding> l = new List<Encoding>();
                try
                {
                    Type t = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages", false);
                    if (t != null)
                    {
                        System.Reflection.PropertyInfo pi = t.GetProperty("Instance");
                        EncodingProvider p = pi == null ? null : pi.GetValue(null, null) as EncodingProvider;
                        if (p != null) Encoding.RegisterProvider(p);
                    }
                }
                catch (Exception) { }
                List<int> pages = new List<int>();
                try
                {
                    int acp = Encoding.Default.CodePage;
                    if (acp == 936 || acp == 950 || acp == 932 || acp == 949 || acp == 54936) pages.Add(acp);
                }
                catch (Exception) { }
                if (!pages.Contains(54936)) pages.Add(54936);
                foreach (int cp in pages)
                {
                    try { l.Add(Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)); }
                    catch (Exception) { }
                }
                legacyEncodings = l;
                return l;
            }
        }

        /// <summary>User message after a tolerant load (public for tests). copy = backup path, or null if it could not be written.</summary>
        public static string DroppedMessage(List<string> dropped, string copy)
        {
            StringBuilder sb = new StringBuilder();
            int show = Math.Min(3, dropped.Count);
            for (int i = 0; i < show; i++) { if (i > 0) sb.Append("、"); sb.Append(dropped[i]); }
            if (dropped.Count > show) sb.Append(" 等");
            string m = "设置文件里有 " + dropped.Count + " 项格式不对（" + sb + "），这几项已恢复默认值，其余设置都保留。";
            if (copy != null) m += "\n原文件已备份为 " + Path.GetFileName(copy) + "。";
            return m;
        }

        /// <summary>
        /// v4.0 tolerant settings reader: a tiny JSON parser + a walk against the [DataContract] classes that removes
        /// members whose JSON type cannot fill the C# member. Used only after DataContractJsonSerializer rejected a file.
        /// </summary>
        private static class TolerantJson
        {
            private const int KNull = 0, KBool = 1, KNum = 2, KStr = 3, KArr = 4, KObj = 5;
            private const int MaxDepth = 64;

            private sealed class Node
            {
                public int Kind;
                public bool B;
                public string S; // string value, or the number's text
                public List<Node> Items;
                public List<KeyValuePair<string, Node>> Members;
            }

            private static readonly Node NullNode = new Node();
            private static readonly Dictionary<Type, Dictionary<string, Type>> memberCache = new Dictionary<Type, Dictionary<string, Type>>();

            public static bool Clean(string json, Type root, List<string> dropped, out string cleaned)
            {
                cleaned = null;
                int pos = 0;
                Node n = ParseValue(json, ref pos, 0);
                if (n == null || n.Kind != KObj) return false;
                SkipWs(json, ref pos);
                if (pos != json.Length) return false;
                Node c = CleanObject(n, root, "", dropped);
                StringBuilder sb = new StringBuilder(json.Length);
                Write(c, sb);
                cleaned = sb.ToString();
                return true;
            }

            /// <summary>v4.1 (BRUTE40 P2-10): true when the first '{' of the text has its matching closing bracket (strings and
            /// escapes respected). DataContractJsonSerializer accepts some cut-off files (e.g. ending right after a comma);
            /// those are not complete and must not load silently.</summary>
            public static bool IsComplete(string json)
            {
                if (json == null) return false;
                int i = json.IndexOf('{');
                if (i < 0) return false;
                int depth = 0;
                bool inStr = false;
                for (; i < json.Length; i++)
                {
                    char c = json[i];
                    if (inStr)
                    {
                        if (c == '\\') i++;
                        else if (c == '"') inStr = false;
                        continue;
                    }
                    if (c == '"') inStr = true;
                    else if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { depth--; if (depth == 0) return true; }
                }
                return false;
            }

            /// <summary>The member names of the top-level object, or null when the text is not one complete JSON object.</summary>
            public static List<string> TopLevelKeys(string json)
            {
                try
                {
                    int pos = 0;
                    Node n = ParseValue(json, ref pos, 0);
                    if (n == null || n.Kind != KObj) return null;
                    List<string> keys = new List<string>();
                    foreach (KeyValuePair<string, Node> kv in n.Members) keys.Add(kv.Key);
                    return keys;
                }
                catch (Exception) { return null; }
            }

            /// <summary>t has a [DataMember] named key.</summary>
            public static bool IsMember(Type t, string key) { return MembersOf(t).ContainsKey(key); }

            /// <summary>
            /// v4.1 (BRUTE40 P2-10): reads what is still complete in a cut-off or damaged file. Parsing stops at the end of the
            /// text or at the first syntax error; what came before is kept: complete members of an unfinished object (the
            /// settings themselves, Keys), complete items of an unfinished list (the profiles before the cut). An unfinished
            /// list item (the profile the cut is in) and a number at the very end (it may be cut short) are dropped. Comments,
            /// a missing comma between members and text after the end are tolerated. The result goes through the same schema
            /// walk as Clean. profiles = complete objects in the top-level "Profiles" list. Never throws.
            /// </summary>
            public static bool Salvage(string json, Type root, List<string> dropped, out string cleaned, out List<string> topKeys, out int profiles)
            {
                cleaned = null; topKeys = new List<string>(); profiles = 0;
                try
                {
                    if (json == null) return false;
                    Node n = LRoot(json);
                    if (n == null || n.Members.Count == 0) return false;
                    foreach (KeyValuePair<string, Node> kv in n.Members)
                    {
                        topKeys.Add(kv.Key);
                        if (kv.Key == "Profiles" && kv.Value.Kind == KArr)
                            foreach (Node it in kv.Value.Items) if (it.Kind == KObj) profiles++;
                    }
                    Node c = CleanObject(n, root, "", dropped);
                    StringBuilder sb = new StringBuilder(json.Length);
                    Write(c, sb);
                    cleaned = sb.ToString();
                    return true;
                }
                catch (Exception) { return false; }
            }

            /// <summary>The top-level object read leniently (see Salvage), or null: whitespace / comments before it are skipped,
            /// else the first '{'.</summary>
            private static Node LRoot(string json) { return LRoot(json, true); }

            /// <summary>anyStart false: the text must start (after whitespace / comments) with '{' (LooksLikeCrosshairSettings: a
            /// top-level array or other text is not a settings file).</summary>
            private static Node LRoot(string json, bool anyStart)
            {
                if (json == null) return null;
                int pos = 0;
                LSkip(json, ref pos);
                if (pos >= json.Length || json[pos] != '{')
                {
                    if (!anyStart) return null;
                    pos = json.IndexOf('{');
                    if (pos < 0) return null;
                }
                bool complete;
                return LObject(json, ref pos, 0, out complete);
            }

            /// <summary>v4.1 (BRUTE40 P1-2): the top-level "Profiles" list holds an object with a Profile member other than Name.</summary>
            public static bool HasProfileObject(string json)
            {
                Node n = LRoot(json, false);
                if (n == null) return false;
                Dictionary<string, Type> pm = MembersOf(typeof(Profile));
                foreach (KeyValuePair<string, Node> kv in n.Members)
                {
                    if (kv.Key != "Profiles" || kv.Value.Kind != KArr) continue;
                    foreach (Node it in kv.Value.Items)
                    {
                        if (it.Kind != KObj) continue;
                        foreach (KeyValuePair<string, Node> m in it.Members) if (m.Key != "Name" && pm.ContainsKey(m.Key)) return true;
                    }
                }
                return false;
            }

            // ---------------------------------------------------------------- lenient parser (salvage only)

            /// <summary>Whitespace, NUL, BOM and // or /* */ comments.</summary>
            private static void LSkip(string s, ref int i)
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF' || c == '\0') { i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        i = e < 0 ? s.Length : e + 2;
                        continue;
                    }
                    break;
                }
            }

            /// <summary>A number is only trusted when a delimiter follows it (at the end of the text it may be cut short).</summary>
            private static bool NumberEnds(string s, int i)
            {
                LSkip(s, ref i);
                return i < s.Length && (s[i] == ',' || s[i] == '}' || s[i] == ']' || s[i] == '"');
            }

            private static Node LValue(string s, ref int i, int depth, out bool complete)
            {
                complete = false;
                if (depth > MaxDepth) return null;
                LSkip(s, ref i);
                if (i >= s.Length) return null;
                char c = s[i];
                if (c == '{') return LObject(s, ref i, depth, out complete);
                if (c == '[') return LArray(s, ref i, depth, out complete);
                if (c == '"')
                {
                    string str = LString(s, ref i); // v5.0.2: a stray quote ends at the line break instead of eating the next lines
                    if (str == null) return null;
                    Node n = new Node(); n.Kind = KStr; n.S = str; complete = true; return n;
                }
                if (Lit(s, ref i, "true")) { Node n = new Node(); n.Kind = KBool; n.B = true; complete = true; return n; }
                if (Lit(s, ref i, "false")) { Node n = new Node(); n.Kind = KBool; complete = true; return n; }
                if (Lit(s, ref i, "null")) { complete = true; return new Node(); }
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    Node num = ParseNumber(s, ref i);
                    if (num == null) return null;
                    complete = NumberEnds(s, i);
                    return num;
                }
                return null;
            }

            private static Node LObject(string s, ref int i, int depth, out bool complete)
            {
                complete = false;
                i++; // '{'
                Node n = new Node(); n.Kind = KObj; n.Members = new List<KeyValuePair<string, Node>>();
                int memberCol = -1;
                while (true)
                {
                    LSkip(s, ref i);
                    if (i >= s.Length) return n;
                    char c = s[i];
                    // v5.0.2: in a pretty-printed file a closing bracket stands at the start of its line (or right after its
                    // opener); one in the middle of a line is a stray typo and is skipped
                    if (c == '}' && memberCol >= 0 && StrayCloser(s, i)) { i++; continue; }
                    if (c == '}') { i++; complete = true; return n; }
                    if (c == ',') { i++; continue; }      // stray / trailing commas
                    // v5.0.2 (BRUTE P2-2): a deleted '}' in a pretty-printed file: a line that starts left of this object's
                    // members belongs to the enclosing object / list, so this object ends here (the line is left for it)
                    int col = LineCol(s, i);
                    if (depth > 0 && col >= 0 && memberCol >= 0 && col < memberCol && (c == ']' || LooksLikeKey(s, i))) { complete = true; return n; }
                    if (memberCol < 0 && col >= 0 && c == '"') memberCol = col;
                    // v5.0.2 (BRUTE P2-2): a syntax error skips only the damaged member (LResync) and reading goes on with the
                    // next one; before, everything after the first typo was lost (all later profiles).
                    int start = i;
                    string key = c == '"' ? LKey(s, ref i) : null;
                    if (key != null) LSkip(s, ref i);
                    if (key != null && i >= s.Length) return n;
                    if (key == null || s[i] != ':')
                    {
                        // a stray quote / letter in the name itself ("Profiles"": [): the name is what stands before the ':'
                        int colon;
                        key = LRepairKey(s, start, out colon);
                        if (key == null) { LResync(s, ref i, start, '}'); continue; }
                        i = colon;
                    }
                    i++;
                    LSkip(s, ref i);
                    while (i < s.Length && s[i] == ',') { i++; LSkip(s, ref i); } // v5.0.2: "key":, value (a stray comma)
                    bool vc;
                    Node v = LValue(s, ref i, depth + 1, out vc);
                    if (v == null)
                    {
                        if (i >= s.Length) return n;
                        LResync(s, ref i, start, '}');
                        continue;
                    }
                    // an unfinished object / list value keeps its complete parts (the settings, Keys, the profiles before the cut)
                    if (vc || v.Kind == KObj || v.Kind == KArr) n.Members.Add(new KeyValuePair<string, Node>(key, v));
                    if (!vc)
                    {
                        if (i >= s.Length) return n;        // cut off: the end of the text
                        LResync(s, ref i, start, '}');      // e.g. a number with a typo right after it
                    }
                }
            }

            /// <summary>v5.0.2: a member name in the lenient reader; a raw line break ends it as damaged (the app writes one member
            /// per line and never a raw line break inside a string), so a stray quote cannot swallow the following lines.</summary>
            private static string LKey(string s, ref int i)
            {
                int j = i;
                string r = LString(s, ref j);
                if (r == null) return null;
                i = j;
                return r;
            }

            /// <summary>v5.0.2: a damaged member name: the text from 'from' to the first ':' of the line, quotes and spaces
            /// removed, when that is a plain name (letters / digits / _) and nothing structural stands before the ':'.</summary>
            private static string LRepairKey(string s, int from, out int colon)
            {
                colon = -1;
                StringBuilder sb = new StringBuilder();
                for (int k = from; k < s.Length && s[k] != '\n'; k++)
                {
                    char c = s[k];
                    if (c == ':') { colon = k; break; }
                    if (c == '{' || c == '}' || c == '[' || c == ']' || c == ',') return null;
                    if (c == '"' || c == ' ' || c == '\t' || c == '\r') continue;
                    if (!(char.IsLetterOrDigit(c) || c == '_')) return null;
                    sb.Append(c);
                }
                if (colon < 0 || sb.Length == 0) return null;
                return sb.ToString();
            }

            /// <summary>v5.0.2: the column of i when only spaces / tabs stand between the line start and i, else -1 (the app
            /// writes one member per line, indented by depth).</summary>
            private static int LineCol(string s, int i)
            {
                int k = i - 1;
                while (k >= 0 && (s[k] == ' ' || s[k] == '\t')) k--;
                if (k >= 0 && s[k] != '\n' && s[k] != '\r') return -1;
                return i - k - 1;
            }

            /// <summary>v5.0.2: the closing bracket at i stands on a member line ("name": value) after the ':', and not right
            /// after an opener ({} / []): a typo inside the value. A closer on its own line (even after a stray letter) is real.</summary>
            private static bool StrayCloser(string s, int i)
            {
                int k = i - 1;
                while (k >= 0 && (s[k] == ' ' || s[k] == '\t')) k--;
                if (k < 0 || s[k] == '{' || s[k] == '[' || s[k] == '\n') return false;
                for (int j = i - 1; j >= 0 && s[j] != '\n'; j--) if (s[j] == ':') return true;
                return false;
            }

            /// <summary>v5.0.2: s[i] starts a clean member name: "name" then ':' on the same line.</summary>
            private static bool LooksLikeKey(string s, int i)
            {
                if (i >= s.Length || s[i] != '"') return false;
                int k = i + 1;
                while (k < s.Length && s[k] != '"' && s[k] != '\n')
                {
                    char c = s[k];
                    if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
                    k++;
                }
                if (k >= s.Length || s[k] != '"' || k == i + 1) return false;
                k++;
                while (k < s.Length && (s[k] == ' ' || s[k] == '\t')) k++;
                return k < s.Length && s[k] == ':';
            }

            /// <summary>The quote at q has a closing quote (escapes respected) before the end of its line.</summary>
            private static bool ClosesOnLine(string s, int q)
            {
                for (int k = q + 1; k < s.Length && s[k] != '\n'; k++)
                {
                    if (s[k] == '\\') { if (k + 1 < s.Length && s[k + 1] == '\n') return false; k++; continue; }
                    if (s[k] == '"') return true;
                }
                return false;
            }

            /// <summary>A string in the lenient reader: like ParseString, but a raw line break means the string is damaged (null).</summary>
            private static string LString(string s, ref int i)
            {
                int q = i + 1;
                while (q < s.Length && s[q] != '"' && s[q] != '\n')
                {
                    if (s[q] == '\\') q++;
                    q++;
                }
                if (q >= s.Length || s[q] != '"') return null;
                return ParseString(s, ref i);
            }

            /// <summary>
            /// v5.0.2 (BRUTE P2-2): skips a damaged member / list item that starts at 'from': to just after the next ',' or line
            /// break outside brackets, or to the closing 'close' of the enclosing object / list (left for the caller to read).
            /// Strings are tracked but end at a line break (a stray quote), and a closing bracket of the wrong kind is skipped.
            /// Always moves forward.
            /// </summary>
            private static void LResync(string s, ref int i, int from, char close)
            {
                int d = 0;
                bool inStr = false;
                for (int k = from; k < s.Length; k++)
                {
                    char c = s[k];
                    if (c == '\n')
                    {
                        inStr = false;
                        if (d == 0 && k > from) { i = k + 1; return; }
                        continue;
                    }
                    if (inStr)
                    {
                        if (c == '\\' && k + 1 < s.Length && s[k + 1] != '\n') k++;
                        else if (c == '"') inStr = false;
                        continue;
                    }
                    if (c == '"') inStr = ClosesOnLine(s, k); // a quote with no partner on its line is a stray one: skipped
                    else if (c == '{' || c == '[') d++;
                    else if (c == '}' || c == ']')
                    {
                        if (d > 0) d--;
                        else if (c == close && k > from) { i = k; return; }
                        // a stray closing bracket at this level: skip it
                    }
                    else if (c == ',' && d == 0 && k > from) { i = k + 1; return; }
                }
                i = s.Length;
            }

            private static Node LArray(string s, ref int i, int depth, out bool complete)
            {
                complete = false;
                i++; // '['
                Node n = new Node(); n.Kind = KArr; n.Items = new List<Node>();
                int itemCol = -1;
                while (true)
                {
                    LSkip(s, ref i);
                    if (i >= s.Length) return n;
                    char c = s[i];
                    if (c == ']' && itemCol >= 0 && StrayCloser(s, i)) { i++; continue; } // v5.0.2: see LObject
                    if (c == ']') { i++; complete = true; return n; }
                    if (c == ',') { i++; continue; }
                    // v5.0.2 (BRUTE P2-2): a deleted ']' (see LObject)
                    int col = LineCol(s, i);
                    if (col >= 0 && itemCol >= 0 && col < itemCol && (c == '}' || LooksLikeKey(s, i))) { complete = true; return n; }
                    if (itemCol < 0 && col >= 0 && c != '}') itemCol = col;
                    bool vc;
                    int start = i;
                    Node v = LValue(s, ref i, depth + 1, out vc);
                    if (v != null && vc) { n.Items.Add(v); continue; }
                    if (i >= s.Length) return n;           // the unfinished item (e.g. the profile the cut is in) is dropped
                    LResync(s, ref i, start, ']');         // v5.0.2 (BRUTE P2-2): skip only the damaged item, read the rest
                }
            }

            // ---------------------------------------------------------------- schema walk

            private static Dictionary<string, Type> MembersOf(Type t)
            {
                lock (memberCache)
                {
                    Dictionary<string, Type> m;
                    if (memberCache.TryGetValue(t, out m)) return m;
                    m = new Dictionary<string, Type>(StringComparer.Ordinal);
                    System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    foreach (System.Reflection.FieldInfo fi in t.GetFields(f))
                    {
                        DataMemberAttribute a = Attribute.GetCustomAttribute(fi, typeof(DataMemberAttribute)) as DataMemberAttribute;
                        if (a != null) m[a.Name ?? fi.Name] = fi.FieldType;
                    }
                    foreach (System.Reflection.PropertyInfo pi in t.GetProperties(f))
                    {
                        DataMemberAttribute a = Attribute.GetCustomAttribute(pi, typeof(DataMemberAttribute)) as DataMemberAttribute;
                        if (a != null) m[a.Name ?? pi.Name] = pi.PropertyType;
                    }
                    memberCache[t] = m;
                    return m;
                }
            }

            private static Node CleanObject(Node n, Type t, string path, List<string> dropped)
            {
                Dictionary<string, Type> schema = MembersOf(t);
                // duplicate keys: the last one wins (as in most JSON readers)
                List<string> order = new List<string>();
                Dictionary<string, Node> last = new Dictionary<string, Node>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, Node> kv in n.Members)
                {
                    if (!last.ContainsKey(kv.Key)) order.Add(kv.Key);
                    last[kv.Key] = kv.Value;
                }
                Node o = new Node();
                o.Kind = KObj;
                o.Members = new List<KeyValuePair<string, Node>>();
                foreach (string key in order)
                {
                    string p = path.Length == 0 ? key : path + "." + key;
                    if (key == "__type") { dropped.Add(p); continue; }
                    Type mt;
                    if (!schema.TryGetValue(key, out mt)) { o.Members.Add(new KeyValuePair<string, Node>(key, last[key])); continue; } // unknown: ignored by the serializer
                    Node v = CleanValue(last[key], mt, p, dropped);
                    if (v == null) { dropped.Add(p); continue; }
                    o.Members.Add(new KeyValuePair<string, Node>(key, v));
                }
                return o;
            }

            /// <summary>The cleaned value, or null = drop this member (it gets its default).</summary>
            private static Node CleanValue(Node n, Type t, string path, List<string> dropped)
            {
                Type under = Nullable.GetUnderlyingType(t);
                bool nullable = under != null || !t.IsValueType;
                if (under != null) t = under;
                if (n.Kind == KNull) return nullable ? n : null;
                if (t == typeof(bool)) return n.Kind == KBool ? n : null;
                if (t == typeof(string)) return n.Kind == KStr ? n : null;
                if (t == typeof(int) || t == typeof(long))
                {
                    if (n.Kind != KNum) return null;
                    decimal d;
                    if (!decimal.TryParse(n.S, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return null;
                    if (d != decimal.Truncate(d)) return null;
                    if (t == typeof(int) && (d < int.MinValue || d > int.MaxValue)) return null;
                    if (t == typeof(long) && (d < long.MinValue || d > long.MaxValue)) return null;
                    return Num(((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                if (t == typeof(double))
                {
                    if (n.Kind != KNum) return null;
                    double d;
                    if (!double.TryParse(n.S, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return null;
                    if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                    return Num(d.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
                Type elem = null;
                if (t.IsArray) elem = t.GetElementType();
                else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) elem = t.GetGenericArguments()[0];
                if (elem != null)
                {
                    if (n.Kind != KArr) return null;
                    bool elemNullable = !elem.IsValueType || Nullable.GetUnderlyingType(elem) != null;
                    Node a = new Node();
                    a.Kind = KArr;
                    a.Items = new List<Node>();
                    for (int i = 0; i < n.Items.Count; i++)
                    {
                        string p = path + "[" + i + "]";
                        Node v = CleanValue(n.Items[i], elem, p, dropped);
                        if (v != null) { a.Items.Add(v); continue; }
                        if (!elemNullable) return null; // e.g. a string inside Speeds: the whole list goes back to its default
                        dropped.Add(p);
                        a.Items.Add(NullNode); // keep the positions (Dirs are indexed by direction); Validate replaces nulls
                    }
                    return a;
                }
                if (Attribute.IsDefined(t, typeof(DataContractAttribute)))
                {
                    if (n.Kind != KObj) return null;
                    return CleanObject(n, t, path, dropped);
                }
                return n; // a type this walk does not know: leave it to the serializer
            }

            private static Node Num(string text) { Node n = new Node(); n.Kind = KNum; n.S = text; return n; }

            // ---------------------------------------------------------------- writer

            private static void Write(Node n, StringBuilder sb)
            {
                switch (n.Kind)
                {
                    case KNull: sb.Append("null"); break;
                    case KBool: sb.Append(n.B ? "true" : "false"); break;
                    case KNum: sb.Append(n.S); break;
                    case KStr: WriteString(n.S, sb); break;
                    case KArr:
                        sb.Append('[');
                        for (int i = 0; i < n.Items.Count; i++) { if (i > 0) sb.Append(','); Write(n.Items[i], sb); }
                        sb.Append(']');
                        break;
                    default:
                        sb.Append('{');
                        for (int i = 0; i < n.Members.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            WriteString(n.Members[i].Key, sb);
                            sb.Append(':');
                            Write(n.Members[i].Value, sb);
                        }
                        sb.Append('}');
                        break;
                }
            }

            private static void WriteString(string s, StringBuilder sb)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    if (c == '"') sb.Append("\\\"");
                    else if (c == '\\') sb.Append("\\\\");
                    else if (c < 0x20 || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                }
                sb.Append('"');
            }

            // ---------------------------------------------------------------- parser (null = syntax error)

            private static void SkipWs(string s, ref int i)
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '\uFEFF')) i++;
            }

            private static Node ParseValue(string s, ref int i, int depth)
            {
                if (depth > MaxDepth) return null;
                SkipWs(s, ref i);
                if (i >= s.Length) return null;
                char c = s[i];
                if (c == '{') return ParseObject(s, ref i, depth);
                if (c == '[') return ParseArray(s, ref i, depth);
                if (c == '"')
                {
                    string str = ParseString(s, ref i);
                    if (str == null) return null;
                    Node n = new Node(); n.Kind = KStr; n.S = str; return n;
                }
                if (Lit(s, ref i, "true")) { Node n = new Node(); n.Kind = KBool; n.B = true; return n; }
                if (Lit(s, ref i, "false")) { Node n = new Node(); n.Kind = KBool; return n; }
                if (Lit(s, ref i, "null")) return new Node();
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
                return null;
            }

            private static bool Lit(string s, ref int i, string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            private static Node ParseNumber(string s, ref int i)
            {
                int start = i;
                if (s[i] == '-') i++;
                int digits = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                if (digits == 0) return null;
                if (i < s.Length && s[i] == '.')
                {
                    i++; digits = 0;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                    if (digits == 0) return null;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    digits = 0;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                    if (digits == 0) return null;
                }
                return Num(s.Substring(start, i - start));
            }

            private static string ParseString(string s, ref int i)
            {
                i++; // opening quote
                StringBuilder sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) return null;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) return null;
                            int v = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int h = Hex(s[i + k]);
                                if (h < 0) return null;
                                v = v * 16 + h;
                            }
                            i += 4;
                            sb.Append((char)v);
                            break;
                        default: return null;
                    }
                }
                return null;
            }

            private static int Hex(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private static Node ParseArray(string s, ref int i, int depth)
            {
                i++;
                Node n = new Node(); n.Kind = KArr; n.Items = new List<Node>();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return n; }
                while (true)
                {
                    Node v = ParseValue(s, ref i, depth + 1);
                    if (v == null) return null;
                    n.Items.Add(v);
                    SkipWs(s, ref i);
                    if (i >= s.Length) return null;
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return n; }
                    return null;
                }
            }

            private static Node ParseObject(string s, ref int i, int depth)
            {
                i++;
                Node n = new Node(); n.Kind = KObj; n.Members = new List<KeyValuePair<string, Node>>();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return n; }
                while (true)
                {
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != '"') return null;
                    string key = ParseString(s, ref i);
                    if (key == null) return null;
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != ':') return null;
                    i++;
                    Node v = ParseValue(s, ref i, depth + 1);
                    if (v == null) return null;
                    n.Members.Add(new KeyValuePair<string, Node>(key, v));
                    SkipWs(s, ref i);
                    if (i >= s.Length) return null;
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return n; }
                    return null;
                }
            }
        }

        /// <summary>Writes settings.json.tmp, then replaces settings.json. Returns null on success, else the error.
        /// v4.1 (BRUTE40 P2-6): when Load could not back up the file (g.BackupBeforeSave), the copy is tried first; while it
        /// fails nothing is written, so the user's original file is never lost.</summary>
        public static string Save(GlobalSettings g, string path)
        {
            if (g != null && g.BackupBeforeSave != null)
            {
                if (File.Exists(g.BackupBeforeSave) && SamePath(g.BackupBeforeSave, path))
                {
                    string err;
                    if (BackupCopy(g.BackupBeforeSave, "bad", out err) == null) return "原来的设置文件还没备份成功，所以先不保存（" + err + "）";
                }
                if (SamePath(g.BackupBeforeSave, path) || !File.Exists(g.BackupBeforeSave)) g.BackupBeforeSave = null;
            }
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
