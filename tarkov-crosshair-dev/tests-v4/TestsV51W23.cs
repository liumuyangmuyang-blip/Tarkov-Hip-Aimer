// v5.1 (W23): shared settings fields (import / clamp / merge), 强化 key default (decision 3, corrected), any W:H 画面比例 and
// 1:1 (decision 12), 站立静止偏移 as the new zero with conversion (decision 5), the FOV / 拉伸 chain audit for asymmetric offsets,
// mirrored hand, lean and diagonals + the 不拉伸 model (decision 11), unreadable values -> Leo's v5 default (decision 17).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W23Init() { V51W23Tests(); }

        static string W23F(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        static GlobalSettings W23G(double w, double h, string aspect, double fov, string stretch)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.ShowFadeMs = 0; g.HideFadeMs = 0; g.OnlyWhenGameFocused = false; g.Enabled = true;
            g.HandEnabled = true; g.BoostEnabled = false; g.HandMode = "toggle";
            g.ResGameW = w; g.ResGameH = h; g.ResAspect = aspect; g.ResFov = fov; g.ResStretched = stretch == "fit" ? (bool?)false : stretch == "stretch" ? (bool?)true : null;
            g.Validate();
            return g;
        }

        /// <summary>Steady state of the engine with these keys held (hand switched first when hand1), 120 fps.</summary>
        static MotionEngine W23Hold(Profile p, GlobalSettings g, bool hand1, InputState keys)
        {
            MotionEngine e = new MotionEngine();
            e.GameFocused = true; e.OwnForeground = true;
            InputState none = new InputState();
            for (int k = 0; k < 30; k++) e.Update(1.0 / 120, none, p, g, PreviewMode.Live, 1.0);
            if (hand1)
            {
                InputState hs = new InputState(); hs.HandSwitch = true;
                e.Update(1.0 / 120, hs, p, g, PreviewMode.Live, 1.0);
                for (int k = 0; k < 600; k++) e.Update(1.0 / 120, none, p, g, PreviewMode.Live, 1.0);
            }
            for (int k = 0; k < 600; k++) e.Update(1.0 / 120, keys, p, g, PreviewMode.Live, 1.0);
            return e;
        }

        static InputState W23Keys(int dir, int lean)
        {
            InputState i = new InputState();
            // W, S, A, D, WA, WD, SA, SD (Profile.DirNames order), -1 = none
            if (dir == 0 || dir == 4 || dir == 5) i.Fwd = true;
            if (dir == 1 || dir == 6 || dir == 7) i.Back = true;
            if (dir == 2 || dir == 4 || dir == 6) i.Left = true;
            if (dir == 3 || dir == 5 || dir == 7) i.Right = true;
            if (lean < 0) i.LeanL = true;
            if (lean > 0) i.LeanR = true;
            return i;
        }

        static void V51W23Tests()
        {
            // W23-1 an older file (Leo's v5 settings) imports: every v5.1 field gets its default, nothing else changes, profiles kept
            {
                GlobalSettings g; string err;
                bool ok = SettingsIO.TryParse(LeoDefaultJson.Text, out g, out err);
                string d = "";
                if (!ok) d = "parse " + err;
                else
                {
                    if (g.AmmoOn || g.AmmoMagSize != 30 || g.AmmoRpm != 850 || g.AmmoSemi || g.AmmoStyle != "brackets,colour") d += "ammo; ";
                    if (g.AmmoReloadKey != 0x52 || g.AmmoQuickTapMs != 300) d += "reload; ";
                    if (string.Join(",", g.AmmoWeaponKeysMain) != "50" || string.Join(",", g.AmmoWeaponKeysOther) != "49,51,86,52,53,54,55,56,57,48") d += "weapon keys; ";
                    if (!g.GrenadeHideOn || g.GrenadeKey != 0x47 || !g.BoostToggle) d += "grenade/boost mode; ";
                    if (g.AnimPreset != "static" || g.AnimDynGap || g.AnimFirePulse || g.AnimLeanRotate || g.AnimStateColour || g.AnimShake) d += "anim; ";
                    if (g.PerfMode || g.ResAspectCustom != "16:9" || g.ResStretched != null || g.WizardDone || g.MustReadAck) d += "misc; ";
                    if (!g.NetUpdateCheck || g.NetStatsConsent != null || !g.NetCrashAsk) d += "net; ";
                    if (g.TapThresholdMs != 200 || g.ReverseMode != "slowstart") d += "tap; "; // Leo 21 Nov 2026
                    if (g.Profiles.Count != 2 || g.Keys.Boost != 6 || g.Keys.BoostMods != 0) d += "kept: profiles=" + g.Profiles.Count + " boost=" + g.Keys.Boost + "; ";
                    // v5.1.1 (X5, Leo 21 Nov 2026): the built-in 「Leo默认 (旧)」 has Leo's 站立 point 24.48 / -25.2; the other stays 0
                    foreach (Profile p in g.Profiles)
                        if (p.Name == LeoDefaultJson.ProfileName ? !(p.StandAsZero && p.StandOffsetX == 24.48 && p.StandOffsetY == -25.2) : (p.StandAsZero || p.StandOffsetX != 0 || p.StandOffsetY != 0)) d += "stand " + p.Name + "; ";
                }
                Check("W23-1 v5.0.2 file imports: v5.1 fields default (ammo off 30/850 R 300 ms, main 2 / other 1 3 V 4-0, grenade G on, boost 切换, anim static, net ask once, tap 200, slowstart), profiles + Boost key kept", d.Length == 0, d);
            }
            // W23-2 round trip + Clone deep-copies the key lists + CopyFrom (导入旧版设置 merge) copies them
            {
                GlobalSettings g = GlobalSettings.CreateDefault();
                g.AmmoOn = true; g.AmmoMagSize = 45; g.AmmoRpm = 900; g.AmmoSemi = true; g.AmmoStyle = "flash,brackets,fade";
                g.AmmoReloadKey = 0x51; g.AmmoQuickTapMs = 250; g.AmmoWeaponKeysMain = new int[] { 0x31 }; g.AmmoWeaponKeysOther = new int[0];
                g.GrenadeHideOn = false; g.GrenadeKey = 0x48; g.BoostToggle = false; g.AnimPreset = "cs"; g.AnimDynGap = true; g.AnimShake = true;
                g.PerfMode = true; g.ResAspect = "123:111"; g.ResAspectCustom = "123:111"; g.ResStretched = false; g.WizardDone = true; g.MustReadAck = true;
                g.NetUpdateCheck = false; g.NetStatsConsent = false; g.NetCrashAsk = false; g.TapThresholdMs = 90;
                g.Validate();
                GlobalSettings b; string err;
                bool rt = SettingsIO.TryParse(SettingsIO.Serialize(g), out b, out err) && b.AmmoOn && b.AmmoMagSize == 45 && b.AmmoRpm == 900 && b.AmmoSemi
                    && b.AmmoStyle == "brackets,fade,flash" && b.AmmoReloadKey == 0x51 && b.AmmoQuickTapMs == 250 && b.AmmoWeaponKeysMain.Length == 1
                    && b.AmmoWeaponKeysOther.Length == 0 && !b.GrenadeHideOn && b.GrenadeKey == 0x48 && !b.BoostToggle && b.AnimPreset == "cs" && b.AnimDynGap && b.AnimShake
                    && b.PerfMode && b.ResAspect == "123:111" && b.ResStretched == false && b.WizardDone && b.MustReadAck && !b.NetUpdateCheck
                    && b.NetStatsConsent == false && !b.NetCrashAsk && b.TapThresholdMs == 90;
                GlobalSettings c = g.Clone(true);
                c.AmmoWeaponKeysMain[0] = 0x39;
                bool deep = g.AmmoWeaponKeysMain[0] == 0x31;
                GlobalSettings m = GlobalSettings.CreateDefault();
                m.CopyFrom(b);
                bool merged = m.AmmoMagSize == 45 && m.ResStretched == false && m.NetStatsConsent == false && m.Profiles.Count == b.Profiles.Count;
                Check("W23-2 v5.1 fields save + reload exactly, Clone deep-copies the weapon key lists, CopyFrom (导入旧版设置) takes them", rt && deep && merged,
                    "rt=" + rt + " " + err + " deep=" + deep + " merged=" + merged);
            }
            // W23-3 bad values -> defaults
            {
                GlobalSettings g = GlobalSettings.CreateDefault();
                g.AmmoMagSize = 0; g.AmmoRpm = 99999; g.AmmoStyle = "nope, ,"; g.AmmoReloadKey = 300; g.AmmoQuickTapMs = 5;
                g.AmmoWeaponKeysMain = null; g.AmmoWeaponKeysOther = new int[] { 0, 0x31, 0x31, 999, 0x32 }; g.GrenadeKey = -1;
                g.AnimPreset = "wobble"; g.ResAspectCustom = "0:5"; g.TapThresholdMs = double.NaN; g.ReverseMode = "jump"; 
                g.Validate();
                string d = "";
                if (g.AmmoMagSize != 30 || g.AmmoRpm != 850 || g.AmmoStyle != "brackets" || g.AmmoReloadKey != 0x52 || g.AmmoQuickTapMs != 300) d += "ammo " + g.AmmoStyle + "; ";
                if (string.Join(",", g.AmmoWeaponKeysMain) != "50" || string.Join(",", g.AmmoWeaponKeysOther) != "49,50" || g.GrenadeKey != 0x47) d += "keys; ";
                if (g.AnimPreset != "static" || g.ResAspectCustom != "16:9" || g.TapThresholdMs != 200 || g.ReverseMode != "slowstart" || g.ResStretched != null) d += "misc; ";
                GlobalSettings h = GlobalSettings.CreateDefault(); h.TapThresholdMs = 10; h.Validate();
                GlobalSettings i = GlobalSettings.CreateDefault(); i.TapThresholdMs = 900; i.AmmoStyle = "COLOR"; i.Validate();
                if (h.TapThresholdMs != 60 || i.TapThresholdMs != 250 || i.AmmoStyle != "colour") d += "clamp " + h.TapThresholdMs + "/" + i.TapThresholdMs + "/" + i.AmmoStyle;
                Check("W23-3 bad v5.1 values -> defaults / clamped (mag 1..200, RPM 60..2000, style tokens, keys 1..254 distinct, tap 60..250)", d.Length == 0, d);
            }
            // W23-4 强化 key (decision 3 corrected): default Ctrl+T (切换战术设备模式); a file's own key is kept; pre-v4.0 = plain key
            {
                KeyBindings k = new KeyBindings();
                GlobalSettings n = GlobalSettings.CreateDefault();
                // v5.1.1 (X5, Leo 21 Nov 2026): default = 鼠标侧键2 (no modifier); the game's 切换战术设备模式 is bound to it too
                bool def = k.Boost == 6 && k.BoostMods == 0 && n.BoostKey == 6 && n.BoostKeyMods == 0
                    && n.Keys.ComboOf(KeyBindings.ActionBoost) == "鼠标侧键2（XButton2）";
                // T alone (战术设备 on/off) is not the boost combo
                bool tAlone = n.Keys.FindOtherCombo(0x54, 0, -1) < 0;
                GlobalSettings g; string err;
                bool keep = SettingsIO.TryParse(LeoDefaultJson.Text, out g, out err) && g.Keys.Boost == 6 && g.Keys.BoostMods == 0;
                string old = "{\"Version\":1,\"BoostEnabled\":true,\"Keys\":{\"Boost\":84,\"HandSwitch\":5},\"Profiles\":[]}";
                GlobalSettings o;
                bool plain = SettingsIO.TryParse(old, out o, out err) && o.Keys.Boost == 0x54 && o.Keys.BoostMods == 0;
                string pre30 = "{\"Version\":1,\"Keys\":{\"HandSwitch\":5},\"Profiles\":[]}";
                GlobalSettings q;
                bool mig = SettingsIO.TryParse(pre30, out q, out err) && q.Keys.Boost == KeyBindings.VkXButton2 && q.Keys.BoostMods == 0 && q.BoostEnabled;
                n.BoostKeyMods = 0; bool alias = n.Keys.BoostMods == 0;
                Check("W23-4 强化准星 key: default Ctrl+T (not T alone), a file's own key kept (Leo's XButton2), pre-v4.0 file = plain key, pre-v3.0 file still migrates to 侧键2 (as v3.0-v5.0.2)",
                    def && tAlone && keep && plain && mig && alias, "def=" + def + " tAlone=" + tAlone + " keep=" + keep + " plain=" + plain + " mig=" + mig + " alias=" + alias);
            }
            // W23-5 画面比例: any W:H, 1:1, full-width input; bad -> 跟随
            {
                int w, h; string d = "";
                if (!ViewScale.TryParseRatio("123:111", out w, out h) || w != 123 || h != 111) d += "123:111; ";
                if (!ViewScale.TryParseRatio(" 16 ： 10 ", out w, out h) || w != 16 || h != 10) d += "fullwidth; ";
                if (!ViewScale.TryParseRatio("21/9", out w, out h) || w != 21) d += "slash; ";
                foreach (string bad in new string[] { "", "16", "a:b", "0:9", "9:0", "100:1", "-4:3", "4:3:2", null })
                    if (ViewScale.TryParseRatio(bad, out w, out h)) d += "accepted '" + bad + "'; ";
                if (ViewScale.NormalizeAspect("123:111") != "123:111" || ViewScale.NormalizeAspect("1:1") != "1:1" || ViewScale.NormalizeAspect("x") != "auto"
                    || ViewScale.NormalizeAspect(" 4:3 ") != "4:3") d += "normalize; ";
                if (Math.Abs(ViewScale.AspectValue("123:111", 1920, 1080) - 123.0 / 111) > 1e-12 || ViewScale.AspectValue("1:1", 1920, 1080) != 1.0) d += "value; ";
                GlobalSettings g = W23G(1920, 1080, "123:111", 75, "stretch");
                double r = g.ResolutionScaleX / g.ResolutionScale, want = (1920.0 / 1080) / (123.0 / 111);
                if (g.ResAspect != "123:111" || Math.Abs(r - want) > 1e-9) d += "stretch " + W23F(r) + " want " + W23F(want) + "; ";
                GlobalSettings s = W23G(1080, 1080, "1:1", 75, "stretch");
                if (s.ResolutionScaleX != s.ResolutionScale) d += "1:1 native; ";
                Check("W23-5 画面比例: any W:H (123:111) and 1:1 accepted and converted, ：／ accepted, junk / 0 / extreme -> 跟随分辨率", d.Length == 0, d);
            }
            // W23-6 站立静止偏移 (decision 5): Leo's example + every direction keeps its place (profile maths and live engine, both hands)
            {
                string d = "";
                GlobalSettings g = W23G(2160, 1440, "auto", 75, "stretch");
                Profile p = Profile.CreateLeoDefault("t"); p.SetStandPoint(0, 0); /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in has a 站立 point now; this case measures around the centre */
                double ppp = OffsetUnits.PxPerPct(g); // 7.2 px per 1 %
                // Leo's example: D ← 4.17 % from centre, stand → 1 % -> D ← 5.17 % from the stand point
                p.Dirs[3].OffX = -4.17 * ppp; Profile.RecomputeDiagonals(p);
                double[] cx = new double[8], cy = new double[8];
                for (int i = 0; i < 8; i++) p.DirFromCentre(i, out cx[i], out cy[i]);
                double[,] before = new double[9, 2], before1 = new double[9, 2];
                for (int dir = -1; dir < 8; dir++)
                {
                    MotionEngine e0 = W23Hold(p, g, false, W23Keys(dir, 0)), e1 = W23Hold(p, g, true, W23Keys(dir, 0));
                    before[dir + 1, 0] = e0.OffsetX; before[dir + 1, 1] = e0.OffsetY; before1[dir + 1, 0] = e1.OffsetX; before1[dir + 1, 1] = e1.OffsetY;
                }
                p.SetStandPoint(1.0 * ppp, 0.5 * ppp);
                bool on = p.StandAsZero && Math.Abs(p.StandOffsetX - ppp) < 1e-12;
                double dPct = OffsetUnits.ToPct(p.Dirs[3].OffX, g);
                if (Math.Abs(dPct - (-5.17)) > 1e-9) d += "D now " + W23F(dPct) + "% (want -5.17); ";
                for (int i = 0; i < 8; i++)
                {
                    double x, y; p.DirFromCentre(i, out x, out y);
                    if (Math.Abs(x - cx[i]) > 1e-9 || Math.Abs(y - cy[i]) > 1e-9) d += Profile.DirNames[i] + " moved; ";
                }
                double worst = 0;
                for (int dir = -1; dir < 8; dir++)
                {
                    MotionEngine e0 = W23Hold(p, g, false, W23Keys(dir, 0)), e1 = W23Hold(p, g, true, W23Keys(dir, 0));
                    if (dir < 0)
                    {
                        // standing still = the stand point (screen px, y down), both hands (hand 1 mirrored)
                        double k = g.ResolutionScale;
                        if (Math.Abs(e0.OffsetX - k * ppp) > 0.05 || Math.Abs(e0.OffsetY + k * 0.5 * ppp) > 0.05) d += "stand " + W23F(e0.OffsetX) + "," + W23F(e0.OffsetY) + "; ";
                        if (Math.Abs(e1.OffsetX + k * ppp) > 0.05) d += "stand hand1 " + W23F(e1.OffsetX) + "; ";
                        continue;
                    }
                    worst = Math.Max(worst, Math.Max(Math.Abs(e0.OffsetX - before[dir + 1, 0]), Math.Abs(e0.OffsetY - before[dir + 1, 1])));
                    worst = Math.Max(worst, Math.Max(Math.Abs(e1.OffsetX - before1[dir + 1, 0]), Math.Abs(e1.OffsetY - before1[dir + 1, 1])));
                }
                if (worst > 0.05) d += "engine moved by " + W23F(worst) + " px; ";
                // the round trip back to 0 restores the old numbers
                p.SetStandPoint(0, 0);
                if (p.StandAsZero || Math.Abs(OffsetUnits.ToPct(p.Dirs[3].OffX, g) + 4.17) > 1e-9) d += "off -> " + W23F(OffsetUnits.ToPct(p.Dirs[3].OffX, g)) + "; ";
                // saved + reloaded keeps it; a file from before v5.1 with a stand value is still forced to 0
                Profile s = Profile.CreateLeoDefault("s"); s.SetStandPoint(10, -5);
                GlobalSettings gs = GlobalSettings.CreateDefault(); gs.Profiles.Clear(); gs.Profiles.Add(s); gs.ActiveProfile = "s";
                GlobalSettings b; string err;
                bool rt = SettingsIO.TryParse(SettingsIO.Serialize(gs), out b, out err) && b.Active.StandAsZero && b.Active.StandOffsetX == 10 && b.Active.StandOffsetY == -5;
                string oldJson = SettingsIO.Serialize(gs).Replace("\"StandAsZero\":true,", "").Replace("\"StandAsZero\": true,", "");
                GlobalSettings o;
                bool oldZero = SettingsIO.TryParse(oldJson, out o, out err) && !o.Active.StandAsZero && o.Active.StandOffsetX == 0 && o.Active.StandOffsetY == 0;
                Profile big = Profile.CreateLeoDefault("b"); big.SetStandPoint(0, 0); /* v5.1.1: the built-in has a 站立 point */ big.SetStandPoint(999, double.NaN);
                bool nan = !big.StandAsZero;
                big.SetStandPoint(999, -999);
                bool clamp = big.StandOffsetX == Profile.StandLimitPx && big.StandOffsetY == -Profile.StandLimitPx;
                Check("W23-6 站立静止偏移: stand → 1 % makes D ← 4.17 % read ← 5.17 % from the stand point; all 8 directions keep their screen place (maths exact, engine < 0.05 px, normal + 换手); standing still = the stand point; back to 0 restores; saved; pre-v5.1 files stay 0; clamp ±200 / NaN ignored",
                    on && d.Length == 0 && rt && oldZero && nan && clamp, d + " on=" + on + " rt=" + rt + " old=" + oldZero + " nan=" + nan + " clamp=" + clamp);
            }
            // W23-7 FOV / 拉伸 chain audit (decision 11): the X factor is the same on BOTH sides (A, D), for 换手 (mirrored),
            // lean Q / E and every diagonal; Y uses the plain factor. 16:10 screen + 4:3 = ×1.20 (the attachment case).
            {
                string d = "";
                foreach (string mode in new string[] { "stretch", "fit" })
                {
                    GlobalSettings g = W23G(1920, 1200, "4:3", 70, mode);
                    double k = g.ResolutionScale, kx = g.ResolutionScaleX;
                    double wantX = mode == "stretch" ? 1.2 : 1.0;
                    if (Math.Abs(kx / k - wantX) > 1e-9) d += mode + " ratio " + W23F(kx / k) + "; ";
                    Profile p = Profile.CreateLeoDefault("a"); p.SetStandPoint(0, 0); /* v5.1.1 (X5, Leo 21 Nov 2026): the built-in has a 站立 point now; this case measures around the centre */
                    // deliberately asymmetric: A far (13.5 %), D near (5.2 %), different lean sides
                    double ppp = 7.2;
                    p.Dirs[2].OffX = 13.5 * ppp; p.Dirs[2].OffY = 1 * ppp; p.Dirs[3].OffX = -5.2 * ppp; p.Dirs[3].OffY = -2 * ppp;
                    p.Dirs[0].OffX = 0.3 * ppp; p.Dirs[0].OffY = -3 * ppp; p.Dirs[1].OffX = -0.4 * ppp; p.Dirs[1].OffY = 4 * ppp;
                    p.LeanQX = -6 * ppp; p.LeanQY = -1 * ppp; p.LeanEX = 2 * ppp; p.LeanEY = -0.5 * ppp;
                    p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0;
                    Profile.RecomputeDiagonals(p);
                    for (int hand = 0; hand < 2; hand++)
                    {
                        HandView v = new HandView(); p.FillView(hand, v);
                        for (int dir = 0; dir < 8; dir++)
                            for (int lean = -1; lean <= 1; lean++)
                            {
                                MotionEngine e = W23Hold(p, g, hand == 1, W23Keys(dir, lean));
                                double lx = lean < 0 ? v.QX : lean > 0 ? v.EX : 0, ly = lean < 0 ? v.QY : lean > 0 ? v.EY : 0;
                                // v5.1.1 (X5, Leo 21 Nov 2026): FOV 70 vs 75 on the final X: gun side x1, other side x 70/75; Y none
                                double ex = ViewScale.FovX(kx * (v.DX[dir] + lx + v.SX), 70.0 / 75.0, hand), ey = -k * (v.DY[dir] + ly + v.SY); // screen y down
                                if (Math.Abs(e.OffsetX - ex) > 0.06 || Math.Abs(e.OffsetY - ey) > 0.06)
                                    d += mode + " hand" + hand + " " + Profile.DirNames[dir] + " lean" + lean + " got " + W23F(e.OffsetX) + "," + W23F(e.OffsetY) + " want " + W23F(ex) + "," + W23F(ey) + "; ";
                            }
                    }
                    // the two sides scale by the same factor: |A| / |D| on screen == |A| / |D| in the profile
                    MotionEngine ea = W23Hold(p, g, false, W23Keys(2, 0)), ed = W23Hold(p, g, false, W23Keys(3, 0));
                    double rs = Math.Abs(ea.OffsetX / ed.OffsetX), rp = Math.Abs(p.Dirs[2].OffX / p.Dirs[3].OffX);
                    if (Math.Abs(rs - rp * 75.0 / 70.0) > 1e-3) d += mode + " A/D ratio " + W23F(rs) + " vs " + W23F(rp * 75.0 / 70.0) + "; "; // D (other side) x 70/75
                }
                Check("W23-7 FOV / 拉伸 chain: X × the same factor left and right (A 13.5 % / D 5.2 %), both hands, Q / E lean, all 8 directions incl. diagonals; Y × the plain factor; 16:10 + 4:3 拉伸 = ×1.20, 不拉伸 = ×1",
                    d.Length == 0, d);
            }
            // W23-8 不拉伸 (fit): a picture wider than the screen gets bars top and bottom (smaller height -> smaller factor and 1 %)
            {
                string d = "";
                GlobalSettings fit = W23G(1920, 1200, "21:9", 75, "fit"), st = W23G(1920, 1200, "21:9", 75, "stretch");
                double rh = 1920.0 / (21.0 / 9.0);
                if (Math.Abs(OffsetUnits.HalfHeight(fit) - rh / 2) > 1e-9) d += "fit half " + W23F(OffsetUnits.HalfHeight(fit)) + "; ";
                if (Math.Abs(OffsetUnits.HalfHeight(st) - 600) > 1e-9) d += "stretch half; ";
                if (Math.Abs(fit.ResolutionScale - rh / 1440.0) > 1e-9 || fit.ResolutionScaleX != fit.ResolutionScale) d += "fit k " + W23F(fit.ResolutionScale) + "; ";
                // a % stays the same % of the visible picture either way: 7.2 px (1 % at 调参) -> 1 % of the picture
                if (Math.Abs(OffsetUnits.ToPct(7.2, fit) - 1.0) > 1e-9 || Math.Abs(OffsetUnits.ToPct(7.2, st) - 1.0) > 1e-9) d += "pct; ";
                // native ratio: both models identical to v5.0.2
                GlobalSettings n1 = W23G(1920, 1080, "16:9", 75, "fit"), n2 = W23G(1920, 1080, "16:9", 75, "stretch");
                if (n1.ResolutionScale != n2.ResolutionScale || n1.ResolutionScaleX != n2.ResolutionScaleX || n1.ResolutionScale != 1080.0 / 1440) d += "native; ";
                Check("W23-8 不拉伸: 21:9 on 1920x1200 = bars top and bottom (picture 822.86 px high), factor and 1 % follow the picture; native ratio identical in both models", d.Length == 0, d);
            }
            // W23-9 decision 17: one unreadable value gets Leo's v5 default, not the class default
            {
                string d = "";
                string j = LeoDefaultJson.Text.Replace("\"Fps\": 360", "\"Fps\": \"fast\"").Replace("\"ShowFadeMs\": 50", "\"ShowFadeMs\": \"x\"");
                GlobalSettings g; string err; List<string> dropped;
                bool ok = SettingsIO.TryParse(j, out g, out err, out dropped);
                if (!ok) d += "parse " + err;
                else
                {
                    if (g.Fps != 360) d += "Fps " + g.Fps + " (class default 144); ";
                    if (g.ShowFadeMs != 50) d += "ShowFade " + g.ShowFadeMs + "; ";
                    if (dropped.Count != 2) d += "dropped " + string.Join(",", dropped) + "; ";
                }
                // a profile value: Profiles[1] (= Leo默认 (旧)) Diameter unreadable -> Leo's value
                Profile leo = Profile.CreateLeoDefault("x");
                string j2 = LeoDefaultJson.Text;
                int p1 = j2.IndexOf("\"Leo默认 (旧)\"", j2.IndexOf("\"Profiles\"", StringComparison.Ordinal), StringComparison.Ordinal);
                int dm = p1 < 0 ? -1 : j2.LastIndexOf("\"Diameter\": ", p1, StringComparison.Ordinal);
                if (dm < 0) dm = j2.IndexOf("\"Diameter\": ", Math.Max(0, p1), StringComparison.Ordinal);
                int end = j2.IndexOfAny(new char[] { ',', '\n' }, dm + 12);
                j2 = j2.Substring(0, dm) + "\"Diameter\": \"big\"" + j2.Substring(end);
                GlobalSettings g2;
                if (!SettingsIO.TryParse(j2, out g2, out err, out dropped)) d += "parse2 " + err;
                else
                {
                    bool any = false;
                    foreach (Profile q in g2.Profiles) if (q.Diameter == leo.Diameter) any = true;
                    if (!any || dropped.Count != 1) d += "Diameter not Leo's " + leo.Diameter + " dropped=" + string.Join(",", dropped) + "; ";
                }
                // an unreadable 强化 key gets the v5.1 default Ctrl+T (decision 3 over Leo's old XButton2)
                string j3 = LeoDefaultJson.Text.Replace("\"Boost\": 6,", "\"Boost\": \"side\",");
                GlobalSettings g3;
                if (!SettingsIO.TryParse(j3, out g3, out err, out dropped) || g3.Keys.Boost != 6 || g3.Keys.BoostMods != 0) d += "boost " /* v5.1.1 (X5, Leo 21 Nov 2026): default 鼠标侧键2 */ + (g3 == null ? err : g3.Keys.ComboOf(12)) + "; ";
                Check("W23-9 an unreadable value gets Leo's v5 default (Fps 360, 出现渐变 50 ms, profile Diameter) not the class default; unreadable 强化 key -> Ctrl+T", d.Length == 0, d);
            }
            // W23-10 弹药提示 weapon keys as text (the 按键 page box)
            {
                int[] v = WeaponKeyText.Parse("1 3 v f12，0、2");
                bool ok = v != null && string.Join(",", v) == "49,51,86,123,48,50" && WeaponKeyText.Format(v) == "1 3 V F12 0 2"
                    && WeaponKeyText.Parse("1 ?") == null && WeaponKeyText.Parse("F13") == null && WeaponKeyText.Parse("").Length == 0
                    && WeaponKeyText.Parse("1 1 1").Length == 1;
                Check("W23-10 weapon keys text: 「1 3 V F12」 ⇄ VK (letters, digits, F1-F12; ，、 separators; duplicates once; junk rejected)", ok, v == null ? "null" : string.Join(",", v));
            }
            // W23-12 Leo round 2 (R2 / R4): reload times + tap / reversal tunables: defaults, clamps, bad -> default, round trip, reset
            {
                string d = "";
                GlobalSettings n = GlobalSettings.CreateDefault();
                if (n.AmmoReloadMs != 3000 || n.AmmoQuickReloadMs != 2200 || /* Leo 21 Nov 2026 */ n.TapThresholdMs != 200 || n.ReverseMinMs != 250 || n.ReturnTimePct != 100 || n.ReversePauseMs != 0) d += "defaults; ";
                GlobalSettings o; string err;
                if (!SettingsIO.TryParse(LeoDefaultJson.Text, out o, out err) || o.AmmoReloadMs != 3000 || o.ReverseMinMs != 250 || o.ReturnTimePct != 100) d += "old file; ";
                GlobalSettings c = GlobalSettings.CreateDefault();
                c.AmmoReloadMs = 1; c.AmmoQuickReloadMs = 99999; c.TapThresholdMs = 1000; c.ReverseMinMs = -5; c.ReturnTimePct = 1; c.ReversePauseMs = 900; c.Validate();
                if (c.AmmoReloadMs != 300 || c.AmmoQuickReloadMs != 4000 || c.TapThresholdMs != 250 || c.ReverseMinMs != 0 || c.ReturnTimePct != 25 || c.ReversePauseMs != 300) d += "clamp; ";
                GlobalSettings b = GlobalSettings.CreateDefault();
                b.AmmoReloadMs = double.NaN; b.ReversePauseMs = double.PositiveInfinity; b.Validate();
                if (b.AmmoReloadMs != 3000 || b.ReversePauseMs != 0) d += "nan; ";
                GlobalSettings r = GlobalSettings.CreateDefault();
                r.AmmoReloadMs = 3100; r.AmmoQuickReloadMs = 1200; r.TapThresholdMs = 90; r.ReverseMinMs = 220; r.ReturnTimePct = 150; r.ReversePauseMs = 40; r.Validate();
                GlobalSettings rr;
                if (!SettingsIO.TryParse(SettingsIO.Serialize(r), out rr, out err) || rr.AmmoReloadMs != 3100 || rr.AmmoQuickReloadMs != 1200 || rr.TapThresholdMs != 90
                    || rr.ReverseMinMs != 220 || rr.ReturnTimePct != 150 || rr.ReversePauseMs != 40) d += "round trip; ";
                string bad = System.Text.RegularExpressions.Regex.Replace(SettingsIO.Serialize(r), "\"AmmoReloadMs\":\\s*3100", "\"AmmoReloadMs\": \"slow\"");
                if (bad.IndexOf("\"slow\"", StringComparison.Ordinal) < 0) d += "replace did not apply; ";
                if (!SettingsIO.TryParse(bad, out rr, out err) || rr.AmmoReloadMs != 3000 || rr.AmmoQuickReloadMs != 1200) d += "unreadable; ";
                r.ResetKeys();
                if (r.AmmoReloadMs != 3000 || r.AmmoQuickReloadMs != 2200) d += "reset keys; ";
                Check("W23-12 R2 / R4 tunables: 换弹 3000 / 快换 2200 ms, 短按 200, 换向最短 250, 回程 100 %, 换向停顿 0; clamped, bad -> default, saved, 按键恢复初始 resets the reload times", d.Length == 0, d);
            }
            // W23-11 W28 / W33 / W26 fields: consent never filled from Leo's file; W33 bools default off and survive a round trip
            {
                string j = LeoDefaultJson.Text.Replace("\"Version\": 2", "\"Version\": 2, \"NetStatsConsent\": \"yes\", \"OverlayDCompMode\": true, \"AdsRealHide\": true, \"GpuDiagLog\": true, \"ResStretched\": false");
                GlobalSettings g; string err; List<string> dropped;
                bool ok = SettingsIO.TryParse(j, out g, out err, out dropped) && g.NetStatsConsent == null && g.OverlayDCompMode && g.AdsRealHide && g.GpuDiagLog
                    && g.ResStretched == false && !g.PictureStretched;
                GlobalSettings d = GlobalSettings.CreateDefault();
                bool defs = !d.OverlayDCompMode && !d.AdsRealHide && !d.GpuDiagLog && d.ResStretched == null && d.PictureStretched && d.Clone(false).ResStretched == null;
                Check("W23-11 NetStatsConsent unreadable -> null (ask), never Leo's answer; OverlayDCompMode / AdsRealHide / GpuDiagLog / ResStretched read, default off / null (= 拉伸)", ok && defs,
                    "ok=" + ok + " defs=" + defs + " " + err + (dropped == null ? "" : " dropped=" + string.Join(",", dropped)));
            }
        }
    }
}
