// PURE file: only System (+ the plain settings classes of LookFxSettings.cs). Compiles under net48 (C# 5) and net8.0.
// v5.1 (W22, Leo decision 1): the ammo hint drawn in the crosshair's own canvas. Only a range is shown (5 bands), never a
// number; the crosshair's pixels are never changed (the hint is drawn UNDER them, outside the look's reach) and the centre
// stays empty. Hidden with the crosshair (开镜 / 丢雷 / off), because it is part of the same canvas (level 0 = nothing drawn).
// v5.1.1 (X3, Leo round B3): the side brackets are gone. The hint is ONE bar (default: a small bar below the crosshair) with
// many look options (length, thickness, distance, segments, rounded, glow, track, frame, colour per band, fade-in), an
// 只在剩余少于 X% 时显示 option, two customisable low-ammo warning levels and a customisable animation per reload kind.
using System;
using System.Threading;

namespace TarkovCrosshair
{
    /// <summary>What the motion engine says about the ammo this frame (filled by the overlay from W21's AmmoModel).</summary>
    public struct AmmoView
    {
        /// <summary>Feature on (false = nothing drawn).</summary>
        public bool Active;
        /// <summary>4 满, 3 大半, 2 一半, 1 少, 0 快空.</summary>
        public int Band;
        /// <summary>+1 for every accepted reload; ReloadKind 1 = normal (R), 2 = quick (double-tap R).</summary>
        public int ReloadSeq, ReloadKind;
        /// <summary>Reload length, ms (0 = default: normal 2400, quick 900).</summary>
        public double ReloadMs;
        /// <summary>The model's reload state and its 0..1 Progress (NaN = not provided: the hint times the reload itself).</summary>
        public bool Reloading;
        public double Progress;
        /// <summary>v5.1.1: share of the mag left 0..1, used only for the 只在少于 X% 时显示 / warning thresholds (never shown
        /// as a number). NaN or default 0 with Band &gt; 0 = not provided: the band's own fill is used.</summary>
        public double Share;
    }

    /// <summary>One warning level, ready for the render thread.</summary>
    public struct AmmoWarnCfg
    {
        public bool On;
        public double Share, Period, Intensity;
        public int Style, Rgb, Blinks;
        public const int Flash = 0, Pulse = 1, Colour = 2, Glow = 3, Blink = 4, Steady = 5;
    }

    /// <summary>One reload animation, ready for the render thread.</summary>
    public struct AmmoReloadCfg
    {
        public int Type, Ease, Marker, Rgb;
        public bool Reverse, ColourOn, Drop;
        public double Out, In;
        public const int Slide = 0, Drain = 1, Fade = 2, Drop_ = 3, FlashFill = 4, None = 5, Steps = 6, Drs = 7;
        public const int MarkNone = 0, MarkDots = 1, MarkProgress = 2, MarkIcon = 3;
    }

    /// <summary>The bar look compiled from AmmoBarSettings (built only when the settings change; immutable afterwards).</summary>
    public sealed class AmmoBarCfg
    {
        private static int nextId;
        /// <summary>Unique per build (part of AmmoDisplay.Key, so a settings change redraws).</summary>
        public readonly int Id;
        public int Pos;                // 0 below, 1 above, 2 left, 3 right
        public double Len, Thick, Gap, SegGap, GlowPx, TrackA, Opacity, FadeInMs, ShowBelow;
        public int Segments;
        public bool Round, Glow, Track, Frame, ShowBelowOn, Slant, Led, Backing;
        /// <summary>Shear of a slanted (斜切) bar: u shifts this many px per px across (tan of 斜切角度).</summary>
        public double SlantK = 0.6;
        public bool Stripes;
        /// <summary>竞速 shift-light colours along the bar (前 / 中 / 后) and the stripe accent.</summary>
        public readonly int[] Led3 = { 0x19E650, 0xFFD21A, 0xFF2A2A };
        public int Accent = 0xFF2A2A;
        public int LedColour(double frac) { return frac < 0.5 ? Led3[0] : (frac < 0.8 ? Led3[1] : Led3[2]); }
        /// <summary>Room the frame / black plate takes between the crosshair and the bar, px.</summary>
        public double PlateRoom { get { return Frame || Backing ? 2 : 0; } }
        public int Single;
        /// <summary>Colour per band, index = band (0 快空 .. 4 满).</summary>
        public readonly int[] Band = new int[5];
        public AmmoWarnCfg W1, W2;
        public AmmoReloadCfg RN, RQ;

        public AmmoBarCfg() { Id = Interlocked.Increment(ref nextId); }

