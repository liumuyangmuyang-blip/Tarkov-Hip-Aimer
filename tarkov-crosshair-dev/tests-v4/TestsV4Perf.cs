using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace TarkovCrosshair.Tests
{
    // v4.0 PERF: the render-thread hot path must not allocate per frame (GC.GetAllocatedBytesForCurrentThread over 100k
    // frames) and must stay cheap; behaviour must be bit-identical to before the PERF change (golden trace hash recorded
    // with the pre-PERF Motion.cs / InputResolver.cs). Self-registers through a module initializer: Tests.cs Main needs no edit.
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V4PerfInit() { V4PerfTests(); }

        // Golden trace of OffsetX/OffsetY/Alpha + every MotionDebug field + resolver output, produced by the pre-PERF code.
        const string PerfGoldenTrace = "512693f19f7a0a82"; // v5.1.1 (X2): same trace with every double rounded to 6 decimals (see R), so Linux and Windows give one hash; the exact Linux hash of this code is still 3e602675905a8c83 (Windows gave cfc98f838ee048b8). v5.1 (W21): re-recorded for decision 7 (A->D / mid-travel changes go back to 0 first, then out; mirrored return) with the v5.1 settings defaults of 20 Nov (强化 = Ctrl+T); v4.1 hash 8b78821b1b30f825. v4.1 MOTION41F: re-recorded for BRUTE40 P2-1 (开关键 x2 keeps the A/D W/S press order) + P2-3 (Tab and Esc cancel on their own); the trace toggles g.Enabled with keys held and presses Tab/Esc. Only those two: reverting both gives e422e310d9e622ac again (P2-1 alone 6dde2d2dc10c71ec, P2-3 alone dfb0b85bfb465565; P2-2 / P2-4 do not change it). Before:
        // v4.1 CORE41b: e422e310d9e622ac, re-recorded for the 开关键 x2 snap (BRUTE40, Leo 17 Nov; only that change, checked by removing the snap: db92ddaab3cc3680 again); before: // v4.0 PERF: was 32bcc5081ebe7a7c (pre-PERF code, identical after PERF); re-recorded after the
        // 开关键 double press began clearing a latched 切换 lean (the trace toggles g.Enabled every 5000 frames; focus option off here)

        const int PerfFrames = 100000;

        sealed class PerfKeys
        {
            public readonly bool[] Down = new bool[256];
            public bool IsDown(int vk)
            {
                if (vk == 0x10) return Down[0xA0] || Down[0xA1] || Down[0x10];
                if (vk == 0x11) return Down[0xA2] || Down[0xA3] || Down[0x11];
                if (vk == 0x12) return Down[0xA4] || Down[0xA5] || Down[0x12];
                return vk >= 0 && vk < 256 && Down[vk];
            }
        }

        static readonly int[] PerfUniverse = { 0x57, 0x53, 0x41, 0x44, 0x51, 0x45, 0x46, 0x20, 1, 2, 4, 5, 6, 9, 0x1B, 0x7B,
            0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x43, 0x56, 0x47, 0x5A };

        // Deterministic xorshift (no Random: identical across runtimes).
        static uint perfRng;
        static uint PerfNext() { uint x = perfRng; x ^= x << 13; x ^= x >> 17; x ^= x << 5; perfRng = x; return x; }

        static void PerfStep(PerfKeys keys)
        {
            // flip 0..2 keys per frame, rarely; keeps keys held for many frames like a player
            uint r = PerfNext();
            if ((r & 7) == 0) { int vk = PerfUniverse[(r >> 3) % (uint)PerfUniverse.Length]; keys.Down[vk] = !keys.Down[vk]; }
            if ((r >> 20 & 31) == 0) { int vk = PerfUniverse[(r >> 8) % (uint)PerfUniverse.Length]; keys.Down[vk] = !keys.Down[vk]; }
        }

        static void PerfSetup(out Profile p, out GlobalSettings g, int variant)
        {
            p = new Profile();
            g = new GlobalSettings();
            g.Enabled = true;
            g.HandEnabled = true;
            g.BoostEnabled = true;
            g.OnlyWhenGameFocused = false;
            g.LeanMode = (variant & 1) != 0 ? "toggle" : "hold";
            g.AdsMode = (variant & 2) != 0 ? "toggle" : "hold";
            g.HandMode = (variant & 4) != 0 ? "hold" : "toggle";
            g.AdsCancelSprint = true;
            g.LeanCancelSprint = true;
            if (p.Jump != null) p.Jump.Enabled = true;
            // combo bindings on W/A/S/D so ReadInput's 组合键 suppression path runs
            if (g.Keys != null && (variant & 1) != 0) g.Keys.SetCombo(KeyBindings.ActionLeanRight, 0x44, KeyBindings.ModCtrl);
        }

        // v5.1.1 (X2): rounded to 6 decimals before hashing. The exact "R" text differs between Linux (glibc) and Windows
        // (MSVC CRT) in the last bits of Math.Exp / Pow, so the exact hash only ever matched one OS (CI run 44729023467).
        static string R(double d) { return Math.Round(d, 6).ToString("R", CultureInfo.InvariantCulture); }

        /// <summary>One frame of exactly what the render thread does with keys and engine (Overlay.Run minus Win32).</summary>
        static void PerfFrame(MotionEngine e, LiveInputFilter f, KeyBindings k, Func<int, bool> isDown, Profile p, GlobalSettings g,
            PreviewMode pm, double dt)
        {
            InputState input = MotionEngine.ReadInput(k, isDown);
            input = f.Next(input, g.Enabled, pm == PreviewMode.Live);
            e.Update(dt, input, p, g, pm, 0.5);
        }

        static string PerfTrace()
        {
            StringBuilder sb = new StringBuilder();
            ulong h = 1469598103934665603UL;
            Action<string> add = s => { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } };
            for (int variant = 0; variant < 8; variant++)
            {
                Profile p; GlobalSettings g;
                PerfSetup(out p, out g, variant);
                PerfKeys keys = new PerfKeys();
                Func<int, bool> isDown = keys.IsDown;
                MotionEngine e = new MotionEngine();
                LiveInputFilter f = new LiveInputFilter();
                perfRng = 0x9E3779B9u + (uint)variant;
                Array pms = Enum.GetValues(typeof(PreviewMode));
                for (int i = 0; i < 20000; i++)
                {
                    PerfStep(keys);
                    PreviewMode pm = PreviewMode.Live;
                    if (variant >= 6 && (i / 1500) % 3 == 1) pm = (PreviewMode)pms.GetValue((i / 4500) % pms.Length);
                    if (i % 5000 == 4999) g.Enabled = !g.Enabled;
                    if (i == 9000) e.Reset();
                    double dt = (variant == 5 && i % 97 == 0) ? 0.25 : (1.0 / 144.0) * (1 + (i % 7) * 0.05);
                    PerfFrame(e, f, g.Keys, isDown, p, g, pm, dt);
                    MotionDebug d = e.Debug;
                    add(R(e.OffsetX)); add(R(e.OffsetY)); add(R(e.Alpha));
                    add(d.Dir.ToString()); add(R(d.Lean)); add(R(d.Recoil)); add(R(d.UiOffsetX)); add(R(d.UiOffsetY));
                    add(d.Switched ? "1" : "0"); add(d.Jumping ? "1" : "0"); add(d.Boosted ? "1" : "0");
                    add(d.DirName); add(d.HandName); add(e.LeanCancelled ? "c" : "-"); add(e.AdsCancelledBySprint ? "a" : "-");
                }
            }
            return h.ToString("x16");
        }

        static long PerfAllocated(Action body)
        {
            body(); // warm-up (JIT, lazy caches, curve tables)
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            body();
            return GC.GetAllocatedBytesForCurrentThread() - a0;
        }

        static void V4PerfTests()
        {
            // ---- P1 behaviour identical to the pre-PERF code ----
            string trace = PerfTrace();
            Console.WriteLine("PERF trace hash " + trace);
            Check("PERF P1 engine/input behaviour identical to pre-PERF code (8 setups x 20k frames, golden hash)",
                PerfGoldenTrace == "PENDING" || trace == PerfGoldenTrace, "hash=" + trace + " golden=" + PerfGoldenTrace);

            // ---- P2 no allocation: ReadInput + LiveInputFilter + Update, live, 100k frames per setup ----
            for (int variant = 0; variant < 8; variant++)
            {
                Profile p; GlobalSettings g;
                PerfSetup(out p, out g, variant);
                PerfKeys keys = new PerfKeys();
                Func<int, bool> isDown = keys.IsDown;
                MotionEngine e = new MotionEngine();
                LiveInputFilter f = new LiveInputFilter();
                KeyBindings k = g.Keys;
                perfRng = 12345u + (uint)variant;
                long bytes = PerfAllocated(() =>
                {
                    for (int i = 0; i < PerfFrames; i++) { PerfStep(keys); PerfFrame(e, f, k, isDown, p, g, PreviewMode.Live, 1.0 / 144.0); }
                });
                Check("PERF P2 live frame allocates 0 bytes over 100k frames (setup " + variant + ")", bytes == 0, "bytes=" + bytes);
            }

            // ---- P3 no allocation in every preview mode ----
            {
                Profile p; GlobalSettings g;
                PerfSetup(out p, out g, 3);
                PerfKeys keys = new PerfKeys();
                Func<int, bool> isDown = keys.IsDown;
                MotionEngine e = new MotionEngine();
                LiveInputFilter f = new LiveInputFilter();
                foreach (PreviewMode pm in (PreviewMode[])Enum.GetValues(typeof(PreviewMode)))
                {
                    PreviewMode mode = pm;
                    long bytes = PerfAllocated(() =>
                    {
                        for (int i = 0; i < PerfFrames / 10; i++) PerfFrame(e, f, g.Keys, isDown, p, g, mode, 1.0 / 144.0);
                    });
                    Check("PERF P3 preview " + mode + " allocates 0 bytes over 10k frames", bytes == 0, "bytes=" + bytes);
                }
            }

            // ---- P4 InputResolver.Resolve allocates nothing ----
            {
                InputResolver r = new InputResolver();
                perfRng = 777u;
                InputState s = new InputState();
                long bytes = PerfAllocated(() =>
                {
                    for (int i = 0; i < PerfFrames; i++)
                    {
                        uint x = PerfNext();
                        s.Left = (x & 1) != 0; s.Right = (x & 2) != 0; s.Back = (x & 4) != 0; s.Fwd = (x & 8) != 0;
                        s = r.Resolve(s);
                    }
                });
                Check("PERF P4 InputResolver.Resolve allocates 0 bytes over 100k polls", bytes == 0, "bytes=" + bytes);
            }

            // ---- P5 MotionEngine.Debug: one snapshot per frame it is read, same object until the next Update ----
            {
                Profile p; GlobalSettings g;
                PerfSetup(out p, out g, 0);
                MotionEngine e = new MotionEngine();
                MotionDebug d0 = e.Debug;
                bool resetOk = d0 != null && d0.Dir == -1 && d0.Lean == 0 && d0.Recoil == 0 && d0.UiOffsetX == 0 && d0.UiOffsetY == 0
                    && !d0.Switched && !d0.Jumping && !d0.Boosted;
                InputState s = new InputState(); s.Fwd = true;
                e.Update(1.0 / 144.0, s, p, g, PreviewMode.Live, 1);
                MotionDebug d1 = e.Debug, d1b = e.Debug;
                e.Update(1.0 / 144.0, s, p, g, PreviewMode.Live, 1);
                MotionDebug d2 = e.Debug;
                bool snap = ReferenceEquals(d1, d1b) && !ReferenceEquals(d1, d2);
                double y1 = d1.UiOffsetY;
                e.Update(1.0 / 144.0, s, p, g, PreviewMode.Live, 1);
                bool immutable = d1.UiOffsetY == y1 && e.Debug.UiOffsetX == e.OffsetX && e.Debug.UiOffsetY == -e.OffsetY;
                e.Reset();
                MotionDebug d3 = e.Debug;
                bool reset2 = d3.Dir == -1 && d3.UiOffsetX == 0 && d3.UiOffsetY == 0 && d3.Lean == 0;
                Check("PERF P5 Debug snapshot: reset values, stable until next Update, old snapshot immutable",
                    resetOk && snap && immutable && reset2, "reset=" + resetOk + " snap=" + snap + " immut=" + immutable + " reset2=" + reset2);
            }

            // ---- P6 RasterChangeDetector == (RingStyle.Key() + S + cx.ToString("R") + cy.ToString("R")) ----
            {
                RasterChangeDetector det = new RasterChangeDetector();
                string lastKey = null;
                int mismatches = 0; string firstBad = "";
                perfRng = 4242u;
                RingStyle s = new RingStyle();
                FieldInfo[] fields = typeof(RingStyle).GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < 200000; i++)
                {
                    uint x = PerfNext();
                    int S = 40 + (int)(x % 3) * 2;
                    double cx = 20 + ((x >> 4) % 3) * 0.125, cy = 20 + ((x >> 6) % 3) * 0.125;
                    if ((x >> 8) % 4 == 0) PerfMutate(s, fields, PerfNext());
                    string key = s.Key() + "|" + S + "|" + cx.ToString("R") + "|" + cy.ToString("R");
                    bool want = key != lastKey;
                    bool got = det.Changed(s, S, cx, cy);
                    if (want != got) { mismatches++; if (firstBad.Length == 0) firstBad = "i=" + i + " want=" + want + " got=" + got + " key=" + key; }
                    lastKey = key;
                    if (i % 50000 == 49999) { det.Reset(); lastKey = null; }
                }
                Check("PERF P6 raster change detector agrees with the old string key (200k frames, random styles)", mismatches == 0,
                    "mismatches=" + mismatches + " " + firstBad);

                // every raster-relevant RingStyle field must be watched (guards fields added later); OpacityPct is applied by
                // the window, PngMissing is derived (Kind "" + Png null), so neither may trigger a re-render.
                List<string> missed = new List<string>();
                foreach (FieldInfo fi in fields)
                {
                    RingStyle a = new RingStyle();
                    RasterChangeDetector d = new RasterChangeDetector();
                    d.Changed(a, 40, 20, 20);
                    string before = a.Key();
                    PerfSetDifferent(a, fi);
                    bool keyChanged = a.Key() != before;
                    bool detChanged = d.Changed(a, 40, 20, 20);
                    if (keyChanged != detChanged) missed.Add(fi.Name + "(key " + keyChanged + ", detector " + detChanged + ")");
                }
                Check("PERF P6b detector watches exactly the fields RingStyle.Key() covers", missed.Count == 0, string.Join(", ", missed));
                RasterChangeDetector d2 = new RasterChangeDetector();
                RingStyle q = new RingStyle();
                bool firstTrue = d2.Changed(q, 40, 20, 20), sameFalse = !d2.Changed(q, 40, 20, 20),
                    sChanged = d2.Changed(q, 42, 20, 20), cxChanged = d2.Changed(q, 42, 20.125, 20), cyChanged = d2.Changed(q, 42, 20.125, 20.25);
                d2.Reset();
                bool afterReset = d2.Changed(q, 42, 20.125, 20.25);
                Check("PERF P6c detector: first frame, same frame, S/cx/cy change, Reset", firstTrue && sameFalse && sChanged && cxChanged && cyChanged && afterReset,
                    firstTrue + " " + sameFalse + " " + sChanged + " " + cxChanged + " " + cyChanged + " " + afterReset);
            }

            // ---- P7 overlay per-frame style work (RingStyle.Set + CanvasSize + detector) allocates nothing ----
            {
                Profile p; GlobalSettings g;
                PerfSetup(out p, out g, 0);
                p.HandColorOn = true;
                RingStyle st = new RingStyle();
                RasterChangeDetector det = new RasterChangeDetector();
                long setBytes = PerfAllocated(() =>
                {
                    for (int i = 0; i < PerfFrames; i++) st.Set(p, (i % 200) / 200.0, (i / 1000) % 2 == 1);
                });
                long detBytes = PerfAllocated(() =>
                {
                    for (int i = 0; i < PerfFrames; i++)
                    {
                        int S = RingRaster.CanvasSize(st);
                        det.Changed(st, S, 20 + (i % 8) * 0.125, 20);
                    }
                });
                Check("PERF P7 per-frame raster change check (CanvasSize + detector) allocates 0 bytes over 100k frames", detBytes == 0, "bytes=" + detBytes);
                Console.WriteLine("PERF info RingStyle.Set (RingRaster.cs, not a PERF file) bytes over 100k frames = " + setBytes);
                Check("PERF P7b RingStyle.Set allocates 0 bytes over 100k frames", setBytes == 0, "bytes=" + setBytes);
            }

            // ---- P8 frame time ----
            {
                Profile p; GlobalSettings g;
                PerfSetup(out p, out g, 7);
                PerfKeys keys = new PerfKeys();
                Func<int, bool> isDown = keys.IsDown;
                MotionEngine e = new MotionEngine();
                LiveInputFilter f = new LiveInputFilter();
                RingStyle st = new RingStyle();
                RasterChangeDetector det = new RasterChangeDetector();
                perfRng = 99u;
                for (int i = 0; i < 20000; i++) { PerfStep(keys); PerfFrame(e, f, g.Keys, isDown, p, g, PreviewMode.Live, 1.0 / 144.0); }
                int gc0 = GC.CollectionCount(0);
                Stopwatch sw = Stopwatch.StartNew();
                int changed = 0;
                for (int i = 0; i < PerfFrames; i++)
                {
                    PerfStep(keys);
                    PerfFrame(e, f, g.Keys, isDown, p, g, PreviewMode.Live, 1.0 / 144.0);
                    st.Set(p, e.HandBlend, e.Boosted);
                    int S = RingRaster.CanvasSize(st);
                    double X = 960 + e.OffsetX, Y = 540 + e.OffsetY;
                    int wx = (int)Math.Floor(X) - S / 2, wy = (int)Math.Floor(Y) - S / 2;
                    if (det.Changed(st, S, Math.Round((X - wx) * 8.0) / 8.0, Math.Round((Y - wy) * 8.0) / 8.0)) changed++;
                }
                sw.Stop();
                double us = sw.Elapsed.TotalMilliseconds * 1000.0 / PerfFrames;
                int gcs = GC.CollectionCount(0) - gc0;
                Console.WriteLine("PERF info frame (input+engine+style+change check, no raster/Win32): " + us.ToString("0.000", CultureInfo.InvariantCulture)
                    + " us avg over 100k frames, " + changed + " re-rasters, gen0 GCs=" + gcs);
                Check("PERF P8 frame cost < 20 us avg (144 Hz budget 6944 us) and no gen0 GC", us < 20 && gcs == 0,
                    "us=" + us.ToString("0.000", CultureInfo.InvariantCulture) + " gen0=" + gcs);
            }
        }

        static void PerfMutate(RingStyle s, FieldInfo[] fields, uint x)
        {
            FieldInfo fi = fields[x % (uint)fields.Length];
            if (fi.Name == "Png") { s.Png = null; return; }
            if ((x >> 16) % 3 == 0) return; // sometimes no change (key must stay equal)
            PerfSetDifferent(s, fi);
        }

        static void PerfSetDifferent(RingStyle s, FieldInfo fi)
        {
            object v = fi.GetValue(s);
            Type t = fi.FieldType;
            if (t == typeof(double)) fi.SetValue(s, (double)v + 0.5);
            else if (t == typeof(int)) fi.SetValue(s, (int)v + 1);
            else if (t == typeof(bool)) fi.SetValue(s, !(bool)v);
            else if (t == typeof(string)) fi.SetValue(s, ((string)v ?? "") + "x");
            else if (t == typeof(PngImage)) fi.SetValue(s, PerfPng());
        }

        static PngImage PerfPng()
        {
            foreach (ConstructorInfo c in typeof(PngImage).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ParameterInfo[] ps = c.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    Type t = ps[i].ParameterType;
                    if (t == typeof(int[])) args[i] = new int[4];
                    else if (t == typeof(int)) args[i] = 2;
                    else if (t == typeof(string)) args[i] = "perf-id";
                    else args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
                }
                try { return (PngImage)c.Invoke(args); } catch (Exception) { }
            }
            return null;
        }
    }
}
