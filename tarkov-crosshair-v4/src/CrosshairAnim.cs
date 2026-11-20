// PURE file: only System. Compiles under net48 (C# 5) and net8.0.
// v5.1 (W22, Leo decision 6): CS:GO-style feel. Applied to the RingStyle of THIS frame from the engine state of THIS frame
// (no queue, no extra frame: zero added latency). The 中心留空 is never made smaller (the centre stays empty) and nothing is
// drawn on 开镜 (the overlay's level-0 path hides the whole canvas before this matters).
using System;

namespace TarkovCrosshair
{
    /// <summary>The five options (presets fill them; each stays editable).</summary>
    public struct AnimOptions
    {
        public bool DynGap, FirePulse, LeanRotate, StateColour, Shake;
        public bool Any { get { return DynGap || FirePulse || LeanRotate || StateColour || Shake; } }
    }

    /// <summary>Engine state of this frame (filled by the overlay).</summary>
    public struct AnimInput
    {
        /// <summary>0 still .. 1 full speed; NaN = not provided (CrosshairAnim smooths Moving itself).</summary>
        public double MoveLevel;
        public bool Moving, Jumping, Firing;
        /// <summary>+1 per counted round; -1 = not provided (CrosshairAnim counts from Firing, Rpm and Semi).</summary>
        public int ShotSeq;
        public double Rpm;
        public bool Semi;
        /// <summary>-1 (Q) .. 1 (E).</summary>
        public double Lean;
    }

    public sealed class CrosshairAnim
    {
        public const string PresetStatic = "static", PresetCs = "cs", PresetClassic = "classic";
        public static readonly string[] Presets = { PresetStatic, PresetCs, PresetClassic };
        public static readonly string[] PresetNames = { "静态", "CS 动态", "经典" };

        /// <summary>Option values of a preset (unknown / null = 静态, every option off = exactly the v5.0.2 look).
        /// V51-RESEARCH 4: 静态 = CS style 4 (never moves); CS 动态 = style 0/3 (opens with movement and with each round);
        /// 经典 = style 5 (legacy: still while moving, opens only on firing). Lean rotation, state colour and shake are extra
        /// options (off in every preset).</summary>
        public static AnimOptions PresetOptions(string preset)
        {
            AnimOptions o = new AnimOptions();
            if (preset == PresetCs) { o.DynGap = true; o.FirePulse = true; }
            else if (preset == PresetClassic) o.FirePulse = true;
            return o;
        }

        /// <summary>The options stored in the settings (GlobalSettings.Anim*).</summary>
        public static AnimOptions From(GlobalSettings g)
        {
            AnimOptions o = new AnimOptions();
            if (g == null) return o;
            o.DynGap = g.AnimDynGap; o.FirePulse = g.AnimFirePulse; o.LeanRotate = g.AnimLeanRotate;
            o.StateColour = g.AnimStateColour; o.Shake = g.AnimShake;
            return o;
        }

        /// <summary>Sets AnimPreset and the five options to the preset's values (the UI calls this when a preset is picked;
        /// each option stays editable afterwards).</summary>
        public static void ApplyPreset(string preset, GlobalSettings g)
        {
            if (g == null) return;
            if (Array.IndexOf(Presets, preset) < 0) preset = PresetStatic;
            AnimOptions o = PresetOptions(preset);
            g.AnimPreset = preset;
            g.AnimDynGap = o.DynGap; g.AnimFirePulse = o.FirePulse; g.AnimLeanRotate = o.LeanRotate;
            g.AnimStateColour = o.StateColour; g.AnimShake = o.Shake;
        }

        /// <summary>The preset an option set equals, or null (自定义).</summary>
        public static string PresetOf(AnimOptions o)
        {
            foreach (string p in Presets)
            {
                AnimOptions q = PresetOptions(p);
                if (q.DynGap == o.DynGap && q.FirePulse == o.FirePulse && q.LeanRotate == o.LeanRotate && q.StateColour == o.StateColour && q.Shake == o.Shake) return p;
            }
            return null;
        }

