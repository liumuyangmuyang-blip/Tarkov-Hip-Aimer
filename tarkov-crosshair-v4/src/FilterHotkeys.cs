using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

// v4.2 (W2) 滤镜 hotkeys + the crosshair-side filter preferences (pure: no WinForms, no P/Invoke; unit-tested).
// Combos use the crosshair's encoding (KeyBindings.ModCtrl 1 / ModShift 2 / ModAlt 4).
// Matching is EXACT on modifiers (like the module's HotkeyPoller): F8 does not fire while Ctrl is held.
// The crosshair's own keys match "key + at least these modifiers" (MotionEngine.IsComboDown): Ctrl+F12 also presses the
// crosshair's plain F12 toggle. Rule (main chat, 17 Nov): a filter key may never use the BASE key of any crosshair key,
// whatever the modifiers, and the crosshair 按键 page refuses a key whose base key a filter key uses (both directions).

namespace TarkovCrosshair
{
    internal static class FilterKeys
    {
        public const int ActionToggle = 0, ActionNext = 1, ActionHold = 2;
        /// <summary>v4.3 (Leo 09:5x UTC): only these three. The 通用 / 黑夜模式 / 全部恢复原样 hotkeys are gone; entries 3-5 of an
        /// older filter-prefs.json are ignored (Normalize keeps the first three).</summary>
        public const int ActionCount = 3;

        /// <summary>Labels on the 滤镜 page (Chinese, key names stay English).</summary>
        public static readonly string[] ActionNames = { "开关滤镜", "下一个预设", "按住看原画面" };

        /// <summary>PilotHelpText keys for each action's tooltip.</summary>
        public static readonly string[] HelpKeys = { "HotkeyToggle", "HotkeyNextPreset", "HotkeyHoldOriginal" };

        public const int VkF6 = 0x75, VkF7 = 0x76, VkF8 = 0x77, VkF9 = 0x78, VkF10 = 0x79, VkF12 = 0x7B, Vk1 = 0x31, Vk2 = 0x32;

        /// <summary>
        /// Defaults (LEO-DECISIONS.md: toggle F8, next F9, hold F7, 通用 Ctrl+1, 黑夜模式 Ctrl+2, restore Ctrl+F12).
        /// Exception (main chat, 17 Nov): restore is Ctrl+F6, because Ctrl+F12 also presses the crosshair's default 开关准星 F12
        /// (the crosshair ignores extra modifiers). User-changeable.
        /// </summary>
        public static readonly int[] DefaultVk = { VkF8, VkF9, VkF7 };
        public static readonly int[] DefaultMods = { 0, 0, 0 };

        public static bool IsFKey(int vk) { return vk >= 0x70 && vk <= 0x87; }

        public static bool IsMouse(int vk) { return vk >= 1 && vk <= 6 && vk != 3; }

        /// <summary>
        /// LEO-DECISIONS rule: F-keys (any modifiers) or a combo with Ctrl / Shift / Alt; never a plain letter, digit, space or
        /// Enter; keyboard only; a modifier alone is not a key. vk 0 = 未设置 (allowed). Returns "" when OK, else Chinese reason.
        /// </summary>
        public static string Validate(int vk, int mods)
        {
            if (vk == 0) return "";
            if (vk < 0 || vk > 254 || IsMouse(vk)) return "滤镜快捷键只能用键盘按键";
            if (KeyBindings.IsModifierVk(vk) || vk == 0x5B || vk == 0x5C) return "Ctrl / Shift / Alt / Win 不能单独当快捷键";
            if (vk == 0x1B) return "Esc 不能当滤镜快捷键";
            if (IsFKey(vk)) return "";
            if ((mods & KeyBindings.ModMask) == 0) return "只能用 F1–F24，或者带 Ctrl / Shift / Alt 的组合键";
            return "";
        }

        /// <summary>Same base key = clash, whatever the modifiers on either side (main chat rule, 17 Nov). Strictly only
        /// "crosshair modifiers within the filter's" would press both, but one key for both programs is refused outright.</summary>
        public static bool ClashesWithCrosshair(int vk, int mods, int xVk, int xMods)
        {
            return vk > 0 && xVk > 0 && vk == xVk;
        }

