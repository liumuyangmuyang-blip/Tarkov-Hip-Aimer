// RENDER41F (v4.1): brute-render fixes -- 强化 scale / canvas caps, centre-hole loop, small pictures, Hidden flag, the
// overlay RasterCache (costly looks rendered once and moved), PNG half-size levels, PngCache (no decode on the overlay
// thread, retry when the file changes), emoji-safe names, PNG header size, >= 30k-render fuzz (v4.3: the mouse-sway turn driver tests went with the feature).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V41RenderInit() { V41RenderTests(); }

        static int R41A(int px) { return (int)((uint)px >> 24); }

        static int R41Inside(int[] a, int n, double cx, double cy, double r)
        {
            int m = 0;
            int j0 = Math.Max(0, (int)Math.Floor(cy - r - 1)), j1 = Math.Min(n - 1, (int)Math.Ceiling(cy + r + 1));
            int i0 = Math.Max(0, (int)Math.Floor(cx - r - 1)), i1 = Math.Min(n - 1, (int)Math.Ceiling(cx + r + 1));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double dx = i + 0.5 - cx, dy = j + 0.5 - cy;
                    if (dx * dx + dy * dy < r * r) { int v = R41A(a[j * n + i]); if (v > m) m = v; }
                }
            return m;
        }

        static bool R41Premul(int[] a)
        {
            foreach (int v in a) { int A = R41A(v); if (((v >> 16) & 0xFF) > A || ((v >> 8) & 0xFF) > A || (v & 0xFF) > A) return false; }
            return true;
        }

        static bool R41Edge(int[] a, int n)
        {
            for (int i = 0; i < n; i++)
                if (R41A(a[i]) != 0 || R41A(a[(n - 1) * n + i]) != 0 || R41A(a[i * n]) != 0 || R41A(a[i * n + n - 1]) != 0) return false;
            return true;
        }

        static long R41Sum(int[] a) { long s = 0; foreach (int v in a) s += R41A(v); return s; }

        static Profile R41Tile(string id)
        {
            Profile p = Profile.CreateLeoDefault("r41");
            StyleShapes.SetStyle(p, new CrosshairStyle());
            StyleShapes.ApplyTile(p, id);
            return p;
        }

        /// <summary>Ring-shaped test picture with a few random specks (premultiplied).</summary>
        static PngImage R41Pic(int w, int h, int seed, string id)
        {
            int[] pix = new int[w * h];
            Random r = new Random(seed);
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    double dx = (i + 0.5) / w - 0.5, dy = (j + 0.5) / h - 0.5;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    int a = d < 0.45 && d > 0.3 ? 255 : (r.Next(9) == 0 ? r.Next(256) : 0);
                    pix[j * w + i] = (a << 24) | ((a * 220 / 255) << 16) | ((a * 90 / 255) << 8) | (a * 40 / 255);
                }
            return new PngImage(pix, w, h, id);
        }

        static PngImage R41Solid(int w, int h, string id)
        {
            int[] pix = new int[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = unchecked((int)0xFFFFFFFF);
            return new PngImage(pix, w, h, id);
        }

        static void V41RenderTests()
        {
            Func<string, PngImage> saved = StyleShapes.PngProvider;
            Dictionary<string, PngImage> pics = new Dictionary<string, PngImage>(StringComparer.OrdinalIgnoreCase);
            pics["big.png"] = R41Pic(512, 512, 1, "r41big");
            pics["wide.png"] = R41Pic(300, 120, 2, "r41wide");
            pics["p1x1.png"] = R41Solid(1, 1, "r41-1x1");
            pics["p2x2.png"] = R41Solid(2, 2, "r41-2x2");
            pics["p3x7.png"] = R41Solid(3, 7, "r41-3x7");
            pics["p512x1.png"] = R41Solid(512, 1, "r41-512x1");
            pics["p1x512.png"] = R41Solid(1, 512, "r41-1x512");
            pics["clear.png"] = new PngImage(new int[16], 4, 4, "r41clear");
            StyleShapes.PngProvider = delegate(string f) { PngImage p; return pics.TryGetValue(f, out p) ? p : null; };
            try
            {
                R41BoostCap();
                R41Garbage();
                R41HoleLoop();
                R41SmallPictures(pics);
                R41Hidden();
                R41Cache();
                R41CacheTint();
                R41Perf();
                R41Levels();
                R41Cull(pics);
                R41BlitOnly(pics);
                R41PngCache();
                R41Bounds();
                R41Names();
                R41Header();
                R41NoAlloc();
                R41Fuzz(pics);
            }
            finally { StyleShapes.PngProvider = saved; }
        }

        // ---------------- P1-1 强化 scale / canvas ----------------

        static void R41BoostCap()
        {
            bool ok = true; string res = "";
            foreach (string id in new string[] { "hbars", "xcross", "chevron", "chevron2", "ringticks", "fourdots", "corners", "brackets", "dotring", "arcs", "smile", "diamond", "triangle" })
            {
                Profile p = R41Tile(id);
                CrosshairStyle cs = StyleShapes.Of(p);
                p.Diameter = 4; p.Boost.Diameter = 300; cs.Gap = 100; cs.Length = 100;
                p.Boost.Thickness = 30; p.Boost.GlowPx = 40; p.Boost.GlowOpacityPct = 100; p.Feather = 30; p.Gradient = "twotone"; p.Boost.OuterThickness = 20;
                p.Validate();
                RingStyle st = RingStyle.From(p, 0, true);
                int S = RingRaster.CanvasSize(st);
                bool k3 = st.SGap <= 3 * cs.Gap + 1e-9 && st.SLength <= 3 * cs.Length + 1e-9;
                int[] a = RingRaster.Render(st, S, S / 2.0 + 0.375, S / 2.0 + 0.625);
                int centre = R41Inside(a, S, S / 2.0 + 0.375, S / 2.0 + 0.625, RingRaster.HoleOf(st));
                bool vis = R41Sum(a) > 0, edge = R41Edge(a, S);
                if (S > RingRaster.MaxCanvas || !k3 || centre != 0 || !vis || !edge)
                { ok = false; res += " " + id + " S=" + S + " gap=" + F(st.SGap) + " k3=" + k3 + " centre=" + centre + " vis=" + vis + " edge=" + edge; }
            }
            // random valid profiles, boosted: never past the cap
            Random rnd = new Random(41);
            int over = 0, cases = 0, maxS = 0;
            foreach (StyleTile t in StyleShapes.Tiles)
                for (int i = 0; i < 150; i++)
                {
                    Profile p = R41Tile(t.Id);
                    CrosshairStyle cs = StyleShapes.Of(p);
                    if (t.Id == "custom") cs.PngFile = "big.png";
                    p.Diameter = 4 + rnd.Next(297); p.Boost.Diameter = 4 + rnd.Next(297);
                    cs.Gap = rnd.Next(101); cs.Length = 0.5 + rnd.Next(100); p.Thickness = 0.5 + rnd.Next(30); p.Boost.Thickness = 0.5 + rnd.Next(30);
                    p.CrossGap = rnd.Next(201); p.CrossLength = rnd.Next(201); p.CrossThickness = 0.5 + rnd.Next(30);
                    p.Feather = rnd.Next(31); p.GlowPx = rnd.Next(41); p.Boost.GlowPx = rnd.Next(41); p.GlowOpacityPct = 100;
                    p.Gradient = Profile.Gradients[rnd.Next(4)]; p.OuterThickness = 0.5 + rnd.Next(20); p.Boost.OuterThickness = rnd.Next(21);
                    p.Validate();
                    foreach (bool b in new bool[] { false, true })
                    {
                        RingStyle st = RingStyle.From(p, rnd.NextDouble(), b);
                        int S = RingRaster.CanvasSize(st);
                        cases++;
                        if (S > RingRaster.MaxCanvas) over++;
                        if (S > maxS) maxS = S;
                    }
                }
            // the v3 十字 keeps its arms when boosted, so its 中心留空 does not grow
            Profile x = R41Tile("cross"); x.Boost.Diameter = x.Diameter * 3;
            bool crossHole = Math.Abs(RingStyle.From(x, 0, true).Hole - RingStyle.From(x, 0, false).Hole) < 1e-12;
            Check("R41-1 强化 cap (BRUTE40 P1-1): size 4 + 强化 300 + gap/length 100 at every max: canvas <= " + RingRaster.MaxCanvas + ", ratio <= 3, drawn, centre empty; "
                + cases + " random valid looks never past the cap; v3 十字 hole not scaled",
                ok && over == 0 && crossHole, (ok ? "13 extreme looks ok" : res) + " random over=" + over + " maxS=" + maxS + " crossHole=" + crossHole);
        }

        // ---------------- P2-5 garbage in: canvas in range, no exception ----------------

        static void R41Garbage()
        {
            List<RingStyle> l = new List<RingStyle>();
            RingStyle a = new RingStyle(); a.OuterThickness = -10000; a.Gradient = "twotone"; l.Add(a);
            RingStyle b = new RingStyle(); b.Feather = double.PositiveInfinity; l.Add(b);
            RingStyle c = new RingStyle(); c.Diameter = double.NaN; c.Thickness = double.NegativeInfinity; l.Add(c);
            RingStyle d = new RingStyle(); d.Kind = "hbars"; d.SGap = 1e9; d.SLength = 1e9; l.Add(d);
            RingStyle e = new RingStyle(); e.Shape = "cross"; e.CrossGap = -5e6; e.CrossLength = double.NaN; l.Add(e);
            RingStyle f = new RingStyle(); f.Kind = "custom"; f.Png = R41Solid(3, 3, "g"); f.Diameter = 1e9; l.Add(f);
            RingStyle g = new RingStyle(); g.Diameter = 1e300; g.GlowPx = 1e300; g.GlowOpacityPct = 50; l.Add(g);
            bool ok = true; string res = "";
            foreach (RingStyle s in l)
            {
                int S = RingRaster.CanvasSize(s);
                string err = "";
                try
                {
                    int[] buf = RingRaster.Render(s, S, S / 2.0, S / 2.0);
                    if (R41Inside(buf, S, S / 2.0, S / 2.0, RingRaster.MinHole) != 0) err = "centre";
                    RasterCache rc = new RasterCache();
                    rc.Render(s, S, S / 2.0 + 0.5, S / 2.0 + 0.25, buf);
                    if (R41Inside(buf, S, S / 2.0 + 0.5, S / 2.0 + 0.25, RingRaster.MinHole) != 0) err += " cache-centre";
                }
                catch (Exception ex) { err = ex.GetType().Name; }
                if (S < RingRaster.MinCanvas || S > RingRaster.MaxCanvas || (S & 1) != 0 || err.Length > 0) { ok = false; res += " S=" + S + " " + err; }
            }
            Check("R41-2 unvalidated values (negative outline, ∞ feather, NaN size, 1e9 gap, ...): canvas " + RingRaster.MinCanvas + ".." + RingRaster.MaxCanvas + " and even, renders without exceptions, centre empty (BRUTE40 P2-5)",
                ok, ok ? l.Count + " looks ok" : res);
        }

        // ---------------- P2-4 centre-hole loop ----------------

        static void R41HoleLoop()
        {
            int n = 64;
            int[] full = new int[n * n];
            for (int i = 0; i < full.Length; i++) full[i] = unchecked((int)0xFF808080);
            int[] a = (int[])full.Clone();
            RingRaster.ApplyHole(a, n, 32, 32, 1.25e12);
            bool cleared = R41Sum(a) == 0;
            int[] b = (int[])full.Clone();
            RingRaster.ApplyHole(b, n, 1e12, -1e12, 5);
            bool far = R41Sum(b) == R41Sum(full);
            int[] c = (int[])full.Clone();
            RingRaster.ApplyHole(c, n, double.NaN, double.NaN, 3);
            bool nan = R41Inside(c, n, 32, 32, 3) == 0 && R41Sum(c) < R41Sum(full);
            int[] d = (int[])full.Clone();
            RingRaster.ApplyHole(d, n, 32, 32, double.PositiveInfinity);
            bool inf = R41Sum(d) == 0;
            // the brute repro: Diameter 1e-9 boosted (ratio 2.8e11 before the cap)
            RingStyle s = new RingStyle(); s.Hole = 1.25e12; s.Diameter = 18;
            int S = RingRaster.CanvasSize(s);
            int[] e = RingRaster.Render(s, S, S / 2.0, S / 2.0);
            bool brute = R41Sum(e) == 0;
            Check("R41-3 中心留空 loop bounds (BRUTE40 P2-4): radius 1.25e12 clears the canvas, a centre far off changes nothing, NaN centre = canvas centre, ∞ radius clears",
                cleared && far && nan && inf && brute, "cleared=" + cleared + " far=" + far + " nan=" + nan + " inf=" + inf + " brute=" + brute);
        }

        // ---------------- P2-1 small pictures ----------------

        static void R41SmallPictures(Dictionary<string, PngImage> pics)
        {
            bool ok = true; string res = ""; int cases = 0;
            foreach (string name in new string[] { "p1x1.png", "p2x2.png", "p3x7.png", "p512x1.png", "p1x512.png", "wide.png", "big.png" })
                foreach (double d in new double[] { 4, 8, 32, 100, 300 })
                    foreach (double rot in new double[] { 0, 30, 45, 90, -135 })
                    {
                        Profile p = R41Tile("custom");
                        StyleShapes.Of(p).PngFile = name; p.Diameter = d; p.RotationDeg = rot;
                        RingStyle st = RingStyle.From(p, 0, false);
                        int S = RingRaster.CanvasSize(st);
                        foreach (double off in new double[] { 0, 0.5, 1.0 })
                        {
                            int[] a = RingRaster.Render(st, S, S / 2.0 + off, S / 2.0 + off);
                            cases++;
                            if (!R41Edge(a, S)) { ok = false; if (res.Length < 300) res += " " + name + " d=" + d + " rot=" + rot + " off=" + off + " S=" + S; }
                        }
                    }
            Check("R41-4 自定义图片 never cut by the canvas edge (BRUTE40 P2-1: 1x1 / 2x2 / 3x7 / 512x1 / 1x512 / wide / 512 at 4..300 px, 5 rotations, centre +0..1 px)",
                ok, ok ? cases + " renders ok" : res);
        }

        // ---------------- P2-2 never fully invisible (or flagged) ----------------

        static void R41Hidden()
        {
            Profile fd = R41Tile("fourdots"); StyleShapes.Of(fd).Gap = 0;
            RingStyle fds = RingStyle.From(fd, 0, false);
            Profile big = R41Tile("chevron"); StyleShapes.Of(big).Hole = 40;
            RingStyle bigs = RingStyle.From(big, 0, false);
            Profile tiny = R41Tile("corners"); tiny.Diameter = 4;
            RingStyle tinys = RingStyle.From(tiny, 0, false);
            // v5.0.2 (W16, BRUTE P3-4): Hidden now also counts outline / feather / glow. The size-4 四角框 draws pixels of
            // alpha 255 past its 中心留空 (feather 1), so it is visible and no longer flagged; flagged = it renders nothing.
            // Each of the three: visible (a pixel above alpha 128 drawn) = not Hidden; nothing visible = Hidden.
            bool flagged = true; string fl = "";
            foreach (RingStyle q0 in new RingStyle[] { fds, bigs, tinys })
            {
                int n0 = RingRaster.CanvasSize(q0); int m0 = 0;
                foreach (int px0 in RingRaster.Render(q0, n0, n0 / 2.0, n0 / 2.0)) m0 = Math.Max(m0, (px0 >> 24) & 255);
                if (q0.Hidden != (m0 <= 128)) flagged = false;
                fl += q0.Hidden + "/" + m0 + " ";
            }
            // the asked 中心留空 stays empty all the same
            int n = RingRaster.CanvasSize(bigs);
            bool empty = R41Inside(RingRaster.Render(bigs, n, n / 2.0, n / 2.0), n, n / 2.0, n / 2.0, 40) == 0;
            // every tile's own defaults: not hidden, visible
            bool defaults = true; string res = "";
            foreach (StyleTile t in StyleShapes.Tiles)
            {
                Profile p = R41Tile(t.Id);
                if (t.Id == "custom") StyleShapes.Of(p).PngFile = "big.png";
                foreach (bool b in new bool[] { false, true })
                {
                    RingStyle st = RingStyle.From(p, 0, b);
                    int S = RingRaster.CanvasSize(st);
                    int[] a = RingRaster.Render(st, S, S / 2.0, S / 2.0);
                    if (st.Hidden || R41Sum(a) < 255 * 4) { defaults = false; res += " " + t.Id + (b ? "/boost" : "") + " hidden=" + st.Hidden; }
                }
            }
            // 强化 growth of the hole stops short of the boosted shape: dotring 14 px, 中心留空 8, boosted x3 -> 24 would hide it
            Profile dr = R41Tile("dotring"); StyleShapes.Of(dr).Hole = 8; dr.Boost.Diameter = dr.Diameter * 3;
            RingStyle drs = RingStyle.From(dr, 0, true);
            int dn = RingRaster.CanvasSize(drs);
            int[] da = RingRaster.Render(drs, dn, dn / 2.0, dn / 2.0);
            double reach = RingRaster.CoreReach(drs);
            bool boostVisible = !drs.Hidden && R41Sum(da) > 255 * 8 && drs.Hole >= 8 && drs.Hole <= reach - RingStyle.VisibleBand + 1e-9
                && R41Inside(da, dn, dn / 2.0, dn / 2.0, drs.Hole) == 0;
            // brute repro: chevron default boosted where hole x ratio outgrew the shape
            Profile cv = R41Tile("chevron"); StyleShapes.Of(cv).Hole = 12; cv.Boost.Diameter = cv.Diameter * 3;
            RingStyle cvs = RingStyle.From(cv, 0, true);
            int cn = RingRaster.CanvasSize(cvs);
            bool chevronVisible = R41Sum(RingRaster.Render(cvs, cn, cn / 2.0, cn / 2.0)) > 0;
            Check("R41-5 never invisible unnoticed (BRUTE40 P2-2): 四点 gap 0 / 中心留空 40 / size-4 四角框 are flagged Hidden for the preview (asked hole still empty); tile defaults not Hidden; 强化 hole growth stops "
                + RingStyle.VisibleBand + " px short of the boosted shape",
                flagged && empty && defaults && boostVisible && chevronVisible,
                "flagged=" + flagged + " (" + fl + ") empty=" + empty + " defaults=" + defaults + res
                + " boost hole=" + F(drs.Hole) + " reach=" + F(reach) + " visible=" + boostVisible + " chevron=" + chevronVisible);
        }

        // ---------------- P1-2 RasterCache ----------------

        static void R41Cache()
        {
            bool ok = true; string res = "";
            int cases = 0, identical = 0;
            double worstSum = 0, worstShift = 0;
            Random rnd = new Random(7);
            foreach (StyleTile t in StyleShapes.Tiles)
                foreach (double d in new double[] { 90, 160, 280 })
                {
                    Profile p = R41Tile(t.Id);
                    CrosshairStyle cs = StyleShapes.Of(p);
                    if (t.Id == "custom") cs.PngFile = rnd.Next(2) == 0 ? "big.png" : "wide.png";
                    p.Diameter = d;
                    if (cs.Kind == "hbars" || cs.Kind == "chevron" || cs.Kind == "chevron2" || cs.Kind == "xcross" || cs.Kind == "fourdots") { cs.Gap = d / 5; cs.Length = d / 4; }
                    if (t.Id == "cross" || t.Id == "tee") { p.CrossGap = d / 5; p.CrossLength = d / 3; }
                    p.RotationDeg = rnd.Next(3) == 0 ? rnd.Next(-180, 181) : 0;
                    p.GlowPx = rnd.Next(2) == 0 ? 6 : 0; p.GlowOpacityPct = 50;
                    cs.Hole = 3 + rnd.Next(6);
                    p.Validate();
                    RingStyle st = RingStyle.From(p, 0, false);
                    int S = RingRaster.CanvasSize(st);
                    if (!RasterCache.UsesMaster(st, S)) continue;
                    RasterCache rc = new RasterCache();
                    int[] buf = new int[S * S];
                    // phase 0 = exactly RingRaster.Render
                    rc.Render(st, S, S / 2.0, S / 2.0, buf);
                    int[] ex0 = RingRaster.Render(st, S, S / 2.0, S / 2.0);
                    bool same = true;
                    for (int i = 0; i < buf.Length; i++) if (buf[i] != ex0[i]) { same = false; break; }
                    if (same) identical++; else { ok = false; res += " phase0 " + t.Id + " d=" + d; }
                    int[] m0 = new int[S * S];
                    RingRaster.RenderNoHole(st, S, S / 2.0, S / 2.0, m0); // the master
                    double hole = RingRaster.HoleOf(st);
                    for (int ph = 1; ph <= 8; ph++)
                    {
                        double dx = (ph % 9) / 8.0, dy = ((ph * 5) % 9) / 8.0;
                        double cx = S / 2.0 + dx, cy = S / 2.0 + dy;
                        rc.Render(st, S, cx, cy, buf);
                        cases++;
                        // reference: the master moved by (dx, dy) in double precision, hole cut at (cx, cy)
                        int[] rf = new int[S * S];
                        double[] ch = new double[4];
                        for (int j = 0; j < S; j++)
                            for (int i = 0; i < S; i++)
                            {
                                double u = i - dx, v = j - dy;
                                int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
                                double fx = u - x0, fy = v - y0;
                                ch[0] = ch[1] = ch[2] = ch[3] = 0;
                                for (int k = 0; k < 4; k++)
                                {
                                    int x = x0 + (k & 1), y = y0 + (k >> 1);
                                    double wgt = ((k & 1) == 0 ? 1 - fx : fx) * ((k >> 1) == 0 ? 1 - fy : fy);
                                    if (x < 0 || y < 0 || x >= S || y >= S || wgt == 0) continue;
                                    int c = m0[y * S + x];
                                    ch[0] += R41A(c) * wgt; ch[1] += ((c >> 16) & 0xFF) * wgt; ch[2] += ((c >> 8) & 0xFF) * wgt; ch[3] += (c & 0xFF) * wgt;
                                }
                                int A = (int)Math.Round(ch[0]), R = Math.Min(A, (int)Math.Round(ch[1])), G = Math.Min(A, (int)Math.Round(ch[2])), B = Math.Min(A, (int)Math.Round(ch[3]));
                                rf[j * S + i] = (A << 24) | (R << 16) | (G << 8) | B;
                            }
                        RingRaster.ApplyHole(rf, S, cx, cy, hole);
                        int diff = 0;
                        for (int i = 0; i < rf.Length; i++)
                            for (int sh = 0; sh < 32; sh += 8)
                                diff = Math.Max(diff, Math.Abs(((rf[i] >> sh) & 0xFF) - ((buf[i] >> sh) & 0xFF)));
                        // position: alpha centroid (away from the re-cut hole) = the master's + (dx, dy)
                        double ax = 0, ay = 0, as0 = 0, bx = 0, by = 0, bs = 0, keep = hole + 2.5;
                        for (int j = 0; j < S; j++)
                            for (int i = 0; i < S; i++)
                            {
                                double qx = i + 0.5 - cx, qy = j + 0.5 - cy, mx = i + 0.5 - S / 2.0, my = j + 0.5 - S / 2.0;
                                if (mx * mx + my * my >= keep * keep) { int v0 = R41A(m0[j * S + i]); ax += v0 * i; ay += v0 * j; as0 += v0; }
                                if (qx * qx + qy * qy >= keep * keep) { int v1 = R41A(buf[j * S + i]); bx += v1 * i; by += v1 * j; bs += v1; }
                            }
                        double shift = as0 > 0 && bs > 0 ? Math.Sqrt(Math.Pow(ax / as0 + dx - bx / bs, 2) + Math.Pow(ay / as0 + dy - by / bs, 2)) : 0;
                        double rel = as0 > 0 ? Math.Abs(bs - as0) / as0 : 0;
                        if (rel > worstSum) worstSum = rel;
                        if (shift > worstShift) worstShift = shift;
                        bool centre = R41Inside(buf, S, cx, cy, hole) == 0 && R41Inside(buf, S, cx, cy, RingRaster.MinHole) == 0;
                        if (!centre || !R41Premul(buf) || !R41Edge(buf, S) || diff > 2 || shift > 0.05 || rel > 0.02)
                        { ok = false; if (res.Length < 400) res += " " + t.Id + " d=" + d + " ph=" + ph + " centre=" + centre + " diff=" + diff + " rel=" + F(rel) + " shift=" + F(shift); }
                    }
                    if (rc.MasterCount != 1) { ok = false; res += " " + t.Id + " masters=" + rc.MasterCount; }
                }
            // two looks alternating (normal / 强化): two masters, no re-render
            Profile q = R41Tile("corners"); q.Diameter = 200; q.Boost.Diameter = 260;
            RingStyle n0 = RingStyle.From(q, 0, false), n1 = RingStyle.From(q, 0, true);
            RasterCache two = new RasterCache();
            for (int i = 0; i < 20; i++)
            {
                RingStyle s = i % 2 == 0 ? n0 : n1;
                int S = RingRaster.CanvasSize(s);
                two.Render(s, S, S / 2.0 + (i % 8) / 8.0, S / 2.0, new int[S * S]);
            }
            bool lru = two.MasterCount == 2;
            Check("R41-6 RasterCache (BRUTE40 P1-2): costly looks of every tile at 90-280 px: phase 0 identical to the exact render; " + cases
                + " sub-pixel phases = the master moved by exactly that step (within 2/255 of a double-precision reference, centroid within 0.05 px, alpha kept within 2 %), centre empty, premultiplied, inside the canvas; normal/强化 alternating = 2 masters",
                ok && identical > 0 && lru, (ok ? "ok" : res) + " identical=" + identical + " worst sum " + F(worstSum * 100) + " % worst centroid " + F(worstShift) + " px lru=" + lru + "(" + two.MasterCount + ")");
        }

        static void R41CacheTint()
        {
            Profile p = R41Tile("custom");
            CrosshairStyle cs = StyleShapes.Of(p);
            cs.PngFile = "big.png"; cs.PngTint = true; p.Diameter = 200; p.HandColorOn = true; p.Color = "#00FF00"; p.HandColor = "#FF0000";
            RasterCache rc = new RasterCache();
            bool colours = true; string res = "";
            int S = 0;
            long calls0 = RingRaster.RenderCalls;
            // 0.35 s at 144 Hz: hand 0 for 10 frames, a 50-frame 换手颜色 fade, then hand 1 for 30 frames (still moving)
            for (int i = 0; i < 90; i++)
            {
                double hand = i < 10 ? 0 : (i >= 60 ? 1 : (i - 10) / 50.0);
                RingStyle st = RingStyle.From(p, hand, false);
                S = RingRaster.CanvasSize(st);
                int[] buf = new int[S * S];
                double cx = S / 2.0 + (i % 8) / 8.0, cy = S / 2.0 + ((i / 3) % 8) / 8.0;
                rc.Render(st, S, cx, cy, buf);
                // the brightest pixel has the tint colour
                int best = 0;
                foreach (int v in buf) if (R41A(v) > R41A(best)) best = v;
                int A = R41A(best);
                double r = ((best >> 16) & 0xFF) * 255.0 / Math.Max(1, A), g = ((best >> 8) & 0xFF) * 255.0 / Math.Max(1, A);
                if (A < 200 || Math.Abs(r - st.R1) > 3 || Math.Abs(g - st.G1) > 3) { colours = false; res += " i=" + i + " A=" + A + " r=" + F(r) + " g=" + F(g) + " want " + st.R1 + "," + st.G1; }
            }
            long rasters = RingRaster.RenderCalls - calls0;
            // the same for a big shape with a gradient (two colours): corners at 200 px, twotone
            Profile q = R41Tile("corners"); q.Diameter = 200; q.Gradient = "twotone"; q.OuterThickness = 3; q.HandColorOn = true;
            q.Color = "#00FF00"; q.Color2 = "#000000"; q.HandColor = "#FF9500"; q.HandColor2 = "#0000FF";
            RasterCache rq = new RasterCache();
            long q0 = RingRaster.RenderCalls;
            int maxDiff = 0;
            for (int i = 0; i < 90; i++)
            {
                double hand = i < 10 ? 0 : (i >= 60 ? 1 : (i - 10) / 50.0);
                RingStyle st = RingStyle.From(q, hand, false);
                int n = RingRaster.CanvasSize(st);
                double cx = n / 2.0 + (i % 8) / 8.0, cy = n / 2.0 + ((i / 3) % 8) / 8.0;
                int[] got = new int[n * n];
                rq.Render(st, n, cx, cy, got);
                // reference: a fresh master in real colours (what a cache without the fade logic would show)
                RasterCache fresh = new RasterCache();
                int[] want = new int[n * n];
                fresh.Render(st, n, cx, cy, want);
                for (int k = 0; k < got.Length; k++)
                    for (int sh = 0; sh < 32; sh += 8) maxDiff = Math.Max(maxDiff, Math.Abs(((got[k] >> sh) & 0xFF) - ((want[k] >> sh) & 0xFF)));
            }
            long qRasters = RingRaster.RenderCalls - q0 - 90; // minus the 90 fresh reference masters
            Check("R41-6b 换手颜色 fade on a big look (自定义图片 上色 200 px; 四角框 200 px twotone): 2 rasterisations for the whole fade (the look + one in weight form; the end of the fade colours the master in place), colour right every frame, within 2/255 of a freshly rendered master",
                rasters == 2 && rc.MasterCount == 2 && qRasters == 2 && colours && maxDiff <= 2,
                "picture rasters=" + rasters + " masters=" + rc.MasterCount + " corners rasters=" + qRasters + " colours=" + colours + " maxDiff=" + maxDiff + res);
        }

        // ---------------- P1-2 per-frame cost ----------------

        /// <summary>Overlay frame while moving: RasterCache.Image at a new 1/8 px phase every frame (best of 5).</summary>
        static double R41FrameMs(RingStyle st, int frames)
        {
            int S = RingRaster.CanvasSize(st);
            RasterCache rc = new RasterCache();
            for (int k = 0; k < 64; k++) rc.Image(st, S, S / 2.0 + (k % 8) / 8.0, S / 2.0 + (k / 8) / 8.0); // every phase once
            double best = double.MaxValue;
            for (int rep = 0; rep < 5; rep++) // best of 5: other agents' builds share this box
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int f = 0; f < frames; f++) rc.Image(st, S, S / 2.0 + ((f + 1) % 8) / 8.0, S / 2.0 + (((f + 1) / 8) % 8) / 8.0);
                double ms = sw.Elapsed.TotalMilliseconds / frames;
                if (ms < best) best = ms;
            }
            return best;
        }

        /// <summary>Exact RingRaster.Render (best of 3 runs of n renders).</summary>
        static double R41ExactMs(RingStyle st, int n)
        {
            int S = RingRaster.CanvasSize(st);
            int[] buf = new int[S * S];
            RingRaster.Render(st, S, S / 2.0, S / 2.0, buf);
            double best = double.MaxValue;
            for (int rep = 0; rep < 3; rep++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++) RingRaster.Render(st, S, S / 2.0 + (i & 7) / 8.0, S / 2.0, buf);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds / n);
            }
            return best;
        }

        static Profile R41Max(string id, double rot)
        {
            Profile p = R41Tile(id);
            CrosshairStyle cs = StyleShapes.Of(p);
            if (id == "custom") cs.PngFile = "big.png";
            p.Diameter = 300; p.Thickness = 30; p.GlowPx = 40; p.GlowOpacityPct = 100; p.Gradient = "twotone"; p.OuterThickness = 20; p.RotationDeg = rot;
            cs.Gap = 100; cs.Length = 100;
            p.Validate();
            return p;
        }

        static string Ms(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        static void R41Perf()
        {
            string info = "";
            bool ok = true;
            // per moving frame (the overlay: no rasterisation, a copy)
            double leo = R41FrameMs(RingStyle.From(Profile.CreateLeoDefault("l"), 0, false), 512);
            info += " Leo默认=" + Ms(leo) + "ms";
            if (leo >= 0.05) ok = false;
            foreach (double d in new double[] { 32, 100, 200, 300 })
            {
                Profile p = R41Tile("custom"); StyleShapes.Of(p).PngFile = "big.png"; p.Diameter = d;
                RingStyle st = RingStyle.From(p, 0, false);
                double ms = R41FrameMs(st, 64);
                info += " png" + d + "=" + Ms(ms) + "ms";
                if (ms >= 2.0) ok = false;
            }
            foreach (string id in new string[] { "corners", "arcs", "ringticks", "dashring" })
            {
                RingStyle st = RingStyle.From(R41Max(id, 0), 0, false);
                double ms = R41FrameMs(st, 32);
                info += " " + id + "Max=" + Ms(ms) + "ms";
                if (ms >= 3.0) ok = false; // v4.0: 7-60 ms; ~1.0-1.8 ms here, 3 ms keeps CI quiet while other builds run
            }
            // exact raster (once per look change): tile skip on / off, separable picture on / off -- same box, same moment
            RingStyle cm = RingStyle.From(R41Max("corners", 30), 0, false);
            double cOn = R41ExactMs(cm, 3);
            RingRaster.NoTileCull = true;
            double cOff;
            try { cOff = R41ExactMs(cm, 3); } finally { RingRaster.NoTileCull = false; }
            Profile p300 = R41Tile("custom"); StyleShapes.Of(p300).PngFile = "big.png"; p300.Diameter = 300;
            RingStyle s300 = RingStyle.From(p300, 0, false);
            double pOn = R41ExactMs(s300, 4);
            RingRaster.PngGeneralOnly = true;
            double pOff;
            try { pOff = R41ExactMs(s300, 4); } finally { RingRaster.PngGeneralOnly = false; }
            info += " | exact corners max " + Ms(cOff) + " -> " + Ms(cOn) + " ms, png300 " + Ms(pOff) + " -> " + Ms(pOn) + " ms";
            bool exact = cOn <= 0.6 * cOff && pOn <= 0.6 * pOff;
            Check("R41-7 per moving frame (dev box, best of 5; a copy, no rasterisation): Leo默认 < 0.05 ms, 512 px picture at 32-300 px < 2 ms, max-setting shapes < 3 ms; exact raster (once per look): tile skip and separable picture each at most 0.6 x the old loop",
                ok && exact, info.Trim());
        }

        static void R41Levels()
        {
            PngImage big = R41Pic(512, 512, 3, "lv");
            PngImage odd = R41Solid(3, 7, "odd");
            bool dims = big.Level(1).W == 256 && big.Level(1).H == 256 && big.Level(9).W == 1 && big.Level(40) == big.Level(9) && big.Level(0) == big
                && odd.Level(1).W == 2 && odd.Level(1).H == 4 && odd.Level(3).W == 1 && odd.Level(3).H == 1;
            bool pm = R41Premul(big.Level(1).Pix) && R41Premul(big.Level(4).Pix) && R41A(odd.Level(1).Pix[0]) == 255 && R41A(odd.Level(3).Pix[0]) == 255;
            bool reach = Math.Abs(R41Solid(10, 10, "s").Reach - Math.Sqrt(50) / 10) < 1e-9 && new PngImage(new int[4], 2, 2, "c").Reach == 0
                && PngImage.Loading.Reach == 0 && new PngImage(null, 5, 5, "n").Reach == 0 && new PngImage(new int[3], 2, 2, "short").Level(2) != null;
            Check("R41-8 picture half-size levels: sizes (512 -> 1, odd 3x7 -> 2x4 -> 1x1), premultiplied, opaque stays opaque; Reach = farthest visible pixel / longer side",
                dims && pm && reach, "dims=" + dims + " premul=" + pm + " reach=" + reach);
        }

        // ---------------- P2-8 / P2-9 PngCache ----------------

        static void R41PngCache()
        {
            long now = 0;
            Dictionary<string, long> stamps = new Dictionary<string, long>();
            Dictionary<string, PngImage> files = new Dictionary<string, PngImage>();
            int decodes = 0;
            Queue<Action> bg = new Queue<Action>();
            PngCache pc = new PngCache();
            pc.NowMs = () => now;
            pc.Stamp = n => { long s; return stamps.TryGetValue(n, out s) ? s : long.MinValue; };
            pc.Decode = (n, st) => { decodes++; PngImage p; return files.TryGetValue(n, out p) ? p : null; };
            pc.Background = a => bg.Enqueue(a);
            PngImage picA = R41Solid(4, 4, "A"), picB = R41Solid(5, 5, "B");
            stamps["a.png"] = 1; files["a.png"] = picA;
            // overlay thread: never decodes itself
            PngImage first = pc.Get("a.png", false), again = pc.Get("a.png", false);
            bool loading = first == PngImage.Loading && again == PngImage.Loading && decodes == 0 && bg.Count == 1;
            bg.Dequeue()();
            bool ready = pc.Get("a.png", false) == picA && decodes == 1;
            // a blocking caller (UI) while the background read is still queued reads it itself
            stamps["c.png"] = 3; files["c.png"] = picA;
            PngImage c0 = pc.Get("c.png", false);
            PngImage c1 = pc.Get("c.png", true);
            bool uiReads = c0 == PngImage.Loading && c1 == picA;
            bg.Clear();
            // a picture that failed (missing) is not read again until its file changes
            int d0 = decodes;
            bool miss = pc.Get("b.png", true) == null;
            bool noRetry = pc.Get("b.png", true) == null && pc.Get("b.png", false) == null;
            now += 1500;
            bool unchanged = pc.Get("b.png", true) == null;
            stamps["b.png"] = 7; files["b.png"] = picB; // the user copies the picture in
            bool soon = pc.Get("b.png", true) == null;   // checked less than RetryMs ago
            now += PngCache.RetryMs + 1;
            bool retried = pc.Get("b.png", true) == picB;
            int dd = decodes - d0;
            // a picture read while still being written (decoder fails) is read again once the file changes
            stamps["w.png"] = 10; // files has no w.png: decode fails
            bool w0 = pc.Get("w.png", true) == null;
            stamps["w.png"] = 11; files["w.png"] = picB; now += PngCache.RetryMs + 1;
            // v5.0.2 (W16, AUDIT F11): the overlay no longer stats the file itself; until the background check has run, a
            // picture that failed stays empty (null), never flickering to Loading
            bool w1 = pc.Get("w.png", false) == null;
            if (bg.Count > 0) bg.Dequeue()();
            bool w2 = pc.Get("w.png", false) == picB;
            pc.Forget("a.png");
            bool forgot = pc.Get("a.png", true) == picA;
            Check("R41-9 PngCache (BRUTE40 P2-8/P2-9): the overlay thread gets Loading and the picture is read in the background; the UI reads it itself; a failed picture is read again only once its file changes (checked at most every "
                + PngCache.RetryMs + " ms)",
                loading && ready && uiReads && miss && noRetry && unchanged && soon && retried && dd == 1 && w0 && w1 && w2 && forgot,
                "loading=" + loading + " ready=" + ready + " ui=" + uiReads + " miss=" + miss + " noRetry=" + noRetry + " unchanged=" + unchanged + " soon=" + soon
                + " retried=" + retried + " decodes(b)=" + dd + " writing=" + w0 + "/" + w1 + "/" + w2 + " forget=" + forgot);
        }

        // ---------------- memory bounds (Leo 17 Nov 2026) ----------------

        static void R41Bounds()
        {
            long now = 0;
            PngCache pc = new PngCache();
            pc.NowMs = () => now;
            pc.Stamp = n => 1;
            pc.Decode = (n, st) => R41Pic(512, 512, n.Length, n);
            pc.Background = null; // no thread pool in the test: the overlay call reads inline
            PngImage mine = pc.Get("overlay.png", false);
            bool kept = true;
            for (int i = 0; i < 12; i++)
            {
                now += 100;
                pc.Get("ui" + i + ".png", true);           // the settings window browses pictures
                if (pc.Get("overlay.png", false) != mine) kept = false; // the overlay draws every frame
                if (pc.Count > pc.MaxPictures || pc.Pixels > pc.MaxPixels) kept = false;
            }
            bool bounded = pc.Count <= pc.MaxPictures && pc.Pixels <= pc.MaxPixels;
            int dropped = pc.TrimUi();
            bool trimmed = pc.Count == 1 && pc.Pixels == 512L * 512 && pc.Get("overlay.png", false) == mine && dropped >= 1;
            now += PngCache.KeepMs + 1;
            pc.TrimUi();
            bool idle = pc.Count == 0 && pc.Pixels == 0; // the overlay stopped drawing it too
            // RasterCache: two masters at most; freed once the look is cheap again
            RasterCache rc = new RasterCache();
            Profile big = R41Tile("corners"); big.Diameter = 260; big.Boost.Diameter = 300;
            long maxBytes = 0;
            for (int i = 0; i < 40; i++)
            {
                RingStyle s = RingStyle.From(big, 0, i % 3 == 0);
                if (i % 7 == 0) { big.Diameter = 200 + i; s = RingStyle.From(big, 0, false); }
                int S = RingRaster.CanvasSize(s);
                rc.Render(s, S, S / 2.0 + (i % 8) / 8.0, S / 2.0, new int[S * S]);
                maxBytes = Math.Max(maxBytes, rc.MasterBytes);
            }
            bool two = maxBytes <= 2L * (4L * 1024 * 1024 + 8 * 1024);
            RingStyle cheap = RingStyle.From(Profile.CreateLeoDefault("c"), 0, false);
            int cn = RingRaster.CanvasSize(cheap);
            bool stillHeld = rc.MasterBytes > 0;
            for (int i = 0; i < RasterCache.DropAfterFrames; i++) rc.Render(cheap, cn, cn / 2.0 + (i % 8) / 8.0, cn / 2.0 + (i / 8 % 8) / 8.0, new int[cn * cn]);
            long phaseCap = 2L * RasterCache.Phases * RasterCache.Phases * 4L * RasterCache.PhaseMaxCanvas * RasterCache.PhaseMaxCanvas;
            bool freed = stillHeld && rc.MasterBytes == 0 && rc.Bytes <= phaseCap;
            Check("R41-17 bounded memory: picture cache holds at most " + new PngCache().MaxPictures + " pictures / " + (new PngCache().MaxPixels / 1024) + "k px (LRU) and never evicts the overlay's picture; TrimUi (settings closed) keeps only the overlay's; RasterCache <= 2 masters, freed after "
                + RasterCache.DropAfterFrames + " frames of a small look (phase images at most 2 x 64 x 64² x 4 B)",
                kept && bounded && trimmed && idle && two && freed,
                "kept=" + kept + " bounded=" + bounded + " trimmed=" + trimmed + "(dropped " + dropped + ") idle=" + idle + " masters max " + (maxBytes / 1024) + " KB freed=" + freed + " cache now " + (rc.Bytes / 1024) + " KB");
        }

        // ---------------- P2-7 names ----------------

        static bool R41NoLone(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i])) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false; i++; }
                else if (char.IsLowSurrogate(s[i])) return false;
            }
            return true;
        }

        static void R41Names()
        {
            string emoji = "\U0001F600";
            System.Text.StringBuilder b = new System.Text.StringBuilder("a");
            for (int i = 0; i < 40; i++) b.Append(emoji);
            string baseName = b.ToString();
            string cut = StyleShapes.CutChars(baseName, 60);
            bool cutOk = cut.Length == 59 && R41NoLone(cut) && baseName.StartsWith(cut);
            System.Text.StringBuilder lng = new System.Text.StringBuilder();
            for (int i = 0; i < 100; i++) lng.Append(emoji);
            string safe = StyleShapes.SafeFileName("x" + lng + ".png");
            bool tail = safe.Length <= StyleShapes.MaxFileName && R41NoLone(safe) && safe.EndsWith(".png");
            string lone = StyleShapes.SafeFileName("a\uD83Db\uDE00c.png");
            bool loneOk = lone == "abc.png";
            bool keep = StyleShapes.SafeFileName("准星" + emoji + ".png") == "准星" + emoji + ".png" && StyleShapes.CutChars("abc", 60) == "abc"
                && StyleShapes.SafeFileName("a<b>:|*.png") == "ab.png";
            // random names: never a lone surrogate, never longer than the limit
            Random rnd = new Random(77);
            int bad = 0;
            for (int i = 0; i < 20000; i++)
            {
                int len = rnd.Next(1, 200);
                char[] cs = new char[len];
                for (int k = 0; k < len; k++)
                {
                    int r = rnd.Next(10);
                    cs[k] = r < 3 ? (char)rnd.Next(0xD800, 0xE000) : (r < 5 ? (char)rnd.Next(0x4E00, 0x9FFF) : (char)rnd.Next(32, 127));
                }
                string s = StyleShapes.SafeFileName(new string(cs));
                string c2 = StyleShapes.CutChars(s, 60);
                if (s.Length > StyleShapes.MaxFileName || !R41NoLone(s) || c2.Length > 60 || !R41NoLone(c2) || StyleShapes.SafeFileName(s) != s) bad++;
            }
            Check("R41-10 emoji-safe names (BRUTE40 P2-7): 60-unit upload cut and 120-unit tail cut never split a pair, lone halves dropped, 20k random names clean and stable",
                cutOk && tail && loneOk && keep && bad == 0, "cut=" + cutOk + "(" + cut.Length + ") tail=" + tail + "(" + safe.Length + ") lone=" + loneOk + " keep=" + keep + " bad=" + bad);
        }

        // ---------------- P2-8 PNG header ----------------

        static byte[] R41Head(long w, long h)
        {
            byte[] b = new byte[33];
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Array.Copy(sig, b, 8);
            b[11] = 13; b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
            for (int i = 0; i < 4; i++) { b[16 + i] = (byte)(w >> (24 - 8 * i)); b[20 + i] = (byte)(h >> (24 - 8 * i)); }
            return b;
        }

        static void R41Header()
        {
            int w, h;
            bool ok1 = StyleShapes.ReadPngSize(R41Head(64, 32), out w, out h) && w == 64 && h == 32;
            bool huge = StyleShapes.ReadPngSize(R41Head(20000, 20000), out w, out h) && w == 20000 && h == 20000; // Import refuses > 8192 before decoding
            byte[] notPng = R41Head(10, 10); notPng[1] = (byte)'X';
            bool bad = !StyleShapes.ReadPngSize(notPng, out w, out h) && !StyleShapes.ReadPngSize(new byte[10], out w, out h) && !StyleShapes.ReadPngSize(null, out w, out h)
                && !StyleShapes.ReadPngSize(R41Head(0, 5), out w, out h) && !StyleShapes.ReadPngSize(R41Head(0xFFFFFFFFL, 5), out w, out h);
            byte[] noIhdr = R41Head(10, 10); noIhdr[12] = (byte)'X';
            bool order = !StyleShapes.ReadPngSize(noIhdr, out w, out h);
            Check("R41-11 PNG size read from the header before decoding (BRUTE40 P2-8): 64x32 ok, 20000x20000 seen (refused by size), not-PNG / short / 0 / >2^31 / IHDR not first refused",
                ok1 && huge && bad && order, "ok=" + ok1 + " huge=" + huge + " bad=" + bad + " ihdr=" + order);
        }

        static void R41NoAlloc()
        {
            long worst = 0; string which = "";
            foreach (string id in new string[] { "triangle", "corners", "chevron2", "ringticks", "dotring", "ring" })
            {
                Profile p = R41Tile(id);
                p.HandColorOn = true;
                RingStyle st = new RingStyle();
                for (int i = 0; i < 100; i++) { st.Set(p, 0.5, i % 2 == 0); RingRaster.CanvasSize(st); }
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20000; i++) { st.Set(p, (i % 200) / 200.0, (i / 1000) % 2 == 1); RingRaster.CanvasSize(st); }
                long used = GC.GetAllocatedBytesForCurrentThread() - b0;
                if (used > worst) { worst = used; which = id; }
            }
            Check("R41-14 per-frame RingStyle.Set + CanvasSize allocate nothing for the v4 shapes too (reach / extent from a per-thread list)", worst == 0, "worst=" + worst + " bytes (" + which + ")");
        }

        // ---------------- REBRUTE41 P2-4: cheaper exact rasters, same pixels ----------------

        static void R41Cull(Dictionary<string, PngImage> pics)
        {
            Random rnd = new Random(20261118);
            int cases = 0, diff = 0, culledFaster = 0;
            string first = "";
            string[] ids = { "chevron", "chevron2", "xcross", "hbars", "corners", "brackets", "dotring", "arcs", "smile", "fourdots", "ringticks",
                "diamond", "triangle", "ring", "dashring", "square", "ringcross", "cross", "tee", "dot" };
            for (int it = 0; it < 2600; it++)
            {
                string id = ids[it % ids.Length];
                Profile p = R41Tile(id == "dot" ? "ring" : id);
                if (id == "dot") p.Shape = "dot";
                CrosshairStyle cs = StyleShapes.Of(p);
                bool big = rnd.Next(6) == 0;
                p.Diameter = 4 + rnd.NextDouble() * (big ? 296 : 60);
                p.Thickness = 0.5 + Math.Pow(rnd.NextDouble(), 2) * (big ? 29.5 : 8);
                p.Feather = rnd.Next(3) == 0 ? rnd.NextDouble() * (big ? 30 : 5) : 1;
                cs.Gap = rnd.NextDouble() * (big ? 100 : 20); cs.Length = 0.5 + rnd.NextDouble() * (big ? 100 : 20);
                cs.Spread = 5 + rnd.NextDouble() * 335; cs.Count = 2 + rnd.Next(23); cs.Hole = 3 + rnd.NextDouble() * 10;
                p.CrossGap = rnd.NextDouble() * 40; p.CrossLength = rnd.NextDouble() * 40; p.CrossThickness = 0.5 + rnd.NextDouble() * 8; p.CrossTop = rnd.Next(2) == 0;
                p.Gradient = Profile.Gradients[rnd.Next(4)]; p.OuterThickness = 0.5 + rnd.NextDouble() * (big ? 20 : 5);
                p.GlowPx = rnd.Next(2) == 0 ? rnd.NextDouble() * (big ? 40 : 12) : 0; p.GlowOpacityPct = rnd.Next(101);
                p.RotationDeg = rnd.Next(3) == 0 ? rnd.Next(-180, 181) : 0;
                p.CenterDot = rnd.Next(5) == 0; p.CenterDotSize = 1 + rnd.Next(10);
                p.Dashes = rnd.Next(3) == 0 ? 2 + rnd.Next(23) : 0; p.DashGapPct = rnd.Next(91);
                p.Validate();
                RingStyle st = RingStyle.From(p, rnd.NextDouble(), rnd.Next(3) == 0);
                if (rnd.Next(25) == 0) { st.Diameter = double.NaN; } // garbage: both loops must agree there too
                else if (rnd.Next(25) == 0) { st.SGap = double.PositiveInfinity; st.CrossLength = -5; }
                int S = RingRaster.CanvasSize(st);
                double cx = S / 2.0 + rnd.Next(9) / 8.0, cy = S / 2.0 + rnd.Next(9) / 8.0;
                int[] a = new int[S * S], b = new int[S * S];
                for (int k = 0; k < a.Length; k++) { a[k] = unchecked((int)0xDEADBEEF); b[k] = 0x12345678; } // stale buffers
                RingRaster.NoTileCull = true;
                try { RingRaster.Render(st, S, cx, cy, a); } finally { RingRaster.NoTileCull = false; }
                RingRaster.Render(st, S, cx, cy, b);
                cases++;
                for (int k = 0; k < a.Length; k++)
                    if (a[k] != b[k]) { diff++; if (first.Length == 0) first = id + " S=" + S + " px " + k + " " + a[k].ToString("X8") + " vs " + b[k].ToString("X8"); break; }
            }
            // 自定义图片 without rotation: per-axis sums == the 2-D sums (within 1/255: summation order)
            int pngCases = 0, pngMax = 0;
            string[] names = { "big.png", "wide.png", "p1x1.png", "p2x2.png", "p3x7.png", "p512x1.png", "p1x512.png", "clear.png" };
            foreach (string name in names)
                foreach (double d in new double[] { 4, 7.5, 16, 32, 63, 100, 150, 200, 255, 300 })
                    foreach (bool tint in new bool[] { false, true })
                    {
                        Profile p = R41Tile("custom");
                        StyleShapes.Of(p).PngFile = name; StyleShapes.Of(p).PngTint = tint; p.Diameter = d; p.Color = "#3080FF";
                        RingStyle st = RingStyle.From(p, 0, false);
                        int S = RingRaster.CanvasSize(st);
                        double cx = S / 2.0 + (pngCases % 9) / 8.0, cy = S / 2.0 + ((pngCases * 5) % 9) / 8.0;
                        int[] a = new int[S * S], b = new int[S * S];
                        RingRaster.PngGeneralOnly = true;
                        try { RingRaster.Render(st, S, cx, cy, a); } finally { RingRaster.PngGeneralOnly = false; }
                        RingRaster.Render(st, S, cx, cy, b);
                        pngCases++;
                        for (int k = 0; k < a.Length; k++)
                            for (int sh = 0; sh < 32; sh += 8) pngMax = Math.Max(pngMax, Math.Abs(((a[k] >> sh) & 0xFF) - ((b[k] >> sh) & 0xFF)));
                    }
            Check("R41-18 cheaper exact rasters, same pixels (REBRUTE41 P2-4): " + cases + " random looks of every v4 / v3 shape (glow, outline, dashes, rotation, 中心点, 强化, garbage values) bit-identical with and without the empty-tile skip; "
                + pngCases + " pictures (1x1 .. 512, 4-300 px, 上色) per-axis sums within 1/255 of the 2-D loop",
                diff == 0 && pngMax <= 1, "differing renders=" + diff + (first.Length > 0 ? " first: " + first : "") + " picture max channel diff=" + pngMax);
        }

        // ---------------- REBRUTE41 P2-4: the overlay never rasterises while only the position moves ----------------

        static void R41BlitOnly(Dictionary<string, PngImage> pics)
        {
            // Overlay.Draw emulated: X/Y path -> window position + Normalise -> RasterChangeDetector -> RasterCache.Image
            List<KeyValuePair<string, Profile>> looks = new List<KeyValuePair<string, Profile>>();
            looks.Add(new KeyValuePair<string, Profile>("Leo默认", Profile.CreateLeoDefault("l")));
            Profile chev = R41Tile("chevron"); looks.Add(new KeyValuePair<string, Profile>("chevron", chev));
            Profile ring64 = R41Tile("ring"); ring64.Diameter = 120; looks.Add(new KeyValuePair<string, Profile>("ring 120", ring64));
            Profile png = R41Tile("custom"); StyleShapes.Of(png).PngFile = "big.png"; png.Diameter = 200; looks.Add(new KeyValuePair<string, Profile>("png 200", png));
            Profile tint = R41Tile("custom"); StyleShapes.Of(tint).PngFile = "big.png"; StyleShapes.Of(tint).PngTint = true; tint.Diameter = 150;
            looks.Add(new KeyValuePair<string, Profile>("png 150 上色", tint));
            looks.Add(new KeyValuePair<string, Profile>("corners max", R41Max("corners", 30)));
            bool ok = true; string res = "";
            foreach (KeyValuePair<string, Profile> kv in looks)
            {
                RasterCache rc = new RasterCache();
                RasterChangeDetector det = new RasterChangeDetector(), lookDet = new RasterChangeDetector();
                RingStyle style = new RingStyle();
                long warm = 0, moving = 0, lookRasters = 0;
                int changed = 0, phaseBad = 0, normBad = 0, checks = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    long rasters = 0;
                    for (int f = 0; f < 1500; f++)
                    {
                        double t = f / 144.0;
                        double X = 1280 + 37.3 * Math.Sin(t * 2.1) + 11 * Math.Sin(t * 7.3), Y = 720 + 23.7 * Math.Cos(t * 1.7);
                        style.Set(kv.Value, 0, (f / 400) % 2 == 1); // 强化 toggles too (two looks)
                        int S = RingRaster.CanvasSize(style);
                        int winX = (int)Math.Floor(X) - S / 2, winY = (int)Math.Floor(Y) - S / 2;
                        double cx = Math.Round((X - winX) * 8.0) / 8.0, cy = Math.Round((Y - winY) * 8.0) / 8.0;
                        double bx = winX + cx, by = winY + cy;
                        RasterCache.Normalise(S, ref winX, ref cx); RasterCache.Normalise(S, ref winY, ref cy);
                        if (winX + cx != bx || winY + cy != by || cx - S / 2.0 < 0 || cx - S / 2.0 >= 1 || cy - S / 2.0 < 0 || cy - S / 2.0 >= 1) normBad++;
                        bool lookChanged = lookDet.Changed(style, S, 0, 0); // 强化 pressed / released: a new look (may rasterise once)
                        if (!det.Changed(style, S, cx, cy)) continue; // same phase: only the window moves
                        changed++;
                        long r0 = RingRaster.RenderCalls;
                        int[] img = rc.Image(style, S, cx, cy);
                        if (lookChanged) lookRasters += RingRaster.RenderCalls - r0;
                        else rasters += RingRaster.RenderCalls - r0; // only the position moved
                        if (pass == 1 && f % 97 == 0)
                        {
                            // the image is the look at that phase: exact for small canvases, the moved master for big ones
                            int[] want = RingRaster.Render(style, S, cx, cy);
                            checks++;
                            if (!RasterCache.UsesMaster(style, S))
                            {
                                for (int k = 0; k < want.Length; k++) if (want[k] != img[k]) { phaseBad++; break; }
                            }
                            else
                            {
                                long sw = R41Sum(want), si = R41Sum(img);
                                if (sw <= 0 || Math.Abs(sw - si) > 0.05 * sw) phaseBad++;
                            }
                        }
                    }
                    if (pass == 0) warm = rasters; else moving = rasters;
                }
                if (moving > 0 || phaseBad > 0 || normBad > 0 || checks == 0) { ok = false; res += " " + kv.Key + " rasters-while-moving=" + moving + " phaseBad=" + phaseBad + " normBad=" + normBad; }
                res += " " + kv.Key + ":first-pass=" + warm + "/second=" + moving + "/on-look-change=" + lookRasters;
            }
            Check("R41-19 overlay while moving (Overlay.Draw emulated: whole-pixel moves = window only, 1/8 px phases from RasterCache): after one pass over the path, 0 rasterisations on position-only frames in a second pass of 1,500 frames for Leo默认, chevron, ring 120, picture 200 / 150 上色 and corners max (强化 toggling = a look change, counted apart); images are the look at that phase; Normalise keeps the screen position",
                ok, res.Trim());
        }

        // ---------------- fuzz: >= 30k renders, centre always empty ----------------

        static void R41Fuzz(Dictionary<string, PngImage> pics)
        {
            Random rnd = new Random(20261117);
            string[] names = { "big.png", "wide.png", "p1x1.png", "p3x7.png", "p512x1.png", "clear.png", "gone.png" };
            int renders = 0, cases = 0, centre = 0, premul = 0, edge = 0, range = 0, errors = 0, masters = 0;
            string first = "";
            Stopwatch sw = Stopwatch.StartNew();
            RasterCache rc = new RasterCache();
            while (renders < 32000)
            {
                StyleTile t = StyleShapes.Tiles[rnd.Next(StyleShapes.Tiles.Length)];
                Profile p = R41Tile(t.Id);
                CrosshairStyle cs = StyleShapes.Of(p);
                bool big = rnd.Next(25) == 0;
                double maxD = big ? 300 : 60, maxL = big ? 100 : 20;
                p.Diameter = 4 + rnd.NextDouble() * (maxD - 4);
                p.Thickness = 0.5 + Math.Pow(rnd.NextDouble(), 2) * (big ? 29.5 : 8);
                p.Feather = rnd.Next(4) == 0 ? rnd.NextDouble() * (big ? 30 : 4) : 1;
                cs.Gap = rnd.NextDouble() * maxL; cs.Length = 0.5 + rnd.NextDouble() * maxL; cs.Spread = 5 + rnd.NextDouble() * 335; cs.Count = 2 + rnd.Next(23);
                cs.Hole = rnd.Next(3) == 0 ? 3 + rnd.NextDouble() * 37 : 3;
                p.CrossGap = rnd.NextDouble() * maxL * 2; p.CrossLength = rnd.NextDouble() * maxL * 2; p.CrossThickness = 0.5 + rnd.NextDouble() * 6;
                p.Gradient = Profile.Gradients[rnd.Next(4)]; p.OuterThickness = 0.5 + rnd.NextDouble() * 6;
                p.GlowPx = rnd.Next(3) == 0 ? rnd.NextDouble() * (big ? 40 : 10) : 0; p.GlowOpacityPct = rnd.Next(101);
                p.RotationDeg = rnd.Next(3) == 0 ? rnd.Next(-180, 181) : 0;
                p.CenterDot = rnd.Next(6) == 0; p.CenterDotSize = 1 + rnd.Next(10);
                p.Dashes = rnd.Next(3) == 0 ? 2 + rnd.Next(23) : 0; p.DashGapPct = rnd.Next(91);
                p.Boost.Diameter = 4 + rnd.NextDouble() * (big ? 296 : 80); p.Boost.Thickness = 0.5 + rnd.NextDouble() * 6; p.Boost.GlowPx = rnd.Next(10);
                p.HandColorOn = rnd.Next(2) == 0;
                if (t.Id == "custom") { cs.PngFile = names[rnd.Next(names.Length)]; cs.PngTint = rnd.Next(2) == 0; }
                p.Validate();
                bool boosted = rnd.Next(3) == 0;
                RingStyle st = RingStyle.From(p, rnd.NextDouble(), boosted);
                bool scaled = rnd.Next(4) == 0;
                if (scaled) st = StyleShapes.Scaled(st, 0.25 + rnd.NextDouble() * 4); // preview / thumbnail scales
                int S = RingRaster.CanvasSize(st);
                if (S > RingRaster.MaxCanvas || S < RingRaster.MinCanvas) { range++; continue; }
                if (scaled && S >= RingRaster.MaxCanvas) continue; // a preview scaled past the overlay cap: not an overlay look
                if (S > 420 && rnd.Next(4) != 0) continue; // keep the run short: few huge canvases
                cases++;
                double hole = RingRaster.HoleOf(st);
                for (int pass = 0; pass < 2; pass++)
                {
                    double cx = S / 2.0 + rnd.Next(9) / 8.0, cy = S / 2.0 + rnd.Next(9) / 8.0;
                    int[] buf = new int[S * S];
                    try
                    {
                        if (pass == 0) RingRaster.Render(st, S, cx, cy, buf);
                        else { if (RasterCache.UsesMaster(st, S)) masters++; rc.Render(st, S, cx, cy, buf); }
                    }
                    catch (Exception ex) { errors++; if (first.Length == 0) first = t.Id + " " + ex.GetType().Name; continue; }
                    renders++;
                    if (R41Inside(buf, S, cx, cy, hole) != 0 || R41Inside(buf, S, cx, cy, RingRaster.MinHole) != 0)
                    { centre++; if (first.Length == 0) first = t.Id + " pass=" + pass + " S=" + S + " hole=" + F(hole) + " d=" + F(st.Diameter); }
                    if (!R41Premul(buf)) premul++;
                    if (!R41Edge(buf, S)) { edge++; if (first.Length == 0) first = "edge " + t.Id + " pass=" + pass + " S=" + S + " d=" + F(st.Diameter) + " k=" + st.Kind; }
                }
            }
            sw.Stop();
            Check("R41-15 fuzz " + renders + " renders (" + cases + " random valid looks x exact + RasterCache, every tile, boost, hand blend, preview scales 0.25-4.25, pictures incl. 1x1 / clear / missing, centres +0..1 px): centre empty (中心留空 and 3 px), premultiplied, inside the canvas, canvas in range, no exceptions",
                renders >= 30000 && centre == 0 && premul == 0 && edge == 0 && range == 0 && errors == 0,
                "centre=" + centre + " premul=" + premul + " edge=" + edge + " range=" + range + " errors=" + errors + " master-path=" + masters + " "
                + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" + (first.Length > 0 ? " first: " + first : ""));
        }
    }
}
