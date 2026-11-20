// v5.1.1 (X11): measured text widths, cached (WinForms; not in the pure test projects).
using System;
using System.Collections.Generic;

namespace TarkovCrosshair
{
    /// <summary>v5.1.1 (X11): TextRenderer width of (text, font), measured once. FitLabelColumn measured every row label of
    /// every page on every fit (hundreds of GDI measurements per switch on Leo's PC).</summary>
    internal static class TextWidthCache
    {
        private static readonly Dictionary<string, int> cache = new Dictionary<string, int>();
        /// <summary>Probe / tests: real measurements made.</summary>
        internal static int Measured;

        public static int Width(string text, System.Drawing.Font font)
        {
            if (string.IsNullOrEmpty(text) || font == null) return 0;
            string key = font.Name + "|" + font.Size.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)font.Style + "|" + text;
            int w;
            if (cache.TryGetValue(key, out w)) return w;
            w = System.Windows.Forms.TextRenderer.MeasureText(text, font).Width;
            Measured++;
            if (cache.Count > 4000) cache.Clear();
            cache[key] = w;
            return w;
        }

        private static readonly Dictionary<string, int> heights = new Dictionary<string, int>();

        /// <summary>v5.1.1 (X11): single-line TextRenderer height of (text, font), measured once (Theme.ButtonNeedH).</summary>
        public static int SingleLineHeight(string text, System.Drawing.Font font)
        {
            if (font == null) return 0;
            string key = font.Name + "|" + font.Size.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)font.Style + "|" + (text ?? "");
            int h;
            if (heights.TryGetValue(key, out h)) return h;
            h = System.Windows.Forms.TextRenderer.MeasureText(text ?? "", font, new System.Drawing.Size(int.MaxValue, int.MaxValue), System.Windows.Forms.TextFormatFlags.SingleLine).Height;
            Measured++;
            if (heights.Count > 4000) heights.Clear();
            heights[key] = h;
            return h;
        }
    }
}
