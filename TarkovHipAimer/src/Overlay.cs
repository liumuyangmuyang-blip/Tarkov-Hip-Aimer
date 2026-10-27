using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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

    public sealed class DebugInfo
    {
        public readonly MotionDebug Motion;
        public readonly double Fps;
        public readonly bool FixedPacing;

        public DebugInfo(MotionDebug m, double fps, bool fixedPacing) { Motion = m; Fps = fps; FixedPacing = fixedPacing; }
    }

    /// <summary>Our own click-through layered popup. Lives on the render thread.</summary>
    internal sealed class OverlayWindow : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST) { m.Result = new IntPtr(Native.HTTRANSPARENT); return; }
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        public static OverlayWindow CreateLayered(string caption)
        {
            CreateParams cp = new CreateParams();
            cp.Caption = caption;
            cp.Style = Native.WS_POPUP;
            cp.ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            cp.X = 0; cp.Y = 0; cp.Width = 1; cp.Height = 1;
            OverlayWindow w = new OverlayWindow();
            w.CreateHandle(cp);
            return w;
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

        public void Present(IntPtr hwnd, int x, int y, byte sca)
        {
            Native.POINT dst = new Native.POINT(x, y);
            Native.SIZE size = new Native.SIZE(W, H);
            Native.POINT src = new Native.POINT(0, 0);
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
            blend.BlendOp = Native.AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = sca;
            blend.AlphaFormat = Native.AC_SRC_ALPHA;
            if (!Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, MemDC, ref src, 0, ref blend, Native.ULW_ALPHA))
                throw new InvalidOperationException("UpdateLayeredWindow failed, Win32 error " + Marshal.GetLastWin32Error());
        }
    }

    public sealed class Overlay
    {
        private volatile RenderConfig config;
        private volatile DebugInfo debug;
        private volatile bool stopRequested;
        private int resetSeq; // read/written with Interlocked
        private Thread thread;

        /// <summary>Called ON THE RENDER THREAD when the toggle key flips the overlay: (sequence, newEnabled).</summary>
        public Action<int, bool> ToggleRequested;
        /// <summary>Called ON THE RENDER THREAD when the thread gives up after 100 errors.</summary>
        public Action<string> Fatal;
        /// <summary>Error logger (must be thread-safe).</summary>
        public Action<string> Log;

        public DebugInfo Debug { get { return debug; } }

        public void Publish(RenderConfig c) { config = c; }

        public void RequestReset() { Interlocked.Increment(ref resetSeq); }

        public void Start(RenderConfig initial)
        {
            config = initial;
            stopRequested = false;
            thread = new Thread(Run);
            thread.Name = "HipAimerRender";
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop()
        {
            stopRequested = true;
            Thread t = thread;
            if (t != null && t.IsAlive) t.Join(3000);
            thread = null;
        }

        // ---------------- render thread ----------------

        private OverlayWindow win;
        private Dib dib;
        private int[] raster;
        private string rasterKey;
        private readonly RingStyle style = new RingStyle(); // v3.0: the look drawn this frame (换手颜色 / 强化准星)

        private void Run()
        {
            bool periodSet = false;
            HashSet<string> logged = new HashSet<string>();
            int errors = 0;
            try
            {
                win = OverlayWindow.CreateLayered("TarkovHipAimerOverlay");
                Native.ShowWindow(win.Handle, Native.SW_SHOWNOACTIVATE);

                MotionEngine eng = new MotionEngine();
                LiveInputFilter liveInput = new LiveInputFilter(); // v2.4.1: opposite keys, last pressed wins (live only)
                Stopwatch sw = Stopwatch.StartNew();
                long freq = Stopwatch.Frequency;
                long last = sw.ElapsedTicks;
                long lastTop = 0, lastDbg = 0, nextTick = last;
                int fpsFrames = 0; long fpsStart = last; double fps = 0;
                int seenVersion = int.MinValue;
                GlobalSettings g = null;
                bool localEnabled = true;
                int toggleSeq = int.MinValue;
                bool prevToggle = false;
                bool dwmBroken = false;
                int fastDwm = 0;
                int zeroFrames = 0;
                int seenReset = Interlocked.CompareExchange(ref resetSeq, 0, 0);
                long lastFocus = long.MinValue;
                bool gameFocused = true, ownForeground = false;

                while (!stopRequested)
                {
                    bool fixedMode = true;
                    try
                    {
                        // Pump our window's messages.
                        Native.MSG msg;
                        while (Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                        {
                            Native.TranslateMessage(ref msg);
                            Native.DispatchMessage(ref msg);
                        }

                        RenderConfig cfg = config;
                        if (cfg.Version != seenVersion)
                        {
                            seenVersion = cfg.Version;
                            g = cfg.G.Clone(false);
                            if (toggleSeq == int.MinValue) toggleSeq = cfg.ToggleAck;
                            if (cfg.ToggleAck == toggleSeq) localEnabled = cfg.G.Enabled;
                        }
                        fixedMode = dwmBroken || g.FrameMode == "fixed";
                        if (fixedMode && !periodSet) { Native.timeBeginPeriod(1); periodSet = true; }

                        int rs = Interlocked.CompareExchange(ref resetSeq, 0, 0);
                        if (rs != seenReset) { seenReset = rs; eng.Reset(); }

                        long now = sw.ElapsedTicks;
                        double dt = (now - last) / (double)freq;
                        last = now;

                        // v3.0 只在塔科夫前台时显示: foreground window title, polled at 10 Hz (GetForegroundWindow + GetWindowText only).
                        if (lastFocus == long.MinValue || now - lastFocus >= freq / 10)
                        {
                            lastFocus = now;
                            gameFocused = !g.OnlyWhenGameFocused || GameWindow.IsForeground(g.GameWindowTitle);
                            ownForeground = Native.ForegroundIsOurs() == 1;
                        }
                        eng.GameFocused = gameFocused;
                        eng.OwnForeground = ownForeground;
                        bool keysLive = FocusGate.KeysLive(g.OnlyWhenGameFocused, gameFocused);

                        // Input: GetAsyncKeyState polling only (none while the game is not in front and the option is on).
                        KeyBindings k = g.Keys;
                        bool toggleDown = keysLive && Native.IsDown(k.Toggle);
                        if (toggleDown && !prevToggle)
                        {
                            localEnabled = !localEnabled;
                            toggleSeq++;
                            Action<int, bool> cb = ToggleRequested;
                            if (cb != null) cb(toggleSeq, localEnabled);
                        }
                        prevToggle = toggleDown;

                        InputState input = keysLive ? MotionEngine.ReadInput(k, Native.IsDown) : new InputState(); // GetAsyncKeyState polling only
                        // Resets itself when the ring is switched on and when the preview returns to 实时.
                        input = liveInput.Next(input, localEnabled, cfg.Preview == PreviewMode.Live);

                        g.Enabled = localEnabled;
                        eng.Update(dt, input, cfg.P, g, cfg.Preview, cfg.PreviewSpeed);

                        // Draw. The look follows the hand (换手颜色) and 强化准星.
                        style.Set(cfg.P, eng.HandBlend, eng.Boosted);
                        int sca = (int)Math.Round(255.0 * (style.OpacityPct / 100.0) * eng.Alpha);
                        if (sca < 0) sca = 0; if (sca > 255) sca = 255;
                        if (sca == 0) zeroFrames++; else zeroFrames = 0;
                        if (zeroFrames < 2) Draw(cfg, style, eng, (byte)sca);

                        if (now - lastTop >= freq)
                        {
                            lastTop = now;
                            Native.SetWindowPos(win.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                        }

                        fpsFrames++;
                        if (now - fpsStart >= freq / 2) { fps = fpsFrames / ((now - fpsStart) / (double)freq); fpsFrames = 0; fpsStart = now; }
                        if (now - lastDbg >= freq / 10) { lastDbg = now; debug = new DebugInfo(eng.Debug, fps, fixedMode); }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        string m = ex.GetType().Name + ": " + ex.Message;
                        if (logged.Add(m)) { Action<string> lg = Log; if (lg != null) lg("render: " + ex.ToString()); }
                        if (errors >= 100)
                        {
                            Action<string> f = Fatal;
                            if (f != null) f(m);
                            break;
                        }
                        Thread.Sleep(10);
                        continue;
                    }

                    // Pacing.
                    if (!fixedMode)
                    {
                        long t0 = sw.ElapsedTicks;
                        int hr = Native.DwmFlush();
                        double ms = (sw.ElapsedTicks - t0) * 1000.0 / freq;
                        if (hr != 0) dwmBroken = true;
                        else if (ms < 1.0) { if (++fastDwm >= 30) dwmBroken = true; }
                        else fastDwm = 0;
                        nextTick = sw.ElapsedTicks;
                    }
                    else
                    {
                        double f = (g != null) ? g.Fps : 144;
                        if (f < 30) f = 30; if (f > 360) f = 360;
                        long period = (long)(freq / f);
                        nextTick += period;
                        long n2 = sw.ElapsedTicks;
                        if (n2 - nextTick > period) nextTick = n2 + period; // fell behind: resync, no catch-up burst
                        while (!stopRequested && sw.ElapsedTicks < nextTick) Thread.Sleep(1);
                    }
                }
            }
            catch (Exception ex)
            {
                Action<string> lg = Log;
                if (lg != null) lg("render thread: " + ex.ToString());
                Action<string> f = Fatal;
                if (f != null) f(ex.Message);
            }
            finally
            {
                if (dib != null) { dib.Dispose(); dib = null; }
                rasterKey = null;
                if (win != null && win.Handle != IntPtr.Zero) { try { win.DestroyHandle(); } catch (Exception) { } }
                win = null;
                if (periodSet) Native.timeEndPeriod(1);
            }
        }

        private void Draw(RenderConfig cfg, RingStyle p, MotionEngine eng, byte sca)
        {
            int S = RingRaster.CanvasSize(p);
            double X = cfg.CenterX + eng.OffsetX;
            double Y = cfg.CenterY + eng.OffsetY;
            int winX = (int)Math.Floor(X) - S / 2;
            int winY = (int)Math.Floor(Y) - S / 2;
            double cx = Math.Round((X - winX) * 8.0) / 8.0;
            double cy = Math.Round((Y - winY) * 8.0) / 8.0;

            if (dib == null || dib.W != S)
            {
                if (dib != null) dib.Dispose();
                dib = null;
                dib = new Dib(S, S);
                rasterKey = null;
            }
            string key = p.Key() + "|" + S + "|" + cx.ToString("R") + "|" + cy.ToString("R");
            if (key != rasterKey)
            {
                if (raster == null || raster.Length != S * S) raster = new int[S * S];
                RingRaster.Render(p, S, cx, cy, raster);
                Marshal.Copy(raster, 0, dib.Bits, S * S);
                rasterKey = key;
            }
            dib.Present(win.Handle, winX, winY, sca);
        }
    }
}
