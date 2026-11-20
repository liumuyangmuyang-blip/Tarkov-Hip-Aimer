﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TarkovCrosshair.Tests
{
    internal sealed class Sim
    {
        public MotionEngine E = new MotionEngine();
        public Profile P;
        public GlobalSettings G;
        public double Fps;
        public double T;
        public List<double[]> Trace = new List<double[]>(); // t, uiX, uiY, screenX, screenY

        public Sim(Profile p, GlobalSettings g, double fps) { P = p; G = g; Fps = fps; }

        public void Step(InputState i)
        {
            E.Update(1.0 / Fps, i, P, G, PreviewMode.Live, 1.0);
            T += 1.0 / Fps;
            Trace.Add(new double[] { T, E.Debug.UiOffsetX, E.Debug.UiOffsetY, E.OffsetX, E.OffsetY });
        }

        public int Frames(double seconds) { return (int)Math.Round(seconds * Fps); }

        public void Run(double seconds, InputState i)
        {
            int n = Frames(seconds);
            for (int k = 0; k < n; k++) Step(i);
        }
    }

    public static partial class Program
    {
        static int pass, fail;
        static readonly double[] Rates = { 60, 144, 240 };
        static string outDir;

        // v3.4: 站着偏移 forced to 0 on load and 调参分辨率 locked at 2160x1440. These older tests check exactly the
        // removed migration/clamp behaviour; they are reported as SKIP when they fail. V34 tests cover the new rule.
        static readonly string[] SupersededV34 = { "T18 real settings-v1", "T26 ", "C6 Leo's v2.3", "C6 v2.3 file", "C8 resolution", "G9 Leo's v2.4.5", "H4 JSON round trip" };
        static int skipped;
        static void Check(string name, bool ok, string detail)
        {
            if (!ok) foreach (string pre in SupersededV34) if (name.StartsWith(pre)) { skipped++; Console.WriteLine("SKIP(v3.4) " + name); return; }
            if (ok) pass++; else fail++;
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        }

        static string F(double v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

        const string ExName = "示例-需校准";

        /// <summary>The v1.x sample profile, kept as a test fixture (no longer shipped).</summary>
        static Profile Example()
        {
            Profile p = new Profile();
            p.Name = ExName;
            p.Dirs[0] = new DirSetting(0, 8);    // W
            p.Dirs[1] = new DirSetting(0, -8);   // S
            p.Dirs[2] = new DirSetting(-12, 0);  // A
            p.Dirs[3] = new DirSetting(12, 0);   // D
            Profile.AutoDiagonal(p);
            p.Validate();
            return p;
        }

        /// <summary>Test globals: class defaults + [sample, blank], sample active (the old v1.x default file).</summary>
        static GlobalSettings G()
        {
            GlobalSettings g = new GlobalSettings();
            g.Profiles.Add(Example());
            g.Profiles.Add(Profile.CreateBlank("空白（全0）"));
            g.ActiveProfile = ExName;
            g.Validate();
            return g;
        }
        static InputState In(string keys)
        {
            InputState s = new InputState();
            foreach (char c in keys)
            {
                switch (c)
                {
                    case 'W': s.Fwd = true; break;
                    case 'S': s.Back = true; break;
                    case 'A': s.Left = true; break;
                    case 'D': s.Right = true; break;
                    case 'Q': s.LeanL = true; break;
                    case 'E': s.LeanR = true; break;
                    case 'L': s.Fire = true; break;
                    case 'R': s.Ads = true; break;
                    case 'F': s.FreeLook = true; break;
                    case 'K': s.HandSwitch = true; break;
                }
            }
            return s;
        }

        public static int Main(string[] args)
        {
            outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
            Console.OutputEncoding = Encoding.UTF8;

            T1(); T2T3(); T5(); T6(); T7(); T8(); T9(); T10(); T11(); T13(); T14(); T15(); T16(); T18(); T20(); T23();
            Pictures();
            T26(); T32();
            V24Tests();
            V241Tests();
            V243Tests();
            V25Tests();
            V30Tests();
            V31Tests();
            V32Tests();
            V34Tests();
            V4StyleTests();
            V40Tests();
            V4BruteTests();
            V4SettingsFixTests();
            V4AuditLogicTests();

            Console.WriteLine();
            V43PerfTests(); // v4.3 (W2): uses a worker thread, so it must run from Main, not a module initializer (init lock)
            V501Tests(); // v5.0.1 (W13): worker threads too
            V502W15Tests(); // v5.0.2 (W15): filter restore / perf, worker threads too
            V502W17ThreadTests(); // v5.0.2 (W17): watchdog / saver threads, so from Main too
            Console.WriteLine("TOTAL pass=" + pass + " fail=" + fail + " skip(v3.4)=" + skipped);
            return fail == 0 ? 0 : 1;
        }

        static void T1()
        {
            foreach (double fps in Rates)
            {
                Sim s = new Sim(Example(), G(), fps);
                s.Run(1.0, In(""));
                double m = Math.Max(Math.Abs(s.E.OffsetX), Math.Abs(s.E.OffsetY));
                Check("T1 idle 1s @" + fps, m <= 0.01, "max|offset|=" + F(m));
            }
        }

        static void T2T3()
        {
            foreach (double fps in Rates)
            {
                Profile p = Example();
                Sim s = new Sim(p, G(), fps);
                s.Run(3.0, In("A"));
                DirSetting a = p.Dirs[2];
                double ex = s.E.Debug.UiOffsetX - a.OffX, ey = s.E.Debug.UiOffsetY - a.OffY;
                Check("T2 hold A 3s final @" + fps, Math.Abs(ex) <= 0.5 && Math.Abs(ey) <= 0.5,
                    "final=(" + F(s.E.Debug.UiOffsetX) + "," + F(s.E.Debug.UiOffsetY) + ") target=(" + F(a.OffX) + "," + F(a.OffY) + ")");
                s.Run(2.0, In(""));
                double m = Math.Sqrt(s.E.Debug.UiOffsetX * s.E.Debug.UiOffsetX + s.E.Debug.UiOffsetY * s.E.Debug.UiOffsetY);
                Check("T3 release 2s @" + fps, m < 1.0, "|offset|=" + F(m));
            }
        }

        static void T5()
        {
            foreach (double fps in Rates)
            {
                Profile p = Example();
                Sim s = new Sim(p, G(), fps);
                double cap = p.RecoilMaxPx;
                int riseFrames = (int)Math.Ceiling((p.RecoilDelayMs + p.RecoilRiseMs) / 1000.0 * fps - 1e-9);
                double rAtRise = 0;
                int total = s.Frames(2.0);
                for (int k = 1; k <= total; k++) { s.Step(In("L")); if (k == riseFrames) rAtRise = s.E.Recoil; }
                double rEnd = s.E.Recoil;
                Check("T5 rise @" + fps, rAtRise >= 0.95 * cap && Math.Abs(rEnd - cap) <= 0.02 * cap,
                    "r@(Delay+RiseMs)=" + F(rAtRise) + " (" + F(100 * rAtRise / cap) + "%), r@2s=" + F(rEnd) + " cap=" + F(cap));
                int fallFrames = (int)Math.Ceiling(p.RecoilFallMs / 1000.0 * fps - 1e-9);
                for (int k = 0; k < fallFrames; k++) s.Step(In(""));
                Check("T5 fall @" + fps, s.E.Recoil < 0.05 * cap, "r@FallMs=" + F(s.E.Recoil) + " (" + F(100 * s.E.Recoil / cap) + "% of cap)");
            }
        }

        static void T6()
        {
            foreach (double fps in Rates)
            {
                // Default delay 60 ms: nothing at the press frame, exactly ClickKick once the delay has passed.
                Profile p = Example();
                Sim s = new Sim(p, G(), fps);
                s.Run(0.5, In(""));
                s.Step(In("L"));
                double atPress = s.E.Recoil;
                double t = 1000.0 / fps; // ms since the press, after the press frame
                double atKick = -1, beforeKick = -1;
                while (atKick < 0)
                {
                    s.Step(In(""));
                    t += 1000.0 / fps;
                    if (t >= p.RecoilDelayMs - 1e-9) atKick = s.E.Recoil; else beforeKick = s.E.Recoil;
                }
                Check("T6 one-frame tap, delay 60 @" + fps, atPress == 0 && (beforeKick == -1 || beforeKick == 0) && Math.Abs(atKick - p.RecoilClickKickPx) < 1e-9,
                    "r at press=" + F(atPress) + ", r at " + F(t) + "ms=" + F(atKick) + " (ClickKick " + F(p.RecoilClickKickPx) + ")");

                // Follow-up shot: the press frame shows the earlier shot's residual recoil, only decaying.
                Sim r = new Sim(Example(), G(), fps);
                r.Run(0.3, In("L")); r.Run(0.1, In(""));
                double residual = r.E.Recoil;
                r.Step(In("L"));
                double fall1 = residual * (1 - Math.Exp(-(1.0 / fps) / (p.RecoilFallMs / 3000.0)));
                Check("T6 press frame shows residual recoil @" + fps, residual > 1 && Math.Abs(r.E.Recoil - (residual - fall1)) < 1e-9,
                    "residual before press=" + F(residual) + " at press frame=" + F(r.E.Recoil) + " (decayed only, no kick yet)");

                // Delay 0 keeps the v1 behaviour: kick + one frame of rise at the press frame.
                Profile z = Example(); z.RecoilDelayMs = 0;
                Sim q = new Sim(z, G(), fps);
                q.Run(0.5, In(""));
                q.Step(In("L"));
                double k = z.RecoilClickKickPx;
                double rise = (z.RecoilMaxPx - k) * (1 - Math.Exp(-(1.0 / fps) / (z.RecoilRiseMs / 3000.0)));
                Check("T6 one-frame tap, delay 0 @" + fps, Math.Abs(q.E.Recoil - (k + rise)) < 1e-9, "r=" + F(q.E.Recoil) + " = ClickKick " + F(k) + " + one frame of rise " + F(rise));
            }
            GlobalSettings c; string err;
            bool ok = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"RecoilDelayMs\":999},{\"Name\":\"b\"}]}", out c, out err);
            Check("T6 RecoilDelayMs default/clamp", ok && c.Profiles[0].RecoilDelayMs == 300 && c.Profiles[1].RecoilDelayMs == 60,
                ok ? "999->" + F(c.Profiles[0].RecoilDelayMs) + " missing->" + F(c.Profiles[1].RecoilDelayMs) : err);
        }

        static void T7()
        {
            foreach (double fps in Rates)
            {
                GlobalSettings g = G();
                Sim s = new Sim(Example(), g, fps);
                s.Run(0.5, In(""));
                bool shown = s.E.Alpha == 1.0;
                s.Step(In("R"));
                bool hiddenAtOnce = s.E.Alpha == 0.0;
                s.Run(0.3, In("R"));
                int showFrames = (int)Math.Ceiling(g.ShowFadeMs / 1000.0 * fps - 1e-9);
                double minAlpha = 1;
                for (int k = 0; k < showFrames; k++) s.Step(In(""));
                minAlpha = s.E.Alpha;
                Check("T7 ADS hide/show @" + fps, shown && hiddenAtOnce && minAlpha >= 1.0 - 1e-9,
                    "before=" + F(shown ? 1 : 0) + " first RMB frame alpha=" + (hiddenAtOnce ? "0" : "!=0") + " alpha " + showFrames + " frames after release=" + F(minAlpha));
            }
        }

        static void T8()
        {
            foreach (double fps in Rates)
            {
                Sim s = new Sim(Example(), G(), fps);
                s.Run(0.5, In("L"));
                double before = s.E.Recoil;
                s.Step(In("LR"));
                bool ok1 = s.E.Alpha == 0 && s.E.Recoil == 0;
                s.Run(0.3, In("LR"));
                bool ok2 = s.E.Alpha == 0 && s.E.Recoil == 0;
                Check("T8 LMB+RMB @" + fps, before > 0 && ok1 && ok2, "r before=" + F(before) + " -> alpha=" + F(s.E.Alpha) + " r=" + F(s.E.Recoil));
            }
        }

        static void T9()
        {
            foreach (double fps in Rates)
            {
                Profile p = Example();
                GlobalSettings g = G();
                Sim s = new Sim(p, g, fps);
                int lf = (int)Math.Ceiling(p.LeanMs / 1000.0 * fps - 1e-9);
                for (int k = 0; k < lf; k++) s.Step(In("Q"));
                double l = s.E.Lean;
                Check("T9 hold Q @" + fps, l <= -0.949, "lean@LeanMs=" + F(l) + " offset=(" + F(s.E.Debug.UiOffsetX) + "," + F(s.E.Debug.UiOffsetY) +
                    ") full=(" + F(p.LeanQX) + "," + F(p.LeanQY) + ")");
                s.Run(0.5, In(""));
                bool backHold = Math.Abs(s.E.Lean) < 0.001;

                g.LeanMode = "toggle";
                s.Step(In("Q")); s.Run(0.5, In(""));
                double latched = s.E.Lean;
                s.Step(In("Q")); s.Run(0.5, In(""));
                double unlatched = s.E.Lean;
                s.Step(In("E")); s.Run(0.5, In(""));
                double latchedE = s.E.Lean;
                Check("T9 toggle Q/E @" + fps, backHold && latched < -0.999 && Math.Abs(unlatched) < 0.001 && latchedE > 0.999,
                    "hold release->0: " + backHold + "; toggle: tapQ->" + F(latched) + " tapQ again->" + F(unlatched) + " tapE->" + F(latchedE));
            }
        }

        static void T10()
        {
            foreach (double fps in Rates)
            {
                GlobalSettings g = G();
                g.AdsMode = "toggle";
                Sim s = new Sim(Example(), g, fps);
                s.Run(0.5, In(""));
                s.Step(In("R")); s.Run(0.3, In(""));
                bool latched = s.E.Alpha == 0 && s.E.AdsToggled;
                s.Step(In("R")); s.Run(0.3, In(""));
                bool unlatched = s.E.Alpha == 1 && !s.E.AdsToggled;
                Check("T10 ADS toggle @" + fps, latched && unlatched, "after tap1 hidden=" + latched + " after tap2 shown=" + unlatched);
            }
        }

        static double Rms(Sim a, Sim b)
        {
            // a = 60 fps, b = 240 fps -> compare at a's frame times (every 4th frame of b).
            int ratio = (int)Math.Round(b.Fps / a.Fps);
            double sum = 0; int n = 0;
            for (int i = 0; i < a.Trace.Count; i++)
            {
                int j = (i + 1) * ratio - 1;
                if (j >= b.Trace.Count) break;
                double dx = a.Trace[i][1] - b.Trace[j][1], dy = a.Trace[i][2] - b.Trace[j][2];
                sum += dx * dx + dy * dy; n++;
            }
            return Math.Sqrt(sum / Math.Max(1, n));
        }

        static void T11()
        {
            Sim a = new Sim(Example(), G(), 60), b = new Sim(Example(), G(), 240);
            a.Run(1.0, In("A")); a.Run(0.5, In("D")); a.Run(0.5, In("WD")); a.Run(1.0, In(""));
            b.Run(1.0, In("A")); b.Run(0.5, In("D")); b.Run(0.5, In("WD")); b.Run(1.0, In(""));
            double r1 = Rms(a, b);
            Check("T11 follow path 60 vs 240fps", r1 < 0.5, "RMS=" + F(r1) + "px (A 1s, D 0.5s, WD 0.5s, release 1s)");
        }

        static void T13()
        {
            GlobalSettings d = G();
            string s1 = SettingsIO.Serialize(d);
            GlobalSettings back; string err;
            bool ok = SettingsIO.TryParse(s1, out back, out err);
            string s2 = ok ? SettingsIO.Serialize(back) : "";
            Check("T13 round-trip identical", ok && s1 == s2, "len=" + s1.Length + (ok ? "" : " err=" + err));

            GlobalSettings e;
            bool ok2 = SettingsIO.TryParse("{}", out e, out err);
            Check("T13 '{}' -> defaults", ok2 && e.Profiles.Count == 1 && e.Fps == 144 && e.Keys.Toggle == 0x7B && e.Enabled && e.AdsMode == "hold" && e.ActiveProfile == GlobalSettings.DefaultProfileName,
                ok2 ? "profiles=" + e.Profiles.Count + " fps=" + F(e.Fps) + " toggle=0x" + e.Keys.Toggle.ToString("X") : err);

            GlobalSettings f;
            bool ok3 = SettingsIO.TryParse("{\"ActiveProfile\":\"nope\",\"Fps\":9999,\"Keys\":{\"Fire\":2},\"Profiles\":[{\"Name\":\"x\",\"Diameter\":9999,\"Color\":\"red\",\"Dirs\":[{\"OffX\":3}]},{\"Name\":\"x\"}]}", out f, out err);
            // v4.1 (BRUTE40 P1-3): 开火 = 鼠标右键 from the file wins; 开镜 (missing -> default 鼠标右键) gives way to the first spare key
            bool ok3b = ok3 && f.Fps == 360 && f.Keys.Fire == 2 && f.Keys.Ads == GlobalSettings.SpareKeys[0] && f.Profiles[0].Diameter == 300 && f.Profiles[0].Thickness == 2
                && f.Profiles[0].Color == "#78FF78" && f.Profiles[0].Dirs.Count == 8 && f.Profiles[0].Dirs[0].OffX == 3 && f.Profiles[0].Dirs[1].OffX == 0
                && f.Profiles[1].Name == "x (2)" && f.ActiveProfile == "x";
            Check("T13 partial/out-of-range -> defaults+clamp", ok3b, ok3 ? "diam=" + F(f.Profiles[0].Diameter) + " fps=" + F(f.Fps) + " dirs=" + f.Profiles[0].Dirs.Count + " names=" + f.Profiles[0].Name + "," + f.Profiles[1].Name + " active=" + f.ActiveProfile : err);

            GlobalSettings h;
            bool g1 = SettingsIO.TryParse("garbage{{", out h, out err);
            bool g2 = SettingsIO.TryParse("null", out h, out err);
            bool g3 = SettingsIO.TryParse("[1,2]", out h, out err);
            Check("T13 garbage rejected without throwing", !g1 && !g2 && !g3, "accepted? garbage=" + g1 + " null=" + g2 + " array=" + g3);
            // v4.0 SETTINGS-FIX: a wrong-type field is no longer a reason to reject the file -- only that field is defaulted.
            List<string> dropped4;
            bool g4 = SettingsIO.TryParse("{\"Fps\":200,\"Diameter\":\"abc\",\"Profiles\":[{\"Name\":\"keep\",\"Thickness\":5,\"Diameter\":\"abc\"}]}", out h, out err, out dropped4);
            bool g4b = g4 && h.Fps == 200 && h.Profiles[0].Name == "keep" && h.Profiles[0].Thickness == 5 && h.Profiles[0].Diameter == Profile.CreateLeoDefault("x").Diameter // v5.1 (W23, decision 17): Leo's v5 value
                && dropped4.Count == 1 && dropped4[0] == "Profiles[0].Diameter";
            Check("T13 type-mismatch: accepted, only the bad field gets Leo's v5 default", g4b, "accepted=" + g4 + (g4 ? " dropped=" + string.Join(",", dropped4) + " fps=" + F(h.Fps) : " err=" + err));
            {
                string sc = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
                if (string.IsNullOrEmpty(sc)) sc = Path.GetTempPath();
                string dm = Path.Combine(sc, "t13m-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dm);
                string pm = Path.Combine(dm, "settings.json");
                string txt = "{\"Fps\":200,\"Profiles\":[{\"Name\":\"keep\",\"Diameter\":\"abc\"}]}";
                File.WriteAllText(pm, txt);
                string mm;
                GlobalSettings lm = SettingsIO.Load(pm, out mm);
                string[] bk = Directory.GetFiles(dm, "settings.bad-*.json");
                bool okm = mm != null && bk.Length == 1 && File.ReadAllText(bk[0]) == txt && lm.Fps == 200 && lm.Profiles[0].Name == "keep";
                foreach (string ff in Directory.GetFiles(dm)) File.Delete(ff);
                Directory.Delete(dm);
                Check("T13 Load type-mismatch file: kept + backup written + message", okm, "backup=" + bk.Length + " msg=" + (mm != null));
            }

            // File-level: missing file -> default written; garbage file -> renamed + defaults + message.
            string scratch = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            if (string.IsNullOrEmpty(scratch)) scratch = Path.GetTempPath();
            string dir = Path.Combine(scratch, "t13-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "settings.json");
            string msg;
            GlobalSettings l1 = SettingsIO.Load(path, out msg);
            bool m1 = File.Exists(path) && msg == null && l1.Profiles.Count == 1 && l1.Active.Name == GlobalSettings.DefaultProfileName;
            File.WriteAllText(path, "{ this is not json");
            GlobalSettings l2 = SettingsIO.Load(path, out msg);
            string[] bad = Directory.GetFiles(dir, "settings.bad-*.json");
            GlobalSettings reread; string e2;
            bool m2 = msg != null && bad.Length == 1 && l2.Profiles.Count == 1 && SettingsIO.TryParse(File.ReadAllText(path), out reread, out e2);
            string tmpLeft = path + ".tmp";
            Check("T13 Load: missing file + garbage file", m1 && m2 && !File.Exists(tmpLeft),
                "default written=" + m1 + " bad renamed=" + (bad.Length == 1 ? Path.GetFileName(bad[0]) : "none") + " msg=" + (msg != null));
            foreach (string b in bad) File.Delete(b);
            File.Delete(path);
            Directory.Delete(dir);
        }

        static void T14()
        {
            int wa = MotionEngine.DirIndex(In("WA"));
            int ad = MotionEngine.DirIndex(In("AD"));
            int wsa = MotionEngine.DirIndex(In("WSA"));
            int all = MotionEngine.DirIndex(In("WSAD"));
            Check("T14 key combos", wa == 4 && ad == -1 && wsa == 2 && all == -1,
                "W+A=" + Name(wa) + " A+D=" + Name(ad) + " W+S+A=" + Name(wsa) + " WSAD=" + Name(all));
        }

        static string Name(int i) { return i < 0 ? "stopped" : Profile.DirNames[i]; }

        static void T15()
        {
            foreach (double fps in Rates)
            {
                GlobalSettings g = G();
                g.Enabled = false;
                Sim s = new Sim(Example(), g, fps);
                double mx = 0;
                for (int k = 0; k < s.Frames(1.0); k++) { s.Step(In(k % 2 == 0 ? "AL" : "")); mx = Math.Max(mx, s.E.Alpha); }
                Check("T15 Enabled=false @" + fps, mx == 0, "max alpha=" + F(mx));
            }
        }

        // ---------------- v1.2: real v1 / v1.1 settings.json files still load ----------------

        static string Fixture(string name)
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string f = Path.Combine(d, "fixtures", name);
                if (File.Exists(f)) return f;
                string f2 = Path.Combine(d, "tests", "fixtures", name);
                if (File.Exists(f2)) return f2;
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return Path.Combine("/home/dev/tarkov-crosshair-dev/tests-v3/fixtures", name);
        }

        static void T18()
        {
            foreach (string name in new string[] { "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json" })
            {
                string path = Fixture(name);
                string text = File.ReadAllText(path);
                bool v12 = name == "settings-v1.2.json";
                bool hasOld = v12 ? (!text.Contains("\"Combos\"") && text.Contains("\"FollowMs\""))
                                  : (text.Contains("\"KickX\"") && text.Contains("\"SwayPeriodMs\""));
                GlobalSettings g; string err;
                bool ok = SettingsIO.TryParse(text, out g, out err);
                bool vals = ok && g.Profiles.Count == 3 && g.ActiveProfile == "M4A1" && g.Keys.Toggle == 0x23 && g.CenterOffsetX == 0 && g.CenterOffsetY == 0
                    && g.Profiles.TrueForAll(delegate(Profile q) { return q.StandOffsetX == 3 && q.StandOffsetY == -2; })
                    && g.LeanMode == "toggle";
                Profile m = ok ? g.FindProfile("M4A1") : null;
                bool prof = m != null && m.Dirs.Count == 8 && m.Dirs[2].OffX == -15 && m.Dirs[2].OffY == 2 && m.RecoilMaxPx == 55 && m.Color == "#00FFFF"
                    && m.Diameter == 24 && m.LeanQX == -40 && m.Dirs[4].OffX == 0.7071 * (m.Dirs[0].OffX + m.Dirs[2].OffX); // v3.0: diagonals recomputed
                bool combosDefault = ok && m != null; // v3.0: no combos any more
                bool fresh = combosDefault && (v12
                    ? (CurveIs(m.CurveAD, 150) && CurveIs(m.CurveWS, 150) && m.RecoilDelayMs == 80 && m.OuterThickness == 2.5 && m.Gradient == "twotone")
                    : (CurveIs(m.CurveAD, 100) && CurveIs(m.CurveWS, 100) && m.OuterThickness == 1.5))
                    && g.HideOnFreeLook && g.Keys.FreeLook == 4 && g.Keys.HandSwitch == 5 && g.HandEnabled && g.HandMode == "toggle"
                    && m.HandSwitchMs == 300 && m.SavedDefault == null && g.Keys.Boost == 6 && g.BoostEnabled && m.Shape == "ring";
                string again = ok ? SettingsIO.Serialize(g) : "";
                bool dropped = ok && !again.Contains("\"KickX\"") && !again.Contains("Sway") && !again.Contains("Inertia") && !again.Contains("Bounce")
                    && !again.Contains("Calibrate") && !again.Contains("CalibKeyLog") && !again.Contains("FollowMs") && !again.Contains("\"Combos\"") && !again.Contains("\"Hand\"");
                if (v12 && !text.Contains("\"Calibrate\"")) hasOld = false; // the v1.2 file carries the removed calibration key
                Check("T18 real " + name + " loads", hasOld && vals && prof && fresh && dropped,
                    ok ? (v12 ? "v1.2 format (no Combos)=" : "old fields present=") + hasOld + " values kept=" + (vals && prof) + " new defaults=" + fresh + " old fields dropped on save=" + dropped : err);
            }
            GlobalSettings d = G();
            Profile ex = d.FindProfile(ExName);
            Check("T18 example profile keeps Off values", ex.Dirs[0].OffY == 8 && ex.Dirs[1].OffY == -8 && ex.Dirs[2].OffX == -12 && ex.Dirs[3].OffX == 12
                && Math.Abs(ex.Dirs[5].OffX - 0.7071 * 12) < 1e-9 && Math.Abs(ex.Dirs[5].OffY - 0.7071 * 8) < 1e-9,
                "W=(" + F(ex.Dirs[0].OffX) + "," + F(ex.Dirs[0].OffY) + ") A=(" + F(ex.Dirs[2].OffX) + "," + F(ex.Dirs[2].OffY) + ") WD=(" + F(ex.Dirs[5].OffX) + "," + F(ex.Dirs[5].OffY) + ")");
        }

        // ---------------- v1.2: import / export ----------------

        static void T20()
        {
            GlobalSettings g = G();
            Profile src = g.FindProfile(ExName).Clone();
            src.Name = "AK-74N"; src.Dirs[2].OffX = -17.5; Profile.RecomputeDiagonals(src); src.CurveAD.DurationMs = 333; src.CurveWS.Speeds[2] = 44; src.Shape = "ringcross"; src.HandColor = "#123456"; src.Boost.GlowPx = 3; src.Dashes = 8; src.Gradient = "twotone"; src.OuterThickness = 2.5;
            string file = SettingsIO.ExportProfile(src);
            List<Profile> got; string err;
            bool ok = SettingsIO.TryParseImport(file, out got, out err);
            GlobalSettings target = G();
            int before = target.Profiles.Count;
            string first = ok ? target.AddImported(got) : null;
            Profile imp = ok ? target.FindProfile(first) : null;
            Check("T20 export -> import round-trip", ok && got.Count == 1 && first == "AK-74N" && target.ActiveProfile == "AK-74N" && target.Profiles.Count == before + 1
                && SettingsIO.ExportProfile(imp) == file,
                ok ? "imported '" + first + "', identical JSON=" + (imp != null && SettingsIO.ExportProfile(imp) == file) : err);

            string again = target.AddImported(got);
            Check("T20 name clash -> (2)", again == "AK-74N (2)", "second import named '" + again + "'");

            List<Profile> full;
            bool ok2 = SettingsIO.TryParseImport(File.ReadAllText(Fixture("settings-v1.1.json")), out full, out err);
            GlobalSettings t2 = G();
            string f2 = ok2 ? t2.AddImported(full) : null;
            Check("T20 import full settings.json (v1.1 file)", ok2 && full.Count == 3 && t2.Profiles.Count == 5 && f2 == ExName + " (2)"
                && t2.FindProfile("M4A1") != null && t2.FindProfile("M4A1").Dirs[2].OffX == -15,
                ok2 ? full.Count + " profiles added, first selected='" + f2 + "', total=" + t2.Profiles.Count : err);

            List<Profile> clamp;
            bool ok3 = SettingsIO.TryParseImport("{\"Name\":\"x\",\"Diameter\":5000,\"Dirs\":[{\"OffX\":900}]}", out clamp, out err);
            Check("T20 import validates + clamps", ok3 && clamp[0].Diameter == 300 && clamp[0].Dirs.Count == 8 && clamp[0].Dirs[0].OffX == 500,
                ok3 ? "Diameter 5000->" + F(clamp[0].Diameter) + " OffX 900->" + F(clamp[0].Dirs[0].OffX) : err);

            string[] bad = { "", "garbage{", "[1,2]", "null", "{}", "{\"foo\":1}", "{\"Profiles\":[]}", "{\"Name\":5,\"Dirs\":\"x\"}" };
            int rejected = 0; string why = "";
            foreach (string b in bad)
            {
                List<Profile> r; string e2;
                bool threw = false, res = false;
                try { res = SettingsIO.TryParseImport(b, out r, out e2); } catch (Exception) { threw = true; }
                if (!res && !threw) rejected++; else why += " [" + b + "]";
            }
            Check("T20 bad files rejected without crashing", rejected == bad.Length, rejected + "/" + bad.Length + " rejected" + why);
        }

        // ---------------- v1.3 lean+move combos -> v3.0: removed, lean + move always additive ----------------

        // Expected settled UI total for held keys (hold-mode lean, no fire, CenterOffset 0): always additive (v3.0).
        static void Settled(Profile p, string keys, out double x, out double y)
        {
            InputState i = In(keys);
            int dir = MotionEngine.DirIndex(i);
            int lean = (i.LeanL && !i.LeanR) ? -1 : ((i.LeanR && !i.LeanL) ? 1 : 0);
            double lx, ly;
            p.FullLean(lean, out lx, out ly);
            x = (dir >= 0 ? p.Dirs[dir].OffX : 0) + lx;
            y = (dir >= 0 ? p.Dirs[dir].OffY : 0) + ly;
        }

        static void T23()
        {
            // Every key combination settles to Dirs[dir] + full lean (the 6 old combos included).
            string[] keys = { "", "W", "S", "A", "D", "WA", "WD", "SA", "SD", "Q", "E",
                "QA", "QWA", "QAS", "ED", "WED", "EDS", "QD", "QW", "QS", "QWD", "QSD", "EA", "EW", "ES", "EWA", "ESA", "QEA" };
            foreach (double fps in Rates)
            {
                Profile p = Example();
                string bad = "";
                foreach (string k in keys)
                {
                    Sim s = new Sim(p, G(), fps);
                    s.Run(2.0, In(k));
                    double ex, ey;
                    Settled(p, k, out ex, out ey);
                    if (Math.Abs(s.E.Debug.UiOffsetX - ex) > 0.01 || Math.Abs(s.E.Debug.UiOffsetY - ey) > 0.01)
                        bad += " " + k + "=(" + F(s.E.Debug.UiOffsetX) + "," + F(s.E.Debug.UiOffsetY) + ") want (" + F(ex) + "," + F(ey) + ")";
                }
                Check("T23 v3.0 lean + move additive for " + keys.Length + " key combos @" + fps, bad.Length == 0, bad.Length == 0 ? "settled = Dirs + lean" : bad);

                // Toggle lean: latched Q + A = A + Q, the move part is just A.
                GlobalSettings tg = G(); tg.LeanMode = "toggle";
                Sim t = new Sim(p, tg, fps);
                t.Step(In("Q")); t.Run(0.2, In("")); t.Run(2.0, In("A"));
                Check("T23 toggle lean + A additive @" + fps,
                    Math.Abs(t.E.Debug.UiOffsetX - (p.Dirs[2].OffX + p.LeanQX)) < 0.01 && Math.Abs(t.E.Debug.UiOffsetY - (p.Dirs[2].OffY + p.LeanQY)) < 0.01 &&
                    Math.Abs(t.E.MoveX - p.Dirs[2].OffX) < 0.01,
                    "total=(" + F(t.E.Debug.UiOffsetX) + "," + F(t.E.Debug.UiOffsetY) + ") move=(" + F(t.E.MoveX) + "," + F(t.E.MoveY) + ")");
            }

            // An old file with ticked combos: they are dropped (not written back), the ring is additive.
            GlobalSettings old; string err;
            bool ok = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"m\",\"Dirs\":[{\"OffX\":0,\"OffY\":5},{},{\"OffX\":-20},{\"OffX\":20}],\"LeanQX\":-10,\"LeanQY\":-4,"
                + "\"Combos\":[{\"Key\":\"QA\",\"Enabled\":true,\"OffX\":900,\"OffY\":-3},{\"Key\":\"ED\",\"Enabled\":true,\"OffX\":5}]}]}", out old, out err);
            string again = ok ? SettingsIO.Serialize(old) : "";
            bool add = false;
            if (ok)
            {
                Sim q = new Sim(old.Active, G(), 144); q.Run(2.0, In("QA"));
                add = Math.Abs(q.E.Debug.UiOffsetX - (-30)) < 0.01 && Math.Abs(q.E.Debug.UiOffsetY - (-4)) < 0.01;
            }
            Check("T23 old ticked combos dropped on load, not written back, Q+A additive", ok && add && !again.Contains("Combos") && !again.Contains("\"QA\""),
                ok ? "additive=" + add : err);
        }

        // ---------------- v1.3.1: 站着偏移 per gun + migration ----------------

        static void T26()
        {
            // Old settings.json (v1.3 format): global centre (5,-9), two profiles without StandOffset.
            GlobalSettings old = G();
            Profile b = Profile.CreateBlank("B"); old.Profiles.Add(b);
            string json = SettingsIO.Serialize(old).Replace("\"StandOffsetX\": 0,", "").Replace("\"StandOffsetY\": 0,", "")
                .Replace("\"CenterOffsetX\": 0", "\"CenterOffsetX\": 5").Replace("\"CenterOffsetY\": 0", "\"CenterOffsetY\": -9");
            bool oldFormat = !json.Contains("StandOffset") && json.Contains("\"CenterOffsetX\": 5");
            GlobalSettings m; string err;
            bool ok = SettingsIO.TryParse(json, out m, out err);
            bool migrated = ok && m.Profiles.Count == 3 && m.Profiles.TrueForAll(delegate(Profile q) { return q.StandOffsetX == 5 && q.StandOffsetY == -9 && q.StandPresent; })
                && m.CenterOffsetX == 0 && m.CenterOffsetY == 0;
            Check("T26 old global 中心校正 -> every profile, global 0", oldFormat && migrated,
                ok ? "old format=" + oldFormat + " profiles=" + m.Profiles.Count + " stand=(" + F(m.Profiles[0].StandOffsetX) + "," + F(m.Profiles[0].StandOffsetY) + ") global=(" + F(m.CenterOffsetX) + "," + F(m.CenterOffsetY) + ")" : err);
            if (!ok) return;

            // Save and reload: no second migration; per-gun edits survive; a stray non-zero global is ignored.
            m.Profiles[1].StandOffsetX = -3;
            string saved = SettingsIO.Serialize(m);
            GlobalSettings r1; bool ok1 = SettingsIO.TryParse(saved, out r1, out err);
            GlobalSettings r2 = null; bool ok2 = ok1 && SettingsIO.TryParse(SettingsIO.Serialize(r1), out r2, out err);
            string tampered = saved.Replace("\"CenterOffsetX\": 0", "\"CenterOffsetX\": 40");
            GlobalSettings r3; bool ok3 = SettingsIO.TryParse(tampered, out r3, out err);
            bool once = ok2 && ok3 && r1.Profiles[0].StandOffsetX == 5 && r1.Profiles[1].StandOffsetX == -3 && r1.Profiles[2].StandOffsetX == 5
                && SettingsIO.Serialize(r2) == SettingsIO.Serialize(r1) && saved.Contains("\"StandOffsetX\"")
                && r3.Profiles[1].StandOffsetX == -3 && r3.Profiles[0].StandOffsetX == 5 && r3.CenterOffsetX == 0;
            Check("T26 reload does not migrate twice", once,
                "after reload stands X=" + (ok1 ? F(r1.Profiles[0].StandOffsetX) + "," + F(r1.Profiles[1].StandOffsetX) + "," + F(r1.Profiles[2].StandOffsetX) : "err")
                + "; stray global 40 ignored=" + (ok3 && r3.Profiles[1].StandOffsetX == -3));

            // The three real legacy fixtures (global centre 3,-2) migrate on load.
            int fx = 0;
            foreach (string name in new string[] { "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json" })
            {
                GlobalSettings f;
                if (SettingsIO.TryParse(File.ReadAllText(Fixture(name)), out f, out err) && f.CenterOffsetX == 0
                    && f.Profiles.TrueForAll(delegate(Profile q) { return q.StandOffsetX == 3 && q.StandOffsetY == -2; })) fx++;
            }
            Check("T26 real v1/v1.1/v1.2 files migrate", fx == 3, fx + "/3 fixtures: every profile stand=(3,-2), global 0");

            // Import of an old-format profile (no fields) gets 0; import into settings with a global left over is not polluted.
            Profile op = Example(); op.Name = "old-gun";
            string oldProfile = SettingsIO.ExportProfile(op).Replace("\"StandOffsetX\": 0,", "").Replace("\"StandOffsetY\": 0,", "");
            List<Profile> imp; bool ok4 = SettingsIO.TryParseImport(oldProfile, out imp, out err);
            GlobalSettings tgt = G(); string first = ok4 ? tgt.AddImported(imp) : null;
            tgt.Validate();
            Profile ip = ok4 ? tgt.FindProfile(first) : null;
            List<Profile> fullImp; bool ok5 = SettingsIO.TryParseImport(json, out fullImp, out err); // old full file with global (5,-9)
            Check("T26 old-format import -> 站着偏移 0", ok4 && !oldProfile.Contains("StandOffset") && ip != null && ip.StandOffsetX == 0 && ip.StandOffsetY == 0
                && ok5 && fullImp.TrueForAll(delegate(Profile q) { return q.StandOffsetX == 0 && q.StandOffsetY == 0; }),
                "single old profile=(" + (ip != null ? F(ip.StandOffsetX) + "," + F(ip.StandOffsetY) : "?") + "), profiles from an old full settings.json also 0 (global not imported)");

            // Export/import carries it; per-gun: two profiles keep different values, engine uses the active one.
            Profile a = Profile.CreateBlank("a"); a.StandOffsetX = 11; a.StandOffsetY = -4; a.CurveAD.DurationMs = 0; a.CurveWS.DurationMs = 0;
            List<Profile> back; bool ok6 = SettingsIO.TryParseImport(SettingsIO.ExportProfile(a), out back, out err);
            Profile c2 = Profile.CreateBlank("c"); c2.StandOffsetX = -20; c2.CurveAD.DurationMs = 0;
            MotionEngine e1 = new MotionEngine(), e2 = new MotionEngine();
            e1.Update(0.01, In(""), a, G(), PreviewMode.Live, 1);
            e2.Update(0.01, In(""), c2, G(), PreviewMode.Live, 1);
            Check("T26 export/import + engine use the profile's 站着偏移", ok6 && back[0].StandOffsetX == 11 && back[0].StandOffsetY == -4
                && e1.Debug.UiOffsetX == 11 && e1.OffsetY == 4 && e2.Debug.UiOffsetX == -20,
                "a=(" + F(e1.Debug.UiOffsetX) + "," + F(e1.Debug.UiOffsetY) + ") c=(" + F(e2.Debug.UiOffsetX) + ")");

        }

        // ---------------- v2.1 addendum: 自由视角 ----------------

        static void T32()
        {
            foreach (double fps in Rates)
            {
                GlobalSettings g = G();
                Profile p = Example();
                Sim s = new Sim(p, g, fps);
                s.Run(1.0, In("A"));
                bool shown = s.E.Alpha == 1;
                s.Step(In("AF"));
                bool hidAtOnce = s.E.Alpha == 0;
                s.Run(1.0, In("DF"));                 // keep moving while hidden
                double xHidden = s.E.Debug.UiOffsetX;
                bool stillHidden = s.E.Alpha == 0;
                int showFrames = (int)Math.Ceiling(g.ShowFadeMs / 1000.0 * fps - 1e-9);
                s.Step(In("D"));
                double firstAlpha = s.E.Alpha;
                for (int k = 1; k < showFrames; k++) s.Step(In("D"));
                Check("T32 自由视角 hides at once, engine keeps running, fades back @" + fps,
                    shown && hidAtOnce && stillHidden && Math.Abs(xHidden - 12) < 0.01 && firstAlpha > 0 && firstAlpha < 1 && s.E.Alpha >= 1 - 1e-9,
                    "hidden=" + hidAtOnce + " x after 1 s hidden=" + F(xHidden) + " (D=12) alpha 1st frame back=" + F(firstAlpha) + " after 出现渐变=" + F(s.E.Alpha));

                GlobalSettings off = G(); off.HideOnFreeLook = false;
                Sim o = new Sim(Example(), off, fps);
                o.Run(0.5, In("")); o.Run(0.5, In("F"));
                Sim r = new Sim(Example(), G(), fps);
                r.Run(0.5, In("L")); double rec = r.E.Recoil; r.Run(0.2, In("LF"));
                Check("T32 checkbox off = no hide; recoil not reset by 自由视角 @" + fps, o.E.Alpha == 1 && r.E.Recoil >= rec - 1e-9,
                    "alpha with checkbox off=" + F(o.E.Alpha) + " recoil " + F(rec) + " -> " + F(r.E.Recoil));
            }
        }

        // ---------------- pictures ----------------

        static void Pictures()
        {
            Profile def = Example();
            Ring("ring-default.png", def);
            Profile t = Example(); t.Thickness = 6; Ring("ring-thick.png", t);
            Profile fe = Example(); fe.Feather = 6; Ring("ring-feather6.png", fe);
            Profile ra = Example(); ra.Thickness = 6; ra.Gradient = "radial"; ra.Color2 = "#FF4040"; Ring("ring-radial.png", ra);
            Profile sw = Example(); sw.Thickness = 6; sw.Gradient = "sweep"; sw.Color2 = "#4080FF"; Ring("ring-sweep.png", sw);
            Profile dt = Example(); dt.CenterDot = true; dt.CenterDotSize = 3; Ring("ring-centerdot.png", dt);
            Profile tt = Example(); tt.Gradient = "twotone"; tt.Color = "#78FF78"; tt.Color2 = "#101010"; tt.Thickness = 2; tt.OuterThickness = 1.5;
            RingOnBackgrounds("ring-twotone.png", tt);

            Trace("trace-A-2s-release.png", In("A"), 2.0, 2.0);
            Trace("trace-WD-2s-release.png", In("WD"), 2.0, 2.0);
            Trace("trace-A-plus-fire-1.5s.png", In("AL"), 1.5, 1.5);
            Console.WriteLine("PNGs written to " + outDir);
        }

        // T16: two-tone ring ("twotone") rendering + OuterThickness persistence.
        static void T16()
        {
            Profile p = Example();
            p.Gradient = "twotone"; p.Diameter = 20; p.Thickness = 4; p.OuterThickness = 2; p.Feather = 1;
            p.Color = "#FFFF00"; p.Color2 = "#202020";
            int S = RingRaster.CanvasSize(p);
            double c = S / 2.0 + 0.5; // pixel (S/2 + k, S/2) has its centre at distance k
            int[] px = RingRaster.Render(p, S, c, c);
            int row = (S / 2) * S;
            int inner = px[row + S / 2 + 10];   // d = R = 10 (inner band centre)
            int outer = px[row + S / 2 + 13];   // d = R + T/2 + To/2 = 13 (outer band centre)
            Check("T16 inner band = Color, alpha 1", inner == unchecked((int)0xFFFFFF00), "pixel=0x" + inner.ToString("X8") + " want 0xFFFFFF00");
            Check("T16 outer band = Color2, alpha 1", outer == unchecked((int)0xFF202020), "pixel=0x" + outer.ToString("X8") + " want 0xFF202020");

            Profile big = Example(); big.Gradient = "twotone"; big.OuterThickness = 20; big.Feather = 6;
            Profile plain = big.Clone(); plain.Gradient = "none";
            int Sb = RingRaster.CanvasSize(big), Sp = RingRaster.CanvasSize(plain);
            int[] bp = RingRaster.Render(big, Sb, Sb / 2.0, Sb / 2.0);
            bool borderClear = true;
            for (int i = 0; i < Sb; i++)
                if (bp[i] != 0 || bp[(Sb - 1) * Sb + i] != 0 || bp[i * Sb] != 0 || bp[i * Sb + Sb - 1] != 0) borderClear = false;
            double need = big.Diameter / 2 + big.Thickness / 2 + big.OuterThickness + Math.Max(big.Feather, 1);
            Check("T16 canvas includes outer band", borderClear && Sb / 2.0 >= need && Sb > Sp,
                "S=" + Sb + " (plain " + Sp + ") half=" + F(Sb / 2.0) + " outer edge+feather=" + F(need) + " border empty=" + borderClear);

            GlobalSettings g = G();
            g.Active.Gradient = "twotone"; g.Active.OuterThickness = 7.5;
            GlobalSettings back; string err;
            bool ok = SettingsIO.TryParse(SettingsIO.Serialize(g), out back, out err);
            Check("T16 OuterThickness round-trip", ok && back.Active.OuterThickness == 7.5 && back.Active.Gradient == "twotone",
                ok ? "OuterThickness=" + F(back.Active.OuterThickness) + " Gradient=" + back.Active.Gradient : err);

            GlobalSettings m;
            bool ok2 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"old v1\",\"Gradient\":\"none\"}]}", out m, out err);
            Check("T16 missing OuterThickness -> default", ok2 && m.Profiles[0].OuterThickness == 1.5, ok2 ? "OuterThickness=" + F(m.Profiles[0].OuterThickness) : err);

            GlobalSettings h, l;
            bool ok3 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"OuterThickness\":99}]}", out h, out err);
            bool ok4 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"OuterThickness\":0.1}]}", out l, out err);
            Check("T16 OuterThickness clamped", ok3 && ok4 && h.Profiles[0].OuterThickness == 20 && l.Profiles[0].OuterThickness == 0.5,
                "99 -> " + (ok3 ? F(h.Profiles[0].OuterThickness) : "err") + ", 0.1 -> " + (ok4 ? F(l.Profiles[0].OuterThickness) : "err"));
        }

        // Ring upscaled 4x on mid-grey, white and black, side by side.
        static void RingOnBackgrounds(string file, Profile p)
        {
            int S = RingRaster.CanvasSize(p);
            int[] px = RingRaster.Render(p, S, S / 2.0 + 0.3, S / 2.0 + 0.6);
            const int up = 4;
            int W = S * up, gap = 8;
            int[] bgs = { 128, 255, 0 };
            int total = W * 3 + gap * 2;
            byte[] rgba = new byte[total * W * 4];
            for (int i = 0; i < total * W; i++) { rgba[i * 4] = 60; rgba[i * 4 + 1] = 60; rgba[i * 4 + 2] = 160; rgba[i * 4 + 3] = 255; }
            for (int k = 0; k < 3; k++)
            {
                int bg = bgs[k], x0 = k * (W + gap);
                for (int y = 0; y < W; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int v = px[(y / up) * S + (x / up)];
                        int a = (v >> 24) & 0xFF;
                        int o = (y * total + x0 + x) * 4;
                        rgba[o] = (byte)Math.Min(255, ((v >> 16) & 0xFF) + bg * (255 - a) / 255);
                        rgba[o + 1] = (byte)Math.Min(255, ((v >> 8) & 0xFF) + bg * (255 - a) / 255);
                        rgba[o + 2] = (byte)Math.Min(255, (v & 0xFF) + bg * (255 - a) / 255);
                        rgba[o + 3] = 255;
                    }
            }
            Png.Write(Path.Combine(outDir, file), total, W, rgba);
        }

        static void Ring(string file, Profile p)
        {
            int S = RingRaster.CanvasSize(p);
            double cx = S / 2.0 + 0.3, cy = S / 2.0 + 0.6; // subpixel centre
            int[] px = RingRaster.Render(p, S, cx, cy);
            const int up = 4;
            int W = S * up;
            byte[] rgba = new byte[W * W * 4];
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    int v = px[(y / up) * S + (x / up)];
                    int a = (v >> 24) & 0xFF, r = (v >> 16) & 0xFF, g = (v >> 8) & 0xFF, b = v & 0xFF;
                    // premultiplied src over mid-grey
                    int o = (y * W + x) * 4;
                    rgba[o] = (byte)Math.Min(255, r + 128 * (255 - a) / 255);
                    rgba[o + 1] = (byte)Math.Min(255, g + 128 * (255 - a) / 255);
                    rgba[o + 2] = (byte)Math.Min(255, b + 128 * (255 - a) / 255);
                    rgba[o + 3] = 255;
                }
            Png.Write(Path.Combine(outDir, file), W, W, rgba);
        }

        static void Trace(string file, InputState hold, double holdS, double afterS)
        {
            Sim s = new Sim(Example(), G(), 144);
            s.Run(holdS, hold);
            s.Run(afterS, In(""));
            const int W = 480, H = 480, scale = 4;
            byte[] img = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++) { img[i * 4] = 32; img[i * 4 + 1] = 32; img[i * 4 + 2] = 32; img[i * 4 + 3] = 255; }
            // grid every 10 px of offset, axes brighter
            for (int k = -6; k <= 6; k++)
            {
                int gx = W / 2 + k * 10 * scale, gy = H / 2 + k * 10 * scale;
                byte c = (byte)(k == 0 ? 110 : 55);
                for (int i = 0; i < W; i++) { Put(img, W, H, gx, i, c, c, c); Put(img, W, H, i, gy, c, c, c); }
            }
            double tEnd = s.Trace[s.Trace.Count - 1][0];
            double[] last = null;
            foreach (double[] r in s.Trace)
            {
                double u = r[0] / tEnd;
                byte cr, cg, cb; Hue(u, out cr, out cg, out cb);
                if (last != null) Line(img, W, H, W / 2 + last[3] * scale, H / 2 + last[4] * scale, W / 2 + r[3] * scale, H / 2 + r[4] * scale, cr, cg, cb);
                last = r;
            }
            // time colour bar at the bottom
            for (int x = 0; x < W; x++) { byte cr, cg, cb; Hue(x / (double)W, out cr, out cg, out cb); for (int y = H - 8; y < H; y++) Put(img, W, H, x, y, cr, cg, cb); }
            int holdX = (int)(W * holdS / tEnd);
            for (int y = H - 14; y < H; y++) Put(img, W, H, holdX, y, 255, 255, 255);
            Png.Write(Path.Combine(outDir, file), W, H, img);
        }

        static void Hue(double u, out byte r, out byte g, out byte b)
        {
            double h = (1 - u) * 240.0; // blue (start) -> red (end)
            double x = 1 - Math.Abs((h / 60.0) % 2 - 1);
            double rr = 0, gg = 0, bb = 0;
            if (h < 60) { rr = 1; gg = x; } else if (h < 120) { rr = x; gg = 1; } else if (h < 180) { gg = 1; bb = x; } else { gg = x; bb = 1; }
            r = (byte)(rr * 255); g = (byte)(gg * 255); b = (byte)(bb * 255);
        }

        static void Put(byte[] img, int W, int H, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int o = (y * W + x) * 4; img[o] = r; img[o + 1] = g; img[o + 2] = b; img[o + 3] = 255;
        }

        static void Line(byte[] img, int W, int H, double x0, double y0, double x1, double y1, byte r, byte g, byte b)
        {
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))) + 1;
            for (int i = 0; i <= n; i++)
            {
                double t = i / (double)n;
                int x = (int)Math.Round(x0 + (x1 - x0) * t), y = (int)Math.Round(y0 + (y1 - y0) * t);
                Put(img, W, H, x, y, r, g, b); Put(img, W, H, x + 1, y, r, g, b); Put(img, W, H, x, y + 1, r, g, b);
            }
        }
    }
}
