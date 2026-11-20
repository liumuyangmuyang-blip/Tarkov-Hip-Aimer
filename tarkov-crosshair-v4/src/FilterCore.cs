using System;
using System.Collections.Generic;
using System.Threading;
using TarkovShade;

// v4.3 (W2, Leo's alt+tab freeze, V43-PERF.md): every ShadeEngine call runs on ONE background worker thread, never on the
// UI thread. Gamma writes are read back, driver vibrance and DDC/CI writes take 0.3-0.6 s each, and Windows fires
// DisplaySettingsChanged on alt+tab out of a fullscreen game -- in v4.2 all of that ran on the UI thread ('Not responding').
// Rules here:
//   - Entry points (called on the UI thread) only queue work and read the last snapshot; they never wait for the worker.
//   - Repeated work is coalesced by key: slider edits (latest wins), display / focus events (debounced 300 ms, skipped when
//     the screen layout did not change or the filter is off), settings save (800 ms).
//   - After each job the worker takes a snapshot (all reads under the engine's own lock, no device I/O) and posts it to the
//     UI thread, which then raises Changed.
// Pure (no WinForms, no P/Invoke): unit-tested with a slow fake gamma backend (tests-v4/TestsV43Perf.cs).

namespace TarkovCrosshair
{
    /// <summary>What the 滤镜 page / tray show; immutable, made on the worker thread.</summary>
    internal sealed class FilterSnapshot
    {
        public bool Started, IsOn;
        public string PresetName = "";
        public List<PilotStyle> Presets = new List<PilotStyle>();
        public PilotStyle Style;
        public MonitorHardware Hardware = new MonitorHardware();
        public int ContrastNow;
    }

    internal sealed class FilterCore
    {
        private sealed class Job
        {
            public string Key;          // null = never coalesced
            public Action Run;
            public DateTime Due;
        }

        private readonly Func<ShadeEngine> create;
        private readonly Action<Action> post;
        private readonly Action<string> log;
        private readonly object qsync = new object();
        private readonly List<Job> queue = new List<Job>();
        private readonly Thread worker;
        private bool closing;
        private ShadeEngine engine;           // worker thread only (StopNow: any thread, crash path)
        private volatile FilterSnapshot snap = new FilterSnapshot();
        private string lastScreens;
        private DateTime lastRefresh = DateTime.MinValue;
        /// <summary>Display / focus events re-apply the curve at most once per this many ms (a refused write is never hammered).</summary>
        public int RefreshMinGapMs = 5000;
        private int jobsRun;
        private int wakeups;
        /// <summary>v5.0.2: times the worker thread woke up (job or timed wait); idle with nothing queued = 0 per second (tests).</summary>
        public int Wakeups { get { lock (qsync) return wakeups; } }
        /// <summary>v5.0.2: on the worker after any job that left the filter off (FilterHost: the gamma backend forgets its last write).</summary>
        public Action WhenOff;

        /// <summary>UI-thread only: the hold-to-see-original state the user asked for (the worker follows it).</summary>
        private bool holding;
        /// <summary>UI-thread only: working values the page shows until the next snapshot (slider edits are instant).</summary>
        private PilotStyle localStyle;

        /// <summary>Raised on the UI thread (through post) after a job finished and its snapshot is in.</summary>
        public event Action Changed;
        /// <summary>Raised on the UI thread with a Chinese problem line ("" = cleared).</summary>
        public event Action<string> ProblemChanged;

        public string Problem = "";
        /// <summary>HDR check per GDI device name (FilterHost: HdrProbe.IsHdrOn); null = never checked. Called on the worker.</summary>
        public Func<string, bool> HdrOn;
        public const string HdrText = "HDR 开着时 Windows 不使用伽马曲线，滤镜看不出变化。请在 Windows 设置 → 系统 → 屏幕 里关掉「使用 HDR」后再开滤镜。";
        public int DebounceMs = 300, SaveDelayMs = 800;

