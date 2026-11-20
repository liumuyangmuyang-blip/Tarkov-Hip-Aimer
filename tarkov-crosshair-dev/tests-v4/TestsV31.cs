using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace TarkovCrosshair.Tests
{
    // v3.1 (SPEC-v3.1.md section 3): built-in = Leo's 27 Oct file, 换手自定义 (HandCustom) model + engine + persistence.
    public static partial class Program
    {
        static void V31Tests()
        {
            H1FreshEqualsFixture(); H2UpgradeMirror(); H3Custom(); H4Persistence();
        }

        // ---------------- reference copies of v3.0 ----------------

        /// <summary>v3.0 Profile.CreateLeoDefault, verbatim values (the shipped v3.0 built-in).</summary>
        static Profile LeoV30Default(string name)
        {
            Profile p = new Profile();
            p.Name = name;
            p.Diameter = 12; p.Thickness = 1.2; p.Feather = 1.0; p.OpacityPct = 60;
            p.Color = "#00DF00"; p.Color2 = "#000000"; p.Gradient = "twotone"; p.OuterThickness = 1.5;
            p.CenterDot = false; p.CenterDotSize = 2;
            p.Dirs[0] = new DirSetting(0, -30); p.Dirs[1] = new DirSetting(0, 8); p.Dirs[2] = new DirSetting(62, 0); p.Dirs[3] = new DirSetting(-33, 3);
            Profile.RecomputeDiagonals(p);
            p.StandOffsetX = 0; p.StandOffsetY = 0;
            p.CurveAD = new MoveCurve(350, MotionCurve.Presets[0]);
            p.CurveWS = new MoveCurve(350, MotionCurve.Presets[0]);
            p.RecoilMaxPx = 23; p.RecoilRiseMs = 300; p.RecoilClickKickPx = 8; p.RecoilFallMs = 180; p.RecoilDelayMs = 30; p.RecoilDriftX = 6;
            p.LeanQX = 5; p.LeanQY = -20; p.LeanEX = 30; p.LeanEY = -15; p.LeanMs = 250;
            p.HandSwitchMs = 400;
            JumpSettings j = new JumpSettings();
            j.Enabled = true; j.TotalMs = 800; j.Phases = new List<JumpPhase>();
            j.Phases.Add(new JumpPhase(200, -300, "smooth")); j.Phases.Add(new JumpPhase(305, 0, "smooth")); j.Phases.Add(new JumpPhase(295, 0, "in"));
            p.Jump = j;
            return p;
        }

        static double N30(double x) { return x == 0 ? 0 : -x; }

        /// <summary>v3.0 Profile.FillView, verbatim (hand 1 = 左右镜像 of the normal hand).</summary>
        static void V30Fill(Profile p, int hand, HandView v)
        {
            for (int i = 0; i < Profile.DirCount; i++)
            {
                DirSetting d = p.Dirs[hand == 1 ? Profile.MirrorDir[i] : i];
                v.DX[i] = hand == 1 ? N30(d.OffX) : d.OffX;
                v.DY[i] = d.OffY;
            }
            if (hand == 1)
            {
                v.SX = N30(p.StandOffsetX); v.SY = p.StandOffsetY;
                v.QX = N30(p.LeanEX); v.QY = p.LeanEY;
                v.EX = N30(p.LeanQX); v.EY = p.LeanQY;
                v.Drift = N30(p.RecoilDriftX);
            }
            else
            {
                v.SX = p.StandOffsetX; v.SY = p.StandOffsetY;
                v.QX = p.LeanQX; v.QY = p.LeanQY;
                v.EX = p.LeanEX; v.EY = p.LeanEY;
                v.Drift = p.RecoilDriftX;
            }
        }

        /// <summary>Max |a − b| over every HandView value (±0 counts as equal).</summary>
        static double ViewDiff(HandView a, HandView b)
        {
            double m = 0;
            for (int i = 0; i < Profile.DirCount; i++) { m = Math.Max(m, Math.Abs(a.DX[i] - b.DX[i])); m = Math.Max(m, Math.Abs(a.DY[i] - b.DY[i])); }
            double[] x = { a.QX, a.QY, a.EX, a.EY, a.SX, a.SY, a.Drift }, y = { b.QX, b.QY, b.EX, b.EY, b.SX, b.SY, b.Drift };
            for (int i = 0; i < x.Length; i++) m = Math.Max(m, Math.Abs(x[i] - y[i]));
            if (double.IsNaN(m)) m = double.PositiveInfinity;
            return m;
        }

        static string ViewStr(HandView v)
        {
            string s = "";
            for (int i = 0; i < Profile.DirCount; i++) s += Profile.DirNames[i] + "=" + F(v.DX[i]) + "/" + F(v.DY[i]) + " ";
            return s + "S=" + F(v.SX) + "/" + F(v.SY) + " Q=" + F(v.QX) + "/" + F(v.QY) + " E=" + F(v.EX) + "/" + F(v.EY) + " drift=" + F(v.Drift);
        }

        // ---------------- reflection deep compare over [DataMember] ----------------

        static readonly HashSet<string> SkipMembers = new HashSet<string> { "SavedDefault", "SavedDefaultAt", "HandCustom" };

        static void DeepEq(object a, object b, string path, List<string> diffs, ref int n)
        {
            if (a == null || b == null)
            {
                n++;
                if (a != null || b != null) diffs.Add(path + ": " + (a == null ? "null" : a.ToString()) + " vs " + (b == null ? "null" : b.ToString()));
                return;
            }
            Type t = a.GetType();
            if (t != b.GetType()) { diffs.Add(path + ": type " + t.Name + " vs " + b.GetType().Name); return; }
            if (t == typeof(double))
            {
                n++;
                double x = (double)a, y = (double)b;
                if (!(Math.Abs(x - y) <= 1e-9 || (double.IsNaN(x) && double.IsNaN(y)))) diffs.Add(path + ": " + x.ToString("R") + " vs " + y.ToString("R"));
                return;
            }
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            {
                n++;
                if (!a.Equals(b)) diffs.Add(path + ": " + a + " vs " + b);
                return;
            }
            IList la = a as IList;
            if (la != null)
            {
                IList lb = (IList)b;
                if (la.Count != lb.Count) { diffs.Add(path + ": count " + la.Count + " vs " + lb.Count); return; }
                for (int i = 0; i < la.Count; i++) DeepEq(la[i], lb[i], path + "[" + i + "]", diffs, ref n);
                return;
            }
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            int members = 0;
            foreach (MemberInfo m in t.GetMembers(bf))
            {
                DataMemberAttribute dm = (DataMemberAttribute)Attribute.GetCustomAttribute(m, typeof(DataMemberAttribute));
                if (dm == null) continue;
                string name = dm.Name ?? m.Name;
                if (SkipMembers.Contains(name)) continue;
                members++;
                FieldInfo fi = m as FieldInfo; PropertyInfo pi = m as PropertyInfo;
                object va = fi != null ? fi.GetValue(a) : pi.GetValue(a), vb = fi != null ? fi.GetValue(b) : pi.GetValue(b);
                DeepEq(va, vb, path + "." + name, diffs, ref n);
            }
            if (members == 0) diffs.Add(path + ": no [DataMember] on " + t.Name);
        }

        static string DiffStr(List<string> d) { return d.Count == 0 ? "none" : string.Join("; ", d.GetRange(0, Math.Min(6, d.Count))) + (d.Count > 6 ? " …(+" + (d.Count - 6) + ")" : ""); }

        /// <summary>HandCustom is exactly 8 items, all 自动.</summary>
        static bool AllAuto(Profile p)
        {
            if (p == null || p.HandCustom == null || p.HandCustom.Count != Profile.HandItemCount || Profile.HandItemCount != 8) return false;
            foreach (HandCustomItem h in p.HandCustom) if (h == null || h.Custom) return false;
            return !p.AnyHandCustom();
        }

        static string HandStr(Profile p)
        {
            if (p == null || p.HandCustom == null) return "null";
            string s = p.HandCustom.Count + ":";
            foreach (HandCustomItem h in p.HandCustom) s += h == null ? " null" : (h.Custom ? " C(" + F(h.X) + "," + F(h.Y) + ")" : " A");
            return s;
        }

        static GlobalSettings LoadFixture(string name, out string err)
        {
            GlobalSettings g;
            if (!SettingsIO.TryParse(File.ReadAllText(Fixture(name)), out g, out err)) return null;
            return g;
        }

        // ---------------- 3.1 fresh install == Leo's file ----------------

        /// <summary>v5.0: profile 「Leo默认 (旧)」 of Leo's v5 file (fixtures/leo-v50-1118-settings.v50), validated.</summary>
        static Profile V50Expected()
        {
            string e; GlobalSettings g = LoadFixture("leo-v50-1118-settings.v50", out e);
            Profile p = g == null ? null : g.FindProfile("Leo默认 (旧)");
            if (p == null) return new Profile();
            p = p.CloneValues();
            // v5.1.1 (X5, Leo 21 Nov 2026): his newly tuned 上抬 + 站立静止偏移 (Dirs / HandCustom stored from the 站立 point, every
            // screen place unchanged), from his settings.json of 21 Nov (fixture leo-v511-1121-settings.v511, profile 「Leo默认」)
            GlobalSettings n = LoadFixture("leo-v511-1121-settings.v511", out e);
            Profile q = n == null ? null : n.FindProfile("Leo默认");
            if (q != null)
            {
                p.RecoilMaxPx = q.RecoilMaxPx; p.StandAsZero = q.StandAsZero; p.StandOffsetX = q.StandOffsetX; p.StandOffsetY = q.StandOffsetY;
                for (int i = 0; i < Profile.DirCount; i++) p.Dirs[i] = new DirSetting(q.Dirs[i].OffX, q.Dirs[i].OffY);
                for (int i = 0; i < p.HandCustom.Count; i++) p.HandCustom[i] = q.HandCustom[i].Clone();
            }
            return p;
        }

        /// <summary>v5.0: fx's active profile gets the V50Expected values; its name and saved snapshot stay.</summary>
        static void V50Apply(GlobalSettings fx)
        {
            Profile a = fx.Active; Profile v = V50Expected();
            v.Name = a.Name; v.SavedDefault = a.SavedDefault; v.SavedDefaultAt = a.SavedDefaultAt;
            fx.Profiles[fx.Profiles.IndexOf(a)] = v;
        }

        static void H1FreshEqualsFixture()
        {
            string err;
            GlobalSettings fx = LoadFixture("leo-v30-1027-settings.json", out err);
            if (fx == null) { Check("H1 fixture leo-v30-1027-settings.json parses", false, err); return; }
            V50Apply(fx); // v5.0: the built-in profile values come from Leo's v5 file (globals still = the 27 Oct file)
            fx.Keys.Boost = 6; fx.Keys.BoostMods = 0; // v5.1.1 (X5, Leo 21 Nov 2026): fresh install 强化 = 鼠标侧键2 (v5.1: Ctrl+T)
            fx.Autostart = true; // Leo 21 Nov 2026 new defaults: fresh install autostart on (the 27 Oct file stores false and keeps it when loaded)

            GlobalSettings raw = GlobalSettings.CreateDefault();
            GlobalSettings val = GlobalSettings.CreateDefault(); val.Validate();
            List<string> d1 = new List<string>(), d2 = new List<string>(); int n1 = 0, n2 = 0;
            DeepEq(raw, fx, "$", d1, ref n1);
            DeepEq(val, fx, "$", d2, ref n2);
            Check("H1 GlobalSettings.CreateDefault() == Leo's 27 Oct file, every global + profile field (Keys, Boost, curves, Dirs incl. diagonals ≤1e-9, Jump, lean, recoil, colours, shapes; SavedDefault ignored)",
                d1.Count == 0 && n1 > 120, n1 + " values compared, differences: " + DiffStr(d1));
            Check("H1 same after Validate (what a fresh install writes)", d2.Count == 0 && n2 > 120, n2 + " values, differences: " + DiffStr(d2));
            Check("H1 HandCustom: 8 items = Leo's v5 file (A, D, Q, E custom), in the built-in (raw + validated) and in the expected fixture",
                HandStr(raw.Active) == HandStr(V50Expected()) && HandStr(val.Active) == HandStr(V50Expected()) && HandStr(fx.Active) == HandStr(V50Expected()),
                "raw=" + HandStr(raw.Active) + " val=" + HandStr(val.Active) + " fixture=" + HandStr(fx.Active));
            Check("H1 built-in has no saved snapshot; globals ResGameW 1920", raw.Active.SavedDefault == null && raw.Active.SavedDefaultAt == null && raw.ResGameW == 1920,
                "ResGameW=" + F(raw.ResGameW));

            // SettingsIO.Load on an empty dir (the real fresh-install path)
            string scratch = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            if (string.IsNullOrEmpty(scratch)) scratch = Path.GetTempPath();
            string dir = Path.Combine(scratch, "h1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "settings.json"), msg;
            GlobalSettings loaded = SettingsIO.Load(path, out msg);
            GlobalSettings reread = SettingsIO.Load(path, out msg);
            List<string> d3 = new List<string>(); int n3 = 0;
            DeepEq(loaded, fx, "$", d3, ref n3);
            DeepEq(reread, fx, "$", d3, ref n3);
            bool la = HandStr(loaded.Active) == HandStr(V50Expected()) && HandStr(reread.Active) == HandStr(V50Expected());
            try { File.Delete(path); Directory.Delete(dir); } catch (Exception) { }
            Check("H1 SettingsIO.Load with no settings.json (written + re-read) == the fixture", d3.Count == 0 && la, "differences: " + DiffStr(d3) + " handAuto=" + la);

            // 恢复到内置默认 -> same values, name + snapshot kept, HandCustom all 自动
            GlobalSettings g = fx.Clone(true);
            Profile p = g.Active;
            p.Dirs[2].OffX = 1; p.Dirs[0].OffY = 2; Profile.RecomputeDiagonals(p); p.LeanQX = 99; p.RecoilDriftX = 7; p.OpacityPct = 20; p.Boost.Diameter = 40;
            p.SetHandCustom(2, 40, -5); p.SetHandCustom(7, 3, 0);
            Profile r = g.RestoreLeoDefault(p);
            List<string> d4 = new List<string>(); int n4 = 0;
            DeepEq(r, fx.Active, "$.P", d4, ref n4);
            List<string> d5 = new List<string>(); int n5 = 0;
            DeepEq(r.SavedDefault, fx.Active.SavedDefault, "$.Saved", d5, ref n5);
            bool kept = r.Name == "Leo默认" && r.SavedDefault != null && r.SavedDefaultAt == fx.Active.SavedDefaultAt && g.Active == r;
            Check("H1 恢复到内置默认 -> every value = Leo's v5 file, HandCustom as in it, name + 「我存的默认」 snapshot kept",
                d4.Count == 0 && d5.Count == 0 && HandStr(r) == HandStr(V50Expected()) && kept, "differences: " + DiffStr(d4) + " snapshot diffs: " + DiffStr(d5) + " hand=" + HandStr(r) + " kept=" + kept);
        }

        // ---------------- 3.2 upgrade: all 自动 == v3.0 mirror ----------------

        static double RndVal(Random rnd, double range)
        {
            switch (rnd.Next(6))
            {
                case 0: return 0;
                case 1: return rnd.Next(-(int)range, (int)range + 1);
                case 2: return Math.Round((rnd.NextDouble() * 2 - 1) * range, 1);
                case 3: return range * (rnd.Next(2) == 0 ? 1 : -1);
                default: return (rnd.NextDouble() * 2 - 1) * range;
            }
        }

        static Profile RandomProfile(Random rnd, int k)
        {
            Profile p = new Profile();
            p.Name = "r" + k;
            for (int i = 0; i < 4; i++) p.Dirs[i] = new DirSetting(RndVal(rnd, 500), RndVal(rnd, 500));
            // write junk diagonals; Validate must recompute them
            for (int i = 4; i < 8; i++) p.Dirs[i] = new DirSetting(RndVal(rnd, 500), RndVal(rnd, 500));
            p.StandOffsetX = RndVal(rnd, 200); p.StandOffsetY = RndVal(rnd, 200);
            p.LeanQX = RndVal(rnd, 500); p.LeanQY = RndVal(rnd, 500); p.LeanEX = RndVal(rnd, 500); p.LeanEY = RndVal(rnd, 500);
            p.RecoilDriftX = RndVal(rnd, 500);
            p.Validate();
            return p;
        }

        static void H2UpgradeMirror()
        {
            string err;
            GlobalSettings fx = LoadFixture("leo-v30-1027-settings.json", out err);
            Profile leo = fx == null ? null : fx.Active;
            HandView a = new HandView(), b = new HandView();
            double dl = double.PositiveInfinity, dl0 = double.PositiveInfinity;
            if (leo != null)
            {
                leo.FillView(1, a); V30Fill(leo, 1, b); dl = ViewDiff(a, b);
                leo.FillView(0, a); V30Fill(leo, 0, b); dl0 = ViewDiff(a, b);
            }
            Check("H2 Leo's v3.0 file loaded by v3.1: HandCustom all 自动, FillView(1) == v3.0 mirror, FillView(0) == v3.0",
                leo != null && AllAuto(leo) && dl <= 1e-9 && dl0 <= 1e-9, leo == null ? err : "hand=" + HandStr(leo) + " max|Δ| hand1=" + dl.ToString("E1") + " hand0=" + dl0.ToString("E1"));

            // every older fixture as well
            string bad = "";
            foreach (string f in new string[] { "settings-v1.json", "settings-v1.1.json", "settings-v1.2.json", "settings-v2.0.json", "settings-v2.1.json",
                "settings-v2.2.json", "settings-v2.3.json", "settings-v2.3-leo1011.json", "settings-v2.4.5.json" })
            {
                GlobalSettings g = LoadFixture(f, out err);
                if (g == null) { bad += " " + f + ":" + err; continue; }
                foreach (Profile p in g.Profiles)
                {
                    p.FillView(1, a); V30Fill(p, 1, b);
                    double d = ViewDiff(a, b);
                    if (!AllAuto(p) || d > 1e-9 || (p.SavedDefault != null && !AllAuto(p.SavedDefault))) bad += " " + f + "/" + p.Name + " Δ=" + d.ToString("E1") + " " + HandStr(p);
                }
            }
            Check("H2 every older fixture (v1 … v2.4.5): HandCustom all 自动 (snapshots too), FillView(1) == v3.0 mirror", bad.Length == 0, bad.Length == 0 ? "9 files" : bad);

            Random rnd = new Random(31);
            double worst = 0; int fails = 0; string first = "";
            double worstItem = 0;
            for (int k = 0; k < 10000; k++)
            {
                Profile p = RandomProfile(rnd, k);
                p.FillView(1, a); V30Fill(p, 1, b);
                double d = ViewDiff(a, b);
                // HandAuto / HandValue per item == the mirror item; all auto -> HandValue == HandAuto
                double[,] want = {
                    { N30(p.Dirs[0].OffX), p.Dirs[0].OffY }, { N30(p.Dirs[1].OffX), p.Dirs[1].OffY }, { N30(p.Dirs[3].OffX), p.Dirs[3].OffY },
                    { N30(p.Dirs[2].OffX), p.Dirs[2].OffY }, { N30(p.StandOffsetX), p.StandOffsetY }, { N30(p.LeanEX), p.LeanEY },
                    { N30(p.LeanQX), p.LeanQY }, { N30(p.RecoilDriftX), 0 } };
                for (int i = 0; i < 8; i++)
                {
                    double ax, ay, vx, vy;
                    p.HandAuto(i, out ax, out ay); p.HandValue(i, out vx, out vy);
                    double e = Math.Max(Math.Abs(ax - want[i, 0]), Math.Abs(vx - ax));
                    if (i < 7) e = Math.Max(e, Math.Max(Math.Abs(ay - want[i, 1]), Math.Abs(vy - ay)));
                    if (double.IsNaN(e)) e = double.PositiveInfinity;
                    worstItem = Math.Max(worstItem, e);
                }
                worst = Math.Max(worst, d);
                if (d > 1e-9 || !AllAuto(p))
                {
                    fails++;
                    if (first.Length == 0) first = " first #" + k + ": got " + ViewStr(a) + " | want " + ViewStr(b) + " hand=" + HandStr(p);
                }
            }
            Check("H2 10,000 random profiles, all 自动: FillView(1) == v3.0 mirror (≤1e-9), HandAuto/HandValue per item == mirror",
                fails == 0 && worstItem <= 1e-9, "fails=" + fails + " max|Δ| view=" + worst.ToString("E1") + " items=" + worstItem.ToString("E1") + first);
        }

        // ---------------- 3.3 custom items ----------------

        static double K(double a, double b) { return 0.7071 * (a + b); }

        /// <summary>Expected hand-1 view from 8 per-item hand-1 values (W,S,A,D cardinals; diagonals = 0.7071 × sum).</summary>
        static void Expect1(double[,] it, HandView v)
        {
            double[] cx = { it[0, 0], it[1, 0], it[2, 0], it[3, 0] }, cy = { it[0, 1], it[1, 1], it[2, 1], it[3, 1] };
            for (int i = 0; i < 4; i++) { v.DX[i] = cx[i]; v.DY[i] = cy[i]; }
            v.DX[4] = K(cx[0], cx[2]); v.DY[4] = K(cy[0], cy[2]); // WA
            v.DX[5] = K(cx[0], cx[3]); v.DY[5] = K(cy[0], cy[3]); // WD
            v.DX[6] = K(cx[1], cx[2]); v.DY[6] = K(cy[1], cy[2]); // SA
            v.DX[7] = K(cx[1], cx[3]); v.DY[7] = K(cy[1], cy[3]); // SD
            v.SX = it[4, 0]; v.SY = it[4, 1]; v.QX = it[5, 0]; v.QY = it[5, 1]; v.EX = it[6, 0]; v.EY = it[6, 1]; v.Drift = it[7, 0];
        }

        static double[,] MirrorItems(Profile p)
        {
            return new double[,] {
                { N30(p.Dirs[0].OffX), p.Dirs[0].OffY }, { N30(p.Dirs[1].OffX), p.Dirs[1].OffY }, { N30(p.Dirs[3].OffX), p.Dirs[3].OffY },
                { N30(p.Dirs[2].OffX), p.Dirs[2].OffY }, { N30(p.StandOffsetX), p.StandOffsetY }, { N30(p.LeanEX), p.LeanEY },
                { N30(p.LeanQX), p.LeanQY }, { N30(p.RecoilDriftX), 0 } };
        }

        static void H3Custom()
        {
            Profile p = Profile.CreateLeoDefaultLegacy("x"); p.Validate(); // v5.0: an all-自动 fixture (the v5.0 default has custom items)
            p.StandOffsetX = 3; p.StandOffsetY = -2; p.RecoilDriftX = 4; // non-zero so every mirror is visible
            HandView got = new HandView(), want = new HandView(), v0 = new HandView(), r0 = new HandView();

            // A (item 2) = (40, -5)
            p.SetHandCustom(2, 40, -5);
            double[,] it = MirrorItems(p); it[2, 0] = 40; it[2, 1] = -5;
            p.FillView(1, got); Expect1(it, want);
            double hx, hy, ax, ay; p.HandValue(2, out hx, out hy); p.HandAuto(2, out ax, out ay);
            bool s1 = ViewDiff(got, want) <= 1e-9 && got.DX[2] == 40 && got.DY[2] == -5 && p.AnyHandCustom() && p.HandCustom[2].Custom
                && hx == 40 && hy == -5 && ax == N30(p.Dirs[3].OffX) && ay == p.Dirs[3].OffY;
            p.FillView(0, v0); V30Fill(p, 0, r0);
            bool h0 = ViewDiff(v0, r0) <= 1e-9;
            Check("H3 custom A (40, −5): hand-1 A = (40, −5), WA/SA = 0.7071 × (hand-1 W/S + A), every other item still mirrors; hand 0 unchanged",
                s1 && h0, "got " + ViewStr(got) + " | want " + ViewStr(want) + " hand0=" + h0);

            // edit normal D and W and Q: hand-1 A stays custom, W/S/D/E follow live
            p.Dirs[3] = new DirSetting(-50, 7); p.Dirs[0] = new DirSetting(2, -40); Profile.RecomputeDiagonals(p); p.LeanQX = -9; p.LeanQY = -1;
            it = MirrorItems(p); it[2, 0] = 40; it[2, 1] = -5;
            p.FillView(1, got); Expect1(it, want);
            p.HandAuto(2, out ax, out ay);
            bool s2 = ViewDiff(got, want) <= 1e-9 && got.DX[2] == 40 && got.DY[2] == -5 && got.DX[0] == -2 && got.DY[0] == -40 && got.EX == 9 && got.EY == -1
                && ax == 50 && ay == 7;
            Check("H3 edit normal D / W / Q afterwards: hand-1 A unchanged (custom), hand-1 W/S/D/E follow the new normal values; HandAuto(A) = (50, 7)",
                s2, "got " + ViewStr(got) + " | want " + ViewStr(want) + " auto A=" + F(ax) + "/" + F(ay));

            // SetHandAuto(2) -> mirror again
            p.SetHandAuto(2);
            p.FillView(1, got); V30Fill(p, 1, want);
            bool s3 = ViewDiff(got, want) <= 1e-9 && !p.AnyHandCustom() && !p.HandCustom[2].Custom && got.DX[2] == 50 && got.DY[2] == 7;
            Check("H3 SetHandAuto(A) -> hand 1 = v3.0 mirror again, no custom item left", s3, "got " + ViewStr(got) + " | want " + ViewStr(want));

            // every item custom, each honoured; hand 0 untouched
            double[,] c = { { 1, -2 }, { -3, 4 }, { 5.5, -6.5 }, { -7, 8 }, { 9, -10 }, { -11, 12 }, { 13, -14 }, { -15, 0 } };
            for (int i = 0; i < 8; i++) p.SetHandCustom(i, c[i, 0], c[i, 1]);
            p.FillView(1, got); Expect1(c, want);
            p.FillView(0, v0); V30Fill(p, 0, r0);
            int customs = 0; foreach (HandCustomItem h in p.HandCustom) if (h.Custom) customs++;
            bool s4 = ViewDiff(got, want) <= 1e-9 && ViewDiff(v0, r0) <= 1e-9 && customs == 8;
            // 自动 items keep following while others are custom
            p.SetHandAuto(5); p.LeanEX = 21; p.LeanEY = -3;
            p.FillView(1, got);
            bool s4b = got.QX == -21 && got.QY == -3 && got.EX == 13 && got.EY == -14;
            Check("H3 all 8 items custom: hand 1 = exactly those (diagonals from them, 水平偏移 X only), hand 0 unchanged; a 自动 item among customs follows live",
                s4 && s4b, "got " + ViewStr(got) + " | want " + ViewStr(want) + " customs=" + customs + " liveQ=" + s4b);

            // AllHandAuto
            p.AllHandAuto();
            p.FillView(1, got); V30Fill(p, 1, want);
            Check("H3 AllHandAuto -> all 8 自动, hand 1 = v3.0 mirror", AllAuto(p) && ViewDiff(got, want) <= 1e-9, "hand=" + HandStr(p) + " Δ=" + ViewDiff(got, want).ToString("E1"));

            // engine: switched hand, settled, uses the custom values
            Profile e = Profile.CreateBlank("e");
            e.Dirs[0] = new DirSetting(3, -30); e.Dirs[1] = new DirSetting(-2, 8); e.Dirs[2] = new DirSetting(62, 1); e.Dirs[3] = new DirSetting(-33, 3);
            Profile.RecomputeDiagonals(e);
            e.LeanQX = 5; e.LeanQY = -20; e.LeanEX = 30; e.LeanEY = -15; e.StandOffsetX = 4; e.StandOffsetY = -2; e.HandSwitchMs = 400;
            e.CurveAD.DurationMs = 0; e.CurveWS.DurationMs = 0; e.LeanMs = 0;
            e.SetHandCustom(2, 40, -5); e.SetHandCustom(4, -1, 6); e.SetHandCustom(5, 7, -9);
            HandView ev = new HandView(); e.FillView(1, ev);
            string bad = "";
            foreach (string k in new string[] { "", "A", "D", "W", "WA", "SA", "Q", "E", "QA" })
            {
                MotionEngine m = new MotionEngine(); GlobalSettings g = G();
                m.Update(0.01, In("K"), e, g, PreviewMode.Live, 1);
                for (int i = 0; i < 100; i++) m.Update(0.01, In(k), e, g, PreviewMode.Live, 1);
                InputState s = In(k);
                int dir = MotionEngine.DirIndex(s);
                int lean = (s.LeanL && !s.LeanR) ? -1 : ((s.LeanR && !s.LeanL) ? 1 : 0);
                double lx, ly; ev.FullLean(lean, out lx, out ly);
                double wx = (dir >= 0 ? ev.DX[dir] : 0) + lx + ev.SX, wy = (dir >= 0 ? ev.DY[dir] : 0) + ly + ev.SY;
                if (Math.Abs(m.Debug.UiOffsetX - wx) > 1e-9 || Math.Abs(m.Debug.UiOffsetY - wy) > 1e-9 || !m.Debug.Switched)
                    bad += " " + k + "=(" + F(m.Debug.UiOffsetX) + "," + F(m.Debug.UiOffsetY) + ") want (" + F(wx) + "," + F(wy) + ")";
            }
            Check("H3 engine, 换手 settled with custom A / 站着 / Q: every key = the hand-1 view (custom where set, mirror elsewhere)",
                bad.Length == 0, bad.Length == 0 ? "9 key sets exact" : bad);
        }

        // ---------------- 3.4 persistence ----------------

        static bool SameHand(Profile a, Profile b)
        {
            if (a == null || b == null || a.HandCustom == null || b.HandCustom == null || a.HandCustom.Count != b.HandCustom.Count) return false;
            for (int i = 0; i < a.HandCustom.Count; i++)
            {
                HandCustomItem x = a.HandCustom[i], y = b.HandCustom[i];
                if (x == null || y == null || x.Custom != y.Custom) return false;
                if (x.Custom && (x.X != y.X || (i < 7 && x.Y != y.Y))) return false;
            }
            return true;
        }

        static bool Shared(Profile a, Profile b)
        {
            if (a.HandCustom == null || b.HandCustom == null) return false;
            if (object.ReferenceEquals(a.HandCustom, b.HandCustom)) return true;
            for (int i = 0; i < Math.Min(a.HandCustom.Count, b.HandCustom.Count); i++) if (object.ReferenceEquals(a.HandCustom[i], b.HandCustom[i])) return true;
            return false;
        }

        static void H4Persistence()
        {
            string err;
            // JSON round trip
            GlobalSettings g = GlobalSettings.CreateDefault(); g.Validate(); g.Active.AllHandAuto(); // v5.0: start from all 自动
            Profile p = g.Active;
            p.SetHandCustom(2, 40.5, -5.25); p.SetHandCustom(7, -3.5, 0); p.SetHandCustom(4, 0, 0);
            string json = SettingsIO.Serialize(g);
            GlobalSettings back; bool ok = SettingsIO.TryParse(json, out back, out err);
            bool rt = ok && SameHand(p, back.Active) && back.Active.HandCustom[2].Custom && back.Active.HandCustom[2].X == 40.5 && back.Active.HandCustom[2].Y == -5.25
                && back.Active.HandCustom[4].Custom && !back.Active.HandCustom[0].Custom && json.Contains("\"HandCustom\"") && SettingsIO.Serialize(back) == json;
            Check("H4 JSON round trip keeps every HandCustom item (custom 0/0 stays custom), reload stable", rt, ok ? "back=" + HandStr(back.Active) : err);

            // SaveAsDefault -> edit -> RestoreSaved
            p.SaveAsDefault("2026-10-27 20:00");
            bool snapDeep = p.SavedDefault != null && SameHand(p, p.SavedDefault) && !Shared(p, p.SavedDefault);
            p.SetHandCustom(2, 1, 1); p.SetHandAuto(7); p.SetHandCustom(0, 9, 9);
            bool snapIndep = p.SavedDefault.HandCustom[2].X == 40.5 && p.SavedDefault.HandCustom[7].Custom && !p.SavedDefault.HandCustom[0].Custom;
            Profile r = g.RestoreSaved(p);
            bool rs = r.HandCustom[2].Custom && r.HandCustom[2].X == 40.5 && r.HandCustom[2].Y == -5.25 && r.HandCustom[7].Custom && r.HandCustom[7].X == -3.5
                && !r.HandCustom[0].Custom && !Shared(r, r.SavedDefault);
            Check("H4 保存为默认 snapshots HandCustom (deep copy, later edits don't leak), 恢复到我存的默认 brings it back",
                snapDeep && snapIndep && rs, "snapDeep=" + snapDeep + " snapIndep=" + snapIndep + " restored=" + HandStr(r));

            // export -> import
            string ex = SettingsIO.ExportProfile(r);
            List<Profile> imp; bool iok = SettingsIO.TryParseImport(ex, out imp, out err);
            bool io = iok && imp.Count == 1 && SameHand(r, imp[0]) && ex.Contains("\"HandCustom\"");
            GlobalSettings tgt = GlobalSettings.CreateDefault(); tgt.Validate();
            string nm = iok ? tgt.AddImported(imp) : null;
            Profile added = nm == null ? null : tgt.FindProfile(nm);
            bool addOk = added != null && SameHand(r, added) && !Shared(added, imp[0]);
            if (added != null) { imp[0].SetHandCustom(2, 77, 77); addOk = addOk && added.HandCustom[2].X == 40.5; }
            // a full settings.json imported too
            List<Profile> imp2; bool iok2 = SettingsIO.TryParseImport(SettingsIO.Serialize(g), out imp2, out err);
            bool io2 = iok2 && imp2.Count == 1 && SameHand(g.Active, imp2[0]);
            Check("H4 导出 -> 导入 (single profile and full settings.json) carries HandCustom; the imported copy is independent",
                io && addOk && io2, "export/import=" + io + " added=" + addOk + " fullFile=" + io2 + (iok ? "" : " " + err));

            // Clone / CloneValues / GlobalSettings.Clone independence
            Profile c1 = r.Clone(), c2 = r.CloneValues();
            GlobalSettings gc = g.Clone(true);
            bool eq = SameHand(r, c1) && SameHand(r, c2) && SameHand(g.Active, gc.Active) && SameHand(r.SavedDefault, c1.SavedDefault);
            bool indep = !Shared(r, c1) && !Shared(r, c2) && !Shared(g.Active, gc.Active) && !Shared(r.SavedDefault, c1.SavedDefault);
            c1.SetHandCustom(2, 5, 5); c2.AllHandAuto(); gc.Active.SetHandCustom(3, 8, 8); c1.SavedDefault.SetHandAuto(2);
            bool after = r.HandCustom[2].X == 40.5 && r.HandCustom[2].Custom && !r.HandCustom[3].Custom && g.Active.HandCustom[2].X == 40.5 && !g.Active.HandCustom[3].Custom
                && r.SavedDefault.HandCustom[2].Custom;
            Check("H4 Clone / CloneValues / GlobalSettings.Clone(true): HandCustom equal and deep (edits to a copy don't touch the original)",
                eq && indep && after, "equal=" + eq + " noSharedRefs=" + indep + " originalUntouched=" + after);

            // CreateBlank all 自动
            Check("H4 CreateBlank / new Profile() / old built-in: HandCustom 8 × 自动; v5.0 CreateLeoDefault = Leo's v5 file items", AllAuto(Profile.CreateBlank("b")) && HandStr(Profile.CreateLeoDefault("l")) == HandStr(V50Expected()) && AllAuto(Profile.CreateLeoDefaultLegacy("l")) && AllAuto(new Profile()),
                "blank=" + HandStr(Profile.CreateBlank("b")) + " leo=" + HandStr(Profile.CreateLeoDefault("l")));

            // Validate: null / short / long lists, null items, clamps, NaN / ∞
            Profile v = Profile.CreateLeoDefaultLegacy("v"); v.HandCustom = null; v.Validate();
            bool vNull = AllAuto(v);
            Profile s = Profile.CreateLeoDefaultLegacy("s");
            s.HandCustom = new List<HandCustomItem>();
            HandCustomItem a0 = new HandCustomItem(); a0.Custom = true; a0.X = 9999; a0.Y = -9999; s.HandCustom.Add(a0);
            s.HandCustom.Add(null);
            HandCustomItem a2 = new HandCustomItem(); a2.Custom = true; a2.X = double.NaN; a2.Y = double.PositiveInfinity; s.HandCustom.Add(a2);
            s.Validate();
            bool vShort = s.HandCustom.Count == 8 && s.HandCustom[0].Custom && s.HandCustom[0].X == 500 && s.HandCustom[0].Y == -500 && s.HandCustom[1] != null && !s.HandCustom[1].Custom
                && s.HandCustom[2].Custom && s.HandCustom[2].X == 0 && s.HandCustom[2].Y == 0;
            for (int i = 3; i < 8; i++) if (s.HandCustom[i] == null || s.HandCustom[i].Custom) vShort = false;
            Profile l = Profile.CreateLeoDefaultLegacy("l");
            l.HandCustom = new List<HandCustomItem>();
            double[,] raw = { { 600, -600 }, { -600, 600 }, { 501, 0 }, { 0, -501 }, { 300, -300 }, { 700, -700 }, { -700, 700 }, { -999, 0 }, { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 } };
            for (int i = 0; i < 12; i++) { HandCustomItem h = new HandCustomItem(); h.Custom = true; h.X = raw[i, 0]; h.Y = raw[i, 1]; l.HandCustom.Add(h); }
            HandCustomItem inf = l.HandCustom[6]; inf.X = double.NegativeInfinity;
            l.Validate();
            double[,] want = { { 500, -500 }, { -500, 500 }, { 500, 0 }, { 0, -500 }, { 200, -200 }, { 500, -500 }, { 0, 500 }, { -500, 0 } };
            bool vLong = l.HandCustom.Count == 8;
            string lb = "";
            for (int i = 0; vLong && i < 8; i++)
            {
                HandCustomItem h = l.HandCustom[i];
                // v4.1 (Leo 17 Nov 2026): the 站着偏移 item is always 自动 after Validate (standing still = screen centre)
                if (i == Profile.HandStand ? h.Custom : (!h.Custom || h.X != want[i, 0] || (i < 7 && h.Y != want[i, 1]))) { vLong = false; lb = " item" + i + "=(" + F(h.X) + "," + F(h.Y) + ") want (" + F(want[i, 0]) + "," + F(want[i, 1]) + ")"; }
            }
            // via JSON: missing / null / short list
            GlobalSettings j; bool jok = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\"},{\"Name\":\"b\",\"HandCustom\":null},"
                + "{\"Name\":\"c\",\"HandCustom\":[{\"Custom\":true,\"X\":9999,\"Y\":1},{\"Custom\":false,\"X\":5,\"Y\":5}]},{\"Name\":\"d\",\"Hand\":{\"Manual\":true}}]}", out j, out err);
            bool vJson = jok && AllAuto(j.Profiles[0]) && AllAuto(j.Profiles[1]) && AllAuto(j.Profiles[3]) && j.Profiles[2].HandCustom.Count == 8 && j.Profiles[2].HandCustom[0].Custom
                && j.Profiles[2].HandCustom[0].X == 500 && j.Profiles[2].HandCustom[0].Y == 1 && !j.Profiles[2].HandCustom[1].Custom;
            Check("H4 Validate: null -> 8 自动; short list padded (null items -> 自动); 12 items truncated to 8; X/Y clamped like the normal field (dirs/lean/drift ±500, 站着 ±200); NaN/∞ -> 0; JSON missing/null/short; old \"Hand\" ignored",
                vNull && vShort && vLong && vJson, "null=" + vNull + " short=" + vShort + " (" + HandStr(s) + ") long=" + vLong + lb + " json=" + vJson + (jok ? "" : " " + err));
        }
    }
}
