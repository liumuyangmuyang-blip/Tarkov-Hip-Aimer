// PURE file: only System (+ the plain settings classes of LookFxSettings.cs). Compiles under net48 (C# 5) and net8.0.
// v5.1 (W22, Leo decision 6): the crosshair's feel. Applied to the RingStyle of THIS frame from the engine state of THIS frame
// (no queue, no extra frame: zero added latency). The 中心留空 is never made smaller (the centre stays empty) and nothing is
// drawn on 开镜 / 丢雷 (the overlay's level-0 path makes the whole canvas transparent).
// v5.1.1 (X3, Leo round B2): plain preset names, no 侧身倾斜 / 抖动 any more; every amount is adjustable (移动 / 开火张开幅度,
// 张开速度, 恢复时间, 强度) and two firing effects are added: 开火波纹 (a ring that runs outward from the crosshair on every round)
// and 开火渐变色 (a colour gradient that spreads from the centre outward through the crosshair while firing).
using System;

namespace TarkovCrosshair
{
    /// <summary>The options (presets fill the switches; every value stays editable). Tuned = false: the amounts are the
    /// built-in defaults (a plain new AnimOptions() with a switch set behaves like v5.1).</summary>
    public struct AnimOptions
    {
        public bool DynGap, FirePulse, StateColour, Ripple, Gradient;
        public bool Tuned;
        /// <summary>Factors (1 = 100 %): 移动张开, 开火张开, 强度 (fire effects).</summary>
        public double MoveGap, FireGap, Intensity;
        public double FireOpenMs, FireRecoverMs;
        public int RippleRgb, GradFrom, GradTo;
        public double RippleSpeed, RippleWidth, RippleRange, RippleFadeMs, GradSpreadMs, GradFadeMs;

        public bool Any { get { return DynGap || FirePulse || StateColour || Ripple || Gradient; } }
        /// <summary>A firing effect drawn into the canvas (波纹 / 渐变色).</summary>
        public bool Fx { get { return Ripple || Gradient; } }

        public double MoveGapF { get { return Tuned ? MoveGap : 1; } }
        public double FireGapF { get { return Tuned ? FireGap : 1; } }
        public double IntensityF { get { return Tuned ? Intensity : 1; } }
        public double OpenMsF { get { return Tuned ? FireOpenMs : 0; } }
        public double RecoverMsF { get { return Tuned ? FireRecoverMs : CrosshairAnim.PulseTauMs * 3; } }
        public int RippleRgbF { get { return Tuned ? RippleRgb : 0x5CF2FF; } }
        public double RippleSpeedF { get { return Tuned ? RippleSpeed : 420; } }
        public double RippleWidthF { get { return Tuned ? RippleWidth : 1.5; } }
        public double RippleRangeF { get { return Tuned ? RippleRange : 22; } }
        public double RippleFadeF { get { return Tuned ? RippleFadeMs : 320; } }
        public int GradFromF { get { return Tuned ? GradFrom : 0xFFFFFF; } }
        public int GradToF { get { return Tuned ? GradTo : 0x5CF2FF; } }
        public double GradSpreadF { get { return Tuned ? GradSpreadMs : 90; } }
        public double GradFadeF { get { return Tuned ? GradFadeMs : 260; } }

        public bool Same(AnimOptions b)
        {
            return DynGap == b.DynGap && FirePulse == b.FirePulse && StateColour == b.StateColour && Ripple == b.Ripple && Gradient == b.Gradient
                && Tuned == b.Tuned && MoveGap == b.MoveGap && FireGap == b.FireGap && Intensity == b.Intensity && FireOpenMs == b.FireOpenMs
                && FireRecoverMs == b.FireRecoverMs && RippleRgb == b.RippleRgb && GradFrom == b.GradFrom && GradTo == b.GradTo
                && RippleSpeed == b.RippleSpeed && RippleWidth == b.RippleWidth && RippleRange == b.RippleRange && RippleFadeMs == b.RippleFadeMs
                && GradSpreadMs == b.GradSpreadMs && GradFadeMs == b.GradFadeMs;
        }
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
        /// <summary>-1 (Q) .. 1 (E). v5.1.1: not used any more (侧身倾斜 removed).</summary>
        public double Lean;
    }

