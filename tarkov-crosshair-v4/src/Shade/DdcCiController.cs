// TarkovShade - monitor hardware brightness / contrast / saturation over DDC/CI (dxva2.dll).
// Probe, write-and-verify and restore rules are ported from ShadePilot DisplayPreset.cs
// (commit 2bb77f6): PhysicalDisplay.Refresh / SetBrightness / SetContrast / SetSaturation /
// WriteAndVerifyVcp (lines 4073-4271), Snapshot (lines 4273-4308), MonitorApi.Enumerate
// (lines 6189-6226), and the WPF slider conversions ToNeutralZero / FromNeutralZero /
// FromNeutral50 (wpf/MainWindow.Full.cs lines 131-134).
// ShadePilot: https://github.com/BYS-XSQ/ShadePilot, Copyright (c) 2026 BYS-XSQ, MIT License
// (see LICENSE-SHADEPILOT.txt).
// Differences from ShadePilot: DLLs are loaded from System32 only (DefaultDllImportSearchPaths),
// the backend is an interface so the logic is testable, originals are captured on the first
// Refresh of each monitor and RestoreOriginals reports every failure. C# 5.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;

namespace TarkovShade
{
    public sealed class DdcMonitorHandle
    {
        public IntPtr Handle;
        public string Name;
        public string DisplayDeviceName; // \\.\DISPLAYn
        public string Id;                // DisplayDeviceName + "|physical:" + index
    }

    /// <summary>Low-level DDC/CI calls. Real implementation: Dxva2DdcBackend.</summary>
    public interface IDdcBackend
    {
        List<DdcMonitorHandle> Enumerate(out string error);
        bool GetCapabilities(IntPtr monitor, out uint caps);
        bool GetBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
        bool SetBrightness(IntPtr monitor, uint value);
        bool GetContrast(IntPtr monitor, out uint min, out uint current, out uint max);
        bool SetContrast(IntPtr monitor, uint value);
        /// <summary>setParameter = MC_SET_PARAMETER (true) or MC_MOMENTARY (false).</summary>
        bool GetVcp(IntPtr monitor, byte code, out bool setParameter, out uint current, out uint max);
        bool SetVcp(IntPtr monitor, byte code, uint value);
        void Destroy(IntPtr monitor);
    }

    public sealed class DdcMonitor
    {
        public string Id, Name, DisplayDeviceName;
        internal IntPtr Handle;
        public uint Capabilities;
        public uint BrightnessMin, BrightnessCurrent, BrightnessMax;
        public uint ContrastMin, ContrastCurrent, ContrastMax;
        public uint SaturationMin, SaturationCurrent, SaturationMax;
        public bool SaturationRestoreSafe;
        /// <summary>VCP 0x87 sharpness (not in ShadePilot). Same double-read probe as saturation.</summary>
        public uint SharpnessMin, SharpnessCurrent, SharpnessMax;
        public string ProbeNote = "";
        public bool SupportsBrightness { get { return (Capabilities & 0x2) != 0; } }
        public bool SupportsContrast { get { return (Capabilities & 0x4) != 0; } }
        public bool SupportsSaturation { get { return SaturationMax > SaturationMin; } }
        public bool SupportsSharpness { get { return SharpnessMax > SharpnessMin; } }
    }

    /// <summary>
    /// Values read at the first probe, restored by RestoreOriginals (ShadePilot Snapshot). DataContract
    /// so the engine can write it to its crash-recovery file and seed it back after a crash.
    /// </summary>
    [DataContract]
    public sealed class DdcSnapshot
    {
        [DataMember(Order = 0)] public string Id;
        [DataMember(Order = 1)] public bool HasBrightness;
        [DataMember(Order = 2)] public bool HasContrast;
        [DataMember(Order = 3)] public bool HasSaturation;
        [DataMember(Order = 4)] public uint Brightness;
        [DataMember(Order = 5)] public uint Contrast;
        [DataMember(Order = 6)] public uint Saturation;
        [DataMember(Order = 7)] public bool HasSharpness;
        [DataMember(Order = 8)] public uint Sharpness;
        public DdcSnapshot Clone() { return (DdcSnapshot)MemberwiseClone(); }
    }