        public static AmmoBarCfg From(AmmoBarSettings s)
        {
            if (s == null) s = AmmoBarSettings.Defaults();
            AmmoBarCfg c = new AmmoBarCfg();
            c.Pos = Math.Max(0, Array.IndexOf(AmmoBarSettings.Positions, s.Pos));
            c.Len = Cl(s.LenPx, 6, 160, 24); c.Thick = Cl(s.ThickPx, 1, 12, 2); c.Gap = Cl(s.GapPx, 0, 60, 4);
            c.Segments = s.Segments >= 2 && s.Segments <= 20 ? s.Segments : 0; c.SegGap = Cl(s.SegGapPx, 0.5, 6, 1);
            c.Round = s.Round; c.Glow = s.Glow; c.GlowPx = Cl(s.GlowPx, 1, 10, 3);
            c.Track = s.Track; c.TrackA = Cl(s.TrackPct, 0, 100, 22) / 100.0; c.Frame = s.Frame;
            c.Slant = s.Slant; c.Led = s.Led; c.Backing = s.Backing; c.Stripes = s.Stripes;
            c.SlantK = Math.Tan(Cl(s.SlantDeg, 0, 60, 31) * Math.PI / 180.0);
            for (int i = 0; i < 3; i++) c.Led3[i] = FxClamp.Rgb(s.LedColours != null && i < s.LedColours.Length ? s.LedColours[i] : null, FxClamp.Rgb(AmmoBarSettings.DefaultLedColours[i], 0xFFFFFF));
            c.Accent = FxClamp.Rgb(s.AccentColour, 0xFF2A2A);
            c.Opacity = Cl(s.OpacityPct, 10, 100, 85) / 100.0; c.FadeInMs = Cl(s.FadeInMs, 0, 1000, 150);
            c.ShowBelowOn = s.ShowBelowOn; c.ShowBelow = Cl(s.ShowBelowPct, 5, 100, 35) / 100.0;
            c.Single = FxClamp.Rgb(s.Colour, 0xE6E6E6);
            for (int i = 0; i < 5; i++)
            {
                string h = s.BandColours != null && i < s.BandColours.Length ? s.BandColours[i] : null;
                c.Band[4 - i] = FxClamp.Rgb(h, FxClamp.Rgb(AmmoBarSettings.DefaultBandColours[i], 0xFFFFFF));
            }
            c.W1 = Warn(s.Warn1 ?? AmmoWarnSettings.Level1()); c.W2 = Warn(s.Warn2 ?? AmmoWarnSettings.Level2());
            c.RN = Reload(s.ReloadNormal ?? ReloadAnimSettings.Normal()); c.RQ = Reload(s.ReloadQuick ?? ReloadAnimSettings.Quick());
            return c;
        }

        private static AmmoWarnCfg Warn(AmmoWarnSettings w)
        {
            AmmoWarnCfg r = new AmmoWarnCfg();
            r.On = w.On; r.Share = Cl(w.Pct, 1, 100, 15) / 100.0; r.Style = Math.Max(0, Array.IndexOf(AmmoWarnSettings.Styles, w.Style));
            r.Period = Cl(w.PeriodMs, 80, 2000, 250); r.Rgb = FxClamp.Rgb(w.Colour, 0xFF3B3B); r.Intensity = Cl(w.IntensityPct, 10, 100, 80) / 100.0;
            r.Blinks = (int)Cl(w.Blinks, 1, 10, 3);
            return r;
        }

        private static AmmoReloadCfg Reload(ReloadAnimSettings a)
        {
            AmmoReloadCfg r = new AmmoReloadCfg();
            r.Type = Math.Max(0, Array.IndexOf(ReloadAnimSettings.Types, a.Type)); r.Ease = Math.Max(0, Array.IndexOf(ReloadAnimSettings.Eases, a.Ease));
            r.Marker = Math.Max(0, Array.IndexOf(ReloadAnimSettings.Markers, a.Marker));
            r.Reverse = a.Reverse; r.ColourOn = a.ColourOn; r.Rgb = FxClamp.Rgb(a.Colour, 0x5CF2FF); r.Drop = a.Drop;
            r.Out = Cl(a.OutPct, 5, 90, 30) / 100.0; r.In = Cl(a.InPct, 5, 90, 40) / 100.0;
            return r;
        }

        private static double Cl(double v, double lo, double hi, double def) { return double.IsNaN(v) || v < lo || v > hi ? def : v; }

        /// <summary>The fresh-install look (a small bar below, 警告 on at 15 %).</summary>
        public static readonly AmmoBarCfg Default = From(AmmoBarSettings.Defaults());
        /// <summary>The same without any warning (old callers whose style has no 闪烁).</summary>
        public static readonly AmmoBarCfg DefaultNoWarn = NoWarn();

        private static AmmoBarCfg NoWarn() { AmmoBarSettings s = AmmoBarSettings.Defaults(); s.Warn1.On = false; s.Warn2.On = false; return From(s); }
    }

    /// <summary>One ammo hint (render thread only). Update once per frame, then Key tells whether the pixels changed.</summary>
    public sealed class AmmoDisplay
    {
        // ---- style flags (AmmoStyle in the settings, v5.1). v5.1.1: only 变色 (colour) and 变淡 (fade) still matter; the
        // shape words (brackets, bottombar) are read and ignored, 闪烁 (flash) is now the warning group of the bar settings.
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
        public const double FlashPeriodMs = 250;
        public const double BaseOpacity = 0.85, TrackOpacity = 0.22;
        /// <summary>Gap between the look's outermost pixel and the hint, px.</summary>
        public const double Clear = 3.0;
        /// <summary>How far an old mag falls (px) when it is dropped.</summary>
        public const double DropPx = 8.0;
        /// <summary>Room for the 发光 of a warning, px.</summary>
        public const double WarnGlowPx = 3.5;
        /// <summary>Room along the bar for the reload marker, px.</summary>
        public const double MarkerRoom = 12.0;

