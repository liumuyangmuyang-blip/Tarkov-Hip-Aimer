using System;
using System.Runtime.InteropServices;

namespace TarkovCrosshair
{
    /// <summary>
    /// v5.1 (W33): GPU memory per process from the Windows performance counters (PDH), the numbers Task Manager shows in its
    /// "Dedicated GPU memory" column: "\GPU Process Memory(*)\Dedicated Usage" / "Shared Usage" (instances
    /// "pid_N_luid_..._phys_K", summed per pid) and "\GPU Adapter Memory(*)\Dedicated Usage" (all processes). Read-only,
    /// public counters; nothing of the game process is opened or read. Overlay worker thread only, about once per second.
    /// Never throws: on any failure Sample returns false and Error says why (e.g. no PDH outside Windows).
    /// </summary>
    internal sealed class GpuMemSampler : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll", ExactSpelling = true)]
        private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
        [DllImport("pdh.dll", ExactSpelling = true)]
        private static extern uint PdhCloseQuery(IntPtr query);

        private const uint PDH_FMT_LARGE = 0x00000400;
        private const uint PDH_MORE_DATA = 0x800007D2;
        private const uint PDH_CSTATUS_VALID_DATA = 0, PDH_CSTATUS_NEW_DATA = 1;

        public const string PathDedicated = @"\GPU Process Memory(*)\Dedicated Usage";
        public const string PathShared = @"\GPU Process Memory(*)\Shared Usage";
        public const string PathAdapter = @"\GPU Adapter Memory(*)\Dedicated Usage";

        private IntPtr query, cDed, cShared, cAdapter;
        private IntPtr buf;
        private uint bufSize;
        private bool opened, failedOpen;
        public string Error;

        private bool Open()
        {
            if (opened) return true;
            if (failedOpen) return false;
            try
            {
                uint r = PdhOpenQueryW(null, IntPtr.Zero, out query);
                if (r != 0) return OpenFail("PdhOpenQuery", r);
                r = PdhAddEnglishCounterW(query, PathDedicated, IntPtr.Zero, out cDed);
                if (r != 0) return OpenFail("PdhAddEnglishCounter " + PathDedicated, r);
                r = PdhAddEnglishCounterW(query, PathShared, IntPtr.Zero, out cShared);
                if (r != 0) return OpenFail("PdhAddEnglishCounter " + PathShared, r);
                if (PdhAddEnglishCounterW(query, PathAdapter, IntPtr.Zero, out cAdapter) != 0) cAdapter = IntPtr.Zero; // optional
                opened = true;
                return true;
            }
            catch (Exception ex)
            {
                failedOpen = true;
                Error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool OpenFail(string what, uint r)
        {
            failedOpen = true;
            Error = what + " failed, PDH status 0x" + r.ToString("X8") + " (Windows 10 1709+ with a WDDM 2 driver has these counters)";
            Close();
            return false;
        }

        /// <summary>Bytes for pid (and ours), summed over all GPU instances; allDedicated over every adapter (-1 when absent).</summary>
        public bool Sample(int pid, int ourPid, out long ded, out long shared, out long allDed, out long oursDed)
        {
            ded = shared = allDed = oursDed = -1;
            if (!Open()) return false;
            try
            {
                uint r = PdhCollectQueryData(query);
                if (r != 0) { Error = "PdhCollectQueryData failed, PDH status 0x" + r.ToString("X8"); return false; }
                long o1, o2, dummy;
                if (!SumFor(cDed, pid, ourPid, out ded, out oursDed, out dummy)) return false;
                if (!SumFor(cShared, pid, -1, out shared, out o2, out dummy)) return false;
                if (cAdapter != IntPtr.Zero) { if (!SumFor(cAdapter, -1, -1, out o1, out o2, out allDed)) allDed = -1; }
                return true;
            }
            catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        // PDH_FMT_COUNTERVALUE_ITEM_W: { LPWSTR szName; { DWORD CStatus; [pad]; union { LONGLONG largeValue; ... } } }
        private static readonly int ItemSize = IntPtr.Size == 8 ? 24 : 16;
        private static readonly int StatusOffset = IntPtr.Size;
        private static readonly int ValueOffset = IntPtr.Size == 8 ? 16 : 8;

        private bool SumFor(IntPtr counter, int pid, int ourPid, out long forPid, out long forOurs, out long all)
        {
            forPid = 0; forOurs = 0; all = 0;
            uint size = 0, count;
            uint r = PdhGetFormattedCounterArrayW(counter, PDH_FMT_LARGE, ref size, out count, IntPtr.Zero);
            if (r == 0 && count == 0) return true;
            if (r != PDH_MORE_DATA) { Error = "PdhGetFormattedCounterArray failed, PDH status 0x" + r.ToString("X8"); return false; }
            if (size > bufSize)
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                bufSize = size * 2;
                buf = Marshal.AllocHGlobal((int)bufSize);
            }
            size = bufSize;
            r = PdhGetFormattedCounterArrayW(counter, PDH_FMT_LARGE, ref size, out count, buf);
            if (r != 0) { Error = "PdhGetFormattedCounterArray failed, PDH status 0x" + r.ToString("X8"); return false; }
            for (int i = 0; i < count; i++)
            {
                IntPtr item = new IntPtr(buf.ToInt64() + (long)i * ItemSize);
                uint st = (uint)Marshal.ReadInt32(item, StatusOffset);
                if (st != PDH_CSTATUS_VALID_DATA && st != PDH_CSTATUS_NEW_DATA) continue;
                long v = Marshal.ReadInt64(item, ValueOffset);
                all += v;
                if (pid < 0 && ourPid < 0) continue;
                int p;
                if (!OverlayDiag.ParsePid(Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)), out p)) continue;
                if (p == pid) forPid += v;
                if (p == ourPid) forOurs += v;
            }
            return true;
        }

        private void Close()
        {
            if (query != IntPtr.Zero) { try { PdhCloseQuery(query); } catch (Exception) { } }
            query = cDed = cShared = cAdapter = IntPtr.Zero;
            opened = false;
        }

        public void Dispose()
        {
            Close();
            if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; bufSize = 0; }
        }
    }
}
