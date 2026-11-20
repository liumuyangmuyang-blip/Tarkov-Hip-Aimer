// v4.1 百分比偏移 (Leo, 17 Nov 2026): offsets are shown to users as a percentage of HALF the game picture height
// (screen centre to the top edge = 100 %), 2 decimals, with a direction arrow (← → ↑ ↓) and a positive number,
// e.g. 「← 4.17%」. Storage does NOT change: profiles keep px (double, never rounded) at the 调参 reference screen.
// The % is display / input only and goes through the EXISTING v4.1 conversion (ViewScale.Factor + ViewScale.HalfSpan);
// nothing here re-derives FOV or resolution maths. Pure (no WinForms): tests-v4 compiles this file.
using System;
using System.Globalization;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>Which offset axis a value belongs to (decides the arrow pair).</summary>
    public enum OffsetAxis { X, Y }

    /// <summary>
    /// px ⇄ 百分比偏移. Two sign conventions exist in the app and both are covered:
    /// - PROFILE values (what the settings pages edit: 移动 / 侧身 / 换手 / 后坐力 / 跳跃): 调参 px, X + = right, Y + = UP
    ///   (「Y（上为正）」, Motion.cs 'UI coords'). Use ToPct / ToPx / Format / TryParse.
    /// - SCREEN values (MotionEngine.OffsetX / OffsetY, the dashboard's live ring): game-screen px AFTER 分辨率换算,
    ///   X + = right, Y + = DOWN. Use ScreenToPct / FormatScreen.
    /// pct = 100 · k · px / h, with k = ViewScale.Factor(g) (调参 px → game px) and h = half the game picture height
    /// (ViewScale.HalfSpan(game, 垂直) = 游戏高度 / 2 for 跟随分辨率). For 「Leo默认」 at FOV 75 (调参 FOV 75): k · 1/h = 1/720,
    /// so D X −30 px → 「← 4.17%」 on every resolution.
    /// v5.1.1 (X5): FOV is applied on X only, per side (ViewScale.FovX): an X offset toward the gun side (right for the normal
    /// hand, left for 换手) is not scaled, one to the other side is × gameFov / baseFov; Y has no FOV term. The axis-aware
    /// overloads (ToPct / ToPx with an OffsetAxis, Format, FormatVector) apply it so every box shows the on-screen %; the
    /// axis-less ToPct(px, g) / ToPx(pct, g) are the resolution part only. At FOV 50 (调参 75) D −30 px shows 「← 2.78%」.
    /// </summary>
    public static class OffsetUnits
    {
        /// <summary>Decimals shown and accepted when typing (0.01 %).</summary>
        public const int Decimals = 2;
        /// <summary>Spin arrows / arrow keys step (0.10 %).</summary>
        public const double SpinStepPct = 0.10;
        /// <summary>Finest typed step (0.01 %).</summary>
        public const double TypeStepPct = 0.01;

        public const string Left = "←", Right = "→", Up = "↑", Down = "↓";
        /// <summary>The unit label next to an input box.</summary>
        public const string Unit = "%";
        /// <summary>The user-facing name of the unit (column / group captions).</summary>
        public const string Name = "百分比偏移";

        /// <summary>Fallback when settings are missing or unusable: the 调参 reference 1440 px high (half = 720).</summary>
        public const double ReferenceHalfHeight = 720.0;

        // ---------------- px ⇄ % ----------------

        /// <summary>Half the game picture height in game px (ViewScale.HalfSpan with 垂直; 720 when unusable).</summary>
        public static double HalfHeight(GlobalSettings g)
        {
            if (g == null) return ReferenceHalfHeight;
            double h = ViewScale.HalfSpan(g.ResGameW, g.ResGameH, g.ResAspect, "v", !g.PictureStretched); // v5.1: 不拉伸 may letterbox
            return (h > 0 && !double.IsInfinity(h) && !double.IsNaN(h)) ? h : ReferenceHalfHeight;
        }

        /// <summary>调参 px that make 1 % on the game screen: h / (100 · k). 7.2 for 「Leo默认」 at FOV 75.</summary>
        public static double PxPerPct(GlobalSettings g)
        {
            double k = Factor(g), h = HalfHeight(g);
            return h / (100.0 * k);
        }

        /// <summary>PROFILE px (调参 screen) → % of half the game picture height. Sign kept (X + right, Y + up).</summary>
        public static double ToPct(double px, GlobalSettings g)
        {
            if (double.IsNaN(px) || double.IsInfinity(px)) return 0;
            return px / PxPerPct(g);
        }

        /// <summary>% → PROFILE px (double, not rounded: what is stored). Sign kept.</summary>
        public static double ToPx(double pct, GlobalSettings g)
        {
            if (double.IsNaN(pct) || double.IsInfinity(pct)) return 0;
            return pct * PxPerPct(g);
        }

        /// <summary>v5.1.1 (X5): the FOV factor of a signed PROFILE offset on this axis: Y 1; X 1 toward the gun side (px > 0
        /// for the normal hand, px < 0 for 换手), gameFov / baseFov on the other side; 1 for 0 px and equal FOVs.</summary>
        public static double FovFactor(double px, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            if (axis != OffsetAxis.X || !(px > 0 || px < 0)) return 1.0;
            double r = ViewScale.FovRatio(g);
            return r == 1.0 ? 1.0 : ViewScale.FovSide(px > 0, r, switched);
        }

        /// <summary>The largest FOV factor any offset can get (max(1, gameFov / baseFov)): box ranges use it.</summary>
        public static double MaxFovFactor(GlobalSettings g)
        {
            double r = ViewScale.FovRatio(g);
            return r > 1.0 ? r : 1.0;
        }

        /// <summary>PROFILE px → the % shown on screen for this axis, normal hand (FOV side rule on X).</summary>
        public static double ToPct(double px, OffsetAxis axis, GlobalSettings g) { return ToPct(px, axis, false, g); }

        /// <summary>PROFILE px → the on-screen % for this axis; switched = the value is a 换手 value (the sides swap).</summary>
        public static double ToPct(double px, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            double p = ToPct(px, g), f = FovFactor(px, axis, switched, g);
            return f == 1.0 ? p : p * f;
        }

        /// <summary>On-screen % (signed) → PROFILE px for this axis, normal hand. Inverse of ToPct(px, axis, g).</summary>
        public static double ToPx(double pct, OffsetAxis axis, GlobalSettings g) { return ToPx(pct, axis, false, g); }

        /// <summary>On-screen % (signed) → PROFILE px; switched = a 换手 value. The sign of pct is the sign of px.</summary>
        public static double ToPx(double pct, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            double px = ToPx(pct, g), f = FovFactor(px, axis, switched, g);
            return f == 1.0 ? px : px / f;
        }

        /// <summary>SCREEN px (game screen, after 分辨率换算, e.g. MotionEngine.OffsetX/Y) → % of half the picture height.</summary>
        public static double ScreenToPct(double screenPx, GlobalSettings g)
        {
            if (double.IsNaN(screenPx) || double.IsInfinity(screenPx)) return 0;
            return 100.0 * screenPx / HalfHeight(g);
        }

        /// <summary>% → SCREEN px (game screen).</summary>
        public static double PctToScreen(double pct, GlobalSettings g)
        {
            if (double.IsNaN(pct) || double.IsInfinity(pct)) return 0;
            return pct * HalfHeight(g) / 100.0;
        }

        /// <summary>Rounds a % to the shown 2 decimals (away from zero at .5).</summary>
        public static double Round(double pct)
        {
            if (double.IsNaN(pct) || double.IsInfinity(pct)) return 0;
            return Math.Round(pct, Decimals, MidpointRounding.AwayFromZero);
        }

        // ---------------- arrows ----------------

        /// <summary>The arrow for a direction: X → (+) / ← (−), Y ↑ (+) / ↓ (−) in the PROFILE convention (Y + = up).</summary>
        public static string Arrow(OffsetAxis axis, bool positive)
        {
            if (axis == OffsetAxis.X) return positive ? Right : Left;
            return positive ? Up : Down;
        }

        /// <summary>The arrow of a signed PROFILE value; "" when it shows as 0.00 %.</summary>
        public static string ArrowOf(OffsetAxis axis, double pct)
        {
            double r = Round(pct);
            if (r == 0) return "";
            return Arrow(axis, r > 0);
        }

        // ---------------- formatting ----------------

        /// <summary>"4.17" (magnitude, invariant culture, always 2 decimals, never "-0.00").</summary>
        public static string Number(double pct)
        {
            double r = Math.Abs(Round(pct));
            return r.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>A signed PROFILE % as 「← 4.17%」 / 「↑ 1.39%」; 0 → 「0.00%」 (no arrow).</summary>
        public static string FormatPct(double pct, OffsetAxis axis)
        {
            string a = ArrowOf(axis, pct);
            return a.Length == 0 ? Number(pct) + Unit : a + " " + Number(pct) + Unit;
        }

        /// <summary>PROFILE px → 「← 4.17%」.</summary>
        public static string Format(double px, OffsetAxis axis, GlobalSettings g)
        {
            return FormatPct(ToPct(px, axis, g), axis);
        }

        /// <summary>v5.1.1 (X5): a value stored relative to an origin (a move direction stored from the 站立静止偏移 point):
        /// the box shows the on-screen distance from the origin, i.e. screen%(origin + px) - screen%(origin), so the FOV side
        /// rule follows the FINAL on-screen X like the overlay. Y, origin 0 or equal FOVs: exactly ToPct(px, axis, switched, g).</summary>
        public static double ToPctAt(double px, double origin, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            if (axis != OffsetAxis.X || origin == 0 || double.IsNaN(origin) || double.IsInfinity(origin) || ViewScale.FovRatio(g) == 1.0)
                return ToPct(px, axis, switched, g);
            if (double.IsNaN(px) || double.IsInfinity(px)) return 0;
            return ToPct(origin + px, axis, switched, g) - ToPct(origin, axis, switched, g);
        }

        /// <summary>Inverse of ToPctAt: the shown % → the stored (origin-relative) PROFILE px.</summary>
        public static double ToPxAt(double pct, double origin, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            if (axis != OffsetAxis.X || origin == 0 || double.IsNaN(origin) || double.IsInfinity(origin) || ViewScale.FovRatio(g) == 1.0)
                return ToPx(pct, axis, switched, g);
            if (double.IsNaN(pct) || double.IsInfinity(pct)) return 0;
            return ToPx(pct + ToPct(origin, axis, switched, g), axis, switched, g) - origin;
        }

        /// <summary>ToPctAt as 「← 4.17%」.</summary>
        public static string FormatAt(double px, double origin, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            return FormatPct(ToPctAt(px, origin, axis, switched, g), axis);
        }

        /// <summary>The X origin of a move direction (W / S / A / D) of profile p: the 站立静止偏移 X of that hand while
        /// StandAsZero is on (换手: its 站立 item, normally the mirror), else 0.</summary>
        public static double StandOriginX(Profile p, bool switched)
        {
            if (p == null || !p.StandAsZero) return 0;
            if (!switched) return p.StandOffsetX;
            double x, y; p.HandValue(Profile.HandStand, out x, out y);
            return x;
        }

        /// <summary>PROFILE px → 「← 4.17%」 for a 换手 value (switched = true swaps the FOV sides).</summary>
        public static string Format(double px, OffsetAxis axis, bool switched, GlobalSettings g)
        {
            return FormatPct(ToPct(px, axis, switched, g), axis);
        }

        /// <summary>A size with no direction: PROFILE px → 「4.17%」.</summary>
        public static string FormatMagnitude(double px, GlobalSettings g)
        {
            return Number(ToPct(px, g)) + Unit;
        }

        /// <summary>A PROFILE vector → 「← 4.17%  ↑ 1.39%」; an axis that shows 0.00 % is left out, both 0 → 「0.00%」.</summary>
        public static string FormatVector(double pxX, double pxYUp, GlobalSettings g)
        {
            string x = Format(pxX, OffsetAxis.X, g), y = Format(pxYUp, OffsetAxis.Y, g);
            bool zx = Round(ToPct(pxX, OffsetAxis.X, g)) == 0, zy = Round(ToPct(pxYUp, OffsetAxis.Y, g)) == 0;
            if (zx && zy) return Number(0) + Unit;
            if (zx) return y;
            if (zy) return x;
            return x + "  " + y;
        }

        /// <summary>SCREEN px (y-down) → 「← 4.17%」 / 「↓ 1.39%」 (screen + Y = down = ↓).</summary>
        public static string FormatScreen(double screenPx, OffsetAxis axis, GlobalSettings g)
        {
            double pct = ScreenToPct(screenPx, g);
            return FormatPct(axis == OffsetAxis.Y ? -pct : pct, axis);
        }

        /// <summary>SCREEN vector (MotionEngine.OffsetX, OffsetY; y-down) → 「→ 2.50%  ↓ 0.83%」 (zero axes left out).</summary>
        public static string FormatScreenVector(double screenX, double screenYDown, GlobalSettings g)
        {
            double pxX = ScreenToPct(screenX, g), pyUp = -ScreenToPct(screenYDown, g);
            bool zx = Round(pxX) == 0, zy = Round(pyUp) == 0;
            if (zx && zy) return Number(0) + Unit;
            if (zx) return FormatPct(pyUp, OffsetAxis.Y);
            if (zy) return FormatPct(pxX, OffsetAxis.X);
            return FormatPct(pxX, OffsetAxis.X) + "  " + FormatPct(pyUp, OffsetAxis.Y);
        }

        // ---------------- parsing ----------------

        /// <summary>
        /// Parses what a user types into a signed PROFILE % (X + right, Y + up). Accepts 「← 4.17%」, 「4.17」, 「-4.17」,
        /// 「4.17 %」, 「左 4.17」 / 「右」 / 「上」 / 「下」, an arrow before or after the number, full-width ％ － ＋ ． ，, and a
        /// decimal comma when there is no point (「4,17」). v4.1 (REBRUTE41 P2-3): ASCII arrows too -- 「<-」 「<」 「<--」 = ←,
        /// 「->」 「>」 「-->」 = →, 「^」 = ↑, 「v」 / 「V」 = ↓ (see AsciiArrows). An arrow of the other axis (↑ on X) is
        /// rejected. A minus with an arrow flips it (「← -2」 = → 2). Not finite / empty / junk → false.
        /// </summary>
        public static bool TryParse(string text, OffsetAxis axis, out double pct)
        {
            pct = 0;
            if (text == null) return false;
            text = AsciiArrows(text);
            StringBuilder num = new StringBuilder();
            int sign = 1; bool dirSeen = false;
            foreach (char c0 in text)
            {
                char c = c0;
                switch (c)
                {
                    case '％': c = '%'; break;
                    case '－': case '−': case '–': c = '-'; break;
                    case '＋': c = '+'; break;
                    case '．': case '。': c = '.'; break;
                    case '，': c = ','; break;
                }
                if (c >= '０' && c <= '９') c = (char)('0' + (c - '０'));
                int d = DirOf(c);
                if (d != 0)
                {
                    bool xDir = (c == '←' || c == '→' || c == '左' || c == '右');
                    if (xDir != (axis == OffsetAxis.X)) return false;
                    if (dirSeen) return false;
                    dirSeen = true; sign = d;
                    continue;
                }
                if (c == '%' || char.IsWhiteSpace(c)) continue;
                if ((c >= '0' && c <= '9') || c == '.' || c == ',' || c == '-' || c == '+') { num.Append(c); continue; }
                return false;
            }
            string s = num.ToString();
            if (s.Length == 0) return false;
            if (s.IndexOf('.') < 0) s = s.Replace(',', '.');
            else s = s.Replace(",", "");
            double v;
            if (!double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v)) return false;
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            pct = sign * v;
            return true;
        }

        /// <summary>TryParse, then % → PROFILE px.</summary>
        public static bool TryParsePx(string text, OffsetAxis axis, GlobalSettings g, out double px)
        {
            double pct;
            px = 0;
            if (!TryParse(text, axis, out pct)) return false;
            px = ToPx(pct, axis, g);
            return true;
        }

        /// <summary>
        /// The text names a direction (so a 百分比偏移 box takes its arrow from it): a sign, an arrow (ASCII ones too) or
        /// 左 / 右 / 上 / 下. A plain number does not (the box keeps its current arrow).
        /// </summary>
        public static bool NamesDirection(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in AsciiArrows(text))
                if (DirOf(c) != 0 || c == '+' || c == '＋' || IsDash(c)) return true;
            return false;
        }

        /// <summary>
        /// v4.1 (REBRUTE41 P2-3): ASCII arrows → arrow characters. 「<」 plus any dashes right after it (「<-」, 「<--」) = ←;
        /// dashes followed by 「>」 (「->」, 「-->」) and a lone 「>」 = →; 「^」 = ↑; 「v」 / 「V」 = ↓. Full-width ＜ ＞ ＾ the
        /// same. Any other dash stays a minus sign.
        /// </summary>
        public static string AsciiArrows(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(AsciiArrowChars) < 0) return text ?? "";
            StringBuilder b = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<' || c == '＜')
                {
                    b.Append('←');
                    while (i + 1 < text.Length && IsDash(text[i + 1])) i++;
                    continue;
                }
                if (IsDash(c))
                {
                    int j = i;
                    while (j < text.Length && IsDash(text[j])) j++;
                    if (j < text.Length && (text[j] == '>' || text[j] == '＞')) { b.Append('→'); i = j; continue; }
                    b.Append(text, i, j - i);
                    i = j - 1;
                    continue;
                }
                switch (c)
                {
                    case '>': case '＞': b.Append('→'); break;
                    case '^': case '＾': b.Append('↑'); break;
                    case 'v': case 'V': b.Append('↓'); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }

        static readonly char[] AsciiArrowChars = { '<', '>', '^', 'v', 'V', '＜', '＞', '＾' };

        static bool IsDash(char c) { return c == '-' || c == '－' || c == '−' || c == '–'; }

        /// <summary>+1 / −1 for a direction character in the PROFILE convention, 0 for anything else.</summary>
        static int DirOf(char c)
        {
            switch (c)
            {
                case '→': case '右': case '↑': case '上': return 1;
                case '←': case '左': case '↓': case '下': return -1;
                default: return 0;
            }
        }

        /// <summary>ViewScale.Factor with a guard (1 when not finite or not positive).</summary>
        static double Factor(GlobalSettings g)
        {
            double k = g == null ? 1.0 : ViewScale.Factor(g);
            return (k > 0 && !double.IsInfinity(k) && !double.IsNaN(k)) ? k : 1.0;
        }
    }

    /// <summary>v5.1.1 (X5): the plain-Chinese FOV line (常规 page, tests): 「FOV 换算：枪那一侧 ×1.00，另一侧 ×0.80，上下不变」.</summary>
    public static class FovText
    {
        public static string Line(GlobalSettings g)
        {
            double r = ViewScale.FovRatio(g);
            return "FOV 换算：枪那一侧 ×1.00，另一侧 ×" + r.ToString("0.00", CultureInfo.InvariantCulture) + "，上下不变";
        }

        /// <summary>The short dashboard line (≤ 25 chars, no English: the dashboard rule): 「视野换算 枪侧×1.00 另侧×0.80」.</summary>
        public static string Short(double r)
        {
            return "视野换算 枪侧×1.00 另侧×" + r.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
