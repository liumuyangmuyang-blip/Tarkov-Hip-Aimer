// DashboardLogic.cs -- v4.0/v4.1 首页 dashboard: the pure pieces (trail buffer, terminal log, number caches, chart range,
// decode effect, formatting, dirty rectangles, HUD placement, live maths). PURE file: only System / System.Text (+ KeyBindings /
// InputState / Profile / OffsetUnits from Settings.cs / Motion.cs / OffsetUnits.cs). Compiles under net48 (C# 5) and net8.0 (tests).
// Nothing here allocates per call except where a NEW string is the result (a log line, a changed number).
// v4.1 (DASH41F): offsets are shown as 百分比偏移 (OffsetUnits, % of half the picture height, arrows); the chart range is in %
// and fixed per profile (DashRange); the panel repaints only dirty rectangles (DashDirty); HUD maths (DashTravel, DashHud).
using System;
using System.Globalization;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>Bit mask of the keys shown as key caps on the dashboard (raw state, before the opposite-key filter).</summary>
    public static class DashKeys
    {
        public const int W = 1, A = 2, S = 4, D = 8, Q = 16, E = 32, Space = 64, Lmb = 128, Rmb = 256, Shift = 512;
        public const int Count = 10;
        /// <summary>Cap order: W A S D Q E Space LMB RMB Shift (also the order of the combo text).</summary>
        public static readonly int[] Bits = new int[] { W, A, S, D, Q, E, Space, Lmb, Rmb, Shift };
        public static readonly string[] Names = new string[] { "W", "A", "S", "D", "Q", "E", "Space", "LMB", "RMB", "Shift" };

        /// <summary>冲刺 = Shift + W (the same rule MotionEngine uses for 冲刺取消开镜 / 冲刺取消侧身).</summary>
        public static bool Sprinting(int mask) { return (mask & (W | Shift)) == (W | Shift); }

        /// <summary>W = 前进, A = 左移, S = 后退, D = 右移, Q/E = 侧身, Space = 跳跃, LMB = 开火, RMB = 开镜, Shift = 冲刺 (with W).</summary>
        public static int FromInput(InputState i)
        {
            int m = 0;
            if (i.Fwd) m |= W;
            if (i.Left) m |= A;
            if (i.Back) m |= S;
            if (i.Right) m |= D;
            if (i.LeanL) m |= Q;
            if (i.LeanR) m |= E;
            if (i.Jump) m |= Space;
            if (i.Fire) m |= Lmb;
            if (i.Ads) m |= Rmb;
            if (i.Shift) m |= Shift;
            return m;
        }
    }

    public static class DashFormat
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Tenths, rounded half away from zero; NaN / infinity -> 0; clamped to +-9,999,999.9.</summary>
        public static long Tenths(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            double t = Math.Round(v * 10.0, MidpointRounding.AwayFromZero);
            if (t > 99999999) t = 99999999;
            if (t < -99999999) t = -99999999;
            return (long)t;
        }

        /// <summary>"+12.0", "-3.4", "+0.0" (never "-0.0").</summary>
        public static string Signed1FromTenths(long tenths)
        {
            long a = tenths < 0 ? -tenths : tenths;
            return (tenths < 0 ? "-" : "+") + (a / 10).ToString(Inv) + "." + (a % 10).ToString(Inv);
        }

        public static string Signed1(double v) { return Signed1FromTenths(Tenths(v)); }

        /// <summary>"12.0", "0.0" (absolute value, no sign).</summary>
        public static string Abs1FromTenths(long tenths)
        {
            long a = tenths < 0 ? -tenths : tenths;
            return (a / 10).ToString(Inv) + "." + (a % 10).ToString(Inv);
        }

        /// <summary>Shown for "no key down" (v4.1: Chinese, was "IDLE").</summary>
        public const string NoKeys = "空闲";

        /// <summary>v4.1 (DASH41G) 实时数据 侧身 row texts (one instance each, so the panel compares by reference).</summary>
        public static readonly string LeanCentre = "居中", LeanLeft = "左侧身", LeanRight = "右侧身",
            LeanLeftMoving = "左侧身（过渡中）", LeanRightMoving = "右侧身（过渡中）";

        /// <summary>The 侧身 row for MotionEngine.Lean (−1 = left … +1 = right). DOCCHECK41 D5: no % here, the rows above it show
        /// 百分比偏移. Settled = at least 95 % of the way, which the engine reaches exactly after 侧身过渡 (LeanMs = 3 time constants);
        /// below 5 % = 居中; in between 「（过渡中）」. NaN / infinity = 居中.</summary>
        public static string LeanText(double lean)
        {
            if (double.IsNaN(lean) || double.IsInfinity(lean)) return LeanCentre;
            double a = Math.Abs(lean);
            if (a < 0.05) return LeanCentre;
            bool left = lean < 0;
            if (a >= 0.95) return left ? LeanLeft : LeanRight;
            return left ? LeanLeftMoving : LeanRightMoving;
        }

        /// <summary>"W+D+LMB"; 「空闲」 when no key is down. Order = DashKeys.Bits.</summary>
        public static string KeyCombo(int mask) { return KeyCombo(mask, null); }

        /// <summary>Same with the user's key names per cap (DashKeys order, e.g. "Ctrl+D" for 右侧身); null = DashKeys.Names.</summary>
        public static string KeyCombo(int mask, string[] names)
        {
            if ((mask & ((1 << DashKeys.Count) - 1)) == 0) return NoKeys;
            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < DashKeys.Count; i++)
            {
                if ((mask & DashKeys.Bits[i]) == 0) continue;
                if (sb.Length > 0) sb.Append('+');
                string n = names != null && i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i] : DashKeys.Names[i];
                sb.Append(n);
            }
            return sb.ToString();
        }

        /// <summary>Seconds with 2 decimals: "0.70 s" (NaN / negative -> "0.00 s", clamped to 999.99; +infinity / huge -> "999.99 s").</summary>
        public static string Seconds2(double ms)
        {
            if (double.IsNaN(ms) || ms < 0) ms = 0;
            if (ms > 999990) ms = 999990; // v4.1 (brute-render P2-6): clamp BEFORE the long cast (it overflowed for 1e300 / +inf)
            long c = (long)Math.Round(ms / 10.0, MidpointRounding.AwayFromZero);
            if (c > 99999) c = 99999;
            return (c / 100).ToString(Inv) + "." + (c % 100).ToString("00", Inv) + " s";
        }

        /// <summary>The line for a key state that has just ENDED: its keys, the ring offset it ended on (the same X / Y the
        /// 实时数据 card showed, as 百分比偏移) and how long it lasted, e.g. "> D  X ← 4.17%  Y ↑ 0.42%  0.70 s"; "> W+Shift 冲刺  …".
        /// x / y = SCREEN px in UI coords (X right +, Y UP +); g = the settings for the % (null = the 1440 reference).</summary>
        public static string StateLine(int mask, string[] names, double x, double y, double durationMs, GlobalSettings g)
        {
            return "> " + KeyCombo(mask, names) + (DashKeys.Sprinting(mask) ? " 冲刺" : "") + "  X " + OffsetUnits.FormatPct(DashStretch.ScreenXToPct(x, g), OffsetAxis.X)
                + "  Y " + OffsetUnits.FormatPct(OffsetUnits.ScreenToPct(y, g), OffsetAxis.Y) + "  " + Seconds2(durationMs);
        }

        /// <summary>StateLine with the 1440 reference picture (no settings).</summary>
        public static string StateLine(int mask, string[] names, double x, double y, double durationMs)
        {
            return StateLine(mask, names, x, y, durationMs, null);
        }

        /// <summary>The shown hundredths of a % (OffsetUnits.Round x 100, NaN -> 0, clamped), for change detection.</summary>
        public static long PctKey(double pct)
        {
            double r = OffsetUnits.Round(pct);
            if (r > 1e12) r = 1e12;
            if (r < -1e12) r = -1e12;
            return (long)Math.Round(r * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Signed with 2 decimals: "+12.34", "-0.50", "+0.00" (never "-0.00"); NaN / infinity -> "+0.00"; clamped to +-99999.99.</summary>
        public static string Signed2(double v)
        {
            long c = Hundredths(v);
            long a = c < 0 ? -c : c;
            return (c < 0 ? "-" : "+") + (a / 100).ToString(Inv) + "." + (a % 100).ToString("00", Inv);
        }

        /// <summary>Hundredths, rounded half away from zero; NaN / infinity -> 0; clamped to +-9,999,999.</summary>
        public static long Hundredths(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            double t = Math.Round(v * 100.0, MidpointRounding.AwayFromZero);
            if (t > 9999999) t = 9999999;
            if (t < -9999999) t = -9999999;
            return (long)t;
        }

        /// <summary>Unsigned fixed decimals (0..3): Fixed(0.8734, 3) = "0.873"; NaN -> 0; negative -> its absolute value; clamped 99999.</summary>
        public static string Fixed(double v, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            v = Math.Abs(v);
            if (v > 99999) v = 99999;
            if (decimals < 0) decimals = 0;
            if (decimals > 3) decimals = 3;
            return v.ToString(decimals == 0 ? "0" : (decimals == 1 ? "0.0" : (decimals == 2 ? "0.00" : "0.000")), Inv);
        }

        /// <summary>Whole ms, clamped 0..99999 (NaN -> 0).</summary>
        public static long Ms(double ms)
        {
            if (double.IsNaN(ms) || ms < 0) return 0;
            if (ms > 99999) return 99999;
            return (long)Math.Round(ms, MidpointRounding.AwayFromZero);
        }

        /// <summary>Bytes -> "18.4 MB" (1 MB = 1024 x 1024 bytes). Negative -> "0.0 MB".</summary>
        public static string Megabytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            return Abs1FromTenths(MbTenths(bytes)) + " MB";
        }

        public static long MbTenths(long bytes)
        {
            if (bytes < 0) bytes = 0;
            return (long)Math.Round(bytes * 10.0 / (1024.0 * 1024.0), MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// A text that is rebuilt only when its displayed number changes (so a steady value costs no allocation per frame).
    /// One instance per on-screen field; prefix / suffix are fixed per call site.
    /// </summary>
    public sealed class DashText
    {
        private long key;
        private bool has;   // v4.1 (brute-render P2-10): a flag, not a long.MinValue key (Int(long.MinValue) never showed)
        private string text = "";
        public string Text { get { return text; } }

        private bool Same(long k)
        {
            if (has && k == key) return true;
            has = true; key = k;
            return false;
        }

        /// <summary>Signed, one decimal: prefix + "+12.0" + suffix. Returns true when the text changed.</summary>
        public bool Signed1(double v, string prefix, string suffix)
        {
            long k = DashFormat.Tenths(v);
            if (Same(k)) return false;
            text = prefix + DashFormat.Signed1FromTenths(k) + suffix;
            return true;
        }

        /// <summary>Unsigned (absolute), one decimal.</summary>
        public bool Abs1(double v, string prefix, string suffix)
        {
            long k = DashFormat.Tenths(Math.Abs(double.IsNaN(v) ? 0 : v));
            if (Same(k)) return false;
            text = prefix + DashFormat.Abs1FromTenths(k) + suffix;
            return true;
        }

        public bool Int(long v, string prefix, string suffix)
        {
            if (Same(v)) return false;
            text = prefix + v.ToString(CultureInfo.InvariantCulture) + suffix;
            return true;
        }

        public bool Megabytes(long bytes, string prefix, string suffix)
        {
            long k = DashFormat.MbTenths(bytes);
            if (Same(k)) return false;
            text = prefix + DashFormat.Abs1FromTenths(k) + " MB" + suffix;
            return true;
        }

        /// <summary>A text chosen by the caller for a small integer state (e.g. lean percent with side).</summary>
        public bool Keyed(long k, string value)
        {
            if (Same(k)) return false;
            text = value ?? "";
            return true;
        }

        public void Invalidate() { has = false; }
    }

    /// <summary>
    /// v4.1 百分比偏移 text for one live value (OffsetUnits format, e.g. 「← 4.17%」): rebuilt only when the SHOWN hundredths
    /// change, so a steady value allocates nothing.
    /// </summary>
    public sealed class DashPct
    {
        private long key;
        private bool has;
        private string text = "";
        public string Text { get { return text; } }

        /// <summary>Signed % in the PROFILE convention (X + right, Y + up). True when the text changed.</summary>
        public bool Set(double pct, OffsetAxis axis)
        {
            long k = DashFormat.PctKey(pct) * 2 + (axis == OffsetAxis.Y ? 1 : 0);
            if (has && k == key) return false;
            has = true; key = k;
            text = OffsetUnits.FormatPct(pct, axis);
            return true;
        }

        public void Invalidate() { has = false; }
    }

    /// <summary>
    /// A text built by the caller from up to three numbers: Same(a, b, c) is true while those keys are unchanged (keep Text),
    /// false once they changed (then set Text). Lets the HUD lines allocate only when a shown number changes.
    /// </summary>
    public sealed class DashLine
    {
        private long a, b, c;
        private bool has;
        public string Text = "";

        public bool Same(long ka, long kb, long kc)
        {
            if (has && ka == a && kb == b && kc == c) return true;
            has = true; a = ka; b = kb; c = kc;
            return false;
        }

        public void Invalidate() { has = false; }
    }

    /// <summary>Fixed-size ring buffer of ring positions with a time stamp; points older than MaxAgeMs are dropped.</summary>
    public sealed class DashTrail
    {
        private readonly double[] xs, ys, ts;
        private int head;   // index of the oldest point
        private int count;
        private double lastX, lastY; // the newest point ever added (kept after the buffer empties: a still ring adds nothing)
        private bool hasLast;
        public readonly int Capacity;
        public double MaxAgeMs;

        public DashTrail(int capacity, double maxAgeMs)
        {
            if (capacity < 2) capacity = 2;
            Capacity = capacity;
            xs = new double[capacity]; ys = new double[capacity]; ts = new double[capacity];
            MaxAgeMs = maxAgeMs > 0 ? maxAgeMs : 1000;
        }

        public int Count { get { return count; } }

        public void Clear() { head = 0; count = 0; hasLast = false; }

        /// <summary>Adds a point unless it is closer than minStep to the newest point ever added -- also after every point
        /// has aged out, so a ring that stands still keeps the trail empty -- or is not a number. Full buffer: the oldest goes.</summary>
        public bool Add(double tMs, double x, double y, double minStep)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || double.IsNaN(tMs)) return false;
            if (hasLast)
            {
                double ddx = x - lastX, ddy = y - lastY;
                if (ddx * ddx + ddy * ddy < minStep * minStep) return false;
            }
            lastX = x; lastY = y; hasLast = true;
            int idx;
            if (count < Capacity) { idx = (head + count) % Capacity; count++; }
            else { idx = head; head = (head + 1) % Capacity; }
            xs[idx] = x; ys[idx] = y; ts[idx] = tMs;
            return true;
        }

        /// <summary>Drops points older than MaxAgeMs (and any stamped in the future by more than a second -- clock reset).</summary>
        public void Trim(double nowMs)
        {
            while (count > 0)
            {
                double age = nowMs - ts[head];
                if (age > MaxAgeMs || age < -1000) { head = (head + 1) % Capacity; count--; }
                else break;
            }
            if (count == 0) head = 0;
        }

        /// <summary>i = 0 is the oldest point.</summary>
        public void Get(int i, out double x, out double y, out double tMs)
        {
            if (i < 0 || i >= count) throw new ArgumentOutOfRangeException("i");
            int k = (head + i) % Capacity;
            x = xs[k]; y = ys[k]; tMs = ts[k];
        }

        /// <summary>Largest |x| or |y| of the stored points (0 when empty).</summary>
        public double AbsPeak()
        {
            double m = 0;
            for (int i = 0; i < count; i++)
            {
                int k = (head + i) % Capacity;
                double a = Math.Abs(xs[k]), b = Math.Abs(ys[k]);
                if (a > m) m = a;
                if (b > m) m = b;
            }
            return m;
        }
    }

    /// <summary>
    /// The terminal box: a capped list of lines with a typed-out effect. A new line starts typing when the previous
    /// one has finished; if that would queue it more than MaxQueueMs, every earlier line is completed at once (so a
    /// burst of key changes never builds a long backlog).
    /// </summary>
    public sealed class DashLog
    {
        private readonly string[] lines;
        private readonly double[] starts;
        private int head, count;
        public readonly int Capacity;
        public double CharsPerSec = 160;
        public double MaxQueueMs = 250;
        private int version;

        public DashLog(int capacity)
        {
            if (capacity < 1) capacity = 1;
            Capacity = capacity;
            lines = new string[capacity];
            starts = new double[capacity];
        }

        public int Count { get { return count; } }
        /// <summary>Changes whenever a line is added or the log is cleared.</summary>
        public int Version { get { return version; } }

        public void Clear() { head = 0; count = 0; version++; }

        private int Index(int i) { return (head + i) % Capacity; }

        private double EndMs(int idx)
        {
            double cps = CharsPerSec > 1 ? CharsPerSec : 1;
            return starts[idx] + lines[idx].Length * 1000.0 / cps;
        }

        public void Add(string line, double nowMs)
        {
            if (line == null) line = "";
            double start = nowMs;
            if (count > 0)
            {
                double end = EndMs(Index(count - 1));
                if (end > nowMs)
                {
                    if (end - nowMs <= MaxQueueMs) start = end;
                    else CompleteAll();
                }
            }
            int idx;
            if (count < Capacity) { idx = Index(count); count++; }
            else { idx = head; head = (head + 1) % Capacity; }
            lines[idx] = line;
            starts[idx] = start;
            version++;
        }

        /// <summary>Shows every line in full right now.</summary>
        public void CompleteAll()
        {
            for (int i = 0; i < count; i++) starts[Index(i)] = double.MinValue / 4;
        }

        /// <summary>i = 0 is the oldest line.</summary>
        public string Line(int i)
        {
            if (i < 0 || i >= count) throw new ArgumentOutOfRangeException("i");
            return lines[Index(i)];
        }

        /// <summary>How many characters of line i are typed out at nowMs (0..length).</summary>
        public int Visible(int i, double nowMs)
        {
            if (i < 0 || i >= count) return 0;
            int idx = Index(i);
            int len = lines[idx].Length;
            double cps = CharsPerSec > 1 ? CharsPerSec : 1;
            double n = (nowMs - starts[idx]) * cps / 1000.0;
            if (double.IsNaN(n) || n <= 0) return 0;
            if (n >= len) return len;
            return (int)n;
        }

        /// <summary>True while the newest line is still being typed.</summary>
        public bool Typing(double nowMs)
        {
            if (count == 0) return false;
            return Visible(count - 1, nowMs) < lines[Index(count - 1)].Length;
        }
    }

    /// <summary>
    /// v4.1 chart range in 百分比偏移 (% of half the picture height, from the centre to the plot edge) on a fixed ladder.
    /// It is FIXED per profile (the largest offset the profile can show, plus headroom), so the same offset always looks the
    /// same on the chart (v4.0 auto-zoomed between moves), and it grows at once when the live ring needs more (a 跳跃 path,
    /// stacked offsets). It never shrinks by itself: SetFloor (settings changed) or Reset brings it back.
    /// Each change re-renders the chart's static layer once (v4.0 animated the zoom and re-rendered it every frame).
    /// </summary>
    public sealed class DashRange
    {
        public static readonly double[] Ladder = new double[] { 3, 5, 8, 12, 20, 30, 50, 80, 120, 200 };
        public static readonly double[] Steps = new double[] { 1, 1, 2, 3, 5, 10, 10, 20, 30, 50 };
        /// <summary>Headroom: a range must hold peak x Headroom.</summary>
        public const double Headroom = 1.15;
        /// <summary>Above the profile's range: back one rung after the peak has fit the smaller one this long.</summary>
        public double ShrinkAfterMs = 2500;

        private int rung, floorRung;
        private double fitSinceMs = double.NaN;

        public int Rung { get { return rung; } }
        public double Range { get { return Ladder[rung]; } }
        public double Step { get { return Steps[rung]; } }

        /// <summary>Smallest rung that holds peak x Headroom (the top rung when nothing does; NaN / negative -> 0).</summary>
        public static int RungFor(double peakPct)
        {
            if (double.IsNaN(peakPct) || peakPct < 0) peakPct = 0;
            double need = peakPct * Headroom;
            for (int i = 0; i < Ladder.Length; i++) if (Ladder[i] >= need) return i;
            return Ladder.Length - 1;
        }

        /// <summary>The profile's own range (also drops any live growth). True when the range changed.</summary>
        public bool SetFloor(double peakPct)
        {
            int old = rung;
            floorRung = RungFor(peakPct);
            rung = floorRung;
            fitSinceMs = double.NaN;
            return rung != old;
        }

        /// <summary>Grows at once when peakPct needs a bigger rung (a 跳跃, stacked offsets); above the profile's range it
        /// goes back one rung after the peak has fit the smaller rung for ShrinkAfterMs; never below the profile's range.
        /// True when the range changed.</summary>
        public bool Update(double peakPct, double nowMs)
        {
            int want = Math.Max(RungFor(peakPct), floorRung);
            if (want > rung) { rung = want; fitSinceMs = double.NaN; return true; }
            if (want < rung)
            {
                if (double.IsNaN(fitSinceMs)) { fitSinceMs = nowMs; return false; }
                if (nowMs - fitSinceMs >= ShrinkAfterMs)
                {
                    rung--;
                    fitSinceMs = rung > want ? nowMs : double.NaN;
                    return true;
                }
                return false;
            }
            fitSinceMs = double.NaN;
            return false;
        }

        /// <summary>Back to the profile's own range.</summary>
        public void Reset() { rung = floorRung; fitSinceMs = double.NaN; }

        /// <summary>
        /// The largest single offset (in %, absolute) the profile shows in normal play: every direction X/Y, 侧身 Q/E X/Y and
        /// 后坐力 (最大 + 水平偏移), for both hands -- the chart's fixed range, so these always look the same. A 跳跃 path or
        /// stacked offsets that need more zooms the chart out for a moment (Update). v0 / v1 = caller-owned buffers (no allocation).
        /// </summary>
        public static double ProfilePeakPct(Profile p, GlobalSettings g, HandView v0, HandView v1)
        {
            if (p == null || v0 == null || v1 == null) return 0;
            double m = 0;
            for (int hand = 0; hand < 2; hand++)
            {
                HandView v = hand == 0 ? v0 : v1;
                try { p.FillView(hand, v); } catch (Exception) { continue; }
                for (int i = 0; i < Profile.DirCount; i++) { m = Max(m, v.DX[i]); m = Max(m, v.DY[i]); }
                m = Max(m, v.QX); m = Max(m, v.QY); m = Max(m, v.EX); m = Max(m, v.EY);
                m = Max(m, v.Drift);
            }
            m = Max(m, p.RecoilMaxPx);
            double pct = Math.Abs(OffsetUnits.ToPct(m, g)) * OffsetUnits.MaxFovFactor(g); // v5.1.1: largest FOV side
            return double.IsNaN(pct) || double.IsInfinity(pct) ? 0 : pct;
        }

        private static double Max(double m, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return m;
            v = Math.Abs(v);
            return v > m ? v : m;
        }
    }

    /// <summary>"Decoding" title effect: characters resolve left to right out of random glyphs.</summary>
    public static class DashDecode
    {
        public const string Glyphs = "01<>/\\#$%&*+=?ABCDEFXZ";

        /// <summary>Writes the frame for progress 0..1 into buf (length = target length). Spaces stay spaces.
        /// Returns true when every character is resolved.</summary>
        public static bool Fill(char[] buf, string target, double progress, int frame)
        {
            if (buf == null || target == null) return true;
            int n = Math.Min(buf.Length, target.Length);
            if (double.IsNaN(progress) || progress < 0) progress = 0;
            if (progress > 1) progress = 1;
            int resolved = (int)Math.Floor(progress * (n + 4)) - 4; // a 4-char scrambling window ahead of the resolved part
            bool done = true;
            for (int i = 0; i < n; i++)
            {
                char t = target[i];
                if (t == ' ' || i < resolved || progress >= 1) { buf[i] = t; continue; }
                done = false;
                uint h = (uint)(i * 7919 + frame * 104729) * 2654435761u;
                buf[i] = Glyphs[(int)((h >> 16) % (uint)Glyphs.Length)];
            }
            return done;
        }

        /// <summary>v4.1 HUD "re-compute" sweep: a window of `window` random glyphs moves left to right over the text (the
        /// rest is shown as is; spaces stay spaces). Writes min(buf, target) chars; true when the window has passed.</summary>
        public static bool Sweep(char[] buf, string target, double progress, int frame, int window)
        {
            if (buf == null || target == null) return true;
            int n = Math.Min(buf.Length, target.Length);
            if (double.IsNaN(progress) || progress < 0) progress = 0;
            if (progress > 1) progress = 1;
            if (window < 1) window = 1;
            int head = (int)Math.Floor(progress * (n + window)) - window; // first scrambled index
            for (int i = 0; i < n; i++)
            {
                char t = target[i];
                if (t == ' ' || progress >= 1 || i < head || i >= head + window) { buf[i] = t; continue; }
                uint h = (uint)(i * 7919 + frame * 104729) * 2654435761u;
                buf[i] = Glyphs[(int)((h >> 16) % (uint)Glyphs.Length)];
            }
            return progress >= 1 || head >= n;
        }
    }

    /// <summary>
    /// Turns state changes into terminal lines. When the key mask changes, the state that just ENDED is logged with the
    /// ring offset it ended on and how long it lasted (DashFormat.StateLine; nothing for a no-key state), so the line
    /// agrees with the 实时数据 card. Plus one line when the hand, 强化准星 or 开镜 state flips. The first Step only records
    /// the state (no line).
    /// </summary>
    public sealed class DashEvents
    {
        public const string HandSwitched = "> 换手：左手持枪";
        public const string HandNormal = "> 换手：回到右手";
        public const string BoostOn = "> 强化准星 开";
        public const string BoostOff = "> 强化准星 关";
        public const string AdsOn = "> 开镜 开（准星隐藏）";
        public const string AdsOff = "> 开镜 关";

        private int prevMask = -1;
        private bool prevHand, prevBoost, prevAds;
        private double lastChangeMs, lastX, lastY;

        /// <summary>Key name per cap for the log (DashKeys order, the user's bindings); null = DashKeys.Names.</summary>
        public string[] Names;
        /// <summary>v4.1: settings for the 百分比偏移 in the lines (null = the 1440 reference picture).</summary>
        public GlobalSettings Units;

        public void Reset() { prevMask = -1; }

        /// <summary>Returns the number of lines added. x / y = the ring offset this frame (after this frame's input).</summary>
        public int Step(int mask, bool hand, bool boost, bool ads, double x, double y, double nowMs, DashLog log)
        {
            if (prevMask < 0)
            {
                prevMask = mask; prevHand = hand; prevBoost = boost; prevAds = ads; lastChangeMs = nowMs; lastX = x; lastY = y;
                return 0;
            }
            int n = 0;
            if (mask != prevMask)
            {
                // the previous frame's offset is where the ended state left the ring (this frame already moved under the new keys)
                if ((prevMask & ((1 << DashKeys.Count) - 1)) != 0)
                {
                    log.Add(DashFormat.StateLine(prevMask, Names, lastX, lastY, nowMs - lastChangeMs, Units), nowMs);
                    n++;
                }
                lastChangeMs = nowMs;
                prevMask = mask;
            }
            lastX = x; lastY = y;
            if (hand != prevHand) { log.Add(hand ? HandSwitched : HandNormal, nowMs); prevHand = hand; n++; }
            if (boost != prevBoost) { log.Add(boost ? BoostOn : BoostOff, nowMs); prevBoost = boost; n++; }
            if (ads != prevAds) { log.Add(ads ? AdsOn : AdsOff, nowMs); prevAds = ads; n++; }
            return n;
        }
    }

    // =================================================================================================== v4.0 layout

    /// <summary>Integer rectangle (pixels) for the pure layout (no System.Drawing here).</summary>
    public struct DashRect
    {
        public int X, Y, W, H;
        public DashRect(int x, int y, int w, int h) { X = x; Y = y; W = w < 0 ? 0 : w; H = h < 0 ? 0 : h; }
        public int Right { get { return X + W; } }
        public int Bottom { get { return Y + H; } }
        public bool IsEmpty { get { return W <= 0 || H <= 0; } }
        public bool Intersects(DashRect o)
        {
            if (IsEmpty || o.IsEmpty) return false;
            return X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
        }
        public bool Contains(DashRect o)
        {
            if (o.IsEmpty) return true;
            return o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;
        }
        public override string ToString() { return X + "," + Y + " " + W + "x" + H; }
    }

    /// <summary>Measured text heights / widths in pixels (the panel measures its fonts; tests use Estimate).</summary>
    public sealed class DashMetrics
    {
        public double Title, Body, Small, Value, Mono, Cap;
        /// <summary>Width of the header's right block ("FPS 000   内存占用 0000.0 MB").</summary>
        public double RightW;

        /// <summary>Typical Microsoft YaHei UI / Consolas heights at u px per design unit (u = DPI scale x zoom).</summary>
        public static DashMetrics Estimate(double u)
        {
            DashMetrics m = new DashMetrics();
            // 16 / 11 / 10.5 pt YaHei UI, 12 pt bold / 10.5 pt / 11 pt bold Consolas
            m.Title = 28 * u; m.Body = 19.5 * u; m.Small = 18.6 * u; m.Value = 19 * u; m.Mono = 16.5 * u; m.Cap = 17.5 * u;
            m.RightW = 150 * u;
            return m;
        }
    }

    /// <summary>
    /// The 首页 dashboard layout, computed from the client size, the DPI scale and the measured font heights only:
    /// header (title + note | FPS + 内存占用), chart, 实时数据 card, terminal and the 按键 card. Wide panels put the
    /// data card right of the chart and the key caps right of the terminal; narrow panels (under 600 design units)
    /// stack the data card (2 columns when it fits) under the chart. Rows that do not fit are dropped from the end
    /// (least important first) and the key caps are hidden when they would be smaller than 16 units -- nothing is
    /// ever drawn outside its card or over another card.
    /// </summary>
    public sealed class DashLayout
    {
        public const int RowCount = 7;
        public const double NarrowBelow = 600;   // design units
        public const double CompactBelow = 420;  // design units of height: one-line header
        public const double MinCap = 22, WantCap = 28, MaxCap = 56, RowMax = 40, MinPlot = 110;

        public int W, H;
        public double S = 1, Zoom = 1, U = 1;
        public bool CompactHeader, Narrow, CapsShown, CapsTwoRows;
        public int Columns = 1, RowsShown, RowH, Pad, Gap, LineH;
        public DashRect Header, HeaderTitle, HeaderNote, HeaderRight1, HeaderRight2;
        public DashRect Chart, ChartTitle, ChartInner;
        public DashRect Data, DataTitle;
        public DashRect Term, TermTitle, TermInner;
        public DashRect Caps, CapsTitle, CapsInner;
        /// <summary>Live key-combo read-out under the caps ("> W+D+LMB"), only when the card has room for it.</summary>
        public DashRect CapsCombo;
        public readonly DashRect[] Rows = new DashRect[RowCount];
        /// <summary>Same order as DashKeys.Bits: W A S D Q E Space LMB RMB.</summary>
        public readonly DashRect[] CapRects = new DashRect[DashKeys.Count];
        public int CapSize;

        private int P(double units) { return (int)Math.Round(units * U); }

        public void Arrange(int w, int h, double s, double zoom, DashMetrics m)
        {
            if (m == null) m = DashMetrics.Estimate((s > 0 ? s : 1) * (zoom > 0 ? zoom : 1));
            W = Math.Max(1, w); H = Math.Max(1, h);
            S = s > 0 && !double.IsNaN(s) ? s : 1;
            Zoom = zoom > 0 && !double.IsNaN(zoom) ? zoom : 1;
            U = S * Zoom;
            Pad = P(12); Gap = P(10);
            int pad = Pad, gap = Gap;

            int tH = Ceil(m.Title), smH = Ceil(m.Small), bodyFontH = Ceil(Math.Max(m.Body, m.Small));
            int rowMin = Ceil(Math.Max(m.Body, m.Value)) + P(2);
            int rowPref = Math.Max(rowMin, P(28));
            LineH = Math.Max(1, Ceil(m.Mono) + P(2));
            int titleBar = bodyFontH + P(10);
            int termTitle = Math.Max(smH + P(10), P(20));
            int termMin = termTitle + P(10) + (H / U < 300 ? 2 : 3) * LineH;   // a very short page keeps 2 log lines

            // ---------------- header
            CompactHeader = H / U < CompactBelow;
            int headerH = CompactHeader ? tH + P(10) : tH + smH + P(20);
            if (headerH > H / 3) headerH = H / 3;
            Header = new DashRect(0, 0, W, headerH);
            int rbw = Ceil(m.RightW);
            int rbMax = W / 2 - pad;
            if (rbw > rbMax) rbw = Math.Max(0, rbMax);
            int rx = W - pad - rbw;
            int row1Y = CompactHeader ? 0 : P(6);
            int row1H = CompactHeader ? headerH : Math.Min(headerH - row1Y, tH + P(2));
            HeaderRight1 = new DashRect(rx, row1Y, rbw, row1H);
            HeaderTitle = new DashRect(pad, row1Y, Math.Max(0, rx - P(12) - pad), row1H);
            if (CompactHeader)
            {
                HeaderNote = new DashRect(0, 0, 0, 0);
                HeaderRight2 = new DashRect(0, 0, 0, 0);
            }
            else
            {
                int ny = row1Y + row1H;
                int nh = Math.Max(0, Math.Min(smH + P(4), headerH - 1 - ny));
                HeaderNote = new DashRect(pad, ny, HeaderTitle.W, nh);
                HeaderRight2 = new DashRect(rx, ny, rbw, nh);
            }

            // ---------------- body
            bTop = Header.Bottom + gap;
            bBottom = H - pad;
            bH = Math.Max(0, bBottom - bTop);
            bW = Math.Max(0, W - 2 * pad);
            kTitleBar = titleBar; kRowMin = rowMin; kRowPref = rowPref; kTermMin = termMin;
            int wideScore = ArrangeWide();
            Narrow = false;
            if (W / U < NarrowBelow)
            {
                int narrowScore = ArrangeNarrow();
                if (narrowScore > wideScore) Narrow = true;
                else ArrangeWide();
            }

            ChartTitle = new DashRect(Chart.X, Chart.Y, Chart.W, Math.Min(titleBar, Chart.H));
            ChartInner = new DashRect(Chart.X + 1, Chart.Y + ChartTitle.H, Chart.W - 2, Chart.H - ChartTitle.H - 1);
            DataTitle = new DashRect(Data.X + P(12), Data.Y, Data.W - P(24), Math.Min(titleBar, Data.H));
            int colGap = P(16);
            int colW = Columns > 1 ? (Data.W - P(24) - colGap) / 2 : Data.W - P(24);
            for (int i = 0; i < RowCount; i++)
            {
                if (i >= RowsShown) { Rows[i] = new DashRect(0, 0, 0, 0); continue; }
                int c = i % Columns, r = i / Columns;
                Rows[i] = new DashRect(Data.X + P(12) + c * (colW + colGap), Data.Y + titleBar + r * RowH, colW, RowH);
            }
            TermTitle = new DashRect(Term.X + P(10), Term.Y, Term.W - P(20), Math.Min(termTitle, Term.H));
            TermInner = new DashRect(Term.X + P(10), Term.Y + termTitle + P(4), Term.W - P(20), Term.H - termTitle - P(8));
        }

        private int bTop, bBottom, bH, bW, kTitleBar, kRowMin, kRowPref, kTermMin;

        private void ResetCaps()
        {
            CapsShown = false; CapsTwoRows = false; CapSize = 0;
            for (int i = 0; i < CapRects.Length; i++) CapRects[i] = new DashRect(0, 0, 0, 0);
            Caps = CapsTitle = CapsInner = CapsCombo = new DashRect(0, 0, 0, 0);
        }

        /// <summary>Chart + terminal on the left; 实时数据 (content height) + 按键 (the rest) on the right.
        /// Returns the chart's usable size (smaller side of its plot area) to compare layouts.</summary>
        private int ArrangeWide()
        {
            ResetCaps();
            int pad = Pad, gap = Gap, titleBar = kTitleBar;
            int sideW = Clamp((int)Math.Round(bW * 0.30), P(200), P(300));
            if (sideW > (int)(bW * 0.45)) sideW = (int)(bW * 0.45);
            int leftW = Math.Max(0, bW - sideW - gap);
            int termH = Clamp((int)Math.Round(bH * 0.30), kTermMin, P(220));
            int chartH = bH - gap - termH;
            if (chartH - titleBar < P(MinPlot))
            {   // too short for chart + terminal: the chart (the main view) gets the whole column, the terminal goes
                Chart = new DashRect(pad, bTop, leftW, bH);
                Term = new DashRect(pad, bBottom, 0, 0);
            }
            else
            {
                Chart = new DashRect(pad, bTop, leftW, chartH);
                Term = new DashRect(pad, Chart.Bottom + gap, leftW, Math.Max(0, bBottom - (Chart.Bottom + gap)));
            }

            int x = pad + leftW + gap;
            int colH = bH;
            Columns = 1;
            int fitRows = kRowMin > 0 ? (colH - titleBar - P(6)) / kRowMin : 0;
            int n = Clamp(fitRows, 0, RowCount);
            RowsShown = n;
            int cg = P(6), innerW = sideW - P(20);
            int capsMinH = titleBar + P(8) + 2 * P(MinCap) + cg;          // 2-row caps at the smallest size
            int capsWantH = titleBar + P(8) + 2 * P(WantCap) + cg;        // 2-row caps at a comfortable size
            int csIdeal = Math.Max(P(MinCap), Math.Min(P(MaxCap), (innerW - 2 * cg) / 3));
            int capsIdealH = titleBar + P(8) + (int)(3.5 * csIdeal) + 3 * cg; // 4-row caps at full size
            bool caps = false;
            if (n > 0)
            {
                // rows share the column with the caps: grow up to RowMax when there is room, shrink to rowMin before
                // the caps go below their comfortable size, and only then let the caps shrink to MinCap
                int rowH = Clamp((colH - gap - capsIdealH - titleBar - P(6)) / n, Math.Min(kRowPref, kRowMin + P(6)), Math.Max(kRowPref, P(RowMax)));
                if (colH - gap - (titleBar + n * rowH + P(6)) < capsWantH)
                    rowH = Clamp((colH - gap - capsWantH - titleBar - P(6)) / n, kRowMin, rowH);
                if (colH - gap - (titleBar + n * rowH + P(6)) < capsMinH)
                    rowH = Math.Max(kRowMin, (colH - gap - capsMinH - titleBar - P(6)) / n);
                int dataH = titleBar + n * rowH + P(6);
                if (colH - gap - dataH >= capsMinH)
                {
                    Data = new DashRect(x, bTop, sideW, dataH);
                    RowH = rowH;
                    caps = PlaceCaps(new DashRect(x, Data.Bottom + gap, sideW, bBottom - (Data.Bottom + gap)), titleBar);
                }
            }
            if (!caps)
            {
                ResetCaps();
                Data = new DashRect(x, bTop, sideW, colH);
                RowH = n > 0 ? Math.Min(kRowPref, (colH - titleBar - P(6)) / n) : 0;
                // short page: the 按键 card goes to the right end of the terminal row instead (the terminal grows to
                // hold 2 rows of comfortable caps if the chart can spare it)
                int boxW = Clamp((int)(leftW * 0.36), P(170), P(260));
                if (!Term.IsEmpty && leftW - boxW - gap >= P(300))
                {
                    int needH = titleBar + P(8) + 2 * P(WantCap) + cg;
                    if (Term.H < needH)
                    {
                        int grow = needH - Term.H;
                        if (Chart.H - grow - titleBar >= P(MinPlot))
                        {
                            Chart.H -= grow;
                            Term = new DashRect(pad, Chart.Bottom + gap, leftW, Math.Max(0, bBottom - (Chart.Bottom + gap)));
                        }
                    }
                    if (PlaceCaps(new DashRect(pad + leftW - boxW, Term.Y, boxW, Term.H), titleBar))
                        Term = new DashRect(pad, Term.Y, leftW - boxW - gap, Term.H);
                    else ResetCaps();
                }
            }
            return Math.Min(Chart.W - 2, Chart.H - titleBar - 1);
        }

        /// <summary>Chart on top, 实时数据 (1-2 columns) under it, terminal (+ 按键 when wide enough) at the bottom.</summary>
        private int ArrangeNarrow()
        {
            ResetCaps();
            int pad = Pad, gap = Gap, titleBar = kTitleBar, cw = bW, bodyH = bH;
            int termH = Clamp((int)Math.Round(bodyH * 0.26), kTermMin, P(170));
            Columns = (cw - P(24)) >= 2 * P(190) + P(16) ? 2 : 1;
            int rowH = kRowMin + P(2);
            int n = RowCount, dataH = 0, chartH = 0;
            for (; ; )
            {
                dataH = titleBar + ((n + Columns - 1) / Columns) * rowH + P(8);
                chartH = bodyH - termH - dataH - 2 * gap;
                if (chartH >= P(130) || n <= 3) break;
                n--;
            }
            if (chartH < P(40))
            {   // a tiny page: give the chart what the terminal can spare, never go negative
                int spare = termH - kTermMin;
                if (spare > 0) { int take = Math.Min(spare, P(40) - chartH); termH -= take; chartH += take; }
                if (chartH < 0) { termH = Math.Max(0, termH + chartH); chartH = 0; }
                if (bodyH - termH - chartH - 2 * gap < dataH) dataH = Math.Max(0, bodyH - termH - chartH - 2 * gap);
                if (dataH < titleBar + rowH) n = 0;
            }
            Chart = new DashRect(pad, bTop, cw, chartH);
            Data = new DashRect(pad, Chart.Bottom + gap, cw, dataH);
            RowsShown = n;
            RowH = rowH;
            int ty = Data.Bottom + gap;
            int th = Math.Max(0, bBottom - ty);
            int capsW = (int)Math.Round(cw * 0.40);
            bool caps = cw - capsW - gap >= P(220) && PlaceCaps(new DashRect(pad + cw - capsW, ty, capsW, th), titleBar);
            if (!caps) ResetCaps();
            Term = new DashRect(pad, ty, caps ? cw - capsW - gap : cw, th);
            return Math.Min(Chart.W - 2, Chart.H - titleBar - 1);
        }

        /// <summary>Places the 按键 card in box (title bar + caps). False (and nothing placed) when the caps would be under MinCap.</summary>
        private bool PlaceCaps(DashRect box, int titleBar)
        {
            int cg = P(6);
            DashRect inner = new DashRect(box.X + P(10), box.Y + titleBar, box.W - P(20), box.H - titleBar - P(8));
            // A: [Q][W][E] / [A][S][D] / [Shift][Space] / [LMB][RMB]    (3 caps wide, 3.5 caps tall)
            int csA = (int)Math.Floor(Math.Min((inner.W - 2 * cg) / 3.0, (inner.H - 3 * cg) / 3.5));
            // B: [Q][W][E]  [Shift][Space] / [A][S][D]  [LMB][RMB]  (5.5 caps wide, 2 caps tall)
            int csB = (int)Math.Floor(Math.Min((inner.W - 5 * cg) / 5.5, (inner.H - cg) / 2.0));
            bool two = csB > csA;
            int cs = Math.Min(two ? csB : csA, P(MaxCap));
            if (cs < P(MinCap) || inner.IsEmpty) return false;
            Caps = box; CapsInner = inner;
            CapsTitle = new DashRect(box.X + P(10), box.Y, box.W - P(20), titleBar);
            CapsShown = true; CapsTwoRows = two; CapSize = cs;
            int sh = Math.Max(P(MinCap) * 3 / 4, cs * 3 / 4);
            int comboH = LineH + P(6);
            bool combo;
            if (!two)
            {
                int bw = 3 * cs + 2 * cg, bh = 2 * cs + 2 * sh + 3 * cg;
                combo = inner.H - bh >= comboH + P(14);
                int x0 = inner.X + (inner.W - bw) / 2, y0 = inner.Y + TopGap(inner.H - bh - (combo ? comboH + P(8) : 0)); // under the title, not floating mid-card
                CapsCombo = combo ? new DashRect(inner.X, y0 + bh + P(8), inner.W, comboH) : new DashRect(0, 0, 0, 0);
                CapRects[4] = new DashRect(x0, y0, cs, cs);                         // Q
                CapRects[0] = new DashRect(x0 + cs + cg, y0, cs, cs);               // W
                CapRects[5] = new DashRect(x0 + 2 * (cs + cg), y0, cs, cs);         // E
                int y1 = y0 + cs + cg;
                CapRects[1] = new DashRect(x0, y1, cs, cs);                         // A
                CapRects[2] = new DashRect(x0 + cs + cg, y1, cs, cs);               // S
                CapRects[3] = new DashRect(x0 + 2 * (cs + cg), y1, cs, cs);         // D
                int y2 = y1 + cs + cg;
                int half = (bw - cg) / 2;
                CapRects[9] = new DashRect(x0, y2, half, sh);                       // Shift
                CapRects[6] = new DashRect(x0 + bw - half, y2, half, sh);           // Space
                int y3 = y2 + sh + cg;
                CapRects[7] = new DashRect(x0, y3, half, sh);                       // LMB
                CapRects[8] = new DashRect(x0 + bw - half, y3, half, sh);           // RMB
            }
            else
            {
                // right block (Space / LMB RMB) takes the spare width so 3-letter mouse labels fit (<= 2 caps each)
                int mw = Math.Min(2 * cs, (inner.W - (3 * cs + 4 * cg) - cg) / 2);
                if (mw < (int)Math.Floor(cs * 1.25)) mw = (int)Math.Floor(cs * 1.25);
                int rw = 2 * mw + cg;
                int bw = 3 * cs + 2 * cg + 2 * cg + rw, bh = 2 * cs + cg;
                combo = inner.H - bh >= comboH + P(14);
                int x0 = inner.X + (inner.W - bw) / 2, y0 = inner.Y + TopGap(inner.H - bh - (combo ? comboH + P(8) : 0)); // under the title, not floating mid-card
                CapsCombo = combo ? new DashRect(inner.X, y0 + bh + P(8), inner.W, comboH) : new DashRect(0, 0, 0, 0);
                int y1 = y0 + cs + cg;
                CapRects[4] = new DashRect(x0, y0, cs, cs);
                CapRects[0] = new DashRect(x0 + cs + cg, y0, cs, cs);
                CapRects[5] = new DashRect(x0 + 2 * (cs + cg), y0, cs, cs);
                CapRects[1] = new DashRect(x0, y1, cs, cs);
                CapRects[2] = new DashRect(x0 + cs + cg, y1, cs, cs);
                CapRects[3] = new DashRect(x0 + 2 * (cs + cg), y1, cs, cs);
                int xr = x0 + 3 * cs + 4 * cg;
                CapRects[9] = new DashRect(xr, y0, mw, cs);                         // Shift
                CapRects[6] = new DashRect(xr + mw + cg, y0, mw, cs);               // Space
                CapRects[7] = new DashRect(xr, y1, mw, cs);
                CapRects[8] = new DashRect(xr + mw + cg, y1, mw, cs);
            }
            return true;
        }

        /// <summary>Space above the caps block: a small fixed gap under the card title (spare height goes below the block).</summary>
        private int TopGap(int spare)
        {
            if (spare <= 0) return 0;
            return Math.Min(spare, P(6));
        }

        /// <summary>Where the 「屏幕中心」 tag goes for a text of tw x th px: in quadrant III, clear of both axes, the
        /// ring at the origin (outer edge ~12 units) and the X-axis number labels (which sit just under the axis).</summary>
        public DashRect CentreLabel(double cx, double cy, int tw, int th)
        {
            int padX = P(5), padY = P(2);
            int w = tw + 2 * padX, h = th + 2 * padY;
            int right = (int)Math.Floor(cx) - P(18);
            int top = (int)Math.Ceiling(cy) + P(22);
            return new DashRect(right - w, top, w, h);
        }

        private static int Ceil(double v) { return double.IsNaN(v) || v < 0 ? 0 : (int)Math.Ceiling(v); }

        private static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) hi = lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }

    /// <summary>Key names for the dashboard hint, key caps and terminal, built from the user's bindings.</summary>
    public static class DashKeyNames
    {
        /// <summary>Action per cap (DashKeys order); -1 = fixed W/A/S/D.</summary>
        public static readonly int[] CapAction = new int[]
        {
            -1, -1, -1, -1, KeyBindings.ActionLeanLeft, KeyBindings.ActionLeanRight, KeyBindings.ActionJump, KeyBindings.ActionFire, KeyBindings.ActionAds, -1
        };
        /// <summary>Short Chinese action names used on a cap when the key name does not fit / is not ASCII.</summary>
        public static readonly string[] CapFallback = new string[] { "W", "A", "S", "D", "侧左", "侧右", "跳跃", "开火", "开镜", "冲刺" };
        /// <summary>v4.1: the terminal's name for a key that has no short ASCII name (or one longer than MaxLogName): the Chinese
        /// action name (v4.0 used English words like "ADS" / "LEAN-L"; Leo: no English in user text except key names).</summary>
        public static readonly string[] LogFallback = new string[] { "W", "A", "S", "D", "侧左", "侧右", "跳跃", "开火", "开镜", "Shift" };
        /// <summary>Longest key name the terminal lines use as is (longer: the action name, brute-render P2-3).</summary>
        public const int MaxLogName = 12;
        /// <summary>Width budget of one try line in em (the terminal at the minimum window holds about 31 em at 10.5 pt).</summary>
        public const double TryLineEm = 30;

        /// <summary>ASCII short name ("Q", "Ctrl+D", "LMB", "M4", "Num5", "LShift"), or null when there is none.</summary>
        public static string Ascii(int vk, int mods)
        {
            string k;
            switch (vk)
            {
                case 0: return null;
                case 0x01: k = "LMB"; break;
                case 0x02: k = "RMB"; break;
                case 0x04: k = "MMB"; break;
                case 0x05: k = "M4"; break;
                case 0x06: k = "M5"; break;
                case 0x14: k = "Caps"; break;
                case 0x25: k = "Left"; break;
                case 0x26: k = "Up"; break;
                case 0x27: k = "Right"; break;
                case 0x28: k = "Down"; break;
                case 0xA0: k = "LShift"; break;
                case 0xA1: k = "RShift"; break;
                case 0xA2: k = "LCtrl"; break;
                case 0xA3: k = "RCtrl"; break;
                case 0xA4: k = "LAlt"; break;
                case 0xA5: k = "RAlt"; break;
                default:
                    if (vk >= 0x60 && vk <= 0x69) { k = "Num" + (vk - 0x60).ToString(CultureInfo.InvariantCulture); break; }
                    k = KeyBindings.KeyName(vk);
                    break;
            }
            if (string.IsNullOrEmpty(k)) return null;
            for (int i = 0; i < k.Length; i++) if (k[i] < 0x20 || k[i] > 0x7E) return null;
            int m = KeyBindings.NormalizeMods(vk, mods);
            if (m == 0) return k;
            return ((m & KeyBindings.ModCtrl) != 0 ? "Ctrl+" : "") + ((m & KeyBindings.ModShift) != 0 ? "Shift+" : "")
                + ((m & KeyBindings.ModAlt) != 0 ? "Alt+" : "") + k;
        }

        /// <summary>Name for the Chinese hint line: mouse buttons as 左键 / 右键 / 中键 / 侧键1 / 侧键2, the rest as ComboName.</summary>
        public static string Hint(int vk, int mods)
        {
            string k;
            switch (vk)
            {
                case 0x01: k = "左键"; break;
                case 0x02: k = "右键"; break;
                case 0x04: k = "中键"; break;
                case 0x05: k = "侧键1"; break;
                case 0x06: k = "侧键2"; break;
                default: return KeyBindings.ComboName(vk, mods);
            }
            int m = KeyBindings.NormalizeMods(vk, mods);
            return ((m & KeyBindings.ModCtrl) != 0 ? "Ctrl+" : "") + ((m & KeyBindings.ModShift) != 0 ? "Shift+" : "")
                + ((m & KeyBindings.ModAlt) != 0 ? "Alt+" : "") + k;
        }

        /// <summary>v5.0.2 (UI L4): mouse buttons are named like the header line, the 按键 page and the README (左键 / 右键 /
        /// 中键 / 侧键1 / 侧键2) on the caps, in the terminal and in the try lines; null for any other key.</summary>
        public static string MouseName(int vk, int mods)
        {
            if (vk < 0x01 || vk > 0x06 || vk == 0x03) return null;
            return Hint(vk, mods);
        }

        /// <summary>Changes when any key shown on the dashboard is rebound (cheap: no allocation).</summary>
        public static int Signature(KeyBindings k)
        {
            if (k == null) return 0;
            int h = 17;
            for (int i = 4; i < CapAction.Length; i++)
            {
                int a = CapAction[i];
                if (a < 0) continue;
                h = unchecked(h * 31 + k.Get(a));
                h = unchecked(h * 31 + k.GetMods(a));
            }
            return h;
        }

        /// <summary>Label of cap i (DashKeys order): the ASCII key name, else the action name. ascii = false means the
        /// label is Chinese (draw it with the UI font, not the monospace one).</summary>
        public static string Cap(KeyBindings k, int i, out bool ascii)
        {
            ascii = true;
            if (i < 0 || i >= DashKeys.Count) return "";
            int a = CapAction[i];
            if (a < 0 || k == null) return DashKeys.Names[i];
            string mouse = MouseName(k.Get(a), k.GetMods(a));
            if (mouse != null) { ascii = false; return mouse; } // Chinese: the UI font draws it
            string n = Ascii(k.Get(a), k.GetMods(a));
            if (n != null) return n;
            ascii = false;
            return CapFallback[i];
        }

        /// <summary>Header hint, e.g. 「按 W A S D 移动 · Q / E 侧身 · Space 跳跃 · 左键开火 · 右键开镜，准星实时跟着动」.</summary>
        public static string HintLine(KeyBindings k)
        {
            if (k == null) return "按 W A S D 移动，准星实时跟着动";
            return "按 W A S D 移动 · " + Hint(k.Get(KeyBindings.ActionLeanLeft), k.GetMods(KeyBindings.ActionLeanLeft)) + " / "
                + Hint(k.Get(KeyBindings.ActionLeanRight), k.GetMods(KeyBindings.ActionLeanRight)) + " 侧身 · "
                + Hint(k.Get(KeyBindings.ActionJump), k.GetMods(KeyBindings.ActionJump)) + " 跳跃 · "
                + Hint(k.Get(KeyBindings.ActionFire), k.GetMods(KeyBindings.ActionFire)) + " 开火 · "
                + Hint(k.Get(KeyBindings.ActionAds), k.GetMods(KeyBindings.ActionAds)) + " 开镜，准星实时跟着动";
        }

        /// <summary>v5.0.2 (UI L16): the header hint for a narrow window, e.g. 「W A S D 移动 · Q / E 侧身 · Space 跳跃 · 左键开火 ·
        /// 右键开镜」 (no 「按」, no 「，准星实时跟着动」).</summary>
        public static string HintLineShort(KeyBindings k)
        {
            if (k == null) return "W A S D 移动";
            return "W A S D 移动 · " + Hint(k.Get(KeyBindings.ActionLeanLeft), k.GetMods(KeyBindings.ActionLeanLeft)) + " / "
                + Hint(k.Get(KeyBindings.ActionLeanRight), k.GetMods(KeyBindings.ActionLeanRight)) + " 侧身 · "
                + Hint(k.Get(KeyBindings.ActionJump), k.GetMods(KeyBindings.ActionJump)) + " 跳跃 · "
                + Hint(k.Get(KeyBindings.ActionFire), k.GetMods(KeyBindings.ActionFire)) + " 开火 · "
                + Hint(k.Get(KeyBindings.ActionAds), k.GetMods(KeyBindings.ActionAds)) + " 开镜";
        }

        /// <summary>Key name per cap for the terminal (DashKeys order): W A S D, the bound ASCII names ("Ctrl+D", "M4")
        /// or the Chinese action name (「开镜」) when a key has none or its name is longer than MaxLogName, and Shift.</summary>
        public static string[] LogNames(KeyBindings k)
        {
            string[] n = new string[DashKeys.Count];
            for (int i = 0; i < DashKeys.Count; i++)
            {
                int a = CapAction[i];
                if (a < 0 || k == null) { n[i] = DashKeys.Names[i]; continue; }
                string mouse = MouseName(k.Get(a), k.GetMods(a));
                if (mouse != null && mouse.Length <= MaxLogName) { n[i] = mouse; continue; }
                string s = Ascii(k.Get(a), k.GetMods(a));
                n[i] = s != null && s.Length <= MaxLogName ? s : LogFallback[i];
            }
            return n;
        }

        /// <summary>Approximate width in em of a terminal / HUD text: CJK and full-width characters 1 em, the rest 0.55 em.</summary>
        public static double EstimateEm(string t)
        {
            if (t == null) return 0;
            double w = 0;
            for (int i = 0; i < t.Length; i++) w += t[i] > 0x2E7F ? 1.0 : 0.55;
            return w;
        }

        /// <summary>Name of action key i for the try lines: the short ASCII name, else the Chinese key name (「小键盘*」), so a
        /// line never reads 「开镜 开镜」.</summary>
        private static string TryName(KeyBindings k, int i)
        {
            int a = CapAction[i];
            if (a < 0 || k == null) return DashKeys.Names[i];
            string mouse = MouseName(k.Get(a), k.GetMods(a));
            if (mouse != null) return mouse;
            string s = Ascii(k.Get(a), k.GetMods(a));
            if (s != null) return s;
            string h = Hint(k.Get(a), k.GetMods(a));
            return string.IsNullOrEmpty(h) ? LogFallback[i] : h;
        }

        /// <summary>Terminal try lines, e.g. "> 试一试：W A S D 移动 | Q E 侧身 | Space 跳跃" and "> LMB 开火 | RMB 开镜 | Shift+W 冲刺".
        /// v4.1 (brute-render P2-3): the parts are packed greedily into lines of at most TryLineEm, so long custom bindings
        /// go onto extra lines (2..6 lines) instead of running off the terminal; a single part longer than a line is drawn
        /// with an ellipsis by the panel.</summary>
        public static string[] TryLines(KeyBindings k)
        {
            string[] parts = new string[] {
                "试一试：W A S D 移动",
                TryName(k, 4) + " " + TryName(k, 5) + " 侧身",
                TryName(k, 6) + " 跳跃",
                TryName(k, 7) + " 开火",
                TryName(k, 8) + " 开镜",
                "Shift+W 冲刺" };
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>(4);
            string cur = "> " + parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + " | " + parts[i];
                if (EstimateEm(next) <= TryLineEm) cur = next;
                else { lines.Add(cur); cur = "> " + parts[i]; }
            }
            lines.Add(cur);
            return lines.ToArray();
        }
    }

    // =================================================================================================== v4.1 repaint + HUD maths

    /// <summary>
    /// v4.1 (DASH41F) dirty rectangles of one frame, in pixels: the panel repaints these instead of the whole client.
    /// Add merges a rectangle into every one it overlaps or touches (2 px); when more than Max would remain, the pair whose
    /// union grows least is merged. SetAll = the whole client. No allocation.
    /// </summary>
    public sealed class DashDirty
    {
        public const int Max = 8;
        private readonly DashRect[] r = new DashRect[Max];
        private int n;
        private bool all;

        public int Count { get { return n; } }
        /// <summary>True after SetAll: repaint everything (Count is then 0).</summary>
        public bool All { get { return all; } }
        public DashRect this[int i] { get { return r[i]; } }

        public void Clear() { n = 0; all = false; }
        public void SetAll() { all = true; n = 0; }

        /// <summary>Adds a rectangle (empty ones are ignored; nothing is kept after SetAll).</summary>
        public void Add(DashRect a)
        {
            if (all || a.IsEmpty) return;
            for (; ; )
            {
                int hit = -1;
                for (int i = 0; i < n; i++) if (Near(r[i], a)) { hit = i; break; }
                if (hit < 0) break;
                a = Union(r[hit], a);
                r[hit] = r[n - 1];
                n--;
            }
            if (n < Max) { r[n++] = a; return; }
            int best = 0;
            long grow = long.MaxValue;
            for (int i = 0; i < n; i++)
            {
                long gi = Area(Union(r[i], a)) - Area(r[i]);
                if (gi < grow) { grow = gi; best = i; }
            }
            DashRect u = Union(r[best], a);
            r[best] = r[n - 1];
            n--;
            Add(u);
        }

        /// <summary>Bounding box of everything added (empty when nothing).</summary>
        public DashRect Bounds()
        {
            if (n == 0) return new DashRect(0, 0, 0, 0);
            DashRect b = r[0];
            for (int i = 1; i < n; i++) b = Union(b, r[i]);
            return b;
        }

        public static DashRect Union(DashRect a, DashRect b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
            int x1 = Math.Max(a.Right, b.Right), y1 = Math.Max(a.Bottom, b.Bottom);
            return new DashRect(x0, y0, x1 - x0, y1 - y0);
        }

        private static bool Near(DashRect a, DashRect b)
        {
            return a.X <= b.Right + 2 && b.X <= a.Right + 2 && a.Y <= b.Bottom + 2 && b.Y <= a.Bottom + 2;
        }

        private static long Area(DashRect a) { return (long)a.W * a.H; }
    }

    /// <summary>
    /// v4.1 HUD widgets in the chart's corners: 0 = top-left 「∑ 运动方程」, 1 = top-right 「⇄ 换算」, 2 = bottom-right 「≡ 计算流」,
    /// 3 = bottom-left 「∫ 曲线」. Each sits in its quadrant's corner: inside the plot, under (top) / above (bottom) the
    /// quadrant-tag row, clear of both axes and their number labels and never over the 屏幕中心 tag or another widget. A widget
    /// wider than MaxShareW of its quadrant is hidden; lines that do not fit (or would pass MaxShareH of the quadrant) are
    /// dropped from the end, and a widget left with fewer than its minimum is hidden. Pure: tests brute-force it.
    /// </summary>
    public static class DashHud
    {
        public const int Count = 4;
        public const double MaxShareW = 0.80, MaxShareH = 0.80;

        /// <param name="inner">the plot rectangle (ChartInner)</param>
        /// <param name="cx">screen centre x in px (the vertical axis)</param>
        /// <param name="cy">screen centre y in px (the horizontal axis)</param>
        /// <param name="tagH">height reserved at the top and bottom for the quadrant tags (Ⅱ 左上 ...)</param>
        /// <param name="bandX">height under the horizontal axis used by the X-axis number labels</param>
        /// <param name="bandY">width right of the vertical axis used by the Y-axis number labels</param>
        /// <param name="gap">clearance to the axes, the edges and the 屏幕中心 tag</param>
        /// <param name="titleH">widget title row height</param>
        /// <param name="lineH">widget line height</param>
        /// <param name="pad">widget inner padding (top + bottom each)</param>
        /// <param name="needW">wanted widget width per corner (px, padding included)</param>
        /// <param name="wantLines">wanted line count per corner</param>
        /// <param name="minLines">fewest lines that are still worth showing</param>
        /// <param name="plate">the 屏幕中心 tag rectangle (empty = none)</param>
        /// <param name="rects">out: widget rectangles (empty = hidden)</param>
        /// <param name="lines">out: lines shown per widget</param>
        public static void Place(DashRect inner, double cx, double cy, int tagH, int bandX, int bandY, int gap, int titleH, int lineH, int pad,
            int[] needW, int[] wantLines, int[] minLines, DashRect plate, DashRect[] rects, int[] lines)
        {
            for (int i = 0; i < Count; i++) { rects[i] = new DashRect(0, 0, 0, 0); lines[i] = 0; }
            if (inner.IsEmpty || lineH <= 0 || double.IsNaN(cx) || double.IsNaN(cy)) return;
            int icx = (int)Math.Floor(cx), icy = (int)Math.Floor(cy);
            if (icx <= inner.X || icx >= inner.Right || icy <= inner.Y || icy >= inner.Bottom) return;
            DashRect blocked = plate.IsEmpty ? plate : new DashRect(plate.X - gap, plate.Y - gap, plate.W + 2 * gap, plate.H + 2 * gap);
            int rx0, rx1, ry0, ry1, maxH;
            double maxW;
            for (int i = 0; i < Count; i++)
            {
                bool left = i == 0 || i == 3, top = i == 0 || i == 1;
                Box(i, inner, icx, icy, tagH, bandX, bandY, gap, out rx0, out rx1, out ry0, out ry1, out maxW, out maxH);
                int w = needW[i];
                if (w <= 0 || w > rx1 - rx0 || w > maxW) continue;
                int n = wantLines[i];
                for (; n >= minLines[i] && n > 0; n--)
                {
                    int h = titleH + n * lineH + 2 * pad;
                    if (h > maxH) continue;
                    int x = left ? rx0 : rx1 - w;
                    int y = top ? ry0 : ry1 - h;
                    DashRect b = new DashRect(x, y, w, h);
                    if (b.Intersects(blocked)) continue;
                    bool clash = false;
                    for (int j = 0; j < i; j++) if (!rects[j].IsEmpty && rects[j].Intersects(b)) clash = true;
                    if (clash) continue;
                    rects[i] = b;
                    lines[i] = n;
                    break;
                }
            }
            // v4.1 (DASH41G) one grid: the two widgets of a column take the wider one's width (inner edges line up), then the two
            // of a row the taller one's height (tops / bottoms line up; a taller box shows more of its lines, if it has them).
            // A box only grows where it still keeps every rule above; else it stays as placed. No allocation.
            for (int pass = 0; pass < 4; pass++)
            {
                int a = pass == 0 ? 0 : (pass == 1 ? 1 : (pass == 2 ? 0 : 3));   // columns (0,3) (1,2), rows (0,1) (3,2)
                int c = pass == 0 ? 3 : (pass == 1 ? 2 : (pass == 2 ? 1 : 2));
                if (rects[a].IsEmpty || rects[c].IsEmpty) continue;
                bool widths = pass < 2;
                int k = widths ? (rects[a].W < rects[c].W ? a : c) : (rects[a].H < rects[c].H ? a : c);
                int o = k == a ? c : a;
                DashRect r = rects[k];
                bool left = k == 0 || k == 3, top = k == 0 || k == 1;
                Box(k, inner, icx, icy, tagH, bandX, bandY, gap, out rx0, out rx1, out ry0, out ry1, out maxW, out maxH);
                DashRect grown;
                if (widths)
                {
                    int nw = rects[o].W;
                    if (r.W >= nw || nw > maxW) continue;
                    grown = left ? new DashRect(r.X, r.Y, nw, r.H) : new DashRect(r.Right - nw, r.Y, nw, r.H);
                }
                else
                {
                    int nh = rects[o].H;
                    if (r.H >= nh || nh > maxH) continue;
                    grown = top ? new DashRect(r.X, r.Y, r.W, nh) : new DashRect(r.X, r.Bottom - nh, r.W, nh);
                }
                if (grown.X < rx0 || grown.Right > rx1 || grown.Y < ry0 || grown.Bottom > ry1) continue;
                if (grown.Intersects(blocked)) continue;
                bool hit = false;
                for (int j = 0; j < Count; j++) if (j != k && !rects[j].IsEmpty && rects[j].Intersects(grown)) hit = true;
                if (hit) continue;
                rects[k] = grown;
                if (!widths) lines[k] = Math.Min(wantLines[k], Math.Max(lines[k], (grown.H - titleH - 2 * pad) / lineH));
            }
        }

        /// <summary>The area widget i may use (clear of the axes, their labels, the edges and the quadrant tags) and its size caps
        /// (MaxShareW / MaxShareH of its quadrant).</summary>
        private static void Box(int i, DashRect inner, int icx, int icy, int tagH, int bandX, int bandY, int gap,
            out int rx0, out int rx1, out int ry0, out int ry1, out double maxW, out int maxH)
        {
            bool left = i == 0 || i == 3, top = i == 0 || i == 1;
            int qx0 = left ? inner.X : icx + 1, qx1 = left ? icx : inner.Right;
            int qy0 = top ? inner.Y : icy + 1, qy1 = top ? icy : inner.Bottom;
            rx0 = left ? inner.X + gap : icx + bandY + gap;
            rx1 = left ? icx - gap : inner.Right - gap;
            ry0 = top ? inner.Y + tagH : icy + bandX + gap;
            ry1 = top ? icy - gap : inner.Bottom - tagH;
            maxW = (qx1 - qx0) * MaxShareW;
            maxH = Math.Min(ry1 - ry0, (int)((qy1 - qy0) * MaxShareH));
        }
    }

    /// <summary>
    /// v4.1 HUD maths for one movement axis. Tracks where the current travel started (m0) so the 运动方程 can show
    /// m = m0 + (m* − m0)·c(u) with the real numbers: u = t / T of the travel, c = MotionCurve.Progress (the engine's own
    /// call), m* = the axis target. Exact says whether those numbers reproduce the engine's position; when a restart was
    /// missed the panel shows only the engine's value, never a wrong equation.
    /// </summary>
    public sealed class DashTravel
    {
        public double Start, Target, U = 1, C = 1, Move;
        /// <summary>v4.1 (DASH41G): t and T of u = t / T in ms (the travel's time so far, the curve's 总时长); t = T when still.</summary>
        public double Ms, Total;
        public bool Active, Exact = true;
        /// <summary>Travels seen starting (for the HUD's axis choice and the tests).</summary>
        public int Restarts;
        private bool lastTraveling, has;
        private double lastMs, lastMove;

        /// <summary>Call once after every MotionEngine.Update: traveling = Traveling(axis), ms = TravelMs(axis), move = MoveX/MoveY,
        /// dtMs = that update's dt in ms, curve = CurveAD (X) / CurveWS (Y), target = the axis target for the current keys.</summary>
        public void Step(bool traveling, double ms, double move, double dtMs, MoveCurve curve, double target)
        {
            if (double.IsNaN(move) || double.IsInfinity(move)) move = 0;
            if (double.IsNaN(target) || double.IsInfinity(target)) target = move;
            if (double.IsNaN(ms) || ms < 0) ms = 0;
            double d = double.IsNaN(dtMs) || dtMs < 0 ? 0 : Math.Min(dtMs, 100); // the engine clamps dt to 0.1 s
            if (!has) { has = true; lastMove = move; Start = move; }
            // the engine restarts a travel with from = the previous position and travelMs = 0, then adds this frame's dt, so
            // right after a restart travelMs == dt exactly (it never equals dt again during one travel: it only grows)
            bool restarted = traveling && (!lastTraveling || Math.Abs(ms - d) <= 1e-9 * (1 + d) || ms < lastMs);
            if (restarted) { Start = lastMove; Restarts++; }
            Active = traveling;
            Target = target;
            Move = move;
            double total = curve == null ? 0 : curve.DurationMs;
            if (double.IsNaN(total) || double.IsInfinity(total) || total < 0) total = 0;
            Total = total;
            if (traveling && total > 0)
            {
                U = ms / total;
                if (U < 0) U = 0;
                if (U > 1) U = 1;
                C = MotionCurve.Progress(curve.Speeds, U);
                Ms = U * total;
            }
            else { U = 1; C = 1; Ms = total; }
            double model = Start + (Target - Start) * C;
            double tol = 0.01 + 1e-9 * (Math.Abs(Start) + Math.Abs(Target));
            Exact = !double.IsNaN(model) && Math.Abs(model - move) <= tol;
            lastTraveling = traveling;
            lastMs = ms;
            lastMove = move;
        }

        /// <summary>v5.1 (W26, decision 7): one step from the engine's own running leg (MotionEngine.LegStart / LegEnd / LegCurve
        /// / LegMs / LegEntry). Since v5.1 a travel runs in slow-start legs (short taps, A to D back to 0 first, entry part-way up
        /// the curve), so the HUD reads the leg instead of re-deriving it: m0 = LegStart, m* = LegEnd (0 on the first leg of a
        /// reversal), c = LegCurve, u = LegEntry + (1 - LegEntry) * t / LegMs, t / T = TravelMs / LegMs.</summary>
        public void StepLeg(MotionEngine e, int axis)
        {
            if (e == null) return;
            bool traveling = e.Traveling(axis);
            double move = axis == 0 ? e.MoveX : e.MoveY;
            double start = e.LegStart(axis), end = e.LegEnd(axis), c = e.LegCurve(axis);
            double ms = e.TravelMs(axis), dur = e.LegMs(axis), u0 = e.LegEntry(axis);
            if (double.IsNaN(move) || double.IsInfinity(move)) move = 0;
            if (double.IsNaN(start) || double.IsInfinity(start)) start = move;
            if (double.IsNaN(end) || double.IsInfinity(end)) end = move;
            if (double.IsNaN(c) || double.IsInfinity(c)) c = 1;
            if (double.IsNaN(ms) || ms < 0) ms = 0;
            if (double.IsNaN(dur) || double.IsInfinity(dur) || dur < 0) dur = 0;
            if (double.IsNaN(u0) || u0 < 0) u0 = 0;
            if (u0 > 1) u0 = 1;
            if (!has) { has = true; lastMove = move; }
            // a new leg: travelling starts, the leg's start / end moves, or its clock went back
            bool newLeg = traveling && (!lastTraveling || ms < lastMs || start != Start || end != lastEnd);
            if (newLeg) Restarts++;
            Active = traveling;
            Start = start; Target = end; Move = move;
            if (traveling && dur > 0)
            {
                double v = ms / dur;
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                U = u0 + (1 - u0) * v;
                C = c < 0 ? 0 : (c > 1 ? 1 : c);
                Ms = Math.Min(ms, dur);
                Total = dur;
            }
            else { U = 1; C = 1; Ms = Total = dur; if (!traveling) { Start = move; Target = move; } }
            double model = Start + (Target - Start) * C;
            double tol = 0.01 + 1e-9 * (Math.Abs(Start) + Math.Abs(Target));
            Exact = !double.IsNaN(model) && Math.Abs(model - move) <= tol;
            lastTraveling = traveling; lastMs = ms; lastMove = move; lastEnd = end;
        }

        private double lastEnd;

        public void Reset() { has = false; lastTraveling = false; lastMs = 0; Active = false; Exact = true; U = 1; C = 1; Restarts = 0; Ms = Total = 0; }
    }

    /// <summary>v4.1 「≡ 计算流」: the last few computed samples (a ring of strings, newest last).</summary>
    public sealed class DashStream
    {
        private readonly string[] lines;
        private int head, count, version;
        public readonly int Capacity;

        public DashStream(int capacity)
        {
            if (capacity < 1) capacity = 1;
            Capacity = capacity;
            lines = new string[capacity];
        }

        public int Count { get { return count; } }
        public int Version { get { return version; } }

        public void Add(string s)
        {
            int idx;
            if (count < Capacity) { idx = (head + count) % Capacity; count++; }
            else { idx = head; head = (head + 1) % Capacity; }
            lines[idx] = s ?? "";
            version++;
        }

        /// <summary>i = 0 is the oldest.</summary>
        public string Line(int i)
        {
            if (i < 0 || i >= count) throw new ArgumentOutOfRangeException("i");
            return lines[(head + i) % Capacity];
        }

        public void Clear() { head = 0; count = 0; version++; }

        /// <summary>Sample with the 1440 reference picture (no settings).</summary>
        public static string Sample(double seconds, double x, double y) { return Sample(seconds, x, y, null); }

        /// <summary>One sample, v4.1 (DASH41G, DOCCHECK41 D2) in 百分比偏移: 「0.42s ← 3.64% ↑ 0.42%」 = the time since the key state
        /// began, then X and Y of the ring offset (x / y = SCREEN px in UI coords: X right +, Y up +; g = the settings for the %,
        /// null = the 1440 reference). An axis at 0.00 % keeps its column (「  0.00%」); |%| is capped at 9999.99 for the box.</summary>
        public static string Sample(double seconds, double x, double y, GlobalSettings g)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            if (seconds > 999.99) seconds = 999.99;
            return DashFormat.Fixed(seconds, 2) + "s " + Column(DashStretch.ScreenXToPct(x, g), OffsetAxis.X) + " " + Column(OffsetUnits.ScreenToPct(y, g), OffsetAxis.Y);
        }

        private static string Column(double pct, OffsetAxis axis)
        {
            if (pct > 9999.99) pct = 9999.99;
            if (pct < -9999.99) pct = -9999.99;
            string s = OffsetUnits.FormatPct(pct, axis);
            return s.Length < 7 ? s.PadLeft(7) : s;   // 「0.00%」 (no arrow) is as wide as 「← 4.17%」
        }
    }

    /// <summary>
    /// v4.1 HUD text of 「∑ 运动方程」 and 「⇄ 换算」, from plain numbers (the panel reads them from the engine and the settings).
    /// Each line is cached (DashLine): a line allocates only when a number on it changes. All lines are monospace-safe
    /// (ASCII + · ÷ ° τ θ ← → ↑ ↓ Σ, all in Consolas / Cascadia Mono).
    /// </summary>
    public sealed class DashMathText
    {
        public const int ALines = 6, BLines = 6;   // v4.1 (DASH41G): A gained 「t = … s  T = … s」, so both top boxes are one height
        public const string AFormula = "m = m0+(m*-m0)·c";
        public const string AFormulaInexact = "m → m*";
        public const string BFormula = "% = 100·v÷(H/2)";
        /// <summary>v4.3: BFormula without spaces, for the stretched form 「%=100·v÷(H/2) X÷1.33」 (20 chars).</summary>
        public const string BFormulaShort = "%=100·v÷(H/2)";
        public readonly DashLine[] A = new DashLine[ALines];
        public readonly DashLine[] B = new DashLine[BLines];

        public DashMathText()
        {
            for (int i = 0; i < ALines; i++) A[i] = new DashLine();
            for (int i = 0; i < BLines; i++) B[i] = new DashLine();
        }

        /// <summary>UpdateA with the 1440 reference picture (no settings).</summary>
        public bool UpdateA(char axis, DashTravel t) { return UpdateA(axis, t, null); }

        /// <summary>「∑ 运动方程」 for one axis ('X' or 'Y'), most important line first (lines that do not fit are dropped from
        /// the end): [0] the formula, [1] its value, [2] u = t/T, [3] c(u), [4] m0 and m*, [5] t and T. v4.1 (DASH41G, DOCCHECK41
        /// D1): m, m0 and m* in 百分比偏移 with arrows, e.g. 「  = ← 4.17%」, 「m0=→ 7.37% m*=← 4.17%」. t's numbers are PROFILE px
        /// (MotionEngine.MoveX / MoveY and the axis target: X + right, Y + up; the engine multiplies them by k only for the
        /// screen), converted with OffsetUnits.ToPct; g = the settings for the % (null = the 1440 reference). True when any line
        /// changed.</summary>
        public bool UpdateA(char axis, DashTravel t, GlobalSettings g)
        {
            return UpdateA(axis, t, g, false);
        }

        /// <summary>v5.1.1 (X5): UpdateA with the hand shown (switched = 换手: the X FOV sides swap).</summary>
        public bool UpdateA(char axis, DashTravel t, GlobalSettings g, bool switched)
        {
            bool ch = false;
            long ex = t.Exact ? 1 : 0;
            long ax = axis == 'Y' ? 1 : 0;
            OffsetAxis oa = axis == 'Y' ? OffsetAxis.Y : OffsetAxis.X;
            double pm = OffsetUnits.ToPct(t.Move, oa, switched, g), p0 = OffsetUnits.ToPct(t.Start, oa, switched, g), pt = OffsetUnits.ToPct(t.Target, oa, switched, g);
            if (!A[0].Same(ex, 0, 0)) { A[0].Text = t.Exact ? AFormula : AFormulaInexact; ch = true; }
            if (!A[1].Same(DashFormat.PctKey(pm), ax, 0)) { A[1].Text = "  = " + OffsetUnits.FormatPct(pm, oa); ch = true; }
            if (!A[2].Same(ax, DashFormat.Hundredths(t.U), 0)) { A[2].Text = (axis == 'Y' ? "Y" : "X") + ": u = t/T = " + DashFormat.Fixed(t.U, 2); ch = true; }
            long ck = (long)Math.Round((double.IsNaN(t.C) ? 0 : t.C) * 1000.0);
            if (!A[3].Same(ck, 0, 0)) { A[3].Text = "c(u) = " + (t.C < 0 ? "-" : "") + DashFormat.Fixed(t.C, 3); ch = true; }
            if (!A[4].Same(t.Exact ? DashFormat.PctKey(p0) : long.MinValue, DashFormat.PctKey(pt), ex * 2 + ax))
            {
                A[4].Text = t.Exact ? "m0=" + OffsetUnits.FormatPct(p0, oa) + " m*=" + OffsetUnits.FormatPct(pt, oa) : "m* = " + OffsetUnits.FormatPct(pt, oa);
                ch = true;
            }
            if (!A[5].Same(DashFormat.Hundredths(t.Ms / 1000.0), DashFormat.Hundredths(t.Total / 1000.0), 0))
            { A[5].Text = "t = " + DashFormat.Fixed(t.Ms / 1000.0, 2) + " s  T = " + DashFormat.Fixed(t.Total / 1000.0, 2) + " s"; ch = true; }
            return ch;
        }

        /// <summary>「⇄ 换算」, most important line first: [0] the formula % = 100·v ÷ (H/2), [1] its value in 百分比偏移,
        /// [2] v in SCREEN px, [3] k and H/2, [4] the FOV ratio τ with its value, [5] |v| and θ. x / y = the ring offset in SCREEN px (UI
        /// coords, Y up +), pctX / pctY = the same in %, k = ViewScale.Factor, halfH = half the picture height (px), baseFov /
        /// gameFov in degrees. True when any line changed.</summary>
        public bool UpdateB(double x, double y, double pctX, double pctY, double k, double halfH, double baseFov, double gameFov)
        {
            return UpdateB(x, y, pctX, pctY, k, halfH, baseFov, gameFov, 1.0);
        }

        /// <summary>v4.3 (W6): UpdateB for a stretched 画面比例. sx = DashStretch.Ratio (X stretch, 1 = native); the caller's
        /// pctX = 100·v.x ÷ sx ÷ (H/2), the same 百分比偏移 the settings pages show. Stretched: [0] reads 「%=100·v÷(H/2) X÷1.33」
        /// and [3] 「k=1.000 ×1.33 H/2=540」. sx within 0.0005 of 1 gives exactly the v4.1 lines.</summary>
        public bool UpdateB(double x, double y, double pctX, double pctY, double k, double halfH, double baseFov, double gameFov, double sx)
        {
            bool ch = false;
            if (double.IsNaN(sx) || double.IsInfinity(sx) || !(sx > 0)) sx = 1.0;
            bool st = Math.Abs(sx - 1.0) >= 0.0005;
            long sk = st ? (long)Math.Round(sx * 100.0) : 100;
            if (!B[0].Same(sk, 0, 0)) { B[0].Text = st ? BFormulaShort + " X÷" + DashFormat.Fixed(sx, 2) : BFormula; ch = true; }
            long kx = DashFormat.PctKey(pctX), ky = DashFormat.PctKey(pctY);
            if (!B[1].Same(kx, ky, 0))
            { B[1].Text = "  = " + OffsetUnits.FormatPct(pctX, OffsetAxis.X) + "  " + OffsetUnits.FormatPct(pctY, OffsetAxis.Y); ch = true; }
            if (!B[2].Same(DashFormat.Hundredths(x), DashFormat.Hundredths(y), 0))
            { B[2].Text = "v = (" + DashFormat.Signed2(x) + ", " + DashFormat.Signed2(y) + ")"; ch = true; }
            long kk = (long)Math.Round((double.IsNaN(k) ? 0 : k) * 1000.0), kh = (long)Math.Round(double.IsNaN(halfH) ? 0 : halfH);
            if (!B[3].Same(kk, kh, sk))
            {
                B[3].Text = st ? "k=" + DashFormat.Fixed(k, 3) + " ×" + DashFormat.Fixed(sx, 2) + " H/2=" + DashFormat.Fixed(halfH, 0)
                               : "k = " + DashFormat.Fixed(k, 3) + "  H/2 = " + DashFormat.Fixed(halfH, 0);
                ch = true;
            }
            long fb = (long)Math.Round((double.IsNaN(baseFov) ? 0 : baseFov) * 10.0), fg = (long)Math.Round((double.IsNaN(gameFov) ? 0 : gameFov) * 10.0);
            if (!B[4].Same(fb, fg, 0))
            {   // v5.1.1 (X5): the FOV rule in words, e.g. 「视野换算 枪侧×1.00 另侧×0.80」 (the old tan ratio τ is gone: Leo measured it
                // wrong); at most 25 chars
                B[4].Text = FovText.Short(ViewScale.FovRatio(gameFov, baseFov));
                ch = true;
            }
            double mag = Math.Sqrt(pctX * pctX + pctY * pctY);
            long km = DashFormat.Hundredths(mag);
            long kt = km == 0 ? -1 : (long)Math.Round(Angle(pctX, pctY));
            if (!B[5].Same(km, kt, 0))
            { B[5].Text = "|v| = " + DashFormat.Fixed(mag, 2) + "%  θ = " + (km == 0 ? "—" : kt.ToString(CultureInfo.InvariantCulture) + "°"); ch = true; }
            return ch;
        }

        /// <summary>v4.1 tan(baseFov / 2) / tan(gameFov / 2). v5.1.1 (X5): no longer used by the conversion or the read-out (the
        /// FOV rule is ViewScale.FovX); kept for old callers.</summary>
        public static double TanRatio(double baseFov, double gameFov)
        {
            if (!(gameFov > 0) || !(baseFov > 0) || gameFov >= 180 || baseFov >= 180) return 1.0;
            return gameFov == baseFov ? 1.0 : Math.Tan(baseFov * Math.PI / 360.0) / Math.Tan(gameFov * Math.PI / 360.0);
        }

        /// <summary>Direction in degrees, 0 = right, counter-clockwise (UI Y up +), 0..359.x.</summary>
        public static double Angle(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return 0;
            double a = Math.Atan2(y, x) * 180.0 / Math.PI;
            if (a < 0) a += 360.0;
            if (a >= 359.5) a -= 360.0; // so rounding never shows 360°
            return a;
        }

        public void Invalidate()
        {
            for (int i = 0; i < ALines; i++) A[i].Invalidate();
            for (int i = 0; i < BLines; i++) B[i].Invalidate();
        }
    }

    /// <summary>
    /// v4.3 (W6) 拉伸 on the dashboard. The overlay draws X offsets × ViewScale.FactorX and Y × ViewScale.Factor (Motion.cs); the
    /// settings pages and tutorials show PROFILE values through OffsetUnits (Factor only). So a SCREEN X px is divided by the
    /// stretch ratio FactorX ÷ Factor before it becomes a %: every page then shows the same number for the same offset
    /// (4:3 on a 16:9 screen: D −30 px = 「← 4.17%」 everywhere, while the ring sits ×1.33 further left on the screen).
    /// Native ratio: exactly 1, so the v4.1 numbers are unchanged bit for bit. UNVERIFIED: the stretch model itself (W1).
    /// </summary>
    public static class DashStretch
    {
        /// <summary>FactorX ÷ Factor (1 when native, missing settings, or anything not finite / positive).</summary>
        public static double Ratio(GlobalSettings g)
        {
            if (g == null) return 1.0;
            double k, kx;
            try { k = ViewScale.Factor(g); kx = ViewScale.FactorX(g); }
            catch (Exception) { return 1.0; }
            if (!(k > 0) || !(kx > 0) || double.IsInfinity(k) || double.IsInfinity(kx)) return 1.0;
            if (kx == k) return 1.0;
            double r = kx / k;
            return (r > 0 && !double.IsInfinity(r) && !double.IsNaN(r)) ? r : 1.0;
        }

        /// <summary>SCREEN X px (MotionEngine.OffsetX) → the same 百分比偏移 the settings pages show.</summary>
        public static double ScreenXToPct(double screenX, GlobalSettings g)
        {
            return ScreenXToPct(screenX, g, Ratio(g));
        }

        /// <summary>ScreenXToPct with a cached Ratio (the panel caches it with the other settings, twice a second).</summary>
        public static double ScreenXToPct(double screenX, GlobalSettings g, double ratio)
        {
            if (!(ratio > 0) || double.IsInfinity(ratio) || double.IsNaN(ratio)) ratio = 1.0;
            return OffsetUnits.ScreenToPct(ratio == 1.0 ? screenX : screenX / ratio, g);
        }
    }

    /// <summary>
    /// v4.3 (W6, V43-PERF): when the dashboard's timer may run at all. It stops COMPLETELY (no tick, no repaint) while the page
    /// or its window is hidden / minimised, while another program (e.g. the game) is in front, during a window drag (Pause) or
    /// after the tick failed; the panel starts it again on the window's Activated / VisibleChanged / Resize and its own
    /// VisibleChanged. fg = Native.ForegroundIsOurs(): 1 ours, 0 another program, -1 unknown (treated as ours).
    /// </summary>
    public static class DashWake
    {
        public static bool ShouldRun(bool pageVisible, bool hasHandle, bool formVisible, bool minimized, int fg, bool paused, bool failed, bool disposed)
        {
            if (disposed || failed || paused || !hasHandle) return false;
            if (!pageVisible || !formVisible || minimized) return false;
            return fg != 0;
        }
    }

    /// <summary>
    /// v4.3 (W6): the dashboard header line 「滤镜：开 · 通用」 / 「滤镜：关 · 黑夜模式」 / 「滤镜：未启动」, from FilterHost's public state
    /// (Started, IsOn, PresetName; the panel polls it twice a second). Text is a new string only when the state changes.
    /// </summary>
    public sealed class DashFilterLine
    {
        public const string Prefix = "滤镜：", On = "开", Off = "关", NotStarted = "滤镜：未启动", Sep = " · ";
        /// <summary>The widest line the header reserves room for (the longest built-in preset name).</summary>
        public const string Widest = "滤镜：关 · 黑夜模式";
        public string Text = NotStarted;
        private int state = -1;
        private string preset;

        /// <summary>True when Text changed. preset null / empty shows no 「 · 」 part.</summary>
        public bool Update(bool started, bool on, string preset)
        {
            int st = !started ? 0 : (on ? 2 : 1);
            if (st == 0) preset = null;
            if (st == state && string.Equals(preset, this.preset, StringComparison.Ordinal)) return false;
            state = st; this.preset = preset;
            Text = Format(started, on, preset);
            return true;
        }

        public static string Format(bool started, bool on, string preset)
        {
            if (!started) return NotStarted;
            string t = Prefix + (on ? On : Off);
            return string.IsNullOrEmpty(preset) ? t : t + Sep + preset;
        }
    }
}