    public sealed class CrosshairAnim
    {
        public const string PresetStatic = "static", PresetCs = "cs", PresetClassic = "classic", PresetFx = "fx", PresetRace = "race";
        public static readonly string[] Presets = { PresetStatic, PresetCs, PresetClassic, PresetFx, PresetRace };
        /// <summary>v5.1.1 (Leo B2): plain names (the id "cs" stays only so older files keep their choice).</summary>
        public static readonly string[] PresetNames = { "静态", "跟随动作", "开火跳动", "开火特效", "竞速" };

        /// <summary>Switches of a preset (unknown / null = 静态, everything off = exactly the v5.0.2 look).</summary>
        public static AnimOptions PresetOptions(string preset)
        {
            AnimOptions o = new AnimOptions();
            if (preset == PresetCs) { o.DynGap = true; o.FirePulse = true; }
            else if (preset == PresetClassic) o.FirePulse = true;
            else if (preset == PresetFx) { o.FirePulse = true; o.Ripple = true; o.Gradient = true; }
            else if (preset == PresetRace) { o.FirePulse = true; o.Ripple = true; }
            return o;
        }

        /// <summary>The options stored in the settings (parses the colours: the overlay uses LookExtras.AnimFrom, cached).</summary>
        public static AnimOptions From(GlobalSettings g)
        {
            AnimOptions o = new AnimOptions();
            if (g == null) return o;
            o.DynGap = g.AnimDynGap; o.FirePulse = g.AnimFirePulse; o.StateColour = g.AnimStateColour;
            AnimFxSettings f = g.AnimFx;
            if (f == null) return o;
            o.Ripple = f.Ripple; o.Gradient = f.Grad;
            o.Tuned = true;
            o.MoveGap = f.MoveGapPct / 100.0; o.FireGap = f.FireGapPct / 100.0; o.Intensity = f.IntensityPct / 100.0;
            o.FireOpenMs = f.FireOpenMs; o.FireRecoverMs = f.FireRecoverMs;
            o.RippleRgb = FxClamp.Rgb(f.RippleColour, 0x5CF2FF); o.RippleSpeed = f.RippleSpeed; o.RippleWidth = f.RippleWidth;
            o.RippleRange = f.RippleRange; o.RippleFadeMs = f.RippleFadeMs;
            o.GradFrom = FxClamp.Rgb(f.GradFrom, 0xFFFFFF); o.GradTo = FxClamp.Rgb(f.GradTo, 0x5CF2FF);
            o.GradSpreadMs = f.GradSpreadMs; o.GradFadeMs = f.GradFadeMs;
            return o;
        }

        /// <summary>Sets AnimPreset and the switches to the preset's values (amounts are left as the user set them).</summary>
        public static void ApplyPreset(string preset, GlobalSettings g)
        {
            if (g == null) return;
            if (Array.IndexOf(Presets, preset) < 0) preset = PresetStatic;
            AnimOptions o = PresetOptions(preset);
            g.AnimPreset = preset;
            g.AnimDynGap = o.DynGap; g.AnimFirePulse = o.FirePulse; g.AnimStateColour = o.StateColour;
            g.AnimLeanRotate = false; g.AnimShake = false;
            if (g.AnimFx == null) g.AnimFx = AnimFxSettings.Defaults();
            if (preset == PresetRace) AnimFxSettings.ApplyRace(g.AnimFx); // 竞速 also sets its sharp amounts (still editable)
            g.AnimFx.Ripple = o.Ripple; g.AnimFx.Grad = o.Gradient;
        }

        /// <summary>The preset whose switches these are, or null (自定义).</summary>
        public static string PresetOf(AnimOptions o)
        {
            foreach (string p in Presets)
            {
                AnimOptions q = PresetOptions(p);
                if (q.DynGap == o.DynGap && q.FirePulse == o.FirePulse && q.StateColour == o.StateColour && q.Ripple == o.Ripple && q.Gradient == o.Gradient) return p;
            }
            return null;
        }

