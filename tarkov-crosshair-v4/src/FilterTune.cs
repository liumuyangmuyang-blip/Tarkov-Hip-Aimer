using System;
using TarkovShade;

// v4.3 (W2, Leo 09:5x UTC) 「调试滤镜」: one bar, left = darker, right = brighter, centre = the preset exactly. Pure (tested).
// v5.0.2 (W15, BRUTE P2-3): monotone. Every value only ever moves in its own brightening direction as the bar moves right (and
// darkening as it moves left), and never past the preset in the other direction, so the picture's mean brightness never drops
// when the bar moves right (tested over the built-ins, fixed user presets and random presets, TestsV502Filter.cs):
//   t > 0 (brighter):  提亮暗部 -> 150, 黑位 -> +30 more, 中间调 -> +35 more;
//   t < 0 (darker):    提亮暗部 -> 0, 黑位 -> min(preset, -25), 中间调 -> min(preset, -30)
//                      (a preset already darker than a target keeps its own value).
//   压低高光 stays at the preset on both sides (raising it darkens the sky, lowering it brightens it: either breaks the order).
// Unchanged at every position: 压低高光, 白位, 中间对比, 色温, 自然色彩, 暗部范围, 高光范围, 过渡柔和, saturation (+ backend),
// monitor values, name. Ends clamp to the slider ranges; centre returns the preset's values exactly.
// v5.1 (W24, Leo decision 10): 11 steps, -5 .. +5 (step k = old position k * 20; the ends are the old ends).
// v5.1 (W24): FilterCurvePoints (the 5 draggable points of the 色调曲线 graph) and FilterLimit (how far a curve may move from
// normal before Windows refuses it) live here too: pure, tested (TestsV51W24.cs).

namespace TarkovCrosshair
{
    internal static class FilterTune
    {
        public const int Min = -5, Max = 5;