        /// <param name="create">makes the engine (called on the worker)</param>
        /// <param name="post">runs an action on the UI thread later (Control.BeginInvoke); never waits</param>
        public FilterCore(Func<ShadeEngine> create, Action<Action> post, Action<string> log)
        {
            this.create = create;
            this.post = post ?? delegate(Action a) { a(); };
            this.log = log ?? delegate(string s) { };
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "FilterWorker";
            worker.Priority = ThreadPriority.BelowNormal; // v5.0.2 (Leo: performance): never competes with the game or the overlay
            worker.Start();
        }

        // ------------------------------------------------------------ snapshot (UI thread reads)

        public FilterSnapshot Snapshot { get { return snap; } }
        public bool Started { get { return snap.Started; } }
        public bool IsOn { get { return snap.IsOn; } }
        public bool Holding { get { return holding; } }
        public string PresetName { get { return snap.PresetName; } }
        public int JobsRun { get { lock (qsync) return jobsRun; } }

        private volatile bool busy;
        /// <summary>v5.0.1: a job is running on the worker right now (tests).</summary>
        public bool Busy { get { return busy; } }

        /// <summary>Jobs waiting (not counting the one running).</summary>
        public int Pending { get { lock (qsync) return queue.Count; } }

        public PilotStyle Style
        {
            get
            {
                PilotStyle s = localStyle ?? snap.Style;
                return s != null ? s.Clone() : PilotBuiltIns.General();
            }
        }

        public List<string> PresetNames()
        {
            List<string> n = new List<string>();
            foreach (PilotStyle p in snap.Presets) n.Add(p.Name);
            if (n.Count == 0) foreach (PilotStyle p in PilotBuiltIns.Create()) n.Add(p.Name);
            return n;
        }

        public PilotStyle Preset(string name)
        {
            foreach (PilotStyle p in snap.Presets) if (p.Name == name) return p;
            return PilotBuiltIns.Find(name);
        }

        // ------------------------------------------------------------ entry points (UI thread; queue only)

        public void Start(bool turnOn)
        {
            Post(null, delegate
            {
                if (engine != null) { if (turnOn && Ready() && !engine.IsEnabled) EnableOnWorker(); return; } // v5.0.1: started once
                try
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    engine = create(); // FilterHost: ShadeEngine on LenientGammaBackend(PilotWin32Gamma), see FilterGamma.cs
                    long made = sw.ElapsedMilliseconds;
                    string msg = engine.Start();
                    log("filter: 滤镜引擎启动 " + sw.ElapsedMilliseconds + " ms（创建 " + made + " ms，在后台线程）");
                    // ShadePilot writes once per apply (no fade); the fade's timer-thread steps were one more way to fail
                    if (engine.IsStarted && engine.SmoothTransition) engine.SmoothTransition = false;
                    if (!engine.IsStarted)
                    {
                        log("filter: engine not started: " + msg);
                        SetProblem("滤镜没有启动（另一个滤镜程序正在用这个文件夹？）");
                        return;
                    }
                    if (!string.IsNullOrEmpty(msg)) log("filter: start: " + msg);
                    if (engine.RecoveryDamaged) SetProblem(DamagedText);
                    if (turnOn) EnableOnWorker();
                }
                catch (Exception ex)
                {
                    log("filter: start failed: " + ex);
                    engine = null;
                    SetProblem("滤镜没有启动，详细信息在 crash.log");
                }
            });
        }

        public const string DamagedText = "上次退出时记下原来屏幕颜色的文件坏了（已改名保存），程序不知道你原来的颜色。关掉滤镜或点「全部恢复原样」会回到 Windows 标准颜色。";

        /// <summary>v5.0.2 (Leo: no file work on the UI thread): App start. On the worker: the engine starts only when the filter
        /// was on at the last exit, or a crash left a recovery file. leftovers(true) runs on the worker before the start.</summary>
        public void StartIfWanted(bool wasOn, Func<bool> leftoversCheck, Action<bool> leftovers)
        {
            Post(null, delegate
            {
                bool left = false;
                try { left = leftoversCheck != null && leftoversCheck(); } catch (Exception) { }
                if (left && leftovers != null) { try { leftovers(true); } catch (Exception) { } }
                if (!wasOn && !left) return;
                Start(wasOn);
            });
        }

