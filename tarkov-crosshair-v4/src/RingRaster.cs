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
        // ---- v4.0 准星样式 (StyleShapes.cs); Kind "" = the v3 shapes above ----
        public string Kind = "";
        /// <summary>Style gap / length (already × the 强化 ratio), angle, count.</summary>
        public double SGap = 4, SLength = 7, SSpread = 120;
        public int SCount = 3;
        /// <summary>中心留空 radius px: every pixel whose centre is closer has alpha 0 (always ≥ RingRaster.MinHole).</summary>
        public double Hole = RingRaster.MinHole;
        /// <summary>自定义图片 (Kind "custom" only; null = picture missing -> drawn as the v3 Shape instead).</summary>
        public PngImage Png;
        public bool PngTint;
        /// <summary>True when Kind was "custom" but the picture could not be loaded.</summary>
        public bool PngMissing;
        /// <summary>v4.1 (BRUTE40 P2-2): true when 中心留空 leaves less than VisibleBand of the shape (the crosshair is
        /// invisible or nearly so: the look is smaller than the hole). The asked 中心留空 is kept empty all the same (the
        /// game's centre dot must stay clear); the preview says so instead.</summary>
        public bool Hidden;

        /// <summary>v4.1 (BRUTE40 P1-1): range of the 强化 ratio applied to the style gap / length and 中心留空 (the range
        /// ApplyTile keeps): size 4 boosted to 300 no longer scales them by 75 (a 30,000 px canvas).</summary>
        public const double MinBoostScale = 0.5, MaxBoostScale = 3.0;
        /// <summary>v4.1 (BRUTE40 P2-2): at least this much of the shape (px, outward from the 中心留空 edge) stays drawn.</summary>
        public const double VisibleBand = 2.0;

        private Profile boostView; // v5.0.2: Boost.OwnLook drawn through a reused Profile (render thread / one instance)

        public RingStyle() { }

        public RingStyle Copy() { return (RingStyle)MemberwiseClone(); }

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
            // v5.0.2 (W16 for W18's decision 4): a 强化准星 with a look of its own (Boost.OwnLook) is drawn exactly like a
            // normal crosshair with those 外观 values (Profile.FillBoostView; colours follow the hand when FollowHandColor).
            // The view Profile is made once per RingStyle and refilled, so a frame allocates nothing.
            if (boosted && p.Boost != null && p.Boost.OwnLook)
            {
                if (boostView == null) boostView = new Profile();
                p.FillBoostView(boostView);
                Set(boostView, handBlend, false);
                return;
            }
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
            // v4.0 准星样式: the new shapes scale their gap / length with the 强化 diameter ratio.
            // v4.1 (BRUTE40 P1-1): that ratio is kept within MinBoostScale..MaxBoostScale.
            CrosshairStyle cs = StyleShapes.Of(p);
            double k = 1.0;
            if (boosted && b != null && p.Diameter > 0) k = b.Diameter / p.Diameter;
            if (double.IsNaN(k) || double.IsInfinity(k) || k <= 0) k = 1.0;
            if (k > MaxBoostScale) k = MaxBoostScale;
            if (k < MinBoostScale) k = MinBoostScale;
            Kind = cs.Kind ?? ""; SGap = cs.Gap * k; SLength = cs.Length * k; SSpread = cs.Spread; SCount = cs.Count;
            PngTint = cs.PngTint; Png = null; PngMissing = false;
            if (Kind == "custom")
            {
                Png = StyleShapes.Png(cs.PngFile);
                if (Png == null) { PngMissing = true; Kind = ""; }
            }
            else if (Kind.Length > 0 && Array.IndexOf(StyleShapes.Kinds, Kind) < 0) Kind = "";
            Hole = double.IsNaN(cs.Hole) ? RingRaster.MinHole : Math.Max(RingRaster.MinHole, Math.Min(CrosshairStyle.MaxHole, cs.Hole));
            // the empty centre grows with the boosted crosshair (v4.1: not for the v3 十字, its arms keep their size when boosted)
            double holeK = boosted && k > 1 && !(Kind.Length == 0 && Shape == "cross") ? k : 1.0;
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
            Fit(holeK);
        }

        /// <summary>
        /// v4.1: keeps the look drawable. (1) BRUTE40 P1-1: a 强化 look whose style gap + length would need a canvas past
        /// RingRaster.MaxCanvas is shrunk (gap and length together) until it fits. (2) BRUTE40 P2-2: the 强化 growth of
        /// 中心留空 (× holeK) stops VisibleBand short of the farthest part of the boosted shape (never below the asked
        /// 中心留空, which always stays empty); Hidden flags a look that the asked 中心留空 (almost) hides.
        /// Allocation-free after the first call on a thread.
        /// </summary>
        private void Fit(double holeK)
        {
            if (Kind.Length > 0 && Kind != "custom" && RingRaster.CanvasRaw(this) > RingRaster.MaxCanvas - 1)
            {
                double g0 = SGap, l0 = SLength, lo = 0, hi = 1;
                for (int it = 0; it < 30; it++)
                {
                    double mid = (lo + hi) / 2;
                    SGap = g0 * mid; SLength = l0 * mid;
                    if (RingRaster.CanvasRaw(this) > RingRaster.MaxCanvas - 1) hi = mid; else lo = mid;
                }
                SGap = g0 * lo; SLength = l0 * lo;
            }
            double reach = RingRaster.CoreReach(this);
            if (double.IsNaN(reach) || double.IsInfinity(reach) || reach < 0) reach = 0;
            if (holeK > 1) Hole = Math.Min(Hole * holeK, Math.Max(Hole, reach - VisibleBand));
            // v5.0.2 (W16, BRUTE P3-4): outline (双色), feather and glow also draw past the core, so a look whose core the
            // 中心留空 covers can still be visible. Hidden only when none of them reaches VisibleBand past the hole.
            Hidden = !(reach + VisibleExtra(this) >= Hole + VisibleBand - 1e-9);
        }

        /// <summary>v5.0.2 (W16, BRUTE P3-4): how far outline (双色 only), feather and glow can draw past CoreReach, px.</summary>
        internal static double VisibleExtra(RingStyle s)
        {
            double e = 0;
            // the outline follows the shape's corners too (√2 on a diagonal edge); + 0.5 px for the anti-aliased edge
            if (s.Gradient == "twotone" && s.OuterThickness > 0 && !double.IsInfinity(s.OuterThickness)) e += s.OuterThickness * Math.Sqrt(2.0);
            e += 0.5;
            if (s.Feather > 0 && !double.IsInfinity(s.Feather)) e += s.Feather;
            if (s.GlowPx > 0 && s.GlowOpacityPct > 0 && !double.IsInfinity(s.GlowPx)) e += s.GlowPx;
            return e;
        }

        /// <summary>Changes whenever the raster changes (OpacityPct excluded: it is applied by the window, not the raster).</summary>
        public string Key()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9},{10},{11}|{12},{13},{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}|{23}",
                Shape, Gradient, Diameter, Thickness, Feather, OuterThickness, CenterDot, CenterDotSize, 0,
                R1, G1, B1, R2, G2, B2, CrossLength, CrossThickness, CrossGap, CrossTop, Dashes, DashGapPct, GlowPx, GlowOpacityPct, RotationDeg)
                + string.Format(System.Globalization.CultureInfo.InvariantCulture, "|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                    Kind, SGap, SLength, SSpread, SCount, Hole, Png == null ? "" : Png.Id, PngTint);
        }

        /// <summary>The v2.4.5 renderer applies exactly: plain ring, no dashes, no glow, no rotation.</summary>
        public bool IsClassic
        {
            get { return Kind.Length == 0 && Shape == "ring" && Dashes < 2 && !(GlowPx > 0 && GlowOpacityPct > 0) && Rot() == 0; }
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
        /// <summary>v4.0: the centre is always empty -- at least this radius (px) around the centre has alpha 0.</summary>
        public const double MinHole = CrosshairStyle.MinHole;
        /// <summary>v4.1 (BRUTE40 P1-1/P2-5): largest canvas side, px (1024² × 4 B = 4 MB per buffer). Validated looks fit
        /// (the largest, a v3 十字 with every value at its maximum, needs 990); RingStyle.Set shrinks a 强化 look to fit.</summary>
        public const int MaxCanvas = 1024;
        /// <summary>v4.1: smallest canvas side, px.</summary>
        public const int MinCanvas = 8;

        /// <summary>S = even(ceil(Diameter + Thickness + 2w [+ 2*OuterThickness for twotone]) + 6), w = max(Feather, 1).</summary>
        public static int CanvasSize(Profile p) { return CanvasSize(RingStyle.From(p, 0, false)); }

        /// <summary>Finite and ≥ 0, else 0 (a profile that skipped Validate() must not give a negative / NaN canvas).</summary>
        private static double Pos(double v) { return v > 0 && !double.IsInfinity(v) ? v : 0.0; }

        /// <summary>
        /// Ring: the v2.4.5 formula (+ 2 × glow). Other shapes: even(ceil(2 × extent + 2w + 2 × glow) + 6), extent = the
        /// farthest any part (incl. outline, rotation) reaches from the centre. v4.1: always MinCanvas..MaxCanvas.
        /// </summary>
        public static int CanvasSize(RingStyle p)
        {
            double s = CanvasRaw(p);
            if (!(s >= MinCanvas)) s = MinCanvas; // also NaN
            if (s > MaxCanvas) s = MaxCanvas;
            int n = (int)s;
            if ((n & 1) != 0) n++;
            return n > MaxCanvas ? MaxCanvas : n;
        }

        /// <summary>v4.1: the canvas side the look needs, before the MinCanvas..MaxCanvas clamp (RingStyle.Set fits 强化
        /// looks with it). Not rounded to even.</summary>
        internal static double CanvasRaw(RingStyle p)
        {
            double w = Math.Max(Pos(p.Feather), 1.0);
            bool two = p.Gradient == "twotone";
            double glow = (p.GlowPx > 0 && p.GlowOpacityPct > 0) ? Pos(p.GlowPx) : 0.0;
            double D = Pos(p.Diameter), T = Pos(p.Thickness), OT = Pos(p.OuterThickness);
            string kind = p.Kind ?? "";
            double s;
            if (kind == "custom" && p.Png != null)
            {
                // the picture's longer side = Diameter, any rotation; v4.1 (BRUTE40 P2-1): + the half source pixel the
                // bilinear filter spreads past the picture edge when it is enlarged (0.5 × dest px per source px)
                double f = D / Math.Max(1, Math.Max(p.Png.W, p.Png.H));
                double ext = (D / 2.0 + 0.5 * Math.Max(1.0, f)) * Math.Sqrt(2.0);
                s = Math.Ceiling(2.0 * ext + 2.0 * w) + 6;
            }
            else if (kind.Length > 0)
            {
                double o = two ? OT : 0.0;
                double ext = Pos(StyleShapes.ExtentOf(p)) + o;
                s = Math.Ceiling(2.0 * ext + 2.0 * w + 2.0 * glow) + 6;
            }
            else if (p.Shape == "ring")
            {
                double outer = two ? 2.0 * OT : 0.0;
                s = Math.Ceiling(D + T + outer + 2.0 * w + 2.0 * glow) + 6;
            }
            else
            {
                double o = two ? OT : 0.0;
                double ext = 0;
                if (HasRing(p.Shape)) ext = D / 2.0 + T / 2.0 + o;
                if (p.Shape == "square") ext *= Math.Sqrt(2.0);
                if (HasCross(p.Shape))
                {
                    double a = Pos(p.CrossGap) + Pos(p.CrossLength) + o, b = Pos(p.CrossThickness) / 2.0 + o;
                    ext = Math.Max(ext, Math.Sqrt(a * a + b * b));
                }
                if (p.Shape == "dot") ext = Math.Max(ext, D / 2.0 + o);
                if (p.CenterDot) ext = Math.Max(ext, Pos(p.CenterDotSize) / 2.0);
                s = Math.Ceiling(2.0 * ext + 2.0 * w + 2.0 * glow) + 6;
            }
            return s;
        }

        /// <summary>
        /// v4.1: farthest point of the shape's core (no outline / feather / glow) from the centre, px. The 中心留空 never
        /// reaches past CoreReach − RingStyle.VisibleBand, so part of the crosshair always stays drawn. Allocation-free.
        /// </summary>
        public static double CoreReach(RingStyle p)
        {
            string kind = p.Kind ?? "";
            if (kind == "custom") return p.Png == null ? 0 : p.Png.Reach * Pos(p.Diameter);
            if (kind.Length > 0) return StyleShapes.CoreReachOf(p);
            double R = Pos(p.Diameter) / 2.0, hh = Pos(p.Thickness) / 2.0, e = 0;
            if (HasRing(p.Shape)) e = p.Shape == "square" ? Math.Sqrt(2.0) * (R + hh) : R + hh;
            if (HasCross(p.Shape))
            {
                double a = Pos(p.CrossGap) + Pos(p.CrossLength), b = Pos(p.CrossThickness) / 2.0;
                e = Math.Max(e, Math.Sqrt(a * a + b * b));
            }
            if (p.Shape == "dot") e = Math.Max(e, R);
            if (p.CenterDot) e = Math.Max(e, Pos(p.CenterDotSize) / 2.0);
            return e;
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
            RenderNoHole(s, size, cx, cy, buf);
            ApplyHole(buf, size, cx, cy, HoleOf(s));
        }

        /// <summary>The 中心留空 radius drawn for a look: its Hole, never below MinHole.</summary>
        public static double HoleOf(RingStyle s) { return Math.Max(MinHole, double.IsNaN(s.Hole) ? MinHole : s.Hole); }

        /// <summary>v4.1 test hooks (tests-v4 only): NoTileCull = the per-pixel loops without the empty-tile skip and the per-tile
        /// primitive subset (the output is identical, only slower); PngGeneralOnly = 自定义图片 without the separable path
        /// (same samples, summed in the 2-D order). RenderCalls counts every rasterisation (RenderNoHole).</summary>
        internal static bool NoTileCull = false, PngGeneralOnly = false;
        internal static long RenderCalls;

        /// <summary>Render without cutting 中心留空 (RasterCache masters; the cut is made at the exact centre later).</summary>
        internal static void RenderNoHole(RingStyle s, int size, double cx, double cy, int[] buf)
        {
            System.Threading.Interlocked.Increment(ref RenderCalls);
            if (s.Kind == "custom" && s.Png != null) RenderPng(s, size, cx, cy, buf);
            else if (s.Kind.Length > 0) RenderKind(s, size, cx, cy, buf);
            else if (s.IsClassic) RenderClassic(s, size, cx, cy, buf);
            else RenderGeneral(s, size, cx, cy, buf);
        }

        /// <summary>v4.1 (REBRUTE41 P2-4) tile side for the empty-tile skip of RenderKind / RenderGeneral.</summary>
        internal const int TileSize = 16;

        /// <summary>double -> int index, clamped to lo..hi (NaN -> lo): no int overflow for huge values (BRUTE40 P2-4).</summary>
        private static int Idx(double v, int lo, int hi)
        {
            if (!(v > lo)) return lo;
            if (v >= hi) return hi;
            return (int)v;
        }

        /// <summary>
        /// v4.0 中心留空 (every style): pixels whose centre is closer than r to (cx, cy) become fully transparent; the next
        /// 1 px fades in so the cut edge is not jagged. Premultiplied pixels stay premultiplied.
        /// v4.1 (BRUTE40 P2-4): the loop bounds are clamped in double first, so a huge r (or centre) clears the canvas
        /// instead of overflowing to an empty loop; a NaN centre counts as the canvas centre.
        /// </summary>
        public static void ApplyHole(int[] buf, int size, double cx, double cy, double r)
        {
            if (r <= 0 || double.IsNaN(r) || size <= 0) return;
            if (double.IsNaN(cx) || double.IsInfinity(cx)) cx = size / 2.0;
            if (double.IsNaN(cy) || double.IsInfinity(cy)) cy = size / 2.0;
            if (r > 4.0 * size + Math.Abs(cx) + Math.Abs(cy)) r = 4.0 * size + Math.Abs(cx) + Math.Abs(cy); // covers the canvas
            double r1 = r + 1.0;
            int j0 = Idx(Math.Floor(cy - r1 - 1), 0, size - 1), j1 = Idx(Math.Ceiling(cy + r1 + 1), 0, size - 1);
            int i0 = Idx(Math.Floor(cx - r1 - 1), 0, size - 1), i1 = Idx(Math.Ceiling(cx + r1 + 1), 0, size - 1);
            if (cy - r1 - 1 > size || cx - r1 - 1 > size || cy + r1 + 1 < 0 || cx + r1 + 1 < 0) return; // the hole is off the canvas
            for (int j = j0; j <= j1; j++)
            {
                double py = j + 0.5 - cy;
                for (int i = i0; i <= i1; i++)
                {
                    int idx = j * size + i;
                    int v = buf[idx];
                    if (v == 0) continue;
                    double px = i + 0.5 - cx;
                    double d = Math.Sqrt(px * px + py * py);
                    if (d < r) { buf[idx] = 0; continue; }
                    if (d >= r1) continue;
                    double f = d - r;
                    int A = (int)(((uint)v) >> 24), R = (v >> 16) & 0xFF, G = (v >> 8) & 0xFF, B = v & 0xFF;
                    A = (int)Math.Round(A * f); R = (int)Math.Round(R * f); G = (int)Math.Round(G * f); B = (int)Math.Round(B * f);
                    if (R > A) R = A; if (G > A) G = A; if (B > A) B = A;
                    buf[idx] = A <= 0 ? 0 : ((A << 24) | (R << 16) | (G << 8) | B);
                }
            }
        }

        /// <summary>
        /// v4.0 shapes (StyleShapes.Kinds): one signed distance for the union of the shape's primitives; colour modes,
        /// outline ("twotone" = core in Color + an outline of OuterThickness in Color2), glow and rotation as in RenderGeneral.
        /// </summary>
        private static void RenderKind(RingStyle p, int size, double cx, double cy, int[] buf)
        {
            System.Collections.Generic.List<ShapePrim> list = StyleShapes.Build(p);
            ShapePrim[] prims = list.ToArray();
            int nP = prims.Length;
            int r1 = p.R1, g1 = p.G1, b1 = p.B1, r2 = p.R2, g2 = p.G2, b2 = p.B2;
            double w = Math.Max(p.Feather, 1.0);
            int mode = p.Gradient == "radial" ? 1 : (p.Gradient == "sweep" ? 2 : (p.Gradient == "twotone" ? 3 : 0));
            double to = mode == 3 ? p.OuterThickness : 0.0;
            double ext = Math.Max(StyleShapes.Extent(list), 0.001);
            double glowPx = p.GlowPx > 0 && p.GlowOpacityPct > 0 ? p.GlowPx : 0.0;
            double glowA = Clamp01(p.GlowOpacityPct / 100.0);
            double rad = p.Rot() * Math.PI / 180.0;
            double cs = Math.Cos(rad), sn = Math.Sin(rad);
            double reach = ext + to + w + glowPx + 1.0;
            // v4.1 (REBRUTE41 P2-4): 16 px tiles. The shape's signed distance is 1-Lipschitz, so a tile whose centre is more
            // than zeroAt + half its diagonal from every primitive holds only alpha-0 pixels (left cleared, no per-pixel work),
            // and inside a tile only the primitives that can be the nearest one are evaluated (the minimum is unchanged).
            // Every pixel is identical to the untiled loop (NoTileCull, tests-v4 R41-18).
            double zeroAt = to + Math.Max(w - 0.5, glowPx) + 0.01; // dist = sd - to ≥ max(w − 0.5, glow) -> alpha 0
            Array.Clear(buf, 0, Math.Min(buf.Length, size * size));
            ShapePrim[] sub = new ShapePrim[nP];
            double[] dk = new double[nP];
            bool cull = !NoTileCull;
            for (int tj = 0; tj < size; tj += TileSize)
            {
                int j1 = Math.Min(size, tj + TileSize) - 1;
                for (int ti = 0; ti < size; ti += TileSize)
                {
                    int i1 = Math.Min(size, ti + TileSize) - 1;
                    ShapePrim[] use = prims;
                    int useN = nP;
                    if (cull)
                    {
                        double hx = (i1 - ti) / 2.0, hy = (j1 - tj) / 2.0, half = Math.Sqrt(hx * hx + hy * hy);
                        double tsx = ti + hx + 0.5 - cx, tsy = tj + hy + 0.5 - cy;
                        if (Math.Sqrt(tsx * tsx + tsy * tsy) - half > reach) continue; // outside the reach circle
                        double tpx = tsx * cs + tsy * sn, tpy = -tsx * sn + tsy * cs;
                        double dmin = double.MaxValue;
                        for (int k = 0; k < nP; k++) { double d = StyleShapes.SdPrim(ref prims[k], tpx, tpy); dk[k] = d; if (d < dmin) dmin = d; }
                        if (dmin - half >= zeroAt) continue; // nothing of the shape, outline or glow reaches this tile
                        double lim = dmin + 2.0 * half + 1e-6; // a primitive farther than this is never the nearest in the tile
                        useN = 0;
                        for (int k = 0; k < nP; k++) if (dk[k] <= lim) sub[useN++] = prims[k];
                        use = sub;
                    }
                    for (int j = tj; j <= j1; j++)
                    {
                        double sy = j + 0.5 - cy;
                        int row = j * size;
                        for (int i = ti; i <= i1; i++)
                        {
                            double sx = i + 0.5 - cx;
                            if (sx * sx + sy * sy > reach * reach) continue; // stays 0
                            double px = sx * cs + sy * sn, py = -sx * sn + sy * cs;
                            double sd = StyleShapes.Sd(use, useN, px, py);
                            double dist = sd - to;
                            double a = Cov(dist, w);
                            double cr, cg, cb;
                            double t = Tone(mode, sd, Math.Sqrt(px * px + py * py) / ext, px, py);
                            Mixc(t, r1, g1, b1, r2, g2, b2, out cr, out cg, out cb);
                            double pa = a, pr = cr * a, pg = cg * a, pb = cb * a;
                            if (glowPx > 0)
                            {
                                double u = Clamp01(dist / glowPx);
                                double gA = glowA * (1.0 - u) * (1.0 - u) * (1.0 - a);
                                pa += gA; pr += r1 * gA; pg += g1 * gA; pb += b1 * gA;
                            }
                            int A8 = (int)Math.Round(Clamp01(pa) * 255.0);
                            if (A8 <= 0) continue; // stays 0
                            int R8 = Byte(pr), G8 = Byte(pg), B8 = Byte(pb);
                            if (R8 > A8) R8 = A8; if (G8 > A8) G8 = A8; if (B8 > A8) B8 = A8;
                            buf[row + i] = (A8 << 24) | (R8 << 16) | (G8 << 8) | B8;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 自定义图片: the picture scaled so its longer side = Diameter, centred, rotated; supersampled (up to 6 × 6 per
        /// pixel when shrinking, bilinear when enlarging). PngTint = the picture's alpha in Color.
        /// </summary>
        private static void RenderPng(RingStyle p, int size, double cx, double cy, int[] buf)
        {
            PngImage img0 = p.Png;
            int W0 = img0.W, H0 = img0.H;
            double D = p.Diameter;
            // v4.1 (BRUTE40 P1-2): sample the smallest half-size copy that is still at least Diameter (PngImage.Level), so a
            // 512 px picture drawn at 32 px needs 2 × 2 samples per pixel instead of 6 × 6 of the full picture.
            PngImage img = img0;
            for (int lv = 1; lv < 16; lv++)
            {
                PngImage next = img0.Level(lv);
                if (next == null || Math.Max(next.W, next.H) < D || next == img) break;
                img = next;
            }
            int W = img.W, H = img.H;
            int[] src = img.Pix;
            Array.Clear(buf, 0, Math.Min(buf.Length, size * size));
            if (src == null || W < 1 || H < 1 || src.Length < W * H) return;
            double f = D / Math.Max(1, Math.Max(W, H)); // dest px per source px
            if (f <= 0 || double.IsNaN(f) || double.IsInfinity(f)) return;
            int n = f >= 1 ? 1 : Math.Min(6, (int)Math.Ceiling(1.0 / f));
            double rad = p.Rot() * Math.PI / 180.0;
            double cs = Math.Cos(rad), sn = Math.Sin(rad);
            // v4.1: only the pixels the (rotated) picture can touch: its half sizes + the bilinear spread + 1 px
            double hw = W * f / 2.0 + Math.Max(1.0, f) + 1.0, hh = H * f / 2.0 + Math.Max(1.0, f) + 1.0;
            double ex = Math.Abs(cs) * hw + Math.Abs(sn) * hh, ey = Math.Abs(sn) * hw + Math.Abs(cs) * hh;
            int jA = Idx(Math.Floor(cy - ey), 0, size - 1), jB = Idx(Math.Ceiling(cy + ey), 0, size - 1);
            int iA = Idx(Math.Floor(cx - ex), 0, size - 1), iB = Idx(Math.Ceiling(cx + ex), 0, size - 1);
            bool tint = p.PngTint;
            if (rad == 0 && !PngGeneralOnly)
            {
                PngAxis(src, W, H, f, n, cx, cy, iA, iB, jA, jB, size, buf, tint, p.R1, p.G1, p.B1);
                return;
            }
            // Rotated: the same n × n bilinear samples as v4.0 (bilinear inlined, identical arithmetic).
            double[] off = new double[n];
            for (int k = 0; k < n; k++) off[k] = (k + 0.5) / n;
            double w2 = W / 2.0, h2 = H / 2.0;
            double nn = n * n;
            for (int j = jA; j <= jB; j++)
            {
                int row = j * size;
                for (int i = iA; i <= iB; i++)
                {
                    double sa = 0, sr = 0, sg = 0, sb = 0;
                    for (int yy = 0; yy < n; yy++)
                        for (int xx = 0; xx < n; xx++)
                        {
                            double sx = i + off[xx] - cx, sy = j + off[yy] - cy;
                            double px = sx * cs + sy * sn, py = -sx * sn + sy * cs;
                            double u = px / f + w2 - 0.5, v = py / f + h2 - 0.5;
                            int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
                            double fx = u - x0, fy = v - y0;
                            double a = 0, r = 0, g = 0, b = 0;
                            if (x0 >= 0 && y0 >= 0 && x0 + 1 < W && y0 + 1 < H)
                            {
                                int o = y0 * W + x0;
                                int c0 = src[o], c1 = src[o + 1], c2 = src[o + W], c3 = src[o + W + 1];
                                if ((c0 | c1 | c2 | c3) == 0) continue; // nothing to add (every product is 0)
                                double w0 = (1 - fx) * (1 - fy), w1 = fx * (1 - fy), wq = (1 - fx) * fy, w3 = fx * fy;
                                if (w0 > 0) { a += ((c0 >> 24) & 0xFF) * w0; r += ((c0 >> 16) & 0xFF) * w0; g += ((c0 >> 8) & 0xFF) * w0; b += (c0 & 0xFF) * w0; }
                                if (w1 > 0) { a += ((c1 >> 24) & 0xFF) * w1; r += ((c1 >> 16) & 0xFF) * w1; g += ((c1 >> 8) & 0xFF) * w1; b += (c1 & 0xFF) * w1; }
                                if (wq > 0) { a += ((c2 >> 24) & 0xFF) * wq; r += ((c2 >> 16) & 0xFF) * wq; g += ((c2 >> 8) & 0xFF) * wq; b += (c2 & 0xFF) * wq; }
                                if (w3 > 0) { a += ((c3 >> 24) & 0xFF) * w3; r += ((c3 >> 16) & 0xFF) * w3; g += ((c3 >> 8) & 0xFF) * w3; b += (c3 & 0xFF) * w3; }
                            }
                            else Bilinear(src, W, H, u, v, out a, out r, out g, out b);
                            sa += a; sr += r; sg += g; sb += b;
                        }
                    sa /= nn; sr /= nn; sg /= nn; sb /= nn;
                    if (tint) { sr = p.R1 * sa / 255.0; sg = p.G1 * sa / 255.0; sb = p.B1 * sa / 255.0; }
                    int A8 = Byte(sa);
                    if (A8 <= 0) { buf[row + i] = 0; continue; }
                    int R8 = Byte(sr), G8 = Byte(sg), B8 = Byte(sb);
                    if (R8 > A8) R8 = A8; if (G8 > A8) G8 = A8; if (B8 > A8) B8 = A8;
                    buf[row + i] = (A8 << 24) | (R8 << 16) | (G8 << 8) | B8;
                }
            }
        }

        /// <summary>
        /// v4.1 (REBRUTE41 P2-4) RenderPng without rotation: exactly the same n × n bilinear samples per pixel, but a bilinear
        /// weight is (x part) × (y part), so the samples are summed per axis -- first along each picture row for every output
        /// column, then down the columns -- instead of n² × 4 taps per pixel. Same values up to floating-point summation order
        /// (at most 1 of 255 apart, tests-v4 R41-18).
        /// </summary>
        private static void PngAxis(int[] src, int W, int H, double f, int n, double cx, double cy, int iA, int iB, int jA, int jB,
            int size, int[] buf, bool tint, int tr, int tg, int tb)
        {
            int cols = iB - iA + 1, rows = jB - jA + 1;
            if (cols <= 0 || rows <= 0) return;
            // taps per output column / row: consecutive source indices (the subsample step is at most 1 source px for the
            // levels RenderPng picks; K also covers a wider step)
            int K = (int)Math.Ceiling((n - 1) / (n * Math.Min(f, 1.0))) + 3;
            if (K < 2) K = 2;
            int[] cs0 = new int[cols], cc = new int[cols], rs0 = new int[rows], rc = new int[rows];
            double[] cw = new double[cols * K], rw = new double[rows * K];
            AxisTaps(iA, cols, n, f, cx, W, K, cs0, cc, cw);
            AxisTaps(jA, rows, n, f, cy, H, K, rs0, rc, rw);
            // ring of horizontally summed picture rows (4 channels per output column), enough for one output row's taps
            int ringN = K + 1;
            double[][] ring = new double[ringN][];
            int[] ringY = new int[ringN];
            for (int k = 0; k < ringN; k++) { ring[k] = new double[cols * 4]; ringY[k] = int.MinValue; }
            double nn = n * n;
            for (int q = 0; q < rows; q++)
            {
                int j = jA + q, row = j * size;
                int y0 = rs0[q], ny = rc[q];
                for (int t = 0; t < ny; t++)
                {
                    int y = y0 + t, slot = y % ringN;
                    if (ringY[slot] == y) continue;
                    ringY[slot] = y;
                    double[] h = ring[slot];
                    int so = y * W;
                    for (int c = 0; c < cols; c++)
                    {
                        double a = 0, r = 0, g = 0, b = 0;
                        int x0 = cs0[c], m = cc[c], wo = c * K;
                        for (int k = 0; k < m; k++)
                        {
                            int v = src[so + x0 + k];
                            if (v == 0) continue;
                            double wt = cw[wo + k];
                            a += ((v >> 24) & 0xFF) * wt; r += ((v >> 16) & 0xFF) * wt; g += ((v >> 8) & 0xFF) * wt; b += (v & 0xFF) * wt;
                        }
                        int ho = c * 4;
                        h[ho] = a; h[ho + 1] = r; h[ho + 2] = g; h[ho + 3] = b;
                    }
                }
                for (int c = 0; c < cols; c++)
                {
                    double sa = 0, sr = 0, sg = 0, sb = 0;
                    int ho = c * 4, wo = q * K;
                    for (int t = 0; t < ny; t++)
                    {
                        double wt = rw[wo + t];
                        if (wt == 0) continue;
                        double[] h = ring[(y0 + t) % ringN];
                        sa += h[ho] * wt; sr += h[ho + 1] * wt; sg += h[ho + 2] * wt; sb += h[ho + 3] * wt;
                    }
                    sa /= nn; sr /= nn; sg /= nn; sb /= nn;
                    if (tint) { sr = tr * sa / 255.0; sg = tg * sa / 255.0; sb = tb * sa / 255.0; }
                    int A8 = Byte(sa);
                    if (A8 <= 0) continue; // buf was cleared
                    int R8 = Byte(sr), G8 = Byte(sg), B8 = Byte(sb);
                    if (R8 > A8) R8 = A8; if (G8 > A8) G8 = A8; if (B8 > A8) B8 = A8;
                    buf[row + iA + c] = (A8 << 24) | (R8 << 16) | (G8 << 8) | B8;
                }
            }
        }

        /// <summary>
        /// For output index o0 + q: the n subsamples s = o + (k + 0.5) / n − c map to the picture coordinate u = s / f + L / 2 −
        /// 0.5 (RenderPng's formula); bilinear taps floor(u) with 1 − frac and floor(u) + 1 with frac; summed per source index
        /// into one run [start, start + count), indices outside 0..L−1 dropped (as RenderPng drops them).
        /// </summary>
        private static void AxisTaps(int o0, int cnt, int n, double f, double c, int L, int K, int[] start, int[] count, double[] wt)
        {
            double l2 = L / 2.0;
            for (int q = 0; q < cnt; q++)
            {
                int o = o0 + q, first = int.MinValue, wo = q * K, used = 0;
                for (int k = 0; k < K; k++) wt[wo + k] = 0;
                for (int k = 0; k < n; k++)
                {
                    double s = o + (k + 0.5) / n - c;
                    double u = s / f + l2 - 0.5;
                    if (double.IsNaN(u) || u < -1e9 || u > 1e9) continue; // no tap (outside any picture)
                    int x0 = (int)Math.Floor(u);
                    double fx = u - x0;
                    if (first == int.MinValue) first = x0;
                    int rel = x0 - first;
                    if (rel < 0 || rel + 1 >= K) continue; // cannot happen for the levels RenderPng uses
                    wt[wo + rel] += 1 - fx;
                    wt[wo + rel + 1] += fx;
                    if (rel + 2 > used) used = rel + 2;
                }
                // drop taps outside the picture
                int a = first;
                int lo = 0, hi = used - 1;
                if (first != int.MinValue)
                {
                    while (lo <= hi && ((long)a + lo < 0)) lo++;
                    while (hi >= lo && ((long)a + hi >= L)) hi--;
                }
                if (first == int.MinValue || lo > hi) { start[q] = 0; count[q] = 0; continue; }
                if (lo > 0) for (int k = lo; k <= hi; k++) wt[wo + k - lo] = wt[wo + k];
                for (int k = hi - lo + 1; k < K; k++) wt[wo + k] = 0;
                start[q] = a + lo; count[q] = hi - lo + 1;
            }
        }

        /// <summary>Premultiplied bilinear sample (0..255 channels); outside the picture = transparent.</summary>
        private static void Bilinear(int[] src, int W, int H, double u, double v, out double a, out double r, out double g, out double b)
        {
            a = r = g = b = 0;
            int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
            double fx = u - x0, fy = v - y0;
            for (int k = 0; k < 4; k++)
            {
                int x = x0 + (k & 1), y = y0 + (k >> 1);
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                double wgt = ((k & 1) == 0 ? 1 - fx : fx) * ((k >> 1) == 0 ? 1 - fy : fy);
                if (wgt <= 0) continue;
                int c = src[y * W + x];
                a += ((c >> 24) & 0xFF) * wgt; r += ((c >> 16) & 0xFF) * wgt; g += ((c >> 8) & 0xFF) * wgt; b += (c & 0xFF) * wgt;
            }
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
            // v4.1 (REBRUTE41 P2-4): 16 px tiles skipped when every part is provably too far for any coverage or glow. Each
            // part's distance (ring band without the dash cut, which only adds distance; cross arms; disc; centre dot) is
            // 1-Lipschitz, so its value at the tile centre minus half the diagonal bounds every pixel of the tile. A NaN
            // bound never skips. Pixels are identical to the untiled loop (NoTileCull, tests-v4 R41-18).
            double zPart = Math.Max(w - 0.5, glowPx) + 0.01, zDot = Math.Max(0.5, glowPx) + 0.01;
            Array.Clear(buf, 0, Math.Min(buf.Length, size * size));
            bool cull = !NoTileCull;

            for (int tj = 0; tj < size; tj += TileSize)
            {
              int j1 = Math.Min(size, tj + TileSize) - 1;
              for (int ti = 0; ti < size; ti += TileSize)
              {
                int i1 = Math.Min(size, ti + TileSize) - 1;
                if (cull)
                {
                    double hx = (i1 - ti) / 2.0, hy = (j1 - tj) / 2.0, half = Math.Sqrt(hx * hx + hy * hy);
                    double tsx = ti + hx + 0.5 - cx, tsy = tj + hy + 0.5 - cy;
                    double tpx = tsx * cs + tsy * sn, tpy = -tsx * sn + tsy * cs;
                    double td = Math.Sqrt(tpx * tpx + tpy * tpy);
                    bool any = false;
                    if (ring)
                    {
                        double dd = square ? R + SdBox(tpx, tpy, 0, 0, R, R) : td;
                        if (!(Math.Abs(dd - bandC) - bandHalf - half >= zPart)) any = true;
                    }
                    if (!any && cross)
                    {
                        double sd = SdBox(tpx, tpy, armC, 0, armHx, armHy);
                        sd = Math.Min(sd, SdBox(tpx, tpy, -armC, 0, armHx, armHy));
                        sd = Math.Min(sd, SdBox(tpx, tpy, 0, armC, armHy, armHx));
                        if (p.CrossTop) sd = Math.Min(sd, SdBox(tpx, tpy, 0, -armC, armHy, armHx));
                        if (!(sd - to - half >= zPart)) any = true;
                    }
                    if (!any && disc && !(td - R - to - half >= zPart)) any = true;
                    if (!any && dot && !(td - dotHalf - half >= zDot)) any = true;
                    if (!any) continue; // the tile stays 0
                }
            for (int j = tj; j <= j1; j++)
            {
                double sy = j + 0.5 - cy;
                int row = j * size;
                for (int i = ti; i <= i1; i++)
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
                    if (A8 <= 0) continue; // stays 0
                    int R8 = Byte(pr), G8 = Byte(pg), B8 = Byte(pb);
                    if (R8 > A8) R8 = A8; if (G8 > A8) G8 = A8; if (B8 > A8) B8 = A8;
                    buf[row + i] = (A8 << 24) | (R8 << 16) | (G8 << 8) | B8;
                }
            }
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

    /// <summary>
    /// v4.1 the overlay's renderer (BRUTE40 P1-2, REBRUTE41 P2-4). While only the position changes nothing is rasterised.
    /// The overlay moves the window by whole pixels (UpdateLayeredWindow at the new place, no pixel work) and asks for a
    /// 1/8 px sub-pixel phase (Normalise keeps it at 0..7/8 per axis). The phase image is a copy:
    /// <para>- canvases up to PhaseMaxCanvas (every tile default, 「Leo默认」): the 8 × 8 phase images, each rendered exactly
    ///   once per look the first time it is shown (identical to RingRaster.Render), then handed out unchanged;</para>
    /// <para>- bigger canvases: one master per look (rendered once, without the 中心留空 cut), moved by the sub-pixel step with
    ///   a bilinear copy (integer maths, only the columns that hold something), then 中心留空 is cut at the exact centre.
    ///   The master holds real colours. When only the colours change (a 换手颜色 fade) it is rasterised once in weight form
    ///   (A, weight of 颜色, weight of 颜色2) and coloured while it is copied; BakeAfter frames after the colours stop
    ///   changing it is coloured into itself again (no rasterisation). 自定义图片 without 上色 ignores the colours.</para>
    /// Two looks are kept (e.g. normal + 强化); a master not used for DropAfterFrames frames while a small look is shown is
    /// freed. Not thread-safe: one instance per render thread.
    /// </summary>
    public sealed class RasterCache
    {
        /// <summary>Canvases up to this side keep their 64 phase images (at most 64 × 64² × 4 B = 1 MB per look).</summary>
        public const int PhaseMaxCanvas = 64;
        /// <summary>Sub-pixel steps per pixel (the overlay quantises the centre to 1/8 px).</summary>
        public const int Phases = 8;
        /// <summary>A master is freed after this many frames on a small (phase) look.</summary>
        public const int DropAfterFrames = 240;

        /// <summary>A master in weight form is coloured into itself (no rasterisation) once its colours have not changed for
        /// this many frames (the end of a 换手颜色 fade).</summary>
        public const int BakeAfter = 8;

        private sealed class Entry
        {
            public string Key;      // phased: the whole look; master: the look without its colours
            public int S;
            public bool Phased;
            public int[][] Img;     // phased: [py * Phases + px], allocated on first use
            public bool[] Valid;
            public int[] Pix;       // master
            public int[] Lo, Hi;    // per master row: first / last column with alpha (Lo > Hi = empty row)
            public bool ColourFree; // 自定义图片 without 上色: the colours do not change a pixel
            public bool Baked;      // master holds real colours C1 / C2; false = weights (A, weight 颜色, weight 颜色2, 0)
            public int C1, C2;      // colours of a baked master (0xRRGGBB)
            public int LastC1, LastC2, Stable; // weight form: frames since the colours last changed
            public bool PhaseHasColours;        // phased: C1 / C2 are the colours the Valid phase images were drawn in
        }

        private Entry cur, old;
        // v5.0.2 (W16, AUDIT F6): the look without its colours. A colour-only change (换手颜色 fade) no longer re-keys the
        // look (no key strings, no Select) and no longer drops the 64 phase images: fade frames are rendered straight into
        // the frame buffer, and the phase images are rebuilt only once the colours have been stable for BakeAfter frames.
        private readonly RasterChangeDetector look = new RasterChangeDetector(false);
        private int[] frame;        // master path output (the moved master), reused every frame
        private int[] fLo, fHi;     // per frame row: columns that may hold a non-zero pixel
        private int frameS;
        private int[] rowA, rowB;
        private int loA, hiA, loB, hiB;
        private int smallRun;       // frames served from a phase look while 'old' is a master

        /// <summary>Statistics (tests): exact renders (off the phase grid), master renders, moved masters, phase images
        /// rendered (first use), phase images handed out again; v5.0.2: phased fade frames rendered straight (no cache).</summary>
        public int ExactCount, MasterCount, ShiftCount, PhaseRenders, PhaseHits, FadeRenders;

        /// <summary>v4.1: whether a look of this canvas size is drawn from a moved master (else from its phase images).</summary>
        public static bool UsesMaster(RingStyle s, int S) { return S > PhaseMaxCanvas; }

        /// <summary>Bytes held now (phase images, masters, the frame buffer and the row buffers).</summary>
        public long Bytes
        {
            get
            {
                long b = EntryBytes(cur) + EntryBytes(old);
                if (frame != null) b += 4L * frame.Length + 8L * fLo.Length;
                if (rowA != null) b += 8L * rowA.Length;
                return b;
            }
        }

        /// <summary>Bytes held by masters only (R41-17).</summary>
        public long MasterBytes
        {
            get
            {
                long b = 0;
                if (cur != null && !cur.Phased) b += EntryBytes(cur);
                if (old != null && !old.Phased) b += EntryBytes(old);
                return b;
            }
        }

        private static long EntryBytes(Entry e)
        {
            if (e == null) return 0;
            long b = 0;
            if (e.Img != null) foreach (int[] a in e.Img) if (a != null) b += 4L * a.Length;
            if (e.Pix != null) b += 4L * e.Pix.Length + 8L * e.S;
            return b;
        }

        /// <summary>Forget everything (the next call renders again).</summary>
        public void Clear() { cur = old = null; frame = null; fLo = fHi = null; frameS = 0; rowA = rowB = null; look.Reset(); smallRun = 0; }

        /// <summary>
        /// Overlay placement (pure): a centre at a whole step (c − S/2 = 1, which Math.Round(... × 8) / 8 can give) is drawn
        /// one window pixel further at phase 0 instead -- identical pixels on screen -- so the phases stay 0..7/8 per axis.
        /// </summary>
        public static void Normalise(int S, ref int win, ref double c)
        {
            if (c - S / 2.0 >= 1.0) { c -= 1.0; win += 1; }
        }

        /// <summary>Renders s at (cx, cy) into buf (S × S, premultiplied), 中心留空 cut at (cx, cy).</summary>
        public void Render(RingStyle s, int S, double cx, double cy, int[] buf)
        {
            int[] img = Image(s, S, cx, cy);
            Array.Copy(img, buf, S * S);
        }

        /// <summary>
        /// The overlay's image of s at (cx, cy) (S × S, premultiplied, 中心留空 cut): a cached phase image or the internal
        /// frame buffer. Read only, valid until the next call. A centre off the 1/8 grid (never asked by the overlay) is
        /// rendered exactly.
        /// </summary>
        public int[] Image(RingStyle s, int S, double cx, double cy)
        {
            double dx = cx - S / 2.0, dy = cy - S / 2.0;
            if (!(Math.Abs(dx) <= S) || !(Math.Abs(dy) <= S)) return ExactFrame(s, S, cx, cy); // NaN / far away
            if (look.Changed(s, S, 0, 0) || cur == null || cur.S != S) Select(s, S);
            if (cur.Phased)
            {
                if (old != null && !old.Phased && ++smallRun >= DropAfterFrames) { old = null; frame = null; fLo = fHi = null; frameS = 0; rowA = rowB = null; }
                int px, py;
                if (!PhaseOf(dx, out px) || !PhaseOf(dy, out py)) return ExactFrame(s, S, cx, cy);
                Entry pe = cur;
                int pc1 = Rgb(s.R1, s.G1, s.B1), pc2 = Rgb(s.R2, s.G2, s.B2);
                if (!pe.PhaseHasColours) { pe.PhaseHasColours = true; pe.C1 = pc1; pe.C2 = pc2; pe.LastC1 = pc1; pe.LastC2 = pc2; pe.Stable = BakeAfter; }
                if (pc1 != pe.C1 || pc2 != pe.C2)
                {
                    // colours differ from the cached phase images: a fade frame (or a look swapped back in other colours)
                    if (pc1 == pe.LastC1 && pc2 == pe.LastC2) pe.Stable++; else { pe.Stable = 0; pe.LastC1 = pc1; pe.LastC2 = pc2; }
                    if (pe.Stable < BakeAfter)
                    {
                        int[] ff = Frame(S);
                        RingRaster.Render(s, S, cx, cy, ff);
                        for (int j = 0; j < S; j++) { fLo[j] = 0; fHi[j] = S - 1; }
                        FadeRenders++;
                        return ff;
                    }
                    Array.Clear(pe.Valid, 0, pe.Valid.Length); // stable in the new colours: rebuild the phase images
                    pe.C1 = pc1; pe.C2 = pc2;
                }
                int k = py * Phases + px;
                int[] img = cur.Img[k];
                if (cur.Valid[k]) { PhaseHits++; return img; }
                if (img == null || img.Length != S * S) { img = new int[S * S]; cur.Img[k] = img; }
                RingRaster.Render(s, S, cx, cy, img);
                cur.Valid[k] = true;
                PhaseRenders++;
                return img;
            }
            smallRun = 0;
            Entry e = cur;
            if (!e.ColourFree)
            {
                int c1 = Rgb(s.R1, s.G1, s.B1), c2 = Rgb(s.R2, s.G2, s.B2);
                if (e.Baked)
                {
                    // the colours changed (a 换手颜色 fade starts): ONE rasterisation into weight form, then colour per copy
                    if (c1 != e.C1 || c2 != e.C2) { RenderMaster(e, WeightLook(s), S); e.Baked = false; e.LastC1 = c1; e.LastC2 = c2; e.Stable = 0; }
                }
                else
                {
                    if (c1 == e.LastC1 && c2 == e.LastC2) e.Stable++; else { e.Stable = 0; e.LastC1 = c1; e.LastC2 = c2; }
                    if (e.Stable >= BakeAfter) Bake(e, S, s); // the fade is over: colour the master into itself (no rasterisation)
                }
            }
            int[] f = Frame(S);
            Shift(e, S, dx, dy, f);
            if (!e.Baked) Colour(f, S, s.R1, s.G1, s.B1, s.R2, s.G2, s.B2);
            RingRaster.ApplyHole(f, S, cx, cy, RingRaster.HoleOf(s));
            ShiftCount++;
            return f;
        }

        private static int Rgb(int r, int g, int b) { return ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF); }

        /// <summary>Rasterises the master of e from look l (centre of the canvas, no 中心留空 cut) and its row ranges.</summary>
        private void RenderMaster(Entry e, RingStyle l, int S)
        {
            if (e.Pix == null || e.Pix.Length != S * S) { e.Pix = new int[S * S]; e.Lo = new int[S]; e.Hi = new int[S]; }
            RingRaster.RenderNoHole(l, S, S / 2.0, S / 2.0, e.Pix);
            for (int j = 0; j < S; j++)
            {
                int row = j * S, lo = S, hi = -1;
                for (int i = 0; i < S; i++) if (e.Pix[row + i] != 0) { lo = i; break; }
                if (lo < S) for (int i = S - 1; i >= lo; i--) if (e.Pix[row + i] != 0) { hi = i; break; }
                e.Lo[j] = lo; e.Hi[j] = hi;
            }
            MasterCount++;
        }

        /// <summary>Weight form -> the current colours, in place (the master's own pixels: no rasterisation).</summary>
        private static void Bake(Entry e, int S, RingStyle s)
        {
            for (int j = 0; j < S; j++)
                if (e.Lo[j] <= e.Hi[j]) ColourRange(e.Pix, j * S + e.Lo[j], j * S + e.Hi[j], s.R1, s.G1, s.B1, s.R2, s.G2, s.B2);
            e.Baked = true; e.C1 = Rgb(s.R1, s.G1, s.B1); e.C2 = Rgb(s.R2, s.G2, s.B2);
        }

        private static bool PhaseOf(double d, out int k)
        {
            double k8 = d * Phases;
            k = (int)k8;
            return k8 == k && k >= 0 && k < Phases;
        }

        private int[] Frame(int S)
        {
            if (frame == null || frameS != S)
            {
                frame = new int[S * S]; fLo = new int[S]; fHi = new int[S]; frameS = S;
                for (int j = 0; j < S; j++) { fLo[j] = S; fHi[j] = -1; }
            }
            return frame;
        }

        private int[] ExactFrame(RingStyle s, int S, double cx, double cy)
        {
            ExactCount++;
            int[] f = Frame(S);
            RingRaster.Render(s, S, cx, cy, f);
            for (int j = 0; j < S; j++) { fLo[j] = 0; fHi[j] = S - 1; } // every row may hold pixels now
            return f;
        }

        /// <summary>The look with 颜色 = (255, 0, 0) and 颜色2 = (0, 255, 0): R = weight of 颜色, G = weight of 颜色2. Every
        /// renderer is linear in the two colours (gradient mix, outline, glow and 中心点 in 颜色, 上色 in 颜色).</summary>
        private static RingStyle WeightLook(RingStyle s)
        {
            RingStyle w = s.Copy();
            w.R1 = 255; w.G1 = 0; w.B1 = 0; w.R2 = 0; w.G2 = 255; w.B2 = 0;
            return w;
        }

        private void Select(RingStyle s, int S)
        {
            bool phased = S <= PhaseMaxCanvas;
            bool png = (s.Kind ?? "") == "custom" && s.Png != null;
            bool colourFree = !phased && png && !s.PngTint;
            string key;
            if (phased) key = "p|" + WeightLook(s).Key() + "|" + S.ToString(System.Globalization.CultureInfo.InvariantCulture); // without the colours
            else if (colourFree) key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "png|{0}|{1}|{2}|{3}", s.Png.Id, s.Diameter, s.Rot(), S);
            else key = "m|" + WeightLook(s).Key() + "|" + S.ToString(System.Globalization.CultureInfo.InvariantCulture); // without the colours
            if (cur != null && cur.Key == key) return;
            if (old != null && old.Key == key) { Entry t = cur; cur = old; old = t; smallRun = 0; return; }
            Entry e = old != null && old.S == S && old.Phased == phased ? old : new Entry(); // reuse the older buffers
            e.Key = key; e.S = S; e.Phased = phased; e.ColourFree = colourFree;
            if (phased)
            {
                if (e.Img == null) { e.Img = new int[Phases * Phases][]; e.Valid = new bool[Phases * Phases]; }
                else Array.Clear(e.Valid, 0, e.Valid.Length);
                e.PhaseHasColours = false;
                e.Pix = null; e.Lo = e.Hi = null;
            }
            else
            {
                e.Img = null; e.Valid = null;
                RenderMaster(e, s, S); // real colours: while they stay, every frame is a plain copy
                e.Baked = true; e.C1 = Rgb(s.R1, s.G1, s.B1); e.C2 = Rgb(s.R2, s.G2, s.B2); e.Stable = 0;
            }
            old = cur; cur = e;
            smallRun = 0;
        }

        /// <summary>Weight 0..256 and whole-pixel part of a shift.</summary>
        private static void Split(double d, out int whole, out int w)
        {
            double fl = Math.Floor(d);
            whole = (int)fl;
            w = (int)Math.Round((d - fl) * 256.0);
            if (w >= 256) { whole++; w = 0; }
        }

        /// <summary>Two premultiplied pixels mixed (P × (256 − w) + Q × w) / 256, all four channels at once; weights 1..255
        /// given as (256 − w, w), no branch (the hot loop of Shift).</summary>
        private static int MixW(uint P, uint Q, uint iw, uint uw)
        {
            uint rb = (((P & 0x00FF00FFu) * iw + (Q & 0x00FF00FFu) * uw + 0x00800080u) >> 8) & 0x00FF00FFu;
            uint ag = (((P >> 8) & 0x00FF00FFu) * iw + ((Q >> 8) & 0x00FF00FFu) * uw + 0x00800080u) & 0xFF00FF00u;
            return (int)(ag | rb);
        }

        /// <summary>out = M(c0) × (256 − w) + M(c0 − 1) × w with the bounds checked (the two ends of a row).</summary>
        private static int MixAt(int[] src, int row, int S, int c0, uint iw, uint uw)
        {
            uint P = c0 >= 0 && c0 < S ? (uint)src[row + c0] : 0u;
            uint Q = c0 - 1 >= 0 && c0 - 1 < S ? (uint)src[row + c0 - 1] : 0u;
            return (P | Q) == 0 ? 0 : MixW(P, Q, iw, uw);
        }

        /// <summary>Master row r moved right by ix + wx/256 px into dst; returns the written column range (lo &gt; hi = none).</summary>
        private static void Row(Entry m, int r, int S, int ix, int wx, int[] dst, out int lo, out int hi)
        {
            lo = S; hi = -1;
            if (r < 0 || r >= S || m.Lo[r] > m.Hi[r]) return;
            int a = m.Lo[r] + ix, b = m.Hi[r] + ix + (wx > 0 ? 1 : 0);
            if (a < 0) a = 0;
            if (b > S - 1) b = S - 1;
            if (a > b) return;
            int[] src = m.Pix;
            int row = r * S;
            lo = a; hi = b;
            if (wx == 0) { Array.Copy(src, row + a - ix, dst, a, b - a + 1); return; } // whole-pixel step: a plain copy
            uint iw = (uint)(256 - wx), uw = (uint)wx;
            // out(i) = M(i − ix) × (256 − wx) + M(i − ix − 1) × wx; only the two ends can read past the row
            dst[a] = MixAt(src, row, S, a - ix, iw, uw);
            int k = row - ix;
            for (int i = a + 1; i < b; i++)
            {
                uint P = (uint)src[k + i], Q = (uint)src[k + i - 1];
                dst[i] = (P | Q) == 0 ? 0 : MixW(P, Q, iw, uw);
            }
            if (b > a) dst[b] = MixAt(src, row, S, b - ix, iw, uw);
        }

        /// <summary>The master moved by (dx, dy) into buf. Clears only the columns the previous frame wrote (fLo / fHi) and
        /// records what this one writes.</summary>
        private void Shift(Entry m, int S, double dx, double dy, int[] buf)
        {
            int ix, wx, iy, wy;
            Split(dx, out ix, out wx);
            Split(dy, out iy, out wy);
            if (rowA == null || rowA.Length < S) { rowA = new int[S]; rowB = new int[S]; }
            for (int j = 0; j < S; j++)
                if (fLo[j] <= fHi[j]) { Array.Clear(buf, j * S + fLo[j], fHi[j] - fLo[j] + 1); fLo[j] = S; fHi[j] = -1; }
            // rowA = moved master row (j − iy), rowB = moved master row (j − iy − 1) = rowA of the previous j
            Array.Clear(rowA, 0, S); Array.Clear(rowB, 0, S);
            loA = S; hiA = -1; loB = S; hiB = -1;
            Row(m, -iy - 1, S, ix, wx, rowA, out loA, out hiA); // becomes rowB at j = 0
            for (int j = 0; j < S; j++)
            {
                int[] t = rowB; rowB = rowA; rowA = t;
                int tl = loB, th = hiB; loB = loA; hiB = hiA;
                if (tl <= th) Array.Clear(rowA, tl, th - tl + 1);
                Row(m, j - iy, S, ix, wx, rowA, out loA, out hiA);
                int lo = loA, hi = hiA;
                if (wy > 0 && loB <= hiB) { if (loB < lo) lo = loB; if (hiB > hi) hi = hiB; }
                if (lo > hi) continue;
                int o = j * S;
                fLo[j] = lo; fHi[j] = hi;
                if (wy == 0) { Array.Copy(rowA, lo, buf, o + lo, hi - lo + 1); continue; }
                int[] ra = rowA, rb = rowB;
                uint ivw = (uint)(256 - wy), uvw = (uint)wy;
                for (int i = lo; i <= hi; i++)
                {
                    uint P = (uint)ra[i], Q = (uint)rb[i];
                    buf[o + i] = (P | Q) == 0 ? 0 : MixW(P, Q, ivw, uvw);
                }
            }
        }

        /// <summary>Weights -> colours on the written columns: R = (w1 × r1 + w2 × r2) / 255 (same for G, B), at most A.</summary>
        private void Colour(int[] buf, int S, int r1, int g1, int b1, int r2, int g2, int b2)
        {
            for (int j = 0; j < S; j++)
            {
                int lo = fLo[j], hi = fHi[j];
                if (lo > hi) continue;
                ColourRange(buf, j * S + lo, j * S + hi, r1, g1, b1, r2, g2, b2);
            }
        }

        private static void ColourRange(int[] buf, int from, int to, int r1, int g1, int b1, int r2, int g2, int b2)
        {
            for (int i = from; i <= to; i++)
            {
                int v = buf[i];
                if (v == 0) continue;
                int A = (int)((uint)v >> 24), w1 = (v >> 16) & 0xFF, w2 = (v >> 8) & 0xFF;
                int R = (w1 * r1 + w2 * r2 + 127) / 255, G = (w1 * g1 + w2 * g2 + 127) / 255, B = (w1 * b1 + w2 * b2 + 127) / 255;
                if (R > A) R = A; if (G > A) G = A; if (B > A) B = A;
                buf[i] = (A << 24) | (R << 16) | (G << 8) | B;
            }
        }
    }
}