        /// <summary>The 按键 page side: the filter action whose base key equals this crosshair key, or -1.</summary>
        public static int FilterUsing(FilterPrefs p, int xVk)
        {
            if (p == null || xVk <= 0) return -1;
            for (int a = 0; a < ActionCount; a++) if (p.Vk[a] == xVk) return a;
            return -1;
        }

        /// <summary>The crosshair action whose key clashes with this filter combo, or -1.</summary>
        public static int CrosshairClash(KeyBindings k, int vk, int mods)
        {
            if (k == null) return -1;
            for (int a = 0; a < KeyBindings.ActionCount; a++)
                if (ClashesWithCrosshair(vk, mods, k.Get(a), k.GetMods(a))) return a;
            return -1;
        }

        /// <summary>Another filter action already on exactly this combo, or -1.</summary>
        public static int FilterClash(FilterPrefs p, int action, int vk, int mods)
        {
            if (p == null || vk <= 0) return -1;
            for (int a = 0; a < ActionCount; a++)
                if (a != action && p.Vk[a] == vk && (p.Mods[a] & KeyBindings.ModMask) == (mods & KeyBindings.ModMask)) return a;
            return -1;
        }

        public static string ComboText(int vk, int mods) { return vk <= 0 ? "未设置" : KeyBindings.ComboName(vk, mods); }

        /// <summary>Exact-modifier chord test: key down, and Ctrl / Shift / Alt down exactly as in mods.</summary>
        public static bool IsDown(int vk, int mods, Func<int, bool> isDown)
        {
            if (vk <= 0 || vk > 254 || isDown == null || !isDown(vk)) return false;
            if (((mods & KeyBindings.ModCtrl) != 0) != isDown(0x11)) return false;
            if (((mods & KeyBindings.ModShift) != 0) != isDown(0x10)) return false;
            if (((mods & KeyBindings.ModAlt) != 0) != isDown(0x12)) return false;
            return true;
        }

        /// <summary>The module's English import error -> one Chinese line.</summary>
        public static string ImportReason(string err)
        {
            string e = err ?? "";
            if (e.IndexOf("TSHADE1", StringComparison.OrdinalIgnoreCase) >= 0) return "这是旧测试版的分享码（TSHADE1），不能导入，请对方用新版重新导出";
            if (e.IndexOf("ShadePilot", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("SP3", StringComparison.Ordinal) >= 0)
                return "不支持 ShadePilot 的分享码，只能导入本程序导出的分享码（TSHADE2 开头）";
            return "分享码不对或不完整，请整段复制后再粘贴（TSHADE2 开头）";
        }

        /// <summary>Module ShadeSettings modifier bits (ShadeSettings.ModAlt / ModCtrl / ModShift).</summary>
        public const int ShadeAlt = 1, ShadeCtrl = 2, ShadeShift = 4;

        /// <summary>Crosshair mods (Ctrl 1, Shift 2, Alt 4) -> module ShadeSettings mods (Alt 1, Ctrl 2, Shift 4).</summary>
        public static int ToShadeMods(int mods)
        {
            int r = 0;
            if ((mods & KeyBindings.ModAlt) != 0) r |= ShadeAlt;
            if ((mods & KeyBindings.ModCtrl) != 0) r |= ShadeCtrl;
            if ((mods & KeyBindings.ModShift) != 0) r |= ShadeShift;
            return r;
        }

        /// <summary>Module ShadeSettings mods (Alt 1, Ctrl 2, Shift 4) -> crosshair mods (Ctrl 1, Shift 2, Alt 4).</summary>
        public static int FromShadeMods(int shadeMods)
        {
            int r = 0;
            if ((shadeMods & ShadeAlt) != 0) r |= KeyBindings.ModAlt;
            if ((shadeMods & ShadeCtrl) != 0) r |= KeyBindings.ModCtrl;
            if ((shadeMods & ShadeShift) != 0) r |= KeyBindings.ModShift;
            return r;
        }
    }

