using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.1 step 10 skeleton peek demo (src/TutorialStep10.cs: TutorialStep10Peek + TutorialStep10Flow, pure part,
    // STEP10_PURE_ONLY + SKEL_PURE_ONLY). Self-registers through a module initializer (Tests.cs Main needs no edit).
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V41Step10Init() { V41Step10Tests(); }

        static void V41Step10Tests()
        {
            S41Edge();
            S41Scenes();
            S41PreAimAtHead();
            S41Mirror();
            S41Determinism();
            S41KeyFrame();
            S41Fuzz();
            S41FlowLoop();
            S41FlowControls();
            S41RingFromProfile();
            S41Directions();
            S41FoePull();
            S41ScreenDir();
        }

        // ---- STEP10-FIX (17 Nov 2026): directions / signs of demo B

        /// <summary>Standalone overlay engine: hold the keys for 3 s (after an optional 换手 tap) with the same settings.</summary>
        static void EngineHold(Profile p, GlobalSettings g, bool right, bool left, bool switchHand, out double x, out double y)
        {
            MotionEngine e = new MotionEngine(); e.GameFocused = true;
            InputState none = new InputState(), tap = new InputState(); tap.HandSwitch = true;
            if (switchHand) { e.Update(0.016, none, p, g, PreviewMode.Live, 1); e.Update(0.016, tap, p, g, PreviewMode.Live, 1); }
            for (int i = 0; i < 40; i++) e.Update(0.05, none, p, g, PreviewMode.Live, 1);
            InputState k = new InputState(); k.Right = right; k.LeanR = right; k.Left = left; k.LeanL = left;
            for (int i = 0; i < 60; i++) e.Update(0.05, k, p, g, PreviewMode.Live, 1);
            x = e.OffsetX; y = e.OffsetY;
        }

        static GlobalSettings DemoG()
        {
            GlobalSettings g = new GlobalSettings();
            g.OnlyWhenGameFocused = false; g.ShowFadeMs = 0; g.HideFadeMs = 0; g.HandEnabled = true; g.Enabled = true;
            g.ResGameW = g.ResBaseW; g.ResGameH = g.ResBaseH;
            return g;
        }

        static void S41RingFromProfile()
        {
            bool ok = true; string info = "";
            Profile[] profs = new Profile[2];
            profs[0] = Profile.CreateLeoDefaultLegacy("t");
            profs[1] = Profile.CreateLeoDefaultLegacy("t2");
            profs[1].Dirs[3] = new DirSetting(-50, 6); profs[1].Dirs[2] = new DirSetting(40, 0);
            profs[1].LeanEX = 5; profs[1].LeanEY = -8; profs[1].LeanQX = -20; profs[1].LeanQY = -4;
            Profile.RecomputeDiagonals(profs[1]);
            for (int k = 0; k < profs.Length; k++)
            {
                GlobalSettings g = DemoG();
                TutorialStep10Peek p = new TutorialStep10Peek();
                p.Configure(profs[k].CloneValues(), g);
                double ex0, ey0, ex1, ey1;
                EngineHold(profs[k].CloneValues(), DemoG(), true, false, false, out ex0, out ey0);
                EngineHold(profs[k].CloneValues(), DemoG(), false, true, true, out ex1, out ey1);
                double[] wantX = { ex0, ex1, 0 }, wantY = { ey0, ey1, 0 };
                for (int s = 0; s < 3; s++)
                {
                    p.Start(s); p.AdvanceTo(TutorialStep10Peek.SceneMs);
                    if (Math.Abs(p.RingOffX - wantX[s]) > 1e-6 || Math.Abs(p.RingOffY - wantY[s]) > 1e-6) ok = false;
                    // at the end the ring (not the screen centre) sits on the head: project both through the eye camera
                    SkelCamera cam = new SkelCamera(); cam.SetFromEye(p.Me, 0, 0, p.GameFocalPx);
                    float rx, ry, hx, hy;
                    double d = Math.Sqrt((p.PreX - cam.Cx) * (p.PreX - cam.Cx) + (p.PreZ - cam.Cz) * (p.PreZ - cam.Cz));
                    double bx, by, bz; p.RingPoint(d, out bx, out by, out bz);
                    cam.Project(bx, by, bz, out rx, out ry); cam.Project(p.PreX, p.PreY, p.PreZ, out hx, out hy);
                    if (Math.Abs(rx - p.RingOffX) > 0.05 || Math.Abs(ry - p.RingOffY) > 0.05) ok = false;   // ring = centre + offset
                    if (Math.Abs(hx - rx) > 0.5 || Math.Abs(hy - ry) > 0.5) ok = false;                     // ring on the head
                    info += " p" + k + "s" + s + ":(" + F(p.RingOffX) + "," + F(p.RingOffY) + ")";
                }
                // D (向右走) puts the bullets LEFT, A RIGHT: Leo default D -30 + E +12 = -18; 换手 Q+A = the mirror +18
                if (k == 0 && !(wantX[0] < -1 && wantX[1] > 1 && Math.Abs(wantX[0] + wantX[1]) < 1e-6)) ok = false;
            }
            Check("S41-11 green ring = overlay MotionEngine offset of the profile (D left, A right, + lean, mirrored), on the head", ok, info);
        }

        static void S41Directions()
        {
            bool ok = true; string info = "";
            TutorialStep10Peek p = new TutorialStep10Peek();
            p.Configure(X18Pre("演示"), null); // X18: the pre-21 Nov default (Leo's tuned 换手 A item puts the ring left of centre)
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? 1 : -1;
                p.Start(s);
                double x0 = p.MeP.X;
                bool hiddenStart = side * (p.Me.X[SkelJ.Eye] - TutorialStep10Peek.OpenX[s]) < 0;   // behind the wall
                p.AdvanceTo(TutorialStep10Peek.SceneMs);
                double move = p.MeP.X - x0;
                SkelCamera cam = new SkelCamera(); cam.SetFromEye(p.Me, 0, 0, 900);
                float tx, ty, bx, by;
                cam.Project(TutorialStep10Peek.OpenX[s], 2.0, TutorialStep10Peek.CovZ0[s], out tx, out ty);
                cam.Project(TutorialStep10Peek.OpenX[s], 0.2, TutorialStep10Peek.CovZ0[s], out bx, out by);
                // a vertical corner leans clockwise on screen (top right of bottom) when the camera rolls clockwise (E)
                bool rollCw = tx - bx > 1;
                bool rollCcw = tx - bx < -1;
                bool past = side * (p.Me.X[SkelJ.Eye] - TutorialStep10Peek.OpenX[s]) > 0.3;          // past the corner
                bool wall = side > 0 ? TutorialStep10Peek.CovX1[s] <= TutorialStep10Peek.OpenX[s] + 1e-9 : TutorialStep10Peek.CovX0[s] >= TutorialStep10Peek.OpenX[s] - 1e-9;
                bool rifle = side * (p.Me.X[SkelJ.Stock] - p.Me.X[SkelJ.Pelvis]) > 0.05;               // right / left shoulder
                bool lean = side * p.MeP.Lean > 0.99 && side * (p.Me.X[SkelJ.Head] - p.Me.X[SkelJ.Pelvis]) > 0.25;
                bool roll = side * p.Me.Roll > 0 && (side > 0 ? rollCw : rollCcw)
                    && Math.Sign(p.Me.Roll) == side * Math.Sign(LeanStepMath.RollPerLean());          // = step 8 (Q ccw, E cw)
                bool hand = s == 0 ? p.MeP.Hand == 0 : p.MeP.Hand == 1;
                bool ringSide = side * p.RingOffX < 0;   // D+E: ring left of centre; Q+A after 换手: right
                bool mv = side * move > 0.45;
                if (!(hiddenStart && past && wall && rifle && lean && roll && hand && ringSide && mv)) ok = false;
                info += " s" + s + ": move=" + F(move) + " roll=" + F(p.Me.Roll * 180 / Math.PI) + "deg tilt=" + F(tx - bx) + " ringX=" + F(p.RingOffX)
                    + (hiddenStart ? "" : " NOTHIDDEN") + (past ? "" : " NOTPAST") + (wall ? "" : " WALL") + (rifle ? "" : " RIFLE") + (lean ? "" : " LEAN")
                    + (roll ? "" : " ROLL") + (hand ? "" : " HAND") + (ringSide ? "" : " RINGSIDE") + (mv ? "" : " MOVE");
            }
            Check("S41-12 D+E: strafe right past the corner, lean E, roll clockwise, right shoulder; Q+A = exact mirror", ok, info);
        }

        static void S41FoePull()
        {
            TutorialStep10Peek p = new TutorialStep10Peek();
            Profile flat = X18Pre("演示"); flat.SetStandPoint(0, 0); /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in has a 站立 point now; this case measures around the centre */
            p.Configure(flat, null);
            int s = TutorialStep10Peek.SceneFoe;
            p.Start(s);
            double f0 = p.FoeP.X, pre0 = p.PreX, head0 = p.Foe.X[SkelJ.Head];
            double preAtContact = 0, headAtContact = 0;
            while (p.LocalMs + TutorialStep10Peek.StepMs <= TutorialStep10Peek.SceneMs)
            {
                p.AdvanceTo(p.LocalMs + TutorialStep10Peek.StepMs);
                if (p.ContactMs >= 0 && headAtContact == 0) { preAtContact = p.PreX; headAtContact = p.Foe.X[SkelJ.Head]; }
            }
            // open side of scene 2 = -X (cover X 0 .. 1.7): the enemy steps out toward it, its head crosses our 预瞄点 edge
            bool ok = p.FoeP.X < f0 - 0.45 && head0 > pre0 && Math.Abs(headAtContact - preAtContact) < 0.05
                && p.RingOffX == 0 && p.RingOffY == 0 && TutorialStep10Peek.CovX0[s] >= TutorialStep10Peek.OpenX[s];
            Check("S41-13 被敌人拉: the enemy leaves its cover toward the open side, head appears on our 预瞄点", ok,
                "foe dX=" + F(p.FoeP.X - f0) + " pre0=" + F(pre0) + " head0=" + F(head0));
        }

        static void S41ScreenDir()
        {
            // ScreenDir is the exact inverse of SkelCamera.Project (Persp) for any yaw / pitch / roll
            TutorialRng r = new TutorialRng(99);
            SkelCamera cam = new SkelCamera();
            double worst = 0;
            for (int i = 0; i < 2000; i++)
            {
                double yaw = (r.Next() % 6283) / 1000.0 - 3.14, pitch = (r.Next() % 1000) / 1000.0 - 0.5, roll = (r.Next() % 400) / 1000.0 - 0.2;
                double ox = (r.Next() % 801) - 400.0, oy = (r.Next() % 601) - 300.0, f = 300 + r.Next() % 900;
                double dx, dy, dz;
                TutorialStep10Peek.ScreenDir(ox, oy, f, yaw, pitch, roll, out dx, out dy, out dz);
                cam.SetPersp(0, 0, 1, 1.6, 2, yaw, pitch, roll, f);
                float sx, sy;
                if (!cam.Project(1 + dx * 5, 1.6 + dy * 5, 2 + dz * 5, out sx, out sy)) { worst = 1e9; break; }
                worst = Math.Max(worst, Math.Max(Math.Abs(sx - ox), Math.Abs(sy - oy)));
            }
            Check("S41-14 ring screen offset <-> world direction (ScreenDir inverts SkelCamera.Project)", worst < 0.01, "worst px=" + F(worst));
        }

        static void S41Edge()
        {
            // cover left (side -1): the larger line; right (+1): the smaller; a point past it is visible
            double e = TutorialStep10Peek.EdgeAt(0, 0, 0.3, 0.84, 0.96, -1, 5.6);
            double a = 0.3 * 5.6 / 0.84, b = 0.3 * 5.6 / 0.96;
            bool ok = Math.Abs(e - Math.Max(a, b)) < 1e-12
                && Math.Abs(TutorialStep10Peek.EdgeAt(0, 0, -0.3, 0.84, 0.96, 1, 5.6) + Math.Max(a, b)) < 1e-12
                && TutorialStep10Peek.EdgeAt(0, 1, 0.3, 1, 1, -1, 5) == 0.3;   // degenerate depth -> the corner X
            // LineClear agrees with EdgeAt for scene 0 along a sweep of eye positions
            for (int i = 0; i <= 200 && ok; i++)
            {
                double ex = -0.2 + i * 0.006;
                double fx = TutorialStep10Peek.FoeX[0], fz = TutorialStep10Peek.FoeZ[0];
                double edge = TutorialStep10Peek.EdgeAt(ex, 0.1, TutorialStep10Peek.OpenX[0], TutorialStep10Peek.CovZ0[0], TutorialStep10Peek.CovZ1[0], -1, fz);
                bool vis = TutorialStep10Peek.LineClear(0, ex, 0.1, fx, fz);
                if (Math.Abs(edge - fx) > 1e-3 && vis != (fx > edge)) ok = false;
            }
            Check("S41-1 corner edge line / line of sight agree", ok, "edge=" + F(e));
        }

        static void S41Scenes()
        {
            bool ok = true; string info = "";
            TutorialStep10Peek p = new TutorialStep10Peek();
            for (int s = 0; s < TutorialStep10Peek.Scenes; s++)
            {
                p.Start(s);
                bool hiddenAtStart = !p.Visible;
                double lagAtContact = -1, minLagAfter = 1e9;
                bool pulled = false;
                while (p.LocalMs + TutorialStep10Peek.StepMs <= TutorialStep10Peek.SceneMs)
                {
                    p.AdvanceTo(p.LocalMs + TutorialStep10Peek.StepMs);
                    if (p.Phase() == TutorialStep10Peek.PhasePulled) pulled = true;
                    if (p.ContactMs >= 0 && lagAtContact < 0) lagAtContact = p.LagRad;
                    if (p.ContactMs >= 0) minLagAfter = Math.Min(minLagAfter, p.LagRad);
                }
                double cg = p.ContactGameS;
                bool contact = p.ContactMs > TutorialStep10Peek.KeysAt && p.ContactMs < TutorialStep10Peek.SceneMs - 2000;
                bool shots = p.Shots == TutorialStep10Peek.ShotsMax && p.LastShotMs < TutorialStep10Peek.SceneMs - TutorialStep10Math.FadeMs - 600;
                bool settled = p.LagRad < TutorialStep10Peek.SettleRad && p.Phase() == TutorialStep10Peek.PhaseSettled;
                // our own peek scenes show a visible pull; every scene ends settled on the head
                bool pull = s == TutorialStep10Peek.SceneFoe || (pulled && p.MaxLagRad > TutorialStep10Peek.PullRad);
                if (!(hiddenAtStart && contact && shots && settled && pull)) ok = false;
                info += " s" + s + ":contact=" + F(cg) + "s maxLag=" + F(p.MaxLagRad * 180 / Math.PI) + "deg endLag=" + F(p.LagRad * 180 / Math.PI)
                    + " lastShot=" + F(p.LastShotMs) + (hiddenAtStart ? "" : " VISIBLE@0") + (contact ? "" : " NOCONTACT") + (shots ? "" : " SHOTS")
                    + (settled ? "" : " UNSETTLED") + (pull ? "" : " NOPULL");
            }
            Check("S41-2 each scene: hidden -> contact -> 5 shots -> settled; own peeks are pulled", ok, info);
            // D+E reaches contact sooner than D alone or E alone for the same corner (Leo's statement, 示意 model)
            double de = TutorialStep10Math.ClearTime(true, true), d = TutorialStep10Math.ClearTime(true, false), e = TutorialStep10Math.ClearTime(false, true);
            Check("S41-3 D+E clears sooner than D / E alone", de < d && de < e, "");
        }

        static void S41PreAimAtHead()
        {
            // the 预瞄点 just before contact sits where the head appears (within ~a head radius + one step of motion)
            bool ok = true; string info = "";
            TutorialStep10Peek p = new TutorialStep10Peek();
            for (int s = 0; s < TutorialStep10Peek.Scenes; s++)
            {
                p.Start(s);
                double px = 0, hx = 0, py = 0, hy = 0;
                while (p.ContactMs < 0 && p.LocalMs < TutorialStep10Peek.SceneMs - 1)
                {
                    px = p.PreX; py = p.PreY; hx = p.Foe.X[SkelJ.Head]; hy = p.Foe.Y[SkelJ.Head];
                    p.AdvanceTo(p.LocalMs + TutorialStep10Peek.StepMs);
                }
                double dx = Math.Abs(px - hx), dy = Math.Abs(py - hy);
                if (!(dx < SkelDims.HeadR * 1.5 && dy < 1e-6)) ok = false;
                info += " s" + s + ":dx=" + F(dx);
            }
            Check("S41-4 pre-aim point = where the head appears", ok, info);
        }

        static void S41Mirror()
        {
            TutorialStep10Peek a = new TutorialStep10Peek(), b = new TutorialStep10Peek();
            a.Start(0); b.Start(1);
            bool ok = true; double worst = 0;
            for (double t = 0; t <= TutorialStep10Peek.SceneMs; t += 40)
            {
                a.AdvanceTo(t); b.AdvanceTo(t);
                worst = Math.Max(worst, Math.Abs(a.MeP.X + b.MeP.X));
                if (Math.Abs(a.MeP.X + b.MeP.X) > 1e-9 || Math.Abs(a.MeP.Lean + b.MeP.Lean) > 1e-9) ok = false;
                if (b.KeysDown && b.MeP.Hand != 1) ok = false;
                if (a.MeP.Hand != 0) ok = false;
            }
            double ca = a.ContactMs, cb = b.ContactMs;
            ok = ok && ca > 0 && cb > 0 && Math.Abs(ca - cb) <= 3 * TutorialStep10Peek.StepMs;
            Check("S41-5 换手 Q+A scene mirrors 右手 D+E (rifle on the left)", ok, "contact " + F(ca) + " / " + F(cb) + " worst=" + F(worst));
        }

        static void S41Determinism()
        {
            // frame rate independent: random frame sizes vs fixed steps give the same state
            TutorialStep10Peek a = new TutorialStep10Peek(), b = new TutorialStep10Peek();
            TutorialRng r = new TutorialRng(7);
            bool ok = true;
            for (int s = 0; s < 3; s++)
            {
                a.Start(s); b.Start(s);
                double t = 0;
                while (t < TutorialStep10Peek.SceneMs)
                {
                    t += 1 + r.Next() % 60;
                    a.AdvanceTo(t);
                }
                b.AdvanceTo(t);
                if (a.LocalMs != b.LocalMs || a.AimYaw != b.AimYaw || a.Shots != b.Shots || a.ContactMs != b.ContactMs) ok = false;
            }
            Check("S41-6 deterministic at any frame rate", ok, "");
        }

        static void S41KeyFrame()
        {
            TutorialStep10Peek p = new TutorialStep10Peek();
            bool ok = true;
            for (int s = 0; s < 3; s++)
            {
                p.SeekKeyFrame(s);
                if (!(p.Shots >= 2 && p.ContactMs >= 0 && p.Visible && p.LocalMs <= TutorialStep10Peek.SceneMs)) ok = false;
            }
            Check("S41-7 reduced-motion still frame shows contact + hits", ok, "");
        }

        static void S41Fuzz()
        {
            TutorialStep10Peek p = new TutorialStep10Peek();
            TutorialRng r = new TutorialRng(41);
            bool ok = true;
            double[] bad = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -50, 1e12 };
            for (int i = 0; i < 4000 && ok; i++)
            {
                if (i % 200 == 0) p.Start((int)(r.Next() % 7) - 2);
                double t = (r.Next() & 15) == 0 ? bad[r.Next() % (uint)bad.Length] : p.LocalMs + r.Next() % 90;
                p.AdvanceTo(t);
                if (double.IsNaN(p.AimYaw) || double.IsNaN(p.AimPitch) || double.IsNaN(p.PreX) || double.IsNaN(p.LagRad)
                    || p.LocalMs > TutorialStep10Peek.SceneMs + 1e-9 || p.Scene < 0 || p.Scene > 2 || p.Shots > TutorialStep10Peek.ShotsMax) ok = false;
                for (int j = 0; j < SkelJ.Count; j++) if (double.IsNaN(p.Me.X[j]) || double.IsNaN(p.Foe.Z[j])) ok = false;
            }
            Check("S41-8 peek sim fuzz (NaN / inf / backwards times)", ok, "");
        }

        static void S41FlowLoop()
        {
            TutorialStep10Flow f = new TutorialStep10Flow(); f.Reset();
            f.Select(TutorialStep10Flow.ModePeek);
            int watched = 0; bool stayed = true;
            for (double t = 0; t < 3 * TutorialStep10Math.PeekTotalMs; t += 16)
            {
                f.Tick(16, false, 0, false);
                if (f.JustWatched) watched++;
                if (f.Mode != TutorialStep10Flow.ModePeek || f.PeekMs < 0 || f.PeekMs >= TutorialStep10Math.PeekTotalMs) stayed = false;
            }
            Check("S41-9 demo B loops smoothly (stays in B, done once)", stayed && watched == 1 && f.Done && f.Prompt == 2, "");
        }

        static void S41FlowControls()
        {
            TutorialStep10Flow f = new TutorialStep10Flow(); f.Reset();
            f.SelectScene(2);
            bool a = f.Mode == TutorialStep10Flow.ModePeek && f.PeekMs == 2 * TutorialStep10Math.PeekSceneMs && f.Prompt == 1;
            f.TogglePause();
            double held = f.PeekMs;
            for (int i = 0; i < 1000; i++) f.Tick(16, false, 0, false);
            bool b = f.Paused && f.PeekMs == held && f.Mode == TutorialStep10Flow.ModePeek;
            f.Replay();
            bool c = !f.Paused && f.PeekMs == 0;
            f.TogglePause();
            f.Tick(16, true, 0, true);   // live jump while paused -> demo A, unpaused
            bool d = f.Mode == TutorialStep10Flow.ModeJump && !f.Paused;
            f.SelectScene(-1); f.SelectScene(3);
            bool e = f.Mode == TutorialStep10Flow.ModeJump;
            Check("S41-10 flow: scene chips, pause holds, replay, live jump", a && b && c && d && e, "");
        }
    }
}
