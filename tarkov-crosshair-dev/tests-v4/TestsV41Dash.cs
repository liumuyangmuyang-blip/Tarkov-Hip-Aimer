using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.1 (DASH41F) 首页 dashboard: 百分比偏移 readouts, fixed chart range, dirty rectangles, HUD placement + maths,
    // brute-render P2-3 / P2-6 / P2-10 fixes. Pure parts only (src/DashboardLogic.cs). Self-registers like TestsV4Dash.cs:
    // do NOT also call V41DashTests() from Main.
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V41DashInit() { V41DashTests(); }

        static void V41DashTests()
        {
            D41Format();
            D41Text();
            D41Pct();
            D41Range();
            D41Dirty();
            D41Hud();
            D41Travel();
            D41Math();
            D41TryLines();
            D41Words();
            D41Alloc();
            D41Lean();
        }

        static void D41Lean()
        {
            // DASH41G (DOCCHECK41 D5): the 侧身 row says 居中 / 左侧身 / 右侧身 (+ 「（过渡中）」), never a %; fixed strings (the panel
            // compares by reference, so a steady lean allocates nothing); settled exactly when the engine's 侧身过渡 time is up
            bool ok = ReferenceEquals(DashFormat.LeanText(0), DashFormat.LeanCentre) && ReferenceEquals(DashFormat.LeanText(-1), DashFormat.LeanLeft)
                && ReferenceEquals(DashFormat.LeanText(1), DashFormat.LeanRight) && ReferenceEquals(DashFormat.LeanText(-0.5), DashFormat.LeanLeftMoving)
                && ReferenceEquals(DashFormat.LeanText(0.5), DashFormat.LeanRightMoving) && ReferenceEquals(DashFormat.LeanText(0.049), DashFormat.LeanCentre)
                && ReferenceEquals(DashFormat.LeanText(-0.95), DashFormat.LeanLeft) && ReferenceEquals(DashFormat.LeanText(double.NaN), DashFormat.LeanCentre)
                && ReferenceEquals(DashFormat.LeanText(double.NegativeInfinity), DashFormat.LeanCentre)
                && DashFormat.LeanLeft == "左侧身" && DashFormat.LeanRight == "右侧身" && DashFormat.LeanCentre == "居中";
            foreach (string s in new string[] { DashFormat.LeanCentre, DashFormat.LeanLeft, DashFormat.LeanRight, DashFormat.LeanLeftMoving, DashFormat.LeanRightMoving })
                if (s.IndexOf('%') >= 0) ok = false;
            // the real engine: hold Q, the row reads 左侧身（过渡中） first and 左侧身 once LeanMs has passed; release, back to 居中
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile p = g.Active;
            MotionEngine e = new MotionEngine();
            InputState q = new InputState(); q.LeanL = true;
            double dt = 1.0 / 144, t = 0;
            string first = null, atLeanMs = null;
            for (int f = 0; f < 2000 && atLeanMs == null; f++)
            {
                e.GameFocused = true; e.OwnForeground = true;
                e.Update(dt, q, p, g, PreviewMode.Live, 1.0);
                t += dt * 1000.0;
                if (first == null) first = DashFormat.LeanText(e.Lean);
                if (t >= p.LeanMs + dt * 1000.0) atLeanMs = DashFormat.LeanText(e.Lean);
            }
            string back = null;
            for (int f = 0; f < 2000; f++) { e.Update(dt, new InputState(), p, g, PreviewMode.Live, 1.0); back = DashFormat.LeanText(e.Lean); }
            ok = ok && (p.LeanMs <= 0 || ReferenceEquals(first, DashFormat.LeanLeftMoving)) && ReferenceEquals(atLeanMs, DashFormat.LeanLeft)
                && ReferenceEquals(back, DashFormat.LeanCentre);
            Check("D41-12 实时数据 侧身 row: 居中 / 左侧身 / 右侧身 (+（过渡中）), no % (DOCCHECK41 D5), settled after 侧身过渡 (real engine, Q hold)", ok,
                "first=" + first + " atLeanMs=" + atLeanMs + " back=" + back + " LeanMs=" + p.LeanMs);
        }

        static void D41Format()
        {
            // brute-render P2-6: huge / infinite durations clamp to 999.99 s (the long cast overflowed before)
            bool ok = DashFormat.Seconds2(double.PositiveInfinity) == "999.99 s" && DashFormat.Seconds2(1e300) == "999.99 s"
                && DashFormat.Seconds2(999990) == "999.99 s" && DashFormat.Seconds2(999995) == "999.99 s" && DashFormat.Seconds2(double.NaN) == "0.00 s"
                && DashFormat.Seconds2(double.NegativeInfinity) == "0.00 s" && DashFormat.Seconds2(700) == "0.70 s";
            Random r = new Random(41);
            string bad = "";
            for (int i = 0; i < 20000 && bad.Length == 0; i++)
            {
                double v = Math.Pow(10, r.NextDouble() * 320) * (r.Next(2) == 0 ? 1 : -1);
                string s = DashFormat.Seconds2(v);
                if (s.IndexOf('-') >= 0 || !s.EndsWith(" s") || s.Length > 8) bad = v.ToString("R") + " -> " + s;
            }
            ok = ok && bad.Length == 0
                && DashFormat.Signed2(-0.004) == "+0.00" && DashFormat.Signed2(-0.005) == "-0.01" && DashFormat.Signed2(12.345) == "+12.35"
                && DashFormat.Signed2(double.NaN) == "+0.00" && DashFormat.Signed2(1e12) == "+99999.99"
                && DashFormat.Fixed(0.8734, 3) == "0.873" && DashFormat.Fixed(-2.5, 1) == "2.5" && DashFormat.Fixed(double.NaN, 2) == "0.00"
                && DashFormat.Fixed(7, 9) == "7.000" && DashFormat.PctKey(4.166) == 417 && DashFormat.PctKey(-4.165) == -417 && DashFormat.PctKey(double.NaN) == 0;
            Check("D41-1 Seconds2 clamps huge / infinite durations (brute-render P2-6), Signed2 / Fixed / PctKey edges", ok, bad);
        }

        static void D41Text()
        {
            // brute-render P2-10: long.MinValue used to be the "nothing yet" key, so Int(long.MinValue) never showed its text
            DashText t = new DashText();
            bool a = t.Int(long.MinValue, "", "") && t.Text == long.MinValue.ToString(CultureInfo.InvariantCulture);
            bool b = !t.Int(long.MinValue, "", "");
            t.Invalidate();
            bool c = t.Int(long.MinValue, "x", "") && t.Text == "x" + long.MinValue.ToString(CultureInfo.InvariantCulture);
            DashText k = new DashText();
            bool d = k.Keyed(long.MinValue, "a") && k.Text == "a" && !k.Keyed(long.MinValue, "b") && k.Keyed(0, "c") && k.Text == "c";
            Check("D41-2 DashText shows Int(long.MinValue) / Keyed(long.MinValue) (brute-render P2-10), Invalidate re-emits", a && b && c && d, a + " " + b + " " + c + " " + d);
        }

        static void D41Pct()
        {
            // the % text is OffsetUnits' own format, rebuilt only when the SHOWN hundredths change
            DashPct p = new DashPct();
            bool a = p.Set(OffsetUnits.ScreenToPct(-30, null), OffsetAxis.X) && p.Text == "← 4.17%";
            string first = p.Text;
            bool b = !p.Set(-4.1701, OffsetAxis.X) && ReferenceEquals(first, p.Text);
            bool c = p.Set(-4.1701, OffsetAxis.Y) && p.Text == "↓ 4.17%";        // same number, other axis: rebuilt
            bool d = p.Set(0.004, OffsetAxis.Y) && p.Text == "0.00%" && !p.Set(-0.004, OffsetAxis.Y);
            bool e = p.Set(OffsetUnits.ScreenToPct(10, null), OffsetAxis.Y) && p.Text == "↑ 1.39%";
            bool f = true; // v4.3: the 鼠标滞后 status text went with the feature

            // the arrows follow the real ring: hold D / A / W / S on the real engine with 「Leo默认」 and compare the shown
            // arrow with the side the ring sits on (dashboard UI coords: x = OffsetX, y = -OffsetY)
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile pr = g.Active; pr.SetStandPoint(0, 0); /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in has a 站立 point now; this case measures around the centre */
            string why = "";
            bool arrows = true;
            string[] keys = { "D", "A", "W", "S" };
            foreach (string key in keys)
            {
                MotionEngine en = new MotionEngine();
                LiveInputFilter fl = new LiveInputFilter();
                for (int i = 0; i < 128; i++)
                {
                    InputState raw = new InputState();
                    raw.Right = key == "D"; raw.Left = key == "A"; raw.Fwd = key == "W"; raw.Back = key == "S";
                    en.GameFocused = true; en.OwnForeground = true;
                    en.Update(1.0 / 64, fl.Next(raw, true, true), pr, g, PreviewMode.Live, 1.0);
                }
                double x = en.OffsetX, y = -en.OffsetY;
                DashPct px = new DashPct(), py = new DashPct();
                px.Set(OffsetUnits.ScreenToPct(x, g), OffsetAxis.X); py.Set(OffsetUnits.ScreenToPct(y, g), OffsetAxis.Y);
                int dir = key == "W" ? 0 : (key == "S" ? 1 : (key == "A" ? 2 : 3));
                double ox = pr.Dirs[dir].OffX, oy = pr.Dirs[dir].OffY;   // profile: X right +, Y up +
                bool okx = Math.Abs(ox) < 0.05 ? true : px.Text.StartsWith(ox < 0 ? "←" : "→");
                bool oky = Math.Abs(oy) < 0.05 ? true : py.Text.StartsWith(oy < 0 ? "↓" : "↑");
                // and the number is the profile value converted by the ONE conversion (OffsetUnits.ToPct of the profile px)
                bool num = px.Text.EndsWith(OffsetUnits.Number(OffsetUnits.ToPct(ox, g)) + "%") || Math.Abs(ox) < 0.05;
                if (!okx || !oky || !num) { arrows = false; why += key + ": " + px.Text + " / " + py.Text + " (profile " + ox + ", " + oy + ") "; }
            }
            Check("D41-3 实时数据 % text = OffsetUnits format, rebuilt only on a shown change; arrows match the real ring for D/A/W/S",
                a && b && c && d && e && f && arrows, a + " " + b + " " + c + " " + d + " " + e + " " + f + " " + why);
        }

        static void D41Range()
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile p = g.Active;
            HandView v0 = new HandView(), v1 = new HandView();
            double peak = DashRange.ProfilePeakPct(p, g, v0, v1);
            // independent maximum over the profile's own values
            double m = 0;
            for (int h = 0; h < 2; h++)
            {
                HandView v = new HandView();
                p.FillView(h, v);
                for (int i = 0; i < Profile.DirCount; i++) m = Math.Max(m, Math.Max(Math.Abs(v.DX[i]), Math.Abs(v.DY[i])));
                m = Math.Max(m, Math.Max(Math.Max(Math.Abs(v.QX), Math.Abs(v.QY)), Math.Max(Math.Abs(v.EX), Math.Abs(v.EY))));
                m = Math.Max(m, Math.Abs(v.Drift));
            }
            m = Math.Max(m, Math.Abs(p.RecoilMaxPx));
            double want = OffsetUnits.ToPct(m, g);
            DashRange r = new DashRange();
            r.SetFloor(peak);
            bool ok = Math.Abs(peak - want) < 1e-6 && r.Range >= peak * DashRange.Headroom && peak > 0
                && DashRange.ProfilePeakPct(null, g, v0, v1) == 0 && DashRange.ProfilePeakPct(p, g, null, v1) == 0;
            // the same profile always comes back to the same range: a 跳跃 (「Leo默认」 dips 300 px) zooms out, then 2.5 s per rung back
            DashRange r2 = new DashRange();
            r2.SetFloor(peak);
            bool jumpOut = r2.Update(OffsetUnits.ToPct(300, g), 0) && r2.Range > r.Range;
            for (int t = 100; t <= 30000; t += 100) r2.Update(peak, t);
            ok = ok && jumpOut && r2.Range == r.Range;
            Check("D41-4 chart range floor = the profile's largest offset in % (dirs, lean, recoil, both hands) x 1.15; a jump zooms out and comes back",
                ok, "peak=" + F(peak) + " want=" + F(want) + " range=" + F(r.Range) + " jumpOut=" + jumpOut + " after=" + F(r2.Range));
        }

        static void D41Dirty()
        {
            // brute force against a pixel model: the stored rectangles cover every added rectangle, never more than Max,
            // and never touch each other (touching ones are merged)
            Random rnd = new Random(4141);
            DashDirty d = new DashDirty();
            string bad = null;
            for (int it = 0; it < 4000 && bad == null; it++)
            {
                d.Clear();
                int n = 1 + rnd.Next(30);
                bool[,] want = new bool[200, 140];
                for (int k = 0; k < n; k++)
                {
                    DashRect a = new DashRect(rnd.Next(190), rnd.Next(130), rnd.Next(40), rnd.Next(40));
                    d.Add(a);
                    for (int y = a.Y; y < Math.Min(140, a.Bottom); y++) for (int x = a.X; x < Math.Min(200, a.Right); x++) want[x, y] = true;
                }
                if (d.Count > DashDirty.Max) { bad = "count " + d.Count; break; }
                for (int i = 0; i < d.Count && bad == null; i++)
                    for (int j = i + 1; j < d.Count; j++)
                    {
                        DashRect p = d[i], q = d[j];
                        if (p.X <= q.Right + 2 && q.X <= p.Right + 2 && p.Y <= q.Bottom + 2 && q.Y <= p.Bottom + 2) { bad = "near rects kept " + p + " / " + q; break; }
                    }
                for (int y = 0; y < 140 && bad == null; y++)
                    for (int x = 0; x < 200; x++)
                    {
                        if (!want[x, y]) continue;
                        bool cov = false;
                        for (int i = 0; i < d.Count; i++) { DashRect q = d[i]; if (x >= q.X && x < q.Right && y >= q.Y && y < q.Bottom) { cov = true; break; } }
                        if (!cov) { bad = "pixel " + x + "," + y + " not covered (it " + it + ")"; break; }
                    }
            }
            d.Clear(); d.Add(new DashRect(1, 1, 5, 5)); d.SetAll(); d.Add(new DashRect(1, 1, 5, 5));
            bool all = d.All && d.Count == 0;
            d.Clear(); d.Add(new DashRect(5, 5, 0, 9));
            bool empty = !d.All && d.Count == 0 && d.Bounds().IsEmpty;
            Check("D41-5 dirty rectangles: 4000 random sets cover every changed pixel, <= 8 rects, none touching; SetAll / empty", bad == null && all && empty, bad ?? "");
        }

        static void D41Hud()
        {
            // the 4 HUD widgets: inside the plot, clear of both axes and their number labels, the quadrant tags, the
            // 屏幕中心 tag and each other; widths / heights capped by their quadrant; line counts in [min, want]
            Random rnd = new Random(4242);
            DashRect[] rects = new DashRect[DashHud.Count];
            int[] lines = new int[DashHud.Count];
            string bad = null;
            int shown = 0, cases = 0, colPairs = 0, colEqual = 0, rowPairs = 0, rowEqual = 0, grown = 0;
            for (int it = 0; it < 30000 && bad == null; it++)
            {
                int w = 100 + rnd.Next(2400), h = 80 + rnd.Next(1600);
                DashRect inner = new DashRect(rnd.Next(50), rnd.Next(50), w, h);
                double cx = inner.X + w / 2.0, cy = inner.Y + h / 2.0;
                double u = 1 + rnd.NextDouble() * 2.6;
                int tagH = (int)(30 * u), bandX = (int)(25 * u), bandY = (int)(40 * u), gap = (int)(6 * u);
                int titleH = (int)(22 * u), lineH = (int)(18 * u), pad = (int)(5 * u);
                int[] needW = { (int)(180 * u), (int)(190 * u), (int)(190 * u), (int)(140 * u) };
                int[] want = { 5, 6, 5, 4 };
                int[] min = { 3, 3, 3, 3 };
                DashRect plate = rnd.Next(3) == 0 ? new DashRect(0, 0, 0, 0)
                    : new DashRect((int)cx - (int)(18 * u) - (int)(70 * u), (int)cy + (int)(22 * u), (int)(70 * u), (int)(23 * u));
                DashHud.Place(inner, cx, cy, tagH, bandX, bandY, gap, titleH, lineH, pad, needW, want, min, plate, rects, lines);
                cases++;
                int icx = (int)Math.Floor(cx), icy = (int)Math.Floor(cy);
                for (int i = 0; i < DashHud.Count && bad == null; i++)
                {
                    DashRect b = rects[i];
                    if (b.IsEmpty) { if (lines[i] != 0) bad = "hidden widget with lines"; continue; }
                    shown++;
                    string at = " (" + i + " at " + w + "x" + h + " u" + F(u) + ")";
                    string rule = HudRule(i, b, rects, inner, icx, icy, tagH, bandX, bandY, gap, plate);
                    if (rule != null) bad = rule + at;
                    else if (lines[i] < min[i] || lines[i] > want[i]) bad = "lines " + lines[i] + at;
                    // DASH41G grid pass: a box is its own size, or grown to its column partner's width / row partner's height;
                    // the lines it shows are always what its height holds (capped at want)
                    else if (b.W != needW[i] && b.W != rects[i == 0 ? 3 : (i == 3 ? 0 : (i == 1 ? 2 : 1))].W) bad = "width" + at;
                    else if (b.H != titleH + lines[i] * lineH + 2 * pad && b.H != rects[i == 0 ? 1 : (i == 1 ? 0 : (i == 2 ? 3 : 2))].H) bad = "height" + at;
                    else if (b.W < needW[i] || b.H < titleH + lines[i] * lineH + 2 * pad) bad = "shrunk" + at;
                    else if (lines[i] != Math.Min(want[i], (b.H - titleH - 2 * pad) / lineH)) bad = "lines vs height" + at;
                    if (b.W != needW[i] || b.H != titleH + lines[i] * lineH + 2 * pad) grown++;
                }
                // one grid: column partners share their inner edge, row partners their outer edge, whenever growing was allowed
                if (bad == null)
                {
                    if (!rects[0].IsEmpty && !rects[3].IsEmpty) { colPairs++; if (rects[0].Right == rects[3].Right && rects[0].X == rects[3].X) colEqual++; }
                    if (!rects[1].IsEmpty && !rects[2].IsEmpty) { colPairs++; if (rects[1].X == rects[2].X && rects[1].Right == rects[2].Right) colEqual++; }
                    if (!rects[0].IsEmpty && !rects[1].IsEmpty) { rowPairs++; if (rects[0].Y == rects[1].Y && rects[0].Bottom == rects[1].Bottom) rowEqual++; }
                    if (!rects[3].IsEmpty && !rects[2].IsEmpty) { rowPairs++; if (rects[3].Y == rects[2].Y && rects[3].Bottom == rects[2].Bottom) rowEqual++; }
                }
            }
            DashHud.Place(new DashRect(0, 0, 0, 0), 0, 0, 1, 1, 1, 1, 1, 1, 1, new int[4], new int[4], new int[4], new DashRect(0, 0, 0, 0), rects, lines);
            bool safe = rects[0].IsEmpty && lines[0] == 0;
            DashHud.Place(new DashRect(0, 0, 500, 400), double.NaN, 3, 1, 1, 1, 1, 1, 1, 1, new int[] { 9, 9, 9, 9 }, new int[] { 1, 1, 1, 1 }, new int[] { 1, 1, 1, 1 }, new DashRect(0, 0, 0, 0), rects, lines);
            safe = safe && rects[1].IsEmpty;
            Check("D41-6 HUD widgets (30k random plots x DPI): inside the plot, clear of axes, labels, quadrant tags, 屏幕中心 and each other; "
                + "DASH41G one grid: column partners one width, row partners one height wherever that still fits",
                // measured 17 Nov (seed 4242): columns 23587/23788 aligned, rows 24331/24408; the grid pass leaves a pair alone only
                // when the grown box would break a placement rule, so the guards sit just under those shares
                bad == null && safe && shown > cases && grown > 0 && colEqual * 100 >= colPairs * 98 && rowEqual * 100 >= rowPairs * 99,
                (bad ?? "") + " shown=" + shown + "/" + cases + " grown=" + grown + " columns " + colEqual + "/" + colPairs + " rows " + rowEqual + "/" + rowPairs);
        }

        /// <summary>The placement rules of HUD widget i at b (null = all kept): inside the plot, clear of both axes and their
        /// labels, the quadrant tags, the 屏幕中心 tag and the other widgets, within MaxShareW / MaxShareH of its quadrant.</summary>
        static string HudRule(int i, DashRect b, DashRect[] rects, DashRect inner, int icx, int icy, int tagH, int bandX, int bandY, int gap, DashRect plate)
        {
            bool left = i == 0 || i == 3, top = i == 0 || i == 1;
            if (!inner.Contains(b)) return "outside plot";
            if (left ? b.Right > icx - gap : b.X < icx + bandY + gap) return "over the vertical axis / its labels";
            if (top ? b.Bottom > icy - gap : b.Y < icy + bandX + gap) return "over the horizontal axis / its labels";
            if (top ? b.Y < inner.Y + tagH : b.Bottom > inner.Bottom - tagH) return "over the quadrant tags";
            if (!plate.IsEmpty && b.Intersects(new DashRect(plate.X - gap, plate.Y - gap, plate.W + 2 * gap, plate.H + 2 * gap))) return "over 屏幕中心";
            if (b.W > (left ? icx - inner.X : inner.Right - icx - 1) * DashHud.MaxShareW) return "too wide";
            if (b.H > (int)((top ? icy - inner.Y : inner.Bottom - icy - 1) * DashHud.MaxShareH)) return "too tall";
            for (int j = 0; j < DashHud.Count; j++) if (j != i && !rects[j].IsEmpty && rects[j].Intersects(b)) return "widgets " + j + "/" + i + " overlap";
            return null;
        }

        static void D41Travel()
        {
            // 运动方程 m = m0 + (m* - m0)·c(u): with the engine's own numbers it must reproduce MoveX / MoveY (Exact), through
            // random key changes, mid-travel restarts, odd frame times and both hands
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile p = g.Active;
            MotionEngine e = new MotionEngine();
            LiveInputFilter fl = new LiveInputFilter();
            DashTravel tx = new DashTravel(), ty = new DashTravel();
            HandView v0 = new HandView(), v1 = new HandView();
            Random rnd = new Random(4343);
            int frames = 0, traveling = 0, exact = 0, starts = 0;
            InputState raw = new InputState();
            string bad = null;
            for (int f = 0; f < 60000 && bad == null; f++)
            {
                if (rnd.Next(9) == 0)
                {
                    raw = new InputState();
                    raw.Right = rnd.Next(3) == 0; raw.Left = rnd.Next(3) == 0; raw.Fwd = rnd.Next(3) == 0; raw.Back = rnd.Next(4) == 0;
                    raw.LeanL = rnd.Next(8) == 0; raw.Fire = rnd.Next(6) == 0;
                }
                double dt = rnd.Next(10) == 0 ? rnd.NextDouble() * 0.2 : 1.0 / (30 + rnd.Next(200));
                e.GameFocused = true; e.OwnForeground = true;
                e.Update(dt, fl.Next(raw, true, true), p, g, PreviewMode.Live, 1.0);
                p.FillView(0, v0); p.FillView(1, v1);
                int dir = e.CurrentDir;
                double b = e.HandBlend;
                double mtx = dir < 0 ? 0 : v0.DX[dir] + (v1.DX[dir] - v0.DX[dir]) * b;
                double mty = dir < 0 ? 0 : v0.DY[dir] + (v1.DY[dir] - v0.DY[dir]) * b;
                tx.StepLeg(e, 0); // v5.1 (W26, decision 7): the HUD reads the engine's running leg (same >= 99 % exact check)
                ty.StepLeg(e, 1);
                frames++;
                foreach (DashTravel t in new DashTravel[] { tx, ty })
                {
                    if (t.Active) { traveling++; if (t.Exact) exact++; }
                    else if (!t.Exact) bad = "not exact while still at frame " + f;
                    if (t.U < 0 || t.U > 1 || double.IsNaN(t.C)) bad = "u/c out of range at frame " + f;
                }
            }
            starts = tx.Restarts + ty.Restarts;
            double share = traveling == 0 ? 0 : exact / (double)traveling;
            Check("D41-7 运动方程 HUD reproduces the engine's travel (m0 + (m*-m0)·c(u) == MoveX/MoveY) on >= 99 % of 60k random frames",
                bad == null && share >= 0.99 && starts > 500 && traveling > 5000, (bad ?? "") + " exact " + exact + "/" + traveling + " starts=" + starts);
        }

        static void D41Math()
        {
            DashMathText mt = new DashMathText();
            DashTravel t = new DashTravel();
            MoveCurve c = new MoveCurve();
            t.Step(true, 50, -15, 16, c, -30);   // first step: starts at the current value
            t.Step(true, 66, -18, 16, c, -30);
            // DASH41G (DOCCHECK41 D1): m, m0, m* in 百分比偏移 with arrows (PROFILE px -> OffsetUnits.ToPct; X ← → , Y ↑ ↓), t and T
            bool a = mt.UpdateA('X', t) && mt.A[0].Text.StartsWith("m") && mt.A[1].Text == "  = ← 2.50%" && mt.A[2].Text.StartsWith("X: u = t/T = ")
                && mt.A[3].Text.StartsWith("c(u) = ") && Math.Abs(t.Ms - Math.Min(66, c.DurationMs)) < 1e-9 && t.Total == c.DurationMs
                && mt.A[5].Text == "t = " + DashFormat.Fixed(t.Ms / 1000.0, 2) + " s  T = " + DashFormat.Fixed(c.DurationMs / 1000.0, 2) + " s"
                && !mt.UpdateA('X', t);
            a = a && mt.UpdateA('Y', t) && mt.A[1].Text == "  = ↓ 2.50%" && mt.A[2].Text.StartsWith("Y: u = t/T = ");
            DashTravel t2 = new DashTravel();
            t2.Exact = true; t2.Start = 53.064; t2.Target = -30; t2.Move = -30; t2.Total = 200; t2.Ms = 200;
            GlobalSettings g1080 = GlobalSettings.CreateDefault();
            g1080.ResGameW = 1920; g1080.ResGameH = 1080;
            DashMathText m2 = new DashMathText(), m3 = new DashMathText();
            m2.UpdateA('X', t2); m3.UpdateA('X', t2, g1080);
            bool pctA = m2.A[1].Text == "  = ← 4.17%" && m2.A[4].Text == "m0=→ 7.37% m*=← 4.17%" && m2.A[5].Text == "t = 0.20 s  T = 0.20 s"
                && m3.A[1].Text == m2.A[1].Text && m3.A[4].Text == m2.A[4].Text;   // 调参 px -> the same % on every resolution
            t2.Exact = false;
            m2.UpdateA('X', t2);
            pctA = pctA && m2.A[0].Text == DashMathText.AFormulaInexact && m2.A[4].Text == "m* = ← 4.17%";
            Random rp = new Random(4141);
            for (int i = 0; i < 20000 && pctA; i++)
            {
                DashTravel tr = new DashTravel();
                tr.Exact = rp.Next(2) == 0; tr.Start = (rp.NextDouble() - 0.5) * 140; tr.Target = (rp.NextDouble() - 0.5) * 140; tr.Move = (rp.NextDouble() - 0.5) * 140;
                tr.Total = rp.Next(2001); tr.Ms = rp.NextDouble() * tr.Total; tr.U = rp.NextDouble(); tr.C = rp.NextDouble();
                DashMathText mr = new DashMathText();
                mr.UpdateA(rp.Next(2) == 0 ? 'X' : 'Y', tr);
                foreach (DashLine l in mr.A) if (l.Text.Length > 24 || l.Text.IndexOf("px") >= 0 || l.Text.IndexOf('-') >= 0 && l.Text.IndexOf("m*-m0") < 0) pctA = false;
            }
            a = a && pctA;
            bool b = mt.UpdateB(-30, 3, OffsetUnits.ScreenToPct(-30, null), OffsetUnits.ScreenToPct(3, null), 1.0, 720, 75, 75)
                && mt.B[0].Text == DashMathText.BFormula && mt.B[1].Text == "  = ← 4.17%  ↑ 0.42%" && mt.B[2].Text == "v = (-30.00, +3.00)"
                && mt.B[3].Text == "k = 1.000  H/2 = 720" && mt.B[4].Text == "视野换算 枪侧×1.00 另侧×1.00" && mt.B[5].Text.StartsWith("|v| = 4.19%  θ = 174°")
                && !mt.UpdateB(-30, 3, OffsetUnits.ScreenToPct(-30, null), OffsetUnits.ScreenToPct(3, null), 1.0, 720, 75, 75);
            bool zero = mt.UpdateB(0, 0, 0, 0, 1, 720, 75, 50) && mt.B[5].Text == "|v| = 0.00%  θ = —" && mt.B[4].Text == "视野换算 枪侧×1.00 另侧×0.67"; // v5.1.1 (X5, Leo 21 Nov 2026): FOV no longer in k; X only, gun side x1, other side x gameFov/baseFov, Y unchanged.
            // DASH41G: τ's value is ViewScale.Factor's FOV part (same resolution both sides -> Factor == τ exactly), every FOV pair
            // in the allowed range stays at <= 24 chars (the widget is 25 wide), unusable FOVs read 1.00 like Factor's fallback
            bool tau = true;
            for (double fb = ViewScale.MinFov; fb <= ViewScale.MaxFov && tau; fb += 0.5)
                for (double fg = ViewScale.MinFov; fg <= ViewScale.MaxFov && tau; fg += 0.5)
                {
                    // v5.1.1 (X5, Leo 21 Nov 2026): k has no FOV part any more; the line shows the FOV side factors
                    double k = ViewScale.Factor(2560, 1440, "auto", fg, 2560, 1440, "auto", fb, "v");
                    if (k != 1.0) tau = false;
                    DashMathText tm = new DashMathText();
                    tm.UpdateB(0, 0, 0, 0, k, 720, fb, fg);
                    if (tm.B[4].Text.Length > 24 || tm.B[4].Text != FovText.Short(ViewScale.FovRatio(fg, fb))) tau = false;
                }
            tau = tau && DashMathText.TanRatio(75, 0) == 1.0 && DashMathText.TanRatio(double.NaN, 60) == 1.0 && DashMathText.TanRatio(180, 60) == 1.0
                && Math.Abs(DashMathText.TanRatio(50, 75) - 0.6077039716577575) < 1e-12;
            zero = zero && tau;
            bool ang = DashMathText.Angle(1, 0) == 0 && Math.Abs(DashMathText.Angle(0, 1) - 90) < 1e-9 && Math.Abs(DashMathText.Angle(-1, 0) - 180) < 1e-9
                && Math.Abs(DashMathText.Angle(0, -1) - 270) < 1e-9 && DashMathText.Angle(1, -1e-9) < 0.5 && DashMathText.Angle(double.NaN, 1) == 0;
            // decode sweep: spaces kept, ends exactly on the text, null-safe, short buffers
            char[] buf = new char[64];
            string txt = "m = m0+(m*-m0)·c";
            bool sw = true;
            for (int f = 0; f <= 40; f++)
            {
                bool done = DashDecode.Sweep(buf, txt, f / 40.0, f, 4);
                for (int i = 0; i < txt.Length; i++) if (txt[i] == ' ' && buf[i] != ' ') sw = false;
                if (f == 40 && (!done || new string(buf, 0, txt.Length) != txt)) sw = false;
            }
            DashDecode.Sweep(buf, txt, 0.5, 3, 4);
            int scr = 0; for (int i = 0; i < txt.Length; i++) if (buf[i] != txt[i]) scr++;
            try { DashDecode.Sweep(null, txt, 0.5, 1, 4); DashDecode.Sweep(new char[2], txt, double.NaN, 1, 0); DashDecode.Sweep(buf, null, 1, 1, 4); }
            catch (Exception) { sw = false; }
            DashStream st = new DashStream(3);
            for (int i = 0; i < 5; i++) st.Add("s" + i);
            // DASH41G (DOCCHECK41 D2): 计算流 in 百分比偏移 (SCREEN px, UI Y up -> OffsetUnits.ScreenToPct), 0.00 % keeps its column
            bool stream = st.Count == 3 && st.Line(0) == "s2" && st.Line(2) == "s4" && DashStream.Sample(0.4243, -26.19, 3) == "0.42s ← 3.64% ↑ 0.42%"
                && DashStream.Sample(double.NaN, double.NaN, 1e12) == "0.00s   0.00% ↑ 9999.99%" && DashStream.Sample(1.5, 10, -20) == "1.50s → 1.39% ↓ 2.78%"
                && DashStream.Sample(0, -30, 0, g1080) == "0.00s ← 5.56%   0.00%"
                && DashStream.Sample(0.3, -30, 27, null).Length == DashStream.Sample(0.3, 0, 27, null).Length;
            for (int i = 0; i < 20000 && stream; i++)
            {
                string sl = DashStream.Sample(rp.NextDouble() * 99.99, (rp.NextDouble() - 0.5) * 1438, (rp.NextDouble() - 0.5) * 1438, null);
                if (sl.Length > 24 || sl.IndexOf("px") >= 0 || sl.IndexOf('x') >= 0 || sl.IndexOf('-') >= 0 || sl.IndexOf('+') >= 0) stream = false;
            }
            Check("D41-8 HUD maths text: 运动方程 / 换算 lines with the real numbers (τ with its value = ViewScale.Factor's FOV part, <= 24 chars for FOV 50..75), cached (no rebuild when unchanged), angle 0..360, decode sweep, 计算流",
                a && b && zero && ang && sw && scr >= 1 && scr <= 4 && stream,
                a + " " + b + " " + zero + " tau=" + tau + " " + ang + " " + sw + " scr=" + scr + " " + stream + " | " + mt.A[0].Text + " | " + mt.B[4].Text + " | " + mt.B[5].Text);
        }

        static void D41TryLines()
        {
            // brute-render P2-3: long custom bindings go onto extra try lines (<= TryLineEm each) and log names are capped
            Random rnd = new Random(4444);
            int[] vks = { 0x01, 0x02, 0x04, 0x05, 0x06, 0x14, 0x20, 0x41, 0x45, 0x51, 0x30, 0x39, 0x60, 0x6A, 0x70, 0x7B, 0x90, 0x91, 0xA0, 0xA2, 0xA4, 0xC0, 0xDE, 0x2D, 0x24, 0x13, 0x26, 0x6F };
            int[] acts = { KeyBindings.ActionLeanLeft, KeyBindings.ActionLeanRight, KeyBindings.ActionJump, KeyBindings.ActionFire, KeyBindings.ActionAds };
            string bad = null;
            int multi = 0;
            for (int it = 0; it < 30000 && bad == null; it++)
            {
                KeyBindings k = new KeyBindings();
                foreach (int a in acts)
                {
                    k.Set(a, vks[rnd.Next(vks.Length)]);
                    k.SetMods(a, rnd.Next(4) == 0 ? rnd.Next(8) : 0);
                }
                string[] lines = DashKeyNames.TryLines(k);
                if (lines.Length < 2 || lines.Length > 6) { bad = "line count " + lines.Length; break; }
                if (lines.Length > 2) multi++;
                List<string> parts = new List<string>();
                foreach (string l in lines)
                {
                    if (!l.StartsWith("> ")) { bad = "no prompt: " + l; break; }
                    string[] ps = l.Substring(2).Split(new string[] { " | " }, StringSplitOptions.None);
                    if (ps.Length > 1 && DashKeyNames.EstimateEm(l) > DashKeyNames.TryLineEm) { bad = "too wide (" + F(DashKeyNames.EstimateEm(l)) + " em): " + l; break; }
                    parts.AddRange(ps);
                }
                if (bad != null) break;
                if (parts.Count != 6 || !parts[0].StartsWith("试一试：") || !parts[1].EndsWith(" 侧身") || !parts[2].EndsWith(" 跳跃")
                    || !parts[3].EndsWith(" 开火") || !parts[4].EndsWith(" 开镜") || parts[5] != "Shift+W 冲刺") { bad = "parts: " + string.Join(" / ", lines); break; }
                foreach (string n in DashKeyNames.LogNames(k))
                    if (string.IsNullOrEmpty(n) || n.Length > DashKeyNames.MaxLogName) { bad = "log name '" + n + "'"; break; }
            }
            KeyBindings lk = new KeyBindings();
            lk.Set(KeyBindings.ActionLeanLeft, 0x91); lk.SetMods(KeyBindings.ActionLeanLeft, KeyBindings.ModCtrl | KeyBindings.ModShift | KeyBindings.ModAlt);
            lk.Set(KeyBindings.ActionLeanRight, 0x90); lk.SetMods(KeyBindings.ActionLeanRight, KeyBindings.ModCtrl | KeyBindings.ModShift);
            lk.Set(KeyBindings.ActionJump, 0x30); lk.SetMods(KeyBindings.ActionJump, KeyBindings.ModCtrl | KeyBindings.ModAlt);
            string[] rl = DashKeyNames.TryLines(lk);   // the brute-render repro: was one ~694 px line
            bool repro = rl.Length >= 3;
            foreach (string l in rl) if (l.Split('|').Length > 1 && DashKeyNames.EstimateEm(l) > DashKeyNames.TryLineEm) repro = false;
            Check("D41-9 try lines wrap long bindings (30k random sets: <= " + F(DashKeyNames.TryLineEm) + " em per line, all parts in order), log names <= "
                + DashKeyNames.MaxLogName + " chars (brute-render P2-3)", bad == null && repro && multi > 0, (bad ?? "") + " multi=" + multi + " repro=" + string.Join(" / ", rl));
        }

        static bool HasEnglishWord(string s)
        {
            // a run of 3+ ASCII letters that is not a key name or maths token
            string[] ok = { "Shift", "Space", "LMB", "RMB", "MMB", "Ctrl", "Alt", "Caps", "Num", "Left", "Right", "Up", "Down", "LShift", "RShift", "LCtrl", "RCtrl", "LAlt", "RAlt", "tan" };
            int i = 0;
            while (i < s.Length)
            {
                if (!char.IsLetter(s[i]) || s[i] > 0x7E) { i++; continue; }
                int j = i;
                while (j < s.Length && s[j] <= 0x7E && char.IsLetter(s[j])) j++;
                string w = s.Substring(i, j - i);
                if (w.Length >= 3 && Array.IndexOf(ok, w) < 0) return true;
                i = j;
            }
            return false;
        }

        static void D41Words()
        {
            // Leo: no English in user text except key names (v4.0 had IDLE, LEAN-L, JUMP, FIRE, ADS)
            List<string> all = new List<string>();
            all.AddRange(new string[] { DashFormat.NoKeys, DashEvents.HandSwitched, DashEvents.HandNormal, DashEvents.BoostOn, DashEvents.BoostOff,
                DashEvents.AdsOn, DashEvents.AdsOff, DashKeyNames.HintLine(null), DashKeyNames.HintLine(new KeyBindings()) });
            all.AddRange(DashKeyNames.CapFallback);
            all.AddRange(DashKeyNames.LogFallback);
            all.AddRange(DashKeyNames.TryLines(new KeyBindings()));
            all.AddRange(DashKeyNames.TryLines(null));
            all.Add(DashFormat.StateLine(DashKeys.W | DashKeys.Shift | DashKeys.Lmb, null, -3, 4, 120));
            // DASH41G: the 侧身 row, the 计算流 line and every 运动方程 / 换算 line (maths symbols only: m, u, t, T, c, v, k, H, τ, θ, tan)
            all.AddRange(new string[] { DashFormat.LeanCentre, DashFormat.LeanLeft, DashFormat.LeanRight, DashFormat.LeanLeftMoving, DashFormat.LeanRightMoving,
                DashStream.Sample(0.42, -26.19, 3), DashMathText.AFormula, DashMathText.AFormulaInexact, DashMathText.BFormula });
            DashMathText wm = new DashMathText();
            DashTravel wt = new DashTravel();
            wt.Step(true, 40, -12, 16, new MoveCurve(), -30);
            wm.UpdateA('X', wt); wm.UpdateB(-30, 3, -4.17, 0.42, 1, 720, 75, 60);
            foreach (DashLine l in wm.A) all.Add(l.Text);
            foreach (DashLine l in wm.B) all.Add(l.Text);
            wt.Exact = false; wm.UpdateA('Y', wt);
            foreach (DashLine l in wm.A) all.Add(l.Text);
            string bad = null;
            foreach (string s in all) if (HasEnglishWord(s)) { bad = s; break; }
            Check("D41-10 dashboard texts from DashboardLogic: no English words except key names", bad == null, bad ?? "");
        }

        static void D41Alloc()
        {
            // the v4.1 per-frame pure path allocates nothing while the shown values stay the same
            DashPct p = new DashPct();
            DashMathText mt = new DashMathText();
            DashTravel t = new DashTravel();
            DashDirty d = new DashDirty();
            DashRange r = new DashRange();
            MoveCurve c = new MoveCurve();
            DashRect[] rects = new DashRect[DashHud.Count];
            int[] lines = new int[DashHud.Count];
            int[] need = { 180, 190, 190, 140 }, want = { 5, 6, 5, 4 }, min = { 3, 3, 3, 3 };
            DashRect inner = new DashRect(10, 10, 1600, 900), plate = new DashRect(700, 500, 90, 25);
            p.Set(-4.17, OffsetAxis.X);
            t.Step(false, 0, -30, 16, c, -30);
            mt.UpdateA('X', t); mt.UpdateB(-30, 3, -4.17, 0.42, 1, 720, 75, 75);
            r.SetFloor(5);
            long before = GC.GetAllocatedBytesForCurrentThread();
            double sink = 0;
            for (int f = 0; f < 10000; f++)
            {
                p.Set(-4.1701, OffsetAxis.X);
                t.Step(false, 0, -30, 16, c, -30);
                mt.UpdateA('X', t);
                mt.UpdateB(-30, 3, -4.17, 0.42, 1, 720, 75, 75);
                r.Update(4.0, f * 16.0);
                d.Clear();
                d.Add(new DashRect(f % 50, 3, 40, 40)); d.Add(new DashRect(300, 300, 20, 20)); d.Add(new DashRect(310, 305, 30, 30));
                DashHud.Place(inner, 810, 455, 30, 25, 40, 6, 22, 18, 5, need, want, min, plate, rects, lines);
                sink += d.Count + lines[0] + t.C;
            }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("D41-11 no allocation in the v4.1 steady-state path (DashPct, HUD maths, travel, range, dirty rects, HUD placement; 10k frames)",
                used == 0, "bytes=" + used + " sink=" + F(sink % 7));
        }
    }
}
