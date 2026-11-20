using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TarkovCrosshair
{
    /// <summary>Immutable snapshot published by the UI thread; the render thread reads it once per frame.</summary>
    public sealed class RenderConfig
    {
        public readonly GlobalSettings G;     // global fields only (Profiles empty), Keys cloned
        public readonly Profile P;            // deep clone of the active profile
        public readonly double CenterX;       // monitor centre, physical px (continuous)
        public readonly double CenterY;
        public readonly PreviewMode Preview;
        public readonly double PreviewSpeed;
        public readonly int Version;
        public readonly int ToggleAck;        // last overlay toggle sequence the UI has applied

        public RenderConfig(GlobalSettings g, Profile p, double cx, double cy, PreviewMode pm, double speed, int version, int toggleAck)
        {
            G = g; P = p; CenterX = cx; CenterY = cy; Preview = pm; PreviewSpeed = speed; Version = version; ToggleAck = toggleAck;
        }
    }

    /// <summary>
    /// Our own click-through layered popup. Lives on the render thread. v5.0.1 (W14): a raw Win32 window (RegisterClassEx +
    /// CreateWindowEx + a static window proc) instead of a WinForms NativeWindow, so nothing of WinForms runs on the render
    /// thread (NativeWindow's callback can route an exception to Application.ThreadException, i.e. a dialog on this thread).
    /// </summary>
    internal static class OverlayWindow
    {
        private const string ClassName = "TarkovHipAimerOverlayV501";
        private static readonly Native.WndProc proc = WndProc; // kept alive for the whole process (the class keeps its pointer)
        private static bool registered;

        private static IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == Native.WM_NCHITTEST) return new IntPtr(Native.HTTRANSPARENT);
            if (msg == Native.WM_MOUSEACTIVATE) return new IntPtr(Native.MA_NOACTIVATE);
            return Native.DefWindowProcW(h, msg, w, l);
        }

        /// <summary>Creates the 1x1 layered topmost popup and shows it once (no activation). Throws when Windows refuses.</summary>
        public static IntPtr CreateLayered(string caption)
        {
            int ex = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return Create(ex, caption, true);
        }

        private static IntPtr Create(int ex, string caption, bool show)
        {
            IntPtr inst = Native.GetModuleHandleW(null);
            if (!registered)
            {
                Native.WNDCLASSEX wc = new Native.WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX));
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc);
                wc.hInstance = inst;
                wc.lpszClassName = ClassName;
                if (Native.RegisterClassExW(ref wc) == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != Native.ERROR_CLASS_ALREADY_EXISTS) throw new InvalidOperationException("RegisterClassEx failed, Win32 error " + err);
                }
                registered = true;
            }
            IntPtr h = Native.CreateWindowExW(ex, ClassName, caption, Native.WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (h == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed, Win32 error " + Marshal.GetLastWin32Error());
            if (show) Native.ShowWindow(h, Native.SW_SHOWNOACTIVATE); // v5.0.2: 开镜 never shows / hides it (all-zero frame); off / game away does
            return h;
        }

        /// <summary>v5.1 (W33): the window for the DirectComposition mode (GlobalSettings.OverlayDCompMode): no redirection
        /// bitmap (its content is a composition swap chain), still layered + transparent so clicks go through to the game
        /// (constant alpha 255, the pixels carry the alpha). Same class, same click-through window proc. Throws when refused.</summary>
        public static IntPtr CreateDComp(string caption)
        {
            int ex = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE
                | Native.WS_EX_NOREDIRECTIONBITMAP;
            IntPtr h = Create(ex, caption, true); // shown like the layered one (nothing is visible before the first present)
            if (!Native.SetLayeredWindowAttributes(h, 0, 255, Native.LWA_ALPHA))
            {
                int err = Marshal.GetLastWin32Error();
                Destroy(h);
                throw new InvalidOperationException("SetLayeredWindowAttributes failed, Win32 error " + err);
            }
            return h;
        }

        public static void Destroy(IntPtr h)
        {
            if (h != IntPtr.Zero) { try { Native.DestroyWindow(h); } catch (Exception) { } }
        }
    }

    /// <summary>A 32bpp top-down DIB selected into a memory DC (for UpdateLayeredWindow).</summary>
    internal sealed class Dib : IDisposable
    {
        public IntPtr MemDC, HBmp, OldBmp, Bits;
        public int W, H;

        public Dib(int w, int h)
        {
            W = w; H = h;
            MemDC = Native.CreateCompatibleDC(IntPtr.Zero);
            if (MemDC == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleDC failed");
            Native.BITMAPINFO bmi = new Native.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = Native.BI_RGB;
            IntPtr b;
            HBmp = Native.CreateDIBSection(MemDC, ref bmi, Native.DIB_RGB_COLORS, out b, IntPtr.Zero, 0);
            if (HBmp == IntPtr.Zero || b == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Native.DeleteDC(MemDC); MemDC = IntPtr.Zero;
                throw new InvalidOperationException("CreateDIBSection failed, Win32 error " + err);
            }
            Bits = b;
            OldBmp = Native.SelectObject(MemDC, HBmp);
        }

        public void Dispose()
        {
            if (MemDC != IntPtr.Zero && OldBmp != IntPtr.Zero) Native.SelectObject(MemDC, OldBmp);
            if (HBmp != IntPtr.Zero) Native.DeleteObject(HBmp);
            if (MemDC != IntPtr.Zero) Native.DeleteDC(MemDC);
            MemDC = HBmp = OldBmp = Bits = IntPtr.Zero;
        }

        /// <summary>v4.3: false (no exception) when Windows refuses, e.g. during a display-mode switch; retried next frame.</summary>
        public bool Present(IntPtr hwnd, int x, int y, byte sca)
        {
            Native.POINT dst = new Native.POINT(x, y);
            Native.SIZE size = new Native.SIZE(W, H);
            Native.POINT src = new Native.POINT(0, 0);
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
            blend.BlendOp = Native.AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = sca;
            blend.AlphaFormat = Native.AC_SRC_ALPHA;
            return Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, MemDC, ref src, 0, ref blend, Native.ULW_ALPHA);
        }

        /// <summary>v5.0.2 (W16, AUDIT F7): moves the window to (x, y) keeping the bitmap already on screen (no surface
        /// upload: hdcSrc / psize / pptSrc NULL). Only when this DIB's pixels are what the window shows.</summary>
        public static bool Move(IntPtr hwnd, int x, int y, byte sca)
        {
            Native.POINT dst = new Native.POINT(x, y);
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
            blend.BlendOp = Native.AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = sca;
            blend.AlphaFormat = Native.AC_SRC_ALPHA;
            return Native.UpdateLayeredWindowMove(hwnd, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, Native.ULW_ALPHA);
        }
    }

    /// <summary>v5.0.2 (W16): what the overlay worker found about the crosshair's monitor (read on the worker, never on the
    /// render thread). Immutable; replaced as a whole.</summary>
    internal sealed class MonitorTarget
    {
        public readonly int Index;          // GlobalSettings.MonitorIndex it was looked up for
        public readonly bool PerMonitor;    // the worker thread is per-monitor DPI aware: CenterX/Y are physical pixels
        public readonly bool Found;
        public readonly double CenterX, CenterY;
        public readonly int Hz;
        public readonly string Device;

        public MonitorTarget(int index, bool perMonitor, bool found, double cx, double cy, int hz, string device)
        {
            Index = index; PerMonitor = perMonitor; Found = found; CenterX = cx; CenterY = cy; Hz = hz; Device = device;
        }
    }

    /// <summary>v5.0.2 (W16, AUDIT F3): the last DWM vblank (Stopwatch timestamp) and the exact refresh period (ticks).</summary>
    internal sealed class VBlankInfo
    {
        public readonly long Stamp;
        public readonly double Period;
        public VBlankInfo(long stamp, double period) { Stamp = stamp; Period = period; }
    }

    public sealed class Overlay
    {
        private volatile RenderConfig config;
        private volatile bool stopRequested;
        private Thread thread;
        /// <summary>v5.0.1 (W14): log lines and UI callbacks leave the render thread through this worker only.</summary>
        private readonly OverlayWorker worker = new OverlayWorker();
        private readonly StallWatch stall = new StallWatch();
        private volatile int refreshHz = RefreshRate.Fallback; // read on the worker (EnumDisplaySettings can be slow)
        private volatile MonitorTarget monitor;                // v5.0.2: the crosshair's monitor, from the worker
        private volatile VBlankInfo vblank;                    // v5.0.2: DWM timing, from the worker
        private volatile bool renderPerMonitor;                // v5.0.2: the render thread is per-monitor DPI aware

        /// <summary>Called when the toggle key flips the overlay: (sequence, newEnabled). v5.0.1: on the overlay worker thread,
        /// in order (was: on the render thread).</summary>
        public Action<int, bool> ToggleRequested;
        /// <summary>Called once (on the overlay worker thread) when the render thread hits 100 errors without 1 s of clean
        /// frames between them. v5.0.1: the thread does NOT stop any more; it keeps retrying (every 250 ms while failing).</summary>
        public Action<string> Fatal;
        /// <summary>Error logger (must be thread-safe). Only the overlay worker thread calls it.</summary>
        public Action<string> Log;

        public void Publish(RenderConfig c) { if (c != null) config = c; }

        /// <summary>v5.0.2 (W16, AUDIT F4): opt this process out of background power throttling (EcoQoS: low clocks and
        /// coarse timers for a process Windows thinks is in the background, e.g. on battery). Ignored on older Windows.</summary>
        internal static string OptOutOfPowerThrottling()
        {
            try
            {
                Native.PROCESS_POWER_THROTTLING_STATE st = new Native.PROCESS_POWER_THROTTLING_STATE();
                st.Version = 1;
                st.ControlMask = Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | Native.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;
                st.StateMask = 0;
                bool ok = Native.SetProcessInformation(Native.GetCurrentProcess(), Native.ProcessPowerThrottling, ref st, (uint)Marshal.SizeOf(typeof(Native.PROCESS_POWER_THROTTLING_STATE)));
                return ok ? "off" : "not available (Win32 error " + Marshal.GetLastWin32Error() + ")";
            }
            catch (Exception ex) { return "not available (" + ex.GetType().Name + ")"; }
        }

        /// <summary>v5.0.2 (W16, AUDIT F12 / UI M9): this thread uses physical pixels on every monitor (no DPI
        /// virtualisation of the overlay window). false on Windows before 10 1703 (the thread stays system-DPI aware).</summary>
        internal static bool MakeThreadPerMonitorAware()
        {
            try { return Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) != IntPtr.Zero; }
            catch (Exception) { return false; }
        }

        public void Start(RenderConfig initial)
        {
            try { CustomCrosshair.Install(); } catch (Exception) { } // v4.0 自定义图片 for the 外观 styles
            config = initial;
            StatusLine.LiveSource = delegate { return LastStatus; }; // v5.1 (W26): the 首页 status line reads the live crosshair
            stopRequested = false;
            Action<string> sink = Log;
            worker.Sink = sink;
            string throttle = OptOutOfPowerThrottling();
            long wfreq = Stopwatch.Frequency;
            Stopwatch wsw = Stopwatch.StartNew();
            long refreshAt = long.MinValue, vblankAt = long.MinValue;
            int seenVersion = int.MinValue;
            bool workerDpi = false, workerDpiTried = false;
            worker.Tick = delegate
            {
                long t = wsw.ElapsedTicks;
                if (!workerDpiTried)
                {
                    workerDpiTried = true;
                    workerDpi = MakeThreadPerMonitorAware();
                    worker.Log("overlay: power throttling " + throttle + ", worker per-monitor DPI " + (workerDpi ? "yes" : "no"));
                }
                RenderConfig c = config;
                bool cfgChanged = c != null && c.Version != seenVersion;
                if (refreshAt == long.MinValue || t - refreshAt >= wfreq * 5 || cfgChanged)
                {
                    refreshAt = t;
                    if (c != null) seenVersion = c.Version;
                    MonitorTarget m = MonitorLookup.Find(c != null && c.G != null ? c.G.MonitorIndex : -1, workerDpi);
                    monitor = m;
                    refreshHz = m.Hz > 0 ? m.Hz : RefreshRate.Primary();
                }
                if (vblankAt == long.MinValue || t - vblankAt >= wfreq)
                {
                    vblankAt = t;
                    vblank = RefreshRate.VBlank();
                }
                string s = stall.Check(Stopwatch.GetTimestamp(), wfreq);
                if (s != null) worker.Log(s);
                GpuDiagTick(c, t, wfreq); // v5.1 (W33): 显存诊断日志 (off = nothing)
            };
            worker.Start("HipAimerOverlayWorker");
            thread = new Thread(Run);
            thread.Name = "HipAimerRender";
            thread.IsBackground = true;
            // v4.3 (W7): the overlay window, key polling and motion all live on this thread with its own message loop.
            // v5.0.1 (W14): paced by a high-resolution waitable timer (Sleep(1) fallback), never DwmFlush; nothing here waits
            // on the UI thread, the settings window, the log file or a lock shared with them.
            thread.SetApartmentState(ApartmentState.STA);
            thread.Priority = ThreadPriority.AboveNormal; // a busy UI thread on the same core cannot starve the crosshair
            thread.Start();
        }

        /// <summary>v5.1 (W33, GlobalSettings.GpuDiagLog): once per second (overlay worker thread) one crash.log line with the
        /// foreground game's dedicated / shared GPU memory (PDH, the numbers Task Manager shows) and what our window is doing
        /// (shown / fully transparent / hidden, layered or DirectComposition), so crosshair on / off can be compared without
        /// putting Task Manager over the game. Off = no counters opened, no lines.</summary>
        private void GpuDiagTick(RenderConfig c, long t, long f)
        {
            bool on = c != null && c.G != null && W33Opt.GpuLog(c.G);
            if (!on)
            {
                if (gpuSampler != null) { gpuSampler.Dispose(); gpuSampler = null; worker.Log("gpu-diag: off"); }
                gpuNextAt = long.MinValue;
                return;
            }
            if (gpuNextAt != long.MinValue && t < gpuNextAt) return;
            gpuNextAt = t + f;
            if (gpuSampler == null)
            {
                gpuSampler = new GpuMemSampler();
                gpuLastError = null;
                worker.Log("gpu-diag: on (one line per second: foreground pid, its dedicated / shared GPU memory, overlay window state)");
            }
            int pid = diagFgPid;
            long ded, sh, all, ours;
            if (!gpuSampler.Sample(pid > 0 ? pid : -2, (int)ourPid, out ded, out sh, out all, out ours) && gpuSampler.Error != gpuLastError)
            {
                gpuLastError = gpuSampler.Error;
                worker.Log("gpu-diag: GPU counters not available (" + gpuLastError + "); window state is still logged");
            }
            worker.Log(OverlayDiag.Line(pid, diagFgIsGame, ded, sh, all, ours, diagBits));
        }

        public void Stop()
        {
            stopRequested = true;
            Thread t = thread;
            if (t != null && t.IsAlive) t.Join(3000);
            thread = null;
            worker.Stop(1000);
            GpuMemSampler gs = gpuSampler; gpuSampler = null; // v5.1 (W33)
            if (gs != null) { try { gs.Dispose(); } catch (Exception) { } }
        }

        // ---------------- render thread ----------------

        private IntPtr hwnd;
        private Dib dib;
        private Dib blank;           // v5.0.2: same size as dib, every pixel 0 (the 开镜 frame)
        private int[] fadeBuf;       // v5.0.2: the image x level (opacity / fades are in the pixels), reused
        private int dibLevel = -1;   // level the dib's pixels were written at (-1 = unknown)
        private bool surfaceCurrent; // the window shows exactly dib's pixels (a move needs no upload)
        private readonly RasterChangeDetector rasterChanged = new RasterChangeDetector(); // v4.0 PERF: was a per-frame string key
        private readonly RasterCache rasterCache = new RasterCache(); // v4.1 (BRUTE40 P1-2): costly looks render once, then move
        private readonly PresentGate presentGate = new PresentGate(); // v4.3 (W7): UpdateLayeredWindow only when something changed
        private readonly FrameVisibility visibility = new FrameVisibility(); // v5.0.2 (W16): 开镜 = one all-zero frame, then nothing
        private readonly WindowShowPolicy windowShow = new WindowShowPolicy(); // v5.0.2: really hidden only when off / game not in front
        private readonly TopmostPolicy topmost = new TopmostPolicy();        // v5.0.1 (W14): no per-second TOPMOST re-assert
        private readonly InputWake wake = new InputWake();                  // v5.0.2: 1 ms key poll between frames
        private readonly KeySnapshot keys = new KeySnapshot(Native.IsDown);  // v5.0.2 (F18): each key once per frame
        private int presentFails; // v4.3: consecutive refused presents (logged once per run of failures)
        private GlobalSettings pollG;    // settings (bindings + v5.1 丢雷 / 换弹 / weapon keys) the 1 ms poll reads (render thread only)
        private InputState lastRaw;      // key state the last frame used
        private bool lastToggleRaw;
        private readonly RingStyle style = new RingStyle(); // v3.0: the look drawn this frame (换手颜色 / 强化准星)
        private readonly LookExtras extras = new LookExtras(); // v5.1 (W22): CS feel + ammo hint, same frame (no added latency)
        private volatile int statusBits; // v5.1 (W26): the live state for the 首页 status line, packed (no lock on the render thread)

        /// <summary>v5.1 (W26 request): the overlay engine's real state, for StatusLine.LiveSource (any thread; one volatile
        /// read, never waits for the render thread).</summary>
        internal StatusSnapshot LastStatus
        {
            get
            {
                int b = statusBits;
                StatusSnapshot s = new StatusSnapshot();
                s.Enabled = (b & 1) != 0; s.AdsHidden = (b & 2) != 0; s.GrenadeHidden = (b & 4) != 0; s.FreeLookHidden = (b & 8) != 0;
                s.WaitingForGame = (b & 16) != 0; s.HandSwitched = (b & 32) != 0; s.BoostOn = (b & 64) != 0; s.BoostEnabled = (b & 128) != 0;
                s.AmmoOn = (b & 256) != 0;
                return s;
            }
        }

        /// <summary>Packs this frame's state into one int (no allocation, no lock; written only when it changed).</summary>
        private void PublishStatus(bool enabled, MotionEngine eng, GlobalSettings g, InputState input, bool gameFocused, bool ownForeground)
        {
            int b = (enabled ? 1 : 0) | (eng.AdsActive ? 2 : 0) | (eng.GrenadeHidden ? 4 : 0) | (g.HideOnFreeLook && input.FreeLook ? 8 : 0)
                | (g.OnlyWhenGameFocused && !gameFocused && !ownForeground ? 16 : 0) | (eng.HandBlend >= 0.5 ? 32 : 0)
                | (eng.Boosted ? 64 : 0) | (g.BoostEnabled ? 128 : 0) | (g.AmmoOn ? 256 : 0);
            if (b != statusBits) statusBits = b;
        }
        private int dibAmmoKey;      // v5.1: ammo hint pixels the dib holds (0 = none)
        private byte dibAmmoLevel;
        /// <summary>v4.1: largest overlay canvas side in px (= RingRaster.MaxCanvas; RingRaster.CanvasSize never returns more).</summary>
        public const int MaxCanvas = RingRaster.MaxCanvas;
        /// <summary>v5.0.2 (AUDIT F7): canvases above this side are drawn at whole-pixel positions (a move needs no new image).</summary>
        public const int SnapAbove = 128;

        private void PostToggle(int seq, bool val)
        {
            Action<int, bool> cb = ToggleRequested;
            if (cb != null) worker.Post(delegate { cb(seq, val); });
        }

        private void PostFatal(string m)
        {
            Action<string> f = Fatal;
            if (f != null) worker.Post(delegate { f(m); });
        }

        // ---- v5.1 (W33): DirectComposition mode, 开镜/丢雷真隐藏, GPU diagnostic log (all default off)
        private DCompPresenter dcomp;                                        // non-null = the live window presents through it
        private readonly PresentModeChoice presentMode = new PresentModeChoice();
        private volatile int diagBits;                                       // OverlayDiag.Pack of this frame (worker reads it)
        private volatile int diagFgPid;                                      // pid of the foreground window (0 none)
        private volatile bool diagFgIsGame;
        private IntPtr diagFg;
        private bool diagFgMatch;
        private GpuMemSampler gpuSampler;                                    // overlay worker thread only
        private long gpuNextAt = long.MinValue;
        private string gpuLastError;

        /// <summary>Creates the window with the present path PresentModeChoice asks for (DirectComposition when wanted and not
        /// failed before; any failure falls back to the layered window in the same call, logged once).</summary>
        private bool TryCreateWindow(long now, Action<string> log, bool wantDComp)
        {
            bool tryD = presentMode.TryDComp(wantDComp);
            if (tryD)
            {
                IntPtr h = IntPtr.Zero;
                DCompPresenter d = new DCompPresenter();
                string why = null;
                try
                {
                    h = OverlayWindow.CreateDComp("TarkovHipAimerOverlay");
                    if (!d.Init(h, 1)) why = d.LastError;
                }
                catch (Exception ex) { why = ex.Message; }
                if (why == null)
                {
                    hwnd = h; dcomp = d;
                    presentMode.Created(true, true);
                    log("overlay: DirectComposition mode on (composition swap chain, no redirection bitmap)");
                    AfterCreate(now);
                    return true;
                }
                d.Dispose();
                OverlayWindow.Destroy(h);
                presentMode.Created(true, false);
                log("overlay: DirectComposition mode failed (" + why + "), using the normal layered window");
            }
            else presentMode.Created(false, false);
            try
            {
                hwnd = OverlayWindow.CreateLayered("TarkovHipAimerOverlay");
                AfterCreate(now);
                return true;
            }
            catch (Exception ex)
            {
                hwnd = IntPtr.Zero;
                log("overlay: window not created (" + ex.Message + "), retrying in 1 s");
                return false;
            }
        }

        private void DestroyWindowNow()
        {
            if (dcomp != null) { dcomp.Dispose(); dcomp = null; }
            OverlayWindow.Destroy(hwnd);
            hwnd = IntPtr.Zero;
            presentGate.Invalidate();
            surfaceCurrent = false;
            visibility.Reset();
        }

        private void AfterCreate(long now)
        {
            topmost.Created(now);
            visibility.Reset();
            windowShow.Created();
            presentGate.Invalidate();
            surfaceCurrent = false;
        }

        private static IntPtr CreatePacerTimer()
        {
            try { return Native.CreateWaitableTimerExW(IntPtr.Zero, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS); }
            catch (Exception) { return IntPtr.Zero; } // Windows before 10 1803: no high-resolution timer -> Sleep(1)
        }

        /// <summary>v5.0.2 (W16): the 1 ms poll between frames. true = a bound key changed since the last frame.</summary>
        private bool PollChanged()
        {
            GlobalSettings pg = pollG;
            if (pg == null || pg.Keys == null) return false;
            keys.NextFrame();
            bool t = MotionEngine.IsActionDown(pg.Keys, 0, keys.IsDown);
            InputState i = MotionEngine.ReadInput(pg, keys.IsDown); // v5.1: G / R / weapon keys start a frame at once too
            return t != lastToggleRaw || !SameKeys(i, lastRaw);
        }

        internal static bool SameKeys(InputState a, InputState b)
        {
            return a.Fwd == b.Fwd && a.Back == b.Back && a.Left == b.Left && a.Right == b.Right && a.LeanL == b.LeanL && a.LeanR == b.LeanR
                && a.Fire == b.Fire && a.Ads == b.Ads && a.FreeLook == b.FreeLook && a.HandSwitch == b.HandSwitch && a.Jump == b.Jump
                && a.Boost == b.Boost && a.Tab == b.Tab && a.Esc == b.Esc && a.Mod == b.Mod && a.Shift == b.Shift
                && a.Grenade == b.Grenade && a.Reload == b.Reload && a.WeaponMain == b.WeaponMain && a.WeaponOther == b.WeaponOther;
        }

        /// <summary>v5.0.2 (W16, AUDIT F15): another program's visible topmost window sits above ours (a few GetWindow calls,
        /// no message to anyone).</summary>
        private static bool CoveredByOtherTopmost(IntPtr h)
        {
            try
            {
                IntPtr w = Native.GetWindow(h, Native.GW_HWNDPREV);
                for (int i = 0; i < 64 && w != IntPtr.Zero; i++, w = Native.GetWindow(w, Native.GW_HWNDPREV))
                {
                    if ((Native.GetWindowLong(w, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0) continue;
                    if (!Native.IsWindowVisible(w)) continue;
                    if (OursOf(w) == 0) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        private void Run()
        {
            bool periodSet = false;
            HashSet<string> logged = new HashSet<string>();
            Action<string> safeLog = worker.Log; // v5.0.1: lock-free queue, the worker thread writes crash.log
            RenderErrorBudget budget = new RenderErrorBudget(Stopwatch.Frequency);
            IntPtr timer = IntPtr.Zero;
            try
            {
                try { CustomCrosshair.NeverBlockThisThread(); } catch (Exception) { } // v4.1: pictures are decoded on the thread pool
                renderPerMonitor = MakeThreadPerMonitorAware(); // v5.0.2 (F12 / M9): before the window is created

                Stopwatch sw = Stopwatch.StartNew();
                long freq = Stopwatch.Frequency;
                long started = sw.ElapsedTicks;
                long swToStamp = Stopwatch.GetTimestamp() - started; // StallWatch / DWM use GetTimestamp (the worker has no 'sw')

                // Pacer: high-resolution waitable timer; its wait times out after one period + 50 ms, so it cannot hang.
                timer = CreatePacerTimer();
                FramePacer.PrimaryWait primary = null;
                if (timer != IntPtr.Zero)
                {
                    IntPtr th = timer;
                    primary = delegate(long ticks)
                    {
                        long due = -(long)(ticks * 10000000.0 / freq); // relative, 100 ns units
                        if (due >= 0) due = -1;
                        if (!Native.SetWaitableTimer(th, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) return false;
                        uint ms = (uint)Math.Min(1000, ticks * 1000 / freq + 50);
                        return Native.WaitForSingleObject(th, ms) == Native.WAIT_OBJECT_0;
                    };
                }
                Func<long> clock = delegate { return sw.ElapsedTicks; };
                Action sleep1 = delegate { Thread.Sleep(1); };
                FramePacer pacer = new FramePacer(clock, freq, primary, sleep1);
                if (!pacer.UsingPrimary) { Native.timeBeginPeriod(1); periodSet = true; } // only the Sleep(1) fallback needs it
                Func<bool> poll = PollChanged;
                VBlankInfo seenVb = null;
                safeLog("overlay: render thread started, pacing " + (pacer.UsingPrimary ? "high-resolution timer" : "Sleep(1)") + ", refresh " + refreshHz
                    + " Hz, 1 ms key poll, per-monitor DPI " + (renderPerMonitor ? "yes" : "no"));

                long nextCreate = 0;
                RenderConfig c0 = config;
                TryCreateWindow(sw.ElapsedTicks, safeLog, c0 != null && c0.G != null && W33Opt.DComp(c0.G)); // v5.1 (W33)
                if (hwnd == IntPtr.Zero) nextCreate = sw.ElapsedTicks + freq;

                MotionEngine eng = new MotionEngine();
                LiveInputFilter liveInput = new LiveInputFilter(); // v2.4.1: opposite keys, last pressed wins (live only)
                long last = sw.ElapsedTicks;
                int fpsFrames = 0; long fpsStart = last; double fps = 0;
                int seenVersion = int.MinValue;
                GlobalSettings g = null;
                bool localEnabled = true;
                int toggleSeq = int.MinValue;
                bool prevToggle = false;
                bool prevKeysLive = true; // v4.0: a 开关键 held while the game comes back to front is not a press
                bool fatalSent = false;
                ForegroundTracker fgTrack = new ForegroundTracker();
                bool gameFocused = true, ownForeground = false, keysLiveNow = false;
                bool inputFrame = false;
                PhaseStats phases = new PhaseStats();
                long nextBeat = HeartbeatSchedule.NextAfter(started, started, freq);
                long totalFrames = 0;

                while (!stopRequested)
                {
                    bool failed = false;
                    long p0 = sw.ElapsedTicks;
                    try
                    {
                        stall.Enter(PhaseStats.Pump);
                        if (hwnd == IntPtr.Zero && p0 >= nextCreate)
                        {
                            if (!TryCreateWindow(p0, safeLog, g != null ? W33Opt.DComp(g) : (config != null && config.G != null && W33Opt.DComp(config.G)))) nextCreate = p0 + freq;
                        }
                        // pump our window's messages (PeekMessage never waits)
                        Native.MSG msg;
                        int pumped = 0;
                        while (pumped++ < 64 && Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                        {
                            Native.TranslateMessage(ref msg);
                            Native.DispatchMessage(ref msg);
                        }
                        long p1 = sw.ElapsedTicks;
                        phases.Add(PhaseStats.Pump, p1 - p0);
                        stall.Enter(PhaseStats.Input);

                        RenderConfig cfg = config;
                        if (cfg == null) throw new InvalidOperationException("no settings yet");
                        if (cfg.Version != seenVersion || g == null)
                        {
                            seenVersion = cfg.Version;
                            g = cfg.G.Clone(false);
                            if (toggleSeq == int.MinValue) toggleSeq = cfg.ToggleAck;
                            if (cfg.ToggleAck == toggleSeq) localEnabled = cfg.G.Enabled;
                        }
                        // v5.1 (W33): 新的显示方式 switched on / off (or DirectComposition failed on the live window): new window
                        if (hwnd != IntPtr.Zero && presentMode.NeedRecreate(W33Opt.DComp(g)))
                        {
                            DestroyWindowNow();
                            if (!TryCreateWindow(p1, safeLog, W33Opt.DComp(g))) nextCreate = p1 + freq;
                        }

                        // v5.0.2 (Leo: minimum latency): the motion step ends when the keys are read, right before the push
                        // (an input frame starts the moment the 1 ms poll saw the change).
                        long now = sw.ElapsedTicks;
                        double dt = (now - last) / (double)freq;
                        last = now;
                        if (dt > 0.25) dt = 0.25; // a long gap (stall, sleep) is not one huge motion step

                        // v3.0 只在塔科夫前台时显示. v4.3 (W7): GetForegroundWindow every frame; owner + title read only when the
                        // foreground window changes (ForegroundTracker), never the title of our own window (that would wait
                        // for the UI thread). Back in the game = visible and keys live in the same frame.
                        IntPtr fgNow = GetForegroundWindow();
                        fgTrack.Update(fgNow, now, freq, OursOfFn, TitleOfFn, g.GameWindowTitle);
                        if (W33Opt.GpuLog(g) && (fgNow != diagFg || fgTrack.Match != diagFgMatch)) // v5.1 (W33): pid for the GPU log
                        {
                            diagFg = fgNow; diagFgMatch = fgTrack.Match;
                            uint fpid = 0;
                            if (fgNow != IntPtr.Zero) GetWindowThreadProcessId(fgNow, out fpid);
                            diagFgIsGame = diagFgMatch; diagFgPid = (int)fpid;
                        }
                        gameFocused = fgTrack.GameFocused(g.OnlyWhenGameFocused);
                        ownForeground = fgTrack.Ours == 1;
                        eng.GameFocused = gameFocused;
                        eng.OwnForeground = ownForeground;
                        bool keysLive = FocusGate.KeysLive(g.OnlyWhenGameFocused, gameFocused);
                        keysLiveNow = keysLive;

                        // Input: GetAsyncKeyState polling only (none while the game is not in front and the option is on).
                        // v5.0.2: an input frame reuses the key reads its poll made a moment ago (same KeySnapshot frame).
                        KeyBindings k = g.Keys;
                        pollG = g;
                        if (!inputFrame) keys.NextFrame();
                        inputFrame = false;
                        FilterKeyFeed.FrameAll(keys.IsDown); // v5.1 (W24): filter hotkeys from this frame's key snapshot (edges only reach the UI)
                        bool toggleRaw = keysLive && MotionEngine.IsActionDown(k, 0, keys.IsDown); // v4.0: combo keys (CORE API)
                        bool toggleDown = toggleRaw;
                        if (keysLive && !prevKeysLive) prevToggle = toggleDown;
                        prevKeysLive = keysLive;
                        if (toggleDown && !prevToggle)
                        {
                            localEnabled = !localEnabled;
                            toggleSeq++;
                            PostToggle(toggleSeq, localEnabled); // v5.0.1: via the worker, never waits for the UI thread
                        }
                        prevToggle = toggleDown;

                        InputState raw = keysLive ? MotionEngine.ReadInput(g, keys.IsDown) : new InputState(); // GetAsyncKeyState polling only; v5.1: + 丢雷 / 换弹 / weapon keys
                        lastRaw = raw; lastToggleRaw = toggleRaw;
                        // Resets itself when the ring is switched on and when the preview returns to 实时.
                        InputState input = liveInput.Next(raw, localEnabled, cfg.Preview == PreviewMode.Live, keysLive);

                        g.Enabled = localEnabled;
                        eng.Update(dt, input, cfg.P, g, cfg.Preview, cfg.PreviewSpeed);
                        long p2 = sw.ElapsedTicks;
                        phases.Add(PhaseStats.Input, p2 - p1);
                        stall.Enter(PhaseStats.Draw);

                        // Draw. The look follows the hand (换手颜色) and 强化准星. v5.0.2 (W16): opacity and fades are in the
                        // pixels; level 0 (开镜 / 自由视角 / faded out) = ONE all-zero frame in the same window, then nothing.
                        // The window is hidden only while the crosshair is off or the game is not in front.
                        style.Set(cfg.P, eng.HandBlend, eng.Boosted);
                        // v5.1 (W22, decisions 1 / 6): the CS feel changes this frame's look from this frame's engine state
                        // and the ammo hint is drawn in the same canvas (hidden with it on 开镜 / 丢雷 / off: level 0 below).
                        AnimInput ai; AmmoView av;
                        OverlayInputs.Fill(eng, g, raw, keysLive, out ai, out av);
                        extras.Frame(style, extras.AnimFrom(g), ai, g.AmmoOn, av, g.AmmoStyle, extras.BarFrom(g), dt, now * 1000.0 / freq); // v5.1.1 B: cached settings
                        byte crossLevel = FrameVisibility.Level(style.OpacityPct, eng.Alpha);
                        byte ammoLevel = extras.Ammo.Visible ? FrameVisibility.Level(100, eng.Alpha) : (byte)0;
                        byte level = crossLevel > ammoLevel ? crossLevel : ammoLevel; // 0 = the all-zero frame (开镜 path)
                        PublishStatus(localEnabled, eng, g, input, gameFocused, ownForeground);
                        // v5.1 (W33): 开镜/丢雷时真正隐藏 (option, default off): 开镜 / 丢雷 hide the window like 'off' does
                        bool realHide = OverlayDiag.RealHideNow(W33Opt.RealHide(g), localEnabled, eng.AdsActive, eng.GrenadeHidden);
                        bool longHidden = WindowShowPolicy.LongHidden(localEnabled, g.OnlyWhenGameFocused, gameFocused, ownForeground) || realHide;
                        if (hwnd != IntPtr.Zero)
                        {
                            bool shownNow = false;
                            if (windowShow.BeforeDraw(longHidden) == WindowOp.Show)
                            {
                                Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE); // shows the all-zero frame it was hidden with
                                shownNow = true;
                            }
                            FrameAction act = windowShow.Shown ? visibility.Next(level) : FrameAction.Skip;
                            if (act == FrameAction.Draw) Draw(cfg, style, eng, crossLevel, ammoLevel, safeLog);
                            else if (act == FrameAction.PresentHidden)
                            {
                                if (!PresentHidden()) visibility.HiddenPresentFailed();
                            }
                            if (windowShow.AfterDraw(longHidden, visibility.Hidden) == WindowOp.Hide)
                                Native.ShowWindow(hwnd, Native.SW_HIDE); // off / game not in front only (开镜 / 丢雷 only with 真隐藏 on)
                            bool covered = windowShow.Shown && topmost.ProbeDue(p2, freq) && CoveredByOtherTopmost(hwnd);
                            if (topmost.Need(fgTrack.Changed || shownNow || covered, p2, freq))
                                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                        }
                        int db = OverlayDiag.Pack(OverlayDiag.StateOf(hwnd != IntPtr.Zero, windowShow.Shown, visibility.Hidden), dcomp != null,
                            localEnabled, eng.AdsActive, eng.GrenadeHidden, W33Opt.RealHide(g));
                        if (db != diagBits) diagBits = db;
                        long p3 = sw.ElapsedTicks;
                        phases.Add(PhaseStats.Draw, p3 - p2);

                        fpsFrames++;
                        if (p3 - fpsStart >= freq / 2) { fps = fpsFrames / ((p3 - fpsStart) / (double)freq); fpsFrames = 0; fpsStart = p3; }
                        budget.Ok(p3);
                        if (fatalSent && budget.Errors == 0) { fatalSent = false; safeLog("overlay: frames are clean again"); }
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        presentGate.Invalidate();
                        surfaceCurrent = false;
                        visibility.Reset();
                        string m = ex.GetType().Name + ": " + ex.Message;
                        if (logged.Count < 50 && logged.Add(m)) safeLog("render: " + ex.ToString());
                        // v4.3: 100 errors without 1 s of clean frames between them. v5.0.1 (W14): report once, keep going.
                        if (budget.Failed() && !fatalSent) { fatalSent = true; PostFatal(m); }
                    }

                    // Pacing (never DwmFlush). A failing frame waits 10 ms (250 ms once the error budget is spent).
                    // v5.0.2: the wait is cut into 1 ms slices; a key change between them starts the next frame at once.
                    // Locked to the vblank when DWM's timing matches the monitor; 60 Hz while the window is hidden.
                    stall.Enter(PhaseStats.Wait);
                    long w0 = sw.ElapsedTicks;
                    if (failed) Thread.Sleep(fatalSent ? 250 : 10);
                    else
                    {
                        VBlankInfo vb = vblank;
                        if (vb != seenVb) { seenVb = vb; if (vb == null) pacer.ClearVBlank(); else pacer.SetVBlank(vb.Stamp - swToStamp, vb.Period); }
                        int hz = refreshHz;
                        if (!windowShow.Shown && hz > 60) hz = 60;
                        bool hadPrimary = pacer.UsingPrimary;
                        inputFrame = wake.WaitNext(pacer, hz, freq, keysLiveNow && hwnd != IntPtr.Zero ? poll : null);
                        if (hadPrimary && pacer.FellBack)
                        {
                            pacer.FellBack = false;
                            if (!periodSet) { Native.timeBeginPeriod(1); periodSet = true; }
                            safeLog("overlay: high-resolution timer failed (" + (pacer.LastError ?? ("Win32 error " + Marshal.GetLastWin32Error())) + "), pacing with Sleep(1)");
                        }
                    }
                    long w1 = sw.ElapsedTicks;
                    phases.Add(PhaseStats.Wait, w1 - w0);
                    phases.Frame();
                    totalFrames++;
                    stall.Frame(w1 + swToStamp);

                    if (w1 >= nextBeat)
                    {
                        nextBeat = HeartbeatSchedule.NextAfter(started, w1, freq);
                        safeLog("overlay alive: " + ((w1 - started) / freq) + " s, total frames " + totalFrames + ", fps " + fps.ToString("0")
                            + ", pacing " + (pacer.UsingPrimary ? "timer" : "sleep") + " @" + refreshHz + " Hz" + (pacer.PhaseLocked(refreshHz) ? " vblank-locked" : "")
                            + ", input frames " + wake.InputFrames + ", window " + (hwnd != IntPtr.Zero ? (windowShow.Shown ? "shown" : "hidden") : "none")
                            + ", blank " + (visibility.Hidden ? "yes" : "no") + ", errors " + budget.Errors + "; " + phases.TakeLine(freq));
                    }
                }
            }
            catch (Exception ex)
            {
                safeLog("render thread: " + ex.ToString());
                PostFatal(ex.Message);
            }
            finally
            {
                if (dib != null) { dib.Dispose(); dib = null; }
                if (blank != null) { blank.Dispose(); blank = null; }
                rasterChanged.Reset();
                if (dcomp != null) { dcomp.Dispose(); dcomp = null; }
                OverlayWindow.Destroy(hwnd);
                hwnd = IntPtr.Zero;
                if (timer != IntPtr.Zero) { try { Native.CloseHandle(timer); } catch (Exception) { } }
                if (periodSet) Native.timeEndPeriod(1);
            }
        }

        /// <summary>v5.0.2 (W16, Leo: 开镜 = COMPLETELY invisible, the window unchanged): ONE UpdateLayeredWindow of a cached
        /// same-size bitmap whose every pixel is 0, at the last place, with the same SourceConstantAlpha (no move, no resize,
        /// no z-order / visibility / style change). true = done (or nothing was ever shown).</summary>
        private bool PresentHidden()
        {
            if (dib == null || !presentGate.Have) return true;
            int S = dib.W;
            if (blank == null || blank.W != S)
            {
                if (blank != null) blank.Dispose();
                blank = null;
                blank = new Dib(S, S);
                Marshal.Copy(new int[S * S], 0, blank.Bits, S * S); // all 0 (once per canvas size)
            }
            bool ok = dcomp != null ? DCompPresent(blank.Bits, S, presentGate.LastX, presentGate.LastY, null)
                : blank.Present(hwnd, presentGate.LastX, presentGate.LastY, presentGate.LastSca);
            presentGate.Invalidate(); // the next visible frame uploads the crosshair again
            surfaceCurrent = false;
            return ok;
        }
        /// <summary>v5.1 (W33): a DirectComposition present; a failure is logged once and the next frame rebuilds the window
        /// on the layered path (PresentModeChoice latches until the option is switched off and on).</summary>
        private bool DCompPresent(IntPtr bits, int s, int x, int y, Action<string> log)
        {
            if (dcomp.Present(bits, s, x, y)) return true;
            DCompFailed(log);
            return false;
        }

        private bool DCompMove(int x, int y, Action<string> log)
        {
            if (dcomp.Move(x, y)) return true;
            DCompFailed(log);
            return false;
        }

        private void DCompFailed(Action<string> log)
        {
            presentMode.RuntimeFailed();
            Action<string> l = log ?? worker.Log;
            l("overlay: DirectComposition present failed (" + dcomp.LastError + "), switching to the normal layered window");
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int max);
        private static readonly uint ourPid = (uint)Process.GetCurrentProcess().Id;
        private static readonly System.Text.StringBuilder titleBuf = new System.Text.StringBuilder(1025); // render thread only

        private static int OursOf(IntPtr h)
        {
            uint pid;
            if (GetWindowThreadProcessId(h, out pid) == 0) return -1;
            return pid == ourPid ? 1 : 0;
        }

        /// <summary>Title of a window of ANOTHER process (cached caption, no message sent). Never called for ours.</summary>
        private static string TitleOf(IntPtr h)
        {
            try
            {
                titleBuf.Length = 0;
                int n = GetWindowText(h, titleBuf, 1025);
                return n <= 0 ? "" : titleBuf.ToString(0, Math.Min(n, titleBuf.Length));
            }
            catch (Exception) { return null; }
        }

        private static readonly Func<IntPtr, int> OursOfFn = OursOf;
        private static readonly Func<IntPtr, string> TitleOfFn = TitleOf;

        /// <summary>v5.0.2 (W16, AUDIT F12): the crosshair's centre in this thread's pixels. The UI thread's centre is in
        /// system-DPI pixels; when both overlay threads are per-monitor aware the worker's physical centre is used.</summary>
        private void Center(RenderConfig cfg, out double cx, out double cy)
        {
            MonitorTarget m = monitor;
            if (renderPerMonitor && m != null && m.PerMonitor && m.Found && m.Index == cfg.G.MonitorIndex) { cx = m.CenterX; cy = m.CenterY; return; }
            cx = cfg.CenterX; cy = cfg.CenterY;
        }

        private void Draw(RenderConfig cfg, RingStyle p, MotionEngine eng, byte level, byte ammoLevel, Action<string> log)
        {
            // v4.1 (BRUTE40 P1-1/P1-5): a huge 强化 scale (size 4 -> 300, gap/length 100) asked for a ~30,000 px canvas (~3.4 GB).
            // RingStyle.Set keeps the 强化 ratio at 0.5..3 and shrinks a look that still would not fit; CanvasSize never
            // returns more than RingRaster.MaxCanvas (1024). The window is only this canvas (the crosshair + its margin).
            // v5.1 (W22): LookExtras.Canvas = that size, or the size the largest CS opening / the ammo hint need (constant
            // while they animate, so the window never resizes for them).
            int S = extras.Canvas;
            if (S < 1) S = 1;
            double ccx, ccy;
            Center(cfg, out ccx, out ccy);
            double X = ccx + eng.OffsetX + extras.Anim.ShakeX;
            double Y = ccy + eng.OffsetY + extras.Anim.ShakeY;
            if (S > SnapAbove) { X = Math.Round(X); Y = Math.Round(Y); } // v5.0.2 (F7): a big look moves by whole pixels only
            int winX = (int)Math.Floor(X) - S / 2;
            int winY = (int)Math.Floor(Y) - S / 2;
            double cx = Math.Round((X - winX) * 8.0) / 8.0;
            double cy = Math.Round((Y - winY) * 8.0) / 8.0;
            RasterCache.Normalise(S, ref winX, ref cx); // v4.1: sub-pixel phase 0..7/8 per axis (same pixels on screen)
            RasterCache.Normalise(S, ref winY, ref cy);

            if (dib == null || dib.W != S)
            {
                if (dib != null) dib.Dispose();
                dib = null;
                dib = new Dib(S, S);
                fadeBuf = new int[S * S];
                dibAmmoKey = -1;
                rasterChanged.Reset();
                presentGate.Invalidate();
                surfaceCurrent = false;
                dibLevel = -1;
            }
            // Same fields as p.Key() + S + cx + cy (v4.0 PERF: compared in place, no string per frame). A whole-pixel move
            // with the same phase changes nothing here: only the window moves (Present below).
            bool ammo = ammoLevel != 0 && extras.Ammo.Visible;
            int ammoKey = ammo ? extras.Ammo.Key : 0;
            bool fx = level != 0 && extras.FxActive; // v5.1.1 (B2): 开火波纹 / 开火渐变色 this frame
            if (fx) unchecked { ammoKey = ammoKey * 397 ^ extras.Fx.Key; }
            bool imageChanged = rasterChanged.Changed(p, S, cx, cy) | level != dibLevel | ammoKey != dibAmmoKey | (ammo && ammoLevel != dibAmmoLevel);
            if (imageChanged)
            {
                try
                {
                    // v4.1 (BRUTE40 P1-2, REBRUTE41 P2-4): never a rasterisation while only the position changes -- the phase
                    // image comes from RasterCache (a cached copy for small canvases, the moved master for big ones); a look
                    // is rasterised once when it changes. v5.0.2: opacity / fades are applied to the pixels here.
                    int[] img = rasterCache.Image(p, S, cx, cy);
                    if (!ammo && !fx && level == 255) Marshal.Copy(img, 0, dib.Bits, S * S);
                    else
                    {
                        FadePixels.Scale(img, fadeBuf, S * S, level);
                        // v5.1 (W22): the ammo hint goes UNDER the crosshair's pixels, outside the look's reach (never
                        // recolours the crosshair, never inside the 中心留空).
                        // v5.1.1 (B2): the gradient recolours the crosshair's own pixels first, the ripples go under it
                        if (fx) extras.Fx.Draw(fadeBuf, S, cx, cy, extras.Reach, level);
                        if (ammo) extras.Ammo.Draw(fadeBuf, S, cx, cy, extras.Inner, extras.AmmoStyle, ammoLevel);
                        Marshal.Copy(fadeBuf, 0, dib.Bits, S * S);
                    }
                    dibLevel = level;
                    dibAmmoKey = ammoKey; dibAmmoLevel = ammoLevel;
                    surfaceCurrent = false;
                }
                catch (Exception) { rasterChanged.Reset(); rasterCache.Clear(); dibLevel = -1; dibAmmoKey = -1; throw; } // not drawn: retry next frame
            }
            // v4.3 (W7, V43-PERF S2b #2): UpdateLayeredWindow only when position or image changed.
            const byte sca = FrameVisibility.WindowSca;
            if (!presentGate.Need(winX, winY, sca, imageChanged)) return;
            // v5.0.2 (F7): only the position changed and the window already shows these pixels: move it, no bitmap upload
            bool ok = dcomp != null ? (surfaceCurrent ? DCompMove(winX, winY, log) : DCompPresent(dib.Bits, S, winX, winY, log))
                : (surfaceCurrent ? Dib.Move(hwnd, winX, winY, sca) : dib.Present(hwnd, winX, winY, sca));
            if (ok) { presentFails = 0; surfaceCurrent = true; return; }
            if (dcomp != null) { presentGate.Invalidate(); surfaceCurrent = false; return; } // v5.1 (W33): layered window next frame
            presentGate.Invalidate(); // refused (e.g. display-mode switch): try again next frame, no exception, no 10 ms sleep
            surfaceCurrent = false;
            if (presentFails++ == 0 && log != null) log("render: UpdateLayeredWindow refused, Win32 error " + Marshal.GetLastWin32Error() + " (retrying each frame)");
        }
    }

    /// <summary>v5.0.2 (W16, AUDIT F3 / F12): finds the monitor of GlobalSettings.MonitorIndex (Screen.AllScreens order =
    /// EnumDisplayMonitors order; -1 or out of range = the primary) on the calling (worker) thread: its centre and its own
    /// refresh rate (was: the primary monitor's rate for every monitor). Never throws.</summary>
    internal static class MonitorLookup
    {
        public static MonitorTarget Find(int index, bool perMonitor)
        {
            try
            {
                List<IntPtr> all = new List<IntPtr>();
                Native.MonitorEnumProc cb = delegate(IntPtr m, IntPtr dc, ref Native.RECT r, IntPtr d) { all.Add(m); return true; };
                Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
                for (int i = 0; i < all.Count; i++)
                {
                    Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                    if (!Native.GetMonitorInfoW(all[i], ref mi)) continue;
                    bool want = index >= 0 && index < all.Count ? i == index : (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                    if (!want) continue;
                    Native.RECT rc = mi.rcMonitor;
                    return new MonitorTarget(index, perMonitor, true, rc.Left + (rc.Right - rc.Left) / 2.0, rc.Top + (rc.Bottom - rc.Top) / 2.0,
                        RefreshRate.Of(mi.szDevice), mi.szDevice);
                }
                return new MonitorTarget(index, perMonitor, false, 0, 0, RefreshRate.Primary(), null);
            }
            catch (Exception) { return new MonitorTarget(index, perMonitor, false, 0, 0, RefreshRate.Fallback, null); }
        }
    }

    /// <summary>
    /// v4.0: the overlay runs at the monitor's refresh rate: the primary display's current refresh from EnumDisplaySettings,
    /// 60 if unknown. v5.0.1 (W14): this is the pacing rate (FramePacer); it is read on the overlay worker every 5 s, never
    /// on the render thread.
    /// </summary>
    internal static class RefreshRate
    {
        public const int Fallback = 60;
        private const int EnumCurrentSettings = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        /// <summary>Hz value -> usable rate: 0/1 ("hardware default") or nonsense -> 60, else clamped to 30..500.</summary>
        public static int Sanitize(int hz)
        {
            if (hz <= 1 || hz > 1000) return Fallback;
            return Math.Max(30, Math.Min(500, hz));
        }

        /// <summary>v5.0.2 (W16, AUDIT F3): current refresh of one display device (e.g. \\.\DISPLAY2); null = primary.</summary>
        public static int Of(string device)
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(string.IsNullOrEmpty(device) ? null : device, EnumCurrentSettings, ref dm)) return Sanitize(dm.dmDisplayFrequency);
            }
            catch (Exception) { }
            return Fallback;
        }

        /// <summary>v5.0.2 (W16, AUDIT F3): DWM's last vblank and exact refresh period in Stopwatch ticks, or null (call failed,
        /// no QPC clock, a period outside 20..1000 Hz). Worker thread only.</summary>
        public static VBlankInfo VBlank()
        {
            try
            {
                if (!Stopwatch.IsHighResolution) return null;
                Native.DWM_TIMING_INFO ti = new Native.DWM_TIMING_INFO();
                ti.cbSize = (uint)Marshal.SizeOf(typeof(Native.DWM_TIMING_INFO));
                if (Native.DwmGetCompositionTimingInfo(IntPtr.Zero, ref ti) != 0) return null;
                double period = ti.qpcRefreshPeriod;
                if (!(period > Stopwatch.Frequency / 1000.0) || period > Stopwatch.Frequency / 20.0 || ti.qpcVBlank == 0) return null;
                return new VBlankInfo((long)ti.qpcVBlank, period);
            }
            catch (Exception) { return null; }
        }

        public static int Primary()
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, EnumCurrentSettings, ref dm)) return Sanitize(dm.dmDisplayFrequency);
            }
            catch (Exception) { }
            return Fallback;
        }
    }
}
