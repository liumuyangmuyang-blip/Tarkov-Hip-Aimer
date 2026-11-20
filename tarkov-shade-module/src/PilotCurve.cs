// TarkovShade - ShadePilot picture-style gamma ramp builder.
// The math below is a line-for-line port of ShadePilot DisplayPreset.cs (commit 2bb77f6):
//   GammaRampController.Apply gain math     lines 3750-3784
//   ScalePositive / ScaleSigned             lines 3787-3800
//   BuildChannel / TransformValue           lines 3846-3912
// Same operations in the same order, so the 256x3 ramp is bit-identical to ShadePilot
// (proved by tests/pilot against an extracted copy of the original code).
// ShadePilot: https://github.com/BYS-XSQ/ShadePilot, Copyright (c) 2026 BYS-XSQ, MIT License
// (see LICENSE-SHADEPILOT.txt). C# 5.
using System;

namespace TarkovShade
{
    public static class PilotCurve
    {
        public const int Size = 256;

        /// <summary>Identity baseline: entry i = i * 257 (ShadePilot GammaRamp.CreateIdentity).</summary>
        public static ushort[] IdentityChannel()
        {
            ushort[] a = new ushort[Size];
            for (int i = 0; i < Size; i++) a[i] = (ushort)(i * 257);
            return a;
        }

        /// <summary>Build on an identity baseline. See the full overload.</summary>
        public static ShadeRamp Build(PilotStyle style)
        {
            ushort[] id = IdentityChannel();
            return Build(style, id, id, id, false);
        }

        /// <summary>
        /// Build the ramp exactly like ShadePilot. ShadePilot transforms the display's ORIGINAL ramp
        /// (captured at start-up), not the identity: pass that baseline here to match it on a
        /// calibrated display (an identity baseline is what most displays report).
        /// clampToStrength=true first clamps to the style's StrengthMode slider ranges (what a
        /// ShadePilot slider does); false clamps only to the full ranges (ShadePilot WPF preset
        /// apply path, NormalizeStyleState). For sun2 both give the same ramp.
        /// </summary>
        public static ShadeRamp Build(PilotStyle style, ushort[] baseRed, ushort[] baseGreen, ushort[] baseBlue,
            bool clampToStrength)
        {
            if (style == null) throw new ArgumentNullException("style");
            CheckChannel(baseRed, "baseRed");
            CheckChannel(baseGreen, "baseGreen");
            CheckChannel(baseBlue, "baseBlue");
            PilotStyle s = style.Clone();
            s.Normalize();
            if (clampToStrength) s.ClampToStrength(s.StrengthMode);

            ShadeRamp ramp = new ShadeRamp();
            BuildRaw(baseRed, baseGreen, baseBlue, ramp.Red, ramp.Green, ramp.Blue,
                s.Shadows, s.Highlights, s.Midtones, s.BlackPoint, s.WhitePoint, s.ContrastPivot,
                s.Temperature, s.Tint, s.Vibrance, s.Exposure, s.ShadowRange, s.HighlightRange,
                s.TransitionSoftness);

            double totalChange = 0;
            for (int i = 0; i < Size; i++)
            {
                double x = i / 255.0;
                totalChange += (Math.Abs(ramp.Red[i] / 65535.0 - x) + Math.Abs(ramp.Green[i] / 65535.0 - x) +
                    Math.Abs(ramp.Blue[i] / 65535.0 - x)) / 3.0;
            }
            ramp.ChangeStrength = Math.Max(0.0, Math.Min(1.0, totalChange / 256.0 * 4.2));
            return ramp;
        }

