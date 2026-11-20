// TarkovShade - LEGACY (tests only) one-preset share code: "TSHADE1:" + base64(UTF-8 JSON { v, preset }).
using System;
using System.Runtime.Serialization;
using System.Text;

namespace TarkovShade
{
    [DataContract]
    internal sealed class ShareEnvelope
    {
        [DataMember(Order = 0)] public int v { get; set; }
        [DataMember(Order = 1)] public ShadePreset preset { get; set; }
    }

    internal static class LegacyShareCode
    {
        public const string Prefix = "TSHADE1:";
        public const int Version = 1;
        public const int MaxCodeLength = 4096;

        public static string Export(ShadePreset preset)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            ShadePreset p = preset.Clone();
            p.Normalize();
            p.BuiltIn = false;
            var env = new ShareEnvelope();
            env.v = Version;
            env.preset = p;
            string json = JsonFile.ToJson(env);
            return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>Validates and clamps. Returns null with an error message on any problem.</summary>
        public static ShadePreset Import(string code, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(code))
            {
                error = "Share code is empty.";
                return null;
            }
            var compact = new StringBuilder();
            foreach (char c in code)
                if (!char.IsWhiteSpace(c)) compact.Append(c);
            string s = compact.ToString();
            if (s.Length > MaxCodeLength)
            {
                error = "Share code is too long.";
                return null;
            }
            if (s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(Prefix.Length);
            else if (s.IndexOf(':') >= 0)
            {
                error = "Unknown share code format (expected " + Prefix + "...).";
                return null;
            }

            string json;
            try
            {
                json = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }
            catch
            {
                error = "Share code is not valid base64.";
                return null;
            }

            ShareEnvelope env;
            try
            {
                env = JsonFile.FromJson<ShareEnvelope>(json);
            }
            catch
            {
                error = "Share code does not contain a valid preset.";
                return null;
            }
            if (env == null || env.preset == null)
            {
                error = "Share code does not contain a preset.";
                return null;
            }
            if (env.v != Version)
            {
                error = "Unsupported share code version " + env.v + " (this build reads version " + Version + ").";
                return null;
            }
            ShadePreset p = env.preset;
            p.BuiltIn = false;
            p.Normalize();
            return p;
        }
    }
}


namespace TarkovShade
{
    [DataContract]
    public sealed class StyleShareEnvelope
    {
        [DataMember(Order = 0)] public int v { get; set; }
        [DataMember(Order = 1)] public PilotStyle style { get; set; }
    }

    /// <summary>
    /// TarkovShade share code: "TSHADE2:" + base64(UTF-8 JSON { v: 2, style: PilotStyle }). Carries the style values,
    /// saturation (+ backend) and the optional monitor values. TSHADE1 (old test builds) and ShadePilot SP3 codes are refused.
    /// </summary>
    public static class ShareCode
    {
        public const string Prefix = "TSHADE2:";
        public const string OldPrefix = "TSHADE1:";
        public const int Version = 2;
        public const int MaxCodeLength = 4096;
        public const string OldCodeMessage =
            "This is an old TSHADE1 code from an earlier test build; it cannot be imported. Ask for a new code (TSHADE2).";

        public static string Export(PilotStyle style)
        {
            if (style == null) throw new ArgumentNullException("style");
            PilotStyle p = style.Clone();
            PilotPresetStore.EnforceFixed(p);
            p.BuiltIn = false;
            p.Untested = false;
            StyleShareEnvelope env = new StyleShareEnvelope();
            env.v = Version;
            env.style = p;
            return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonFile.ToJson(env)));
        }

        /// <summary>Null + plain English error on any problem. Fixed fields forced; BuiltIn / Untested false.</summary>
        public static PilotStyle Import(string code, out string error)
        {
            error = "";
            if (code == null || code.Trim().Length == 0) { error = "Share code is empty."; return null; }
            StringBuilder compact = new StringBuilder();
            foreach (char c in code) if (!char.IsWhiteSpace(c)) compact.Append(c);
            string s = compact.ToString();
            if (s.Length > MaxCodeLength) { error = "Share code is too long."; return null; }
            if (s.StartsWith(OldPrefix, StringComparison.OrdinalIgnoreCase)) { error = OldCodeMessage; return null; }
            if (s.StartsWith("SP3-", StringComparison.OrdinalIgnoreCase) || s.StartsWith("SP2-", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("SP1-", StringComparison.OrdinalIgnoreCase))
            {
                error = "ShadePilot codes are not supported. Use a TarkovShade code (" + Prefix + "...).";
                return null;
            }
            if (s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(Prefix.Length);
            else { error = "Unknown share code format (expected " + Prefix + "...)."; return null; }

            string json;
            try { json = Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { error = "Share code is not valid base64."; return null; }

            StyleShareEnvelope env;
            try { env = JsonFile.FromJson<StyleShareEnvelope>(json); }
            catch { error = "Share code does not contain a valid preset."; return null; }
            if (env == null || env.style == null) { error = "Share code does not contain a preset."; return null; }
            if (env.v != Version)
            {
                error = "Unsupported share code version " + env.v + " (this build reads version " + Version + ").";
                return null;
            }
            PilotStyle p = env.style;
            PilotPresetStore.EnforceFixed(p);
            p.BuiltIn = false;
            p.Untested = false;
            return p;
        }
    }
}
