// v5.1 (W21): motion engine -- slow-start legs (短按, A->D 反向), 丢雷隐藏, 强化 = device-mode key (toggle / hold), 弹药提示 model,
// 站立静止偏移 in the engine, MotionCurve.ProgressInverse / ProgressFrom, ReadInput(GlobalSettings).
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W21Init() { V51W21Tests(); }

        const double W21T = 400; // curve 总时长 used by these tests

        static Sim W21Sim(double fps)
        {
            GlobalSettings g = G();
            Profile p = g.Active;
            p.CurveAD = new MoveCurve(W21T, Profile.RecommendedSpeeds);
            p.CurveWS = new MoveCurve(W21T, Profile.RecommendedSpeeds);
            return new Sim(p, g, fps);
        }

        static InputState W21Keys(bool g, bool r, bool main, bool other)
        {
            InputState i = new InputState();
            i.Grenade = g; i.Reload = r; i.WeaponMain = main; i.WeaponOther = other;
            return i;
        }

        static void V51W21Tests()
        {
            double[] sp = Profile.RecommendedSpeeds;

            // W21-1 curve helpers
            {
                bool ok = true; string bad = "";
                for (int k = 0; k <= 100; k++)
                {
                    double u = k / 100.0;
                    double pr = MotionCurve.Progress(sp, u);
                    double back = MotionCurve.ProgressInverse(sp, pr);
                    if (Math.Abs(MotionCurve.Progress(sp, back) - pr) > 1e-9) { ok = false; bad = "u=" + F(u); }
                    if (MotionCurve.ProgressFrom(sp, 0, u) != pr) { ok = false; bad = "from0 u=" + F(u); }
                }
                double u0 = 0.4, p0 = MotionCurve.Progress(sp, u0);
                double mid = MotionCurve.ProgressFrom(sp, u0, 0.5);
                double expect = (MotionCurve.Progress(sp, 0.7) - p0) / (1 - p0);
                ok &= Math.Abs(mid - expect) < 1e-12 && MotionCurve.ProgressFrom(sp, u0, 1) == 1 && MotionCurve.ProgressFrom(sp, u0, 0) == 0;
                ok &= MotionCurve.ProgressInverse(sp, 0) == 0 && MotionCurve.ProgressInverse(sp, 1) == 1 && MotionCurve.ProgressInverse(sp, double.NaN) == 0;
                Check("W21-1 ProgressInverse round trip, ProgressFrom(u0=0) == Progress, ProgressFrom(u0) = rescaled tail", ok, bad);
            }

            // W21-2 from rest: exactly the v5.0.2 travel (out) and a release from full: exactly the v5.0.2 return
            foreach (double fps in Rates)
            {
                Sim s = W21Sim(fps);
                double A = s.P.Dirs[2].OffX;
                double maxErr = 0;
                int n = s.Frames(1.0);
                for (int k = 1; k <= n; k++)
                {
                    s.Step(In("A"));
                    double t = k * 1000.0 / fps;
                    double want = t >= W21T ? A : A * MotionCurve.Progress(sp, t / W21T);
                    maxErr = Math.Max(maxErr, Math.Abs(s.E.MoveX - want));
                }
                double maxErr2 = 0;
                for (int k = 1; k <= n; k++)
                {
                    s.Step(In(""));
                    double t = k * 1000.0 / fps;
                    double want = t >= W21T ? 0 : A + (0 - A) * MotionCurve.Progress(sp, t / W21T);
                    maxErr2 = Math.Max(maxErr2, Math.Abs(s.E.MoveX - want));
                }
                Check("W21-2 @" + fps + " from rest = v5.0.2 curve; release from full = v5.0.2 return", maxErr < 1e-9 && maxErr2 < 1e-9 && !s.E.LastTap,
                    "err out " + maxErr + " back " + maxErr2);
            }

            // W21-3 短按: an 80 ms tap moves a little and comes back in about the same time; counted as a tap
            foreach (double fps in Rates)
            {
                Sim s = W21Sim(fps);
                double A = s.P.Dirs[2].OffX;
                s.Run(0.08, In("A"));
                double peak = Math.Abs(s.E.MoveX);
                bool tap = s.E.LastTap; // not yet released
                s.Step(In(""));
                bool tapAfter = s.E.LastTap && s.E.TapCount == 1;
                double backMs = 1000.0 / fps;
                while (s.E.Traveling(0) && backMs < 2000) { s.Step(In("")); backMs += 1000.0 / fps; }
                double heldMs = s.Frames(0.08) * 1000.0 / fps;
                bool ok = peak > 0 && peak < 0.06 * Math.Abs(A) && !tap && tapAfter && s.E.MoveX == 0 && backMs <= heldMs + 2 * 1000.0 / fps;
                Check("W21-3 @" + fps + " tap 80 ms: small peak, back to 0 in ~held time, LastTap", ok,
                    "peak " + F(peak) + " of " + F(Math.Abs(A)) + ", back " + F(backMs) + " ms vs held " + F(heldMs));
            }

            // W21-4 long hold is not a tap; tap threshold from the settings (60 ms: an 80 ms press is not a tap)
            {
                Sim s = W21Sim(144);
                s.Run(0.5, In("D")); s.Step(In(""));
                bool longNo = !s.E.LastTap && s.E.TapCount == 0;
                Sim t = W21Sim(144); t.G.TapThresholdMs = 60;
                t.Run(0.08, In("D")); t.Step(In(""));
                Check("W21-4 long hold not 短按; TapThresholdMs 60 -> 80 ms press not 短按", longNo && !t.E.LastTap, "");
            }

            // W21-5 A full -> D: decelerate to 0 (no jump), then accelerate right from rest with the full curve
            foreach (double fps in Rates)
            {
                Sim s = W21Sim(fps);
                double A = s.P.Dirs[2].OffX, D = s.P.Dirs[3].OffX;
                s.Run(1.0, In("A"));
                double last = s.E.MoveX;
                double maxStep = 0; bool monotone = true, reversing = false; double tZero = -1, tFull = -1; double t = 0;
                bool beyond = false;
                for (int k = 0; k < s.Frames(1.5); k++)
                {
                    s.Step(In("D"));
                    t += 1000.0 / fps;
                    double x = s.E.MoveX;
                    if (k == 0) reversing = s.E.Reversing(0);
                    maxStep = Math.Max(maxStep, Math.Abs(x - last));
                    if (x < last - 1e-12) monotone = false;
                    if (x > D + 1e-9) beyond = true;
                    if (tZero < 0 && x >= 0) tZero = t;
                    if (tFull < 0 && x == D) tFull = t;
                    last = x;
                }
                double firstLimit = Math.Abs(A) * MotionCurve.Progress(sp, 1000.0 / fps / W21T) + 1e-9;
                Sim s2 = W21Sim(fps); s2.Run(1.0, In("A")); s2.Step(In("D"));
                double first = Math.Abs(s2.E.MoveX - A);
                bool ok = monotone && !beyond && reversing && first <= firstLimit && tZero >= W21T - 1000.0 / fps && tFull >= 2 * W21T - 2000.0 / fps
                    && tFull <= 2 * W21T + 2000.0 / fps;
                Check("W21-5 @" + fps + " A full -> D: slow start off A, 0 after ~T, D after ~2T, monotone, no overshoot", ok,
                    "first " + F(first) + " (<= " + F(firstLimit) + "), 0 @" + F(tZero) + " ms, D @" + F(tFull) + " ms");
            }

            // W21-6 A held 150 ms then D: back to 0 in ~150 ms (the time it took to get there), then out to D over T
            {
                Sim s = W21Sim(240);
                s.G.ReverseMinMs = 150; s.G.TapThresholdMs = 120; // Leo 21 Nov 2026 new defaults (250 / 200): pin the old tunables so this still tests the same behaviour
                s.Run(0.15, In("A"));
                double t = 0, tZero = -1, tFull = -1;
                for (int k = 0; k < s.Frames(1.0); k++)
                {
                    s.Step(In("D")); t += 1000.0 / 240;
                    if (tZero < 0 && s.E.MoveX >= 0) tZero = t;
                    if (tFull < 0 && s.E.MoveX == s.P.Dirs[3].OffX) tFull = t;
                }
                bool ok = Math.Abs(tZero - 150) <= 9 && Math.Abs(tFull - (150 + W21T)) <= 9 && s.E.LastTap == false;
                Check("W21-6 A 150 ms -> D: 0 after ~150 ms, D after ~150+T", ok, "0 @" + F(tZero) + ", D @" + F(tFull));
            }

            // W21-7 press A again while coming back: continues outward from where it is (no restart from 0 speed, no jump)
            {
                Sim s = W21Sim(144);
                double A = s.P.Dirs[2].OffX;
                s.Run(1.0, In("A")); s.Run(0.2, In(""));
                double x0 = s.E.MoveX;
                double u0 = MotionCurve.ProgressInverse(sp, Math.Abs(x0) / Math.Abs(A));
                s.Step(In("A"));
                double x1 = s.E.MoveX;
                double want1 = A * MotionCurve.Progress(sp, u0 + (1000.0 / 144) / W21T);
                double t = 1000.0 / 144, tFull = -1;
                for (int k = 0; k < 200 && tFull < 0; k++) { s.Step(In("A")); t += 1000.0 / 144; if (s.E.MoveX == A) tFull = t; }
                bool ok = x1 < x0 && Math.Abs(x1 - want1) < 1e-6 && Math.Abs(tFull - W21T * (1 - u0)) <= 1000.0 / 144 + 1e-6;
                Check("W21-7 re-press while returning: continues up the ramp from u0, full after T(1-u0)", ok,
                    "x0 " + F(x0) + " x1 " + F(x1) + " want " + F(want1) + ", full @" + F(tFull) + " vs " + F(W21T * (1 - u0)));
            }

            // W21-8 reversal never shorter than MinReverseMs (short curve)
            {
                Sim s = W21Sim(240);
                s.P.CurveAD = new MoveCurve(40, sp);
                s.G.ReverseMinMs = 150; s.G.TapThresholdMs = 120; // Leo 21 Nov 2026 new defaults (250 / 200): pin the old tunables (= MinReverseMs) so this still tests the same behaviour
                s.Run(0.5, In("A"));
                double t = 0, tFull = -1;
                for (int k = 0; k < 240 && tFull < 0; k++) { s.Step(In("D")); t += 1000.0 / 240; if (s.E.MoveX == s.P.Dirs[3].OffX) tFull = t; }
                Check("W21-8 A->D with a 40 ms curve still takes >= " + MotionEngine.MinReverseMs + " ms", tFull >= MotionEngine.MinReverseMs - 1e-6 && tFull <= MotionEngine.MinReverseMs + 9,
                    "D @" + F(tFull));
            }

            // W21-9 MoveLevel: 0 at rest, 1 at full, between while ramping
            {
                Sim s = W21Sim(144);
                s.Run(0.3, In(""));
                double l0 = s.E.MoveLevel;
                s.Run(0.2, In("A"));
                double lm = s.E.MoveLevel;
                s.Run(1.0, In("A"));
                double l1 = s.E.MoveLevel;
                Check("W21-9 MoveLevel 0 / between / 1", l0 == 0 && lm > 0 && lm < 1 && Math.Abs(l1 - 1) < 1e-12, F(l0) + " " + F(lm) + " " + F(l1));
            }

            // W21-10 丢雷隐藏: G toggles, Alpha 0 the same frame, stays hidden, G again shows, F12 reset shows, off = never
            {
                Sim s = W21Sim(144);
                s.Run(0.5, In(""));
                bool shown0 = s.E.Alpha > 0.99 && !s.E.GrenadeHidden;
                s.Step(W21Keys(true, false, false, false));
                bool hidNow = s.E.GrenadeHidden && s.E.Alpha == 0;
                s.Run(5.0, In(""));
                bool stays = s.E.GrenadeHidden && s.E.Alpha == 0;
                s.Step(W21Keys(true, false, false, false)); s.Run(1.0, In(""));
                bool back = !s.E.GrenadeHidden && s.E.Alpha > 0.99;
                s.Step(W21Keys(true, false, false, false));
                s.G.Enabled = false; s.Step(In("")); s.G.Enabled = true; s.Step(In(""));
                bool f12 = !s.E.GrenadeHidden;
                Sim o = W21Sim(144); o.G.GrenadeHideOn = false;
                o.Run(0.5, In("")); o.Step(W21Keys(true, false, false, false));
                bool off = !o.E.GrenadeHidden && o.E.Alpha > 0.99;
                Check("W21-10 丢雷: hide same frame, stays, G again shows, F12 shows, switched off never hides", shown0 && hidNow && stays && back && f12 && off, "");
            }

            // W21-11 强化 = 切换战术设备模式 key (default Ctrl+T): T alone does nothing; toggle / hold; weapon key and F12 reset
            {
                GlobalSettings g0 = G();
                // v5.1.1 (X5, Leo 21 Nov 2026): default = 鼠标侧键2; the exact-combo rules are checked on a Ctrl+T binding
                bool def = g0.Keys.Boost == KeyBindings.VkXButton2 && g0.Keys.BoostMods == 0 && g0.BoostToggle;
                g0.Keys.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkT, KeyBindings.ModCtrl);
                HashSet<int> down = new HashSet<int>();
                Func<int, bool> isDown = delegate(int vk) { return down.Contains(vk); };
                down.Add(KeyBindings.VkT);
                bool tAlone = !MotionEngine.ReadInput(g0, isDown).Boost;
                down.Add(KeyBindings.VkControl);
                bool ctrlT = MotionEngine.ReadInput(g0, isDown).Boost;

                Sim s = W21Sim(144);
                InputState b = new InputState(); b.Boost = true;
                s.Step(b); s.Step(In(""));
                bool on = s.E.LiveBoost;
                s.Step(b); s.Step(In(""));
                bool offAgain = !s.E.LiveBoost;
                s.Step(b); s.Step(In(""));
                s.Step(W21Keys(false, false, true, true)); s.Step(In(""));
                bool weaponReset = s.E.LiveBoost; // R3: weapon keys do nothing to 强化
                s.G.Enabled = false; s.Step(In("")); s.G.Enabled = true; s.Step(In(""));
                bool f12 = !s.E.LiveBoost;
                Sim h = W21Sim(144); h.G.BoostToggle = false;
                h.Step(b); bool held = h.E.LiveBoost; h.Step(In("")); bool released = !h.E.LiveBoost;
                Check("W21-11 强化: default Ctrl+T 切换, T alone no, Ctrl+T yes, toggle, weapon key no effect (R3), F12 reset, 按住 mode",
                    def && tAlone && ctrlT && on && offAgain && weaponReset && f12 && held && released,
                    "def " + def + " tAlone " + tAlone + " ctrlT " + ctrlT);
            }

            // W21-12 ReadInput(GlobalSettings): 丢雷 / 换弹 from the settings; R3: weapon keys are never read
            {
                GlobalSettings g = G();
                HashSet<int> down = new HashSet<int>();
                Func<int, bool> isDown = delegate(int vk) { return down.Contains(vk); };
                InputState none = MotionEngine.ReadInput(g, isDown);
                down.Add(g.GrenadeKey); down.Add(g.AmmoReloadKey); down.Add(0x32); down.Add(0x31); down.Add(0x56);
                InputState all = MotionEngine.ReadInput(g, isDown);
                bool ok = !none.Grenade && !none.Reload && all.Grenade && all.Reload && !all.WeaponMain && !all.WeaponOther
                    && g.GrenadeKey == 0x47 && g.AmmoReloadKey == 0x52;
                Check("W21-12 ReadInput(g): G / R read from GlobalSettings; 1 / 2 / V not read (R3)", ok, "");
            }

            // W21-13 弹药 model: auto RPM count, semi per click, bands, R2 reloads = new full mag after the set time
            {
                AmmoModel m = new AmmoModel();
                m.Configure(30, 600, false, 300, 2400, 900);
                double dt = 1000.0 / 144;
                int frames = (int)Math.Round(1.0 * 144);
                for (int k = 0; k < frames; k++) m.Update(dt, true, false);
                m.Update(dt, false, false);
                int autoShots = 30 - m.Rounds;
                bool autoOk = autoShots == 10 || autoShots == 11;
                int bandAfter = m.Band;
                m.Update(dt, false, true);
                bool reloading = m.Reloading && m.ReloadKind == 1 && m.ReloadSeq == 1 && m.ReloadMs == 2400;
                for (int k = 0; k < 100; k++) m.Update(dt, true, false); // fire during reload: not counted
                bool oldMagIn = m.Rounds == 30 - autoShots; // normal reload: the old mag stays in until the swap (no top-up)
                double t = 100 * dt;
                while (m.Reloading && t < 5000) { m.Update(dt, false, false); t += dt; }
                bool full = m.Rounds == 30 && m.Band == AmmoModel.BandFull && Math.Abs(t - 2400) <= dt + 1e-9;

                // quick reload: R, R within 300 ms -> old mag dropped at once, new full mag 900 ms after the 2nd press
                m.Update(dt, false, true); m.Update(dt, false, false);
                for (int k = 0; k < 20; k++) m.Update(dt, false, false);
                m.Update(dt, false, true);
                bool quick = m.Reloading && m.ReloadKind == 2 && m.Rounds == 0 && m.ReloadSeq == 3 && m.Band == AmmoModel.BandNearEmpty && m.ReloadMs == 900;
                double tq = 0;
                while (m.Reloading && tq < 5000) { m.Update(dt, false, false); tq += dt; }
                bool quickDone = m.Rounds == 30 && Math.Abs(tq - 900) <= dt + 1e-9;

                // R2 settable times: 1000 / 400 ms
                AmmoModel c = new AmmoModel(); c.Configure(30, 600, false, 300, 1000, 400);
                c.Update(dt, true, false); c.Update(dt, false, false); c.Update(dt, false, true);
                double tc = 0; while (c.Reloading && tc < 5000) { c.Update(dt, false, false); tc += dt; }
                c.Update(dt, false, true); c.Update(dt, false, false); c.Update(dt, false, true);
                double tc2 = 0; while (c.Reloading && tc2 < 5000) { c.Update(dt, false, false); tc2 += dt; }
                AmmoModel bad = new AmmoModel(); bad.Configure(30, 600, false, 300, double.NaN, -5);
                bool times = Math.Abs(tc - 1000) <= dt + 1e-9 && Math.Abs(tc2 - 400) <= dt + 1e-9 && c.Rounds == 30
                    && bad.NormalReloadMs == AmmoModel.DefaultNormalReloadMs && bad.QuickReloadMs == AmmoModel.DefaultQuickReloadMs;

                // semi: 5 clicks = 5 rounds; empty never below 0
                AmmoModel s = new AmmoModel(); s.Configure(10, 600, true, 300);
                for (int k2 = 0; k2 < 5; k2++) { for (int k = 0; k < 10; k++) s.Update(dt, true, false); s.Update(dt, false, false); }
                bool semiOk = s.Rounds == 5 && s.Band == AmmoModel.BandHalf;
                for (int k2 = 0; k2 < 20; k2++) { s.Update(dt, true, false); s.Update(dt, false, false); }
                bool emptyOk = s.Rounds == 0 && s.Empty && s.ShotSeq == 10;

                bool bands = AmmoModel.BandOf(30, 30) == 4 && AmmoModel.BandOf(27, 30) == 4 && AmmoModel.BandOf(26, 30) == 3
                    && AmmoModel.BandOf(18, 30) == 3 && AmmoModel.BandOf(17, 30) == 2 && AmmoModel.BandOf(11, 30) == 2
                    && AmmoModel.BandOf(10, 30) == 1 && AmmoModel.BandOf(5, 30) == 1 && AmmoModel.BandOf(4, 30) == 0 && AmmoModel.BandOf(0, 30) == 0;
                Check("W21-13 弹药 model: auto x RPM, bands, R2 normal = old mag until a new full mag at 2400 ms, quick = dropped then full at 900 ms, settable times, semi, empty",
                    autoOk && bandAfter == AmmoModel.BandMost && reloading && oldMagIn && full && quick && quickDone && times && semiOk && emptyOk && bands,
                    "auto " + autoShots + " band " + bandAfter + " r " + reloading + oldMagIn + full + " (" + F(t) + " ms) q " + quick + quickDone
                    + " (" + F(tq) + " ms) times " + times + " semi " + semiOk + emptyOk + " bands " + bands);
            }

            // W21-14 engine ammo wiring: off by default, on = full + count while firing on main, 丢雷 blocks, F12 refills
            {
                Sim s = W21Sim(144);
                s.Run(0.5, In("L"));
                bool offDefault = !s.E.AmmoActive && !s.G.AmmoOn;
                s.G.AmmoOn = true; s.Run(0.1, In(""));
                bool fullOn = s.E.AmmoActive && s.E.Ammo.Rounds == s.G.AmmoMagSize;
                s.Run(0.5, In("L")); s.Step(In(""));
                int afterFire = s.E.Ammo.Rounds;
                bool firingCounted = afterFire < s.G.AmmoMagSize && s.E.AmmoShotSeq > 0;
                s.Step(W21Keys(true, false, false, false)); // 丢雷
                s.Run(0.5, In("L"));
                bool grenadeBlocks = s.E.Ammo.Rounds == afterFire && !s.E.Firing;
                s.G.Enabled = false; s.Step(In("")); s.G.Enabled = true; s.Step(In(""));
                bool f12 = s.E.Ammo.Rounds == s.G.AmmoMagSize && !s.E.GrenadeHidden;
                InputState other = W21Keys(false, false, true, true); other.Fire = true; // R3: weapon flags are ignored
                s.Run(0.3, other);
                bool noPause = s.E.AmmoActive && s.E.Ammo.Rounds < s.G.AmmoMagSize;
                s.G.AmmoReloadMs = 1000; s.G.AmmoQuickReloadMs = 400;
                s.Step(W21Keys(false, true, false, false)); s.Step(In(""));
                bool fromSettings = s.E.AmmoReloadMs == 1000 && s.E.Ammo.QuickReloadMs == 400;
                Check("W21-14 engine: ammo off by default, on = full, fire counts, 丢雷 blocks, F12 refills, weapon keys never pause (R3), reload times from settings (R2)",
                    offDefault && fullOn && firingCounted && grenadeBlocks && f12 && noPause && fromSettings, "noPause " + noPause + " settings " + fromSettings);
            }

            // W21-15 站立静止偏移: the standing point is the rest position (also right after an F12 reset); directions add to it
            {
                Sim s = W21Sim(144);
                s.P.StandOffsetX = 10; s.P.StandOffsetY = -4; // as SetStandPoint leaves it (Validate keeps it with StandAsZero)
                s.Run(0.5, In(""));
                double rx = s.G.ResolutionScaleX, ry = s.G.ResolutionScale;
                bool rest = Math.Abs(s.E.OffsetX - rx * 10) < 1e-9 && Math.Abs(s.E.OffsetY - ry * 4) < 1e-9;
                s.G.Enabled = false; s.Step(In("")); s.G.Enabled = true; s.Run(0.5, In(""));
                bool home = Math.Abs(s.E.OffsetX - rx * 10) < 1e-9;
                s.Run(1.0, In("D"));
                bool dir = Math.Abs(s.E.OffsetX - rx * (10 + s.P.Dirs[3].OffX)) < 1e-9;
                Check("W21-15 站立静止偏移: rest = stand point, F12 home = stand point, D = stand + D", rest && home && dir,
                    F(s.E.OffsetX) + " " + F(s.E.OffsetY));
            }

            // W21-16 random key mash: finite, bounded by the largest offsets, no NaN, every axis settles
            {
                Random rnd = new Random(5121);
                bool ok = true; string bad = "";
                for (int run = 0; run < 40 && ok; run++)
                {
                    Sim s = W21Sim(new double[] { 60, 144, 240, 31 }[run % 4]);
                    if (run % 3 == 1) s.P.CurveAD = new MoveCurve(rnd.Next(0, 900), sp);
                    double lim = 0;
                    for (int d = 0; d < 8; d++) lim = Math.Max(lim, Math.Abs(s.P.Dirs[d].OffX));
                    string keys = "WASD";
                    for (int k = 0; k < 600; k++)
                    {
                        string cur = "";
                        foreach (char c in keys) if (rnd.Next(3) == 0) cur += c;
                        int hold = rnd.Next(1, 30);
                        for (int f = 0; f < hold; f++)
                        {
                            s.Step(In(cur));
                            double x = s.E.MoveX;
                            if (double.IsNaN(x) || Math.Abs(x) > lim + 1e-9) { ok = false; bad = "run " + run + " x " + x; break; }
                        }
                        if (!ok) break;
                    }
                    s.Run(3.0, In(""));
                    if (ok && (s.E.MoveX != 0 || s.E.MoveY != 0 || s.E.Traveling(0) || s.E.Traveling(1))) { ok = false; bad = "run " + run + " did not settle"; }
                }
                Check("W21-16 key mash (40 runs): finite, within the offsets, settles at 0", ok, bad);
            }

            // W21-17 R4 ReverseMinMs from the settings: 40 ms curve, A->D takes max(ReverseMinMs, 2 x 40 ms)
            foreach (double rmin in new double[] { 50, 150, 300, 500 })
            {
                Sim s = W21Sim(240);
                s.P.CurveAD = new MoveCurve(40, sp); s.G.ReverseMinMs = rmin;
                s.Run(0.5, In("A"));
                double t = 0, tFull = -1;
                for (int k = 0; k < 480 && tFull < 0; k++) { s.Step(In("D")); t += 1000.0 / 240; if (s.E.MoveX == s.P.Dirs[3].OffX) tFull = t; }
                double want = Math.Max(rmin, 80);
                Check("W21-17 ReverseMinMs " + rmin + ": A->D (40 ms curve) on D after " + want + " ms", Math.Abs(tFull - want) <= 1000.0 / 240 + 1e-6, "D @" + F(tFull));
            }

            // W21-18 R4 ReturnTimePct: a 150 ms press comes back in 150 ms x pct; A->D first leg scales the same way
            foreach (double pct in new double[] { 50, 100, 200 })
            {
                Sim s = W21Sim(240); s.G.ReturnTimePct = pct;
                s.Run(0.15, In("A"));
                double held = s.Frames(0.15) * 1000.0 / 240, back = 0;
                do { s.Step(In("")); back += 1000.0 / 240; } while (s.E.Traveling(0) && back < 3000);
                Sim r = W21Sim(240); r.G.ReturnTimePct = pct;
                r.Run(0.15, In("A"));
                double t = 0, tZero = -1;
                for (int k = 0; k < 480 && tZero < 0; k++) { r.Step(In("D")); t += 1000.0 / 240; if (r.E.MoveX >= 0) tZero = t; }
                bool ok = Math.Abs(back - held * pct / 100) <= 1000.0 / 240 + 1e-6 && Math.Abs(tZero - held * pct / 100) <= 1000.0 / 240 + 1e-6;
                Check("W21-18 ReturnTimePct " + pct + ": 150 ms press back in " + F(held * pct / 100) + " ms, A->D at 0 after the same", ok,
                    "release " + F(back) + " ms, A->D zero @" + F(tZero));
            }

            // W21-19 R4 ReversePauseMs: rests exactly at 0 for the pause, then out over 总时长; 0 = no rest
            foreach (double pause in new double[] { 0, 80, 200 })
            {
                Sim s = W21Sim(240); s.G.ReversePauseMs = pause;
                s.Run(1.0, In("A"));
                double t = 0, firstZero = -1, lastZero = -1, tFull = -1; bool exactZero = true;
                for (int k = 0; k < 600 && tFull < 0; k++)
                {
                    s.Step(In("D")); t += 1000.0 / 240;
                    double x = s.E.MoveX;
                    if (x >= 0 && firstZero < 0) firstZero = t;
                    if (x == 0) lastZero = t;
                    if (firstZero >= 0 && t < firstZero + pause - 1e-6 && x != 0) exactZero = false;
                    if (x == s.P.Dirs[3].OffX) tFull = t;
                }
                bool ok = exactZero && Math.Abs(tFull - (W21T + pause + W21T)) <= 1000.0 / 240 + 1e-6;
                Check("W21-19 ReversePauseMs " + pause + ": rests at 0, D after T + pause + T", ok, "0 @" + F(firstZero) + "..@" + F(lastZero) + ", D @" + F(tFull));
            }

            // W21-20 R4 TapThresholdMs from the settings labels 短按; out-of-range / NaN tunables are clamped (finite path)
            {
                Sim s = W21Sim(144); s.G.TapThresholdMs = 250;
                s.Run(0.2, In("D")); s.Step(In(""));
                bool tap250 = s.E.LastTap;
                Sim t = W21Sim(144); t.G.TapThresholdMs = 120;
                t.Run(0.2, In("D")); t.Step(In(""));
                bool notTap120 = !t.E.LastTap;
                Sim n = W21Sim(144); n.G.ReverseMinMs = double.NaN; n.G.ReturnTimePct = 1e9; n.G.ReversePauseMs = -5; n.G.TapThresholdMs = double.NaN;
                bool fin = true;
                for (int k = 0; k < 600; k++) { n.Step(In(k % 50 < 25 ? "A" : "D")); if (double.IsNaN(n.E.MoveX)) fin = false; }
                n.Run(5.0, In(""));
                bool settled = n.E.MoveX == 0 && !n.E.Traveling(0);
                bool defs = GlobalSettings.CreateDefault().TapThresholdMs == 200 && GlobalSettings.CreateDefault().ReverseMinMs == 250 /* Leo 21 Nov 2026 */
                    && double.IsNaN(new MotionEngine().ReturnTimePct) && double.IsNaN(new MotionEngine().ReversePauseMs)
                    && GlobalSettings.CreateDefault().ReturnTimePct == 100 && GlobalSettings.CreateDefault().ReversePauseMs == 0
                    && GlobalSettings.CreateDefault().AmmoReloadMs == 3000 && GlobalSettings.CreateDefault().AmmoQuickReloadMs == 2200; // Leo 21 Nov 2026
                Check("W21-20 TapThresholdMs 250: 200 ms press = 短按, 120: not; bad tunables clamped, path finite and settles; defaults 200/250/100/0, reload 3000/2200",
                    tap250 && notTap120 && fin && settled && defs, "tap250 " + tap250 + " notTap120 " + notTap120 + " finite " + fin + " settled " + settled + " defs " + defs);
            }
        }
    }
}
