using System;
using System.Runtime.InteropServices;
using System.Threading;
using TarkovShade;

// v4.3 (W2): gdi32 gamma backend with ShadePilot's write behaviour (DisplayPreset.cs SetRamp, commit 2bb77f6): up to 3
// SetDeviceGammaRamp attempts on a fresh CreateDC("DISPLAY", device) with 120 ms / 260 ms pauses, and the Win32 error number
// in the message. Runs only on FilterCore's worker thread (the pauses never reach the UI thread). Windows only.

namespace TarkovCrosshair
{
    internal sealed class PilotWin32Gamma : IGammaBackend
    {
        public bool TryGet(string deviceName, out GammaRamp ramp, out string error)
        {
            ramp = GammaRamp.CreateEmpty();
            error = "";
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1) Thread.Sleep(attempt == 2 ? 80 : 180);
                IntPtr dc = Open(deviceName);
                if (dc == IntPtr.Zero) { error = "CreateDC(" + deviceName + ") failed, Win32 " + Marshal.GetLastWin32Error() + "."; continue; }
                try
                {
                    GammaRamp v = GammaRamp.CreateEmpty();
                    if (GetDeviceGammaRamp(dc, ref v)) { ramp = v; return true; }
                    error = "GetDeviceGammaRamp(" + deviceName + ") failed, Win32 " + Marshal.GetLastWin32Error() + ".";
                }
                finally { DeleteDC(dc); }
            }
            return false;
        }

        public bool TrySet(string deviceName, GammaRamp ramp, out string error)
        {
            error = "";
            int last = 0;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1) Thread.Sleep(attempt == 2 ? 120 : 260);
                IntPtr dc = Open(deviceName);
                if (dc == IntPtr.Zero) { last = Marshal.GetLastWin32Error(); continue; }
                try
                {
                    GammaRamp copy = ramp.Clone();
                    if (SetDeviceGammaRamp(dc, ref copy)) return true;
                    last = Marshal.GetLastWin32Error();
                }
                finally { DeleteDC(dc); }
            }
            error = "SetDeviceGammaRamp refused 3 times on " + deviceName + ", Win32 " + last + ".";
            return false;
        }

        private static IntPtr Open(string device)
        {
            IntPtr dc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) dc = CreateDC(device, null, null, IntPtr.Zero);
            return dc;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool GetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool SetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
    }
}


namespace TarkovCrosshair
{
    /// <summary>
    /// v4.3 (V43-PERF TOP 0 fix 4): is HDR ("advanced color") on for a display? Windows does not use GDI gamma ramps while it
    /// is, so the filter shows no change. QueryDisplayConfig + DisplayConfigGetDeviceInfo(GET_ADVANCED_COLOR_INFO = 9), the GDI
    /// name from GET_SOURCE_NAME = 1. Never throws; false when unknown. UNVERIFIED on real hardware (written from the Win32
    /// documentation; not run on Windows here).
    /// </summary>
    internal static class HdrProbe
    {
        public static bool IsHdrOn(string gdiDeviceName)
        {
            try
            {
                uint np, nm;
                if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out np, out nm) != 0 || np == 0) return false;
                PathInfo[] paths = new PathInfo[np];
                ModeInfo[] modes = new ModeInfo[nm];
                if (QueryDisplayConfig(QdcOnlyActivePaths, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
                for (int i = 0; i < np; i++)
                {
                    SourceName sn = new SourceName();
                    sn.Header.Type = 1;
                    sn.Header.Size = (uint)Marshal.SizeOf(typeof(SourceName));
                    sn.Header.AdapterId = paths[i].SourceAdapter;
                    sn.Header.Id = paths[i].SourceId;
                    if (DisplayConfigGetDeviceInfo(ref sn) != 0) continue;
                    if (!string.Equals(sn.GdiName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                    AdvancedColor ac = new AdvancedColor();
                    ac.Header.Type = 9;
                    ac.Header.Size = (uint)Marshal.SizeOf(typeof(AdvancedColor));
                    ac.Header.AdapterId = paths[i].TargetAdapter;
                    ac.Header.Id = paths[i].TargetId;
                    if (DisplayConfigGetDeviceInfo(ref ac) != 0) return false;
                    return (ac.Value & 0x2) != 0; // advancedColorEnabled
                }
            }
            catch (Exception) { }
            return false;
        }

        private const uint QdcOnlyActivePaths = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PathInfo // DISPLAYCONFIG_PATH_INFO, 72 bytes
        {
            public Luid SourceAdapter; public uint SourceId, SourceModeIdx, SourceFlags;
            public Luid TargetAdapter; public uint TargetId, TargetModeIdx, OutputTechnology, Rotation, Scaling;
            public uint RefreshNum, RefreshDen, ScanLineOrdering; public int TargetAvailable; public uint TargetFlags;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ModeInfo // DISPLAYCONFIG_MODE_INFO, 64 bytes (union kept as raw words)
        {
            public uint InfoType, Id; public Luid Adapter;
            public ulong U0, U1, U2, U3, U4, U5;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Header { public uint Type, Size; public Luid AdapterId; public uint Id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SourceName
        {
            public Header Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdvancedColor { public Header Header; public uint Value, ColorEncoding, BitsPerColorChannel; }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths,
            ref uint numModes, [Out] ModeInfo[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref SourceName info);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColor info);
    }
}