        // ---- amounts (px are canvas px; scaled with the look's size) ----
        public const double MoveShare = 0.45, MinOpen = 2.0, MaxOpen = 14.0;
        public const double JumpExtra = 0.5;
        /// <summary>One round opens this share of the movement opening; pulses add up to PulseCap x it; default decay.</summary>
        public const double PulseShare = 0.45, PulseCap = 2.0, PulseTauMs = 60;
        public const double MoveWhite = 0.30, JumpWhite = 0.45, PulseWhite = 0.25;
        public const double GapStep = 0.25;
        public const double AttackMs = 120, ReleaseMs = 200;

        private double move, pulse, target;
        private int seenShot = int.MinValue;
        private bool prevFire;
        private double fireAcc;

        /// <summary>Opening this frame (px, stepped), whiteness 0..1. Rotation / ShakeX / ShakeY stay 0 (removed in v5.1.1).</summary>
        public double Open, Rotation, White, ShakeX, ShakeY;
        /// <summary>Rounds counted in the last Step (the firing effects start from them, in the same frame).</summary>
        public int LastShots;
        public int Key;

        public void Reset() { move = 0; pulse = 0; target = 0; seenShot = int.MinValue; prevFire = false; fireAcc = 0; Open = Rotation = White = ShakeX = ShakeY = 0; Key = 0; LastShots = 0; }

        public static double MoveOpen(double reach)
        {
            double v = (reach > 0 && !double.IsInfinity(reach) ? reach : 0) * MoveShare;
            return v < MinOpen ? MinOpen : (v > MaxOpen ? MaxOpen : v);
        }

        /// <summary>The largest opening these options can ever give (px): the canvas is sized with it once.</summary>
        public static double MaxOpenOf(AnimOptions o, double reach)
        {
            double m = MoveOpen(reach), r = 0;
            if (o.DynGap) r += m * (1 + JumpExtra) * o.MoveGapF;
            if (o.FirePulse) r += m * PulseShare * PulseCap * o.FireGapF * o.IntensityF;
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
                move += ((a.Moving ? 1.0 : 0.0) - move) * (1.0 - Math.Exp(-ms / tau));
                lvl = move;
            }
            if (lvl < 0) lvl = 0; if (lvl > 1) lvl = 1;

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
            LastShots = shots;

            // 开火张开: target jumps up on a round and decays over 恢复时间; the opening follows it within 张开速度
            double rec = Math.Max(30, o.RecoverMsF);
            target *= Math.Exp(-ms / (rec / 3.0));
            if (o.FirePulse && shots > 0) target = Math.Min(PulseCap, target + shots);
            if (!o.FirePulse) target = 0;
            if (target < 0.01) target = 0;
            double att = o.OpenMsF;
            if (att <= 0 || target <= pulse) pulse = target;
            else pulse += (target - pulse) * (1.0 - Math.Exp(-ms / (att / 3.0)));

            double m = MoveOpen(reach);
            double open = 0;
            if (o.DynGap) open += (m * lvl + (a.Jumping ? m * JumpExtra : 0)) * o.MoveGapF;
            if (o.FirePulse) open += m * PulseShare * pulse * o.FireGapF * o.IntensityF;
            Open = Math.Round(open / GapStep) * GapStep;
            Rotation = 0; ShakeX = ShakeY = 0;

            double w = 0;
            if (o.StateColour)
            {
                w = Math.Max(lvl * MoveWhite, a.Jumping ? JumpWhite : 0) + pulse / PulseCap * PulseWhite;
                if (w > 0.6) w = 0.6;
                w = Math.Round(w * 32) / 32;
            }
            White = w;
            Key = ((int)Math.Round(Open / GapStep) & 0xFFF) | (((int)Math.Round(White * 32) & 0x3F) << 20);
        }

        /// <summary>Applies an opening of 'open' px, a rotation and a whiteness to s (everything opens outward, so the centre
        /// stays empty).</summary>
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
                    else if (s.Shape != "dot") s.Diameter += 2 * open;
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

