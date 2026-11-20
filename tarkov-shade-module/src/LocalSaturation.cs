// TarkovShade - local saturation through the documented Windows Magnification API
// (Magnification.dll: MagInitialize, MagSetFullscreenColorEffect, MagUninitialize; Windows 8+).
// No GPU vendor API, no screen capture, no other process is touched: the call only asks DWM
// to apply a 5x5 colour matrix to the whole desktop.
// UNVERIFIED on Windows: whether the effect needs UIAccess on some builds, whether it adds a
// frame of latency, how anti-cheat views it, and whether exclusive fullscreen bypasses it
// (expected per docs: it does, DWM composition is bypassed).
using System;
using System.Runtime.InteropServices;

namespace TarkovShade
{
    public interface ILocalSaturationBackend
    {
        bool IsSupported { get; }
        string Status { get; }
        /// <summary>Applies the luma-preserving saturation factor (clamped to 0.5..2.0).</summary>
        bool TrySet(double factor, out string error);
        /// <summary>Puts the identity matrix back.</summary>
        bool TryReset(out string error);
    }

    public static class SaturationMatrix
    {
        public const double RedWeight = 0.2126;   // Rec.709
        public const double GreenWeight = 0.7152;
        public const double BlueWeight = 0.0722;
        public const double MinFactor = 0.5;
        public const double MaxFactor = 2.0;

        /// <summary>Preset Saturation slider (-100..100) to factor: 0 -> 1.0, +100 -> 2.0, -100 -> 0.5.</summary>
        public static double SliderToFactor(int saturation)
        {
            int s = ShadeMath.Clamp(saturation, ShadePreset.SaturationMin, ShadePreset.SaturationMax);
            double f = s >= 0 ? 1.0 + s / 100.0 : 1.0 + s / 200.0;
            return ShadeMath.Clamp(f, MinFactor, MaxFactor);
        }

        /// <summary>
        /// 5x5 row-major matrix for MAGCOLOREFFECT. Windows applies it to a ROW vector
        /// [r g b a 1] * M, so out_j = sum_i in_i * M[i,j].
        /// M[i,j] = w_i * (1 - s) + (i == j ? s : 0) for the RGB block; alpha and the
        /// translation row are identity. Every column sums to 1, so greys stay grey and luma
        /// (with Rec.709 weights) is preserved.
        /// </summary>
        public static float[] Build(double factor)
        {
            double s = double.IsNaN(factor) ? 1.0 : ShadeMath.Clamp(factor, MinFactor, MaxFactor);
            double[] w = { RedWeight, GreenWeight, BlueWeight };
            var m = new float[25];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    m[i * 5 + j] = (float)(w[i] * (1.0 - s) + (i == j ? s : 0.0));
            m[3 * 5 + 3] = 1f;
            m[4 * 5 + 4] = 1f;
            return m;
        }

        public static float[] Identity()
        {
            var m = new float[25];
            for (int i = 0; i < 5; i++) m[i * 5 + i] = 1f;
            return m;
        }

        /// <summary>Row-vector multiply, used by the tests: returns [r', g', b'].</summary>
        public static double[] Apply(float[] m, double r, double g, double b)
        {
            double[] v = { r, g, b, 1.0, 1.0 };
            var o = new double[3];
            for (int j = 0; j < 3; j++)
            {
                double sum = 0.0;
                for (int i = 0; i < 5; i++) sum += v[i] * m[i * 5 + j];
                o[j] = sum;
            }
            return o;
        }
    }

    /// <summary>The three Magnification.dll calls plus a message pump, behind an interface so the
    /// threading rule (A6) can be tested with a fake on Linux.</summary>
    internal interface IMagApi
    {
        bool Initialize(out int lastError);
        bool Uninitialize();
        bool SetEffect(float[] matrix, out int lastError);
        /// <summary>Dispatches any window messages queued for the calling thread.</summary>
        void PumpMessages();
    }

