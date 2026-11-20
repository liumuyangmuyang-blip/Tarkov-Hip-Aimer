// TutorialStep10.cs -- 首次设置教程 script step 10 (跳跃与拉枪, the last page). System.Drawing only (no WinForms), C# 5,
// no per-frame allocation. Owner: step worker 10. Registered by TutorialStepsB.AddSteps (reflection: TutorialStep10.Add).
// The pure maths (TutorialStep10Math, TutorialStep10Flow) compiles on its own: tests-v4 defines STEP10_PURE_ONLY and
// compiles only that part (tests in tests-v4/TestsV4Step10.cs).
//
// Demo A 「跳跃」 (示意): first-person, wall fixed at 10 m. The bullets follow the developer's preset jump path (Leo's default
// profile); the ring uses the user's own 跳跃 values. The path is drawn as a trail on the scene and as an offset / time
// plot (预设路线 · 示意 vs 你的数值). A small side view shows the character jumping. Live: press the jump key and fire.
// Demo B 「拉枪」 (v4.1 skeleton peek, layout 示意): 右手 D+E -> 换手 Q+A (mirrored) -> 被敌人拉, top-down corner view +
// first-person view. The green ring's offset is the overlay's MotionEngine output for the user's profile (TutorialStep10Peek).
using System;
using System.Collections.Generic;
#if !STEP10_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>
    /// Pure maths of the 跳跃与拉枪 step (System only, unit-tested). Every number here is 示意 (demo timing / layout),
    /// not measured game data.
    /// </summary>
    public static class TutorialStep10Math
    {
        // ---------------- demo A: jump ----------------
        /// <summary>Scripted jump loop (ms): jump pressed at JumpAt, fire held FireFrom..FireTo.</summary>
        public const int JumpLoopMs = 2600, JumpAt = 350, FireFrom = 300, FireTo = 1300;
        /// <summary>Wall distance of the jump demo (示意).</summary>
        public const double DistanceM = 10;
        /// <summary>Live: shots fired while a jump runs before the jump task counts as done.</summary>
        public const int JumpShotsNeeded = 2;
        /// <summary>Trail samples kept for one jump (60 fps x 2 s).</summary>
        public const int TrailMax = 128;
        /// <summary>Trail fade after the jump ended (ms).</summary>
        public const double TrailFadeMs = 1400;

        // ---------------- demo B: peek ----------------
        /// <summary>Length of one peek scene (ms) and the number of scenes (右手 D+E, 换手 Q+A, 敌人视角).</summary>
        public const int PeekSceneMs = 6400, PeekScenes = 3;
        public const int PeekTotalMs = PeekSceneMs * PeekScenes;
        /// <summary>Keys go down at KeysAt (ms into the scene); the scene fades in / out over FadeMs.</summary>
        public const int KeysAt = 600, FadeMs = 260;
        /// <summary>First shot ReactMs after the target became visible; shots every ShotGapMs for ShotsPerPeek shots.</summary>
        public const int ReactMs = 140, ShotGapMs = 95, ShotsPerPeek = 7;
        /// <summary>Camera stops moving sideways at this shift (m, 示意).</summary>
        public const double MaxCamM = 0.55;
        /// <summary>Peek geometry (m, 示意): the cover edge is EdgeX to the side at CoverZ ahead; the target stands TargetX
        /// to the side at TargetZ.</summary>
        public const double EdgeX = 0.35, CoverZ = 1.0, TargetX = 0.6, TargetZ = 9.0;
        /// <summary>Height of the target (m, 示意) and of the aim point above its feet (chest).</summary>
        public const double TargetH = 1.75, AimH = 1.3;

        /// <summary>Camera sideways shift (m) at which the target appears past the cover edge (pinhole camera looking
        /// straight ahead): (EdgeX * TargetZ - TargetX * CoverZ) / (TargetZ - CoverZ).</summary>
        public static double RevealCamX(double edgeX, double coverZ, double targetX, double targetZ)
        {
            double d = targetZ - coverZ;
            if (!(d > 0)) return 0;
            double c = (edgeX * targetZ - targetX * coverZ) / d;
            return c < 0 ? 0 : c;
        }

        public static double RevealCamX() { return RevealCamX(EdgeX, CoverZ, TargetX, TargetZ); }

        // ---------------- v4.3 (W6): 画面比例 stretch in the first-person view ----------------
        /// <summary>The X stretch of the user's game screen relative to the 调参 screen: ViewScale.FactorX ÷ ViewScale.Factor
        /// (4:3 on a 1920x1080 screen → 1.333; native → exactly 1; missing / unusable settings → 1). The first-person view
        /// draws the scene and the ring this much wider, as the stretched game looks; the % numbers do not change.
        /// UNVERIFIED stretch model (W1, V42-W1.md).</summary>
        public static double ViewStretch(GlobalSettings g)
        {
            if (g == null) return 1.0;
            double k, kx;
            try { k = ViewScale.Factor(g); kx = ViewScale.FactorX(g); }
            catch (Exception) { return 1.0; }
            if (!(k > 0) || !(kx > 0) || double.IsInfinity(k) || double.IsInfinity(kx) || kx == k) return 1.0;
            double r = kx / k;
            return (r > 0.2 && r < 5 && !double.IsNaN(r)) ? r : 1.0;
        }

        /// <summary>v4.3: the contact tag, e.g. 「你只露出 0.15 米 · 准星已偏 ← 4.17%」 (we peek) or 「敌人只露出 0.15 米 · 准星已偏 …」
        /// (scene 2). metres = TutorialStep10Peek.ContactExposureM, ringText = the ring's X offset as the settings pages write it.</summary>
        public static string ExposureText(double metres, string ringText, bool weArePeeking)
        {
            if (double.IsNaN(metres) || metres < 0) metres = 0;
            if (metres > 9.99) metres = 9.99;
            return (weArePeeking ? "你只露出 " : "敌人只露出 ") + metres.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + " 米 · 准星已偏 " + (ringText ?? "");
        }

        /// <summary>The first-person view's tag for a stretched picture: 「画面拉伸 · 左右 ×1.33」 (2 decimals).</summary>
        public static string StretchTagText(double s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s)) s = 1.0;
            return "画面拉伸 · 左右 ×" + s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>x stretched by s about the view centre cx (s = ViewStretch).</summary>
        public static double StretchAbout(double x, double cx, double s)
        {
            return s == 1.0 ? x : cx + (x - cx) * s;
        }

        /// <summary>The ring's X in the first-person view: centre + RingOffX (profile px on the demo / 调参 screen) × the view's
        /// px per 调参 px (k) × the stretch s. With s = 1 this is exactly the v4.1 expression.</summary>
        public static double RingViewX(double vcx, double ringOffX, double k, double s)
        {
            return vcx + ringOffX * k * s;
        }

        /// <summary>Screen offset (px from the centre, toward the peek side +) of a point X to the side at Z ahead, camera
        /// shifted camX, focal f (px at 1 m). Z &lt;= 0 gives 0.</summary>
        public static double ScreenX(double x, double z, double camX, double focal)
        {
            if (!(z > 0)) return 0;
            return (x - camX) * focal / z;
        }

        /// <summary>The target is visible when it is beyond the cover edge on screen.</summary>
        public static bool TargetVisible(double camX)
        {
            return ScreenX(TargetX, TargetZ, camX, 1) > ScreenX(EdgeX, CoverZ, camX, 1);
        }

        /// <summary>Camera shift (m) tau seconds after the keys went down: strafe and/or lean (PeekModel), capped at MaxCamM.</summary>
        public static double CamX(bool strafe, bool lean, double tauS)
        {
            double x = PeekModel.CameraX(strafe, lean, tauS);
            if (double.IsNaN(x) || x < 0) x = 0;
            return x > MaxCamM ? MaxCamM : x;
        }

        /// <summary>Lean amount 0..1 tau seconds after the lean key went down (PeekModel lean shape).</summary>
        public static double Lean01(double tauS)
        {
            if (!(tauS > 0)) return 0;
            return TutorialGeometry.Smooth(tauS / PeekModel.LeanS);
        }

        /// <summary>Time (s) until the target is visible: strafe only, lean only, both (∞ = never within 3 s).</summary>
        public static double ClearTime(bool strafe, bool lean)
        {
            return PeekModel.TimeToClear(strafe, lean, RevealCamX(), 3.0);
        }

        /// <summary>Peek scene index 0..2 at time t (ms into demo B, clamped) and the ms into that scene.</summary>
        public static int PeekScene(double tMs, out double localMs)
        {
            if (double.IsNaN(tMs) || tMs < 0) tMs = 0;
            if (tMs >= PeekTotalMs) tMs = PeekTotalMs - 0.001;
            int i = (int)(tMs / PeekSceneMs);
            if (i < 0) i = 0;
            if (i >= PeekScenes) i = PeekScenes - 1;
            localMs = tMs - i * PeekSceneMs;
            return i;
        }

        /// <summary>Scene fade 0 (black) .. 1 (fully shown) at localMs.</summary>
        public static double SceneFade(double localMs)
        {
            if (double.IsNaN(localMs)) return 0;
            double a = localMs / FadeMs, b = (PeekSceneMs - localMs) / FadeMs;
            double v = Math.Min(a, b);
            if (v < 0) v = 0;
            return v > 1 ? 1 : v;
        }

        /// <summary>Shots fired up to localMs in a D+E peek scene (0 before the first one).</summary>
        public static int ShotsAt(double localMs)
        {
            double first = KeysAt + ClearTime(true, true) * 1000.0 + ReactMs;
            if (double.IsInfinity(first) || double.IsNaN(localMs) || localMs < first) return 0;
            int n = 1 + (int)((localMs - first) / ShotGapMs);
            return n > ShotsPerPeek ? ShotsPerPeek : n;
        }

        /// <summary>ms since the last shot at localMs (∞ when none yet).</summary>
        public static double SinceShot(double localMs)
        {
            int n = ShotsAt(localMs);
            if (n <= 0) return double.PositiveInfinity;
            double first = KeysAt + ClearTime(true, true) * 1000.0 + ReactMs;
            return localMs - (first + (n - 1) * ShotGapMs);
        }

        /// <summary>Wall height (m) whose top edge sits halfPx above the centre at zoom zk (scene px per UI px) and distM;
        /// at least 1 m.</summary>
        public static double WallHeightM(double halfPx, double zk, double distM)
        {
            if (!(zk > 0) || !(distM > 0) || !(halfPx > 0)) return 1;
            double m = 2 * halfPx / zk * distM / TutorialGeometry.FocalPx;
            return m < 1 ? 1 : m;
        }

        /// <summary>Airborne height 0..1 of the side-view figure tMs into a jump of totalMs (half sine, 示意).</summary>
        public static double AirHeight01(double tMs, double totalMs)
        {
            if (!(totalMs > 0) || !(tMs > 0) || tMs >= totalMs) return 0;
            return Math.Sin(Math.PI * tMs / totalMs);
        }

        /// <summary>Largest |end px| of the first two jump phases of two paths (disabled paths count 0) plus margin,
        /// at least minExtent. NaN / infinity count as 0.</summary>
        public static double JumpExtent(bool aOn, double a0, double a1, bool bOn, double b0, double b1, double margin, double minExtent)
        {
            double m = 0;
            if (aOn) { m = Math.Max(m, Fin(Math.Abs(a0))); m = Math.Max(m, Fin(Math.Abs(a1))); }
            if (bOn) { m = Math.Max(m, Fin(Math.Abs(b0))); m = Math.Max(m, Fin(Math.Abs(b1))); }
            m += Fin(margin);
            return m < minExtent ? minExtent : m;
        }

        /// <summary>Scene px per UI px so that extentUi fills 'fill' of halfPx; k = DPI scale. Clamped 0.05 .. 40.</summary>
        public static double FitZoom(double halfPx, double k, double extentUi, double fill)
        {
            if (!(k > 0)) k = 1;
            if (!(halfPx > 0)) return 0.05;
            if (!(fill > 0)) fill = 1;
            double e = Fin(Math.Abs(extentUi));
            if (!(e > 0)) e = 1;
            double z = halfPx * fill / (e * k);
            if (z < 0.05) z = 0.05;
            if (z > 40) z = 40;
            return z;
        }

        /// <summary>Plot y (px, y down) of value in [lo, hi] inside [top, top + height]; clamped to the box.</summary>
        public static float PlotY(double value, double lo, double hi, float top, float height)
        {
            if (!(hi > lo)) { hi = lo + 1; }
            double u = (Fin(value) - lo) / (hi - lo);
            if (u < 0) u = 0;
            if (u > 1) u = 1;
            return (float)(top + height - u * height);
        }

        private static double Fin(double v) { return (double.IsNaN(v) || double.IsInfinity(v)) ? 0 : v; }

        /// <summary>「≈0.16 s」 (two decimals, 示意); 「—」 for never / NaN. Allocates: call once, not per frame.</summary>
        public static string SecondsText(double s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 0) return "—";
            return "≈" + s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";
        }
    }

    /// <summary>
    /// Page flow of step 10 (pure): demo A (jump) until the user did the jump task and paused, or after JumpIdleMs without
    /// live input; then demo B (peek: 3 skeleton scenes) loops smoothly until the user picks A or jumps / fires live.
    /// Prompts 0 → 1 (demo B started) → 2 (demo B watched once through; the step is Done). Demo B can be paused, replayed
    /// from scene 1, or jumped to one scene (v4.1).
    /// </summary>
    public sealed class TutorialStep10Flow
    {
        public const int ModeJump = 0, ModePeek = 1;
        /// <summary>Demo A without live input for this long (ms) -> demo B.</summary>
        public const double JumpIdleMs = 5200;
        /// <summary>After the live jump task: this long without live input (ms) -> demo B.</summary>
        public const double AfterTaskMs = 1200;

        public int Mode;
        public double ModeMs, PeekMs, IdleMs;
        public int Prompt;
        public bool Done, JumpTaskDone, PeekWatched;
        /// <summary>Demo B is paused (PeekMs holds still; live jump / fire still switches to demo A).</summary>
        public bool Paused;
        public int LiveJumpShots;
        /// <summary>True on the tick demo B finished for the first time (the step pushes its terminal line then).</summary>
        public bool JustWatched;
        /// <summary>True on the tick the mode changed (the step clears its demo state).</summary>
        public bool ModeChanged;

        public void Reset()
        {
            Mode = ModeJump; ModeMs = 0; PeekMs = 0; IdleMs = 0; Prompt = 0;
            Done = false; JumpTaskDone = false; PeekWatched = false; LiveJumpShots = 0; JustWatched = false; ModeChanged = false;
            Paused = false;
        }

        /// <summary>Demo B from scene 0..2 (chip). Switches to demo B when needed; does not change Paused.</summary>
        public void SelectScene(int scene)
        {
            if (scene < 0 || scene >= TutorialStep10Math.PeekScenes) return;
            if (Mode != ModePeek) SetMode(ModePeek);
            if (Prompt < 1) Prompt = 1;
            PeekMs = scene * (double)TutorialStep10Math.PeekSceneMs;
        }

        /// <summary>Replay demo B from scene 1 and play.</summary>
        public void Replay()
        {
            if (Mode != ModePeek) SetMode(ModePeek);
            if (Prompt < 1) Prompt = 1;
            PeekMs = 0; Paused = false;
        }

        public void TogglePause() { Paused = !Paused; }

        /// <summary>Manual pick (chip).</summary>
        public void Select(int mode)
        {
            if (mode != ModeJump && mode != ModePeek) return;
            SetMode(mode);
            if (mode == ModePeek && Prompt < 1) Prompt = 1;
        }

        private void SetMode(int m)
        {
            Mode = m; ModeMs = 0; PeekMs = 0; IdleMs = 0; ModeChanged = true;
        }

        /// <summary>One frame. live = any live key / click this frame; jumpShots = shots fired this frame while a jump
        /// runs (live only); wantJump = live jump key or fire this frame.</summary>
        public void Tick(double dtMs, bool live, int jumpShots, bool wantJump)
        {
            JustWatched = false; ModeChanged = false;
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            if (dtMs > 100) dtMs = 100;
            ModeMs += dtMs;
            if (live) IdleMs = 0; else IdleMs += dtMs;
            if (Mode == ModeJump)
            {
                if (live && jumpShots > 0) LiveJumpShots += jumpShots;
                if (!JumpTaskDone && LiveJumpShots >= TutorialStep10Math.JumpShotsNeeded) JumpTaskDone = true;
                bool go = IdleMs >= JumpIdleMs || (JumpTaskDone && IdleMs >= AfterTaskMs && LiveJumpShots > 0 && ModeMs >= AfterTaskMs);
                if (go)
                {
                    SetMode(ModePeek);
                    if (Prompt < 1) Prompt = 1;
                }
                return;
            }
            // demo B: loops (scene 1 -> 2 -> 3 -> 1 ...); done after the first full pass
            if (wantJump) { SetMode(ModeJump); Paused = false; return; }
            if (Paused) return;
            PeekMs += dtMs;
            if (PeekMs >= TutorialStep10Math.PeekTotalMs)
            {
                if (!PeekWatched) JustWatched = true;
                PeekWatched = true; Done = true; Prompt = 2;
                PeekMs -= TutorialStep10Math.PeekTotalMs;
                if (!(PeekMs >= 0) || PeekMs >= TutorialStep10Math.PeekTotalMs) PeekMs = 0;
            }
        }
    }

    /// <summary>
    /// Skeleton peek demo (v4.1, pure, System only; uses the shared SkelRig / SkelMotion and the overlay's MotionEngine).
    /// Layout sizes are 示意; the green ring is NOT: its offset from the screen centre is the overlay's own MotionEngine output
    /// for the user's profile (Configure) and the scene's keys (D = 向右走 -> 落点偏左, A -> 偏右, E / Q add the lean
    /// offsets, 换手 uses the mirrored values), converted to an angle through the game screen's focal length (ViewScale).
    /// We aim with the mouse so that the RING (where the bullets go), not the screen centre, sits on the 预瞄点; the aim
    /// follows with a lag (TauMs), so the ring is visibly 'pulled' off the 预瞄点 while the body accelerates.
    /// Scene 0 「右手 D+E」: wall on our left, open side right; we strafe right past the corner and lean E (camera roll
    /// clockwise, rifle on the right shoulder). Scene 1 「换手 Q+A」: the exact mirror (strafe left, lean Q, roll
    /// counter-clockwise, rifle on the left shoulder, wall on our right). Scene 2 「被敌人拉」: we hold still on the corner
    /// edge; the enemy steps and leans out of its cover toward the open side; its head appears on our 预瞄点.
    /// Motion after the keys runs in slow motion (Slow). Fixed StepMs steps: the state at a given LocalMs does not depend on
    /// the frame rate (AdvanceTo / SeekKeyFrame are deterministic).
    /// </summary>
    public sealed class TutorialStep10Peek
    {
        public const int SceneRight = 0, SceneLeft = 1, SceneFoe = 2, Scenes = 3;
        public const double SceneMs = TutorialStep10Math.PeekSceneMs;
        /// <summary>Keys go down (scenes 0-1: ours, scene 2: the enemy's) this long into the scene (ms, wall clock).</summary>
        public const double KeysAt = 900;
        /// <summary>Slow motion after the keys: game time = wall time x Slow (慢放 示意).</summary>
        public const double Slow = 0.3;
        /// <summary>Fixed simulation step (ms, wall clock).</summary>
        public const double StepMs = 8;
        /// <summary>Aim follow lag (game ms): the visible 'pull' of the ring off the 预瞄点.</summary>
        public const double TauMs = 90;
        /// <summary>First shot ReactMs (game) after contact, then one every ShotGapMs, ShotsMax shots.</summary>
        public const double ReactMs = 140, ShotGapMs = 95;
        public const int ShotsMax = 5;
        /// <summary>Strafe stops after this many metres (the lean stays held): from behind the wall to past the corner.</summary>
        public const double MaxStrafeM = 0.5;
        /// <summary>Lag (rad) above which the ring counts as 'pulled', and below which it counts as settled.</summary>
        public const double PullRad = 0.012, SettleRad = 0.004;
        /// <summary>Cover height (m) for drawing.</summary>
        public const double CoverH = 2.3;
        /// <summary>Focal length (px) used when the settings give no usable one (2160x1440, vertical FOV 75).</summary>
        public const double FallbackFocalPx = 938.3;

        // scene tables (world m: X right, Z forward; 示意 layout). Scene 1 = scene 0 mirrored in X.
        // v4.3 (Leo, 17 Nov 2026): the enemy stands about 7-7.5 m away (a typical close fight in game), so the view shows how
        // little of the peeking body is visible when the lines of sight meet, while the ring is already offset. Scene 2: the
        // cover is the ENEMY's (just in front of it), we hold still 7 m away.
        public static readonly double[] CovX0 = { -1.7, 0.0, 0.0 }, CovX1 = { 0.0, 1.7, 1.7 };
        public static readonly double[] CovZ0 = { 1.4, 1.4, 6.0 }, CovZ1 = { 1.7, 1.7, 6.3 };
        /// <summary>X of the cover's open end (the corner the 预瞄点 is held on).</summary>
        public static readonly double[] OpenX = { 0.0, 0.0, 0.0 };
        /// <summary>Which side of the line (our eye -> enemy head) the cover is on: -1 left, +1 right (SkelMotion.ClearOfCorner).</summary>
        public static readonly int[] CoverSide = { -1, 1, 1 };
        public static readonly double[] MeX = { -0.55, 0.55, -0.8 }, MeZ = { 0, 0, 0 };
        /// <summary>Our peek: +1 right (D+E), -1 left (换手 Q+A), 0 = we hold still.</summary>
        public static readonly int[] MeSide = { 1, -1, 0 };
        public static readonly double[] FoeX = { 0.35, -0.35, 0.6 }, FoeZ = { 7.5, 7.5, 7.0 };
        /// <summary>The enemy's peek (its own right = +1; it faces us, so +1 = world -X = the open side), 0 = it stands still.</summary>
        public static readonly int[] FoeSide = { 0, 0, 1 };

        public int Scene;
        public double LocalMs;
        public readonly SkelRig Me = new SkelRig(), Foe = new SkelRig();
        public SkelPose MeP, FoeP;
        /// <summary>The overlay's motion engine, fed the scene's keys with the user's profile (game time).</summary>
        public readonly MotionEngine Eng = new MotionEngine();
        public Profile Prof;
        public GlobalSettings Gs;
        /// <summary>Game screen focal length (px) the profile offsets are measured on.</summary>
        public double GameFocalPx = FallbackFocalPx;
        /// <summary>Ring offset from the screen centre right now: MotionEngine.OffsetX / OffsetY (game px, x right, y down).</summary>
        public double RingOffX, RingOffY;
        /// <summary>Our view (screen centre) direction as world yaw / pitch; the enemy's view direction.</summary>
        public double AimYaw, AimPitch, FoeYaw, FoePitch;
        /// <summary>World yaw / pitch of the ring (where the bullets go) right now.</summary>
        public double RingYaw, RingPitch;
        /// <summary>Where we want the ring: the 预瞄点 on the corner edge, then the enemy's head after contact.</summary>
        public double PreX, PreY, PreZ;
        /// <summary>LocalMs of first line of sight (contact), -1 before.</summary>
        public double ContactMs = -1;
        public bool Visible;
        public int Shots;
        public double LastShotMs = -1;
        public readonly double[] HitX = new double[ShotsMax], HitY = new double[ShotsMax], HitZ = new double[ShotsMax];
        /// <summary>Angle (rad) between where the bullets go (the ring) and the 预瞄点 / head right now.</summary>
        public double LagRad, MaxLagRad;
        /// <summary>v4.3: how far (m, sideways) the PEEKING body (us in scenes 0-1, the enemy in scene 2) sticks out past the
        /// cover edge as the other one sees it, right now (0 = fully hidden), and that value / the ring's X offset (profile px)
        /// at first contact (-1 / 0 before contact).</summary>
        public double ExposureM, ContactExposureM = -1, ContactRingOffX;
        private double meYawBody;
        private bool init;

        /// <summary>Uses this profile and these settings (callers pass copies; null = 「Leo默认」 / defaults). Restart after.</summary>
        public void Configure(Profile p, GlobalSettings g)
        {
            Prof = p; Gs = g;
            EnsureConfig();
            double f = 0;
            try { f = ViewScale.FocalPx(Gs.ResGameW, Gs.ResGameH, Gs.ResAspect, Gs.ResFov, Gs.ResFovType); }
            catch (Exception) { f = 0; }
            GameFocalPx = (f > 10 && f < 1e6) ? f : FallbackFocalPx;
        }

        private void EnsureConfig()
        {
            if (Prof == null) Prof = Profile.CreateLeoDefault("演示");
            if (Gs == null)
            {
                Gs = new GlobalSettings();
                Gs.OnlyWhenGameFocused = false; Gs.ShowFadeMs = 0; Gs.HideFadeMs = 0; Gs.AdsMode = "hold"; Gs.HideOnFreeLook = false;
                Gs.BoostEnabled = false; Gs.HandEnabled = true; Gs.Enabled = true; Gs.EditHand = 0;
                Gs.ResGameW = Gs.ResBaseW; Gs.ResGameH = Gs.ResBaseH;
                GameFocalPx = FallbackFocalPx;
            }
            Gs.Enabled = true; Gs.HandEnabled = true; Gs.OnlyWhenGameFocused = false;
        }

        /// <summary>The engine input for scene s now (our keys after KeysAt; 换手 held in 按住 mode).</summary>
        private InputState Keys(int s, bool down)
        {
            InputState i = new InputState();
            int side = MeSide[s];
            if (down && side > 0) { i.Right = true; i.LeanR = true; }
            if (down && side < 0) { i.Left = true; i.LeanL = true; }
            if (side < 0 && Gs != null && Gs.HandMode == "hold") i.HandSwitch = true;
            return i;
        }

        /// <summary>Restart scene s (clamped 0..2) at LocalMs 0.</summary>
        public void Start(int s)
        {
            if (s < 0) s = 0;
            if (s >= Scenes) s = Scenes - 1;
            EnsureConfig();
            Scene = s; LocalMs = 0; ContactMs = -1; Visible = false; Shots = 0; LastShotMs = -1; LagRad = 0; MaxLagRad = 0;
            ExposureM = 0; ContactExposureM = -1; ContactRingOffX = 0;
            init = false;
            meYawBody = MeSide[s] == 0 ? SkelMotion.YawTo(MeX[s], MeZ[s], OpenX[s], CovZ0[s]) : 0;
            MeP = SkelPose.Standing(MeX[s], MeZ[s], meYawBody);
            FoeP = SkelPose.Standing(FoeX[s], FoeZ[s], Math.PI);
            // the engine: fresh, and already switched to 换手 (fully blended) in scene 1
            Eng.Reset();
            Eng.GameFocused = true;
            InputState idle = Keys(s, false);
            if (MeSide[s] < 0 && Gs.HandMode != "hold")
            {
                InputState tap = idle; tap.HandSwitch = true;
                Eng.Update(0.016, idle, Prof, Gs, PreviewMode.Live, 1.0);
                Eng.Update(0.016, tap, Prof, Gs, PreviewMode.Live, 1.0);
            }
            int n = 2 + (int)Math.Ceiling(Math.Max(0, Math.Min(5000, Prof.HandSwitchMs + Prof.LeanMs)) / 50.0);
            for (int k = 0; k < n; k++) Eng.Update(0.05, idle, Prof, Gs, PreviewMode.Live, 1.0);
            Step(0);
        }

        /// <summary>Wall ms since the keys went down x Slow, in seconds of game time (0 before).</summary>
        public double GameS { get { return GameSAt(LocalMs); } }

        public static double GameSAt(double localMs)
        {
            if (!(localMs > KeysAt)) return 0;
            return (localMs - KeysAt) * Slow / 1000.0;
        }

        /// <summary>Game seconds from the keys to contact (示意), -1 before contact.</summary>
        public double ContactGameS { get { return ContactMs < 0 ? -1 : GameSAt(ContactMs); } }

        /// <summary>True while our scripted keys (scenes 0-1) or the enemy's (scene 2) are held.</summary>
        public bool KeysDown { get { return LocalMs >= KeysAt; } }

        /// <summary>Steps (fixed StepMs) until LocalMs reaches localMs (clamped to the scene). Never goes back.</summary>
        public void AdvanceTo(double localMs)
        {
            if (double.IsNaN(localMs)) return;
            if (localMs > SceneMs) localMs = SceneMs;
            int guard = 0;
            while (LocalMs + StepMs <= localMs + 1e-9 && guard++ < 2000) Step(StepMs);
        }

        /// <summary>Reduced motion: the still frame of scene s = the second shot after contact (the pull and the hit both
        /// visible), or the scene end when that never happens.</summary>
        public void SeekKeyFrame(int s)
        {
            Start(s);
            int guard = 0;
            while (Shots < 2 && LocalMs + StepMs <= SceneMs && guard++ < 2000) Step(StepMs);
        }

        /// <summary>Corner-edge X at depth zd seen from (ex, ez) through the open end (ox, z0 / z1) of the cover: the line
        /// past which a point becomes visible. coverSide -1 (cover left): the larger of the two corner lines; +1: the smaller.</summary>
        public static double EdgeAt(double ex, double ez, double ox, double z0, double z1, int coverSide, double zd)
        {
            double a = LineX(ex, ez, ox, z0, zd), b = LineX(ex, ez, ox, z1, zd);
            return coverSide < 0 ? Math.Max(a, b) : Math.Min(a, b);
        }

        private static double LineX(double ex, double ez, double ox, double cz, double zd)
        {
            double d = cz - ez;
            if (!(Math.Abs(d) > 1e-9)) return ox;
            return ex + (ox - ex) * (zd - ez) / d;
        }

        /// <summary>v4.3: sideways metres the peeker's body (all joints except the gun) sticks out past the cover edge as seen
        /// from the viewer's eye: scenes 0-1 the peeker is us (viewer = the enemy), scene 2 the enemy (viewer = us). The open
        /// side is -CoverSide. 0 when fully hidden.</summary>
        public static double Exposure(int s, SkelRig me, SkelRig foe)
        {
            if (s < 0 || s >= Scenes || me == null || foe == null) return 0;
            SkelRig peeker = MeSide[s] != 0 ? me : foe, viewer = MeSide[s] != 0 ? foe : me;
            double vx = viewer.X[SkelJ.Eye], vz = viewer.Z[SkelJ.Eye];
            int side = -CoverSide[s];
            double m = 0;
            for (int j = 0; j < SkelJ.Stock; j++)
            {
                double jx = peeker.X[j], jz = peeker.Z[j];
                if (double.IsNaN(jx) || double.IsNaN(jz)) continue;
                double e = EdgeAt(vx, vz, OpenX[s], CovZ0[s], CovZ1[s], CoverSide[s], jz);
                double d = side * (jx - e);
                if (d > m) m = d;
            }
            return m > 5 ? 5 : m;
        }

        /// <summary>Line of sight from (ex, ez) to (tx, tz) passes both corners of the open end on the open side.</summary>
        public static bool LineClear(int s, double ex, double ez, double tx, double tz)
        {
            return SkelMotion.ClearOfCorner(ex, ez, tx, tz, OpenX[s], CovZ0[s], CoverSide[s])
                && SkelMotion.ClearOfCorner(ex, ez, tx, tz, OpenX[s], CovZ1[s], CoverSide[s]);
        }

        /// <summary>Screen offset (px, x right, y down) -> unit world direction from a first-person camera looking along
        /// (viewYaw, viewPitch) with screen roll 'roll' (+ = clockwise, SkelCamera's Persp convention) and focal f px.
        /// Exact inverse of SkelCamera.Project for that camera.</summary>
        public static void ScreenDir(double offX, double offYDown, double f, double viewYaw, double viewPitch, double roll,
            out double dx, out double dy, out double dz)
        {
            if (!(f > 1e-6)) f = FallbackFocalPx;
            // undo the roll (Project: screen = R(roll) * plane)
            double cr = Math.Cos(roll), sr = Math.Sin(roll);
            double px = offX * cr + offYDown * sr, py = -offX * sr + offYDown * cr;
            double u = px / f, v2 = -py / f, w2 = 1;
            double cp = Math.Cos(viewPitch), sp = Math.Sin(viewPitch);
            double v = v2 * cp + w2 * sp, w = -v2 * sp + w2 * cp;
            double c = Math.Cos(viewYaw), s = Math.Sin(viewYaw);
            dx = u * c + w * s; dz = -u * s + w * c; dy = v;
            double n = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(n > 1e-12)) { dx = 0; dy = 0; dz = 1; return; }
            dx /= n; dy /= n; dz /= n;
        }

        /// <summary>Point on the ring's line (from our eye) at horizontal distance distM.</summary>
        public void RingPoint(double distM, out double x, out double y, out double z)
        {
            double ex = Me.X[SkelJ.Eye], ey = Me.Y[SkelJ.Eye], ez = Me.Z[SkelJ.Eye];
            double cp = Math.Cos(RingPitch);
            double k = cp > 1e-6 ? distM / cp : distM;
            x = Fin(ex + Math.Sin(RingYaw) * cp * k);
            y = Fin(ey + Math.Sin(RingPitch) * k);
            z = Fin(ez + Math.Cos(RingYaw) * cp * k);
        }

        private static double Fin(double v) { return (double.IsNaN(v) || double.IsInfinity(v)) ? 0 : v; }

        /// <summary>Ring world yaw / pitch for the current view (Me solved) and engine offset.</summary>
        private void UpdateRingDir()
        {
            double dx, dy, dz;
            ScreenDir(RingOffX, RingOffY, GameFocalPx, AimYaw, AimPitch, Me.Roll, out dx, out dy, out dz);
            RingYaw = Math.Atan2(dx, dz);
            RingPitch = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
            if (double.IsNaN(RingYaw) || double.IsInfinity(RingYaw)) RingYaw = AimYaw;
            if (double.IsNaN(RingPitch) || double.IsInfinity(RingPitch)) RingPitch = AimPitch;
        }

        private void Step(double dt)
        {
            int s = Scene;
            LocalMs += dt;
            double g = GameS;
            double gdt = LocalMs > KeysAt ? dt * Slow : dt;
            // the overlay's motion engine (game time): our keys with the user's profile
            Eng.Update(gdt / 1000.0, Keys(s, KeysDown), Prof, Gs, PreviewMode.Live, 1.0);
            RingOffX = Fin(Eng.OffsetX); RingOffY = Fin(Eng.OffsetY);
            // poses
            if (MeSide[s] != 0) SkelMotion.PeekPose(ref MeP, MeX[s], MeZ[s], 0, MeSide[s], g, true, true, MaxStrafeM);
            else { MeP.X = MeX[s]; MeP.Z = MeZ[s]; MeP.Yaw = meYawBody; MeP.Lean = 0; MeP.Hand = 0; MeP.Stride = 0; }
            if (FoeSide[s] != 0) SkelMotion.PeekPose(ref FoeP, FoeX[s], FoeZ[s], Math.PI, FoeSide[s], g, true, true, MaxStrafeM);
            else { FoeP.X = FoeX[s]; FoeP.Z = FoeZ[s]; FoeP.Yaw = Math.PI; FoeP.Lean = 0; FoeP.Hand = 0; FoeP.Stride = 0; }
            FoeP.AimYaw = SkelMotion.Wrap(FoeYaw - FoeP.Yaw); FoeP.AimPitch = FoePitch;
            Foe.Solve(FoeP);
            MeP.AimYaw = SkelMotion.Wrap(AimYaw - MeP.Yaw); MeP.AimPitch = AimPitch;
            Me.Solve(MeP);
            double ex = Me.X[SkelJ.Eye], ey = Me.Y[SkelJ.Eye], ez = Me.Z[SkelJ.Eye];
            double hx = Foe.X[SkelJ.Head], hy = Foe.Y[SkelJ.Head], hz = Foe.Z[SkelJ.Head];
            Visible = LineClear(s, ex, ez, hx, hz);
            if (Visible && ContactMs < 0) ContactMs = LocalMs;
            if (ContactMs >= 0) { PreX = hx; PreY = hy; PreZ = hz; }
            else { PreX = EdgeAt(ex, ez, OpenX[s], CovZ0[s], CovZ1[s], CoverSide[s], hz); PreY = hy; PreZ = hz; }
            // bearing of the 预瞄点 from the eye; the view goes where it puts the RING on it (offset from the profile)
            double qx = PreX - ex, qy = PreY - ey, qz = PreZ - ez;
            double bYaw = Math.Atan2(qx, qz), bPitch = Math.Atan2(qy, Math.Sqrt(qx * qx + qz * qz));
            UpdateRingDir();
            double offYaw = SkelMotion.Wrap(RingYaw - AimYaw), offPitch = RingPitch - AimPitch;
            double ty = bYaw - offYaw, tp = bPitch - offPitch;
            if (!init)
            {
                // settle the view so the ring starts exactly on the 预瞄点 (a few fixed-point passes)
                for (int k = 0; k < 4; k++)
                {
                    AimYaw = ty; AimPitch = tp;
                    MeP.AimYaw = SkelMotion.Wrap(AimYaw - MeP.Yaw); MeP.AimPitch = AimPitch; Me.Solve(MeP);
                    UpdateRingDir();
                    offYaw = SkelMotion.Wrap(RingYaw - AimYaw); offPitch = RingPitch - AimPitch;
                    ty = bYaw - offYaw; tp = bPitch - offPitch;
                }
            }
            if (!init || !(LocalMs > KeysAt)) { AimYaw = ty; AimPitch = tp; }
            else
            {
                AimYaw = SkelMotion.Follow(AimYaw, AimYaw + SkelMotion.Wrap(ty - AimYaw), gdt, TauMs);
                AimPitch = SkelMotion.Follow(AimPitch, tp, gdt, TauMs);
            }
            MeP.AimYaw = SkelMotion.Wrap(AimYaw - MeP.Yaw); MeP.AimPitch = AimPitch;
            Me.Solve(MeP);
            UpdateRingDir();
            double ly = SkelMotion.Wrap(bYaw - RingYaw), lp = bPitch - RingPitch;
            LagRad = Math.Sqrt(ly * ly + lp * lp);
            if (double.IsNaN(LagRad)) LagRad = 0;
            if (LagRad > MaxLagRad) MaxLagRad = LagRad;
            // the enemy looks at our head (it lags too while it moves)
            double mx = Me.X[SkelJ.Head] - Foe.X[SkelJ.Eye], my = Me.Y[SkelJ.Head] - Foe.Y[SkelJ.Eye], mz = Me.Z[SkelJ.Head] - Foe.Z[SkelJ.Eye];
            double fy = Math.Atan2(mx, mz), fp = Math.Atan2(my, Math.Sqrt(mx * mx + mz * mz));
            if (!init || !(LocalMs > KeysAt)) { FoeYaw = fy; FoePitch = fp; }
            else
            {
                // unwrap the target next to the current yaw (the enemy faces -Z: yaw near ±π)
                double t = FoeYaw + SkelMotion.Wrap(fy - FoeYaw);
                FoeYaw = SkelMotion.Follow(FoeYaw, t, gdt, TauMs);
                FoePitch = SkelMotion.Follow(FoePitch, fp, gdt, TauMs);
            }
            FoeP.AimYaw = SkelMotion.Wrap(FoeYaw - FoeP.Yaw); FoeP.AimPitch = FoePitch;
            Foe.Solve(FoeP);
            init = true;
            ExposureM = Exposure(s, Me, Foe);
            if (ContactMs >= 0 && ContactExposureM < 0) { ContactExposureM = ExposureM; ContactRingOffX = RingOffX; }
            // our shots after contact: where the ring is, at the enemy's distance
            if (ContactMs >= 0)
            {
                double gc = (LocalMs - ContactMs) * Slow;
                int n = gc < ReactMs ? 0 : 1 + (int)((gc - ReactMs) / ShotGapMs);
                if (n > ShotsMax) n = ShotsMax;
                while (Shots < n)
                {
                    double d = Math.Sqrt(qx * qx + qz * qz);
                    double bx, by, bz;
                    RingPoint(Fin(d), out bx, out by, out bz);
                    HitX[Shots] = bx; HitY[Shots] = by; HitZ[Shots] = bz;
                    Shots++; LastShotMs = LocalMs;
                }
            }
        }

        public const int PhaseHold = 0, PhasePulled = 1, PhaseTrack = 2, PhaseContact = 3, PhaseSettled = 4;

        /// <summary>Phase shown to the user: PhaseHold 预瞄墙角 (before the keys), PhaseContact 接触 (first 600 ms wall after
        /// contact), PhasePulled 被拉动 (lag above PullRad), PhaseTrack 跟住墙角 (keys down, no contact yet), PhaseSettled
        /// 稳定在目标上 (after contact, lag at most PullRad).</summary>
        public int Phase()
        {
            if (!KeysDown) return PhaseHold;
            if (ContactMs >= 0 && LocalMs - ContactMs < 600) return PhaseContact;
            if (LagRad > PullRad) return PhasePulled;
            return ContactMs < 0 ? PhaseTrack : PhaseSettled;
        }
    }

