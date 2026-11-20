// TutorialSkeleton.cs -- 首次设置教程 v4.1: shared skeleton / bone rig for the 侧身 (歪头), 侧拉 (D+E / Q+A) and 被敌人拉 demos.
// Owner: TUT41. API: /home/dev/tarkov-crosshair-dev/v4-requests/SKEL-api.md (keep stable; append changes there).
// Pure part (SkelJ, SkelDims, SkelPose, SkelRig, SkelCamera, SkelMotion) is System only: tests-v4 defines SKEL_PURE_ONLY.
// Drawing part (SkelStyle, SkelDraw) is System.Drawing only (no WinForms). C# 5. No allocation per frame.
// Every size, angle and timing here is 示意 (illustration), not measured game data.
using System;
#if !SKEL_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>Joint indices of SkelRig (X[j], Y[j], Z[j]).</summary>
    public static class SkelJ
    {
        public const int Pelvis = 0, Spine = 1, Chest = 2, Neck = 3, Head = 4, Eye = 5,
            ShoulderL = 6, ElbowL = 7, HandL = 8, ShoulderR = 9, ElbowR = 10, HandR = 11,
            HipL = 12, KneeL = 13, FootL = 14, HipR = 15, KneeR = 16, FootR = 17,
            Stock = 18, Muzzle = 19, Count = 20;
    }

    /// <summary>Body sizes in metres (示意). Y up, ground = 0.</summary>
    public static class SkelDims
    {
        public const double PelvisH = 0.95, SpineLen = 0.24, ChestLen = 0.26, NeckLen = 0.10, HeadUp = 0.12, HeadR = 0.115,
            EyeFwd = 0.085, EyeUp = 0.02, ShoulderHalf = 0.19, ShoulderDrop = 0.03, HipHalf = 0.11,
            UpperArm = 0.30, Forearm = 0.28, Thigh = 0.47, Shin = 0.46, FootHalf = 0.13,
            RifleLen = 0.88, StockSide = 0.15, StockDrop = 0.08, StockFwd = 0.06, GripAlong = 0.22, SupportAlong = 0.52;
        /// <summary>Eye height above the pelvis when upright.</summary>
        public const double EyeAbovePelvis = SpineLen + ChestLen + NeckLen + HeadUp + EyeUp;
        /// <summary>Spine angle of a full lean: the eye moves PeekModel.LeanShiftM sideways (示意, ~33°).</summary>
        public static readonly double LeanMaxRad = Math.Asin(PeekModel.LeanShiftM / EyeAbovePelvis);
        /// <summary>Camera (screen) roll of a full lean, the same as the 侧身 step's scene roll (6°, 示意).</summary>
        public const double CameraRollDeg = 6.0;
        /// <summary>Hip-fire zero distance: the bullet line crosses the screen-centre line here (Leo: 25 m).</summary>
        public const double ZeroM = TutorialGeometry.ZeroDistanceM;
    }

    /// <summary>Pose input. World: X right, Z forward ("up" in the top-down view), Y up; metres.</summary>
    public struct SkelPose
    {
        /// <summary>Pelvis position on the ground (world m).</summary>
        public double X, Z;
        /// <summary>Body facing (rad): 0 = +Z, + = turn right (clockwise seen from above).</summary>
        public double Yaw;
        /// <summary>View / rifle direction relative to the body (rad, + = right) and pitch (rad, + = up).</summary>
        public double AimYaw, AimPitch;
        /// <summary>-1 = full left lean (Q) .. +1 = full right lean (E). Spine + neck rotate around the pelvis.</summary>
        public double Lean;
        /// <summary>0 = rifle on the right shoulder, 1 = left shoulder (换手); values between blend.</summary>
        public double Hand;
        /// <summary>Legs: gait phase (rad), stride 0 (standing) .. 1, local move direction (x right, z forward; any length).</summary>
        public double Walk, Stride, MoveX, MoveZ;

        public static SkelPose Standing(double x, double z, double yaw)
        {
            SkelPose p = new SkelPose();
            p.X = x; p.Z = z; p.Yaw = yaw;
            return p;
        }
    }

    /// <summary>
    /// Forward kinematics. Solve(pose) fills world joints X/Y/Z[SkelJ.*], the view (screen-centre) direction View*, the
    /// bullet direction Bullet* (from the muzzle, crossing the view line at SkelDims.ZeroM), BodyAngle and the camera Roll.
    /// One instance per figure; Solve allocates nothing.
    /// </summary>
    public sealed class SkelRig
    {
        public readonly double[] X = new double[SkelJ.Count], Y = new double[SkelJ.Count], Z = new double[SkelJ.Count];
        public double ViewX, ViewY, ViewZ, BulletX, BulletY, BulletZ;
        /// <summary>Spine angle (rad, + = right) and screen roll (rad, GDI+ clockwise +; Q = counter-clockwise).</summary>
        public double BodyAngle, Roll;
        public SkelPose Pose;

        // local (body frame) scratch
        private readonly double[] lx = new double[SkelJ.Count], ly = new double[SkelJ.Count], lz = new double[SkelJ.Count];

        public void Solve(SkelPose p)
        {
            Pose = p;
            double lean = Clamp(p.Lean, -1, 1), hand = Clamp(p.Hand, 0, 1);
            double ph = SkelDims.PelvisH;
            // ---- upright upper body (local: x right, y up, z forward)
            Set(SkelJ.Pelvis, 0, ph, 0);
            Set(SkelJ.Spine, 0, ph + SkelDims.SpineLen, 0);
            double chestY = ph + SkelDims.SpineLen + SkelDims.ChestLen;
            Set(SkelJ.Chest, 0, chestY, 0);
            Set(SkelJ.Neck, 0, chestY + SkelDims.NeckLen, 0);
            Set(SkelJ.Head, 0, chestY + SkelDims.NeckLen + SkelDims.HeadUp, 0.01);
            double eyeY = chestY + SkelDims.NeckLen + SkelDims.HeadUp + SkelDims.EyeUp;
            Set(SkelJ.Eye, 0, eyeY, SkelDims.EyeFwd);
            Set(SkelJ.ShoulderL, -SkelDims.ShoulderHalf, chestY - SkelDims.ShoulderDrop, 0);
            Set(SkelJ.ShoulderR, SkelDims.ShoulderHalf, chestY - SkelDims.ShoulderDrop, 0);
            double side = 1 - 2 * hand;
            Set(SkelJ.Stock, side * SkelDims.StockSide, chestY - SkelDims.StockDrop, SkelDims.StockFwd);
            // ---- lean: everything above the pelvis rotates around it in the body's frontal plane (legs stay)
            double a = lean * SkelDims.LeanMaxRad;
            BodyAngle = a;
            Roll = lean * SkelDims.CameraRollDeg * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            for (int j = SkelJ.Spine; j <= SkelJ.ShoulderR; j++) RotLean(j, ca, sa, ph);
            RotLean(SkelJ.Stock, ca, sa, ph);
            // view direction (the mouse: lean moves the eye, not where it looks)
            double cp = Math.Cos(p.AimPitch), sp = Math.Sin(p.AimPitch), cy = Math.Cos(p.AimYaw), sy = Math.Sin(p.AimYaw);
            double vx = sy * cp, vy = sp, vz = cy * cp;
            // rifle: stock in the shooting shoulder pocket, pointing at the zero point on the view line (hip fire)
            double stx = lx[SkelJ.Stock], sty = ly[SkelJ.Stock], stz = lz[SkelJ.Stock];
            double zx = lx[SkelJ.Eye] + vx * SkelDims.ZeroM, zy = ly[SkelJ.Eye] + vy * SkelDims.ZeroM, zz = lz[SkelJ.Eye] + vz * SkelDims.ZeroM;
            double bx = zx - stx, by = zy - sty, bz = zz - stz;
            double bl = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (!(bl > 1e-9)) { bx = 0; by = 0; bz = 1; bl = 1; }
            bx /= bl; by /= bl; bz /= bl;
            Set(SkelJ.Muzzle, stx + bx * SkelDims.RifleLen, sty + by * SkelDims.RifleLen, stz + bz * SkelDims.RifleLen);
            double gx = stx + bx * SkelDims.GripAlong, gy = sty + by * SkelDims.GripAlong - 0.05, gz = stz + bz * SkelDims.GripAlong;
            double ux = stx + bx * SkelDims.SupportAlong, uy = sty + by * SkelDims.SupportAlong - 0.03, uz = stz + bz * SkelDims.SupportAlong;
            // right-handed: right hand on the grip, left hand on the handguard; 换手 swaps them (blend)
            Set(SkelJ.HandR, Lerp(gx, ux, hand), Lerp(gy, uy, hand), Lerp(gz, uz, hand));
            Set(SkelJ.HandL, Lerp(ux, gx, hand), Lerp(uy, gy, hand), Lerp(uz, gz, hand));
            // elbows bend down and out in the leaned body frame
            Elbow(SkelJ.ShoulderL, SkelJ.HandL, SkelJ.ElbowL, -0.7 * ca - sa, 0.7 * sa - ca);
            Elbow(SkelJ.ShoulderR, SkelJ.HandR, SkelJ.ElbowR, 0.7 * ca - sa, -0.7 * sa - ca);
            double rvx = vx, rvy = vy, rbx = bx, rby = by;
            // ---- legs (not leaned)
            double mx = p.MoveX, mz = p.MoveZ, ml = Math.Sqrt(mx * mx + mz * mz);
            if (ml > 1e-9) { mx /= ml; mz /= ml; } else { mx = 0; mz = 1; }
            double st = Clamp(p.Stride, 0, 1);
            double sw = Math.Sin(p.Walk), cw = Math.Cos(p.Walk);
            Leg(SkelJ.HipL, SkelJ.KneeL, SkelJ.FootL, -1, st * 0.20 * sw, st * 0.07 * Math.Max(0, cw), mx, mz);
            Leg(SkelJ.HipR, SkelJ.KneeR, SkelJ.FootR, 1, -st * 0.20 * sw, st * 0.07 * Math.Max(0, -cw), mx, mz);
            // ---- to world
            double cyw = Math.Cos(p.Yaw), syw = Math.Sin(p.Yaw);
            for (int j = 0; j < SkelJ.Count; j++)
            {
                X[j] = p.X + lx[j] * cyw + lz[j] * syw;
                Z[j] = p.Z - lx[j] * syw + lz[j] * cyw;
                Y[j] = ly[j];
            }
            ViewX = rvx * cyw + vz * syw; ViewZ = -rvx * syw + vz * cyw; ViewY = rvy;
            BulletX = rbx * cyw + bz * syw; BulletZ = -rbx * syw + bz * cyw; BulletY = rby;
        }

        /// <summary>Point dist m along the screen-centre line from the eye (world).</summary>
        public void ViewPoint(double dist, out double x, out double y, out double z)
        {
            x = X[SkelJ.Eye] + ViewX * dist; y = Y[SkelJ.Eye] + ViewY * dist; z = Z[SkelJ.Eye] + ViewZ * dist;
        }

        /// <summary>Point dist m along the bullet line from the muzzle (world).</summary>
        public void BulletPoint(double dist, out double x, out double y, out double z)
        {
            x = X[SkelJ.Muzzle] + BulletX * dist; y = Y[SkelJ.Muzzle] + BulletY * dist; z = Z[SkelJ.Muzzle] + BulletZ * dist;
        }

        private void Set(int j, double x, double y, double z) { lx[j] = x; ly[j] = y; lz[j] = z; }

        private void RotLean(int j, double ca, double sa, double py)
        {
            double x = lx[j], dy = ly[j] - py;
            lx[j] = x * ca + dy * sa;
            ly[j] = py - x * sa + dy * ca;
        }

        private void Elbow(int s, int h, int e, double poleX, double poleY)
        {
            double jx, jy, jz;
            SkelMotion.TwoBone(lx[s], ly[s], lz[s], lx[h], ly[h], lz[h], SkelDims.UpperArm, SkelDims.Forearm, poleX, poleY, -0.15, out jx, out jy, out jz);
            Set(e, jx, jy, jz);
        }

        private void Leg(int hip, int knee, int foot, double sideSign, double step, double lift, double mx, double mz)
        {
            Set(hip, sideSign * SkelDims.HipHalf, SkelDims.PelvisH, 0);
            double fx = sideSign * SkelDims.FootHalf + mx * step, fz = mz * step, fy = 0.03 + lift;
            Set(foot, fx, fy, fz);
            double jx, jy, jz;
            SkelMotion.TwoBone(lx[hip], ly[hip], lz[hip], fx, fy, fz, SkelDims.Thigh, SkelDims.Shin, sideSign * 0.1, 0, 1, out jx, out jy, out jz);
            Set(knee, jx, jy, jz);
        }

        internal static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo > 0 ? lo : (hi < 0 ? hi : 0);
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
    }

    /// <summary>
    /// Projection of world points (metres) to screen pixels. Top = top-down (world Z up the screen unless rotated), Back =
    /// orthographic from behind a facing Yaw, Side = orthographic from the right of a facing Yaw (forward = screen right),
    /// Persp = pinhole camera (e.g. a first-person inset from a rig's eye). Depth(): larger = farther from the viewer.
    /// </summary>
    public sealed class SkelCamera
    {
        public const int Top = 0, Back = 1, Side = 2, Persp = 3;
        public int Kind;
        /// <summary>Reference point (world m) drawn at (Ox, Oy); for Persp = the camera position.</summary>
        public double Cx, Cy, Cz;
        /// <summary>Top: the world direction drawn "up" (rad, 0 = +Z). Back/Side/Persp: the view facing. Persp: Pitch, Roll.</summary>
        public double Yaw, Pitch, Roll;
        /// <summary>Ortho: px per metre. Persp: focal length (px at 1 m).</summary>
        public double Scale = 100;
        public float Ox, Oy;
        public double Near = 0.05;

        public void SetTop(float ox, float oy, double cx, double cz, double pxPerM, double upYaw)
        {
            Kind = Top; Ox = ox; Oy = oy; Cx = cx; Cy = 0; Cz = cz; Scale = pxPerM; Yaw = upYaw; Pitch = 0; Roll = 0;
        }

        public void SetBack(float ox, float oy, double cx, double cy, double cz, double yaw, double pxPerM)
        {
            Kind = Back; Ox = ox; Oy = oy; Cx = cx; Cy = cy; Cz = cz; Scale = pxPerM; Yaw = yaw; Pitch = 0; Roll = 0;
        }

        public void SetSide(float ox, float oy, double cx, double cy, double cz, double yaw, double pxPerM)
        {
            Kind = Side; Ox = ox; Oy = oy; Cx = cx; Cy = cy; Cz = cz; Scale = pxPerM; Yaw = yaw; Pitch = 0; Roll = 0;
        }

        public void SetPersp(float ox, float oy, double ex, double ey, double ez, double yaw, double pitch, double roll, double focalPx)
        {
            Kind = Persp; Ox = ox; Oy = oy; Cx = ex; Cy = ey; Cz = ez; Yaw = yaw; Pitch = pitch; Roll = roll; Scale = focalPx;
        }

        /// <summary>First-person camera at the rig's eye, looking along its view line, rolled like the game camera.</summary>
        public void SetFromEye(SkelRig r, float ox, float oy, double focalPx)
        {
            double h = Math.Sqrt(r.ViewX * r.ViewX + r.ViewZ * r.ViewZ);
            SetPersp(ox, oy, r.X[SkelJ.Eye], r.Y[SkelJ.Eye], r.Z[SkelJ.Eye], Math.Atan2(r.ViewX, r.ViewZ), Math.Atan2(r.ViewY, h), r.Roll, focalPx);
        }

        private void Local(double x, double y, double z, out double u, out double v, out double w)
        {
            double dx = x - Cx, dy = y - Cy, dz = z - Cz;
            double c = Math.Cos(Yaw), s = Math.Sin(Yaw);
            u = dx * c - dz * s;      // right of the facing
            w = dx * s + dz * c;      // along the facing
            v = dy;
        }

        /// <summary>World point -> screen px. False only for Persp points behind the camera (closer than Near).</summary>
        public bool Project(double x, double y, double z, out float sx, out float sy)
        {
            double u, v, w;
            Local(x, y, z, out u, out v, out w);
            switch (Kind)
            {
                case Top: sx = (float)(Ox + u * Scale); sy = (float)(Oy - w * Scale); return true;
                case Back: sx = (float)(Ox + u * Scale); sy = (float)(Oy - v * Scale); return true;
                case Side: sx = (float)(Ox + w * Scale); sy = (float)(Oy - v * Scale); return true;
                default:
                    {
                        double cp = Math.Cos(Pitch), sp = Math.Sin(Pitch);
                        double v2 = v * cp - w * sp, w2 = v * sp + w * cp;
                        if (!(w2 >= Near)) { sx = Ox; sy = Oy; return false; }
                        double px = u * Scale / w2, py = -v2 * Scale / w2;
                        double cr = Math.Cos(Roll), sr = Math.Sin(Roll);
                        sx = (float)(Ox + px * cr - py * sr);
                        sy = (float)(Oy + px * sr + py * cr);
                        return true;
                    }
            }
        }

        /// <summary>Larger = farther from the viewer (Top: lower = farther).</summary>
        public double Depth(double x, double y, double z)
        {
            double u, v, w;
            Local(x, y, z, out u, out v, out w);
            switch (Kind)
            {
                case Top: return -v;
                case Back: return w;
                case Side: return -u;
                default:
                    {
                        double cp = Math.Cos(Pitch), sp = Math.Sin(Pitch);
                        return v * sp + w * cp;
                    }
            }
        }

        /// <summary>Screen radius (px) of a sphere of rM metres at the point (Persp: 0 behind the camera).</summary>
        public float Radius(double x, double y, double z, double rM)
        {
            if (Kind != Persp) return (float)(rM * Scale);
            double d = Depth(x, y, z);
            return d >= Near ? (float)(rM * Scale / d) : 0f;
        }
    }

    /// <summary>Pose / motion helpers (pure, 示意 timings from PeekModel).</summary>
    public static class SkelMotion
    {
        /// <summary>Exponential follow: the 'pulled' crosshair lag. tauMs &lt;= 0 snaps.</summary>
        public static double Follow(double current, double target, double dtMs, double tauMs)
        {
            if (double.IsNaN(current)) return target;
            if (!(tauMs > 0) || !(dtMs > 0)) return tauMs > 0 ? current : target;
            return current + (target - current) * (1 - Math.Exp(-dtMs / tauMs));
        }

        /// <summary>Angle wrapped to -π..π.</summary>
        public static double Wrap(double a)
        {
            if (double.IsNaN(a) || double.IsInfinity(a)) return 0;
            a = Math.IEEERemainder(a, 2 * Math.PI);
            return a;
        }

        /// <summary>Yaw (rad, 0 = +Z, + = right) from (fx, fz) toward (tx, tz).</summary>
        public static double YawTo(double fx, double fz, double tx, double tz) { return Math.Atan2(tx - fx, tz - fz); }

        /// <summary>Lean 0..1 ramp: 0 before startMs, smooth to 1 over rampMs, held to endMs, smooth back to 0 over rampMs.</summary>
        public static double Hold01(double tMs, double startMs, double endMs, double rampMs)
        {
            if (double.IsNaN(tMs)) return 0;
            if (!(rampMs > 0)) return (tMs >= startMs && tMs < endMs) ? 1 : 0;
            double up = TutorialGeometry.Smooth((tMs - startMs) / rampMs);
            double down = 1 - TutorialGeometry.Smooth((tMs - endMs) / rampMs);
            return Math.Min(up, down);
        }

        /// <summary>Peek motion tS seconds after the keys went down: sideways strafe distance (m, capped at maxStrafeM) and
        /// lean 0..1 (PeekModel: strafe ramps to 2 m/s, a full lean takes 0.3 s; 示意).</summary>
        public static void Peek(double tS, bool strafe, bool lean, double maxStrafeM, out double strafeM, out double lean01)
        {
            strafeM = strafe ? PeekModel.StrafeX(tS) : 0;
            if (double.IsNaN(strafeM) || strafeM < 0) strafeM = 0;
            if (maxStrafeM >= 0 && strafeM > maxStrafeM) strafeM = maxStrafeM;
            lean01 = lean && tS > 0 ? TutorialGeometry.Smooth(tS / PeekModel.LeanS) : 0;
        }

        /// <summary>Sets a peek pose: start (baseX, baseZ) facing yaw; side +1 = to the right (右手 D+E), -1 = to the left
        /// (换手 Q+A: Hand = 1). Moves the pelvis sideways, leans, steps the legs.</summary>
        public static void PeekPose(ref SkelPose p, double baseX, double baseZ, double yaw, int side, double tS, bool strafe, bool lean, double maxStrafeM)
        {
            double s, l;
            Peek(tS, strafe, lean, maxStrafeM, out s, out l);
            double sg = side < 0 ? -1 : 1;
            double c = Math.Cos(yaw), sn = Math.Sin(yaw);
            p.X = baseX + sg * s * c;      // local right axis = (cos yaw, -sin yaw)
            p.Z = baseZ - sg * s * sn;
            p.Yaw = yaw;
            p.Lean = sg * l;
            p.Hand = sg < 0 ? 1 : 0;
            p.MoveX = sg; p.MoveZ = 0;
            p.Stride = strafe && s > 0 && (maxStrafeM < 0 || s < maxStrafeM - 1e-6) ? 1 : 0;
            p.Walk = s / 0.30 * Math.PI;
        }

        /// <summary>Turns the view (AimYaw / AimPitch, body Yaw kept) so the eye looks at the world point. Solves 'scratch'
        /// twice (the eye moves with the aim); leaves it solved for the final pose.</summary>
        public static void AimAt(ref SkelPose p, SkelRig scratch, double tx, double ty, double tz)
        {
            for (int k = 0; k < 2; k++)
            {
                scratch.Solve(p);
                double ex = scratch.X[SkelJ.Eye], ey = scratch.Y[SkelJ.Eye], ez = scratch.Z[SkelJ.Eye];
                double dx = tx - ex, dy = ty - ey, dz = tz - ez;
                p.AimYaw = Wrap(Math.Atan2(dx, dz) - p.Yaw);
                p.AimPitch = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
            }
            scratch.Solve(p);
        }

        /// <summary>2D cross product sign: &gt; 0 when p is left of the line a -&gt; b (top-down, X right, Z up).</summary>
        public static double SideOfLine(double ax, double az, double bx, double bz, double px, double pz)
        {
            return (bx - ax) * (pz - az) - (bz - az) * (px - ax);
        }

        /// <summary>True when the straight line eye -&gt; target passes the corner point on the open side: the corner is on
        /// the 'coverSide' of the line (+1 = right of eye -&gt; target, -1 = left). Use it for "can they see each other".</summary>
        public static bool ClearOfCorner(double ex, double ez, double tx, double tz, double cx, double cz, int coverSide)
        {
            double s = SideOfLine(ex, ez, tx, tz, cx, cz);    // > 0: corner left of the line
            return coverSide > 0 ? s < 0 : s > 0;
        }

        /// <summary>Two-bone IK (law of cosines): joint between a and b with lengths l1, l2, bent toward the pole direction.</summary>
        public static void TwoBone(double ax, double ay, double az, double bx, double by, double bz, double l1, double l2,
            double polX, double polY, double polZ, out double jx, out double jy, out double jz)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(d > 1e-9)) { jx = ax + polX * l1; jy = ay + polY * l1; jz = az + polZ * l1; return; }
            double ux = dx / d, uy = dy / d, uz = dz / d;
            double dm = Math.Min(d, (l1 + l2) * 0.9999);
            dm = Math.Max(dm, Math.Abs(l1 - l2) + 1e-6);
            double a1 = (l1 * l1 - l2 * l2 + dm * dm) / (2 * dm);
            double h = Math.Sqrt(Math.Max(0, l1 * l1 - a1 * a1));
            double pd = polX * ux + polY * uy + polZ * uz;
            double px = polX - pd * ux, py = polY - pd * uy, pz = polZ - pd * uz;
            double pl = Math.Sqrt(px * px + py * py + pz * pz);
            if (!(pl > 1e-9))
            {   // pole parallel to the limb: any perpendicular
                px = -uy; py = ux; pz = 0; pl = Math.Sqrt(px * px + py * py);
                if (!(pl > 1e-9)) { px = 0; py = -uz; pz = uy; pl = Math.Sqrt(py * py + pz * pz); }
            }
            px /= pl; py /= pl; pz /= pl;
            jx = ax + ux * a1 + px * h; jy = ay + uy * a1 + py * h; jz = az + uz * a1 + pz * h;
        }
    }

