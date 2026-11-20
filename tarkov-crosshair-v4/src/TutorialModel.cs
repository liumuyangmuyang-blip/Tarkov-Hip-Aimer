// TutorialModel.cs -- 首次设置教程 (v4.0): pure maths for the tutorial animations.
// PURE file: only System. Compiles under net48 (C# 5) and net8.0 (tests-v4).
// Every number in this file is 示意 (an illustration of Leo's own description). None of it is measured game data.
using System;

namespace TarkovCrosshair
{
    /// <summary>
    /// Hipfire geometry used by the animations (示意). Leo: the hipfire impact is not at the screen centre; the barrel
    /// shoots from the bottom-right toward the centre and meets it (归零点) at about 25 m, so at 5-15 m the impact sits
    /// bottom-right of the centre. Model: the bullet leaves the muzzle (offset m from the eye, bottom-right) on a straight
    /// line to the point on the sight line at Z0; on a wall at distance d its screen offset is f * m * (1/d - 1/Z0).
    /// Screen offsets use the program's UI convention: X right +, Y up + (UI px on a 1440-high screen).
    /// </summary>
    public static class TutorialGeometry
    {
        /// <summary>归零点 (Leo's statement): about 25 m.</summary>
        public const double ZeroDistanceM = 25.0;
        /// <summary>示意 focal length: UI px per metre at 1 m.</summary>
        public const double FocalPx = 1500.0;
        /// <summary>示意 muzzle offset from the eye in metres: right +, up + (so bottom-right).</summary>
        public const double MuzzleRightM = 0.09, MuzzleUpM = -0.11;
        /// <summary>The demo distances of the 腰射 step (示意).</summary>
        public static readonly double[] DemoDistancesM = new double[] { 5, 10, 15, 25 };

        /// <summary>Hipfire impact offset on screen (UI px, X right +, Y up +) on a wall at distM.
        /// 0 at the zero distance, bottom-right closer than it, top-left beyond it. distM or zeroM &lt;= 0 gives 0.</summary>
        public static void HipImpactPx(double distM, double zeroM, double focal, double mx, double my, out double x, out double y)
        {
            if (!(distM > 0) || !(zeroM > 0) || double.IsInfinity(distM)) { x = 0; y = 0; return; }
            double k = focal * (1.0 / distM - 1.0 / zeroM);
            x = mx * k;
            y = my * k;
        }

        public static void HipImpactPx(double distM, out double x, out double y)
        {
            HipImpactPx(distM, ZeroDistanceM, FocalPx, MuzzleRightM, MuzzleUpM, out x, out y);
        }

        /// <summary>On-screen size (UI px) of something sizeM metres big at distM metres.</summary>
        public static double ProjectedPx(double sizeM, double distM)
        {
            return distM > 0 ? FocalPx * sizeM / distM : 0;
        }

        /// <summary>Screen UI px -> wall metres at distM (and back with WallToScreen). Used to keep bullet holes on the wall.</summary>
        public static double ScreenToWall(double px, double distM) { return px * distM / FocalPx; }
        public static double WallToScreen(double m, double distM) { return distM > 0 ? m * FocalPx / distM : 0; }

        /// <summary>Height of the bullet path below the sight line (m, negative = below) at distance z for the side view.</summary>
        public static double PathHeightM(double z, double zeroM, double muzzleUpM)
        {
            if (!(zeroM > 0)) return 0;
            return muzzleUpM * (1.0 - z / zeroM);
        }

        /// <summary>Smoothstep 0..1.</summary>
        public static double Smooth(double u)
        {
            if (u <= 0) return 0;
            if (u >= 1) return 1;
            return u * u * (3 - 2 * u);
        }

        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
    }

    /// <summary>
    /// 拉枪 timing for the 开发者的话 demo (示意). Leo: right hand + D + E is the fastest relative peek; to the left, switch hands,
    /// then Q + A. Camera sideways position = strafe (A/D, ramps to full speed) + lean (Q/E, smoothstep to its full shift).
    /// </summary>
    public static class PeekModel
    {
        public const double StrafeSpeedMps = 2.0; // 示意
        public const double StrafeRampS = 0.25;   // 示意: time to reach full strafe speed
        public const double LeanShiftM = 0.40;    // 示意: sideways camera shift of a full lean
        public const double LeanS = 0.30;         // 示意: time for a full lean