        public void SetOn(bool on)
        {
            Post("onoff", delegate
            {
                if (!Ready()) return;
                if (engine.IsHoldingOriginal) engine.EndHoldOriginal();
                if (on) EnableOnWorker();
                else { engine.Disable(); SetProblem(engine.RecoveryDamaged ? DamagedText : ""); }
            });
        }

        public void Toggle() { SetOn(!snap.IsOn); }

        public void SelectPreset(string name)
        {
            localStyle = null;
            Post("preset", delegate
            {
                if (!Ready()) return;
                if (!engine.ApplyPreset(name))
                {
                    log("filter: apply preset " + name + " failed: " + engine.LastError);
                    SetProblem(engine.IsEnabled && engine.LastError.IndexOf("gamma", StringComparison.OrdinalIgnoreCase) < 0
                        ? PartialProblem() : GammaProblem(engine.LastError));
                }
                else SetProblem("");
            });
        }

        public void NextPreset()
        {
            localStyle = null;
            Post("preset", delegate { if (Ready()) engine.NextPreset(); });
        }

        /// <summary>Slider edit: shown at once (local copy), applied on the worker, latest value wins; saved 800 ms later.</summary>
        public void SetStyle(PilotStyle s)
        {
            if (s == null) return;
            PilotStyle v = s.Clone();
            v.Tint = 0; v.Exposure = 0; v.StrengthMode = PilotViews.FixedStrengthMode; v.Normalize();
            localStyle = v.Clone();
            Post("style", delegate { if (Ready()) engine.SetCurrent(v, false); });
            Post("save", SaveDelayMs, delegate { if (Ready()) engine.SaveSettingsNow(); });
        }

        /// <summary>Hold-to-see-original: off while held (filter on only), back at release.</summary>
        public void Hold(bool down)
        {
            if (down == holding) return;
            if (down && !snap.IsOn) return;
            holding = down;
            Post("hold", delegate
            {
                if (!Ready()) return;
                if (down) { if (!engine.IsHoldingOriginal) engine.BeginHoldOriginal(); }
                else if (engine.IsHoldingOriginal) engine.EndHoldOriginal();
            });
            Raise();
        }

        public void RestoreAll()
        {
            holding = false;
            localStyle = null;
            Post("onoff", delegate
            {
                if (!Ready()) return;
                if (!engine.RestoreEverything()) log("filter: restore everything: " + engine.LastError);
                SetProblem(engine.RecoveryDamaged ? DamagedText : "");
            });
        }

        /// <summary>Display / focus event (alt+tab, resolution change, monitor plugged in). Debounced; skipped when the screen
        /// layout signature equals the last one handled, or the filter is off (the engine re-reads baselines on enable).</summary>
        public void DisplaysChanged(string screenSignature)
        {
            Post("display", DebounceMs, delegate
            {
                if (!Ready()) return;
                string sig = screenSignature ?? "";
                bool same = sig.Length > 0 && sig == lastScreens;
                if (same || !engine.IsEnabled) { lastScreens = sig; return; }
                double since = (DateTime.UtcNow - lastRefresh).TotalMilliseconds;
                if (since < RefreshMinGapMs)
                {
                    // too soon after the last re-apply: try once more when the gap is over (coalesced with later events)
                    string again = screenSignature;
                    Post("display", (int)(RefreshMinGapMs - since) + 50, delegate { lastScreens = null; DisplaysChangedOnWorker(again); });
                    return;
                }
                lastScreens = sig;
                lastRefresh = DateTime.UtcNow;
                if (!engine.RefreshDisplays())
                {
                    log("filter: refresh displays: " + engine.LastError);
                    SetProblem(GammaProblem(engine.LastError));
                }
            });
        }

        private void DisplaysChangedOnWorker(string sig)
        {
            if (!Ready() || !engine.IsEnabled) return;
            lastScreens = sig ?? "";
            lastRefresh = DateTime.UtcNow;
            if (!engine.RefreshDisplays())
            {
                log("filter: refresh displays: " + engine.LastError);
                SetProblem(GammaProblem(engine.LastError));
            }
        }

        public void RefreshMonitors() { Post("monitors", delegate { if (Ready()) engine.RefreshMonitors(); }); }

