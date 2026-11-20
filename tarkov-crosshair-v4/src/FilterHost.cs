using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TarkovShade;

// v4.2 (W2) 滤镜 host, v4.3 rewritten for Leo's alt+tab freeze (V43-PERF.md): a thin WinForms wrapper around FilterCore.
// FilterCore runs EVERY ShadeEngine call on one background worker; this class only reads its snapshot, polls the filter
// hotkeys (GetAsyncKeyState, 30 ms timer), keeps filter-prefs.json and marshals results back with BeginInvoke.
// Nothing here waits for the worker on the UI thread. v5.0.1 (W13): the engine starts on demand (StartIfWanted after the UI is up,
// or the first real use: tray / hotkey / 滤镜 page), never in the App constructor. Exit: Dispose queues restore + dispose; Program.Main joins the worker
// after the message loop has ended.
// v5.1 (W24, Leo decision 10): filter idle = zero wake-ups. The filter keys are read by the overlay's render thread from its own
// per-frame key snapshot (FilterKeyFeed, FilterHotkeys.cs); only a press / release reaches this class (BeginInvoke). The old 30 ms
// timer is only a fallback until the first overlay frame is seen, then it stops for good.

namespace TarkovCrosshair
{
    internal sealed class FilterHost : IDisposable
    {
        private readonly Control invoker;
        private readonly string folder;
        private readonly FilterCore core;
        private bool disposed, lastOn, lastOnKnown;
        private readonly FilterKeyPoller poller = new FilterKeyPoller();
        private readonly Timer keyTimer = new Timer();
        private readonly FilterKeyFeed feed = new FilterKeyFeed();
        private long feedFramesAtStart = -1;
        /// <summary>v5.1: the overlay feeds the filter keys (event driven); the fallback timer never runs again.</summary>
        private bool feedLive;
        private readonly int[] polledAt = new int[FilterKeys.ActionCount]; // fallback-poll press times (no double press at hand-over)

        public FilterPrefs Prefs;
        /// <summary>Raised on the UI thread after any state change (on / off, preset, values, problem).</summary>
        public event Action Changed;
        /// <summary>Balloon request (Chinese text, warning) for the App's tray.</summary>
        public Action<string> Notify;
        /// <summary>Hotkey gate: true when the filter keys may act now (App: game in front, if 只在游戏里 is on).</summary>
        public Func<bool> KeysAllowed;
        /// <summary>Crosshair key bindings, for the clash check on the 滤镜 page.</summary>
        public Func<KeyBindings> CrosshairKeys;

        /// <summary>v5.0.1: monitor DDC/CI stays closed until the 显示器硬件 card is opened (FilterGuards.cs).</summary>
        private readonly GatedDdcBackend ddc;
        /// <summary>v5.0.1: the engine start was queued (the engine starts on demand, never in the App constructor).</summary>
        private volatile bool startRequested;
        private volatile LenientGammaBackend gammaBackend;

        /// <param name="invoker">UI-thread control for BeginInvoke; null = a stand-in host that never starts (render harness)</param>
        public FilterHost(Control invoker, string folder)
        {
            this.invoker = invoker;
            this.folder = folder;
            Prefs = FilterPrefs.Load(folder);
            Action<string> log = delegate(string s) { Program.Log(s); };
            ddc = new GatedDdcBackend(new Dxva2DdcBackend(), log);
            core = new FilterCore(delegate
            {
                // v4.3: ShadePilot-style gamma writes + read-back rule (FilterGamma.cs); the rest = ShadeEngine.CreateDefault.
                // v5.0.1: driver vibrance detected on first use only, DDC/CI gated + time-limited (FilterGuards.cs).
                gammaBackend = new LenientGammaBackend(new PilotWin32Gamma());
                return new ShadeEngine(folder, gammaBackend, new LazyVibrance(VibranceController.Detect, log),
                    new LocalSaturation(), Win32Displays.Enumerate, ddc);
            }, Marshal, log);
            // v5.0.2 (BRUTE P3-2): once the filter is off the screen shows the user's own curve; a later change by another program
            // is read as it is (no substitution of our last write)
            core.WhenOff = delegate { LenientGammaBackend g = gammaBackend; if (g != null) g.Forget(null); };
            core.HdrOn = HdrProbe.IsHdrOn;
            core.BeforeEnable = delegate(PilotStyle s)
            {
                if (s != null && (s.MonitorBrightness.HasValue || s.MonitorContrast.HasValue || s.MonitorSharpness.HasValue)) ddc.Allowed = true;
            };
            core.Changed += OnCoreChanged;
            core.ProblemChanged += delegate(string p)
            {
                Raise();
                Action<string> n = Notify;
                if (n != null && p.Length > 0) { try { n(p); } catch (Exception) { } }
            };
            keyTimer.Interval = 30;
            keyTimer.Tick += delegate { PollKeys(); };
            poller.Pressed += delegate(int a) { polledAt[a] = Environment.TickCount; OnKey(a); };
            poller.HoldChanged += delegate(bool d) { Hold(d); };
            feed.Edge = delegate(int a, bool d) { Marshal(delegate { OnEdge(a, d); }); }; // render thread -> UI thread, edges only
        }

