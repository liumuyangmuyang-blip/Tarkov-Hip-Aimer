// TarkovShade - ShadePilot share codes ("SP3-...").
// Exact port of ShadePilot DisplayPreset.cs PresetCode (lines 5951-6014, commit 2bb77f6):
// payload = Name|13 style ints|Saturation joined with '|', UTF-8, URL-safe base64 without '=',
// then "-" + FNV-1a 32-bit checksum of the UTF-8 bytes as 8 upper-case hex digits.
// Decoding also accepts SP2- (saturation stored 0..200, halved on import) and SP1- (14 fields).
// ShadePilot: https://github.com/BYS-XSQ/ShadePilot, Copyright (c) 2026 BYS-XSQ, MIT License
// (see LICENSE-SHADEPILOT.txt). C# 5.
using System;
using System.Text;

namespace TarkovShade
{
    public static class PilotShareCode
    {
        public const string Prefix = "SP3-";

        /// <summary>
        /// Same string ShadePilot's "\u5bfc\u51fa\u5206\u4eab\u4ee3\u7801" produces. Only Name, the 13 style values and
        /// Saturation are in a code; strength mode and hardware values are not.
        /// </summary>
        public static string Encode(PilotStyle p)
        {
            if (p == null) throw new ArgumentNullException("p");
            string payload = string.Join("|", new string[] {
                p.Name ?? "\u9884\u8bbe", p.Shadows.ToString(), p.Highlights.ToString(),
                p.Midtones.ToString(), p.BlackPoint.ToString(), p.WhitePoint.ToString(),
                p.ContrastPivot.ToString(), p.Temperature.ToString(), p.Tint.ToString(),
                p.Vibrance.ToString(), p.Exposure.ToString(), p.ShadowRange.ToString(),
                p.HighlightRange.ToString(), p.TransitionSoftness.ToString(),
                (p.Saturation <= 0 ? 50 : p.Saturation).ToString() });
            byte[] data = Encoding.UTF8.GetBytes(payload);
            string body = Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return Prefix + body + "-" + Checksum(data).ToString("X8");
        }

