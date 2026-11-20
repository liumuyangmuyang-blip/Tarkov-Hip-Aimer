// TarkovShade - GPU driver Digital Vibrance (saturation) control.
//
// Gamma ramps cannot change saturation, so this goes through the GPU driver's own public API,
// loaded at runtime from the driver DLL in this process (LoadLibrary + GetProcAddress). It does
// NOT open, read or inject into any other process.
//
// NVIDIA: nvapi64.dll (nvapi.dll for 32-bit) export "nvapi_QueryInterface", function IDs as used by
//   the open-source vibranceGUI project (https://github.com/juv/vibranceGUI; checked at
//   commit 919a9f2, Dec 2024). vibranceGUI calls NVAPI from a bundled native vibranceDLL.dll; the IDs
//   below marked [found in vibranceDLL.dll] were confirmed by searching that binary for the 32-bit
//   little-endian constant. Only the numeric IDs were taken, no vibranceGUI code.
//     NvAPI_Initialize                     0x0150E828  [found in vibranceDLL.dll]
//     NvAPI_Unload                         0xD22BDD7E  [found in vibranceDLL.dll]
//     NvAPI_EnumNvidiaDisplayHandle        0x9ABDD40D  [found in vibranceDLL.dll]
//     NvAPI_GetAssociatedNvidiaDisplayHandle 0x35C29134 [found in vibranceDLL.dll]
//     NvAPI_GetDVCInfo                     0x4085DE45  [found in vibranceDLL.dll]
//     NvAPI_SetDVCLevel                    0x172409B4  [found in vibranceDLL.dll]
//     NvAPI_GetDVCInfoEx                   0x0E45002D  [found in vibranceDLL.dll]
//     NvAPI_SetDVCLevelEx                  0x4A82C2B1  UNVERIFIED (not found in vibranceDLL.dll; matches
//                                                       NvAPIWrapper FunctionId.cs). The Ex pair is tried
//                                                       first per display, legacy is the fallback (DvcModes).
//   NV_DISPLAY_DVC_INFO = { version, currentLevel, minLevel, maxLevel }, version = sizeof | (1 << 16)
//   = 0x00010010 (that constant also occurs in vibranceDLL.dll; layout from vibranceGUI NvidiaTypes.cs).
//   NV_DISPLAY_DVC_INFO_EX layout (adds defaultLevel) is UNVERIFIED.
//
// AMD: atiadlxx.dll (64-bit; atiadlxy.dll fallback) ADL_Display_Color_Get / ADL_Display_Color_Set
//   with ADL_DISPLAY_COLOR_SATURATION = 1 << 2 (constant and struct layouts as in vibranceGUI AMD/vendor/adl64).
//
// Everything here is UNVERIFIED on real Windows hardware: it was only compile-checked on Linux.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TarkovShade
{
    public struct VibranceInfo
    {
        public int Current;
        public int Min;
        public int Max;
        /// <summary>The driver's "normal" level. NVIDIA: taken from NvAPI_GetDVCInfoEx when the driver has it
        /// (then -100 = driver minimum works as on AMD). Only in the legacy NvAPI_GetDVCInfo fallback is there
        /// no default level: Min is used, so negative DriverVibrance values have no effect there
        /// (UNVERIFIED that Min = 50 % in NVIDIA Control Panel).</summary>
        public int Default;
    }

    /// <summary>Driver vibrance backend. Implementations: NVIDIA, AMD, Unsupported (and test fakes).</summary>
    public interface IVibranceBackend
    {
        /// <summary>"NVIDIA", "AMD" or "" when not supported.</summary>
        string Vendor { get; }
        bool IsSupported { get; }
        /// <summary>Human-readable state, e.g. "Not supported: no NVIDIA or AMD driver API found."</summary>
        string Status { get; }
        bool TryGet(string deviceName, out VibranceInfo info, out string error);
        bool TrySet(string deviceName, int level, out string error);
    }

    public static class VibranceMath
    {
        /// <summary>
        /// Maps the preset Saturation slider (-100..100, 0 = driver default) onto the driver range:
        /// 0 -> Default, +100 -> Max, -100 -> Min. Result is clamped to [Min, Max].
        /// </summary>
        public static int ToDriverLevel(int saturation, VibranceInfo info)
        {
            int s = ShadeMath.Clamp(saturation, ShadePreset.SaturationMin, ShadePreset.SaturationMax);
            int min = Math.Min(info.Min, info.Max);
            int max = Math.Max(info.Min, info.Max);
            int def = ShadeMath.Clamp(info.Default, min, max);
            double level = s >= 0 ?
                def + (max - def) * (s / 100.0) :
                def + (def - min) * (s / 100.0);
            return ShadeMath.Clamp((int)Math.Round(level), min, max);
        }
    }

    public static class VibranceController
    {
        /// <summary>Tries NVIDIA, then AMD; returns an Unsupported backend otherwise. Never throws.</summary>
        public static IVibranceBackend Detect()
        {
            string nvError = "";
            string amdError = "";
            try
            {
                NvidiaVibrance nv = NvidiaVibrance.TryCreate(out nvError);
                if (nv != null) return nv;
            }
            catch (Exception ex) { nvError = ex.Message; }
            try
            {
                AmdVibrance amd = AmdVibrance.TryCreate(out amdError);
                if (amd != null) return amd;
            }
            catch (Exception ex) { amdError = ex.Message; }
            return new UnsupportedVibrance("Not supported: no NVIDIA or AMD vibrance API found (NVIDIA: " +
                nvError + "; AMD: " + amdError + ").");
        }
    }

    public sealed class UnsupportedVibrance : IVibranceBackend
    {
        private readonly string status;
        public UnsupportedVibrance(string status) { this.status = status; }
        public string Vendor { get { return ""; } }
        public bool IsSupported { get { return false; } }
        public string Status { get { return status; } }
        public bool TryGet(string deviceName, out VibranceInfo info, out string error)
        {
            info = new VibranceInfo();
            error = status;
            return false;
        }
        public bool TrySet(string deviceName, int level, out string error)
        {
            error = status;
            return false;
        }
    }

    internal static class NativeLibrary
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

        private const uint LoadLibrarySearchSystem32 = 0x00000800;

        /// <summary>
        /// Loads a driver DLL from System32 only (never from the exe folder or PATH, so a planted
        /// DLL next to the exe is not picked up). Falls back to the absolute System32 path on systems
        /// without LOAD_LIBRARY_SEARCH_SYSTEM32 support (Windows 7 without KB2533623).
        /// </summary>
        public static IntPtr LoadLibrary(string fileName)
        {
            IntPtr h = LoadLibraryEx(fileName, IntPtr.Zero, LoadLibrarySearchSystem32);
            if (h != IntPtr.Zero) return h;
            string full = System.IO.Path.Combine(Environment.SystemDirectory, fileName);
            if (!System.IO.File.Exists(full)) return IntPtr.Zero;
            return LoadLibraryEx(full, IntPtr.Zero, 0);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
        public static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr module);

        public static T GetDelegate<T>(IntPtr module, string name) where T : class
        {
            if (module == IntPtr.Zero) return null;
            IntPtr p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
    }

    // ------------------------------------------------------------------ NVIDIA

    public sealed class NvidiaVibrance : IVibranceBackend, IDisposable
    {
        private const uint IdInitialize = 0x0150E828;
        private const uint IdUnload = 0xD22BDD7E;
        private const uint IdEnumNvidiaDisplayHandle = 0x9ABDD40D;
        private const uint IdGetAssociatedNvidiaDisplayHandle = 0x35C29134;
        private const uint IdGetDVCInfo = 0x4085DE45;
        private const uint IdSetDVCLevel = 0x172409B4;
        private const uint IdGetDVCInfoEx = 0x0E45002D;
        private const uint IdSetDVCLevelEx = 0x4A82C2B1; // UNVERIFIED
        private const int NvapiOk = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct NvDvcInfo
        {
            public uint Version;
            public int CurrentLevel;
            public int MinLevel;
            public int MaxLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvDvcInfoEx // UNVERIFIED layout
        {
            public uint Version;
            public int CurrentLevel;
            public int MinLevel;
            public int MaxLevel;
            public int DefaultLevel;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr QueryInterfaceFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UnloadFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnumNvidiaDisplayHandleFn(uint index, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int GetAssociatedNvidiaDisplayHandleFn(
            [MarshalAs(UnmanagedType.LPStr)] string displayName, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDVCInfoFn(IntPtr display, uint outputId, ref NvDvcInfo info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetDVCLevelFn(IntPtr display, uint outputId, int level);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDVCInfoExFn(IntPtr display, uint outputId, ref NvDvcInfoEx info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetDVCLevelExFn(IntPtr display, uint outputId, ref NvDvcInfoEx info);

        private IntPtr module;
        private UnloadFn unload;
        private EnumNvidiaDisplayHandleFn enumDisplay;
        private GetAssociatedNvidiaDisplayHandleFn getAssociated;
        private GetDVCInfoFn getInfo;
        private SetDVCLevelFn setLevel;
        private GetDVCInfoExFn getInfoEx;
        private SetDVCLevelExFn setLevelEx;
        private DvcModes modes;
        private readonly object sync = new object();

        private NvidiaVibrance() { }

        public static NvidiaVibrance TryCreate(out string error)
        {
            error = "";
            string dll = IntPtr.Size == 8 ? "nvapi64.dll" : "nvapi.dll";
            IntPtr module = NativeLibrary.LoadLibrary(dll);
            if (module == IntPtr.Zero)
            {
                error = dll + " not found";
                return null;
            }
            var query = NativeLibrary.GetDelegate<QueryInterfaceFn>(module, "nvapi_QueryInterface");
            if (query == null)
            {
                NativeLibrary.FreeLibrary(module);
                error = "nvapi_QueryInterface missing";
                return null;
            }
            var nv = new NvidiaVibrance();
            nv.module = module;
            InitializeFn init = Get<InitializeFn>(query, IdInitialize);
            nv.unload = Get<UnloadFn>(query, IdUnload);
            nv.enumDisplay = Get<EnumNvidiaDisplayHandleFn>(query, IdEnumNvidiaDisplayHandle);
            nv.getAssociated = Get<GetAssociatedNvidiaDisplayHandleFn>(query, IdGetAssociatedNvidiaDisplayHandle);
            nv.getInfo = Get<GetDVCInfoFn>(query, IdGetDVCInfo);
            nv.setLevel = Get<SetDVCLevelFn>(query, IdSetDVCLevel);
            nv.getInfoEx = Get<GetDVCInfoExFn>(query, IdGetDVCInfoEx);
            nv.setLevelEx = Get<SetDVCLevelExFn>(query, IdSetDVCLevelEx);

            if (init == null || init() != NvapiOk)
            {
                nv.Dispose();
                error = "NvAPI_Initialize failed";
                return null;
            }
            bool exPair = nv.getInfoEx != null && nv.setLevelEx != null;
            bool legacyPair = nv.getInfo != null && nv.setLevel != null;
            if ((!exPair && !legacyPair) || (nv.getAssociated == null && nv.enumDisplay == null))
            {
                nv.Dispose();
                error = "DVC functions not exposed by this driver";
                return null;
            }
            IntPtr first;
            if (nv.enumDisplay != null && nv.enumDisplay(0, out first) != NvapiOk)
            {
                nv.Dispose();
                error = "no NVIDIA-driven display";
                return null;
            }
            return nv;
        }

        private static T Get<T>(QueryInterfaceFn query, uint id) where T : class
        {
            IntPtr p = query(id);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        public string Vendor { get { return "NVIDIA"; } }
        public bool IsSupported { get { return module != IntPtr.Zero; } }
        public string Status { get { return "NVIDIA Digital Vibrance (NvAPI DVC) - UNVERIFIED on hardware"; } }

        private bool TryHandle(string deviceName, out IntPtr handle, out string error)
        {
            handle = IntPtr.Zero;
            error = "";
            if (getAssociated != null && getAssociated(deviceName, out handle) == NvapiOk && handle != IntPtr.Zero)
                return true;
            error = "Display " + deviceName + " is not driven by an NVIDIA GPU.";
            return false;
        }

        private DvcModes Modes()
        {
            if (modes != null) return modes;
            DvcGet ex = null, legacy = null;
            DvcSet exSet = null, legacySet = null;
            GetDVCInfoExFn gEx = getInfoEx;
            SetDVCLevelExFn sEx = setLevelEx;
            GetDVCInfoFn g = getInfo;
            SetDVCLevelFn sl = setLevel;
            if (gEx != null)
                ex = delegate(IntPtr h, out VibranceInfo i)
                {
                    var e = new NvDvcInfoEx();
                    e.Version = (uint)Marshal.SizeOf(typeof(NvDvcInfoEx)) | (1u << 16);
                    int st = gEx(h, 0, ref e);
                    i = new VibranceInfo();
                    i.Current = e.CurrentLevel; i.Min = e.MinLevel; i.Max = e.MaxLevel; i.Default = e.DefaultLevel;
                    return st;
                };
            if (sEx != null)
                exSet = delegate(IntPtr h, int level)
                {
                    var e = new NvDvcInfoEx();
                    e.Version = (uint)Marshal.SizeOf(typeof(NvDvcInfoEx)) | (1u << 16);
                    e.CurrentLevel = level;
                    return sEx(h, 0, ref e);
                };
            if (g != null)
                legacy = delegate(IntPtr h, out VibranceInfo i)
                {
                    var d = new NvDvcInfo();
                    d.Version = (uint)Marshal.SizeOf(typeof(NvDvcInfo)) | (1u << 16);
                    int st = g(h, 0, ref d);
                    i = new VibranceInfo();
                    i.Current = d.CurrentLevel; i.Min = d.MinLevel; i.Max = d.MaxLevel; i.Default = d.MinLevel;
                    return st;
                };
            if (sl != null)
                legacySet = delegate(IntPtr h, int level) { return sl(h, 0, level); };
            modes = new DvcModes(ex, exSet, legacy, legacySet);
            return modes;
        }

        public bool TryGet(string deviceName, out VibranceInfo info, out string error)
        {
            info = new VibranceInfo();
            lock (sync)
            {
                // Fix A3: never call into nvapi64.dll after Dispose freed it.
                if (module == IntPtr.Zero) { error = "NVAPI unloaded."; return false; }
                IntPtr h;
                if (!TryHandle(deviceName, out h, out error)) return false;
                return Modes().TryGet(deviceName, h, out info, out error);
            }
        }

        public bool TrySet(string deviceName, int level, out string error)
        {
            lock (sync)
            {
                if (module == IntPtr.Zero) { error = "NVAPI unloaded."; return false; }
                IntPtr h;
                if (!TryHandle(deviceName, out h, out error)) return false;
                return Modes().TrySet(deviceName, h, level, out error);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (module == IntPtr.Zero) return;
                try { if (unload != null) unload(); } catch { }
                NativeLibrary.FreeLibrary(module);
                module = IntPtr.Zero;
                // The delegates point into the freed DLL; drop them (A3).
                unload = null; enumDisplay = null; getAssociated = null;
                getInfo = null; setLevel = null; getInfoEx = null; setLevelEx = null; modes = null;
            }
        }
    }

    internal delegate int DvcGet(IntPtr display, out VibranceInfo info);
    internal delegate int DvcSet(IntPtr display, int level);

    internal enum DvcMode { Unknown, Ex, Legacy }

    /// <summary>
    /// Fix A4 / R7: picks ONE NVAPI DVC mode per display and pairs Get with the matching Set.
    /// NvAPI_GetDVCInfoEx + NvAPI_SetDVCLevelEx are tried first (they report a real default level, so
    /// negative DriverVibrance works), then the legacy NvAPI_GetDVCInfo + NvAPI_SetDVCLevel. A mode is
    /// only usable when both its Get and Set exist; a result with Current = Min = Max = 0 counts as
    /// "not supported" (as NvAPIWrapper does). The choice is remembered per display. If the first Ex Set
    /// fails (SetDVCLevelEx's ID is UNVERIFIED) and legacy reports the same Min..Max, the display switches
    /// to legacy for good and the level is written through the legacy setter; with a different range the
    /// call fails rather than writing an Ex-range level through the legacy setter.
    /// Whether the Ex and legacy APIs use the same scale is UNVERIFIED.
    /// </summary>
    internal sealed class DvcModes
    {
        private const int Ok = 0;
        private readonly DvcGet getEx, getLegacy;
        private readonly DvcSet setEx, setLegacy;
        private readonly Dictionary<string, DvcMode> mode = new Dictionary<string, DvcMode>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> exSetConfirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DvcModes(DvcGet getEx, DvcSet setEx, DvcGet getLegacy, DvcSet setLegacy)
        {
            this.getEx = getEx; this.setEx = setEx; this.getLegacy = getLegacy; this.setLegacy = setLegacy;
        }

        public bool ExUsable { get { return getEx != null && setEx != null; } }
        public bool LegacyUsable { get { return getLegacy != null && setLegacy != null; } }

        public DvcMode ModeOf(string device)
        {
            DvcMode m;
            return mode.TryGetValue(device ?? "", out m) ? m : DvcMode.Unknown;
        }

        private static bool Valid(VibranceInfo i)
        {
            return !(i.Current == 0 && i.Min == 0 && i.Max == 0);
        }

        private bool Read(DvcMode m, IntPtr h, out VibranceInfo info, out string error)
        {
            int st = m == DvcMode.Ex ? getEx(h, out info) : getLegacy(h, out info);
            string name = m == DvcMode.Ex ? "NvAPI_GetDVCInfoEx" : "NvAPI_GetDVCInfo";
            if (st != Ok) { error = name + " failed (" + st + ")."; return false; }
            if (!Valid(info)) { error = name + " reports no vibrance range (0..0)."; return false; }
            error = "";
            return true;
        }

        public bool TryGet(string device, IntPtr h, out VibranceInfo info, out string error)
        {
            string key = device ?? "";
            DvcMode m = ModeOf(key);
            if (m != DvcMode.Unknown) return Read(m, h, out info, out error);
            string exError = "";
            if (ExUsable)
            {
                if (Read(DvcMode.Ex, h, out info, out exError)) { mode[key] = DvcMode.Ex; error = ""; return true; }
            }
            if (LegacyUsable)
            {
                if (Read(DvcMode.Legacy, h, out info, out error)) { mode[key] = DvcMode.Legacy; return true; }
                if (exError.Length > 0) error = exError + " " + error;
                return false;
            }
            info = new VibranceInfo();
            error = exError.Length > 0 ? exError : "No usable NVAPI DVC Get/Set pair.";
            return false;
        }

        public bool TrySet(string device, IntPtr h, int level, out string error)
        {
            string key = device ?? "";
            VibranceInfo probe;
            if (ModeOf(key) == DvcMode.Unknown && !TryGet(key, h, out probe, out error)) return false;
            if (ModeOf(key) == DvcMode.Legacy)
            {
                int st = setLegacy(h, level);
                error = st == Ok ? "" : "NvAPI_SetDVCLevel failed (" + st + ").";
                return st == Ok;
            }
            int status = setEx(h, level);
            if (status == Ok) { exSetConfirmed.Add(key); error = ""; return true; }
            error = "NvAPI_SetDVCLevelEx failed (" + status + ").";
            if (exSetConfirmed.Contains(key) || !LegacyUsable) return false;
            VibranceInfo exInfo, legacyInfo;
            string e1, e2;
            if (!Read(DvcMode.Ex, h, out exInfo, out e1) || !Read(DvcMode.Legacy, h, out legacyInfo, out e2)) return false;
            mode[key] = DvcMode.Legacy;
            if (exInfo.Min != legacyInfo.Min || exInfo.Max != legacyInfo.Max)
            {
                error += " Switched to legacy NvAPI_SetDVCLevel (range " + legacyInfo.Min + ".." + legacyInfo.Max +
                    "); apply the preset again.";
                return false;
            }
            int st2 = setLegacy(h, level);
            error = st2 == Ok ? "" : error + " NvAPI_SetDVCLevel failed (" + st2 + ").";
            return st2 == Ok;
        }
    }

    // ------------------------------------------------------------------ AMD

    public sealed class AmdVibrance : IVibranceBackend, IDisposable
    {
        private const int AdlOk = 0;
        private const int AdlDisplayColorSaturation = 1 << 2;
        private const int AdlMaxPath = 256;
        // ADL SDK ADL_DISPLAY_DISPLAYINFO_DISPLAYCONNECTED / _DISPLAYMAPPED (UNVERIFIED on hardware).
        private const int AdlDisplayConnected = 0x00000001;
        private const int AdlDisplayMapped = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdlAdapterInfo
        {
            public int Size;
            public int AdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string UDID;
            public int BusNumber;
            public int DriverNumber;
            public int FunctionNumber;
            public int VendorID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DisplayName;
            public int Present;
            public int Exist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string PNPString;
            public int OSDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlDisplayId
        {
            public int DisplayLogicalIndex;
            public int DisplayPhysicalIndex;
            public int DisplayLogicalAdapterIndex;
            public int DisplayPhysicalAdapterIndex;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdlDisplayInfo
        {
            public AdlDisplayId DisplayID;
            public int DisplayControllerIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DisplayManufacturerName;
            public int DisplayType;
            public int DisplayOutputType;
            public int DisplayConnector;
            public int DisplayInfoMask;
            public int DisplayInfoValue;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AdlMallocFn(int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MainControlCreateFn(AdlMallocFn callback, int enumConnectedAdapters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MainControlDestroyFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NumberOfAdaptersGetFn(ref int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdapterInfoGetFn(IntPtr info, int inputSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DisplayInfoGetFn(int adapterIndex, ref int count, out IntPtr info, int forceDetect);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ColorGetFn(int adapterIndex, int displayIndex, int colorType,
            ref int current, ref int defaultValue, ref int min, ref int max, ref int step);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ColorSetFn(int adapterIndex, int displayIndex, int colorType, int value);

        // Kept in a static field so the GC never collects the callback ADL holds on to.
        private static readonly AdlMallocFn Malloc = AllocateForAdl;

        private IntPtr module;
        private MainControlDestroyFn destroy;
        private NumberOfAdaptersGetFn numberOfAdapters;
        private AdapterInfoGetFn adapterInfo;
        private DisplayInfoGetFn displayInfo;
        private ColorGetFn colorGet;
        private ColorSetFn colorSet;
        private readonly AdlProbePolicy probe = new AdlProbePolicy();
        private readonly object sync = new object();

        private AmdVibrance() { }

        private static IntPtr AllocateForAdl(int size)
        {
            return Marshal.AllocCoTaskMem(size);
        }

        public static AmdVibrance TryCreate(out string error)
        {
            error = "";
            IntPtr module = NativeLibrary.LoadLibrary("atiadlxx.dll");
            if (module == IntPtr.Zero) module = NativeLibrary.LoadLibrary("atiadlxy.dll");
            if (module == IntPtr.Zero)
            {
                error = "atiadlxx.dll not found";
                return null;
            }
            var amd = new AmdVibrance();
            amd.module = module;
            var create = NativeLibrary.GetDelegate<MainControlCreateFn>(module, "ADL_Main_Control_Create");
            amd.destroy = NativeLibrary.GetDelegate<MainControlDestroyFn>(module, "ADL_Main_Control_Destroy");
            amd.numberOfAdapters = NativeLibrary.GetDelegate<NumberOfAdaptersGetFn>(module, "ADL_Adapter_NumberOfAdapters_Get");
            amd.adapterInfo = NativeLibrary.GetDelegate<AdapterInfoGetFn>(module, "ADL_Adapter_AdapterInfo_Get");
            amd.displayInfo = NativeLibrary.GetDelegate<DisplayInfoGetFn>(module, "ADL_Display_DisplayInfo_Get");
            amd.colorGet = NativeLibrary.GetDelegate<ColorGetFn>(module, "ADL_Display_Color_Get");
            amd.colorSet = NativeLibrary.GetDelegate<ColorSetFn>(module, "ADL_Display_Color_Set");
            if (create == null || amd.numberOfAdapters == null || amd.adapterInfo == null ||
                amd.displayInfo == null || amd.colorGet == null || amd.colorSet == null)
            {
                NativeLibrary.FreeLibrary(module);
                amd.module = IntPtr.Zero;
                error = "ADL display colour functions missing";
                return null;
            }
            if (create(Malloc, 1) != AdlOk)
            {
                NativeLibrary.FreeLibrary(module);
                amd.module = IntPtr.Zero;
                error = "ADL_Main_Control_Create failed";
                return null;
            }
            return amd;
        }

        public string Vendor { get { return "AMD"; } }
        public bool IsSupported { get { return module != IntPtr.Zero; } }
        public string Status { get { return "AMD saturation (ADL Display_Color) - UNVERIFIED on hardware"; } }

        private bool TryFind(string deviceName, out int adapterIndex, out int displayIndex, out string error)
        {
            adapterIndex = -1;
            displayIndex = -1;
            error = "Display " + deviceName + " is not driven by an AMD GPU.";
            int forceDetect = probe.NextForceDetect();
            int count = 0;
            if (numberOfAdapters(ref count) != AdlOk || count <= 0 || count > 64) return false;
            int size = Marshal.SizeOf(typeof(AdlAdapterInfo));
            IntPtr buffer = Marshal.AllocCoTaskMem(size * count);
            try
            {
                // Zero the buffer so unread fields are defined.
                Marshal.Copy(new byte[size * count], 0, buffer, size * count);
                if (adapterInfo(buffer, size * count) != AdlOk) return false;
                for (int i = 0; i < count; i++)
                {
                    var a = (AdlAdapterInfo)Marshal.PtrToStructure(
                        new IntPtr(buffer.ToInt64() + (long)i * size), typeof(AdlAdapterInfo));
                    if (!string.Equals(a.DisplayName, deviceName, StringComparison.OrdinalIgnoreCase)) continue;

                    int displays = 0;
                    IntPtr info;
                    // Fix A7: force connector re-detection only on the first probe (and after a failed
                    // call, see InvalidateDetection); every other call reads the driver's cached state.
                    if (displayInfo(a.AdapterIndex, ref displays, out info, forceDetect) != AdlOk) continue;
                    try
                    {
                        int dsize = Marshal.SizeOf(typeof(AdlDisplayInfo));
                        for (int j = 0; j < displays; j++)
                        {
                            var d = (AdlDisplayInfo)Marshal.PtrToStructure(
                                new IntPtr(info.ToInt64() + (long)j * dsize), typeof(AdlDisplayInfo));
                            if (d.DisplayID.DisplayLogicalAdapterIndex != a.AdapterIndex) continue;
                            int need = AdlDisplayConnected | AdlDisplayMapped;
                            if ((d.DisplayInfoValue & need) != need) continue;
                            adapterIndex = a.AdapterIndex;
                            displayIndex = d.DisplayID.DisplayLogicalIndex;
                            error = "";
                            return true;
                        }
                    }
                    finally
                    {
                        if (info != IntPtr.Zero) Marshal.FreeCoTaskMem(info);
                    }
                }
                return false;
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        public bool TryGet(string deviceName, out VibranceInfo info, out string error)
        {
            info = new VibranceInfo();
            lock (sync)
            {
                if (module == IntPtr.Zero) { error = "ADL unloaded."; return false; }
                int adapter, display;
                if (!TryFind(deviceName, out adapter, out display, out error)) { probe.Invalidate(); return false; }
                int current = 0, def = 0, min = 0, max = 0, step = 0;
                int status = colorGet(adapter, display, AdlDisplayColorSaturation,
                    ref current, ref def, ref min, ref max, ref step);
                if (status != AdlOk)
                {
                    probe.Invalidate();
                    error = "ADL_Display_Color_Get failed (" + status + ").";
                    return false;
                }
                probe.Succeeded();
                info.Current = current;
                info.Default = def;
                info.Min = min;
                info.Max = max;
                return true;
            }
        }

        public bool TrySet(string deviceName, int level, out string error)
        {
            lock (sync)
            {
                if (module == IntPtr.Zero) { error = "ADL unloaded."; return false; }
                int adapter, display;
                if (!TryFind(deviceName, out adapter, out display, out error)) { probe.Invalidate(); return false; }
                int status = colorSet(adapter, display, AdlDisplayColorSaturation, level);
                if (status != AdlOk)
                {
                    probe.Invalidate();
                    error = "ADL_Display_Color_Set failed (" + status + ").";
                    return false;
                }
                probe.Succeeded();
                return true;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (module == IntPtr.Zero) return;
                try { if (destroy != null) destroy(); } catch { }
                NativeLibrary.FreeLibrary(module);
                module = IntPtr.Zero;
            }
        }
    }

    /// <summary>Fix A7: iForceDetect for ADL_Display_DisplayInfo_Get. 1 on the first probe; 0 after that.
    /// A failed lookup or colour call forces ONE re-detection (a monitor may have been reconnected), but
    /// repeated failures (e.g. a display on another GPU) do not force again until a call succeeds.</summary>
    internal sealed class AdlProbePolicy
    {
        private bool forceNext = true;
        private bool forcedSinceSuccess;

        public int NextForceDetect()
        {
            if (!forceNext) return 0;
            forceNext = false;
            forcedSinceSuccess = true;
            return 1;
        }

        public void Invalidate() { if (!forcedSinceSuccess) forceNext = true; }

        public void Succeeded() { forcedSinceSuccess = false; }
    }
}