        private void Marshal(Action a)
        {
            try
            {
                if (invoker == null || invoker.IsDisposed || !invoker.IsHandleCreated) return;
                invoker.BeginInvoke(a);
            }
            catch (Exception) { }
        }

        private void OnCoreChanged()
        {
            if (disposed) return;
            bool on = core.IsOn;
            if (core.Started && (!lastOnKnown || on != lastOn) && !core.Holding)
            {
                lastOn = on; lastOnKnown = true;
                if (Prefs.WasOn != on) { Prefs.WasOn = on; SavePrefs(); }
            }
            Raise();
        }

        // ------------------------------------------------------------ lifecycle

        /// <summary>v5.0.1: filter hotkeys only (30 ms GetAsyncKeyState poll, no engine). App: right after the tray exists.</summary>
        public void StartKeys()
        {
            if (invoker == null || disposed) return;
            keysStarted = true;
            feed.Configure(Prefs);
            feedFramesAtStart = feed.Frames;
            FilterKeyFeed.Current = feed;
            UpdateKeyTimer();
        }

        /// <summary>v5.1: true once the overlay's render thread has fed at least one frame since the keys started.</summary>
        internal bool FeedLive { get { return feedLive; } }

        private bool keysStarted;
        /// <summary>v5.0.2 (Leo: no timers when nothing can happen): the 30 ms key poll runs only while a filter key is assigned.</summary>
        private void UpdateKeyTimer()
        {
            if (invoker == null || disposed || !keysStarted) return;
            bool any = false;
            for (int a = 0; a < FilterKeys.ActionCount; a++) if (Prefs.Vk[a] > 0) any = true;
            if (feedLive) any = false; // v5.1: event driven from the overlay feed, no timer
            if (any && !keyTimer.Enabled) keyTimer.Start();
            else if (!any && keyTimer.Enabled) { keyTimer.Stop(); if (core.Holding) core.Hold(false); poller.Reset(); }
        }

        /// <summary>v5.0.1, App: after the UI is up. Starts the engine (background worker) only when the filter was on at the
        /// last exit, or a crash left a filter / monitor change to undo. Otherwise the engine waits for the first real use.
        /// True when the start was queued.</summary>
        public bool StartIfWanted()
        {
            if (invoker == null || disposed) return false;
            // v5.0.2 (Leo: no file work on the UI thread): the recovery-file check runs on the filter worker
            string f = folder;
            Func<bool> check = delegate
            {
                string p = System.IO.Path.Combine(f, RecoveryStore.FileName);
                return System.IO.File.Exists(p) || System.IO.File.Exists(p + ".bak") || System.IO.File.Exists(p + ".tmp");
            };
            Action<bool> leftovers = delegate { ddc.Allowed = true; startRequested = true; }; // saved monitor values may need putting back
            if (startRequested) return false;
            if (Prefs.WasOn) startRequested = true;
            core.StartIfWanted(Prefs.WasOn, check, leftovers);
            return Prefs.WasOn;
        }

        /// <summary>v5.0.1: queue the engine start once (later calls: turnOn re-queues an enable only). UI thread, never waits.</summary>
        public void EnsureStarted(bool turnOn)
        {
            if (invoker == null || disposed) return;
            if (!startRequested)
            {
                startRequested = true;
                core.Start(turnOn);
                return;
            }
            if (turnOn) core.SetOn(true);
        }

        /// <summary>The 滤镜 page was opened: the engine starts (in the background) so the page shows presets and state.</summary>
        public void PageOpened() { EnsureStarted(false); }

        /// <summary>The 显示器硬件 card was opened: DDC/CI may now be probed (on the worker, every call time-limited).</summary>
        public void HardwareOpened()
        {
            if (invoker == null || disposed) return;
            bool first = !ddc.Allowed;
            ddc.Allowed = true;
            if (!startRequested) { EnsureStarted(false); return; } // the engine's own start probes the monitors
            if (first) core.RefreshMonitors();
        }

        public bool HardwareProbed { get { return ddc.Allowed; } }

