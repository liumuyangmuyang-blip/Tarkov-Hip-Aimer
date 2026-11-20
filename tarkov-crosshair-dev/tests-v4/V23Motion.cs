// TEST ONLY: verbatim v2.3 Motion.cs (namespace renamed) — reference to prove what remains behaves exactly like v2.3.
// PURE file: only System / System.Collections.Generic. Compiles under net48 (C# 5) and net8.0.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.V23
{
    public enum PreviewMode
    {
        Live = 0,
        Static = 1,
        DirW = 2, DirS = 3, DirA = 4, DirD = 5, DirWA = 6, DirWD = 7, DirSA = 8, DirSD = 9,
        RecoilMax = 10,
        LeanQ = 11,
        LeanE = 12,
        ReverseAD = 13,   // A→D 反向: A 1.2 s, D 1.2 s, repeat
        ReverseWS = 14    // W→S 反向: W 1.2 s, S 1.2 s, repeat
    }

    public struct InputState
    {
        public bool Fwd, Back, Left, Right, LeanL, LeanR, Fire, Ads;
        // v2.1: 自由视角 (hide while held), 蹲下 tracker inputs (C toggles; Shift+W sprint, Space jump, X prone stand you up).
        public bool FreeLook, Crouch, Sprint, Jump, Prone;
    }

    /// <summary>Immutable snapshot of engine state for the status line.</summary>
    public sealed class MotionDebug
    {
        public readonly int Dir;           // -1 = stopped
        public readonly double Lean;       // -1..1
        public readonly double Recoil;     // px (as shown, crouch scale applied)
        public readonly double UiOffsetX;  // UI coords (X right +, Y up +), includes StandOffset
        public readonly double UiOffsetY;
        public readonly bool Crouched;

        public MotionDebug(int dir, double lean, double recoil, double x, double y)
            : this(dir, lean, recoil, x, y, false) { }

        public MotionDebug(int dir, double lean, double recoil, double x, double y, bool crouched)
        {
            Dir = dir; Lean = lean; Recoil = recoil; UiOffsetX = x; UiOffsetY = y; Crouched = crouched;
        }

        public string DirName
        {
            get { return Dir >= 0 && Dir < Profile.DirNames.Length ? Profile.DirNames[Dir] : "静止"; }
        }
    }

    public sealed class MotionEngine
    {
        // ---- outputs ----
        public double OffsetX;   // screen px, y-down, includes the profile's StandOffset
        public double OffsetY;
        public double Alpha;     // 0..1 (visibility fade: 开镜 / 自由视角 / toggled off)
        /// <summary>冲刺后恢复 opacity multiplier 0..1 (1 unless the feature dims the ring). Drawn opacity = OpacityPct x Alpha x Dim.</summary>
        public double Dim;
        public MotionDebug Debug;

        // ---- state (UI coords: X right +, Y up +) ----
        private double posX, posY, velX, velY;
        private int curDir;      // effective direction (after 反向惯性)
        private double lean, leanTarget;
        private bool adsToggled;
        private double recoil;   // internal, in UNSCALED px (0..RecoilMaxPx); output = recoil * crouch scale
        private InputState prev;
        private bool prevPreview;
        private PreviewMode prevPm;
        private double previewMs;
        private double sincePressMs;
        private bool kickPending;

        // ---- 蹲下 tracker + eased recoil scales ----
        private bool crouched;
        private double sMax, sKick, sDrift;
        private double sMove;          // 蹲着走 movement scale (eased)
        private double sinceSprintMs;  // ms since sprint ended (0 while sprinting)
        private bool sprintOn;         // 冲刺方式 切换: latched sprint state

        // ---- 反向惯性 v2.1: one active reversal path at a time ----
        public const int PhaseNone = 0, PhaseDecel = 1, PhasePause = 2, PhaseAccel = 3;
        private bool revOn;
        private readonly bool[] revAxis = new bool[2];   // [0] 左右 (A-/D+), [1] 前后 (S-/W+)
        private readonly int[] revFrom = new int[2];      // key sign before the flip
        private readonly int[] prevEff = new int[2];      // effective sign last frame
        private ReverseAxis revCfg;                       // copy of the config driving this reversal
        private double revT;                              // ms since the trigger
        private double p0x, p0y;                          // anchor = ring offset at the trigger
        private double cX, cY, cVX, cVY;                  // re-anchor correction (decays at 跟随时间)
        private double lastNx, lastNy, lastWx, lastWy;    // last neutral / new targets
        private double pathVX, pathVY;                    // path velocity (px/s), handed to the spring on exit

        public MotionEngine() { Reset(); }

        /// <summary>Back to the initial state (the "重播" button).</summary>
        public void Reset()
        {
            posX = posY = velX = velY = 0;
            curDir = -1;
            lean = leanTarget = 0;
            adsToggled = false;
            recoil = 0;
            prev = new InputState();
            prevPreview = false;
            prevPm = PreviewMode.Live;
            previewMs = 0;
            sincePressMs = 0;
            kickPending = false;
            crouched = false;
            sMax = sKick = sDrift = 1.0;
            sMove = 1.0; Dim = 1.0; sinceSprintMs = double.PositiveInfinity; sprintOn = false;
            revOn = false;
            for (int a = 0; a < 2; a++) { revAxis[a] = false; revFrom[a] = 0; prevEff[a] = 0; }
            revCfg = null; revT = 0; p0x = p0y = 0; cX = cY = cVX = cVY = 0;
            lastNx = lastNy = lastWx = lastWy = 0; pathVX = pathVY = 0;
            Alpha = 0;
            OffsetX = OffsetY = 0;
            Debug = new MotionDebug(-1, 0, 0, 0, 0);
        }

        // Exposed for tests and the status line.
        public int CurrentDir { get { return curDir; } }
        /// <summary>Recoil as shown (UNSCALED internal value x crouch 最高上抬 scale).</summary>
        public double Recoil { get { return recoil * sMax; } }
        public double Lean { get { return lean; } }
        /// <summary>-1 = Q, +1 = E, 0 = none (held keys in hold mode, latched state in toggle mode).</summary>
        public int LeanTarget { get { return (int)leanTarget; } }
        public bool AdsToggled { get { return adsToggled; } }
        public bool Crouched { get { return crouched; } }
        public double MoveScale { get { return sMove; } }
        /// <summary>Sprint state after the last Update (按住: key + 前进 held; 切换: latched).</summary>
        public bool Sprinting { get { return sprintOn; } }

        /// <summary>Reads every binding through isDown (GetAsyncKeyState in the app, a fake in tests). Jump = Space.</summary>
        public static InputState ReadInput(KeyBindings k, Func<int, bool> isDown)
        {
            InputState i = new InputState();
            i.Fwd = isDown(k.Forward); i.Back = isDown(k.Back); i.Left = isDown(k.Left); i.Right = isDown(k.Right);
            i.LeanL = isDown(k.LeanLeft); i.LeanR = isDown(k.LeanRight);
            i.Fire = isDown(k.Fire); i.Ads = isDown(k.Ads); i.FreeLook = isDown(k.FreeLook);
            i.Crouch = isDown(k.Crouch); i.Sprint = isDown(k.Sprint); i.Jump = isDown(0x20); i.Prone = isDown(k.Prone);
            return i;
        }

        /// <summary>
        /// 冲刺 state (UNVERIFIED vs the game). 按住: sprint key + 前进 held. 切换: a sprint-key press with 前进 held (and 后退 not)
        /// starts it, another press ends it; it also ends when 前进 is released, 后退 is held, or 开镜 / 开火 / 蹲下 / 趴下 is pressed.
        /// </summary>
        private void UpdateSprint(InputState input, GlobalSettings g)
        {
            if (g.SprintMode == "hold") { sprintOn = input.Sprint && input.Fwd; return; }
            bool keyEdge = input.Sprint && !prev.Sprint;
            if (sprintOn)
            {
                if (keyEdge || !input.Fwd || input.Back || (input.Ads && !prev.Ads) || (input.Fire && !prev.Fire)
                    || (input.Crouch && !prev.Crouch) || (input.Prone && !prev.Prone)) sprintOn = false;
            }
            else if (keyEdge && input.Fwd && !input.Back) sprintOn = true;
        }
        public double SpringX { get { return posX; } }
        public double SpringY { get { return posY; } }

        /// <summary>反向惯性 phase of an axis (0 = 左右, 1 = 前后): PhaseNone / PhaseDecel / PhasePause / PhaseAccel.</summary>
        public int ReversePhase(int axis) { return (revOn && revAxis[axis]) ? PhaseAt(revT) : PhaseNone; }

        private int PhaseAt(double t)
        {
            if (revCfg == null) return PhaseNone;
            double d = revCfg.DecelMs, q = revCfg.PauseMs, a = revCfg.AccelMs;
            if (t < d) return PhaseDecel;
            if (t < d + q) return PhasePause;
            if (t < d + q + a) return PhaseAccel;
            return PhaseNone;
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

        /// <summary>ω of the critically damped follow: the step response 1-(1+ωt)e^(-ωt) reaches 95 % at ωt = 4.744.</summary>
        public static double FollowOmega(Profile p) { return OmegaFor(p.FollowMs); }

        public static double OmegaFor(double ms95)
        {
            if (ms95 <= 0) return double.PositiveInfinity;
            return 4.744 / (ms95 / 1000.0);
        }

        /// <summary>1 - e^(-dt/τ) with τ = ms/3000 s (≈95 % after 'ms'); 1 (instant) when ms ≤ 0.</summary>
        private static double Ease(double dt, double ms)
        {
            if (ms <= 0) return 1.0;
            double tau = ms / 3000.0;
            return 1.0 - Math.Exp(-dt / tau);
        }

        /// <summary>Exact critically damped step toward target (target constant over dt): frame-rate independent, unconditionally stable.</summary>
        private static void Follow(ref double pos, ref double vel, double target, double w, double dt)
        {
            if (double.IsInfinity(w)) { pos = target; vel = 0; return; }
            double e0 = pos - target;
            double c = vel + w * e0;
            double ex = Math.Exp(-w * dt);
            pos = target + (e0 + c * dt) * ex;
            vel = (vel - w * c * dt) * ex;
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
                case PreviewMode.ReverseAD: if (firstHalf) s.Left = true; else s.Right = true; break;
                case PreviewMode.ReverseWS: if (firstHalf) s.Fwd = true; else s.Back = true; break;
                default: break; // Static: nothing
            }
            s.Ads = false;
            return s;
        }

        /// <summary>Spring target (move component) for a direction, honouring an Enabled lean+move combo.</summary>
        private void TargetFor(int dir, Profile p, out double tx, out double ty)
        {
            tx = dir >= 0 ? p.Dirs[dir].OffX : 0.0;
            ty = dir >= 0 ? p.Dirs[dir].OffY : 0.0;
            if (leanTarget == 0 || dir < 0 || p.Combos == null || p.Combos.Count != Profile.ComboCount) return;
            int i = Profile.ComboIndex((int)leanTarget, dir);
            if (i < 0 || p.Combos[i] == null || !p.Combos[i].Enabled) return;
            // Settled: Move + Lean = combo.Off.
            double flx, fly;
            p.FullLean(Profile.ComboLean[i], out flx, out fly);
            tx = p.Combos[i].OffX - flx;
            ty = p.Combos[i].OffY - fly;
        }

        /// <summary>Reversal path at time t for neutral N and new target W (anchor P0 = ring at the trigger).</summary>
        private void Nominal(double t, double nx, double ny, double wx, double wy, out double x, out double y)
        {
            double d = revCfg.DecelMs, q = revCfg.PauseMs, a = revCfg.AccelMs;
            if (t < d)
            {
                double s = ReverseSplit.Ease(revCfg.DecelShape, revCfg.DecelStrength, t / d);
                x = p0x + (nx - p0x) * s; y = p0y + (ny - p0y) * s;
            }
            else if (t < d + q) { x = nx; y = ny; }
            else if (t < d + q + a)
            {
                double s = ReverseSplit.Ease(revCfg.AccelShape, revCfg.AccelStrength, (t - d - q) / a);
                x = nx + (wx - nx) * s; y = ny + (wy - ny) * s;
            }
            else { x = wx; y = wy; }
        }

        /// <summary>d/du of ReverseSplit.Ease.</summary>
        private static double EaseSlope(string shape, double strengthPct, double u)
        {
            if (u < 0 || u > 1) return 0;
            double f;
            switch (shape)
            {
                case "in": f = 2 * u; break;
                case "out": f = 2 * (1 - u); break;
                case "linear": f = 1; break;
                default: f = 6 * u * (1 - u); break;
            }
            double k = Clamp.D(strengthPct, 0, 100, 100) / 100.0;
            return (1 - k) + k * f;
        }

        /// <summary>Velocity (px/s) of the reversal path at time t (0 in the pause and after the end).</summary>
        private void NominalVel(double t, double nx, double ny, double wx, double wy, out double vx, out double vy)
        {
            double d = revCfg.DecelMs, q = revCfg.PauseMs, a = revCfg.AccelMs;
            vx = vy = 0;
            if (t < d)
            {
                double k = EaseSlope(revCfg.DecelShape, revCfg.DecelStrength, t / d) * 1000.0 / d;
                vx = (nx - p0x) * k; vy = (ny - p0y) * k;
            }
            else if (t >= d + q && t < d + q + a)
            {
                double k = EaseSlope(revCfg.AccelShape, revCfg.AccelStrength, (t - d - q) / a) * 1000.0 / a;
                vx = (wx - nx) * k; vy = (wy - ny) * k;
            }
        }

        /// <summary>
        /// Direction + 反向惯性 + spring. Disabled axes / no reversal = exactly the plain 跟随 (v1.3.1 / v2.0-off paths).
        /// </summary>
        private void UpdateMove(int rawF, int rawX, Profile p, double dt)
        {
            double dtMs = dt * 1000.0;
            int[] raw = new int[] { rawX, rawF };
            ReverseAxis[] cfg = new ReverseAxis[] { p.ReverseLR, p.ReverseFB };
            double w = FollowOmega(p);

            // 1. Flip back during 减速 / 停顿 cancels that axis (all axes cancelled -> plain 跟随 from where the ring is).
            if (revOn)
            {
                int ph = PhaseAt(revT);
                if (ph == PhaseDecel || ph == PhasePause)
                {
                    for (int a = 0; a < 2; a++)
                        if (revAxis[a] && raw[a] != 0 && raw[a] == revFrom[a]) revAxis[a] = false;
                    if (!revAxis[0] && !revAxis[1]) { revOn = false; velX = pathVX + cVX; velY = pathVY + cVY; }
                }
            }

            // 2. Triggers: an enabled axis whose key flips to the opposite of the ring's current side.
            bool[] trig = new bool[2];
            bool any = false;
            for (int a = 0; a < 2; a++)
            {
                ReverseAxis c = cfg[a];
                if (c != null && c.Enabled && c.TotalMs > 0 && prevEff[a] != 0 && raw[a] == -prevEff[a]) { trig[a] = true; any = true; }
            }
            if (any)
            {
                // New reversal anchored where the ring is. Axes still on their way to the middle stay in it.
                bool[] keep = new bool[2];
                if (revOn)
                {
                    int ph = PhaseAt(revT);
                    for (int a = 0; a < 2; a++) keep[a] = revAxis[a] && (ph == PhaseDecel || ph == PhasePause);
                }
                ReverseAxis use = null;
                for (int a = 0; a < 2; a++)
                {
                    revAxis[a] = trig[a] || keep[a];
                    if (trig[a]) revFrom[a] = prevEff[a];
                    if (revAxis[a] && cfg[a] != null && cfg[a].Enabled && (use == null || cfg[a].TotalMs > use.TotalMs)) use = cfg[a];
                }
                revCfg = use.Clone();
                revOn = true;
                revT = 0;
                p0x = posX; p0y = posY;
                cX = cY = cVX = cVY = 0;
                pathVX = pathVY = 0;
            }

            // 3. Effective direction: axes on their way to / waiting in the middle count as 0.
            int[] eff = new int[] { raw[0], raw[1] };
            if (revOn)
            {
                int ph = PhaseAt(revT);
                if (ph == PhaseDecel || ph == PhasePause)
                    for (int a = 0; a < 2; a++) if (revAxis[a]) eff[a] = 0;
            }

            if (!revOn)
            {
                curDir = DirFromSigns(eff[1], eff[0]);
                double tx, ty;
                TargetFor(curDir, p, out tx, out ty);
                Follow(ref posX, ref velX, tx, w, dt);
                Follow(ref posY, ref velY, ty, w, dt);
            }
            else
            {
                int nDir = DirFromSigns(revAxis[1] ? 0 : raw[1], revAxis[0] ? 0 : raw[0]);
                int wDir = DirFromSigns(raw[1], raw[0]);
                double nx, ny, wx, wy;
                TargetFor(nDir, p, out nx, out ny);
                TargetFor(wDir, p, out wx, out wy);
                if (any) { lastNx = nx; lastNy = ny; lastWx = wx; lastWy = wy; }
                if (nx != lastNx || ny != lastNy || wx != lastWx || wy != lastWy)
                {
                    // Another key moved the targets: absorb the jump into a correction that decays at 跟随时间.
                    double ox, oy, mx, my;
                    Nominal(revT, lastNx, lastNy, lastWx, lastWy, out ox, out oy);
                    Nominal(revT, nx, ny, wx, wy, out mx, out my);
                    cX += ox - mx; cY += oy - my;
                    lastNx = nx; lastNy = ny; lastWx = wx; lastWy = wy;
                }
                revT += dtMs;
                Follow(ref cX, ref cVX, 0, w, dt);
                Follow(ref cY, ref cVY, 0, w, dt);
                double px, py;
                Nominal(revT, nx, ny, wx, wy, out px, out py);
                posX = px + cX; posY = py + cY;
                NominalVel(revT, nx, ny, wx, wy, out pathVX, out pathVY); // path velocity, px/s (0 once the path has ended)
                int ph = PhaseAt(revT);
                curDir = (ph == PhaseDecel || ph == PhasePause) ? nDir : wDir;
                if (revT >= revCfg.TotalMs)
                {
                    // Done: back to plain 跟随 from exactly where the ring is.
                    revOn = false;
                    revAxis[0] = revAxis[1] = false;
                    velX = pathVX + cVX; velY = pathVY + cVY;
                    eff[0] = raw[0]; eff[1] = raw[1];
                }
                else if (ph == PhaseAccel || ph == PhaseNone) { eff[0] = raw[0]; eff[1] = raw[1]; }
            }
            prevEff[0] = eff[0]; prevEff[1] = eff[1];
        }

        /// <summary>蹲下 tracker (UNVERIFIED vs the game): C toggles; sprinting, jump (Space) and prone (趴下, default Z) stand you up.</summary>
        private void UpdateCrouch(InputState input)
        {
            if (input.Crouch && !prev.Crouch) crouched = !crouched;
            if (sprintOn) crouched = false;
            if (input.Jump && !prev.Jump) crouched = false;
            if (input.Prone && !prev.Prone) crouched = false;
        }

        public void Update(double dt, InputState input, Profile p, GlobalSettings g, PreviewMode pm, double previewSpeed)
        {
            bool preview = pm != PreviewMode.Live;
            if (pm != prevPm) { previewMs = 0; prevPm = pm; }

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

            // Lean target first (an Enabled lean+move combo changes the spring target)
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

            // Direction (+ 反向惯性) -> spring
            int rawX = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
            int rawF = (input.Fwd ? 1 : 0) - (input.Back ? 1 : 0);
            UpdateMove(rawF, rawX, p, dt);

            lean += (leanTarget - lean) * Ease(dt, p.LeanMs);
            double lx, ly;
            if (lean < 0) { lx = -lean * p.LeanQX; ly = -lean * p.LeanQY; }
            else { lx = lean * p.LeanEX; ly = lean * p.LeanEY; }

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

            // 蹲下: tracker + scales eased over ~100 ms so a state change mid-burst never jumps.
            UpdateSprint(input, g);
            UpdateCrouch(input);
            double tMax = 1, tKick = 1, tDrift = 1;
            if (p.CrouchEnabled && crouched)
            {
                if (p.CrouchSplit) { tMax = p.CrouchMaxPct / 100.0; tKick = p.CrouchKickPct / 100.0; tDrift = p.CrouchDriftPct / 100.0; }
                else tMax = tKick = tDrift = p.CrouchPct / 100.0;
            }
            double ks = Ease(dt, 100);
            if (sMax != tMax) sMax += (tMax - sMax) * ks;
            if (sKick != tKick) sKick += (tKick - sKick) * ks;
            if (sDrift != tDrift) sDrift += (tDrift - sDrift) * ks;
            double tMove = (p.CrouchMoveEnabled && crouched) ? p.CrouchMovePct / 100.0 : 1.0;
            if (sMove != tMove) sMove += (tMove - sMove) * ks;

            // 冲刺后恢复: dimmed while sprinting and for SprintRecoveryMs after (eased ~80 ms).
            bool sprinting = sprintOn;
            if (sprinting) sinceSprintMs = 0; else sinceSprintMs += dtMs;
            double tDim = (p.SprintDimEnabled && (sprinting || sinceSprintMs < p.SprintRecoveryMs)) ? p.SprintDimPct / 100.0 : 1.0;
            if (Dim != tDim) Dim += (tDim - Dim) * Ease(dt, 80);

            // Recoil. The click kick and the hold rise start RecoilDelayMs after the press, so at the press frame
            // the ring still shows the residual recoil this bullet really carries. Internal value is unscaled.
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
                    // shown kick = ClickKick x sKick; the internal value is divided by the 最高上抬 scale
                    double kickF = sKick == sMax ? 1.0 : (sMax > 1e-9 ? sKick / sMax : 0.0);
                    recoil += p.RecoilClickKickPx * kickF;
                    kickPending = false;
                }
                if (riseMs > 0) recoil += (cap - recoil) * Ease(riseMs / 1000.0, p.RecoilRiseMs);
                if (recoil < 0) recoil = 0;
                if (recoil > cap) recoil = cap;
                if (pm == PreviewMode.RecoilMax) recoil = cap;
            }
            double rx = cap > 0 ? p.RecoilDriftX * sDrift * recoil / cap : 0.0;
            double ry = recoil * sMax;

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

            // Output (UI coords -> screen y-down, in exactly one place)
            // 分辨率换算 s scales every pixel offset; 蹲着走 scales only the movement part (spring incl. combo move part).
            double rs = g.ResolutionScale;
            double uiX = rs * (posX * sMove + lx + rx + p.StandOffsetX); // 站着偏移 (per gun)
            double uiY = rs * (posY * sMove + ly + ry + p.StandOffsetY);
            OffsetX = uiX;
            OffsetY = -uiY;
            Debug = new MotionDebug(curDir, lean, ry * rs, uiX, uiY, crouched);

            prev = input;
        }
    }
}