        /// <summary>Strafe distance after t s (linear ramp to full speed over StrafeRampS).</summary>
        public static double StrafeX(double t)
        {
            if (!(t > 0)) return 0;
            double v = StrafeSpeedMps, r = StrafeRampS;
            if (t <= r) return 0.5 * v / r * t * t;
            return 0.5 * v * r + v * (t - r);
        }

        /// <summary>Lean shift after t s (smoothstep over LeanS).</summary>
        public static double LeanX(double t)
        {
            if (!(t > 0)) return 0;
            return LeanShiftM * TutorialGeometry.Smooth(t / LeanS);
        }

        public static double CameraX(bool strafe, bool lean, double t)
        {
            return (strafe ? StrafeX(t) : 0) + (lean ? LeanX(t) : 0);
        }

        /// <summary>First time (s, 1 ms steps) the camera has moved clearM sideways; +infinity if not within maxS.</summary>
        public static double TimeToClear(bool strafe, bool lean, double clearM, double maxS)
        {
            if (clearM <= 0) return 0;
            for (int i = 1; i <= (int)Math.Round(maxS * 1000); i++)
            {
                double t = i / 1000.0;
                if (CameraX(strafe, lean, t) >= clearM) return t;
            }
            return double.PositiveInfinity;
        }
    }

    /// <summary>Key bits of the tutorial scripts.</summary>
    public static class TKey
    {
        public const int W = 1, S = 2, A = 4, D = 8, Q = 16, E = 32, Fire = 64, Jump = 128, Hand = 256;
        public const int Move = W | S | A | D;

        /// <summary>Mask of the keys held in an engine InputState.</summary>
        public static int Of(InputState i, bool fire)
        {
            int m = 0;
            if (i.Fwd) m |= W;
            if (i.Back) m |= S;
            if (i.Left) m |= A;
            if (i.Right) m |= D;
            if (i.LeanL) m |= Q;
            if (i.LeanR) m |= E;
            if (fire || i.Fire) m |= Fire;
            if (i.Jump) m |= Jump;
            if (i.HandSwitch) m |= Hand;
            return m;
        }
    }

    /// <summary>One scripted interval: keys held from A to B ms (loop time).</summary>
    public struct ScriptSeg
    {
        public int A, B, Keys;
        public ScriptSeg(int a, int b, int keys) { A = a; B = b; Keys = keys; }
    }

    /// <summary>
    /// A looping demo script. Sample() turns intervals into the InputState the MotionEngine needs:
    /// held keys stay down for the interval; in 切换 (toggle) mode Q / E / 换手 become a short press at the start AND
    /// at the end of the interval (so the lean / hand is on exactly for the interval); 跳跃 is always one short press.
    /// </summary>
    public sealed class TutorialScript
    {
        public const int PulseMs = 60;
        public readonly int LoopMs;
        public readonly ScriptSeg[] Segs;

        public TutorialScript(int loopMs, params ScriptSeg[] segs)
        {
            LoopMs = loopMs > 0 ? loopMs : 1;
            Segs = segs ?? new ScriptSeg[0];
        }

        public double Wrap(double tMs)
        {
            if (!(tMs > 0)) return 0;
            return tMs % LoopMs;
        }

        /// <summary>Raw held mask at loop time t (for key-cap lights).</summary>
        public int HeldAt(double tMs)
        {
            double t = Wrap(tMs);
            int m = 0;
            for (int i = 0; i < Segs.Length; i++) if (t >= Segs[i].A && t < Segs[i].B) m |= Segs[i].Keys;
            return m;
        }

        private static bool Pulse(double t, int at) { return t >= at && t < at + PulseMs; }