    /// <summary>
    /// v5.1.1 (X3, Leo B2): the two firing effects, started by this frame's rounds (no added latency).
    /// 开火波纹: on every round a thin ring starts just outside the crosshair and runs outward (速度, 宽度, 距离, 颜色, 褪色时间),
    /// drawn UNDER the crosshair. 开火渐变色: the crosshair's own pixels take a colour gradient (中心颜色 -> 外圈颜色) that spreads
    /// from the centre outward (扩散时间) and fades after the last round (褪色时间). No pixel is ever added inside the look, so
    /// the centre stays empty. Allocation-free.
    /// </summary>
    public sealed class FireFx
    {
        public const int MaxRipples = 8;
        private readonly double[] age = new double[MaxRipples];
        private readonly bool[] on = new bool[MaxRipples];
        private int next;
        private bool grad;
        private double sinceShot, sinceBurst, sinceSpawn = 1e9;
        public const double RippleMinGapMs = 60;
        private AnimOptions o;

        /// <summary>Gradient front 0..1 (share of the look's reach) and strength 0..1 this frame.</summary>
        public double GradFront, GradStrength;
        public int Ripples;
        public int Key;
        public bool Active { get { return Ripples > 0 || GradStrength > 0; } }

        public void Reset() { for (int i = 0; i < MaxRipples; i++) on[i] = false; grad = false; sinceSpawn = 1e9; GradFront = GradStrength = 0; Ripples = 0; Key = 0; }

        public void Step(AnimOptions opt, int shots, double ms)
        {
            o = opt;
            if (!(ms >= 0) || ms > 250) ms = ms > 250 ? 250 : 0;
            Ripples = 0;
            int h = 0;
            double fade = Math.Max(30, o.RippleFadeF), range = o.RippleRangeF, speed = o.RippleSpeedF;
            for (int i = 0; i < MaxRipples; i++)
            {
                if (!on[i]) continue;
                age[i] += ms;
                if (age[i] >= fade || speed * age[i] / 1000.0 >= range) { on[i] = false; continue; }
                Ripples++;
                h = h * 31 + (int)(age[i] / 4) + 1;
            }
            sinceSpawn += ms;
            // a new ring in the same frame as the round; at very high fire rates rings start at least RippleMinGapMs apart
            // (they would overlap into one thick band anyway, and it bounds the work per frame)
            if (o.Ripple && shots > 0 && sinceSpawn >= RippleMinGapMs) { on[next] = true; age[next] = 0; next = (next + 1) % MaxRipples; Ripples++; h = h * 31 + 7; sinceSpawn = 0; }
            if (!o.Ripple) { for (int i = 0; i < MaxRipples; i++) on[i] = false; Ripples = 0; h = 0; }

            if (o.Gradient && shots > 0)
            {
                if (!grad) { grad = true; sinceBurst = 0; }
                sinceShot = 0;
            }
            else if (grad) { sinceShot += ms; sinceBurst += ms; }
            GradFront = GradStrength = 0;
            if (grad && o.Gradient)
            {
                double s = 1 - sinceShot / Math.Max(30, o.GradFadeF);
                if (s <= 0) grad = false;
                else
                {
                    GradStrength = Math.Round(Math.Min(1, s * Math.Min(1, o.IntensityF)) * 32) / 32;
                    GradFront = Math.Round(Math.Min(1, (sinceBurst + 16) / Math.Max(10, o.GradSpreadF)) * 32) / 32;
                }
            }
            else grad = false;
            Key = Active ? (h * 397 + (int)(GradFront * 32) * 41 + (int)(GradStrength * 32)) | 1 : 0;
        }

        /// <summary>Half the canvas side the ripples need for a look reaching 'reach' px.</summary>
        public static double HalfExtent(AnimOptions o, double reach)
        {
            if (!o.Ripple) return 0;
            return reach + 1.5 + o.RippleRangeF + o.RippleWidthF + 3;
        }