        /// <summary>
        /// Decode like ShadePilot's "\u5bfc\u5165". Throws InvalidOperationException with ShadePilot's own
        /// (Chinese) message on a bad code. The result has ApplySaturation = true only when the
        /// code carried a non-neutral saturation (saturation is always in a v2/v3 code; 50 = neutral).
        /// Hardware brightness/contrast stay "do not change".
        /// </summary>
        public static PilotStyle Decode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) ||
                !(code.StartsWith(Prefix) || code.StartsWith("SP2-") || code.StartsWith("SP1-")))
                throw new InvalidOperationException("\u4e0d\u662f ShadePilot \u5206\u4eab\u4ee3\u7801\u3002"); // \u4e0d\u662f ShadePilot \u5206\u4eab\u4ee3\u7801\u3002
            string codePrefix = code.StartsWith(Prefix) ? Prefix :
                (code.StartsWith("SP2-") ? "SP2-" : "SP1-");
            int split = code.LastIndexOf('-');
            if (split <= Prefix.Length) throw new InvalidOperationException("\u4ee3\u7801\u4e0d\u5b8c\u6574\u3002"); // \u4ee3\u7801\u4e0d\u5b8c\u6574\u3002
            string body = code.Substring(codePrefix.Length, split - codePrefix.Length)
                .Replace('-', '+').Replace('_', '/');
            while (body.Length % 4 != 0) body += "=";
            byte[] data = Convert.FromBase64String(body);
            uint expected;
            if (!uint.TryParse(code.Substring(split + 1),
                System.Globalization.NumberStyles.HexNumber, null, out expected) ||
                expected != Checksum(data))
                throw new InvalidOperationException("\u6821\u9a8c\u5931\u8d25\uff0c\u4ee3\u7801\u53ef\u80fd\u590d\u5236\u4e0d\u5b8c\u6574\u3002"); // \u6821\u9a8c\u5931\u8d25\uff0c\u4ee3\u7801\u53ef\u80fd\u590d\u5236\u4e0d\u5b8c\u6574\u3002
            string[] v = Encoding.UTF8.GetString(data).Split('|');
            if (v.Length != 14 && v.Length != 15) throw new InvalidOperationException("\u53c2\u6570\u6570\u91cf\u4e0d\u6b63\u786e\u3002"); // \u53c2\u6570\u6570\u91cf\u4e0d\u6b63\u786e\u3002
            PilotStyle preset = new PilotStyle();
            preset.Name = v[0]; preset.Shadows = I(v[1]); preset.Highlights = I(v[2]);
            preset.Midtones = I(v[3]); preset.BlackPoint = I(v[4]); preset.WhitePoint = I(v[5]);
            preset.ContrastPivot = I(v[6]); preset.Temperature = I(v[7]); preset.Tint = I(v[8]);
            preset.Vibrance = I(v[9]); preset.Exposure = I(v[10]); preset.ShadowRange = I(v[11]);
            preset.HighlightRange = I(v[12]); preset.TransitionSoftness = I(v[13]);
            preset.Saturation = v.Length == 15 ? I(v[14]) : 50;
            if (codePrefix == "SP2-")
                preset.Saturation = (int)Math.Round(preset.Saturation / 2.0);
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 ||
                preset.Shadows < 0 || preset.Shadows > 200 ||
                preset.Highlights < 0 || preset.Highlights > 200 ||
                !Signed(preset.Midtones) || !Signed(preset.BlackPoint) ||
                !Signed(preset.WhitePoint) || !Signed(preset.ContrastPivot) ||
                !Signed(preset.Temperature) || !Signed(preset.Tint) ||
                !Signed(preset.Vibrance) || !Signed(preset.Exposure) ||
                !Signed(preset.ShadowRange) || !Signed(preset.HighlightRange) ||
                !Signed(preset.TransitionSoftness) ||
                preset.Saturation < 0 || preset.Saturation > 100)
                throw new InvalidOperationException("\u5206\u4eab\u4ee3\u7801\u5305\u542b\u8d85\u51fa\u5141\u8bb8\u8303\u56f4\u7684\u53c2\u6570\u3002"); // \u5206\u4eab\u4ee3\u7801\u5305\u542b\u8d85\u51fa\u5141\u8bb8\u8303\u56f4\u7684\u53c2\u6570\u3002
            preset.Name = preset.Name.Trim();
            preset.ApplySaturation = preset.Saturation != PilotStyle.SaturationNeutral;
            // ShadePilot keeps strength app-wide; pick the narrowest mode the values fit in.
            preset.StrengthMode = preset.FitsStrength(PilotStyle.StrengthSoft) ? PilotStyle.StrengthSoft
                : preset.FitsStrength(PilotStyle.StrengthStandard) ? PilotStyle.StrengthStandard
                : PilotStyle.StrengthExtreme;
            return preset;
        }

        /// <summary>Non-throwing Decode. Also catches FormatException from bad base64.</summary>
        public static bool TryDecode(string code, out PilotStyle style, out string error)
        {
            style = null; error = "";
            try { style = Decode(code == null ? null : code.Trim()); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool LooksLikeCode(string text)
        {
            if (text == null) return false;
            text = text.Trim();
            return text.StartsWith(Prefix) || text.StartsWith("SP2-") || text.StartsWith("SP1-");
        }

        private static bool Signed(int value) { return value >= -100 && value <= 100; }
        private static int I(string value)
        {
            int result;
            if (!int.TryParse(value, out result))
                throw new InvalidOperationException("\u4ee3\u7801\u4e2d\u5305\u542b\u65e0\u6548\u53c2\u6570\u3002"); // \u4ee3\u7801\u4e2d\u5305\u542b\u65e0\u6548\u53c2\u6570\u3002
            return result;
        }
        internal static uint Checksum(byte[] data)
        {
            uint hash = 2166136261;
            foreach (byte b in data) { hash ^= b; hash *= 16777619; }
            return hash;
        }
    }
}