        public InputState Sample(double tMs, bool leanToggle, bool handToggle, out bool fire)
        {
            double t = Wrap(tMs);
            InputState s = new InputState();
            fire = false;
            for (int i = 0; i < Segs.Length; i++)
            {
                ScriptSeg g = Segs[i];
                bool held = t >= g.A && t < g.B;
                int k = g.Keys;
                if (held)
                {
                    if ((k & TKey.W) != 0) s.Fwd = true;
                    if ((k & TKey.S) != 0) s.Back = true;
                    if ((k & TKey.A) != 0) s.Left = true;
                    if ((k & TKey.D) != 0) s.Right = true;
                    if ((k & TKey.Fire) != 0) fire = true;
                }
                bool edge = Pulse(t, g.A) || Pulse(t, g.B);
                if ((k & TKey.Q) != 0 && (leanToggle ? edge : held)) s.LeanL = true;
                if ((k & TKey.E) != 0 && (leanToggle ? edge : held)) s.LeanR = true;
                if ((k & TKey.Hand) != 0 && (handToggle ? edge : held)) s.HandSwitch = true;
                if ((k & TKey.Jump) != 0 && Pulse(t, g.A)) s.Jump = true;
            }
            s.Fire = fire;
            return s;
        }
    }

    /// <summary>
    /// Live input vs the demo script: any live key or click switches to live for HoldMs; after that the script restarts
    /// from its beginning (ScriptMs = 0).
    /// </summary>
    public sealed class LiveArbiter
    {
        public const double HoldMs = 2500;
        private double liveLeft;
        private double scriptMs;
        private bool live;

        public bool Live { get { return live; } }
        public double ScriptMs { get { return scriptMs; } }
        /// <summary>True on the update the script resumed after live input (steps may clear their state).</summary>
        public bool Resumed;

        public void Reset() { liveLeft = 0; scriptMs = 0; live = false; Resumed = false; }

        public void Update(double dtMs, bool anyLive)
        {
            Resumed = false;
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            if (anyLive) { liveLeft = HoldMs; live = true; return; }
            if (live)
            {
                liveLeft -= dtMs;
                if (liveLeft > 0) return;
                live = false;
                scriptMs = 0;
                Resumed = true;
                return;
            }
            scriptMs += dtMs;
        }
    }

    /// <summary>Text readability (SPEC: never grey-on-dark below 4.5:1). WCAG contrast of opaque colours given as 0..255 RGB.</summary>
    public static class TutorialContrast
    {
        public const double MinRatio = 4.5;

        private static double Ch(int c)
        {
            double s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(int r, int g, int b) { return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b); }

        public static double Ratio(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            double a = Luminance(r1, g1, b1) + 0.05, b = Luminance(r2, g2, b2) + 0.05;
            return a > b ? a / b : b / a;
        }

        /// <summary>fg (with alpha 0..255) composited over the opaque bg, then lightened toward white until the contrast
        /// with bg is at least MinRatio. Returns the opaque result. Dark text (luminance &lt; 0.18) is returned composited only
        /// (it sits on a light surface such as a lit key cap).</summary>
        public static void Readable(int a, int r, int g, int b, int br, int bg, int bb, out int or, out int og, out int ob)
        {
            double t = a / 255.0;
            int cr = (int)Math.Round(r * t + br * (1 - t)), cg = (int)Math.Round(g * t + bg * (1 - t)), cb = (int)Math.Round(b * t + bb * (1 - t));
            if (Luminance(r, g, b) < 0.18) { or = cr; og = cg; ob = cb; return; }
            for (int i = 0; i <= 20 && Ratio(cr, cg, cb, br, bg, bb) < MinRatio; i++)
            {
                cr = cr + (255 - cr) / 4 + (cr < 255 ? 1 : 0); cg = cg + (255 - cg) / 4 + (cg < 255 ? 1 : 0); cb = cb + (255 - cb) / 4 + (cb < 255 ? 1 : 0);
                if (cr > 255) cr = 255; if (cg > 255) cg = 255; if (cb > 255) cb = 255;
            }
            or = cr; og = cg; ob = cb;
        }
    }

    /// <summary>Allocation-free number strings for per-frame labels (-3000..3000; outside that range a new string).</summary>
    public static class TutorialNum
    {
        private const int Lim = 3000;
        private static string[] ints, signed;

        public static string Int(double v)
        {
            int i = Round(v);
            if (i < -Lim || i > Lim) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (ints == null)
            {
                string[] a = new string[2 * Lim + 1];
                for (int k = -Lim; k <= Lim; k++) a[k + Lim] = k.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ints = a;
            }
            return ints[i + Lim];
        }