        /// <summary>"brackets,colour" -> flags (unchanged from v5.1, so old files read the same).</summary>
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
            if ((f & (Brackets | BottomBar)) == 0) f |= Brackets;
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
        private AmmoBarCfg cfg = AmmoBarCfg.Default;
        private bool has;
        private int seenSeq;
        private int animKind;        // 0 none, 1 normal reload, 2 quick reload
        private double animStart, animFrom, animMs, animU, animEl;
        private int animFromBand;
        private double fill;
        private int band;
        private bool visible;
        private int colour;
        private double alpha, vis, lastT, glowBoost;
        private int warnLevel;
        private double warnStart;
        private int key, styleNow;

        public double Fill { get { return fill; } }
        public bool Visible { get { return visible; } }
        public int Rgb { get { return colour; } }
        public double Alpha { get { return alpha; } }
        /// <summary>0 none, 1 警告, 2 紧急警告 (this frame).</summary>
        public int WarnLevel { get { return warnLevel; } }
        public int Key { get { return key; } }
        public bool Animating { get { return animKind != 0 || (vis > 0 && vis < 1); } }
        public int SwapKind { get { return animKind; } }
        public double Phase { get { return animU; } }
        public AmmoBarCfg Cfg { get { return cfg; } }

        public void Reset() { has = false; animKind = 0; visible = false; key = 0; vis = 0; warnLevel = 0; }

        private static int Band(int b) { return b < 0 ? 0 : (b > 4 ? 4 : b); }
        private static double C01(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }

        /// <summary>The reload easing curves (匀速 / 平滑 / 先快后慢 / 先慢后快 / 回弹).</summary>
        public static double Ease(int kind, double t)
        {
            t = C01(t);
            switch (kind)
            {
                case 0: return t;
                case 2: return 1 - (1 - t) * (1 - t);
                case 3: return t * t;
                case 4: { double u = t - 1; return 1 + 2.70158 * u * u * u + 1.70158 * u * u; }
                default: return t * t * (3 - 2 * t);
            }
        }

        /// <summary>Advances to tMs with the v5.1 call shape (fresh-install bar; a warning only when the style has 闪烁).</summary>
        public void Update(AmmoView v, double tMs, int style)
        {
            Update(v, tMs, style, (ParseFlags(style) & Flash) != 0 ? AmmoBarCfg.Default : AmmoBarCfg.DefaultNoWarn);
        }

        /// <summary>Advances to time tMs (any monotonic ms clock) with this bar look.</summary>
        public void Update(AmmoView v, double tMs, int style, AmmoBarCfg c)
        {
            cfg = c ?? AmmoBarCfg.Default;
            style = ParseFlags(style);
            styleNow = style;
            if (!v.Active) { visible = false; animKind = 0; has = false; key = 0; vis = 0; return; }
            int target = Band(v.Band);
            bool first = !has;
            if (first) { has = true; seenSeq = v.ReloadSeq; band = target; animKind = 0; lastT = tMs; warnLevel = 0; }
            else if (v.ReloadSeq != seenSeq)
            {
                seenSeq = v.ReloadSeq;
                int kind = v.ReloadKind == 2 ? 2 : 1;
                if (animKind == 0) { animFrom = BandFill[band]; animFromBand = band; }
                animKind = kind;
                animStart = tMs;
                animMs = v.ReloadMs > 0 ? v.ReloadMs : (kind == 2 ? QuickReloadMs : NormalReloadMs);
                if (animMs < 100) animMs = 100;
            }
            animU = 0; animEl = 0;
            if (animKind != 0)
            {
                double t = tMs - animStart;
                if (!double.IsNaN(v.Progress))
                {
                    if (!v.Reloading) t = animMs;
                    else t = Math.Max(0, Math.Min(1, v.Progress)) * animMs;
                }
                if (t < 0) t = 0;
                if (t >= animMs) { animKind = 0; band = target; }
                else { animU = t / animMs; animEl = t; }
            }
            else band = target; // a shot changes the band at once (no animation, no added latency)
            fill = animKind == 0 ? BandFill[band] : 0;

            double share = !double.IsNaN(v.Share) && (v.Share > 0 || target == 0) ? C01(v.Share) : BandFill[band];
            if (!double.IsNaN(v.Share) && v.Share == 0 && target == 0) share = 0;
            bool show = !cfg.ShowBelowOn || share < cfg.ShowBelow - 1e-9 || animKind != 0;
            double dt = tMs - lastT; lastT = tMs;
            if (!(dt >= 0) || dt > 1000) dt = 1000;
            if (first || cfg.FadeInMs <= 0) vis = show ? 1 : 0;
            else vis = C01(vis + (show ? dt : -dt) / cfg.FadeInMs);
            vis = Math.Round(vis * 32) / 32;
            if (vis <= 0) { visible = false; key = 0; warnLevel = 0; return; }
            visible = true;

            int wl = 0;
            if (animKind == 0)
            {
                if (cfg.W2.On && share < cfg.W2.Share - 1e-9) wl = 2;
                else if (cfg.W1.On && share < cfg.W1.Share - 1e-9) wl = 1;
            }
            if (wl != warnLevel) { warnLevel = wl; warnStart = tMs; }

            int rgb = (style & Colour) != 0 ? cfg.Band[band] : cfg.Single;
            double a = cfg.Opacity, af = 1, ge = 0;
            if ((style & Fade) != 0) a *= 0.35 + 0.65 * BandFill[band];
            if (wl > 0) WarnFx(wl == 2 ? cfg.W2 : cfg.W1, tMs - warnStart, ref rgb, ref af, ref ge);
            colour = rgb;
            glowBoost = ge;
            alpha = Math.Round(a * af * vis * 64) / 64;
            int ph = animKind == 0 ? 0 : 1 + (int)Math.Round(animU * 255);
            unchecked
            {
                int h = 17;
                h = h * 31 + band; h = h * 31 + (int)Math.Round(alpha * 64); h = h * 31 + rgb; h = h * 31 + (int)Math.Round(ge * 16);
                h = h * 31 + animKind; h = h * 31 + ph; h = h * 31 + cfg.Id; h = h * 31 + style; h = h * 31 + (int)(vis * 32);
                if (animKind != 0) h = h * 31 + (int)(animEl / 60); // reload marker steps
                key = h | 1;
            }
        }

