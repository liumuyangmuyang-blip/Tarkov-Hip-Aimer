using System;
using System.IO;

namespace TarkovCrosshair.Tests
{
    // v3.4: 站着偏移 removed (forced 0 on load, 换手 item auto), 调参分辨率 locked at 2160x1440.
    public static partial class Program
    {
        static void V34Tests()
        {
            string dir = Path.Combine(Path.GetTempPath(), "v34-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            GlobalSettings g = GlobalSettings.CreateDefault();
            Profile p = g.Profiles[0];
            p.StandOffsetX = 7; p.StandOffsetY = -27; p.SetHandCustom(Profile.HandStand, 5, 5);
            g.ResBaseW = 2560; g.ResBaseH = 1600; g.ResGameW = 2560; g.ResGameH = 1600;
            string f = Path.Combine(dir, "s.json");
            File.WriteAllText(f, System.Text.RegularExpressions.Regex.Replace(SettingsIO.Serialize(g), "\"ResV41\"\\s*:\\s*true", "\"ResV41\":false")); // v4.1: an older (pre-v4.1) file keeps the lock
            string msg;
            GlobalSettings b = SettingsIO.Load(f, out msg);
            Profile q = b.Profiles[0];
            bool stand = q.StandOffsetX == 0 && q.StandOffsetY == 0 && !q.IsHandCustom(Profile.HandStand);
            bool res = b.ResBaseW == 2160 && b.ResBaseH == 1440 && b.ResGameH == 1600;
            Sim s = new Sim(q, b, 144); s.Run(1.0, In(""));
            b.ResGameH = 1440; Sim s2 = new Sim(q, b, 144); s2.Run(1.0, In(""));
            bool centre = Math.Abs(s.E.OffsetX) < 1e-9 && Math.Abs(s.E.OffsetY) < 1e-9 && Math.Abs(s2.E.OffsetX) < 1e-9;
            File.Delete(f); Directory.Delete(dir);
            Check("V34-1 站着偏移 forced 0 on load, 换手 item auto, ring centred when standing", stand && centre, "stand=" + stand + " centre=" + centre);
            Check("V34-2 调参分辨率 locked 2160x1440, 游戏分辨率 kept", res, b.ResBaseW + "x" + b.ResBaseH + " game " + b.ResGameH);
        }
    }
}