        /// <summary>"+12" / "-5" / "0".</summary>
        public static string Signed(double v)
        {
            int i = Round(v);
            if (i < -Lim || i > Lim) return (i > 0 ? "+" : "") + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (signed == null)
            {
                string[] a = new string[2 * Lim + 1];
                for (int k = -Lim; k <= Lim; k++) a[k + Lim] = (k > 0 ? "+" : "") + k.ToString(System.Globalization.CultureInfo.InvariantCulture);
                signed = a;
            }
            return signed[i + Lim];
        }

        public static int Round(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v > int.MaxValue / 2) return int.MaxValue / 2;
            if (v < int.MinValue / 2) return int.MinValue / 2;
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// v4.1 百分比偏移 for tutorial text (Leo, 17 Nov 2026): every offset the tutorial shows is a share of HALF the game
    /// picture height with a direction arrow and 2 decimals (「← 4.17%」), through OffsetUnits (= ViewScale, the same
    /// conversion as the settings pages; nothing is converted here). Inputs are PROFILE px or the demo engines' UI px
    /// (X right +, Y up +): the demo engines run at the 调参 screen (TutorialContext.DemoSettings), so their UI px are
    /// profile px. One-off strings (「当前：」 notes, terminal lines) use these helpers; per-frame labels use TutorialPctLabel.
    /// </summary>
    public static class TutorialPct
    {
        /// <summary>X offset: 「← 4.17%」 / 「→ 10.42%」 / 「0.00%」.</summary>
        public static string X(double px, GlobalSettings g) { return OffsetUnits.Format(px, OffsetAxis.X, g); }
        /// <summary>Y offset (+ = up): 「↑ 1.39%」 / 「↓ 4.58%」 / 「0.00%」.</summary>
        public static string Y(double pxUp, GlobalSettings g) { return OffsetUnits.Format(pxUp, OffsetAxis.Y, g); }
        /// <summary>Both axes: 「← 4.17%  ↑ 0.42%」 (an axis that shows 0.00% is left out; both 0 -> 「0.00%」).</summary>
        public static string Vec(double px, double pxUp, GlobalSettings g) { return OffsetUnits.FormatVector(px, pxUp, g); }
        /// <summary>A distance without direction (e.g. 还差 …): 「4.17%」.</summary>
        public static string Mag(double px, GlobalSettings g) { return OffsetUnits.FormatMagnitude(Math.Abs(px), g); }

        /// <summary>Input box of a 百分比偏移 field: the positive size in % (2 decimals) for a PROFILE px value.</summary>
        public static double FieldPct(double px, GlobalSettings g) { return Math.Abs(OffsetUnits.Round(OffsetUnits.ToPct(px, g))); }

        /// <summary>Typed / spun size |pct| in direction sign (-1 = ← / ↓) -> PROFILE px (double, not rounded), clamped to
        /// the field's px range. Not finite -> 0 (then clamped).</summary>
        public static double FieldPx(double pct, int sign, double minPx, double maxPx, GlobalSettings g)
        {
            double px = (sign < 0 ? -1.0 : 1.0) * OffsetUnits.ToPx(Math.Abs(pct), g);
            if (double.IsNaN(px) || double.IsInfinity(px)) px = 0;
            if (px < minPx) px = minPx;
            if (px > maxPx) px = maxPx;
            return px;
        }

        /// <summary>Largest % a field accepts for its px range [minPx, maxPx] at these settings, rounded DOWN to 0.01 % so the
        /// px limit is never passed; 100 when the range or settings are unusable.</summary>
        public static double FieldLimit(double minPx, double maxPx, GlobalSettings g)
        {
            double m = Math.Max(Math.Abs(OffsetUnits.ToPct(minPx, g)), Math.Abs(OffsetUnits.ToPct(maxPx, g)));
            if (!(m > 0) || double.IsInfinity(m)) return 100;
            return Math.Floor(m * 100.0 + 1e-9) / 100.0;
        }
    }

