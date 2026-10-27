// PURE file: only System. Compiles under net48 (C# 5) and net8.0.
using System;

namespace TarkovCrosshair
{
    /// <summary>
    /// The look actually drawn this frame (v3.0): the profile's look with 换手颜色 blended in by the hand factor and,
    /// when boosted, the 强化准星 dimensions / colours. Mutable and reusable (the render thread fills one per frame).
    /// </summary>
    public sealed class RingStyle
    {
        public string Shape = "ring", Gradient = "none";
        public double Diameter = 18, Thickness = 2, Feather = 1, OpacityPct = 60, OuterThickness = 1.5;
        public bool CenterDot;
        public double CenterDotSize = 2;
        public int R1 = 0x78, G1 = 0xFF, B1 = 0x78, R2 = 0xFF, G2 = 0xFF, B2 = 0xFF;
        public double CrossLength = 6, CrossThickness = 1.5, CrossGap = 4;
        public bool CrossTop = true;
        public int Dashes;
        public double DashGapPct = 30, GlowPx, GlowOpacityPct = 40, RotationDeg;

        public RingStyle() { }

        public static RingStyle From(Profile p, double handBlend, bool boosted)
        {
            RingStyle s = new RingStyle();
            s.Set(p, handBlend, boosted);
            return s;
        }

        private static void Parse(string c, int dr, int dg, int db, out int r, out int g, out int b)
        {
            if (!ColorUtil.TryParse(c, out r, out g, out b)) { r = dr; g = dg; b = db; }
        }

        private static int Lerp(int a, int b, double w) { return (int)Math.Round(a + (b - a) * w, MidpointRounding.AwayFromZero); }

        /// <summary>
        /// handBlend 0 = 正常手 .. 1 = 换手 (MotionEngine.HandBlend, the same smoothstep factor as the 换手 offsets).
        /// 换手颜色 on: Color → HandColor and Color2 → HandColor2 by that factor. boosted: the 强化准星 dimensions; its colours
        /// are the current hand's (FollowHandColor) or its own Color / Color2.
        /// </summary>
        public void Set(Profile p, double handBlend, bool boosted)
        {
            Shape = p.Shape ?? "ring"; Gradient = p.Gradient ?? "none";
            Diameter = p.Diameter; Thickness = p.Thickness; Feather = p.Feather; OpacityPct = p.OpacityPct;
            OuterThickness = p.OuterThickness; CenterDot = p.CenterDot; CenterDotSize = p.CenterDotSize;
            CrossLength = p.CrossLength; CrossThickness = p.CrossThickness; CrossGap = p.CrossGap; CrossTop = p.CrossTop;
            Dashes = p.Dashes; DashGapPct = p.DashGapPct; GlowPx = p.GlowPx; GlowOpacityPct = p.GlowOpacityPct; RotationDeg = p.RotationDeg;
            Parse(p.Color, 0x78, 0xFF, 0x78, out R1, out G1, out B1);
            Parse(p.Color2, 0xFF, 0xFF, 0xFF, out R2, out G2, out B2);
            double w = handBlend;
            if (double.IsNaN(w) || w < 0) w = 0;
            if (w > 1) w = 1;
            if (p.HandColorOn && w > 0)
            {
                int hr, hg, hb, h2r, h2g, h2b;
                Parse(p.HandColor, R1, G1, B1, out hr, out hg, out hb);
                Parse(p.HandColor2, R2, G2, B2, out h2r, out h2g, out h2b);
                R1 = Lerp(R1, hr, w); G1 = Lerp(G1, hg, w); B1 = Lerp(B1, hb, w);
                R2 = Lerp(R2, h2r, w); G2 = Lerp(G2, h2g, w); B2 = Lerp(B2, h2b, w);
            }
            BoostStyle b = p.Boost;
            if (boosted && b != null)
            {
                Diameter = b.Diameter; Thickness = b.Thickness; OuterThickness = b.OuterThickness; OpacityPct = b.OpacityPct;
                GlowPx = b.GlowPx; GlowOpacityPct = b.GlowOpacityPct;
                if (!b.FollowHandColor)
                {
                    Parse(b.Color, 0x00, 0xFF, 0x00, out R1, out G1, out B1);
                    Parse(b.Color2, 0x00, 0x00, 0x00, out R2, out G2, out B2);
                }
            }
        }

