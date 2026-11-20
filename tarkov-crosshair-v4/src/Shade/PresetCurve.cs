// TarkovShade - fixed-preset display filter module.
// Builds a 256-step RGB gamma ramp from fixed preset values only (no image analysis).
// The ramp conventions (monotonic guard for SetDeviceGammaRamp, 16-bit word rounding,
// 4*v*(1-v) colour shaping, SmoothStep masks, change-strength measure) are adapted from
// TarkovAutoShade src/ToneCurve.cs, Copyright (c) 2026 TarkovAutoShade contributors, MIT License.
// https://github.com/lu1249765686/TarkovAutoShade  - see LICENSE-THIRD-PARTY.txt
using System;

namespace TarkovShade
{
    public sealed class ShadeRamp
    {
        public ushort[] Red = new ushort[256];
        public ushort[] Green = new ushort[256];
        public ushort[] Blue = new ushort[256];
        /// <summary>0..1, how far the ramp is from identity (drives smooth-transition length).</summary>
        public double ChangeStrength;

        public bool IsIdentity
        {
            get
            {
                for (int i = 0; i < 256; i++)
                {
                    ushort id = (ushort)(i * 257);
                    if (Red[i] != id || Green[i] != id || Blue[i] != id) return false;
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Pure, deterministic curve builder. Same preset values -> same ramp, bit for bit.
    /// Apply order (per input level x = i/255):
    ///   BlackLevel -> ShadowLift (ShadowRange) -> Gamma -> Contrast (pivot 0.5) -> Brightness
    ///   -> HighlightProtect (HighlightStart) -> clamp -> Warmth / RGB gain / ShadowTint -> clamp -> monotonic guard.
    /// Every stage is the identity at its neutral value, so a neutral preset gives ramp[i] = i * 257.
    /// </summary>
    internal static class PresetCurve
    {
        // Scale factors from slider units to curve units.
        public const double BlackLevelScale = 0.15;      // +-50 -> floor raised / crushed by up to 7.5 %
        public const double ShadowLiftScale = 0.40;      // 100 -> bump amplitude 0.40 * pivot (kept < 1/2.25: monotonic)
        public const double ContrastScale = 1.0;         // +-50 -> slope 1.5 / 0.5 around 0.5
        public const double BrightnessScale = 0.20;      // +-50 -> +-0.10 * 4v(1-v): black and white stay put
        public const double HighlightScale = 0.60;       // 100 -> white lowered by 30 % of the span above the knee
        public const double WarmthScale = 0.08;          // +-50 -> +-0.04 * 4v(1-v) on R (and the opposite on B)
        public const double GainScale = 0.50;            // +-50 -> channel x 1.25 / 0.75
        public const double ShadowTintScale = 0.28;      // +-50 -> peak +-0.14 * pivot on R (opposite on B); < pivot/6.75 keeps it monotonic

        public static ShadeRamp Build(ShadePreset source)
        {
            if (source == null) throw new ArgumentNullException("source");
            ShadePreset p = source.Clone();
            p.Normalize();

            var ramp = new ShadeRamp();
            double previousRed = 0.0, previousGreen = 0.0, previousBlue = 0.0;
            double totalChange = 0.0;
            double pivot = p.ShadowRange / 100.0;

            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0;
                double v = ToneValue(p, x);

                // Colour stage.
                double colorShape = 4.0 * v * (1.0 - v);
                double warmth = p.Warmth / 100.0 * WarmthScale * colorShape;
                double tint = p.ShadowTint / 100.0 * ShadowTintScale * pivot * ShadowMask(v, pivot);
                double red = v * (1.0 + p.RedGain / 100.0 * GainScale) + warmth + tint;
                double green = v * (1.0 + p.GreenGain / 100.0 * GainScale);
                double blue = v * (1.0 + p.BlueGain / 100.0 * GainScale) - warmth - tint;
                red = ShadeMath.Clamp(red, 0.0, 1.0);
                green = ShadeMath.Clamp(green, 0.0, 1.0);
                blue = ShadeMath.Clamp(blue, 0.0, 1.0);

                // SetDeviceGammaRamp requires a monotonic ramp on many drivers.
                red = Math.Max(previousRed, red);
                green = Math.Max(previousGreen, green);
                blue = Math.Max(previousBlue, blue);
                previousRed = red;
                previousGreen = green;
                previousBlue = blue;

                ramp.Red[i] = ToWord(red);
                ramp.Green[i] = ToWord(green);
                ramp.Blue[i] = ToWord(blue);
                totalChange += (Math.Abs(red - x) + Math.Abs(green - x) + Math.Abs(blue - x)) / 3.0;
            }
            ramp.ChangeStrength = ShadeMath.Clamp(totalChange / 256.0 * 4.2, 0.0, 1.0);
            return ramp;
        }

        /// <summary>Luminance tone stages for one input level (0..1), before the colour stage. Result is clamped to 0..1.</summary>
        public static double ToneValue(ShadePreset p, double x)
        {
            if (p == null) throw new ArgumentNullException("p");
            double v = ShadeMath.Clamp(x, 0.0, 1.0);

            // 1. BlackLevel: + raises the floor (white stays 1), - crushes the bottom (black stays 0).
            double black = p.BlackLevel / 100.0 * BlackLevelScale;
            if (black > 0.0) v = black + (1.0 - black) * v;
            else if (black < 0.0) v = Math.Max(0.0, (v + black) / (1.0 + black));

            // 2. ShadowLift: bump below the pivot. g(t) = 6.75 t (1-t)^2 is 0 at black and at the pivot,
            //    peaks at t = 1/3; slope stays positive while amplitude < 1/2.25 of the pivot.
            double pivot = p.ShadowRange / 100.0;
            double lift = p.ShadowLift / 100.0 * ShadowLiftScale;
            if (lift > 0.0 && v < pivot)
            {
                double t = v / pivot;
                v += lift * pivot * 6.75 * t * (1.0 - t) * (1.0 - t);
            }

            // 3. Gamma (midtone). 100 = neutral; higher = brighter midtones.
            if (p.Gamma != ShadePreset.GammaNeutral && v > 0.0)
                v = Math.Pow(v, ShadePreset.GammaNeutral / (double)p.Gamma);

            // 4. Contrast around 0.5.
            if (p.Contrast != 0)
                v = 0.5 + (v - 0.5) * (1.0 + p.Contrast / 100.0 * ContrastScale);
            v = ShadeMath.Clamp(v, 0.0, 1.0);

            // 5. Brightness, midtone-weighted so black and white endpoints do not move.
            if (p.Brightness != 0)
                v += p.Brightness / 100.0 * BrightnessScale * 4.0 * v * (1.0 - v);

            // 6. HighlightProtect: soft roll-off above the knee (slope 1 at the knee, never negative).
            double knee = p.HighlightStart / 100.0;
            double protect = p.HighlightProtect / 100.0 * HighlightScale;
            if (protect > 0.0 && v > knee)
            {
                double t = (v - knee) / (1.0 - knee);
                v = knee + (1.0 - knee) * t * (1.0 - protect * t * 0.5);
            }

            return ShadeMath.Clamp(v, 0.0, 1.0);
        }

        /// <summary>ShadowTint weight: 0 at black, peak at 1/3 of the pivot, back to 0 at the pivot.</summary>
        public static double ShadowMask(double v, double pivot)
        {
            if (double.IsNaN(v) || double.IsNaN(pivot) || pivot <= 0.0 || v <= 0.0 || v >= pivot) return 0.0;
            double t = v / pivot;
            return 6.75 * t * (1.0 - t) * (1.0 - t);
        }

        public static ShadeRamp Identity()
        {
            var ramp = new ShadeRamp();
            for (int i = 0; i < 256; i++)
            {
                ushort id = (ushort)(i * 257);
                ramp.Red[i] = id;
                ramp.Green[i] = id;
                ramp.Blue[i] = id;
            }
            return ramp;
        }

        public static ushort ToWord(double value)
        {
            return (ushort)Math.Round(ShadeMath.Clamp(value, 0.0, 1.0) * ushort.MaxValue);
        }
    }
}