    /// <summary>One hardware value of one monitor, for the UI.</summary>
    public struct DdcValue
    {
        public bool Supported;
        public uint Min, Current, Max;
        /// <summary>"" when supported, else a plain reason (shown next to the disabled slider).</summary>
        public string Reason;
    }

    public sealed class DdcCiController : IDisposable
    {
        public const byte VcpBrightness = 0x10, VcpContrast = 0x12, VcpSaturation = 0x8A, VcpSharpness = 0x87;
        /// <summary>"\u4e0d\u652f\u6301" (not supported), ShadePilot's label for a disabled hardware slider.</summary>
        public const string NotSupportedText = "\u4e0d\u652f\u6301";

        private readonly object sync = new object();
        private readonly IDdcBackend backend;
        private readonly Dictionary<string, DdcSnapshot> originals =
            new Dictionary<string, DdcSnapshot>(StringComparer.OrdinalIgnoreCase);
        private List<DdcMonitor> monitors = new List<DdcMonitor>();
        // v5.0.2 (crosshair W15, BRUTE P1-2): "id|vcp" whose value on the monitor is unknown (a write or its read-back failed or
        // timed out; the write may still land), and ids this controller wrote to (or a crash left changed) and has not put back.
        private readonly HashSet<string> unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // ids this controller itself wrote to (a value a crash-time seed lacks was never written: it may be filled from a probe)
        private readonly HashSet<string> writtenHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string Key(string id, byte code) { return (id ?? "") + "|" + code; }
        /// <summary>Sleep hook (ShadePilot waits 70-300 ms between DDC calls); tests replace it.</summary>
        public Action<int> Sleep = delegate(int ms) { Thread.Sleep(ms); };
        public bool RestoreOnDispose = true;
        public string LastError = "";

        public DdcCiController(IDdcBackend backend)
        {
            if (backend == null) throw new ArgumentNullException("backend");
            this.backend = backend;
        }

        public static DdcCiController CreateDefault() { return new DdcCiController(new Dxva2DdcBackend()); }

        public List<DdcMonitor> Monitors { get { lock (sync) return new List<DdcMonitor>(monitors); } }