        /// <summary>Changes whenever the raster changes (OpacityPct excluded: it is applied by the window, not the raster).</summary>
        public string Key()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9},{10},{11}|{12},{13},{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}|{23}",
                Shape, Gradient, Diameter, Thickness, Feather, OuterThickness, CenterDot, CenterDotSize, 0,
                R1, G1, B1, R2, G2, B2, CrossLength, CrossThickness, CrossGap, CrossTop, Dashes, DashGapPct, GlowPx, GlowOpacityPct, RotationDeg);
        }

        /// <summary>The v2.4.5 renderer applies exactly: plain ring, no dashes, no glow, no rotation.</summary>
        public bool IsClassic
        {
            get { return Shape == "ring" && Dashes < 2 && !(GlowPx > 0 && GlowOpacityPct > 0) && Rot() == 0; }
        }

        internal double Rot()
        {
            double r = RotationDeg % 360.0;
            if (double.IsNaN(r)) r = 0;
            return r;
        }
    }

    /// <summary>Analytic anti-aliased ring rasteriser -> premultiplied BGRA (0xAARRGGBB ints), top-down.</summary>
    public static class RingRaster
    {
        /// <summary>S = even(ceil(Diameter + Thickness + 2w [+ 2*OuterThickness for twotone]) + 6), w = max(Feather, 1).</summary>
        public static int CanvasSize(Profile p) { return CanvasSize(RingStyle.From(p, 0, false)); }

        /// <summary>
        /// Ring: the v2.4.5 formula (+ 2 × glow). Other shapes: even(ceil(2 × extent + 2w + 2 × glow) + 6), extent = the
        /// farthest any part (incl. outline, rotation) reaches from the centre.
        /// </summary>
        public static int CanvasSize(RingStyle p)
        {
            double w = Math.Max(p.Feather, 1.0);
            bool two = p.Gradient == "twotone";
            double glow = (p.GlowPx > 0 && p.GlowOpacityPct > 0) ? p.GlowPx : 0.0;
            int s;
            if (p.Shape == "ring")
            {
                double outer = two ? 2.0 * p.OuterThickness : 0.0;
                s = (int)Math.Ceiling(p.Diameter + p.Thickness + outer + 2.0 * w + 2.0 * glow) + 6;
            }
            else
            {
                double o = two ? p.OuterThickness : 0.0;
                double ext = 0;
                if (HasRing(p.Shape)) ext = p.Diameter / 2.0 + p.Thickness / 2.0 + o;
                if (p.Shape == "square") ext *= Math.Sqrt(2.0);
                if (HasCross(p.Shape))
                {
                    double a = p.CrossGap + p.CrossLength + o, b = p.CrossThickness / 2.0 + o;
                    ext = Math.Max(ext, Math.Sqrt(a * a + b * b));
                }
                if (p.Shape == "dot") ext = Math.Max(ext, p.Diameter / 2.0 + o);
                if (p.CenterDot) ext = Math.Max(ext, p.CenterDotSize / 2.0);
                s = (int)Math.Ceiling(2.0 * ext + 2.0 * w + 2.0 * glow) + 6;
            }
            if ((s & 1) != 0) s++;
            return s;
        }

        private static bool HasRing(string shape) { return shape == "ring" || shape == "ringcross" || shape == "square"; }
        private static bool HasCross(string shape) { return shape == "cross" || shape == "ringcross"; }

        public static int[] Render(Profile p, int size, double cx, double cy)
        {
            int[] buf = new int[size * size];
            Render(p, size, cx, cy, buf);
            return buf;
        }

        /// <summary>Renders into buf (length ≥ size*size). OpacityPct is NOT applied here.</summary>
        public static void Render(Profile p, int size, double cx, double cy, int[] buf)
        {
            Render(RingStyle.From(p, 0, false), size, cx, cy, buf);
        }

        public static int[] Render(RingStyle s, int size, double cx, double cy)
        {
            int[] buf = new int[size * size];
            Render(s, size, cx, cy, buf);
            return buf;
        }

        public static void Render(RingStyle s, int size, double cx, double cy, int[] buf)
        {
            if (s.IsClassic) RenderClassic(s, size, cx, cy, buf);
            else RenderGeneral(s, size, cx, cy, buf);
        }

        /// <summary>The v2.4.5 ring renderer, unchanged (pixel-identical defaults).</summary>
        private static void RenderClassic(RingStyle p, int size, double cx, double cy, int[] buf)
        {
            int r1 = p.R1, g1 = p.G1, b1 = p.B1, r2 = p.R2, g2 = p.G2, b2 = p.B2;

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

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        /// <summary>Coverage of a signed distance (negative inside) with the v2.4.5 feather + smoothstep.</summary>
        private static double Cov(double dist, double w)
        {
            double a = Clamp01(1.0 - Math.Max(0.0, dist + 0.5) / w);
            return a * a * (3.0 - 2.0 * a);
        }

        /// <summary>Signed distance to an axis-aligned box centred at (bx, by) with half sizes (hx, hy).</summary>
        private static double SdBox(double x, double y, double bx, double by, double hx, double hy)
        {
            double qx = Math.Abs(x - bx) - hx, qy = Math.Abs(y - by) - hy;
            double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
            return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0);
        }

        /// <summary>
        /// Every shape (v3.0). Each part (ring / square band, cross arms, dot) has a signed distance; "twotone" = the part's
        /// core in Color plus an outline of OuterThickness in Color2 (for the ring band: outside only, exactly as v2.4.5);
        /// parts combine by the larger coverage; glow = a soft halo of GlowPx in Color around the union, under the shape.
        /// </summary>
        private static void RenderGeneral(RingStyle p, int size, double cx, double cy, int[] buf)
        {
            int r1 = p.R1, g1 = p.G1, b1 = p.B1, r2 = p.R2, g2 = p.G2, b2 = p.B2;
            double R = p.Diameter / 2.0, hh = p.Thickness / 2.0, w = Math.Max(p.Feather, 1.0);
            int mode = p.Gradient == "radial" ? 1 : (p.Gradient == "sweep" ? 2 : (p.Gradient == "twotone" ? 3 : 0));
            double to = mode == 3 ? p.OuterThickness : 0.0;
            bool ring = HasRing(p.Shape), square = p.Shape == "square", cross = HasCross(p.Shape), disc = p.Shape == "dot";
            double rMid = R + hh, rIn = R - hh, rOut = rMid + to;
            double bandC = (rIn + rOut) / 2.0, bandHalf = (rOut - rIn) / 2.0;
            int dashes = p.Dashes >= 2 ? p.Dashes : 0;
            double period = dashes > 0 ? 2.0 * Math.PI / dashes : 0;
            double dashHalf = period * (1.0 - p.DashGapPct / 100.0) / 2.0;
            double armHx = p.CrossLength / 2.0, armHy = p.CrossThickness / 2.0, armC = p.CrossGap + p.CrossLength / 2.0;
            double crossLen = Math.Max(p.CrossLength, 0.001);
            double glowPx = p.GlowPx > 0 && p.GlowOpacityPct > 0 ? p.GlowPx : 0.0;
            double glowA = Clamp01(p.GlowOpacityPct / 100.0);
            double rad = p.Rot() * Math.PI / 180.0;
            double cs = Math.Cos(rad), sn = Math.Sin(rad);
            bool dot = p.CenterDot;
            double dotHalf = p.CenterDotSize / 2.0;

            for (int j = 0; j < size; j++)
            {
                double sy = j + 0.5 - cy;
                int row = j * size;
                for (int i = 0; i < size; i++)
                {
                    double sx = i + 0.5 - cx;
                    // shape space: the screen point rotated by −RotationDeg (positive = clockwise on screen)
                    double px = sx * cs + sy * sn, py = -sx * sn + sy * cs;
                    double d = Math.Sqrt(px * px + py * py);
                    double a = 0, cr = r1, cg = g1, cb = b1;
                    double minDist = double.MaxValue; // union coverage distance, for the glow

                    if (ring)
                    {
                        double dd = square ? R + SdBox(px, py, 0, 0, R, R) : d;
                        double dist = Math.Abs(dd - bandC) - bandHalf;
                        double core = dd - rMid; // ≤ 0: inner band (Color) for twotone
                        if (dashes > 0)
                        {
                            double th = Math.Atan2(py, px) + Math.PI / 2.0; // dashes start at the top
                            double ph = th % period; if (ph < 0) ph += period;
                            double off = Math.Min(ph, period - ph); // angle from the nearest dash centre
                            double arc = (off - dashHalf) * Math.Max(dd, 1.0);
                            dist = Math.Max(dist, arc - to);
                            core = Math.Max(core, arc);
                        }
                        double ar = Cov(dist, w);
                        double t = Tone(mode, core, (dd - rIn) / Math.Max(2.0 * hh, 0.001), px, py);
                        if (dist < minDist) minDist = dist;
                        if (ar > a) { a = ar; Mixc(t, r1, g1, b1, r2, g2, b2, out cr, out cg, out cb); }
                    }
                    if (cross)
                    {
                        double sd = SdBox(px, py, armC, 0, armHx, armHy);                   // right
                        sd = Math.Min(sd, SdBox(px, py, -armC, 0, armHx, armHy));           // left
                        sd = Math.Min(sd, SdBox(px, py, 0, armC, armHy, armHx));            // bottom (screen y down)
                        if (p.CrossTop) sd = Math.Min(sd, SdBox(px, py, 0, -armC, armHy, armHx)); // top (off = T 形)
                        double dist = sd - to;
                        double ac = Cov(dist, w);
                        double t = Tone(mode, sd, (d - p.CrossGap) / crossLen, px, py);
                        if (dist < minDist) minDist = dist;
                        if (ac > a) { a = ac; Mixc(t, r1, g1, b1, r2, g2, b2, out cr, out cg, out cb); }
                    }
                    if (disc)
                    {
                        double sd = d - R;
                        double dist = sd - to;
                        double ad0 = Cov(dist, w);
                        double t = Tone(mode, sd, d / Math.Max(R, 0.001), px, py);
                        if (dist < minDist) minDist = dist;
                        if (ad0 > a) { a = ad0; Mixc(t, r1, g1, b1, r2, g2, b2, out cr, out cg, out cb); }
                    }
                    if (dot)
                    {
                        double ad = Clamp01(1.0 - Math.Max(0.0, d - dotHalf + 0.5));
                        if (d - dotHalf < minDist) minDist = d - dotHalf;
                        if (ad > a) { a = ad; cr = r1; cg = g1; cb = b1; }
                    }

                    // premultiplied: shape over glow
                    double pa = a, pr = cr * a, pg = cg * a, pb = cb * a;
                    if (glowPx > 0 && minDist < double.MaxValue)
                    {
                        double u = Clamp01(minDist / glowPx);
                        double gA = glowA * (1.0 - u) * (1.0 - u) * (1.0 - a);
                        pa += gA; pr += r1 * gA; pg += g1 * gA; pb += b1 * gA;
                    }

                    int A8 = (int)Math.Round(Clamp01(pa) * 255.0);
                    if (A8 <= 0) { buf[row + i] = 0; continue; }
                    int R8 = Byte(pr), G8 = Byte(pg), B8 = Byte(pb);
                    if (R8 > A8) R8 = A8; if (G8 > A8) G8 = A8; if (B8 > A8) B8 = A8;
                    buf[row + i] = (A8 << 24) | (R8 << 16) | (G8 << 8) | B8;
                }
            }
        }

        private static int Byte(double v)
        {
            int x = (int)Math.Round(v);
            return x < 0 ? 0 : (x > 255 ? 255 : x);
        }

        /// <summary>Gradient position t (0 = Color, 1 = Color2). core = signed distance to the core edge (twotone).</summary>
        private static double Tone(int mode, double core, double radialT, double px, double py)
        {
            if (mode == 3) return Clamp01(core + 0.5);
            if (mode == 1) return Clamp01(radialT);
            if (mode == 2)
            {
                double t = (Math.Atan2(py, px) + Math.PI) / (2.0 * Math.PI);
                return 1.0 - Math.Abs(2.0 * t - 1.0);
            }
            return 0;
        }

        private static void Mixc(double t, int r1, int g1, int b1, int r2, int g2, int b2, out double r, out double g, out double b)
        {
            r = r1 + (r2 - r1) * t; g = g1 + (g2 - g1) * t; b = b1 + (b2 - b1) * t;
        }
    }
}
