using System;
using System.Globalization;

namespace TarkovCrosshair
{
    /// <summary>v5.1 (W33): what the overlay window is doing, for the GPU diagnostic log.</summary>
    internal enum OverlayWinState { None = 0, Hidden = 1, Transparent = 2, Visible = 3 }

    /// <summary>
    /// v5.1 (W33, texture-drop investigation). Pure helpers shared by the render thread (state packing, 开镜/丢雷真隐藏 rule)
    /// and the overlay worker (GPU memory log line). No Win32 here, so the tests run them.
    /// </summary>
    internal static class OverlayDiag
    {
        // packed render-thread state (one volatile int): bits 0-1 window state, 2 DirectComposition, 3 crosshair on,
        // 4 开镜, 5 丢雷, 6 真隐藏 option on
        public static int Pack(OverlayWinState s, bool dcomp, bool enabled, bool ads, bool grenade, bool realHide)
        {
            return ((int)s & 3) | (dcomp ? 4 : 0) | (enabled ? 8 : 0) | (ads ? 16 : 0) | (grenade ? 32 : 0) | (realHide ? 64 : 0);
        }

        public static OverlayWinState State(int packed) { return (OverlayWinState)(packed & 3); }

        public static OverlayWinState StateOf(bool haveWindow, bool shown, bool blankShown)
        {
            if (!haveWindow) return OverlayWinState.None;
            if (!shown) return OverlayWinState.Hidden;
            return blankShown ? OverlayWinState.Transparent : OverlayWinState.Visible;
        }

        /// <summary>开镜/丢雷时真正隐藏 (GlobalSettings.AdsRealHide): the window is SW_HIDE'd for 开镜 / 丢雷 as well, not only made
        /// fully transparent. Only while the crosshair is on (off already hides it).</summary>
        public static bool RealHideNow(bool adsRealHide, bool enabled, bool ads, bool grenade)
        {
            return adsRealHide && enabled && (ads || grenade);
        }

        /// <summary>PDH instance name of "GPU Process Memory" / "GPU Engine": "pid_1234_luid_0x..._phys_0" -> 1234.</summary>
        public static bool ParsePid(string instance, out int pid)
        {
            pid = 0;
            if (instance == null || !instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)) return false;
            long v = 0; int i = 4; int digits = 0;
            while (i < instance.Length && instance[i] >= '0' && instance[i] <= '9' && digits < 10)
            {
                v = v * 10 + (instance[i] - '0'); i++; digits++;
            }
            if (digits == 0 || v > int.MaxValue) return false;
            if (i < instance.Length && instance[i] != '_') return false;
            pid = (int)v;
            return true;
        }

        private static string Mb(long bytes)
        {
            if (bytes < 0) return "?";
            return (bytes / (1024.0 * 1024.0)).ToString("0", CultureInfo.InvariantCulture) + " MB";
        }

        private static string StateWord(OverlayWinState s)
        {
            switch (s)
            {
                case OverlayWinState.Hidden: return "hidden";
                case OverlayWinState.Transparent: return "shown-transparent";
                case OverlayWinState.Visible: return "shown-visible";
                default: return "none";
            }
        }

        /// <summary>One crash.log line per second. fgPid 0 = no foreground window; bytes -1 = not read.</summary>
        public static string Line(int fgPid, bool fgIsGame, long fgDedicated, long fgShared, long allDedicated, long oursDedicated, int packed)
        {
            string fg = fgPid <= 0 ? "no foreground window" : ((fgIsGame ? "game" : "foreground (not the game title)") + " pid " + fgPid
                + " dedicated " + Mb(fgDedicated) + " shared " + Mb(fgShared));
            return "gpu-diag: " + fg + "; all processes dedicated " + Mb(allDedicated) + "; ours dedicated " + Mb(oursDedicated)
                + "; overlay " + StateWord(State(packed)) + " (" + ((packed & 4) != 0 ? "dcomp" : "layered") + ")"
                + ", crosshair " + ((packed & 8) != 0 ? "on" : "off")
                + ((packed & 16) != 0 ? ", ads" : "") + ((packed & 32) != 0 ? ", grenade" : "")
                + ((packed & 64) != 0 ? ", real-hide on" : "");
        }
    }

    /// <summary>
    /// v5.1 (W33): which present path the overlay window uses. Wanted = GlobalSettings.OverlayDCompMode. A failed
    /// DirectComposition setup (or a later present failure) latches back to the layered window until the option is turned off
    /// and on again, so a PC without support does not rebuild the window every frame.
    /// </summary>
    internal sealed class PresentModeChoice
    {
        private bool current;      // the live window uses DirectComposition
        private bool failedLatch;  // DirectComposition failed while wanted

        public bool Current { get { return current; } }
        public bool FailedLatch { get { return failedLatch; } }
        public int Failures;

        /// <summary>What the next window creation should try.</summary>
        public bool TryDComp(bool wanted) { return wanted && !failedLatch; }

        /// <summary>true = destroy the window and create it again with TryDComp(wanted).</summary>
        public bool NeedRecreate(bool wanted)
        {
            if (!wanted) failedLatch = false;
            return current != TryDComp(wanted);
        }

        public void Created(bool triedDComp, bool dcompOk)
        {
            current = triedDComp && dcompOk;
            if (triedDComp && !dcompOk) { failedLatch = true; Failures++; }
        }

        /// <summary>A DirectComposition present failed on the live window: NeedRecreate is true from now on.</summary>
        public void RuntimeFailed() { if (current) { failedLatch = true; Failures++; } }
    }
}

namespace TarkovCrosshair
{
    /// <summary>v5.1 (W33): the three W33 options (GlobalSettings.OverlayDCompMode / AdsRealHide / GpuDiagLog, owner W23), read
    /// in one place (null settings = off).</summary>
    internal static class W33Opt
    {
        public static bool DComp(GlobalSettings g) { return g != null && g.OverlayDCompMode; }
        public static bool RealHide(GlobalSettings g) { return g != null && g.AdsRealHide; }
        public static bool GpuLog(GlobalSettings g) { return g != null && g.GpuDiagLog; }
    }
}