    /// <summary>
    /// A per-frame 百分比偏移 label for one call site: the string is rebuilt only when the shown value (0.01 %) changes, so
    /// drawing it every frame does not allocate. Text = Prefix + 「← 4.17%」 (or 「4.17%」 for Magnitude) + Suffix.
    /// </summary>
    public sealed class TutorialPctLabel
    {
        public const int Signed = 0, Magnitude = 1;
        private readonly OffsetAxis axis;
        private readonly int mode;
        private readonly string prefix, suffix;
        private long key = long.MinValue;
        private string text;

        public TutorialPctLabel(OffsetAxis axis, int mode) : this(axis, mode, "", "") { }

        public TutorialPctLabel(OffsetAxis axis, int mode, string prefix, string suffix)
        {
            this.axis = axis; this.mode = mode;
            this.prefix = prefix ?? ""; this.suffix = suffix ?? "";
        }

        /// <summary>The label for a PROFILE / demo UI px value.</summary>
        public string Of(double px, GlobalSettings g)
        {
            double pct = OffsetUnits.Round(OffsetUnits.ToPct(px, g));
            if (mode == Magnitude) pct = Math.Abs(pct);
            long k = Key(pct);
            if (k != key || text == null)
            {
                key = k;
                text = prefix + (mode == Magnitude ? OffsetUnits.Number(pct) + OffsetUnits.Unit : OffsetUnits.FormatPct(pct, axis)) + suffix;
            }
            return text;
        }

        private static long Key(double pct)
        {
            double v = Math.Round(pct * 100.0);
            if (!(v > -1e15 && v < 1e15)) return 0;
            return (long)v;
        }
    }

    /// <summary>Like TutorialPctLabel for a vector (X and Y): Prefix + 「← 2.64%  ↑ 0.28%」 + Suffix, rebuilt only on change.</summary>
    public sealed class TutorialPctVecLabel
    {
        private readonly string prefix, suffix;
        private long kx = long.MinValue, ky = long.MinValue;
        private string text;

        public TutorialPctVecLabel(string prefix, string suffix) { this.prefix = prefix ?? ""; this.suffix = suffix ?? ""; }

        public string Of(double px, double pxUp, GlobalSettings g)
        {
            long a = (long)Math.Round(Clip(OffsetUnits.Round(OffsetUnits.ToPct(px, g))) * 100.0);
            long b = (long)Math.Round(Clip(OffsetUnits.Round(OffsetUnits.ToPct(pxUp, g))) * 100.0);
            if (a != kx || b != ky || text == null) { kx = a; ky = b; text = prefix + OffsetUnits.FormatVector(px, pxUp, g) + suffix; }
            return text;
        }

        private static double Clip(double v) { return v > 1e12 ? 1e12 : (v < -1e12 ? -1e12 : v); }
    }

    /// <summary>
    /// Pure layout rules of the tutorial scenes (unit-tested in tests-v4): label placement that keeps text inside its view
    /// and off other labels, the per-view tag budget (at most MaxTagsPerView pills in one view), finite-number guards.
    /// Rectangles are quads (x, y, w, h) in float arrays, so this file stays System-only.
    /// </summary>
    public static class TutorialPlace
    {
        /// <summary>Leo (v4.1): at most this many tags (pills) in one view of a scene.</summary>
        public const int MaxTagsPerView = 2;

