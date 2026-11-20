// PURE file: only System. Compiles under net48 (C# 5) and net8.0.
// v5.1 (W22, Leo decision 1): the ammo hint drawn in the crosshair's own canvas. Only a range is shown (5 bands), never a
// number; the crosshair's pixels are never changed (the hint is drawn UNDER them and starts outside the look's reach) and the
// centre stays empty. Hidden with the crosshair (开镜 / 丢雷 / off), because it is part of the same canvas.
using System;

namespace TarkovCrosshair
{
    /// <summary>What the motion engine says about the ammo this frame (filled by the overlay from W21's AmmoModel).</summary>
    public struct AmmoView
    {
        /// <summary>Feature on AND the main weapon is in the hands (false = nothing drawn).</summary>
        public bool Active;
        /// <summary>4 满, 3 大半, 2 一半, 1 少, 0 快空.</summary>
        public int Band;
        /// <summary>+1 for every accepted reload; ReloadKind 1 = normal (R), 2 = quick (double-tap R).</summary>
        public int ReloadSeq, ReloadKind;
        /// <summary>Refill animation length, ms (0 = default: normal 2400, quick 900).</summary>
        public double ReloadMs;
        /// <summary>The model's reload state: Reloading and its 0..1 Progress (NaN = not provided: the hint times the refill
        /// itself). With them the refill follows the model exactly (a reload cancelled by a weapon switch stops at once).</summary>
        public bool Reloading;
        public double Progress;
    }

    /// <summary>One ammo hint (render thread only). Update once per frame, then Key tells whether the pixels changed.</summary>
    public sealed class AmmoDisplay
    {
        // ---- styles (combinable flags; AmmoStyle in the settings is "brackets,bottombar,colour,fade,flash") ----
        public const int Brackets = 1, BottomBar = 2, Colour = 4, Fade = 8, Flash = 16;
        public const int AllStyles = Brackets | BottomBar | Colour | Fade | Flash;
        public const int DefaultStyle = Brackets | Colour;
        public const string DefaultStyleText = "brackets,colour";
        private static readonly string[] names = { "brackets", "bottombar", "colour", "fade", "flash" };

        /// <summary>Band names (UI / status line).</summary>
        public static readonly string[] BandNames = { "快空", "少", "一半", "大半", "满" };
        /// <summary>Fill shown for each band (a range, not a count).</summary>
        public static readonly double[] BandFill = { 0.10, 0.25, 0.50, 0.75, 1.00 };

        public const double NormalReloadMs = 2400, QuickReloadMs = 900;
        /// <summary>快空 blinks at this period (on half, dim half), ms.</summary>
        public const double FlashPeriodMs = 250;
        /// <summary>Opacity of the hint (x the fade of the crosshair), and of the empty part of a bar.</summary>
        public const double BaseOpacity = 0.85, TrackOpacity = 0.22;
        /// <summary>Gap between the look's outermost pixel and the hint, px.</summary>
        public const double Clear = 3.0;

        /// <summary>"brackets,colour" -> flags. Unknown words are ignored; null / "" / nothing known = DefaultStyle.
        /// "none" = 0 is never returned: a hint with no shape is drawn as brackets.</summary>
        public static int ParseStyle(string s)
        {
            if (string.IsNullOrEmpty(s)) return DefaultStyle;
            int f = 0;
            foreach (string part in s.Split(',', ';', '|', ' ', '+'))
            {
                string w = part.Trim().ToLowerInvariant();
                if (w == "color") w = "colour";
                int i = Array.IndexOf(names, w);
                if (i >= 0) f |= 1 << i;
            }
            if (f == 0) return DefaultStyle;
            if ((f & (Brackets | BottomBar)) == 0) f |= Brackets; // colour / fade / flash need something to draw on
            return f;
        }

        public static string FormatStyle(int flags)
        {
            flags &= AllStyles;
            if ((flags & (Brackets | BottomBar)) == 0) flags |= Brackets;
            string r = "";
            for (int i = 0; i < names.Length; i++) if ((flags & (1 << i)) != 0) r += (r.Length > 0 ? "," : "") + names[i];
            return r;
        }