        /// <summary>Recolours the crosshair (渐变色), then draws the ripples under it. reach = the look's visible reach now.</summary>
        public void Draw(int[] buf, int S, double cx, double cy, double reach, byte level)
        {
            if (level == 0 || !Active) return;
            if (GradStrength > 0 && GradFront > 0) Tint(buf, S, cx, cy, reach);
            if (Ripples > 0)
            {
                double fade = Math.Max(30, o.RippleFadeF), range = o.RippleRangeF, speed = o.RippleSpeedF, w = o.RippleWidthF;
                double I = Math.Min(1, 0.9 * o.IntensityF) * level / 255.0;
                for (int i = 0; i < MaxRipples; i++)
                {
                    if (!on[i]) continue;
                    double trav = speed * age[i] / 1000.0;
                    double t = Math.Max(age[i] / fade, trav / range);
                    if (t >= 1) continue;
                    double a = I * Math.Pow(1 - t, 1.5);
                    Ring(buf, S, cx, cy, reach + 1.5 + w / 2 + trav, w, o.RippleRgbF, a);
                }
            }
        }

        private void Tint(int[] buf, int S, double cx, double cy, double reach)
        {
            double R = Math.Max(2, reach + 1);
            double front = GradFront * R, m0 = GradStrength;
            int from = o.GradFromF, to = o.GradToF;
            int i0 = Math.Max(0, (int)Math.Floor(cx - R - 1)), i1 = Math.Min(S - 1, (int)Math.Ceiling(cx + R + 1));
            int j0 = Math.Max(0, (int)Math.Floor(cy - R - 1)), j1 = Math.Min(S - 1, (int)Math.Ceiling(cy + R + 1));
            for (int j = j0; j <= j1; j++)
            {
                int row = j * S;
                for (int i = i0; i <= i1; i++)
                {
                    int d = buf[row + i];
                    int da = (int)((uint)d >> 24);
                    if (da == 0) continue;
                    double dx = i + 0.5 - cx, dy = j + 0.5 - cy, r = Math.Sqrt(dx * dx + dy * dy);
                    double edge = front - r + 1; if (edge <= 0) continue; if (edge > 1) edge = 1;
                    double m = m0 * edge;
                    double t = r / R; if (t > 1) t = 1;
                    int tr = (int)(((from >> 16) & 0xFF) + ((((to >> 16) & 0xFF) - ((from >> 16) & 0xFF)) * t));
                    int tg = (int)(((from >> 8) & 0xFF) + ((((to >> 8) & 0xFF) - ((from >> 8) & 0xFF)) * t));
                    int tb = (int)((from & 0xFF) + (((to & 0xFF) - (from & 0xFF)) * t));
                    int pr = (d >> 16) & 0xFF, pg = (d >> 8) & 0xFF, pb = d & 0xFF;
                    pr = (int)Math.Round(pr + (tr * da / 255.0 - pr) * m);
                    pg = (int)Math.Round(pg + (tg * da / 255.0 - pg) * m);
                    pb = (int)Math.Round(pb + (tb * da / 255.0 - pb) * m);
                    if (pr > da) pr = da; if (pg > da) pg = da; if (pb > da) pb = da;
                    if (pr < 0) pr = 0; if (pg < 0) pg = 0; if (pb < 0) pb = 0;
                    buf[row + i] = (da << 24) | (pr << 16) | (pg << 8) | pb;
                }
            }
        }

