using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TarkovCrosshair.Tests
{
    // v4.0 准星样式 (worker STYLE): every style keeps the centre empty, defaults pixel-identical, 自定义图片, tiles.
    public static partial class Program
    {
        static int A8(int px) { return (px >> 24) & 0xFF; }

        /// <summary>Largest alpha of any pixel whose centre is closer than r to (cx, cy).</summary>
        static int MaxAlphaInside(int[] a, int n, double cx, double cy, double r)
        {
            int m = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double dx = i + 0.5 - cx, dy = j + 0.5 - cy;
                    if (dx * dx + dy * dy < r * r) { int v = A8(a[j * n + i]); if (v > m) m = v; }
                }
            return m;
        }

        static bool Premul(int[] a)
        {
            foreach (int v in a) { int A = A8(v); if (((v >> 16) & 0xFF) > A || ((v >> 8) & 0xFF) > A || (v & 0xFF) > A) return false; }
            return true;
        }

        static bool Edge(int[] a, int n)
        {
            for (int i = 0; i < n; i++)
                if (A8(a[i]) != 0 || A8(a[(n - 1) * n + i]) != 0 || A8(a[i * n]) != 0 || A8(a[i * n + n - 1]) != 0) return false;
            return true;
        }

        static long ASum(int[] a) { long s = 0; foreach (int v in a) s += A8(v); return s; }

        /// <summary>A synthetic 自定义图片: opaque colour disc on a transparent square (premultiplied).</summary>
        static PngImage TestPicture(int w, int h, string id)
        {
            int[] pix = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double dx = x + 0.5 - w / 2.0, dy = y + 0.5 - h / 2.0;
                    bool on = dx * dx / (w * w / 4.0) + dy * dy / (h * h / 4.0) <= 1.0;
                    pix[y * w + x] = on ? unchecked((int)0xFFFF3020) : 0;
                }
            return new PngImage(pix, w, h, id);
        }

        static void V4StyleTests()
        {
            Func<string, PngImage> savedProvider = StyleShapes.PngProvider;
            Dictionary<string, PngImage> pics = new Dictionary<string, PngImage>();
            pics["wide.png"] = TestPicture(64, 32, "wide");
            pics["tall.png"] = TestPicture(20, 90, "tall");
            pics["big.png"] = TestPicture(400, 400, "big");
            StyleShapes.PngProvider = delegate(string f) { PngImage p; return pics.TryGetValue(f, out p) ? p : null; };
            try
            {
                V4S1Defaults();
                V4S2CentreAllStyles();
                V4S3Legacy();
                V4S4Png();
                V4S5Data();
                V4S6Tiles();
                V4S7Shapes();
                V4S8Pictures();
            }
            finally { StyleShapes.PngProvider = savedProvider; }
        }

        // ---------------- defaults unchanged ----------------

        static void V4S1Defaults()
        {
            List<Profile> looks = new List<Profile>();
            looks.Add(new Profile());
            looks.Add(Profile.CreateLeoDefaultLegacy("leo"));
            Random rnd = new Random(4);
            for (int i = 0; i < 60; i++)
            {
                Profile r = new Profile();
                r.Diameter = 4 + rnd.Next(120); r.Thickness = 0.5 + rnd.NextDouble() * 8; r.Feather = rnd.NextDouble() * 4;
                r.OuterThickness = 0.5 + rnd.NextDouble() * 4; r.Gradient = Profile.Gradients[rnd.Next(4)];
                r.Color = ColorUtil.Format(rnd.Next(256), rnd.Next(256), rnd.Next(256)); r.Color2 = ColorUtil.Format(rnd.Next(256), rnd.Next(256), rnd.Next(256));
                looks.Add(r);
            }
            int exact = 0, exactBad = 0, holeBad = 0, cases = 0;
            foreach (Profile p in looks)
            {
                int s0 = V245.RingRaster.CanvasSize(p), s1 = RingRaster.CanvasSize(p);
                if (s0 != s1) { exactBad++; continue; }
                foreach (double off in new double[] { 0, 0.125, 0.5, 0.875 })
                {
                    cases++;
                    double cx = s0 / 2.0 + off, cy = s0 / 2.0 + off * 0.5;
                    int[] a0 = V245.RingRaster.Render(p, s0, cx, cy);
                    int[] a1 = RingRaster.Render(RingStyle.From(p, 0, false), s1, cx, cy);
                    // Rings whose inner edge stays outside the hole: every pixel identical to v2.4.5.
                    bool clear = MaxAlphaInside(a0, s0, cx, cy, RingRaster.MinHole + 1.0) == 0;
                    if (clear)
                    {
                        exact++;
                        for (int i = 0; i < a0.Length; i++) if (a0[i] != a1[i]) { exactBad++; break; }
                    }
                    // Every ring: v2.4.5 pixels with the v4 centre hole cut.
                    RingRaster.ApplyHole(a0, s0, cx, cy, RingRaster.MinHole);
                    for (int i = 0; i < a0.Length; i++) if (a0[i] != a1[i]) { holeBad++; break; }
                }
            }
            bool defOk = MaxAlphaInside(RingRaster.Render(new Profile(), RingRaster.CanvasSize(new Profile()), 13, 13), RingRaster.CanvasSize(new Profile()), 13, 13, 4) == 0;
            Check("V4S-1 default + Leo默认 + 60 random rings: pixel-identical to v2.4.5 when the ring leaves the centre empty; otherwise identical outside the hole",
                exactBad == 0 && holeBad == 0 && exact >= 8 && defOk,
                cases + " renders, exact-compared=" + exact + " mismatches=" + exactBad + " hole-rule mismatches=" + holeBad);
        }

        // ---------------- centre alpha 0 for every style ----------------

        static void V4S2CentreAllStyles()
        {
            int cases = 0, bad = 0, invisible = 0, edges = 0, premul = 0;
            string first = "";
            Random rnd = new Random(2026);
            double[] sizes = { 4, 6, 8, 10, 12, 16, 20, 30, 45, 70, 120 };
            double[] thick = { 0.5, 1.2, 2.5, 5, 12 };
            double[] gaps = { 0, 1, 3, 6, 15 };
            foreach (StyleTile t in StyleShapes.Tiles)
            {
                for (int it = 0; it < 220; it++)
                {
                    Profile p = Profile.CreateLeoDefaultLegacy("t");
                    StyleShapes.SetStyle(p, new CrosshairStyle());
                    StyleShapes.ApplyTile(p, t.Id);
                    CrosshairStyle cs = StyleShapes.Of(p);
                    if (t.Id == "custom") cs.PngFile = it % 3 == 0 ? "wide.png" : (it % 3 == 1 ? "tall.png" : "big.png");
                    bool plain = it == 0; // the tile's own defaults: must be visible
                    if (!plain)
                    {
                        p.Diameter = sizes[rnd.Next(sizes.Length)];
                        p.Thickness = thick[rnd.Next(thick.Length)];
                        cs.Gap = gaps[rnd.Next(gaps.Length)]; cs.Length = 1 + rnd.Next(30); cs.Spread = 10 + rnd.Next(320); cs.Count = 2 + rnd.Next(23);
                        p.CrossGap = gaps[rnd.Next(gaps.Length)]; p.CrossLength = 1 + rnd.Next(20); p.CrossThickness = thick[rnd.Next(thick.Length)];
                        p.Gradient = Profile.Gradients[rnd.Next(4)]; p.OuterThickness = 0.5 + rnd.NextDouble() * 6;
                        p.Feather = rnd.NextDouble() * 5; p.GlowPx = rnd.Next(3) == 0 ? rnd.NextDouble() * 12 : 0; p.GlowOpacityPct = rnd.Next(101);
                        p.RotationDeg = rnd.Next(4) == 0 ? rnd.Next(-180, 181) : 0;
                        p.CenterDot = rnd.Next(4) == 0; p.CenterDotSize = 1 + rnd.Next(10);
                        cs.Hole = rnd.Next(3) == 0 ? 3 + rnd.NextDouble() * 10 : (rnd.Next(5) == 0 ? 0 : 3); // 0 = a bad file: still >= 3
                        cs.PngTint = rnd.Next(2) == 0;
                        p.Boost.Diameter = 4 + rnd.Next(100); p.Boost.Thickness = thick[rnd.Next(thick.Length)]; p.Boost.GlowPx = rnd.Next(8);
                    }
                    bool boosted = !plain && rnd.Next(3) == 0;
                    double hand = plain ? 0 : rnd.NextDouble();
                    RingStyle st = RingStyle.From(p, hand, boosted);
                    int n = RingRaster.CanvasSize(st);
                    double off = plain ? 0 : rnd.Next(8) / 8.0;
                    double cx = n / 2.0 + off, cy = n / 2.0 + (plain ? 0 : rnd.Next(8) / 8.0);
                    int[] a = RingRaster.Render(st, n, cx, cy);
                    cases++;
                    double hole = Math.Max(RingRaster.MinHole, st.Hole);
                    int inside = MaxAlphaInside(a, n, cx, cy, hole);
                    int inside3 = MaxAlphaInside(a, n, cx, cy, RingRaster.MinHole);
                    if (inside != 0 || inside3 != 0 || st.Hole < RingRaster.MinHole)
                    {
                        bad++;
                        if (first.Length == 0) first = t.Id + " d=" + p.Diameter + " t=" + p.Thickness + " gap=" + cs.Gap + " hole=" + st.Hole + " a=" + inside;
                    }
                    if (!Premul(a)) premul++;
                    // off = 0..0.875 shifts the centre right/down; the canvas has a 3 px margin, so the far edge must stay clear
                    if (!Edge(a, n)) edges++;
                    if (plain && ASum(a) < 255 * 4) invisible++;
                }
            }
            Check("V4S-2 中心留空: " + StyleShapes.Tiles.Length + " styles × 220 looks (sizes 4–120 px, thickness 0.5–12, gaps 0–15, glow, outline, rotation, boost, hand blend, sub-pixel centres, 自定义图片): every pixel within 中心留空 (≥ 3 px) has alpha 0",
                bad == 0 && premul == 0 && edges == 0 && invisible == 0,
                cases + " renders; centre-not-empty=" + bad + (first.Length > 0 ? " first: " + first : "") + " non-premultiplied=" + premul + " touching-edge=" + edges + " invisible-default=" + invisible);
        }

        static void V4S3Legacy()
        {
            // The v3 实心点 shape and 中心点 option: the centre is cut out as well (v4 rule).
            Profile dot = Profile.CreateLeoDefaultLegacy("d"); dot.Shape = "dot"; dot.Gradient = "none";
            Profile cdot = Profile.CreateLeoDefaultLegacy("c"); cdot.CenterDot = true; cdot.CenterDotSize = 10;
            Profile cross0 = Profile.CreateLeoDefaultLegacy("x"); cross0.Shape = "cross"; cross0.CrossGap = 0; cross0.CrossLength = 8;
            Profile thick = new Profile(); thick.Diameter = 4; thick.Thickness = 30;
            bool ok = true; string res = "";
            foreach (Profile p in new Profile[] { dot, cdot, cross0, thick })
                foreach (bool b in new bool[] { false, true })
                {
                    RingStyle st = RingStyle.From(p, 0, b);
                    int n = RingRaster.CanvasSize(st);
                    int[] a = RingRaster.Render(st, n, n / 2.0, n / 2.0);
                    int m = MaxAlphaInside(a, n, n / 2.0, n / 2.0, 3);
                    bool vis = ASum(a) > 0;
                    if (m != 0 || !vis) { ok = false; res += " " + p.Shape + (p.CenterDot ? "+dot" : "") + (b ? "/boost" : "") + " centre=" + m + " visible=" + vis; }
                }
            Check("V4S-3 v3 shapes that used to cover the centre (实心点, 中心点, 十字间隙 0, very thick ring) now keep a ≥ 3 px empty centre, rest still drawn",
                ok, ok ? "8 renders ok" : res);
        }

        // ---------------- 自定义图片 ----------------

        static void V4S4Png()
        {
            Profile p = Profile.CreateLeoDefaultLegacy("png");
            StyleShapes.SetStyle(p, new CrosshairStyle());
            StyleShapes.ApplyTile(p, "custom");
            CrosshairStyle cs = StyleShapes.Of(p);
            cs.PngFile = "wide.png";
            bool ok = true; string res = "";
            foreach (double d in new double[] { 8, 12, 20, 32, 64, 150, 300 })
                foreach (double rot in new double[] { 0, 45, 90 })
                    foreach (double hole in new double[] { 0, 3, 8 })
                    {
                        p.Diameter = d; p.RotationDeg = rot; cs.Hole = hole;
                        RingStyle st = RingStyle.From(p, 0, false);
                        int n = RingRaster.CanvasSize(st);
                        int[] a = RingRaster.Render(st, n, n / 2.0, n / 2.0);
                        int m = MaxAlphaInside(a, n, n / 2.0, n / 2.0, Math.Max(3, hole));
                        bool vis = ASum(a) > 0, edge = Edge(a, n), pm = Premul(a), kind = st.Kind == "custom" && st.Png != null;
                        if (!vis && Math.Max(3, hole) >= d / 4) vis = true; // the whole 64x32 picture fits inside the hole: nothing left to show
                        if (m != 0 || !vis || !edge || !pm || !kind) { ok = false; res += " d=" + d + " rot=" + rot + " hole=" + hole + " centre=" + m + " vis=" + vis + " edge=" + edge; }
                    }
            // aspect: the longer side = Diameter (64x32 wide picture at 40 px -> about 40 x 20 opaque)
            p.Diameter = 40; p.RotationDeg = 0; cs.Hole = 3;
            RingStyle w = RingRaster_Style(p);
            int wn = RingRaster.CanvasSize(w);
            int[] wa = RingRaster.Render(w, wn, wn / 2.0, wn / 2.0);
            int minX = wn, maxX = -1, minY = wn, maxY = -1;
            for (int j = 0; j < wn; j++) for (int i = 0; i < wn; i++) if (A8(wa[j * wn + i]) > 127) { minX = Math.Min(minX, i); maxX = Math.Max(maxX, i); minY = Math.Min(minY, j); maxY = Math.Max(maxY, j); }
            bool aspect = Math.Abs((maxX - minX + 1) - 40) <= 2 && Math.Abs((maxY - minY + 1) - 20) <= 2;
            // tint: colour follows 颜色 (and 换手颜色)
            int centreRow = wn / 2, probe = centreRow * wn + wn / 2 + 12;
            int c0 = wa[probe];
            cs.PngTint = true; p.Color = "#00FF00";
            int[] ta = RingRaster.Render(RingRaster_Style(p), wn, wn / 2.0, wn / 2.0);
            int c1 = ta[probe];
            bool tint = ((c0 >> 16) & 0xFF) > 200 && ((c1 >> 8) & 0xFF) > 200 && ((c1 >> 16) & 0xFF) == 0;
            // missing picture: falls back to the v3 shape (a crosshair is still shown), flagged for the UI
            cs.PngFile = "gone.png";
            RingStyle miss = RingRaster_Style(p);
            int mn = RingRaster.CanvasSize(miss);
            int[] ma = RingRaster.Render(miss, mn, mn / 2.0, mn / 2.0);
            bool missing = miss.PngMissing && miss.Kind == "" && ASum(ma) > 0 && MaxAlphaInside(ma, mn, mn / 2.0, mn / 2.0, 3) == 0;
            // key changes with the picture (raster cache)
            cs.PngFile = "wide.png"; string k1 = RingRaster_Style(p).Key();
            cs.PngFile = "tall.png"; string k2 = RingRaster_Style(p).Key();
            Check("V4S-4 自定义图片: 7 sizes × 3 rotations × 3 holes keep the centre empty and inside the canvas; longer side = 大小; 上色 uses 颜色; missing picture -> v3 shape + warning flag; key follows the picture",
                ok && aspect && tint && missing && k1 != k2,
                (ok ? "63 renders ok" : res) + " aspect=" + aspect + " (" + (maxX - minX + 1) + "x" + (maxY - minY + 1) + ") tint=" + tint + " missing=" + missing + " key=" + (k1 != k2));
        }

        static RingStyle RingRaster_Style(Profile p) { return RingStyle.From(p, 0, false); }

        // ---------------- data ----------------

        static void V4S5Data()
        {
            CrosshairStyle s = new CrosshairStyle();
            s.Kind = "chevron"; s.Gap = 6.5; s.Length = 9; s.Spread = 100; s.Count = 5; s.Hole = 4.5; s.PngFile = "a.png"; s.PngTint = true;
            string json;
            using (MemoryStream ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CrosshairStyle)).WriteObject(ms, s);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }
            CrosshairStyle r;
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                r = (CrosshairStyle)new DataContractJsonSerializer(typeof(CrosshairStyle)).ReadObject(ms);
            bool round = r.Key() == s.Key();
            CrosshairStyle empty;
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes("{}")))
                empty = (CrosshairStyle)new DataContractJsonSerializer(typeof(CrosshairStyle)).ReadObject(ms);
            bool defaults = empty.Kind == "" && empty.Hole == 3 && empty.Count == 3;
            CrosshairStyle bad = new CrosshairStyle();
            bad.Kind = "nope"; bad.Gap = double.NaN; bad.Length = -5; bad.Spread = 9999; bad.Count = 99; bad.Hole = 0.5; bad.PngFile = "..\\..\\Windows\\evil?.png";
            bad.Validate();
            bool valid = bad.Kind == "" && bad.Gap == 4 && bad.Length == 0.5 && bad.Spread == 340 && bad.Count == 24 && bad.Hole == 3 && bad.PngFile == "evil.png";
            bool safe = StyleShapes.SafeFileName("C:/x/y/准星 1.png") == "准星 1.png" && StyleShapes.SafeFileName("..") == "" && StyleShapes.SafeFileName(null) == ""
                && StyleShapes.SafeFileName("a<b>:|*.png") == "ab.png";
            // a profile copy never shares the style data
            Profile p = new Profile();
            StyleShapes.SetStyle(p, new CrosshairStyle());
            StyleShapes.ApplyTile(p, "chevron");
            Profile c = StyleShapes.CloneWithStyle(p);
            StyleShapes.Of(c).Gap = 30;
            bool own = StyleShapes.Of(p).Gap == 4 && StyleShapes.Of(c).Gap == 30;
            Console.WriteLine("INFO V4S-5 Profile.Style saved field present: " + StyleShapes.StyleIsSaved + (StyleShapes.StyleIsSaved ? "" : " (waiting for CORE, v4-requests/STYLE.md)"));
            Check("V4S-5 CrosshairStyle: JSON round trip, {} -> defaults, Validate clamps (unknown kind, NaN, 中心留空 < 3, path in file name), copies do not share",
                round && defaults && valid && safe && own, "round=" + round + " defaults=" + defaults + " valid=" + valid + " safe=" + safe + " own=" + own);
        }

        static void V4S6Tiles()
        {
            bool ok = true; string res = "";
            HashSet<string> ids = new HashSet<string>();
            foreach (StyleTile t in StyleShapes.Tiles)
            {
                if (!ids.Add(t.Id)) { ok = false; res += " dup " + t.Id; }
                Profile p = Profile.CreateLeoDefaultLegacy("t");
                StyleShapes.SetStyle(p, new CrosshairStyle());
                p.Color = "#123456"; p.OpacityPct = 77; p.Gradient = "twotone"; p.Color2 = "#ABCDEF";
                double ratio = p.Boost.Diameter / p.Diameter;
                bool applied = StyleShapes.ApplyTile(p, t.Id);
                StyleTile back = StyleShapes.TileOf(p);
                bool kept = p.Color == "#123456" && p.OpacityPct == 77 && p.Gradient == "twotone" && p.Color2 == "#ABCDEF";
                bool boost = Math.Abs(p.Boost.Diameter / p.Diameter - ratio) < 0.06;
                if (!applied || back == null || back.Id != t.Id || !kept || !boost) { ok = false; res += " " + t.Id + " back=" + (back == null ? "null" : back.Id) + " kept=" + kept + " boost=" + boost; }
                if (t.Label.Length == 0 || t.Hint.Length == 0 || t.Params.Length == 0) { ok = false; res += " " + t.Id + " no label/hint/params"; }
            }
            Profile q = new Profile(); q.Shape = "dot";
            bool none = StyleShapes.TileOf(q) == null && !StyleShapes.ApplyTile(q, "nope");
            bool kinds = true;
            foreach (string kd in StyleShapes.Kinds) if (StyleShapes.FindTile(kd) == null) kinds = false;
            Check("V4S-6 style tiles: " + StyleShapes.Tiles.Length + " tiles, each switches the profile and is found again, colours / opacity / outline / 强化 ratio kept; every Kind has a tile; v3 实心点 has none",
                ok && none && kinds, (ok ? "ok" : res) + " none=" + none + " kinds=" + kinds);
        }

        static void V4S7Shapes()
        {
            // 宽距三角: the apex is BELOW the centre (sunk), nothing above the centre; legs spread wider with a bigger angle
            Profile p = Profile.CreateLeoDefaultLegacy("c");
            StyleShapes.SetStyle(p, new CrosshairStyle());
            StyleShapes.ApplyTile(p, "chevron");
            p.Gradient = "none";
            RingStyle st = RingStyle.From(p, 0, false);
            int n = RingRaster.CanvasSize(st);
            int[] a = RingRaster.Render(st, n, n / 2.0, n / 2.0);
            int c = n / 2;
            bool above = true;
            for (int j = 0; j < c; j++) for (int i = 0; i < n; i++) if (A8(a[j * n + i]) != 0) above = false;
            double apexY = StyleShapes.Of(p).Gap + p.Thickness / 2.0; // apex centre below the screen centre
            bool apex = A8(a[(c + (int)Math.Round(apexY)) * n + c]) > 128;
            bool noBottom = A8(a[(c + (int)Math.Round(apexY + 5)) * n + c]) == 0; // no shaft / bottom line under the apex
            // 四角框 has no middle of the sides; T 形 has no top arm; 下半弧 nothing above the centre
            Profile cor = Profile.CreateLeoDefaultLegacy("k"); StyleShapes.SetStyle(cor, new CrosshairStyle()); StyleShapes.ApplyTile(cor, "corners"); cor.Gradient = "none";
            RingStyle cs = RingStyle.From(cor, 0, false); int cn = RingRaster.CanvasSize(cs); int[] ca = RingRaster.Render(cs, cn, cn / 2.0, cn / 2.0);
            int cc = cn / 2, R = (int)Math.Round(cor.Diameter / 2.0);
            bool corners = A8(ca[(cc - R) * cn + cc]) == 0 && A8(ca[(cc - R) * cn + cc - R]) > 128;
            Profile sm = Profile.CreateLeoDefaultLegacy("s"); StyleShapes.SetStyle(sm, new CrosshairStyle()); StyleShapes.ApplyTile(sm, "smile"); sm.Gradient = "none";
            RingStyle ss = RingStyle.From(sm, 0, false); int sn = RingRaster.CanvasSize(ss); int[] sa = RingRaster.Render(ss, sn, sn / 2.0, sn / 2.0);
            bool smileTop = true; for (int j = 0; j < sn / 2 - 1; j++) for (int i = 0; i < sn; i++) if (A8(sa[j * sn + i]) != 0) smileTop = false;
            // style values change the raster key
            string k0 = RingStyle.From(p, 0, false).Key(); StyleShapes.Of(p).Gap += 1; string k1 = RingStyle.From(p, 0, false).Key();
            // boosted: gap / length scale with the 强化 ratio
            StyleShapes.Of(p).Gap = 4; p.Boost.Diameter = p.Diameter * 2;
            RingStyle bst = RingStyle.From(p, 0, true);
            bool boost = Math.Abs(bst.SGap - 8) < 1e-9 && Math.Abs(bst.SLength - StyleShapes.Of(p).Length * 2) < 1e-9;
            Check("V4S-7 shapes: 宽距三角 sunk below the centre (nothing above, apex drawn, no bottom line); 四角框 corners only; 下半弧 below only; key follows the values; 强化 scales gap/length",
                above && apex && noBottom && corners && smileTop && k0 != k1 && boost,
                "above-empty=" + above + " apex=" + apex + " no-bottom=" + noBottom + " corners=" + corners + " smile=" + smileTop + " key=" + (k0 != k1) + " boost=" + boost);
        }

        // ---------------- pictures ----------------

        static void V4S8Pictures()
        {
            int written = 0;
            try
            {
                Directory.CreateDirectory(outDir);
                int cell = 160, cols = 5, rows = (StyleShapes.Tiles.Length + cols - 1) / cols;
                byte[] sheet = new byte[cols * cell * rows * cell * 4];
                for (int t = 0; t < StyleShapes.Tiles.Length; t++)
                {
                    StyleTile tile = StyleShapes.Tiles[t];
                    Profile p = Profile.CreateLeoDefaultLegacy("pic");
                    StyleShapes.SetStyle(p, new CrosshairStyle());
                    StyleShapes.ApplyTile(p, tile.Id);
                    if (tile.Id == "custom") StyleShapes.Of(p).PngFile = "wide.png";
                    RingStyle st = RingStyle.From(p, 0, false);
                    int n = RingRaster.CanvasSize(st);
                    int[] a = RingRaster.Render(st, n, n / 2.0, n / 2.0);
                    int scale = Math.Max(1, Math.Min(8, cell / n));
                    int W = n * scale;
                    byte[] rgba = new byte[W * W * 4];
                    for (int y = 0; y < W; y++)
                        for (int x = 0; x < W; x++)
                        {
                            int sx = x / scale, sy = y / scale, v = a[sy * n + sx];
                            int A = A8(v), o = (y * W + x) * 4;
                            // dim wall + the game's white 2 px centre dot, to show it stays visible
                            int bg = 0x40;
                            double dx = sx + 0.5 - n / 2.0, dy = sy + 0.5 - n / 2.0;
                            int bgR = bg, bgG = bg + 6, bgB = bg;
                            if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) { bgR = bgG = bgB = 255; }
                            rgba[o] = (byte)(((v >> 16) & 0xFF) + bgR * (255 - A) / 255);
                            rgba[o + 1] = (byte)(((v >> 8) & 0xFF) + bgG * (255 - A) / 255);
                            rgba[o + 2] = (byte)((v & 0xFF) + bgB * (255 - A) / 255);
                            rgba[o + 3] = 255;
                        }
                    Png.Write(Path.Combine(outDir, "v4-style-" + tile.Id + ".png"), W, W, rgba);
                    written++;
                    int ox = (t % cols) * cell + (cell - Math.Min(W, cell)) / 2, oy = (t / cols) * cell + (cell - Math.Min(W, cell)) / 2;
                    int SW = cols * cell;
                    for (int y = 0; y < Math.Min(W, cell); y++)
                        for (int x = 0; x < Math.Min(W, cell); x++)
                        {
                            int si = (y * W + x) * 4, di = ((oy + y) * SW + ox + x) * 4;
                            sheet[di] = rgba[si]; sheet[di + 1] = rgba[si + 1]; sheet[di + 2] = rgba[si + 2]; sheet[di + 3] = 255;
                        }
                }
                Png.Write(Path.Combine(outDir, "v4-styles-all.png"), cols * cell, rows * cell, sheet);
                written++;
            }
            catch (Exception ex) { Console.WriteLine("v4 style pictures: " + ex.Message); }
            Check("V4S-8 thumbnails of every style written (v4-style-<id>.png + v4-styles-all.png, Leo默认 colours, white = the game's centre dot)",
                written == StyleShapes.Tiles.Length + 1, written + " files in " + outDir);
        }
    }

#if STYLE_ONLY
    public static class StyleOnlyMain
    {
        public static int Main(string[] args)
        {
            Program.RunStyleOnly(args);
            return 0;
        }
    }

    public static partial class Program
    {
        public static void RunStyleOnly(string[] args)
        {
            outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
            Console.OutputEncoding = Encoding.UTF8;
            V4StyleTests();
            Console.WriteLine("STYLE pass=" + pass + " fail=" + fail);
        }
    }
#endif
}