        public static bool Fin(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        /// <summary>v when finite, else fallback.</summary>
        public static double Finite(double v, double fallback) { return Fin(v) ? v : fallback; }

        private static bool Hits(double x, double y, double w, double h, float[] obs, int i, double gap)
        {
            double bx = obs[4 * i], by = obs[4 * i + 1], bw = obs[4 * i + 2], bh = obs[4 * i + 3];
            return x < bx + bw + gap && x + w + gap > bx && y < by + bh + gap && y + h + gap > by;
        }

        /// <summary>
        /// A w x h box wanted at (x, y): kept inside [minX, maxX] x [minY, maxY] and moved DOWN past every obstacle it touches
        /// (gap apart). obs holds n quads. Returns false (ox / oy = the last try) when it cannot sit inside the bounds off
        /// every obstacle.
        /// </summary>
        public static bool Down(double x, double y, double w, double h, double minX, double minY, double maxX, double maxY,
                                float[] obs, int n, double gap, out double ox, out double oy)
        {
            ox = x; oy = y;
            if (!Fin(x) || !Fin(y) || !(w > 0) || !(h > 0) || !Fin(minX) || !Fin(minY) || !Fin(maxX) || !Fin(maxY)) return false;
            if (obs == null) n = 0;
            if (n > 0 && obs.Length < 4 * n) n = obs.Length / 4;
            if (ox + w > maxX) ox = maxX - w;
            if (ox < minX) ox = minX;
            if (oy < minY) oy = minY;
            for (int pass = 0; pass <= n; pass++)
            {
                bool moved = false;
                for (int i = 0; i < n; i++)
                    if (Hits(ox, oy, w, h, obs, i, gap)) { oy = obs[4 * i + 1] + obs[4 * i + 3] + gap; moved = true; }
                if (!moved) break;
            }
            if (ox < minX - 1e-6 || ox + w > maxX + 1e-6 || oy + h > maxY + 1e-6) return false;
            for (int i = 0; i < n; i++) if (Hits(ox, oy, w, h, obs, i, 0)) return false;
            return true;
        }

        /// <summary>
        /// A w x h name label for a marker at (cx, cy): centred below it (gapBelow) when that fits inside the bounds, else
        /// beside it (gapSide; right first, or left first when preferLeft), else centred above it, else clamped inside.
        /// Returns the label's top-left. The label never leaves the bounds when it is smaller than them.
        /// </summary>
        public static void NameLabel(double cx, double cy, double w, double h, double gapBelow, double gapSide, bool preferLeft,
                                     double minX, double minY, double maxX, double maxY, out double x, out double y)
        {
            if (!Fin(cx)) cx = (minX + maxX) / 2;
            if (!Fin(cy)) cy = (minY + maxY) / 2;
            // below, centred
            x = cx - w / 2; y = cy + gapBelow;
            if (y + h <= maxY && x >= minX && x + w <= maxX) return;
            double yMid = cy - h / 2;
            if (yMid < minY) yMid = minY;
            if (yMid + h > maxY) yMid = maxY - h;
            double right = cx + gapSide, left = cx - gapSide - w;
            if (preferLeft)
            {
                if (left >= minX) { x = left; y = yMid; return; }
                if (right + w <= maxX) { x = right; y = yMid; return; }
            }
            else
            {
                if (right + w <= maxX) { x = right; y = yMid; return; }
                if (left >= minX) { x = left; y = yMid; return; }
            }
            // above, centred (clamped)
            x = cx - w / 2; y = cy - gapBelow - h;
            if (x < minX) x = minX;
            if (x + w > maxX) x = maxX - w;
            if (y < minY) y = minY;
            if (y + h > maxY) y = maxY - h;
        }

        /// <summary>
        /// The most tags (pills) in one view: each tag (quad in tags) belongs to the smallest view (quad in views) that holds
        /// its centre, else to the scene (when the scene holds it; tags outside the scene are not counted). counts = scratch
        /// of at least nv entries (no allocation per call); views beyond counts.Length are ignored.
        /// </summary>
        public static int MaxPerView(float[] tags, int nt, float[] views, int nv, double sx, double sy, double sw, double sh, int[] counts)
        {
            if (tags == null) nt = 0;
            if (views == null || counts == null) nv = 0;
            if (nt > 0 && tags.Length < 4 * nt) nt = tags.Length / 4;
            if (nv > 0 && views.Length < 4 * nv) nv = views.Length / 4;
            if (nv > 0 && nv > counts.Length) nv = counts.Length;
            for (int v = 0; v < nv; v++) counts[v] = 0;
            int sceneCount = 0;
            for (int t = 0; t < nt; t++)
            {
                double mx = tags[4 * t] + tags[4 * t + 2] / 2.0, my = tags[4 * t + 1] + tags[4 * t + 3] / 2.0;
                int owner = -1; double area = double.MaxValue;
                for (int v = 0; v < nv; v++)
                {
                    double vx = views[4 * v], vy = views[4 * v + 1], vw = views[4 * v + 2], vh = views[4 * v + 3];
                    if (mx >= vx && mx <= vx + vw && my >= vy && my <= vy + vh && vw * vh < area) { owner = v; area = vw * vh; }
                }
                if (owner >= 0) counts[owner]++;
                else if (mx >= sx && mx <= sx + sw && my >= sy && my <= sy + sh) sceneCount++;
            }
            int best = sceneCount;
            for (int v = 0; v < nv; v++) if (counts[v] > best) best = counts[v];
            return best;
        }
    }