    /// <summary>Edge detector for the filter hotkeys (pure; the App polls it every 30 ms with GetAsyncKeyState).</summary>
    internal sealed class FilterKeyPoller
    {
        private readonly bool[] was = new bool[FilterKeys.ActionCount];
        private bool primed;

        public event Action<int> Pressed;      // action, on key down (not the hold action)
        public event Action<bool> HoldChanged; // hold-to-see-original: true on down, false on up

        /// <summary>One poll. The first poll only records the state (a key already held at start does not fire).</summary>
        public void Poll(FilterPrefs p, Func<int, bool> isDown)
        {
            if (p == null) return;
            for (int a = 0; a < FilterKeys.ActionCount; a++)
            {
                bool d = FilterKeys.IsDown(p.Vk[a], p.Mods[a], isDown);
                if (primed && d != was[a])
                {
                    if (a == FilterKeys.ActionHold)
                    {
                        Action<bool> h = HoldChanged;
                        if (h != null) h(d);
                    }
                    else if (d)
                    {
                        Action<int> h = Pressed;
                        if (h != null) h(a);
                    }
                }
                was[a] = d;
            }
            primed = true;
        }

        /// <summary>Forget the key state (e.g. after the keys changed), so nothing fires on the next poll.</summary>
        public void Reset() { primed = false; }

        public bool Holding { get { return was[FilterKeys.ActionHold]; } }
    }

    /// <summary>
    /// v5.1 (W24, Leo decision 10: filter idle = zero wake-ups). The overlay's render thread already reads the keyboard every
    /// frame (GetAsyncKeyState through its KeySnapshot); it calls FrameAll once per frame and this feed reads the 3 filter combos
    /// from that same snapshot. Only a press / release (an edge) calls Edge, which FilterHost marshals to the UI thread, so the
    /// filter has no timer of its own and the UI thread wakes only when a filter key changes. Never throws; no locks
    /// (the combo array is swapped whole). Pure: tested with a fake isDown.
    /// </summary>
    internal sealed class FilterKeyFeed
    {
        private static volatile FilterKeyFeed current;
        /// <summary>The feed the render thread reads (FilterHost sets it once its keys start; null = nothing to read).</summary>
        public static FilterKeyFeed Current { get { return current; } set { current = value; } }

        private volatile int[] combos;   // vk, mods per action; null = no filter key assigned
        private volatile bool reprime = true;
        private readonly bool[] was = new bool[FilterKeys.ActionCount];
        private long frames;
        /// <summary>Called on the render thread on an edge: (action, down). Hold reports both edges, the others only the press.</summary>
        public Action<int, bool> Edge;

        /// <summary>UI thread: the keys changed (or start). The next frame only records the state (a held key does not fire).</summary>
        public void Configure(FilterPrefs p)
        {
            int[] c = null;
            if (p != null && p.Vk != null && p.Mods != null)
            {
                bool any = false;
                c = new int[FilterKeys.ActionCount * 2];
                for (int a = 0; a < FilterKeys.ActionCount; a++)
                {
                    c[2 * a] = a < p.Vk.Length ? p.Vk[a] : 0;
                    c[2 * a + 1] = a < p.Mods.Length ? p.Mods[a] : 0;
                    if (c[2 * a] > 0) any = true;
                }
                if (!any) c = null;
            }
            combos = c;
            reprime = true;
        }

        /// <summary>Frames seen so far (FilterHost: the overlay is feeding, so its own fallback poll can stop).</summary>
        public long Frames { get { return System.Threading.Interlocked.Read(ref frames); } }

        /// <summary>One frame (render thread). isDown = the overlay's per-frame key snapshot.</summary>
        public void Frame(Func<int, bool> isDown)
        {
            System.Threading.Interlocked.Increment(ref frames);
            int[] c = combos;
            if (c == null || isDown == null) return;
            bool prime = reprime;
            if (prime) reprime = false;
            for (int a = 0; a < FilterKeys.ActionCount; a++)
            {
                bool d = FilterKeys.IsDown(c[2 * a], c[2 * a + 1], isDown);
                if (!prime && d != was[a] && (d || a == FilterKeys.ActionHold))
                {
                    Action<int, bool> h = Edge;
                    if (h != null) h(a, d);
                }
                was[a] = d;
            }
        }

