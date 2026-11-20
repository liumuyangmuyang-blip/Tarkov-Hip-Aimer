using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    // v4.0 首页 dashboard: the pure pieces in src/DashboardLogic.cs (trail buffer, terminal log, events, number cache,
    // chart scale, decode, formatting). Self-registers through a module initializer, so Tests.cs Main needs no edit.
    // Do NOT also call V4DashTests() from Main (it would run twice).
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V4DashInit() { V4DashTests(); }

        static void V4DashTests()
        {
            DashFormatTests();
            DashKeysTests();
            DashTextTests();
            DashTrailTests();
            DashLogTests();
            DashEventsTests();
            DashScaleTests();
            DashDecodeTests();
            DashAllocTests();
            DashEngineTests();
            DashLayoutTests();
            DashKeyNameTests();
            DashFix2Tests();
        }

        static void DashFix2Tests()
        {
            // DB26: Shift cap is placed in both cap layouts and the block sits right under the card title (no floating)
            DashLayout L = new DashLayout();
            string bad = null; int placed = 0, two = 0, one = 0;
            Random r = new Random(26);
            for (int i = 0; i < 20000 && bad == null; i++)
            {
                int w = 300 + r.Next(3500), h = 200 + r.Next(2200);
                double s = new double[] { 1, 1.25, 1.5, 1.75, 2 }[r.Next(5)];
                double z = 1.0; // v4.3.1: fixed text size
                L.Arrange(w, h, s, z, DashMetrics.Estimate(s * z));
                if (!L.CapsShown) continue;
                placed++; if (L.CapsTwoRows) two++; else one++;
                DashRect sh = L.CapRects[9];
                if (sh.IsEmpty || !L.CapsInner.Contains(sh)) bad = "Shift cap missing/outside at " + w + "x" + h + "@" + s;
                int topCap = int.MaxValue;
                for (int k = 0; k < DashKeys.Count; k++) topCap = Math.Min(topCap, L.CapRects[k].Y);
                if (topCap - L.CapsInner.Y > (int)Math.Round(6 * s * z) + 1) bad = "caps float " + (topCap - L.CapsInner.Y) + " px below the title at " + w + "x" + h + "@" + s;
            }
            Check("DB26 Shift cap placed in both cap layouts, caps block top-aligned under its title (20k random sizes)",
                bad == null && placed > 1000 && two > 100 && one > 100, (bad ?? "") + " placed=" + placed + " two=" + two + " one=" + one);

            // DB27: 冲刺 = Shift + W only (Shift alone = 屏息, W alone = walk)
            bool sp = DashKeys.Sprinting(DashKeys.W | DashKeys.Shift) && DashKeys.Sprinting(DashKeys.W | DashKeys.Shift | DashKeys.D)
                && !DashKeys.Sprinting(DashKeys.Shift) && !DashKeys.Sprinting(DashKeys.W) && !DashKeys.Sprinting(DashKeys.S | DashKeys.Shift);
            InputState i2 = new InputState(); i2.Shift = true;
            Check("DB27 冲刺 rule = Shift + W; the Shift key shows on its cap", sp && DashKeys.FromInput(i2) == DashKeys.Shift, "");
        }

        static void DashFormatTests()
        {
            bool ok = DashFormat.Signed1(12) == "+12.0" && DashFormat.Signed1(-3.44) == "-3.4" && DashFormat.Signed1(-0.04) == "+0.0"
                && DashFormat.Signed1(0.05) == "+0.1" && DashFormat.Signed1(-0.05) == "-0.1" && DashFormat.Signed1(double.NaN) == "+0.0"
                && DashFormat.Signed1(double.PositiveInfinity) == "+0.0" && DashFormat.Signed1(1e12) == "+9999999.9"
                && DashFormat.Signed1(-1e12) == "-9999999.9" && DashFormat.Signed1(-12.35) == "-12.4";
            Check("DB1 Signed1 rounding / sign / NaN / clamp", ok, DashFormat.Signed1(-0.04) + " " + DashFormat.Signed1(-12.35));

            // brute force: Signed1 == invariant "+0.0;-0.0" formatting of the rounded tenths, never "-0.0"
            bool all = true; string bad = "";
            Random r = new Random(7);
            for (int i = 0; i < 200000 && all; i++)
            {
                double v = (r.NextDouble() - 0.5) * 2000.0;
                if (i % 3 == 0) v = Math.Round(v, 2);
                string got = DashFormat.Signed1(v);
                long t = (long)Math.Round(v * 10.0, MidpointRounding.AwayFromZero);
                string want = (t < 0 ? "-" : "+") + (Math.Abs(t) / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
                if (got != want || got == "-0.0") { all = false; bad = v.ToString("R") + " got " + got + " want " + want; }
            }
            Check("DB2 Signed1 brute force 200k values", all, bad);

            string[] cn = new string[] { "W", "A", "S", "D", "Q", "Ctrl+D", "M4", "LMB", "ADS", "Shift" };
            // v4.1: X / Y in 百分比偏移 (OffsetUnits, % of half the picture height; 1440 reference without settings: 1 % = 7.2 px)
            ok = DashFormat.StateLine(DashKeys.D, null, -30, 3, 700) == "> D  X ← 4.17%  Y ↑ 0.42%  0.70 s"
                && DashFormat.StateLine(DashKeys.W | DashKeys.Shift, null, 0, 12, 2040) == "> W+Shift 冲刺  X 0.00%  Y ↑ 1.67%  2.04 s"
                && DashFormat.StateLine(DashKeys.Shift, null, 0, 0, 5) == "> Shift  X 0.00%  Y 0.00%  0.01 s"     // Shift alone is not 冲刺
                && DashFormat.StateLine(DashKeys.E | DashKeys.Lmb, cn, 1, 2, 1e9) == "> Ctrl+D+LMB  X → 0.14%  Y ↑ 0.28%  999.99 s"
                && DashFormat.StateLine(DashKeys.Space, null, 1, 2, double.NaN) == "> Space  X → 0.14%  Y ↑ 0.28%  0.00 s"
                && DashFormat.Seconds2(-3) == "0.00 s" && DashFormat.Seconds2(1234.5) == "1.23 s" && DashFormat.Seconds2(1235) == "1.24 s";
            Check("DB3 log line = the ENDED state with the X / Y it ended on (百分比偏移) and its duration ('> D  X ← 4.17%  Y ↑ 0.42%  0.70 s'), bound names, 冲刺",
                ok, DashFormat.StateLine(DashKeys.D, null, -30, 3, 700) + " | " + DashFormat.StateLine(DashKeys.E | DashKeys.Lmb, cn, 1, 2, 1e9));

            ok = DashFormat.Megabytes((long)(18.4 * 1024 * 1024)) == "18.4 MB" && DashFormat.Megabytes(0) == "0.0 MB"
                && DashFormat.Megabytes(-5) == "0.0 MB" && DashFormat.Megabytes(1024L * 1024 * 1024) == "1024.0 MB";
            Check("DB4 memory text '18.4 MB'", ok, DashFormat.Megabytes((long)(18.4 * 1024 * 1024)));

            all = true; bad = "";
            for (int m = 0; m < (1 << DashKeys.Count) && all; m++)
            {
                string c = DashFormat.KeyCombo(m);
                int back = 0;
                if (c != DashFormat.NoKeys)
                {
                    string[] parts = c.Split('+');
                    int lastIdx = -1;
                    foreach (string p in parts)
                    {
                        int idx = Array.IndexOf(DashKeys.Names, p);
                        if (idx < 0 || idx <= lastIdx) { all = false; bad = "mask " + m + " -> " + c; break; }
                        lastIdx = idx;
                        back |= DashKeys.Bits[idx];
                    }
                }
                if (all && back != m) { all = false; bad = "mask " + m + " -> " + c; }
            }
            Check("DB5 key combo text: all 1024 masks round-trip, fixed order W A S D Q E Space LMB RMB Shift", all && DashFormat.KeyCombo(0) == DashFormat.NoKeys && DashFormat.NoKeys == "空闲"
                && DashFormat.KeyCombo(DashKeys.E | DashKeys.W, new string[] { "W", "A", "S", "D", "Q", "Ctrl+D", "", "", "", "" }) == "W+Ctrl+D"
                && DashFormat.KeyCombo(DashKeys.Space, new string[2]) == "Space", bad);
        }

        static void DashKeysTests()
        {
            bool all = true; string bad = "";
            for (int m = 0; m < (1 << DashKeys.Count) && all; m++)
            {
                InputState i = new InputState();
                i.Fwd = (m & DashKeys.W) != 0; i.Left = (m & DashKeys.A) != 0; i.Back = (m & DashKeys.S) != 0; i.Right = (m & DashKeys.D) != 0;
                i.LeanL = (m & DashKeys.Q) != 0; i.LeanR = (m & DashKeys.E) != 0; i.Jump = (m & DashKeys.Space) != 0;
                i.Fire = (m & DashKeys.Lmb) != 0; i.Ads = (m & DashKeys.Rmb) != 0;
                i.Shift = (m & DashKeys.Shift) != 0;
                i.FreeLook = true; i.HandSwitch = true; i.Boost = true; i.Cancel = true; i.Mod = true; // not caps
                if (DashKeys.FromInput(i) != m) { all = false; bad = "mask " + m; }
            }
            bool distinct = true; int seen = 0;
            foreach (int b in DashKeys.Bits) { if ((seen & b) != 0) distinct = false; seen |= b; }
            Check("DB6 key caps: every InputState combo maps to its mask (other keys ignored)", all && distinct && DashKeys.Bits.Length == DashKeys.Count && DashKeys.Names.Length == DashKeys.Count, bad);
        }

        static void DashTextTests()
        {
            DashText t = new DashText();
            bool a = t.Signed1(12.04, "", " px");
            string first = t.Text;
            bool b = t.Signed1(12.0, "", " px");      // same tenths -> no rebuild
            bool same = ReferenceEquals(first, t.Text);
            bool c = t.Signed1(12.06, "", " px");     // 12.1
            t.Invalidate();
            bool d = t.Signed1(12.06, "", " px");
            DashText m = new DashText();
            bool e = m.Megabytes((long)(18.4 * 1024 * 1024), "内存 ", "");
            DashText f = new DashText();
            f.Int(60, "FPS ", "");
            bool ok = a && !b && same && c && d && first == "+12.0 px" && t.Text == "+12.1 px" && e && m.Text == "内存 18.4 MB" && f.Text == "FPS 60"
                && !f.Int(60, "FPS ", "");
            Check("DB7 cached texts rebuild only when the shown number changes", ok, first + " | " + t.Text + " | " + m.Text);
        }

        static void DashTrailTests()
        {
            DashTrail tr = new DashTrail(4, 1000);
            tr.Add(0, 0, 0, 0.2); tr.Add(10, 0.1, 0, 0.2); // second too close
            bool skip = tr.Count == 1;
            tr.Add(20, 1, 0, 0.2); tr.Add(30, 2, 0, 0.2); tr.Add(40, 3, 0, 0.2); tr.Add(50, -9, 4, 0.2); // overflow drops (0,0)
            double x, y, ts;
            tr.Get(0, out x, out y, out ts);
            bool overflow = tr.Count == 4 && x == 1 && ts == 20;
            tr.Get(3, out x, out y, out ts);
            bool newest = x == -9 && y == 4 && tr.AbsPeak() == 9;
            tr.Trim(1035); // age(20)=1015, age(30)=1005 -> dropped; 40 and 50 kept
            bool trim = tr.Count == 2;
            bool nan = !tr.Add(60, double.NaN, 0, 0.2) && !tr.Add(60, 0, double.PositiveInfinity, 0.2) && tr.Count == 2;
            tr.Trim(5000);
            bool empty = tr.Count == 0 && tr.AbsPeak() == 0 && !tr.Add(6000, -9, 4, 0.2) && tr.Count == 0; // still ring: stays empty
            tr.Clear();
            empty = empty && tr.Add(6100, -9, 4, 0.2) && tr.Count == 1;
            bool threw = false;
            try { tr.Get(1, out x, out y, out ts); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check("DB8 trail: min step, overflow keeps newest, age trim, NaN rejected, bounds", skip && overflow && newest && trim && nan && empty && threw,
                "skip=" + skip + " overflow=" + overflow + " newest=" + newest + " trim=" + trim + " nan=" + nan + " empty=" + empty);

            // brute force against a List model
            Random r = new Random(11);
            DashTrail t2 = new DashTrail(37, 300);
            List<double[]> model = new List<double[]>();
            double[] lastAdd = null;
            double now = 0; bool all = true; string bad = "";
            for (int i = 0; i < 100000 && all; i++)
            {
                now += r.NextDouble() * 20;
                double px = Math.Round((r.NextDouble() - 0.5) * 100, 1), py = Math.Round((r.NextDouble() - 0.5) * 100, 1);
                if (r.Next(5) == 0 && lastAdd != null) { px = lastAdd[1]; py = lastAdd[2]; }
                if (r.Next(200) == 0) { t2.Clear(); model.Clear(); lastAdd = null; }
                bool added = t2.Add(now, px, py, 0.2);
                bool want = lastAdd == null || Math.Pow(px - lastAdd[1], 2) + Math.Pow(py - lastAdd[2], 2) >= 0.04;
                if (added != want) { all = false; bad = "add mismatch at " + i; break; }
                if (added) { lastAdd = new double[] { now, px, py }; model.Add(lastAdd); if (model.Count > 37) model.RemoveAt(0); }
                if (r.Next(3) == 0) { t2.Trim(now); while (model.Count > 0 && now - model[0][0] > 300) model.RemoveAt(0); }
                if (t2.Count != model.Count) { all = false; bad = "count " + t2.Count + " vs " + model.Count + " at " + i; break; }
                for (int k = 0; k < model.Count; k++)
                {
                    t2.Get(k, out x, out y, out ts);
                    if (x != model[k][1] || y != model[k][2] || ts != model[k][0]) { all = false; bad = "item " + k + " at " + i; break; }
                }
            }
            Check("DB9 trail brute force: 100k random adds/trims match a list model", all, bad);
        }

        static void DashLogTests()
        {
            DashLog log = new DashLog(5);
            log.CharsPerSec = 100; // 10 ms per char
            log.MaxQueueMs = 250;
            log.Add("abcdefghij", 0);                 // 0..100 ms
            bool v0 = log.Visible(0, 0) == 0 && log.Visible(0, 55) == 5 && log.Visible(0, 100) == 10 && log.Visible(0, 5000) == 10;
            log.Add("0123", 50);                      // queued: starts at 100
            bool queued = log.Visible(1, 99) == 0 && log.Visible(1, 120) == 2 && log.Typing(120) && !log.Typing(140);
            log.Add(new string('x', 40), 140);        // ends at 540
            log.Add("y", 150);                        // would wait 390 ms > 250 -> previous lines complete at once
            bool snap = log.Visible(2, 150) == 40 && log.Visible(0, 150) == 10 && log.Visible(3, 150) == 0 && log.Visible(3, 160) == 1;
            log.Add("5", 1000); log.Add("6", 1000);   // 6 lines into capacity 5
            bool cap = log.Count == 5 && log.Line(0) == "0123" && log.Line(4) == "6";
            int ver = log.Version;
            log.Add(null, 2000);
            bool nul = log.Line(4) == "" && log.Version != ver && log.Visible(99, 0) == 0 && log.Visible(-1, 0) == 0;
            log.Clear();
            bool clr = log.Count == 0 && !log.Typing(0);
            Check("DB10 terminal log: typing speed, queue, burst snap, cap, null, clear", v0 && queued && snap && cap && nul && clr,
                "v0=" + v0 + " queued=" + queued + " snap=" + snap + " cap=" + cap + " nul=" + nul + " clr=" + clr);

            // brute force invariants under random bursts
            Random r = new Random(3);
            DashLog b = new DashLog(48);
            double now = 0; bool all = true; string bad = ""; string lastAdded = null;
            for (int i = 0; i < 50000 && all; i++)
            {
                now += r.Next(4) == 0 ? r.NextDouble() * 400 : r.NextDouble() * 5;
                if (r.Next(2) == 0) { lastAdded = new string('z', r.Next(0, 60)); b.Add(lastAdded, now); }
                if (b.Count > 48) { all = false; bad = "count " + b.Count; }
                for (int k = 0; k < b.Count && all; k++)
                {
                    int v = b.Visible(k, now);
                    if (v < 0 || v > b.Line(k).Length) { all = false; bad = "visible " + v; }
                    if (k < b.Count - 1 && v < b.Line(k).Length && b.Visible(k + 1, now) > 0) { all = false; bad = "line " + (k + 1) + " typed before " + k + " finished"; }
                }
                if (lastAdded != null && !ReferenceEquals(b.Line(b.Count - 1), lastAdded)) { all = false; bad = "newest line"; }
                // backlog bound: the newest line finishes within MaxQueueMs + its own length after it was added
            }
            Check("DB11 terminal log brute force: 50k random bursts keep order, cap and bounds", all, bad);

            // backlog bound: a burst of 1000 lines 1 ms apart never makes the newest line wait more than MaxQueueMs
            DashLog q = new DashLog(48);
            q.CharsPerSec = 160;
            bool bound = true; double worst = 0;
            for (int i = 0; i < 1000; i++)
            {
                double t = i;
                q.Add("> W+D  dx=+12.0 dy=-3.4  t=73ms", t);
                // the newest starts typing by t + MaxQueueMs + 1 frame
                int startVisible = q.Visible(q.Count - 1, t + q.MaxQueueMs + 7);
                if (startVisible == 0) bound = false;
            }
            Check("DB12 terminal log: a 1000-line burst never queues the newest line past 250 ms", bound, "worst=" + F(worst));
        }

        static void DashEventsTests()
        {
            DashLog log = new DashLog(48);
            log.CharsPerSec = 1e9;
            DashEvents ev = new DashEvents();
            int n0 = ev.Step(0, false, false, false, 0, 0, 0, log);                     // first step: no line
            int n1 = ev.Step(DashKeys.W | DashKeys.D, false, false, false, 1, -1, 73, log);  // idle ended: no line
            int n2 = ev.Step(DashKeys.W | DashKeys.D, false, false, false, 12, -3.4, 90, log); // same keys: nothing
            int n3 = ev.Step(DashKeys.W | DashKeys.D, true, true, true, 12, -3.4, 100, log);  // hand + boost + ads flip
            int n4 = ev.Step(0, true, true, false, 9, -2, 773, log);   // W+D ended at the previous frame's +12.0 / -3.4 after 700 ms
            bool ok = n0 == 0 && n1 == 0 && n2 == 0 && n3 == 3 && n4 == 2 && log.Count == 5
                && log.Line(0) == DashEvents.HandSwitched && log.Line(1) == DashEvents.BoostOn && log.Line(2) == DashEvents.AdsOn
                && log.Line(3) == "> W+D  X → 1.67%  Y ↓ 0.47%  0.70 s" && log.Line(4) == DashEvents.AdsOff;
            ev.Reset();
            int n5 = ev.Step(DashKeys.Q, false, false, false, 0, 0, 900, log);
            ev.Names = new string[] { "W", "A", "S", "D", "Q", "Ctrl+D", "Space", "LMB", "RMB", "Shift" };
            int n6 = ev.Step(DashKeys.E, false, false, false, 5, 5, 1000, log);   // Q ended (100 ms)
            int n7 = ev.Step(0, false, false, false, 0, 0, 1500, log);            // Ctrl+D ended (500 ms)
            bool named = n6 == 1 && n7 == 1 && log.Line(5) == "> Q  X 0.00%  Y 0.00%  0.10 s" && log.Line(6) == "> Ctrl+D  X → 0.69%  Y ↑ 0.69%  0.50 s";
            Check("DB13 events: the ended key state is logged with its last X/Y + duration (agrees with the data card), no idle line, bound names, reset = silent first step",
                ok && n5 == 0 && named, n0 + "," + n1 + "," + n2 + "," + n3 + "," + n4 + "," + n5 + " " + (log.Count > 3 ? log.Line(3) : "") + " | " + (log.Count > 6 ? log.Line(6) : ""));

            // brute force: number of key lines == number of mask changes
            Random r = new Random(5);
            DashEvents e2 = new DashEvents();
            DashLog l2 = new DashLog(100000);
            int prev = -1, changes = 0, flips = 0; bool ph = false, pb = false, pa = false;
            for (int i = 0; i < 20000; i++)
            {
                int m = r.Next(6) == 0 ? (r.Next(4) == 0 ? 0 : r.Next(1 << DashKeys.Count)) : (prev < 0 ? 0 : prev);
                bool h = r.Next(50) == 0 ? !ph : ph, bo = r.Next(70) == 0 ? !pb : pb, a = r.Next(30) == 0 ? !pa : pa;
                int got = e2.Step(m, h, bo, a, r.NextDouble() * 50, r.NextDouble() * 50, i * 7.0, l2);
                if (prev >= 0) { if (m != prev && prev != 0) changes++; if (h != ph) flips++; if (bo != pb) flips++; if (a != pa) flips++; }
                prev = m; ph = h; pb = bo; pa = a;
            }
            Check("DB14 events brute force: 20k steps, lines == ended non-idle key states + flips", l2.Count == changes + flips, l2.Count + " vs " + (changes + flips));
        }

        static void DashScaleTests()
        {
            bool mono = true; int last = 0;
            for (double p = 0; p <= 400; p += 0.01)
            {
                int rg = DashRange.RungFor(p);
                if (rg < last) mono = false;
                last = rg;
                if (p * DashRange.Headroom <= DashRange.Ladder[DashRange.Ladder.Length - 1] && DashRange.Ladder[rg] < p * DashRange.Headroom) mono = false;
            }
            bool steps = true;
            for (int i = 0; i < DashRange.Ladder.Length; i++)
            {
                double k = DashRange.Ladder[i] / DashRange.Steps[i];
                if (k < 2 || k > 6 || (i > 0 && DashRange.Ladder[i] <= DashRange.Ladder[i - 1])) steps = false;
            }
            Check("DB15 chart range ladder (百分比偏移): monotonic, always holds peak x 1.15, 2-6 grid lines per half axis",
                mono && steps && DashRange.RungFor(double.NaN) == 0 && DashRange.RungFor(-5) == 0 && DashRange.RungFor(1e9) == DashRange.Ladder.Length - 1, "");

            // v4.1: the profile's range is a floor (the same offset always looks the same; v4.0 zoomed in below it between
            // moves); a 跳跃 / 鼠标滞后 swing grows it at once and it goes back one rung per 2.5 s, never below the floor
            DashRange sc = new DashRange();
            bool floor = sc.SetFloor(4) && sc.Range == 5;                    // 4 x 1.15 = 4.6 -> 5
            bool grow = sc.Update(10, 0) && sc.Range == 12;                   // 11.5 -> 12, at once
            bool hold1 = !sc.Update(0.1, 100) && sc.Range == 12;              // starts the shrink timer
            bool hold2 = !sc.Update(0.1, 2500) && sc.Range == 12;             // 2400 ms < 2500
            bool shrink = sc.Update(0.1, 2600) && sc.Range == 8;              // one rung at a time
            bool toFloor = sc.Update(0.1, 5100) && sc.Range == 5;
            bool atFloor = !sc.Update(0, 9000) && !sc.Update(0, 20000) && sc.Range == 5;   // never below the profile's range
            bool regrow = sc.Update(10, 20100) && sc.Range == 12;
            sc.Reset();
            bool reset = sc.Range == 5;
            bool refloor = sc.SetFloor(0) && sc.Range == 3 && !sc.SetFloor(1) && sc.Range == 3;
            Check("DB16 chart range: profile floor, grows at once, back to the floor one rung per 2.5 s (never below), Reset / SetFloor",
                floor && grow && hold1 && hold2 && shrink && toFloor && atFloor && regrow && reset && refloor,
                floor + " " + grow + " " + hold1 + " " + hold2 + " " + shrink + " " + toFloor + " " + atFloor + " " + regrow + " " + reset + " " + refloor);
        }

        static void DashDecodeTests()
        {
            string target = "TARKOV HIP AIMER  //  实时仪表盘 DASHBOARD";
            char[] buf = new char[target.Length];
            bool all = true; string bad = "";
            for (int f = 0; f <= 100 && all; f++)
            {
                double prog = f / 100.0;
                bool done = DashDecode.Fill(buf, target, prog, f);
                for (int i = 0; i < target.Length; i++)
                    if (target[i] == ' ' && buf[i] != ' ') { all = false; bad = "space " + i + " at " + prog; }
                if (prog >= 1 && (!done || new string(buf) != target)) { all = false; bad = "final"; }
            }
            DashDecode.Fill(buf, target, 0, 3);
            int scrambled = 0;
            for (int i = 0; i < target.Length; i++) if (target[i] != ' ' && buf[i] != target[i]) scrambled++;
            bool safe = true;
            try { DashDecode.Fill(null, target, 0.5, 1); DashDecode.Fill(new char[3], target, double.NaN, 1); DashDecode.Fill(new char[99], target, 2.0, 1); DashDecode.Fill(buf, null, 0.5, 1); }
            catch (Exception) { safe = false; }
            Check("DB17 title decode: spaces kept, ends exactly on the title, starts scrambled, null-safe", all && scrambled > target.Length / 2 && safe, bad + " scrambled=" + scrambled);
        }

        static void DashAllocTests()
        {
            // steady state (nothing changes): the per-frame calls allocate nothing
            DashTrail tr = new DashTrail(160, 1200);
            DashLog log = new DashLog(48);
            for (int i = 0; i < 48; i++) log.Add("> W+D  dx=+12.0 dy=-3.4  t=73ms", i);
            DashText tx = new DashText();
            DashRange sc = new DashRange();
            DashEvents ev = new DashEvents();
            tx.Signed1(5, "", " px");
            ev.Step(DashKeys.W, false, false, false, 1, 1, 0, log);
            for (int i = 0; i < 100; i++) tr.Add(i * 16, i, i, 0.2);
            long before = GC.GetAllocatedBytesForCurrentThread();
            double sink = 0;
            for (int f = 0; f < 10000; f++)
            {
                double now = 2000 + f * 16;
                tr.Add(now, 99, 99, 0.2);  // same point: skipped
                tr.Trim(now - 900);
                sink += tr.AbsPeak();
                for (int k = 0; k < tr.Count; k++) { double x, y, t; tr.Get(k, out x, out y, out t); sink += x; }
                for (int k = 0; k < log.Count; k++) sink += log.Visible(k, now) + log.Line(k).Length;
                sink += log.Typing(now) ? 1 : 0;
                tx.Signed1(5.01, "", " px");
                sc.Update(4.2, now);
                ev.Step(DashKeys.W, false, false, false, 1, 1, now, log);
            }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("DB18 no allocation in the steady-state frame path (10k frames)", used == 0, "bytes=" + used + " sink=" + F(sink % 7));
        }

        static string TrailDump(DashTrail tr, double now)
        {
            string r = "";
            for (int i = 0; i < tr.Count; i++) { double x, y, t; tr.Get(i, out x, out y, out t); r += " (" + F(x) + "," + F(y) + " age " + F(now - t) + ")"; }
            return r;
        }

        static void DashEngineTests()
        {
            // The panel's data path: real MotionEngine + raw input -> mask/events/trail. Holding D moves the ring right;
            // pressing and releasing keys logs one line per change; the trail fills while moving and empties when still.
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile p = g.Active;
            MotionEngine e = new MotionEngine();
            DashTrail tr = new DashTrail(160, 1200);
            DashLog log = new DashLog(48);
            log.CharsPerSec = 1e9;
            DashEvents ev = new DashEvents();
            LiveInputFilter filter = new LiveInputFilter();
            double now = 0, dt = 1.0 / 64;
            string[] script = { "", "D", "D", "D", "WD", "WD", "", "", "", "", "", "" }; // 6 s still at the end (slow curve tails)
            int maxTrail = 0, lines0 = 0;
            double xAfterD = double.NaN, dEndX = double.NaN, dEndY = double.NaN;
            for (int seg = 0; seg < script.Length; seg++)
            {
                for (int f = 0; f < 64; f++)
                {
                    InputState raw = new InputState();
                    raw.Fwd = script[seg].Contains("W"); raw.Right = script[seg].Contains("D");
                    e.GameFocused = true; e.OwnForeground = true;
                    e.Update(dt, filter.Next(raw, true, true), p, g, PreviewMode.Live, 1.0);
                    now += dt * 1000;
                    double x = e.OffsetX, y = -e.OffsetY;
                    tr.Add(now, x, y, 0.2); tr.Trim(now);
                    if (tr.Count > maxTrail) maxTrail = tr.Count;
                    int mask = DashKeys.FromInput(raw);
                    if (seg == 0 && f == 0) lines0 = log.Count;
                    ev.Step(mask, e.LiveSwitched, e.LiveBoost, e.AdsToggled, x, y, now, log);
                    if (seg == 3 && f == 63) { dEndX = x; dEndY = y; }
                }
                if (seg == 3) xAfterD = e.Debug.UiOffsetX;
            }
            // D held 3 s then W+D 2 s: two lines, each with the ring offset the state ended on (= what the data card showed)
            bool lines = log.Count == 2 && log.Line(0).StartsWith("> D  X ") && log.Line(0).EndsWith("3.00 s")
                && log.Line(1).StartsWith("> W+D  X ") && log.Line(1).EndsWith("2.00 s")
                && log.Line(0).Contains("X " + OffsetUnits.FormatPct(OffsetUnits.ScreenToPct(dEndX, null), OffsetAxis.X)
                    + "  Y " + OffsetUnits.FormatPct(OffsetUnits.ScreenToPct(dEndY, null), OffsetAxis.Y) + "  ");
            bool moved = p.Dirs[3].OffX != 0 && Math.Sign(xAfterD) == Math.Sign(p.Dirs[3].OffX);
            bool trailOk = maxTrail > 5 && tr.Count == 0;
            Check("DB19 engine -> dashboard data path: D moves the ring, 2 ended-state lines with the card's X/Y, trail fades out when still",
                lines && moved && trailOk && lines0 == 0,
                "lines=" + log.Count + " [" + (log.Count > 1 ? log.Line(1) : "") + "] x(D)=" + F(xAfterD) + " maxTrail=" + maxTrail + " trailEnd=" + tr.Count + TrailDump(tr, now));
        }

        // ------------------------------------------------------------------ v4.0 DASH-FIX: responsive layout + key names

        static string DashLayoutProblem(DashLayout L, DashMetrics m)
        {
            DashRect client = new DashRect(0, 0, L.W, L.H);
            DashRect[] main = { L.Header, L.Chart, L.Data, L.Term, L.Caps };
            string[] names = { "header", "chart", "data", "term", "caps" };
            for (int i = 0; i < main.Length; i++)
            {
                if (!client.Contains(main[i])) return names[i] + " outside client " + main[i];
                for (int j = i + 1; j < main.Length; j++)
                    if (main[i].Intersects(main[j])) return names[i] + " overlaps " + names[j] + " " + main[i] + " / " + main[j];
            }
            if (L.HeaderTitle.Intersects(L.HeaderRight1)) return "title under memory";
            if (L.HeaderNote.Intersects(L.HeaderRight2)) return "note under memory note";
            if (!L.Header.Contains(L.HeaderTitle) || !L.Header.Contains(L.HeaderRight1) || !L.Header.Contains(L.HeaderNote) || !L.Header.Contains(L.HeaderRight2)) return "header text outside header";
            if (!L.Chart.Contains(L.ChartInner)) return "plot outside chart";
            if (!L.Term.IsEmpty && !L.Term.Contains(L.TermInner)) return "log outside terminal";
            for (int i = 0; i < DashLayout.RowCount; i++)
            {
                DashRect r = L.Rows[i];
                if (i >= L.RowsShown) { if (!r.IsEmpty) return "hidden row " + i + " has a rect"; continue; }
                if (!L.Data.Contains(r)) return "row " + i + " outside data card " + r + " in " + L.Data;
                if (r.Y < L.Data.Y + L.DataTitle.H) return "row " + i + " under the card title";
                if (r.H < Math.Ceiling(Math.Max(m.Body, m.Value))) return "row " + i + " lower than its text " + r.H;
                for (int j = 0; j < i; j++) if (r.Intersects(L.Rows[j])) return "rows " + j + "/" + i + " overlap";
            }
            if (L.CapsShown)
            {
                if (L.CapSize < (int)Math.Round(DashLayout.MinCap * L.U)) return "caps too small " + L.CapSize;
                for (int i = 0; i < DashKeys.Count; i++)
                {
                    DashRect c = L.CapRects[i];
                    if (c.IsEmpty || !L.CapsInner.Contains(c)) return "cap " + DashKeys.Names[i] + " outside caps card " + c + " in " + L.CapsInner;
                    if (c.Y < L.Caps.Y + L.CapsTitle.H) return "cap under the caps title";
                    for (int j = 0; j < i; j++) if (c.Intersects(L.CapRects[j])) return "caps " + DashKeys.Names[j] + "/" + DashKeys.Names[i] + " overlap";
                    if (c.Intersects(L.CapsCombo)) return "combo read-out over cap " + DashKeys.Names[i];
                }
                if (!L.CapsInner.Contains(L.CapsCombo)) return "combo read-out outside the caps card";
            }
            else for (int i = 0; i < DashKeys.Count; i++) if (!L.CapRects[i].IsEmpty) return "hidden caps still placed";
            return null;
        }

        static void DashLayoutTests()
        {
            // DB20 brute force: every client size x DPI x window zoom -> nothing outside the panel, no card over another
            // card, rows / caps inside their cards and never over each other, rows tall enough for their text.
            DashLayout L = new DashLayout();
            double[] scales = { 1, 1.25, 1.5, 1.75, 2 };
            int n = 0; string bad = null;
            for (int w = 60; w <= 4000 && bad == null; w += 23)
                for (int h = 50; h <= 2400 && bad == null; h += 29)
                    foreach (double s in scales)
                    {
                        double z = 1.0; // v4.3.1: fixed text size
                        DashMetrics m = DashMetrics.Estimate(s * z);
                        L.Arrange(w, h, s, z, m);
                        string p = DashLayoutProblem(L, m);
                        n++;
                        if (p != null) { bad = w + "x" + h + " @" + F(s) + " z" + F(z) + ": " + p; break; }
                    }
            Check("DB20 layout brute force (" + n + " sizes x DPI): inside the panel, no overlap, rows/caps inside their cards", bad == null, bad ?? "");

            // DB21 the reported case: 1280x720 @150 % (settings window at its minimum, page ~958 x 530 px) and the same
            // logical size at 200 % / 100 %: all 7 rows, the key caps and a terminal of >= 3 lines are shown; the caps fit
            // a 3-letter label (LMB) at 11 pt bold monospace (~0.6 em per char).
            string why = "";
            bool ok = true;
            int[][] cases = { new[] { 958, 530, 150 }, new[] { 1277, 707, 200 }, new[] { 639, 353, 100 }, new[] { 1350, 860, 150 } };
            foreach (int[] c in cases)
            {
                double s = c[2] / 100.0;
                double z = 1.0; // v4.3.1: fixed text size
                DashMetrics m = DashMetrics.Estimate(s * z);
                L.Arrange(c[0], c[1], s, z, m);
                string p = DashLayoutProblem(L, m);
                bool lmbFits = L.CapsShown && L.CapRects[7].W - 4 * s >= 3 * 0.6 * 11 * 1.333 * s * z;
                bool lines = L.TermInner.H >= 3 * L.LineH;
                if (p != null || L.RowsShown != 7 || !lmbFits || !lines || L.Narrow)
                {
                    ok = false;
                    why += c[0] + "x" + c[1] + "@" + c[2] + ": " + (p ?? "") + " rows=" + L.RowsShown + " caps=" + L.CapsShown + " cap=" + L.CapSize + " lines=" + lines + " narrow=" + L.Narrow + "; ";
                }
            }
            Check("DB21 1280x720 @100/150/200 %: 7 data rows + key caps (LMB fits) + 3-line terminal, wide layout", ok, why);

            // DB22 narrow page: data card under the chart (2 columns when wide enough); very short page: chart keeps a
            // usable plot (terminal dropped first)
            DashMetrics m1 = DashMetrics.Estimate(1);
            L.Arrange(520, 760, 1, 1, m1);
            bool stacked = L.Narrow && L.Data.Y >= L.Chart.Bottom && L.Term.Y >= L.Data.Bottom && L.Columns == 2 && L.RowsShown == 7;
            string d1 = "narrow=" + L.Narrow + " cols=" + L.Columns + " rows=" + L.RowsShown;
            L.Arrange(980, 440, 2, 1, DashMetrics.Estimate(2));
            bool shortOk = L.ChartInner.H >= (int)(DashLayout.MinPlot * 2) - 2 && L.Term.IsEmpty && DashLayoutProblem(L, DashMetrics.Estimate(2)) == null;
            Check("DB22 narrow page stacks data under the chart (2 columns); very short page drops the terminal, keeps a >= 110-unit plot",
                stacked && shortOk, d1 + " plotH=" + L.ChartInner.H + " term=" + L.Term);

            // DB24 屏幕中心 tag: left of the Y axis, below the X axis and its number labels, clear of the ring (~12 units)
            bool tagOk = true;
            foreach (double s in scales)
            {
                L.Arrange(1000, 700, s, 1, DashMetrics.Estimate(s));
                double cx = 400.5, cy = 300.5;
                DashRect t = L.CentreLabel(cx, cy, (int)(60 * s), (int)(19 * s));
                double nearX = Math.Max(t.X, Math.Min(cx, t.Right)), nearY = Math.Max(t.Y, Math.Min(cy, t.Bottom));
                double dist = Math.Sqrt((nearX - cx) * (nearX - cx) + (nearY - cy) * (nearY - cy));
                if (t.Right >= cx || t.Y <= cy + 20 * s || dist < 14 * s) tagOk = false;
            }
            Check("DB24 '屏幕中心' tag clear of both axes, the X-axis labels and the ring", tagOk, "");
        }

        static void DashKeyNameTests()
        {
            KeyBindings k = new KeyBindings();
            bool a;
            string q = DashKeyNames.Cap(k, 4, out a), e = DashKeyNames.Cap(k, 5, out a), sp = DashKeyNames.Cap(k, 6, out a),
                   l = DashKeyNames.Cap(k, 7, out a), r = DashKeyNames.Cap(k, 8, out a), w = DashKeyNames.Cap(k, 0, out a);
            bool defaults = q == "Q" && e == "E" && sp == "Space" && l == "左键" && r == "右键" && w == "W" // v5.0.2 (UI L4): mouse = 左键 / 右键
                && string.Join("\n", DashKeyNames.TryLines(k)) == "> 试一试：W A S D 移动 | Q E 侧身 | Space 跳跃\n> 左键 开火 | 右键 开镜 | Shift+W 冲刺"
                && DashKeyNames.Cap(k, 9, out a) == "Shift" && a
                && DashKeyNames.HintLine(k) == "按 W A S D 移动 · Q / E 侧身 · Space 跳跃 · 左键 开火 · 右键 开镜，准星实时跟着动";
            int sig0 = DashKeyNames.Signature(k);
            k.LeanRight = KeyBindings.VkD; k.LeanRightMods = KeyBindings.ModCtrl;
            k.Jump = KeyBindings.VkXButton1; k.Ads = 0x6A; // 小键盘* -> no ASCII name
            bool ascii;
            string er = DashKeyNames.Cap(k, 5, out ascii);
            bool ctrlD = er == "Ctrl+D" && ascii && DashKeyNames.HintLine(k).Contains("Q / Ctrl+D 侧身") && DashKeyNames.HintLine(k).Contains("侧键1 跳跃");
            bool m4 = DashKeyNames.Cap(k, 6, out ascii) == "侧键1" && !ascii; // v5.0.2 (UI L4): Chinese name, UI font
            bool fb = DashKeyNames.Cap(k, 8, out ascii) == "开镜" && !ascii
                && string.Join("\n", DashKeyNames.TryLines(k)) == "> 试一试：W A S D 移动 | Q Ctrl+D 侧身 | 侧键1 跳跃\n> 左键 开火 | 小键盘* 开镜 | Shift+W 冲刺"
                && string.Join(",", DashKeyNames.LogNames(k)) == "W,A,S,D,Q,Ctrl+D,侧键1,左键,开镜,Shift";
            bool sig = DashKeyNames.Signature(k) != sig0;
            // every vk 0..255 x mods 0..7: Ascii is null or printable ASCII; Hint never empty for a real key
            string bad = null;
            for (int vk = 1; vk < 256 && bad == null; vk++)
                for (int md = 0; md < 8; md++)
                {
                    string s = DashKeyNames.Ascii(vk, md);
                    if (s != null) foreach (char ch in s) if (ch < 0x20 || ch > 0x7E) { bad = "vk " + vk + " -> " + s; break; }
                    if (string.IsNullOrEmpty(DashKeyNames.Hint(vk, md))) { bad = "hint empty vk " + vk; break; }
                }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int acc = 0;
            for (int i = 0; i < 10000; i++) acc += DashKeyNames.Signature(k);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("DB25 hint, key caps and try-line come from the bindings (Ctrl+D, M4, fallback 开镜), signature changes, no alloc",
                defaults && ctrlD && m4 && fb && sig && bad == null && used == 0,
                q + e + sp + l + r + " | " + er + " | " + string.Join(" / ", DashKeyNames.TryLines(k)) + " | " + (bad ?? "") + " alloc=" + used + " " + (acc & 1));
        }
    }
}
