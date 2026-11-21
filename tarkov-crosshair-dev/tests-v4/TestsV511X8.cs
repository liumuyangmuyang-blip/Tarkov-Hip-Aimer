// v5.1.1 (X8, Leo 21 Nov 2026 option A; X12 Leo 21 Nov 2026: recoil now rises from the current point): with 站立静止偏移 on (StandAsZero), lean (Q / E, 换手 Q / E) count from
// the SCREEN CENTRE again, so they land where v5.0.2 put them; the 站立 point fades out smoothly while leaning. W/S/A/D and
// diagonals are unchanged; recoil top = the current point ↑ 28.8 px (4.00 %) in both hands (X12); stand 0 = as before.
using System;
using System.IO;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X8Init() { V511X8Tests(); }

        static MotionEngine X8Run(Profile p, GlobalSettings g, bool hand1, InputState keys, double sec, Action<MotionEngine> perFrame)
        {
            MotionEngine e = new MotionEngine();
            e.GameFocused = true; e.OwnForeground = true;
            InputState none = new InputState();
            const double dt = 1.0 / 360;
            for (int k = 0; k < 360; k++) e.Update(dt, none, p, g, PreviewMode.Live, 1.0);
            if (hand1)
            {
                InputState hs = new InputState(); hs.HandSwitch = true;
                for (int k = 0; k < 18; k++) e.Update(dt, hs, p, g, PreviewMode.Live, 1.0);
                for (int k = 0; k < 1080; k++) e.Update(dt, none, p, g, PreviewMode.Live, 1.0);
            }
            int n = (int)Math.Round(sec * 360);
            for (int k = 0; k < n; k++) { e.Update(dt, keys, p, g, PreviewMode.Live, 1.0); if (perFrame != null) perFrame(e); }
            return e;
        }

        static string X8Cmp(string name, MotionEngine e, double x, double y)
        {
            double dx = e.OffsetX - x, dy = e.OffsetY - y;
            return Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01 ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                " {0}: got {1:F3},{2:F3} want {3:F3},{4:F3};", name, e.OffsetX, e.OffsetY, x, y);
        }

        static void V511X8Tests()
        {
            GlobalSettings g = X5G(75);
            // X8-1 the v5.1 built-in vs the pre-21-Nov built-in (no stand point, same lean values): Q / E / 换手 Q / E land on
            // the same screen spot (< 0.01 px); every direction too; standing = the stand point.
            {
                string d = "";
                Profile n = X18Pre("n"), o = X5OldDefault();
                n.RecoilMaxPx = 24; // compare lean / moves with the old cap (recoil is not used here)
                for (int h = 0; h < 2; h++)
                {
                    bool hand1 = h == 1;
                    string pre = hand1 ? "换手 " : "";
                    for (int dir = -1; dir < 8; dir++)
                        for (int lean = -1; lean <= 1; lean++)
                        {
                            if (dir < 0 && lean == 0) continue;
                            MotionEngine en = X8Run(n, g, hand1, W23Keys(dir, lean), 6, null), eo = X8Run(o, g, hand1, W23Keys(dir, lean), 6, null);
                            d += X8Cmp(pre + "dir" + dir + " lean" + lean, en, eo.OffsetX, eo.OffsetY);
                        }
                    MotionEngine s = X8Run(n, g, hand1, new InputState(), 6, null);
                    d += X8Cmp(pre + "stand", s, hand1 ? -24.48 : 24.48, 25.2);
                }
                Check("X8-1 站立 point on: Q / E (and with every move key), 换手 Q / E land where v5.0.2 put them (< 0.01 px), W/S/A/D/diagonals unchanged, standing = 站立 point", d.Length == 0, d);
            }
            // X8-2 (updated by X12, Leo 21 Nov 2026: recoil rises from the CURRENT point, not from the centre): standing, recoil
            // top = the 站立 point ↑ 28.8 px (4.00 %); after 换手 = the mirrored 站立 point ↑ 28.8 px
            {
                string d = "";
                Profile n = X18Pre("n");
                InputState f = new InputState(); f.Fire = true;
                d += X8Cmp("recoil", X8Run(n, g, false, f, 6, null), 24.48, 25.2 - 28.8);
                d += X8Cmp("换手 recoil", X8Run(n, g, true, f, 6, null), -24.48, 25.2 - 28.8);
                Check("X8-2 recoil top = 站立 point ↑ 28.8 px (4.00 %), 换手 = mirrored 站立 point ↑ 28.8 px (X12, Leo 21 Nov 2026)", d.Length == 0 && n.RecoilMaxPx == 28.8, d);
            }
            // X8-3 nothing jumps: press / release Q, E and fire with the stand point on, at 360 fps the ring never moves more than
            // 1.5 px in one frame (the stand fade rides the 侧身过渡 and recoil curves).
            {
                double[] worst = new double[2];
                for (int run = 0; run < 2; run++)
                {
                    Profile n = X18Pre("n");
                    if (run == 1) n.SetStandPoint(0, 0); // same values, no 站立 point: the v5.0.2-style reference (kick = its own 8 px step)
                    MotionEngine m = new MotionEngine(); m.GameFocused = true; m.OwnForeground = true;
                    InputState none = new InputState(), q = W23Keys(-1, -1), e2 = W23Keys(-1, 1), fire = new InputState(); fire.Fire = true;
                    InputState qf = W23Keys(-1, -1); qf.Fire = true;
                    InputState[] seq = { none, q, none, e2, none, fire, none, qf, none };
                    double lx = double.NaN, ly = double.NaN;
                    foreach (InputState s in seq)
                        for (int k = 0; k < 720; k++)
                        {
                            m.Update(1.0 / 360, s, n, g, PreviewMode.Live, 1.0);
                            if (!double.IsNaN(lx)) worst[run] = Math.Max(worst[run], Math.Max(Math.Abs(m.OffsetX - lx), Math.Abs(m.OffsetY - ly)));
                            lx = m.OffsetX; ly = m.OffsetY;
                        }
                }
                Check("X8-3 no jump: lean / recoil press + release with the 站立 point on: worst step per frame (360 fps) <= the same profile without a 站立 point + 1 px (the click kick is its own step)",
                    worst[0] <= worst[1] + 1.0, "on " + worst[0] + " off " + worst[1]);
            }
            // X8-4 stand off: lean + recoil are plain additions from the centre as before (Q standing = lean value exactly
            // once the lean has settled), and a profile with stand 0 gives the same frames as StandAsZero true with 0 / 0.
            {
                string d = "";
                Profile o = X5OldDefault();
                MotionEngine eq = X8Run(o, g, false, W23Keys(-1, -1), 6, null);
                d += X8Cmp("off Q", eq, o.LeanQX, -o.LeanQY);
                Profile z = X5OldDefault(); z.StandAsZero = true; // forced on with a 0 point: the factor is applied to 0
                InputState f = new InputState(); f.Fire = true; f.LeanL = true;
                MotionEngine a = X8Run(o, g, false, f, 3, null), b = X8Run(z, g, false, f, 3, null);
                if (a.OffsetX != b.OffsetX || a.OffsetY != b.OffsetY) d += " zero-stand differs;";
                Check("X8-4 站立 off / 0: lean from the centre exactly as before", d.Length == 0, d);
            }
            // X8-5 the lean / recoil X boxes show the distance from the centre (no 站立 origin), W/S/A/D keep theirs
            {
                string dir = UiSrcDir();
                string lh = File.ReadAllText(Path.Combine(dir, "SettingsForm.PageLeanHand.cs")), rc = File.ReadAllText(Path.Combine(dir, "SettingsForm.PageRecoil.cs")),
                    tb = File.ReadAllText(Path.Combine(dir, "TutorialStepsB.cs")), t9 = File.ReadAllText(Path.Combine(dir, "TutorialStep9.cs")),
                    mv = File.ReadAllText(Path.Combine(dir, "SettingsForm.PageMove.cs"));
                bool ok = !lh.Contains("qx.Origin") && lh.Contains("if (item < Profile.HandStand) fx.Origin")
                    && !rc.Contains("drift.Origin") && !tb.Contains("p.LeanQX, OffsetUnits.StandOriginX")
                    && t9.Contains("if (item < Profile.HandStand) f.OriginX") && mv.Contains("fx.Origin = delegate { return OffsetUnits.StandOriginX(Cur, false); }")
                    && mv.Contains("侧身照旧从屏幕中心算起"); // X12 (Leo 21 Nov 2026): recoil now rises from the current point
                Check("X8-5 侧身 / 换手 Q E 到顶时水平偏移 / 后坐力 X boxes count from the centre; 移动 directions still from the 站立 point; note on 移动", ok, "");
            }
        }
    }
}
