// v4.3 (W2) Leo's alt+tab freeze + "no visible change": FilterCore (all engine calls on one worker, coalesced, never waited
// for on the UI thread) and LenientGammaBackend (ShadePilot's write / read-back rule), src/FilterCore.cs + src/FilterGamma.cs.
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
        /// <summary>Fake display: every call sleeps DelayMs; can refuse writes or report a drifted read-back.</summary>
        sealed class SlowGamma : IGammaBackend
        {
            public int DelayMs, Gets, Sets;
            public bool RefuseSets;
            public int DriftRed;           // added to every red entry on read-back (driver LUT rounding)
            public GammaRamp Hw = Identity256();
            readonly object sync = new object();
            public bool TryGet(string d, out GammaRamp r, out string e)
            {
                Thread.Sleep(DelayMs); e = "";
                lock (sync)
                {
                    Gets++;
                    r = Hw.Clone();
                    for (int i = 0; i < 256; i++) r.Red[i] = (ushort)Math.Max(0, Math.Min(65535, r.Red[i] + DriftRed));
                }
                return true;
            }
            public bool TrySet(string d, GammaRamp r, out string e)
            {
                Thread.Sleep(DelayMs);
                lock (sync)
                {
                    Sets++;
                    if (RefuseSets) { e = "SetDeviceGammaRamp refused 3 times on " + d + ", Win32 87."; return false; }
                    Hw = r.Clone(); e = ""; return true;
                }
            }
        }

        static GammaRamp Identity256()
        {
            GammaRamp g = GammaRamp.CreateEmpty();
            for (int i = 0; i < 256; i++) { g.Red[i] = g.Green[i] = g.Blue[i] = (ushort)(i * 257); }
            return g;
        }

        static string PerfFolder()
        {
            string b = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            string d = Path.Combine(string.IsNullOrEmpty(b) ? Path.GetTempPath() : b, "w2perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static List<DisplayTarget> OneDisplay()
        {
            DisplayTarget t = new DisplayTarget(); t.DeviceName = @"\\.\DISPLAY1"; t.FriendlyName = "DISPLAY1"; t.Primary = true;
            return new List<DisplayTarget> { t };
        }

        /// <summary>A FilterCore whose "UI thread" is this test thread: posted actions wait in uiQueue until Pump runs them.</summary>
        sealed class CoreRig
        {
            public readonly Queue<Action> Ui = new Queue<Action>();
            public readonly SlowGamma Gamma = new SlowGamma();
            public FilterCore Core;
            public readonly List<string> Log = new List<string>();
            public string LogText() { lock (Log) return string.Join(" | ", Log.ToArray()); }
            public CoreRig(int delay, bool lenient)
            {
                Gamma.DelayMs = delay;
                string f = PerfFolder();
                IGammaBackend gb = lenient ? (IGammaBackend)new LenientGammaBackend(Gamma) : Gamma;
                Core = new FilterCore(delegate { return new ShadeEngine(f, gb, null, null, OneDisplay); },
                    delegate(Action a) { lock (Ui) Ui.Enqueue(a); }, delegate(string m) { lock (Log) Log.Add(m); });
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
            /// <summary>Run UI posts until the worker queue is empty and idle for 'quietMs' (max timeoutMs).</summary>
            public void Settle(int quietMs, int timeoutMs)
            {
                Stopwatch sw = Stopwatch.StartNew();
                int lastJobs = -1; long since = 0;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    Pump();
                    int j = Core.JobsRun;
                    if (j != lastJobs || Core.Pending > 0) { lastJobs = j; since = sw.ElapsedMilliseconds; }
                    else if (sw.ElapsedMilliseconds - since >= quietMs) break;
                    Thread.Sleep(10);
                }
                Pump();
            }
        }

        static void V43PerfTests()
        {
            // P1 slow display (500 ms per gamma call): no UI-thread entry point waits for the worker.
            {
                CoreRig r = new CoreRig(500, true);
                Stopwatch sw = Stopwatch.StartNew();
                long worst = 0; string worstName = "";
                Action<string, Action> time = delegate(string n, Action a)
                {
                    long t0 = sw.ElapsedMilliseconds; a(); long t = sw.ElapsedMilliseconds - t0;
                    if (t > worst) { worst = t; worstName = n; }
                };
                time("Start", delegate { r.Core.Start(true); });
                time("SetOn", delegate { r.Core.SetOn(true); });
                for (int i = 0; i < 40; i++)
                {
                    int v = i;
                    time("SetStyle", delegate { PilotStyle s = r.Core.Style; s.Shadows = 60 + v; r.Core.SetStyle(s); });
                }
                for (int i = 0; i < 20; i++) { string sig = "s" + (i % 3); time("DisplaysChanged", delegate { r.Core.DisplaysChanged(sig); }); }
                time("Hold", delegate { r.Core.Hold(true); });
                time("Hold", delegate { r.Core.Hold(false); });
                time("SelectPreset", delegate { r.Core.SelectPreset(PilotBuiltIns.NightName); });
                time("NextPreset", delegate { r.Core.NextPreset(); });
                time("Toggle", delegate { r.Core.Toggle(); });
                time("RestoreAll", delegate { r.Core.RestoreAll(); });
                time("SaveAs", delegate { r.Core.SaveAs("mine", null); });
                time("ExportCode", delegate { r.Core.ExportCode(null); });
                time("ImportCode", delegate { r.Core.ImportCode("nope", null); });
                time("RefreshMonitors", delegate { r.Core.RefreshMonitors(); });
                time("reads", delegate { bool a = r.Core.IsOn; string b = r.Core.PresetName; PilotStyle c = r.Core.Style; List<string> d = r.Core.PresetNames(); });
                long total = sw.ElapsedMilliseconds;
                Check("V43-P1 500 ms per gamma call: every UI-thread FilterCore entry point returns at once (worst " + worstName + ")",
                    worst < 50 && total < 300, "worst=" + worst + " ms total=" + total + " ms");
                r.Core.Shutdown();
                bool joined = r.Core.Join(60000);
                r.Pump();
                Check("V43-P1b Shutdown queues the restore; Join (Main, after the message loop) finishes it", joined, "");
            }

            // P2 coalescing: 40 slider edits and 20 display events end as a handful of engine calls, and the last value wins.
            {
                CoreRig r = new CoreRig(40, true);
                r.Core.Start(true);
                r.Settle(200, 20000);
                int sets0 = r.Gamma.Sets;
                for (int i = 0; i < 40; i++) { PilotStyle s = r.Core.Style; s.Shadows = 41 + i; r.Core.SetStyle(s); }
                for (int i = 0; i < 20; i++) r.Core.DisplaysChanged("same");
                r.Settle(800, 30000);
                int styleSets = r.Gamma.Sets - sets0;
                bool ok = r.Core.Snapshot.Style != null && r.Core.Snapshot.Style.Shadows == 80 && r.Core.IsOn && styleSets <= 6;
                Check("V43-P2 40 slider edits + 20 display events -> few gamma writes, last value applied", ok,
                    "writes=" + styleSets + " shadows=" + (r.Core.Snapshot.Style == null ? -1 : r.Core.Snapshot.Style.Shadows));
                r.Core.Shutdown(); r.Core.Join(20000);
            }

            // P3 a refused write is reported in Chinese with the Windows error number, and display events do not retry it
            // more than once per 5 s.
            {
                CoreRig r = new CoreRig(5, true);
                r.Gamma.RefuseSets = true;
                r.Core.Start(false);
                r.Settle(100, 10000);
                r.Core.SetOn(true);
                r.Settle(150, 10000);
                string p = r.Core.Problem;
                bool ok = p.Contains("拒绝") && p.Contains("87") && !r.Core.IsOn;
                int sets = r.Gamma.Sets;
                for (int i = 0; i < 30; i++) { r.Core.DisplaysChanged("x" + i); Thread.Sleep(20); r.Pump(); }
                r.Settle(400, 10000);
                ok = ok && r.Gamma.Sets - sets == 0; // filter is off after the refusal: display events never write
                Check("V43-P3 refused gamma write -> Chinese reason with Win32 87, filter off, display events do not retry", ok,
                    "problem=" + p + " extraWrites=" + (r.Gamma.Sets - sets));
                r.Core.Shutdown(); r.Core.Join(10000);
            }

            // P4 display events while ON: at most one re-apply per 5 s, the rest coalesced (RefreshMinGapMs).
            {
                CoreRig r = new CoreRig(5, true);
                r.Core.Start(true);
                r.Settle(150, 10000);
                r.Core.DebounceMs = 20; r.Core.RefreshMinGapMs = 1000;
                int gets0 = r.Gamma.Gets;
                Stopwatch sw = Stopwatch.StartNew();
                int n = 0;
                while (sw.ElapsedMilliseconds < 1500) { r.Core.DisplaysChanged("sig" + (n++)); Thread.Sleep(15); r.Pump(); }
                r.Settle(300, 5000);
                int reads = r.Gamma.Gets - gets0;
                // one RefreshDisplays reads each target a few times; 1.5 s with a 1 s gap = at most 2-3 refreshes
                Check("V43-P4 " + n + " display events in 1.5 s with a 1 s gap -> at most 3 re-applies (Leo: 5 s gap)", reads <= 3 * 4 && r.Core.IsOn,
                    "gamma reads=" + reads);
                r.Core.Shutdown(); r.Core.Join(10000);
            }

            // P5 ShadePilot read-back rule: a driver that rounds the LUT (red off by 400) is accepted; a curve that was replaced
            // (red off by 3000) is not; module's strict compare would have refused the first one.
            {
                GammaRamp w = Identity256();
                for (int i = 0; i < 256; i++) w.Red[i] = (ushort)Math.Min(65535, i * 300);
                GammaRamp near = w.Clone(), far = w.Clone(), half = w.Clone();
                for (int i = 0; i < 256; i++) { near.Red[i] = (ushort)Math.Max(0, near.Red[i] - 400); far.Red[i] = (ushort)Math.Max(0, Math.Min(65535, far.Red[i] + 3000)); }
                for (int i = 0; i < 256; i += 32) half.Red[i] = (ushort)Math.Min(65535, half.Red[i] + 2000); // 8 of 16 samples off
                bool ok = LenientGammaBackend.PilotAccepts(near, w) && !LenientGammaBackend.PilotAccepts(far, w) && LenientGammaBackend.PilotAccepts(half, w)
                    && !GammaRampController.RampsClose(near, w, GammaRampController.VerifyTolerance);
                SlowGamma g = new SlowGamma(); g.DriftRed = -400;
                LenientGammaBackend lb = new LenientGammaBackend(g);
                string e; GammaRamp back;
                ok = ok && lb.TrySet("D", w, out e) && lb.TryGet("D", out back, out e) && GammaRampController.RampsClose(back, w, 0);
                g.DriftRed = 5000;
                ok = ok && lb.TryGet("D", out back, out e) && !GammaRampController.RampsClose(back, w, GammaRampController.VerifyTolerance);
                Check("V43-P5 read-back = ShadePilot rule (red, 16 samples, > 512 off, refused only if > 8 differ)", ok, "");
            }

            // P6 an engine on a rounding driver (red read-back off by 400): with LenientGammaBackend the filter turns on;
            // on the bare backend (module's strict check) it is refused -- Leo's "no visible change".
            {
                CoreRig a = new CoreRig(0, true); a.Gamma.DriftRed = -400;
                a.Core.Start(true); a.Settle(150, 10000);
                CoreRig b = new CoreRig(0, false); b.Gamma.DriftRed = -400;
                b.Core.Start(true); b.Settle(150, 10000);
                Check("V43-P6 rounding driver: on with the ShadePilot rule, refused by the strict module check", a.Core.IsOn && !b.Core.IsOn,
                    "lenient=" + a.Core.IsOn + " strict=" + b.Core.IsOn + " strictProblem=" + b.Core.Problem + " logA=" + a.LogText() + " logB=" + b.LogText());
                a.Core.Shutdown(); b.Core.Shutdown(); a.Core.Join(5000); b.Core.Join(5000);
            }

            // P7 GammaProblem: plain Chinese for each engine failure, with the Win32 number when there is one.
            {
                string a = FilterCore.GammaProblem("Applying the gamma curve failed: SetDeviceGammaRamp refused 3 times on X, Win32 1450.");
                string b = FilterCore.GammaProblem("Windows did not apply the gamma curve on X (read-back differs). ...");
                string c = FilterCore.GammaProblem("");
                bool ok = a.Contains("拒绝") && a.Contains("1450") && b.Contains("读回来的不一样") && c.Contains("没有给出原因");
                Check("V43-P7 gamma failures in plain Chinese with the Windows error code", ok, a);
            }
        }
    }
}