        /// <summary>The overlay render thread, once per frame, right after its key snapshot moved to the new frame.</summary>
        public static void FrameAll(Func<int, bool> isDown)
        {
            try
            {
                FilterKeyFeed f = current;
                if (f != null) f.Frame(isDown);
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Crosshair-side filter preferences, filter-prefs.json next to settings.json (the module keeps its own shade-settings.json
    /// / Presets.json in the same folder). Never throws on file errors.
    /// </summary>
    [DataContract]
    internal sealed class FilterPrefs
    {
        public const string FileName = "filter-prefs.json";

        [DataMember(Order = 0)] public int Version = 1;
        /// <summary>Filter on when the app last closed. false on first run: the filter starts off (brief B).</summary>
        [DataMember(Order = 1)] public bool WasOn;
        [DataMember(Order = 2)] public int[] Vk;
        [DataMember(Order = 3)] public int[] Mods;
        /// <summary>Hotkeys work only while the game window is in front (same rule as the crosshair's 只在塔科夫前台时).</summary>
        [DataMember(Order = 4)] public bool KeysOnlyInGame;

        public FilterPrefs() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        public void SetDefaults()
        {
            Version = 1;
            WasOn = false;
            KeysOnlyInGame = false;
            ResetKeys();
        }

        public void ResetKeys()
        {
            Vk = (int[])FilterKeys.DefaultVk.Clone();
            Mods = (int[])FilterKeys.DefaultMods.Clone();
        }

        /// <summary>Fixes array lengths and drops any key that breaks the rules or repeats another filter key (-> 未设置).</summary>
        public void Normalize()
        {
            int[] v = new int[FilterKeys.ActionCount], m = new int[FilterKeys.ActionCount];
            for (int a = 0; a < FilterKeys.ActionCount; a++)
            {
                v[a] = Vk != null && a < Vk.Length ? Vk[a] : FilterKeys.DefaultVk[a];
                m[a] = Mods != null && a < Mods.Length ? Mods[a] & KeyBindings.ModMask : FilterKeys.DefaultMods[a];
                if (v[a] != 0 && FilterKeys.Validate(v[a], m[a]).Length > 0) { v[a] = 0; m[a] = 0; }
                if (v[a] == 0) m[a] = 0;
            }
            for (int a = 0; a < FilterKeys.ActionCount; a++)
                for (int b = 0; b < a; b++)
                    if (v[a] != 0 && v[a] == v[b] && m[a] == m[b]) { v[a] = 0; m[a] = 0; }
            Vk = v; Mods = m;
            Version = 1;
        }

        public FilterPrefs Clone()
        {
            FilterPrefs c = (FilterPrefs)MemberwiseClone();
            c.Vk = (int[])Vk.Clone();
            c.Mods = (int[])Mods.Clone();
            return c;
        }

        public static string PathIn(string folder) { return Path.Combine(folder ?? "", FileName); }

        public static string ToJson(FilterPrefs p)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(FilterPrefs));
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, p);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Parse; null on any error.</summary>
        public static FilterPrefs FromJson(string json)
        {
            try
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(FilterPrefs));
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes((json ?? "").TrimStart('\uFEFF'))))
                {
                    FilterPrefs p = ser.ReadObject(ms) as FilterPrefs;
                    if (p == null) return null;
                    p.Normalize();
                    return p;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Missing or unreadable file: defaults (filter off).</summary>
        public static FilterPrefs Load(string folder)
        {
            try
            {
                string path = PathIn(folder);
                if (File.Exists(path))
                {
                    FilterPrefs p = FromJson(File.ReadAllText(path, Encoding.UTF8));
                    if (p != null) return p;
                }
            }
            catch (Exception) { }
            FilterPrefs d = new FilterPrefs();
            d.Normalize();
            return d;
        }

        /// <summary>Write via *.tmp then replace. Returns "" or the error text.</summary>
        public static string Save(FilterPrefs p, string folder)
        {
            try
            {
                string path = PathIn(folder), tmp = path + ".tmp";
                File.WriteAllText(tmp, ToJson(p), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