        /// <summary>Re-enumerate and probe all monitors. Originals are recorded the first time a monitor Id is seen.</summary>
        public bool Refresh(out string error)
        {
            lock (sync)
            {
                List<DdcMonitorHandle> found = backend.Enumerate(out error) ?? new List<DdcMonitorHandle>();
                if (!string.IsNullOrEmpty(error) && found.Count == 0 && monitors.Count > 0)
                {
                    // v5.0.2 (BRUTE P1-2 c): a failed or timed-out enumeration keeps the monitors (and handles) we already have,
                    // so their saved originals can still be put back.
                    LastError = error;
                    return false;
                }
                ReleaseHandles();
                List<DdcMonitor> old = monitors;
                List<DdcMonitor> list = new List<DdcMonitor>();
                foreach (DdcMonitorHandle h in found)
                {
                    DdcMonitor m = new DdcMonitor();
                    m.Id = h.Id; m.Name = h.Name; m.DisplayDeviceName = h.DisplayDeviceName; m.Handle = h.Handle;
                    try { Probe(m); }
                    catch (Exception ex) { MarkProbeFailed(m); m.ProbeNote += " (" + ex.Message + ")"; }
                    list.Add(m);
                    DdcSnapshot have;
                    if (originals.TryGetValue(m.Id, out have))
                    {
                        // v5.0.2 (BRUTE P1-2 d): a value an earlier probe could not read is recorded once it can be read,
                        // as long as this session has not written it
                        if (!have.HasBrightness && m.SupportsBrightness && !Touched(m.Id, VcpBrightness)) { have.HasBrightness = true; have.Brightness = m.BrightnessCurrent; }
                        if (!have.HasContrast && m.SupportsContrast && !Touched(m.Id, VcpContrast)) { have.HasContrast = true; have.Contrast = m.ContrastCurrent; }
                        if (!have.HasSaturation && m.SupportsSaturation && m.SaturationRestoreSafe && !Touched(m.Id, VcpSaturation)) { have.HasSaturation = true; have.Saturation = m.SaturationCurrent; }
                        if (!have.HasSharpness && m.SupportsSharpness && !Touched(m.Id, VcpSharpness)) { have.HasSharpness = true; have.Sharpness = m.SharpnessCurrent; }
                    }
                    else
                    {
                        DdcSnapshot s = new DdcSnapshot();
                        s.Id = m.Id;
                        s.HasBrightness = m.SupportsBrightness; s.Brightness = m.BrightnessCurrent;
                        s.HasContrast = m.SupportsContrast; s.Contrast = m.ContrastCurrent;
                        s.HasSaturation = m.SupportsSaturation && m.SaturationRestoreSafe; s.Saturation = m.SaturationCurrent;
                        s.HasSharpness = m.SupportsSharpness; s.Sharpness = m.SharpnessCurrent;
                        originals[m.Id] = s;
                    }
                }
                // v5.0.2 (H5 seed 50375): a re-probe that cannot read a value this controller wrote (slow or failing monitor) must
                // not hide it from the restore: keep the earlier range and mark the value unknown, so the original is written back
                foreach (DdcMonitor m in list)
                {
                    if (!writtenHere.Contains(m.Id)) continue;
                    DdcMonitor o = null;
                    foreach (DdcMonitor x in old) if (string.Equals(x.Id, m.Id, StringComparison.OrdinalIgnoreCase)) { o = x; break; }
                    if (o == null) continue;
                    if (o.SupportsBrightness && !m.SupportsBrightness)
                    { m.Capabilities |= 0x2u; m.BrightnessMin = o.BrightnessMin; m.BrightnessMax = o.BrightnessMax; m.BrightnessCurrent = o.BrightnessCurrent; unknown.Add(Key(m.Id, VcpBrightness)); }
                    if (o.SupportsContrast && !m.SupportsContrast)
                    { m.Capabilities |= 0x4u; m.ContrastMin = o.ContrastMin; m.ContrastMax = o.ContrastMax; m.ContrastCurrent = o.ContrastCurrent; unknown.Add(Key(m.Id, VcpContrast)); }
                    if (o.SupportsSharpness && !m.SupportsSharpness)
                    { m.SharpnessMin = o.SharpnessMin; m.SharpnessMax = o.SharpnessMax; m.SharpnessCurrent = o.SharpnessCurrent; unknown.Add(Key(m.Id, VcpSharpness)); }
                    if (o.SupportsSaturation && o.SaturationRestoreSafe && !m.SupportsSaturation)
                    { m.SaturationMin = o.SaturationMin; m.SaturationMax = o.SaturationMax; m.SaturationCurrent = o.SaturationCurrent; m.SaturationRestoreSafe = true; unknown.Add(Key(m.Id, VcpSaturation)); }
                }
                monitors = list;
                LastError = error ?? "";
                return string.IsNullOrEmpty(error);
            }
        }

        public DdcSnapshot GetOriginal(string id)
        {
            lock (sync) { DdcSnapshot s; return originals.TryGetValue(id ?? "", out s) ? s : null; }
        }

        /// <summary>Copies of every recorded original (write these to the crash-recovery file).</summary>
        public List<DdcSnapshot> GetOriginals()
        {
            lock (sync)
            {
                List<DdcSnapshot> list = new List<DdcSnapshot>();
                foreach (DdcSnapshot s in originals.Values) list.Add(s.Clone());
                return list;
            }
        }

        /// <summary>
        /// After a crash: install originals read from the recovery file BEFORE the first Refresh, so the
        /// still-changed monitor values are not mistaken for the originals. Write-once: an Id already
        /// recorded is kept.
        /// </summary>
        public void SeedOriginals(IEnumerable<DdcSnapshot> saved)
        {
            if (saved == null) return;
            lock (sync)
            {
                foreach (DdcSnapshot s in saved)
                    if (s != null && !string.IsNullOrEmpty(s.Id) && !originals.ContainsKey(s.Id))
                    {
                        originals[s.Id] = s.Clone();
                        dirty.Add(s.Id); // a crash left this monitor changed: it must be put back (or stay on record)
                    }
            }
        }