        /// <summary>done(saved name or null) on the UI thread.</summary>
        public void SaveAs(string name, Action<string> done)
        {
            Post(null, delegate
            {
                string r = null;
                if (Ready()) { PilotStyle p = engine.SaveCurrentAs(name); r = p == null ? null : p.Name; if (r == null) log("filter: save as: " + engine.LastError); }
                Reply(done, r);
            });
        }

        public void SaveToCurrent(Action<bool> done)
        {
            Post(null, delegate
            {
                bool ok = Ready() && engine.UpdatePreset(engine.CurrentPresetName);
                ReplyB(done, ok);
            });
        }

        public void Rename(string oldName, string newName, Action<bool> done)
        {
            Post(null, delegate { bool ok = Ready() && engine.RenamePreset(oldName, newName); ReplyB(done, ok); });
        }

        /// <summary>Delete a user preset; when it was current, 通用 becomes current.</summary>
        public void Delete(string name, Action<bool> done)
        {
            localStyle = null;
            Post(null, delegate
            {
                bool ok = false;
                if (Ready())
                {
                    bool wasCurrent = engine.CurrentPresetName == name;
                    ok = engine.DeletePreset(name);
                    if (ok && wasCurrent) engine.ApplyPreset(PilotBuiltIns.GeneralName);
                }
                ReplyB(done, ok);
            });
        }

        public void ResetBuiltIn(string name, Action<bool> done)
        {
            localStyle = null;
            Post(null, delegate
            {
                bool ok = Ready() && engine.ResetBuiltInPreset(name);
                if (ok && engine.CurrentPresetName == name) engine.ApplyPreset(name);
                ReplyB(done, ok);
            });
        }

        /// <summary>done(TSHADE2 code of the working values, or null).</summary>
        public void ExportCode(Action<string> done)
        {
            Post(null, delegate { Reply(done, Ready() ? engine.ExportCode(null) : null); });
        }

        /// <summary>done(new preset name or null, Chinese reason). The import becomes the current preset.</summary>
        public void ImportCode(string code, Action<string, string> done)
        {
            localStyle = null;
            Post(null, delegate
            {
                string name = null, why = "";
                if (!Ready()) why = "滤镜没有启动";
                else
                {
                    string err;
                    name = engine.ImportCode(code, out err);
                    if (name == null) { why = FilterKeys.ImportReason(err); log("filter: import: " + err); }
                    else engine.ApplyPreset(name);
                }
                string n = name, w = why;
                post(delegate { if (done != null) done(n, w); });
            });
        }

        // ------------------------------------------------------------ shutdown

        /// <summary>App exit (UI thread): drop pending edits, then restore the screen and release the engine on the worker.
        /// Does not wait; Program's Main calls Join after the message loop has ended.</summary>
        public void Shutdown()
        {
            lock (qsync)
            {
                if (closing) return;
                // v5.0.2 (BRUTE P1-1, seed 50152): an on / off or hold job queued just before exit no longer writes to the driver
                // first. The screen is restored FIRST; jobs that only save (preset choice, save-as, rename ...) run after it with
                // the filter off (they do not touch the screen then), then the engine is released.
                queue.RemoveAll(delegate(Job j) { return j.Key == "style" || j.Key == "display" || j.Key == "monitors" || j.Key == "hold" || j.Key == "onoff"; });
                DateTime now = DateTime.UtcNow;
                foreach (Job j in queue) j.Due = now;
                queue.Insert(0, new Job { Key = null, Due = now, Run = delegate
                {
                    ShadeEngine e = engine;
                    if (e == null) return;
                    try { e.Stop(); } catch (Exception ex) { log("filter: stop: " + ex.Message); }
                } });
                queue.Add(new Job { Key = null, Due = now, Run = delegate
                {
                    ShadeEngine e = engine;
                    engine = null;
                    if (e == null) return;
                    try { e.Stop(); } catch (Exception ex) { log("filter: stop: " + ex.Message); }
                    try { e.Dispose(); } catch (Exception ex) { log("filter: dispose: " + ex.Message); }
                } });
                closing = true;
                Monitor.PulseAll(qsync);
            }
        }

