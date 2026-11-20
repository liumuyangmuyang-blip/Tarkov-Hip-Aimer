using System;
using System.Collections.Generic;
using System.IO;

// v5.1 (W25): boost = EXACTLY the game's 切换战术设备模式 key (Ctrl+T); Ctrl+Shift+T / Ctrl+Alt+T / T alone do not count.
// Also: no 「起点」 wording in the 滤镜 preset tip (brief: no disclaimers).
namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W25Init()
        {
            KeyBindings k = GlobalSettings.CreateDefault().Keys;
            // v5.1.1 (X5, Leo 21 Nov 2026): the default is plain 鼠标侧键2 now; the exact-combo rule is checked on a Ctrl+T binding
            bool defSide = k.Boost == KeyBindings.VkXButton2 && k.BoostMods == 0;
            k.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkT, KeyBindings.ModCtrl);
            int vk = k.Get(KeyBindings.ActionBoost);
            Func<HashSet<int>, Func<int, bool>> held = delegate(HashSet<int> s) { return delegate(int v) { return s.Contains(v); }; };
            bool ctrlT = MotionEngine.ReadInput(k, held(new HashSet<int> { vk, MotionEngine.VkControl })).Boost;
            bool ctrlShiftT = MotionEngine.ReadInput(k, held(new HashSet<int> { vk, MotionEngine.VkControl, MotionEngine.VkShift })).Boost;
            bool ctrlAltT = MotionEngine.ReadInput(k, held(new HashSet<int> { vk, MotionEngine.VkControl, MotionEngine.VkMenu })).Boost;
            bool tOnly = MotionEngine.ReadInput(k, held(new HashSet<int> { vk })).Boost;
            // a plain-key binding (old files: XButton2, mods 0) still counts with any modifier held, as before
            KeyBindings p = GlobalSettings.CreateDefault().Keys;
            p.Boost = KeyBindings.VkXButton2; p.BoostMods = 0;
            bool plain = MotionEngine.ReadInput(p, held(new HashSet<int> { KeyBindings.VkXButton2, MotionEngine.VkShift })).Boost;
            Check("V51-W25-1 boost key = exactly " + k.ComboOf(KeyBindings.ActionBoost) + " (Ctrl+Shift+T / Ctrl+Alt+T / T alone do not toggle boost); plain-key bindings unchanged",
                defSide && ctrlT && !ctrlShiftT && !ctrlAltT && !tOnly && plain,
                "ctrlT=" + ctrlT + " ctrlShiftT=" + ctrlShiftT + " ctrlAltT=" + ctrlAltT + " T=" + tOnly + " plain+shift=" + plain);
            string pf = File.ReadAllText("/home/dev/tarkov-crosshair-v4/src/SettingsForm.PageFilter.cs");
            Check("V51-W25-2 滤镜 preset tip has no 「起点」", pf.IndexOf("起点", StringComparison.Ordinal) < 0, "");
        }
    }
}
