// PURE file: only System / System.Collections.Generic. Compiles under net48 (C# 5) and net8.0.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair
{
    public enum PreviewMode
    {
        Live = 0,
        Static = 1,
        DirW = 2, DirS = 3, DirA = 4, DirD = 5, DirWA = 6, DirWD = 7, DirSA = 8, DirSD = 9,
        RecoilMax = 10,
        LeanQ = 11,
        LeanE = 12,
        TurnAD = 13,     // A→D 换向: A 1.2 s, D 1.2 s, repeat
        TurnWS = 14,     // W→S 换向: W 1.2 s, S 1.2 s, repeat
        HandFlip = 15,   // 换手来回切（站着）: the hand flips every 1.2 s, no movement keys
        JumpStand = 16,  // 跳跃（站着）: one jump every 总时长 + 500 ms, no other keys
        BoostStand = 17  // 强化准星（站着）: the boosted look, no keys (v3.0)
    }

    /// <summary>
    /// v3.0 只在塔科夫前台时显示 (pure). GameFocused = the foreground window's title contains GlobalSettings.GameWindowTitle
    /// (GameWindow.IsForeground). Our own window in front keeps the ring visible so tuning and previews still work,
    /// but live keys are only read while the game is in front (no 换手 / 强化 / 跳跃 toggles from typing elsewhere).
    /// </summary>
    public static class FocusGate
    {
        /// <summary>Live keys may be read: the option is off, or the game is in front.</summary>
        public static bool KeysLive(bool onlyWhenGameFocused, bool gameFocused)
        {
            return !onlyWhenGameFocused || gameFocused;
        }

        /// <summary>Visible = master on && not hidden by 开镜 / 自由视角 && (option off || game in front || our window in front).</summary>
        public static bool Visible(bool master, bool hiddenByAdsOrFreeLook, bool onlyWhenGameFocused, bool gameFocused, bool ownForeground)
        {
            return master && !hiddenByAdsOrFreeLook && (!onlyWhenGameFocused || gameFocused || ownForeground);
        }
    }

    public struct InputState
    {
        public bool Fwd, Back, Left, Right, LeanL, LeanR, Fire, Ads;
        /// <summary>自由视角 (hide while held).</summary>
        public bool FreeLook;
        /// <summary>换手 key (v2.4).</summary>
        public bool HandSwitch;
        /// <summary>跳跃 key (v2.4.3, press edge only).</summary>
        public bool Jump;
        /// <summary>强化准星 key (v3.0, press edge toggles).</summary>
        public bool Boost;
        /// <summary>v3.2: Tab or Esc down (取消开镜 in 点一下切换 mode).</summary>
        public bool Cancel;
        /// <summary>v4.1 (BRUTE40 P2-3): Tab and Esc on their own, so pressing one while the other is held still cancels
        /// 开镜 (ReadInput sets both and Cancel = Tab || Esc; a caller that only sets Cancel keeps working).</summary>
        public bool Tab, Esc;
        /// <summary>v3.2: Alt or Ctrl down (Alt/Ctrl+右键 = 切倍率/切瞄具, not 开镜).</summary>
        public bool Mod;
        /// <summary>v3.3: either Shift down (冲刺 with W).</summary>
        public bool Shift;
        /// <summary>v5.1 (W21): 丢雷键 (GlobalSettings.GrenadeKey, press edge toggles 丢雷隐藏).</summary>
        public bool Grenade;
        /// <summary>v5.1 (W21): 换弹键 (GlobalSettings.AmmoReloadKey).</summary>
        public bool Reload;
        /// <summary>Leo round 2 (R3): weapon-switch handling is removed. Never set by ReadInput and ignored by the engine; kept only
        /// so older callers still compile.</summary>
        public bool WeaponMain, WeaponOther;
    }

    /// <summary>Immutable snapshot of engine state for the status line.</summary>
    public sealed class MotionDebug
    {
        public readonly int Dir;           // -1 = stopped
        public readonly double Lean;       // -1..1
        public readonly double Recoil;     // px (as shown)
        public readonly double UiOffsetX;  // UI coords (X right +, Y up +), includes StandOffset
        public readonly double UiOffsetY;
        public readonly bool Switched;     // the hand being shown: false 正常手, true 换手
        public readonly bool Jumping;      // a 跳跃 is running
        public readonly bool Boosted;      // 强化准星 shown (v3.0)

        public MotionDebug(int dir, double lean, double recoil, double x, double y)
            : this(dir, lean, recoil, x, y, false, false, false) { }

        public MotionDebug(int dir, double lean, double recoil, double x, double y, bool switched)
            : this(dir, lean, recoil, x, y, switched, false) { }

        public MotionDebug(int dir, double lean, double recoil, double x, double y, bool switched, bool jumping)
            : this(dir, lean, recoil, x, y, switched, jumping, false) { }

        public MotionDebug(int dir, double lean, double recoil, double x, double y, bool switched, bool jumping, bool boosted)
        {
            Dir = dir; Lean = lean; Recoil = recoil; UiOffsetX = x; UiOffsetY = y; Switched = switched; Jumping = jumping; Boosted = boosted;
        }

        public string DirName
        {
            get { return Dir >= 0 && Dir < Profile.DirNames.Length ? Profile.DirNames[Dir] : "静止"; }
        }

        public string HandName { get { return Switched ? "换手" : "正常手"; } }
    }

    public sealed class MotionEngine
    {
        // ---- outputs ----
        public double OffsetX;   // screen px, y-down, includes the profile's StandOffset
        public double OffsetY;
        public double Alpha;     // 0..1 (visibility fade: 开镜 / 自由视角 / toggled off)

        /// <summary>Snapshot of this frame for the status line. v4.0 PERF: built on first read after an Update / Reset (not
        /// every frame), then the same object until the next Update; old snapshots stay unchanged (immutable).</summary>
        public MotionDebug Debug
        {
            get
            {
                if (dbg == null) dbg = new MotionDebug(dbgDir, dbgLean, dbgRecoil, dbgX, dbgY, dbgSwitched, dbgJumping, dbgBoosted);
                return dbg;
            }
        }
        private MotionDebug dbg;
        private int dbgDir;
        private double dbgLean, dbgRecoil, dbgX, dbgY;
        private bool dbgSwitched, dbgJumping, dbgBoosted;

        private void SetDebug(int dir, double lean, double recoil, double x, double y, bool switched, bool jumping, bool boosted)
        {
            dbg = null;
            dbgDir = dir; dbgLean = lean; dbgRecoil = recoil; dbgX = x; dbgY = y;
            dbgSwitched = switched; dbgJumping = jumping; dbgBoosted = boosted;
        }

        // ---- movement (UI coords: X right +, Y up +), one travel per axis: [0] = X (左右, CurveAD), [1] = Y (前后, CurveWS) ----
        private readonly double[] pos = new double[2];
        private readonly double[] from = new double[2];
        private readonly double[] travelMs = new double[2];
        private readonly bool[] moving = new bool[2];
        private int prevDir;     // key state of the last frame (direction, lean target)
        private int prevLean;
        private int curDir;

        private double lean, leanTarget;
        private bool adsToggled;
        private bool adsPressBlocked; // v3.2: this 开镜 press started with Alt/Ctrl held
        private double recoil;
        private InputState prev;
        private bool prevPreview;
        private PreviewMode prevPm;
        private double previewMs;
        private double sincePressMs;
        private bool kickPending;

        // ---- 换手 ----
        private bool liveSwitched;   // tracked from the real 换手 key
        private bool prevHandKey;
        private bool prevEnabled;
        private int handTarget;      // hand shown (0 / 1)
        private double blend, blendFrom, blendT; // blend 0 = 正常手 .. 1 = 换手 (smoothstep over HandSwitchMs)
        private int blendTo;
        private readonly HandView view0 = new HandView(), view1 = new HandView();

        // ---- 跳跃 (v2.4.3) ----
        private bool jumpOn;
        private double jumpT;    // ms since the press

        // ---- 强化准星 (v3.0) ----
        private bool liveBoost;      // toggled by press edges of the real Boost key
        private bool prevBoostKey;
        private bool boostShown;

        // ---- v5.1 (W21): 丢雷隐藏, weapon keys, 弹药提示, slow-start legs ----
        private bool grenadeHidden, prevGrenadeKey, prevAmmoOn;
        /// <summary>弹药提示 count model (main weapon only). Updated from LIVE keys every frame (also while hidden).</summary>
        public readonly AmmoModel Ammo = new AmmoModel();
        // per axis: the current leg runs from[a] -> (legToZero ? 0 : live target) over legDur[a] ms, entering the curve at legU0[a]
        private readonly double[] legDur = new double[2];
        private readonly double[] legU0 = new double[2];
        private readonly bool[] legToZero = new bool[2];
        private readonly double[] refMag = new double[2]; // |full offset| of the ramp the position is on
        private readonly double[] pressMs = new double[2]; // ms since the current outward leg started (tap detection)
        private readonly bool[] outward = new bool[2];
        private readonly double[] legC = new double[2];  // curve value used for this frame's position (dashboard HUD)
        /// <summary>Default minimum total time of a reversal (to zero + out the other way), ms (V51-RESEARCH §1: MinDirectionBlendTime).
        /// Leo round 2 (R4): the live value is GlobalSettings.ReverseMinMs.</summary>
        public const double MinReverseMs = 150;
        /// <summary>R4 defaults of the other tunables (settings TapThresholdMs / ReturnTimePct / ReversePauseMs).</summary>
        public const double DefaultTapThresholdMs = 120, DefaultReturnTimePct = 100, DefaultReversePauseMs = 0;
        // this frame's tunables (read from GlobalSettings at the start of Update, clamped)
        private double tapMs = DefaultTapThresholdMs, reverseMinMs = MinReverseMs, returnScale = 1.0, reversePauseMs = DefaultReversePauseMs;
        /// <summary>R4 tunables until GlobalSettings has them (W23): 回程时间 % and 换向停顿 ms.</summary>
        public double ReturnTimePct = DefaultReturnTimePct, ReversePauseMs = DefaultReversePauseMs;
        private readonly double[] legPause = new double[2]; // rest at 0 between the two legs of a reversal, ms

        private static double Tune(double v, double lo, double hi, double def)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return def;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>R4: the tap / reversal tunables from the settings (same clamps as GlobalSettings.Validate).</summary>
        private void ReadTuning(GlobalSettings g)
        {
            tapMs = Tune(g.TapThresholdMs, GlobalSettings.TapThresholdMsMin, GlobalSettings.TapThresholdMsMax, DefaultTapThresholdMs);
            reverseMinMs = Tune(g.ReverseMinMs, GlobalSettings.ReverseMinMsMin, GlobalSettings.ReverseMinMsMax, MinReverseMs);
            returnScale = Tune(ReturnTimePct, 25, 300, DefaultReturnTimePct) / 100.0;   // W21-R2-PENDING: g.ReturnTimePct (W23)
            reversePauseMs = Tune(ReversePauseMs, 0, 300, DefaultReversePauseMs);       // W21-R2-PENDING: g.ReversePauseMs (W23)
        }

        /// <summary>v3.0 只在塔科夫前台时显示: set by the render thread (≤ 10 Hz) before Update. Defaults = game in front.</summary>
        public bool GameFocused = true;
        private bool prevKeysLive = true; // v4.0: last frame's FocusGate.KeysLive (focus regain = not a key press)
        public bool OwnForeground;

        public MotionEngine() { Reset(); }

        /// <summary>Back to the initial state (the "重播" button).</summary>
        public void Reset()
        {
            for (int a = 0; a < 2; a++) { pos[a] = 0; from[a] = 0; travelMs[a] = 0; moving[a] = false; }
            prevDir = -1; prevLean = 0; curDir = -1;
            lean = leanTarget = 0; leanCancelled = false; adsSprintCancelled = false;
            adsToggled = false; adsPressBlocked = false;
            recoil = 0;
            prev = new InputState();
            prevPreview = false;
            prevPm = PreviewMode.Live;
            previewMs = 0;
            sincePressMs = 0;
            kickPending = false;
            liveSwitched = false; prevHandKey = false; prevEnabled = true;
            handTarget = 0; blend = 0; blendFrom = 0; blendT = 0; blendTo = 0;
            jumpOn = false; jumpT = 0;
            liveBoost = false; prevBoostKey = false; boostShown = false;
            grenadeHidden = false; prevGrenadeKey = false; prevAmmoOn = false;
            Ammo.Reset();
            for (int a = 0; a < 2; a++) { legDur[a] = 0; legU0[a] = 0; legToZero[a] = false; refMag[a] = 0; pressMs[a] = 0; outward[a] = false; legC[a] = 1; liveTarget[a] = 0; legPause[a] = 0; }
            LastTap = false; TapCount = 0; MoveLevel = 0; Firing = false;
            prevKeysLive = true;
            Alpha = 0;
            OffsetX = OffsetY = 0;
            SetDebug(-1, 0, 0, 0, 0, false, false, false);
        }

        // Exposed for tests and the status line.
        public int CurrentDir { get { return curDir; } }
        public double Recoil { get { return recoil; } }
        public double Lean { get { return lean; } }
        /// <summary>-1 = Q, +1 = E, 0 = none (held keys in hold mode, latched state in toggle mode).</summary>
        public int LeanTarget { get { return (int)leanTarget; } }
        public bool AdsToggled { get { return adsToggled; } }
        private bool adsActiveOut;
        /// <summary>True while ADS is really active this frame (after Alt/Ctrl ignore and preview); the ring hides.</summary>
        public bool AdsActive { get { return adsActiveOut; } }
        /// <summary>Movement component of the ring (UI coords, before lean / recoil / 站着偏移 / resolution).</summary>
        public double MoveX { get { return pos[0]; } }
        public double MoveY { get { return pos[1]; } }
        /// <summary>True while that axis (0 = 左右, 1 = 前后) is still travelling along its curve.</summary>
        public bool Traveling(int axis) { return moving[axis]; }
        /// <summary>ms since that axis's current travel started.</summary>
        public double TravelMs(int axis) { return travelMs[axis]; }
        /// <summary>The live tracked hand (real 换手 key): false 正常手, true 换手.</summary>
        public bool LiveSwitched { get { return liveSwitched; } }
        /// <summary>The hand being shown (0 / 1) and the blend 0..1 towards 换手.</summary>
        public int Hand { get { return handTarget; } }
        public double HandBlend { get { return blend; } }
        /// <summary>True while a 跳跃 runs; JumpMs = ms since its press; JumpOffset = its vertical UI px (+ = up, before resolution scaling).</summary>
        public bool Jumping { get { return jumpOn; } }
        public double JumpMs { get { return jumpT; } }
        public double JumpOffset;
        /// <summary>The live 强化准星 state (real key) and whether the boosted look is shown this frame (BoostStand preview: always).</summary>
        public bool LiveBoost { get { return liveBoost; } }
        public bool Boosted { get { return boostShown; } }
        /// <summary>v4.0: true on the frame 冲刺取消侧身 cleared a latched lean (toggle lean mode).</summary>
        public bool LeanCancelled { get { return leanCancelled; } }
        /// <summary>v4.0: true on the frame 冲刺取消开镜 cleared the 开镜 latch.</summary>
        public bool AdsCancelledBySprint { get { return adsSprintCancelled; } }
        private bool leanCancelled, adsSprintCancelled;

        // ---- v5.1 (W21) outputs (W22 interface, V51-W21.md) ----
        /// <summary>丢雷隐藏 is on: the crosshair is hidden like 开镜 (Alpha 0 the same frame) until the next 丢雷键 press or F12 reset.</summary>
        public bool GrenadeHidden { get { return grenadeHidden; } }
        /// <summary>弹药提示 on (false = draw no ammo hint). R3: no weapon switch, so this is just GlobalSettings.AmmoOn.</summary>
        public bool AmmoActive;
        /// <summary>Ammo.Band: 4 满, 3 大半, 2 一半, 1 少, 0 快空.</summary>
        public int AmmoBand { get { return Ammo.Band; } }
        public int AmmoReloadSeq { get { return Ammo.ReloadSeq; } }
        /// <summary>1 = normal (R), 2 = quick (R double-tap).</summary>
        public int AmmoReloadKind { get { return Ammo.ReloadKind; } }
        /// <summary>Duration of the reload running now in ms (0 = none).</summary>
        public double AmmoReloadMs { get { return Ammo.ReloadMs; } }
        public int AmmoShotSeq { get { return Ammo.ShotSeq; } }
        /// <summary>0..1: how far the movement offset is from rest towards the full offset (follows the slow-start legs).</summary>
        public double MoveLevel;
        /// <summary>LMB held with live keys, not while 丢雷隐藏 (not 开镜-gated; the overlay decides).</summary>
        public bool Firing;
        /// <summary>The last released outward press on an axis was shorter than GlobalSettings.TapThresholdMs (短按).</summary>
        public bool LastTap;
        /// <summary>短按 seen since Reset (tests / dashboard).</summary>
        public int TapCount;
        /// <summary>True while that axis runs the first leg of a reversal (back to zero, then out the other way).</summary>
        public bool Reversing(int axis) { return moving[axis] && legToZero[axis]; }
        /// <summary>The running leg as the dashboard's 运动方程 shows it: Move = LegStart + (LegEnd − LegStart) × LegCurve, exactly.
        /// LegEnd = 0 during the first leg of a reversal, else the live axis target. At rest: start = end = Move, curve 1.</summary>
        public double LegStart(int axis) { return moving[axis] ? from[axis] : pos[axis]; }
        public double LegEnd(int axis) { return moving[axis] ? (legToZero[axis] ? 0.0 : liveTarget[axis]) : pos[axis]; }
        public double LegCurve(int axis) { return moving[axis] ? legC[axis] : 1.0; }
        /// <summary>Length of the running leg in ms (TravelMs counts from its start) and where it entered the curve (0..1).</summary>
        public double LegMs(int axis) { return legDur[axis]; }
        public double LegEntry(int axis) { return legU0[axis]; }
        private readonly double[] liveTarget = new double[2];

        public const int VkTab = 0x09, VkEscape = 0x1B, VkControl = 0x11, VkMenu = 0x12, VkRButton = 0x02, VkShift = 0x10;

        /// <summary>v4.0 组合键: the binding is down when its key AND every modifier in mods is down (generic Shift/Ctrl/Alt
        /// codes, so left and right both count). mods 0 = a plain key: down whenever the key is down (as before).</summary>
        public static bool IsComboDown(int vk, int mods, Func<int, bool> isDown)
        {
            if (vk <= 0 || !isDown(vk)) return false;
            int m = KeyBindings.NormalizeMods(vk, mods);
            if ((m & KeyBindings.ModCtrl) != 0 && !isDown(VkControl)) return false;
            if ((m & KeyBindings.ModShift) != 0 && !isDown(VkShift)) return false;
            if ((m & KeyBindings.ModAlt) != 0 && !isDown(VkMenu)) return false;
            return true;
        }

        /// <summary>v5.1: like IsComboDown, but a modifier that is NOT part of the binding must be up (Ctrl+T is not Ctrl+Shift+T).
        /// A plain-key binding (mods 0) stays "down whenever the key is down", as before.</summary>
        public static bool IsExactComboDown(int vk, int mods, Func<int, bool> isDown)
        {
            if (!IsComboDown(vk, mods, isDown)) return false;
            int m = KeyBindings.NormalizeMods(vk, mods);
            if (m == 0) return true;
            m |= KeyBindings.ModBitOf(vk); // the key itself may be a modifier key
            if ((m & KeyBindings.ModCtrl) == 0 && isDown(VkControl)) return false;
            if ((m & KeyBindings.ModShift) == 0 && isDown(VkShift)) return false;
            if ((m & KeyBindings.ModAlt) == 0 && isDown(VkMenu)) return false;
            return true;
        }

        /// <summary>One action's binding (key + 组合键 modifiers), e.g. the 开关准星 key for the overlay's F12 toggle.</summary>
        public static bool IsActionDown(KeyBindings k, int action, Func<int, bool> isDown)
        {
            return IsComboDown(k.Get(action), k.GetMods(action), isDown);
        }

        /// <summary>Reads every binding through isDown (GetAsyncKeyState in the app, a fake in tests).
        /// v4.0: 前后左右 are always W/S/A/D; bindings may be 组合键; while a 组合键 binding whose key is W/A/S/D is down,
        /// that movement key reads as NOT pressed (右侧身 = Ctrl+D leans right and the ring does not move right).</summary>
        public static InputState ReadInput(KeyBindings k, Func<int, bool> isDown)
        {
            InputState i = new InputState();
            bool w = isDown(KeyBindings.VkW), s = isDown(KeyBindings.VkS), a = isDown(KeyBindings.VkA), d = isDown(KeyBindings.VkD);
            foreach (int act in KeyBindings.RebindableActions)
            {
                int vk = k.Get(act);
                int mods = KeyBindings.NormalizeMods(vk, k.GetMods(act));
                if (mods == 0) continue;
                if (vk != KeyBindings.VkW && vk != KeyBindings.VkS && vk != KeyBindings.VkA && vk != KeyBindings.VkD) continue;
                if (!IsComboDown(vk, mods, isDown)) continue;
                if (vk == KeyBindings.VkW) w = false;
                else if (vk == KeyBindings.VkS) s = false;
                else if (vk == KeyBindings.VkA) a = false;
                else d = false;
            }
            i.Fwd = w; i.Back = s; i.Left = a; i.Right = d;
            i.LeanL = IsActionDown(k, KeyBindings.ActionLeanLeft, isDown); i.LeanR = IsActionDown(k, KeyBindings.ActionLeanRight, isDown);
            i.Fire = IsActionDown(k, KeyBindings.ActionFire, isDown); i.Ads = IsActionDown(k, KeyBindings.ActionAds, isDown);
            i.FreeLook = IsActionDown(k, KeyBindings.ActionFreeLook, isDown);
            i.HandSwitch = IsActionDown(k, KeyBindings.ActionHandSwitch, isDown);
            i.Jump = IsActionDown(k, KeyBindings.ActionJump, isDown);
            // v5.1 (W25 for W21, QA W32): the game's 切换战术设备模式 is EXACTLY Ctrl+T; Ctrl+Shift+T / Ctrl+Alt+T are other game
            // keys, so boost needs the binding's modifiers and no others
            i.Boost = IsExactComboDown(k.Get(KeyBindings.ActionBoost), k.GetMods(KeyBindings.ActionBoost), isDown);
            i.Tab = isDown(VkTab); i.Esc = isDown(VkEscape);
            i.Cancel = i.Tab || i.Esc;
            i.Mod = isDown(VkMenu) || isDown(VkControl);
            i.Shift = isDown(VkShift);
            return i;
        }

        /// <summary>v5.1 (W21): ReadInput plus the keys kept on GlobalSettings: 丢雷键 and 换弹键 (plain VK codes, no 组合键). Callers
        /// that read live keys for the engine (overlay, dashboard, tutorial) should use this one. R3: no weapon keys.</summary>
        public static InputState ReadInput(GlobalSettings g, Func<int, bool> isDown)
        {
            InputState i = ReadInput(g.Keys, isDown);
            i.Grenade = g.GrenadeKey > 0 && isDown(g.GrenadeKey);
            i.Reload = g.AmmoReloadKey > 0 && isDown(g.AmmoReloadKey);
            return i;
        }

        /// <summary>Direction index from the four movement keys. Opposite keys cancel.</summary>
        public static int DirIndex(InputState i)
        {
            int x = (i.Right ? 1 : 0) - (i.Left ? 1 : 0);
            int f = (i.Fwd ? 1 : 0) - (i.Back ? 1 : 0);
            return DirFromSigns(f, x);
        }

        /// <summary>f: +1 W / -1 S; x: +1 D / -1 A. Same table as DirIndex.</summary>
        public static int DirFromSigns(int f, int x)
        {
            if (f == 1 && x == 0) return 0;   // W
            if (f == -1 && x == 0) return 1;  // S
            if (f == 0 && x == -1) return 2;  // A
            if (f == 0 && x == 1) return 3;   // D
            if (f == 1 && x == -1) return 4;  // WA
            if (f == 1 && x == 1) return 5;   // WD
            if (f == -1 && x == -1) return 6; // SA
            if (f == -1 && x == 1) return 7;  // SD
            return -1;
        }

        /// <summary>1 - e^(-dt/τ) with τ = ms/3000 s (≈95 % after 'ms'); 1 (instant) when ms ≤ 0.</summary>
        private static double Ease(double dt, double ms)
        {
            if (ms <= 0) return 1.0;
            double tau = ms / 3000.0;
            return 1.0 - Math.Exp(-dt / tau);
        }

        private static InputState Synthetic(PreviewMode pm, double t, double jumpPeriodMs)
        {
            InputState s = new InputState();
            bool firstHalf = (t % 2400.0) < 1200.0;
            switch (pm)
            {
                case PreviewMode.DirW: s.Fwd = true; break;
                case PreviewMode.DirS: s.Back = true; break;
                case PreviewMode.DirA: s.Left = true; break;
                case PreviewMode.DirD: s.Right = true; break;
                case PreviewMode.DirWA: s.Fwd = true; s.Left = true; break;
                case PreviewMode.DirWD: s.Fwd = true; s.Right = true; break;
                case PreviewMode.DirSA: s.Back = true; s.Left = true; break;
                case PreviewMode.DirSD: s.Back = true; s.Right = true; break;
                case PreviewMode.RecoilMax: s.Fire = true; break;
                case PreviewMode.LeanQ: s.LeanL = true; break;
                case PreviewMode.LeanE: s.LeanR = true; break;
                case PreviewMode.TurnAD: if (firstHalf) s.Left = true; else s.Right = true; break;
                case PreviewMode.TurnWS: if (firstHalf) s.Fwd = true; else s.Back = true; break;
                case PreviewMode.JumpStand:
                    {
                        // stand 500 ms, then a 100 ms Space press; one jump every 总时长 + 500 ms
                        double ph = t % jumpPeriodMs;
                        s.Jump = ph >= 500 && ph < 600;
                        break;
                    }
                default: break; // Static, HandFlip, BoostStand: nothing
            }
            s.Ads = false;
            return s;
        }

        /// <summary>Movement target of one axis for a direction with one hand's values. v3.0: lean + move is always
        /// additive (the 侧身+移动 combos are gone), so the lean target no longer changes the movement target.</summary>
        private static double TargetIn(HandView v, int axis, int dir, int leanT)
        {
            if (dir < 0) return 0.0;
            return axis == 0 ? v.DX[dir] : v.DY[dir];
        }

        private static double Mix(double a, double b, double w) { return a + (b - a) * w; }

        /// <summary>Live axis target (current values, current hand blend).</summary>
        private double AxisTarget(int axis, int dir, int leanT)
        {
            return Mix(TargetIn(view0, axis, dir, leanT), TargetIn(view1, axis, dir, leanT), blend);
        }

        /// <summary>
        /// 换手 tracking from the REAL key (also during previews, 开镜 and 自由视角). 切换: every press flips; 按住: 换手 while held.
        /// Our convention (not game behaviour): switching the ring back on resets to 正常手.
        /// </summary>
        private void UpdateHand(bool key, GlobalSettings g, bool ringOnEdge)
        {
            bool edge = key && !prevHandKey;
            prevHandKey = key;
            if (ringOnEdge) liveSwitched = false;
            if (!g.HandEnabled) { liveSwitched = false; return; }
            if (g.HandMode == "hold") liveSwitched = key;
            else if (edge) liveSwitched = !liveSwitched;
        }

        /// <summary>
        /// 强化准星 (v3.0) from the REAL key, in either hand: every press edge toggles. Switching the ring on (F12 off -> on)
        /// resets it to off, like the hand; a profile switch does not. ☑ 启用 off = never boosted.
        /// v5.1 (W21, decision 3 as corrected by Leo 20 Nov): the key is the game's 切换战术设备模式 key (Keys.Boost + BoostMods,
        /// default Ctrl+T); T alone (device on/off) does nothing because a 组合键 binding needs its Ctrl. BoostToggle false = 按住:
        /// boosted while held. Leo round 2 (R3): weapon keys do nothing to it.
        /// </summary>
        private void UpdateBoost(bool key, GlobalSettings g, bool ringOnEdge)
        {
            bool edge = key && !prevBoostKey;
            prevBoostKey = key;
            if (ringOnEdge) liveBoost = false;
            if (!g.BoostEnabled) { liveBoost = false; return; }
            if (!g.BoostToggle) liveBoost = key;
            else if (edge) liveBoost = !liveBoost;
        }

        /// <summary>v5.1 (W21, decision 2): 丢雷隐藏 toggle from the REAL key. Each press flips hidden / shown; it never comes back
        /// by itself; F12 reset (ring switched on) shows it; GrenadeHideOn off = never hidden.</summary>
        private void UpdateGrenade(bool key, GlobalSettings g, bool ringOnEdge)
        {
            bool edge = key && !prevGrenadeKey;
            prevGrenadeKey = key;
            if (ringOnEdge) grenadeHidden = false;
            if (!g.GrenadeHideOn) { grenadeHidden = false; return; }
            if (edge) grenadeHidden = !grenadeHidden;
        }

        /// <summary>Largest |target| of this axis over the 8 directions (current hand blend): the "full speed" offset.</summary>
        private double FullMag(int axis)
        {
            double m = 0;
            for (int d = 0; d < Profile.DirCount; d++)
            {
                double v = Math.Abs(AxisTarget(axis, d, 0));
                if (v > m) m = v;
            }
            return m;
        }

        /// <summary>
        /// v5.1 (W21, decision 7): a new travel of one axis from where the ring is (x) to newT, along the ONE slow-start curve
        /// (T = 总时长). The offset stands for the character's speed, so (V51-RESEARCH §1, "decelerate then accelerate"):
        /// - out (|newT| ≥ |x|, same side or from 0): continue up the ramp from where x already is (entry u0 with
        ///   Progress(u0) = |x| / |newT|; from rest u0 = 0 = exactly the v5.0.2 travel).
        /// - back (|newT| &lt; |x|, same side or to 0): down the ramp in the time it took to get there
        ///   (T × (Pinv(|x|/F) − Pinv(|newT|/F)), F = the ramp's full offset) × ReturnTimePct / 100: a short tap moves a little
        ///   and returns as fast (100 %).
        /// - other side (A→D): first back to 0 like a release, rest ReversePauseMs at 0, then out from rest with the full curve;
        ///   never a jump; total ≥ ReverseMinMs (the first leg is stretched when needed).
        /// R4 tunables (settings): TapThresholdMs (短按 label only), ReverseMinMs, ReturnTimePct, ReversePauseMs.
        /// </summary>
        private void StartLeg(int a, double oldT, double newT, double T, double[] speeds)
        {
            double x = pos[a];
            double ax = Math.Abs(x), an = Math.Abs(newT);
            if (an < Math.Abs(oldT) || (oldT != 0 && (oldT > 0) != (newT > 0)))
            {
                // a press that was still on its way out when let go (or reversed) before 短按 threshold = 短按
                LastTap = outward[a] && moving[a] && pressMs[a] < tapMs;
                if (LastTap) TapCount++;
            }
            travelMs[a] = 0; moving[a] = true; from[a] = x;
            legPause[a] = 0;
            if (!(T > 0)) { legDur[a] = 0; legU0[a] = 0; legToZero[a] = false; outward[a] = false; refMag[a] = an; return; }
            double F = Math.Max(refMag[a], ax);
            bool cross = (x > 0 && newT < 0) || (x < 0 && newT > 0);
            if (cross)
            {
                legToZero[a] = true; legU0[a] = 0; outward[a] = false;
                double d1 = F > 0 ? returnScale * T * MotionCurve.ProgressInverse(speeds, ax / F) : 0;
                legPause[a] = reversePauseMs;
                if (d1 + legPause[a] + T < reverseMinMs) d1 = reverseMinMs - legPause[a] - T;
                legDur[a] = d1;
                refMag[a] = F;
            }
            else if (an >= ax)
            {
                legToZero[a] = false;
                legU0[a] = an > 0 ? MotionCurve.ProgressInverse(speeds, ax / an) : 0;
                legDur[a] = T * (1 - legU0[a]);
                refMag[a] = an; outward[a] = an > 0; pressMs[a] = 0;
            }
            else
            {
                legToZero[a] = false; legU0[a] = 0; outward[a] = false;
                legDur[a] = F > 0 ? returnScale * T * (MotionCurve.ProgressInverse(speeds, ax / F) - MotionCurve.ProgressInverse(speeds, an / F)) : 0;
                refMag[a] = F;
            }
        }

        /// <summary>One frame of the current leg (travelMs already advanced). target = the live axis target.</summary>
        private void AdvanceLeg(int a, double target, double T, double[] speeds)
        {
            if (legToZero[a])
            {
                double d1 = legDur[a];
                if (d1 > 0 && travelMs[a] < d1)
                {
                    legC[a] = MotionCurve.Progress(speeds, travelMs[a] / d1);
                    pos[a] = from[a] * (1 - legC[a]);
                    return;
                }
                // R4: rest at 0 for ReversePauseMs (LegStart = from, LegEnd = 0, curve 1 -> exactly 0)
                double tz = d1 > 0 ? travelMs[a] - d1 : travelMs[a];
                if (tz < legPause[a]) { legC[a] = 1; pos[a] = 0; return; }
                // back at 0: out the other way from rest with the full curve (the left-over time of this frame counts)
                double over = tz - legPause[a];
                legPause[a] = 0;
                legToZero[a] = false; from[a] = 0; legU0[a] = 0; legDur[a] = T > 0 ? T : 0;
                refMag[a] = Math.Abs(target); outward[a] = target != 0; pressMs[a] = over; travelMs[a] = over;
            }
            double dur = legDur[a];
            if (!(dur > 0) || travelMs[a] >= dur)
            {
                moving[a] = false; pos[a] = target; outward[a] = false; legC[a] = 1;
                if (target != 0) refMag[a] = Math.Abs(target);
                return;
            }
            legC[a] = MotionCurve.ProgressFrom(speeds, legU0[a], travelMs[a] / dur);
            pos[a] = from[a] + (target - from[a]) * legC[a];
        }

        public void Update(double dt, InputState input, Profile p, GlobalSettings g, PreviewMode pm, double previewSpeed)
        {
            bool preview = pm != PreviewMode.Live;
            if (pm != prevPm) { previewMs = 0; prevPm = pm; jumpOn = false; jumpT = 0; }
            // v3.0: while 只在塔科夫前台时显示 is on and the game is not in front, no live key counts as pressed.
            ReadTuning(g); // R4: 短按 / 换向 tunables from the settings
            bool keysLive = FocusGate.KeysLive(g.OnlyWhenGameFocused, GameFocused);
            if (!keysLive) input = new InputState();
            // v4.0: keys already held when the game comes back to front are NOT a new press (like PR #2's preview->实时 rule):
            // no 开镜 / 侧身 / 换手 / 强化 toggle, no 冲刺 cancel, no fire kick, no jump on that frame.
            bool focusRegained = keysLive && !prevKeysLive;
            prevKeysLive = keysLive;
            bool realHandKey = input.HandSwitch;
            bool realBoostKey = input.Boost;
            InputState live = input; // v5.1: 丢雷 / weapon / ammo always follow the REAL keys, also during previews
            if (focusRegained)
            {
                prevHandKey = realHandKey; prevBoostKey = realBoostKey;
                prevGrenadeKey = live.Grenade;
                Ammo.SyncKeys(live.Fire, live.Reload);
            }
            bool ringOnEdge = g.Enabled && !prevEnabled;

            double realDtMs = double.IsNaN(dt) || dt < 0 ? 0 : Math.Min(dt, 0.1) * 1000.0; // v5.1: ammo runs in real time
            // Preview: synthetic input, slow motion.
            if (preview)
            {
                double sp = previewSpeed;
                if (double.IsNaN(sp) || sp < 0.1) sp = 0.1;
                if (sp > 1.0) sp = 1.0;
                dt *= sp;
                input = Synthetic(pm, previewMs, (p.Jump != null ? p.Jump.TotalMs : 700) + 500);
            }
            if (prevPreview != preview)
            {
                // Avoid a spurious toggle/fire edge when switching between preview and live input.
                prev = input;
                // v5.0.2 (W16, BRUTE P3-6): the LeanQ / LeanE previews hold Q / E from their first frame, so in 侧身方式 切换
                // the first preview frame must be a press edge (else the toggle never latches and the preview shows no lean).
                if (preview) { prev.LeanL = false; prev.LeanR = false; }
                prevPreview = preview;
            }
            else if (focusRegained && !preview) prev = input;
            if (double.IsNaN(dt) || dt < 0) dt = 0;
            if (dt > 0.1) dt = 0.1;

            double dtMs = dt * 1000.0;
            if (preview) previewMs += dtMs;

            // 冲刺 = Shift + W (W after the opposite-key filter and 组合键 suppression); its rising edge cancels.
            bool sprintEdge = input.Shift && input.Fwd && !(prev.Shift && prev.Fwd);
            leanCancelled = false; adsSprintCancelled = false;

            // Lean target
            if (g.LeanMode == "toggle")
            {
                // v4.0: switching the ring on (开关键 pressed twice) also clears a latched 切换 lean, like 正常手 / 强化.
                if (ringOnEdge) leanTarget = 0;
                if (input.LeanL && !prev.LeanL) leanTarget = (leanTarget == -1 ? 0 : -1);
                if (input.LeanR && !prev.LeanR) leanTarget = (leanTarget == 1 ? 0 : 1);
                // v4.0 冲刺取消侧身 (toggle only): wins over a same-frame Q/E press, like 冲刺取消开镜.
                if (g.LeanCancelSprint && sprintEdge)
                {
                    leanCancelled = leanTarget != 0;
                    leanTarget = 0;
                }
            }
            else
            {
                if (input.LeanL && !input.LeanR) leanTarget = -1;
                else if (input.LeanR && !input.LeanL) leanTarget = 1;
                else leanTarget = 0;
            }

            // 换手: tracked hand, hand shown (previews use the hand being edited), smoothstep blend.
            // 跳跃 (v2.4.3): a press edge starts the jump clock; presses during a jump are ignored (no double jump —
            // our assumption, UNVERIFIED). The clock keeps running while hidden; switching the ring on resets it.
            if (ringOnEdge) { jumpOn = false; jumpT = 0; }
            JumpSettings js = p.Jump;
            bool jumpOk = js != null && js.Enabled && js.TotalMs > 0;
            if (!jumpOk) jumpOn = false;
            else
            {
                if (input.Jump && !prev.Jump && !jumpOn) { jumpOn = true; jumpT = 0; }
                if (jumpOn) { jumpT += dtMs; if (jumpT >= js.TotalMs) { jumpOn = false; jumpT = 0; } }
            }
            JumpOffset = jumpOn ? JumpPath.Offset(js, jumpT) : 0.0;

            UpdateHand(realHandKey, g, ringOnEdge);
            UpdateBoost(realBoostKey, g, ringOnEdge);
            UpdateGrenade(live.Grenade, g, ringOnEdge);
            prevEnabled = g.Enabled;
            boostShown = preview ? pm == PreviewMode.BoostStand : liveBoost;
            int edit = g.EditHand == 1 ? 1 : 0;
            int want;
            if (!preview) want = liveSwitched ? 1 : 0;
            else if (pm == PreviewMode.HandFlip) want = ((previewMs % 2400.0) < 1200.0) ? edit : 1 - edit;
            else want = edit;
            handTarget = want;
            if (want != blendTo) { blendFrom = blend; blendTo = want; blendT = 0; }
            // v4.1 (BRUTE40, Leo 17 Nov): 开关键 pressed twice = full reset SNAPS back to 正常手 (no 换手过渡 slide).
            if (ringOnEdge && !preview) { blend = want; blendFrom = want; blendTo = want; blendT = Math.Max(0.0, p.HandSwitchMs); }
            blendT += dtMs;
            if (p.HandSwitchMs <= 0 || blendT >= p.HandSwitchMs) blend = blendTo;
            else
            {
                double u = blendT / p.HandSwitchMs;
                blend = blendFrom + (blendTo - blendFrom) * (u * u * (3 - 2 * u));
            }
            p.FillView(0, view0);
            p.FillView(1, view1);

            // Movement: one travel per axis along its speed curve.
            int rawX = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
            int rawF = (input.Fwd ? 1 : 0) - (input.Back ? 1 : 0);
            int dir = DirFromSigns(rawF, rawX);
            int lt = (int)leanTarget;
            if (dir != prevDir || lt != prevLean)
            {
                for (int a = 0; a < 2; a++)
                {
                    // Only a key change that changes THIS axis's target restarts it, from where the ring is.
                    double oldT = AxisTarget(a, prevDir, prevLean), newT = AxisTarget(a, dir, lt);
                    if (oldT != newT)
                    {
                        MoveCurve c0 = a == 0 ? p.CurveAD : p.CurveWS;
                        StartLeg(a, oldT, newT, c0 == null ? 0 : c0.DurationMs, c0 == null ? null : c0.Speeds);
                    }
                }
                prevDir = dir; prevLean = lt;
            }
            curDir = dir;
            double level = 0;
            bool anyFull = false;
            for (int a = 0; a < 2; a++)
            {
                MoveCurve c = a == 0 ? p.CurveAD : p.CurveWS;
                double total = c == null ? 0 : c.DurationMs;
                double target = AxisTarget(a, dir, lt);
                liveTarget[a] = target;
                if (moving[a])
                {
                    travelMs[a] += dtMs;
                    if (outward[a]) pressMs[a] += dtMs;
                    AdvanceLeg(a, target, total, c == null ? null : c.Speeds);
                }
                else pos[a] = target;
                double full = FullMag(a);
                if (full > 0) { anyFull = true; double lv = Math.Abs(pos[a]) / full; if (lv > level) level = lv; }
            }
            if (!anyFull) level = dir >= 0 ? 1.0 : 0.0;
            MoveLevel = level > 1 ? 1 : level;

            lean += (leanTarget - lean) * Ease(dt, p.LeanMs);
            if (ringOnEdge && !preview) lean = leanTarget; // v4.1: the reset also snaps the lean (no 侧身过渡 slide)
            double lx, ly;
            if (lean < 0) { lx = -lean * Mix(view0.QX, view1.QX, blend); ly = -lean * Mix(view0.QY, view1.QY, blend); }
            else { lx = lean * Mix(view0.EX, view1.EX, blend); ly = lean * Mix(view0.EY, view1.EY, blend); }

            // ADS
            // v3.2: (1) 开镜键是右键 and Alt/Ctrl is held at the press -> 切倍率/切瞄具, not 开镜 (the whole press is
            // ignored, also if Alt/Ctrl is let go first). (2) 点一下切换: Tab/Esc (背包/菜单) clears the 开镜 latch.
            // (3) Switching the ring on (F12) clears it too. Game behaviour behind (1)/(2) is from a bug report, UNVERIFIED.
            if (ringOnEdge) adsToggled = false;
            bool adsEdge = input.Ads && !prev.Ads;
            // v4.0: only a plain 右键 binding (no 组合键) uses the Alt/Ctrl rule; Ctrl+右键 bound on purpose is a real 开镜.
            if (adsEdge) adsPressBlocked = g.AdsIgnoreAltCtrl && g.Keys != null && g.Keys.Ads == VkRButton && g.Keys.AdsMods == 0 && input.Mod;
            // v4.1 (BRUTE40 P2-4): only a release seen while keys are live ends the ignored press. Unfocused frames read
            // all keys up, so without keysLive an Alt/Ctrl+右键 held across a focus change came back as 开镜 (按住).
            if (!input.Ads && keysLive) adsPressBlocked = false;
            bool adsActive;
            if (g.AdsMode == "toggle")
            {
                if (adsEdge && !adsPressBlocked) adsToggled = !adsToggled;
                // v4.1 (BRUTE40 P2-3): every Tab or Esc press cancels, also while the other one is held.
                bool cancelEdge = (input.Cancel && !prev.Cancel) || (input.Tab && !prev.Tab) || (input.Esc && !prev.Esc);
                if (g.AdsCancelTabEsc && cancelEdge) adsToggled = false;
                // v3.3 冲刺取消开镜: Shift + W together (either order) clears the latch. Shift alone (屏息) does not.
                if (g.AdsCancelSprint && sprintEdge)
                {
                    adsSprintCancelled = adsToggled;
                    adsToggled = false;
                }
                adsActive = adsToggled;
            }
            else
            {
                adsActive = input.Ads && !adsPressBlocked;
                adsToggled = false; // v4.1 (BRUTE40 P2-2): no stale 切换 latch survives a switch to 按住 and back
            }
            if (preview) adsActive = false;
            adsActiveOut = adsActive;

            // Recoil. The click kick and the hold rise start RecoilDelayMs after the press, so at the press frame
            // the ring still shows the residual recoil this bullet really carries.
            bool pressEdge = input.Fire && !prev.Fire;
            if (pressEdge) { kickPending = true; sincePressMs = 0; }
            sincePressMs += dtMs;
            double cap = p.RecoilMaxPx;
            if (cap <= 0 || adsActive)
            {
                recoil = 0;
                kickPending = false;
            }
            else
            {
                // Part of this frame spent held AND past the delay rises; the rest falls.
                double riseMs = input.Fire ? Math.Max(0.0, Math.Min(dtMs, sincePressMs - p.RecoilDelayMs)) : 0.0;
                double fallMs = dtMs - riseMs;
                if (fallMs > 0) recoil -= recoil * Ease(fallMs / 1000.0, p.RecoilFallMs);
                if (kickPending && sincePressMs >= p.RecoilDelayMs - 1e-9)
                {
                    recoil += p.RecoilClickKickPx;
                    kickPending = false;
                }
                if (riseMs > 0) recoil += (cap - recoil) * Ease(riseMs / 1000.0, p.RecoilRiseMs);
                if (recoil < 0) recoil = 0;
                if (recoil > cap) recoil = cap;
                if (pm == PreviewMode.RecoilMax) recoil = cap;
            }
            double rx = cap > 0 ? Mix(view0.Drift, view1.Drift, blend) * recoil / cap : 0.0;
            double ry = recoil;

            // Visibility: 开镜 and (optionally) 自由视角 hide the ring; motion keeps running underneath.
            bool freeLookHide = g.HideOnFreeLook && input.FreeLook;
            bool grenadeHide = !preview && grenadeHidden;
            bool visible = FocusGate.Visible(g.Enabled, !preview && (adsActive || freeLookHide || grenadeHidden), g.OnlyWhenGameFocused, GameFocused, OwnForeground);

            // v5.1 (W21) 弹药提示: counted from the REAL keys (previews do not fire). Not counted while 丢雷隐藏 is on
            // (LMB then throws the grenade, UNVERIFIED). Switching the feature on, or F12 reset, = full mag on the main weapon.
            Ammo.Configure(g.AmmoMagSize, g.AmmoRpm, g.AmmoSemi, g.AmmoQuickTapMs, g.AmmoReloadMs, g.AmmoQuickReloadMs); // R2
            if (ringOnEdge || (g.AmmoOn && !prevAmmoOn)) Ammo.Reset();
            prevAmmoOn = g.AmmoOn;
            Ammo.Update(realDtMs, live.Fire && !grenadeHidden, live.Reload);
            AmmoActive = g.AmmoOn; // R3: no weapon switch, the hint shows whenever the feature is on
            Firing = live.Fire && !grenadeHidden;

            if (grenadeHide) Alpha = 0; // 丢雷: hidden in the same frame, no fade (the overlay's one all-zero frame)
            else if (visible)
            {
                if (g.ShowFadeMs <= 0) Alpha = 1;
                else Alpha = Math.Min(1.0, Alpha + dt / (g.ShowFadeMs / 1000.0));
            }
            else
            {
                if (g.HideFadeMs <= 0) Alpha = 0;
                else Alpha = Math.Max(0.0, Alpha - dt / (g.HideFadeMs / 1000.0));
            }

            // Output (UI coords -> screen y-down, in exactly one place). 分辨率换算 s scales every pixel offset.
            double rs = g.ResolutionScale;
            double rsX = g.ResolutionScaleX; // v4.2: X also carries the 画面比例 stretch (== rs when the ratio is native)
            double uiX = rsX * (pos[0] + lx + rx + Mix(view0.SX, view1.SX, blend)); // 站立静止偏移 (v5.1 decision 5: 0 unless StandAsZero; then the new zero, Dirs stored relative to it)
            double sumY = pos[1] + ly + ry + Mix(view0.SY, view1.SY, blend);
            if (jumpOn) sumY += JumpOffset; // 跳跃: additive, vertical only, both hands; absent = exactly the v2.4.2 expression
            double uiY = rs * sumY;
            OffsetX = uiX;
            OffsetY = -uiY;
            SetDebug(curDir, lean, ry * rs, uiX, uiY, handTarget == 1, jumpOn, boostShown);

            prev = input;
        }
    }

    /// <summary>
    /// v4.0 PERF: the overlay's "does the raster need redrawing" test without building a string per frame. Watches exactly
    /// the fields RingStyle.Key() covers (OpacityPct and PngMissing excluded; the picture by Png.Id) plus canvas size S and
    /// the sub-pixel centre cx/cy. Changed() compares against the last committed frame and commits; no allocation.
    /// Lives in Motion.cs (a pure file) so tests-v4 compiles it; TestsV4Perf P6 checks it against Key() and RingStyle's fields.
    /// </summary>
    public sealed class RasterChangeDetector
    {
        private bool has;
        private int s;
        private double cx, cy;
        private string shape, gradient, kind, pngId;
        private double diameter, thickness, feather, outerThickness, centerDotSize, crossLength, crossThickness, crossGap,
            dashGapPct, glowPx, glowOpacityPct, rotationDeg, sGap, sLength, sSpread, hole;
        private bool centerDot, crossTop, pngTint;
        private int r1, g1, b1, r2, g2, b2, dashes, sCount;
        private readonly bool colours;

        /// <summary>Compares every field the raster depends on, the two colours included.</summary>
        public RasterChangeDetector() : this(true) { }

        /// <summary>v5.0.2 (W16, AUDIT F6): withColours=false ignores 颜色 / 颜色2 (RasterCache keys a look by its shape and
        /// handles a colour-only change (a 换手颜色 fade) without new key strings).</summary>
        public RasterChangeDetector(bool withColours) { colours = withColours; }

        /// <summary>Forget the last frame: the next Changed() returns true.</summary>
        public void Reset() { has = false; }

        private static bool Eq(double a, double b) { return a == b || (double.IsNaN(a) && double.IsNaN(b)); }

        /// <summary>true when (style, S, cx, cy) differs from the previous call (always true after Reset); then remembers it.</summary>
        public bool Changed(RingStyle p, int S, double cX, double cY)
        {
            string id = p.Png == null ? "" : p.Png.Id;
            if (has && s == S && Eq(cx, cX) && Eq(cy, cY)
                && shape == p.Shape && gradient == p.Gradient && kind == p.Kind && pngId == id
                && Eq(diameter, p.Diameter) && Eq(thickness, p.Thickness) && Eq(feather, p.Feather) && Eq(outerThickness, p.OuterThickness)
                && Eq(centerDotSize, p.CenterDotSize) && Eq(crossLength, p.CrossLength) && Eq(crossThickness, p.CrossThickness)
                && Eq(crossGap, p.CrossGap) && Eq(dashGapPct, p.DashGapPct) && Eq(glowPx, p.GlowPx) && Eq(glowOpacityPct, p.GlowOpacityPct)
                && Eq(rotationDeg, p.RotationDeg) && Eq(sGap, p.SGap) && Eq(sLength, p.SLength) && Eq(sSpread, p.SSpread) && Eq(hole, p.Hole)
                && centerDot == p.CenterDot && crossTop == p.CrossTop && pngTint == p.PngTint
                && (!colours || (r1 == p.R1 && g1 == p.G1 && b1 == p.B1 && r2 == p.R2 && g2 == p.G2 && b2 == p.B2)) && dashes == p.Dashes && sCount == p.SCount)
                return false;
            has = true; s = S; cx = cX; cy = cY;
            shape = p.Shape; gradient = p.Gradient; kind = p.Kind; pngId = id;
            diameter = p.Diameter; thickness = p.Thickness; feather = p.Feather; outerThickness = p.OuterThickness;
            centerDotSize = p.CenterDotSize; crossLength = p.CrossLength; crossThickness = p.CrossThickness; crossGap = p.CrossGap;
            dashGapPct = p.DashGapPct; glowPx = p.GlowPx; glowOpacityPct = p.GlowOpacityPct; rotationDeg = p.RotationDeg;
            sGap = p.SGap; sLength = p.SLength; sSpread = p.SSpread; hole = p.Hole;
            centerDot = p.CenterDot; crossTop = p.CrossTop; pngTint = p.PngTint;
            r1 = p.R1; g1 = p.G1; b1 = p.B1; r2 = p.R2; g2 = p.G2; b2 = p.B2; dashes = p.Dashes; sCount = p.SCount;
            return true;
        }
    }
}