        /// <summary>Wait for the worker to finish after Shutdown (Main's finally: no UI any more). True if it finished.</summary>
        public bool Join(int ms) { return worker.Join(ms); }

        /// <summary>Crash path (any thread): put the screen back now. The engine's own lock serialises it with the worker.</summary>
        public void StopNow()
        {
            ShadeEngine e = engine;
            if (e == null) return;
            try { e.Stop(); } catch (Exception) { }
        }

        // ------------------------------------------------------------ worker

        private void Post(string key, Action run) { Post(key, 0, run); }

        private void Post(string key, int delayMs, Action run)
        {
            lock (qsync)
            {
                if (closing) return;
                DateTime due = DateTime.UtcNow.AddMilliseconds(delayMs);
                if (key != null)
                    foreach (Job j in queue)
                        if (j.Key == key) { j.Run = run; j.Due = due; Monitor.PulseAll(qsync); return; }
                queue.Add(new Job { Key = key, Run = run, Due = due });
                Monitor.PulseAll(qsync);
            }
        }

        private void Loop()
        {
            while (true)
            {
                Job job = null;
                lock (qsync)
                {
                    while (job == null)
                    {
                        if (queue.Count == 0)
                        {
                            if (closing) return;
                            Monitor.Wait(qsync); // no timeout: an idle filter (on or off) never wakes this thread
                            wakeups++;
                            continue;
                        }
                        DateTime now = DateTime.UtcNow, next = DateTime.MaxValue;
                        foreach (Job j in queue)
                        {
                            if (j.Due <= now) { job = j; break; }
                            if (j.Due < next) next = j.Due;
                        }
                        if (job != null) queue.Remove(job);
                        else { Monitor.Wait(qsync, Math.Max(1, (int)Math.Min(1000, (next - now).TotalMilliseconds))); wakeups++; }
                    }
                }
                busy = true;
                try
                {
                    job.Run();
                    Action off = WhenOff;
                    ShadeEngine e0 = engine;
                    if (off != null && e0 != null && e0.IsStarted && !e0.IsEnabled) off();
                }
                catch (Exception ex) { log("filter: worker job: " + ex); }
                finally { busy = false; }
                FilterSnapshot s = TakeSnapshot();
                bool styleJob = job.Key == "style";
                lock (qsync) jobsRun++;
                post(delegate
                {
                    snap = s;
                    if (!styleJob) localStyle = null; // a slider edit keeps showing the local copy until the next other job
                    else if (!StylePending()) localStyle = null;
                    Raise();
                });
            }
        }

        private bool StylePending()
        {
            lock (qsync) foreach (Job j in queue) if (j.Key == "style") return true;
            return false;
        }

        private bool Ready() { return engine != null && engine.IsStarted; }

        private FilterSnapshot TakeSnapshot()
        {
            FilterSnapshot s = new FilterSnapshot();
            ShadeEngine e = engine;
            if (e == null || !e.IsStarted) return s;
            try
            {
                s.Started = true;
                s.IsOn = e.IsEnabled;
                s.PresetName = e.CurrentPresetName ?? "";
                s.Presets = e.Presets ?? new List<PilotStyle>();
                s.Style = e.Current;
                s.Hardware = e.GetMonitorHardware();
                s.ContrastNow = e.MonitorContrastNeutralZero;
            }
            catch (Exception ex) { log("filter: snapshot: " + ex.Message); }
            return s;
        }

        /// <summary>v5.0.1: called on the worker with the working values just before Enable (FilterHost: lets DDC/CI through
        /// only when the preset sets monitor values).</summary>
        public Action<PilotStyle> BeforeEnable;

        private void EnableOnWorker()
        {
            Action<PilotStyle> be = BeforeEnable;
            if (be != null) { try { be(engine.Current); } catch (Exception ex) { log("filter: before enable: " + ex.Message); } }
            bool hdr = false;
            if (HdrOn != null)
                foreach (string d in engine.GetTargetDevices())
                {
                    try { if (HdrOn(d)) hdr = true; } catch (Exception) { }
                }
            if (engine.Enable()) { SetProblem(hdr ? HdrText : ""); return; }
            if (hdr) { log("filter: enable failed (HDR on): " + engine.LastError); SetProblem(HdrText); return; }
            log("filter: enable failed: " + engine.LastError);
            SetProblem(engine.IsEnabled ? PartialProblem() : GammaProblem(engine.LastError));
        }

