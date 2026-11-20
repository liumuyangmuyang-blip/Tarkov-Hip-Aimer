using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TarkovShade;

// v5.0.1 (W13, Leo: on some PCs v5.0 needed ~20 s before anything answered, or the crosshair and the tray menu froze; v4.0 was
// fine on the same PC). Hard time limits around the filter's slow driver calls, without editing the synced module:
//   - SerialWorker: ONE dedicated background thread per device API; every call waits at most N ms. A call that does not come
//     back marks the worker stuck: later calls fail at once (no pile-up) until the stuck call returns by itself.
//   - LazyVibrance: NVAPI / ADL (LoadLibrary + NvAPI_Initialize / ADL_Main_Control_Create) only on first real use (a preset
//     that changes saturation through the graphics driver), not at engine start, on its own thread, max 5 s.
//   - GatedDdcBackend: monitor DDC/CI (dxva2, can take seconds per monitor and hang on some monitors / docks / laptops) is not
//     touched at all until the 显示器硬件 card is opened (or a preset with monitor values is switched on, or a crash left
//     monitor values to put back); then every call runs on its own thread with a hard limit.
// Pure (no WinForms): tested with fake slow backends in tests-v4/TestsV501.cs.

namespace TarkovCrosshair
{
    internal sealed class SerialWorker
    {
        private readonly string name;
        private readonly object sync = new object();
        private readonly Queue<Action> queue = new Queue<Action>();
        private Thread thread;
        private volatile bool stuck;

        public SerialWorker(string name) { this.name = name; }

        /// <summary>True while an earlier call that timed out is still running.</summary>
        public bool Stuck { get { return stuck; } }
        public int ThreadId { get { Thread t = thread; return t == null ? 0 : t.ManagedThreadId; } }

        /// <summary>Runs fn on the worker thread and waits at most ms. false: timed out (error says so), stuck, or fn threw.</summary>
        public bool Invoke<T>(Func<T> fn, int ms, out T result, out string error)
        {
            result = default(T);
            error = "";
            if (stuck) { error = name + " is still busy with an earlier call that did not return"; return false; }
            ManualResetEvent done = new ManualResetEvent(false);
            T r = default(T);
            Exception ex = null;
            // v5.0.2 (AUDIT F10): 'finished' and 'timedOut' are only read and written under sync, so the waiter's timeout and the
            // job's end can never both miss each other (the old done.WaitOne(0) test could leave the worker stuck for the session).
            // v5.0.2 (H5 seed 50375): a call that was still WAITING in the queue when its caller gave up is cancelled: it must never
            // reach the device later (a late monitor write after the restore had already reported success).
            bool finished = false, timedOut = false, started = false, cancelled = false;
            Action job = delegate
            {
                lock (sync)
                {
                    if (cancelled) { finished = true; done.Close(); return; }
                    started = true;
                }
                try { r = fn(); }
                catch (Exception e) { ex = e; }
                finally
                {
                    bool abandoned;
                    lock (sync)
                    {
                        finished = true;
                        abandoned = timedOut;
                        if (abandoned) { stuck = false; Monitor.PulseAll(sync); }
                    }
                    if (abandoned) done.Close(); // the waiter has gone: nobody else touches the event (no handle leak per timeout)
                    else done.Set();
                }
            };
            lock (sync)
            {
                if (thread == null)
                {
                    Thread t = new Thread(Loop);
                    t.IsBackground = true;
                    t.Name = name;
                    thread = t;
                    t.Start();
                }
                queue.Enqueue(job);
                Monitor.PulseAll(sync);
            }
            if (!done.WaitOne(Math.Max(1, ms)))
            {
                lock (sync)
                {
                    if (!started) cancelled = true;
                    else if (!finished) { timedOut = true; stuck = true; }
                }
                if (cancelled) { error = name + " was still busy with an earlier call; this call did not start within " + ms + " ms (cancelled)"; return false; }
                if (timedOut) { error = name + " did not answer within " + ms + " ms"; return false; }
                done.WaitOne(); // the job finished at the limit: its Set follows right after its lock
            }
            done.Close();
            if (ex != null) { error = ex.Message; return false; }
            result = r;
            return true;
        }