        /// <summary>One warning level's effect at 'el' ms since it began: colour mix, opacity factor, extra glow.</summary>
        public static void WarnFx(AmmoWarnCfg w, double el, ref int rgb, ref double af, ref double ge)
        {
            double I = w.Intensity, per = w.Period > 0 ? w.Period : 250;
            double p = (el % per) / per; if (p < 0) p += 1;
            double m;
            switch (w.Style)
            {
                case AmmoWarnCfg.Pulse: { double s = 0.5 + 0.5 * Math.Cos(2 * Math.PI * p); m = I * s; af = 1 - 0.6 * I * (1 - s); break; }
                case AmmoWarnCfg.Colour: m = I * (0.5 - 0.5 * Math.Cos(2 * Math.PI * p)); break;
                case AmmoWarnCfg.Glow: m = 0.5 * I; ge = I * (0.55 + 0.45 * Math.Sin(2 * Math.PI * p)); break;
                case AmmoWarnCfg.Blink: m = I; if (el < w.Blinks * per) af = p < 0.5 ? 1 : 1 - 0.9 * I; break;
                case AmmoWarnCfg.Steady: m = I; break;
                default: m = I; af = p < 0.5 ? 1 : 1 - 0.85 * I; break;
            }
            m = Math.Round(C01(m) * 32) / 32; af = Math.Round(C01(af) * 32) / 32; ge = Math.Round(C01(ge) * 16) / 16;
            rgb = Mix(rgb, w.Rgb, m);
        }

        public static int Mix(int a, int b, double t)
        {
            int r = (int)Math.Round(((a >> 16) & 0xFF) + ((((b >> 16) & 0xFF) - ((a >> 16) & 0xFF)) * t));
            int g = (int)Math.Round(((a >> 8) & 0xFF) + ((((b >> 8) & 0xFF) - ((a >> 8) & 0xFF)) * t));
            int bl = (int)Math.Round((a & 0xFF) + (((b & 0xFF) - (a & 0xFF)) * t));
            return (r << 16) | (g << 8) | bl;
        }

        private static int ParseFlags(int style)
        {
            style &= AllStyles;
            if ((style & (Brackets | BottomBar)) == 0) style |= Brackets;
            return style;
        }

        /// <summary>green -> yellow -> red by band (满 green .. 快空 red), the default band colours.</summary>
        public static int ColourOf(int band) { return AmmoBarCfg.Default.Band[Band(band)]; }

        // ---- geometry (canvas px, centre (cx, cy)) ----
        /// <summary>Inner distance of the hint from the centre for a look whose outermost pixel is at 'reach' px.</summary>
        public static double Inner(double reach)
        {
            if (!(reach > 0) || double.IsInfinity(reach)) reach = 0;
            return Math.Max(RingRaster.MinHole + 2, reach) + Clear;
        }

        public static double HalfExtent(double inner, int style) { return HalfExtent(inner, style, AmmoBarCfg.Default); }

        /// <summary>Half the canvas side the bar (with glow, frame, a dropped mag and the reload marker) needs.</summary>
        public static double HalfExtent(double inner, int style, AmmoBarCfg c)
        {
            if (c == null) c = AmmoBarCfg.Default;
            double g = Math.Max(c.Glow ? c.GlowPx : 0, WarnGlowPx);
            double across = inner + c.Gap + c.PlateRoom + c.Thick + Math.Max(g, 3.5) + DropPx + 1;
            double along = c.Len / 2 + MarkerRoom + g + (c.Slant ? c.SlantK * (c.Thick + 6) : 0);
            return Math.Max(across, along) + 2.0;
        }

        public static int CanvasNeed(double inner, int style) { return CanvasNeed(inner, style, AmmoBarCfg.Default); }

        public static int CanvasNeed(double inner, int style, AmmoBarCfg c)
        {
            int n = (int)Math.Ceiling(2 * HalfExtent(inner, style, c)) + 2;
            if ((n & 1) != 0) n++;
            if (n > RingRaster.MaxCanvas) n = RingRaster.MaxCanvas;
            return n;
        }

        // ---- drawing ----
        private int[] dBuf; private int dS; private double dCx, dCy, dInner, dD0; private bool dLed, dTrack;

