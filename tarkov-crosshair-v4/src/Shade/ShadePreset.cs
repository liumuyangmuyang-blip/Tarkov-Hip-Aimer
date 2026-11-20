// TarkovShade - fixed-preset display filter module.
// Preset model, starter presets and the Presets.json store.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace TarkovShade
{
    /// <summary>
    /// One fixed filter preset. All values are integer slider positions.
    /// Neutral values (see CreateNeutral) produce an identity gamma ramp.
    /// </summary>
    [DataContract]
    internal sealed class ShadePreset
    {
        // ---- ranges (min, max, neutral) ----
        public const int BrightnessMin = -50, BrightnessMax = 50;
        public const int ContrastMin = -50, ContrastMax = 50;
        public const int ShadowLiftMin = 0, ShadowLiftMax = 100;
        public const int HighlightProtectMin = 0, HighlightProtectMax = 100;
        public const int SaturationMin = -100, SaturationMax = 100;
        public const int GammaMin = 50, GammaMax = 200, GammaNeutral = 100;
        public const int BlackLevelMin = -50, BlackLevelMax = 50;
        public const int ShadowRangeMin = 5, ShadowRangeMax = 50, ShadowRangeDefault = 30;
        public const int HighlightStartMin = 50, HighlightStartMax = 95, HighlightStartDefault = 75;
        public const int WarmthMin = -50, WarmthMax = 50;
        public const int GainMin = -50, GainMax = 50;
        public const int ShadowTintMin = -50, ShadowTintMax = 50;

        [DataMember(Order = 0)] public string Name { get; set; }
        [DataMember(Order = 1)] public bool BuiltIn { get; set; }

        // BASIC
        [DataMember(Order = 2)] public int Brightness { get; set; }
        [DataMember(Order = 3)] public int Contrast { get; set; }
        [DataMember(Order = 4)] public int ShadowLift { get; set; }
        [DataMember(Order = 5)] public int HighlightProtect { get; set; }
        /// <summary>Driver Digital Vibrance on/off. Off = driver value left untouched.</summary>
        [DataMember(Order = 6)] public bool UseDriverVibrance { get; set; }
        /// <summary>-100..100, 0 = driver default level (see VibranceMath).</summary>
        [DataMember(Order = 7)] public int DriverVibrance { get; set; }
        /// <summary>Magnification-API saturation on/off. Off = identity matrix.</summary>
        [DataMember(Order = 17)] public bool UseLocalSaturation { get; set; }
        /// <summary>-100..100, 0 = neutral (factor 1.0; +100 = 2.0, -100 = 0.5).</summary>
        [DataMember(Order = 18)] public int LocalSaturation { get; set; }

        // ADVANCED
        [DataMember(Order = 8)] public int Gamma { get; set; }
        [DataMember(Order = 9)] public int BlackLevel { get; set; }
        [DataMember(Order = 10)] public int ShadowRange { get; set; }
        [DataMember(Order = 11)] public int HighlightStart { get; set; }
        [DataMember(Order = 12)] public int Warmth { get; set; }
        [DataMember(Order = 13)] public int RedGain { get; set; }
        [DataMember(Order = 14)] public int GreenGain { get; set; }
        [DataMember(Order = 15)] public int BlueGain { get; set; }
        [DataMember(Order = 16)] public int ShadowTint { get; set; }

        public static ShadePreset CreateNeutral(string name)
        {
            var p = new ShadePreset();
            p.Name = name;
            p.Gamma = GammaNeutral;
            p.ShadowRange = ShadowRangeDefault;
            p.HighlightStart = HighlightStartDefault;
            return p;
        }

        /// <summary>Clamps every value into its range. Called after loading, importing and before use.</summary>
        public void Normalize()
        {
            Name = CleanName(Name);
            Brightness = ShadeMath.Clamp(Brightness, BrightnessMin, BrightnessMax);
            Contrast = ShadeMath.Clamp(Contrast, ContrastMin, ContrastMax);
            ShadowLift = ShadeMath.Clamp(ShadowLift, ShadowLiftMin, ShadowLiftMax);
            HighlightProtect = ShadeMath.Clamp(HighlightProtect, HighlightProtectMin, HighlightProtectMax);
            DriverVibrance = ShadeMath.Clamp(DriverVibrance, SaturationMin, SaturationMax);
            LocalSaturation = ShadeMath.Clamp(LocalSaturation, SaturationMin, SaturationMax);
            // 0 is never a valid gamma; treat a missing field (old/foreign JSON) as neutral.
            if (Gamma == 0) Gamma = GammaNeutral;
            Gamma = ShadeMath.Clamp(Gamma, GammaMin, GammaMax);
            BlackLevel = ShadeMath.Clamp(BlackLevel, BlackLevelMin, BlackLevelMax);
            if (ShadowRange == 0) ShadowRange = ShadowRangeDefault;
            ShadowRange = ShadeMath.Clamp(ShadowRange, ShadowRangeMin, ShadowRangeMax);
            if (HighlightStart == 0) HighlightStart = HighlightStartDefault;
            HighlightStart = ShadeMath.Clamp(HighlightStart, HighlightStartMin, HighlightStartMax);
            Warmth = ShadeMath.Clamp(Warmth, WarmthMin, WarmthMax);
            RedGain = ShadeMath.Clamp(RedGain, GainMin, GainMax);
            GreenGain = ShadeMath.Clamp(GreenGain, GainMin, GainMax);
            BlueGain = ShadeMath.Clamp(BlueGain, GainMin, GainMax);
            ShadowTint = ShadeMath.Clamp(ShadowTint, ShadowTintMin, ShadowTintMax);
        }

        public static string CleanName(string name)
        {
            if (name == null) name = "";
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
                if (!char.IsControl(c)) sb.Append(c);
            string s = DropLoneSurrogates(sb.ToString()).Trim();
            if (s.Length > MaxNameLength) s = CutName(s, MaxNameLength);
            return s.Length == 0 ? "Preset" : s;
        }

        /// <summary>Longest preset name, in UTF-16 chars, that CleanName keeps.</summary>
        public const int MaxNameLength = 40;

        /// <summary>Cuts to at most maxLength chars without splitting a surrogate pair (fix B-10), then trims.</summary>
        internal static string CutName(string s, int maxLength)
        {
            if (s.Length <= maxLength) return s.Trim();
            int cut = maxLength;
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1]) && char.IsLowSurrogate(s[cut])) cut--;
            return s.Substring(0, cut).Trim();
        }

        private static string DropLoneSurrogates(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
                    continue;
                }
                if (char.IsLowSurrogate(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public ShadePreset Clone()
        {
            return (ShadePreset)MemberwiseClone();
        }

        /// <summary>True when the two presets would produce the same output (name / BuiltIn ignored).</summary>
        public bool SameValues(ShadePreset other)
        {
            if (other == null) return false;
            return Brightness == other.Brightness && Contrast == other.Contrast &&
                ShadowLift == other.ShadowLift && HighlightProtect == other.HighlightProtect &&
                UseDriverVibrance == other.UseDriverVibrance && DriverVibrance == other.DriverVibrance &&
                UseLocalSaturation == other.UseLocalSaturation && LocalSaturation == other.LocalSaturation &&
                Gamma == other.Gamma && BlackLevel == other.BlackLevel &&
                ShadowRange == other.ShadowRange && HighlightStart == other.HighlightStart &&
                Warmth == other.Warmth && RedGain == other.RedGain &&
                GreenGain == other.GreenGain && BlueGain == other.BlueGain &&
                ShadowTint == other.ShadowTint;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Starter presets. EVERY NUMBER HERE IS A PLACEHOLDER: Leo tunes the real values.
    /// Names are written as \u escapes so the file stays ASCII (csc code-page safe).
    /// </summary>
    internal static class BuiltInPresets
    {
        public const string General = "\u901A\u7528";                    // General
        public const string DarkIndoor = "\u6697\u56FE/\u5BA4\u5185";     // Dark map / indoor
        public const string Night = "\u591C\u56FE";                      // Night map
        public const string SnowBright = "\u96EA\u5730/\u5F3A\u5149";     // Snow / bright
        public const string Off = "\u5173\u95ED";                        // Off

        public static List<ShadePreset> Create()
        {
            var list = new List<ShadePreset>();

            ShadePreset p = ShadePreset.CreateNeutral(General);       // PLACEHOLDER values
            p.Brightness = 8; p.Contrast = 6; p.ShadowLift = 25; p.HighlightProtect = 20;
            list.Add(p);

            p = ShadePreset.CreateNeutral(DarkIndoor);                // PLACEHOLDER values
            p.Brightness = 18; p.Contrast = 8; p.ShadowLift = 55; p.HighlightProtect = 35;
            p.Gamma = 115; p.ShadowRange = 35;
            list.Add(p);

            p = ShadePreset.CreateNeutral(Night);                     // PLACEHOLDER values
            p.Brightness = 22; p.Contrast = 4; p.ShadowLift = 60; p.HighlightProtect = 45;
            p.Gamma = 120; p.ShadowRange = 40; p.BlackLevel = 5;
            list.Add(p);

            p = ShadePreset.CreateNeutral(SnowBright);                // PLACEHOLDER values
            p.Brightness = -10; p.Contrast = 4; p.ShadowLift = 15; p.HighlightProtect = 70;
            p.HighlightStart = 65;
            list.Add(p);

            list.Add(ShadePreset.CreateNeutral(Off));                 // identity ramp

            foreach (ShadePreset item in list) item.BuiltIn = true;
            return list;
        }

        public static ShadePreset Find(string name)
        {
            foreach (ShadePreset p in Create())
                if (string.Equals(p.Name, name, StringComparison.Ordinal)) return p;
            return null;
        }
    }

    [DataContract]
    internal sealed class PresetFile
    {
        public const int CurrentVersion = 1;
        [DataMember(Order = 0)] public int Version { get; set; }
        [DataMember(Order = 1)] public List<ShadePreset> Presets { get; set; }
    }

    /// <summary>Presets.json next to the exe (or any folder given). Indented UTF-8 JSON so it can be hand-edited.</summary>
    internal sealed class PresetStore
    {
        public const string FileName = "Presets.json";
        private readonly string filePath;
        private List<ShadePreset> presets = new List<ShadePreset>();

        public PresetStore(string folder)
        {
            filePath = Path.Combine(folder, FileName);
        }

        public string FilePath { get { return filePath; } }

        /// <summary>Copy of the list, in display/cycle order.</summary>
        public List<ShadePreset> All
        {
            get
            {
                var copy = new List<ShadePreset>();
                foreach (ShadePreset p in presets) copy.Add(p.Clone());
                return copy;
            }
        }

        public int Count { get { return presets.Count; } }

        /// <summary>Loads Presets.json; creates it from the starter presets when missing.
        /// A corrupt file is kept as Presets.json.bad and the starters are used.</summary>
        public void Load()
        {
            presets = new List<ShadePreset>();
            if (!File.Exists(filePath) && !File.Exists(filePath + ".tmp"))
            {
                presets = BuiltInPresets.Create();
                Save();
                return;
            }
            PresetFile data = JsonFile.Read<PresetFile>(filePath);
            if (data == null || data.Presets == null)
            {
                // Keep every bad copy: a second bad edit must not overwrite the first backup.
                try
                {
                    string bad = filePath + ".bad";
                    if (File.Exists(bad)) bad = filePath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bad";
                    File.Copy(filePath, bad, false);
                }
                catch { }
                presets = BuiltInPresets.Create();
                Save();
                return;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShadePreset p in data.Presets)
            {
                if (p == null) continue;
                p.Normalize();
                if (seen.Contains(p.Name)) continue;
                seen.Add(p.Name);
                presets.Add(p);
            }
            if (presets.Count == 0)
            {
                presets = BuiltInPresets.Create();
                Save();
            }
            else if (!File.Exists(filePath))
            {
                Save(); // loaded from a leftover *.tmp: promote it to Presets.json
            }
        }

        /// <summary>Last write error ("" after a successful save). Saving never throws.</summary>
        public string LastSaveError { get; private set; }

        public bool Save()
        {
            var data = new PresetFile();
            data.Version = PresetFile.CurrentVersion;
            data.Presets = presets;
            try
            {
                JsonFile.Write(filePath, data);
                LastSaveError = "";
                return true;
            }
            catch (Exception ex)
            {
                LastSaveError = "Presets.json not saved: " + ex.Message;
                return false;
            }
        }

        public ShadePreset Get(string name)
        {
            int i = IndexOf(name);
            return i < 0 ? null : presets[i].Clone();
        }

        public int IndexOf(string name)
        {
            for (int i = 0; i < presets.Count; i++)
                if (string.Equals(presets[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Adds or replaces (by name). Replacing a built-in keeps its BuiltIn flag.</summary>
        public ShadePreset Upsert(ShadePreset preset)
        {
            ShadePreset p = preset.Clone();
            p.Normalize();
            int i = IndexOf(p.Name);
            if (i >= 0)
            {
                p.BuiltIn = presets[i].BuiltIn;
                presets[i] = p;
            }
            else
            {
                p.BuiltIn = BuiltInPresets.Find(p.Name) != null && p.BuiltIn;
                presets.Add(p);
            }
            Save();
            return p.Clone();
        }

        public bool Delete(string name)
        {
            int i = IndexOf(name);
            if (i < 0) return false;
            presets.RemoveAt(i);
            Save();
            return true;
        }

        /// <summary>Re-adds any starter preset that was deleted. Existing presets (user or edited built-in) are not touched.</summary>
        public int RestoreMissingBuiltIns()
        {
            int added = 0;
            foreach (ShadePreset p in BuiltInPresets.Create())
            {
                if (IndexOf(p.Name) >= 0) continue;
                presets.Add(p);
                added++;
            }
            if (added > 0) Save();
            return added;
        }

        /// <summary>Puts one starter preset back to its factory values (adds it if missing).</summary>
        public bool ResetBuiltIn(string name)
        {
            ShadePreset factory = BuiltInPresets.Find(name);
            if (factory == null) return false;
            int i = IndexOf(name);
            if (i >= 0) presets[i] = factory; else presets.Add(factory);
            Save();
            return true;
        }

        /// <summary>Returns a name not yet used: "name", "name (2)", "name (3)"...
        /// The result always survives CleanName unchanged (fix B-05): for a long name the base is
        /// shortened so that base + " (n)" fits in ShadePreset.MaxNameLength chars.</summary>
        public string UniqueName(string name)
        {
            string baseName = ShadePreset.CleanName(name);
            if (IndexOf(baseName) < 0) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = WithSuffix(baseName, " (" + n + ")");
                if (IndexOf(candidate) < 0) return candidate;
            }
            for (int k = 0; k < 100; k++)
            {
                string candidate = WithSuffix(baseName, " " + Guid.NewGuid().ToString("N").Substring(0, 6));
                if (IndexOf(candidate) < 0) return candidate;
            }
            return WithSuffix(baseName, " " + Guid.NewGuid().ToString("N"));
        }

        private static string WithSuffix(string baseName, string suffix)
        {
            string b = ShadePreset.CutName(baseName, ShadePreset.MaxNameLength - suffix.Length);
            string candidate = b + suffix;
            // CleanName trims; a suffix never ends in whitespace, and the base is non-empty after trim.
            return ShadePreset.CleanName(candidate) == candidate ? candidate : ShadePreset.CleanName(candidate);
        }
    }

    /// <summary>Small DataContractJsonSerializer helpers: indented UTF-8, atomic write (tmp + replace).</summary>
    internal static class JsonFile
    {
        public static T Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path))
                {
                    if (!File.Exists(path + ".tmp")) return null;
                    path = path + ".tmp";
                }
                byte[] bytes = File.ReadAllBytes(path);
                int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    return serializer.ReadObject(stream) as T;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Write<T>(string path, T value) { Write(path, value, false); }

        /// <summary>v5.0.2 (crosshair W15, BRUTE P2-1): durable = the temp file is flushed to the disk (Flush(true)) before the
        /// swap, so a power loss or a blue screen right after the write cannot leave a zero-filled or short file.</summary>
        public static void Write<T>(string path, T value, bool durable)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temporary = path + ".tmp";
            using (FileStream stream = File.Create(temporary))
            {
                using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(
                    stream, new UTF8Encoding(false), false, true))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    serializer.WriteObject(writer, value);
                    writer.Flush();
                    if (durable) stream.Flush(true);
                }
            }
            // Atomic swap: the target is never missing. Read() falls back to *.tmp if a crash
            // happened between writing the temp file and the very first Move.
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static string ToJson<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T FromJson<T>(string json) where T : class
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(stream) as T;
            }
        }
    }
}