        // ---- amounts (px are canvas px; scaled with the look's size) ----
        /// <summary>Opening at full movement: this share of the look's reach, kept within MinOpen..MaxOpen px.</summary>
        public const double MoveShare = 0.45, MinOpen = 2.0, MaxOpen = 14.0;
        /// <summary>Jumping opens a further half of the movement opening.</summary>
        public const double JumpExtra = 0.5;
        /// <summary>One round opens this share of the movement opening; pulses add up to PulseCap x it; decay time constant.</summary>
        public const double PulseShare = 0.45, PulseCap = 2.0, PulseTauMs = 60; // ~95 % recovered after 180 ms (research: 150-250)
        /// <summary>Lean rotation at full lean, degrees (positive = E = clockwise).</summary>
        public const double LeanDeg = 8.0;
        /// <summary>State colour: towards white by this much while moving / jumping, plus this per pulse share.</summary>
        public const double MoveWhite = 0.30, JumpWhite = 0.45, PulseWhite = 0.25;
        /// <summary>Shake while the pulse is up: at most this many px.</summary>
        public const double ShakePx = 1.2;
        /// <summary>The opening is drawn in steps of this many px (fewer distinct images while it moves).</summary>
        public const double GapStep = 0.25;
        /// <summary>Fallback smoothing of Moving when MoveLevel is not provided (ms).</summary>
        public const double AttackMs = 120, ReleaseMs = 200;

        private double move;      // fallback smoothed movement 0..1
        private double pulse;     // 0..PulseCap (in units of one round)
        private int seenShot = int.MinValue;
        private bool prevFire;
        private double fireAcc;   // own round counter (ms) when ShotSeq is not provided
        private uint rng = 0x9E3779B9u;

        /// <summary>Opening this frame (px, stepped), rotation (deg), whiteness 0..1, shake (px).</summary>
        public double Open, Rotation, White, ShakeX, ShakeY;
        /// <summary>Changes whenever the drawn look changes because of the animation.</summary>
        public int Key;

        public void Reset() { move = 0; pulse = 0; seenShot = int.MinValue; prevFire = false; fireAcc = 0; Open = Rotation = White = ShakeX = ShakeY = 0; Key = 0; }

        /// <summary>Movement opening at full movement for a look with this reach (px).</summary>
        public static double MoveOpen(double reach)
        {
            double v = (reach > 0 && !double.IsInfinity(reach) ? reach : 0) * MoveShare;
            return v < MinOpen ? MinOpen : (v > MaxOpen ? MaxOpen : v);
        }

        /// <summary>The largest opening these options can ever give (px) -- the overlay sizes the canvas with it, so the window
        /// never changes size while the animation runs.</summary>
        public static double MaxOpenOf(AnimOptions o, double reach)
        {
            double m = MoveOpen(reach), r = 0;
            if (o.DynGap) r += m * (1 + JumpExtra);
            if (o.FirePulse) r += m * PulseShare * PulseCap;
            if (o.Shake && o.FirePulse) r += ShakePx;
            return r;
        }