        /// <summary>Draws the bar into buf (S x S premultiplied) UNDER the pixels already there, at level / 255. Nothing is
        /// drawn inside 'inner' (so neither the look nor the centre is touched). Allocation-free.</summary>
        public void Draw(int[] buf, int S, double cx, double cy, double inner, int style, byte level)
        {
            if (!visible || level == 0) return;
            AmmoBarCfg c = cfg;
            double lv = level / 255.0;
            dBuf = buf; dS = S; dCx = cx; dCy = cy; dInner = inner;
            dD0 = inner + c.Gap + c.PlateRoom;
            dLed = c.Led && warnLevel == 0;
            double baseA = c.Opacity * vis * lv;
            double A = alpha * lv;
            double glow = c.Glow ? c.GlowPx : 0, glowA = c.Glow ? 0.45 : 0;
            if (glowBoost > 0) { glow = Math.Max(glow, WarnGlowPx); glowA = Math.Max(glowA, 0.7 * glowBoost); }
            // lit part first (it is on top: everything is drawn under what is already there)
            if (animKind == 0)
            {
                double f = fill;
                if (c.Segments >= 2) f = Math.Min(1, Math.Ceiling(f * c.Segments - 1e-9) / c.Segments);
                Range(0, f, 0, false, colour, A, glow, glowA);
            }
            else DrawReload(style, baseA, glow, glowA);
            dTrack = true;
            if (c.Track && c.TrackA > 0) Range(0, 1, 0, false, colour, c.TrackA * baseA, 0, 0);
            dTrack = false;
            if (c.Frame)
            {
                double L = c.Len, T = c.Thick, a = 0.55 * baseA, d = dD0;
                Box(-L / 2 - 2, L / 2 + 2, d - 2, d - 1, colour, a, 0, 0, 0);
                Box(-L / 2 - 2, L / 2 + 2, d + T + 1, d + T + 2, colour, a, 0, 0, 0);
                Box(-L / 2 - 2, -L / 2 - 1, d - 1, d + T + 1, colour, a, 0, 0, 0);
                Box(L / 2 + 1, L / 2 + 2, d - 1, d + T + 1, colour, a, 0, 0, 0);
            }
            if (c.Backing)
            {
                // 竞速: speed stripes past the far end, then the high-contrast black plate under everything
                double L = c.Len, T = c.Thick, d = dD0;
                Box(-L / 2 - 3, L / 2 + 3, d - 2, d + T + 2, 0x000000, 0.7 * baseA, 0, 0, 0);
            }
            if (c.Stripes)
            {
                double L = c.Len, T = c.Thick, d = dD0;
                for (int i = 0; i < 3; i++) Box(L / 2 + 4 + i * 3, L / 2 + 5.5 + i * 3, d, d + T, c.Accent, baseA * (1 - i * 0.3), 0, 0, 0);
            }
            dBuf = null;
        }

        private void DrawReload(int style, double baseA, double glow, double glowA)
        {
            AmmoBarCfg c = cfg;
            AmmoReloadCfg rc = animKind == 2 ? c.RQ : c.RN;
            double u = animU;
            bool col = (style & Colour) != 0, fadeOn = (style & Fade) != 0;
            int cOld = rc.ColourOn ? rc.Rgb : (col ? c.Band[animFromBand] : c.Single);
            int cNew = rc.ColourOn ? rc.Rgb : (col ? c.Band[4] : c.Single);
            double aOld = baseA * (fadeOn ? 0.35 + 0.65 * animFrom : 1), aNew = baseA;
            bool rev = rc.Reverse;
            if (rc.ColourOn) dLed = false;
            if (rc.Type == AmmoReloadCfg.None)
            {
                Range(0, animFrom, 0, rev, cOld, aOld, glow, glowA);
            }
            else
            {
                if (u < rc.Out && rc.Type != AmmoReloadCfg.FlashFill && rc.Type < AmmoReloadCfg.Steps)
                {
                    double e = Ease(rc.Ease, u / rc.Out), drop = 0, oa = aOld, f0 = 0, f1 = animFrom;
                    switch (rc.Type)
                    {
                        case AmmoReloadCfg.Slide: f0 = e; f1 = Math.Min(1, e + animFrom); break;
                        case AmmoReloadCfg.Drain: f1 = animFrom * (1 - C01(e)); break;
                        case AmmoReloadCfg.Fade: oa *= 1 - C01(e); break;
                        default: drop = DropPx * e * e; oa *= 1 - C01(e); break; // 掉落
                    }
                    if (rc.Drop && rc.Type != AmmoReloadCfg.Drop_) { drop = DropPx * e * e; oa *= 1 - 0.7 * C01(e); }
                    if (f1 > f0) Range(f0, f1, drop, rev, cOld, oa, glow, glowA);
                }
                double s0 = 1 - rc.In;
                if (u >= s0)
                {
                    double e = Ease(rc.Ease, (u - s0) / rc.In), na = aNew, f1 = C01(e);
                    switch (rc.Type)
                    {
                        case AmmoReloadCfg.Fade: na *= C01(e); f1 = 1; break;
                        case AmmoReloadCfg.Drop_: f1 = 1; break; // snaps in
                        case AmmoReloadCfg.FlashFill: if (((int)(animEl / 70)) % 2 == 1) na *= 0.35; break;
                        case AmmoReloadCfg.Steps: { int n = c.Segments >= 2 ? c.Segments : 5; f1 = Math.Floor(C01(e) * n + 1e-9) / n; break; } // LEDs one by one, no fade
                        case AmmoReloadCfg.Drs: // 闪光速换: a hard double flash of the whole bar, then the new mag snaps in
                            {
                                double t2 = (u - s0) / rc.In;
                                if (t2 < 0.45) { if (((int)(animEl / 60)) % 2 == 1) na = 0; f1 = 1; }
                                else f1 = 1;
                                break;
                            }
                    }
                    if (f1 > 0) Range(0, f1, 0, rev, cNew, na, glow, glowA);
                }
            }
            Marker(rc, u, cNew, baseA);
        }

