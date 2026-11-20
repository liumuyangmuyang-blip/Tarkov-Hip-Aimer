// UI41F (v4.1): 百分比偏移 (src/OffsetUnits.cs): px <-> % of half the game picture height through ViewScale, arrows, parsing.
using System;
using System.Globalization;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V41UnitsInit() { V41UnitsTests(); }

        static GlobalSettings UnitsG(int w, int h, double fov, double baseFov)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.ResGameW = w; g.ResGameH = h; g.ResFov = fov; g.ResBaseFov = baseFov;
            g.ShowFadeMs = 0; g.HideFadeMs = 0;
            g.Validate();
            return g;
        }

        static void V41UnitsTests()
        {
            // U1 Leo's example: D X -30 px (调参 1440 high) = 「← 4.17%」 on every resolution at FOV 75 / 调参 FOV 75.
            {
                int[][] res = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 3440, 1440 }, new[] { 1920, 1200 } };
                bool ok = true; string d = "";
                foreach (int[] r in res)
                {
                    GlobalSettings g = UnitsG(r[0], r[1], 75, 75);
                    string s = OffsetUnits.Format(-30, OffsetAxis.X, g);
                    if (s != "← 4.17%") { ok = false; d += r[0] + "x" + r[1] + "=" + s + " "; }
                    if (Math.Abs(OffsetUnits.PxPerPct(g) - 7.2) > 1e-9) { ok = false; d += "pxPerPct=" + OffsetUnits.PxPerPct(g) + " "; }
                }
                Check("U1 D X -30 px -> 「← 4.17%」 at 6 resolutions (FOV 75)", ok, d.Length == 0 ? "1 % = 7.2 px" : d);
            }

            // U2 arrows: profile X + = →, Y + = ↑ (UI y-up); screen Y + (y-down) = ↓; zero = 0.00% without an arrow.
            {
                GlobalSettings g = UnitsG(2560, 1440, 75, 75);
                string a = OffsetUnits.Format(30, OffsetAxis.X, g), b = OffsetUnits.Format(10, OffsetAxis.Y, g),
                       c = OffsetUnits.Format(-10, OffsetAxis.Y, g), e = OffsetUnits.FormatScreen(10, OffsetAxis.Y, g),
                       f = OffsetUnits.FormatScreen(-30, OffsetAxis.X, g), z = OffsetUnits.Format(0.001, OffsetAxis.X, g),
                       zn = OffsetUnits.Format(-0.001, OffsetAxis.Y, g);
                bool ok = a == "→ 4.17%" && b == "↑ 1.39%" && c == "↓ 1.39%" && e == "↓ 1.39%" && f == "← 4.17%" && z == "0.00%" && zn == "0.00%";
                Check("U2 arrows → ↑ ↓ ← and 0.00% (no -0.00)", ok, a + " | " + b + " | " + c + " | " + e + " | " + f + " | " + z + " | " + zn);
            }

            // U3 the on-screen direction is real: a profile D X > 0 (→) moves the ring RIGHT, W Y > 0 (↑) moves it UP.
            {
                GlobalSettings g = UnitsG(2560, 1440, 75, 75);
                Profile p = g.Active; p.SetStandPoint(0, 0); /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in has a 站立 point now; this case measures around the centre */ p.Dirs[3].OffX = 30; p.Dirs[3].OffY = 0; p.Dirs[0].OffX = 0; p.Dirs[0].OffY = 20;
                Profile.RecomputeDiagonals(p);
                Sim s = new Sim(p, g, 144); s.Run(2.0, In("D"));
                double ox = s.E.OffsetX; string fx = OffsetUnits.FormatScreen(ox, OffsetAxis.X, g);
                Sim t = new Sim(p, g, 144); t.Run(2.0, In("W"));
                double oy = t.E.OffsetY; string fy = OffsetUnits.FormatScreen(oy, OffsetAxis.Y, g);
                bool ok = ox > 0 && fx.StartsWith("→") && oy < 0 && fy.StartsWith("↑")
                    && OffsetUnits.Format(30, OffsetAxis.X, g) == fx && OffsetUnits.Format(20, OffsetAxis.Y, g) == fy;
                Check("U3 → = ring right of centre, ↑ = above (MotionEngine screen offsets)", ok,
                    "D OffsetX=" + F(ox) + " " + fx + ", W OffsetY=" + F(oy) + " " + fy);
            }

            // U4 storage round trip: px -> % -> px is exact to 1e-9 px (double, nothing rounded) at several screens and FOVs.
            {
                bool ok = true; double worst = 0;
                GlobalSettings[] gs = { UnitsG(1920, 1080, 75, 75), UnitsG(3840, 2160, 60, 75), UnitsG(3440, 1440, 50, 70) };
                foreach (GlobalSettings g in gs)
                    for (double px = -500; px <= 500; px += 0.37)
                    {
                        double back = OffsetUnits.ToPx(OffsetUnits.ToPct(px, g), g);
                        worst = Math.Max(worst, Math.Abs(back - px));
                    }
                ok = worst < 1e-9;
                Check("U4 px -> % -> px exact (double)", ok, "worst " + worst.ToString("E2", CultureInfo.InvariantCulture));
            }

            // U5 typed % round trip: every 0.01 % from -70 to 70 shows back the same 2-decimal text after % -> px -> %.
            {
                bool ok = true; string d = ""; int n = 0;
                GlobalSettings[] gs = { UnitsG(2560, 1440, 75, 75), UnitsG(1920, 1080, 55, 75) };
                foreach (GlobalSettings g in gs)
                    for (int i = -7000; i <= 7000; i++)
                    {
                        double pct = i / 100.0;
                        double px = OffsetUnits.ToPx(pct, OffsetAxis.X, g); // v5.1.1 (X5): the on-screen % (FOV side on X)
                        string want = OffsetUnits.FormatPct(pct, OffsetAxis.X), got = OffsetUnits.Format(px, OffsetAxis.X, g);
                        n++;
                        if (want != got) { ok = false; if (d.Length < 200) d += want + "->" + got + " "; }
                    }
                Check("U5 typed 0.01 % steps survive % -> px -> % (" + n + " values)", ok, d);
            }

            // U6 // v5.1.1 (X5, Leo 21 Nov 2026): FOV no longer in k; X only, gun side x1, other side x gameFov/baseFov, Y unchanged.
            // 调参 FOV 75, game FOV 50: D -30 px (left = other side) -> x 50/75 = 「← 2.78%」, A +30 px (gun side) stays 「→ 4.17%」,
            // 换手 swaps the sides, Y never scaled.
            {
                GlobalSettings g = UnitsG(2560, 1440, 50, 75);
                double want = 30.0 / 720.0 * 100.0 * 50.0 / 75.0;
                double got = -OffsetUnits.ToPct(-30, OffsetAxis.X, g);
                string s = OffsetUnits.Format(-30, OffsetAxis.X, g);
                double k = ViewScale.Factor(g);
                bool ok = Math.Abs(got - want) < 1e-9 && s == "← 2.78%" && OffsetUnits.Format(30, OffsetAxis.X, g) == "→ 4.17%"
                    && OffsetUnits.Format(-30, OffsetAxis.X, true, g) == "← 4.17%" && OffsetUnits.Format(30, OffsetAxis.X, true, g) == "→ 2.78%"
                    && OffsetUnits.Format(30, OffsetAxis.Y, g) == "↑ 4.17%" && OffsetUnits.Format(-30, OffsetAxis.Y, g) == "↓ 4.17%" && k == 1.0;
                Check("U6 FOV 50 vs 调参 FOV 75: D -30 px -> 「← 2.78%」, A +30 px -> 「→ 4.17%」, 换手 mirrored, Y unchanged", ok, F(got) + "% " + s + " k=" + F(k));
            }

            // U7 parsing what people type.
            {
                string[] xs = { "← 4.17%", "4.17", "-4.17", "4.17 %", "左 4.17", "右4.17", "4,17", "４．１７％", "－4.17", "→ -4.17", "4.17←", "+4.17", " 4.17 " };
                double[] xv = { -4.17, 4.17, -4.17, 4.17, -4.17, 4.17, 4.17, 4.17, -4.17, -4.17, -4.17, 4.17, 4.17 };
                bool ok = true; string d = "";
                for (int i = 0; i < xs.Length; i++)
                {
                    double v; bool r = OffsetUnits.TryParse(xs[i], OffsetAxis.X, out v);
                    if (!r || Math.Abs(v - xv[i]) > 1e-12) { ok = false; d += "[" + xs[i] + "]=" + (r ? F(v) : "fail") + " "; }
                }
                double y;
                if (!OffsetUnits.TryParse("↓ 1.39%", OffsetAxis.Y, out y) || Math.Abs(y + 1.39) > 1e-12) { ok = false; d += "↓ "; }
                if (!OffsetUnits.TryParse("上 2", OffsetAxis.Y, out y) || Math.Abs(y - 2) > 1e-12) { ok = false; d += "上 "; }
                string[] bad = { "", "%", "abc", "↑ 2", "← → 2", "1e400", "NaN", "4.1.7" };
                double tmp;
                foreach (string b in bad) if (OffsetUnits.TryParse(b, OffsetAxis.X, out tmp)) { ok = false; d += "accepted[" + b + "] "; }
                if (OffsetUnits.TryParse("← 2", OffsetAxis.Y, out tmp)) { ok = false; d += "accepted ← on Y "; }
                Check("U7 TryParse: arrows / words / signs / full-width / comma; junk and wrong-axis arrows rejected", ok, d);
            }

            // U8 unusable settings fall back to the 1440 reference (no NaN / divide by zero).
            {
                GlobalSettings g = UnitsG(2560, 1440, 75, 75);
                g.ResGameH = 0; g.ResGameW = 0;
                double a = OffsetUnits.ToPct(-30, null), b = OffsetUnits.ToPct(-30, g), c = OffsetUnits.ToPct(double.NaN, g);
                bool ok = Math.Abs(a + 30.0 / 7.2) < 1e-9 && !double.IsNaN(b) && !double.IsInfinity(b) && c == 0
                    && OffsetUnits.Format(double.PositiveInfinity, OffsetAxis.Y, g) == "0.00%";
                Check("U8 null / zero-size settings / NaN -> finite (reference 1440)", ok, F(a) + " " + F(b) + " " + F(c));
            }

            // U9 vectors: zero axis left out; screen vector uses y-down.
            {
                GlobalSettings g = UnitsG(1920, 1080, 75, 75);
                string v1 = OffsetUnits.FormatVector(-30, 10, g), v2 = OffsetUnits.FormatVector(0, -10, g), v3 = OffsetUnits.FormatVector(0, 0, g),
                       v4 = OffsetUnits.FormatScreenVector(OffsetUnits.PctToScreen(2.5, g), OffsetUnits.PctToScreen(0.83, g), g);
                bool ok = v1 == "← 4.17%  ↑ 1.39%" && v2 == "↓ 1.39%" && v3 == "0.00%" && v4 == "→ 2.50%  ↓ 0.83%";
                Check("U9 FormatVector / FormatScreenVector", ok, v1 + " | " + v2 + " | " + v3 + " | " + v4);
            }

            // U10 magnitude and rounding (away from zero at .5): 0.005 -> 0.01, 4.165 -> 4.17 (within double).
            {
                GlobalSettings g = UnitsG(2560, 1440, 75, 75);
                string m = OffsetUnits.FormatMagnitude(-30, g);
                bool ok = m == "4.17%" && OffsetUnits.Round(0.005) == 0.01 && OffsetUnits.Number(-0.004) == "0.00"
                    && OffsetUnits.SpinStepPct == 0.10 && OffsetUnits.Decimals == 2;
                Check("U10 FormatMagnitude / Round / Number", ok, m);
            }

            // U11 (REBRUTE41 P2-3) ASCII arrows: <- -> < > <-- --> ^ v V, before or after the number; a dash that is not part
            // of an arrow stays a minus; wrong axis / two arrows still rejected; NamesDirection sees them (plain number does not).
            {
                string[] xs = { "<- 4.17%", "-> 4.17", "< 4.17", "> 4.17", "<-- 2", "--> 2", "4.17 <-", "4.17->", "<-4.17", "-> -4.17", "-4.17 ->", "＜ 1", "<－ 1" };
                double[] xv = { -4.17, 4.17, -4.17, 4.17, -2, 2, -4.17, 4.17, -4.17, -4.17, -4.17, -1, -1 };
                bool ok = true; string d = "";
                for (int i = 0; i < xs.Length; i++)
                {
                    double v; bool r = OffsetUnits.TryParse(xs[i], OffsetAxis.X, out v);
                    if (!r || Math.Abs(v - xv[i]) > 1e-12) { ok = false; d += "[" + xs[i] + "]=" + (r ? F(v) : "fail") + " "; }
                }
                string[] ys = { "^ 1.39", "v 1.39", "V1.39", "1.39v", "＾2", "^ -1" };
                double[] yv = { 1.39, -1.39, -1.39, -1.39, 2, -1 };
                for (int i = 0; i < ys.Length; i++)
                {
                    double v; bool r = OffsetUnits.TryParse(ys[i], OffsetAxis.Y, out v);
                    if (!r || Math.Abs(v - yv[i]) > 1e-12) { ok = false; d += "Y[" + ys[i] + "]=" + (r ? F(v) : "fail") + " "; }
                }
                double tmp;
                string[] badX = { "^ 2", "v 2", "<->", "<- -> 2", "<-", "<<2", "4.17 vs" };
                foreach (string b in badX) if (OffsetUnits.TryParse(b, OffsetAxis.X, out tmp)) { ok = false; d += "accepted X[" + b + "] "; }
                string[] badY = { "<- 2", "> 2", "^ v 2" };
                foreach (string b in badY) if (OffsetUnits.TryParse(b, OffsetAxis.Y, out tmp)) { ok = false; d += "accepted Y[" + b + "] "; }
                // the old forms still mean the same
                if (!OffsetUnits.TryParse("-4.17", OffsetAxis.X, out tmp) || tmp != -4.17) { ok = false; d += "minus "; }
                string[] named = { "<- 1", "^ 1", "v 1", "-1", "+1", "左 1", "← 1" };
                foreach (string n in named) if (!OffsetUnits.NamesDirection(n)) { ok = false; d += "unnamed[" + n + "] "; }
                string[] plain = { "1", "4.17%", " 4,17 ", "", null };
                foreach (string p in plain) if (OffsetUnits.NamesDirection(p)) { ok = false; d += "named[" + p + "] "; }
                if (OffsetUnits.AsciiArrows("4.17") != "4.17" || OffsetUnits.AsciiArrows("<-4") != "←4" || OffsetUnits.AsciiArrows("-->-4") != "→-4")
                { ok = false; d += "AsciiArrows "; }
                Check("U11 TryParse: ASCII arrows <- -> ^ v (and < > <-- -->) work like ← → ↑ ↓", ok, d);
            }
        }
    }
}
