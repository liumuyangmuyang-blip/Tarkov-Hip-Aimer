// v5.1.1 (X16, Leo 21 Nov 2026): golden hashes of the 跳跃 path with the default trigger (按下时触发). The constants were
// computed by this exact code compiled against the pre-X16 src (backup-v5.1-X16), so the default must stay bit-identical.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X16GoldInit() { V511X16GoldTests(); }

        static ulong X16Hash(Profile p, GlobalSettings g, int seed, int frames)
        {
            MotionEngine e = new MotionEngine(); e.GameFocused = true; e.OwnForeground = true;
            Random rnd = new Random(seed);
            InputState s = new InputState();
            ulong h = 1469598103934665603UL;
            for (int k = 0; k < frames; k++)
            {
                if (rnd.Next(10) == 0)
                {
                    s = new InputState();
                    s.Fwd = rnd.Next(4) == 0; s.Left = rnd.Next(5) == 0; s.Right = rnd.Next(5) == 0;
                    s.LeanL = rnd.Next(4) == 0; s.Fire = rnd.Next(3) == 0; s.Ads = rnd.Next(4) == 0;
                    s.Jump = rnd.Next(3) == 0; s.Shift = rnd.Next(6) == 0;
                }
                if (rnd.Next(400) == 0) e.GameFocused = !e.GameFocused;
                double dt = (rnd.Next(3) == 0 ? 1.0 / 60 : rnd.Next(2) == 0 ? 1.0 / 144 : 1.0 / 240);
                e.Update(dt, s, p, g, PreviewMode.Live, 1.0);
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetX)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetY)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.JumpOffset)) * 1099511628211UL;
                h = (h ^ (e.Jumping ? 1UL : 0UL)) * 1099511628211UL;
            }
            return h;
        }

        static ulong X16PreviewHash(Profile p, GlobalSettings g)
        {
            MotionEngine e = new MotionEngine(); e.GameFocused = false; e.OwnForeground = true;
            ulong h = 1469598103934665603UL;
            for (int k = 0; k < 3000; k++)
            {
                e.Update(1.0 / 144, new InputState(), p, g, PreviewMode.JumpStand, 1.0);
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.JumpOffset)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetY)) * 1099511628211UL;
            }
            return h;
        }

        static List<Profile> X16Profiles()
        {
            List<Profile> ps = new List<Profile>();
            Profile a = X18Pre("x16a") /* X18: goldens were taken on the pre-21 Nov default */; a.Jump.Enabled = true; ps.Add(a);
            Profile b = X18PreBlank("x16b"); b.Jump.Enabled = true; b.Jump.Phases[0].EndPx = -40; ps.Add(b);
            ps.Add(X18Pre("x16c")); // 跳跃 as shipped
            return ps;
        }

        static readonly ulong[] X16Golden = new ulong[] {
            1990195736994939276UL, 355108193569137668UL, 4985203467649481608UL, 12674436476449984155UL,
            14288654440430954824UL, 14786538278673972983UL, 15808890535718436876UL, 12674436476449984155UL,
            15931632203102841096UL, 16448627265116507169UL, 3409033301600646180UL, 11855052660000814867UL,
            3307375896006671565UL, 3410358919605851623UL, 3987113514917878104UL, 11855052660000814867UL,
            1990195736994939276UL, 355108193569137668UL, 4985203467649481608UL, 12674436476449984155UL,
            14288654440430954824UL, 14786538278673972983UL, 15808890535718436876UL, 12674436476449984155UL };

        static void V511X16GoldTests()
        {
            List<ulong> got = new List<ulong>();
            List<Profile> ps = X16Profiles();
            GlobalSettings g1 = new GlobalSettings(); g1.OnlyWhenGameFocused = false;
            GlobalSettings g2 = new GlobalSettings(); g2.OnlyWhenGameFocused = true;
            foreach (Profile p in ps)
                foreach (GlobalSettings g in new GlobalSettings[] { g1, g2 })
                {
                    for (int seed = 1; seed <= 3; seed++) got.Add(X16Hash(p, g, seed, 20000));
                    got.Add(X16PreviewHash(p, g));
                }
            bool ok = got.Count == X16Golden.Length;
            for (int i = 0; ok && i < got.Count; i++) if (got[i] != X16Golden[i]) ok = false;
            string s = "";
            foreach (ulong v in got) s += v + "UL, ";
            Check("X16-1 跳跃 按下时触发 (default) = pre-X16 behaviour: 24 golden hashes (3 profiles x 2 focus modes x 3 random live seeds + preview)", ok, ok ? "" : "got " + s);
        }
    }
}
