// TarkovShade - public API for the display filter (Final A: ShadePilot curve, PilotStyle presets).
// Apply / restore / crash-recovery flow follows TarkovAutoShade src-wpf/MainWindow.xaml.cs
// (save original ramps before the first change, restore on toggle-off / exit, restore after a
// crash at next start), Copyright (c) 2026 TarkovAutoShade contributors, MIT License.
// https://github.com/lu1249765686/TarkovAutoShade - see LICENSE-THIRD-PARTY.txt
// Curve: ShadePilot port (PilotCurve.cs), see LICENSE-SHADEPILOT.txt.
//
// Safety: no network, no other-process access, no injection, no screen capture, no input
// simulation. Hardware calls: SetDeviceGammaRamp (gdi32), the GPU driver's own vibrance API
// (optional), the Magnification colour effect (optional) and DDC/CI monitor controls (optional).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace TarkovShade
{
    [DataContract]
    public sealed class ShadeSettings
    {
        public const string FileName = "shade-settings.json";
        public const int CurrentVersion = 2;
        public const int ModAlt = HotkeyBinding.ModAlt, ModCtrl = HotkeyBinding.ModCtrl, ModShift = HotkeyBinding.ModShift;

        [DataMember(Order = 0)] public int Version { get; set; }
        [DataMember(Order = 1)] public string CurrentPresetName { get; set; }
        /// <summary>Working values (may differ from the saved preset until "save as preset").</summary>
        [DataMember(Order = 2)] public PilotStyle Current { get; set; }
        /// <summary>Device names (e.g. \\.\DISPLAY1). Empty = primary display only.</summary>
        [DataMember(Order = 3)] public List<string> SelectedDisplays { get; set; }
        [DataMember(Order = 4)] public bool SmoothTransition { get; set; }
        [DataMember(Order = 5)] public HotkeySettings Hotkeys { get; set; }

        public static ShadeSettings CreateDefault()
        {
            ShadeSettings s = new ShadeSettings();
            s.Version = CurrentVersion;
            s.CurrentPresetName = PilotBuiltIns.GeneralName;
            s.Current = PilotBuiltIns.General();
            s.SelectedDisplays = new List<string>();
            s.SmoothTransition = true;
            s.Hotkeys = HotkeySettings.CreateDefault();
            return s;
        }

        public void Normalize()
        {
            if (Version < CurrentVersion)
            {
                // v1 = retired ShadePreset build: its values mean nothing to the new curve. Keep displays / fade only.
                ShadeSettings d = CreateDefault();
                CurrentPresetName = d.CurrentPresetName;
                Current = d.Current;
                Hotkeys = d.Hotkeys;
                Version = CurrentVersion;
            }
            if (Current == null) Current = PilotBuiltIns.General();
            PilotPresetStore.EnforceFixed(Current);
            if (!string.IsNullOrWhiteSpace(CurrentPresetName)) CurrentPresetName = PilotPresetStore.CleanName(CurrentPresetName);
            if (string.IsNullOrWhiteSpace(CurrentPresetName)) CurrentPresetName = Current.Name;
            Current.Name = CurrentPresetName;
            if (SelectedDisplays == null) SelectedDisplays = new List<string>();
            List<string> clean = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in SelectedDisplays)
                if (!string.IsNullOrWhiteSpace(d) && seen.Add(d)) clean.Add(d);
            SelectedDisplays = clean;
            if (Hotkeys == null) Hotkeys = HotkeySettings.CreateDefault();
            Hotkeys.Normalize();
            if (Hotkeys.Validate() != null) Hotkeys = HotkeySettings.CreateDefault();
        }

        public ShadeSettings Clone()
        {
            ShadeSettings s = (ShadeSettings)MemberwiseClone();
            s.Current = Current == null ? null : Current.Clone();
            s.SelectedDisplays = SelectedDisplays == null ? new List<string>() : new List<string>(SelectedDisplays);
            s.Hotkeys = Hotkeys == null ? null : Hotkeys.Clone();
            return s;
        }
    }

    /// <summary>Monitor hardware values of the first target display (UI), raw monitor units (contrast slider: -50..50, see ShadeEngine.MonitorContrastNeutralZero).</summary>
    public sealed class MonitorHardware
    {
        public string MonitorId = "", Name = "";
        public DdcValue Brightness, Contrast, Sharpness;
    }

    public sealed partial class ShadeEngine : IDisposable
    {
        /// <summary>Single-owner lock file in the data folder (B-03/B-04).</summary>
        public const string LockFileName = "shade-engine.lock";

        private readonly object sync = new object();
        private readonly object reportSync = new object();
        private readonly Queue<string> pendingReports = new Queue<string>();
        private readonly string dataFolder;
        private readonly IGammaBackend gammaBackend;
        private readonly GammaRampController gamma;
        private readonly IVibranceBackend vibrance;
        private readonly ILocalSaturationBackend local;
        private readonly DdcCiController ddc;
        private readonly Func<List<DisplayTarget>> enumerateDisplays;
        private readonly PilotPresetStore presets;
        private readonly RecoveryStore recovery;
        private ShadeSettings settings = ShadeSettings.CreateDefault();
        private bool enabled;
        private bool started;
        private bool disposed;
        private bool holdsFolderLock;
        private bool localActive;
        private bool holding;
        private int heldKey;
        private bool ownsRecoveryEntries;
        private bool inheritedRecovery;
        private Dictionary<string, GammaRamp> inheritedRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private bool lastApplyGammaFailed;
        private readonly Dictionary<string, int> vibranceOriginals =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> vibranceChanged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // DDC: "monitorId|vcp" -> value last written by this session.
        private readonly Dictionary<string, int> hardwareWritten =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string lastError = "";
        // v5.0.2 (crosshair W15, BRUTE P2-1): the crash-recovery file was unreadable at start. Until the user restores, the
        // original is taken as Windows' standard (identity curve, the driver's default vibrance), never the still-filtered screen.
        private bool recoveryDamaged;
        private string recoveryNotice = "";
        // v5.0.2 (Leo: never re-apply unchanged values): last Magnification level written while active
        private double lastLocal = double.NaN;

        /// <summary>
        /// Raised with a short English status line after every state change or error. Raised after the
        /// engine lock is released, on the calling thread, or on a thread-pool thread when a smooth fade
        /// fails part-way (marshal to the UI thread with BeginInvoke).
        /// </summary>
        public event Action<string> StatusChanged;

        public ShadeEngine(string dataFolder, IGammaBackend gammaBackend, IVibranceBackend vibrance,
            ILocalSaturationBackend local, Func<List<DisplayTarget>> enumerateDisplays)
            : this(dataFolder, gammaBackend, vibrance, local, enumerateDisplays, null)
        {
        }

        /// <summary>ddcBackend null = no monitor hardware control.</summary>
        public ShadeEngine(string dataFolder, IGammaBackend gammaBackend, IVibranceBackend vibrance,
            ILocalSaturationBackend local, Func<List<DisplayTarget>> enumerateDisplays, IDdcBackend ddcBackend)
        {
            if (string.IsNullOrWhiteSpace(dataFolder)) throw new ArgumentException("dataFolder");
            if (gammaBackend == null) throw new ArgumentNullException("gammaBackend");
            if (enumerateDisplays == null) throw new ArgumentNullException("enumerateDisplays");
            this.dataFolder = dataFolder;
            this.gammaBackend = gammaBackend;
            this.gamma = new GammaRampController(gammaBackend);
            this.gamma.TransitionFailed += OnTransitionFailed;
            this.vibrance = vibrance ?? new UnsupportedVibrance("Not supported.");
            this.local = local;
            this.enumerateDisplays = enumerateDisplays;
            if (ddcBackend != null)
            {
                ddc = new DdcCiController(ddcBackend);
                ddc.RestoreOnDispose = true;
            }
            presets = new PilotPresetStore(dataFolder);
            recovery = new RecoveryStore(dataFolder);
        }

        // ------------------------------------------------------------ state

        public string DataFolder { get { return dataFolder; } }
        public bool IsEnabled { get { lock (sync) { return enabled; } } }
        public bool IsStarted { get { lock (sync) { return started && !disposed; } } }
        public bool IsHoldingOriginal { get { lock (sync) { return holding; } } }
        public string LastError { get { lock (sync) { return lastError; } } }
        /// <summary>v5.0.2: "" or an English line about the crash-recovery file found at Start (damaged: RecoveryDamaged).</summary>
        public string RecoveryNotice { get { lock (sync) { return recoveryNotice; } } }
        /// <summary>v5.0.2: the recovery file was unreadable; filter off / RestoreEverything put Windows' standard values back.</summary>
        public bool RecoveryDamaged { get { lock (sync) { return recoveryDamaged; } } }
        public string CurrentPresetName { get { lock (sync) { return settings.CurrentPresetName; } } }
        public string VibranceStatus { get { return vibrance.IsSupported ? vibrance.Status : "Driver vibrance: " + vibrance.Status; } }
        public bool VibranceSupported { get { return vibrance.IsSupported; } }
        public string LocalSaturationStatus
        {
            get { return local == null ? "Not available." : (local.IsSupported ? local.Status : "Not supported: needs Windows 8 or later."); }
        }
        public bool LocalSaturationSupported { get { return local != null && local.IsSupported; } }

        /// <summary>Forces the fixed fields (Tint 0, Exposure 0, strength 标准, backend driver/Windows) on a copy.</summary>
        public static PilotStyle EnforceFixed(PilotStyle style)
        {
            if (style == null) return null;
            PilotStyle s = style.Clone();
            PilotPresetStore.EnforceFixed(s);
            return s;
        }

        /// <summary>Copy of the working values. Set: forces fixed fields, saves and (if enabled) applies at once.</summary>
        public PilotStyle Current
        {
            get { lock (sync) { return settings.Current.Clone(); } }
            set { SetCurrent(value, true); }
        }

        public bool SmoothTransition
        {
            get { lock (sync) { return settings.SmoothTransition; } }
            set
            {
                lock (sync)
                {
                    if (!CanMutate()) return;
                    settings.SmoothTransition = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>Copy of the app settings (current values, hotkeys, monitors, smooth transition).</summary>
        public ShadeSettings Settings
        {
            get { lock (sync) { return settings.Clone(); } }
            set
            {
                if (value == null) return;
                try
                {
                    lock (sync)
                    {
                        if (!CanMutate()) return;
                        ShadeSettings s = value.Clone();
                        if (s.Version < ShadeSettings.CurrentVersion) s.Version = ShadeSettings.CurrentVersion;
                        s.Normalize();
                        settings = s;
                        SaveSettings();
                        if (enabled) ApplyAndReport(0);
                    }
                }
                finally { FlushReports(); }
            }
        }

        public List<PilotStyle> Presets { get { lock (sync) { return presets.All; } } }

        public List<DisplayTarget> GetDisplays()
        {
            try { return enumerateDisplays() ?? new List<DisplayTarget>(); }
            catch { return new List<DisplayTarget>(); }
        }

        // ------------------------------------------------------------ lifecycle

        /// <summary>
        /// Takes the data folder's single-owner lock, loads settings + presets and undoes any filter /
        /// monitor change left over by a crash. Returns a status line.
        /// </summary>
        public string Start()
        {
            try
            {
                lock (sync)
                {
                    if (disposed) { lastError = "Engine disposed."; return lastError; }
                    if (started) return "Already started.";
                    if (!holdsFolderLock)
                    {
                        string lockError;
                        if (!FolderLock.Acquire(dataFolder, out lockError))
                        {
                            lastError = lockError;
                            Report(lockError);
                            return lockError;
                        }
                        holdsFolderLock = true;
                    }
                    started = true;
                    ShadeSettings loaded = JsonFile.Read<ShadeSettings>(Path.Combine(dataFolder, ShadeSettings.FileName));
                    settings = loaded ?? ShadeSettings.CreateDefault();
                    settings.Normalize();
                    presets.Load();
                    // v5.1 round 2: the chosen preset was a retired early-v5.1 built-in that Load just dropped -> 通用
                    if (PilotBuiltIns.IsRetiredV51(settings.CurrentPresetName) && presets.IndexOf(settings.CurrentPresetName) < 0)
                    {
                        settings.CurrentPresetName = PilotBuiltIns.GeneralName;
                        settings.Current = PilotBuiltIns.General();
                    }
                    string message = RecoverPreviousSession();
                    SaveSettings();
                    Report(message.Length > 0 ? message : "Ready.");
                    return message;
                }
            }
            finally { FlushReports(); }
        }

        /// <summary>Restores the original display state (incl. monitor hardware) and saves settings.</summary>
        public void Stop()
        {
            try
            {
                lock (sync)
                {
                    if (disposed || !started) return;
                    RestoreOriginalInternal();
                    SaveSettings();
                }
            }
            finally { FlushReports(); }
        }

        public void Dispose()
        {
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    if (started) RestoreOriginalInternal();
                    disposed = true;
                    enabled = false;
                    gamma.Dispose();
                    if (ddc != null) { try { ddc.Dispose(); } catch { } }
                    IDisposable v = vibrance as IDisposable;
                    if (v != null) { try { v.Dispose(); } catch { } }
                    IDisposable l = local as IDisposable;
                    if (l != null) { try { l.Dispose(); } catch { } }
                    if (holdsFolderLock)
                    {
                        FolderLock.Release(dataFolder);
                        holdsFolderLock = false;
                    }
                }
            }
            finally { FlushReports(); }
        }

        // ------------------------------------------------------------ on / off

        public bool Enable()
        {
            try { lock (sync) { return EnableCore(); } }
            finally { FlushReports(); }
        }

        public bool Disable()
        {
            try { lock (sync) { return DisableCore(); } }
            finally { FlushReports(); }
        }

        public bool Toggle()
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    return enabled ? DisableCore() : EnableCore();
                }
            }
            finally { FlushReports(); }
        }

        /// <summary>Same as Disable.</summary>
        public bool RestoreOriginal() { return Disable(); }

        /// <summary>Ctrl+F12: gamma baseline + saturation + every monitor's DDC originals; filter off.</summary>
        public bool RestoreEverything()
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    bool damaged = recoveryDamaged;
                    bool ok = RestoreOriginalInternal();
                    if (damaged) ok &= RestoreStandard();
                    if (ddc != null && ddc.HasChanges)
                    {
                        string error;
                        if (!ddc.RestoreOriginals(out error)) { ok = false; AddError("Monitor restore: " + error); }
                    }
                    Report(ok ? "Everything restored (screen, saturation, monitor)." : "Restore problem: " + lastError);
                    return ok;
                }
            }
            finally { FlushReports(); }
        }

        /// <summary>Hold-to-see-original: puts the gamma baseline back at once (no fade). No-op when the filter is off.</summary>
        public bool BeginHoldOriginal()
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    if (!enabled || holding) return true;
                    holding = true;
                    string error;
                    if (!gamma.RestoreAll(out error)) { AddError(error); Report("Hold original: " + error); return false; }
                    Report("Showing original (hold).");
                    return true;
                }
            }
            finally { FlushReports(); }
        }

        /// <summary>Releases hold-to-see-original: the filter curve is written again at once.</summary>
        public bool EndHoldOriginal()
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    if (!holding) return true;
                    holding = false;
                    if (!enabled) return true;
                    bool ok = ApplyInternal(0);
                    if (!ok) Report("Filter not re-applied: " + lastError);
                    else Report("Filter on: " + settings.CurrentPresetName);
                    return ok;
                }
            }
            finally { FlushReports(); }
        }

        private string lastDdcDisplays; // v4.3: displays at the last DDC probe (RefreshDisplays)

        public bool RefreshDisplays()
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    // v4.3 (crosshair W2, V43-PERF TOP 1 d): DDC/CI re-probe only when the set of displays changed
                    // (alt+tab out of a fullscreen game fires this event 1-3 times; a probe can take seconds).
                    string displays = string.Join("|", GetTargetDevices().ToArray());
                    if (ddc != null && displays != lastDdcDisplays) { string e; ddc.Refresh(out e); lastDdcDisplays = displays; }
                    if (!enabled || holding) return true;
                    string error;
                    int n = gamma.Resync(out error);
                    if (n < 0)
                    {
                        // V43-PERF TOP 0 fix 2: Windows refused the re-write -> the filter is NOT on any more; say so once.
                        AddError(error);
                        string why = lastError;
                        RestoreOriginalInternal();
                        enabled = false;
                        lastError = why;
                        Report("Display change: Windows refused the gamma curve, filter turned off: " + why);
                        return false;
                    }
                    if (n == 0) return true; // nothing was reset (V43-PERF fix e): no second apply, no report
                    bool ok = ApplyInternal(0);
                    if (!ok) Report("Display change: " + lastError);
                    return ok; // success is not reported (fix f): no UI refresh for a non-change
                }
            }
            finally { FlushReports(); }
        }

        // ------------------------------------------------------------ hotkeys

        public HotkeySettings Hotkeys { get { lock (sync) { return settings.Hotkeys.Clone(); } } }

        /// <summary>Validates (rules + duplicates) and saves. False + error = nothing changed.</summary>
        public bool SetHotkeys(HotkeySettings hotkeys, out string error)
        {
            error = "";
            lock (sync)
            {
                if (!CanMutate()) { error = lastError; return false; }
                if (hotkeys == null) { error = "No hotkeys."; return false; }
                HotkeySettings h = hotkeys.Clone();
                h.Normalize();
                string problem = h.Validate();
                if (problem != null) { error = problem; return false; }
                settings.Hotkeys = h;
                if (!SaveSettings()) { error = lastError; return true; }
                return true;
            }
        }

        /// <summary>Runs the action bound to this chord. True = a binding matched (consumed).</summary>
        public bool OnHotkeyDown(int vk, int modifiers)
        {
            HotkeySettings h;
            lock (sync)
            {
                if (disposed || !started) return false;
                h = settings.Hotkeys.Clone();
            }
            if (h.HoldOriginal.Matches(vk, modifiers))
            {
                lock (sync) { heldKey = vk; }
                BeginHoldOriginal();
                return true;
            }
            if (h.Toggle.Matches(vk, modifiers)) { Toggle(); return true; }
            if (h.NextPreset.Matches(vk, modifiers)) { NextPreset(); return true; }
            if (h.RestoreAll.Matches(vk, modifiers)) { RestoreEverything(); return true; }
            foreach (PresetHotkey p in h.Presets)
            {
                if (p.Key != null && p.Key.Matches(vk, modifiers))
                {
                    ApplyPreset(p.PresetName);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Key released: ends hold-to-see-original when it is the held key.</summary>
        public bool OnHotkeyUp(int vk)
        {
            lock (sync)
            {
                if (!holding || vk != heldKey) return false;
                heldKey = 0;
            }
            EndHoldOriginal();
            return true;
        }

        // ------------------------------------------------------------ presets

        public bool ApplyPreset(string name)
        {
            try { lock (sync) { return ApplyPresetCore(name); } }
            finally { FlushReports(); }
        }

        public string NextPreset()
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return settings.CurrentPresetName;
                    List<PilotStyle> all = presets.All;
                    if (all.Count == 0) return settings.CurrentPresetName;
                    int i = presets.IndexOf(settings.CurrentPresetName);
                    string next = all[(i + 1 + all.Count) % all.Count].Name;
                    ApplyPresetCore(next);
                    return next;
                }
            }
            finally { FlushReports(); }
        }

        public void SetCurrent(PilotStyle values)
        {
            SetCurrent(values, true);
        }

        /// <summary>save=false: apply without writing shade-settings.json (slider drags; persist later with SaveSettingsNow).</summary>
        public void SetCurrent(PilotStyle values, bool save)
        {
            if (values == null) return;
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return;
                    PilotStyle p = values.Clone();
                    p.Name = settings.CurrentPresetName;
                    PilotPresetStore.EnforceFixed(p);
                    p.Name = settings.CurrentPresetName;
                    p.BuiltIn = false;
                    p.Untested = false;
                    bool same = settings.Current != null && settings.Current.SameStyleValues(p) &&
                        settings.Current.ApplySaturation == p.ApplySaturation && settings.Current.SaturationBackend == p.SaturationBackend &&
                        settings.Current.MonitorBrightness == p.MonitorBrightness && settings.Current.MonitorContrast == p.MonitorContrast &&
                        settings.Current.MonitorSharpness == p.MonitorSharpness && settings.Current.Saturation == p.Saturation;
                    settings.Current = p;
                    if (save) SaveSettings();
                    // v5.0.2 (Leo: never re-apply unchanged values): an edit that changes nothing does not touch the display
                    if (enabled && !same) ApplyAndReport(0);
                }
            }
            finally { FlushReports(); }
        }

        public bool SaveSettingsNow()
        {
            lock (sync)
            {
                if (!CanMutate()) return false;
                return SaveSettings();
            }
        }

        /// <summary>Saves the working values as a user preset; a built-in name is saved as "name (2)". Becomes current.</summary>
        public PilotStyle SaveCurrentAs(string name)
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return null;
                    PilotStyle p = settings.Current.Clone();
                    p.Name = PilotPresetStore.CleanName(name);
                    if (presets.IsBuiltIn(p.Name)) p.Name = presets.UniqueName(p.Name);
                    PilotStyle saved = presets.Upsert(p);
                    settings.CurrentPresetName = saved.Name;
                    settings.Current = saved.Clone();
                    SaveSettings();
                    Report(PresetSaveProblem() ?? ("Saved preset: " + saved.Name));
                    return saved;
                }
            }
            finally { FlushReports(); }
        }

        /// <summary>Saves the working values into an existing preset (built-ins too: Reset gets factory values back).</summary>
        public bool UpdatePreset(string name)
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    if (presets.IndexOf(name) < 0) { lastError = "Preset not found: " + name; return false; }
                    PilotStyle p = settings.Current.Clone();
                    p.Name = name;
                    presets.Upsert(p);
                    Report(PresetSaveProblem() ?? ("Saved preset: " + name));
                    return true;
                }
            }
            finally { FlushReports(); }
        }

        public bool DeletePreset(string name)
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    if (presets.IsBuiltIn(name)) { lastError = "Built-in presets cannot be deleted."; Report(lastError); return false; }
                    bool ok = presets.Delete(name);
                    if (ok)
                    {
                        settings.Hotkeys.SetPresetKey(name, null);
                        SaveSettings();
                        Report(PresetSaveProblem() ?? ("Deleted preset: " + name));
                    }
                    return ok;
                }
            }
            finally { FlushReports(); }
        }

        public bool RenamePreset(string oldName, string newName)
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return false;
                    string n = PilotPresetStore.CleanName(newName);
                    if (!presets.Rename(oldName, n))
                    {
                        lastError = presets.IsBuiltIn(oldName) ? "Built-in presets cannot be renamed." : "Rename failed (name in use or preset not found).";
                        return false;
                    }
                    HotkeyBinding key = settings.Hotkeys.GetPresetKey(oldName);
                    settings.Hotkeys.SetPresetKey(oldName, null);
                    if (key.IsAssigned) settings.Hotkeys.SetPresetKey(n, key);
                    if (string.Equals(settings.CurrentPresetName, oldName, StringComparison.Ordinal))
                    {
                        settings.CurrentPresetName = n;
                        settings.Current.Name = n;
                    }
                    SaveSettings();
                    Report(PresetSaveProblem() ?? ("Renamed preset: " + n));
                    return true;
                }
            }
            finally { FlushReports(); }
        }

        public int RestoreMissingBuiltInPresets()
        {
            lock (sync)
            {
                if (!CanMutate()) return 0;
                return presets.RestoreMissingBuiltIns();
            }
        }

        public bool ResetBuiltInPreset(string name)
        {
            lock (sync)
            {
                if (!CanMutate()) return false;
                return presets.ResetBuiltIn(name);
            }
        }

        /// <summary>TSHADE2 code for a saved preset, or for the working values when name is null.</summary>
        public string ExportCode(string name)
        {
            lock (sync)
            {
                if (disposed) return null;
                PilotStyle p = name == null ? settings.Current.Clone() : presets.Get(name);
                if (p == null) return null;
                if (name == null) p.Name = settings.CurrentPresetName;
                return ShareCode.Export(p);
            }
        }

        /// <summary>Validates and saves an imported preset under a unique name. Returns the name or null.</summary>
        public string ImportCode(string code, out string error)
        {
            error = "";
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) { error = lastError; return null; }
                    PilotStyle p = ShareCode.Import(code, out error);
                    if (p == null)
                    {
                        Report("Import failed: " + error);
                        return null;
                    }
                    p.Name = presets.UniqueName(p.Name);
                    PilotStyle saved = presets.Upsert(p);
                    Report(PresetSaveProblem() ?? ("Imported preset: " + saved.Name));
                    return saved.Name;
                }
            }
            finally { FlushReports(); }
        }

        // ------------------------------------------------------------ monitors

        public void SetSelectedDisplays(IList<string> deviceNames)
        {
            try
            {
                lock (sync)
                {
                    if (!CanMutate()) return;
                    settings.SelectedDisplays = deviceNames == null ? new List<string>() : new List<string>(deviceNames);
                    settings.Normalize();
                    SaveSettings();
                    if (enabled) ApplyAndReport(0);
                }
            }
            finally { FlushReports(); }
        }

        public List<string> GetTargetDevices()
        {
            lock (sync)
            {
                List<string> result = new List<string>();
                List<DisplayTarget> displays = GetDisplays();
                foreach (string d in settings.SelectedDisplays)
                {
                    foreach (DisplayTarget t in displays)
                        if (t != null && string.Equals(t.DeviceName, d, StringComparison.OrdinalIgnoreCase) && !result.Contains(t.DeviceName))
                            result.Add(t.DeviceName);
                }
                if (result.Count == 0)
                {
                    foreach (DisplayTarget t in displays)
                        if (t != null && t.Primary && !string.IsNullOrWhiteSpace(t.DeviceName)) { result.Add(t.DeviceName); break; }
                    if (result.Count == 0)
                        foreach (DisplayTarget t in displays)
                            if (t != null && !string.IsNullOrWhiteSpace(t.DeviceName)) { result.Add(t.DeviceName); break; }
                }
                return result;
            }
        }

        /// <summary>The ramp the first target display gets (built on its baseline when known, else identity).</summary>
        public ShadeRamp BuildCurrentRamp()
        {
            lock (sync)
            {
                List<string> t = GetTargetDevices();
                GammaRamp? b = t.Count > 0 ? gamma.GetBaseline(t[0]) : null;
                return BuildFor(settings.Current, b);
            }
        }

        public bool HardwareAvailable
        {
            get
            {
                lock (sync)
                {
                    if (ddc == null) return false;
                    List<string> t = GetTargetDevices();
                    return t.Count > 0 && ddc.Find(t[0]) != null;
                }
            }
        }

        /// <summary>Hardware values of the first target display's monitor. Never null.</summary>
        public MonitorHardware GetMonitorHardware()
        {
            lock (sync)
            {
                MonitorHardware h = new MonitorHardware();
                string none = DdcCiController.NotSupportedText;
                h.Brightness.Reason = none; h.Contrast.Reason = none; h.Sharpness.Reason = none;
                if (ddc == null) return h;
                List<string> t = GetTargetDevices();
                DdcMonitor m = t.Count > 0 ? ddc.Find(t[0]) : null;
                if (m == null) return h;
                h.MonitorId = m.Id ?? "";
                h.Name = m.Name ?? "";
                h.Brightness = ddc.GetBrightness(m.Id);
                h.Contrast = ddc.GetContrast(m.Id); // raw units; -50..50 reading: MonitorContrastNeutralZero
                h.Sharpness = ddc.GetSharpness(m.Id);
                return h;
            }
        }

        /// <summary>Contrast of GetMonitorHardware() in -50..50 units (DdcValue is unsigned).</summary>
        public int MonitorContrastNeutralZero
        {
            get
            {
                lock (sync)
                {
                    if (ddc == null) return 0;
                    List<string> t = GetTargetDevices();
                    DdcMonitor m = t.Count > 0 ? ddc.Find(t[0]) : null;
                    if (m == null || !m.SupportsContrast) return 0;
                    return (int)Math.Round(DdcCiController.ToNeutralZero(m.ContrastCurrent, m.ContrastMin, m.ContrastMax));
                }
            }
        }

        public bool RefreshMonitors()
        {
            lock (sync)
            {
                if (ddc == null) return false;
                string e;
                bool ok = ddc.Refresh(out e);
                if (!ok) AddError("Monitor probe: " + e);
                return ok;
            }
        }

        // ------------------------------------------------------------ internals (sync held)

        private static ShadeRamp BuildFor(PilotStyle style, GammaRamp? baseline)
        {
            if (baseline.HasValue && baseline.Value.IsValid)
                return PilotCurve.Build(style, baseline.Value.Red, baseline.Value.Green, baseline.Value.Blue, false);
            return PilotCurve.Build(style);
        }

        private bool CanMutate()
        {
            if (disposed) { lastError = "Engine disposed."; return false; }
            if (!started) { lastError = "Not started (call Start first)."; return false; }
            return true;
        }

        private bool EnableCore()
        {
            if (!CanMutate()) return false;
            bool wasEnabled = enabled;
            enabled = true;
            bool ok = ApplyInternal(SmoothDuration());
            if (!ok && lastApplyGammaFailed && !gamma.HasActiveFilter)
            {
                string error = lastError;
                RestoreOriginalInternal();
                lastError = error;
                Report("Filter not turned on: " + error);
                return false;
            }
            if (!ok && lastApplyGammaFailed && wasEnabled)
            {
                Report("Filter still on with the previous curve: " + lastError);
                return false;
            }
            Report(ok ? "Filter on: " + settings.CurrentPresetName : "Filter on, but: " + lastError);
            return ok;
        }

        private bool DisableCore()
        {
            if (!CanMutate()) return false;
            bool ok = RestoreOriginalInternal();
            Report(ok ? "Filter off (original restored)." : "Filter off, restore problem: " + lastError);
            return ok;
        }

        private bool ApplyPresetCore(string name)
        {
            if (!CanMutate()) return false;
            PilotStyle p = presets.Get(name);
            if (p == null)
            {
                lastError = "Preset not found: " + name;
                Report(lastError);
                return false;
            }
            settings.Current = p;
            settings.CurrentPresetName = p.Name;
            SaveSettings();
            if (!enabled)
            {
                Report("Preset: " + p.Name + " (filter is off)");
                return true;
            }
            bool ok = ApplyInternal(SmoothDuration());
            if (ok) Report("Preset: " + p.Name);
            else if (lastApplyGammaFailed)
                Report("Preset " + p.Name + " not applied" + (gamma.HasActiveFilter ? " (screen keeps the previous curve)" : "") + ": " + lastError);
            else Report("Preset: " + p.Name + ", but: " + lastError);
            return ok;
        }

        private void ApplyAndReport(int durationMilliseconds)
        {
            if (!ApplyInternal(durationMilliseconds))
                Report((lastApplyGammaFailed && gamma.HasActiveFilter ? "Not applied (screen keeps the previous curve): " : "Not applied: ") + lastError);
        }

        private int SmoothDuration()
        {
            if (!settings.SmoothTransition || holding) return 0;
            ShadeRamp ramp = PilotCurve.Build(settings.Current);
            return 280 + (int)Math.Round(ramp.ChangeStrength * 120.0);
        }

        private bool ApplyInternal(int durationMilliseconds)
        {
            lastError = "";
            lastApplyGammaFailed = false;
            List<string> targets = GetTargetDevices();
            if (targets.Count == 0)
            {
                lastError = "No display found.";
                lastApplyGammaFailed = true;
                return false;
            }
            bool ok = true;
            string error;
            PilotStyle p = settings.Current;

            // 1. Gamma: capture + persist originals BEFORE changing anything; each device on its own baseline.
            foreach (string device in targets)
            {
                GammaRamp original;
                if (inheritedRamps.TryGetValue(device, out original)) gamma.SeedBaseline(device, original);
            }
            if (!gamma.CaptureBaselines(targets, out error))
            {
                lastError = "Could not read the display's original curve: " + error;
                lastApplyGammaFailed = true;
                return false;
            }
            Dictionary<string, GammaRamp> originals = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, GammaRamp> wanted = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            foreach (string device in targets)
            {
                GammaRamp? baseline = gamma.GetBaseline(device);
                if (baseline.HasValue) originals[device] = baseline.Value;
                wanted[device] = GammaRamp.FromShadeRamp(BuildFor(p, baseline));
            }
            ownsRecoveryEntries = true;
            if (!recovery.AddRamps(originals))
            {
                // v5.0.2 (BRUTE P2-1): no record of the original = no change (a crash would leave the filter on for good)
                AddError(recovery.LastWriteError);
                lastApplyGammaFailed = true;
                return false;
            }

            if (holding || p.IsNeutralStyle)
            {
                // Hold-to-see-original or a neutral style: the user's own original ramp.
                if (!gamma.RestoreAll(out error)) { ok = false; AddError(error); }
            }
            else if (!gamma.TransitionTo(wanted, durationMilliseconds, out error))
            {
                AddError("Applying the gamma curve failed: " + error);
                lastApplyGammaFailed = true;
                return false;
            }

            // 2. Saturation (only when ApplySaturation): driver vibrance or Windows Magnification.
            bool wantDriver = p.ApplySaturation && p.SaturationBackend == PilotStyle.SaturationBackendDriver;
            bool wantLocal = p.ApplySaturation && p.SaturationBackend == PilotStyle.SaturationBackendSoftware &&
                p.Saturation != PilotStyle.SaturationNeutral;
            foreach (string device in new List<string>(vibranceChanged))
            {
                if (!wantDriver || !targets.Contains(device))
                {
                    if (!RestoreVibrance(device, out error)) { ok = false; AddError(error); }
                }
            }
            if (wantDriver)
            {
                foreach (string device in targets)
                {
                    if (!ApplyVibrance(device, (p.Saturation - PilotStyle.SaturationNeutral) * 2, out error)) { ok = false; AddError(error); }
                }
            }
            if (!wantLocal && localActive)
            {
                if (!ResetLocal(out error)) { ok = false; AddError(error); }
            }
            else if (wantLocal)
            {
                if (local == null || !local.IsSupported)
                {
                    ok = false;
                    AddError("Windows saturation (Magnification) not supported on this system.");
                }
                else if (!recovery.MarkLocalSaturation()) { ok = false; AddError(recovery.LastWriteError); }
                else
                {
                    double want = p.Saturation / 50.0;
                    if (!(localActive && lastLocal == want))
                    {
                        localActive = true;
                        lastLocal = double.NaN;
                        if (!local.TrySet(want, out error)) { ok = false; AddError(error); }
                        else lastLocal = want;
                    }
                }
            }

            // 3. Monitor hardware (first target only).
            if (!ApplyHardware(targets[0], p)) ok = false;
            return ok;
        }

        private static string HwKey(string id, byte vcp) { return id + "|" + vcp; }

        private bool ApplyHardware(string device, PilotStyle p)
        {
            bool any = p.ApplyHardwareBrightness || p.ApplyHardwareContrast || p.ApplyHardwareSharpness;
            if (ddc == null)
            {
                return true; // no hardware backend: values are ignored
            }
            DdcMonitor m = ddc.Find(device);
            if (m == null)
            {
                if (any && hardwareWritten.Count == 0)
                {
                    string e;
                    ddc.Refresh(out e);
                    m = ddc.Find(device);
                }
                if (m == null)
                {
                    if (any) { AddError("Monitor hardware: no DDC/CI monitor found for " + device + "."); return false; }
                    return true;
                }
            }
            if (any)
            {
                ownsRecoveryEntries = true;
                if (!recovery.AddDdc(ddc.GetOriginals())) { AddError(recovery.LastWriteError); return false; }
            }
            bool ok = true;
            ok &= HardwareOne(m, DdcCiController.VcpBrightness, p.ApplyHardwareBrightness, p.HardwareBrightness, m.SupportsBrightness);
            ok &= HardwareOne(m, DdcCiController.VcpContrast, p.ApplyHardwareContrast, p.HardwareContrast, m.SupportsContrast);
            ok &= HardwareOne(m, DdcCiController.VcpSharpness, p.ApplyHardwareSharpness, p.HardwareSharpness, m.SupportsSharpness);
            return ok;
        }

        private bool HardwareOne(DdcMonitor m, byte vcp, bool apply, int value, bool supported)
        {
            string key = HwKey(m.Id, vcp);
            string error = "";
            int last;
            bool written = hardwareWritten.TryGetValue(key, out last);
            if (apply)
            {
                if (!supported) return true; // UI hides / disables it; nothing to write
                if (written && last == value) return true;
                bool ok;
                if (vcp == DdcCiController.VcpContrast) ok = ddc.SetContrastNeutralZero(m.Id, value, out error);
                else
                {
                    uint lo = vcp == DdcCiController.VcpBrightness ? m.BrightnessMin : m.SharpnessMin;
                    uint hi = vcp == DdcCiController.VcpBrightness ? m.BrightnessMax : m.SharpnessMax;
                    uint raw = (uint)Math.Max((long)lo, Math.Min((long)hi, (long)value));
                    ok = vcp == DdcCiController.VcpBrightness ? ddc.SetBrightness(m.Id, raw, out error) : ddc.SetSharpness(m.Id, raw, out error);
                }
                if (!ok)
                {
                    // v5.0.2 (BRUTE P1-2): the write may have reached the monitor (slow or unverified): remember that it was
                    // touched, with a value no request equals, so the restore writes the original
                    hardwareWritten[key] = int.MinValue;
                    AddError("Monitor " + VcpName(vcp) + ": " + error);
                    return false;
                }
                hardwareWritten[key] = value;
                return true;
            }
            if (!written) return true;
            // Value went back to "leave unchanged": restore this one original.
            DdcSnapshot s = ddc.GetOriginal(m.Id);
            bool restored = true;
            if (s != null)
            {
                if (vcp == DdcCiController.VcpBrightness && s.HasBrightness) restored = ddc.SetBrightness(m.Id, s.Brightness, out error);
                else if (vcp == DdcCiController.VcpContrast && s.HasContrast) restored = ddc.SetContrast(m.Id, s.Contrast, out error);
                else if (vcp == DdcCiController.VcpSharpness && s.HasSharpness) restored = ddc.SetSharpness(m.Id, s.Sharpness, out error);
            }
            if (!restored) { AddError("Monitor " + VcpName(vcp) + " restore: " + error); return false; }
            hardwareWritten.Remove(key);
            return true;
        }

        private static string VcpName(byte vcp)
        {
            return vcp == DdcCiController.VcpBrightness ? "brightness" : vcp == DdcCiController.VcpContrast ? "contrast" : "sharpness";
        }

        private bool RestoreHardware(out string error)
        {
            error = "";
            if (ddc == null) return true;
            if (hardwareWritten.Count == 0 && !ddc.HasChanges) return true;
            if (!ddc.RestoreOriginals(out error)) return false;
            hardwareWritten.Clear();
            return true;
        }

        private bool ApplyVibrance(string device, int saturation, out string error)
        {
            error = "";
            if (!vibrance.IsSupported)
            {
                error = "Driver vibrance " + vibrance.Status;
                return false;
            }
            VibranceInfo info;
            if (!vibrance.TryGet(device, out info, out error)) return false;
            if (!vibranceOriginals.ContainsKey(device))
            {
                RecoveryVibrance saved;
                int original = recovery.TryGetVibrance(device, out saved) && saved.Vendor == vibrance.Vendor ?
                    saved.Level : (recoveryDamaged ? info.Default : info.Current);
                vibranceOriginals[device] = original;
            }
            ownsRecoveryEntries = true;
            if (!recovery.AddVibrance(device, vibrance.Vendor, vibranceOriginals[device]))
            {
                error = recovery.LastWriteError; // v5.0.2: no record of the original = no change
                return false;
            }
            int level = VibranceMath.ToDriverLevel(saturation, info);
            // v5.0.2 (BRUTE P1-1): marked changed BEFORE the call: a call that times out may still reach the driver later,
            // so the restore must write the original whatever this call reports
            vibranceChanged.Add(device);
            if (!vibrance.TrySet(device, level, out error)) return false;
            return true;
        }

        private bool RestoreVibrance(string device, out string error)
        {
            error = "";
            int original;
            if (!vibranceOriginals.TryGetValue(device, out original)) { vibranceChanged.Remove(device); return true; }
            if (!vibrance.TrySet(device, original, out error)) return false;
            vibranceChanged.Remove(device);
            return true;
        }

        /// <summary>v5.0.2 (BRUTE P2-1): the recovery file was unreadable; 全部恢复 puts Windows' standard values back (identity
        /// curve on every display, the driver's default vibrance). Monitor hardware has no known standard and is left alone.</summary>
        private bool RestoreStandard()
        {
            bool ok = true;
            string error;
            Dictionary<string, GammaRamp> ids = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayTarget t in GetDisplays())
                if (t != null && !string.IsNullOrWhiteSpace(t.DeviceName)) ids[t.DeviceName] = GammaRamp.CreateIdentity();
            if (ids.Count > 0 && !gamma.ApplyDirect(ids, out error)) { ok = false; AddError(error); }
            if (vibrance.IsSupported)
                foreach (string d in ids.Keys)
                {
                    VibranceInfo info;
                    if (vibrance.TryGet(d, out info, out error) && info.Current != info.Default && !vibrance.TrySet(d, info.Default, out error))
                    { ok = false; AddError(error); }
                }
            if (ok)
            {
                recoveryDamaged = false;
                recoveryNotice = "";
                inheritedRamps = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            }
            return ok;
        }

        private bool ResetLocal(out string error)
        {
            error = "";
            if (local == null) { localActive = false; return true; }
            if (!local.TryReset(out error)) return false;
            localActive = false;
            lastLocal = double.NaN;
            return true;
        }

        private bool RestoreOriginalInternal()
        {
            enabled = false;
            holding = false;
            heldKey = 0;
            lastError = "";
            bool ok = true;
            string error;
            if (!gamma.RestoreAll(out error)) { ok = false; AddError(error); }
            foreach (string device in new List<string>(vibranceChanged))
                if (!RestoreVibrance(device, out error)) { ok = false; AddError(error); }
            if (localActive && !ResetLocal(out error)) { ok = false; AddError(error); }
            if (!RestoreHardware(out error)) { ok = false; AddError("Monitor restore: " + error); }
            if (ok && recoveryDamaged && ownsRecoveryEntries)
            {
                // the standard values the filter was built on are back on screen: from now on the screen is the original
                recoveryDamaged = false;
                recoveryNotice = "";
                inheritedRamps = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            }
            if (ok && ownsRecoveryEntries && !inheritedRecovery)
            {
                recovery.Clear();
                ownsRecoveryEntries = false;
            }
            return ok;
        }

        private string RecoverPreviousSession()
        {
            RecoveryData data;
            bool have = recovery.TryLoad(out data);
            if (!have && recovery.Damaged)
            {
                recoveryDamaged = true;
                recoveryNotice = "The crash-recovery file could not be read (moved to " + Path.GetFileName(recovery.BadFilePath) +
                    "). The original screen is unknown: filter off and 'restore everything' use Windows' standard values.";
                inheritedRamps = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
                foreach (DisplayTarget t in GetDisplays())
                    if (t != null && !string.IsNullOrWhiteSpace(t.DeviceName)) inheritedRamps[t.DeviceName] = GammaRamp.CreateIdentity();
            }
            if (ddc != null)
            {
                // Seed crash-time originals BEFORE the first probe, so a changed monitor is not taken as original.
                if (have) ddc.SeedOriginals(data.Ddc);
                string probeError;
                ddc.Refresh(out probeError);
            }
            if (!have) return recoveryNotice;
            bool ok = true;
            string error;
            Dictionary<string, GammaRamp> ramps = recovery.GetRamps(data);
            if (ramps.Count > 0 && !gamma.ApplyDirect(ramps, out error)) { ok = false; AddError(error); }
            foreach (RecoveryVibrance v in data.Vibrance)
            {
                if (!vibrance.IsSupported || v.Vendor != vibrance.Vendor)
                {
                    ok = false;
                    AddError("Saved " + v.Vendor + " vibrance for " + v.Device + " not restored (driver API not available).");
                    continue;
                }
                int level = v.Level;
                VibranceInfo info;
                string ignored;
                if (vibrance.TryGet(v.Device, out info, out ignored))
                    level = ShadeMath.Clamp(level, Math.Min(info.Min, info.Max), Math.Max(info.Min, info.Max));
                if (!vibrance.TrySet(v.Device, level, out error)) { ok = false; AddError(error); }
            }
            if (data.LocalSaturationActive && local != null && local.IsSupported)
            {
                if (!local.TryReset(out error)) { ok = false; AddError(error); }
            }
            if (data.Ddc.Count > 0)
            {
                if (ddc == null) { ok = false; AddError("Saved monitor values not restored (no DDC/CI available)."); }
                else if (!ddc.RestoreOriginals(out error)) { ok = false; AddError("Monitor restore: " + error); }
            }
            if (ok)
            {
                recovery.Clear();
                return "Restored the display state left over from an unclean exit.";
            }
            inheritedRecovery = true;
            inheritedRamps = ramps;
            return "Crash recovery incomplete (file kept for next start): " + lastError;
        }

        private void OnTransitionFailed(string error)
        {
            try
            {
                lock (sync)
                {
                    if (disposed || !started) return;
                    lastError = "Smooth fade stopped: " + error;
                    if (enabled && !gamma.HasActiveFilter)
                    {
                        string message = lastError;
                        RestoreOriginalInternal();
                        lastError = message;
                        Report("Filter turned off: " + message);
                    }
                    else
                    {
                        Report(lastError + (gamma.HasActiveFilter ? " (screen keeps the previous curve)" : ""));
                    }
                }
            }
            catch
            {
            }
            finally { FlushReports(); }
        }

        private string PresetSaveProblem()
        {
            string e = presets.LastSaveError;
            if (string.IsNullOrEmpty(e)) return null;
            lastError = e;
            return e + " (kept in memory for this session)";
        }

        private void AddError(string error)
        {
            if (string.IsNullOrEmpty(error)) return;
            lastError = lastError.Length == 0 ? error : lastError + " | " + error;
        }

        private bool SaveSettings()
        {
            try
            {
                JsonFile.Write(Path.Combine(dataFolder, ShadeSettings.FileName), settings);
                return true;
            }
            catch (Exception ex)
            {
                AddError("Settings not saved: " + ex.Message);
                return false;
            }
        }

        private void Report(string message)
        {
            lock (reportSync) { pendingReports.Enqueue(message); }
        }

        private void FlushReports()
        {
            if (Monitor.IsEntered(sync)) return;
            while (true)
            {
                string message;
                lock (reportSync)
                {
                    if (pendingReports.Count == 0) return;
                    message = pendingReports.Dequeue();
                }
                Action<string> handler = StatusChanged;
                if (handler != null)
                {
                    try { handler(message); } catch { }
                }
            }
        }

        /// <summary>
        /// Process-wide single-owner lock per data folder: a lock file opened with FileShare.None.
        /// Engines inside one process share it (reference count); another process is refused.
        /// A folder where the lock file cannot be created (read-only) runs without the lock.
        /// </summary>
        private static class FolderLock
        {
            private static readonly object gate = new object();
            private static readonly Dictionary<string, KeyValuePair<FileStream, int>> held =
                new Dictionary<string, KeyValuePair<FileStream, int>>(StringComparer.OrdinalIgnoreCase);

            private static string Key(string folder)
            {
                try { return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch { return folder; }
            }

            public static bool Acquire(string folder, out string error)
            {
                error = "";
                string key = Key(folder);
                lock (gate)
                {
                    KeyValuePair<FileStream, int> entry;
                    if (held.TryGetValue(key, out entry))
                    {
                        held[key] = new KeyValuePair<FileStream, int>(entry.Key, entry.Value + 1);
                        return true;
                    }
                    FileStream stream = null;
                    try
                    {
                        stream = new FileStream(Path.Combine(folder, LockFileName), FileMode.OpenOrCreate,
                            FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (UnauthorizedAccessException) { stream = null; }
                    catch (DirectoryNotFoundException) { stream = null; }
                    catch (NotSupportedException) { stream = null; }
                    catch (System.Security.SecurityException) { stream = null; }
                    catch (IOException)
                    {
                        error = "Another TarkovShade is already running from this folder (" + LockFileName +
                            " is in use). Close it first; this copy will not touch the screen.";
                        return false;
                    }
                    held[key] = new KeyValuePair<FileStream, int>(stream, 1);
                    return true;
                }
            }

            public static void Release(string folder)
            {
                string key = Key(folder);
                lock (gate)
                {
                    KeyValuePair<FileStream, int> entry;
                    if (!held.TryGetValue(key, out entry)) return;
                    if (entry.Value > 1)
                    {
                        held[key] = new KeyValuePair<FileStream, int>(entry.Key, entry.Value - 1);
                        return;
                    }
                    held.Remove(key);
                    if (entry.Key != null) { try { entry.Key.Dispose(); } catch { } }
                }
            }
        }
    }
}