        /// <summary>
        /// The gamma curve was not applied: plain Chinese + the exact Windows reason (Win32 number / read-back) from the engine.
        /// Pure (tested).
        /// </summary>
        public static string GammaProblem(string engineError)
        {
            string e = engineError ?? "";
            string detail = "";
            int w = e.IndexOf("Win32 ", StringComparison.Ordinal);
            if (w >= 0)
            {
                int end = w + 6;
                while (end < e.Length && char.IsDigit(e[end])) end++;
                detail = "（Windows 错误码 " + e.Substring(w + 6, end - w - 6) + "）";
            }
            if (e.IndexOf("read-back differs", StringComparison.OrdinalIgnoreCase) >= 0)
                return "滤镜没有生效：Windows 收下了颜色曲线，但读回来的不一样（显卡驱动或色彩管理把它覆盖了）。请关掉 HDR 和其他调色程序（如显卡控制面板的颜色设置、夜间模式）后再试。";
            if (e.IndexOf("CreateDC", StringComparison.Ordinal) >= 0 || e.IndexOf("Cannot open display", StringComparison.Ordinal) >= 0)
                return "滤镜没有生效：打不开显示器" + detail + "。换了显示器或刚切换全屏时会这样，稍等几秒再开。";
            if (e.IndexOf("SetDeviceGammaRamp", StringComparison.Ordinal) >= 0 || e.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0)
                return "滤镜没有生效：Windows 拒绝了这个颜色曲线" + detail + "。可能是 HDR 开着、显卡正在切换全屏，或曲线超出了 Windows 允许的范围；可以先把「提亮暗部」调小再试。";
            if (e.IndexOf("Crash-recovery file not written", StringComparison.Ordinal) >= 0)
                return "滤镜没有打开：程序文件夹写不进文件，没法先记下原来的屏幕颜色（万一程序意外退出就恢复不了）。把程序放到一个可以写入的文件夹（例如桌面上的文件夹）再试。";
            if (e.IndexOf("fade", StringComparison.OrdinalIgnoreCase) >= 0)
                return "滤镜没有生效：切换时 Windows 拒绝了颜色曲线，已恢复原画面。";
            if (e.Length == 0) return "滤镜没有生效（Windows 没有给出原因）。";
            return "滤镜没有生效：" + e;
        }

        /// <summary>The filter is on but part of a preset was not applied (driver vibrance, Magnification, monitor).</summary>
        private string PartialProblem()
        {
            string e = engine != null ? engine.LastError ?? "" : "";
            if (e.IndexOf("vibrance", StringComparison.OrdinalIgnoreCase) >= 0)
                return "滤镜已开，但显卡不支持数字振动，饱和度没改（其他设置已生效）";
            if (e.IndexOf("Magnification", StringComparison.OrdinalIgnoreCase) >= 0)
                return "滤镜已开，但 Windows 放大镜饱和度用不了，饱和度没改";
            if (e.IndexOf("onitor", StringComparison.Ordinal) >= 0 || e.IndexOf("DDC", StringComparison.Ordinal) >= 0)
                return "滤镜已开，但显示器没有接受亮度 / 对比度 / 锐度的设置";
            return "滤镜已开，但有一部分设置没生效，详细信息在 crash.log";
        }

        private void SetProblem(string text)
        {
            string t = text ?? "";
            post(delegate
            {
                if (t == Problem) return;
                Problem = t;
                Action<string> h = ProblemChanged;
                if (h != null) h(t);
            });
        }

        private void Reply(Action<string> done, string v) { post(delegate { if (done != null) done(v); }); }
        private void ReplyB(Action<bool> done, bool v) { post(delegate { if (done != null) done(v); }); }

        private void Raise()
        {
            Action h = Changed;
            if (h != null) { try { h(); } catch (Exception ex) { log("filter: changed handler: " + ex); } }
        }
    }
}
