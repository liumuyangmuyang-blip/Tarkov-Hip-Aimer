// TEST ONLY: verbatim v2.4.2 Motion.cs (namespace renamed) - reference to prove 跳跃 off = v2.4.2 bit for bit.
// PURE file: only System / System.Collections.Generic. Compiles under net48 (C# 5) and net8.0.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.V242
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
        HandFlip = 15    // 换手来回切（站着）: the hand flips every 1.2 s, no movement keys
    }

    public struct InputState
    {
        public bool Fwd, Back, Left, Right, LeanL, LeanR, Fire, Ads;
        /// <summary>自由视角 (hide while held).</summary>
        public bool FreeLook;
        /// <summary>换手 key (v2.4).</summary>
        public bool HandSwitch;
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

        public MotionDebug(int dir, double lean, double recoil, double x, double y)
            : this(dir, lean, recoil, x, y, false) { }

        public MotionDebug(int dir, double lean, double recoil, double x, double y, bool switched)
        {
            Dir = dir; Lean = lean; Recoil = recoil; UiOffsetX = x; UiOffsetY = y; Switched = switched;
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

        /// <summary>Reads every binding through isDown (GetAsyncKeyState in the app, a fake in tests).</summary>
        public static InputState ReadInput(KeyBindings k, Func<int, bool> isDown)
        {
            InputState i = new InputState();
            i.Fwd = isDown(k.Forward); i.Back = isDown(k.Back); i.Left = isDown(k.Left); i.Right = isDown(k.Right);
            i.LeanL = isDown(k.LeanLeft); i.LeanR = isDown(k.LeanRight);
            i.Fire = isDown(k.Fire); i.Ads = isDown(k.Ads); i.FreeLook = isDown(k.FreeLook);
            i.HandSwitch = isDown(k.HandSwitch);
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

        private static InputState Synthetic(PreviewMode pm, double t)
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
                default: break; // Static, HandFlip: nothing
            }
            s.Ads = false;
            return s;
        }

        /// <summary>Movement target of one axis for (dir, lean target) with one hand's values: direction offset, or enabled combo total minus full lean.</summary>
        private static double TargetIn(HandView v, int axis, int dir, int leanT)
        {
            if (dir < 0) return 0.0;
            double tx = v.DX[dir], ty = v.DY[dir];
            // v3.0 test adaptation: the lean+move combos are gone (= every 单独设置 off in v2.4.2).
            return axis == 0 ? tx : ty;
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
        private void UpdateHand(bool key, GlobalSettings g)
        {
            bool edge = key && !prevHandKey;
            prevHandKey = key;
            if (g.Enabled && !prevEnabled) liveSwitched = false;
            prevEnabled = g.Enabled;
            if (!g.HandEnabled) { liveSwitched = false; return; }
            if (g.HandMode == "hold") liveSwitched = key;
            else if (edge) liveSwitched = !liveSwitched;
        }

        public void Update(double dt, InputState input, Profile p, GlobalSettings g, PreviewMode pm, double previewSpeed)
        {
            bool preview = pm != PreviewMode.Live;
            if (pm != prevPm) { previewMs = 0; prevPm = pm; }
            bool realHandKey = input.HandSwitch;

            // Preview: synthetic input, slow motion.
            if (preview)
            {
                double sp = previewSpeed;
                if (double.IsNaN(sp) || sp < 0.1) sp = 0.1;
                if (sp > 1.0) sp = 1.0;
                dt *= sp;
                input = Synthetic(pm, previewMs);
            }
            if (prevPreview != preview)
            {
                // Avoid a spurious toggle/fire edge when switching between preview and live input.
                prev = input;
                prevPreview = preview;
            }
            if (double.IsNaN(dt) || dt < 0) dt = 0;
            if (dt > 0.1) dt = 0.1;

            double dtMs = dt * 1000.0;
            if (preview) previewMs += dtMs;

            // Lean target first (an Enabled lean+move combo changes the movement target)
            if (g.LeanMode == "toggle")
            {
                if (input.LeanL && !prev.LeanL) leanTarget = (leanTarget == -1 ? 0 : -1);
                if (input.LeanR && !prev.LeanR) leanTarget = (leanTarget == 1 ? 0 : 1);
            }
            else
            {
                if (input.LeanL && !input.LeanR) leanTarget = -1;
                else if (input.LeanR && !input.LeanL) leanTarget = 1;
                else leanTarget = 0;
            }

            // 换手: tracked hand, hand shown (previews use the hand being edited), smoothstep blend.
            UpdateHand(realHandKey, g);
            int edit = g.EditHand == 1 ? 1 : 0;
            int want;
            if (!preview) want = liveSwitched ? 1 : 0;
            else if (pm == PreviewMode.HandFlip) want = ((previewMs % 2400.0) < 1200.0) ? edit : 1 - edit;
            else want = edit;
            handTarget = want;
            if (want != blendTo) { blendFrom = blend; blendTo = want; blendT = 0; }
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
                    if (AxisTarget(a, prevDir, prevLean) != AxisTarget(a, dir, lt))
                    {
                        from[a] = pos[a]; travelMs[a] = 0; moving[a] = true;
                    }
                }
                prevDir = dir; prevLean = lt;
            }
            curDir = dir;
            for (int a = 0; a < 2; a++)
            {
                MoveCurve c = a == 0 ? p.CurveAD : p.CurveWS;
                double total = c == null ? 0 : c.DurationMs;
                double target = AxisTarget(a, dir, lt);
                if (moving[a])
                {
                    travelMs[a] += dtMs;
                    if (total <= 0 || travelMs[a] >= total) { moving[a] = false; pos[a] = target; }
                    else pos[a] = from[a] + (target - from[a]) * MotionCurve.Progress(c.Speeds, travelMs[a] / total);
                }
                else pos[a] = target;
            }

            lean += (leanTarget - lean) * Ease(dt, p.LeanMs);
            double lx, ly;
            if (lean < 0) { lx = -lean * Mix(view0.QX, view1.QX, blend); ly = -lean * Mix(view0.QY, view1.QY, blend); }
            else { lx = lean * Mix(view0.EX, view1.EX, blend); ly = lean * Mix(view0.EY, view1.EY, blend); }

            // ADS
            bool adsActive;
            if (g.AdsMode == "toggle")
            {
                if (input.Ads && !prev.Ads) adsToggled = !adsToggled;
                adsActive = adsToggled;
            }
            else
            {
                adsActive = input.Ads;
            }
            if (preview) adsActive = false;

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
            bool visible = g.Enabled && (preview || (!adsActive && !freeLookHide));
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

            // Output (UI coords -> screen y-down, in exactly one place). 分辨率换算 s scales every pixel offset.
            double rs = g.ResolutionScale;
            double uiX = rs * (pos[0] + lx + rx + Mix(view0.SX, view1.SX, blend)); // 站着偏移 (per gun, per hand)
            double uiY = rs * (pos[1] + ly + ry + Mix(view0.SY, view1.SY, blend));
            OffsetX = uiX;
            OffsetY = -uiY;
            Debug = new MotionDebug(curDir, lean, ry * rs, uiX, uiY, handTarget == 1);

            prev = input;
        }
    }
}
