using System;
using System.Collections.Generic;
using System.IO;

namespace TarkovCrosshair.Tests
{
    // v4.0 AUDIT-LOGIC items 4-7 (Settings.cs) + AUDIT-SETTINGS item 2 (import error text).
    public static partial class Program
    {
        static void V4AuditLogicTests()
        {
            // AL4: 开关准星 default is F12 everywhere.
            GlobalSettings a; string e; List<string> dr;
            bool p1 = SettingsIO.TryParse("{\"Keys\":{\"Toggle\":\"F12\"}}", out a, out e, out dr);
            GlobalSettings b; bool p2 = SettingsIO.TryParse("{\"Version\":1}", out b, out e, out dr);
            Check("AL4 Toggle default = F12 (wrong-type Toggle, missing Keys, new KeyBindings)",
                p1 && a.Keys.Toggle == 0x7B && p2 && b.Keys.Toggle == 0x7B && new KeyBindings().Toggle == 0x7B,
                p1 + " " + (a == null ? -1 : a.Keys.Toggle) + " " + p2 + " " + (b == null ? -1 : b.Keys.Toggle));

            // AL5: dropped / short Speeds -> 推荐 speeds; full Speeds kept.
            double[] rec = Profile.RecommendedSpeeds; // v5.0.2 (W18): 推荐 = Leo's curve
            MoveCurve n = new MoveCurve(380, rec); n.Speeds = null; n.Validate();
            MoveCurve sh = new MoveCurve(380, rec); sh.Speeds = new double[] { 10, 20 }; sh.Validate();
            double[] full = new double[] { 5, 15, 25, 35, 45 };
            MoveCurve fu = new MoveCurve(200, full); fu.Validate();
            bool nOk = true, shOk = sh.Speeds[0] == 10 && sh.Speeds[1] == 20, fuOk = true;
            for (int i = 0; i < MotionCurve.Points; i++)
            {
                nOk &= n.Speeds[i] == Math.Round(rec[i]);
                if (i >= 2) shOk &= sh.Speeds[i] == Math.Round(rec[i]);
                fuOk &= fu.Speeds[i] == full[i];
            }
            Check("AL5 MoveCurve.Validate: null / short Speeds fall back to 推荐, full Speeds kept", nOk && shOk && fuOk, nOk + " " + shOk + " " + fuOk);

            // AL6: broken / unreadable settings.json -> defaults with detected resolution.
            string dir = Path.Combine(Path.GetTempPath(), "al6-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, "settings.json");
            File.WriteAllText(f, "{ this is not json");
            SettingsIO.ScreenProbe = delegate { return new int[] { 1920, 1080 }; };
            string m; GlobalSettings broken = SettingsIO.Load(f, out m);
            bool brokenOk = broken.ResGameW == 1920 && broken.ResGameH == 1080 && m != null;
            bool readOk = true; string rdetail = "skipped";
            File.WriteAllText(f, "{}");
            try
            {
                using (FileStream lockIt = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    string m3; GlobalSettings rd = SettingsIO.Load(f, out m3);
                    if (m3 != null && m3.StartsWith("读取设置文件失败")) { readOk = rd.ResGameW == 1920 && rd.ResGameH == 1080; rdetail = rd.ResGameW + "x" + rd.ResGameH; }
                }
            }
            catch (Exception) { }
            SettingsIO.ScreenProbe = null;
            foreach (string x in Directory.GetFiles(dir)) File.Delete(x);
            Directory.Delete(dir);
            Check("AL6 broken / unreadable settings.json: defaults use the detected 游戏分辨率 (1920x1080)", brokenOk && readOk,
                broken.ResGameW + "x" + broken.ResGameH + " read=" + rdetail);

            // AL7: rebindable action holding a plain W/A/S/D goes back to its default; combos (Ctrl+D) kept.
            GlobalSettings g = GlobalSettings.CreateDefault();
            KeyBindings d = new KeyBindings();
            g.Keys.LeanLeft = KeyBindings.VkW; g.Keys.Jump = KeyBindings.VkS;
            g.Keys.SetCombo(KeyBindings.ActionLeanRight, KeyBindings.VkD, KeyBindings.ModCtrl);
            g.Validate();
            bool wOk = g.Keys.LeanLeft == d.LeanLeft && g.Keys.Jump == d.Jump
                && g.Keys.LeanRight == KeyBindings.VkD && g.Keys.GetMods(KeyBindings.ActionLeanRight) == KeyBindings.ModCtrl
                && g.Keys.Forward == KeyBindings.VkW;
            Check("AL7 after ForceWasd: plain W/S on 左侧身/跳跃 reset to defaults, Ctrl+D on 右侧身 kept",
                wOk, g.Keys.LeanLeft + " " + g.Keys.Jump + " " + g.Keys.LeanRight + "/" + g.Keys.GetMods(KeyBindings.ActionLeanRight));

            // AS2: import error is Chinese, no exception text.
            List<Profile> pl; string ie;
            bool imp = SettingsIO.TryParseImport("{\"Profiles\":[{\"Name\":", out pl, out ie);
            GlobalSettings gg; string ge;
            bool gp = SettingsIO.TryParse("{ nope", out gg, out ge);
            Check("AS2 import error = 文件内容不完整或格式不对，不是准星配置文件。", !imp && ie == "文件内容不完整或格式不对，不是准星配置文件。", ie + " | " + ge);
        }
    }
}