        /// <summary>True when any monitor value differs from its recorded original (recovery file still needed).</summary>
        public bool HasChanges
        {
            get
            {
                lock (sync)
                {
                    if (unknown.Count > 0) return true;
                    foreach (string id in dirty)
                    {
                        bool present = false;
                        foreach (DdcMonitor m in monitors) if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) { present = true; break; }
                        if (!present) return true;
                    }
                    foreach (DdcMonitor m in monitors)
                    {
                        DdcSnapshot s;
                        if (!originals.TryGetValue(m.Id, out s)) continue;
                        if ((s.HasBrightness && m.SupportsBrightness && m.BrightnessCurrent != s.Brightness) ||
                            (s.HasContrast && m.SupportsContrast && m.ContrastCurrent != s.Contrast) ||
                            (s.HasSaturation && m.SupportsSaturation && m.SaturationCurrent != s.Saturation) ||
                            (s.HasSharpness && m.SupportsSharpness && m.SharpnessCurrent != s.Sharpness)) return true;
                    }
                    return false;
                }
            }
        }

        public DdcValue GetBrightness(string id) { return Get(id, VcpBrightness); }
        public DdcValue GetContrast(string id) { return Get(id, VcpContrast); }
        public DdcValue GetSaturation(string id) { return Get(id, VcpSaturation); }
        /// <summary>Hardware sharpness (VCP 0x87). Supported=false -> hide the slider.</summary>
        public DdcValue GetSharpness(string id) { return Get(id, VcpSharpness); }

        private DdcValue Get(string id, byte code)
        {
            DdcValue v = new DdcValue();
            DdcMonitor m = Find(id);
            if (m == null) { v.Reason = "monitor not found"; return v; }
            lock (sync)
            {
                if (code == VcpBrightness && m.SupportsBrightness) { v.Supported = true; v.Min = m.BrightnessMin; v.Current = m.BrightnessCurrent; v.Max = m.BrightnessMax; }
                else if (code == VcpContrast && m.SupportsContrast) { v.Supported = true; v.Min = m.ContrastMin; v.Current = m.ContrastCurrent; v.Max = m.ContrastMax; }
                else if (code == VcpSaturation && m.SupportsSaturation) { v.Supported = true; v.Min = m.SaturationMin; v.Current = m.SaturationCurrent; v.Max = m.SaturationMax; }
                else if (code == VcpSharpness && m.SupportsSharpness) { v.Supported = true; v.Min = m.SharpnessMin; v.Current = m.SharpnessCurrent; v.Max = m.SharpnessMax; }
            }
            v.Reason = v.Supported ? "" : NotSupportedText + " (" + (m.ProbeNote ?? "") + ")";
            return v;
        }

        public DdcMonitor Find(string idOrDevice)
        {
            lock (sync)
            {
                foreach (DdcMonitor m in monitors)
                    if (string.Equals(m.Id, idOrDevice, StringComparison.OrdinalIgnoreCase)) return m;
                foreach (DdcMonitor m in monitors)
                    if (string.Equals(m.DisplayDeviceName, idOrDevice, StringComparison.OrdinalIgnoreCase)) return m;
                return null;
            }
        }

        // ---- writes (raw monitor units) ----
        public bool SetBrightness(string id, uint value, out string error)
        {
            return Write(id, VcpBrightness, value, out error);
        }
        public bool SetContrast(string id, uint value, out string error)
        {
            return Write(id, VcpContrast, value, out error);
        }
        /// <summary>Hardware saturation (VCP 0x8A), only on monitors that passed ShadePilot's double-read probe.</summary>
        public bool SetSaturation(string id, uint value, out string error)
        {
            return Write(id, VcpSaturation, value, out error);
        }

        /// <summary>Hardware sharpness (VCP 0x87), raw monitor units, only when SupportsSharpness.</summary>
        public bool SetSharpness(string id, uint value, out string error)
        {
            return Write(id, VcpSharpness, value, out error);
        }

        /// <summary>Contrast as ShadePilot WPF shows it: -50..50, 0 = middle of the monitor's range.</summary>
        public bool SetContrastNeutralZero(string id, int value, out string error)
        {
            DdcMonitor m = Find(id);
            if (m == null) { error = "monitor not found"; return false; }
            return SetContrast(id, FromNeutralZero(value, m.ContrastMin, m.ContrastMax), out error);
        }
        /// <summary>Saturation as ShadePilot stores it: 0..100, 50 = middle of the monitor's range.</summary>
        public bool SetSaturationNeutral50(string id, int value, out string error)
        {
            DdcMonitor m = Find(id);
            if (m == null) { error = "monitor not found"; return false; }
            return SetSaturation(id, FromNeutral50(value, m.SaturationMin, m.SaturationMax), out error);
        }

