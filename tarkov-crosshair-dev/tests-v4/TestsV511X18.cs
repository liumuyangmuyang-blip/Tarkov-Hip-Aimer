// v5.1.1 (X18, Leo 21 Nov 2026 tuned default): the built-in default profile = every field of Leo's tuned profile 「Leo默认」
// (his settings.json of 21 Nov 23:43, fixture leo-v511-1121-tuned-settings.v511, SavedDefault ignored); the fresh-install
// globals = his values except the screen-specific ones, the first-run flags and Autostart / GrenadeHideOn (Leo still to decide).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X18Init() { V511X18Tests(); }

        /// <summary>The built-in default BEFORE Leo's 21 Nov 2026 tuned default (X5 values, fixture leo-default-pre-x18.v511):
        /// the engine tests written against it keep their exact numbers.</summary>
        static Profile X18Pre(string name)
        {
            string e; GlobalSettings g = LoadFixture("leo-default-pre-x18.v511", out e);
            Profile p = g == null ? null : g.FindProfile("Leo默认 (旧)");
            if (p == null) return Profile.CreateLeoDefault(name);
            p = p.CloneValues(); p.Name = name; p.SavedDefault = null; p.SavedDefaultAt = null;
            return p;
        }

        /// <summary>Profile.CreateBlank on the pre-21 Nov default (same steps as CreateBlank).</summary>
        static Profile X18PreBlank(string name)
        {
            Profile p = X18Pre(name);
            p.Dirs = Profile.CreateBlank("z").Dirs;
            p.StandOffsetX = 0; p.StandOffsetY = 0; p.StandAsZero = false;
            p.RecoilMaxPx = 0; p.RecoilClickKickPx = 0; p.RecoilDriftX = 0;
            p.LeanQX = 0; p.LeanQY = 0; p.LeanEX = 0; p.LeanEY = 0;
            p.StandLeanSame = true; p.StandLeanQX = 0; p.StandLeanQY = 0; p.StandLeanEX = 0; p.StandLeanEY = 0;
            p.AllHandAuto();
            if (p.Jump == null) p.Jump = new JumpSettings();
            p.Jump.Enabled = false;
            if (p.Jump.Phases != null) foreach (JumpPhase ph in p.Jump.Phases) if (ph != null) ph.EndPx = 0;
            p.ResetCurvesToRecommended();
            p.SavedDefault = null; p.SavedDefaultAt = null;
            return p;
        }

        static Profile X18Leo()
        {
            string e; GlobalSettings g = LoadFixture("leo-v511-1121-tuned-settings.v511", out e);
            Profile p = g == null ? null : g.FindProfile("Leo默认");
            if (p == null) return null;
            p = p.CloneValues(); p.SavedDefault = null; p.SavedDefaultAt = null;
            p.Boost = V50Expected().Boost.Clone(); // Leo: 强化准星 keeps the old built-in block, OwnLook off
            return p;
        }

        static string X18ViewDiff(HandView a, HandView b)
        {
            string d = "";
            foreach (FieldInfo f in typeof(HandView).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object x = f.GetValue(a), y = f.GetValue(b);
                if (x is double[])
                {
                    double[] xa = (double[])x, ya = (double[])y;
                    for (int i = 0; i < xa.Length; i++) if (Math.Abs(xa[i] - ya[i]) > 1e-9) d += f.Name + "[" + i + "] ";
                }
                else if (x is double) { if (Math.Abs((double)x - (double)y) > 1e-9) d += f.Name + " "; }
                else if (!object.Equals(x, y) && !(x is Array)) d += f.Name + " ";
            }
            return d;
        }

        // 调参 base, monitor, window title and first-run flags stay the code defaults (Leo 21 Nov 2026)
        static readonly string[] X18GlobalSkip = { "Profiles", "ActiveProfile", "ResAspectCustom",
            "ResFovType", "ResBaseW", "ResBaseH", "ResBaseFov", "ResBaseAspect", "ResV41",
            "MonitorIndex", "GameWindowTitle", "WizardDone", "TutorialDone", "MustReadAck", "NetStatsConsent", "Version" };
        // Leo 21 Nov 2026: his screen (1800×1440, 5:4, stretched, FOV 75) and Autostart / GrenadeHideOn off are defaults too
        static readonly string[] X18GlobalPending = new string[0];

        static void V511X18Tests()
        {
            Profile leo = X18Leo();
            // X18-1 every field of the built-in default (fresh install, CreateLeoDefault, 恢复到 Leo 推荐) = Leo's tuned profile
            {
                string d = "";
                if (leo == null) d = "fixture missing; ";
                else
                {
                    GlobalSettings fresh = GlobalSettings.CreateDefault(); fresh.Validate();
                    foreach (Profile got in new Profile[] { fresh.Active, Profile.CreateLeoDefault("x"), fresh.RestoreLeoDefault(Profile.CreateBlank("y")) })
                    {
                        Profile c = got.CloneValues(); c.Name = leo.Name; c.SavedDefault = null; c.SavedDefaultAt = null;
                        List<string> diffs = new List<string>(); int n = 0;
                        DeepEq(c, leo, "$", diffs, ref n);
                        if (diffs.Count > 0 || n < 100) d += n + " values, diffs " + DiffStr(diffs) + "; ";
                        if (SettingsIO.ExportProfile(c) != SettingsIO.ExportProfile(leo)) d += "export differs; ";
                    }
                    Profile a = fresh.Active;
                    if (!(a.StandAsZero && a.StandOffsetX == 26.28 && a.StandOffsetY == -25.92 && a.LeanMs == 350 && a.RecoilRiseMs == 310
                        && !a.StandLeanSame && Math.Abs(a.StandLeanQX - 15.84) < 1e-9 && a.StandLeanQY == -28.8 && a.StandLeanEX == 20.16 && a.StandLeanEY == -21.6
                        && !a.Boost.OwnLook && a.Boost.Diameter == 15 && a.Boost.GlowPx == 4 && a.Boost.OuterThickness == 2.5 && a.Boost.Thickness == 1.8 && a.Boost.FollowHandColor && a.Shape == "cross" && a.CrossGap == 5.5)) d += "spot values; ";
                }
                Check("X18-1 built-in default (fresh install, CreateLeoDefault, 恢复到 Leo 推荐) = Leo's tuned 「Leo默认」 of 21 Nov 2026 in every field (站立 26.28 / -25.92, 侧身 350 ms, 站立侧身 own, 上抬 310 ms, 强化准星 old block with OwnLook off, cross)",
                    d.Length == 0, d);
            }
            // X18-2 on-screen positions (all 8 directions, lean, 站立, standing lean, both hands incl. 换手自定义) = his file's
            {
                string d = "";
                if (leo == null) d = "fixture missing; ";
                else
                {
                    Profile n = Profile.CreateLeoDefault("n");
                    for (int hand = 0; hand < 2; hand++)
                    {
                        HandView vn = new HandView(), vl = new HandView(); n.FillView(hand, vn); leo.FillView(hand, vl);
                        string vd = X18ViewDiff(vn, vl);
                        if (vd.Length > 0) d += "hand " + hand + ": " + vd + "; ";
                    }
                    GlobalSettings g = GlobalSettings.CreateDefault();
                    double we = 0;
                    Profile n0 = n.CloneValues(), l0 = leo.CloneValues();
                    n0.RecoilMaxPx = l0.RecoilMaxPx = 0; n0.RecoilClickKickPx = l0.RecoilClickKickPx = 0;
                    for (int hand = 0; hand < 2; hand++)
                        for (int dir = -1; dir < 8; dir++)
                            for (int lean = -1; lean <= 1; lean++)
                            {
                                MotionEngine en = W23Hold(n0, g, hand == 1, W23Keys(dir, lean)), el = W23Hold(l0, g, hand == 1, W23Keys(dir, lean));
                                we = Math.Max(we, Math.Abs(en.OffsetX - el.OffsetX) + Math.Abs(en.OffsetY - el.OffsetY));
                            }
                    if (we > 1e-9) d += "engine " + we + "; ";
                }
                Check("X18-2 on-screen positions of the built-in default = Leo's file (8 directions, Q/E, 站立, standing lean, both hands, engine 0 px)", d.Length == 0, d);
            }
            // X18-3 fresh-install globals = Leo's file except screen-specific values, first-run flags and Autostart / GrenadeHideOn
            {
                string d = "", pend = "";
                string file = File.ReadAllText(Fixture("leo-v511-1121-tuned-settings.v511")).TrimStart('\uFEFF');
                JsonObject jf = (JsonObject)JsonNode.Parse(file), jd;
                // the real first run: SettingsIO.Load on a missing file (no screen probe -> Leo's 1800×1440 stays)
                string dir = Path.Combine(Path.GetTempPath(), "x18-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
                string msg; Func<int[]> probe = SettingsIO.ScreenProbe; SettingsIO.ScreenProbe = null;
                GlobalSettings first;
                try { first = SettingsIO.Load(Path.Combine(dir, "settings.json"), out msg); } finally { SettingsIO.ScreenProbe = probe; }
                jd = (JsonObject)JsonNode.Parse(SettingsIO.Serialize(first));
                GlobalSettings det = GlobalSettings.CreateDefault(); det.ApplyLeoScreen(); det.DetectGameResolution(delegate { return new int[] { 2560, 1440 }; });
                if (det.ResGameW != 2560 || det.ResAspect != "5:4") d += "detect " + det.ResGameW + " " + det.ResAspect + "; ";
                HashSet<string> keys = new HashSet<string>();
                foreach (KeyValuePair<string, JsonNode> kv in jf) keys.Add(kv.Key);
                foreach (KeyValuePair<string, JsonNode> kv in jd) keys.Add(kv.Key);
                foreach (string k in keys)
                {
                    if (Array.IndexOf(X18GlobalSkip, k) >= 0) continue;
                    JsonNode a = jf[k], b = jd[k];
                    bool eq = (a == null && b == null) || (a != null && b != null && JsonNode.DeepEquals(a, b));
                    if (Array.IndexOf(X18GlobalPending, k) >= 0) { pend += k + "=" + b + " (Leo " + a + ") "; if (b == null || (bool)b != true) d += k + " changed before Leo decided; "; continue; }
                    if (!eq) d += k + ": default " + (b == null ? "-" : b.ToJsonString()) + " vs Leo " + (a == null ? "-" : a.ToJsonString()) + "; ";
                }
                GlobalSettings cd = GlobalSettings.CreateDefault();
                if (cd.Autostart || cd.GrenadeHideOn || new GlobalSettings().Autostart || new GlobalSettings().GrenadeHideOn) d += "autostart/grenade not off; ";
                Check("X18-3 fresh-install globals = Leo's 21 Nov file (incl. 1800×1440 5:4 stretched FOV 75, 开机自启 off, 丢雷隐藏 off) except 调参 base / monitor / first-run values", d.Length == 0, d);
            }
        }
    }
}
