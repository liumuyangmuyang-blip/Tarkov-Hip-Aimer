// v5.0.2 (W15) 滤镜: restore after slow / failing drivers and monitors (BRUTE P1-1, P1-2), damaged recovery file (P2-1),
// monotone 调试滤镜 (P2-3), gamma rollback refused (P3-1), lenient read-back on all channels (P3-2), SerialWorker race (AUDIT F10),
// no repeated driver calls (F9), worker priority / zero idle wakeups / unchanged values not re-applied (Leo, 18 Nov), exit order,
// UI-C H2 / H3 / M1 / M2 / M8 / F5 wording (source checks). Uses worker threads: runs from Main (not a module initializer).
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
        // ------------------------------------------------------------ fakes

        sealed class W15Vib : IVibranceBackend
        {
            public readonly object Sync = new object();
            public int Level = 30, Default = 0, SetMs, Gets, Sets;
            public string Vendor { get { return "NVIDIA"; } }
            public bool IsSupported { get { return true; } }
            public string Status { get { return "ok"; } }
            public bool TryGet(string d, out VibranceInfo info, out string error)
            {
                info = new VibranceInfo();
                lock (Sync) { Gets++; info.Current = Level; info.Min = 0; info.Max = 63; info.Default = Default; }
                error = "";
                return true;
            }
            public bool TrySet(string d, int level, out string error)
            {
                int ms; lock (Sync) { ms = SetMs; Sets++; }
                if (ms > 0) Thread.Sleep(ms);
                lock (Sync) Level = level;
                error = "";
                return true;
            }
        }

        sealed class W15Ddc : IDdcBackend
        {
            public readonly object Sync = new object();
            public uint Bright = 70;
            public int SetMs, FailReadsAfterSet, Enumerates;
            public bool FailEnumerate;
            public List<DdcMonitorHandle> Enumerate(out string error)
            {
                lock (Sync) Enumerates++;
                if (FailEnumerate) { error = "EnumDisplayMonitors failed (Win32 5)"; return new List<DdcMonitorHandle>(); }
                error = "";
                DdcMonitorHandle h = new DdcMonitorHandle();
                h.Handle = new IntPtr(1); h.Name = "Fake"; h.DisplayDeviceName = @"\\.\DISPLAY1"; h.Id = @"\\.\DISPLAY1|physical:0";
                return new List<DdcMonitorHandle> { h };
            }
            public bool GetCapabilities(IntPtr m, out uint caps) { caps = 0x2; return true; }
            public bool GetBrightness(IntPtr m, out uint a, out uint b, out uint c) { a = 0; lock (Sync) b = Bright; c = 100; return true; }
            public bool SetBrightness(IntPtr m, uint v) { lock (Sync) Bright = v; return true; }
            public bool GetContrast(IntPtr m, out uint a, out uint b, out uint c) { a = b = c = 0; return false; }
            public bool SetContrast(IntPtr m, uint v) { return false; }
            public bool GetVcp(IntPtr m, byte code, out bool sp, out uint cur, out uint max)
            {
                sp = true; cur = 0; max = 0;
                if (code != DdcCiController.VcpBrightness) return false;
                lock (Sync)
                {
                    if (FailReadsAfterSet > 0) { FailReadsAfterSet--; return false; }
                    cur = Bright; max = 100;
                }
                return true;
            }
            public bool SetVcp(IntPtr m, byte code, uint v)
            {
                if (code != DdcCiController.VcpBrightness) return false;
                int ms; lock (Sync) ms = SetMs;
                if (ms > 0) Thread.Sleep(ms);
                lock (Sync) Bright = v;
                return true;
            }
            public void Destroy(IntPtr m) { }
        }

        /// <summary>Gamma fake for P3-1: next get after a set fails, next set refused, on request.</summary>
        sealed class W15Gamma : IGammaBackend
        {
            public GammaRamp Hw = Identity256();
            public int FailGetsAfterNextSet, RefuseSetsAfter = -1, Sets;
            int pendingFailGets;
            public bool TryGet(string d, out GammaRamp r, out string e)
            {
                e = ""; r = Hw.Clone();
                if (pendingFailGets > 0) { pendingFailGets--; e = "read failed"; return false; }
                return true;
            }
            public bool TrySet(string d, GammaRamp r, out string e)
            {
                e = "";
                Sets++;
                if (RefuseSetsAfter >= 0 && Sets > RefuseSetsAfter) { e = "SetDeviceGammaRamp refused"; return false; }
                Hw = r.Clone();
                if (FailGetsAfterNextSet > 0) { pendingFailGets = FailGetsAfterNextSet; FailGetsAfterNextSet = 0; }
                return true;
            }
        }

        static PilotStyle W15Style(bool vib, bool bright)
        {
            PilotStyle s = PilotBuiltIns.General();
            s.ApplySaturation = vib;
            s.SaturationBackend = PilotStyle.SaturationBackendDriver;
            s.Saturation = 70;
            s.MonitorBrightness = bright ? (int?)20 : null;
            return s;
        }

        static bool W15Until(Func<bool> f, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { if (f()) return true; Thread.Sleep(20); }
            return f();
        }

        static void V502W15Tests()
        {
            Console.WriteLine("-- v5.0.2 filter (W15)");
            string recName = RecoveryStore.FileName;

            // ---------------------------------------------------------------- AUDIT F10: SerialWorker race at the exact limit
            {
                SerialWorker w = new SerialWorker("W15Race");
                int stuckLeft = 0, n = 300;
                for (int i = 0; i < n; i++)
                {
                    int r; string e;
                    w.Invoke(delegate { Thread.SpinWait(20000 + (i % 7) * 3000); return 1; }, 1, out r, out e);
                    if (!w.WaitIdle(2000)) stuckLeft++;
                }
                Check("V502-W15 F10 SerialWorker never stays stuck after a call at the limit (" + n + " calls)", stuckLeft == 0 && !w.Stuck, "stuck after " + stuckLeft);
                int r2; string e2;
                bool to = !w.Invoke(delegate { Thread.Sleep(400); return 2; }, 50, out r2, out e2);
                bool stuckNow = w.Stuck;
                bool idle = w.WaitIdle(2000);
                bool after = w.Invoke(delegate { return 3; }, 500, out r2, out e2) && r2 == 3;
                Check("V502-W15 F10 timeout -> stuck -> WaitIdle -> next call works", to && stuckNow && idle && after, to + " " + stuckNow + " " + idle + " " + after);
            }

            {
                // H5 seed 50375: a call still waiting in the queue when its caller gave up never runs later
                SerialWorker w = new SerialWorker("W15Cancel");
                int ran = 0, r; string e;
                ThreadPool.QueueUserWorkItem(delegate { int x; string y; w.Invoke(delegate { Thread.Sleep(600); return 1; }, 2000, out x, out y); });
                Thread.Sleep(50);
                bool gaveUp = !w.Invoke(delegate { Interlocked.Increment(ref ran); return 2; }, 100, out r, out e);
                Thread.Sleep(900);
                Check("V502-W15 a queued call whose caller gave up is cancelled (never reaches the device later)", gaveUp && ran == 0 && e.Contains("cancelled"), "gaveUp " + gaveUp + ", ran " + ran + ", '" + e + "'");
            }
            {
                // H5 seed 50375: a re-probe that cannot read a value this controller wrote keeps it on record for the restore
                W15Ddc dev = new W15Ddc();
                DdcCiController c = new DdcCiController(dev);
                c.Sleep = delegate(int ms) { };
                string e;
                c.Refresh(out e);
                string id = @"\\.\DISPLAY1|physical:0";
                bool w = c.SetBrightness(id, 37, out e);
                FlakyDdc flaky = new FlakyDdc(dev);
                DdcCiController c2 = c; // same controller, probe now fails on brightness reads
                flaky.Broken = true;
                System.Reflection.FieldInfo bf = typeof(DdcCiController).GetField("backend", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                bf.SetValue(c2, flaky);
                c2.Refresh(out e);
                flaky.Broken = false;
                bool has = c2.HasChanges;
                bool r = c2.RestoreOriginals(out e);
                uint b; lock (dev.Sync) b = dev.Bright;
                Check("V502-W15 P1-2 re-probe that cannot read a written value: still restored (70)", w && has && r && b == 70, "write " + w + ", has " + has + ", restore " + r + ", brightness " + b + " " + e);
            }

            // ---------------------------------------------------------------- BRUTE P1-1: slow driver vibrance write
            foreach (int setMs in new int[] { 700, 2500 })
            {
                string f = PerfFolder();
                W15Vib vib = new W15Vib();
                LazyVibrance lv = new LazyVibrance(delegate { return vib; }, null);
                lv.CallTimeoutMs = 300; lv.StuckWaitMs = 1000;
                ShadeEngine en = new ShadeEngine(f, Identity256Gamma(), lv, null, OneDisplay);
                en.Start();
                en.SmoothTransition = false;
                en.Current = W15Style(true, false);
                lock (vib.Sync) vib.SetMs = setMs;
                en.Enable();                              // the vibrance write times out, lands later
                lock (vib.Sync) vib.SetMs = 0;
                en.Disable();
                bool fileKeptOrRestored;
                lock (vib.Sync) fileKeptOrRestored = vib.Level == 30 || File.Exists(Path.Combine(f, recName));
                W15Until(delegate { lock (vib.Sync) return vib.Sets > 0 && !IsStuck(lv); }, 4000);
                Thread.Sleep(setMs + 200);
                en.Stop();
                int level; lock (vib.Sync) level = vib.Level;
                bool gone = !File.Exists(Path.Combine(f, recName));
                en.Dispose();
                Check("V502-W15 P1-1 driver vibrance write " + setMs + " ms (limit 300): original back, recovery file kept until then",
                    fileKeptOrRestored && level == 30 && gone, "level " + level + " (original 30), file gone " + gone + ", kept-or-restored at off " + fileKeptOrRestored);
            }

            // ---------------------------------------------------------------- AUDIT F9: unchanged level = no driver call
            {
                string f = PerfFolder();
                W15Vib vib = new W15Vib();
                LazyVibrance lv = new LazyVibrance(delegate { return vib; }, null);
                ShadeEngine en = new ShadeEngine(f, Identity256Gamma(), lv, null, OneDisplay);
                en.Start(); en.SmoothTransition = false;
                en.Current = W15Style(true, false);
                en.Enable();
                int g0, s0; lock (vib.Sync) { g0 = vib.Gets; s0 = vib.Sets; }
                PilotStyle st = en.Current;
                Stopwatch swa = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++) { st.Shadows = 100 + i; en.SetCurrent(st, false); }   // curve changes, saturation does not
                double perApply = swa.Elapsed.TotalMilliseconds / 10.0;
                swa.Restart();
                for (int i = 0; i < 10; i++) en.SetCurrent(st, false);                               // nothing changes
                double perSame = swa.Elapsed.TotalMilliseconds / 10.0;
                Console.WriteLine("   V502-W15 measured: engine apply of a changed curve " + perApply.ToString("0.000") + " ms (0 ms fake devices, curve build + 1 write + 1 read-back + recovery check), unchanged " + perSame.ToString("0.000") + " ms");
                en.BeginHoldOriginal(); en.EndHoldOriginal();
                int g1, s1; lock (vib.Sync) { g1 = vib.Gets; s1 = vib.Sets; }
                en.Dispose();
                Check("V502-W15 F9 11 applies with the same saturation make no driver vibrance call", g1 == g0 && s1 == s0,
                    "gets " + g0 + "->" + g1 + ", sets " + s0 + "->" + s1);
            }

            // ---------------------------------------------------------------- BRUTE P1-2: DDC write slow / read-back failed / enumeration failed
            foreach (string mode in new string[] { "slow write 600 ms", "slow write 2600 ms", "read-back failed", "enumeration failed" })
            {
                string f = PerfFolder();
                W15Ddc dev = new W15Ddc();
                GatedDdcBackend gd = new GatedDdcBackend(dev, null);
                gd.Allowed = true; gd.CallTimeoutMs = 300; gd.EnumerateTimeoutMs = 300; gd.StuckWaitMs = 1000;
                ShadeEngine en = new ShadeEngine(f, Identity256Gamma(), null, null, OneDisplay, gd);
                en.Start(); en.SmoothTransition = false;
                en.Current = W15Style(false, true);
                if (mode.StartsWith("slow")) lock (dev.Sync) dev.SetMs = mode.Contains("2600") ? 2600 : 600;
                if (mode == "read-back failed") lock (dev.Sync) dev.FailReadsAfterSet = 1;
                en.Enable();
                lock (dev.Sync) dev.SetMs = 0;
                if (mode == "enumeration failed") { dev.FailEnumerate = true; en.RefreshMonitors(); dev.FailEnumerate = false; }
                Thread.Sleep(mode.Contains("2600") ? 2800 : 700);
                en.Disable();
                uint b1; lock (dev.Sync) b1 = dev.Bright;
                bool keptOrRestored = b1 == 70 || File.Exists(Path.Combine(f, recName));
                Thread.Sleep(300);
                en.Stop();
                uint b2; lock (dev.Sync) b2 = dev.Bright;
                bool gone = !File.Exists(Path.Combine(f, recName));
                en.Dispose();
                Check("V502-W15 P1-2 DDC " + mode + ": monitor brightness back to 70, recovery file kept until then",
                    keptOrRestored && b2 == 70 && gone, "after off " + b1 + ", after stop " + b2 + ", file gone " + gone);
            }

            {
                // P1-2 d / H5 seed 50402: a crash-time record whose values the first probe could not read (slow monitor) is
                // completed by the next good probe, so a later write is put back
                W15Ddc dev = new W15Ddc();
                DdcCiController c = new DdcCiController(dev);
                c.Sleep = delegate(int ms) { };
                DdcSnapshot seed = new DdcSnapshot(); seed.Id = @"\\.\DISPLAY1|physical:0";
                c.SeedOriginals(new DdcSnapshot[] { seed });
                string e;
                c.Refresh(out e);
                bool w = c.SetBrightness(seed.Id, 37, out e);
                bool r = c.RestoreOriginals(out e);
                uint b; lock (dev.Sync) b = dev.Bright;
                Check("V502-W15 P1-2 a crash-time record without values is completed by the next probe: brightness 37 -> back to 70",
                    w && r && b == 70, "write " + w + ", restore " + r + ", brightness " + b + " " + e);
            }

            // ---------------------------------------------------------------- BRUTE P2-1: damaged recovery file
            foreach (string dmg in new string[] { "cut", "zero-filled", "empty", "missing" })
            {
                // crash with the filter on; then damage the main file. The .bak written with it restores the true original.
                string f = PerfFolder();
                SlowGamma g = new SlowGamma();
                GammaRamp user = Identity256(); for (int i = 0; i < 256; i++) user.Blue[i] = (ushort)(user.Blue[i] * 9 / 10);
                g.Hw = user.Clone();
                ShadeEngine en = new ShadeEngine(f, g, null, null, OneDisplay);
                en.Start(); en.SmoothTransition = false; en.Enable();
                string p = Path.Combine(f, recName);
                bool bak = File.Exists(p + ".bak");
                // crash: no Stop / Dispose (the folder lock is process-wide and reference-counted, so a second engine may start)
                byte[] b = File.ReadAllBytes(p);
                if (dmg == "cut") Array.Resize(ref b, b.Length / 2);
                else if (dmg == "zero-filled") b = new byte[b.Length];
                else if (dmg == "empty") b = new byte[0];
                if (dmg == "missing") File.Delete(p); else File.WriteAllBytes(p, b);
                ShadeEngine en2 = new ShadeEngine(f, g, null, null, OneDisplay);
                en2.Start();
                bool back = SameRamp(g.Hw, user);
                en2.Dispose(); en.Dispose();
                Check("V502-W15 P2-1 recovery file " + dmg + " after a crash: the .bak puts the user's own curve back", bak && back, "bak " + bak + ", restored " + back);
            }
            {
                // both copies unreadable: moved to shade-recovery.bad-*.json, never overwritten; the original is Windows' standard
                string f = PerfFolder();
                SlowGamma g = new SlowGamma();
                GammaRamp filtered = Identity256(); for (int i = 0; i < 256; i++) filtered.Red[i] = (ushort)Math.Min(65535, filtered.Red[i] + 9000);
                g.Hw = filtered.Clone();
                File.WriteAllText(Path.Combine(f, recName), "{\"Version\":1,\"Ramps\":[{\"Dev");
                File.WriteAllBytes(Path.Combine(f, recName + ".bak"), new byte[64]);
                W15Vib vib = new W15Vib(); vib.Level = 9; vib.Default = 0;
                ShadeEngine en = new ShadeEngine(f, g, vib, null, OneDisplay);
                en.Start(); en.SmoothTransition = false;
                bool flagged = en.RecoveryDamaged && en.RecoveryNotice.Length > 0;
                string[] bad = Directory.GetFiles(f, "shade-recovery.bad-*.json");
                en.Current = W15Style(true, false);
                en.Enable(); en.Disable();
                bool standard = SameRamp(g.Hw, Identity256());
                int lvl; lock (vib.Sync) lvl = vib.Level;
                bool badKept = bad.Length == 1 && File.ReadAllText(bad[0]).StartsWith("{\"Version\":1,\"Ramps\":[{\"Dev");
                en.Dispose();
                Check("V502-W15 P2-1 unreadable recovery file + backup: kept as shade-recovery.bad-*.json, reported, filter off = standard curve and default vibrance",
                    flagged && badKept && standard && lvl == 0, "flagged " + flagged + ", bad files " + bad.Length + " kept " + badKept + ", identity " + standard + ", vibrance " + lvl);
            }
            {
                // the recovery file cannot be written: the filter is not turned on (a crash would leave it on for good)
                string f = PerfFolder();
                Directory.CreateDirectory(Path.Combine(f, recName + ".tmp")); // File.Create of the temp file fails
                SlowGamma g = new SlowGamma();
                ShadeEngine en = new ShadeEngine(f, g, null, null, OneDisplay);
                en.Start(); en.SmoothTransition = false;
                bool on = en.Enable();
                bool untouched = SameRamp(g.Hw, Identity256()) && !en.IsEnabled;
                string why = FilterCore.GammaProblem(en.LastError);
                en.Dispose();
                Check("V502-W15 P2-1 recovery file not writable: filter refuses to turn on, screen untouched, Chinese reason",
                    !on && untouched && why.Contains("写不进文件"), "on " + on + ", untouched " + untouched + ", '" + why + "'");
            }
            {
                string src = File.ReadAllText(SrcPath("Shade/ShadePreset.cs")) + File.ReadAllText(SrcPath("Shade/RecoveryStore.cs"));
                Check("V502-W15 P2-1 recovery writes are flushed to disk (Flush(true)) and doubled into .bak",
                    src.Contains("if (durable) stream.Flush(true);") && src.Contains("JsonFile.Write(filePath, data, true)") && src.Contains("JsonFile.Write(BackupPath, data, true)"), "");
            }

            // ---------------------------------------------------------------- BRUTE P3-1: unverified first step + refused rollback
            {
                W15Gamma g = new W15Gamma();
                GammaRamp user = Identity256(); for (int i = 0; i < 256; i++) user.Green[i] = (ushort)(user.Green[i] * 95 / 100);
                g.Hw = user.Clone();
                GammaRampController c = new GammaRampController(g);
                g.FailGetsAfterNextSet = 1;       // the first ramp write lands but cannot be read back
                g.RefuseSetsAfter = 1;            // ... and the rollback write is refused
                string e;
                bool ok = c.TransitionTo(new List<string> { @"\\.\DISPLAY1" }, PilotCurve.Build(PilotBuiltIns.General()), 0, out e);
                bool filteredOnScreen = !SameRamp(g.Hw, user);
                g.RefuseSetsAfter = -1;
                c.RestoreAll(out e);
                bool back = SameRamp(g.Hw, user);
                GammaRamp? bl = c.GetBaseline(@"\\.\DISPLAY1");
                Check("V502-W15 P3-1 rollback refused: the device stays touched, RestoreAll puts the user's curve back",
                    !ok && filteredOnScreen && back && bl.HasValue && SameRamp(bl.Value, user), "ok " + ok + ", filtered " + filteredOnScreen + ", restored " + back);
            }

            {
                // H5 seed 50325: the same while the filter is already on (unverified step, refused rollback): the unverified ramp
                // is never adopted as "another program's" curve or as the baseline; filter off puts the user's curve back
                W15Gamma g = new W15Gamma();
                GammaRamp user = Identity256(); for (int i = 0; i < 256; i++) user.Green[i] = (ushort)(user.Green[i] * 95 / 100);
                g.Hw = user.Clone();
                GammaRampController c = new GammaRampController(g);
                string e;
                bool on = c.TransitionTo(new List<string> { @"\\.\DISPLAY1" }, PilotCurve.Build(PilotBuiltIns.General()), 0, out e);
                g.FailGetsAfterNextSet = 1; g.RefuseSetsAfter = g.Sets + 1;
                bool second = c.TransitionTo(new List<string> { @"\\.\DISPLAY1" }, PilotCurve.Build(PilotBuiltIns.Create()[1]), 0, out e);
                g.RefuseSetsAfter = -1;
                int n; c.Resync(out e);
                c.RestoreAll(out e);
                bool back = SameRamp(g.Hw, user);
                Check("V502-W15 P3-1 (filter on) unverified step + refused rollback: never adopted as the original, filter off restores",
                    on && !second && back, "on " + on + ", second " + second + ", restored " + back);
            }

            // ---------------------------------------------------------------- BRUTE P3-2: lenient read-back on all three channels
            {
                GammaRamp a = Identity256(), b = Identity256();
                for (int i = 0; i < 256; i++) b.Blue[i] = (ushort)(b.Blue[i] * 97 / 100);
                GammaRamp q = Identity256();
                for (int i = 0; i < 256; i++) { q.Red[i] = (ushort)Math.Min(65535, q.Red[i] + 300); q.Green[i] = (ushort)Math.Min(65535, q.Green[i] + 300); q.Blue[i] = (ushort)Math.Min(65535, q.Blue[i] + 300); }
                SlowGamma g = new SlowGamma();
                LenientGammaBackend lg = new LenientGammaBackend(g);
                string e; GammaRamp r;
                lg.TrySet("D", a, out e);
                g.Hw = b.Clone();                          // another program changes blue only
                lg.TryGet("D", out r, out e);
                bool seen = SameRamp(r, b);
                g.Hw = q.Clone();                          // a driver that rounds every channel a little
                lg.TryGet("D", out r, out e);
                bool lenient = SameRamp(r, a);
                lg.Forget(null);
                lg.TryGet("D", out r, out e);
                bool forgot = SameRamp(r, q);
                Check("V502-W15 P3-2 blue-only change is read as it is; a small all-channel rounding still passes; Forget stops substituting",
                    LenientGammaBackend.Misses(b, a) <= 8 && LenientGammaBackend.Misses3(b, a) > 8 && seen && lenient && forgot,
                    "misses red " + LenientGammaBackend.Misses(b, a) + " all " + LenientGammaBackend.Misses3(b, a) + ", seen " + seen + ", lenient " + lenient + ", forgot " + forgot);
            }

            // ---------------------------------------------------------------- BRUTE P2-3: 调试滤镜 monotone
            {
                List<PilotStyle> fixedSet = new List<PilotStyle>(PilotBuiltIns.Create());
                Random rnd = new Random(502);
                int tonalMax = PilotStyle.StrengthTonalMax(PilotViews.FixedStrengthMode), sMax = PilotStyle.StrengthSignedMax(PilotViews.FixedStrengthMode);
                List<string> strict = new List<string>();
                foreach (PilotStyle p in fixedSet) { string w = TuneDrop(p, 0.0); if (w != null) strict.Add(w); }
                // user presets: everyday ranges (the kind a user saves) must be strictly non-decreasing too
                for (int i = 0; i < 150; i++)
                {
                    PilotStyle p = PilotBuiltIns.General();
                    p.Name = "u" + i;
                    p.Shadows = rnd.Next(0, 131); p.Highlights = rnd.Next(0, 101); p.Midtones = rnd.Next(-40, 41); p.BlackPoint = rnd.Next(-40, 61);
                    p.ShadowRange = rnd.Next(-60, 61); p.TransitionSoftness = rnd.Next(-40, 51);
                    string w = TuneDrop(p, 0.0); if (w != null) strict.Add(w);
                }
                // any preset over the full slider ranges: never more than 0.3 % of full scale darker when moving right
                double worst = 0; string worstAt = "";
                for (int i = 0; i < 400; i++)
                {
                    PilotStyle p = PilotBuiltIns.General();
                    p.Name = "r" + i;
                    p.Shadows = rnd.Next(0, tonalMax + 1); p.Highlights = rnd.Next(0, tonalMax + 1); p.Midtones = rnd.Next(-sMax, sMax + 1);
                    p.BlackPoint = rnd.Next(-sMax, sMax + 1); p.ShadowRange = rnd.Next(-sMax, sMax + 1); p.TransitionSoftness = rnd.Next(-sMax, sMax + 1);
                    double d = TuneWorstDrop(p);
                    if (d > worst) { worst = d; worstAt = p.Shadows + "/" + p.Highlights + "/" + p.Midtones + "/" + p.BlackPoint + "/" + p.ShadowRange + "/" + p.TransitionSoftness; }
                }
                Check("V502-W15 P2-3 调试滤镜: built-ins and 150 everyday presets get brighter at every step to the right",
                    strict.Count == 0, strict.Count + ": " + string.Join("; ", strict.GetRange(0, Math.Min(3, strict.Count)).ToArray()));
                Check("V502-W15 P2-3 调试滤镜: 400 presets over the full ranges never drop more than 0.3 % of full scale", worst <= 0.003,
                    "worst " + (worst * 100).ToString("0.000") + " % at " + worstAt);
            }

            // ---------------------------------------------------------------- Leo 18 Nov: performance
            {
                string fc = File.ReadAllText(SrcPath("FilterCore.cs"));
                Check("V502-W15 perf filter worker runs at BelowNormal priority", fc.Contains("worker.Priority = ThreadPriority.BelowNormal;"), "");
                CoreRig rig = new CoreRig(0, true);
                rig.Core.Start(true);
                W15Until(delegate { rig.Pump(); return rig.Core.IsOn; }, 5000);
                Thread.Sleep(300); rig.Pump();
                int w0 = rig.Core.Wakeups;
                Thread.Sleep(2000); rig.Pump();
                int wOn = rig.Core.Wakeups - w0;
                rig.Core.SetOn(false);
                W15Until(delegate { rig.Pump(); return !rig.Core.IsOn; }, 5000);
                Thread.Sleep(300); rig.Pump();
                int w1 = rig.Core.Wakeups;
                int g0 = rig.Gamma.Gets + rig.Gamma.Sets;
                Thread.Sleep(2000); rig.Pump();
                int wOff = rig.Core.Wakeups - w1, gOff = rig.Gamma.Gets + rig.Gamma.Sets - g0;
                Check("V502-W15 perf idle filter worker: 0 wakeups per second with the filter on and off, no device call while off",
                    wOn == 0 && wOff == 0 && gOff == 0, "on " + wOn + "/2 s, off " + wOff + "/2 s, device calls while off " + gOff);
                rig.Core.SetOn(true);
                W15Until(delegate { rig.Pump(); return rig.Core.IsOn; }, 5000);
                Thread.Sleep(200); rig.Pump();
                PilotStyle st = rig.Core.Style;
                int c0 = rig.Gamma.Sets, gg0 = rig.Gamma.Gets;
                rig.Core.SetStyle(st);                              // the same values again
                Thread.Sleep(400); rig.Pump();
                int same = rig.Gamma.Sets - c0, sameGets = rig.Gamma.Gets - gg0;
                st.Shadows = st.Shadows > 50 ? st.Shadows - 10 : st.Shadows + 10;
                Stopwatch sw = Stopwatch.StartNew();
                int c1 = rig.Gamma.Sets, gg1 = rig.Gamma.Gets;
                rig.Core.SetStyle(st);
                W15Until(delegate { rig.Pump(); return rig.Gamma.Sets > c1; }, 3000);
                long ms = sw.ElapsedMilliseconds;
                Thread.Sleep(200); rig.Pump();
                int changedSets = rig.Gamma.Sets - c1, changedGets = rig.Gamma.Gets - gg1;
                Check("V502-W15 perf unchanged values are not re-applied; a changed value = 1 gamma write + 1 read-back",
                    same == 0 && sameGets == 0 && changedSets == 1 && changedGets <= 1,
                    "same: " + same + " sets " + sameGets + " gets; changed: " + changedSets + " sets " + changedGets + " gets, " + ms + " ms with a 0 ms fake");
                Console.WriteLine("   V502-W15 measured: idle wakeups on " + (wOn / 2.0) + "/s, off " + (wOff / 2.0) + "/s; apply of a changed value " + changedSets
                    + " write + " + changedGets + " read, unchanged 0 calls");
                rig.Core.Shutdown(); rig.Core.Join(5000);
            }

            // ---------------------------------------------------------------- exit order (BRUTE P1-1 seed 50152)
            {
                string f = PerfFolder();
                W15LogGamma g = new W15LogGamma();
                FilterCore core = new FilterCore(delegate { return new ShadeEngine(f, g, null, null, OneDisplay); }, delegate(Action a) { a(); }, null);
                core.Start(true);
                W15Until(delegate { return core.IsOn; }, 5000);
                lock (g) g.DelayMs = 150;
                core.SetOn(false);                 // the worker is busy restoring (slow display) ...
                Thread.Sleep(60);
                core.SetOn(true);                  // ... while an 'on' is queued just before exit
                int mark; lock (g) mark = g.Writes.Count;
                core.Shutdown();
                bool joined = core.Join(10000);
                int filteredAfter = 0;
                lock (g) for (int i = mark; i < g.Writes.Count; i++) if (!g.Writes[i]) filteredAfter++;
                bool ident; lock (g) ident = SameRamp(g.Hw, Identity256());
                Check("V502-W15 exit right after a toggle: the screen is restored first and never filtered again",
                    joined && ident && filteredAfter == 0, "joined " + joined + ", identity " + ident + ", filter writes after exit " + filteredAfter);
            }

            // ---------------------------------------------------------------- UI-C wording / behaviour (source)
            {
                string pf = File.ReadAllText(SrcPath("SettingsForm.PageFilter.cs"));
                string ht = File.ReadAllText(SrcPath("Shade/PilotHelpText.cs"));
                string htText = System.Text.RegularExpressions.Regex.Replace(ht, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
                string tv = File.ReadAllText(SrcPath("FilterTutorialView.cs"));
                Check("V502-W15 UI-C H2 share tooltip: 分享码, TSHADE2", pf.Contains("Tip(copy, ShareTip)") && pf.Contains("TSHADE2 开头") && !htText.Contains("TSHADE1"), "");
                Check("V502-W15 UI-C H3 restore tooltip: no hotkey promised", pf.Contains("Tip(restore, RestoreTip)") && pf.Contains("没有快捷键") && !htText.Contains("Ctrl+F12"), "");
                Check("V502-W15 UI-C M1 saturation tooltip: no DDC/CI saturation (page, check box, tutorial)",
                    pf.Contains("string tip = StyleTip(key);") && pf.Contains("Tip(satOn, SatTip)") && tv.Contains("key == PilotHelpText.Saturation") && !pf.Contains("DDC/CI 硬件饱和度"), "");
                Check("V502-W15 UI-C M2 显示器硬件 fold: only the user's click opens it and probes DDC/CI",
                    pf.Contains("ManualFold(hwFold, delegate { if (!IsDisposed) Fh.HardwareOpened(); });") && !pf.Contains("hwCard.VisibleChanged"), "");
                Check("V502-W15 UI-C M8 滤镜教程 gets the user's keys", pf.Contains("typeof(FilterTutorialKeys)") && pf.Contains("TutorialKeys(Fh.Prefs)"), "");
                Check("V502-W15 AUDIT F5 Magnification cost shown on the page and in the module tooltip",
                    pf.Contains("Windows 放大镜（会降低帧数）") && pf.Contains("MagWarnText") && htText.Contains("会降低游戏帧数"), "");
                Check("V502-W15 UI-C L13 no disclaimer / foreign product wording left in the module texts",
                    !htText.Contains("未实测") && !htText.Contains("只是一个起点") && !htText.Contains("ShadePilot 本身") && !htText.Contains("ShadePilot 的软件"), "");
                Check("V502-W15 UI-C L10 看原画面 everywhere", tv.Contains("看原画面\"") && htText.Contains("按住看原画面"), "");
                Check("V502-W15 UI-C L6 高级设置 hint says 顶部", pf.Contains("在顶部打开「高级设置」后显示"), "");
            }
        }

        /// <summary>Gamma fake that logs every write (true = identity).</summary>
        sealed class W15LogGamma : IGammaBackend
        {
            public GammaRamp Hw = Identity256();
            public int DelayMs;
            public readonly List<bool> Writes = new List<bool>();
            public bool TryGet(string d, out GammaRamp r, out string e) { int ms; lock (this) ms = DelayMs; if (ms > 0) Thread.Sleep(ms); lock (this) r = Hw.Clone(); e = ""; return true; }
            public bool TrySet(string d, GammaRamp r, out string e)
            {
                int ms; lock (this) ms = DelayMs;
                if (ms > 0) Thread.Sleep(ms);
                lock (this) { Hw = r.Clone(); Writes.Add(SameRamp(r, Identity256())); }
                e = ""; return true;
            }
        }

        /// <summary>DDC wrapper whose reads fail while Broken (a slow / flaky probe).</summary>
        sealed class FlakyDdc : IDdcBackend
        {
            readonly IDdcBackend inner; public volatile bool Broken;
            public FlakyDdc(IDdcBackend i) { inner = i; }
            public List<DdcMonitorHandle> Enumerate(out string error) { return inner.Enumerate(out error); }
            public bool GetCapabilities(IntPtr m, out uint caps) { if (Broken) { caps = 0; return false; } return inner.GetCapabilities(m, out caps); }
            public bool GetBrightness(IntPtr m, out uint a, out uint b, out uint c) { if (Broken) { a = b = c = 0; return false; } return inner.GetBrightness(m, out a, out b, out c); }
            public bool SetBrightness(IntPtr m, uint v) { return inner.SetBrightness(m, v); }
            public bool GetContrast(IntPtr m, out uint a, out uint b, out uint c) { return inner.GetContrast(m, out a, out b, out c); }
            public bool SetContrast(IntPtr m, uint v) { return inner.SetContrast(m, v); }
            public bool GetVcp(IntPtr m, byte code, out bool sp, out uint cur, out uint max) { if (Broken) { sp = false; cur = max = 0; return false; } return inner.GetVcp(m, code, out sp, out cur, out max); }
            public bool SetVcp(IntPtr m, byte code, uint v) { return inner.SetVcp(m, code, v); }
            public void Destroy(IntPtr m) { inner.Destroy(m); }
        }

        static bool IsStuck(LazyVibrance lv)
        {
            System.Reflection.FieldInfo fi = typeof(LazyVibrance).GetField("worker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            SerialWorker w = fi == null ? null : fi.GetValue(lv) as SerialWorker;
            return w != null && w.Stuck;
        }

        static IGammaBackend Identity256Gamma() { return new SlowGamma(); }

        static bool SameRamp(GammaRamp a, GammaRamp b)
        {
            if (!a.IsValid || !b.IsValid) return false;
            for (int i = 0; i < 256; i++) if (a.Red[i] != b.Red[i] || a.Green[i] != b.Green[i] || a.Blue[i] != b.Blue[i]) return false;
            return true;
        }

        static string SrcPath(string rel) { return Path.Combine("/home/dev/tarkov-crosshair-v4/src", rel); }

        static double TuneMean(PilotStyle s)
        {
            ShadeRamp r = PilotCurve.Build(s);
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += 0.2126 * r.Red[i] + 0.7152 * r.Green[i] + 0.0722 * r.Blue[i];
            return sum / 256.0 / 65535.0;
        }

        static string TuneDrop(PilotStyle p, double tol)
        {
            double prev = double.NegativeInfinity;
            for (int bar = FilterTune.Min; bar <= FilterTune.Max; bar++)
            {
                double m = TuneMean(FilterTune.Apply(p, bar));
                if (m < prev - tol - 1e-12) return p.Name + " " + (bar - 1) + "->" + bar + " " + prev.ToString("0.0000") + "->" + m.ToString("0.0000");
                prev = Math.Max(prev, m);
            }
            return null;
        }

        static double TuneWorstDrop(PilotStyle p)
        {
            double best = double.NegativeInfinity, worst = 0;
            for (int bar = FilterTune.Min; bar <= FilterTune.Max; bar++)
            {
                double m = TuneMean(FilterTune.Apply(p, bar));
                if (best - m > worst) worst = best - m;
                if (m > best) best = m;
            }
            return worst;
        }
    }
}