        /// <summary>Anti-aliased ring of radius rm and width w, under the existing pixels; only the rows' ring spans are visited.</summary>
        private static void Ring(int[] buf, int S, double cx, double cy, double rm, double w, int rgb, double a)
        {
            if (!(a > 0)) return;
            double ro = rm + w / 2 + 1, ri = Math.Max(0, rm - w / 2 - 1);
            int j0 = Math.Max(0, (int)Math.Floor(cy - ro)), j1 = Math.Min(S - 1, (int)Math.Ceiling(cy + ro));
            for (int j = j0; j <= j1; j++)
            {
                double dy = j + 0.5 - cy;
                double xo2 = ro * ro - dy * dy; if (xo2 <= 0) continue;
                double xo = Math.Sqrt(xo2), xi = ri * ri - dy * dy > 0 ? Math.Sqrt(ri * ri - dy * dy) : 0;
                for (int side = -1; side <= 1; side += 2)
                {
                    int a0, a1;
                    if (side < 0) { a0 = (int)Math.Floor(cx - xo); a1 = (int)Math.Ceiling(cx - xi); }
                    else { a0 = (int)Math.Floor(cx + xi); a1 = (int)Math.Ceiling(cx + xo); }
                    if (xi == 0 && side > 0) a0 = (int)Math.Ceiling(cx - xi) + 1; // one span when the row misses the hole
                    if (a0 < 0) a0 = 0; if (a1 > S - 1) a1 = S - 1;
                    double ro2 = ro * ro, ri2 = ri * ri, dy2 = dy * dy;
                    for (int i = a0; i <= a1; i++)
                    {
                        double dx = i + 0.5 - cx, r2 = dx * dx + dy2;
                        if (r2 > ro2 || r2 < ri2) continue;
                        double r = Math.Sqrt(r2);
                        double c = w / 2 - Math.Abs(r - rm) + 0.5; if (c <= 0) continue; if (c > 1) c = 1;
                        AmmoDisplay.Plot(buf, S, i, j, rgb, a * c);
                    }
                }
            }
        }
    }
}


namespace TarkovCrosshair
{
    /// <summary>
    /// v5.1 (W22): the per-frame extras of the overlay look -- the feel (CrosshairAnim + FireFx) and the ammo bar (AmmoDisplay)
    /// -- and the canvas they need. Render thread only; allocation-free except when the base look or the options change.
    /// With every option off and the ammo hint off the canvas is exactly RingRaster.CanvasSize(look) (the v5.0.2 window).
    /// </summary>
    public sealed class LookExtras
    {
        public readonly CrosshairAnim Anim = new CrosshairAnim();
        public readonly AmmoDisplay Ammo = new AmmoDisplay();
        public readonly FireFx Fx = new FireFx();
        private readonly RasterChangeDetector baseLook = new RasterChangeDetector(false);
        private AnimOptions opts;
        private int ammoStyle = -1, parsedStyle;
        private string styleText;
        private bool ammoOn, has;
        private AmmoBarCfg bar;
        private int extraCanvas;
        private double baseReach;

        public int Canvas;
        /// <summary>Inner distance of the ammo bar from the centre, px (outside the largest opened look).</summary>
        public double Inner;
        /// <summary>The look's visible reach this frame (the ripples start just outside it).</summary>
        public double Reach;
        public int AmmoStyle { get { return ammoStyle < 0 ? AmmoDisplay.DefaultStyle : ammoStyle; } }
        public int Recomputes;
        /// <summary>A firing effect is drawn this frame (the overlay composes through the scratch buffer then).</summary>
        public bool FxActive { get { return opts.Fx && Fx.Active; } }

        public void Reset() { has = false; baseLook.Reset(); Anim.Reset(); Ammo.Reset(); Fx.Reset(); }

        // ---- settings caches (compared every frame without allocation; rebuilt only when a value changes) ----
        private AmmoBarSettings barSrc;
        private AmmoBarCfg barCache;
        private AnimFxSettings fxSrc;
        private bool fxDyn, fxPulse, fxState, fxHas;
        private AnimOptions fxCache;

        /// <summary>The ammo bar look of the settings.</summary>
        public AmmoBarCfg BarFrom(GlobalSettings g)
        {
            AmmoBarSettings s = g == null ? null : g.AmmoBar;
            if (s == null) return AmmoBarCfg.Default;
            if (barCache == null || barSrc == null || !s.Same(barSrc)) { barSrc = s.Clone(); barCache = AmmoBarCfg.From(s); }
            return barCache;
        }

