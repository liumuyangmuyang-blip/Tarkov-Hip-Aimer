// TarkovShade - configurable hotkeys (Leo, 17 Nov 2026). Pure logic; Win32 polling is in Win32Gamma.cs.
// C# 5; non-ASCII text as \u escapes.
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TarkovShade
{
    /// <summary>One key chord: a virtual-key code plus ModAlt 1 | ModCtrl 2 | ModShift 4. Key 0 = unassigned.</summary>
    [DataContract]
    public sealed class HotkeyBinding
    {
        public const int ModAlt = 1, ModCtrl = 2, ModShift = 4;

        [DataMember(Order = 0)] public int Key { get; set; }
        [DataMember(Order = 1)] public int Modifiers { get; set; }

        public HotkeyBinding() { }
        public HotkeyBinding(int key, int modifiers) { Key = key; Modifiers = modifiers & 7; }

        public static HotkeyBinding None { get { return new HotkeyBinding(0, 0); } }
        public bool IsAssigned { get { return Key > 0; } }

        public HotkeyBinding Clone() { return new HotkeyBinding(Key, Modifiers); }

        public bool SameAs(HotkeyBinding o)
        {
            return o != null && IsAssigned && o.IsAssigned && Key == o.Key && (Modifiers & 7) == (o.Modifiers & 7);
        }

        public bool Matches(int vk, int modifiers)
        {
            return IsAssigned && Key == vk && (Modifiers & 7) == (modifiers & 7);
        }

        public static bool IsFunctionKey(int vk) { return vk >= 0x70 && vk <= 0x87; } // F1..F24
        public static bool IsModifierKey(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
        }

        /// <summary>
        /// Null when the chord is allowed (or unassigned); else a plain English reason. Allowed: F1..F24 alone or
        /// with modifiers, or any other non-modifier key WITH Ctrl/Alt/Shift. Never a plain letter, digit,
        /// Space, Enter, Esc or Tab (they would fire while typing or playing).
        /// </summary>
        public static string Check(HotkeyBinding b)
        {
            if (b == null || !b.IsAssigned) return null;
            if (b.Key > 255) return "Unknown key.";
            if (IsModifierKey(b.Key)) return "Ctrl, Alt or Shift alone cannot be a hotkey.";
            if ((b.Modifiers & ~7) != 0) return "Unknown modifier.";
            if (IsFunctionKey(b.Key)) return null;
            if ((b.Modifiers & 7) == 0)
                return "Use an F-key (F1-F24) or a combination with Ctrl, Alt or Shift; plain letters, digits, Space and Enter are not allowed.";
            return null;
        }

        public override string ToString()
        {
            if (!IsAssigned) return "";
            string s = "";
            if ((Modifiers & ModCtrl) != 0) s += "Ctrl+";
            if ((Modifiers & ModAlt) != 0) s += "Alt+";
            if ((Modifiers & ModShift) != 0) s += "Shift+";
            return s + KeyName(Key);
        }

        public static string KeyName(int vk)
        {
            if (IsFunctionKey(vk)) return "F" + (vk - 0x6F);
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
            switch (vk)
            {
                case 0x20: return "Space";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x09: return "Tab";
                case 0x08: return "Backspace";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x13: return "Pause";
                case 0x91: return "ScrollLock";
            }
            return "Key" + vk;
        }
    }

    [DataContract]
    public sealed class PresetHotkey
    {
        [DataMember(Order = 0)] public string PresetName { get; set; }
        [DataMember(Order = 1)] public HotkeyBinding Key { get; set; }

        public PresetHotkey() { }
        public PresetHotkey(string presetName, HotkeyBinding key) { PresetName = presetName; Key = key; }
        public PresetHotkey Clone() { return new PresetHotkey(PresetName, Key == null ? HotkeyBinding.None : Key.Clone()); }
    }

    /// <summary>All hotkeys (one Hotkeys page). Persisted in shade-settings.json.</summary>
    [DataContract]
    public sealed class HotkeySettings
    {
        public const int VkF7 = 0x76, VkF8 = 0x77, VkF9 = 0x78, VkF12 = 0x7B, Vk1 = 0x31, Vk2 = 0x32;

        [DataMember(Order = 0)] public HotkeyBinding Toggle { get; set; }
        [DataMember(Order = 1)] public HotkeyBinding NextPreset { get; set; }
        [DataMember(Order = 2)] public HotkeyBinding HoldOriginal { get; set; }
        [DataMember(Order = 3)] public HotkeyBinding RestoreAll { get; set; }
        [DataMember(Order = 4)] public List<PresetHotkey> Presets { get; set; }

        public static HotkeySettings CreateDefault()
        {
            HotkeySettings h = new HotkeySettings();
            h.Toggle = new HotkeyBinding(VkF8, 0);
            h.NextPreset = new HotkeyBinding(VkF9, 0);
            h.HoldOriginal = new HotkeyBinding(VkF7, 0);
            h.RestoreAll = new HotkeyBinding(VkF12, HotkeyBinding.ModCtrl);
            h.Presets = new List<PresetHotkey>();
            h.Presets.Add(new PresetHotkey(PilotBuiltIns.GeneralName, new HotkeyBinding(Vk1, HotkeyBinding.ModCtrl)));
            h.Presets.Add(new PresetHotkey(PilotBuiltIns.NightName, new HotkeyBinding(Vk2, HotkeyBinding.ModCtrl)));
            return h;
        }

        public HotkeySettings Clone()
        {
            HotkeySettings h = new HotkeySettings();
            h.Toggle = C(Toggle); h.NextPreset = C(NextPreset); h.HoldOriginal = C(HoldOriginal); h.RestoreAll = C(RestoreAll);
            h.Presets = new List<PresetHotkey>();
            if (Presets != null) foreach (PresetHotkey p in Presets) if (p != null) h.Presets.Add(p.Clone());
            return h;
        }

        private static HotkeyBinding C(HotkeyBinding b) { return b == null ? HotkeyBinding.None : b.Clone(); }

        /// <summary>Fills nulls, drops unnamed / unassigned preset entries and duplicate names (first wins).</summary>
        public void Normalize()
        {
            if (Toggle == null) Toggle = HotkeyBinding.None;
            if (NextPreset == null) NextPreset = HotkeyBinding.None;
            if (HoldOriginal == null) HoldOriginal = HotkeyBinding.None;
            if (RestoreAll == null) RestoreAll = HotkeyBinding.None;
            List<PresetHotkey> clean = new List<PresetHotkey>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (Presets != null)
                foreach (PresetHotkey p in Presets)
                    if (p != null && !string.IsNullOrEmpty(p.PresetName) && p.Key != null && p.Key.IsAssigned && seen.Add(p.PresetName))
                        clean.Add(p.Clone());
            Presets = clean;
        }

        public HotkeyBinding GetPresetKey(string presetName)
        {
            if (Presets != null)
                foreach (PresetHotkey p in Presets)
                    if (p != null && string.Equals(p.PresetName, presetName, StringComparison.Ordinal) && p.Key != null)
                        return p.Key.Clone();
            return HotkeyBinding.None;
        }

        /// <summary>Assigns (or with None / null clears) the direct hotkey of one preset.</summary>
        public void SetPresetKey(string presetName, HotkeyBinding key)
        {
            if (Presets == null) Presets = new List<PresetHotkey>();
            Presets.RemoveAll(delegate(PresetHotkey p) { return p == null || string.Equals(p.PresetName, presetName, StringComparison.Ordinal); });
            if (key != null && key.IsAssigned && !string.IsNullOrEmpty(presetName))
                Presets.Add(new PresetHotkey(presetName, key.Clone()));
        }

        /// <summary>All assigned bindings with a display label, in page order.</summary>
        public List<KeyValuePair<string, HotkeyBinding>> Labelled()
        {
            List<KeyValuePair<string, HotkeyBinding>> list = new List<KeyValuePair<string, HotkeyBinding>>();
            list.Add(new KeyValuePair<string, HotkeyBinding>("Toggle filter", Toggle));
            list.Add(new KeyValuePair<string, HotkeyBinding>("Next preset", NextPreset));
            list.Add(new KeyValuePair<string, HotkeyBinding>("Hold to see original", HoldOriginal));
            list.Add(new KeyValuePair<string, HotkeyBinding>("Restore everything", RestoreAll));
            if (Presets != null)
                foreach (PresetHotkey p in Presets)
                    if (p != null) list.Add(new KeyValuePair<string, HotkeyBinding>("Preset " + p.PresetName, p.Key));
            return list;
        }

        /// <summary>Null when every binding follows the rules and none is used twice; else the first problem.</summary>
        public string Validate()
        {
            List<KeyValuePair<string, HotkeyBinding>> all = Labelled();
            for (int i = 0; i < all.Count; i++)
            {
                string problem = HotkeyBinding.Check(all[i].Value);
                if (problem != null) return all[i].Key + " (" + all[i].Value + "): " + problem;
            }
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    if (all[i].Value != null && all[i].Value.SameAs(all[j].Value))
                        return all[i].Value + " is used twice: " + all[i].Key + " and " + all[j].Key + ".";
            return null;
        }
    }
}