        /// <summary>
        /// ShadePilot GammaRampController.Apply minus the device I/O: raw slider ints in, ramp out.
        /// No clamping beyond what ShadePilot itself does inside ScalePositive / ScaleSigned.
        /// </summary>
        public static void BuildRaw(ushort[] srcR, ushort[] srcG, ushort[] srcB,
            ushort[] outR, ushort[] outG, ushort[] outB,
            int shadowPercent, int highlightPercent, int midtoneValue,
            int blackPointValue, int whitePointValue, int contrastPivotValue,
            int temperatureValue, int tintValue, int vibranceValue,
            int exposureValue, int shadowRangeValue,
            int highlightRangeValue, int transitionSoftnessValue)
        {
            double shadow = ScalePositive(shadowPercent, 200);
            double highlight = ScalePositive(highlightPercent, 200);
            double midtone = ScaleSigned(midtoneValue);
            double blackPoint = ScaleSigned(blackPointValue);
            double whitePoint = ScaleSigned(whitePointValue);
            double contrastPivot = ScaleSigned(contrastPivotValue);
            double temperature = ScaleSigned(temperatureValue);
            double tint = ScaleSigned(tintValue);
            double vibrance = ScaleSigned(vibranceValue);
            double exposure = ScaleSigned(exposureValue);
            double shadowRange = ScaleSigned(shadowRangeValue);
            double highlightRange = ScaleSigned(highlightRangeValue);
            double transitionSoftness = ScaleSigned(transitionSoftnessValue);

            double redGain, greenGain, blueGain;
            ChannelGains(temperature, tint, out redGain, out greenGain, out blueGain);

            BuildChannel(srcR, outR, shadow, highlight, midtone, blackPoint, whitePoint, contrastPivot,
                vibrance, exposure, shadowRange, highlightRange, transitionSoftness, redGain);
            BuildChannel(srcG, outG, shadow, highlight, midtone, blackPoint, whitePoint, contrastPivot,
                vibrance, exposure, shadowRange, highlightRange, transitionSoftness, greenGain);
            BuildChannel(srcB, outB, shadow, highlight, midtone, blackPoint, whitePoint, contrastPivot,
                vibrance, exposure, shadowRange, highlightRange, transitionSoftness, blueGain);
        }

        /// <summary>\u8272\u6e29 / \u8272\u8c03 channel gains from SCALED temperature/tint (ShadePilot lines 3764-3770).</summary>
        public static void ChannelGains(double temperature, double tint,
            out double redGain, out double greenGain, out double blueGain)
        {
            redGain = 1.0 + 0.32 * temperature + 0.12 * tint;
            greenGain = 1.0 - 0.20 * tint;
            blueGain = 1.0 - 0.32 * temperature + 0.12 * tint;
            double maximumGain = Math.Max(1.0, Math.Max(redGain, Math.Max(greenGain, blueGain)));
            redGain /= maximumGain;
            greenGain /= maximumGain;
            blueGain /= maximumGain;
        }

        /// <summary>ShadePilot ScalePositive: (clamp(v,0,max)/max)^0.78 * 2.</summary>
        public static double ScalePositive(int value, int maximum)
        {
            double normalized = Math.Max(0, Math.Min(maximum, value)) / (double)maximum;
            return Math.Pow(normalized, 0.78) * 2.0;
        }

        /// <summary>ShadePilot ScaleSigned: sign(v) * (|clamp(v,-100,100)|/100)^0.78.</summary>
        public static double ScaleSigned(int value)
        {
            double normalized = Math.Max(-100, Math.Min(100, value)) / 100.0;
            if (normalized == 0.0) return 0.0;
            return Math.Sign(normalized) * Math.Pow(Math.Abs(normalized), 0.78);
        }

        /// <summary>One channel, ShadePilot BuildChannel: gain, clamp, monotonic guard, round.</summary>
        public static void BuildChannel(
            ushort[] source, ushort[] target, double shadow, double highlight,
            double midtone, double blackPoint, double whitePoint,
            double contrastPivot, double vibrance, double exposure,
            double shadowRange, double highlightRange, double transitionSoftness,
            double channelGain)
        {
            double previous = 0;
            for (int i = 0; i < 256; i++)
            {
                double x = source[i] / 65535.0;
                double y = TransformValue(
                    x, shadow, highlight, midtone, blackPoint,
                    whitePoint, contrastPivot, vibrance, exposure,
                    shadowRange, highlightRange, transitionSoftness);
                y = Math.Max(0.0, Math.Min(1.0, y * channelGain));
                y = Math.Max(previous, y);
                target[i] = (ushort)Math.Round(y * 65535.0);
                previous = y;
            }
        }

