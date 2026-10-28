// 用途：根据按键和枪械设置计算腰射瞄准点的位置与透明度，不依赖窗口。
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
        /// <summary>本帧按住的取消开镜键，分别记录每个键的新按下事件。</summary>
        public CancelAdsInput CancelAds;
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

    public sealed partial class MotionEngine
    {
        // ---- outputs ----
        public double OffsetX;   // screen px, y-down, includes the profile's StandOffset
        public double OffsetY;
        public double Alpha;     // 0..1 (visibility fade: 开镜 / 自由视角 / toggled off)
        public MotionDebug Debug;

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

        /// <summary>v3.0 只在塔科夫前台时显示: set by the render thread (≤ 10 Hz) before Update. Defaults = game in front.</summary>
        public bool GameFocused = true;
        public bool OwnForeground;

        public MotionEngine() { Reset(); }

        /// <summary>Back to the initial state (the "重播" button).</summary>
        public void Reset()
        {
            for (int a = 0; a < 2; a++) { pos[a] = 0; from[a] = 0; travelMs[a] = 0; moving[a] = false; }
            prevDir = -1; prevLean = 0; curDir = -1;
            lean = leanTarget = 0;
            adsToggled = false;
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
            Alpha = 0;
            OffsetX = OffsetY = 0;
            Debug = new MotionDebug(-1, 0, 0, 0, 0);
        }

        // Exposed for tests and the status line.
        public int CurrentDir { get { return curDir; } }
        public double Recoil { get { return recoil; } }
        public double Lean { get { return lean; } }
        /// <summary>-1 = Q, +1 = E, 0 = none (held keys in hold mode, latched state in toggle mode).</summary>
        public int LeanTarget { get { return (int)leanTarget; } }
        public bool AdsToggled { get { return adsToggled; } }
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

        /// <summary>Reads every binding through isDown (GetAsyncKeyState in the app, a fake in tests).</summary>
        public static InputState ReadInput(KeyBindings k, Func<int, bool> isDown)
        {
            InputState i = new InputState();
            i.Fwd = isDown(k.Forward); i.Back = isDown(k.Back); i.Left = isDown(k.Left); i.Right = isDown(k.Right);
            i.LeanL = isDown(k.LeanLeft); i.LeanR = isDown(k.LeanRight);
            i.Fire = isDown(k.Fire); i.Ads = isDown(k.Ads); i.FreeLook = isDown(k.FreeLook);
            i.HandSwitch = isDown(k.HandSwitch);
            i.Jump = isDown(k.Jump);
            i.Boost = isDown(k.Boost);
            // 分别记录取消键，按住 Tab 再按 Esc 也能产生新的取消事件。
            int[] cancelKeys = k.CancelAdsKeys;
            if (cancelKeys != null)
                for (int n = 0; n < cancelKeys.Length; n++)
                    if (isDown(cancelKeys[n])) i.CancelAds.SetDown(cancelKeys[n]);
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
        /// </summary>
        private void UpdateBoost(bool key, GlobalSettings g, bool ringOnEdge)
        {
            bool edge = key && !prevBoostKey;
            prevBoostKey = key;
            if (ringOnEdge) liveBoost = false;
            if (!g.BoostEnabled) { liveBoost = false; return; }
            if (edge) liveBoost = !liveBoost;
        }

        /// <summary>更新一帧位置和显隐；开镜同步不会重置其他运动状态。</summary>
        public void Update(double dt, InputState input, Profile p, GlobalSettings g, PreviewMode pm, double previewSpeed)
        {
            bool preview = pm != PreviewMode.Live;
            if (pm != prevPm) { previewMs = 0; prevPm = pm; jumpOn = false; jumpT = 0; }
            // 开启前台限制时，其他窗口的输入不参与实时状态计算。
            if (!FocusGate.KeysLive(g.OnlyWhenGameFocused, GameFocused)) input = new InputState();
            bool realHandKey = input.HandSwitch;
            bool realBoostKey = input.Boost;
            bool ringOnEdge = g.Enabled && !prevEnabled;

            // 预览使用模拟输入，并应用预览速度。
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
                // 切换预览和实时时接续当前输入，避免产生额外的按下事件。
                prev = input;
                prevPreview = preview;
            }
            if (double.IsNaN(dt) || dt < 0) dt = 0;
            if (dt > 0.1) dt = 0.1;

            double dtMs = dt * 1000.0;
            if (preview) previewMs += dtMs;

            UpdateLean(input, g);
            UpdateJump(input, p.Jump, ringOnEdge, dtMs);
            UpdateHand(realHandKey, g, ringOnEdge);
            UpdateBoost(realBoostKey, g, ringOnEdge);
            prevEnabled = g.Enabled;
            boostShown = preview ? pm == PreviewMode.BoostStand : liveBoost;
            UpdateHandBlend(p, g, pm, preview, dtMs);
            UpdateMovement(input, p, dtMs);
            lean += (leanTarget - lean) * Ease(dt, p.LeanMs);
            double lx, ly;
            if (lean < 0) { lx = -lean * Mix(view0.QX, view1.QX, blend); ly = -lean * Mix(view0.QY, view1.QY, blend); }
            else { lx = lean * Mix(view0.EX, view1.EX, blend); ly = lean * Mix(view0.EY, view1.EY, blend); }

            bool adsActive = UpdateAds(input, g, preview);

            // 开火延迟后再上抬，按下这一帧仍保留上次射击的残余偏移。
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
                // 超过开火延迟且仍按住的时段上抬，其他时段回落。
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

            // 开镜和自由视角按原有规则隐藏瞄准点，运动状态继续更新。
            bool freeLookHide = g.HideOnFreeLook && input.FreeLook;
            bool visible = FocusGate.Visible(g.Enabled, !preview && (adsActive || freeLookHide), g.OnlyWhenGameFocused, GameFocused, OwnForeground);
            if (visible)
            {
                if (g.ShowFadeMs <= 0) Alpha = 1;
                else Alpha = Math.Min(1.0, Alpha + dt / (g.ShowFadeMs / 1000.0));
            }
            else
            {
                if (g.HideFadeMs <= 0) Alpha = 0;
                else Alpha = Math.Max(0.0, Alpha - dt / (g.HideFadeMs / 1000.0));
            }

            // 最后统一换算分辨率，并转为屏幕向下为正的坐标。
            double rs = g.ResolutionScale;
            double uiX = rs * (pos[0] + lx + rx + Mix(view0.SX, view1.SX, blend)); // 站着偏移 (per gun, per hand)
            double sumY = pos[1] + ly + ry + Mix(view0.SY, view1.SY, blend);
            if (jumpOn) sumY += JumpOffset; // 跳跃: additive, vertical only, both hands; absent = exactly the v2.4.2 expression
            double uiY = rs * sumY;
            OffsetX = uiX;
            OffsetY = -uiY;
            Debug = new MotionDebug(curDir, lean, ry * rs, uiX, uiY, handTarget == 1, jumpOn, boostShown);

            // 取消后也保留本帧按键历史，长按开镜键不会被误认为再次按下。
            prev = input;
        }
    }
}
