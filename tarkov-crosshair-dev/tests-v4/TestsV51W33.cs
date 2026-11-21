// v5.1 (W33): texture-drop diagnostics. 开镜/丢雷时真正隐藏 (window SW_HIDE'd only with the option on), GPU diagnostic log
// line / PDH instance parsing, graceful failure of the PDH path where Windows is missing, and source checks (no injection /
// hooks / process access). v5.1.1 (X17, Leo 21 Nov): the DirectComposition mode is removed; V51-X17-* check it stays gone
// and that a stored OverlayDCompMode=true still loads and is ignored. Windows behaviour is UNVERIFIED here.
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
            int pk = OverlayDiag.Pack(OverlayWinState.Transparent, true, true, false, true);
            string line = OverlayDiag.Line(4321, true, 5L * 1024 * 1024 * 1024, 300L * 1024 * 1024, 7L * 1024 * 1024 * 1024, 12L * 1024 * 1024, pk);
            bool l1 = OverlayDiag.State(pk) == OverlayWinState.Transparent && line.Contains("game pid 4321") && line.Contains("dedicated 5120 MB")
                && line.Contains("shared 300 MB") && line.Contains("all processes dedicated 7168 MB") && line.Contains("ours dedicated 12 MB")
                && line.Contains("overlay shown-transparent (layered)") && line.Contains("crosshair on") && line.Contains(", ads") && line.Contains("real-hide on")
                && !line.Contains("grenade");
            string l0 = OverlayDiag.Line(0, false, -1, -1, -1, -1, OverlayDiag.Pack(OverlayWinState.Hidden, false, false, false, false));
            bool l2 = l0.Contains("no foreground window") && l0.Contains("all processes dedicated ?") && l0.Contains("overlay hidden (layered)") && l0.Contains("crosshair off");
            bool st = OverlayDiag.StateOf(false, true, false) == OverlayWinState.None && OverlayDiag.StateOf(true, false, true) == OverlayWinState.Hidden
                && OverlayDiag.StateOf(true, true, true) == OverlayWinState.Transparent && OverlayDiag.StateOf(true, true, false) == OverlayWinState.Visible;
            Check("V51-W33-2 GPU log line: foreground pid, dedicated / shared / all / ours MB, overlay state (hidden / shown-transparent / shown-visible, always layered), crosshair / 开镜 / 丢雷 / 真隐藏",
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

            // W33-5 the PDH path fails gracefully where Windows (pdh) is missing
            bool gThrew = false, gOk = false; string gErr = null;
            GpuMemSampler gs = new GpuMemSampler();
            long g1, g2, g3, g4;
            try { gOk = gs.Sample(-2, Process.GetCurrentProcess().Id, out g1, out g2, out g3, out g4); gErr = gs.Error; gs.Sample(1, 1, out g1, out g2, out g3, out g4); }
            catch (Exception) { gThrew = true; }
            gs.Dispose(); gs.Dispose();
            bool gGood = !gThrew && (gOk || !string.IsNullOrEmpty(gErr));
            Check("V51-W33-5 the PDH sampler without pdh.dll: false + reason, never an exception, Dispose twice is safe",
                gGood, "pdh=" + (gOk ? "ok" : gErr));

            // W33-6 per-frame cost of the W33 additions (Pack + StateOf + RealHideNow): far below the 1.5 ms key -> present budget
            int sink = 0;
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 100000; i++)
            {
                bool rh = OverlayDiag.RealHideNow((i & 1) != 0, true, (i & 2) != 0, (i & 4) != 0);
                sink += OverlayDiag.Pack(OverlayDiag.StateOf(true, (i & 8) != 0, rh), true, rh, false, (i & 1) != 0);
            }
            double perFrameUs = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / 100000.0;
            Check("V51-W33-6 per-frame cost of the W33 state work " + perFrameUs.ToString("0.000") + " us (limit 5 us; key -> present budget 1500 us)", perFrameUs < 5 && sink != 1, "");

            // W33-8 the two remaining options: default off, read through W33Opt, copied by Clone (the render thread's copy)
            GlobalSettings gd0 = GlobalSettings.CreateDefault();
            bool defOff = !W33Opt.RealHide(gd0) && !W33Opt.GpuLog(gd0) && !W33Opt.RealHide(null) && !W33Opt.GpuLog(null);
            gd0.AdsRealHide = true; gd0.GpuDiagLog = true;
            GlobalSettings gc = gd0.Clone(false);
            bool cloned = W33Opt.RealHide(gc) && W33Opt.GpuLog(gc);
            Check("V51-W33-8 options AdsRealHide / GpuDiagLog: default off, read by the overlay through W33Opt, kept by Clone", defOff && cloned, "def=" + defOff + " clone=" + cloned);

            // W33-7 source checks
            string ov = W33Src("Overlay.cs"), gd = W33Src("GpuDiag.cs"), od = W33Src("OverlayDiag.cs");
            if (ov == null || gd == null || od == null) { Check("V51-W33-7 sources found", false, "missing source"); return; }
            string all = gd + od;
            bool noInject = !Regex.IsMatch(all + ov, @"OpenProcess|ReadProcessMemory|WriteProcessMemory|SetWindowsHookEx|CreateRemoteThread|VirtualAllocEx|LoadLibrary");
            bool hideOnlyOpt = Regex.IsMatch(ov, @"bool longHidden = WindowShowPolicy\.LongHidden\([^;]*\) \|\| realHide;")
                && Regex.IsMatch(ov, @"OverlayDiag\.RealHideNow\(W33Opt\.RealHide\(g\), localEnabled, eng\.AdsActive, eng\.GrenadeHidden\)");
            bool logWorker = Regex.IsMatch(ov, @"GpuDiagTick\(c, t, wfreq\)") && !Regex.IsMatch(ov, @"new GpuMemSampler\(\)[\s\S]{0,40}Run\(") 
                && ov.IndexOf("gpuSampler.Sample(", StringComparison.Ordinal) > ov.IndexOf("private void GpuDiagTick", StringComparison.Ordinal);
            Check("V51-W33-7 sources: no injection / hooks / process memory; 真隐藏 only joins the off / away rule; PDH only on the worker tick",
                noInject && hideOnlyOpt && logWorker, "noInject=" + noInject + " hide=" + hideOnlyOpt + " worker=" + logWorker);

            V51X17Tests(ov);
        }

        /// <summary>v5.1.1 (X17, Leo 21 Nov): the DirectComposition mode is removed for good.</summary>
        static void V51X17Tests(string ov)
        {
            // X17-1 no DirectComposition code anywhere in the exe source; the 常规 page has no DComp checkbox
            string srcDir = Path.GetDirectoryName(W33Src("Overlay.cs") != null ? FindW33Path("Overlay.cs") : "");
            string bad = "";
            if (srcDir == null || !Directory.Exists(srcDir)) bad = "src dir not found";
            else
            {
                foreach (string f in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
                {
                    string t = File.ReadAllText(f), n = Path.GetFileName(f);
                    if (Regex.IsMatch(t, @"dcomp\.dll|DCompositionCreateDevice|d3d11\.dll|dxgi\.dll|D3D11CreateDevice|CreateSwapChainForComposition|DCompPresenter|WS_EX_NOREDIRECTIONBITMAP|IDCompositionDevice", RegexOptions.IgnoreCase))
                        bad += " " + n;
                    if (n != "Settings.cs" && t.Contains("OverlayDCompMode")) bad += " reads:" + n;
                }
                if (File.Exists(Path.Combine(srcDir, "DCompPresenter.cs"))) bad += " DCompPresenter.cs exists";
            }
            string pg = W33Src("SettingsForm.PageGeneral.cs") ?? "";
            bool page = pg.Length > 0 && !pg.Contains("OverlayDCompMode") && !pg.Contains("新的显示方式") && !pg.Contains("DirectComposition (") && !Regex.IsMatch(pg, @"Check\(""[^""]*DirectComposition")
                && pg.Contains("Check(\"开镜/丢雷时真正隐藏准星窗口\"") && pg.Contains("Check(\"显存诊断日志\"") && pg.Contains("G.AdsRealHide") && pg.Contains("G.GpuDiagLog");
            string texts = (W33Src("HelpText.cs") ?? "") + (W33Src("FaqText.cs") ?? "") + (W33Src("GuideText.cs") ?? "");
            bool txt = texts.Length > 0 && !texts.Contains("DirectComposition") && !texts.Contains("新的显示方式");
            Check("V51-X17-1 DirectComposition removed: no dcomp.dll / DCompositionCreateDevice / d3d11 / dxgi / swap-chain code in src, OverlayDCompMode read nowhere but Settings.cs, 常规 page keeps 真隐藏 + 显存诊断日志 but has no DComp checkbox, help / FAQ / guide do not mention it",
                bad.Length == 0 && page && txt, "bad=" + bad + " page=" + page + " txt=" + txt);

            // X17-2 a settings file with OverlayDCompMode=true still loads, and the overlay draws through the normal layered path
            string j = LeoDefaultJson.Text.Replace("\"Version\": 2", "\"Version\": 2, \"OverlayDCompMode\": true");
            GlobalSettings g; string err = null; System.Collections.Generic.List<string> dropped;
            bool loads = j != LeoDefaultJson.Text && SettingsIO.TryParse(j, out g, out err, out dropped) && g != null && g.OverlayDCompMode;
            bool noOpt = typeof(W33Opt).GetMethod("DComp") == null && Type.GetType("TarkovCrosshair.PresentModeChoice") == null && Type.GetType("TarkovCrosshair.DCompPresenter") == null;
            bool layeredOnly = ov != null && Regex.IsMatch(ov, @"private bool TryCreateWindow\(long now, Action<string> log\)\s*\{\s*try\s*\{\s*hwnd = OverlayWindow\.CreateLayered\(")
                && !ov.Contains("CreateDComp") && Regex.IsMatch(ov, @"bool ok = surfaceCurrent \? Dib\.Move\(hwnd, winX, winY, sca\) : dib\.Present\(hwnd, winX, winY, sca\);")
                && Regex.IsMatch(ov, @"bool ok = blank\.Present\(hwnd, ");
            string line = OverlayDiag.Line(1, true, 0, 0, 0, 0, OverlayDiag.Pack(OverlayWinState.Visible, true, false, false, false));
            bool diag = line.Contains("(layered)") && !line.Contains("dcomp");
            Check("V51-X17-2 settings with OverlayDCompMode=true load (value kept for old files) and are ignored: no DComp option reader, window always created with CreateLayered and drawn with UpdateLayeredWindow (frame + 开镜 all-zero frame)",
                loads && noOpt && layeredOnly && diag, "loads=" + loads + " (" + err + ") noOpt=" + noOpt + " layered=" + layeredOnly + " diag=" + diag);
        }

        static string FindW33Path(string name)
        {
            string here = AppContext.BaseDirectory;
            string[] tries = {
                "/home/dev/tarkov-crosshair-v4/src/" + name,
                Path.Combine(here, "../../../../tarkov-crosshair-v4/src/" + name),
                Path.Combine(here, "../../tarkov-crosshair-v4/src/" + name) };
            foreach (string t in tries) if (File.Exists(t)) return Path.GetFullPath(t);
            return null;
        }
    }
}