        public static double ToNeutralZero(uint value, uint min, uint max)
        {
            return max <= min ? 0 : Math.Max(-50.0, Math.Min(50.0, (value - (double)min) * 100.0 / (max - min) - 50.0));
        }
        public static uint FromNeutralZero(int value, uint min, uint max)
        {
            return max <= min ? min : (uint)Math.Round(min + (Math.Max(-50, Math.Min(50, value)) + 50) / 100.0 * (max - min));
        }
        public static double ToNeutral50(uint value, uint min, uint max)
        {
            return max <= min ? 50 : Math.Max(0.0, Math.Min(100.0, (value - (double)min) * 100.0 / (max - min)));
        }
        public static uint FromNeutral50(int value, uint min, uint max)
        {
            return max <= min ? min : (uint)Math.Round(min + Math.Max(0, Math.Min(100, value)) / 100.0 * (max - min));
        }

        /// <summary>Write back every value recorded at first probe that differs now, or whose value is unknown (ShadePilot
        /// Snapshot.Apply). v5.0.2: false also when a monitor this controller changed is not found any more (keep the record).</summary>
        public bool RestoreOriginals(out string error)
        {
            lock (sync)
            {
                List<string> failures = new List<string>();
                foreach (DdcMonitor m in monitors)
                {
                    DdcSnapshot s;
                    if (!originals.TryGetValue(m.Id, out s)) continue;
                    int before = failures.Count;
                    RestoreOne(m, VcpBrightness, s.HasBrightness && m.SupportsBrightness, m.BrightnessCurrent, s.Brightness, failures);
                    RestoreOne(m, VcpContrast, s.HasContrast && m.SupportsContrast, m.ContrastCurrent, s.Contrast, failures);
                    RestoreOne(m, VcpSaturation, s.HasSaturation && m.SupportsSaturation && m.SaturationRestoreSafe, m.SaturationCurrent, s.Saturation, failures);
                    RestoreOne(m, VcpSharpness, s.HasSharpness && m.SupportsSharpness, m.SharpnessCurrent, s.Sharpness, failures);
                    if (failures.Count == before)
                    {
                        dirty.Remove(m.Id);
                        foreach (byte c in new byte[] { VcpBrightness, VcpContrast, VcpSaturation, VcpSharpness }) unknown.Remove(Key(m.Id, c));
                    }
                }
                foreach (string id in new List<string>(dirty))
                {
                    bool present = false;
                    foreach (DdcMonitor m in monitors) if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) { present = true; break; }
                    if (!present) failures.Add(id + ": monitor not found (its saved values are kept for later)");
                }
                error = string.Join("; ", failures.ToArray());
                return failures.Count == 0;
            }
        }

        private void RestoreOne(DdcMonitor m, byte code, bool has, uint current, uint original, List<string> failures)
        {
            if (!has) return;
            if (current == original && !unknown.Contains(Key(m.Id, code))) return;
            string e;
            if (!WriteLocked(m, code, original, out e)) failures.Add(m.Name + ": " + e);
            Sleep(80);
        }

        private bool Touched(string id, byte code) { return unknown.Contains(Key(id, code)) || writtenHere.Contains(id); }

        public void Dispose()
        {
            lock (sync)
            {
                if (RestoreOnDispose) { string e; try { RestoreOriginals(out e); } catch { } }
                ReleaseHandles();
                monitors = new List<DdcMonitor>();
            }
        }

        private bool Write(string id, byte code, uint value, out string error)
        {
            lock (sync)
            {
                DdcMonitor m = Find(id);
                if (m == null) { error = "monitor not found"; return false; }
                bool ok = WriteLocked(m, code, value, out error);
                if (!ok) LastError = error;
                return ok;
            }
        }

        private bool WriteLocked(DdcMonitor m, byte code, uint value, out string error)
        {
            error = "";
            bool ok = WriteLockedCore(m, code, value, out error);
            string k = Key(m.Id, code);
            if (ok) unknown.Remove(k);
            else if (error.IndexOf("not supported", StringComparison.Ordinal) < 0) unknown.Add(k); // the write may still land
            return ok;
        }

        private bool WriteLockedCore(DdcMonitor m, byte code, uint value, out string error)
        {
            error = "";
            dirty.Add(m.Id);
            writtenHere.Add(m.Id);
            try
            {
                if (code == VcpBrightness)
                {
                    if (!m.SupportsBrightness) { error = "brightness not supported"; return false; }
                    m.BrightnessCurrent = WriteAndVerifyVcp(m, code, value, m.BrightnessCurrent, m.BrightnessMax, true, "\u4eae\u5ea6");
                }
                else if (code == VcpContrast)
                {
                    if (!m.SupportsContrast) { error = "contrast not supported"; return false; }
                    m.ContrastCurrent = WriteAndVerifyVcp(m, code, value, m.ContrastCurrent, m.ContrastMax, true, "\u5bf9\u6bd4\u5ea6");
                }
                else if (code == VcpSharpness)
                {
                    if (!m.SupportsSharpness) { error = "sharpness not supported"; return false; }
                    m.SharpnessCurrent = WriteAndVerifyVcp(m, code, value, m.SharpnessCurrent, m.SharpnessMax, false, "\u9510\u5ea6");
                }
                else
                {
                    if (!m.SupportsSaturation) { error = "saturation not supported"; return false; }
                    m.SaturationCurrent = WriteAndVerifyVcp(m, code, value, m.SaturationCurrent, m.SaturationMax, false, "\u9971\u548c\u5ea6");
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // Port of ShadePilot PhysicalDisplay.Refresh (lines 4097-4180).
        private void Probe(DdcMonitor m)
        {
            uint caps;
            bool capabilityQueryWorked = backend.GetCapabilities(m.Handle, out caps);
            if (!capabilityQueryWorked) caps = 0;
            m.Capabilities = caps;

            bool brightnessRead = m.SupportsBrightness &&
                backend.GetBrightness(m.Handle, out m.BrightnessMin, out m.BrightnessCurrent, out m.BrightnessMax);
            if (!brightnessRead)
            {
                uint current, maximum; bool type;
                if (backend.GetVcp(m.Handle, VcpBrightness, out type, out current, out maximum) && maximum > 0)
                {
                    m.BrightnessMin = 0; m.BrightnessCurrent = current; m.BrightnessMax = maximum;
                    m.Capabilities |= 0x2u; brightnessRead = true;
                }
                else m.Capabilities &= ~0x2u;
            }

            bool contrastRead = m.SupportsContrast &&
                backend.GetContrast(m.Handle, out m.ContrastMin, out m.ContrastCurrent, out m.ContrastMax);
            if (!contrastRead)
            {
                uint current, maximum; bool type;
                if (backend.GetVcp(m.Handle, VcpContrast, out type, out current, out maximum) && maximum > 0)
                {
                    m.ContrastMin = 0; m.ContrastCurrent = current; m.ContrastMax = maximum;
                    m.Capabilities |= 0x4u; contrastRead = true;
                }
                else m.Capabilities &= ~0x4u;
            }

            uint saturationCurrent, saturationMaximum; bool saturationType;
            bool saturationRead = backend.GetVcp(m.Handle, VcpSaturation, out saturationType, out saturationCurrent, out saturationMaximum);
            Sleep(70);
            uint confirmedCurrent = 0, confirmedMaximum = 0; bool confirmedType = false;
            bool saturationConfirmed = saturationRead &&
                backend.GetVcp(m.Handle, VcpSaturation, out confirmedType, out confirmedCurrent, out confirmedMaximum);
            if (saturationConfirmed && saturationType && confirmedType == saturationType &&
                saturationMaximum == confirmedMaximum && saturationCurrent == confirmedCurrent &&
                saturationMaximum >= 10 && saturationMaximum <= 255 && saturationCurrent <= saturationMaximum)
            {
                m.SaturationMin = 0; m.SaturationCurrent = saturationCurrent; m.SaturationMax = saturationMaximum;
                m.SaturationRestoreSafe = true;
            }
            else
            {
                m.SaturationMin = m.SaturationCurrent = m.SaturationMax = 0;
                m.SaturationRestoreSafe = false;
            }
            // Sharpness VCP 0x87 (TarkovShade addition): same double-read safety rule as saturation,
            // with max 2..255 (some monitors expose only a few steps).
            uint shCur, shMax, shCur2 = 0, shMax2 = 0; bool shType, shType2 = false;
            bool shRead = backend.GetVcp(m.Handle, VcpSharpness, out shType, out shCur, out shMax);
            Sleep(70);
            bool shConfirmed = shRead && backend.GetVcp(m.Handle, VcpSharpness, out shType2, out shCur2, out shMax2);
            if (shConfirmed && shType && shType2 && shMax == shMax2 && shCur == shCur2 &&
                shMax >= 2 && shMax <= 255 && shCur <= shMax)
            { m.SharpnessMin = 0; m.SharpnessCurrent = shCur; m.SharpnessMax = shMax; }
            else m.SharpnessMin = m.SharpnessCurrent = m.SharpnessMax = 0;

            m.ProbeNote = capabilityQueryWorked ? "high-level DDC/CI query ok"
                : (brightnessRead || contrastRead ? "high-level query failed, low-level VCP fallback used"
                : "no usable DDC/CI values");
        }

        private static void MarkProbeFailed(DdcMonitor m)
        {
            m.Capabilities = 0;
            m.SaturationMin = m.SaturationCurrent = m.SaturationMax = 0;
            m.SaturationRestoreSafe = false;
            m.SharpnessMin = m.SharpnessCurrent = m.SharpnessMax = 0;
            m.ProbeNote = "DDC/CI probe failed; hardware writes disabled";
        }

        // Port of ShadePilot WriteAndVerifyVcp (lines 4221-4258). Standard VCP first; the
        // high-level setter is only a fallback (saturation has no high-level setter).
        private uint WriteAndVerifyVcp(DdcMonitor m, byte code, uint requested, uint before,
            uint knownMaximum, bool hasHighLevel, string label)
        {
            bool writeReturnedSuccess = backend.SetVcp(m.Handle, code, requested);
            Sleep(300);
            uint actual, maximum; bool type;
            bool readWorked = backend.GetVcp(m.Handle, code, out type, out actual, out maximum);

            if (!writeReturnedSuccess || (readWorked && requested != before && actual == before))
            {
                bool fallbackReturnedSuccess = false;
                if (hasHighLevel)
                    fallbackReturnedSuccess = code == VcpBrightness
                        ? backend.SetBrightness(m.Handle, requested)
                        : backend.SetContrast(m.Handle, requested);
                Sleep(300);
                readWorked = backend.GetVcp(m.Handle, code, out type, out actual, out maximum);
                if (!fallbackReturnedSuccess && !writeReturnedSuccess)
                    throw new InvalidOperationException(label + " write failed (Win32 " + Marshal.GetLastWin32Error() + ")");
            }
            if (!readWorked)
                throw new InvalidOperationException(label + ": written but could not be read back; the monitor or cable may block DDC/CI writes.");

            uint effectiveMaximum = maximum > 0 ? maximum : knownMaximum;
            uint tolerance = Math.Max(1u, effectiveMaximum / 50u);
            uint difference = actual > requested ? actual - requested : requested - actual;
            if (requested != before && actual == before)
                throw new InvalidOperationException(label + " is still " + actual +
                    "; the monitor said OK but did nothing (turn off HDR / dynamic contrast / locked game picture mode).");
            if (difference > tolerance)
                throw new InvalidOperationException(label + ": requested " + requested + ", monitor reads " + actual +
                    "; the firmware may limit or quantise this value.");
            return actual;
        }

        private void ReleaseHandles()
        {
            foreach (DdcMonitor m in monitors)
            {
                if (m.Handle != IntPtr.Zero)
                {
                    try { backend.Destroy(m.Handle); } catch { }
                    m.Handle = IntPtr.Zero;
                }
            }
        }
    }

    /// <summary>Real DDC/CI backend: dxva2.dll + user32.dll, both resolved from System32 only.</summary>
    public sealed class Dxva2DdcBackend : IDdcBackend
    {
        public List<DdcMonitorHandle> Enumerate(out string error)
        {
            error = "";
            List<DdcMonitorHandle> result = new List<DdcMonitorHandle>();
            int logicalIndex = 0;
            NativeMethods.MonitorEnumProc callback = delegate(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
            {
                NativeMethods.MONITORINFOEX info = new NativeMethods.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                string device = NativeMethods.GetMonitorInfo(hMonitor, ref info) ? info.szDevice : null;
                uint count;
                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) { logicalIndex++; return true; }
                NativeMethods.PHYSICAL_MONITOR[] physical = new NativeMethods.PHYSICAL_MONITOR[count];
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) { logicalIndex++; return true; }
                for (int i = 0; i < physical.Length; i++)
                {
                    DdcMonitorHandle h = new DdcMonitorHandle();
                    h.Handle = physical[i].hPhysicalMonitor;
                    h.Name = string.IsNullOrWhiteSpace(physical[i].szPhysicalMonitorDescription)
                        ? "Monitor " + (logicalIndex + 1) : physical[i].szPhysicalMonitorDescription;
                    h.DisplayDeviceName = device;
                    h.Id = (string.IsNullOrEmpty(device) ? "desktop-monitor" : device) + "|physical:" + i;
                    result.Add(h);
                }
                logicalIndex++;
                return true;
            };
            try
            {
                if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                    error = "EnumDisplayMonitors failed (Win32 " + Marshal.GetLastWin32Error() + ")";
            }
            catch (Exception ex) { error = "DDC/CI unavailable: " + ex.Message; }
            GC.KeepAlive(callback);
            return result;
        }

        public bool GetCapabilities(IntPtr monitor, out uint caps)
        {
            uint temps;
            return NativeMethods.GetMonitorCapabilities(monitor, out caps, out temps);
        }
        public bool GetBrightness(IntPtr monitor, out uint min, out uint current, out uint max)
        { return NativeMethods.GetMonitorBrightness(monitor, out min, out current, out max); }
        public bool SetBrightness(IntPtr monitor, uint value) { return NativeMethods.SetMonitorBrightness(monitor, value); }
        public bool GetContrast(IntPtr monitor, out uint min, out uint current, out uint max)
        { return NativeMethods.GetMonitorContrast(monitor, out min, out current, out max); }
        public bool SetContrast(IntPtr monitor, uint value) { return NativeMethods.SetMonitorContrast(monitor, value); }
        public bool GetVcp(IntPtr monitor, byte code, out bool setParameter, out uint current, out uint max)
        {
            uint type;
            bool ok = NativeMethods.GetVCPFeatureAndVCPFeatureReply(monitor, code, out type, out current, out max);
            setParameter = type == 1; // MC_SET_PARAMETER
            return ok;
        }
        public bool SetVcp(IntPtr monitor, byte code, uint value) { return NativeMethods.SetVCPFeature(monitor, code, value); }
        public void Destroy(IntPtr monitor) { NativeMethods.DestroyPhysicalMonitor(monitor); }

        private static class NativeMethods
        {
            internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

            [StructLayout(LayoutKind.Sequential)]
            internal struct RECT { public int Left, Top, Right, Bottom; }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct PHYSICAL_MONITOR
            {
                public IntPtr hPhysicalMonitor;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string szPhysicalMonitorDescription;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct MONITORINFOEX
            {
                public int cbSize;
                public RECT rcMonitor;
                public RECT rcWork;
                public uint dwFlags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string szDevice;
            }

            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint size, [Out] PHYSICAL_MONITOR[] monitors);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool GetMonitorCapabilities(IntPtr monitor, out uint caps, out uint temperatures);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool GetMonitorContrast(IntPtr monitor, out uint min, out uint current, out uint max);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool SetMonitorContrast(IntPtr monitor, uint value);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool GetVCPFeatureAndVCPFeatureReply(
                IntPtr monitor, byte vcpCode, out uint codeType, out uint currentValue, out uint maximumValue);
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [DllImport("dxva2.dll", SetLastError = true)]
            internal static extern bool SetVCPFeature(IntPtr monitor, byte vcpCode, uint newValue);
        }
    }
}
