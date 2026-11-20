using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TarkovShade;

// v5.1 (W24) 滤镜, Leo decision 10: preset tiers, 11-step 调试滤镜, 5-point curve (FilterCurvePoints), Windows limit (FilterLimit),
// event-driven filter hotkeys (FilterKeyFeed). Pure checks + source checks of the WinForms files (not compiled here).
namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W24Init()
        {
            V51W24Tiers();
            V51W24Tune();
            V51W24Curve();
            V51W24Feed();
            V51W24Source();
        }

        static string W24Src(string f) { return File.ReadAllText(Path.Combine("/home/dev/tarkov-crosshair-v4/src", f)); }

        static double W24Mean(PilotStyle s)
        {
            ShadeRamp r = PilotCurve.Build(s);
            double m = 0;
            for (int i = 0; i < 256; i++) m += r.Green[i];
            return m / 256.0 / 257.0;
        }

        static void V51W24Tiers()
        {
            // v5.1 round 2 (Leo R5): built-ins are only 通用 and 黑夜模式 again; the early v5.1 tiers are listed as retired
            List<PilotStyle> all = PilotBuiltIns.Create();
            string d = "";
            if (all.Count != 2 || all[0].Name != PilotBuiltIns.GeneralName || all[1].Name != PilotBuiltIns.NightName) d += "built-ins; ";
            foreach (string n in new[] { "白天", "黄昏", "夜图", "室内", "实验室" })
                if (PilotBuiltIns.Find(n) != null || !PilotBuiltIns.IsRetiredV51(n)) d += n + " still built-in / not retired; ";
            if (PilotBuiltIns.IsRetiredV51(PilotBuiltIns.GeneralName) || PilotBuiltIns.IsRetiredV51(PilotBuiltIns.NightName) || PilotBuiltIns.IsRetiredV51(null)) d += "retired list; ";
            foreach (PilotStyle p in all)
            {
                if (!FilterLimit.Fits(p)) d += p.Name + " past the Windows limit " + FilterLimit.Deviation8(p).ToString("0") + "; ";
                if (!FilterTutorialModelShim.EnemySeen(p)) d += p.Name + " dark enemy not seen; ";
            }
            foreach (string f in new[] { "SettingsForm.PageFilter.cs", "FilterTutorialModel.cs" })
            {
                string src = W24Src(f);
                foreach (string n in new[] { "白天", "黄昏", "室内", "实验室" }) if (src.Contains(n)) d += f + " still names " + n + "; ";
            }
            Check("V51-W24-1 presets (R5): built-ins = 通用 + 黑夜模式 only, 白天 / 黄昏 / 夜图 / 室内 / 实验室 retired, inside the Windows limit, no tier names left in the page / tutorial",
                d.Length == 0, d);
        }

        static void V51W24Tune()
        {
            string d = "";
            if (FilterTune.Min != -5 || FilterTune.Max != 5 || FilterTune.Max - FilterTune.Min + 1 != 11) d += "not 11 steps; ";
            foreach (PilotStyle p in PilotBuiltIns.Create())
            {
                double last = -1;
                for (int b = FilterTune.Min; b <= FilterTune.Max; b++)
                {
                    double m = W24Mean(FilterTune.Apply(p, b));
                    if (last >= 0 && m + 1e-9 < last) d += p.Name + " darker at " + b + "; ";
                    last = m;
                }
                if (!FilterTune.Apply(p, 0).SameStyleValues(p)) d += p.Name + " centre; ";
                if (!FilterTune.Apply(p, 99).SameStyleValues(FilterTune.Apply(p, 5))) d += p.Name + " clamp; ";
                string why;
                int f = FilterTune.Fit(p, 5, out why);
                if (f < 0 || f > 5 || !FilterLimit.Fits(FilterTune.Apply(p, f))) d += p.Name + " fit " + f + "; ";
                if ((f == 5) != (why.Length == 0)) d += p.Name + " fit reason; ";
                if (f < 5 && !why.Contains("Windows 允许的最大值")) d += p.Name + " reason text; ";
                if (FilterTune.Fit(p, 0, out why) != 0 || why.Length != 0) d += p.Name + " fit 0; ";
            }
            // a preset past the limit is clamped at the bar with the reason
            PilotStyle x = PilotStyle.CreateNeutral("x"); x.Shadows = 150; x.BlackPoint = 70; x.Midtones = 60; x.ShadowRange = 70;
            string w;
            int fx = FilterTune.Fit(x, 5, out w);
            if (FilterLimit.Fits(FilterTune.Apply(x, 5)) || fx >= 5 || !w.Contains("Windows")) d += "extreme preset not clamped (" + fx + "); ";
            Check("V51-W24-2 调试滤镜: 11 steps -5..+5, brighter to the right, centre = preset, stops at the Windows limit with the reason", d.Length == 0, d);
        }

        static void V51W24Curve()
        {
            string d = "";
            PilotStyle g = PilotBuiltIns.General();
            if (FilterCurvePoints.Count != 5 || string.Join(",", FilterCurvePoints.Names) != "黑位,暗部,中间调,高光,白位") d += "names; ";
            for (int i = 1; i < 5; i++) if (FilterCurvePoints.Inputs[i] <= FilterCurvePoints.Inputs[i - 1]) d += "inputs order; ";
            // every point follows the mouse a little up and down from 通用 (or stops with a reason), and only its own slider changes
            string[] keys = { PilotHelpText.BlackPoint, PilotHelpText.Shadows, PilotHelpText.Midtones, PilotHelpText.Highlights, PilotHelpText.WhitePoint };
            for (int i = 0; i < 5; i++)
            {
                double o = FilterCurvePoints.Output(g, i);
                foreach (int dir in new[] { -6, 6 })
                {
                    string why;
                    PilotStyle s = FilterCurvePoints.Drag(g, i, o + dir, out why);
                    double n = FilterCurvePoints.Output(s, i);
                    bool moved = dir > 0 ? n > o + 0.5 : n < o - 0.5;
                    if (!moved && why.Length == 0) d += FilterCurvePoints.Names[i] + " " + dir + " did not move and gave no reason; ";
                    if (why.Length == 0 && Math.Abs(n - (o + dir)) > 1.5) d += FilterCurvePoints.Names[i] + " " + dir + " missed " + n.ToString("0.0") + "; ";
                    foreach (string k in keys)
                        if (k != keys[i] && SettingsFormShimGet(s, k) != SettingsFormShimGet(g, k)) d += FilterCurvePoints.Names[i] + " changed " + k + "; ";
                    if (!FilterLimit.Fits(s)) d += FilterCurvePoints.Names[i] + " past the limit; ";
                }
            }
            // 暗部 cannot go below its own input when 提亮暗部 is already 0: the reason names the slider and its end
            PilotStyle n0 = PilotStyle.CreateNeutral("n");
            string r0;
            PilotStyle low = FilterCurvePoints.Drag(n0, FilterCurvePoints.Shadow, 10, out r0);
            if (low.Shadows != 0 || !r0.Contains("提亮暗部") || !r0.Contains("最小 0")) d += "shadow floor reason: " + r0 + "; ";
            // 高光 up = less 压低高光: from 通用 (32) dragging up lowers Highlights
            string r1;
            PilotStyle hi = FilterCurvePoints.Drag(g, FilterCurvePoints.High, 255, out r1);
            if (hi.Highlights >= g.Highlights) d += "high point up should lower 压低高光; ";
            // dragging a point far up stops at the Windows limit (黑夜模式 is already near it)
            string r2;
            PilotStyle far = FilterCurvePoints.Drag(PilotBuiltIns.Night(), FilterCurvePoints.Black, 255, out r2);
            if (!FilterLimit.Fits(far) || r2.Length == 0) d += "far drag: fits " + FilterLimit.Fits(far) + " reason '" + r2 + "'; ";
            // readout: input → output for all 5, 0..255
            string ro = FilterCurvePoints.Readout(g);
            foreach (string nm in FilterCurvePoints.Names) if (!ro.Contains(nm + " ")) d += "readout " + nm + "; ";
            if (!ro.Contains("64→" + (int)Math.Round(FilterCurvePoints.Output(g, 1)))) d += "readout value; ";
            // NaN / out-of-range targets are safe
            string r3;
            FilterCurvePoints.Drag(g, 2, double.NaN, out r3);
            FilterCurvePoints.Drag(g, 2, 9999, out r3);
            // speed: one drag step (scan + limit) stays well under a frame on this box
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 20; k++) { string rr; FilterCurvePoints.Drag(PilotBuiltIns.Night(), k % 5, 255, out rr); }
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            if (ms > 40) d += "drag " + ms.ToString("0.0") + " ms; ";
            Check("V51-W24-3 curve: 5 points (黑位 / 暗部 / 中间调 / 高光 / 白位) follow the mouse via their own slider, stop with a Chinese reason (slider end / Windows limit), live readout; drag " + ms.ToString("0.0") + " ms",
                d.Length == 0, d);
        }

        static int SettingsFormShimGet(PilotStyle s, string k)
        {
            if (k == PilotHelpText.BlackPoint) return s.BlackPoint;
            if (k == PilotHelpText.Shadows) return s.Shadows;
            if (k == PilotHelpText.Midtones) return s.Midtones;
            if (k == PilotHelpText.Highlights) return s.Highlights;
            return s.WhitePoint;
        }

        static void V51W24Feed()
        {
            string d = "";
            FilterPrefs p = new FilterPrefs(); p.Normalize(); // F8 toggle, F9 next, F7 hold
            FilterKeyFeed f = new FilterKeyFeed();
            List<string> ev = new List<string>();
            f.Edge = delegate(int a, bool down) { ev.Add(a + (down ? "+" : "-")); };
            HashSet<int> keys = new HashSet<int>();
            Func<int, bool> isDown = delegate(int vk) { return keys.Contains(vk); };
            f.Frame(isDown); // not configured: nothing
            f.Configure(p);
            keys.Add(FilterKeys.VkF8);
            f.Frame(isDown); // first frame primes: a key held at start does not fire
            if (ev.Count != 0) d += "primed frame fired; ";
            keys.Clear(); f.Frame(isDown);
            keys.Add(FilterKeys.VkF8); f.Frame(isDown); f.Frame(isDown);
            keys.Clear(); f.Frame(isDown);
            keys.Add(FilterKeys.VkF7); f.Frame(isDown); keys.Clear(); f.Frame(isDown);
            keys.Add(0x11); keys.Add(FilterKeys.VkF9); f.Frame(isDown); keys.Clear(); f.Frame(isDown); // Ctrl+F9 is not F9 (exact mods)
            keys.Add(FilterKeys.VkF9); f.Frame(isDown);
            if (string.Join(" ", ev) != "0+ 2+ 2- 1+") d += "edges '" + string.Join(" ", ev) + "'; ";
            ev.Clear();
            FilterPrefs none = p.Clone(); none.Vk = new int[3]; none.Mods = new int[3];
            f.Configure(none); keys.Clear(); keys.Add(FilterKeys.VkF8); f.Frame(isDown); keys.Clear(); f.Frame(isDown);
            if (ev.Count != 0) d += "unassigned keys fired; ";
            if (f.Frames != 13) d += "frames " + f.Frames + "; ";
            f.Edge = delegate { throw new InvalidOperationException("x"); };
            f.Configure(p); keys.Clear(); FilterKeyFeed.Current = f;
            FilterKeyFeed.FrameAll(isDown); keys.Add(FilterKeys.VkF8);
            try { FilterKeyFeed.FrameAll(isDown); } catch (Exception) { d += "FrameAll threw; "; }
            FilterKeyFeed.Current = null;
            FilterKeyFeed.FrameAll(isDown);
            // the feed allocates nothing per frame and takes no lock
            string src = W24Src("FilterHotkeys.cs");
            int a0 = src.IndexOf("public void Frame(", StringComparison.Ordinal), a1 = src.IndexOf("public static void FrameAll", StringComparison.Ordinal);
            string body = a0 >= 0 && a1 > a0 ? src.Substring(a0, a1 - a0) : "";
            body = string.Join("\n", body.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).ToArray());
            if (body.Length == 0 || body.Contains(" new ") || body.Contains("lock (")) d += "Frame allocates / locks; ";
            Check("V51-W24-4 filter keys from the overlay's frame: edges only (press; hold press + release), exact modifiers, primes after (re)configure, never throws",
                d.Length == 0, d);
        }

        static void V51W24Source()
        {
            string d = "";
            string host = W24Src("FilterHost.cs"), page = W24Src("SettingsForm.PageFilter.cs"), ctl = W24Src("FilterControls.cs");
            if (!host.Contains("FilterKeyFeed.Current = feed") || !host.Contains("if (feedLive) any = false;") || !host.Contains("GoEventDriven()")) d += "host feed; ";
            if (!host.Contains("Marshal(delegate { OnEdge(a, d); })")) d += "host marshals edges; ";
            foreach (PilotStyle t in PilotBuiltIns.Create()) if (!page.Contains(t.Name)) d += "page tip misses " + t.Name + "; ";
            if (!page.Contains("FilterCurvePoints.Drag(") || !page.Contains("FilterCurvePoints.Readout(") || !page.Contains("FilterTune.Fit(")) d += "page wiring; ";
            if (page.Contains("直方图") || ctl.Contains("直方图") || ctl.Contains("CopyFromScreen") || ctl.Contains("BitBlt")) d += "histogram / capture; ";
            if (!ctl.Contains("PointDragged") || !ctl.Contains("ControlStyles.Selectable")) d += "graph points; ";
            if (page.Contains("+100")) d += "old ±100 bar text; ";
            foreach (string f in new[] { "FilterTune.cs", "FilterControls.cs", "SettingsForm.PageFilter.cs", "FilterTutorialModel.cs" })
            {
                string s = W24Src(f);
                foreach (string bad in new[] { "未实测", "示意", "估计", "起点值" })
                    foreach (string line in s.Split('\n'))
                        if (line.Contains("\"") && line.Contains(bad) && !line.TrimStart().StartsWith("//")) { d += f + " UI text has " + bad + "; "; break; }
            }
            Check("V51-W24-5 sources: host event driven (fallback poll stops once the overlay feeds), page wires curve / readout / limit, all tiers named, no histogram / capture",
                d.Length == 0, d);
        }
    }

    /// <summary>The filter tutorial's "dark enemy seen" rule (FilterTutorialModel is not compiled into tests-v4): same thresholds.</summary>
    internal static class FilterTutorialModelShim
    {
        static int Out(PilotStyle s, int level)
        {
            ShadeRamp r = PilotCurve.Build(s, PilotCurve.IdentityChannel(), PilotCurve.IdentityChannel(), PilotCurve.IdentityChannel(), true);
            int v = (r.Green[level] + 128) / 257;
            return v > 255 ? 255 : v;
        }

        public static bool EnemySeen(PilotStyle s) { return Out(s, 16) - Out(s, 8) >= 13 && Out(s, 16) >= 28; }
    }
}
