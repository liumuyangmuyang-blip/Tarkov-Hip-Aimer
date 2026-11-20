// TarkovShade - Windows-only pieces: gdi32 gamma backend, display list, hotkey poller,
// and ShadeEngine.CreateDefault(). Gamma calls ported from TarkovAutoShade
// src/GammaRampController.cs (CreateDC / Get/SetDeviceGammaRamp / DeleteDC),
// Copyright (c) 2026 TarkovAutoShade contributors, MIT License.
// https://github.com/lu1249765686/TarkovAutoShade - see LICENSE-THIRD-PARTY.txt
// Hotkeys: polled with GetAsyncKeyState (reads key state only; no hook, no RegisterHotKey,
// no SendInput), the same input policy as Leo's crosshair app.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TarkovShade
{
    public sealed class Win32GammaBackend : IGammaBackend
    {
        public bool TryGet(string deviceName, out GammaRamp ramp, out string error)
        {
            ramp = GammaRamp.CreateEmpty();
            error = "";
            IntPtr dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                error = "Cannot open display " + deviceName + ".";
                return false;
            }
            try
            {
                var value = GammaRamp.CreateEmpty();
                if (!GetDeviceGammaRamp(dc, ref value))
                {
                    // A12: GetDeviceGammaRamp does not document SetLastError, so no error number is shown.
                    error = "Reading the gamma curve of " + deviceName + " failed.";
                    return false;
                }
                ramp = value;
                return true;
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        public bool TrySet(string deviceName, GammaRamp ramp, out string error)
        {
            error = "";
            IntPtr dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                error = "Cannot open display " + deviceName + ".";
                return false;
            }
            try
            {
                GammaRamp copy = ramp.Clone();
                if (!SetDeviceGammaRamp(dc, ref copy))
                {
                    // A1: a FALSE return is a refusal; a silent refusal (TRUE without applying) is caught by
                    // GammaRampController, which reads the ramp back after every write.
                    error = "Windows refused the gamma curve on " + deviceName + ". " + GammaRampController.RangeLimitHint;
                    return false;
                }
                return true;
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
    }

    public static class Win32Displays
    {
        public static List<DisplayTarget> Enumerate()
        {
            var result = new List<DisplayTarget>();
            foreach (Screen screen in Screen.AllScreens)
            {
                var t = new DisplayTarget();
                t.DeviceName = screen.DeviceName;
                t.FriendlyName = screen.DeviceName.Replace(@"\\.\", "");
                t.Primary = screen.Primary;
                result.Add(t);
            }
            return result;
        }
    }

    /// <summary>
    /// Polls every hotkey of engine.Hotkeys on a WinForms timer (30 ms, UI thread) and calls
    /// engine.OnHotkeyDown on a key-down edge and engine.OnHotkeyUp when the held key is released.
    /// Modifiers must match exactly (Ctrl+1 does not fire on Ctrl+Shift+1).
    /// </summary>
    public sealed class HotkeyPoller : IDisposable
    {
        private readonly Timer timer = new Timer();
        private readonly ShadeEngine engine;
        private readonly HashSet<string> down = new HashSet<string>();
        private int holdKey;

        /// <summary>Raised on the UI thread after an action ran: "toggle", "next", "hold", "restore", "preset:NAME".</summary>
        public event Action<string> Fired;

        public HotkeyPoller(ShadeEngine engine)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            this.engine = engine;
            timer.Interval = 30;
            timer.Tick += delegate { Poll(); };
        }

        public void Start() { timer.Start(); }
        public void Stop() { timer.Stop(); down.Clear(); if (holdKey != 0) { engine.OnHotkeyUp(holdKey); holdKey = 0; } }

        private void Poll()
        {
            HotkeySettings h = engine.Hotkeys;
            if (holdKey != 0 && !Down(holdKey))
            {
                int k = holdKey;
                holdKey = 0;
                engine.OnHotkeyUp(k);
            }
            int mods = (Down(0x12) ? HotkeyBinding.ModAlt : 0) | (Down(0x11) ? HotkeyBinding.ModCtrl : 0) |
                (Down(0x10) ? HotkeyBinding.ModShift : 0);
            Check(h.Toggle, "toggle", mods);
            Check(h.NextPreset, "next", mods);
            Check(h.HoldOriginal, "hold", mods);
            Check(h.RestoreAll, "restore", mods);
            foreach (PresetHotkey p in h.Presets) Check(p.Key, "preset:" + p.PresetName, mods);
        }

        private void Check(HotkeyBinding b, string action, int mods)
        {
            if (b == null || !b.IsAssigned || b.Key > 255) return;
            string id = action + "|" + b;
            bool isDown = Down(b.Key) && (b.Modifiers & 7) == mods;
            if (!isDown) { down.Remove(id); return; }
            if (!down.Add(id)) return;
            if (engine.OnHotkeyDown(b.Key, mods))
            {
                if (action == "hold") holdKey = b.Key;
                Action<string> f = Fired;
                if (f != null) { try { f(action); } catch { } }
            }
        }

        private static bool Down(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }
    }

    public sealed partial class ShadeEngine
    {
        /// <summary>Windows engine: files next to the exe, gdi32 gamma, detected driver vibrance, Magnification saturation, DDC/CI monitor.</summary>
        public static ShadeEngine CreateDefault()
        {
            return CreateDefault(AppDomain.CurrentDomain.BaseDirectory);
        }

        public static ShadeEngine CreateDefault(string dataFolder)
        {
            return new ShadeEngine(dataFolder, new Win32GammaBackend(), VibranceController.Detect(),
                new LocalSaturation(), Win32Displays.Enumerate, new Dxva2DdcBackend());
        }
    }
}