#if !SKEL_PURE_ONLY
    /// <summary>How SkelDraw paints a figure. Colours default from the palette (Friendly / Enemy). Widths are 96-DPI px.</summary>
    public sealed class SkelStyle
    {
        public Color Bone, Joint, Body, Rifle, Eye, Bullet, Centre;
        /// <summary>Soft body volume under the bones (flat, translucent); bones and joints always stay visible.</summary>
        public bool Outline = true;
        public bool Helmet = true, ShowJoints = true, ShowEye = true, ShowBullet = false, ShowCentre = false;
        /// <summary>Lengths (m) of the bullet line (from the muzzle) and the screen-centre line (from the eye).</summary>
        public double BulletLenM = 6, CentreLenM = 6;
        public float BoneW = 2.2f, JointR = 2.6f, RayW = 1.6f;
        /// <summary>0..1, multiplies every alpha (fade in / out).</summary>
        public double Alpha = 1;

        public static SkelStyle Friendly(TutorialPalette pal)
        {
            SkelStyle s = new SkelStyle();
            s.Bone = pal.Cyan; s.Joint = pal.Text; s.Body = pal.Cyan; s.Rifle = pal.Accent;
            s.Eye = pal.Text; s.Bullet = pal.Hot; s.Centre = pal.Neon;
            return s;
        }

        public static SkelStyle Enemy(TutorialPalette pal)
        {
            SkelStyle s = Friendly(pal);
            s.Bone = pal.Red; s.Body = pal.Red; s.Joint = TutorialPalette.Mix(pal.Red, pal.Text, 0.55);
            return s;
        }
    }

    /// <summary>Draws a SkelRig through a SkelCamera. Static scratch (UI thread only), no allocation per frame.</summary>
    public static class SkelDraw
    {
        // bone list: (a, b, body-volume width m). Rifle (Stock-Muzzle) and the head are extra items.
        private static readonly int[] BA = { SkelJ.Pelvis, SkelJ.Spine, SkelJ.Chest, SkelJ.Neck, SkelJ.Chest, SkelJ.ShoulderL, SkelJ.ElbowL,
            SkelJ.Chest, SkelJ.ShoulderR, SkelJ.ElbowR, SkelJ.Pelvis, SkelJ.HipL, SkelJ.KneeL, SkelJ.Pelvis, SkelJ.HipR, SkelJ.KneeR };
        private static readonly int[] BB = { SkelJ.Spine, SkelJ.Chest, SkelJ.Neck, SkelJ.Head, SkelJ.ShoulderL, SkelJ.ElbowL, SkelJ.HandL,
            SkelJ.ShoulderR, SkelJ.ElbowR, SkelJ.HandR, SkelJ.HipL, SkelJ.KneeL, SkelJ.FootL, SkelJ.HipR, SkelJ.KneeR, SkelJ.FootR };
        private static readonly double[] BW = { 0.30, 0.34, 0.12, 0.0, 0.12, 0.10, 0.09, 0.12, 0.10, 0.09, 0.0, 0.15, 0.12, 0.0, 0.15, 0.12 };
        private const int Bones = 16, ItemRifle = 16, ItemHead = 17, Items = 18;
        private static readonly float[] sx = new float[SkelJ.Count], sy = new float[SkelJ.Count];
        private static readonly bool[] ok = new bool[SkelJ.Count];
        private static readonly int[] order = new int[Items];
        private static readonly double[] depth = new double[Items];

        /// <summary>Screen point of joint j (false when behind a Persp camera).</summary>
        public static bool Joint(SkelCamera cam, SkelRig r, int j, out float x, out float y)
        {
            return cam.Project(r.X[j], r.Y[j], r.Z[j], out x, out y);
        }

        public static void Draw(TutorialGfx gx, SkelRig r, SkelCamera cam, SkelStyle st)
        {
            if (gx == null || r == null || cam == null || st == null) return;
            Graphics g = gx.G;
            for (int j = 0; j < SkelJ.Count; j++) ok[j] = cam.Project(r.X[j], r.Y[j], r.Z[j], out sx[j], out sy[j]);
            for (int i = 0; i < Items; i++)
            {
                order[i] = i;
                int a, b; ItemEnds(i, out a, out b);
                depth[i] = (cam.Depth(r.X[a], r.Y[a], r.Z[a]) + cam.Depth(r.X[b], r.Y[b], r.Z[b])) * 0.5;
            }
            // far first (insertion sort, 18 items)
            for (int i = 1; i < Items; i++)
            {
                int v = order[i]; double dv = depth[v]; int k = i - 1;
                while (k >= 0 && depth[order[k]] < dv) { order[k + 1] = order[k]; k--; }
                order[k + 1] = v;
            }
            double al = SkelRig.Clamp(st.Alpha, 0, 1);
            for (int n = 0; n < Items; n++)
            {
                int i = order[n];
                if (i == ItemHead) { DrawHead(gx, r, cam, st, al); continue; }
                int a, b; ItemEnds(i, out a, out b);
                if (!ok[a] || !ok[b]) continue;
                if (i == ItemRifle)
                {
                    float rw = Math.Max(gx.S(3f), cam.Radius(r.X[a], r.Y[a], r.Z[a], 0.035) * 2f);
                    g.DrawLine(Px(gx, TutorialGfx.A(Color.Black, al * 0.75), rw + gx.S(2)), sx[a], sy[a], sx[b], sy[b]);
                    g.DrawLine(Px(gx, TutorialGfx.A(st.Rifle, al), rw), sx[a], sy[a], sx[b], sy[b]);
                    continue;
                }
                if (st.Outline && BW[i] > 0)
                {
                    float w = cam.Radius((r.X[a] + r.X[b]) * 0.5, (r.Y[a] + r.Y[b]) * 0.5, (r.Z[a] + r.Z[b]) * 0.5, BW[i] * 0.5) * 2f;
                    if (w > gx.S(2)) g.DrawLine(Px(gx, TutorialGfx.A(st.Body, al * 0.22), w), sx[a], sy[a], sx[b], sy[b]);
                }
                g.DrawLine(gx.P(TutorialGfx.A(st.Bone, al), st.BoneW), sx[a], sy[a], sx[b], sy[b]);
            }
            if (st.ShowJoints)
            {
                float jr = gx.S(st.JointR);
                for (int j = 0; j < SkelJ.Count; j++)
                {
                    if (!ok[j] || j == SkelJ.Eye || j == SkelJ.Head || j == SkelJ.Stock) continue;
                    if (j == SkelJ.Muzzle) { gx.Dot(TutorialGfx.A(st.Bullet, al), sx[j], sy[j], jr); continue; }
                    gx.Dot(TutorialGfx.A(Color.Black, al * 0.8), sx[j], sy[j], jr + gx.S(1));
                    gx.Dot(TutorialGfx.A(st.Joint, al), sx[j], sy[j], jr);
                }
            }
            if (st.ShowEye && ok[SkelJ.Eye])
            {
                float er = gx.S(st.JointR + 1.2f), ex = sx[SkelJ.Eye], ey = sy[SkelJ.Eye];
                PointF[] d = gx.Pts(4);
                d[0] = new PointF(ex, ey - er); d[1] = new PointF(ex + er, ey); d[2] = new PointF(ex, ey + er); d[3] = new PointF(ex - er, ey);
                g.FillPolygon(gx.B(TutorialGfx.A(Color.Black, al * 0.85)), d);
                g.DrawPolygon(gx.P(TutorialGfx.A(st.Eye, al), 1.4f), d);
            }
            Rays(gx, r, cam, st);
        }

        /// <summary>The screen-centre line (eye, dashed, st.Centre) and the bullet line (muzzle, solid, st.Bullet) when
        /// st.ShowCentre / st.ShowBullet. Draw() already calls it; call it alone to draw only the rays.</summary>
        public static void Rays(TutorialGfx gx, SkelRig r, SkelCamera cam, SkelStyle st)
        {
            if (gx == null || r == null || cam == null || st == null) return;
            double al = SkelRig.Clamp(st.Alpha, 0, 1);
            float x0, y0, x1, y1;
            double px, py, pz;
            if (st.ShowCentre)
            {
                r.ViewPoint(st.CentreLenM, out px, out py, out pz);
                if (cam.Project(r.X[SkelJ.Eye], r.Y[SkelJ.Eye], r.Z[SkelJ.Eye], out x0, out y0) && cam.Project(px, py, pz, out x1, out y1))
                    gx.G.DrawLine(gx.Dashed(TutorialGfx.A(st.Centre, al * 0.9), st.RayW), x0, y0, x1, y1);
            }
            if (st.ShowBullet)
            {
                r.BulletPoint(st.BulletLenM, out px, out py, out pz);
                if (cam.Project(r.X[SkelJ.Muzzle], r.Y[SkelJ.Muzzle], r.Z[SkelJ.Muzzle], out x0, out y0) && cam.Project(px, py, pz, out x1, out y1))
                    gx.G.DrawLine(gx.P(TutorialGfx.A(st.Bullet, al * 0.95), st.RayW), x0, y0, x1, y1);
            }
        }

        private static void DrawHead(TutorialGfx gx, SkelRig r, SkelCamera cam, SkelStyle st, double al)
        {
            int h = SkelJ.Head;
            if (!ok[h]) return;
            float rr = cam.Radius(r.X[h], r.Y[h], r.Z[h], SkelDims.HeadR);
            if (rr < gx.S(2)) rr = gx.S(2);
            Graphics g = gx.G;
            float x = sx[h], y = sy[h];
            g.FillEllipse(gx.B(TutorialGfx.A(st.Body, al * 0.30)), x - rr, y - rr, 2 * rr, 2 * rr);
            g.DrawEllipse(gx.P(TutorialGfx.A(st.Bone, al), st.BoneW), x - rr, y - rr, 2 * rr, 2 * rr);
            if (!st.Helmet) return;
            float hr = rr * 1.12f;
            if (cam.Kind == SkelCamera.Top)
            {   // helmet seen from above: a ring around the head
                g.DrawEllipse(gx.P(TutorialGfx.A(st.Bone, al * 0.85), st.BoneW * 1.4f), x - hr, y - hr, 2 * hr, 2 * hr);
                return;
            }
            // helmet shell over the top half; the head's "up" on screen = neck -> head (follows lean and camera roll)
            double up = 0;
            float nx = sx[SkelJ.Neck], ny = sy[SkelJ.Neck];
            if (ok[SkelJ.Neck] && Math.Abs(x - nx) + Math.Abs(y - ny) > 0.5)
                up = Math.Atan2(y - ny, x - nx) * 180.0 / Math.PI + 90.0;   // 0 when upright (y down)
            float start = (float)(180 + up);
            g.DrawArc(Px(gx, TutorialGfx.A(Color.Black, al * 0.7), Math.Max(gx.S(3f), rr * 0.42f) + gx.S(1.5f)), x - hr, y - hr, 2 * hr, 2 * hr, start, 180f);
            g.DrawArc(Px(gx, TutorialGfx.A(st.Bone, al), Math.Max(gx.S(3f), rr * 0.42f)), x - hr, y - hr, 2 * hr, 2 * hr, start, 180f);
        }

        private static void ItemEnds(int i, out int a, out int b)
        {
            if (i < Bones) { a = BA[i]; b = BB[i]; return; }
            if (i == ItemRifle) { a = SkelJ.Stock; b = SkelJ.Muzzle; return; }
            a = SkelJ.Head; b = SkelJ.Head;
        }

        /// <summary>Scratch pen with a width already in screen pixels.</summary>
        private static Pen Px(TutorialGfx gx, Color c, float px)
        {
            return gx.P(c, gx.K > 0 ? px / gx.K : px);
        }
    }
#endif
}
