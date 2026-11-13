// PURE file: only System. Compiles under net48 (C# 5) and net8.0.
using System;

namespace TarkovCrosshair
{
    /// <summary>
    /// 移动速率曲线 maths (v2.4). A curve = 5 relative speeds (0..100) at 0 / 25 / 50 / 75 / 100 % of the travel time.
    /// SpeedAt is a monotone cubic (Fritsch–Carlson / PCHIP) through the 5 points: smooth, no overshoot, never negative.
    /// Progress is the normalised integral of SpeedAt: 0 at u = 0, 1 at u = 1, never decreasing.
    /// The integral of each cubic piece is computed exactly; the per-curve coefficients are cached.
    /// </summary>
    public static class MotionCurve
    {
        public const int Points = 5;
        public const double MaxDurationMs = 2000;

        public static readonly string[] PresetNames = new string[] { "慢起", "慢停", "慢起慢停", "匀速", "v2.3 手感" };
        public static readonly double[][] Presets = new double[][]
        {
            new double[] { 0, 25, 50, 75, 100 },
            new double[] { 100, 75, 50, 25, 0 },
            new double[] { 0, 70, 100, 70, 0 },
            new double[] { 100, 100, 100, 100, 100 },
            new double[] { 0, 100, 61, 28, 11 }
        };
        public const int PresetV23 = 4;

        /// <summary>A fresh copy of the 「v2.3 手感」 speeds.</summary>
        public static double[] V23Feel() { return (double[])Presets[PresetV23].Clone(); }

        /// <summary>Precomputed shape of one curve (immutable).</summary>
        private sealed class Shape
        {
            public readonly double[] Key = new double[Points];
            // per segment k (u in [k/4,(k+1)/4], local s in [0,1]): speed = a + b s + c s² + d s³
            public readonly double[] A = new double[4], B = new double[4], C = new double[4], D = new double[4];
            public readonly double[] Cum = new double[Points]; // ∫0..k/4 SpeedAt
            public double Total;
            public bool Flat; // all zero -> linear progress

            public Shape(double[] speeds)
            {
                double[] y = new double[Points];
                for (int i = 0; i < Points; i++)
                {
                    double v = (speeds != null && i < speeds.Length) ? speeds[i] : 0;
                    if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
                    if (v < 0) v = 0;
                    y[i] = v; Key[i] = v;
                }
                const double h = 0.25;
                double[] del = new double[4];
                for (int k = 0; k < 4; k++) del[k] = (y[k + 1] - y[k]) / h;
                double[] m = new double[Points];
                // interior slopes: harmonic mean of the neighbouring secants, 0 at a local extremum (Fritsch–Carlson, equal spacing)
                for (int k = 1; k < 4; k++)
                {
                    double d0 = del[k - 1], d1 = del[k];
                    if (d0 == 0 || d1 == 0 || (d0 > 0) != (d1 > 0)) m[k] = 0;
                    else m[k] = 2.0 / (1.0 / d0 + 1.0 / d1);
                }
                m[0] = EndSlope(del[0], del[1]);
                m[4] = EndSlope(del[3], del[2]);
                Total = 0;
                Cum[0] = 0;
                for (int k = 0; k < 4; k++)
                {
                    // Hermite with p0, p1, tangents scaled by h
                    double p0 = y[k], p1 = y[k + 1], t0 = m[k] * h, t1 = m[k + 1] * h;
                    A[k] = p0;
                    B[k] = t0;
                    C[k] = 3 * (p1 - p0) - 2 * t0 - t1;
                    D[k] = 2 * (p0 - p1) + t0 + t1;
                    double seg = 0.25 * (A[k] + (B[k] / 2 + (C[k] / 3 + D[k] / 4))); // same expression as Prog at s = 1
                    Total += seg;
                    Cum[k + 1] = Total;
                }
                Flat = !(Total > 1e-12);
            }

            /// <summary>Three-point end slope, limited so the end piece cannot overshoot (Moler's pchip).</summary>
            private static double EndSlope(double d0, double d1)
            {
                double s = (3 * d0 - d1) / 2.0;
                if (s == 0 || d0 == 0 || (s > 0) != (d0 > 0)) return 0;
                if ((d0 > 0) != (d1 > 0) && Math.Abs(s) > Math.Abs(3 * d0)) return 3 * d0;
                return s;
            }

            public bool Same(double[] speeds)
            {
                for (int i = 0; i < Points; i++)
                {
                    double v = (speeds != null && i < speeds.Length) ? speeds[i] : 0;
                    if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) v = 0;
                    if (v != Key[i]) return false;
                }
                return true;
            }

            public double Speed(double u)
            {
                if (u <= 0) u = 0;
                if (u >= 1) u = 1;
                int k = (int)Math.Floor(u * 4);
                if (k > 3) k = 3;
                double s = u * 4 - k;
                double v = A[k] + s * (B[k] + s * (C[k] + s * D[k]));
                return v < 0 ? 0 : v;
            }

            public double Prog(double u)
            {
                if (!(u > 0)) return 0;
                if (u >= 1) return 1;
                if (Flat) return u;
                int k = (int)Math.Floor(u * 4);
                if (k > 3) k = 3;
                double s = u * 4 - k;
                double part = 0.25 * s * (A[k] + s * (B[k] / 2 + s * (C[k] / 3 + s * D[k] / 4)));
                double p = (Cum[k] + part) / Total;
                if (p < 0) p = 0;
                if (p > 1) p = 1;
                return p;
            }
        }

        // Two-entry cache per thread (the render thread uses one curve per axis; the UI thread draws).
        [ThreadStatic] private static Shape cache0;
        [ThreadStatic] private static Shape cache1;

        private static Shape Get(double[] speeds)
        {
            Shape a = cache0;
            if (a != null && a.Same(speeds)) return a;
            Shape b = cache1;
            if (b != null && b.Same(speeds)) { cache1 = a; cache0 = b; return b; }
            Shape n = new Shape(speeds);
            cache1 = a; cache0 = n;
            return n;
        }

        /// <summary>Relative speed (0..100 scale) at u in [0,1].</summary>
        public static double SpeedAt(double[] speeds, double u) { return Get(speeds).Speed(u); }

        /// <summary>∫0..u SpeedAt / ∫0..1 SpeedAt, u clamped to [0,1]. All speeds 0 -> u.</summary>
        public static double Progress(double[] speeds, double u) { return Get(speeds).Prog(u); }
    }
}
