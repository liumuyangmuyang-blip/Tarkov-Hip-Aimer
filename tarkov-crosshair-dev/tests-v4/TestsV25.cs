using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TarkovCrosshair.Tests
{
    // Leo's v2.3 「Leo 1011」 settings.json (fixture from the v2.5 step). v3.0: it loads with every tuned value kept, the diagonals
    // recomputed from the cardinals, the (unticked) combos dropped and the v3.0 fields at their defaults.
    public static partial class Program
    {
        static void V25Tests() { F1Leo1011(); }

        /// <summary>A v2.3-format file with the values of Leo's v2.3 screenshots (SPEC-v2.4.4-defaults.md), written with the
        /// verbatim v2.3 serializer (V23Settings.cs), including his v2.3-only fields as shown (all switched off).</summary>
        static string Leo1011V23Json()
        {
            V23.GlobalSettings g = V23.GlobalSettings.CreateDefault();
            g.Profiles.Clear();
            V23.Profile p = new V23.Profile();
            p.Name = "Leo 1011";
            p.Diameter = 12; p.Thickness = 1.2; p.Feather = 1.0; p.OpacityPct = 60;
            p.Color = "#00DF00"; p.Color2 = "#000000"; p.Gradient = "twotone"; p.OuterThickness = 1.5;
            p.CenterDot = false; p.CenterDotSize = 2;
            p.FollowMs = 600;
            double[,] dirs = { { 0, -30 }, { 0, 8 }, { 59, 0 }, { -36, 0 }, { 41.7, -21.2 }, { -25.5, -21.2 }, { 41.7, 5.7 }, { -25.5, 5.7 } };
            for (int i = 0; i < 8; i++) { p.Dirs[i].OffX = dirs[i, 0]; p.Dirs[i].OffY = dirs[i, 1]; }
            double[,] combo = { { 53.0, -7.0 }, { 36.9, -21.4 }, { 36.9, -6.6 }, { -10.0, -4.0 }, { 0.3, -18.4 }, { 0.3, -3.6 } };
            for (int i = 0; i < 6; i++) { p.Combos[i].Enabled = false; p.Combos[i].OffX = combo[i, 0]; p.Combos[i].OffY = combo[i, 1]; }
            p.ReverseLR = new V23.ReverseAxis(false, 254, 103, 143);   // 反向惯性 off, 500 = 254/103/143
            p.ReverseFB = new V23.ReverseAxis(false, 100, 200, 100);   // 反向惯性 off, 400 = 100/200/100
            p.RecoilMaxPx = 23; p.RecoilRiseMs = 300; p.RecoilClickKickPx = 8; p.RecoilFallMs = 180; p.RecoilDelayMs = 30; p.RecoilDriftX = 6;
            p.SprintDimEnabled = false; p.SprintRecoveryMs = 300; p.SprintDimPct = 30;   // 冲刺后恢复 off 300 / 30 %
            p.CrouchEnabled = false; p.CrouchPct = 50;                                    // 蹲下 off 50 %
            p.StandOffsetX = 0; p.StandOffsetY = 0;
            p.LeanQX = -6; p.LeanQY = -20; p.LeanEX = 10; p.LeanEY = -15; p.LeanMs = 250;
            g.Profiles.Add(p);
            g.ActiveProfile = p.Name;
            g.SprintMode = "toggle";                                                      // 冲刺 切换
            g.Keys.Sprint = 0xA0; g.Keys.Crouch = 0x43; g.Keys.Prone = 0x5A;              // LShift, C, Z
            return V23.SettingsIO.Serialize(g);
        }

        static void F1Leo1011()
        {
            string json = Leo1011V23Json();
            string path = Fixture("settings-v2.3-leo1011.json");
            if (!File.Exists(path)) File.WriteAllText(path, json, new UTF8Encoding(false));
            string onDisk = File.ReadAllText(path, Encoding.UTF8);
            GlobalSettings g; string err;
            bool ok = SettingsIO.TryParse(onDisk, out g, out err);
            Profile leo = ok ? g.FindProfile("Leo 1011") : null;
            Profile m4 = Leo1011V30("x"); m4.Validate();
            string a = "", b = "";
            if (leo != null)
            {
                Profile c = leo.Clone(); c.Name = "x";
                a = SettingsIO.ExportProfile(c); b = SettingsIO.ExportProfile(m4);
            }
            Check("F1 Leo's v2.3 settings.json 「Leo 1011」 loads with every tuned value kept (v3.0: diagonals recomputed, combos dropped, new fields default)",
                ok && leo != null && a == b && leo.SavedDefault == null && onDisk == json,
                "parsed=" + ok + " " + err + " found=" + (leo != null) + " sameJson=" + (a == b) + " fixtureStable=" + (onDisk == json)
                + (a != b ? FirstDiff(a, b) : ""));
        }

        /// <summary>What 「Leo 1011」 must become in v3.0 (= the v2.4.4 built-in values, combos gone, diagonals automatic).</summary>
        static Profile Leo1011V30(string name)
        {
            Profile p = new Profile();
            p.Name = name;
            p.Diameter = 12; p.Thickness = 1.2; p.Feather = 1.0; p.OpacityPct = 60;
            p.Color = "#00DF00"; p.Color2 = "#000000"; p.Gradient = "twotone"; p.OuterThickness = 1.5;
            p.CenterDot = false; p.CenterDotSize = 2;
            p.Dirs[0] = new DirSetting(0, -30); p.Dirs[1] = new DirSetting(0, 8); p.Dirs[2] = new DirSetting(59, 0); p.Dirs[3] = new DirSetting(-36, 0);
            Profile.RecomputeDiagonals(p);
            p.CurveAD = new MoveCurve(600, MotionCurve.V23Feel()); p.CurveWS = new MoveCurve(600, MotionCurve.V23Feel());
            p.StandOffsetX = 0; p.StandOffsetY = 0;
            p.RecoilMaxPx = 23; p.RecoilRiseMs = 300; p.RecoilClickKickPx = 8; p.RecoilFallMs = 180; p.RecoilDelayMs = 30; p.RecoilDriftX = 6;
            p.LeanQX = -6; p.LeanQY = -20; p.LeanEX = 10; p.LeanEY = -15; p.LeanMs = 250;
            return p;
        }

        static string FirstDiff(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            int s = Math.Max(0, i - 60);
            return " diff@" + i + ": loaded …" + a.Substring(s, Math.Min(120, a.Length - s)) + "… vs M4 …" + b.Substring(s, Math.Min(120, b.Length - s)) + "…";
        }
    }
}