        // ---- state ----
        // v5.1 R2 (Leo round 2): a reload is a MAG SWAP for both R and double-tap R. The old bar leaves (normal: slides down out
        // of its track; quick: drops and fades, faster), the track is empty for a moment, then a NEW FULL bar slides in.
        // Outside a reload the look is driven only by the 5 bands (fill, colour, fade, 快空 flash).
        /// <summary>Phases of a reload (share of its length): normal = old out 0..OutEnd, empty, new in InStart..1;
        /// quick = old dropped 0..QuickOutEnd, new in QuickInStart..1.</summary>
        public const double OutEnd = 0.30, InStart = 0.60, QuickOutEnd = 0.30, QuickInStart = 0.35;
        /// <summary>How far a dropped mag falls (share of the track height) and the drop distance of the bottom bar, px.</summary>
        public const double DropTrack = 0.8, DropPx = 8.0;
        private bool has;
        private int seenSeq;
        private int animKind;        // 0 none, 1 normal swap, 2 quick swap (old dropped)
        private double animStart, animFrom, animMs, animU;
        private int animFromBand;
        private double fill;         // 0..1: the share of the track the bars cover now
        private int band;
        private bool visible;
        private int colour;          // 0xRRGGBB (band colour, or neutral without 变色)
        private double alpha;        // 0..1 (before the crosshair's fade)
        private int key;

        public double Fill { get { return fill; } }
        public bool Visible { get { return visible; } }
        public int Rgb { get { return colour; } }
        public double Alpha { get { return alpha; } }
        /// <summary>Changes whenever the drawn pixels change (style, band, opacity step, blink phase, swap phase).</summary>
        public int Key { get { return key; } }
        /// <summary>A mag swap is running (the overlay keeps drawing frames until it ends).</summary>
        public bool Animating { get { return animKind != 0; } }
        /// <summary>1 normal / 2 quick while a swap runs, else 0; Phase = 0..1 through it.</summary>
        public int SwapKind { get { return animKind; } }
        public double Phase { get { return animU; } }

        public void Reset() { has = false; animKind = 0; visible = false; key = 0; }

        private static int Band(int b) { return b < 0 ? 0 : (b > 4 ? 4 : b); }

        private static double Smooth(double t) { t = t < 0 ? 0 : (t > 1 ? 1 : t); return t * t * (3 - 2 * t); }

        /// <summary>Old bar this frame: its offset (share of the track, + = down / right), its alpha factor, whether it is
        /// clipped to the track (slides out) or falls free (dropped). false = no old bar.</summary>
        private bool OldBar(out double off, out double af, out bool clip)
        {
            off = 0; af = 1; clip = true;
            if (animKind == 1)
            {
                if (animU >= OutEnd) return false;
                off = Smooth(animU / OutEnd); return true;
            }
            if (animKind == 2)
            {
                if (animU >= QuickOutEnd) return false;
                double t = animU / QuickOutEnd;
                off = DropTrack * t * t; af = 1 - t; clip = false; return true; // gravity, fades out
            }
            return false;
        }

        /// <summary>New full bar this frame: its offset (1 = still below / left of the track, 0 = in place). false = none yet.</summary>
        private bool NewBar(out double off)
        {
            off = 0;
            double s0 = animKind == 2 ? QuickInStart : InStart;
            if (animU < s0) return false;
            off = 1 - Smooth((animU - s0) / (1 - s0));
            return true;
        }