        /// <summary>
        /// ShadePilot TransformValue (lines 3868-3912). Inputs are SCALED values (ScalePositive /
        /// ScaleSigned), x in 0..1. Also what ShadePilot's ToneCurveGraph draws (line 3268).
        /// </summary>
        public static double TransformValue(
            double x, double shadow, double highlight, double midtone,
            double blackPoint, double whitePoint, double contrastPivot, double vibrance,
            double exposure, double shadowRange, double highlightRange,
            double transitionSoftness)
        {
            x = Math.Max(0.0, Math.Min(1.0, x * Math.Pow(2.0, exposure * 2.0)));
            double shadowWarp = Math.Pow(x, Math.Pow(2.0, -shadowRange * 0.85));
            double highlightWarp = Math.Pow(x, Math.Pow(2.0, highlightRange * 0.85));
            double shadowMask = shadowWarp * Math.Pow(1.0 - shadowWarp, 2.0);
            double highlightMask = highlightWarp * highlightWarp * (3.0 - 2.0 * highlightWarp);
            double falloffPower = Math.Pow(2.0, -transitionSoftness * 0.75);
            shadowMask = Math.Pow(Math.Max(0.0, shadowMask), falloffPower);
            highlightMask = Math.Pow(Math.Max(0.0, highlightMask), falloffPower);
            double lift = shadow * 1.35 * shadowMask;
            double compression = highlight * 0.27 * highlightMask;
            double blackOffset = blackPoint * 0.10 * Math.Pow(1.0 - x, 3.0);
            double whiteOffset = whitePoint * 0.10 * Math.Pow(x, 3.0);
            double y = Math.Max(0.0, Math.Min(1.0,
                x + lift - compression + blackOffset + whiteOffset));
            y = Math.Pow(y, Math.Pow(2.0, -midtone));

            // Endpoint-preserving contrast (ShadePilot comment): cannot clip black/white.
            double endpointGuard = Math.Max(0.0, 4.0 * y * (1.0 - y));
            double localContrastShape = (y - 0.5) * endpointGuard;
            y += contrastPivot * 0.78 * localContrastShape;

            // Automatic anti-haze: restores some mid separation lost to lift/compression.
            y = Math.Max(0.0, Math.Min(1.0, y));
            endpointGuard = Math.Max(0.0, 4.0 * y * (1.0 - y));
            double antiHaze = Math.Min(0.34, shadow * 0.13 + highlight * 0.07);
            y += antiHaze * (y - 0.5) * endpointGuard;

            // \u81ea\u7136\u8272\u5f69: per-channel S-curve, an approximation (a gamma ramp cannot see saturation).
            y = Math.Max(0.0, Math.Min(1.0, y));
            endpointGuard = Math.Max(0.0, 4.0 * y * (1.0 - y));
            double colorShape = (y - 0.5) * Math.Pow(endpointGuard, 0.72);
            y += vibrance * 0.48 * colorShape;
            return Math.Max(0.0, Math.Min(1.0, y));
        }

        /// <summary>Luminance curve value for preview drawing, from raw slider ints (no colour gains).</summary>
        public static double PreviewValue(PilotStyle style, double x)
        {
            if (style == null) throw new ArgumentNullException("style");
            if (double.IsNaN(x)) x = 0;
            PilotStyle s = style.Clone();
            s.Normalize();
            return TransformValue(Math.Max(0.0, Math.Min(1.0, x)),
                ScalePositive(s.Shadows, 200), ScalePositive(s.Highlights, 200),
                ScaleSigned(s.Midtones), ScaleSigned(s.BlackPoint), ScaleSigned(s.WhitePoint),
                ScaleSigned(s.ContrastPivot), ScaleSigned(s.Vibrance), ScaleSigned(s.Exposure),
                ScaleSigned(s.ShadowRange), ScaleSigned(s.HighlightRange), ScaleSigned(s.TransitionSoftness));
        }

        private static void CheckChannel(ushort[] a, string name)
        {
            if (a == null) throw new ArgumentNullException(name);
            if (a.Length != Size) throw new ArgumentException(name + " must have 256 entries", name);
        }
    }
}
