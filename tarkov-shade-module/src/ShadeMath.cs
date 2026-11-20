// TarkovShade - fixed-preset display filter module.
// Clamp / Lerp / SmoothStep are ported from TarkovAutoShade (src/Models.cs, MathUtil),
// Copyright (c) 2026 TarkovAutoShade contributors, MIT License.
// https://github.com/lu1249765686/TarkovAutoShade  - see LICENSE-THIRD-PARTY.txt
using System;

namespace TarkovShade
{
    public static class ShadeMath
    {
        public static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        /// <summary>NaN maps to minimum (TarkovShade addition: never let NaN reach a ramp or matrix).</summary>
        public static double Clamp(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value)) return minimum;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        public static double SmoothStep(double start, double end, double value)
        {
            if (end <= start) return value >= end ? 1.0 : 0.0;
            double t = Clamp((value - start) / (end - start), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