        /// <summary>Advances to time tMs (any monotonic ms clock).</summary>
        public void Update(AmmoView v, double tMs, int style)
        {
            style = ParseFlags(style);
            if (!v.Active) { visible = false; animKind = 0; has = false; key = 0; return; }
            int target = Band(v.Band);
            if (!has) { has = true; seenSeq = v.ReloadSeq; band = target; animKind = 0; }
            else if (v.ReloadSeq != seenSeq)
            {
                seenSeq = v.ReloadSeq;
                int kind = v.ReloadKind == 2 ? 2 : 1;
                if (animKind == 0) { animFrom = BandFill[band]; animFromBand = band; } // a quick tap during a normal swap keeps the old mag
                else if (kind == 2 && animKind == 1 && animU >= OutEnd) { animFrom = 0; }
                animKind = kind;
                animStart = tMs;
                animMs = v.ReloadMs > 0 ? v.ReloadMs : (kind == 2 ? QuickReloadMs : NormalReloadMs);
                if (animMs < 100) animMs = 100;
            }
            animU = 0;
            if (animKind != 0)
            {
                double t = tMs - animStart;
                if (!double.IsNaN(v.Progress))
                {
                    if (!v.Reloading) t = animMs; // the model finished (or cancelled) the reload: show its band now
                    else t = Math.Max(0, Math.Min(1, v.Progress)) * animMs;
                }
                if (t < 0) t = 0;
                if (t >= animMs) { animKind = 0; band = target; }
                else animU = t / animMs;
            }
            else band = target; // a shot changes the band at once (no animation, no added latency)

            double o, af, no; bool clip;
            if (animKind == 0) fill = BandFill[band];
            else
            {
                fill = 0;
                if (OldBar(out o, out af, out clip)) fill = clip ? Math.Max(0, animFrom - o) : animFrom * af;
                if (NewBar(out no)) fill = Math.Max(fill, 1 - no);
            }
            visible = true;

            colour = (style & Colour) != 0 ? ColourOf(band) : 0xE6E6E6;
            double a = BaseOpacity;
            if ((style & Fade) != 0) a *= 0.35 + 0.65 * BandFill[band];
            if ((style & Flash) != 0 && animKind == 0 && band == 0)
            {
                double ph = tMs % FlashPeriodMs;
                if (ph < 0) ph += FlashPeriodMs;
                if (ph >= FlashPeriodMs / 2) a *= 0.25;
            }
            alpha = a;
            int aq = (int)Math.Round(a * 64);
            int ph8 = animKind == 0 ? 0 : 1 + (int)Math.Round(animU * 255);
            key = 1 | (style << 1) | (band << 6) | (aq << 9) | (animKind << 16) | (ph8 << 18) | ((colour & 0x10) << 22);
        }

        private static int ParseFlags(int style)
        {
            style &= AllStyles;
            if ((style & (Brackets | BottomBar)) == 0) style |= Brackets;
            return style;
        }

        private static int Hash(int rgb) { return ((rgb >> 16) * 7 + ((rgb >> 8) & 0xFF) * 3 + (rgb & 0xFF)) & 0x7FF; }

        private static int FillBand(double f)
        {
            if (f >= 0.875) return 4;
            if (f >= 0.625) return 3;
            if (f >= 0.375) return 2;
            if (f >= 0.175) return 1;
            return 0;
        }

        /// <summary>green -> yellow -> red by band (满 green .. 快空 red).</summary>
        public static int ColourOf(int band)
        {
            switch (Band(band))
            {
                case 4: return 0x46DC5A;
                case 3: return 0x9ADC46;
                case 2: return 0xEAD23C;
                case 1: return 0xF08C32;
                default: return 0xE63C3C;
            }
        }

        // ---- geometry (canvas px, centre (cx, cy)) ----
        /// <summary>Inner distance of the hint from the centre for a look whose outermost pixel is at 'reach' px.</summary>
        public static double Inner(double reach)
        {
            if (!(reach > 0) || double.IsInfinity(reach)) reach = 0;
            return Math.Max(RingRaster.MinHole + 2, reach) + Clear;
        }

        public static double BracketHalfH(double inner) { return Clamp(inner * 0.9, 6, 40); }
        public const double Line = 1.5, Serif = 3.0, BarW = 2.0, BarGap = 1.5, BottomH = 2.0;

