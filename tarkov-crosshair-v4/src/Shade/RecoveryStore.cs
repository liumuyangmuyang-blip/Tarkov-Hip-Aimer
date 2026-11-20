// TarkovShade - fixed-preset display filter module.
// Crash recovery, ported in behaviour from TarkovAutoShade src/RecoveryStore.cs
// (save the ORIGINAL ramp once before the first change, restore it at the next start after
// a crash, clear it after a clean restore), Copyright (c) 2026 TarkovAutoShade contributors,
// MIT License. https://github.com/lu1249765686/TarkovAutoShade - see LICENSE-THIRD-PARTY.txt
// Changes: JSON file "shade-recovery.json" in a caller-chosen folder (next to the exe by
// default) instead of %LOCALAPPDATA%\TarkovAutoShade\gamma-recovery.bin, plus entries for the
// original driver vibrance level and the Magnification colour effect.
// v5.0.2 (crosshair W15, BRUTE P2-1, AUDIT F9): every write goes to shade-recovery.json AND shade-recovery.json.bak, both flushed
// to disk; a damaged file falls back to the .bak; a file that cannot be read at all is moved to shade-recovery.bad-<time>.json
// (never overwritten) and reported (Damaged); the data is kept in memory after the first load (no re-parse per apply).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace TarkovShade
{
    [DataContract]
    public sealed class RecoveryRamp
    {
        [DataMember(Order = 0)] public string Device { get; set; }
        [DataMember(Order = 1)] public ushort[] Red { get; set; }
        [DataMember(Order = 2)] public ushort[] Green { get; set; }
        [DataMember(Order = 3)] public ushort[] Blue { get; set; }
    }

    [DataContract]
    public sealed class RecoveryVibrance
    {
        [DataMember(Order = 0)] public string Device { get; set; }
        [DataMember(Order = 1)] public string Vendor { get; set; }
        [DataMember(Order = 2)] public int Level { get; set; }
    }

    [DataContract]
    public sealed class RecoveryData
    {
        public const int CurrentVersion = 1;
        [DataMember(Order = 0)] public int Version { get; set; }
        [DataMember(Order = 1)] public List<RecoveryRamp> Ramps { get; set; }
        [DataMember(Order = 2)] public List<RecoveryVibrance> Vibrance { get; set; }
        /// <summary>True while a Magnification colour effect may be active; next start resets it to identity.</summary>
        [DataMember(Order = 3)] public bool LocalSaturationActive { get; set; }
        /// <summary>Monitor hardware (DDC/CI) originals, one per monitor id (Final A).</summary>
        [DataMember(Order = 4)] public List<DdcSnapshot> Ddc { get; set; }

        public static RecoveryData CreateEmpty()
        {
            var d = new RecoveryData();
            d.Version = CurrentVersion;
            d.Ramps = new List<RecoveryRamp>();
            d.Vibrance = new List<RecoveryVibrance>();
            d.Ddc = new List<DdcSnapshot>();
            return d;
        }
    }

    /// <summary>
    /// Write-once-per-device store of the ORIGINAL display state. An entry that is already in the
    /// file is never overwritten, so a crash after a filter was applied cannot replace the true
    /// original with a filtered ramp.
    /// </summary>
    public sealed class RecoveryStore
    {
        public const string FileName = "shade-recovery.json";
        private readonly object sync = new object();
        private readonly string filePath;

        public RecoveryStore(string folder)
        {
            filePath = Path.Combine(folder, FileName);
        }

        public string FilePath { get { return filePath; } }

        /// <summary>Last write error ("" after a successful write). Writes never throw.</summary>
        public string LastWriteError { get; private set; }

        // In-memory copy of the file (AUDIT F9). state: 0 = not read yet, 1 = no file, 2 = cache holds the file's data.
        private int cacheState;
        private RecoveryData cache;
        private string cacheStamp = "";

        // one stat per load: the cache is used only while the file is the one it was made from (size + time)
        private string Stamp()
        {
            try
            {
                FileInfo f = new FileInfo(filePath);
                return f.Exists ? f.Length + "|" + f.LastWriteTimeUtc.Ticks : (File.Exists(filePath + ".tmp") || File.Exists(BackupPath) ? "other" : "none");
            }
            catch (Exception) { return "?"; }
        }

        /// <summary>True when the last load found a recovery file (or its backup) that could not be read; it was moved to BadFilePath.</summary>
        public bool Damaged { get; private set; }
        /// <summary>Where the unreadable file was moved ("" when none).</summary>
        public string BadFilePath { get; private set; }

        public string BackupPath { get { return filePath + ".bak"; } }

        private bool Write(RecoveryData data)
        {
            try
            {
                JsonFile.Write(filePath, data, true);
                LastWriteError = "";
            }
            catch (Exception ex)
            {
                LastWriteError = "Crash-recovery file not written: " + ex.Message;
                cacheState = 0; // unknown what is on disk now: read it again next time
                return false;
            }
            try { JsonFile.Write(BackupPath, data, true); }
            catch (Exception) { } // the main file is written; the backup is a second chance only
            cache = data;
            cacheState = 2;
            cacheStamp = Stamp();
            return true;
        }

        public bool Exists
        {
            get { return File.Exists(filePath) || File.Exists(filePath + ".tmp") || File.Exists(BackupPath); }
        }

        private static RecoveryData Copy(RecoveryData d)
        {
            RecoveryData c = RecoveryData.CreateEmpty();
            c.Version = d.Version;
            c.LocalSaturationActive = d.LocalSaturationActive;
            c.Ramps.AddRange(d.Ramps);
            c.Vibrance.AddRange(d.Vibrance);
            c.Ddc.AddRange(d.Ddc);
            return c;
        }

        public bool AddRamps(IDictionary<string, GammaRamp> ramps)
        {
            if (ramps == null || ramps.Count == 0) return true;
            lock (sync)
            {
                RecoveryData data = LoadOrEmpty();
                bool changed = false;
                foreach (KeyValuePair<string, GammaRamp> item in ramps)
                {
                    if (string.IsNullOrWhiteSpace(item.Key) || !item.Value.IsValid) continue;
                    if (FindRamp(data, item.Key) != null) continue;
                    var r = new RecoveryRamp();
                    r.Device = item.Key;
                    r.Red = (ushort[])item.Value.Red.Clone();
                    r.Green = (ushort[])item.Value.Green.Clone();
                    r.Blue = (ushort[])item.Value.Blue.Clone();
                    data.Ramps.Add(r);
                    changed = true;
                }
                return !changed || Write(data);
            }
        }

        public bool AddVibrance(string device, string vendor, int level)
        {
            if (string.IsNullOrWhiteSpace(device)) return true;
            lock (sync)
            {
                RecoveryData data = LoadOrEmpty();
                foreach (RecoveryVibrance v in data.Vibrance)
                    if (string.Equals(v.Device, device, StringComparison.OrdinalIgnoreCase)) return true;
                var entry = new RecoveryVibrance();
                entry.Device = device;
                entry.Vendor = vendor ?? "";
                entry.Level = level;
                data.Vibrance.Add(entry);
                return Write(data);
            }
        }

        public bool MarkLocalSaturation()
        {
            lock (sync)
            {
                RecoveryData data = LoadOrEmpty();
                if (data.LocalSaturationActive) return true;
                data.LocalSaturationActive = true;
                return Write(data);
            }
        }

        /// <summary>Adds DDC originals for monitors not yet in the file (write-once per monitor id).</summary>
        public bool AddDdc(IEnumerable<DdcSnapshot> originals)
        {
            if (originals == null) return true;
            lock (sync)
            {
                RecoveryData data = LoadOrEmpty();
                bool changed = false;
                foreach (DdcSnapshot o in originals)
                {
                    if (o == null || string.IsNullOrWhiteSpace(o.Id)) continue;
                    int found = -1;
                    for (int i = 0; i < data.Ddc.Count; i++)
                        if (string.Equals(data.Ddc[i].Id, o.Id, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
                    if (found < 0) { data.Ddc.Add(o.Clone()); changed = true; continue; }
                    // v5.0.2 (BRUTE P1-2 d): a value the first probe could not read is added once it is known (the values already
                    // in the file are never replaced)
                    DdcSnapshot e = data.Ddc[found], m = null;
                    if (!e.HasBrightness && o.HasBrightness) { m = m ?? e.Clone(); m.HasBrightness = true; m.Brightness = o.Brightness; }
                    if (!e.HasContrast && o.HasContrast) { m = m ?? e.Clone(); m.HasContrast = true; m.Contrast = o.Contrast; }
                    if (!e.HasSaturation && o.HasSaturation) { m = m ?? e.Clone(); m.HasSaturation = true; m.Saturation = o.Saturation; }
                    if (!e.HasSharpness && o.HasSharpness) { m = m ?? e.Clone(); m.HasSharpness = true; m.Sharpness = o.Sharpness; }
                    if (m != null) { data.Ddc[found] = m; changed = true; }
                }
                return !changed || Write(data);
            }
        }

        /// <summary>Removes the DDC entries (after the monitors were restored); deletes the file when nothing else is left.</summary>
        public bool ClearDdc()
        {
            lock (sync)
            {
                RecoveryData data;
                if (!TryLoad(out data) || data.Ddc.Count == 0) return true;
                data.Ddc.Clear();
                if (data.Ramps.Count == 0 && data.Vibrance.Count == 0 && !data.LocalSaturationActive) { Clear(); return true; }
                return Write(data);
            }
        }

        public bool TryGetVibrance(string device, out RecoveryVibrance entry)
        {
            entry = null;
            RecoveryData data;
            if (!TryLoad(out data)) return false;
            foreach (RecoveryVibrance v in data.Vibrance)
            {
                if (string.Equals(v.Device, device, StringComparison.OrdinalIgnoreCase))
                {
                    entry = v;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Loads and validates the file (from memory after the first load). Invalid ramp entries (wrong length) are dropped.
        /// A damaged file falls back to its .bak; when neither can be read, the file is moved to shade-recovery.bad-*.json, Damaged
        /// is set and false is returned (the caller must not take the current screen as the original then).</summary>
        public bool TryLoad(out RecoveryData data)
        {
            data = null;
            lock (sync)
            {
                string stamp = Stamp();
                if (cacheState == 1 && stamp == cacheStamp) return false;
                if (cacheState == 2 && cache != null && stamp == cacheStamp) { data = Copy(cache); return true; }
                bool mainThere = File.Exists(filePath) || File.Exists(filePath + ".tmp");
                bool bakThere = File.Exists(BackupPath);
                RecoveryData raw = mainThere ? JsonFile.Read<RecoveryData>(filePath) : null;
                if (raw == null && bakThere) raw = JsonFile.Read<RecoveryData>(BackupPath);
                if (raw == null)
                {
                    if (mainThere || bakThere) MoveBad(mainThere);
                    cacheState = 1;
                    cache = null;
                    cacheStamp = Stamp();
                    return false;
                }
                RecoveryData clean = Clean(raw);
                cache = clean;
                cacheState = 2;
                cacheStamp = stamp;
                data = Copy(clean);
                return true;
            }
        }

        private void MoveBad(bool mainThere)
        {
            Damaged = true;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string folder = Path.GetDirectoryName(filePath) ?? "";
            string bad = Path.Combine(folder, "shade-recovery.bad-" + stamp + ".json");
            for (int i = 2; File.Exists(bad) && i < 100; i++) bad = Path.Combine(folder, "shade-recovery.bad-" + stamp + "-" + i + ".json");
            BadFilePath = "";
            foreach (string src in new string[] { filePath, filePath + ".tmp", BackupPath })
            {
                try
                {
                    if (!File.Exists(src)) continue;
                    string dst = src == filePath || (!mainThere && src == BackupPath) ? bad : bad + (src == BackupPath ? ".bak" : ".tmp");
                    if (File.Exists(dst)) dst = dst + "." + Guid.NewGuid().ToString("N").Substring(0, 6);
                    File.Move(src, dst);
                    if (BadFilePath.Length == 0) BadFilePath = dst;
                }
                catch (Exception) { }
            }
        }

        private static RecoveryData Clean(RecoveryData raw)
        {
            RecoveryData clean = RecoveryData.CreateEmpty();
            clean.LocalSaturationActive = raw.LocalSaturationActive;
            if (raw.Ramps != null)
            {
                foreach (RecoveryRamp r in raw.Ramps)
                {
                    if (r == null || string.IsNullOrWhiteSpace(r.Device) ||
                        r.Red == null || r.Green == null || r.Blue == null ||
                        r.Red.Length != 256 || r.Green.Length != 256 || r.Blue.Length != 256)
                        continue;
                    if (clean.Ramps.Count >= 32) break;
                    clean.Ramps.Add(r);
                }
            }
            if (raw.Vibrance != null)
            {
                foreach (RecoveryVibrance v in raw.Vibrance)
                    if (v != null && !string.IsNullOrWhiteSpace(v.Device)) clean.Vibrance.Add(v);
            }
            if (raw.Ddc != null)
            {
                foreach (DdcSnapshot d in raw.Ddc)
                    if (d != null && !string.IsNullOrWhiteSpace(d.Id)) clean.Ddc.Add(d);
            }
            return clean;
        }

        public Dictionary<string, GammaRamp> GetRamps(RecoveryData data)
        {
            var result = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            if (data == null || data.Ramps == null) return result;
            foreach (RecoveryRamp r in data.Ramps)
            {
                var ramp = new GammaRamp();
                ramp.Red = (ushort[])r.Red.Clone();
                ramp.Green = (ushort[])r.Green.Clone();
                ramp.Blue = (ushort[])r.Blue.Clone();
                result[r.Device] = ramp;
            }
            return result;
        }

        public void Clear()
        {
            lock (sync)
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    if (File.Exists(filePath + ".tmp")) File.Delete(filePath + ".tmp");
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    if (File.Exists(BackupPath + ".tmp")) File.Delete(BackupPath + ".tmp");
                    cacheState = 1;
                    cache = null;
                    cacheStamp = Stamp();
                }
                catch
                {
                    cacheState = 0;
                }
            }
        }

        private RecoveryData LoadOrEmpty()
        {
            RecoveryData data;
            if (!TryLoad(out data) || data == null) data = RecoveryData.CreateEmpty();
            return data;
        }

        private static RecoveryRamp FindRamp(RecoveryData data, string device)
        {
            foreach (RecoveryRamp r in data.Ramps)
                if (string.Equals(r.Device, device, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }
    }
}
