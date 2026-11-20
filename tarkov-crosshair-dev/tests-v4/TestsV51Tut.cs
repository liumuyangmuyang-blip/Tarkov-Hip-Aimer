using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    // v5.1 (W27): tutorial pages for the v5.1 features -- pure maths of src/TutorialStepsV51.cs + source checks.
    public static partial class Program
    {
        private static bool v51TutRan;

        [ModuleInitializer]
        internal static void V51TutInit() { V51TutTests(); }

        private static string V51Src(string f)
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string p = Path.Combine(d, "..", "tarkov-crosshair-v4", "src", f);
                if (File.Exists(p)) return File.ReadAllText(p);
                d = Path.GetDirectoryName(d);
            }
            string abs = "/home/dev/tarkov-crosshair-v4/src/" + f;
            return File.Exists(abs) ? File.ReadAllText(abs) : "";
        }

        private static bool SwapNan()
        {
            double o, n; TutorialV51Math.Swap(double.NaN, 900, true, out o, out n);
            double o2, n2; TutorialV51Math.Swap(100, 0, false, out o2, out n2);
            return o == 1 && n == 1 && o2 == 1 && n2 == 1;
        }

        public static void V51TutTests()
        {
            if (v51TutRan) return;
            v51TutRan = true;

            // ammo bands: 5 words, full -> empty, never a number
            Check("V51T-1 five bands", TutorialV51Math.BandName.Length == 5 && TutorialV51Math.BandName[0] == "满" && TutorialV51Math.BandName[4] == "快空", "");
            Check("V51T-2 band order full..empty", TutorialV51Math.AmmoBand(30, 30) == 0 && TutorialV51Math.AmmoBand(20, 30) == 1
                && TutorialV51Math.AmmoBand(12, 30) == 2 && TutorialV51Math.AmmoBand(6, 30) == 3 && TutorialV51Math.AmmoBand(0, 30) == 4, "");
            bool mono = true; int prev = 0;
            for (int i = 30; i >= 0; i--) { int b = TutorialV51Math.AmmoBand(i, 30); if (b < prev) mono = false; prev = b; }
            Check("V51T-3 band never goes back up while firing", mono, "");
            Check("V51T-4 band bad input safe", TutorialV51Math.AmmoBand(double.NaN, 30) == 0 && TutorialV51Math.AmmoBand(5, 0) == 0
                && TutorialV51Math.AmmoBand(5, double.PositiveInfinity) == 0, "");
            // count = hold time x RPM
            Check("V51T-5 600 RPM for 1 s = 10 rounds", Math.Abs(TutorialV51Math.RoundsFor(1000, 600) - 10) < 1e-9, "");
            Check("V51T-6 rounds NaN/neg safe", TutorialV51Math.RoundsFor(-5, 600) == 0 && TutorialV51Math.RoundsFor(100, double.NaN) == 0, "");
            // reloads (R2): both swap in a NEW FULL mag after the reload time; old count until then
            Check("V51T-2b bands 90/60/35/15 (R2)", TutorialV51Math.AmmoBand(27, 30) == 0 && TutorialV51Math.AmmoBand(26.9, 30) == 1
                && TutorialV51Math.AmmoBand(18, 30) == 1 && TutorialV51Math.AmmoBand(17.9, 30) == 2, "");
            Check("V51T-7 reload: old count until the time, then a full mag", TutorialV51Math.AfterReload(5, 30, 2399, 2400) == 5
                && TutorialV51Math.AfterReload(5, 30, 2400, 2400) == 30 && TutorialV51Math.AfterReload(29, 30, 900, 900) == 30, "");
            double o1, n1, o2, n2;
            TutorialV51Math.Swap(0, 2400, false, out o1, out n1);
            TutorialV51Math.Swap(2400, 2400, false, out o2, out n2);
            Check("V51T-8 swap: old in place, then gone; new seated at the end", o1 == 0 && n1 == 0 && o2 == 1 && n2 == 1, "");
            double qo, qn, no, nn;
            TutorialV51Math.Swap(900 * TutorialV51Math.QuickOutShare, 900, true, out qo, out qn);
            TutorialV51Math.Swap(900 * TutorialV51Math.QuickOutShare, 2400, false, out no, out nn);
            Check("V51T-9 quick drops the old mag faster than normal", qo == 1 && no < 0.5, "qo=" + qo + " no=" + no);
            Check("V51T-10 defaults 2400 / 900 ms; swap NaN-safe", TutorialV51Math.DefaultNormalMs == 2400 && TutorialV51Math.DefaultQuickMs == 900
                && SwapNan(), "");
            // standing zero example (decision 5)
            double fs = TutorialV51Math.FromStand(-4.17, 1.0);
            Check("V51T-11 D ← 4.17% from centre = ← 5.17% from stand point", Math.Abs(fs + 5.17) < 1e-9, "fs=" + fs);
            Check("V51T-12 pct text", TutorialV51Math.PctText(-5.17) == "← 5.17%" && TutorialV51Math.PctText(1) == "→ 1.00%", TutorialV51Math.PctText(-5.17));
            // boost (corrected decision 3): toggle flips per press of the mode-switch key; hold follows the key
            Check("V51T-13 boost toggle", TutorialV51Math.BoostAfter(false, true, true, true) && !TutorialV51Math.BoostAfter(true, true, true, true)
                && TutorialV51Math.BoostAfter(true, true, false, false), "");
            Check("V51T-14 boost hold", TutorialV51Math.BoostAfter(false, false, true, true) && !TutorialV51Math.BoostAfter(true, false, false, false), "");
            // grenade toggle never comes back by itself
            Check("V51T-15 grenade toggle", TutorialV51Math.GrenadeAfter(false, true) && TutorialV51Math.GrenadeAfter(true, false) && !TutorialV51Math.GrenadeAfter(true, true), "");
            // tap script: taps are shorter than the 120 ms tap threshold, reversal A -> D is contiguous
            int[] s = TutorialV51Math.TapScript;
            Check("V51T-16 taps < 120 ms, A then D back to back", s[1] - s[0] < 120 && s[4] - s[3] < 120 && s[7] == s[9]
                && s[8] == TKey.A && s[11] == TKey.D, "");
            Check("V51T-17 tap phases", TutorialV51Math.TapPhase(800) == 0 && TutorialV51Math.TapPhase(3500) == 1 && TutorialV51Math.TapPhase(5000) == 2
                && TutorialV51Math.TapPhase(100) == -1, "");
            double x, y, w, h;
            TutorialV51Math.Fit(0, 0, 400, 100, 16.0 / 9.0, out x, out y, out w, out h);
            Check("V51T-18 fit 16:9 keeps aspect inside", Math.Abs(w / h - 16.0 / 9.0) < 1e-9 && w <= 400 && h <= 100 && x >= 0, "w=" + w);

            // source checks
            string src = V51Src("TutorialStepsV51.cs");
            Check("V51T-19 source found", src.Length > 1000, "");
            string lits = string.Join("\n", Regex.Matches(src, "\"[^\"\\n]*\"").Cast<Match>().Select(m => m.Value));
            string[] banned = { "未实测", "示意", "估计", "起点设置", "只是起点", "未核实", "慢启动" };
            string hit = null;
            foreach (string b in banned) if (lits.Contains(b)) hit = b;
            Check("V51T-20 no disclaimer / 慢启动 wording in v5.1 tutorial strings", hit == null, hit ?? "");
            Check("V51T-21 boost text: T keeps the device on, mode-switch key = boost", lits.Contains("先按 T 打开战术装置") && lits.Contains("T 不管强化")
                && src.Contains("KeyBindings.ActionBoost"), "");
            Check("V51T-22 pages registered", V51Src("TutorialStepsA.cs").Contains("TutorialStepsV51.AddCalibration(l)")
                && V51Src("TutorialStepsB.cs").Contains("TutorialStepsV51.AddFeatures(steps)"), "");
            Check("V51T-23 hipfire note no longer says the crosshair is fixed at centre when standing",
                !V51Src("TutorialSteps12.cs").Contains("站着不动时，准星固定在屏幕正中间") && V51Src("TutorialSteps12.cs").Contains("站立静止偏移"), "");
            Check("V51T-25 R3: no weapon-switch wording in the tutorial", !lits.Contains("换武器") && !lits.Contains("副武器") && !lits.Contains("栏位")
                && !src.Contains("WeaponKeys"), "");
            Check("V51T-26 R4: taps page names 短按阈值 / 换向最短时间 on 移动", lits.Contains("短按阈值") && lits.Contains("换向最短时间") && lits.Contains("「移动」"), "");
            Check("V51T-27 R2: ammo text says a new full mag, shows the swap; bands match AmmoModel", lits.Contains("新的满弹匣") && lits.Contains("旧弹匣直接丢在地上")
                && V51Src("AmmoModel.cs").Contains("FullFrom = 0.9, MostFrom = 0.6, HalfFrom = 0.35, LowFrom = 0.15"), "");
            Check("V51T-24 jump page no longer says the tutorial ends there", !V51Src("TutorialStep10.cs").Contains("教程到这里就结束了"), "");
        }
    }
}