    /// <summary>
    /// Local saturation through MagSetFullscreenColorEffect.
    /// Fix A6: every Magnification call (MagInitialize, MagSetFullscreenColorEffect, MagUninitialize)
    /// runs on ONE dedicated background thread, the same one that called MagInitialize, whichever
    /// thread calls TrySet / TryReset / Dispose. That thread pumps window messages every 50 ms while it
    /// waits for work, and lives until Dispose. UNVERIFIED on Windows: that the Magnification runtime
    /// needs a single thread / a message pump at all, and that a window-less pumping thread is enough
    /// for the full-screen colour effect to stay on.
    /// </summary>
    public sealed class LocalSaturation : ILocalSaturationBackend, IDisposable
    {
        // MAGCOLOREFFECT: float transform[5][5].
        [StructLayout(LayoutKind.Sequential)]
        private struct MagColorEffect
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] Transform;
        }

        private sealed class NativeMagApi : IMagApi
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct Msg
            {
                public IntPtr Hwnd;
                public uint Message;
                public IntPtr WParam;
                public IntPtr LParam;
                public uint Time;
                public int PtX;
                public int PtY;
            }

            // Fix A5: Magnification.dll is not a KnownDLL (UNVERIFIED, believed not), so load it from
            // System32 only; a planted copy next to the exe is never used.
            [DllImport("Magnification.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern bool MagInitialize();

            [DllImport("Magnification.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern bool MagUninitialize();

            [DllImport("Magnification.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern bool MagSetFullscreenColorEffect(ref MagColorEffect effect);

            [DllImport("user32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

            [DllImport("user32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern bool TranslateMessage(ref Msg msg);

            [DllImport("user32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern IntPtr DispatchMessage(ref Msg msg);

            private const uint PmRemove = 0x0001;

            public bool Initialize(out int lastError)
            {
                bool ok = MagInitialize();
                lastError = ok ? 0 : Marshal.GetLastWin32Error();
                return ok;
            }

            public bool Uninitialize()
            {
                return MagUninitialize();
            }

            public bool SetEffect(float[] matrix, out int lastError)
            {
                var effect = new MagColorEffect();
                effect.Transform = matrix;
                bool ok = MagSetFullscreenColorEffect(ref effect);
                lastError = ok ? 0 : Marshal.GetLastWin32Error();
                return ok;
            }

            public void PumpMessages()
            {
                Msg msg;
                int guard = 0;
                while (guard++ < 100 && PeekMessage(out msg, IntPtr.Zero, 0, 0, PmRemove))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
        }

        private const int CallTimeoutMs = 500; // v4.3 (V43-PERF fix 5): was 5000
        private const int PumpIntervalMs = 50;

        private readonly object sync = new object();
        private readonly IMagApi api;
        private readonly bool? supportedOverride;
        private bool initialized;
        private bool disposed;
        private string status = "Not started.";

        // Dedicated Magnification thread.
        private readonly object queueLock = new object();
        private readonly System.Collections.Generic.Queue<Action> queue = new System.Collections.Generic.Queue<Action>();
        private readonly System.Threading.AutoResetEvent wake = new System.Threading.AutoResetEvent(false);
        private System.Threading.Thread magThread;
        private bool stopping;

        public LocalSaturation() : this(new NativeMagApi(), null) { }

        /// <summary>Test hook: inject a fake Magnification API and force IsSupported.</summary>
        internal LocalSaturation(IMagApi api, bool? supportedOverride)
        {
            if (api == null) throw new ArgumentNullException("api");
            this.api = api;
            this.supportedOverride = supportedOverride;
        }

        public bool IsSupported
        {
            get
            {
                if (supportedOverride.HasValue) return supportedOverride.Value;
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
                Version v = Environment.OSVersion.Version;
                return v.Major > 6 || (v.Major == 6 && v.Minor >= 2); // Windows 8 = 6.2
            }
        }

        public string Status { get { return status; } }

        /// <summary>Managed thread id of the Magnification thread, or 0 before the first call (tests).</summary>
        internal int MagThreadId
        {
            get { var t = magThread; return t == null ? 0 : t.ManagedThreadId; }
        }

        private void ThreadLoop()
        {
            while (true)
            {
                Action work = null;
                bool exit = false;
                lock (queueLock)
                {
                    if (queue.Count > 0) work = queue.Dequeue();
                    else if (stopping) exit = true;
                }
                if (exit) return;
                if (work != null) { work(); continue; }
                try { api.PumpMessages(); } catch { }
                wake.WaitOne(PumpIntervalMs);
            }
        }

        /// <summary>Runs fn on the Magnification thread and waits for it. Caller holds sync.</summary>
        private bool RunOnMagThread(Func<string> fn, out string error)
        {
            if (magThread == null)
            {
                var t = new System.Threading.Thread(ThreadLoop);
                t.IsBackground = true;
                t.Name = "TarkovShade Magnification";
                magThread = t;
                t.Start();
            }
            string result = null;
            Exception failure = null;
            var done = new System.Threading.ManualResetEvent(false);
            Action work = delegate
            {
                try { result = fn(); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            };
            if (System.Threading.Thread.CurrentThread == magThread) work();
            else
            {
                lock (queueLock) queue.Enqueue(work);
                wake.Set();
                if (!done.WaitOne(CallTimeoutMs))
                {
                    error = "Magnification thread did not respond within " + CallTimeoutMs + " ms.";
                    return false;
                }
            }
            done.Close();
            if (failure != null)
            {
                error = "Magnification API unavailable: " + failure.Message;
                return false;
            }
            error = result ?? "";
            return error.Length == 0;
        }

        // Runs on the Magnification thread only.
        private string SetOnThread(float[] matrix)
        {
            if (!initialized)
            {
                int initError;
                if (!api.Initialize(out initError))
                {
                    status = "MagInitialize failed (last error " + initError + ", may be unrelated).";
                    return status;
                }
                initialized = true;
                status = "Windows Magnification colour effect - UNVERIFIED on hardware";
            }
            int setError;
            if (!api.SetEffect(matrix, out setError))
                return "MagSetFullscreenColorEffect failed (last error " + setError + ", may be unrelated). " +
                    "Another magnifier or colour filter may be active.";
            return "";
        }

        private bool Set(float[] matrix, out string error)
        {
            lock (sync)
            {
                if (disposed) { error = "Disposed."; return false; }
                if (!IsSupported)
                {
                    error = status = "Not supported: needs Windows 8 or later.";
                    return false;
                }
                bool ok = RunOnMagThread(delegate { return SetOnThread(matrix); }, out error);
                if (!ok && error.StartsWith("Magnification API unavailable", StringComparison.Ordinal)) status = error;
                return ok;
            }
        }

        public bool TrySet(double factor, out string error)
        {
            return Set(SaturationMatrix.Build(factor), out error);
        }

        public bool TryReset(out string error)
        {
            return Set(SaturationMatrix.Identity(), out error);
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (magThread == null) return;
                string ignored;
                RunOnMagThread(delegate
                {
                    if (initialized)
                    {
                        try
                        {
                            int e;
                            api.SetEffect(SaturationMatrix.Identity(), out e);
                            api.Uninitialize();
                        }
                        catch { }
                        initialized = false;
                    }
                    return "";
                }, out ignored);
                lock (queueLock) stopping = true;
                wake.Set();
                if (System.Threading.Thread.CurrentThread != magThread) magThread.Join(2000);
            }
        }
    }
}
