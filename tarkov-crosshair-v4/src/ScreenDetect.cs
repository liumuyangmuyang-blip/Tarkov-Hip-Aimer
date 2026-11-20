// v4.0 游戏分辨率 auto-detect (Windows only: user32 / gdi32). Logic lives in Settings.cs (ResolutionDetect).
using System;
using System.Runtime.InteropServices;

namespace TarkovCrosshair
{
    /// <summary>
    /// Real pixel size of the PRIMARY screen, independent of the Windows DPI scale and of this process's DPI awareness:
    /// 1) EnumDisplaySettings(ENUM_CURRENT_SETTINGS) on the primary display device = the current display mode in pixels;
    /// 2) GetDeviceCaps(DESKTOPHORZRES / DESKTOPVERTRES) on the screen DC (also physical pixels);
    /// 3) GetSystemMetrics(SM_CXSCREEN / SM_CYSCREEN) (correct when the process is DPI aware, as our manifest declares).
    /// Returns null when nothing works. Never throws.
    /// </summary>
    public static class ScreenDetect
    {
        public static int[] PrimaryPixels()
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                    return new int[] { dm.dmPelsWidth, dm.dmPelsHeight };
            }
            catch (Exception) { }
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    try
                    {
                        int w = GetDeviceCaps(dc, DESKTOPHORZRES), h = GetDeviceCaps(dc, DESKTOPVERTRES);
                        if (w > 0 && h > 0) return new int[] { w, h };
                    }
                    finally { ReleaseDC(IntPtr.Zero, dc); }
                }
            }
            catch (Exception) { }
            try
            {
                int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
                if (w > 0 && h > 0) return new int[] { w, h };
            }
            catch (Exception) { }
            return null;
        }

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DESKTOPVERTRES = 117, DESKTOPHORZRES = 118;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    }
}