        /// <summary>Advances by dt seconds with this frame's input.</summary>
        public void Step(AnimOptions o, AnimInput a, double dt, double reach)
        {
            if (!(dt >= 0) || dt > 0.25) dt = dt > 0.25 ? 0.25 : 0;
            double ms = dt * 1000.0;
            double lvl = a.MoveLevel;
            if (double.IsNaN(lvl))
            {
                double tau = a.Moving ? AttackMs : ReleaseMs;
                double tgt = a.Moving ? 1.0 : 0.0;
                move += (tgt - move) * (1.0 - Math.Exp(-ms / tau));
                lvl = move;
            }
            if (lvl < 0) lvl = 0; if (lvl > 1) lvl = 1;

            // rounds this frame
            int shots = 0;
            if (a.ShotSeq >= 0)
            {
                if (seenShot != int.MinValue && a.ShotSeq != seenShot) shots = Math.Max(0, Math.Min(10, a.ShotSeq - seenShot));
                seenShot = a.ShotSeq;
            }
            else
            {
                if (a.Semi) { if (a.Firing && !prevFire) shots = 1; }
                else if (a.Firing)
                {
                    double per = a.Rpm > 0 ? 60000.0 / a.Rpm : 100;
                    if (!prevFire) { shots = 1; fireAcc = 0; }
                    else { fireAcc += ms; while (fireAcc >= per && shots < 10) { fireAcc -= per; shots++; } }
                }
            }
            prevFire = a.Firing;
            pulse *= Math.Exp(-ms / PulseTauMs);
            if (o.FirePulse && shots > 0) pulse = Math.Min(PulseCap, pulse + shots);
            if (!o.FirePulse) pulse = 0;
            if (pulse < 0.01) pulse = 0;

            double m = MoveOpen(reach);
            double open = 0;
            if (o.DynGap) open += m * lvl + (a.Jumping ? m * JumpExtra : 0);
            if (o.FirePulse) open += m * PulseShare * pulse;
            Open = Math.Round(open / GapStep) * GapStep;

            Rotation = o.LeanRotate ? Math.Round(Clamp(a.Lean, -1, 1) * LeanDeg * 4) / 4 : 0;

            double w = 0;
            if (o.StateColour)
            {
                w = Math.Max(lvl * MoveWhite, a.Jumping ? JumpWhite : 0) + pulse / PulseCap * PulseWhite;
                if (w > 0.6) w = 0.6;
                w = Math.Round(w * 32) / 32;
            }
            White = w;

            if (o.Shake && pulse > 0)
            {
                double amp = ShakePx * Math.Min(1, pulse);
                ShakeX = Math.Round((Rand() * 2 - 1) * amp * 8) / 8;
                ShakeY = Math.Round((Rand() * 2 - 1) * amp * 8) / 8;
            }
            else ShakeX = ShakeY = 0;

            Key = ((int)Math.Round(Open / GapStep) & 0xFFF) | (((int)Math.Round(Rotation * 4) & 0xFF) << 12) | (((int)Math.Round(White * 32) & 0x3F) << 20);
        }

        private double Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216.0;
        }

        private static double Clamp(double v, double lo, double hi) { return double.IsNaN(v) ? 0 : (v < lo ? lo : (v > hi ? hi : v)); }

        /// <summary>
        /// Applies an opening of 'open' px, a rotation and a whiteness to s (after RingStyle.Set). The 中心留空 is not changed
        /// (only moved away from: everything opens outward), so the centre stays empty.
        /// </summary>
        public static void Apply(RingStyle s, double open, double rotation, double white)
        {
            if (open > 0)
            {
                string kind = s.Kind ?? "";
                if (kind.Length > 0 && kind != "custom") s.SGap += open;
                else if (kind == "custom") s.Diameter += 2 * open;
                else
                {
                    if (s.Shape == "cross") s.CrossGap += open;
                    else if (s.Shape == "ringcross") { s.Diameter += 2 * open; s.CrossGap += open; }
                    else if (s.Shape != "dot") s.Diameter += 2 * open; // ring / square (a dot keeps its size)
                }
            }
            if (rotation != 0) s.RotationDeg += rotation;
            if (white > 0)
            {
                s.R1 = Mix(s.R1, white); s.G1 = Mix(s.G1, white); s.B1 = Mix(s.B1, white);
                s.R2 = Mix(s.R2, white); s.G2 = Mix(s.G2, white); s.B2 = Mix(s.B2, white);
            }
        }

        private static int Mix(int c, double w) { return (int)Math.Round(c + (255 - c) * w, MidpointRounding.AwayFromZero); }
    }
}


namespace TarkovCrosshair
{
    /// <summary>
    /// v5.1 (W22): the per-frame extras of the overlay look -- the CS feel (CrosshairAnim) and the ammo hint (AmmoDisplay) --
    /// and the canvas they need. Render thread only; allocation-free except when the base look or the options change.
    /// With every option off and the ammo hint off the canvas is exactly RingRaster.CanvasSize(look) (the v5.0.2 window).
    /// </summary>
    public sealed class LookExtras
    {
        public readonly CrosshairAnim Anim = new CrosshairAnim();
        public readonly AmmoDisplay Ammo = new AmmoDisplay();
        private readonly RasterChangeDetector baseLook = new RasterChangeDetector(false);
        private AnimOptions opts;
        private int ammoStyle = -1, parsedStyle;
        private string styleText;
        private bool ammoOn, has;
        private int extraCanvas;   // canvas the largest opening / the ammo hint need (0 = none)
        private double baseReach;  // CoreReach of the look without animation (scales the opening)

