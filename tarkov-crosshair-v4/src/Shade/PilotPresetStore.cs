// TarkovShade - pilot-presets.json: the PilotStyle preset list (built-ins 通用 + 黑夜模式 first).
// C# 5; non-ASCII text as \u escapes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace TarkovShade
{
    [DataContract]
    public sealed class PilotPresetFile
    {
        public const int CurrentVersion = 2;
        [DataMember(Order = 0)] public int Version { get; set; }
        [DataMember(Order = 1)] public List<PilotStyle> Presets { get; set; }
    }

    /// <summary>Preset list stored next to the exe. Saving never throws (LastSaveError).</summary>
    public sealed class PilotPresetStore
    {
        public const string FileName = "pilot-presets.json";
        public const int MaxNameLength = 40;
        private readonly string filePath;
        private List<PilotStyle> presets = new List<PilotStyle>();

        public PilotPresetStore(string folder) { filePath = Path.Combine(folder, FileName); }

        public string FilePath { get { return filePath; } }
        public string LastSaveError { get; private set; }
        public int Count { get { return presets.Count; } }

        public List<PilotStyle> All
        {
            get
            {
                List<PilotStyle> copy = new List<PilotStyle>();
                foreach (PilotStyle p in presets) copy.Add(p.Clone());
                return copy;
            }
        }

        public static string CleanName(string name) { return ShadePreset.CleanName(name); }

        /// <summary>Forces the fields TarkovShade never exposes: Tint 0, Exposure 0, strength 标准, backend driver/Windows,
        /// values clamped to the 标准 slider ranges, hardware values in range.</summary>
        public static void EnforceFixed(PilotStyle s)
        {
            if (s == null) return;
            s.Normalize();
            s.Tint = 0;
            s.Exposure = 0;
            s.StrengthMode = PilotViews.FixedStrengthMode;
            s.ClampToStrength(PilotViews.FixedStrengthMode);
            if (s.SaturationBackend != PilotStyle.SaturationBackendSoftware) s.SaturationBackend = PilotStyle.SaturationBackendDriver;
            s.Name = CleanName(s.Name);
        }

        public void Load()
        {
            presets = new List<PilotStyle>();
            if (!File.Exists(filePath) && !File.Exists(filePath + ".tmp"))
            {
                presets = PilotBuiltIns.Create();
                Save();
                return;
            }
            PilotPresetFile data = JsonFile.Read<PilotPresetFile>(filePath);
            if (data == null || data.Presets == null)
            {
                try
                {
                    string bad = filePath + ".bad";
                    if (File.Exists(bad)) bad = filePath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bad";
                    if (File.Exists(filePath)) File.Copy(filePath, bad, false);
                }
                catch { }
                presets = PilotBuiltIns.Create();
                Save();
                return;
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int dropped = 0;
            foreach (PilotStyle p in data.Presets)
            {
                if (p == null) continue;
                // v5.1 round 2: built-ins an early v5.1 build wrote (白天 / 黄昏 / 夜图 / 室内 / 实验室) are gone; user presets stay
                if (p.BuiltIn && PilotBuiltIns.IsRetiredV51(PilotPresetStore.CleanName(p.Name))) { dropped++; continue; }
                EnforceFixed(p);
                if (!seen.Add(p.Name)) continue;
                p.BuiltIn = PilotBuiltIns.Find(p.Name) != null && p.BuiltIn;
                if (p.BuiltIn) p.Untested = PilotBuiltIns.Find(p.Name).Untested;
                presets.Add(p);
            }
            int added = RestoreMissingBuiltIns(false);
            if (added > 0 || dropped > 0 || !File.Exists(filePath)) Save();
        }

        public bool Save()
        {
            PilotPresetFile data = new PilotPresetFile();
            data.Version = PilotPresetFile.CurrentVersion;
            data.Presets = presets;
            try
            {
                JsonFile.Write(filePath, data);
                LastSaveError = "";
                return true;
            }
            catch (Exception ex)
            {
                LastSaveError = FileName + " not saved: " + ex.Message;
                return false;
            }
        }

        public int IndexOf(string name)
        {
            for (int i = 0; i < presets.Count; i++)
                if (string.Equals(presets[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        public PilotStyle Get(string name)
        {
            int i = IndexOf(name);
            return i < 0 ? null : presets[i].Clone();
        }

        public bool IsBuiltIn(string name)
        {
            int i = IndexOf(name);
            return i >= 0 && presets[i].BuiltIn;
        }

        /// <summary>Adds or replaces by name. Replacing a built-in keeps BuiltIn/Untested flags.</summary>
        public PilotStyle Upsert(PilotStyle style)
        {
            PilotStyle p = style.Clone();
            EnforceFixed(p);
            int i = IndexOf(p.Name);
            if (i >= 0)
            {
                p.BuiltIn = presets[i].BuiltIn;
                p.Untested = presets[i].Untested;
                presets[i] = p;
            }
            else
            {
                p.BuiltIn = false;
                p.Untested = false;
                presets.Add(p);
            }
            Save();
            return p.Clone();
        }

        public bool Delete(string name)
        {
            int i = IndexOf(name);
            if (i < 0 || presets[i].BuiltIn) return false;
            presets.RemoveAt(i);
            Save();
            return true;
        }

        public bool Rename(string oldName, string newName)
        {
            int i = IndexOf(oldName);
            string n = CleanName(newName);
            if (i < 0 || presets[i].BuiltIn || IndexOf(n) >= 0) return false;
            presets[i].Name = n;
            Save();
            return true;
        }

        public int RestoreMissingBuiltIns() { return RestoreMissingBuiltIns(true); }

        private int RestoreMissingBuiltIns(bool save)
        {
            int added = 0;
            List<PilotStyle> factory = PilotBuiltIns.Create();
            for (int k = 0; k < factory.Count; k++)
            {
                if (IndexOf(factory[k].Name) >= 0) continue;
                presets.Insert(Math.Min(k, presets.Count), factory[k]);
                added++;
            }
            if (added > 0 && save) Save();
            return added;
        }

        public bool ResetBuiltIn(string name)
        {
            PilotStyle factory = PilotBuiltIns.Find(name);
            if (factory == null) return false;
            int i = IndexOf(name);
            if (i >= 0) presets[i] = factory; else presets.Add(factory);
            Save();
            return true;
        }

        public string UniqueName(string name)
        {
            string baseName = CleanName(name);
            if (IndexOf(baseName) < 0) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string suffix = " (" + n + ")";
                string candidate = CleanName(ShadePreset.CutName(baseName, MaxNameLength - suffix.Length) + suffix);
                if (IndexOf(candidate) < 0) return candidate;
            }
            return CleanName(ShadePreset.CutName(baseName, 30) + " " + Guid.NewGuid().ToString("N").Substring(0, 6));
        }
    }
}
