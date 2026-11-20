using System;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.1 TUT41: shared skeleton rig (src/TutorialSkeleton.cs, pure part compiled with SKEL_PURE_ONLY).
    // Self-registers through a module initializer (Tests.cs Main needs no edit).
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V41SkelInit() { V41SkelTests(); }

        static double Dist(SkelRig r, int a, int b)
        {
            double dx = r.X[a] - r.X[b], dy = r.Y[a] - r.Y[b], dz = r.Z[a] - r.Z[b];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static void V41SkelTests()
        {
            SkelRig r = new SkelRig();
            // SK1 upright: head above the pelvis, eye in front, feet on the ground, bone lengths kept by IK
            r.Solve(SkelPose.Standing(0, 0, 0));
            bool up = Math.Abs(r.X[SkelJ.Head]) < 1e-9 && r.Y[SkelJ.Head] > r.Y[SkelJ.Pelvis] + 0.6 && r.Z[SkelJ.Eye] > r.Z[SkelJ.Head]
                && r.Y[SkelJ.FootL] < 0.1 && r.Y[SkelJ.FootR] < 0.1 && Math.Abs(r.Y[SkelJ.Eye] - SkelDims.PelvisH - SkelDims.EyeAbovePelvis) < 1e-9;
            bool lens = Math.Abs(Dist(r, SkelJ.ShoulderR, SkelJ.ElbowR) - SkelDims.UpperArm) < 1e-6 && Math.Abs(Dist(r, SkelJ.ElbowR, SkelJ.HandR) - SkelDims.Forearm) < 1e-6
                && Math.Abs(Dist(r, SkelJ.ShoulderL, SkelJ.ElbowL) - SkelDims.UpperArm) < 1e-6 && Math.Abs(Dist(r, SkelJ.HipL, SkelJ.KneeL) - SkelDims.Thigh) < 1e-6
                && Math.Abs(Dist(r, SkelJ.Stock, SkelJ.Muzzle) - SkelDims.RifleLen) < 1e-9;
            Check("SK1 skeleton upright: head over pelvis, eye forward, feet on the ground, limb + rifle lengths kept", up && lens, "");

            // SK2 hip fire: bullet line from the muzzle crosses the screen-centre line at 25 m (and not at 10 m)
            double bx, by, bz, vx, vy, vz;
            double tb = (SkelDims.ZeroM - r.Z[SkelJ.Muzzle] + r.Z[SkelJ.Eye]) / r.BulletZ;
            r.BulletPoint(tb, out bx, out by, out bz); r.ViewPoint(SkelDims.ZeroM, out vx, out vy, out vz);
            double miss25 = Math.Sqrt((bx - vx) * (bx - vx) + (by - vy) * (by - vy) + (bz - vz) * (bz - vz));
            r.BulletPoint(10, out bx, out by, out bz); r.ViewPoint(10, out vx, out vy, out vz);
            double gap10 = Math.Abs(bx - vx) + Math.Abs(by - vy);
            Check("SK2 bullet line meets the centre line at the 25 m zero point; offset at 10 m (bottom-right of centre)",
                miss25 < 1e-6 && gap10 > 0.02 && r.X[SkelJ.Muzzle] > r.X[SkelJ.Eye] && r.Y[SkelJ.Muzzle] < r.Y[SkelJ.Eye], miss25 + " " + gap10);

            // SK3 lean: E moves the eye right by PeekModel.LeanShiftM, Q left; legs stay; pelvis fixed; roll sign = step 8
            SkelPose p = SkelPose.Standing(0, 0, 0); double eye0 = r.X[SkelJ.Eye];
            p.Lean = 1; r.Solve(p); double eyeE = r.X[SkelJ.Eye], footE = r.X[SkelJ.FootR], rollE = r.Roll;
            p.Lean = -1; r.Solve(p); double eyeQ = r.X[SkelJ.Eye], rollQ = r.Roll;
            bool lean = Math.Abs(eyeE - eye0 - PeekModel.LeanShiftM) < 1e-9 && Math.Abs(eyeQ - eye0 + PeekModel.LeanShiftM) < 1e-9
                && Math.Abs(footE - SkelDims.FootHalf) < 1e-9 && Math.Abs(r.X[SkelJ.Pelvis]) < 1e-12
                && rollQ < 0 && rollE > 0 && Math.Abs(rollE - LeanStepMath.RollPerLean()) < 1e-12;
            Check("SK3 lean rotates spine/neck around the pelvis: eye ±0.40 m, legs + pelvis stay, Q roll = counter-clockwise like step 8", lean, eyeE + " " + eyeQ);

            // SK4 换手 mirrors the rifle to the left shoulder
            p = SkelPose.Standing(0, 0, 0); p.Hand = 1; r.Solve(p);
            Check("SK4 换手: stock on the left shoulder, muzzle left of the eye", r.X[SkelJ.Stock] < 0 && r.X[SkelJ.Muzzle] < r.X[SkelJ.Eye], "");

            // SK5 yaw: facing +X (yaw 90°) puts the eye at +X of the pelvis, right side at -Z
            p = SkelPose.Standing(2, 3, Math.PI / 2); r.Solve(p);
            Check("SK5 yaw 90°: eye toward +X, right shoulder toward -Z, view along +X",
                r.X[SkelJ.Eye] > 2 && r.Z[SkelJ.ShoulderR] < 3 && Math.Abs(r.ViewX - 1) < 1e-9, "");

            // SK6 PeekPose: D+E moves the pelvis right and leans right; Q+A moves left, leans left, Hand = 1; capped strafe
            SkelPose a = new SkelPose(), b = new SkelPose();
            SkelMotion.PeekPose(ref a, 0, 0, 0, 1, 0.5, true, true, 0.6);
            SkelMotion.PeekPose(ref b, 0, 0, 0, -1, 0.5, true, true, 0.6);
            bool pk = a.X > 0 && a.X <= 0.6 + 1e-12 && a.Lean > 0.99 && a.Hand == 0 && b.X < 0 && b.Lean < -0.99 && b.Hand == 1;
            SkelPose c0 = new SkelPose(); SkelMotion.PeekPose(ref c0, 0, 0, 0, 1, 0, true, true, 0.6);
            Check("SK6 PeekPose: D+E right + lean E, Q+A left + lean Q + 换手, strafe capped, rest at t=0", pk && c0.X == 0 && c0.Lean == 0, a.X + " " + b.X);

            // SK7 D+E clears a corner sooner than D only (Leo's statement, PeekModel)
            double sD, lD, sDE, lDE;
            SkelMotion.Peek(0.2, true, false, -1, out sD, out lD); SkelMotion.Peek(0.2, true, true, -1, out sDE, out lDE);
            SkelPose pd = new SkelPose(), pde = new SkelPose();
            SkelMotion.PeekPose(ref pd, 0, 0, 0, 1, 0.2, true, false, -1); SkelMotion.PeekPose(ref pde, 0, 0, 0, 1, 0.2, true, true, -1);
            SkelRig rd = new SkelRig(), rde = new SkelRig(); rd.Solve(pd); rde.Solve(pde);
            Check("SK7 at 0.2 s the D+E eye is further out than D alone", rde.X[SkelJ.Eye] > rd.X[SkelJ.Eye] + 0.1, rde.X[SkelJ.Eye] + " " + rd.X[SkelJ.Eye]);

            // SK8 AimAt: the view line passes through the target (also while leaning)
            p = SkelPose.Standing(0, 0, 0.3); p.Lean = 0.7;
            SkelMotion.AimAt(ref p, r, 1.5, 1.6, 8);
            double t = (8 - r.Z[SkelJ.Eye]) / r.ViewZ; double ax, ay, az; r.ViewPoint(t, out ax, out ay, out az);
            Check("SK8 AimAt: view line hits the target point within 2 cm", Math.Abs(ax - 1.5) < 0.02 && Math.Abs(ay - 1.6) < 0.02, ax + " " + ay);

            // SK9 cameras: Top / Back / Side / Persp map as documented; depth order; behind-camera rejected
            SkelCamera cam = new SkelCamera(); float sx, sy;
            cam.SetTop(100, 100, 0, 0, 10, 0); cam.Project(1, 5, 2, out sx, out sy);
            bool top = sx == 110 && sy == 80;
            cam.SetTop(100, 100, 0, 0, 10, Math.PI / 2); cam.Project(1, 0, 0, out sx, out sy);   // +X drawn up
            top = top && Math.Abs(sx - 100) < 1e-3 && Math.Abs(sy - 90) < 1e-3;
            cam.SetBack(0, 0, 0, 0, 0, 0, 100); cam.Project(0.5, 1, 3, out sx, out sy);
            bool back = sx == 50 && sy == -100 && cam.Depth(0, 0, 5) > cam.Depth(0, 0, 1);
            cam.SetSide(0, 0, 0, 0, 0, 0, 100); cam.Project(0.5, 1, 3, out sx, out sy);
            bool side = sx == 300 && sy == -100 && cam.Depth(0.5, 0, 0) < cam.Depth(-0.5, 0, 0);
            cam.SetPersp(0, 0, 0, 1, 0, 0, 0, 0, 1000);
            bool front = cam.Project(1, 1, 10, out sx, out sy) && Math.Abs(sx - 100) < 1e-3 && Math.Abs(sy) < 1e-3;
            bool behind = !cam.Project(0, 1, -1, out sx, out sy);
            cam.SetPersp(0, 0, 0, 0, 0, 0, 0, 0.2, 1000); cam.Project(1, 0, 10, out sx, out sy);
            bool roll = sy > 0;   // + roll = clockwise: a point right of centre goes down
            Check("SK9 SkelCamera Top/Back/Side/Persp mapping, depth, roll, behind-camera", top && back && side && front && behind && roll, "");

            // SK10 SetFromEye: the rig's view point projects to the screen centre, its bullet point at 10 m bottom-right
            p = SkelPose.Standing(0, 0, 0); r.Solve(p);
            cam.SetFromEye(r, 500, 300, 1000);
            r.ViewPoint(10, out ax, out ay, out az); cam.Project(ax, ay, az, out sx, out sy);
            float bsx, bsy; r.BulletPoint(9.5, out bx, out by, out bz); cam.Project(bx, by, bz, out bsx, out bsy);
            Check("SK10 first-person from the eye: centre line = screen centre, bullet at ~10 m = bottom-right of centre",
                Math.Abs(sx - 500) < 1e-3 && Math.Abs(sy - 300) < 1e-3 && bsx > 500 && bsy > 300, bsx + "," + bsy);

            // SK11 Follow / Hold01 / ClearOfCorner / TwoBone robustness
            double f = 0; for (int i = 0; i < 60; i++) f = SkelMotion.Follow(f, 1, 16.7, 120);
            bool fol = f > 0.99 && f < 1 && SkelMotion.Follow(0.3, 1, 16, 0) == 1 && SkelMotion.Follow(double.NaN, 2, 16, 50) == 2;
            bool hold = SkelMotion.Hold01(0, 100, 500, 100) == 0 && SkelMotion.Hold01(300, 100, 500, 100) == 1 && SkelMotion.Hold01(700, 100, 500, 100) == 0;
            // eye (0,0) -> target (2,10); corner at (1,2): corner is left of the line? line x at z=2 is 0.4 -> corner right of line
            bool corner = SkelMotion.ClearOfCorner(0, 0, 2, 10, 1, 2, 1) && !SkelMotion.ClearOfCorner(0, 0, 2, 10, 1, 2, -1);
            double jx, jy, jz; SkelMotion.TwoBone(0, 0, 0, 5, 0, 0, 1, 1, 0, 1, 0, out jx, out jy, out jz);   // out of reach: straight
            bool ik = !double.IsNaN(jx) && Math.Abs(Math.Sqrt(jx * jx + jy * jy + jz * jz) - 1) < 1e-6;
            SkelMotion.TwoBone(0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 0, out jx, out jy, out jz);
            ik = ik && !double.IsNaN(jx) && !double.IsNaN(jy);
            SkelPose nan = SkelPose.Standing(0, 0, 0); nan.Lean = double.NaN; nan.Hand = double.NaN; r.Solve(nan);
            bool finite = true; for (int j = 0; j < SkelJ.Count; j++) if (double.IsNaN(r.X[j]) || double.IsNaN(r.Y[j])) finite = false;
            Check("SK11 Follow lag, Hold01 envelope, ClearOfCorner, TwoBone out-of-reach / zero length, NaN pose stays finite", fol && hold && corner && ik && finite, f.ToString());

            // SK13 step 8 skeleton panel layout (LeanStepMath, v4.1)
            bool lay = LeanStepMath.SkelPanelW(1000, 1, false) == 380 && LeanStepMath.SkelPanelW(600, 1.5, true) == 252
                && LeanStepMath.SkelPanelW(400, 1.5, true) == 0 && LeanStepMath.SkelPanelW(double.NaN, 1, false) == 0
                && LeanStepMath.FreezeMs > LeanStepMath.QFireFrom && LeanStepMath.FreezeMs < LeanStepMath.QTo;
            double sc = LeanStepMath.SkelScale(300, 500), sc2 = LeanStepMath.SkelScale(1000, 300);
            lay = lay && Math.Abs(sc - 300 / 1.75) < 1e-9 && Math.Abs(sc2 - 300 * 0.84 / 1.95) < 1e-9 && LeanStepMath.SkelScale(0, 0) == 1;
            Check("SK13 step 8 skeleton panel: width share, too-narrow scene -> no panel, scale fits the figure, reduced-motion stop inside the Q hold", lay, sc + " " + sc2);

            // SK12 Solve allocates nothing
            long before = GC.GetAllocatedBytesForCurrentThread();
            SkelPose q = SkelPose.Standing(0, 0, 0);
            for (int i = 0; i < 2000; i++) { q.Lean = Math.Sin(i * 0.01); q.Walk = i * 0.1; q.Stride = 1; r.Solve(q); SkelMotion.AimAt(ref q, r, 1, 1.6, 10); }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("SK12 Solve + AimAt: 0 bytes allocated over 2000 frames", alloc == 0, alloc + " B");
        }
    }
}