        public static PilotStyle Apply(PilotStyle preset, int bar)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            PilotStyle s = preset.Clone();
            if (bar == 0) return s;
            double t = Math.Max(Min, Math.Min(Max, bar)) / (double)Max;
            int tonalMax = PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode);
            int sMax = PilotStyle.StrengthSignedMax(PilotViews.FixedStrengthMode);
            if (t > 0)
            {
                s.Shadows = Lerp(preset.Shadows, Math.Max(preset.Shadows, tonalMax), t, 0, tonalMax);
                s.BlackPoint = Lerp(preset.BlackPoint, preset.BlackPoint + 30, t, -sMax, sMax);
                s.Midtones = Lerp(preset.Midtones, preset.Midtones + 35, t, -sMax, sMax);
            }
            else
            {
                double u = -t;
                s.Shadows = Lerp(preset.Shadows, 0, u, 0, tonalMax);
                s.BlackPoint = Lerp(preset.BlackPoint, Math.Min(preset.BlackPoint, -25), u, -sMax, sMax);
                s.Midtones = Lerp(preset.Midtones, Math.Min(preset.Midtones, -30), u, -sMax, sMax);
            }
            return s;
        }

        /// <summary>v5.1: the step closest to `bar` (towards 0) whose curve Windows still accepts (FilterLimit). Returns that step;
        /// `why` = "" when `bar` itself is fine, else the Chinese reason shown under the bar.</summary>
        public static int Fit(PilotStyle preset, int bar, out string why)
        {
            why = "";
            if (preset == null) return 0;
            bar = Math.Max(Min, Math.Min(Max, bar));
            for (int b = bar; b != 0; b -= Math.Sign(b))
            {
                if (FilterLimit.Fits(Apply(preset, b)))
                {
                    if (b != bar) why = FilterLimit.Reason;
                    return b;
                }
            }
            if (bar != 0) why = FilterLimit.Reason;
            return 0;
        }

        private static int Lerp(int a, int b, double t, int lo, int hi)
        {
            int v = (int)Math.Round(a + (b - a) * t, MidpointRounding.AwayFromZero);
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }

    /// <summary>
    /// v5.1 (W24): how far a curve may move from the normal straight line. Windows checks every gamma curve it is given and
    /// refuses curves that move too far from normal (MS docs, SetDeviceGammaRamp: such a ramp "fails silently"); the exact limit
    /// is not published. This app's limit: every entry of every channel within MaxDeviation8 (out of 255) of normal. Chosen so
    /// every built-in preset fits (黑夜模式 moves 113 / 255); the read-back check in the module still catches a refusal below it.
    /// </summary>
    internal static class FilterLimit
    {
        public const double MaxDeviation8 = 128.0;

        /// <summary>Shown wherever a value stops at the limit (curve points, 调试滤镜).</summary>
        public const string Reason = "已到 Windows 允许的最大值：再拉曲线就离原来的样子太远，Windows 会拒绝这条颜色曲线（画面不会变），所以停在这里。";

        /// <summary>Largest distance of any R / G / B entry from normal, in 8-bit steps (0..255).</summary>
        public static double Deviation8(PilotStyle s)
        {
            if (s == null) return 0;
            ShadeRamp r = PilotCurve.Build(s);
            double m = 0;
            foreach (ushort[] ch in new ushort[][] { r.Red, r.Green, r.Blue })
                for (int i = 0; i < PilotCurve.Size; i++) m = Math.Max(m, Math.Abs(ch[i] / 257.0 - i));
            return m;
        }

        public static bool Fits(PilotStyle s) { return Deviation8(s) <= MaxDeviation8 + 1e-9; }
    }

    /// <summary>
    /// v5.1 (W24, Leo decision 10) 色调曲线 points, Photoshop style: 5 points at fixed inputs, dragged up / down. Each point is
    /// driven by one slider of the filter (the curve maths stays ShadePilot's): 黑位 (input 20) -> 黑位, 暗部 (64) -> 提亮暗部,
    /// 中间调 (128) -> 中间调, 高光 (191) -> 压低高光 (higher point = less 压低), 白位 (235) -> 白位. A drag picks the slider value
    /// whose curve passes closest to the mouse; other points may move a little with it (the sliders shape the whole curve).
    /// Values stop at the slider range and at FilterLimit, with the reason in Chinese.
    /// </summary>
    internal static class FilterCurvePoints
    {
        public const int Count = 5;
        public const int Black = 0, Shadow = 1, Mid = 2, High = 3, White = 4;
        /// <summary>Input of each point, 0..255.</summary>
        public static readonly int[] Inputs = { 20, 64, 128, 191, 235 };
        public static readonly string[] Names = { "黑位", "暗部", "中间调", "高光", "白位" };
        /// <summary>The slider each point drives (PilotHelpText keys).</summary>
        public static readonly string[] Keys = { PilotHelpText.BlackPoint, PilotHelpText.Shadows, PilotHelpText.Midtones, PilotHelpText.Highlights, PilotHelpText.WhitePoint };

        /// <summary>Output (0..255, not rounded) of the curve at point i's input: luminance curve, no colour gains (what the graph shows).</summary>
        public static double Output(PilotStyle s, int i)
        {
            return PilotCurve.PreviewValue(s, Inputs[i] / 255.0) * 255.0;
        }

        public static int Get(PilotStyle s, int i)
        {
            switch (i)
            {
                case Black: return s.BlackPoint;
                case Shadow: return s.Shadows;
                case Mid: return s.Midtones;
                case High: return s.Highlights;
                default: return s.WhitePoint;
            }
        }

        public static void Set(PilotStyle s, int i, int v)
        {
            switch (i)
            {
                case Black: s.BlackPoint = v; break;
                case Shadow: s.Shadows = v; break;
                case Mid: s.Midtones = v; break;
                case High: s.Highlights = v; break;
                default: s.WhitePoint = v; break;
            }
        }

        public static void Range(int i, out int min, out int max)
        {
            if (i == Shadow || i == High) { min = 0; max = PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode); }
            else { int m = PilotStyle.StrengthSignedMax(PilotViews.FixedStrengthMode); min = -m; max = m; }
        }

        /// <summary>
        /// Drag point i to `target` (0..255 output). Returns the new style (a copy; only point i's slider changes) and `why`:
        /// "" when the point reached the mouse (within 1.5 steps), else the Chinese reason it stopped.
        /// </summary>
        public static PilotStyle Drag(PilotStyle s, int i, double target, out string why)
        {
            why = "";
            if (s == null) throw new ArgumentNullException("s");
            if (i < 0 || i >= Count) throw new ArgumentOutOfRangeException("i");
            if (double.IsNaN(target)) target = Output(s, i);
            target = Math.Max(0, Math.Min(255, target));
            int min, max;
            Range(i, out min, out max);
            PilotStyle w = s.Clone();
            int cur = Math.Max(min, Math.Min(max, Get(s, i)));
            // closest slider value (scan: the curve is cheap at one input, ~300 values); ties keep the value nearest the current one
            int best = cur;
            double bestErr = double.MaxValue;
            for (int v = min; v <= max; v++)
            {
                Set(w, i, v);
                double e = Math.Abs(Output(w, i) - target);
                if (e < bestErr - 1e-9 || (Math.Abs(e - bestErr) <= 1e-9 && Math.Abs(v - cur) < Math.Abs(best - cur))) { bestErr = e; best = v; }
            }
            Set(w, i, best);
            bool limited = false;
            if (!FilterLimit.Fits(w))
            {
                // walk back towards the current value: the last value that still fits (bisection; the start fits or is the start)
                int ok = cur, bad = best;
                Set(w, i, cur);
                if (!FilterLimit.Fits(w)) ok = bad = cur; // already past the limit (old file / imported code): do not move further out
                while (Math.Abs(bad - ok) > 1)
                {
                    int m = (ok + bad) / 2;
                    Set(w, i, m);
                    if (FilterLimit.Fits(w)) ok = m; else bad = m;
                }
                best = ok;
                Set(w, i, best);
                limited = true;
            }
            double got = Output(w, i);
            if (Math.Abs(got - target) > 1.5)
            {
                if (limited) why = FilterLimit.Reason;
                else why = EndReason(i, best, min, max, target > got);
            }
            return w;
        }

        /// <summary>Why point i could not follow the mouse at slider value v (range end or the slider cannot move it that way).</summary>
        internal static string EndReason(int i, int v, int min, int max, bool wantedUp)
        {
            string slider = PilotHelpText.Label(Keys[i]);
            bool atEnd = wantedUp ? (i == High ? v == min : v == max) : (i == High ? v == max : v == min);
            string end = (i == High ? !wantedUp : wantedUp) ? "最大 " + max : "最小 " + min;
            if (atEnd) return "「" + Names[i] + "」点停住了：它连着的「" + slider + "」已经到" + end + "。";
            return "「" + Names[i] + "」点停住了：「" + slider + "」再怎么改也到不了那里，其他滑块也在影响这一段曲线。";
        }

        /// <summary>Live readout under the graph: 「黑位 20→26 · 暗部 64→88 · …」 (input → output, 0..255).</summary>
        public static string Readout(PilotStyle s)
        {
            if (s == null) return "";
            string[] parts = new string[Count];
            for (int i = 0; i < Count; i++) parts[i] = Names[i] + " " + Inputs[i] + "→" + (int)Math.Round(Output(s, i));
            return string.Join(" · ", parts);
        }
    }
}
