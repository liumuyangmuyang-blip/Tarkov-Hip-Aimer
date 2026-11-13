using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>
    /// v3.0 只在塔科夫前台时显示: window TITLES only. GetForegroundWindow + GetWindowText for the check (polled ≤ 10 Hz by the
    /// render thread), EnumWindows + IsWindowVisible + GetWindowText for 「从打开的窗口里选」. Never opens a handle to another
    /// process, never reads its memory, never sends it anything. For windows of other processes GetWindowText returns the
    /// cached caption without sending WM_GETTEXT, so a hung game cannot block us.
    /// UNVERIFIED: the real Tarkov window title (default GlobalSettings.DefaultGameWindowTitle = "EscapeFromTarkov").
    /// </summary>
    public static class GameWindow
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const int DWMWA_CLOAKED = 14;

        [ThreadStatic] private static StringBuilder sb;

        /// <summary>Pure: title contains 'contains' (trimmed, case-insensitive). Empty pattern or null title = false.</summary>
        public static bool TitleMatches(string title, string contains)
        {
            if (title == null || contains == null) return false;
            string c = contains.Trim();
            if (c.Length == 0) return false;
            return title.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Title(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            int n = GetWindowTextLength(h);
            if (n <= 0) return "";
            if (n > 1024) n = 1024;
            StringBuilder b = sb;
            if (b == null) { b = new StringBuilder(1025); sb = b; }
            b.Length = 0;
            b.EnsureCapacity(n + 1);
            int got = GetWindowText(h, b, n + 1);
            return got <= 0 ? "" : b.ToString(0, Math.Min(got, b.Length));
        }

        /// <summary>Title of the foreground window, or null when there is none (or on any error).</summary>
        public static string ForegroundTitle()
        {
            try { return Title(GetForegroundWindow()); }
            catch (Exception) { return null; }
        }

        /// <summary>True when the foreground window's title contains titleContains (case-insensitive). Never throws.</summary>
        public static bool IsForeground(string titleContains)
        {
            return TitleMatches(ForegroundTitle(), titleContains);
        }

        /// <summary>
        /// Titles of the visible top-level windows of OTHER processes (non-empty, not DWM-cloaked, duplicates removed), in Z
        /// order. For 「从打开的窗口里选」. Never throws (empty list on error).
        /// </summary>
        public static List<string> VisibleTitles()
        {
            List<string> result = new List<string>();
            try
            {
                uint ours = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                EnumWindowsProc cb = delegate(IntPtr h, IntPtr l)
                {
                    try
                    {
                        if (!IsWindowVisible(h)) return true;
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid == ours) return true;
                        int cloaked;
                        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;
                        string t = Title(h);
                        if (string.IsNullOrEmpty(t) || t.Trim().Length == 0) return true;
                        if (seen.Add(t)) result.Add(t);
                    }
                    catch (Exception) { }
                    return true;
                };
                EnumWindows(cb, IntPtr.Zero);
                GC.KeepAlive(cb);
            }
            catch (Exception) { }
            return result;
        }
    }
}