        /// <summary>The 'reloading' marker beside the far end of the bar (三个点 / 进度线 / 转圈图标).</summary>
        private void Marker(AmmoReloadCfg rc, double u, int rgb, double a)
        {
            AmmoBarCfg c = cfg;
            double L = c.Len, T = c.Thick, d = dD0, mid = d + T / 2;
            double sgn = rc.Reverse ? -1 : 1;
            switch (rc.Marker)
            {
                case AmmoReloadCfg.MarkDots:
                    {
                        double r = Math.Max(0.8, Math.Min(1.6, T * 0.45));
                        int lit = ((int)(animEl / 160)) % 4;
                        for (int k = 0; k < 3; k++)
                        {
                            double uu = sgn * (L / 2 + 3 + r + k * (2 * r + 1.4));
                            double x, y; Map(uu, mid, out x, out y);
                            Disc(x, y, r, rgb, k < lit ? a : a * 0.3);
                        }
                        break;
                    }
                case AmmoReloadCfg.MarkProgress:
                    {
                        double w0 = d + T + 1.5, p = C01(u);
                        if (rc.Reverse) Box(L / 2 - L * p, L / 2, w0, w0 + 1, rgb, a * 0.9, 0, 0, 0);
                        else Box(-L / 2, -L / 2 + L * p, w0, w0 + 1, rgb, a * 0.9, 0, 0, 0);
                        break;
                    }
                case AmmoReloadCfg.MarkIcon:
                    {
                        double x, y; Map(sgn * (L / 2 + 6), mid, out x, out y);
                        Spinner(x, y, 2.8, 1.1, animEl * 0.012, rgb, a);
                        break;
                    }
            }
        }

        /// <summary>Lit fraction range [f0, f1] of the bar (segments respected), moved 'dw' px away from the centre.</summary>
        private void Range(double f0, double f1, double dw, bool rev, int rgb, double a, double glow, double glowA)
        {
            if (!(a > 0) || !(f1 > f0)) return;
            AmmoBarCfg c = cfg;
            int N = c.Segments >= 2 ? c.Segments : 1;
            double L = c.Len, gap = N > 1 ? c.SegGap : 0;
            double segLen = (L - (N - 1) * gap) / N;
            if (segLen < 0.5) { segLen = L / N; gap = 0; }
            double w0 = dD0 + dw, w1 = w0 + c.Thick;
            if (glow > 0 && glowA > 0)
            {
                // one glow around the whole lit range (not one per segment: N times less work, no double-bright gaps)
                double g0 = -L / 2 + f0 * L, g1 = -L / 2 + f1 * L;
                if (rev) { double t = -g0; g0 = -g1; g1 = t; }
                GlowOnly(g0, g1, w0, w1, dLed && !dTrack ? c.LedColour(Math.Min(0.999, f1 - 0.5 / N)) : rgb, a * glowA, glow);
            }
            for (int k = 0; k < N; k++)
            {
                double s0 = (double)k / N, s1 = (double)(k + 1) / N;
                double o0 = Math.Max(s0, f0), o1 = Math.Min(s1, f1);
                if (o1 <= o0) continue;
                double b = -L / 2 + k * (segLen + gap);
                double u0 = b + (o0 - s0) * N * segLen, u1 = b + (o1 - s0) * N * segLen;
                if (rev) { double t = -u0; u0 = -u1; u1 = t; }
                double rad = c.Round ? Math.Min(c.Thick, u1 - u0) / 2 : 0;
                int col = dLed && !dTrack ? c.LedColour((k + 0.5) / N) : rgb;
                Box(u0, u1, w0, w1, col, a, rad, 0, 0);
            }
        }


        /// <summary>Canvas -> bar coordinates (the inverse of Map).</summary>
        private void Unmap(double x, double y, out double u, out double w)
        {
            switch (cfg.Pos)
            {
                case 1: u = x - dCx; w = dCy - y; break;
                case 2: w = dCx - x; u = dCy - y; break;
                case 3: w = x - dCx; u = dCy - y; break;
                default: u = x - dCx; w = y - dCy; break;
            }
        }

        /// <summary>Bar coordinates -> canvas: u along the bar (fill starts at -L/2: left, or bottom), w = distance from the centre.</summary>
        private void Map(double u, double w, out double x, out double y)
        {
            switch (cfg.Pos)
            {
                case 1: x = dCx + u; y = dCy - w; break;
                case 2: x = dCx - w; y = dCy - u; break;
                case 3: x = dCx + w; y = dCy - u; break;
                default: x = dCx + u; y = dCy + w; break;
            }
        }

