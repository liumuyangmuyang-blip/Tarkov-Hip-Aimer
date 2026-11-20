// TEST ONLY: verbatim v2.4.5 RingRaster.cs (namespace renamed) - reference to prove the v3.0 defaults render the same pixels.
using System;

namespace TarkovCrosshair.V245
{
    /// <summary>Analytic anti-aliased ring rasteriser -> premultiplied BGRA (0xAARRGGBB ints), top-down.</summary>
    public static class RingRaster
    {
        /// <summary>S = even(ceil(Diameter + Thickness + 2w [+ 2*OuterThickness for twotone]) + 6), w = max(Feather, 1).</summary>
        public static int CanvasSize(Profile p)
        {
            double w = Math.Max(p.Feather, 1.0);
            double outer = p.Gradient == "twotone" ? 2.0 * p.OuterThickness : 0.0;
            int s = (int)Math.Ceiling(p.Diameter + p.Thickness + outer + 2.0 * w) + 6;
            if ((s & 1) != 0) s++;
            return s;
        }

        public static int[] Render(Profile p, int size, double cx, double cy)
        {
            int[] buf = new int[size * size];
            Render(p, size, cx, cy, buf);
            return buf;
        }

        /// <summary>Renders into buf (length ≥ size*size). OpacityPct is NOT applied here.</summary>
        public static void Render(Profile p, int size, double cx, double cy, int[] buf)
        {
            int r1, g1, b1, r2, g2, b2;
            if (!ColorUtil.TryParse(p.Color, out r1, out g1, out b1)) { r1 = 0x78; g1 = 0xFF; b1 = 0x78; }
            if (!ColorUtil.TryParse(p.Color2, out r2, out g2, out b2)) { r2 = 0xFF; g2 = 0xFF; b2 = 0xFF; }

            double R = p.Diameter / 2.0;
            double hh = p.Thickness / 2.0;
            double w = Math.Max(p.Feather, 1.0);
            int mode = p.Gradient == "radial" ? 1 : (p.Gradient == "sweep" ? 2 : (p.Gradient == "twotone" ? 3 : 0));
            // Coverage band: [R - hh, R + hh] normally; for twotone the union [R - hh, R + hh + To].
            double rMid = R + hh;
            double bandC = R, bandHalf = hh;
            if (mode == 3)
            {
                double rIn = R - hh, rOut = rMid + p.OuterThickness;
                bandC = (rIn + rOut) / 2.0;
                bandHalf = (rOut - rIn) / 2.0;
            }
            bool dot = p.CenterDot;
            double dotHalf = p.CenterDotSize / 2.0;

            for (int j = 0; j < size; j++)
            {
                double py = j + 0.5 - cy;
                int row = j * size;
                for (int i = 0; i < size; i++)
                {
                    double px = i + 0.5 - cx;
                    double d = Math.Sqrt(px * px + py * py);

                    double dist = Math.Abs(d - bandC) - bandHalf;
                    double a = 1.0 - Math.Max(0.0, dist + 0.5) / w;
                    if (a < 0) a = 0; else if (a > 1) a = 1;
                    a = a * a * (3.0 - 2.0 * a);

                    double cr, cg, cb;
                    if (mode == 0)
                    {
                        cr = r1; cg = g1; cb = b1;
                    }
                    else
                    {
                        double t;
                        if (mode == 3)
                        {
                            // Inner band = Color, outer band = Color2, 1 px blend at r_mid.
                            t = d - rMid + 0.5;
                            if (t < 0) t = 0; else if (t > 1) t = 1;
                        }
                        else if (mode == 1)
                        {
                            t = (d - (R - hh)) / Math.Max(2.0 * hh, 0.001);
                            if (t < 0) t = 0; else if (t > 1) t = 1;
                        }
                        else
                        {
                            t = (Math.Atan2(py, px) + Math.PI) / (2.0 * Math.PI);
                            t = 1.0 - Math.Abs(2.0 * t - 1.0);
                        }
                        cr = r1 + (r2 - r1) * t; cg = g1 + (g2 - g1) * t; cb = b1 + (b2 - b1) * t;
                    }

                    if (dot)
                    {
                        double ad = 1.0 - Math.Max(0.0, d - dotHalf + 0.5);
                        if (ad < 0) ad = 0; else if (ad > 1) ad = 1;
                        if (ad > a) { a = ad; cr = r1; cg = g1; cb = b1; }
                    }

                    int A8 = (int)Math.Round(a * 255.0);
                    if (A8 <= 0) { buf[row + i] = 0; continue; }
                    int R8 = (int)Math.Round(cr * A8 / 255.0);
                    int G8 = (int)Math.Round(cg * A8 / 255.0);
                    int B8 = (int)Math.Round(cb * A8 / 255.0);
                    buf[row + i] = (A8 << 24) | (R8 << 16) | (G8 << 8) | B8;
                }
            }
        }
    }
}
