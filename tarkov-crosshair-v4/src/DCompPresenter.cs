using System;
using System.Runtime.InteropServices;

namespace TarkovCrosshair
{
    /// <summary>
    /// v5.1 (W33, texture-drop investigation): an alternative present path for the overlay window, default OFF
    /// (GlobalSettings.OverlayDCompMode). Instead of UpdateLayeredWindow (a GDI bitmap DWM copies into its own redirection
    /// surface), the window has no redirection bitmap (WS_EX_NOREDIRECTIONBITMAP) and its content is a DXGI flip-model swap
    /// chain made for composition (premultiplied alpha, B8G8R8A8) attached through DirectComposition (target -> visual ->
    /// swap chain). This is the way out-of-process overlays are built when they want DWM to be able to put them on a hardware
    /// overlay plane (MPO) instead of composing the whole desktop; whether that keeps the game in independent flip on a given
    /// PC is UNVERIFIED (see V51-W33.md). No injection, no hooks, nothing of the game is touched.
    /// COM calls go through the vtables (C# 5 / .NET Framework, no interop assembly). Slots are from the SDK headers;
    /// IDCompositionVisual::SetContent = slot 15 (independent of the SetOffsetX overload order, win32metadata issue 600).
    /// Every call is checked; any failure returns false with LastError set, and the caller falls back to the layered path.
    /// Render thread only.
    /// </summary>
    internal sealed class DCompPresenter : IDisposable
    {
        // ---- P/Invoke
        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
            uint numLevels, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("dcomp.dll", ExactSpelling = true)]
        private static extern int DCompositionCreateDevice(IntPtr dxgiDevice, ref Guid iid, out IntPtr dcompDevice);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private const int D3D_DRIVER_TYPE_HARDWARE = 1;
        private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        private const uint D3D11_SDK_VERSION = 7;
        private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        private const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
        private const uint DXGI_SCALING_STRETCH = 0;
        private const uint DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL = 3;
        private const uint DXGI_ALPHA_MODE_PREMULTIPLIED = 1;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        private static Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static Guid IID_IDXGIFactory2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
        private static Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private static Guid IID_IDCompositionDevice = new Guid("c37ea93a-e7aa-450d-b16f-9746cb0407f3");

        [StructLayout(LayoutKind.Sequential)]
        private struct DXGI_SWAP_CHAIN_DESC1
        {
            public uint Width, Height, Format;
            public int Stereo;
            public uint SampleCount, SampleQuality, BufferUsage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags;
        }

        // ---- vtable delegates (stdcall; 'this' first)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QIFn(IntPtr self, ref Guid iid, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutPtrFn(IntPtr self, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetParentFn(IntPtr self, ref Guid iid, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateForCompFn(IntPtr self, IntPtr device, ref DXGI_SWAP_CHAIN_DESC1 desc, IntPtr restrict, out IntPtr swap);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PresentFn(IntPtr self, uint sync, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBufferFn(IntPtr self, uint index, ref Guid iid, out IntPtr surface);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResizeBuffersFn(IntPtr self, uint count, uint w, uint h, uint format, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UpdateSubresourceFn(IntPtr self, IntPtr dst, uint sub, IntPtr box, IntPtr src, uint rowPitch, uint depthPitch);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int TargetForHwndFn(IntPtr self, IntPtr hwnd, int topmost, out IntPtr target);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetPtrFn(IntPtr self, IntPtr p);

        /// <summary>Vtable slots used (SDK header order). Exposed for the source checks in the tests.</summary>
        internal const int SlotQI = 0, SlotRelease = 2, SlotDxgiGetParent = 6, SlotDxgiDeviceGetAdapter = 7,
            SlotFactory2CreateSwapChainForComposition = 24, SlotSwapPresent = 8, SlotSwapGetBuffer = 9, SlotSwapResizeBuffers = 13,
            SlotCtxUpdateSubresource = 48, SlotDCompCommit = 3, SlotDCompCreateTargetForHwnd = 6, SlotDCompCreateVisual = 7,
            SlotTargetSetRoot = 3, SlotVisualSetContent = 15;

        private static T Fn<T>(IntPtr com, int slot) where T : class
        {
            IntPtr vtbl = Marshal.ReadIntPtr(com);
            IntPtr f = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(f, typeof(T));
        }

        private static void Rel(ref IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            try { Fn<ReleaseFn>(p, SlotRelease)(p); } catch (Exception) { }
            p = IntPtr.Zero;
        }

        // ---- state
        private IntPtr device, context, dxgiDevice, swap, dcompDevice, target, visual;
        private PresentFn swapPresent;
        private GetBufferFn swapGetBuffer;
        private ResizeBuffersFn swapResize;
        private UpdateSubresourceFn ctxUpdate;
        private NoArgFn commit;
        private IntPtr hwnd;
        private int size, lastX = int.MinValue, lastY = int.MinValue;

        /// <summary>Why the last call failed (null = no failure yet).</summary>
        public string LastError;
        /// <summary>Diagnostics: presents done.</summary>
        public int Presents;
        public bool Ready { get { return swap != IntPtr.Zero && visual != IntPtr.Zero; } }

        private bool Fail(string what, int hr)
        {
            LastError = what + " failed, HRESULT 0x" + hr.ToString("X8");
            return false;
        }