        /// <summary>A box in bar coordinates: anti-aliased, optionally rounded (radius) and glowing; drawn under the pixels.</summary>
        private void Box(double u0, double u1, double w0, double w1, int rgb, double a, double radius, double glow, double glowA)
        {
            if (!(a > 0) || !(u1 > u0) || !(w1 > w0)) return;
            double xa, ya, xb, yb;
            Map(u0, w0, out xa, out ya); Map(u1, w1, out xb, out yb);
            double x0 = Math.Min(xa, xb), x1 = Math.Max(xa, xb), y0 = Math.Min(ya, yb), y1 = Math.Max(ya, yb);
            if (cfg.Slant) { SlantBox(u0, u1, w0, w1, rgb, a, radius, glow, glowA); return; }
            if (radius <= 0 && glow <= 0) { Rect(dBuf, dS, x0, y0, x1, y1, rgb, a); return; }
            double hx = (x1 - x0) / 2, hy = (y1 - y0) / 2, mx = x0 + hx, my = y0 + hy;
            double r = Math.Min(radius, Math.Min(hx, hy));
            double ext = glow + 1;
            int i0 = (int)Math.Floor(x0 - ext), i1 = (int)Math.Ceiling(x1 + ext), j0 = (int)Math.Floor(y0 - ext), j1 = (int)Math.Ceiling(y1 + ext);
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0; if (i1 > dS - 1) i1 = dS - 1; if (j1 > dS - 1) j1 = dS - 1;
            double in2 = dInner * dInner;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = i + 0.5, py = j + 0.5;
                    double ddx = px - dCx, ddy = py - dCy;
                    if (ddx * ddx + ddy * ddy < in2) continue; // the centre stays empty
                    double qx = Math.Abs(px - mx) - (hx - r), qy = Math.Abs(py - my) - (hy - r);
                    double ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
                    double sd = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
                    double v = a * C01(0.5 - sd);
                    if (glow > 0 && sd > -0.5)
                    {
                        double gk = C01(1 - Math.Max(sd, 0) / glow);
                        double gv = glowA * a * gk * gk;
                        if (gv > v) v = gv;
                    }
                    if (v > 0) Plot(dBuf, dS, i, j, rgb, v);
                }
        }