        /// <summary>v5.0.1: true also while the engine is deferred (not needed yet): the filter is simply off then.</summary>
        public bool Started { get { return core.Started || (!startRequested && invoker != null); } }

        /// <summary>Crash / sign-out (any thread): put the screen back now.</summary>
        public void StopNow() { core.StopNow(); }

        /// <summary>App exit (UI thread): queue restore + dispose on the worker; does not wait (Join does, from Main).</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (FilterKeyFeed.Current == feed) FilterKeyFeed.Current = null;
            keyTimer.Stop();
            keyTimer.Dispose();
            SavePrefs();
            core.Shutdown();
        }

        /// <summary>Main's finally, after the message loop: wait for the restore to finish (max ms).</summary>
        public bool Join(int ms) { return core.Join(ms); }

        public void SavePrefs()
        {
            string err = FilterPrefs.Save(Prefs, folder);
            if (err.Length > 0) Program.Log("filter: save prefs: " + err);
        }

        /// <summary>Display change / alt+tab: debounced on the worker, skipped when the layout is unchanged.</summary>
        public void DisplaysChanged()
        {
            if (!startRequested) return; // v5.0.1: engine not running, nothing to re-apply
            string sig = "";
            try
            {
                foreach (Screen s in Screen.AllScreens) sig += s.DeviceName + "=" + s.Bounds.ToString() + ";";
            }
            catch (Exception) { sig = ""; }
            core.DisplaysChanged(sig);
        }

        // ------------------------------------------------------------ state (snapshot reads only)

        public bool IsOn { get { return core.IsOn; } }
        public bool Holding { get { return core.Holding; } }
        public string PresetName { get { return core.PresetName; } }
        public string Problem { get { return core.Problem; } }
        public List<string> PresetNames() { return core.PresetNames(); }
        public bool PresetUntested(string name) { PilotStyle p = core.Preset(name); return p != null && p.Untested; }
        public bool PresetBuiltIn(string name) { PilotStyle p = core.Preset(name); return p != null && p.BuiltIn; }
        public PilotStyle Style { get { return core.Style; } }
        /// <summary>The saved values of a preset (from the last snapshot), not the working values. null if unknown.</summary>
        public PilotStyle SavedPreset(string name) { PilotStyle p = core.Preset(name); return p == null ? null : p.Clone(); }
        public MonitorHardware Hardware() { return core.Snapshot.Hardware ?? new MonitorHardware(); }
        public int HardwareContrastNow() { return core.Snapshot.ContrastNow; }

        /// <summary>Dashboard / tray line: 「滤镜：开 · 通用」.</summary>
        public string StatusLine()
        {
            if (!Started) return "滤镜：未启动";
            return "滤镜：" + (IsOn ? "开" : "关") + " · " + PresetName;
        }

        // ------------------------------------------------------------ actions (queue only, results come back on the UI thread)

        // v5.0.1: every action first makes sure the engine start is queued (it runs before the action on the same worker).
        public void Toggle() { if (!startRequested) { EnsureStarted(true); return; } core.Toggle(); }
        public void SetOn(bool on) { if (!startRequested) { if (on) EnsureStarted(true); return; } core.SetOn(on); }
        public void SelectPreset(string name) { EnsureStarted(false); core.SelectPreset(name); }
        public void NextPreset() { EnsureStarted(false); core.NextPreset(); }
        public void SetStyle(PilotStyle s) { EnsureStarted(false); core.SetStyle(s); }
        public void Hold(bool down) { if (!startRequested) return; core.Hold(down); }
        public void RestoreAll() { EnsureStarted(false); core.RestoreAll(); }
        public void RefreshMonitors()
        {
            if (invoker == null || disposed) return;
            ddc.Allowed = true;
            ddc.ResetTimeoutNote();
            if (!startRequested) { EnsureStarted(false); return; }
            core.RefreshMonitors();
        }
        public void SaveAs(string name, Action<string> done) { EnsureStarted(false); core.SaveAs(name, done); }
        public void SaveToCurrent(Action<bool> done) { EnsureStarted(false); core.SaveToCurrent(done); }
        public void Rename(string oldName, string newName, Action<bool> done) { EnsureStarted(false); core.Rename(oldName, newName, done); }
        public void Delete(string name, Action<bool> done) { EnsureStarted(false); core.Delete(name, done); }
        public void ResetBuiltIn(string name, Action<bool> done) { EnsureStarted(false); core.ResetBuiltIn(name, done); }
        public void ExportCode(Action<string> done) { EnsureStarted(false); core.ExportCode(done); }
        public void ImportCode(string code, Action<string, string> done) { EnsureStarted(false); core.ImportCode(code, done); }

