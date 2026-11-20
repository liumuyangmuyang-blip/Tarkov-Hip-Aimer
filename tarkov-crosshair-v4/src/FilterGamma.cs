using System;
using System.Collections.Generic;
using TarkovShade;

// v4.3 (W2, Leo: "filter on/off makes no visible change"): write and verify the gamma ramp the way ShadePilot does, because
// ShadePilot's sun2 visibly works on Leo's PC. ShadePilot BETA 0.4.5 (DisplayPreset.cs, GammaRampController.SetRamp /
// SetAndVerify, commit 2bb77f6), read 17 Nov 2026:
//   - one write per apply (no fade), SetDeviceGammaRamp on CreateDC("DISPLAY", \\.\DISPLAYn), up to 3 attempts with
//     120 ms / 260 ms pauses, the Win32 error kept for the message;
//   - read-back check: RED channel only, every 16th entry (16 samples), a sample "differs" when off by more than 512, and
//     the write counts as refused only when MORE THAN 8 of the 16 samples differ.
// The synced module's GammaRampController compares all 768 entries within 256 and treats any miss as a refusal, then rolls
// the screen back -- on a driver that rounds or re-samples the LUT that is "no visible change". The module is not edited:
// LenientGammaBackend wraps the real backend and, when the hardware read-back passes ShadePilot's rule against the ramp we
// last wrote, reports that ramp, so the module's strict compare sees what ShadePilot would accept. Pure (tested).
// v5.0.2 (BRUTE P3-2): all three channels are checked (Misses3), and Forget drops the last write once the module put the user's
// own curve back, so a later change by another program while the filter is off is read as it is.
// No registry value is read or written (GdiICMGammaRange is not touched; ShadePilot's DisplayPreset.cs does not use it).

namespace TarkovCrosshair
{
    internal sealed class LenientGammaBackend : IGammaBackend
    {
        private readonly IGammaBackend inner;
        private readonly object sync = new object();
        private readonly Dictionary<string, GammaRamp> written = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Last raw read-back difference per device (samples off by > 512, of 16), for the 滤镜 page's diagnostic line.</summary>
        public readonly Dictionary<string, int> LastDiffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public LenientGammaBackend(IGammaBackend inner) { this.inner = inner; }

        /// <summary>ShadePilot SetAndVerify rule: red channel, i = 0, 16, ..., 240; |a - b| > 512 is a miss; ok while misses &lt;= 8.</summary>
        public static int Misses(GammaRamp actual, GammaRamp wanted)
        {
            if (!actual.IsValid || !wanted.IsValid) return 16;
            int d = 0;
            for (int i = 0; i < 256; i += 16) if (Math.Abs(actual.Red[i] - wanted.Red[i]) > 512) d++;
            return d;
        }

        public static bool PilotAccepts(GammaRamp actual, GammaRamp wanted) { return Misses(actual, wanted) <= 8; }

        /// <summary>v5.0.2 (BRUTE P3-2): the same 16-sample rule on red, green AND blue, worst channel. ShadePilot samples red only,
        /// so a calibration that changes only green / blue (a white-point fix, a warm tint) looked like our last write and was
        /// undone at the next filter off. Drivers that round the LUT round all three channels alike, so this keeps their writes.</summary>
        public static int Misses3(GammaRamp actual, GammaRamp wanted)
        {
            if (!actual.IsValid || !wanted.IsValid) return 16;
            int r = 0, g = 0, b = 0;
            for (int i = 0; i < 256; i += 16)
            {
                if (Math.Abs(actual.Red[i] - wanted.Red[i]) > 512) r++;
                if (Math.Abs(actual.Green[i] - wanted.Green[i]) > 512) g++;
                if (Math.Abs(actual.Blue[i] - wanted.Blue[i]) > 512) b++;
            }
            return Math.Max(r, Math.Max(g, b));
        }

        public bool TryGet(string device, out GammaRamp ramp, out string error)
        {
            if (!inner.TryGet(device, out ramp, out error)) return false;
            lock (sync)
            {
                GammaRamp w;
                if (written.TryGetValue(device, out w))
                {
                    int m = Misses3(ramp, w);
                    LastDiffs[device] = m;
                    if (m <= 8) ramp = w.Clone(); // what ShadePilot would call "written and read back OK"
                }
            }
            return true;
        }

        /// <summary>Stop substituting for this device (or all when null) until the next write.</summary>
        public void Forget(string device)
        {
            lock (sync) { if (device == null) written.Clear(); else written.Remove(device); }
        }

        public bool TrySet(string device, GammaRamp ramp, out string error)
        {
            if (!inner.TrySet(device, ramp, out error))
            {
                lock (sync) written.Remove(device);
                return false;
            }
            lock (sync) written[device] = ramp.Clone();
            return true;
        }
    }
}
