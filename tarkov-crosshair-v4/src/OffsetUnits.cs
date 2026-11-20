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
    /// so D X −30 px → 「← 4.17%」 on every resolution; at FOV 50 the same px shows 6.86 % (tan(37.5°) / tan(25°)).
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
            return FormatPct(ToPct(px, g), axis);
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
            bool zx = Round(ToPct(pxX, g)) == 0, zy = Round(ToPct(pxYUp, g)) == 0;
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
            px = ToPx(pct, g);
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
}