        /// <summary>v5.0.2 (BRUTE P1-1 / P1-2): waits at most ms for a timed-out call to come back. True when not stuck (any more).</summary>
        public bool WaitIdle(int ms)
        {
            if (!stuck) return true;
            DateTime end = DateTime.UtcNow.AddMilliseconds(Math.Max(0, ms));
            lock (sync)
            {
                while (stuck)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return false;
                    Monitor.Wait(sync, left);
                }
                return true;
            }
        }

        private void Loop()
        {
            while (true)
            {
                Action job;
                lock (sync)
                {
                    while (queue.Count == 0) Monitor.Wait(sync);
                    job = queue.Dequeue();
                }
                job();
            }
        }
    }

    /// <summary>Graphics-driver vibrance, detected on first use (not at engine start), every call on one thread with a time limit.</summary>
    internal sealed class LazyVibrance : IVibranceBackend
    {
        private readonly Func<IVibranceBackend> detect;
        private readonly Action<string> log;
        private readonly object sync = new object();
        private readonly SerialWorker worker = new SerialWorker("FilterVibrance");
        private IVibranceBackend inner;
        public int DetectTimeoutMs = 5000, CallTimeoutMs = 2000;

        public LazyVibrance(Func<IVibranceBackend> detect, Action<string> log)
        {
            this.detect = detect;
            this.log = log ?? delegate(string s) { };
        }

        /// <summary>True once detection ran (tests: engine start must not run it).</summary>
        public bool Detected { get { lock (sync) return inner != null; } }

        private IVibranceBackend Get()
        {
            lock (sync)
            {
                if (inner != null) return inner;
                Stopwatch sw = Stopwatch.StartNew();
                IVibranceBackend r; string err;
                bool ok = worker.Invoke(detect, DetectTimeoutMs, out r, out err);
                if (!ok || r == null)
                {
                    r = new UnsupportedVibrance("Not supported: " + (ok ? "no backend" : err) + ".");
                    log("filter: 显卡驱动饱和度检测 (NVAPI / ADL) 失败，" + sw.ElapsedMilliseconds + " ms: " + err);
                }
                else log("filter: 显卡驱动饱和度检测 (NVAPI / ADL) " + sw.ElapsedMilliseconds + " ms: " + (r.IsSupported ? r.Vendor : r.Status));
                inner = r;
                return inner;
            }
        }

        public string Vendor { get { return Get().Vendor; } }
        public bool IsSupported { get { return Get().IsSupported; } }
        public string Status { get { return Get().Status; } }

        // v5.0.2 (AUDIT F9): every filter apply (slider tick, hold release, display change) used to make 2 driver calls even when
        // the level did not change. The last level read or confirmed per device is kept for KnownMs (renewed by every
        // confirmed set); a set to that same level returns at once, a read answers from it. A failed or timed-out call forgets
        // the device (the driver may or may not have taken it), so the next call goes to the driver again.
        private sealed class Known { public VibranceInfo Info; public DateTime At; }
        private readonly Dictionary<string, Known> known = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
        public int KnownMs = 5000;
        /// <summary>v5.0.2 (BRUTE P1-1): before a call, wait this long for an earlier timed-out driver call to come back.</summary>
        public int StuckWaitMs = 2500;
        /// <summary>Calls that reached the driver (tests).</summary>
        public int DriverGets, DriverSets;

        private bool Fresh(string deviceName, out VibranceInfo info)
        {
            info = new VibranceInfo();
            lock (sync)
            {
                Known k;
                if (!known.TryGetValue(deviceName ?? "", out k)) return false;
                if ((DateTime.UtcNow - k.At).TotalMilliseconds > KnownMs) { known.Remove(deviceName ?? ""); return false; }
                info = k.Info;
                return true;
            }
        }

        private void Remember(string deviceName, VibranceInfo info)
        {
            lock (sync)
            {
                Known k = new Known { Info = info, At = DateTime.UtcNow };
                known[deviceName ?? ""] = k;
                lastRead[deviceName ?? ""] = k;
            }
        }
        private void Forget(string deviceName) { lock (sync) known.Remove(deviceName ?? ""); }

        private volatile bool gaveUp;
        /// <summary>One bounded wait per stuck episode; while the same episode lasts, later calls fail fast.</summary>
        private bool Idle(out string error)
        {
            error = "";
            if (!worker.Stuck) { gaveUp = false; return true; }
            if (!gaveUp && worker.WaitIdle(StuckWaitMs)) return true;
            gaveUp = true;
            error = "Driver vibrance: FilterVibrance is still busy with an earlier call that did not return";
            log("filter: " + error);
            return false;
        }

        public bool TryGet(string deviceName, out VibranceInfo info, out string error)
        {
            IVibranceBackend b = Get();
            info = new VibranceInfo();
            if (!b.IsSupported) { error = b.Status; return false; }
            if (Fresh(deviceName, out info)) { error = ""; return true; }
            if (!Idle(out error)) return false;
            VibranceInfo got = new VibranceInfo(); string e2 = "";
            bool r; string err;
            Interlocked.Increment(ref DriverGets);
            bool ok = worker.Invoke(delegate { VibranceInfo i; string e; bool x = b.TryGet(deviceName, out i, out e); got = i; e2 = e; return x; },
                CallTimeoutMs, out r, out err);
            if (!ok) { Forget(deviceName); error = "Driver vibrance: " + err; log("filter: " + error); return false; }
            info = got; error = e2 ?? "";
            if (r) Remember(deviceName, got); else Forget(deviceName);
            return r;
        }

        public bool TrySet(string deviceName, int level, out string error)
        {
            IVibranceBackend b = Get();
            if (!b.IsSupported) { error = b.Status; return false; }
            VibranceInfo k;
            if (Fresh(deviceName, out k) && k.Current == level) { Remember(deviceName, k); error = ""; return true; }
            if (!Idle(out error)) { Forget(deviceName); return false; }
            string e2 = "";
            bool r; string err;
            Forget(deviceName); // unknown until the driver confirms
            Interlocked.Increment(ref DriverSets);
            bool ok = worker.Invoke(delegate { string e; bool x = b.TrySet(deviceName, level, out e); e2 = e; return x; },
                CallTimeoutMs, out r, out err);
            if (!ok) { error = "Driver vibrance: " + err; log("filter: " + error); return false; }
            error = e2 ?? "";
            if (r)
            {
                bool had; VibranceInfo last;
                lock (sync) { Known kk; had = lastRead.TryGetValue(deviceName ?? "", out kk); last = had ? kk.Info : new VibranceInfo(); }
                if (had) { last.Current = level; Remember(deviceName, last); }
            }
            return r;
        }

        // range (Min / Max / Default) of the last real read per device, so a confirmed set can be remembered with its range
        private readonly Dictionary<string, Known> lastRead = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Monitor DDC/CI: nothing until Allowed; then each call on one thread with a hard limit.</summary>
    internal sealed class GatedDdcBackend : IDdcBackend
    {
        public const string NotProbedText = "DDC/CI not probed yet (the 显示器硬件 card was not opened).";
        private readonly IDdcBackend inner;
        private readonly Action<string> log;
        private readonly SerialWorker worker = new SerialWorker("FilterDdc");
        private volatile bool allowed;
        private bool loggedTimeout;
        public int EnumerateTimeoutMs = 3000, CallTimeoutMs = 1500;
        /// <summary>v5.0.2 (BRUTE P1-2): before a call, wait this long for an earlier timed-out DDC call to come back.</summary>
        public int StuckWaitMs = 2000;
        /// <summary>Enumerate calls that reached the real backend (tests).</summary>
        public int RealEnumerates;

        public GatedDdcBackend(IDdcBackend inner, Action<string> log)
        {
            this.inner = inner;
            this.log = log ?? delegate(string s) { };
        }

        public bool Allowed { get { return allowed; } set { allowed = value; } }
        public bool Stuck { get { return worker.Stuck; } }

        public List<DdcMonitorHandle> Enumerate(out string error)
        {
            if (!allowed) { error = NotProbedText; return new List<DdcMonitorHandle>(); }
            Interlocked.Increment(ref RealEnumerates);
            Stopwatch sw = Stopwatch.StartNew();
            string e2 = "";
            List<DdcMonitorHandle> r; string err;
            bool ok = worker.Invoke(delegate { string e; List<DdcMonitorHandle> l = inner.Enumerate(out e); e2 = e; return l; },
                EnumerateTimeoutMs, out r, out err);
            if (!ok) { error = "DDC/CI: " + err; Timeout(error); return new List<DdcMonitorHandle>(); }
            error = e2 ?? "";
            if (r == null) r = new List<DdcMonitorHandle>();
            log("filter: 显示器 DDC/CI 列举 " + sw.ElapsedMilliseconds + " ms，" + r.Count + " 台");
            return r;
        }

        private void Timeout(string e)
        {
            if (loggedTimeout) return;
            loggedTimeout = true;
            log("filter: " + e + "（显示器硬件设置先停用，点「重新检测显示器」再试）");
        }

        /// <summary>「重新检测显示器」: allow one more try after an earlier timeout.</summary>
        public void ResetTimeoutNote() { loggedTimeout = false; gaveUp = false; }

        private volatile bool gaveUp;
        /// <summary>A stuck worker gets one bounded wait per stuck episode (a write that lands late is then followed by the next
        /// call instead of failing at once); while the same episode lasts, later calls fail fast (a hung monitor costs one wait).</summary>
        private void WaitStuck()
        {
            if (!worker.Stuck) { gaveUp = false; return; }
            if (gaveUp) return;
            if (!worker.WaitIdle(StuckWaitMs)) gaveUp = true;
        }

        private bool Call(Func<bool> fn) { return Call(fn, false); }

        /// <param name="write">a write first gets the one bounded wait for an earlier timed-out call (reads fail fast)</param>
        private bool Call(Func<bool> fn, bool write)
        {
            bool r; string err;
            if (write) WaitStuck();
            if (!worker.Invoke(fn, CallTimeoutMs, out r, out err)) { Timeout("DDC/CI: " + err); return false; }
            return r;
        }

        public bool GetCapabilities(IntPtr monitor, out uint caps)
        {
            uint c = 0;
            bool ok = Call(delegate { uint x; bool b = inner.GetCapabilities(monitor, out x); c = x; return b; });
            caps = ok ? c : 0;
            return ok;
        }

        public bool GetBrightness(IntPtr monitor, out uint min, out uint current, out uint max)
        {
            uint a = 0, b = 0, c = 0;
            bool ok = Call(delegate { uint x, y, z; bool r = inner.GetBrightness(monitor, out x, out y, out z); a = x; b = y; c = z; return r; });
            min = ok ? a : 0; current = ok ? b : 0; max = ok ? c : 0;
            return ok;
        }

        public bool SetBrightness(IntPtr monitor, uint value) { return Call(delegate { return inner.SetBrightness(monitor, value); }, true); }

        public bool GetContrast(IntPtr monitor, out uint min, out uint current, out uint max)
        {
            uint a = 0, b = 0, c = 0;
            bool ok = Call(delegate { uint x, y, z; bool r = inner.GetContrast(monitor, out x, out y, out z); a = x; b = y; c = z; return r; });
            min = ok ? a : 0; current = ok ? b : 0; max = ok ? c : 0;
            return ok;
        }

        public bool SetContrast(IntPtr monitor, uint value) { return Call(delegate { return inner.SetContrast(monitor, value); }, true); }

        public bool GetVcp(IntPtr monitor, byte code, out bool setParameter, out uint current, out uint max)
        {
            bool sp = false; uint b = 0, c = 0;
            bool ok = Call(delegate { bool s; uint y, z; bool r = inner.GetVcp(monitor, code, out s, out y, out z); sp = s; b = y; c = z; return r; });
            setParameter = ok && sp; current = ok ? b : 0; max = ok ? c : 0;
            return ok;
        }

        public bool SetVcp(IntPtr monitor, byte code, uint value) { return Call(delegate { return inner.SetVcp(monitor, code, value); }, true); }

        public void Destroy(IntPtr monitor) { Call(delegate { inner.Destroy(monitor); return true; }); }
    }
}