        /// <summary>The 准星动态 options of the settings.</summary>
        public AnimOptions AnimFrom(GlobalSettings g)
        {
            if (g == null) return new AnimOptions();
            AnimFxSettings f = g.AnimFx;
            if (!fxHas || g.AnimDynGap != fxDyn || g.AnimFirePulse != fxPulse || g.AnimStateColour != fxState || (f == null) != (fxSrc == null) || (f != null && !f.Same(fxSrc)))
            {
                fxHas = true; fxDyn = g.AnimDynGap; fxPulse = g.AnimFirePulse; fxState = g.AnimStateColour;
                fxSrc = f == null ? null : f.Clone();
                fxCache = CrosshairAnim.From(g);
            }
            return fxCache;
        }

        /// <summary>v5.1 call shape: the fresh-install bar (a warning only when the style has 闪烁).</summary>
        public void Frame(RingStyle s, AnimOptions o, AnimInput ai, bool ammoFeatureOn, AmmoView av, string ammoStyleText, double dt, double tMs)
        {
            if (!ReferenceEquals(ammoStyleText, styleText) || ammoStyle < 0) { styleText = ammoStyleText; parsedStyle = AmmoDisplay.ParseStyle(ammoStyleText); }
            Frame(s, o, ai, ammoFeatureOn, av, ammoStyleText, (parsedStyle & AmmoDisplay.Flash) != 0 ? AmmoBarCfg.Default : AmmoBarCfg.DefaultNoWarn, dt, tMs);
        }

        /// <summary>s = this frame's look straight from RingStyle.Set; changed in place by the animation. dt in seconds.</summary>
        public void Frame(RingStyle s, AnimOptions o, AnimInput ai, bool ammoFeatureOn, AmmoView av, string ammoStyleText, AmmoBarCfg barCfg, double dt, double tMs)
        {
            if (!ReferenceEquals(ammoStyleText, styleText) || ammoStyle < 0) { styleText = ammoStyleText; parsedStyle = AmmoDisplay.ParseStyle(ammoStyleText); }
            if (barCfg == null) barCfg = AmmoBarCfg.Default;
            int st = parsedStyle;
            bool lookChanged = baseLook.Changed(s, 0, 0, 0);
            if (!has || lookChanged || !o.Same(opts) || st != ammoStyle || ammoFeatureOn != ammoOn || !ReferenceEquals(barCfg, bar))
            {
                has = true; opts = o; ammoStyle = st; ammoOn = ammoFeatureOn; bar = barCfg;
                Recompute(s);
            }
            if (o.Any)
            {
                Anim.Step(o, ai, dt, baseReach);
                CrosshairAnim.Apply(s, Anim.Open, 0, Anim.White);
            }
            else { Anim.Open = Anim.Rotation = Anim.White = Anim.ShakeX = Anim.ShakeY = 0; Anim.Key = 0; Anim.LastShots = 0; }
            if (o.Fx)
            {
                if (!o.Any) Anim.Step(o, ai, dt, baseReach);
                Fx.Step(o, Anim.LastShots, dt * 1000.0);
                if (Fx.Active) Reach = RingRaster.CoreReach(s) + RingStyle.VisibleExtra(s);
            }
            else if (Fx.Ripples > 0 || Fx.GradStrength > 0) Fx.Reset();
            if (ammoFeatureOn) Ammo.Update(av, tMs, st, bar);
            else { AmmoView off = new AmmoView(); Ammo.Update(off, tMs, st, bar); }
            int c = RingRaster.CanvasSize(s);
            Canvas = extraCanvas > c ? extraCanvas : c;
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
            double reach = RingRaster.CoreReach(m) + RingStyle.VisibleExtra(m);
            if (opts.Ripple)
            {
                int rc = (int)Math.Ceiling(2 * FireFx.HalfExtent(opts, reach)) + 2;
                if ((rc & 1) != 0) rc++;
                if (rc > c) c = rc;
            }
            if (ammoOn)
            {
                Inner = AmmoDisplay.Inner(reach);
                int a = AmmoDisplay.CanvasNeed(Inner, ammoStyle, bar);
                if (a > c) c = a;
            }
            extraCanvas = c > RingRaster.MaxCanvas ? RingRaster.MaxCanvas : c;
        }
    }
}