        /// <summary>Half the canvas side the hint needs around the centre (px, unrounded).</summary>
        public static double HalfExtent(double inner, int style)
        {
            style = ParseFlags(style);
            double e = 0;
            if ((style & Brackets) != 0)
            {
                double x = inner + BarW + BarGap + Line + Serif;
                double h = BracketHalfH(inner);
                e = Math.Max(e, Math.Max(x, h));
            }
            if ((style & BottomBar) != 0)
            {
                double y = inner + BottomH + ((style & Brackets) != 0 ? 0 : 0);
                double w = BottomHalfW(inner);
                e = Math.Max(e, Math.Max(y, w));
            }
            return e + 2.0; // anti-aliased edge + safety
        }

        public static double BottomHalfW(double inner) { return Clamp(inner * 0.9, 6, 60); }

        /// <summary>Even canvas side that holds the hint (0 when the style draws nothing).</summary>
        public static int CanvasNeed(double inner, int style)
        {
            int n = (int)Math.Ceiling(2 * HalfExtent(inner, style)) + 2;
            if ((n & 1) != 0) n++;
            if (n > RingRaster.MaxCanvas) n = RingRaster.MaxCanvas;
            return n;
        }

        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>
        /// Draws the hint into buf (S x S premultiplied) UNDER the pixels already there, at opacity alpha x level / 255.
        /// Nothing is drawn inside 'inner' (so neither the look nor the centre is touched). Allocation-free.
        /// </summary>
        public void Draw(int[] buf, int S, double cx, double cy, double inner, int style, byte level)
        {
            if (!visible || level == 0) return;
            style = ParseFlags(style);
            double a = alpha * level / 255.0;
            double ta = TrackOpacity * a / BaseOpacity;
            bool fadeOn = (style & Fade) != 0;
            int c = colour;
            int cOld = (style & Colour) != 0 ? ColourOf(animFromBand) : colour;
            int cNew = (style & Colour) != 0 ? ColourOf(4) : colour;
            double aOld = BaseOpacity * level / 255.0 * (fadeOn ? 0.35 + 0.65 * animFrom : 1);
            double aNew = BaseOpacity * level / 255.0;
            double oo = 0, oa = 1, no = 0; bool oclip = true;
            bool hasOld = animKind != 0 && OldBar(out oo, out oa, out oclip);
            if (!hasOld) { oo = 0; oa = 1; oclip = true; }
            bool hasNew = animKind != 0 && NewBar(out no);
            if (!hasNew) no = 0;
            if ((style & Brackets) != 0)
            {
                double hh = BracketHalfH(inner);
                double top = cy - hh, bot = cy + hh, H = bot - top;
                for (int side = -1; side <= 1; side += 2)
                {
                    double b0 = inner, b1 = inner + BarW;
                    double l0 = inner + BarW + BarGap, l1 = l0 + Line;
                    double xa = X(cx, side, b0, b1, true), xb = X(cx, side, b0, b1, false);
                    Rect(buf, S, xa, top, xb, bot, c, ta); // the empty track (under the bars)
                    if (animKind == 0) VBar(buf, S, xa, xb, top, bot, fill, 0, true, c, a);
                    else
                    {
                        if (hasOld) VBar(buf, S, xa, xb, top, bot, animFrom, oo * H, oclip, cOld, aOld * oa);
                        if (hasNew) VBar(buf, S, xa, xb, top, bot, 1.0, no * H, true, cNew, aNew);
                    }
                    Rect(buf, S, X(cx, side, l0, l1, true), top, X(cx, side, l0, l1, false), bot, c, a);
                    double s0 = inner, s1 = l1;
                    Rect(buf, S, X(cx, side, s0, s1, true), top - Line, X(cx, side, s0, s1, false), top, c, a);
                    Rect(buf, S, X(cx, side, s0, s1, true), bot, X(cx, side, s0, s1, false), bot + Line, c, a);
                }
            }
            if ((style & BottomBar) != 0)
            {
                double hw = BottomHalfW(inner);
                double x0 = cx - hw, x1 = cx + hw, W = x1 - x0;
                double y0 = cy + inner, y1 = y0 + BottomH;
                Rect(buf, S, x0, y0, x1, y1, c, ta);
                if (animKind == 0) Rect(buf, S, x0, y0, x0 + W * fill, y1, c, a);
                else
                {
                    if (hasOld)
                    {
                        if (oclip) Rect(buf, S, Math.Min(x1, x0 + oo * W), y0, Math.Min(x1, x0 + W * animFrom + oo * W), y1, cOld, aOld * oa); // slides out right
                        else { double dy = oo / DropTrack * DropPx; Rect(buf, S, x0, y0 + dy, x0 + W * animFrom, y1 + dy, cOld, aOld * oa); } // dropped
                    }
                    if (hasNew) Rect(buf, S, x0, y0, Math.Max(x0, x1 - no * W), y1, cNew, aNew); // slides in from the left
                }
            }
        }