#if !STEP10_PURE_ONLY
    public static class TutorialStep10
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order (the last page of the tutorial).</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep10Page());
        }
    }

    /// <summary>Script step 10 「跳跃与拉枪」 (last page: the frame's primary button is 「完成，去精调」).</summary>
    internal sealed class TutorialStep10Page : TutorialStep
    {
        private static readonly TutorialScript Script = new TutorialScript(TutorialStep10Math.JumpLoopMs,
            new ScriptSeg(TutorialStep10Math.JumpAt, TutorialStep10Math.JumpAt + 200, TKey.Jump),
            new ScriptSeg(TutorialStep10Math.FireFrom, TutorialStep10Math.FireTo, TKey.Fire));

        private static bool JumpOk(Profile p)
        {
            return p != null && p.Jump != null && p.Jump.Phases != null && p.Jump.Phases.Count >= JumpSettings.PhaseCount;
        }

        private static readonly TutorialField[] JumpFields = MakeFields();

        private static TutorialField[] MakeFields()
        {
            TutorialField on = TutorialField.Check("跳跃时准星跟着动", "可选",
                delegate(Profile p) { return p != null && p.Jump != null && p.Jump.Enabled; },
                delegate(Profile p, bool v) { if (p == null) return; if (p.Jump == null) p.Jump = new JumpSettings(); p.Jump.Enabled = v; });
            Func<Profile, bool> enabled = delegate(Profile p) { return JumpOk(p) && p.Jump.Enabled; };
            TutorialField down = TutorialField.Offset("1. 起跳后先往下移多少", "可选", true, OffsetAxis.Y, -JumpSettings.LimitPx, JumpSettings.LimitPx,
                delegate(Profile p) { return JumpOk(p) ? p.Jump.Phases[0].EndPx : 0; },
                delegate(Profile p, double v) { if (JumpOk(p)) p.Jump.Phases[0].EndPx = v; });
            down.Enabled = enabled; down.ZeroSign = -1; // 百分比偏移 (2 decimals, spin 0.10 %); at 0 the arrow points down
            TutorialField up = TutorialField.Offset("2. 然后往上移到多少", "可选", false, OffsetAxis.Y, -JumpSettings.LimitPx, JumpSettings.LimitPx,
                delegate(Profile p) { return JumpOk(p) ? p.Jump.Phases[1].EndPx : 0; },
                delegate(Profile p, double v) { if (JumpOk(p)) p.Jump.Phases[1].EndPx = v; });
            up.Enabled = enabled; up.Optional = true;
            TutorialField total = TutorialField.Number("总时长", "可选", false, JumpSettings.MinTotalMs, JumpSettings.MaxTotalMs, "ms",
                delegate(Profile p) { return JumpOk(p) ? p.Jump.TotalMs : 700; },
                delegate(Profile p, double v) { if (JumpOk(p)) JumpSplit.SetTotal(p.Jump, (int)Math.Round(v)); });
            total.Enabled = enabled; total.Optional = true; total.Increment = 10; total.Decimals = 0;
            return new TutorialField[] { on, down, up, total };
        }

        // HUD text (constants: no per-frame strings)
        private const string WallLabel = "10 米";
        private const string TagPreset = "预设路线";
        private const string TagYours = "准星：你的数值";
        private const string TagYoursOff = "你的跳跃偏移：已关闭";
        private const string TagJumpNow = "起跳 …";
        private const string ChipJump = "A · 跳跃", ChipPeek = "B · 拉枪";
        private const string PlotTitle = "跳跃偏移 / 时间";
        private const string PlotRef = "预设", PlotYou = "你的";
        private static readonly string[] LegendBoth = new string[] { PlotRef, PlotYou };
        private static readonly string[] LegendRef = new string[] { PlotRef };
        private Color[] legendColors;
        private const string SideTitle = "侧视";
        private const string TermJump = "> 跳跃  预设路线 ……完成";
        private const string TermDone = "> 设置完成  返回精调";
        private static readonly string[] PhaseShort = new string[] { "先往下", "再往上", "回到起点" };

        // demo B (v4.1 skeleton peek) text
        private static readonly string[] SceneChip = new string[] { "右手 D+E", "换手 Q+A", "被敌人拉" };
        private const string ChipPause = "暂停", ChipPlay = "播放", ChipReplay = "重播";
        private const string TopTitle = "俯视";
        private const string FpTitle = "第一人称 · 慢放 ×0.3";
        private const string FpTitleStill = "第一人称 · 静止画面";
        private const string FpTitleShort = "第一人称";
        /// <summary>The one 示意 note of demo B (bottom of the first-person view).</summary>
        private const string NoteDemo = "绿圈偏移来自你的配置";
        private const string NoteDemoShort = "你的配置";
        private const string LblMe = "你", LblFoe = "敌人", LblCorner = "墙角", LblPre = "预瞄点";
        /// <summary>v4.3: 「敌人 · 7.5 米」 per scene (distance from our start, 示意 layout).</summary>
        private static readonly string[] LblFoeDist = FoeDistLabels();

        private static string[] FoeDistLabels()
        {
            string[] r = new string[TutorialStep10Peek.Scenes];
            for (int i = 0; i < r.Length; i++)
            {
                double dx = TutorialStep10Peek.FoeX[i] - TutorialStep10Peek.MeX[i], dz = TutorialStep10Peek.FoeZ[i] - TutorialStep10Peek.MeZ[i];
                r[i] = LblFoe + " · " + Math.Sqrt(dx * dx + dz * dz).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " 米";
            }
            return r;
        }
        private const string LegCentre = "屏幕中心线", LegBullet = "枪口弹道线";
        private const string LegDot = "屏幕中心", LegRing = "绿圈＝子弹落点", LegPre = "预瞄点";
        private const string FoeKeys = "敌人按 D+E";
        private static readonly string[] PhaseText = new string[] { "预瞄：卡住墙角", "被拉动：身体在加速", "跟住墙角", "接触！", "稳定在目标上" };
        private const string TipTitle = "强烈推荐：WMX200 战术装置";
        private const string TipLine = "PVP 最优选：平时开 IR，接敌时开手电，实测光遮蔽效果最大";

        private readonly TutorialStep10Flow flow = new TutorialStep10Flow();
        private readonly FpRig rig = new FpRig();
        private Profile truthRef;
        private double prevT = -1;
        private bool prevJumping;
        private double sinceJumpEnd = 1e9;
        private bool termPushed;
        private string jumpName = "空格";
        private Color[] kindColors;

        // jump trail (UI px)
        private readonly float[] trRingY = new float[TutorialStep10Math.TrailMax];
        private readonly float[] trAimY = new float[TutorialStep10Math.TrailMax];
        private int trN;

        // cached labels (rebuilt only when the value changes)
        private double cachedMin = double.NaN;
        private string cachedMinStr = "";

        // demo B: skeleton peek (v4.1)
        private readonly TutorialStep10Peek sim = new TutorialStep10Peek();
        private bool simValid, reduced;
        private int shownScene = -1;
        private readonly SkelCamera topCam = new SkelCamera(), fpCam = new SkelCamera();
        private SkelStyle meStyle, foeStyle, foeStyleFp;
        private TutorialPalette stylePal;
        private int contactBuiltScene = -1;
        private string contactText = "";

        public TutorialStep10Page()
        {
            Id = "jump";
            Chapter = "收尾";
            Title = "跳跃与拉枪";
            Body = "跳起来，同时开枪。\n准星会按预设路线移动。\n觉得不准，可以调几个数值。\n最后看「B · 拉枪」：骨架演示怎么拉枪、怎么预瞄。";
            DevNote = "最好的拉枪方式有两种：右手持枪按 D+E，这样相对速度最快；或者换手后按 Q+A，同样好用。战术手电推荐 WMX200，PVP 最优选：平时开 IR，接敌时开手电，它是我实测光遮蔽效果最大的手电。接下来几页讲开镜、丢雷、强化准星和弹药提示。";
            Prompts = new string[] { "按{跳跃键}跳起来，同时开枪", "看拉枪演示", "点「完成，去精调」" };
            Hint = Prompts[0];
            Terminal = new string[] { TermJump };
            FieldNote = NoteBase;
            SettingsPage = TutorialPages.Jump;
            Fields = JumpFields;
            KeysShown = TKey.Jump | TKey.Fire;
            ClickFires = true;
        }

        /// <summary>Reduced motion: the context's ReducedMotion flag when the framework has one (read by name, so this
        /// file builds with or without it), or TUT_REDUCED_MOTION=1 (render harness). Read on Enter only.</summary>
        private static bool ReadReducedMotion(TutorialContext c)
        {
            if (Environment.GetEnvironmentVariable("TUT_REDUCED_MOTION") == "1") return true;
            if (c == null) return false;
            try
            {
                Type t = c.GetType();
                System.Reflection.PropertyInfo p = t.GetProperty("ReducedMotion");
                if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(c, null);
                System.Reflection.FieldInfo f = t.GetField("ReducedMotion");
                if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(c);
            }
            catch (Exception) { }
            return false;
        }

        private const string NoteBase = "↑ 是往上，↓ 是往下。";

        /// <summary>FieldNote with the profile's current jump values (built on enter / field edit only, never per frame).</summary>
        private void RefreshFieldNote(TutorialContext c)
        {
            Profile p = c.P;
            string cur = "";
            if (JumpOk(p))
                cur = !p.Jump.Enabled ? "当前：跳跃偏移已关闭\n"
                    : "当前：先到 " + TutorialPct.Y(p.Jump.Phases[0].EndPx, c.G) + "，再到 " + TutorialPct.Y(p.Jump.Phases[1].EndPx, c.G)
                      + "，总时长 " + TutorialNum.Int(p.Jump.TotalMs) + " ms\n";
            FieldNote = cur + NoteBase;
        }

        public override void Enter(TutorialContext c)
        {
            RefreshFieldNote(c);
            truthRef = Profile.CreateLeoDefault("演示");
            rig.TargetDistanceM = TutorialStep10Math.DistanceM;
            rig.Setup(c, truthRef, c.P == null ? truthRef : c.P, false);
            Profile t = rig.TruthP, r = rig.RingP;
            // only the jump shows: no recoil (示意)
            t.RecoilMaxPx = 0; t.RecoilClickKickPx = 0; t.RecoilDriftX = 0;
            r.RecoilMaxPx = 0; r.RecoilClickKickPx = 0; r.RecoilDriftX = 0;
            if (t.Jump == null) t.Jump = new JumpSettings();
            t.Jump.Enabled = true;
            if (r.Jump == null) r.Jump = new JumpSettings();
            rig.SetDistanceNow(TutorialStep10Math.DistanceM);
            rig.WallLabel = WallLabel;
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallWm = 3.4; rig.WallHm = 6.0;
            rig.SpreadPx = 3.0;
            flow.Reset();
            prevT = -1; prevJumping = false; sinceJumpEnd = 1e9; trN = 0; termPushed = false;
            jumpName = c.PromptKey(KeyBindings.ActionJump);
            if (string.IsNullOrEmpty(jumpName)) jumpName = "?";
            PromptIndex = 0; Done = false;
            KeysShown = TKey.Jump | TKey.Fire;
            reduced = ReadReducedMotion(c);
            simValid = false; shownScene = -1; contactBuiltScene = -1;
            // the green ring of demo B = the overlay's MotionEngine with the user's own profile (copies; same settings as the
            // other steps' demos, 换手 enabled so the Q+A scene can switch hands)
            sim.Configure(c.P == null ? Profile.CreateLeoDefault("演示") : c.P.CloneValues(), c.DemoSettings(true));
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            RefreshFieldNote(c);
            Profile p = c.P, r = rig.RingP;
            if (p == null || r == null) return;
            r.Jump = p.Jump == null ? new JumpSettings() : p.Jump.Clone();
            r.Diameter = p.Diameter; r.Thickness = p.Thickness; r.Color = p.Color; r.Color2 = p.Color2;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id == 1) flow.Select(TutorialStep10Flow.ModeJump);
            else if (id == 2) flow.Select(TutorialStep10Flow.ModePeek);
            else if (id == ChipIdPause) flow.TogglePause();
            else if (id == ChipIdReplay) { flow.Replay(); simValid = false; }
            else if (id >= ChipIdScene0 && id < ChipIdScene0 + TutorialStep10Peek.Scenes) { flow.SelectScene(id - ChipIdScene0); simValid = false; }
            if (flow.ModeChanged) { ResetJumpDemo(); simValid = false; }
            if (flow.Mode == TutorialStep10Flow.ModePeek) SyncPeek();
            PromptIndex = flow.Prompt;
        }

        private void ResetJumpDemo()
        {
            rig.ClearHoles();
            trN = 0; prevJumping = false; sinceJumpEnd = 1e9; prevT = -1;
            rig.Truth.Reset(); rig.Ring.Reset();
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool live = !c.Scripted && (c.LiveMask != 0 || c.LiveFire);
            bool liveNow = c.LiveMask != 0 || c.LiveFire;
            int jumpShots = 0;
            if (flow.Mode == TutorialStep10Flow.ModeJump)
            {
                bool fire; int lit;
                InputState i = c.Input(Script, out fire, out lit);
                // this step is about the jump: lean and hand keys do not move the demo
                i.LeanL = false; i.LeanR = false; i.HandSwitch = false;
                LitKeys = lit & (TKey.Jump | TKey.Fire);
                if (c.Scripted)
                {
                    double t = Script.Wrap(c.Arbiter.ScriptMs);
                    if (t < prevT || c.Arbiter.Resumed) { rig.ClearHoles(); }
                    prevT = t;
                }
                else prevT = -1;
                rig.Update(dtMs, i, fire);
                if (!c.Scripted && rig.Truth.Jumping) jumpShots = rig.ShotsThisFrame;

                // trail of the current jump
                bool jumping = rig.Truth.Jumping || rig.Ring.Jumping;
                if (jumping && !prevJumping) trN = 0;
                if (jumping)
                {
                    sinceJumpEnd = 0;
                    if (trN < TutorialStep10Math.TrailMax) { trRingY[trN] = (float)rig.RingY; trAimY[trN] = (float)rig.AimY; trN++; }
                }
                else sinceJumpEnd += dtMs;
                if (sinceJumpEnd > 1e9) sinceJumpEnd = 1e9;
                prevJumping = jumping;
            }
            else
            {
                LitKeys = c.LiveMask & (TKey.Jump | TKey.Fire);
            }
            bool wantJump = liveNow && (c.Live.Jump || c.LiveFire || (c.LiveMask & (TKey.Jump | TKey.Fire)) != 0);
            flow.Tick(dtMs, live || liveNow, jumpShots, wantJump);
            if (flow.ModeChanged) { ResetJumpDemo(); simValid = false; }
            if (flow.Mode == TutorialStep10Flow.ModePeek)
            {
                SyncPeek();
                // the demo's own keys light the footer strip (D+E / Q+A while our scripted peek holds them)
                int side = TutorialStep10Peek.MeSide[sim.Scene];
                if (sim.KeysDown && side != 0) LitKeys |= side > 0 ? (TKey.D | TKey.E) : (TKey.Q | TKey.A);
            }
            KeysShown = flow.Mode == TutorialStep10Flow.ModeJump ? (TKey.Jump | TKey.Fire) : (TKey.D | TKey.E | TKey.Q | TKey.A);
            PromptIndex = flow.Prompt;
            if (flow.Done) Done = true;
            if (flow.JustWatched && !termPushed) { termPushed = true; c.Term.Push(TermDone); }
        }

        // =============================================================================================== drawing
        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            if (flow.Mode == TutorialStep10Flow.ModePeek) DrawPeek(gx, c, scene);
            else DrawJump(gx, c, scene);
            // chips (top centre) on top of either demo
            Graphics g = gx.G;
            g.SetClip(scene);
            float cw, ch, gap, x0, y0;
            ChipRow(gx, scene, out cw, out ch, out gap, out x0, out y0);
            gx.Chip(1, new RectangleF(x0, y0, cw, ch), ChipJump, flow.Mode == TutorialStep10Flow.ModeJump);
            gx.Chip(2, new RectangleF(x0 + cw + gap, y0, cw, ch), ChipPeek, flow.Mode == TutorialStep10Flow.ModePeek);
            g.ResetClip();
        }

        // ---------------------------------------------------------------- demo A: jump
        private void DrawJump(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            TutorialPalette pal = gx.Pal;
            if (kindColors == null) kindColors = new Color[] { pal.Hot };
            rig.KindColors = kindColors;
            JumpSettings tj = rig.TruthP.Jump, rj = rig.RingP.Jump;
            bool ringOn = rj != null && rj.Enabled && rj.Phases != null && rj.Phases.Count >= 3;
            bool truthOn = tj != null && tj.Phases != null && tj.Phases.Count >= 3;
            double ext = TutorialStep10Math.JumpExtent(truthOn, truthOn ? tj.Phases[0].EndPx : 0, truthOn ? tj.Phases[1].EndPx : 0,
                ringOn, ringOn ? rj.Phases[0].EndPx : 0, ringOn ? rj.Phases[1].EndPx : 0, 24, 60);
            rig.Zoom = TutorialStep10Math.FitZoom(scene.Height / 2f, gx.K, ext, 0.74);
            // wall: top edge just below the chips so its label stays readable (示意 size)
            double zk = rig.Zoom * gx.K;
            if (zk > 0) rig.WallHm = TutorialStep10Math.WallHeightM(scene.Height / 2f - gx.S(14) - Math.Max(gx.S(24), gx.F.Button.GetHeight(gx.G) + gx.S(8)) - gx.S(8), zk, TutorialStep10Math.DistanceM);
            rig.Draw(gx, scene);

            Graphics g = gx.G;
            g.SetClip(scene);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float z = (float)(rig.Zoom * gx.K);
            float pad = gx.S(10);

            // trail of the last jump: bullets (hot, dashed guide) and ring (neon dots)
            double fade = 1 - sinceJumpEnd / TutorialStep10Math.TrailFadeMs;
            if (fade > 0 && trN > 1)
            {
                float xr = (float)(cx + rig.RingX * z), xa = (float)(cx + rig.AimX * z);
                for (int i = 1; i < trN; i++)
                {
                    double a = fade * (0.35 + 0.65 * i / (double)trN);
                    float y0 = cy - trAimY[i - 1] * z, y1 = cy - trAimY[i] * z;
                    g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, a * 0.55), 3.5f), xa, y0, xa, y1);
                    g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, a), 1.2f), xa, y0, xa, y1);
                }
                for (int i = 0; i < trN; i += 3)
                {
                    double a = fade * (0.3 + 0.7 * i / (double)trN);
                    gx.Dot(TutorialGfx.A(pal.Neon, a), xr + gx.S(8), cy - trRingY[i] * z, gx.S(1.6f));
                }
            }

            // depth bracket of the preset path (lowest point of the first phase)
            if (truthOn)
            {
                double low = Math.Min(0, Math.Min(tj.Phases[0].EndPx, tj.Phases[1].EndPx));
                if (low < -0.5)
                {
                    if (low != cachedMin) { cachedMin = low; cachedMinStr = TutorialPct.Y(low, c.G); }
                    float bx = cx - gx.S(44);
                    float yb = (float)(cy - low * z);
                    g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 140), 1f), bx - gx.S(4), yb, cx + gx.S(20), yb);
                    gx.Bracket(bx, cy, yb, TutorialGfx.A(pal.Hot, 220), null);
                    float lh1 = gx.F.Mono.GetHeight(g), lh2 = gx.F.Tag.GetHeight(g);
                    float ly = (cy + yb) / 2f - (lh1 + lh2) / 2f;
                    gx.Text(cachedMinStr, gx.F.Mono, pal.Hot, bx - gx.S(10), ly, 2);
                    if (!c.Compact) gx.Text(TagPreset, gx.F.Tag, pal.Hot, bx - gx.S(10), ly + lh1, 2);
                }
            }

            // HUD tags (top-left)
            // v4.3 (V43-OVERLAP #1): when the tag, the A/B chips and the key cap do not fit in one row, the tag and the key
            // cap move one row below the chips
            float hudTop = JumpHudTop(gx, scene);
            RectangleF t1 = gx.Tag(TagPreset, scene.Left + pad, hudTop < 0 ? scene.Top + pad : hudTop, pal.Hot, 0);
            float capBottom = DrawJumpKeys(gx, scene, hudTop);
            if (!c.Compact)
            {
                RectangleF t2 = gx.Tag(ringOn ? TagYours : TagYoursOff, scene.Left + pad, t1.Bottom + gx.S(5), ringOn ? pal.Neon : pal.Hot, 0);
                if (rig.Truth.Jumping) gx.Legend(TagJumpNow, scene.Left + pad, t2.Bottom + gx.S(3), pal.Accent, TutorialGfx.MarkDot, 0); // v4.1: max 2 tags
                // offset / time plot (right, under the key caps) and side view (bottom-left)
                DrawJumpPlot(gx, scene, capBottom + gx.S(8), tj, truthOn, rj, ringOn);
                DrawSideView(gx, scene, tj);
            }
            g.ResetClip();
        }

        /// <summary>The A/B chip row (top centre of the scene): width of one chip, height, gap, left X and top Y.</summary>
        private static void ChipRow(TutorialGfx gx, RectangleF scene, out float cw, out float ch, out float gap, out float x0, out float y0)
        {
            Font cf = gx.F.Button;
            cw = Math.Max(gx.S(86), Math.Max(gx.Measure(ChipJump, cf).Width, gx.Measure(ChipPeek, cf).Width) + gx.S(20));
            ch = Math.Max(gx.S(24), cf.GetHeight(gx.G) + gx.S(8)); gap = gx.S(8);
            x0 = scene.Left + scene.Width / 2f - cw - gap / 2f; y0 = scene.Top + gx.S(10);
        }

        /// <summary>-1 when the 「预设路线」 tag (left), the chips (centre) and the key cap + mouse (right) fit in the top row
        /// without touching; else the Y one row below the chips, where the tag and the key cap go instead.</summary>
        private float JumpHudTop(TutorialGfx gx, RectangleF scene)
        {
            float cw, ch, gap, x0, y0;
            ChipRow(gx, scene, out cw, out ch, out gap, out x0, out y0);
            float tagRight = scene.Left + gx.S(10) + gx.Measure(TagPreset, gx.F.Tag).Width + gx.S(12);
            float keyW = Math.Max(gx.S(64), gx.Measure(jumpName, gx.F.Key).Width + gx.S(18));
            float keyLeft = scene.Right - gx.S(10) - gx.S(18) - gx.S(10) - keyW;
            bool fits = tagRight + gx.S(8) <= x0 && x0 + 2 * cw + gap + gx.S(8) <= keyLeft;
            return fits ? -1f : y0 + ch + gx.S(6);
        }

        private float DrawJumpKeys(TutorialGfx gx, RectangleF scene, float hudTop)
        {
            float top = hudTop < 0 ? scene.Top + gx.S(10) : hudTop;
            TutorialPalette pal = gx.Pal;
            bool jumpLit = (LitKeys & TKey.Jump) != 0 || rig.Truth.Jumping;
            bool fireLit = (LitKeys & TKey.Fire) != 0;
            float h = gx.S(28);
            float tw = gx.Measure(jumpName, gx.F.Key).Width;
            float w = Math.Max(gx.S(64), tw + gx.S(18));
            float mw = gx.S(18), mh = gx.S(27);
            float right = scene.Right - gx.S(10);
            float mx = right - mw, my = top;
            RectangleF k = new RectangleF(mx - gx.S(10) - w, top, w, h);
            if (jumpLit) gx.G.FillRectangle(gx.B(TutorialGfx.A(pal.Neon, 60)), k.X - gx.S(2), k.Y - gx.S(2), k.Width + gx.S(4), k.Height + gx.S(4));
            gx.G.FillRectangle(gx.B(jumpLit ? TutorialGfx.A(pal.Neon, 150) : TutorialGfx.A(pal.Surface, 235)), k);
            gx.G.DrawRectangle(gx.P(jumpLit ? pal.Neon : pal.Accent, jumpLit ? 1.6f : 1.1f), k.X, k.Y, k.Width, k.Height);
            gx.TextIn(jumpName, gx.F.Key, jumpLit ? Color.Black : pal.Text, k, gx.CenterMid);
            // mouse (left button = 开枪)
            gx.G.FillEllipse(gx.B(TutorialGfx.A(pal.Surface, 235)), mx, my, mw, mh);
            gx.G.DrawEllipse(gx.P(fireLit ? pal.Hot : pal.Border, 1.1f), mx, my, mw, mh);
            gx.G.FillPie(gx.B(fireLit ? pal.Hot : TutorialGfx.A(pal.Border, 160)), mx, my, mw, mw, 180, 90);
            if (fireLit) gx.Dot(TutorialGfx.A(pal.Hot, 60), mx + mw / 2, my + mh / 2, mw);
            return Math.Max(k.Bottom, my + mh);
        }

        private void DrawJumpPlot(TutorialGfx gx, RectangleF scene, float top, JumpSettings tj, bool truthOn, JumpSettings rj, bool ringOn)
        {
            TutorialPalette pal = gx.Pal;
            float w = Math.Min(gx.S(270), scene.Width * 0.34f), h = Math.Min(gx.S(160), scene.Height * 0.36f);
            if (w < gx.S(150) || h < gx.S(90)) return;
            RectangleF box = new RectangleF(scene.Right - w - gx.S(10), top, w, h);
            if (legendColors == null) legendColors = new Color[] { pal.Hot, pal.Neon };
            RectangleF inr = gx.PlotFrame(box, PlotTitle, ringOn ? LegendBoth : LegendRef, legendColors);
            float phaseH = gx.F.MonoSmall.GetHeight(gx.G);
            inr = new RectangleF(inr.Left, inr.Top, inr.Width, inr.Height - phaseH - gx.S(2));
            if (inr.Height < gx.S(24)) return;
            double total = 1;
            if (truthOn) total = Math.Max(total, tj.TotalMs);
            if (ringOn) total = Math.Max(total, rj.TotalMs);
            double lo = 0, hi = 0;
            for (int i = 0; i < 2; i++)
            {
                if (truthOn) { lo = Math.Min(lo, tj.Phases[i].EndPx); hi = Math.Max(hi, tj.Phases[i].EndPx); }
                if (ringOn) { lo = Math.Min(lo, rj.Phases[i].EndPx); hi = Math.Max(hi, rj.Phases[i].EndPx); }
            }
            double span = Math.Max(20, hi - lo);
            lo -= span * 0.12; hi += span * 0.12;
            Graphics g = gx.G;
            float y0 = TutorialStep10Math.PlotY(0, lo, hi, inr.Top, inr.Height);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Border, 220), 1f), inr.Left, y0, inr.Right, y0);
            // phase boundaries of the preset
            if (truthOn)
            {
                double b = 0;
                for (int i = 0; i < 3; i++)
                {
                    double e = b + tj.Phases[i].Ms;
                    float xa = (float)(inr.Left + inr.Width * b / total), xb = (float)(inr.Left + inr.Width * e / total);
                    if (i > 0) g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Muted, 90), 1f), xa, inr.Top, xa, inr.Bottom);
                    if (xb - xa > gx.Measure(PhaseShort[i], gx.F.MonoSmall).Width + gx.S(2))
                        gx.Text(PhaseShort[i], gx.F.MonoSmall, pal.Muted, (xa + xb) / 2f, inr.Bottom + gx.S(1), 1);
                    b = e;
                }
            }
            const int N = 64;
            if (ringOn) PlotCurve(gx, inr, rj, total, lo, hi, N, TutorialGfx.A(pal.Neon, 220), 1.3f, false);
            if (truthOn) PlotCurve(gx, inr, tj, total, lo, hi, N, pal.Hot, 1.4f, true);
            // cursor (current jump time)
            if (rig.Truth.Jumping && truthOn)
            {
                double t = rig.Truth.JumpMs;
                float x = (float)(inr.Left + inr.Width * Math.Min(1, t / total));
                g.DrawLine(gx.P(TutorialGfx.A(pal.Text, 90), 1f), x, inr.Top, x, inr.Bottom);
                gx.GlowDot(pal.Hot, x, TutorialStep10Math.PlotY(JumpPath.Offset(tj, t), lo, hi, inr.Top, inr.Height), gx.S(2.2f));
                if (ringOn && rig.Ring.Jumping)
                    gx.GlowDot(pal.Neon, x, TutorialStep10Math.PlotY(JumpPath.Offset(rj, rig.Ring.JumpMs), lo, hi, inr.Top, inr.Height), gx.S(1.8f));
            }
        }

        private static void PlotCurve(TutorialGfx gx, RectangleF inr, JumpSettings j, double total, double lo, double hi, int n,
            Color col, float w, bool glow)
        {
            Graphics g = gx.G;
            float px = inr.Left, py = TutorialStep10Math.PlotY(0, lo, hi, inr.Top, inr.Height);
            for (int i = 1; i <= n; i++)
            {
                double t = total * i / n;
                float x = (float)(inr.Left + inr.Width * i / n);
                float y = TutorialStep10Math.PlotY(t >= j.TotalMs ? 0 : JumpPath.Offset(j, t), lo, hi, inr.Top, inr.Height);
                if (glow) g.DrawLine(gx.P(TutorialGfx.A(col, 60), w * 3.2f), px, py, x, y);
                g.DrawLine(gx.P(col, w), px, py, x, y);
                px = x; py = y;
            }
        }

        private void DrawSideView(TutorialGfx gx, RectangleF scene, JumpSettings tj)
        {
            TutorialPalette pal = gx.Pal;
            float w = gx.S(132), h = gx.S(92);
            if (scene.Width < gx.S(420) || scene.Height < gx.S(260)) return;
            RectangleF box = new RectangleF(scene.Left + gx.S(10), scene.Bottom - h - gx.S(10), w, h);
            RectangleF inr = gx.PlotFrame(box, SideTitle);
            Graphics g = gx.G;
            float ground = inr.Bottom - gx.S(4);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 120), 1f), inr.Left, ground, inr.Right, ground);
            for (float x = inr.Left + gx.S(6); x < inr.Right; x += gx.S(10)) g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 50), 1f), x, ground, x - gx.S(5), ground + gx.S(4));
            double air = rig.Truth.Jumping && tj != null ? TutorialStep10Math.AirHeight01(rig.Truth.JumpMs, tj.TotalMs) : 0;
            float lift = (float)(air * gx.S(24));
            float fx = inr.Left + inr.Width * 0.42f, feet = ground - lift;
            float bodyH = gx.S(30);
            // shadow
            gx.G.FillEllipse(gx.B(TutorialGfx.A(Color.Black, 120 - (int)(60 * air))), fx - gx.S(9) * (float)(1 - 0.3 * air), ground - gx.S(2), gx.S(18) * (float)(1 - 0.3 * air), gx.S(4));
            Color col = pal.Cyan;
            float hip = feet - bodyH * 0.45f, neck = feet - bodyH * 0.85f;
            float knee = (float)(air * gx.S(4));
            g.DrawLine(gx.P(col, 1.6f), fx, hip, fx - gx.S(4) - knee, feet);
            g.DrawLine(gx.P(col, 1.6f), fx, hip, fx + gx.S(4) + knee, feet);
            g.DrawLine(gx.P(col, 1.8f), fx, hip, fx, neck);
            gx.GlowCircle(col, 1.4f, fx, neck - gx.S(5), gx.S(4.5f));
            // rifle toward the wall (right)
            float gy = neck + gx.S(5);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Text, 220), 1.6f), fx - gx.S(2), gy + gx.S(2), fx + gx.S(18), gy);
            // tracer to the wall when firing
            float wallX = inr.Right - gx.S(6);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Accent, 160), 2f), wallX, inr.Top + gx.S(2), wallX, ground);
            if (rig.Firing) g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, 170), 1f), fx + gx.S(18), gy, wallX, gy + (float)(-rig.AimY * 0.02 * gx.K));
            if (air > 0.02) gx.Arrow(TutorialGfx.A(pal.Neon, 200), 1.2f, fx - gx.S(16), ground - gx.S(4), fx - gx.S(16), ground - gx.S(6) - lift);
        }

        // ---------------------------------------------------------------- demo B: skeleton peek (v4.1)
        private const int ChipIdPause = 3, ChipIdReplay = 4, ChipIdScene0 = 5;

        /// <summary>Keeps the peek sim on the flow's clock (fixed steps); reduced motion holds each scene's still frame.</summary>
        private void SyncPeek()
        {
            double local;
            int sc = TutorialStep10Math.PeekScene(flow.PeekMs, out local);
            if (reduced)
            {
                if (!simValid || sc != shownScene) { sim.SeekKeyFrame(sc); shownScene = sc; simValid = true; contactBuiltScene = -1; }
            }
            else
            {
                if (!simValid || sc != sim.Scene || local + 1 < sim.LocalMs) { sim.Start(sc); shownScene = sc; simValid = true; contactBuiltScene = -1; }
                sim.AdvanceTo(local);
            }
            if (sim.ContactMs >= 0 && contactBuiltScene != sc)
            {
                // built once per scene (the only string made at run time here)
                contactBuiltScene = sc;
                contactText = "接触 " + TutorialStep10Math.SecondsText(sim.ContactGameS);
            }
        }

        private void EnsureStyles(TutorialPalette pal)
        {
            if (meStyle != null && stylePal == pal) return;
            stylePal = pal;
            meStyle = SkelStyle.Friendly(pal);
            meStyle.ShowCentre = true; meStyle.ShowBullet = false;   // the bullet line is drawn along the ring (DrawTop)
            foeStyle = SkelStyle.Enemy(pal);
            foeStyle.ShowCentre = true;
            foeStyleFp = SkelStyle.Enemy(pal);
            foeStyleFp.ShowEye = false;
        }

        private void DrawPeek(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            EnsureStyles(pal);
            if (!simValid) SyncPeek();
            g.SetClip(scene);
            gx.SceneBackground(scene);
            float pad = gx.S(10), gap = gx.S(8);
            Font cf = gx.F.Button;
            float ch = Math.Max(gx.S(24), cf.GetHeight(g) + gx.S(8));
            // row 1 = the A / B chips (Draw); row 2 = scene chips (left) + pause / replay (right)
            float row2 = scene.Top + gx.S(10) + ch + gx.S(6);
            bool compact = c.Compact;
            int sc = sim.Scene;
            float right = scene.Right - pad;
            float rw = Math.Max(gx.S(64), gx.Measure(ChipReplay, cf).Width + gx.S(20));
            float pw = Math.Max(gx.S(64), Math.Max(gx.Measure(ChipPause, cf).Width, gx.Measure(ChipPlay, cf).Width) + gx.S(20));
            RectangleF rRe = new RectangleF(right - rw, row2, rw, ch);
            RectangleF rPa = new RectangleF(rRe.Left - gap - pw, row2, pw, ch);
            gx.Chip(ChipIdPause, rPa, flow.Paused ? ChipPlay : ChipPause, flow.Paused);
            gx.Chip(ChipIdReplay, rRe, ChipReplay, false);
            float x = scene.Left + pad;
            double local;
            TutorialStep10Math.PeekScene(flow.PeekMs, out local);
            for (int i = 0; i < TutorialStep10Peek.Scenes; i++)
            {
                float w = Math.Max(gx.S(72), gx.Measure(SceneChip[i], cf).Width + gx.S(18));
                if (x + w > rPa.Left - gap) break;          // narrow: the rest is reachable through the loop / replay
                RectangleF r = new RectangleF(x, row2, w, ch);
                gx.Chip(ChipIdScene0 + i, r, SceneChip[i], i == sc);
                if (i == sc && !reduced)
                {   // progress of the scene under its chip
                    float f = (float)Math.Max(0, Math.Min(1, local / TutorialStep10Peek.SceneMs));
                    g.FillRectangle(gx.B(pal.Neon), r.Left, r.Bottom + gx.S(2), r.Width * f, gx.S(2));
                }
                x += w + gap;
            }
            float bodyTop = row2 + ch + gx.S(10);
            // tip card (bottom)
            float cardW = scene.Width - 2 * pad;
            bool twoLines;
            float cardH = TipHeight(gx, cardW, compact, out twoLines);
            RectangleF card = new RectangleF(scene.Left + pad, scene.Bottom - pad - cardH, cardW, cardH);
            RectangleF body = new RectangleF(scene.Left + pad, bodyTop, scene.Width - 2 * pad, card.Top - gap - bodyTop);
            if (body.Height > gx.S(80))
            {
                if (compact || body.Width < gx.S(620))
                    DrawFp(gx, c, body, true);
                else
                {
                    float tw = Math.Min(body.Width * 0.44f, body.Height * 1.1f);
                    RectangleF top = new RectangleF(body.Left, body.Top, tw, body.Height);
                    RectangleF fp = new RectangleF(top.Right + gap, body.Top, body.Right - top.Right - gap, body.Height);
                    DrawTop(gx, top);
                    DrawFp(gx, c, fp, false);
                }
            }
            DrawTip(gx, card, compact, twoLines);
            // scene fade in / out (none with reduced motion)
            if (!reduced)
            {
                double fade = TutorialStep10Math.SceneFade(local);
                if (fade < 1)
                {
                    RectangleF fr = new RectangleF(scene.Left, bodyTop - gx.S(4), scene.Width, card.Top - bodyTop);
                    g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 1 - fade)), fr);
                }
            }
            g.ResetClip();
        }

        /// <summary>Tip card height for width w (title + one or two wrapped lines; compact: title + short line if it fits).</summary>
        private static float TipHeight(TutorialGfx gx, float w, bool compact, out bool twoLines)
        {
            Graphics g = gx.G;
            float padY = gx.S(7), th = gx.F.Big.GetHeight(g), lh = gx.F.Body.GetHeight(g);
            float avail = w - gx.S(4) - gx.S(24);
            twoLines = false;
            if (compact)
            {
                bool fits = gx.Measure(TipLine, gx.F.Small).Width <= avail;
                return th + (fits ? gx.F.Small.GetHeight(g) : 0) + 2 * padY;
            }
            float lw = gx.Measure(TipLine, gx.F.Body).Width;
            twoLines = lw > avail;
            return th + gx.S(2) + lh * (twoLines ? 2 : 1) + 2 * padY;
        }

        /// <summary>Highlighted flat tip card: accent stripe + 1 px accent border, title in accent, one plain line.</summary>
        private static void DrawTip(TutorialGfx gx, RectangleF r, bool compact, bool twoLines)
        {
            if (r.Width < gx.S(120) || r.Height < gx.S(16)) return;
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            g.FillRectangle(gx.B(TutorialPalette.Mix(pal.SceneBg, pal.Accent, 0.16)), r);
            g.FillRectangle(gx.B(pal.Accent), r.Left, r.Top, gx.S(4), r.Height);
            g.DrawRectangle(gx.P(pal.Accent, 1f), r.X, r.Y, r.Width, r.Height);
            float tx = r.Left + gx.S(4) + gx.S(12), padY = gx.S(7);
            float th = gx.F.Big.GetHeight(g);
            gx.Text(TipTitle, gx.F.Big, pal.Accent, tx, r.Top + padY, 0);
            float y = r.Top + padY + th + (compact ? 0 : gx.S(2));
            if (compact)
            {
                if (r.Bottom - padY - y >= gx.F.Small.GetHeight(g) - 0.5f) gx.Text(TipLine, gx.F.Small, pal.Text, tx, y, 0);
                return;
            }
            RectangleF lr = new RectangleF(tx, y, r.Right - gx.S(12) - tx, r.Bottom - padY - y + gx.S(1));
            gx.TextIn(TipLine, gx.F.Body, pal.Text, lr, gx.Near);
        }

        // ---- top-down corner view
        private void DrawTop(TutorialGfx gx, RectangleF box)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            RectangleF inr = gx.PlotFrame(box, TopTitle);
            int s = sim.Scene;
            float lh = gx.F.Small.GetHeight(g);
            float legendH = lh + gx.S(6);
            float cap = Math.Max(gx.S(24), gx.F.Key.GetHeight(g) + gx.S(8));
            RectangleF map = new RectangleF(inr.Left, inr.Top, inr.Width, inr.Height - legendH);
            if (map.Height < gx.S(60) || map.Width < gx.S(80)) return;
            // fixed 示意 window around our start, the corner and the enemy (large; the far wall end is clipped)
            double x0 = -1.35, x1 = 1.35, z0 = -0.42, z1 = TutorialStep10Peek.FoeZ[s] + 0.62;   // room for the 敌人 label
            double scale = Math.Min(map.Width / (x1 - x0), map.Height / (z1 - z0));
            double wcx = (x0 + x1) / 2, wcz = (z0 + z1) / 2;
            float ox = map.Left + map.Width / 2f, oy = map.Top + map.Height / 2f;
            topCam.SetTop(ox, oy, wcx, wcz, scale, 0);
            double vx0 = wcx - map.Width / 2 / scale, vx1 = wcx + map.Width / 2 / scale;
            double vz0 = wcz - map.Height / 2 / scale, vz1 = wcz + map.Height / 2 / scale;
            g.SetClip(map);
            // 1 m grid
            Pen grid = gx.P(TutorialGfx.A(pal.Border, 110), 1f);
            float gx0, gy0, gx1, gy1;
            for (int i = (int)Math.Ceiling(vx0); i <= (int)Math.Floor(vx1); i++)
            {
                topCam.Project(i, 0, vz0, out gx0, out gy0); topCam.Project(i, 0, vz1, out gx1, out gy1);
                g.DrawLine(grid, gx0, gy0, gx1, gy1);
            }
            for (int i = (int)Math.Ceiling(vz0); i <= (int)Math.Floor(vz1); i++)
            {
                topCam.Project(vx0, 0, i, out gx0, out gy0); topCam.Project(vx1, 0, i, out gx1, out gy1);
                g.DrawLine(grid, gx0, gy0, gx1, gy1);
            }
            // cover (flat block) and its corner
            float ax, ay, bx, by;
            topCam.Project(TutorialStep10Peek.CovX0[s], 0, TutorialStep10Peek.CovZ1[s], out ax, out ay);
            topCam.Project(TutorialStep10Peek.CovX1[s], 0, TutorialStep10Peek.CovZ0[s], out bx, out by);
            RectangleF cov = RectangleF.FromLTRB(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
            g.FillRectangle(gx.B(TutorialPalette.Mix(pal.Surface, pal.Accent, 0.25)), cov);
            g.DrawRectangle(gx.P(pal.Accent, 1f), cov.X, cov.Y, cov.Width, cov.Height);
            float cx, cy;
            topCam.Project(TutorialStep10Peek.OpenX[s], 0, TutorialStep10Peek.CovZ0[s], out cx, out cy);
            gx.Dot(pal.Accent, cx, cy, gx.S(3));
            bool coverLeft = TutorialStep10Peek.CoverSide[s] < 0;      // label on the cover side of the corner, under the wall
            gx.Text(LblCorner, gx.F.Small, pal.Accent, cx + (coverLeft ? -gx.S(6) : gx.S(6)), cy + gx.S(4), coverLeft ? 2 : 0);

            // line of sight: our eye -> the enemy's head (dashed until contact, then solid)
            float ex, ey, hx, hy;
            topCam.Project(sim.Me.X[SkelJ.Eye], 0, sim.Me.Z[SkelJ.Eye], out ex, out ey);
            topCam.Project(sim.Foe.X[SkelJ.Head], 0, sim.Foe.Z[SkelJ.Head], out hx, out hy);
            if (sim.Visible) g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, 200), 1.2f), ex, ey, hx, hy);
            else g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Muted, 170), 1f), ex, ey, hx, hy);
            // strafe path (start -> now) under the feet, ours or the enemy's
            float sx0, sy0, sx1, sy1;
            topCam.Project(TutorialStep10Peek.MeX[s], 0, TutorialStep10Peek.MeZ[s], out sx0, out sy0);
            topCam.Project(sim.MeP.X, 0, sim.MeP.Z, out sx1, out sy1);
            if (Math.Abs(sx1 - sx0) > gx.S(3)) gx.Arrow(TutorialGfx.A(pal.Cyan, 200), 1.4f, sx0, sy0 + gx.S(10), sx1, sy1 + gx.S(10));
            topCam.Project(TutorialStep10Peek.FoeX[s], 0, TutorialStep10Peek.FoeZ[s], out sx0, out sy0);
            topCam.Project(sim.FoeP.X, 0, sim.FoeP.Z, out sx1, out sy1);
            if (Math.Abs(sx1 - sx0) > gx.S(3)) gx.Arrow(TutorialGfx.A(pal.Red, 200), 1.4f, sx0, sy0 - gx.S(10), sx1, sy1 - gx.S(10));
            // rays reach the enemy's depth
            double dz = Math.Max(1, sim.Foe.Z[SkelJ.Head] - sim.Me.Z[SkelJ.Eye]);
            meStyle.CentreLenM = dz + 0.4;
            foeStyle.CentreLenM = 1.2;
            SkelDraw.Draw(gx, sim.Foe, topCam, foeStyle);
            SkelDraw.Draw(gx, sim.Me, topCam, meStyle);
            // bullet line = along the green ring (the profile's offset), from the muzzle to the enemy's depth
            {
                double rx, ry, rz;
                sim.RingPoint(dz + 0.4, out rx, out ry, out rz);
                float mxs, mys, rxs, rys;
                topCam.Project(sim.Me.X[SkelJ.Muzzle], 0, sim.Me.Z[SkelJ.Muzzle], out mxs, out mys);
                topCam.Project(rx, 0, rz, out rxs, out rys);
                g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, 220), 1.6f), mxs, mys, rxs, rys);
            }
            // lean: head offset from the upright position (pelvis), ours and the enemy's
            LeanMark(gx, sim.Me, pal.Cyan);
            LeanMark(gx, sim.Foe, pal.Red);
            // 预瞄点 (label on the side away from the enemy)
            float px, py;
            topCam.Project(sim.PreX, 0, sim.PreZ, out px, out py);
            Diamond(gx, px, py, gx.S(5), pal.Accent);
            if (sim.ContactMs < 0)
            {
                bool right = px >= hx;
                gx.Text(LblPre, gx.F.Small, pal.Accent, px + (right ? gx.S(9) : -gx.S(9)), py - lh / 2, right ? 0 : 2);
            }
            // contact marker
            if (sim.ContactMs >= 0)
            {
                double age = sim.LocalMs - sim.ContactMs;
                float rr = gx.S(11) + (float)(age < 600 && !reduced ? (age / 600.0) * gx.S(8) : 0);
                g.DrawEllipse(gx.P(pal.Hot, 1.6f), hx - rr, hy - rr, 2 * rr, 2 * rr);
            }
            // names
            float mx, my, fx, fy;
            topCam.Project(sim.MeP.X, 0, sim.MeP.Z, out mx, out my);
            topCam.Project(sim.FoeP.X, 0, sim.FoeP.Z, out fx, out fy);
            // v4.1: names stay whole inside the map (「你」 was cut in half by the map's bottom edge): below the figure when
            // it fits, else beside it on the open side; the enemy's name is kept inside the map too
            double nlx, nly;
            SizeF meSz = gx.Measure(LblMe, gx.F.Small), foeSz = gx.Measure(LblFoeDist[s], gx.F.Small);
            TutorialPlace.NameLabel(mx, my, meSz.Width, meSz.Height, gx.S(16), gx.S(16), !coverLeft,
                map.Left + gx.S(2), map.Top + gx.S(2), map.Right - gx.S(2), map.Bottom - gx.S(2), out nlx, out nly);
            gx.Text(LblMe, gx.F.Small, pal.Cyan, (float)nlx, (float)nly, 0);
            float fyl = Math.Max(map.Top + gx.S(2), Math.Min(map.Bottom - gx.S(2) - foeSz.Height, fy - gx.S(20) - lh));
            float fxl = Math.Max(map.Left + gx.S(2) + foeSz.Width / 2, Math.Min(map.Right - gx.S(2) - foeSz.Width / 2, fx));
            gx.Text(LblFoeDist[s], gx.F.Small, pal.Red, fxl, fyl, 1);
            // keys (bottom corner on the wall side): ours (lean above move, like the keyboard) or the enemy's (one tag)
            int side = TutorialStep10Peek.MeSide[s];
            float ky = map.Bottom - 2 * cap - gx.S(10);
            if (side != 0 && ky > map.Top + gx.S(30))
            {
                bool down = sim.KeysDown;
                float kx = side > 0 ? map.Left + gx.S(6) : map.Right - gx.S(6) - cap;
                gx.KeyCap(kx, ky, cap, side > 0 ? "E" : "Q", down, true);
                gx.KeyCap(kx, ky + cap + gx.S(4), cap, side > 0 ? "D" : "A", down, true);
            }
            else if (side == 0) gx.Tag(FoeKeys, map.Right - gx.S(6), map.Bottom - gx.S(6) - gx.F.Tag.GetHeight(g) - gx.S(5), pal.Red, 2);
            g.ResetClip();
            g.SetClip(box);
            // legend: dashed = screen-centre line (eye), solid = bullet line (along the green ring)
            float ly = map.Bottom + gx.S(3) + lh / 2f, lx = inr.Left;
            float seg = gx.S(18);
            g.DrawLine(gx.Dashed(pal.Neon, 1.6f), lx, ly, lx + seg, ly);
            RectangleF l1 = new RectangleF(lx + seg + gx.S(4), ly - lh / 2f, gx.Measure(LegCentre, gx.F.Small).Width, lh);
            gx.Text(LegCentre, gx.F.Small, pal.Neon, l1.Left, l1.Top, 0);
            float lx2 = l1.Right + gx.S(12);
            if (lx2 + seg + gx.S(4) + gx.Measure(LegBullet, gx.F.Small).Width <= inr.Right)
            {
                g.DrawLine(gx.P(pal.Hot, 1.6f), lx2, ly, lx2 + seg, ly);
                gx.Text(LegBullet, gx.F.Small, pal.Hot, lx2 + seg + gx.S(4), ly - lh / 2f, 0);
            }
            g.ResetClip();
        }

        /// <summary>Top-down lean marker: a ring at the upright head spot (over the pelvis) and an arrow to the leaned head.</summary>
        private void LeanMark(TutorialGfx gx, SkelRig r, Color col)
        {
            if (!(Math.Abs(r.Pose.Lean) > 0.15)) return;
            float ux, uy, hx, hy;
            topCam.Project(r.X[SkelJ.Pelvis], 0, r.Z[SkelJ.Pelvis], out ux, out uy);
            topCam.Project(r.X[SkelJ.Head], 0, r.Z[SkelJ.Head], out hx, out hy);
            if (Math.Abs(hx - ux) + Math.Abs(hy - uy) < gx.S(6)) return;
            float rr = gx.S(4);
            gx.G.DrawEllipse(gx.Dashed(TutorialGfx.A(col, 200), 1.2f), ux - rr, uy - rr, 2 * rr, 2 * rr);
            gx.Arrow(TutorialGfx.A(col, 230), 1.6f, ux, uy, hx, hy);
        }

        private static void Diamond(TutorialGfx gx, float x, float y, float r, Color col)
        {
            PointF[] d = gx.Pts(4);
            d[0] = new PointF(x, y - r); d[1] = new PointF(x + r, y); d[2] = new PointF(x, y + r); d[3] = new PointF(x - r, y);
            gx.G.FillPolygon(gx.B(TutorialGfx.A(Color.Black, 170)), d);
            gx.G.DrawPolygon(gx.P(col, 1.6f), d);
        }

        /// <summary>First-person view zoom: focal length = this x the view width (a crop around the screen centre, 示意).</summary>
        private const double FpFocalPerWidth = 1.35;

        /// <summary>v4.3: first-person focal length (px): the view is the centre crop of the user's game picture (its aspect,
        /// 16:9 when unknown) at the user's vertical FOV (ResFov, 50..75; 75 when missing). A view wider than the game's
        /// aspect (short boxes) is the full picture width, so the picture height there is viewW / aspect.</summary>
        private static double FpFocal(float viewW, float viewH, GlobalSettings g)
        {
            double fov = g != null ? g.ResFov : ViewScale.DefaultFov;
            if (!(fov >= ViewScale.MinFov && fov <= ViewScale.MaxFov)) fov = ViewScale.DefaultFov;
            double a = 16.0 / 9.0;
            if (g != null && g.ResGameW > 0 && g.ResGameH > 0) a = ViewScale.EffectiveAspect(g.ResAspect, g.ResGameW, g.ResGameH);
            if (!(a > 0.5 && a < 5)) a = 16.0 / 9.0;
            double picH = Math.Max(viewH, viewW / a);
            double f = (picH / 2.0) / Math.Tan(fov * Math.PI / 360.0);
            return f > 1 ? f : viewH;
        }
        // v4.3 (W6): cached 「画面拉伸 · 左右 ×1.33」 tag; contact exposure tag
        private string exposureText;
        private int exposureBuiltScene = -1;
        private string stretchTag;
        private double stretchTagS = 1.0;

        // ---- first-person inset: cover edge, enemy skeleton, green ring (bullets) vs screen centre, 预瞄点
        private void DrawFp(TutorialGfx gx, TutorialContext c, RectangleF box, bool full)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            string title = reduced ? FpTitleStill : (box.Width < gx.S(420) ? FpTitleShort : FpTitle);
            // short (compact) box: no frame title, the picture gets the room
            bool tight = full && box.Height < gx.S(260);
            RectangleF inr = tight ? box : gx.PlotFrame(box, title);
            if (tight) g.DrawRectangle(gx.P(gx.Pal.Border, 1f), box.X, box.Y, box.Width, box.Height);
            if (inr.Width < gx.S(120) || inr.Height < gx.S(80)) return;
            int s = sim.Scene;
            float lh = gx.F.Small.GetHeight(g);
            float legendH = lh + gx.S(6);
            RectangleF view = new RectangleF(inr.Left, inr.Top, inr.Width, inr.Height - legendH);
            g.SetClip(view);
            g.FillRectangle(gx.B(pal.SceneBg), view);
            float vcx = view.Left + view.Width / 2f, vcy = view.Top + view.Height / 2f;
            // v4.3 (Leo): sizes as in game: the view's height = the game picture's height at the user's real FOV (vertical), so an
            // enemy 7 m away looks as small as in game (was a 1.35 x width zoom). The ring keeps its real angle (k below).
            double focal = FpFocal(view.Width, view.Height, c != null ? c.G : null);
            fpCam.SetFromEye(sim.Me, vcx, vcy, focal);
            // v4.3 (W6): a stretched 画面比例 (e.g. 4:3 on a 16:9 screen) draws the game picture wider: the scene AND the ring
            // are stretched by the same X factor, so the ring still lands where the bullets go (read live from the settings)
            double sxv = TutorialStep10Math.ViewStretch(c != null ? c.G : null);
            GraphicsState stretchState = null;
            if (sxv != 1.0)
            {
                stretchState = g.Save();
                g.TranslateTransform(vcx, 0); g.ScaleTransform((float)sxv, 1f); g.TranslateTransform(-vcx, 0);
            }
            // horizon + floor (rolled with the lean: E clockwise, Q counter-clockwise, like step 8)
            double fx = Math.Sin(fpCam.Yaw), fz = Math.Cos(fpCam.Yaw);
            float hx, hy;
            if (fpCam.Project(fpCam.Cx + fx * 80, fpCam.Cy, fpCam.Cz + fz * 80, out hx, out hy))
                gx.Floor(view, hx, hy, fpCam.Roll, pal.Neon);
            // enemy first (the cover is nearer and hides it)
            SkelDraw.Draw(gx, sim.Foe, fpCam, foeStyleFp);
            DrawCoverFp(gx, s, view);
            if (stretchState != null) g.Restore(stretchState);   // marks below keep their shape; only their X is stretched
            // hits
            for (int i = 0; i < sim.Shots; i++)
            {
                float qx, qy;
                if (!fpCam.Project(sim.HitX[i], sim.HitY[i], sim.HitZ[i], out qx, out qy)) continue;
                qx = (float)TutorialStep10Math.StretchAbout(qx, vcx, sxv);
                double fresh = i == sim.Shots - 1 ? Math.Max(0, 1 - (sim.LocalMs - sim.LastShotMs) / 400.0) : 0;
                gx.Hole(qx, qy, gx.S(2.4f), fresh, 1, pal.Hot);
            }
            // 预瞄点; green ring = screen centre + the overlay's offset for your profile (same angle as in game)
            float px, py;
            bool preOk = fpCam.Project(sim.PreX, sim.PreY, sim.PreZ, out px, out py);
            px = (float)TutorialStep10Math.StretchAbout(px, vcx, sxv);
            double k = focal / sim.GameFocalPx;
            float rx = (float)TutorialStep10Math.RingViewX(vcx, sim.RingOffX, k, sxv), ry = (float)(vcy + sim.RingOffY * k);
            int phase = sim.Phase();
            if (preOk) Diamond(gx, px, py, gx.S(6), pal.Accent);
            if (preOk && phase == TutorialStep10Peek.PhasePulled)
                g.DrawLine(gx.Dashed(pal.Hot, 1.4f), rx, ry, px, py);
            Color ringCol = c.P != null ? FpRig.ParseColor(c.P.Color, pal.Neon) : pal.Neon;
            gx.Ring(rx, ry, gx.S(12), ringCol, Color.Black, 1.6f);   // v4.3: smaller, the enemy is 7 m away now
            // screen centre: a small dot with a dark rim, nothing else drawn on top of it
            gx.Dot(Color.Black, vcx, vcy, gx.S(3.2f));
            gx.Dot(pal.Text, vcx, vcy, gx.S(2));
            // contact flash at the head
            if (sim.ContactMs >= 0)
            {
                float qx, qy;
                if (fpCam.Project(sim.Foe.X[SkelJ.Head], sim.Foe.Y[SkelJ.Head], sim.Foe.Z[SkelJ.Head], out qx, out qy))
                {
                    qx = (float)TutorialStep10Math.StretchAbout(qx, vcx, sxv);
                    double age = sim.LocalMs - sim.ContactMs;
                    if (age < 600 && !reduced)
                    {
                        float rr = gx.S(12) + (float)(age / 600.0) * gx.S(16);
                        g.DrawEllipse(gx.P(TutorialGfx.A(pal.Hot, 1 - age / 600.0), 2f), qx - rr, qy - rr, 2 * rr, 2 * rr);
                    }
                }
            }
            // our rifle (bottom corner, left shoulder after 换手) + tracer / flash
            double since = sim.LastShotMs < 0 ? 1e9 : sim.LocalMs - sim.LastShotMs;
            float kick = since < 60 ? (float)(1 - since / 60.0) * gx.S(6) : 0f;
            if (view.Height > gx.S(150))
            {
                PointF muzzle = gx.Gun(view, 0f, 0f, kick, sim.MeP.Hand);
                if (since < 80 && !reduced)
                {
                    double life = 1 - since / 80.0;
                    g.DrawLine(gx.P(TutorialGfx.A(pal.Hot, life * 0.9), 1.6f), muzzle.X, muzzle.Y, rx, ry);
                    gx.Flash(muzzle, life);
                }
            }
            // camera roll cue (top centre): level line, rolled line, curved arrow (E clockwise / Q counter-clockwise)
            RollCue(gx, vcx, view.Top + gx.S(26), fpCam.Roll);
            // at most two tags: status (top-left) and contact time / the enemy's keys (top-right), away from the centre
            Color stc = phase == TutorialStep10Peek.PhasePulled || phase == TutorialStep10Peek.PhaseContact ? pal.Hot
                : (phase == TutorialStep10Peek.PhaseSettled ? pal.Neon : pal.Accent);
            RectangleF st = gx.Tag(PhaseText[phase], view.Left + gx.S(8), view.Top + gx.S(8), stc, 0);
            string tr = null; Color trc = pal.Hot;
            if (sim.ContactMs >= 0 && contactText.Length > 0 && !tight) tr = contactText;
            else if (sim.ContactMs < 0 && full && TutorialStep10Peek.MeSide[s] == 0) { tr = FoeKeys; trc = pal.Red; }
            if (tr != null)
            {
                float cw = gx.Measure(tr, gx.F.Tag).Width + gx.S(12);
                if (view.Right - gx.S(8) - cw > st.Right + gx.S(8)) gx.Tag(tr, view.Right - gx.S(8), view.Top + gx.S(8), trc, 2);
                else gx.Tag(tr, view.Left + gx.S(8), st.Bottom + gx.S(4), trc, 0);
            }
            // v4.3 (Leo): after contact, bottom-right: how little of the peeking body showed vs how far the ring was already
            // offset (your real settings), e.g. 「接触时露出 0.15 米 · 准星已偏 → 4.17%」 (built once per scene)
            if (sim.ContactExposureM >= 0 && view.Height > gx.S(150))
            {
                if (exposureBuiltScene != s || exposureText == null)
                {
                    exposureBuiltScene = s;
                    exposureText = TutorialStep10Math.ExposureText(sim.ContactExposureM, OffsetUnits.Format(sim.ContactRingOffX, OffsetAxis.X, c != null ? c.G : null),
                        TutorialStep10Peek.MeSide[s] != 0);
                }
                float ew = gx.Measure(exposureText, gx.F.Tag).Width + gx.S(12);
                if (ew <= view.Width - gx.S(16))
                    gx.Tag(exposureText, view.Right - gx.S(8), view.Bottom - gx.S(8) - gx.F.Tag.GetHeight(g) - gx.S(6), pal.Neon, 2);
            }
            // v4.3 (W6): stretched 画面比例 → one small tag bottom-left (cached; a new string only when the factor changes)
            if (sxv != 1.0 && view.Height > gx.S(150))
            {
                if (stretchTag == null || stretchTagS != sxv) { stretchTagS = sxv; stretchTag = TutorialStep10Math.StretchTagText(sxv); }
                gx.Tag(stretchTag, view.Left + gx.S(8), view.Bottom - gx.S(8) - gx.F.Tag.GetHeight(g) - gx.S(6), pal.Cyan, 0);
            }
            g.ResetClip();
            // legend under the view: ● 屏幕中心  ◯ 绿圈＝子弹落点  ◇ 预瞄点, then the one 示意 note (right)
            g.SetClip(box);
            float ly = view.Bottom + gx.S(3) + lh / 2f, lx = inr.Left + gx.S(4);
            gx.Dot(pal.Text, lx + gx.S(4), ly, gx.S(2.4f));
            lx += gx.S(12);
            gx.Text(LegDot, gx.F.Small, pal.Text, lx, ly - lh / 2f, 0);
            lx += gx.Measure(LegDot, gx.F.Small).Width + gx.S(14);
            float w2 = gx.S(18) + gx.Measure(LegRing, gx.F.Small).Width;
            if (lx + w2 <= inr.Right)
            {
                gx.Ring(lx + gx.S(6), ly, gx.S(11), ringCol, Color.Black, 1.4f);
                gx.Text(LegRing, gx.F.Small, ringCol, lx + gx.S(16), ly - lh / 2f, 0);
                lx += w2 + gx.S(14);
                float w3 = gx.S(18) + gx.Measure(LegPre, gx.F.Small).Width;
                if (lx + w3 <= inr.Right)
                {
                    Diamond(gx, lx + gx.S(5), ly, gx.S(4.5f), pal.Accent);
                    gx.Text(LegPre, gx.F.Small, pal.Accent, lx + gx.S(16), ly - lh / 2f, 0);
                    lx += w3 + gx.S(14);
                }
            }
            float nw = gx.Measure(NoteDemo, gx.F.Small).Width;
            if (lx + nw <= inr.Right - gx.S(4)) gx.Text(NoteDemo, gx.F.Small, pal.Muted, inr.Right - gx.S(4), ly - lh / 2f, 2);
            else if (lx + gx.Measure(NoteDemoShort, gx.F.Small).Width <= inr.Right - gx.S(4))
                gx.Text(NoteDemoShort, gx.F.Small, pal.Muted, inr.Right - gx.S(4), ly - lh / 2f, 2);
            g.ResetClip();
        }

        /// <summary>Roll cue: a faint level line, the line rolled by 'roll' (screen, + = clockwise) and a curved arrow over
        /// it in the roll direction. Nothing below about 1 degree.</summary>
        private static void RollCue(TutorialGfx gx, float x, float y, double roll)
        {
            double deg = roll * 180.0 / Math.PI;
            if (!(Math.Abs(deg) > 1.0)) return;
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            float half = gx.S(24);
            g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Muted, 160), 1f), x - half, y, x + half, y);
            float c = (float)Math.Cos(roll), sn = (float)Math.Sin(roll);
            g.DrawLine(gx.P(pal.Cyan, 1.8f), x - half * c, y - half * sn, x + half * c, y + half * sn);
            // arc over the top, ending with an arrow head on the side the roll goes to (clockwise = ends on the right)
            float r = gx.S(13);
            int dir = deg > 0 ? 1 : -1;
            double a0 = dir > 0 ? 200 : 340, sweep = dir * 120;
            g.DrawArc(gx.P(pal.Cyan, 1.4f), x - r, y - r, 2 * r, 2 * r, (float)a0, (float)sweep);
            double aEnd = (a0 + sweep) * Math.PI / 180.0, aPre = (a0 + sweep - dir * 18) * Math.PI / 180.0;
            gx.Arrow(pal.Cyan, 1.4f, (float)(x + r * Math.Cos(aPre)), (float)(y + r * Math.Sin(aPre)),
                (float)(x + r * Math.Cos(aEnd)), (float)(y + r * Math.Sin(aEnd)));
        }

        /// <summary>The cover's near face and open-end side face, flat, in the first-person view (the wall end is clipped
        /// back toward the corner when it would fall behind the camera).</summary>
        private void DrawCoverFp(TutorialGfx gx, int s, RectangleF view)
        {
            TutorialPalette pal = gx.Pal;
            double ox = TutorialStep10Peek.OpenX[s], z0 = TutorialStep10Peek.CovZ0[s], z1 = TutorialStep10Peek.CovZ1[s];
            double far = TutorialStep10Peek.CoverSide[s] < 0 ? TutorialStep10Peek.CovX0[s] : TutorialStep10Peek.CovX1[s];
            double h = TutorialStep10Peek.CoverH;
            Color face = TutorialPalette.Mix(pal.Surface, pal.SceneBg, 0.25), side = TutorialPalette.Mix(pal.Surface, pal.Accent, 0.12);
            // near face, shortened step by step until all four corners are in front of the camera
            for (int k = 0; k < 6; k++)
            {
                double fx = ox + (far - ox) * (1 - k / 6.0);
                if (Quad(gx, ox, 0, z0, fx, 0, z0, fx, h, z0, ox, h, z0, face)) break;
            }
            Quad(gx, ox, 0, z0, ox, 0, z1, ox, h, z1, ox, h, z0, side);
            float ax, ay, bx, by;
            if (fpCam.Project(ox, 0, z0, out ax, out ay) && fpCam.Project(ox, h, z0, out bx, out by))
                gx.G.DrawLine(gx.P(pal.Accent, 1.6f), ax, ay, bx, by);
        }

        private bool Quad(TutorialGfx gx, double x1, double y1, double z1, double x2, double y2, double z2,
            double x3, double y3, double z3, double x4, double y4, double z4, Color fill)
        {
            PointF[] q = gx.Pts(4);
            float a, b;
            if (!fpCam.Project(x1, y1, z1, out a, out b)) return false; q[0] = new PointF(a, b);
            if (!fpCam.Project(x2, y2, z2, out a, out b)) return false; q[1] = new PointF(a, b);
            if (!fpCam.Project(x3, y3, z3, out a, out b)) return false; q[2] = new PointF(a, b);
            if (!fpCam.Project(x4, y4, z4, out a, out b)) return false; q[3] = new PointF(a, b);
            gx.G.FillPolygon(gx.B(fill), q);
            gx.G.DrawPolygon(gx.P(TutorialGfx.A(gx.Pal.Border, 220), 1f), q);
            return true;
        }
    }
#endif
}
