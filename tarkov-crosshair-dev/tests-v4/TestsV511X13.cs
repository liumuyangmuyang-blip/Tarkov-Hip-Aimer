// v5.1.1 (X13, Leo 21 Nov 2026): W/S short presses a bit FASTER than A/D. GlobalSettings has separate W/S copies of the four
// 短按和换向 tunables (WsTapThresholdMs 160, WsReverseMinMs 180, WsReturnTimePct 80, WsReversePauseMs 0; A/D stays 200 / 250 /
// 100 / 0). The engine uses the W/S values on the Y axis and the A/D values on the X axis.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X13Init() { V511X13Tests(); }

        /// <summary>X13: a 1-axis tap: ms from release until that axis is back at rest (MoveX / MoveY 0, not travelling), peak px.</summary>
        static void X13Tap(GlobalSettings g, char key, double held, out double backMs, out double peak, out int taps)
        {
            Profile p = Profile.CreateLeoDefault("x13");
            int ax = key == 'W' || key == 'S' ? 1 : 0;
            List<X4Press> ps = new List<X4Press>(); ps.Add(new X4Press(100, 100 + held, key));
            double rel = 100 + held, back = -1, pk = 0;
            MotionEngine e = X12Script(p, g, ps, 3000, 1000, delegate(double t, MotionEngine m)
            {
                double v = ax == 0 ? m.MoveX : m.MoveY;
                pk = Math.Max(pk, Math.Abs(v));
                if (t > rel && back < 0 && v == 0 && !m.Traveling(ax)) back = t - rel;
            });
            backMs = back; peak = pk; taps = e.TapCount;
        }

        static void V511X13Tests()
        {
            // X13-1 defaults, old files, clamps, save round trip, 恢复默认 values
            {
                string d = "";
                GlobalSettings n = GlobalSettings.CreateDefault();
                if (n.TapThresholdMs != 200 || n.ReverseMinMs != 250 || n.ReturnTimePct != 100 || n.ReversePauseMs != 0) d += "A/D defaults; ";
                if (n.WsTapThresholdMs != 160 || n.WsReverseMinMs != 180 || n.WsReturnTimePct != 80 || n.WsReversePauseMs != 0) d += "W/S defaults; ";
                GlobalSettings o; string err;
                if (LeoDefaultJson.Text.IndexOf("WsTapThresholdMs", StringComparison.Ordinal) >= 0) d += "LeoDefaultJson already has Ws fields; ";
                if (!SettingsIO.TryParse(LeoDefaultJson.Text, out o, out err) || o.WsTapThresholdMs != 160 || o.WsReverseMinMs != 180 || o.WsReturnTimePct != 80 || o.WsReversePauseMs != 0) d += "old file; ";
                GlobalSettings c = GlobalSettings.CreateDefault();
                c.WsTapThresholdMs = 1000; c.WsReverseMinMs = -5; c.WsReturnTimePct = 1; c.WsReversePauseMs = 900; c.Validate();
                if (c.WsTapThresholdMs != 250 || c.WsReverseMinMs != 0 || c.WsReturnTimePct != 25 || c.WsReversePauseMs != 300) d += "clamp hi/lo; ";
                c.WsTapThresholdMs = 1; c.WsReverseMinMs = 9999; c.WsReturnTimePct = 9999; c.WsReversePauseMs = -1; c.Validate();
                if (c.WsTapThresholdMs != 60 || c.WsReverseMinMs != 600 || c.WsReturnTimePct != 300 || c.WsReversePauseMs != 0) d += "clamp lo/hi; ";
                GlobalSettings b = GlobalSettings.CreateDefault();
                b.WsTapThresholdMs = double.NaN; b.WsReversePauseMs = double.PositiveInfinity; b.Validate();
                if (b.WsTapThresholdMs != 160 || b.WsReversePauseMs != 0) d += "nan; ";
                GlobalSettings r = GlobalSettings.CreateDefault();
                r.WsTapThresholdMs = 110; r.WsReverseMinMs = 140; r.WsReturnTimePct = 65; r.WsReversePauseMs = 30; r.TapThresholdMs = 190; r.Validate();
                GlobalSettings rr;
                if (!SettingsIO.TryParse(SettingsIO.Serialize(r), out rr, out err) || rr.WsTapThresholdMs != 110 || rr.WsReverseMinMs != 140 || rr.WsReturnTimePct != 65
                    || rr.WsReversePauseMs != 30 || rr.TapThresholdMs != 190 || rr.ReverseMinMs != 250) d += "round trip; ";
                GlobalSettings fresh = new GlobalSettings();
                if (fresh.WsTapThresholdMs != 160 || fresh.WsReverseMinMs != 180 || fresh.WsReturnTimePct != 80 || fresh.WsReversePauseMs != 0) d += "new GlobalSettings() (恢复默认 source); ";
                Check("X13-1 W/S tunables: defaults 160 / 180 / 80 % / 0 (A/D 200 / 250 / 100 % / 0), old file -> defaults, same clamps as A/D, saved", d.Length == 0, d);
            }
            // X13-2 恢复默认 resets both columns; the 移动 page shows both columns (source check)
            {
                string src = System.IO.File.ReadAllText(System.IO.Path.Combine(X13Src(), "SettingsForm.PageMove.cs"));
                int i = src.IndexOf("private Control BuildTapBox()", StringComparison.Ordinal);
                string box = i < 0 ? "" : src.Substring(i, Math.Min(6000, src.Length - i));
                string d = "";
                foreach (string s in new string[] { "左右（A/D）", "前后（W/S）", "G.WsTapThresholdMs = d.WsTapThresholdMs", "G.WsReverseMinMs = d.WsReverseMinMs",
                    "G.WsReturnTimePct = d.WsReturnTimePct", "G.WsReversePauseMs = d.WsReversePauseMs", "G.TapThresholdMs = d.TapThresholdMs", "G.ReversePauseMs = d.ReversePauseMs" })
                    if (box.IndexOf(s, StringComparison.Ordinal) < 0) d += s + "; ";
                Check("X13-2 移动 page 短按和换向: 左右（A/D）and 前后（W/S）columns, 恢复默认 resets both", d.Length == 0, d);
            }
            GlobalSettings g = X12G();
            // X13-3 W taps use the W/S threshold (160): 120 ms counts as 短按 on Y, 180 ms does not on Y (but an A of 180 ms is a
            // 短按 on X: 200); a W tap comes back faster than an A tap of the same length
            {
                string d = ""; string info = "";
                foreach (double held in new double[] { 60, 100, 150 })
                {
                    double wb, wp, ab, ap; int wt, at;
                    X13Tap(g, 'W', held, out wb, out wp, out wt);
                    X13Tap(g, 'A', held, out ab, out ap, out at);
                    info += held + " ms: W back " + F(wb) + " ms (peak " + F(wp) + "), A back " + F(ab) + " ms (peak " + F(ap) + "); ";
                    if (!(wb > 0 && ab > 0 && wb < ab)) d += held + " ms W not faster; ";
                }
                // threshold: per-axis tap count with a profile whose W and A only move one axis each
                Profile p = Profile.CreateLeoDefault("x13");
                int w120 = X13Taps(g, 'W', 120), w180 = X13Taps(g, 'W', 180), a180 = X13Taps(g, 'A', 180), a220 = X13Taps(g, 'A', 220);
                info += "taps W120 " + w120 + " W180 " + w180 + " A180 " + a180 + " A220 " + a220;
                // W on Leo默认 moves X too (to the centre x, A/D threshold) and Y (W/S threshold): 120 = both axes, 180 = X only
                if (!(w120 == w180 + 1)) d += "W threshold not 160 on Y; ";
                if (!(a180 > a220)) d += "A threshold not 200; ";
                Check("X13-3 W tap uses 短按 160 ms on Y (W 180 ms = not a tap on Y; A 180 ms still a tap), W tap comes back faster than an A tap of the same length", d.Length == 0, d + info);
            }
            // X13-4 W -> S reversal: >= WsReverseMinMs (180) and faster than A -> D (same hold); S -> W the same
            {
                string d = ""; string info = "";
                double ad = X13Rev(g, "AD", 0), da = X13Rev(g, "DA", 0), ws = X13Rev(g, "WS", 1), sw = X13Rev(g, "SW", 1);
                info = "A->D " + F(ad) + " D->A " + F(da) + " W->S " + F(ws) + " S->W " + F(sw) + " ms";
                if (!(ws >= g.WsReverseMinMs - 1e-6 && sw >= g.WsReverseMinMs - 1e-6)) d += "below WsReverseMinMs; ";
                if (!(ws < ad && sw < da)) d += "not faster than A/D; ";
                if (!(ad >= g.ReverseMinMs - 1e-6)) d += "A/D below ReverseMinMs; ";
                // a long WsReverseMinMs stretches only W/S
                GlobalSettings g2 = X12G(); g2.WsReverseMinMs = 600;
                double ws2 = X13Rev(g2, "WS", 1), ad2 = X13Rev(g2, "AD", 0);
                info += "; WsReverseMinMs 600: W->S " + F(ws2) + " A->D " + F(ad2);
                if (!(ws2 >= 600 - 1e-6 && ad2 == ad)) d += "WsReverseMinMs not Y-only; ";
                Check("X13-4 W->S / S->W reversal >= 换向最短 W/S (" + F(g.WsReverseMinMs) + " ms) and faster than A->D / D->A", d.Length == 0, d + info);
            }
            // X13-5 rapid W/S alternation with the W/S defaults: no build-up (last second <= 5 px for presses <= 150 ms), settles
            {
                bool ok = true; string bad = ""; double worstLate = 0; int runs = 0;
                foreach (double fps in new double[] { 60, 144, 240 })
                    foreach (double per in new double[] { 60, 80, 100, 120, 150 })
                        for (int seed = 0; seed < 3; seed++)
                        {
                            Profile p = Profile.CreateLeoDefault("x13");
                            Random rnd = new Random(seed * 131 + (int)per);
                            List<X4Press> ps = seed == 0 ? X4Alt("WS", per, 3000, 0, 0, rnd, 0) : X4Alt("WS", per, 3000, rnd.Next(0, 41), 0.3, rnd, 0);
                            double last = 0;
                            foreach (X4Press q in ps) last = Math.Max(last, q.E);
                            double late = 0;
                            MotionEngine e = X12Script(p, g, ps, last + 3000, fps, delegate(double t, MotionEngine m)
                            {
                                if (t < last && t >= last - 1000) late = Math.Max(late, Math.Abs(m.MoveY));
                            });
                            runs++; worstLate = Math.Max(worstLate, late);
                            bool settled = e.MoveX == 0 && e.MoveY == 0 && !e.Traveling(0) && !e.Traveling(1);
                            if (late > 5 || !settled) { ok = false; if (bad.Length < 300) bad += per + " @" + fps + " s" + seed + " late " + F(late) + " settled " + settled + "; "; }
                        }
                Check("X13-5 rapid W/S alternation with W/S defaults (" + runs + " runs): last second <= 5 px from rest, settles", ok, bad == "" ? "worst " + F(worstLate) + " px" : bad);
            }
            // X13-6 A/D (X axis) bit-identical: the X trace of A/D taps, spam, reversals and mixed W/A scripts does not depend on
            // the W/S tunables at all (W/S at the new defaults vs W/S = the A/D values vs W/S at extremes)
            {
                string d = ""; int frames = 0;
                GlobalSettings ga = X12G();
                GlobalSettings gb = X12G(); gb.WsTapThresholdMs = gb.TapThresholdMs; gb.WsReverseMinMs = gb.ReverseMinMs; gb.WsReturnTimePct = gb.ReturnTimePct; gb.WsReversePauseMs = gb.ReversePauseMs;
                GlobalSettings gc = X12G(); gc.WsTapThresholdMs = 60; gc.WsReverseMinMs = 600; gc.WsReturnTimePct = 300; gc.WsReversePauseMs = 300;
                List<List<X4Press>> scripts = new List<List<X4Press>>();
                List<X4Press> s1 = new List<X4Press>(); s1.Add(new X4Press(100, 180, 'A')); s1.Add(new X4Press(600, 1900, 'D')); s1.Add(new X4Press(1900, 3200, 'A')); scripts.Add(s1);
                scripts.Add(X4Alt("AD", 90, 3000, 0, 0, new Random(7), 0));
                List<X4Press> s3 = X4Alt("WS", 110, 2500, 20, 0.3, new Random(3), 0); s3.AddRange(X4Alt("AD", 130, 2500, 10, 0.3, new Random(5), 0)); scripts.Add(s3);
                List<X4Press> s4 = new List<X4Press>(); s4.Add(new X4Press(0, 900, 'W')); s4.Add(new X4Press(300, 1400, 'A')); s4.Add(new X4Press(900, 2000, 'S')); s4.Add(new X4Press(1400, 2600, 'D')); scripts.Add(s4);
                for (int si = 0; si < scripts.Count; si++)
                    foreach (double fps in new double[] { 60, 144 })
                    {
                        List<double> xa = new List<double>(), xb = new List<double>(), xc = new List<double>();
                        X12Script(Profile.CreateLeoDefault("x13"), ga, scripts[si], 5000, fps, delegate(double t, MotionEngine m) { xa.Add(m.MoveX); });
                        X12Script(Profile.CreateLeoDefault("x13"), gb, scripts[si], 5000, fps, delegate(double t, MotionEngine m) { xb.Add(m.MoveX); });
                        X12Script(Profile.CreateLeoDefault("x13"), gc, scripts[si], 5000, fps, delegate(double t, MotionEngine m) { xc.Add(m.MoveX); });
                        for (int k = 0; k < xa.Count; k++)
                        {
                            frames++;
                            if (xa[k] != xb[k] || xa[k] != xc[k]) { d += "script " + si + " @" + fps + " frame " + k + " " + xa[k] + " / " + xb[k] + " / " + xc[k] + "; "; break; }
                        }
                    }
                Check("X13-6 A/D (X axis) bit-identical whatever the W/S tunables (" + frames + " frames, taps / spam / reversals / mixed W A S D)", d.Length == 0, d);
            }
            // X13-7 W/S = the A/D values gives the old (X12) Y behaviour: W tap / W->S use the same times as before X13
            {
                GlobalSettings gb = X12G(); gb.WsTapThresholdMs = gb.TapThresholdMs; gb.WsReverseMinMs = gb.ReverseMinMs; gb.WsReturnTimePct = gb.ReturnTimePct; gb.WsReversePauseMs = gb.ReversePauseMs;
                double wb, wp, ab, ap; int wt, at;
                X13Tap(gb, 'W', 100, out wb, out wp, out wt);
                X13Tap(g, 'W', 100, out ab, out ap, out at);
                double wsOld = X13Rev(gb, "WS", 1), wsNew = X13Rev(g, "WS", 1);
                bool ok = wb > ab && wp == ap && wsOld > wsNew && wsOld >= gb.ReverseMinMs - 1e-6;
                Check("X13-7 W/S set to the A/D values = slower W/S (tap back " + F(wb) + " vs " + F(ab) + " ms, W->S " + F(wsOld) + " vs " + F(wsNew) + " ms), same tap peak", ok, "peaks " + wp + " / " + ap);
            }
            // X13-8 held keys and diagonals still land exactly on their stored offsets with the W/S defaults
            {
                bool ok = true; string bad = "";
                string[] keys = { "W", "S", "A", "D", "WA", "WD", "SA", "SD" };
                for (int dd = 0; dd < 8; dd++)
                    foreach (string pre in new string[] { "", "W", "S", "SA", "WD" })
                    {
                        Profile p = Profile.CreateLeoDefault("x13");
                        List<X4Press> ps = new List<X4Press>();
                        double t0 = 0;
                        if (pre.Length > 0) { foreach (char ch in pre) ps.Add(new X4Press(0, 1200, ch)); t0 = 1200; }
                        foreach (char ch in keys[dd]) ps.Add(new X4Press(t0, t0 + 3000, ch));
                        MotionEngine e = X12Script(p, g, ps, t0 + 2500, 144, null);
                        int di = X13DirIndex(keys[dd]);
                        if (e.MoveX != p.Dirs[di].OffX || e.MoveY != p.Dirs[di].OffY)
                        { ok = false; if (bad.Length < 300) bad += pre + "->" + keys[dd] + " " + e.MoveX + "," + e.MoveY + " vs " + p.Dirs[di].OffX + "," + p.Dirs[di].OffY + "; "; }
                    }
                Check("X13-8 held W / S / A / D / diagonals land exactly on their stored offsets with the W/S defaults (from rest and after a reversal)", ok, bad);
            }
            // X13-9 text: help, FAQ, tutorial and both READMEs name the W/S defaults; the README copies are identical
            {
                string src = X13Src(), d = "";
                string help = System.IO.File.ReadAllText(System.IO.Path.Combine(src, "HelpText.cs"));
                string faq = System.IO.File.ReadAllText(System.IO.Path.Combine(src, "FaqText.cs"));
                string tut = System.IO.File.ReadAllText(System.IO.Path.Combine(src, "TutorialStepsV51.cs"));
                string guide = System.IO.File.ReadAllText(System.IO.Path.Combine(src, "GuideText.cs"));
                string root = System.IO.Path.GetDirectoryName(src);
                string r1 = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "README.txt"));
                string r2 = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "说明 README.txt"));
                if (help.IndexOf("W/S 160", StringComparison.Ordinal) < 0 || help.IndexOf("W/S 180", StringComparison.Ordinal) < 0) d += "help; ";
                if (faq.IndexOf("W/S 160", StringComparison.Ordinal) < 0) d += "faq; ";
                if (tut.IndexOf("W/S 160", StringComparison.Ordinal) < 0) d += "tutorial; ";
                if (guide.IndexOf("W/S 160", StringComparison.Ordinal) < 0) d += "guide; ";
                if (r1 != r2) d += "README copies differ; ";
                if (r1.IndexOf("W/S 160 ms", StringComparison.Ordinal) < 0 || r1.IndexOf("W/S 80%", StringComparison.Ordinal) < 0) d += "README; ";
                Check("X13-9 help / FAQ / tutorial / guide / both READMEs name the W/S defaults", d.Length == 0, d);
            }
        }

        static string X13Src() { return UiSrcDir(); }

        static int X13DirIndex(string k)
        {
            switch (k)
            {
                case "W": return 0; case "S": return 1; case "A": return 2; case "D": return 3;
                case "WA": return 4; case "WD": return 5; case "SA": return 6; default: return 7;
            }
        }

        static int X13Taps(GlobalSettings g, char key, double held)
        {
            double b, p; int t;
            X13Tap(g, key, held, out b, out p, out t);
            return t;
        }

        /// <summary>X13: hold order[0] 1500 ms, then order[1]: ms from the switch until the axis lands exactly on order[1]'s offset.</summary>
        static double X13Rev(GlobalSettings g, string order, int ax)
        {
            Profile p = Profile.CreateLeoDefault("x13");
            List<X4Press> ps = new List<X4Press>(); ps.Add(new X4Press(0, 1500, order[0])); ps.Add(new X4Press(1500, 5000, order[1]));
            int di = X13DirIndex(order[1].ToString());
            double target = ax == 0 ? p.Dirs[di].OffX : p.Dirs[di].OffY, land = -1;
            X12Script(p, g, ps, 4500, 1000, delegate(double t, MotionEngine m)
            {
                double v = ax == 0 ? m.MoveX : m.MoveY;
                if (t >= 1500 && land < 0 && v == target && !m.Traveling(ax)) land = t - 1500;
            });
            return land;
        }
    }
}