        /// <summary>A vertical fill bar of share f, bottom-aligned in [top, bot], moved down by dy px; clipped to the track when
        /// clip, else free (a dropped mag) within the canvas.</summary>
        private static void VBar(int[] buf, int S, double xa, double xb, double top, double bot, double f, double dy, bool clip, int rgb, double a)
        {
            if (!(f > 0) || !(a > 0)) return;
            double y0 = bot - (bot - top) * f + dy, y1 = bot + dy;
            if (clip) { if (y0 < top) y0 = top; if (y1 > bot) y1 = bot; }
            Rect(buf, S, xa, y0, xb, y1, rgb, a);
        }

        /// <summary>x range [d0, d1] from the centre on one side, as left / right screen x.</summary>
        private static double X(double cx, int side, double d0, double d1, bool left)
        {
            if (side > 0) return left ? cx + d0 : cx + d1;
            return left ? cx - d1 : cx - d0;
        }

        /// <summary>Anti-aliased rectangle (exact area coverage) drawn under the existing premultiplied pixels.</summary>
        internal static void Rect(int[] buf, int S, double x0, double y0, double x1, double y1, int rgb, double a)
        {
            if (!(a > 0) || !(x1 > x0) || !(y1 > y0)) return;
            if (a > 1) a = 1;
            int i0 = (int)Math.Floor(x0), i1 = (int)Math.Ceiling(x1) - 1;
            int j0 = (int)Math.Floor(y0), j1 = (int)Math.Ceiling(y1) - 1;
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0;
            if (i1 > S - 1) i1 = S - 1; if (j1 > S - 1) j1 = S - 1;
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            for (int j = j0; j <= j1; j++)
            {
                double cyv = Math.Min(y1, j + 1) - Math.Max(y0, j);
                if (cyv <= 0) continue;
                int row = j * S;
                for (int i = i0; i <= i1; i++)
                {
                    double cxv = Math.Min(x1, i + 1) - Math.Max(x0, i);
                    if (cxv <= 0) continue;
                    double cov = cxv * cyv * a;
                    int sa = (int)Math.Round(cov * 255);
                    if (sa <= 0) continue;
                    int d = buf[row + i];
                    int da = (int)((uint)d >> 24);
                    if (da >= 255) continue;
                    int k = 255 - da; // under: dst + src x (1 - dstA)
                    int na = da + (sa * k + 127) / 255;
                    int nr = ((d >> 16) & 0xFF) + (r * sa / 255 * k + 127) / 255;
                    int ng = ((d >> 8) & 0xFF) + (g * sa / 255 * k + 127) / 255;
                    int nb = (d & 0xFF) + (b * sa / 255 * k + 127) / 255;
                    if (na > 255) na = 255;
                    if (nr > na) nr = na; if (ng > na) ng = na; if (nb > na) nb = na;
                    buf[row + i] = (na << 24) | (nr << 16) | (ng << 8) | nb;
                }
            }
        }
    }
}
