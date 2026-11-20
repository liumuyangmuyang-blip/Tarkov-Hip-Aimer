// TarkovShade - fixed-preset display filter module.
// Ported from TarkovAutoShade src/GammaRampController.cs (per-monitor baselines, smooth
// 75 ms transition, restore), Copyright (c) 2026 TarkovAutoShade contributors, MIT License.
// https://github.com/lu1249765686/TarkovAutoShade  - see LICENSE-THIRD-PARTY.txt
// Change from the original: the Win32 calls sit behind IGammaBackend (Win32Gamma.cs) so the
// transition / baseline logic can be unit-tested without a display.
// TarkovShade additions (E1 fixes): every write is read back and compared (A1), the first fade
// step is written synchronously and timer-thread failures roll back and raise TransitionFailed
// (A2/D1/B-09), baselines are refreshed when the device is not in use and checked before a
// restore (A8), one-shot re-armed timer with a generation check (A10/B-13), dispose guard (B-06).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TarkovShade
{
    public sealed class DisplayTarget
    {
        public string DeviceName;
        public string FriendlyName;
        public bool Primary;

        public override string ToString()
        {
            return FriendlyName + (Primary ? " (primary)" : "");
        }
    }

    /// <summary>Reads / writes one display's hardware gamma ramp.</summary>
    public interface IGammaBackend
    {
        bool TryGet(string deviceName, out GammaRamp ramp, out string error);
        bool TrySet(string deviceName, GammaRamp ramp, out string error);
    }

    public sealed class GammaRampController : IDisposable
    {
        /// <summary>Text appended to every "Windows refused the curve" error.</summary>
        public const string RangeLimitHint =
            "Windows limits how far a gamma curve may move from normal and refuses curves past that limit. " +
            "Lower the strongest sliders (or pick a milder preset), and turn HDR off. " +
            "The registry value HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ICM\\GdiICMGammaRange " +
            "(DWORD 256) is reported to lift the limit (UNVERIFIED community workaround).";

        /// <summary>Allowed difference per entry between the written and the read-back ramp
        /// (drivers may quantise the LUT; 256 = one 8-bit step; UNVERIFIED on hardware).</summary>
        public const int VerifyTolerance = 256;

        private const int TransitionIntervalMilliseconds = 75;
        private readonly object sync = new object();
        private readonly IGammaBackend backend;
        private readonly Dictionary<string, GammaRamp> baselines =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        // Baselines seeded from a crash-recovery file: the true original, never re-captured.
        private readonly HashSet<string> pinned =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Devices this controller wrote to and has not restored yet.
        private readonly HashSet<string> touched =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GammaRamp> currentRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeDevices =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // v5.0.2 (crosshair W15, H5 seed 50325): devices whose screen may show a ramp this controller wrote but could not verify
        // and could not roll back. Their read-back is never taken as "another program's ramp" (A8) or as a new baseline.
        private readonly HashSet<string> uncertain =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private System.Threading.Timer transitionTimer;
        private int transitionGeneration;
        private int transitionStep;
        private int transitionSteps;
        private readonly Dictionary<string, GammaRamp> transitionFromRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GammaRamp> transitionToRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> transitionWasActive =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool hasTransition;
        private bool disposed;
        private string lastTransitionError = "";

        /// <summary>
        /// Raised on the timer thread (outside the controller lock) when a smooth-fade step failed.
        /// The fade was stopped and every device in it was put back to the curve it had before the fade.
        /// </summary>
        public event Action<string> TransitionFailed;

        public GammaRampController(IGammaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException("backend");
            this.backend = backend;
        }

        public bool HasActiveFilter
        {
            get { lock (sync) { return activeDevices.Count > 0 || hasTransition; } }
        }

        public bool IsTransitioning
        {
            get { lock (sync) { return hasTransition; } }
        }

        /// <summary>Error of the last failed fade step ("" when none).</summary>
        public string LastTransitionError
        {
            get { lock (sync) { return lastTransitionError; } }
        }

        /// <summary>
        /// Reads the device's current ramp as its baseline. A device that is not in use by this
        /// controller is re-read every time (A8: the user's ramp may have changed); a device in use,
        /// not yet restored, or seeded from a recovery file keeps its baseline.
        /// </summary>
        public bool CaptureBaseline(string deviceName, out string error)
        {
            error = "";
            lock (sync)
            {
                if (disposed) { error = "Display controller disposed."; return false; }
                return CaptureBaselineInternal(deviceName, out error);
            }
        }

        public bool CaptureBaselines(IList<string> deviceNames, out string error)
        {
            error = "";
            if (deviceNames == null || deviceNames.Count == 0)
            {
                error = "No display selected.";
                return false;
            }
            lock (sync)
            {
                if (disposed) { error = "Display controller disposed."; return false; }
                foreach (string deviceName in deviceNames)
                {
                    if (string.IsNullOrWhiteSpace(deviceName) ||
                        !CaptureBaselineInternal(deviceName, out error))
                    {
                        if (error.Length == 0) error = "No display selected.";
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Uses a known original (from a crash-recovery file) as the device's baseline, so a screen
        /// that is still filtered from a crash is never captured as the "original" (B-02).
        /// Ignored while the device is in use.
        /// </summary>
        public bool SeedBaseline(string deviceName, GammaRamp original)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || !original.IsValid) return false;
            lock (sync)
            {
                if (disposed || activeDevices.Contains(deviceName) || touched.Contains(deviceName)) return false;
                baselines[deviceName] = original.Clone();
                pinned.Add(deviceName);
                return true;
            }
        }

        public bool TransitionTo(
            IList<string> deviceNames,
            ShadeRamp shadeRamp,
            int durationMilliseconds,
            out string error)
        {
            return TransitionCore(deviceNames, shadeRamp, null, durationMilliseconds, out error);
        }

        /// <summary>
        /// Per-device destination ramps (Final A: each device gets the PilotCurve built on its own baseline).
        /// Same fade, read-back verification and rollback rules as the single-ramp overload.
        /// </summary>
        public bool TransitionTo(
            IDictionary<string, GammaRamp> perDevice,
            int durationMilliseconds,
            out string error)
        {
            error = "";
            if (perDevice == null || perDevice.Count == 0) { error = "No display selected."; return false; }
            var copy = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, GammaRamp> item in perDevice)
            {
                if (string.IsNullOrWhiteSpace(item.Key)) continue;
                if (!item.Value.IsValid) { error = "Invalid ramp for " + item.Key + "."; return false; }
                copy[item.Key] = item.Value.Clone();
            }
            return TransitionCore(new List<string>(copy.Keys), null, copy, durationMilliseconds, out error);
        }

        private bool TransitionCore(
            IList<string> deviceNames,
            ShadeRamp shadeRamp,
            IDictionary<string, GammaRamp> perDevice,
            int durationMilliseconds,
            out string error)
        {
            error = "";
            if (shadeRamp == null && perDevice == null)
            {
                error = "No ramp to apply.";
                return false;
            }
            if (deviceNames == null || deviceNames.Count == 0)
            {
                error = "No display selected.";
                return false;
            }

            lock (sync)
            {
                if (disposed) { error = "Display controller disposed."; return false; }
                if (!CaptureBaselines(deviceNames, out error)) return false;

                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string deviceName in deviceNames)
                {
                    if (!string.IsNullOrWhiteSpace(deviceName)) targets.Add(deviceName);
                }
                if (targets.Count == 0)
                {
                    error = "No display selected.";
                    return false;
                }

                var requestedRamps = new Dictionary<string, GammaRamp>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string deviceName in targets)
                    requestedRamps[deviceName] = perDevice != null ?
                        perDevice[deviceName].Clone() : GammaRamp.FromShadeRamp(shadeRamp);

                bool sameTargets = activeDevices.Count == targets.Count;
                if (sameTargets)
                {
                    foreach (string deviceName in targets)
                    {
                        if (!activeDevices.Contains(deviceName))
                        {
                            sameTargets = false;
                            break;
                        }
                    }
                }
                if (sameTargets)
                {
                    bool sameDestination = true;
                    foreach (string deviceName in targets)
                    {
                        GammaRamp existing;
                        if (hasTransition)
                            sameDestination = transitionToRamps.TryGetValue(
                                deviceName, out existing) &&
                                RampsEqual(existing, requestedRamps[deviceName]);
                        else
                            sameDestination = currentRamps.TryGetValue(
                                deviceName, out existing) &&
                                RampsEqual(existing, requestedRamps[deviceName]);
                        if (!sameDestination) break;
                    }
                    if (sameDestination) return true;
                }

                // The in-progress fade's current step is the starting point of the new one.
                StopTransition();

                foreach (string oldDevice in new List<string>(activeDevices))
                {
                    if (!targets.Contains(oldDevice))
                    {
                        string ignored;
                        RestoreDevice(oldDevice, out ignored);
                        activeDevices.Remove(oldDevice);
                        currentRamps.Remove(oldDevice);
                    }
                }

                transitionWasActive.Clear();
                foreach (string deviceName in targets)
                {
                    GammaRamp from = new GammaRamp();
                    bool wasActive = activeDevices.Contains(deviceName) &&
                        currentRamps.TryGetValue(deviceName, out from);
                    if (!wasActive) from = baselines[deviceName].Clone();
                    else transitionWasActive.Add(deviceName);
                    transitionFromRamps[deviceName] = from.Clone();
                    transitionToRamps[deviceName] = requestedRamps[deviceName].Clone();
                }

                int steps = durationMilliseconds <= 0 ? 1 :
                    Math.Max(1, durationMilliseconds / TransitionIntervalMilliseconds);

                // First step synchronously (A2/D1): a refusal is reported to the caller.
                // v5.0.2 (crosshair W15, BRUTE P3-1): the device counts as touched BEFORE the write (its baseline stays pinned),
                // so an unverified write whose rollback is also refused is still restored later and never re-read as the original.
                foreach (string deviceName in targets)
                {
                    GammaRamp ramp = StepRamp(deviceName, 1, steps);
                    touched.Add(deviceName);
                    if (!SetVerified(deviceName, ramp, out error))
                    {
                        RollbackTransition();
                        return false;
                    }
                    currentRamps[deviceName] = ramp;
                    touched.Add(deviceName);
                    activeDevices.Add(deviceName);
                }
                lastTransitionError = "";

                if (steps <= 1)
                {
                    ClearTransitionState();
                    return true;
                }

                transitionStep = 1;
                transitionSteps = steps;
                hasTransition = true;
                int generation = ++transitionGeneration;
                // One-shot timer re-armed after every tick (A10): callbacks never overlap, and a
                // queued callback of an earlier fade is ignored by the generation check (B-13).
                transitionTimer = new System.Threading.Timer(delegate {
                    TickTransition(generation);
                }, null, TransitionIntervalMilliseconds, System.Threading.Timeout.Infinite);
                return true;
            }
        }

        /// <summary>
        /// Puts the baseline back on every device this controller changed. A8: before writing, the
        /// device's ramp is read back; if it is no longer the curve this controller wrote (a display
        /// event or another program replaced it), the stale baseline is NOT written over it.
        /// </summary>
        public bool RestoreAll(out string error)
        {
            lock (sync)
            {
                StopTransition();
                error = "";
                if (disposed) return true;
                return RestoreAllInternal(out error);
            }
        }

        /// <summary>
        /// Call after a display change (mode switch, monitor reconnect, the game changing resolution).
        /// Every device in use whose ramp was reset by Windows or replaced by another program gets
        /// that ramp as its new baseline and the filter is written again. Returns the number of
        /// devices re-applied, -1 on a write error.
        /// </summary>
        public int Resync(out string error)
        {
            error = "";
            lock (sync)
            {
                if (disposed) return 0;
                int reapplied = 0;
                foreach (string device in new List<string>(activeDevices))
                {
                    GammaRamp current;
                    if (!currentRamps.TryGetValue(device, out current)) continue;
                    GammaRamp hardware;
                    string readError;
                    if (!GetSafe(device, out hardware, out readError)) continue;
                    if (RampsClose(hardware, current, VerifyTolerance)) continue;
                    if (!pinned.Contains(device) && !uncertain.Contains(device)) baselines[device] = hardware.Clone();
                    if (hasTransition && transitionFromRamps.ContainsKey(device) &&
                        !transitionWasActive.Contains(device))
                        transitionFromRamps[device] = baselines[device].Clone();
                    string setError;
                    if (!SetVerified(device, current, out setError))
                    {
                        if (error.Length == 0) error = setError;
                        reapplied = -1;
                        continue;
                    }
                    if (reapplied >= 0) reapplied++;
                }
                return reapplied;
            }
        }

        public GammaRamp? GetBaseline(string deviceName)
        {
            lock (sync)
            {
                GammaRamp ramp;
                return baselines.TryGetValue(deviceName, out ramp) ?
                    (GammaRamp?)ramp.Clone() : null;
            }
        }

        public bool ApplyDirect(IDictionary<string, GammaRamp> ramps, out string error)
        {
            error = "";
            if (ramps == null || ramps.Count == 0)
            {
                error = "No saved display curve to restore.";
                return false;
            }
            lock (sync)
            {
                if (disposed) { error = "Display controller disposed."; return false; }
                bool success = true;
                foreach (KeyValuePair<string, GammaRamp> item in ramps)
                {
                    string itemError = "";
                    if (!item.Value.IsValid || !SetSafe(item.Key, item.Value, out itemError))
                    {
                        if (!item.Value.IsValid) itemError = "Saved display curve is invalid.";
                        success = false;
                        if (error.Length == 0) error = itemError;
                    }
                }
                return success;
            }
        }

        // ------------------------------------------------------------------ internals

        private bool CaptureBaselineInternal(string deviceName, out string error)
        {
            error = "";
            if (baselines.ContainsKey(deviceName) &&
                (activeDevices.Contains(deviceName) || touched.Contains(deviceName) || pinned.Contains(deviceName)))
                return true;
            GammaRamp ramp;
            if (!GetSafe(deviceName, out ramp, out error)) return false;
            if (!ramp.IsValid)
            {
                error = "The display returned an invalid gamma curve.";
                return false;
            }
            baselines[deviceName] = ramp.Clone();
            return true;
        }

        private GammaRamp StepRamp(string deviceName, int step, int steps)
        {
            GammaRamp to = transitionToRamps[deviceName];
            if (step >= steps) return to.Clone();
            double amount = ShadeMath.Clamp(step / (double)steps, 0.0, 1.0);
            amount = amount * amount * (3.0 - 2.0 * amount);
            return GammaRamp.Lerp(transitionFromRamps[deviceName], to, amount);
        }

        private void TickTransition(int generation)
        {
            string failure = null;
            Action<string> handler = null;
            try
            {
                lock (sync)
                {
                    if (disposed || generation != transitionGeneration || transitionTimer == null || !hasTransition)
                        return;

                    transitionStep++;
                    foreach (string deviceName in new List<string>(transitionToRamps.Keys))
                    {
                        GammaRamp ramp = StepRamp(deviceName, transitionStep, transitionSteps);
                        string error;
                        if (!SetVerified(deviceName, ramp, out error))
                        {
                            failure = error;
                            break;
                        }
                        currentRamps[deviceName] = ramp;
                    }

                    if (failure != null)
                    {
                        lastTransitionError = failure;
                        RollbackTransition();
                        handler = TransitionFailed;
                    }
                    else if (transitionStep >= transitionSteps)
                    {
                        StopTransition();
                    }
                    else if (transitionTimer != null)
                    {
                        transitionTimer.Change(TransitionIntervalMilliseconds, System.Threading.Timeout.Infinite);
                    }
                }
            }
            catch (Exception ex)
            {
                // B-09: nothing may escape a thread-pool callback (it would kill the process).
                failure = "Smooth fade failed: " + ex.Message;
                try
                {
                    lock (sync)
                    {
                        if (generation == transitionGeneration && hasTransition)
                        {
                            lastTransitionError = failure;
                            RollbackTransition();
                        }
                    }
                }
                catch
                {
                }
                handler = TransitionFailed;
            }
            if (failure != null && handler != null)
            {
                try { handler(failure); } catch { }
            }
        }

        /// <summary>
        /// Stops the fade and puts every device in it back to where it was before the fade:
        /// the previous filter for devices that were already filtered, the baseline for the others.
        /// </summary>
        private void RollbackTransition()
        {
            foreach (KeyValuePair<string, GammaRamp> item in new Dictionary<string, GammaRamp>(transitionFromRamps))
            {
                string ignored;
                bool wasActive = transitionWasActive.Contains(item.Key);
                bool written = SetSafe(item.Key, item.Value, out ignored);
                if (!written) uncertain.Add(item.Key);
                else uncertain.Remove(item.Key);
                if (wasActive)
                {
                    currentRamps[item.Key] = item.Value.Clone();
                    activeDevices.Add(item.Key);
                }
                else
                {
                    activeDevices.Remove(item.Key);
                    currentRamps.Remove(item.Key);
                    if (written) touched.Remove(item.Key);
                }
            }
            StopTransition();
        }

        private bool RestoreAllInternal(out string error)
        {
            error = "";
            bool success = true;
            foreach (string device in new List<string>(touched))
            {
                string itemError;
                if (!RestoreDevice(device, out itemError))
                {
                    success = false;
                    if (error.Length == 0) error = itemError;
                }
            }
            activeDevices.Clear();
            currentRamps.Clear();
            return success;
        }

        private bool RestoreDevice(string deviceName, out string error)
        {
            error = "";
            GammaRamp baseline;
            if (!touched.Contains(deviceName) || !baselines.TryGetValue(deviceName, out baseline))
            {
                touched.Remove(deviceName);
                return true;
            }
            GammaRamp written, hardware;
            string readError;
            if (!uncertain.Contains(deviceName) && currentRamps.TryGetValue(deviceName, out written) &&
                GetSafe(deviceName, out hardware, out readError) && hardware.IsValid &&
                !RampsClose(hardware, written, VerifyTolerance))
            {
                // A8: the ramp was replaced since our last write; keep it and use it as the baseline.
                if (!RampsClose(hardware, baseline, VerifyTolerance))
                {
                    baselines[deviceName] = hardware.Clone();
                    pinned.Remove(deviceName);
                }
                touched.Remove(deviceName);
                return true;
            }
            if (!SetSafe(deviceName, baseline, out error)) return false;
            touched.Remove(deviceName);
            pinned.Remove(deviceName);
            uncertain.Remove(deviceName);
            return true;
        }

        /// <summary>A1: write, then read back and compare (SetDeviceGammaRamp may return TRUE without applying).</summary>
        private bool SetVerified(string deviceName, GammaRamp ramp, out string error)
        {
            if (!SetSafe(deviceName, ramp, out error))
            {
                if (string.IsNullOrEmpty(error)) error = "Setting the gamma curve failed.";
                return false;
            }
            GammaRamp readBack;
            string readError;
            if (!GetSafe(deviceName, out readBack, out readError) || !readBack.IsValid)
            {
                // v4.3 (V43-PERF TOP 0 fix 4): an unreadable read-back is NOT success -- the screen may show nothing.
                error = "Windows accepted the gamma curve on " + deviceName + " but it could not be read back (could not verify)" +
                    (string.IsNullOrEmpty(readError) ? "." : ": " + readError);
                return false;
            }
            if (RampsClose(readBack, ramp, VerifyTolerance)) { uncertain.Remove(deviceName); return true; }
            error = "Windows did not apply the gamma curve on " + deviceName + " (read-back differs). " + RangeLimitHint;
            return false;
        }

        private bool SetSafe(string deviceName, GammaRamp ramp, out string error)
        {
            try
            {
                return backend.TrySet(deviceName, ramp, out error);
            }
            catch (Exception ex)
            {
                error = "Setting the gamma curve failed: " + ex.Message;
                return false;
            }
        }

        private bool GetSafe(string deviceName, out GammaRamp ramp, out string error)
        {
            try
            {
                return backend.TryGet(deviceName, out ramp, out error);
            }
            catch (Exception ex)
            {
                ramp = GammaRamp.CreateEmpty();
                error = "Reading the gamma curve failed: " + ex.Message;
                return false;
            }
        }

        private static bool RampsEqual(GammaRamp left, GammaRamp right)
        {
            return RampsClose(left, right, 0);
        }

        /// <summary>True when both ramps are valid and every entry differs by at most tolerance.</summary>
        public static bool RampsClose(GammaRamp left, GammaRamp right, int tolerance)
        {
            if (!left.IsValid || !right.IsValid) return false;
            for (int i = 0; i < 256; i++)
            {
                if (Math.Abs(left.Red[i] - right.Red[i]) > tolerance ||
                    Math.Abs(left.Green[i] - right.Green[i]) > tolerance ||
                    Math.Abs(left.Blue[i] - right.Blue[i]) > tolerance) return false;
            }
            return true;
        }

        private void StopTransition()
        {
            if (transitionTimer != null)
            {
                transitionTimer.Dispose();
                transitionTimer = null;
            }
            transitionGeneration++;
            ClearTransitionState();
        }

        private void ClearTransitionState()
        {
            hasTransition = false;
            transitionFromRamps.Clear();
            transitionToRamps.Clear();
            transitionWasActive.Clear();
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                StopTransition();
                string ignored;
                RestoreAllInternal(out ignored);
                disposed = true;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;

        public static GammaRamp CreateEmpty()
        {
            var r = new GammaRamp();
            r.Red = new ushort[256];
            r.Green = new ushort[256];
            r.Blue = new ushort[256];
            return r;
        }

        /// <summary>Windows' standard curve: entry i = i * 257 on every channel.</summary>
        public static GammaRamp CreateIdentity()
        {
            var r = new GammaRamp();
            r.Red = PilotCurve.IdentityChannel();
            r.Green = PilotCurve.IdentityChannel();
            r.Blue = PilotCurve.IdentityChannel();
            return r;
        }

        public static GammaRamp FromShadeRamp(ShadeRamp ramp)
        {
            var r = new GammaRamp();
            r.Red = (ushort[])ramp.Red.Clone();
            r.Green = (ushort[])ramp.Green.Clone();
            r.Blue = (ushort[])ramp.Blue.Clone();
            return r;
        }

        public bool IsValid
        {
            get
            {
                return Red != null && Green != null && Blue != null &&
                    Red.Length == 256 && Green.Length == 256 && Blue.Length == 256;
            }
        }

        public GammaRamp Clone()
        {
            var r = new GammaRamp();
            r.Red = Red == null ? new ushort[256] : (ushort[])Red.Clone();
            r.Green = Green == null ? new ushort[256] : (ushort[])Green.Clone();
            r.Blue = Blue == null ? new ushort[256] : (ushort[])Blue.Clone();
            return r;
        }

        public static GammaRamp Lerp(GammaRamp from, GammaRamp to, double amount)
        {
            GammaRamp result = CreateEmpty();
            for (int i = 0; i < 256; i++)
            {
                result.Red[i] = Interpolate(from.Red[i], to.Red[i], amount);
                result.Green[i] = Interpolate(from.Green[i], to.Green[i], amount);
                result.Blue[i] = Interpolate(from.Blue[i], to.Blue[i], amount);
            }
            return result;
        }

        private static ushort Interpolate(ushort from, ushort to, double amount)
        {
            return (ushort)Math.Round(from + (to - from) * amount);
        }
    }
}