        // ------------------------------------------------------------ hotkey settings (filter-prefs.json, UI thread, no engine)

        /// <summary>Hotkey change from the page. Returns "" or a Chinese reason (nothing changed then).</summary>
        public string SetKey(int action, int vk, int mods)
        {
            if (action < 0 || action >= FilterKeys.ActionCount) return "";
            mods &= KeyBindings.ModMask;
            if (vk <= 0) { vk = 0; mods = 0; }
            string why = FilterKeys.Validate(vk, mods);
            if (why.Length > 0) return why;
            int other = FilterKeys.FilterClash(Prefs, action, vk, mods);
            if (other >= 0) return "「" + FilterKeys.ComboText(vk, mods) + "」已经是滤镜的「" + FilterKeys.ActionNames[other] + "」";
            KeyBindings k = CrosshairKeys != null ? CrosshairKeys() : null;
            int x = FilterKeys.CrosshairClash(k, vk, mods);
            if (x >= 0) return "「" + FilterKeys.ComboText(vk, mods) + "」会同时按到准星的「" + KeyBindings.ActionNames[x] + "」（" + k.ComboOf(x) + "）";
            Prefs.Vk[action] = vk;
            Prefs.Mods[action] = mods;
            poller.Reset();
            feed.Configure(Prefs);
            SavePrefs();
            UpdateKeyTimer();
            Raise();
            return "";
        }

        public void ResetKeys()
        {
            Prefs.ResetKeys();
            Prefs.Normalize();
            poller.Reset();
            feed.Configure(Prefs);
            SavePrefs();
            UpdateKeyTimer();
            Raise();
        }

        /// <summary>Filter keys that clash with the crosshair's current keys (the crosshair keys may change later): Chinese lines.</summary>
        public List<string> KeyClashes()
        {
            List<string> r = new List<string>();
            KeyBindings k = CrosshairKeys != null ? CrosshairKeys() : null;
            for (int a = 0; a < FilterKeys.ActionCount; a++)
            {
                int x = FilterKeys.CrosshairClash(k, Prefs.Vk[a], Prefs.Mods[a]);
                if (x >= 0) r.Add("「" + FilterKeys.ActionNames[a] + "」" + FilterKeys.ComboText(Prefs.Vk[a], Prefs.Mods[a]) + " 和准星的「" + KeyBindings.ActionNames[x] + "」冲突");
            }
            return r;
        }

        // ------------------------------------------------------------ hotkeys

        private void PollKeys()
        {
            if (disposed) return;
            if (!feedLive && feedFramesAtStart >= 0 && feed.Frames > feedFramesAtStart) GoEventDriven();
            if (feedLive) return;
            bool allowed = true;
            try { if (KeysAllowed != null) allowed = KeysAllowed(); } catch (Exception) { allowed = true; }
            if (!allowed)
            {
                if (core.Holding) core.Hold(false);
                poller.Reset();
                return;
            }
            try { poller.Poll(Prefs, Native.IsDown); }
            catch (Exception ex) { Program.Log("filter: keys: " + ex.Message); }
        }

        /// <summary>v5.1: the overlay feed works; stop the fallback timer for good.</summary>
        private void GoEventDriven()
        {
            feedLive = true;
            keyTimer.Stop();
            if (core.Holding && !feedHoldDown) core.Hold(false); // a hold the fallback started ends with the feed's release, or now
            poller.Reset();
        }

        private bool feedHoldDown;

        /// <summary>v5.1: a filter key edge from the overlay feed (UI thread).</summary>
        private void OnEdge(int action, bool down)
        {
            if (disposed) return;
            if (!feedLive) GoEventDriven();
            if (action == FilterKeys.ActionHold) feedHoldDown = down;
            bool allowed = true;
            try { if (KeysAllowed != null) allowed = KeysAllowed(); } catch (Exception) { allowed = true; }
            if (action == FilterKeys.ActionHold)
            {
                if (!down) { if (core.Holding) Hold(false); return; } // a release always ends the hold
                if (allowed) Hold(true);
                return;
            }
            if (!down || !allowed) return;
            if (unchecked(Environment.TickCount - polledAt[action]) < 200) return; // the fallback poll already did this press
            OnKey(action);
        }

        private void OnKey(int action)
        {
            switch (action)
            {
                case FilterKeys.ActionToggle: Toggle(); break;
                case FilterKeys.ActionNext: NextPreset(); break;
            }
        }

        private void Raise()
        {
            Action h = Changed;
            if (h != null) { try { h(); } catch (Exception ex) { Program.Log("filter: changed handler: " + ex); } }
        }
    }
}
