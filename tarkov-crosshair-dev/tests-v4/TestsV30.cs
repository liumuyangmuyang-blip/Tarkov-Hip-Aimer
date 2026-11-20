using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TarkovCrosshair.Tests
{
    // v3.0 (SPEC-v3.0.md section 6): 「Leo默认」, removals, 换手颜色, 强化准星, shapes, game-window gate, autostart, migration.
    public static partial class Program
    {
        static void V30Tests()
        {
            G1Diagonals(); G2HandMirrorEngine(); G3HandColor(); G4Boost(); G5ShapesV245(); G6Shapes(); G7Gate(); G8Autostart();
            G9Migration245(); G10KeysPreview();
        }

        static InputState InB(string keys, bool boost) { InputState s = In(keys); s.Boost = boost; return s; }

        // ---------------- diagonals always automatic ----------------

        static void G1Diagonals()
        {
            Profile p = Profile.CreateLeoDefaultLegacy("x");
            double[,] want = { { 53.0, -23.3 }, { -21.2, -21.2 }, { 53.0, 7.8 }, { -21.2, 9.9 } }; // v3.1 built-in (Leo's 27 Oct file)
            bool shot = true;
            for (int i = 0; i < 4; i++) if (Math.Round(p.Dirs[4 + i].OffX, 1) != want[i, 0] || Math.Round(p.Dirs[4 + i].OffY, 1) != want[i, 1]) shot = false;
            p.Dirs[2].OffX = 10; p.Dirs[0].OffY = -4;
            Profile.RecomputeDiagonals(p);
            bool edit = p.Dirs[4].OffX == 0.7071 * (0 + 10) && p.Dirs[4].OffY == 0.7071 * (-4 + 0) && p.Dirs[6].OffX == 0.7071 * 10 && p.Dirs[5].OffY == 0.7071 * (-4 + 3);
            GlobalSettings g; string err;
            bool ok = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"Dirs\":[{\"OffX\":1,\"OffY\":2},{\"OffX\":3,\"OffY\":4},{\"OffX\":5,\"OffY\":6},{\"OffX\":7,\"OffY\":8},"
                + "{\"OffX\":99,\"OffY\":99},{\"OffX\":-99},{},{\"OffY\":400}]}]}", out g, out err);
            Profile a = ok ? g.Profiles[0] : null;
            bool load = ok && a.Dirs[4].OffX == 0.7071 * 6 && a.Dirs[4].OffY == 0.7071 * 8 && a.Dirs[5].OffX == 0.7071 * 8 && a.Dirs[6].OffY == 0.7071 * 10
                && a.Dirs[7].OffX == 0.7071 * 10 && a.Dirs[7].OffY == 0.7071 * 12;
            GlobalSettings back; bool stable = ok && SettingsIO.TryParse(SettingsIO.Serialize(g), out back, out err) && SettingsIO.Serialize(back) == SettingsIO.Serialize(g);
            Check("G1 diagonals = 0.7071 × (sum of cardinals): built-in WA 53.0/−23.3 WD −21.2/−21.2 SA 53.0/7.8 SD −21.2/9.9; recomputed after an edit and on load (file diagonals ignored); stable",
                shot && edit && load && stable, "screenshot=" + shot + " edit=" + edit + " load=" + load + " stable=" + stable + (ok ? "" : " " + err));
        }

        // ---------------- switched hand = mirror, in the engine ----------------

        static void G2HandMirrorEngine()
        {
            Profile p = Profile.CreateBlank("m");
            p.Dirs[0] = new DirSetting(3, -30); p.Dirs[1] = new DirSetting(-2, 8); p.Dirs[2] = new DirSetting(62, 1); p.Dirs[3] = new DirSetting(-33, 3);
            Profile.RecomputeDiagonals(p);
            p.LeanQX = 5; p.LeanQY = -20; p.LeanEX = 30; p.LeanEY = -15; p.StandOffsetX = 4; p.StandOffsetY = -2;
            p.RecoilMaxPx = 20; p.RecoilDriftX = 6; p.HandSwitchMs = 400;
            p.CurveAD.DurationMs = 0; p.CurveWS.DurationMs = 0; p.LeanMs = 0;
            string bad = "";
            foreach (string k in new string[] { "", "A", "D", "W", "S", "WA", "SD", "Q", "E", "QA", "ED" })
            {
                MotionEngine e = new MotionEngine(); GlobalSettings g = G();
                e.Update(0.01, In("K"), p, g, PreviewMode.Live, 1);
                for (int i = 0; i < 100; i++) e.Update(0.01, In(k), p, g, PreviewMode.Live, 1);
                InputState s = In(k);
                int dir = MotionEngine.DirIndex(s);
                int md = dir < 0 ? -1 : Profile.MirrorDir[dir];
                int lean = (s.LeanL && !s.LeanR) ? -1 : ((s.LeanR && !s.LeanL) ? 1 : 0);
                double lx = lean < 0 ? -p.LeanEX : (lean > 0 ? -p.LeanQX : 0), ly = lean < 0 ? p.LeanEY : (lean > 0 ? p.LeanQY : 0);
                double wx = (md >= 0 ? -p.Dirs[md].OffX : 0) + lx - p.StandOffsetX, wy = (md >= 0 ? p.Dirs[md].OffY : 0) + ly + p.StandOffsetY;
                if (Math.Abs(e.Debug.UiOffsetX - wx) > 1e-9 || Math.Abs(e.Debug.UiOffsetY - wy) > 1e-9 || !e.Debug.Switched)
                    bad += " " + k + "=(" + F(e.Debug.UiOffsetX) + "," + F(e.Debug.UiOffsetY) + ") want (" + F(wx) + "," + F(wy) + ")";
            }
            // recoil drift mirrored at the cap
            MotionEngine r = new MotionEngine(); GlobalSettings rg = G();
            r.Update(0.01, In("K"), p, rg, PreviewMode.Live, 1);
            for (int i = 0; i < 300; i++) r.Update(0.01, In("L"), p, rg, PreviewMode.Live, 1);
            double driftX = r.Debug.UiOffsetX + p.StandOffsetX; // remove the mirrored stand X
            bool drift = Math.Abs(driftX - (-p.RecoilDriftX * r.Recoil / p.RecoilMaxPx)) < 1e-9 && r.Recoil > 19;
            Check("G2 engine, 换手 settled: every key = mirrored target (A↔D, WA↔WD, Q↔E with X negated, 站着 X negated), lean + move additive; 水平偏移 negated",
                bad.Length == 0 && drift, (bad.Length == 0 ? "11 key sets exact" : bad) + " drift=" + drift + " (" + F(driftX) + ")");
        }

        // ---------------- 换手颜色 ----------------

        static void G3HandColor()
        {
            Profile p = Profile.CreateLeoDefaultLegacy("x"); p.HandSwitchMs = 400;
            MotionEngine e = new MotionEngine(); GlobalSettings g = G();
            RingStyle st = new RingStyle();
            for (int i = 0; i < 10; i++) e.Update(0.001, In(""), p, g, PreviewMode.Live, 1);
            st.Set(p, e.HandBlend, e.Boosted);
            bool start = st.R1 == 0x00 && st.G1 == 0xDF && st.B1 == 0x00 && st.R2 == 0 && st.G2 == 0 && st.B2 == 0;
            double worst = 0; bool monotone = true; int lastR = st.R1; double doneAt = -1;
            for (int t = 1; t <= 500; t++)
            {
                e.Update(0.001, In(t == 1 ? "K" : ""), p, g, PreviewMode.Live, 1);
                double u = Math.Min(1, t / 400.0), sm = u * u * (3 - 2 * u);
                worst = Math.Max(worst, Math.Abs(e.HandBlend - sm));
                st.Set(p, e.HandBlend, false);
                int wr = (int)Math.Round(0x00 + (0xFF - 0x00) * e.HandBlend, MidpointRounding.AwayFromZero);
                int wg = (int)Math.Round(0xDF + (0x95 - 0xDF) * e.HandBlend, MidpointRounding.AwayFromZero);
                if (st.R1 != wr || st.G1 != wg || st.B1 != 0) worst = Math.Max(worst, 1);
                if (st.R1 < lastR) monotone = false;
                lastR = st.R1;
                if (doneAt < 0 && st.R1 == 0xFF && st.G1 == 0x95) doneAt = t;
            }
            bool end = st.R1 == 0xFF && st.G1 == 0x95 && st.B1 == 0x00 && st.R2 == 0 && st.G2 == 0 && st.B2 == 0;
            Profile off = p.Clone(); off.HandColorOn = false;
            RingStyle so = RingStyle.From(off, 1.0, false);
            bool offOk = so.R1 == 0 && so.G1 == 0xDF && so.B1 == 0;
            Profile c2 = p.Clone(); c2.HandColor2 = "#204060"; RingStyle s2 = RingStyle.From(c2, 0.5, false);
            bool second = s2.R2 == 16 && s2.G2 == 32 && s2.B2 == 48;
            // the 换手来回切 preview flips the colour too
            MotionEngine f = new MotionEngine(); RingStyle sf = new RingStyle(); bool sawOrange = false, sawGreen = false;
            for (int i = 0; i < 2600; i++)
            {
                f.Update(0.002, In(""), p, g, PreviewMode.HandFlip, 1);
                sf.Set(p, f.HandBlend, f.Boosted);
                if (i == 1100 && sf.R1 == 0xFF && sf.G1 == 0x95) sawOrange = true;
                if (i == 1500 && sf.R1 == 0 && sf.G1 == 0xDF) sawGreen = true;
            }
            Check("G3 换手颜色: Color/Color2 → HandColor/HandColor2 by exactly the 换手 smoothstep factor (400 ms), monotone, both colours; off = unchanged; 换手来回切 preview flips it",
                start && end && worst < 1e-9 && monotone && doneAt <= 400 && offOk && second && sawOrange && sawGreen,
                "start=" + start + " end=" + end + " |blend − smoothstep| + colour mismatch=" + worst.ToString("E1") + " done at " + doneAt + " ms off=" + offOk + " colour2=" + second +
                " preview orange/green=" + sawOrange + "/" + sawGreen);
        }

        // ---------------- 强化准星 ----------------

        static bool[] BoostSteps(GlobalSettings g, Profile p, MotionEngine e, params string[] steps)
        {
            bool[] r = new bool[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                string s = steps[i];
                bool b = s.Contains("B");
                for (int k = 0; k < 3; k++) e.Update(0.005, InB(s.Replace("B", ""), b && k == 0 ? true : b), p, g, PreviewMode.Live, 1);
                r[i] = e.Boosted;
            }
            return r;
        }

        static string Bits(bool[] a) { string s = ""; foreach (bool b in a) s += b ? "1" : "0"; return s; }

        static void G4Boost()
        {
            Profile p = Profile.CreateLeoDefaultLegacy("x");
            GlobalSettings g = G();
            // press edges toggle, held = one edge; works in both hands
            bool[] t = BoostSteps(g, p, new MotionEngine(), "", "B", "B", "", "B", "", "K", "", "B", "", "KB", "", "B");
            bool toggles = Bits(t) == "0111000011001";
            // hand switched while boosted keeps the boost, and the boost toggles in the switched hand
            MotionEngine hb = new MotionEngine();
            bool[] h = BoostSteps(g, p, hb, "B", "", "K", "", "B", "");
            bool hands = Bits(h) == "111100" && hb.LiveSwitched;
            // ring on resets (F12 twice), a profile switch does not, ☑ 启用 off = never
            MotionEngine re = new MotionEngine(); GlobalSettings rg = G();
            BoostSteps(rg, p, re, "B", "");
            bool on1 = re.Boosted;
            rg.Enabled = false; BoostSteps(rg, p, re, "");
            bool whileOff = re.Boosted;
            rg.Enabled = true; BoostSteps(rg, p, re, "");
            bool reset = on1 && whileOff && !re.Boosted;
            MotionEngine ps = new MotionEngine();
            BoostSteps(g, p, ps, "B", "");
            Profile other = Profile.CreateBlank("other");
            BoostSteps(g, other, ps, "", "");
            bool keepsOnProfile = ps.Boosted;
            GlobalSettings dg = G(); dg.BoostEnabled = false;
            bool[] d = BoostSteps(dg, p, new MotionEngine(), "B", "", "B", "");
            bool disabled = Bits(d) == "0000";
            // ADS / free look still hide while boosted
            MotionEngine ah = new MotionEngine();
            BoostSteps(g, p, ah, "B", "");
            for (int i = 0; i < 5; i++) ah.Update(0.01, In("R"), p, g, PreviewMode.Live, 1);
            bool adsHides = ah.Boosted && ah.Alpha == 0;
            for (int i = 0; i < 5; i++) ah.Update(0.01, In("F"), p, g, PreviewMode.Live, 1);
            bool flHides = ah.Boosted && ah.Alpha == 0;
            // style switches instantly (the press frame), FollowHandColor, own colours
            MotionEngine si = new MotionEngine(); RingStyle st = new RingStyle();
            si.Update(0.005, In(""), p, g, PreviewMode.Live, 1);
            si.Update(0.005, InB("", true), p, g, PreviewMode.Live, 1);
            st.Set(p, si.HandBlend, si.Boosted);
            bool instant = si.Boosted && si.Debug.Boosted && st.Diameter == 15 && st.Thickness == 1 && st.OuterThickness == 2.5 && st.OpacityPct == 90
                && st.GlowPx == 5 && st.GlowOpacityPct == 45 && st.G1 == 0xDF && st.Shape == p.Shape && st.Feather == p.Feather && st.Gradient == p.Gradient;
            si.Update(0.005, In(""), p, g, PreviewMode.Live, 1);
            si.Update(0.005, InB("", true), p, g, PreviewMode.Live, 1);
            st.Set(p, si.HandBlend, si.Boosted);
            bool back = !si.Boosted && st.Diameter == 12 && st.OpacityPct == 65 && st.GlowPx == 0;
            RingStyle sw = RingStyle.From(p, 1.0, true);
            bool follow = sw.R1 == 0xFF && sw.G1 == 0x95 && sw.Diameter == 15;
            Profile own = p.Clone(); own.Boost.FollowHandColor = false; own.Boost.Color = "#112233"; own.Boost.Color2 = "#445566";
            RingStyle so = RingStyle.From(own, 1.0, true);
            bool ownCol = so.R1 == 0x11 && so.G1 == 0x22 && so.B1 == 0x33 && so.R2 == 0x44 && so.G2 == 0x55 && so.B2 == 0x66;
            // previews: 强化准星（站着） shows it, others not; the status flag
            MotionEngine pv = new MotionEngine();
            for (int i = 0; i < 50; i++) pv.Update(0.01, In(""), p, g, PreviewMode.BoostStand, 1);
            bool prevOk = pv.Boosted && pv.Debug.Boosted && Math.Abs(pv.Debug.UiOffsetX) < 1e-12 && Math.Abs(pv.Debug.UiOffsetY) < 1e-12;
            for (int i = 0; i < 5; i++) pv.Update(0.01, In(""), p, g, PreviewMode.DirA, 1);
            prevOk = prevOk && !pv.Boosted;
            Check("G4 强化准星: each press edge toggles (both hands), F12 off→on resets, profile switch keeps it, ☑ off = never; 开镜 / 自由视角 still hide; look switches on the press frame; FollowHandColor / own colours; preview 强化准星（站着）",
                toggles && hands && reset && keepsOnProfile && disabled && adsHides && flHides && instant && back && follow && ownCol && prevOk,
                "toggle=" + Bits(t) + " hands=" + Bits(h) + " reset=" + reset + " profile switch keeps=" + keepsOnProfile + " disabled=" + Bits(d) + " ads/fl hide=" + adsHides + "/" + flHides +
                " instant=" + instant + " back=" + back + " follow=" + follow + " own=" + ownCol + " preview=" + prevOk);
        }

        // ---------------- defaults = v2.4.5 pixels ----------------

        static void G5ShapesV245()
        {
            List<Profile> looks = new List<Profile>();
            looks.Add(Profile.CreateLeoDefaultLegacy("a"));
            looks.Add(new Profile());
            Profile b = new Profile(); b.Gradient = "radial"; b.Diameter = 30; b.Thickness = 4; b.Feather = 3; b.CenterDot = true; looks.Add(b);
            Profile c = new Profile(); c.Gradient = "sweep"; c.Color2 = "#FF0000"; c.Diameter = 9; c.Feather = 0; looks.Add(c);
            Profile d = new Profile(); d.Gradient = "twotone"; d.OuterThickness = 6; d.Thickness = 0.5; d.CenterDot = true; d.CenterDotSize = 5; looks.Add(d);
            Profile e2 = new Profile(); e2.Color = "bogus"; looks.Add(e2);
            Random rnd = new Random(99);
            for (int i = 0; i < 20; i++)
            {
                Profile r = new Profile();
                r.Diameter = 4 + rnd.Next(80); r.Thickness = 0.5 + rnd.NextDouble() * 6; r.Feather = rnd.NextDouble() * 4; r.OuterThickness = 0.5 + rnd.NextDouble() * 4;
                r.Gradient = Profile.Gradients[rnd.Next(4)]; r.CenterDot = rnd.Next(2) == 0; r.CenterDotSize = 1 + rnd.Next(9);
                r.Color = ColorUtil.Format(rnd.Next(256), rnd.Next(256), rnd.Next(256)); r.Color2 = ColorUtil.Format(rnd.Next(256), rnd.Next(256), rnd.Next(256));
                r.HandColorOn = rnd.Next(2) == 0; r.CrossLength = 3 + rnd.Next(10); r.DashGapPct = 10 + rnd.Next(60); // unused by a plain ring
                looks.Add(r);
            }
            int bad = 0, cases = 0;
            foreach (Profile p in looks)
            {
                int s0 = V245.RingRaster.CanvasSize(p), s1 = RingRaster.CanvasSize(p);
                if (s0 != s1) { bad++; continue; }
                foreach (double off in new double[] { 0, 0.125, 0.5, 0.875 })
                {
                    cases++;
                    int[] a0 = V245.RingRaster.Render(p, s0, s0 / 2.0 + off, s0 / 2.0 + off * 0.5);
                    RingRaster.ApplyHole(a0, s0, s0 / 2.0 + off, s0 / 2.0 + off * 0.5, RingRaster.MinHole); // v4.0: centre always empty (STYLE)
                    int[] a1 = RingRaster.Render(p, s1, s1 / 2.0 + off, s1 / 2.0 + off * 0.5);
                    int[] a2 = RingRaster.Render(RingStyle.From(p, 0, false), s1, s1 / 2.0 + off, s1 / 2.0 + off * 0.5);
                    for (int i = 0; i < a0.Length; i++) if (a0[i] != a1[i] || a0[i] != a2[i]) { bad++; break; }
                }
            }
            Check("G5 default ring look = v2.4.5 pixels exactly (canvas size + every pixel, " + looks.Count + " looks incl. Leo默认, 4 sub-pixel offsets)", bad == 0,
                cases + " renders compared, mismatches=" + bad);
        }

        // ---------------- shapes ----------------

        static int Alpha(int px) { return (px >> 24) & 0xFF; }

        static double AlphaSum(int[] a) { double s = 0; foreach (int v in a) s += Alpha(v); return s; }

        static bool BorderClear(int[] a, int n)
        {
            for (int i = 0; i < n; i++)
                if (Alpha(a[i]) != 0 || Alpha(a[(n - 1) * n + i]) != 0 || Alpha(a[i * n]) != 0 || Alpha(a[i * n + n - 1]) != 0) return false;
            return true;
        }

        static void SavePng(string file, int[] a, int n)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                int scale = 8, W = n * scale;
                byte[] rgba = new byte[W * W * 4];
                for (int y = 0; y < W; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int v = a[(y / scale) * n + x / scale];
                        int A = Alpha(v), R = (v >> 16) & 0xFF, Gc = (v >> 8) & 0xFF, B = v & 0xFF;
                        // over a mid-grey background
                        int bg = 0x60, o = (y * W + x) * 4;
                        rgba[o] = (byte)(R + bg * (255 - A) / 255); rgba[o + 1] = (byte)(Gc + bg * (255 - A) / 255); rgba[o + 2] = (byte)(B + bg * (255 - A) / 255); rgba[o + 3] = 255;
                    }
                Png.Write(Path.Combine(outDir, file), W, W, rgba);
            }
            catch (Exception) { }
        }

        static void G6Shapes()
        {
            string res = ""; bool all = true;
            foreach (string shape in Profile.Shapes)
                foreach (string grad in Profile.Gradients)
                    foreach (bool boosted in new bool[] { false, true })
                    {
                        Profile p = Profile.CreateLeoDefaultLegacy("s"); p.Shape = shape; p.Gradient = grad; p.CrossGap = 3; p.CrossLength = 7;
                        RingStyle st = RingStyle.From(p, 0, boosted);
                        int n = RingRaster.CanvasSize(st);
                        int[] a = RingRaster.Render(st, n, n / 2.0, n / 2.0), b = RingRaster.Render(st, n, n / 2.0, n / 2.0);
                        bool same = true; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) same = false;
                        bool premul = true;
                        foreach (int v in a) { int A = Alpha(v); if (((v >> 16) & 0xFF) > A || ((v >> 8) & 0xFF) > A || (v & 0xFF) > A) premul = false; }
                        bool ok = same && premul && AlphaSum(a) > 0 && BorderClear(a, n) && (n & 1) == 0;
                        if (!ok) { all = false; res += " " + shape + "/" + grad + (boosted ? "/boost" : "") + " same=" + same + " premul=" + premul + " border=" + BorderClear(a, n); }
                        if (grad == "twotone") SavePng("v30-shape-" + shape + (boosted ? "-boost" : "") + ".png", a, n);
                    }
            // dot covers the centre, ring does not; T 形 = no top arm; dashes leave gaps; glow adds a halo; rotation 90 of a T moves the gap
            Profile ring = Profile.CreateLeoDefaultLegacy("r");
            int rn = RingRaster.CanvasSize(ring); int[] ra = RingRaster.Render(ring, rn, rn / 2.0, rn / 2.0);
            Profile dot = ring.Clone(); dot.Shape = "dot";
            int dn = RingRaster.CanvasSize(dot); int[] da = RingRaster.Render(dot, dn, dn / 2.0, dn / 2.0);
            bool centre = Alpha(ra[(rn / 2) * rn + rn / 2]) == 0 && Alpha(da[(dn / 2) * dn + dn / 2]) == 0; // v4.0: the dot shape no longer covers the centre (centre always empty)
            Profile cross = ring.Clone(); cross.Shape = "cross"; cross.Gradient = "none"; cross.CrossGap = 4; cross.CrossLength = 8; cross.CrossThickness = 2;
            Profile tee = cross.Clone(); tee.CrossTop = false;
            int cn = RingRaster.CanvasSize(cross);
            int[] ca = RingRaster.Render(cross, cn, cn / 2.0, cn / 2.0), ta = RingRaster.Render(tee, cn, cn / 2.0, cn / 2.0);
            int top = (cn / 2 - 8) * cn + cn / 2, bottom = (cn / 2 + 7) * cn + cn / 2, left = (cn / 2) * cn + cn / 2 - 8;
            bool tShape = Alpha(ca[top]) == 255 && Alpha(ta[top]) == 0 && Alpha(ta[bottom]) == 255 && Alpha(ta[left]) == 255 && Alpha(ca[(cn / 2) * cn + cn / 2]) == 0;
            Profile rot = tee.Clone(); rot.RotationDeg = 90; // the missing arm moves from the top to the right (clockwise)
            int[] rota = RingRaster.Render(rot, cn, cn / 2.0, cn / 2.0);
            int right = (cn / 2) * cn + cn / 2 + 7;
            bool rotOk = Alpha(rota[top]) == 255 && Alpha(rota[right]) == 0 && Alpha(rota[left]) == 255;
            Profile big = ring.Clone(); big.Diameter = 40; big.Thickness = 3; big.Gradient = "none";
            Profile dash = big.Clone(); dash.Dashes = 8; dash.DashGapPct = 40;
            int bn = RingRaster.CanvasSize(big);
            double solid = AlphaSum(RingRaster.Render(big, bn, bn / 2.0, bn / 2.0)), dashed = AlphaSum(RingRaster.Render(dash, bn, bn / 2.0, bn / 2.0));
            int[] dsh = RingRaster.Render(dash, bn, bn / 2.0, bn / 2.0);
            int topRing = (bn / 2 - 20) * bn + bn / 2; // a dash is centred on the top
            bool dashOk = dashed > 0.45 * solid && dashed < 0.75 * solid && Alpha(dsh[topRing]) > 200;
            Profile glow = big.Clone(); glow.GlowPx = 6; glow.GlowOpacityPct = 50;
            int gn = RingRaster.CanvasSize(glow);
            int[] ga = RingRaster.Render(glow, gn, gn / 2.0, gn / 2.0);
            int outside = (gn / 2 - 20 - 4) * gn + gn / 2; // 2.5 px beyond the ring edge
            bool glowOk = gn == bn + 12 && Alpha(ga[outside]) > 0 && Alpha(ga[outside]) < 128 && BorderClear(ga, gn);
            Profile sq = big.Clone(); sq.Shape = "square"; sq.RotationDeg = 45;
            int sn = RingRaster.CanvasSize(sq); int[] sa = RingRaster.Render(sq, sn, sn / 2.0, sn / 2.0);
            bool sqOk = BorderClear(sa, sn) && Alpha(sa[(sn / 2 - 28) * sn + sn / 2]) > 200; // the diamond's tip at √2 · R
            SavePng("v30-tee.png", ta, cn); SavePng("v30-tee-rot90.png", rota, cn); SavePng("v30-dashes.png", dsh, bn); SavePng("v30-glow.png", ga, gn); SavePng("v30-square45.png", sa, sn);
            Check("G6 shapes ring / cross / ringcross / square / dot × 4 colour modes × normal / boosted: deterministic, premultiplied, inside the canvas; dot covers the centre; T 形; rotation clockwise; dashes; glow; rotated square fits",
                all && centre && tShape && rotOk && dashOk && glowOk && sqOk,
                (all ? "40 renders ok" : res) + " centre=" + centre + " T=" + tShape + " rot=" + rotOk + " dashes=" + dashOk + " (" + F(dashed / solid) + ") glow=" + glowOk + " square45=" + sqOk);
        }

        // ---------------- 只在塔科夫前台时显示 ----------------

        static void G7Gate()
        {
            // truth table
            string bad = "";
            for (int m = 0; m < 32; m++)
            {
                bool master = (m & 1) != 0, hidden = (m & 2) != 0, only = (m & 4) != 0, game = (m & 8) != 0, own = (m & 16) != 0;
                bool want = master && !hidden && (!only || game || own);
                if (FocusGate.Visible(master, hidden, only, game, own) != want) bad += " " + m;
                if (FocusGate.KeysLive(only, game) != (!only || game)) bad += " k" + m;
            }
            // engine: alpha per (only, game, own)
            string eb = "";
            foreach (bool only in new bool[] { false, true })
                foreach (bool game in new bool[] { false, true })
                    foreach (bool own in new bool[] { false, true })
                    {
                        Profile p = Profile.CreateLeoDefaultLegacy("x"); GlobalSettings g = G(); g.OnlyWhenGameFocused = only;
                        MotionEngine e = new MotionEngine(); e.GameFocused = game; e.OwnForeground = own;
                        for (int i = 0; i < 20; i++) e.Update(0.01, In(""), p, g, PreviewMode.Live, 1);
                        bool vis = e.Alpha == 1, want = !only || game || own;
                        if (vis != want || (!vis && e.Alpha != 0)) eb += " " + only + "/" + game + "/" + own;
                    }
            // no live keys while the game is not in front: no 换手 / 强化 / 跳跃 / movement / ADS; resumes when it comes back
            Profile q = Profile.CreateLeoDefaultLegacy("x"); GlobalSettings gq = G(); gq.OnlyWhenGameFocused = true;
            MotionEngine k = new MotionEngine(); k.GameFocused = false; k.OwnForeground = true;
            for (int i = 0; i < 30; i++) { InputState s = InB(i % 2 == 0 ? "AKLR" : "A", i % 2 == 0); s.Jump = i % 2 == 0; k.Update(0.01, s, q, gq, PreviewMode.Live, 1); }
            bool deaf = !k.LiveSwitched && !k.LiveBoost && !k.Jumping && k.MoveX == 0 && k.Recoil == 0 && k.Alpha == 1;
            k.GameFocused = true;
            InputState s2 = InB("K", true); s2.Jump = true;
            k.Update(0.01, s2, q, gq, PreviewMode.Live, 1);
            // v4.0: keys already down on the frame the game comes back are NOT a new press; release + press again works
            bool notOnRegain = !k.LiveSwitched && !k.LiveBoost && !k.Jumping;
            k.Update(0.01, In(""), q, gq, PreviewMode.Live, 1);
            k.Update(0.01, s2, q, gq, PreviewMode.Live, 1);
            bool live = notOnRegain && k.LiveSwitched && k.LiveBoost && k.Jumping;
            // the option off: keys read even when the game is not in front (v2.4.5 behaviour)
            GlobalSettings go = G(); go.OnlyWhenGameFocused = false;
            MotionEngine ko = new MotionEngine(); ko.GameFocused = false;
            ko.Update(0.01, InB("K", true), q, go, PreviewMode.Live, 1);
            bool offReads = ko.LiveSwitched && ko.LiveBoost && ko.Alpha > 0;
            // previews keep working with our own window in front
            MotionEngine pv = new MotionEngine(); pv.GameFocused = false; pv.OwnForeground = true;
            for (int i = 0; i < 100; i++) pv.Update(0.01, In(""), q, gq, PreviewMode.DirA, 1);
            bool preview = pv.Alpha == 1 && Math.Abs(pv.MoveX - q.Dirs[2].OffX) < 1e-9;
            bool titles = GameWindow.TitleMatches("EscapeFromTarkov", "EscapeFromTarkov") && GameWindow.TitleMatches("x - escapefromtarkov - y", "EscapeFromTarkov")
                && GameWindow.TitleMatches("EscapeFromTarkov", "  escape ") && !GameWindow.TitleMatches("Notepad", "EscapeFromTarkov") && !GameWindow.TitleMatches(null, "a")
                && !GameWindow.TitleMatches("abc", "") && !GameWindow.TitleMatches("abc", "   ") && !GameWindow.TitleMatches("abc", null);
            GlobalSettings tv; string err;
            bool clamp = SettingsIO.TryParse("{\"GameWindowTitle\":\"   \",\"OnlyWhenGameFocused\":false}", out tv, out err) && tv.GameWindowTitle == "EscapeFromTarkov" && !tv.OnlyWhenGameFocused;
            GlobalSettings tw; bool trim = SettingsIO.TryParse("{\"GameWindowTitle\":\"  Tarkov  \"}", out tw, out err) && tw.GameWindowTitle == "Tarkov" && tw.OnlyWhenGameFocused;
            Check("G7 只在塔科夫前台时显示: truth table (master / 开镜·自由视角 / option / game / own window, 32 rows); engine alpha; no live keys while the game is not in front; option off = v2.4.5; previews work; title match; settings",
                bad.Length == 0 && eb.Length == 0 && deaf && live && offReads && preview && titles && clamp && trim,
                "table=" + (bad.Length == 0 ? "ok" : bad) + " engine=" + (eb.Length == 0 ? "ok" : eb) + " deaf=" + deaf + " live again=" + live + " option off=" + offReads +
                " preview=" + preview + " titles=" + titles + " empty->default=" + clamp + " trim=" + trim);
        }

        // ---------------- 开机自动启动 ----------------

        static void G8Autostart()
        {
            string exe = "C:\\Games\\Hip Aimer & Co\\TarkovHipAimer.exe";
            string xml = Autostart.BuildTaskXml(exe, "PC\\Leo");
            bool parsed = false; string cmd = null, args = null, wd = null, runLevel = null, limit = null, multi = null, bat1 = null, bat2 = null, trigUser = null, principal = null;
            bool logon = false;
            try
            {
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml.Substring(xml.IndexOf("<Task", StringComparison.Ordinal)));
                System.Xml.XmlNamespaceManager ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                Func<string, string> X = delegate(string path) { System.Xml.XmlNode n = doc.SelectSingleNode(path, ns); return n == null ? null : n.InnerText; };
                cmd = X("/t:Task/t:Actions/t:Exec/t:Command"); args = X("/t:Task/t:Actions/t:Exec/t:Arguments"); wd = X("/t:Task/t:Actions/t:Exec/t:WorkingDirectory");
                runLevel = X("/t:Task/t:Principals/t:Principal/t:RunLevel"); limit = X("/t:Task/t:Settings/t:ExecutionTimeLimit");
                multi = X("/t:Task/t:Settings/t:MultipleInstancesPolicy"); bat1 = X("/t:Task/t:Settings/t:DisallowStartIfOnBatteries"); bat2 = X("/t:Task/t:Settings/t:StopIfGoingOnBatteries");
                trigUser = X("/t:Task/t:Triggers/t:LogonTrigger/t:UserId"); principal = X("/t:Task/t:Principals/t:Principal/t:UserId");
                logon = doc.SelectSingleNode("/t:Task/t:Triggers/t:LogonTrigger", ns) != null && doc.SelectNodes("/t:Task/t:Triggers/*", ns).Count == 1;
                parsed = true;
            }
            catch (Exception) { }
            bool fields = parsed && cmd == "\"" + exe + "\"" && args == "--autostart" && runLevel == "HighestAvailable" && limit == "PT0S" && multi == "IgnoreNew"
                && bat1 == "false" && bat2 == "false" && logon && trigUser == "PC\\Leo" && principal == "PC\\Leo" && xml.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>")
                && xml.Contains("&amp; Co");
            bool round = Autostart.CommandFromXml(xml) == exe && Autostart.CommandFromXml("<Command>C:\\a\\b.exe</Command>") == "C:\\a\\b.exe" && Autostart.CommandFromXml("nope") == null;
            bool same = Autostart.SamePath("C:\\A\\x.exe", "c:\\a\\X.EXE") && !Autostart.SamePath("C:\\A\\x.exe", "C:\\B\\x.exe") && !Autostart.SamePath(null, "a");
            bool names = Autostart.TaskName == "TarkovHipAimer" && Autostart.Argument == "--autostart";
            Check("G8 开机自动启动 task XML: logon trigger for the user, HighestAvailable, PT0S, IgnoreNew, no battery limits, exe + --autostart (+ working dir), XML-escaped; Command round-trip; path compare",
                fields && round && same && names, "parsed=" + parsed + " fields=" + fields + " (cmd=" + cmd + " wd=" + wd + ") command round-trip=" + round + " samePath=" + same);
        }

        // ---------------- migration: a real v2.4.5 settings.json ----------------

        static void G9Migration245()
        {
            string text = File.ReadAllText(Fixture("settings-v2.4.5.json"));
            GlobalSettings g; string err;
            bool ok = SettingsIO.TryParse(text, out g, out err);
            bool isOld = text.Contains("\"Combos\"") && text.Contains("\"Hand\"") && text.Contains("M4（Leo 改装）") && !text.Contains("\"Boost\"") && text.Contains("\"Manual\": true");
            Profile leo = ok ? g.FindProfile("Leo默认") : null, ak = ok ? g.FindProfile("AK-74N") : null;
            bool renamed = ok && leo != null && g.FindProfile("M4（Leo 改装）") == null && g.ActiveProfile == "Leo默认" && g.Profiles.Count == 2;
            // Leo's v2.4.5 values = exactly 「Leo默认」 (his screenshots), apart from the snapshot
            bool values = false;
            if (leo != null)
            {
                Profile want = LeoV30Default("Leo默认"); want.Validate(); // v3.1: the built-in moved on; his v2.4.5 file = the v3.0 built-in
                values = SettingsIO.ExportProfile(leo) == SettingsIO.ExportProfile(want);
            }
            bool snap = leo != null && leo.SavedDefault != null && leo.SavedDefaultAt == "2026-10-12 18:00" && leo.SavedDefault.Dirs[4].OffX == 0.7071 * 62;
            bool akOk = ak != null && ak.Dirs[2].OffX == 40 && ak.Dirs[4].OffX == 0.7071 * 40 && ak.Dirs[4].OffY == 0.7071 * 2 && ak.LeanQX == -12 && ak.RecoilDriftX == 4 && ak.StandOffsetX == 2;
            HandView v = new HandView(); if (ak != null) ak.FillView(1, v);
            bool akHand = ak != null && v.DX[2] == 41 && v.QY == ak.LeanEY; // mirror, the old 手动 77 / swap rule are gone
            string saved = ok ? SettingsIO.Serialize(g) : "";
            bool gone = ok && !saved.Contains("\"Combos\"") && !saved.Contains("\"Hand\"") && !saved.Contains("Manual") && !saved.Contains("M4（Leo 改装）");
            bool fresh = ok && g.Keys.Boost == 6 && g.BoostEnabled && g.OnlyWhenGameFocused && g.GameWindowTitle == "EscapeFromTarkov" && !g.Autostart && g.SplashOnStart
                && ak.Shape == "ring" && ak.HandColorOn && ak.HandColor == "#FF9500" && ak.Boost.Diameter == 24;
            GlobalSettings again; bool stable = ok && SettingsIO.TryParse(saved, out again, out err) && SettingsIO.Serialize(again) == saved;
            // a file that already has a 「Leo默认」 keeps both names
            GlobalSettings both; bool keepBoth = SettingsIO.TryParse("{\"ActiveProfile\":\"M4（Leo 改装）\",\"Profiles\":[{\"Name\":\"M4（Leo 改装）\"},{\"Name\":\"Leo默认\"}]}", out both, out err)
                && both.FindProfile("M4（Leo 改装）") != null && both.FindProfile("Leo默认") != null && both.ActiveProfile == "M4（Leo 改装）";
            // the old built-in profile on its own, active: renamed + active follows
            GlobalSettings solo; bool soloOk = SettingsIO.TryParse("{\"ActiveProfile\":\"M4（Leo 改装）\",\"Profiles\":[{\"Name\":\"AK\"},{\"Name\":\"M4（Leo 改装）\",\"Diameter\":33}]}", out solo, out err)
                && solo.ActiveProfile == "Leo默认" && solo.Active.Diameter == 33;
            Check("G9 Leo's v2.4.5 settings.json: 「M4（Leo 改装）」 → 「Leo默认」 (active follows; not when the name is taken), his values = the built-in exactly, snapshot kept, combos / 换手 manual / rule dropped, diagonals recomputed, v3.0 defaults, reload stable",
                ok && isOld && renamed && values && snap && akOk && akHand && gone && fresh && stable && keepBoth && soloOk,
                "old format=" + isOld + " renamed=" + renamed + " values=" + values + " snapshot=" + snap + " AK=" + akOk + " AK hand mirror=" + akHand + " removed gone=" + gone +
                " new defaults=" + fresh + " stable=" + stable + " name taken=" + keepBoth + " solo=" + soloOk + (ok ? "" : " " + err));
        }

        // ---------------- keys, preview list ----------------

        static void G10KeysPreview()
        {
            KeyBindings k = new KeyBindings();
            bool table = KeyBindings.ActionCount == 13 && KeyBindings.ActionNames.Length == 13 /* v4.3: the v4.1 转一圈校准 key (13) is gone */ && KeyBindings.ActionNames[12] == "强化准星"
                && k.Get(12) == 6 && k.GetMods(12) == 0 // v5.1.1 (X5, Leo 21 Nov 2026): default 鼠标侧键2 (v5.1: Ctrl+T)
                && KeyBindings.ActionBoost == 12 && k.FindOtherCombo(6, 0, -1) == 12 && k.FindOtherCombo(6, 0, 12) == -1;
            KeyBindings r = new KeyBindings(); r.SetCombo(12, 0x47, 0);
            HashSet<int> held = new HashSet<int>(); held.Add(0x47);
            bool read = r.Boost == 0x47 && MotionEngine.ReadInput(r, delegate(int vk) { return held.Contains(vk); }).Boost
                && !MotionEngine.ReadInput(new KeyBindings(), delegate(int vk) { return vk == 5; }).Boost && MotionEngine.ReadInput(new KeyBindings(), delegate(int vk) { return vk == 6; }).Boost;
            GlobalSettings c; string err;
            bool conflict = SettingsIO.TryParse("{\"Keys\":{\"FreeLook\":6,\"HandSwitch\":5}}", out c, out err) && c.Keys.Boost == 6 && !c.BoostEnabled;
            GlobalSettings c2; bool present = SettingsIO.TryParse("{\"BoostEnabled\":false,\"Keys\":{\"Boost\":7,\"HandSwitch\":5}}", out c2, out err) && c2.Keys.Boost == 7 && !c2.BoostEnabled;
            GlobalSettings c3; bool badVk = SettingsIO.TryParse("{\"Keys\":{\"Boost\":999,\"HandSwitch\":5}}", out c3, out err) && c3.Keys.Boost == 6 && c3.Keys.BoostMods == 0 && c3.BoostEnabled; // v5.1.1 (Leo 21 Nov): bad vk -> default 鼠标侧键2
            GlobalSettings rk = G(); rk.Keys.Boost = 0x48; rk.BoostEnabled = false; rk.ResetKeys();
            bool reset = rk.Keys.Boost == 6 && rk.Keys.BoostMods == 0 && rk.BoostEnabled; // v5.1.1 (Leo 21 Nov): 鼠标侧键2
            bool pm = (int)PreviewMode.BoostStand == 17 && (int)PreviewMode.JumpStand == 16 && (int)PreviewMode.HandFlip == 15 && (int)PreviewMode.Live == 0
                && Enum.GetValues(typeof(PreviewMode)).Length == 18;
            MotionDebug d = new MotionDebug(1, 0, 0, 0, 0, true, false, true);
            bool dbg = d.Boosted && d.Switched && !new MotionDebug(1, 0, 0, 0, 0, true, false).Boosted;
            bool name = GlobalSettings.DefaultProfileName == "Leo默认";
            Check("G10 强化准星 key: action 13 「强化准星」 = Ctrl+T (v5.1; was 侧键2), rebindable, conflict lookup, old file with 0x06 taken -> off, present values kept, bad vk -> default, 按键恢复默认; PreviewMode BoostStand appended (17); MotionDebug.Boosted",
                table && read && conflict && present && badVk && reset && pm && dbg && name,
                "table=" + table + " read=" + read + " migration conflict=" + conflict + " present=" + present + " bad vk=" + badVk + " reset=" + reset + " preview=" + pm + " debug=" + dbg);
        }
    }
}
