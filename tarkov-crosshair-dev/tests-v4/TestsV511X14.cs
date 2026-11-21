// v5.1.1 (X14, Leo 21 Nov 2026): 站立侧身. Leo: standing fire, Q/E lean, A/D spray and A+Q / D+E are accurate, but Q or E
// while STANDING STILL (no move key) with short bursts is not. New per-profile StandLeanQX/QY/EX/EY (+ 换手 items, 自动 =
// mirror) = the lean target with no move key; StandLeanSame (和移动时一样, default on, also for old files) keeps the old
// behaviour bit for bit. With a move key the normal lean applies exactly; the engine blends over the move 总时长.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X14Init() { V511X14Tests(); }

        static InputState X14Keys(bool q, bool e, bool a, bool d, bool w, bool fire)
        {
            InputState i = new InputState();
            i.LeanL = q; i.LeanR = e; i.Left = a; i.Right = d; i.Fwd = w; i.Fire = fire;
            return i;
        }

        /// <summary>Flag-off copy of p whose 站立侧身 equals its lean (normal hand) and its 换手 lean (custom rows copied).</summary>
        static Profile X14SameAsLean(Profile p)
        {
            Profile r = p.Clone();
            r.StandLeanSame = false;
            r.StandLeanQX = p.LeanQX; r.StandLeanQY = p.LeanQY; r.StandLeanEX = p.LeanEX; r.StandLeanEY = p.LeanEY;
            double x, y;
            p.HandValue(Profile.HandQ, out x, out y); r.SetStandLeanHandCustom(Profile.StandLeanHandQ, x, y);
            p.HandValue(Profile.HandE, out x, out y); r.SetStandLeanHandCustom(Profile.StandLeanHandE, x, y);
            return r;
        }

        static ulong X14Hash(Profile p, GlobalSettings g, int seed, int frames)
        {
            MotionEngine e = new MotionEngine(); e.GameFocused = true; e.OwnForeground = true;
            Random rnd = new Random(seed);
            InputState s = new InputState();
            ulong h = 1469598103934665603UL;
            for (int k = 0; k < frames; k++)
            {
                if (rnd.Next(12) == 0)
                {
                    s = new InputState();
                    s.Fwd = rnd.Next(4) == 0; s.Back = rnd.Next(5) == 0; s.Left = rnd.Next(4) == 0; s.Right = rnd.Next(4) == 0;
                    s.LeanL = rnd.Next(3) == 0; s.LeanR = rnd.Next(4) == 0; s.Fire = rnd.Next(3) == 0; s.HandSwitch = rnd.Next(25) == 0;
                }
                double dt = (rnd.Next(3) == 0 ? 1.0 / 60 : rnd.Next(2) == 0 ? 1.0 / 144 : 1.0 / 240);
                e.Update(dt, s, p, g, PreviewMode.Live, 1.0);
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetX)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetY)) * 1099511628211UL;
            }
            return h;
        }

        static void V511X14Tests()
        {
            GlobalSettings g = X12G();
            GlobalSettings gt = X12G(); gt.LeanMode = "hold"; gt.HandMode = "toggle";

            // X14-1 flag on = old path: a flag-off profile whose 站立侧身 equals its lean gives the same bits (the blend of two equal
            // values is exact), on Leo默认 (站立 point on), the blank profile with a lean, and 换手 custom rows
            {
                bool ok = true; string bad = "";
                List<Profile> ps = new List<Profile>();
                ps.Add(X18Pre("x14"));
                Profile b = Profile.CreateBlank("x14b"); b.LeanQX = -30; b.LeanQY = -12; b.LeanEX = 25; b.LeanEY = -8; ps.Add(b);
                Profile c = X18Pre("x14c"); c.SetHandCustom(Profile.HandQ, -40, -10); c.SetHandCustom(Profile.HandE, 18, -20); ps.Add(c);
                for (int i = 0; i < ps.Count; i++)
                    foreach (GlobalSettings gg in new GlobalSettings[] { g, gt })
                        for (int seed = 1; seed <= 3; seed++)
                        {
                            ulong h0 = X14Hash(ps[i], gg, seed, 20000), h1 = X14Hash(X14SameAsLean(ps[i]), gg, seed, 20000);
                            if (h0 != h1) { ok = false; bad += " profile " + i + " seed " + seed + ";"; }
                        }
                Check("X14-1 和移动时一样 on = old behaviour: flag-off with 站立侧身 = lean hashes bit-identical (3 profiles x 2 modes x 3 seeds, 20000 frames)", ok, bad);
            }
            // X14-2 flag off, standing Q / E (no move key): lands exactly on StandLeanQ / E = where a flag-on profile with that lean lands
            foreach (int side in new int[] { -1, 1 })
            {
                Profile p = X18Pre("x14");
                p.SetStandLeanSame(false);
                if (side < 0) { p.StandLeanQX = -40; p.StandLeanQY = -30; } else { p.StandLeanEX = 35; p.StandLeanEY = 6; }
                Profile r = X18Pre("x14r");
                if (side < 0) { r.LeanQX = -40; r.LeanQY = -30; } else { r.LeanEX = 35; r.LeanEY = 6; }
                InputState k = X14Keys(side < 0, side > 0, false, false, false, false);
                MotionEngine e = X8Run(p, g, false, k, 3, null), er = X8Run(r, g, false, k, 3, null);
                MotionEngine old = X8Run(X18Pre("x14o"), g, false, k, 3, null);
                bool ok = e.OffsetX == er.OffsetX && e.OffsetY == er.OffsetY && (e.OffsetX != old.OffsetX || e.OffsetY != old.OffsetY);
                Check("X14-2 standing " + (side < 0 ? "Q" : "E") + " (flag off) lands exactly on 站立侧身 (" + F(e.OffsetX) + ", " + F(e.OffsetY) + "; normal lean " + F(old.OffsetX) + ", " + F(old.OffsetY) + ")",
                    ok, "want " + er.OffsetX + "," + er.OffsetY + " got " + e.OffsetX + "," + e.OffsetY);
            }
            // X14-3 a move key held: A+Q, D+E, W+Q, A+E land exactly where they land with the flag on (both hands)
            {
                bool ok = true; string bad = "";
                Profile on = X18Pre("x14");
                Profile off = on.Clone(); off.SetStandLeanSame(false);
                off.StandLeanQX = -40; off.StandLeanQY = -30; off.StandLeanEX = 35; off.StandLeanEY = 6;
                InputState[] ks = { X14Keys(true, false, true, false, false, false), X14Keys(false, true, false, true, false, false),
                    X14Keys(true, false, false, false, true, false), X14Keys(false, true, true, false, false, false), X14Keys(true, false, true, false, false, true) };
                string[] names = { "A+Q", "D+E", "W+Q", "A+E", "A+Q+fire" };
                for (int h = 0; h < 2; h++)
                    for (int i = 0; i < ks.Length; i++)
                    {
                        MotionEngine a = X8Run(on, g, h == 1, ks[i], 3, null), b = X8Run(off, g, h == 1, ks[i], 3, null);
                        if (a.OffsetX != b.OffsetX || a.OffsetY != b.OffsetY) { ok = false; bad += " " + names[i] + (h == 1 ? " 换手" : "") + ": " + a.OffsetX + "," + a.OffsetY + " vs " + b.OffsetX + "," + b.OffsetY + ";"; }
                    }
                Check("X14-3 moving while leaning (A+Q, D+E, W+Q, A+E, A+Q+fire; both hands): exactly the old spot with 站立侧身 set", ok, bad);
            }
            // X14-4 Q held, then A pressed and released (240 fps and 60 fps): no frame-to-frame jump; 240 fps worst step <= 2 px
            // when the move itself is well under 2 px/frame; compared with the same path with the flag on
            foreach (double fps in new double[] { 240, 60 })
            {
                Profile on = X18Pre("x14");
                Profile off = on.Clone(); off.SetStandLeanSame(false);
                off.StandLeanQX = on.LeanQX - 20; off.StandLeanQY = on.LeanQY - 15; // 25 px away from the moving lean
                double[] worst = new double[2];
                double[] endX = new double[2], endY = new double[2];
                for (int v = 0; v < 2; v++)
                {
                    Profile p = v == 0 ? on : off;
                    MotionEngine e = new MotionEngine(); e.GameFocused = true; e.OwnForeground = true;
                    double dt = 1.0 / fps, px = double.NaN, py = 0;
                    int n = (int)(4.0 * fps);
                    for (int k = 0; k < n; k++)
                    {
                        double t = k * dt;
                        bool q = t >= 0.2, a = t >= 1.2 && t < 2.4;
                        e.Update(dt, X14Keys(q, false, a, false, false, false), p, g, PreviewMode.Live, 1.0);
                        if (t >= 1.0 && !double.IsNaN(px)) worst[v] = Math.Max(worst[v], Math.Sqrt((e.OffsetX - px) * (e.OffsetX - px) + (e.OffsetY - py) * (e.OffsetY - py)));
                        px = e.OffsetX; py = e.OffsetY;
                        if (k == (int)(2.3 * fps)) { endX[v] = e.OffsetX; endY[v] = e.OffsetY; }
                    }
                }
                double lim = fps >= 240 ? 2.0 : 8.0;
                bool ok = worst[1] <= lim && endX[0] == endX[1] && endY[0] == endY[1];
                Check("X14-4 Q held, A pressed + released at " + F(fps) + " fps: worst step " + F(worst[1]) + " px (flag on " + F(worst[0]) + "; limit " + F(lim) + "), A+Q spot identical",
                    ok, "end " + endX[0] + "," + endY[0] + " vs " + endX[1] + "," + endY[1]);
            }
            // X14-5 换手: 自动 = mirror of the 站立侧身 (Q <- E, X negated), custom rows used as typed
            {
                // Leo默认's 换手 Q / E are custom; both sides use 自动 here so the mirror rule itself is tested
                Profile off = X18Pre("x14"); off.SetHandAuto(Profile.HandQ); off.SetHandAuto(Profile.HandE); off.SetStandLeanSame(false);
                off.StandLeanQX = -40; off.StandLeanQY = -30; off.StandLeanEX = 35; off.StandLeanEY = 6;
                Profile r = X18Pre("x14r"); r.SetHandAuto(Profile.HandQ); r.SetHandAuto(Profile.HandE);
                r.LeanQX = -40; r.LeanQY = -30; r.LeanEX = 35; r.LeanEY = 6;
                InputState kq = X14Keys(true, false, false, false, false, false), ke = X14Keys(false, true, false, false, false, false);
                MotionEngine a = X8Run(off, g, true, kq, 3, null), b = X8Run(r, g, true, kq, 3, null);
                MotionEngine a2 = X8Run(off, g, true, ke, 3, null), b2 = X8Run(r, g, true, ke, 3, null);
                double x, y; off.StandLeanHandValue(Profile.StandLeanHandQ, out x, out y);
                bool okAuto = a.OffsetX == b.OffsetX && a.OffsetY == b.OffsetY && a2.OffsetX == b2.OffsetX && a2.OffsetY == b2.OffsetY && x == -35 && y == 6;
                off.SetStandLeanHandCustom(Profile.StandLeanHandQ, -12, -44);
                r.SetHandCustom(Profile.HandQ, -12, -44);
                MotionEngine c = X8Run(off, g, true, kq, 3, null), d = X8Run(r, g, true, kq, 3, null);
                bool okCustom = c.OffsetX == d.OffsetX && c.OffsetY == d.OffsetY && off.AnyHandCustom();
                off.AllHandAuto();
                bool okReset = !off.IsStandLeanHandCustom(Profile.StandLeanHandQ);
                Check("X14-5 换手 standing Q/E: 自动 = mirror (换手 Q = (" + F(x) + ", " + F(y) + ")), custom as typed, 全部恢复自动 clears it", okAuto && okCustom && okReset,
                    a.OffsetX + "," + a.OffsetY + " vs " + b.OffsetX + "," + b.OffsetY + " custom " + c.OffsetX + "," + c.OffsetY + " vs " + d.OffsetX + "," + d.OffsetY);
            }
            // X14-6 save / load round trip; X14-7 old files, the built-in and a blank profile load with the flag on
            {
                GlobalSettings s = GlobalSettings.CreateDefault();
                Profile p = s.FindProfile(s.ActiveProfile);
                p.SetStandLeanSame(false);
                p.StandLeanQX = -41.5; p.StandLeanQY = -29; p.StandLeanEX = 33; p.StandLeanEY = 7.25;
                p.SetStandLeanHandCustom(Profile.StandLeanHandE, 9, -3);
                p.SetStandLeanHandAuto(Profile.StandLeanHandQ); // (unticking copied Leo默认's custom 换手 Q)
                string js = SettingsIO.Serialize(s);
                GlobalSettings back; string err;
                bool ok = SettingsIO.TryParse(js, out back, out err);
                Profile q = ok ? back.FindProfile(s.ActiveProfile) : null;
                double x, y;
                if (q != null) q.StandLeanHandValue(Profile.StandLeanHandE, out x, out y); else { x = y = 0; }
                ok = ok && q != null && !q.StandLeanSame && q.StandLeanQX == -41.5 && q.StandLeanQY == -29 && q.StandLeanEX == 33 && q.StandLeanEY == 7.25
                    && q.IsStandLeanHandCustom(Profile.StandLeanHandE) && !q.IsStandLeanHandCustom(Profile.StandLeanHandQ) && x == 9 && y == -3
                    && SettingsIO.Serialize(back) == js;
                Check("X14-6 站立侧身 save / load round trip (flag, 4 numbers, 换手 custom row)", ok, err ?? "");

                GlobalSettings old; string err2;
                bool ok2 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"LeanQX\":-20,\"LeanQY\":-9}]}", out old, out err2);
                Profile o = ok2 ? old.FindProfile("a") : null;
                bool okOld = o != null && o.StandLeanSame && o.StandLeanHand != null && o.StandLeanHand.Count == Profile.StandLeanHandCount;
                bool okBuilt = X18Pre("z").StandLeanSame && Profile.CreateBlank("z").StandLeanSame && new Profile().StandLeanSame;
                Check("X14-7 old file / built-in Leo默认 / blank profile: 和移动时一样 on", ok2 && okOld && okBuilt, err2 ?? "");
            }
            // X14-8 unticking alone changes nothing: the 4 numbers start at the current lean (standing Q / E, both hands)
            {
                bool ok = true; string bad = "";
                Profile on = X18Pre("x14"); on.SetHandCustom(Profile.HandE, 22, -9);
                Profile off = on.Clone(); off.SetStandLeanSame(false);
                for (int h = 0; h < 2; h++)
                    foreach (InputState k in new InputState[] { X14Keys(true, false, false, false, false, false), X14Keys(false, true, false, false, false, false) })
                    {
                        MotionEngine a = X8Run(on, g, h == 1, k, 2, null), b = X8Run(off, g, h == 1, k, 2, null);
                        if (a.OffsetX != b.OffsetX || a.OffsetY != b.OffsetY) { ok = false; bad += " hand " + h + ";"; }
                    }
                Check("X14-8 untick 和移动时一样 without editing: standing Q / E unchanged (numbers copied from the lean, 换手 custom copied)", ok && !off.StandLeanSame && off.StandLeanQX == on.LeanQX, bad);
            }
            // X14-9 standing lean + fire: recoil rises straight up from the 站立侧身 point (X12 rule)
            {
                Profile p = X18Pre("x14"); p.SetStandLeanSame(false);
                p.StandLeanQX = -40; p.StandLeanQY = -30;
                MotionEngine q = X8Run(p, g, false, X14Keys(true, false, false, false, false, false), 3, null);
                MotionEngine qf = X8Run(p, g, false, X14Keys(true, false, false, false, false, true), 4, null);
                bool ok = p.RecoilDriftX == 0 && Math.Abs(qf.OffsetX - q.OffsetX) < 1e-9 && Math.Abs(qf.OffsetY - (q.OffsetY - p.RecoilMaxPx)) < 0.01;
                Check("X14-9 standing Q + fire: top = 站立侧身 point + (0, -RecoilMaxPx)", ok, "Q " + F(q.OffsetX) + "," + F(q.OffsetY) + " Q+fire " + F(qf.OffsetX) + "," + F(qf.OffsetY));
            }
            // X14-10 text: the guide (= 说明 README.txt) and the help list explain 站立不动时的侧身 / 和移动时一样
            {
                bool ok = GuideText.Text.Contains("站立不动时的侧身") && GuideText.Text.Contains("和移动时一样")
                    && HelpText.Find("standlean") != null && HelpText.Body("standlean", g).Contains("和移动时一样");
                Check("X14-10 guide + help text describe 站立不动时的侧身 (和移动时一样)", ok, "");
            }
        }
    }
}