        /// <summary>Canvas side this frame (even, MinCanvas..MaxCanvas).</summary>
        public int Canvas;
        /// <summary>Inner distance of the ammo hint from the centre, px (outside the largest opened look).</summary>
        public double Inner;
        public int AmmoStyle { get { return ammoStyle < 0 ? AmmoDisplay.DefaultStyle : ammoStyle; } }
        /// <summary>Times the extra geometry was recomputed (tests: not per frame).</summary>
        public int Recomputes;

        public void Reset() { has = false; baseLook.Reset(); Anim.Reset(); Ammo.Reset(); }

        /// <summary>
        /// s = this frame's look straight from RingStyle.Set; it is changed in place by the animation. ammoFeatureOn =
        /// GlobalSettings.AmmoOn (sizes the canvas even while another weapon is in the hands, so a weapon switch never
        /// resizes the window). dt in seconds, tMs any monotonic ms clock.
        /// </summary>
        public void Frame(RingStyle s, AnimOptions o, AnimInput ai, bool ammoFeatureOn, AmmoView av, string ammoStyleText, double dt, double tMs)
        {
            // the settings string is parsed only when it changes (a new RenderConfig), never per frame (no allocation)
            if (!ReferenceEquals(ammoStyleText, styleText) || ammoStyle < 0) { styleText = ammoStyleText; parsedStyle = AmmoDisplay.ParseStyle(ammoStyleText); }
            int st = parsedStyle;
            bool lookChanged = baseLook.Changed(s, 0, 0, 0);
            if (!has || lookChanged || !Same(o, opts) || st != ammoStyle || ammoFeatureOn != ammoOn)
            {
                has = true; opts = o; ammoStyle = st; ammoOn = ammoFeatureOn;
                Recompute(s);
            }
            if (o.Any)
            {
                Anim.Step(o, ai, dt, baseReach);
                CrosshairAnim.Apply(s, Anim.Open, Anim.Rotation, Anim.White);
            }
            else { Anim.Open = Anim.Rotation = Anim.White = Anim.ShakeX = Anim.ShakeY = 0; Anim.Key = 0; }
            if (ammoFeatureOn) Ammo.Update(av, tMs, st);
            else { AmmoView off = new AmmoView(); Ammo.Update(off, tMs, st); }
            int c = RingRaster.CanvasSize(s);
            Canvas = extraCanvas > c ? extraCanvas : c;
        }

        private static bool Same(AnimOptions a, AnimOptions b)
        {
            return a.DynGap == b.DynGap && a.FirePulse == b.FirePulse && a.LeanRotate == b.LeanRotate && a.StateColour == b.StateColour && a.Shake == b.Shake;
        }

        private void Recompute(RingStyle s)
        {
            Recomputes++;
            baseReach = RingRaster.CoreReach(s);
            if (double.IsNaN(baseReach) || double.IsInfinity(baseReach) || baseReach < 0) baseReach = 0;
            extraCanvas = 0;
            Inner = 0;
            if (!opts.Any && !ammoOn) return;
            RingStyle m = s.Copy();
            double maxOpen = opts.Any ? CrosshairAnim.MaxOpenOf(opts, baseReach) : 0;
            CrosshairAnim.Apply(m, maxOpen, 0, 0);
            int c = RingRaster.CanvasSize(m);
            if (ammoOn)
            {
                double reach = RingRaster.CoreReach(m) + RingStyle.VisibleExtra(m);
                Inner = AmmoDisplay.Inner(reach);
                int a = AmmoDisplay.CanvasNeed(Inner, ammoStyle);
                if (a > c) c = a;
            }
            extraCanvas = c > RingRaster.MaxCanvas ? RingRaster.MaxCanvas : c;
        }
    }
}
