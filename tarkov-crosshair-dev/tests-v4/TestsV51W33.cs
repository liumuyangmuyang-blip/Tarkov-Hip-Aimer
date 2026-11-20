// v5.1 (W33): texture-drop diagnostics. 开镜/丢雷时真正隐藏 (window SW_HIDE'd only with the option on), DirectComposition
// mode choice + fallback latch, GPU diagnostic log line / PDH instance parsing, graceful failure of the DComp and PDH paths
// where Windows is missing, and source checks (no injection / hooks / process access). Windows behaviour is UNVERIFIED here.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W33Init() { V51W33Tests(); }

        static string W33Src(string name)
        {
            string here = AppContext.BaseDirectory;
            string[] tries = {
                "/home/dev/tarkov-crosshair-v4/src/" + name,
                Path.Combine(here, "../../../../tarkov-crosshair-v4/src/" + name),
                Path.Combine(here, "../../tarkov-crosshair-v4/src/" + name) };
            foreach (string t in tries) if (File.Exists(t)) return File.ReadAllText(t);
            return null;
        }

        /// <summary>One frame of Overlay.Run's window logic (FrameVisibility + WindowShowPolicy, as wired in Overlay.cs).</summary>
        sealed class W33Win
        {
            public readonly FrameVisibility Vis = new FrameVisibility();
            public readonly WindowShowPolicy Show = new WindowShowPolicy();
            public int Draws, Blanks, Shows, Hides;
            public void Frame(bool enabled, bool ads, bool grenade, bool realHideOpt)
            {
                bool realHide = OverlayDiag.RealHideNow(realHideOpt, enabled, ads, grenade);
                bool longHidden = WindowShowPolicy.LongHidden(enabled, true, true, false) || realHide;
                if (Show.BeforeDraw(longHidden) == WindowOp.Show) Shows++;
                byte level = (!enabled || ads || grenade) ? (byte)0 : (byte)255;
                FrameAction a = Show.Shown ? Vis.Next(level) : FrameAction.Skip;
                if (a == FrameAction.Draw) Draws++; else if (a == FrameAction.PresentHidden) Blanks++;
                if (Show.AfterDraw(longHidden, Vis.Hidden) == WindowOp.Hide) Hides++;
            }
        }

        static void V51W33Tests()
        {
            // W33-1 PDH instance -> pid
            int p;
            bool a1 = OverlayDiag.ParsePid("pid_1234_luid_0x00000000_0x0000C2B5_phys_0", out p) && p == 1234;
            bool a2 = OverlayDiag.ParsePid("pid_7", out p) && p == 7;
            bool a3 = !OverlayDiag.ParsePid("pid__luid", out p) && !OverlayDiag.ParsePid("luid_0x0_phys_0", out p) && !OverlayDiag.ParsePid(null, out p)
                && !OverlayDiag.ParsePid("pid_12x_luid", out p) && !OverlayDiag.ParsePid("pid_99999999999_luid", out p);
            Check("V51-W33-1 GPU log: PDH instance name pid_N_luid_..._phys_K -> N; bad names rejected", a1 && a2 && a3, a1 + " " + a2 + " " + a3);

            // W33-2 packed state + log line
            int pk = OverlayDiag.Pack(OverlayWinState.Transparent, true, true, true, false, true);
            string line = OverlayDiag.Line(4321, true, 5L * 1024 * 1024 * 1024, 300L * 1024 * 1024, 7L * 1024 * 1024 * 1024, 12L * 1024 * 1024, pk);
            bool l1 = OverlayDiag.State(pk) == OverlayWinState.Transparent && line.Contains("game pid 4321") && line.Contains("dedicated 5120 MB")
                && line.Contains("shared 300 MB") && line.Contains("all processes dedicated 7168 MB") && line.Contains("ours dedicated 12 MB")
                && line.Contains("overlay shown-transparent (dcomp)") && line.Contains("crosshair on") && line.Contains(", ads") && line.Contains("real-hide on")
                && !line.Contains("grenade");
            string l0 = OverlayDiag.Line(0, false, -1, -1, -1, -1, OverlayDiag.Pack(OverlayWinState.Hidden, false, false, false, false, false));
            bool l2 = l0.Contains("no foreground window") && l0.Contains("all processes dedicated ?") && l0.Contains("overlay hidden (layered)") && l0.Contains("crosshair off");
            bool st = OverlayDiag.StateOf(false, true, false) == OverlayWinState.None && OverlayDiag.StateOf(true, false, true) == OverlayWinState.Hidden
                && OverlayDiag.StateOf(true, true, true) == OverlayWinState.Transparent && OverlayDiag.StateOf(true, true, false) == OverlayWinState.Visible;
            Check("V51-W33-2 GPU log line: foreground pid, dedicated / shared / all / ours MB, overlay state (hidden / shown-transparent / shown-visible, layered / dcomp), crosshair / 开镜 / 丢雷 / 真隐藏",
                l1 && l2 && st, line + " | " + l0);

            // W33-3 开镜/丢雷真隐藏: option off = v5.0.2 (never hidden for 开镜, one all-zero frame); on = blank then SW_HIDE, shown again before drawing
            W33Win off = new W33Win(), on = new W33Win();
            for (int i = 0; i < 10; i++) { off.Frame(true, false, false, false); on.Frame(true, false, false, true); }
            for (int i = 0; i < 50; i++) { off.Frame(true, true, false, false); on.Frame(true, true, false, true); }
            bool offAds = off.Hides == 0 && off.Blanks == 1 && off.Show.Shown;
            bool onAds = on.Hides == 1 && on.Blanks == 1 && !on.Show.Shown;
            int onDrawsBefore = on.Draws;
            on.Frame(true, false, false, true); off.Frame(true, false, false, false);
            bool back = on.Shows == 1 && on.Show.Shown && on.Draws == onDrawsBefore + 1; // shown and drawn in the same frame
            for (int i = 0; i < 20; i++) { on.Frame(true, false, true, true); }
            bool gren = on.Hides == 2 && !on.Show.Shown;
            on.Frame(true, false, false, true);
            bool gBack = on.Shows == 2 && on.Show.Shown;
            bool truth = !OverlayDiag.RealHideNow(false, true, true, true) && !OverlayDiag.RealHideNow(true, false, true, false)
                && OverlayDiag.RealHideNow(true, true, true, false) && OverlayDiag.RealHideNow(true, true, false, true) && !OverlayDiag.RealHideNow(true, true, false, false);
            Check("V51-W33-3 开镜/丢雷时真正隐藏: off = window untouched on 开镜 (one all-zero frame, 0 hides); on = all-zero frame then SW_HIDE for 开镜 and 丢雷, shown again and drawn in the release frame",
                offAds && onAds && back && gren && gBack && truth, "off=" + offAds + " on=" + onAds + " back=" + back + " gren=" + gren + " gBack=" + gBack + " truth=" + truth);

            // W33-4 present mode choice: off = layered; on = dcomp; failure latches to layered until the option is switched off and on
            PresentModeChoice m = new PresentModeChoice();
            bool m1 = !m.TryDComp(false) && !m.NeedRecreate(false);
            bool m2 = m.NeedRecreate(true) && m.TryDComp(true);
            m.Created(true, true);
            bool m3 = m.Current && !m.NeedRecreate(true) && m.NeedRecreate(false);
            m.Created(false, false);
            bool m4 = !m.Current && m.NeedRecreate(true);
            m.Created(true, false); // DComp setup failed -> layered
            bool m5 = !m.Current && m.FailedLatch && !m.NeedRecreate(true) && !m.TryDComp(true); // no rebuild every frame
            bool m6 = !m.NeedRecreate(false) && !m.FailedLatch && m.NeedRecreate(true); // off then on: try again
            m.Created(true, true); m.RuntimeFailed();
            bool m7 = m.NeedRecreate(true) && !m.TryDComp(true);
            m.Created(false, false);
            bool m8 = !m.NeedRecreate(true) && m.Failures == 2;
            Check("V51-W33-4 新的显示方式: off = layered window; on = DirectComposition; a failed setup or present falls back to layered and stays there (no rebuild loop) until switched off and on",
                m1 && m2 && m3 && m4 && m5 && m6 && m7 && m8, m1 + " " + m2 + " " + m3 + " " + m4 + " " + m5 + " " + m6 + " " + m7 + " " + m8);

            // W33-5 DComp / PDH paths fail gracefully where Windows (d3d11 / dcomp / pdh) is missing or no window is given
            bool dOk; string dErr = null; bool dThrew = false;
            DCompPresenter d = new DCompPresenter();
            try { dOk = d.Init(IntPtr.Zero, 64); dErr = d.LastError; } catch (Exception) { dOk = false; dThrew = true; }
            bool dGood = !dThrew && !dOk && !d.Ready && !string.IsNullOrEmpty(dErr) && !d.Present(IntPtr.Zero, 64, 0, 0);
            d.Dispose(); d.Dispose();
            bool gThrew = false, gOk = false; string gErr = null;
            GpuMemSampler gs = new GpuMemSampler();
            long g1, g2, g3, g4;
            try { gOk = gs.Sample(-2, Process.GetCurrentProcess().Id, out g1, out g2, out g3, out g4); gErr = gs.Error; gs.Sample(1, 1, out g1, out g2, out g3, out g4); }
            catch (Exception) { gThrew = true; }
            gs.Dispose(); gs.Dispose();
            bool gGood = !gThrew && (gOk || !string.IsNullOrEmpty(gErr));
            Check("V51-W33-5 DirectComposition setup with no window / no d3d11 and the PDH sampler without pdh.dll: false + reason, never an exception, Dispose twice is safe",
                dGood && gGood, "dcomp=" + dErr + " pdh=" + (gOk ? "ok" : gErr));

            // W33-6 per-frame cost of the W33 additions (Pack + StateOf + RealHideNow): far below the 1.5 ms key -> present budget
            int sink = 0;
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 100000; i++)
            {
                bool rh = OverlayDiag.RealHideNow((i & 1) != 0, true, (i & 2) != 0, (i & 4) != 0);
                sink += OverlayDiag.Pack(OverlayDiag.StateOf(true, (i & 8) != 0, rh), (i & 16) != 0, true, rh, false, (i & 1) != 0);
            }
            double perFrameUs = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / 100000.0;
            Check("V51-W33-6 per-frame cost of the W33 state work " + perFrameUs.ToString("0.000") + " us (limit 5 us; key -> present budget 1500 us)", perFrameUs < 5 && sink != 1, "");

            // W33-8 the three options: default off, read through W33Opt, copied by Clone (the render thread's copy)
            GlobalSettings gd0 = GlobalSettings.CreateDefault();
            bool defOff = !W33Opt.DComp(gd0) && !W33Opt.RealHide(gd0) && !W33Opt.GpuLog(gd0) && !W33Opt.DComp(null) && !W33Opt.RealHide(null) && !W33Opt.GpuLog(null);
            gd0.OverlayDCompMode = true; gd0.AdsRealHide = true; gd0.GpuDiagLog = true;
            GlobalSettings gc = gd0.Clone(false);
            bool cloned = W33Opt.DComp(gc) && W33Opt.RealHide(gc) && W33Opt.GpuLog(gc);
            Check("V51-W33-8 options OverlayDCompMode / AdsRealHide / GpuDiagLog: default off, read by the overlay through W33Opt, kept by Clone", defOff && cloned, "def=" + defOff + " clone=" + cloned);

            // W33-7 source checks
            string ov = W33Src("Overlay.cs"), dc = W33Src("DCompPresenter.cs"), gd = W33Src("GpuDiag.cs"), od = W33Src("OverlayDiag.cs");
            if (ov == null || dc == null || gd == null || od == null) { Check("V51-W33-7 sources found", false, "missing source"); return; }
            string all = dc + gd + od;
            bool noInject = !Regex.IsMatch(all + ov, @"OpenProcess|ReadProcessMemory|WriteProcessMemory|SetWindowsHookEx|CreateRemoteThread|VirtualAllocEx|LoadLibrary");
            bool fallback = ov.Contains("DirectComposition mode failed (") && ov.Contains("using the normal layered window")
                && ov.Contains("DirectComposition present failed (") && Regex.IsMatch(ov, @"presentMode\.TryDComp\(wantDComp\)")
                && Regex.IsMatch(ov, @"presentMode\.NeedRecreate\(W33Opt\.DComp\(g\)\)");
            bool slots = dc.Contains("SlotVisualSetContent = 15") && dc.Contains("SlotFactory2CreateSwapChainForComposition = 24")
                && dc.Contains("SlotCtxUpdateSubresource = 48") && dc.Contains("DXGI_ALPHA_MODE_PREMULTIPLIED") && dc.Contains("CreateSwapChainForComposition")
                && ov.Contains("Native.WS_EX_NOREDIRECTIONBITMAP");
            bool hideOnlyOpt = Regex.IsMatch(ov, @"bool longHidden = WindowShowPolicy\.LongHidden\([^;]*\) \|\| realHide;")
                && Regex.IsMatch(ov, @"OverlayDiag\.RealHideNow\(W33Opt\.RealHide\(g\), localEnabled, eng\.AdsActive, eng\.GrenadeHidden\)");
            bool logWorker = Regex.IsMatch(ov, @"GpuDiagTick\(c, t, wfreq\)") && !Regex.IsMatch(ov, @"new GpuMemSampler\(\)[\s\S]{0,40}Run\(") 
                && ov.IndexOf("gpuSampler.Sample(", StringComparison.Ordinal) > ov.IndexOf("private void GpuDiagTick", StringComparison.Ordinal);
            Check("V51-W33-7 sources: no injection / hooks / process memory; DirectComposition falls back to the layered window (logged); vtable slots; 真隐藏 only joins the off / away rule; PDH only on the worker tick",
                noInject && fallback && slots && hideOnlyOpt && logWorker, "noInject=" + noInject + " fallback=" + fallback + " slots=" + slots + " hide=" + hideOnlyOpt + " worker=" + logWorker);
        }
    }
}
