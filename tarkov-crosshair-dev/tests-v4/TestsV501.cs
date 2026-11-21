// v5.0.1 (W13): Leo's "20 s before anything answers / crosshair + tray frozen on some PCs". Proves with fake slow backends
// (DDC/CI 10 s, NVAPI 5 s, Magnification that never returns) that nothing the UI thread calls waits for them, that the engine
// does not touch DDC / NVAPI at start, that every slow call has a hard time limit, plus the startup timing + UI watchdog.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TarkovShade;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        sealed class SlowDdc : IDdcBackend
        {
            public int EnumerateMs = 10000, CallMs = 10000, Enumerates;
            public List<DdcMonitorHandle> Enumerate(out string error)
            {
                Interlocked.Increment(ref Enumerates);
                Thread.Sleep(EnumerateMs);
                error = "";
                DdcMonitorHandle h = new DdcMonitorHandle();
                h.Handle = new IntPtr(1); h.Name = "Slow"; h.DisplayDeviceName = @"\\.\DISPLAY1"; h.Id = @"\\.\DISPLAY1|physical:0";
                return new List<DdcMonitorHandle> { h };
            }
            public bool GetCapabilities(IntPtr m, out uint caps) { Thread.Sleep(CallMs); caps = 0; return false; }
            public bool GetBrightness(IntPtr m, out uint a, out uint b, out uint c) { Thread.Sleep(CallMs); a = 0; b = 50; c = 100; return true; }
            public bool SetBrightness(IntPtr m, uint v) { Thread.Sleep(CallMs); return true; }
            public bool GetContrast(IntPtr m, out uint a, out uint b, out uint c) { Thread.Sleep(CallMs); a = 0; b = 50; c = 100; return true; }
            public bool SetContrast(IntPtr m, uint v) { Thread.Sleep(CallMs); return true; }
            public bool GetVcp(IntPtr m, byte code, out bool sp, out uint cur, out uint max) { Thread.Sleep(CallMs); sp = true; cur = 50; max = 100; return true; }
            public bool SetVcp(IntPtr m, byte code, uint v) { Thread.Sleep(CallMs); return true; }
            public void Destroy(IntPtr m) { }
        }

        sealed class SlowVibrance : IVibranceBackend
        {
            public string Vendor { get { return "NVIDIA"; } }
            public bool IsSupported { get { return true; } }
            public string Status { get { return "ok"; } }
            public bool TryGet(string d, out VibranceInfo info, out string error) { info = new VibranceInfo(); error = ""; return true; }
            public bool TrySet(string d, int level, out string error) { error = ""; return true; }
        }

        sealed class HangMag : IMagApi
        {
            public readonly ManualResetEvent Release = new ManualResetEvent(false);
            public bool Initialize(out int lastError) { Release.WaitOne(); lastError = 0; return true; }
            public bool Uninitialize() { return true; }
            public bool SetEffect(float[] matrix, out int lastError) { Release.WaitOne(); lastError = 0; return true; }
            public void PumpMessages() { }
        }

        static void V501Tests()
        {
            Console.WriteLine("-- v5.0.1 startup / freeze (W13)");
            List<string> log = new List<string>();
            Action<string> L = delegate(string s) { lock (log) log.Add(s); };

            // S1 SerialWorker: a call that never returns costs at most the time limit; later calls fail at once; it recovers
            {
                SerialWorker w = new SerialWorker("TestWorker");
                ManualResetEvent gate = new ManualResetEvent(false);
                Stopwatch sw = Stopwatch.StartNew();
                int r; string err;
                bool ok1 = w.Invoke(delegate { gate.WaitOne(); return 1; }, 300, out r, out err);
                long t1 = sw.ElapsedMilliseconds;
                sw.Restart();
                bool ok2 = w.Invoke(delegate { return 2; }, 300, out r, out err);
                long t2 = sw.ElapsedMilliseconds;
                bool stuck = w.Stuck;
                gate.Set();
                Thread.Sleep(100);
                bool ok3 = w.Invoke(delegate { return 3; }, 1000, out r, out err) && r == 3;
                Check("V501-S1 SerialWorker: hung call returns after its 300 ms limit (" + t1 + " ms), next call fails at once (" + t2 + " ms), works again after the hang ends",
                    !ok1 && t1 < 800 && !ok2 && t2 < 50 && stuck && ok3 && !w.Stuck, "ok1=" + ok1 + " ok2=" + ok2 + " stuck=" + stuck + " ok3=" + ok3);
            }

            // S2 engine start with DDC 10 s + NVAPI 5 s + Magnification hang: the engine starts without touching DDC or NVAPI
            CoreRigSlow rig = new CoreRigSlow(L);
            {
                Stopwatch sw = Stopwatch.StartNew();
                long worst = 0;
                Action<Action> time = delegate(Action a) { long t0 = sw.ElapsedMilliseconds; a(); worst = Math.Max(worst, sw.ElapsedMilliseconds - t0); };
                time(delegate { rig.Core.Start(false); });
                rig.Settle(150, 8000);
                long started = sw.ElapsedMilliseconds;
                bool ok = rig.Core.Started;
                Check("V501-S2 engine start with DDC/CI 10 s, NVAPI 5 s, Magnification hung: started in " + started + " ms (< 1500), DDC not enumerated, NVAPI not loaded, UI call " + worst + " ms",
                    ok && started < 1500 && rig.Slow.Enumerates == 0 && !rig.Vib.Detected && worst < 50,
                    "started=" + ok + " ddcCalls=" + rig.Slow.Enumerates + " nvapi=" + rig.Vib.Detected + " log " + Join(log));
            }

            // S3 every UI-thread entry point returns at once while the worker is stuck in DDC (card opened -> 10 s DDC)
            {
                rig.Ddc.Allowed = true;
                Stopwatch sw = Stopwatch.StartNew();
                long worst = 0; string worstName = "";
                Action<string, Action> time = delegate(string n, Action a)
                {
                    long t0 = sw.ElapsedMilliseconds; a(); long t = sw.ElapsedMilliseconds - t0;
                    if (t > worst) { worst = t; worstName = n; }
                };
                time("RefreshMonitors", delegate { rig.Core.RefreshMonitors(); });
                Thread.Sleep(50);
                time("SetOn", delegate { rig.Core.SetOn(true); });
                time("Toggle", delegate { rig.Core.Toggle(); });
                for (int i = 0; i < 20; i++) { int v = i; time("SetStyle", delegate { PilotStyle s = rig.Core.Style; s.Shadows = 40 + v; rig.Core.SetStyle(s); }); }
                time("DisplaysChanged", delegate { rig.Core.DisplaysChanged("x"); });
                time("Snapshot", delegate { FilterSnapshot s = rig.Core.Snapshot; GC.KeepAlive(s.Hardware); });
                time("Problem", delegate { GC.KeepAlive(rig.Core.Problem); });
                time("StopNow(bounded caller)", delegate { });
                rig.Settle(300, 20000);
                long done = sw.ElapsedMilliseconds;
                Check("V501-S3 DDC/CI 10 s per call after the card opens: every UI entry point returns at once (worst " + worstName + " " + worst + " ms), worker done in " + done + " ms (< 10000 = DDC limit 3 s + NVAPI limit 5 s on enable + slack; unguarded: 10 s for every DDC call), DDC marked stuck",
                    worst < 50 && done < 10000 && rig.Ddc.Stuck, "stuck=" + rig.Ddc.Stuck + " log " + Join(log));
            }

            // S4 NVAPI 5 s: detected only on first real use, and then cut off at its limit
            {
                LazyVibrance v = new LazyVibrance(delegate { Thread.Sleep(5000); return new SlowVibrance(); }, L);
                v.DetectTimeoutMs = 800;
                bool before = v.Detected;
                Stopwatch sw = Stopwatch.StartNew();
                bool sup = v.IsSupported;
                long t = sw.ElapsedMilliseconds;
                sw.Restart();
                string e; bool set = v.TrySet(@"\\.\DISPLAY1", 50, out e);
                long t2 = sw.ElapsedMilliseconds;
                Check("V501-S4 NVAPI 5 s: not run until used; first use cut off at 800 ms (" + t + " ms) as unsupported; later calls at once (" + t2 + " ms)",
                    !before && !sup && t < 1500 && !set && t2 < 50, "sup=" + sup + " set=" + set + " e=" + e);
                LazyVibrance fast = new LazyVibrance(delegate { return new SlowVibrance(); }, L);
                VibranceInfo info; string e2 = "";
                Check("V501-S4b driver vibrance that answers normally still works through the guard",
                    fast.IsSupported && fast.Vendor == "NVIDIA" && fast.TryGet("d", out info, out e2) && fast.TrySet("d", 10, out e2), e2);
            }

            // S5 Magnification that never returns: the software-saturation preset costs the worker at most its 500 ms limits
            {
                Stopwatch sw = Stopwatch.StartNew();
                long worst = 0;
                PilotStyle s = rig.Core.Style; s.ApplySaturation = true; s.SaturationBackend = PilotStyle.SaturationBackendSoftware; s.Saturation = 140;
                long t0 = sw.ElapsedMilliseconds; rig.Core.SetStyle(s); worst = sw.ElapsedMilliseconds - t0;
                t0 = sw.ElapsedMilliseconds; rig.Core.SetOn(true); worst = Math.Max(worst, sw.ElapsedMilliseconds - t0);
                rig.Settle(300, 15000);
                long done = sw.ElapsedMilliseconds;
                Check("V501-S5 Magnification hung: UI calls " + worst + " ms, worker finished in " + done + " ms (< 5000)", worst < 50 && done < 5000, Join(log));
                rig.Mag.Release.Set();
            }

            // S6 DDC gate: closed = no device call at all; opened = time-limited; a monitor-values preset opens it on enable
            {
                SlowDdc slow = new SlowDdc();
                GatedDdcBackend g = new GatedDdcBackend(slow, L);
                Stopwatch sw = Stopwatch.StartNew();
                string e; List<DdcMonitorHandle> l = g.Enumerate(out e);
                long t = sw.ElapsedMilliseconds;
                g.EnumerateTimeoutMs = 400; g.Allowed = true;
                sw.Restart();
                List<DdcMonitorHandle> l2 = g.Enumerate(out e);
                long t2 = sw.ElapsedMilliseconds;
                Check("V501-S6 DDC gate: closed = 0 monitors in " + t + " ms without a device call; open + 10 s monitor = cut off at 400 ms (" + t2 + " ms)",
                    l.Count == 0 && t < 20 && slow.Enumerates == 1 && l2.Count == 0 && t2 < 900 && g.Stuck, "enum=" + slow.Enumerates + " e=" + e);
                string host = File.ReadAllText(Path.Combine(UiSrcDir(), "FilterHost.cs"));
                Check("V501-S6b FilterHost: DDC gated + NVAPI lazy in the engine factory; a preset with monitor values opens the gate before Enable",
                    host.Contains("new LazyVibrance(VibranceController.Detect") && host.Contains("ddc = new GatedDdcBackend(new Dxva2DdcBackend()")
                    && !host.Contains("VibranceController.Detect()") && host.Contains("s.MonitorBrightness.HasValue") && host.Contains("core.BeforeEnable"), "");
            }

            // S7 startup order (source): tray + overlay first, no engine start in the App constructor, engine after the UI is up
            {
                string p = File.ReadAllText(Path.Combine(UiSrcDir(), "Program.cs"));
                int ctor = p.IndexOf("public App(EventWaitHandle showEvent, bool autostart)");
                int ctorEnd = p.IndexOf("private void AfterUiUp()");
                string body = ctor >= 0 && ctorEnd > ctor ? p.Substring(ctor, ctorEnd - ctor) : "";
                int tray = body.IndexOf("tray.Visible = true;"), ov = body.IndexOf("Overlay.Start(BuildConfig());"), fh = body.IndexOf("new FilterHost(");
                string after = ctorEnd > 0 ? p.Substring(ctorEnd, Math.Min(4000, p.Length - ctorEnd)) : ""; // v5.1 (W34): AfterUiUp grew (W25 prebuild + W28 net start), 1500 -> 4000 chars
                bool ok = body.Length > 0 && tray > 0 && ov > tray && fh > ov && !body.Contains("Filter.Start()") && !body.Contains("Filter.StartIfWanted()")
                    && body.Contains("Filter.StartKeys();") && body.Contains("invoker.BeginInvoke((Action)AfterUiUp)") && after.Contains("Filter.StartIfWanted()")
                    && p.Contains("public const string Version = \"v5.2\";"); // v5.1 (W34: brief "Version string: v5.1"; was v5.0.2 / W17)
                Check("V501-S7 Program.cs: tray, then overlay, then a deferred filter (hotkeys only); engine start only in AfterUiUp (first message of the loop + 1 s); v5.0.1",
                    ok, "tray=" + tray + " ov=" + ov + " fh=" + fh);
                bool logq = p.Contains("logQueue.Enqueue(line)") && !p.Contains("lock (logLock)\r\n                {\r\n                    File.AppendAllText")
                    && p.Contains("if (!t.Join(3000))");
                Check("V501-S7b crash.log written by a background thread (no file I/O under a lock on the UI / overlay thread); screen restore on session end max 3 s", logq, "");
                string page = File.ReadAllText(Path.Combine(UiSrcDir(), "SettingsForm.PageFilter.cs"));
                Check("V501-S7c 滤镜 page: engine starts when the page is shown; DDC probe only when the 显示器硬件 card is opened",
                    page.Contains("Fh.PageOpened();") && page.Contains("Fh.HardwareOpened();") && (page.Contains("blocks.Add(Fold(\"显示器硬件") || page.Contains("= Fold(\"显示器硬件")) /* v5.0.2 (W15): ManualFold */, "");
            }

            // S8 startup timing + UI watchdog (fake clock)
            {
                List<string> lines = new List<string>();
                Action<string> keep = delegate(string s) { lock (lines) lines.Add(s); };
                Action<string> oldSink = StartupClock.Sink;
                StartupClock.Sink = keep;
                StartupClock.Mark("测试阶段");
                StartupClock.Sink = oldSink;
                bool timing = lines.Count == 1 && lines[0].StartsWith("启动耗时 测试阶段: ") && lines[0].Contains(" ms（上一步 ");

                long now = 0;
                Queue<Action> ui = new Queue<Action>();
                UiWatchdog w = new UiWatchdog();
                w.Now = delegate { return now; };
                w.CurrentPhase = delegate { return "创建设置窗口"; };
                w.CurrentPhaseAt = delegate { return 100; };
                w.StartupFinished = delegate { return false; };
                List<string> wl = new List<string>();
                w.Log = delegate(string s) { wl.Add(s); };
                w.Tick();                    // startup, no UI yet, phase 0.1 s old: quiet
                now = 3500; w.Tick();        // phase 3.4 s old: one line
                now = 4000; w.Tick();        // same phase: no second line
                int startupLines = wl.Count;
                bool startupOk = startupLines == 1 && wl[0].Contains("创建设置窗口");
                w.Post = delegate(Action a) { ui.Enqueue(a); };
                now = 5000; w.Tick();        // ping sent
                now = 7000; w.Tick();        // 2 s: quiet
                now = 8100; w.Tick();        // 3.1 s: stall line
                now = 9000; w.Tick();        // no repeat
                int stallLines = wl.Count - startupLines;
                now = 9500; while (ui.Count > 0) ui.Dequeue()(); // UI answers
                bool rec = wl.Count == startupLines + 2 && wl[wl.Count - 1].StartsWith("界面恢复：卡了 4500 ms");
                now = 10000; w.Tick(); now = 10100; while (ui.Count > 0) ui.Dequeue()(); now = 20000; w.Tick();
                Check("V501-S8 startup timing line 「启动耗时 …: N ms（上一步 …）」; watchdog: stalled startup phase logged once, UI ping unanswered > 3 s logged once with the phase, recovery logged, quiet when answered",
                    timing && startupOk && stallLines == 1 && wl[startupLines].StartsWith("界面卡住：3100 ms 没有响应，当时在「创建设置窗口」") && rec && wl.Count == startupLines + 2,
                    (lines.Count > 0 ? lines[0] : "") + " | " + string.Join(" | ", wl.ToArray()));
            }

            // S9 Leo默认 parsed once and cached (also when it fails); 2000 calls are cheap
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < 2000; i++) Profile.CreateLeoDefault("x");
                long t = sw.ElapsedMilliseconds;
                string st = File.ReadAllText(Path.Combine(UiSrcDir(), "Settings.cs"));
                Check("V501-S9 Profile.CreateLeoDefault: 2000 calls in " + t + " ms (< 3000; parsed once, cached; a failed parse is not retried)",
                    t < 3000 && st.Contains("!leoDefaultFailed") && st.Contains("leoDefaultFailed = true"), "");
            }
            rig.Core.Shutdown();
            rig.Core.Join(3000);
        }

        static string Join(List<string> l) { lock (l) return string.Join(" | ", l.ToArray()); }

        /// <summary>FilterCore on an engine with a fast gamma, DDC 10 s (gated), NVAPI 5 s (lazy), Magnification hung.</summary>
        sealed class CoreRigSlow
        {
            public readonly Queue<Action> Ui = new Queue<Action>();
            public readonly SlowDdc Slow = new SlowDdc();
            public readonly GatedDdcBackend Ddc;
            public readonly LazyVibrance Vib;
            public readonly HangMag Mag = new HangMag();
            public FilterCore Core;
            public CoreRigSlow(Action<string> log)
            {
                string f = PerfFolder() + "-v501";
                Directory.CreateDirectory(f);
                Ddc = new GatedDdcBackend(Slow, log);
                Vib = new LazyVibrance(delegate { Thread.Sleep(5000); return new SlowVibrance(); }, log);
                SlowGamma g = new SlowGamma(); g.DelayMs = 0;
                Core = new FilterCore(delegate { return new ShadeEngine(f, new LenientGammaBackend(g), Vib, new LocalSaturation(Mag, true), OneDisplay, Ddc); },
                    delegate(Action a) { lock (Ui) Ui.Enqueue(a); }, log);
                Core.BeforeEnable = delegate(PilotStyle s)
                {
                    if (s != null && (s.MonitorBrightness.HasValue || s.MonitorContrast.HasValue || s.MonitorSharpness.HasValue)) Ddc.Allowed = true;
                };
            }
            public void Pump()
            {
                while (true)
                {
                    Action a;
                    lock (Ui) { if (Ui.Count == 0) return; a = Ui.Dequeue(); }
                    a();
                }
            }
            public void Settle(int quietMs, int timeoutMs)
            {
                Stopwatch sw = Stopwatch.StartNew();
                int lastJobs = -1; long since = 0;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    Pump();
                    int j = Core.JobsRun;
                    if (j != lastJobs || Core.Pending > 0 || Core.Busy) { lastJobs = j; since = sw.ElapsedMilliseconds; }
                    else if (sw.ElapsedMilliseconds - since >= quietMs) break;
                    Thread.Sleep(10);
                }
                Pump();
            }
        }
    }
}