        /// <summary>Builds D3D11 device -> composition swap chain (size x size) -> DirectComposition target on hwnd.
        /// false (LastError set, everything released) on any failure, including a missing d3d11 / dcomp DLL.</summary>
        public bool Init(IntPtr window, int initialSize)
        {
            try
            {
                hwnd = window;
                int s = initialSize < 1 ? 1 : initialSize;
                int level;
                int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0,
                    D3D11_SDK_VERSION, out device, out level, out context);
                if (hr < 0 || device == IntPtr.Zero || context == IntPtr.Zero) { Dispose(); return Fail("D3D11CreateDevice", hr); }
                hr = Fn<QIFn>(device, SlotQI)(device, ref IID_IDXGIDevice, out dxgiDevice);
                if (hr < 0) { Dispose(); return Fail("QueryInterface(IDXGIDevice)", hr); }
                IntPtr adapter, factory;
                hr = Fn<OutPtrFn>(dxgiDevice, SlotDxgiDeviceGetAdapter)(dxgiDevice, out adapter);
                if (hr < 0) { Dispose(); return Fail("IDXGIDevice::GetAdapter", hr); }
                hr = Fn<GetParentFn>(adapter, SlotDxgiGetParent)(adapter, ref IID_IDXGIFactory2, out factory);
                Rel(ref adapter);
                if (hr < 0) { Dispose(); return Fail("IDXGIAdapter::GetParent(IDXGIFactory2)", hr); }
                DXGI_SWAP_CHAIN_DESC1 d = new DXGI_SWAP_CHAIN_DESC1();
                d.Width = (uint)s; d.Height = (uint)s; d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleCount = 1;
                d.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; d.BufferCount = 2; d.Scaling = DXGI_SCALING_STRETCH;
                d.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; d.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;
                hr = Fn<CreateForCompFn>(factory, SlotFactory2CreateSwapChainForComposition)(factory, device, ref d, IntPtr.Zero, out swap);
                Rel(ref factory);
                if (hr < 0 || swap == IntPtr.Zero) { Dispose(); return Fail("IDXGIFactory2::CreateSwapChainForComposition", hr); }
                swapPresent = Fn<PresentFn>(swap, SlotSwapPresent);
                swapGetBuffer = Fn<GetBufferFn>(swap, SlotSwapGetBuffer);
                swapResize = Fn<ResizeBuffersFn>(swap, SlotSwapResizeBuffers);
                ctxUpdate = Fn<UpdateSubresourceFn>(context, SlotCtxUpdateSubresource);

                hr = DCompositionCreateDevice(dxgiDevice, ref IID_IDCompositionDevice, out dcompDevice);
                if (hr < 0 || dcompDevice == IntPtr.Zero) { Dispose(); return Fail("DCompositionCreateDevice", hr); }
                commit = Fn<NoArgFn>(dcompDevice, SlotDCompCommit);
                hr = Fn<TargetForHwndFn>(dcompDevice, SlotDCompCreateTargetForHwnd)(dcompDevice, hwnd, 1, out target);
                if (hr < 0) { Dispose(); return Fail("IDCompositionDevice::CreateTargetForHwnd", hr); }
                hr = Fn<OutPtrFn>(dcompDevice, SlotDCompCreateVisual)(dcompDevice, out visual);
                if (hr < 0) { Dispose(); return Fail("IDCompositionDevice::CreateVisual", hr); }
                hr = Fn<SetPtrFn>(visual, SlotVisualSetContent)(visual, swap);
                if (hr < 0) { Dispose(); return Fail("IDCompositionVisual::SetContent", hr); }
                hr = Fn<SetPtrFn>(target, SlotTargetSetRoot)(target, visual);
                if (hr < 0) { Dispose(); return Fail("IDCompositionTarget::SetRoot", hr); }
                hr = commit(dcompDevice);
                if (hr < 0) { Dispose(); return Fail("IDCompositionDevice::Commit", hr); }
                size = s;
                return true;
            }
            catch (Exception ex)
            {
                Dispose();
                LastError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Uploads S x S premultiplied BGRA pixels (top-down, the Dib layout) and presents them at (x, y). The window is
        /// resized / moved with it (one SetWindowPos only when its place or size changed).</summary>
        public bool Present(IntPtr bits, int s, int x, int y)
        {
            if (!Ready || bits == IntPtr.Zero) { LastError = LastError ?? "not ready"; return false; }
            try
            {
                int hr;
                if (s != size)
                {
                    hr = swapResize(swap, 0, (uint)s, (uint)s, 0, 0);
                    if (hr < 0) return Fail("IDXGISwapChain::ResizeBuffers", hr);
                    size = s;
                    if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, s, s, SWP_NOZORDER | SWP_NOACTIVATE)) { LastError = "SetWindowPos failed, Win32 error " + Marshal.GetLastWin32Error(); return false; }
                    lastX = x; lastY = y;
                }
                IntPtr tex;
                hr = swapGetBuffer(swap, 0, ref IID_ID3D11Texture2D, out tex);
                if (hr < 0 || tex == IntPtr.Zero) return Fail("IDXGISwapChain::GetBuffer", hr);
                ctxUpdate(context, tex, 0, IntPtr.Zero, bits, (uint)(s * 4), 0);
                Rel(ref tex);
                hr = swapPresent(swap, 0, 0);
                if (hr < 0) return Fail("IDXGISwapChain::Present", hr);
                Presents++;
                return Move(x, y);
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>Moves the window only (the swap chain keeps its pixels).</summary>
        public bool Move(int x, int y)
        {
            if (x == lastX && y == lastY) return true;
            if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, size, size, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE))
            {
                LastError = "SetWindowPos failed, Win32 error " + Marshal.GetLastWin32Error();
                return false;
            }
            lastX = x; lastY = y;
            return true;
        }

        public void Dispose()
        {
            Rel(ref visual); Rel(ref target); Rel(ref dcompDevice); Rel(ref swap); Rel(ref dxgiDevice); Rel(ref context); Rel(ref device);
            swapPresent = null; swapGetBuffer = null; swapResize = null; ctxUpdate = null; commit = null;
            size = 0; lastX = lastY = int.MinValue;
        }
    }
}
