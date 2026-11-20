// W29 (v5.1) Windows CI helper: change the screen resolution and the Windows display scale (100/125/150/... %) of the
// primary screen WITHOUT signing out, so each probe run starts at a real Windows DPI. CI only, never shipped.
//   DpiTool.exe modes                 list the resolutions the display adapter offers
//   DpiTool.exe res <width> <height>  switch the primary screen to that resolution (not saved; back after the job)
//   DpiTool.exe get                   print "scale cur=<%> min=<%> max=<%> recommended=<%>" for the primary screen
//   DpiTool.exe set <percent>         set the display scale (only values between min and max for this resolution)
// Exit code 0 = done, 2 = bad arguments, 3 = Windows refused.
// The scale calls are the undocumented DisplayConfig types -3 / -4 used by the Settings app (same approach as the MIT
// "SetDPI" tool by imniko). A system-DPI-aware process started AFTER the change gets the new DPI (Windows 10 1803+,
// per-process system DPI; the workflow also sets HKCU\Control Panel\Desktop\EnablePerProcessSystemDPI=1). The probe
// checks its own DPI and reports a mismatch, so a runner that cannot do this never passes silently.
using System;
using System.Runtime.InteropServices;

internal static class DpiTool
{
    private static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSource { public LUID AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTarget
    {
        public LUID AdapterId; public uint Id; public uint ModeInfoIdx; public uint OutputTechnology; public uint Rotation;
        public uint Scaling; public uint RefreshNum; public uint RefreshDen; public uint ScanLineOrdering; public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ModeInfo
    {
        [FieldOffset(0)] public uint InfoType; // 1 = source, 2 = target, 3 = desktop image
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public LUID AdapterId;
        [FieldOffset(16)] public uint SrcWidth;
        [FieldOffset(20)] public uint SrcHeight;
        [FieldOffset(24)] public uint SrcPixelFormat;
        [FieldOffset(28)] public int SrcX;
        [FieldOffset(32)] public int SrcY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public int Type; public uint Size; public LUID AdapterId; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScaleGet { public Header H; public int MinRel; public int CurRel; public int MaxRel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScaleSet { public Header H; public int Rel; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency, ICMMethod, ICMIntent, MediaType,
            DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] p, ref uint modes, [Out] ModeInfo[] m, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetInfo(ref ScaleGet req);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] private static extern int SetInfo(ref ScaleSet req);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsW(ref DEVMODE dm, int flags);

    private static int Main(string[] args)
    {
        try
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (cmd == "modes") return Modes();
            if (cmd == "res" && args.Length == 3) return Res(int.Parse(args[1]), int.Parse(args[2]));
            if (cmd == "get") return Get();
            if (cmd == "set" && args.Length == 2) return Set(int.Parse(args[1]));
            Console.WriteLine("usage: DpiTool modes | res <w> <h> | get | set <percent>");
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("DpiTool error: " + ex.Message);
            return 3;
        }
    }

    private static int Modes()
    {
        DEVMODE dm = NewMode();
        System.Collections.Generic.SortedSet<string> seen = new System.Collections.Generic.SortedSet<string>();
        for (int i = 0; EnumDisplaySettingsW(null, i, ref dm); i++)
            if (dm.BitsPerPel >= 24) seen.Add(dm.PelsWidth.ToString("00000") + "x" + dm.PelsHeight);
        foreach (string s in seen) Console.WriteLine("mode " + s.TrimStart('0'));
        DEVMODE cur = NewMode();
        if (EnumDisplaySettingsW(null, -1, ref cur)) Console.WriteLine("current " + cur.PelsWidth + "x" + cur.PelsHeight);
        return 0;
    }

    private static DEVMODE NewMode()
    {
        DEVMODE dm = new DEVMODE();
        dm.DeviceName = new string('\0', 32);
        dm.FormName = new string('\0', 32);
        dm.Size = (short)Marshal.SizeOf(typeof(DEVMODE));
        return dm;
    }

    private static int Res(int w, int h)
    {
        DEVMODE dm = NewMode();
        if (!EnumDisplaySettingsW(null, -1, ref dm)) { Console.WriteLine("res: cannot read the current mode"); return 3; }
        dm.PelsWidth = w;
        dm.PelsHeight = h;
        dm.Fields = 0x80000 | 0x100000; // DM_PELSWIDTH | DM_PELSHEIGHT
        int rc = ChangeDisplaySettingsW(ref dm, 0); // 0 = for this session only
        Console.WriteLine("res " + w + "x" + h + " -> " + (rc == 0 ? "ok" : "refused (" + rc + ")"));
        return rc == 0 ? 0 : 3;
    }

    private static bool Primary(out LUID adapter, out uint sourceId)
    {
        adapter = new LUID();
        sourceId = 0;
        uint np, nm;
        if (GetDisplayConfigBufferSizes(2, out np, out nm) != 0) return false; // QDC_ONLY_ACTIVE_PATHS
        PathInfo[] paths = new PathInfo[np];
        ModeInfo[] modes = new ModeInfo[nm];
        if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0 || np == 0) return false;
        int pick = 0;
        for (int i = 0; i < np; i++)
        {
            uint mi = paths[i].Source.ModeInfoIdx;
            if (mi < nm && modes[mi].InfoType == 1 && modes[mi].SrcX == 0 && modes[mi].SrcY == 0) { pick = i; break; }
        }
        adapter = paths[pick].Source.AdapterId;
        sourceId = paths[pick].Source.Id;
        return true;
    }

    private static bool Query(out int rec, out int min, out int cur, out int max, out LUID adapter, out uint id)
    {
        rec = min = cur = max = -1;
        if (!Primary(out adapter, out id)) return false;
        ScaleGet q = new ScaleGet();
        q.H.Type = -3;
        q.H.Size = (uint)Marshal.SizeOf(typeof(ScaleGet));
        q.H.AdapterId = adapter;
        q.H.Id = id;
        if (GetInfo(ref q) != 0) return false;
        rec = Math.Abs(q.MinRel);
        min = 0;
        cur = rec + q.CurRel;
        max = rec + q.MaxRel;
        return true;
    }

    private static string P(int idx) { return idx >= 0 && idx < Steps.Length ? Steps[idx].ToString() : "?"; }

    private static int Get()
    {
        int rec, min, cur, max; LUID a; uint id;
        if (!Query(out rec, out min, out cur, out max, out a, out id)) { Console.WriteLine("scale unknown (DisplayConfig refused)"); return 3; }
        Console.WriteLine("scale cur=" + P(cur) + " min=" + P(min) + " max=" + P(max) + " recommended=" + P(rec));
        return 0;
    }

    private static int Set(int percent)
    {
        int target = Array.IndexOf(Steps, percent);
        if (target < 0) { Console.WriteLine("set: " + percent + " is not a Windows scale step"); return 2; }
        int rec, min, cur, max; LUID a; uint id;
        if (!Query(out rec, out min, out cur, out max, out a, out id)) { Console.WriteLine("set: DisplayConfig refused the query"); return 3; }
        if (target > max) { Console.WriteLine("set: " + percent + "% is above the maximum " + P(max) + "% at this resolution"); return 3; }
        if (target == cur) { Console.WriteLine("set " + percent + "% -> already"); return 0; }
        ScaleSet s = new ScaleSet();
        s.H.Type = -4;
        s.H.Size = (uint)Marshal.SizeOf(typeof(ScaleSet));
        s.H.AdapterId = a;
        s.H.Id = id;
        s.Rel = target - rec;
        int rc = SetInfo(ref s);
        int rec2, min2, cur2, max2; LUID a2; uint id2;
        Query(out rec2, out min2, out cur2, out max2, out a2, out id2);
        Console.WriteLine("set " + percent + "% -> " + (rc == 0 && cur2 == target ? "ok" : "refused (" + rc + ", now " + P(cur2) + "%)"));
        return rc == 0 && cur2 == target ? 0 : 3;
    }
}