    /// <summary>
    /// The small terminal under the scene: the last MaxLines lines, each typed in at CharsPerSec. Push allocates the
    /// line's prefixes once (so drawing a half-typed line never allocates); Visible(i) returns the text to draw now.
    /// </summary>
    public sealed class TutorialTerminal
    {
        public const int MaxLines = 4;
        public double CharsPerSec = 55;
        private readonly string[][] prefixes = new string[MaxLines][];
        private readonly double[] startMs = new double[MaxLines];
        private int count, head;
        private double clockMs, lastStart;

        public int Count { get { return count; } }

        public void Clear() { count = 0; head = 0; lastStart = clockMs; }

        public void Update(double dtMs) { if (dtMs > 0 && !double.IsNaN(dtMs)) clockMs += dtMs; }

        /// <summary>Adds a line; it starts typing after the previous line finished.</summary>
        public void Push(string line)
        {
            if (line == null) line = "";
            string[] pre = new string[line.Length + 1];
            for (int i = 0; i <= line.Length; i++) pre[i] = line.Substring(0, i);
            double start = Math.Max(clockMs, lastStart);
            lastStart = start + line.Length * 1000.0 / CharsPerSec + 120;
            int slot;
            if (count < MaxLines) { slot = (head + count) % MaxLines; count++; }
            else { slot = head; head = (head + 1) % MaxLines; }
            prefixes[slot] = pre;
            startMs[slot] = start;
        }

        /// <summary>Line i (0 = oldest shown) as typed so far ("" before it starts).</summary>
        public string Visible(int i)
        {
            if (i < 0 || i >= count) return "";
            int slot = (head + i) % MaxLines;
            string[] pre = prefixes[slot];
            double t = clockMs - startMs[slot];
            if (t <= 0) return pre[0];
            int n = (int)(t * CharsPerSec / 1000.0);
            if (n >= pre.Length) n = pre.Length - 1;
            return pre[n];
        }

        /// <summary>True while line i is still typing (draw a cursor).</summary>
        public bool Typing(int i)
        {
            if (i < 0 || i >= count) return false;
            int slot = (head + i) % MaxLines;
            double t = clockMs - startMs[slot];
            return t >= 0 && t * CharsPerSec / 1000.0 < prefixes[slot].Length - 1;
        }

        /// <summary>Everything typed (no line still typing or waiting).</summary>
        public bool Idle { get { return clockMs >= lastStart; } }
    }

    /// <summary>
    /// v4.1 (DOCCHECK41 T7): the tutorial's name in its window title and header -- 「首次设置教程」 until it was finished once,
    /// then 「设置教程」, the same rule as the 设置 window's tutorial button (SettingsForm.RefreshTutorialButton). Decided when
    /// the window opens, so finishing the last step does not rename the window under the user.
    /// </summary>
    public static class TutorialTitle
    {
        public const string First = "首次设置教程", Again = "设置教程";
        /// <summary>The product name after the 「 · 」 in the window title (allowed English: the product name).</summary>
        public const string Product = "Tarkov Hip Aimer";

        public static string For(bool tutorialDone) { return tutorialDone ? Again : First; }

        /// <summary>Window title: 「首次设置教程 · Tarkov Hip Aimer」 / 「设置教程 · Tarkov Hip Aimer」.</summary>
        public static string Window(bool tutorialDone) { return For(tutorialDone) + " · " + Product; }
    }

    /// <summary>Small deterministic RNG (xorshift32), no allocation.</summary>
    public sealed class TutorialRng
    {
        private uint s;
        public TutorialRng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
        public void Seed(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }

        public uint Next()
        {
            uint x = s;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            s = x;
            return x;
        }

        /// <summary>0 .. 1 (exclusive).</summary>
        public double Unit() { return (Next() >> 8) / 16777216.0; }

        /// <summary>-1 .. 1 roughly normal-ish (sum of two uniforms).</summary>
        public double Spread() { return Unit() + Unit() - 1.0; }
    }
}