        /// <summary>Only the glow halo around a box (outside it), under the existing pixels.</summary>
        private void GlowOnly(double u0, double u1, double w0, double w1, int rgb, double ga, double glow)
        {
            if (!(ga > 0) || !(u1 > u0)) return;
            double k = cfg.Slant ? cfg.SlantK : 0, wm = (w0 + w1) / 2, hw = (w1 - w0) / 2, um = (u0 + u1) / 2, hu = (u1 - u0) / 2;
            double ext = glow + 1 + k * hw;
            double xa, ya, xb, yb;
            Map(u0 - ext, w0 - glow - 1, out xa, out ya); Map(u1 + ext, w1 + glow + 1, out xb, out yb);
            int i0 = (int)Math.Floor(Math.Min(xa, xb)), i1 = (int)Math.Ceiling(Math.Max(xa, xb)), j0 = (int)Math.Floor(Math.Min(ya, yb)), j1 = (int)Math.Ceiling(Math.Max(ya, yb));
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0; if (i1 > dS - 1) i1 = dS - 1; if (j1 > dS - 1) j1 = dS - 1;
            double in2 = dInner * dInner, inv = 1.0 / glow, g2 = glow * glow;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = i + 0.5, py = j + 0.5, ddx = px - dCx, ddy = py - dCy;
                    if (ddx * ddx + ddy * ddy < in2) continue;
                    double u, w; Unmap(px, py, out u, out w);
                    if (k != 0) u -= (w - wm) * k;
                    double qx = Math.Abs(u - um) - hu, qy = Math.Abs(w - wm) - hw;
                    if (qx <= 0 && qy <= 0) continue; // inside: the bar itself is drawn there
                    double ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0, sd2 = ox * ox + oy * oy;
                    if (sd2 >= g2) continue;
                    double gk = 1 - Math.Sqrt(sd2) * inv;
                    Plot(dBuf, dS, i, j, rgb, ga * gk * gk);
                }
        }

        /// <summary>A box sheared along the bar (斜切, the 竞速 look): every pixel is mapped back to bar coordinates.</summary>
        private void SlantBox(double u0, double u1, double w0, double w1, int rgb, double a, double radius, double glow, double glowA)
        {
            double k = cfg.SlantK, wm = (w0 + w1) / 2, hw = (w1 - w0) / 2, um = (u0 + u1) / 2, hu = (u1 - u0) / 2;
            double r = Math.Min(radius, Math.Min(hu, hw));
            double ext = glow + 1 + k * hw;
            double xa, ya, xb, yb;
            Map(u0 - ext, w0 - glow - 1, out xa, out ya); Map(u1 + ext, w1 + glow + 1, out xb, out yb);
            int i0 = (int)Math.Floor(Math.Min(xa, xb)), i1 = (int)Math.Ceiling(Math.Max(xa, xb)), j0 = (int)Math.Floor(Math.Min(ya, yb)), j1 = (int)Math.Ceiling(Math.Max(ya, yb));
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0; if (i1 > dS - 1) i1 = dS - 1; if (j1 > dS - 1) j1 = dS - 1;
            double in2 = dInner * dInner;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = i + 0.5, py = j + 0.5, ddx = px - dCx, ddy = py - dCy;
                    if (ddx * ddx + ddy * ddy < in2) continue;
                    double u, w; Unmap(px, py, out u, out w);
                    u -= (w - wm) * k; // shear
                    double qx = Math.Abs(u - um) - (hu - r), qy = Math.Abs(w - wm) - (hw - r);
                    double sd;
                    if (qx <= 0 && qy <= 0) sd = Math.Max(qx, qy) - r; // inside: no square root
                    else { double ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0; sd = Math.Sqrt(ox * ox + oy * oy) - r; }
                    if (sd >= 0.5 && glow <= 0) continue;
                    double v = a * C01(0.5 - sd);
                    if (glow > 0 && sd > -0.5) { double gk = C01(1 - Math.Max(sd, 0) / glow); double gv = glowA * a * gk * gk; if (gv > v) v = gv; }
                    if (v > 0) Plot(dBuf, dS, i, j, rgb, v);
                }
        }

        private void Disc(double x, double y, double r, int rgb, double a)
        {
            if (!(a > 0)) return;
            int i0 = (int)Math.Floor(x - r - 1), i1 = (int)Math.Ceiling(x + r + 1), j0 = (int)Math.Floor(y - r - 1), j1 = (int)Math.Ceiling(y + r + 1);
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0; if (i1 > dS - 1) i1 = dS - 1; if (j1 > dS - 1) j1 = dS - 1;
            double in2 = dInner * dInner;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double dx = i + 0.5 - x, dy = j + 0.5 - y, cx = i + 0.5 - dCx, cy = j + 0.5 - dCy;
                    if (cx * cx + cy * cy < in2) continue;
                    double v = C01(r - Math.Sqrt(dx * dx + dy * dy) + 0.5);
                    if (v > 0) Plot(dBuf, dS, i, j, rgb, a * v);
                }
        }

        /// <summary>A 270° ring that turns (the 转圈图标 marker).</summary>
        private void Spinner(double x, double y, double r, double w, double ang, int rgb, double a)
        {
            int i0 = (int)Math.Floor(x - r - w - 1), i1 = (int)Math.Ceiling(x + r + w + 1), j0 = (int)Math.Floor(y - r - w - 1), j1 = (int)Math.Ceiling(y + r + w + 1);
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0; if (i1 > dS - 1) i1 = dS - 1; if (j1 > dS - 1) j1 = dS - 1;
            double in2 = dInner * dInner;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double dx = i + 0.5 - x, dy = j + 0.5 - y, cx = i + 0.5 - dCx, cy = j + 0.5 - dCy;
                    if (cx * cx + cy * cy < in2) continue;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    double cv = C01(w / 2 - Math.Abs(d - r) + 0.5);
                    if (cv <= 0) continue;
                    double t = Math.Atan2(dy, dx) - ang;
                    t = t % (2 * Math.PI); if (t < 0) t += 2 * Math.PI;
                    if (t > 1.5 * Math.PI) continue; // the open quarter
                    Plot(dBuf, dS, i, j, rgb, a * cv * (0.35 + 0.65 * t / (1.5 * Math.PI)));
                }
        }

        /// <summary>One pixel at opacity a (coverage included), under the existing premultiplied pixel.</summary>
        internal static void Plot(int[] buf, int S, int i, int j, int rgb, double a)
        {
            if (i < 0 || j < 0 || i >= S || j >= S) return;
            if (a > 1) a = 1;
            int sa = (int)(a * 255 + 0.5);
            if (sa <= 0) return;
            int idx = j * S + i;
            int d = buf[idx];
            int da = (int)((uint)d >> 24);
            if (da >= 255) return;
            int k = 255 - da;
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            // v5.1.1: divide by 255 with a multiply-shift (the hot path of every bar / ripple pixel)
            int na = da + Div255(sa * k);
            int nr = ((d >> 16) & 0xFF) + Div255(Div255(r * sa) * k);
            int ng = ((d >> 8) & 0xFF) + Div255(Div255(g * sa) * k);
            int nb = (d & 0xFF) + Div255(Div255(b * sa) * k);
            if (na > 255) na = 255;
            if (nr > na) nr = na; if (ng > na) ng = na; if (nb > na) nb = na;
            buf[idx] = (na << 24) | (nr << 16) | (ng << 8) | nb;
        }

        /// <summary>x / 255 rounded, for 0 &lt;= x &lt;= 65025.</summary>
        private static int Div255(int x) { x += 128; return (x + (x >> 8)) >> 8; }

        /// <summary>Anti-aliased rectangle (exact area coverage) drawn under the existing premultiplied pixels.</summary>
        internal static void Rect(int[] buf, int S, double x0, double y0, double x1, double y1, int rgb, double a)
        {
            if (!(a > 0) || !(x1 > x0) || !(y1 > y0)) return;
            if (a > 1) a = 1;
            int i0 = (int)Math.Floor(x0), i1 = (int)Math.Ceiling(x1) - 1;
            int j0 = (int)Math.Floor(y0), j1 = (int)Math.Ceiling(y1) - 1;
            if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0;
            if (i1 > S - 1) i1 = S - 1; if (j1 > S - 1) j1 = S - 1;
            for (int j = j0; j <= j1; j++)
            {
                double cyv = Math.Min(y1, j + 1) - Math.Max(y0, j);
                if (cyv <= 0) continue;
                for (int i = i0; i <= i1; i++)
                {
                    double cxv = Math.Min(x1, i + 1) - Math.Max(x0, i);
                    if (cxv <= 0) continue;
                    Plot(buf, S, i, j, rgb, cxv * cyv * a);
                }
            }
        }
    }
}
