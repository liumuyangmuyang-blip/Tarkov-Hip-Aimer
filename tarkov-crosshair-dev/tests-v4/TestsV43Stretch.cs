// v4.3 (W6) 拉伸: every screen gives the same 百分比偏移 for a stretched 画面比例 (dashboard live % / log / HUD, settings pages,
// tutorial labels, step 10 first-person view), the dashboard's live state follows resolution / 画面比例 / FOV changes, and the
// dashboard's 滤镜 line. Cases: 4:3 on 16:9, 16:10 on 16:9, 21:9 native, 16:9 native. The stretch model itself is W1's
// (UNVERIFIED in game).
using System;
using System.Globalization;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V43StretchInit() { V43StretchTests(); }

        /// <summary>The app's src folder: walks up from the test binary to <ancestor>/../tarkov-crosshair-v4/src.</summary>
        static string V43Src()
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && d != null; i++)
            {
                string c = System.IO.Path.Combine(d, "..", "tarkov-crosshair-v4", "src");
                if (System.IO.File.Exists(System.IO.Path.Combine(c, "DashboardPanel.cs"))) return System.IO.Path.GetFullPath(c);
                d = System.IO.Path.GetDirectoryName(d.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            }
            return "/home/dev/tarkov-crosshair-v4/src";
        }

        static GlobalSettings V43G(double w, double h, string aspect, double fov)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.ShowFadeMs = 0; g.HideFadeMs = 0; g.OnlyWhenGameFocused = false; g.Enabled = true;
            g.HandEnabled = true; g.BoostEnabled = false;
            g.ResGameW = w; g.ResGameH = h; g.ResAspect = aspect; g.ResFov = fov;
            g.Validate();
            g.ResGameW = w; g.ResGameH = h; g.ResAspect = aspect; g.ResFov = fov;
            return g;
        }

        /// <summary>A copy with the game screen = the 调参 screen (what the tutorial demo engines run on): OffsetX = profile px.</summary>
        static GlobalSettings V43Demo(GlobalSettings g)
        {
            GlobalSettings d = g.Clone(false);
            d.ResGameW = d.ResBaseW; d.ResGameH = d.ResBaseH; d.ResAspect = d.ResBaseAspect; d.ResFov = d.ResBaseFov;
            return d;
        }

        /// <summary>Holds the keys for 'seconds' at 120 fps and returns the engine.</summary>
        static MotionEngine V43Run(Profile p, GlobalSettings g, bool right, bool left, bool leanR, bool fwd, double seconds)
        {
            MotionEngine e = new MotionEngine();
            e.GameFocused = true; e.OwnForeground = true;
            InputState i = new InputState();
            i.Right = right; i.Left = left; i.LeanR = leanR; i.Fwd = fwd;
            int n = (int)(seconds * 120);
            for (int k = 0; k < n; k++) e.Update(1.0 / 120, i, p, g, PreviewMode.Live, 1.0);
            return e;
        }

        static void V43StretchTests()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            // the 4 setups Leo asked for (FOV 75 and 60 each), with the expected X stretch
            string[] names = { "4:3 on 1920x1080", "16:10 on 1920x1080", "21:9 native 2560x1080", "16:9 native 1920x1080" };
            double[] ws = { 1920, 1920, 2560, 1920 }, hs = { 1080, 1080, 1080, 1080 };
            string[] asp = { "4:3", "16:10", "21:9", "16:9" };
            double[] sx = { (16.0 / 9.0) / (4.0 / 3.0), (16.0 / 9.0) / 1.6, 1.0, 1.0 };

            // S1 ratio = FactorX ÷ Factor = the expected stretch; dashboard and step 10 agree with it and with the 常规 page's kx / k
            {
                string d = "";
                for (int c = 0; c < 4; c++)
                    foreach (double fov in new double[] { 75, 60 })
                    {
                        GlobalSettings g = V43G(ws[c], hs[c], asp[c], fov);
                        double r = DashStretch.Ratio(g), r10 = TutorialStep10Math.ViewStretch(g), page = g.ResolutionScaleX / g.ResolutionScale;
                        if (Math.Abs(r - sx[c]) > 1e-9 || r10 != r || Math.Abs(page - r) > 1e-12) d += names[c] + "@" + fov + " r=" + r.ToString("0.0000", inv) + " r10=" + r10 + "; ";
                        if (c >= 2 && r != 1.0) d += names[c] + " not exactly 1; ";
                    }
                Check("V43-S1 stretch factor: 4:3 on 16:9 ×1.333, 16:10 on 16:9 ×1.111, 21:9 and 16:9 native exactly ×1 (dashboard = step 10 = 常规 page kx/k)", d.Length == 0, d);
            }

            // S2 the same offset gives the same number on every screen: settings page (profile px through OffsetUnits), tutorial
            // labels (TutorialFormat), dashboard live % (DashStretch on the real screen px), its log line, stream sample and HUD.
            {
                string d = "";
                Profile p = Profile.CreateLeoDefault("t");
                bool[][] keys = { new bool[] { true, false, false, false }, new bool[] { true, false, true, false }, new bool[] { false, true, false, true } };
                for (int c = 0; c < 4; c++)
                    foreach (double fov in new double[] { 75, 60 })
                        for (int kk = 0; kk < keys.Length; kk++)
                        {
                            GlobalSettings g = V43G(ws[c], hs[c], asp[c], fov);
                            bool[] ks = keys[kk];
                            MotionEngine real = V43Run(p, g, ks[0], ks[1], ks[2], ks[3], 2.0);
                            MotionEngine demo = V43Run(p, V43Demo(g), ks[0], ks[1], ks[2], ks[3], 2.0);
                            double profX = demo.OffsetX, profYUp = -demo.OffsetY;           // profile px (调参 screen)
                            double scrX = real.OffsetX, scrYUp = -real.OffsetY;             // real screen px
                            string tag = names[c] + "@" + fov + " keys" + kk;
                            if (Math.Abs(profX) < 1) { d += tag + " no X offset; "; continue; }
                            // the ring really moves ×stretch on the screen
                            double kY = g.ResolutionScale;
                            // v5.1.1 (X5, Leo 21 Nov 2026): plus the FOV side rule on X (normal hand)
                            double wantX = ViewScale.FovX(profX * kY * sx[c], ViewScale.FovRatio(g), 0);
                            if (Math.Abs(scrX - wantX) > 1e-6 * Math.Max(1, Math.Abs(scrX))) d += tag + " screen X " + scrX + " vs " + wantX + "; ";
                            string page = OffsetUnits.Format(profX, OffsetAxis.X, g), pageY = OffsetUnits.Format(profYUp, OffsetAxis.Y, g);
                            string tut = TutorialPct.X(profX, g), tutY = TutorialPct.Y(profYUp, g);
                            double dashPct = DashStretch.ScreenXToPct(scrX, g), dashPctC = DashStretch.ScreenXToPct(scrX, g, DashStretch.Ratio(g));
                            string dash = OffsetUnits.FormatPct(dashPct, OffsetAxis.X), dashY = OffsetUnits.FormatPct(OffsetUnits.ScreenToPct(scrYUp, g), OffsetAxis.Y);
                            if (page != tut || page != dash || dashPct != dashPctC) d += tag + " X page " + page + " tut " + tut + " dash " + dash + "; ";
                            if (pageY != tutY || pageY != dashY) d += tag + " Y page " + pageY + " tut " + tutY + " dash " + dashY + "; ";
                            string line = DashFormat.StateLine(DashKeys.D, DashKeys.Names, scrX, scrYUp, 700, g);
                            if (line.IndexOf("X " + page, StringComparison.Ordinal) < 0 || line.IndexOf("Y " + pageY, StringComparison.Ordinal) < 0) d += tag + " log " + line + "; ";
                            string sm = DashStream.Sample(0.5, scrX, scrYUp, g);
                            if (sm.IndexOf(page, StringComparison.Ordinal) < 0) d += tag + " stream " + sm + "; ";
                            DashMathText m = new DashMathText();
                            m.UpdateB(scrX, scrYUp, dashPct, OffsetUnits.ScreenToPct(scrYUp, g), g.ResolutionScale, OffsetUnits.HalfHeight(g), g.ResBaseFov, g.ResFov, DashStretch.Ratio(g));
                            if (m.B[1].Text.IndexOf(page, StringComparison.Ordinal) < 0) d += tag + " HUD " + m.B[1].Text + "; ";
                            for (int b = 0; b < DashMathText.BLines; b++) if (m.B[b].Text.Length > 25) d += tag + " HUD line " + b + " too long: " + m.B[b].Text + "; ";
                            bool st = sx[c] != 1.0;
                            if (st != (m.B[0].Text.IndexOf("X÷", StringComparison.Ordinal) >= 0)) d += tag + " HUD formula " + m.B[0].Text + "; ";
                            if (!st && (m.B[0].Text != DashMathText.BFormula || !m.B[3].Text.StartsWith("k = ", StringComparison.Ordinal))) d += tag + " native HUD changed; ";
                        }
                Check("V43-S2 4 setups × 2 FOVs × 3 key states: settings page = tutorial label = dashboard live X/Y % = log line = stream = HUD; ring moves ×stretch on screen; HUD lines ≤ 25 chars", d.Length == 0, d);
            }

            // S3 native setups keep the v4.1 dashboard numbers bit for bit (no stretch = plain ScreenToPct).
            {
                string d = "";
                Random rnd = new Random(43);
                for (int c = 2; c < 4; c++)
                    for (int i = 0; i < 2000; i++)
                    {
                        GlobalSettings g = V43G(ws[c], hs[c], asp[c], 50 + rnd.Next(26));
                        double x = (rnd.NextDouble() - 0.5) * 800;
                        if (DashStretch.ScreenXToPct(x, g) != OffsetUnits.ScreenToPct(x, g)) { d += names[c] + " x=" + x + "; "; break; }
                    }
                Check("V43-S3 21:9 / 16:9 native: dashboard X % identical to v4.1 (4000 random offsets / FOVs)", d.Length == 0, d);
            }

            // S4 live: changing 画面比例, resolution or FOV changes what the dashboard signature reads (Ratio / HalfHeight / Factor),
            // so the panel's 0.5 s CheckSettings re-renders the chart and every % (no restart).
            {
                string d = "";
                GlobalSettings g = V43G(1920, 1080, "16:9", 75);
                double r0 = DashStretch.Ratio(g), k0 = g.ResolutionScale, h0 = OffsetUnits.HalfHeight(g);
                g.ResAspect = "4:3";
                if (DashStretch.Ratio(g) == r0) d += "aspect; ";
                g.ResAspect = "16:9"; g.ResGameW = 1440; // 4:3 resolution, 16:9 picture → ×0.75
                if (Math.Abs(DashStretch.Ratio(g) - 0.75) > 1e-9) d += "res " + DashStretch.Ratio(g) + "; ";
                g.ResGameW = 1920; g.ResFov = 60;
                // v5.1.1 (X5, Leo 21 Nov 2026): FOV no longer changes k; it changes the FOV ratio the panel signature also mixes in
                if (g.ResolutionScale != k0 || ViewScale.FovRatio(g) == 1.0 || DashStretch.Ratio(g) != r0 || OffsetUnits.HalfHeight(g) != h0) d += "fov; ";
                Check("V43-S4 live: 画面比例 / resolution / FOV changes each change the dashboard's inputs (ratio, k)", d.Length == 0, d);
            }

            // S5 step 10 first-person view: scene points and the ring are stretched by the same factor, so the ring stays on the
            // point it aims at; s = 1 is exactly the v4.1 expression.
            {
                string d = "";
                double vcx = 400, k = 0.85;
                foreach (double s in new double[] { 1.0, 4.0 / 3.0, 10.0 / 9.0, 0.75 })
                    for (double off = -120; off <= 120; off += 7.5)
                    {
                        double scene = TutorialStep10Math.StretchAbout(vcx + off * k, vcx, s);
                        double ring = TutorialStep10Math.RingViewX(vcx, off, k, s);
                        if (Math.Abs(scene - ring) > 1e-9) d += "s=" + s + " off=" + off + "; ";
                        if (s == 1.0 && (ring != vcx + off * k || scene != vcx + off * k)) d += "s=1 changed; ";
                    }
                if (TutorialStep10Math.StretchTagText(4.0 / 3.0) != "画面拉伸 · 左右 ×1.33") d += "tag " + TutorialStep10Math.StretchTagText(4.0 / 3.0) + "; ";
                if (TutorialStep10Math.ViewStretch(null) != 1.0) d += "null; ";
                Check("V43-S5 step 10 first-person: scene and ring stretched alike (ring stays on target), ×1 unchanged, tag 「画面拉伸 · 左右 ×1.33」", d.Length == 0, d);
            }

            // S6 dashboard 滤镜 line from FilterHost's state; a new string only on change.
            {
                string d = "";
                DashFilterLine f = new DashFilterLine();
                if (f.Text != "滤镜：未启动") d += "init " + f.Text + "; ";
                if (!f.Update(true, true, "通用") || f.Text != "滤镜：开 · 通用") d += "on " + f.Text + "; ";
                string same = f.Text;
                if (f.Update(true, true, "通用") || !ReferenceEquals(same, f.Text)) d += "no-change allocated; ";
                if (!f.Update(true, false, "黑夜模式") || f.Text != "滤镜：关 · 黑夜模式") d += "off " + f.Text + "; ";
                if (!f.Update(false, false, "通用") || f.Text != "滤镜：未启动") d += "stopped " + f.Text + "; ";
                if (f.Update(false, true, "黑夜模式")) d += "stopped changed; ";
                if (DashFilterLine.Format(true, true, null) != "滤镜：开") d += "no preset; ";
                if (DashFilterLine.Widest.Length < "滤镜：开 · 通用".Length) d += "widest; ";
                // S8 (Leo) step 10 拉枪 at 5-10 m: every scene still reaches contact, the peeker is hidden before the keys,
                // shows only a little body at contact, and the ring (real engine, real settings) is already offset when we peek
                string q = "";
                for (int sc = 0; sc < TutorialStep10Peek.Scenes; sc++)
                {
                    double dist = Math.Sqrt(Math.Pow(TutorialStep10Peek.FoeX[sc] - TutorialStep10Peek.MeX[sc], 2) + Math.Pow(TutorialStep10Peek.FoeZ[sc] - TutorialStep10Peek.MeZ[sc], 2));
                    if (dist < 5 || dist > 10) q += "s" + sc + " dist " + dist + "; ";
                    TutorialStep10Peek p = new TutorialStep10Peek();
                    p.Configure(null, null);
                    p.Start(sc);
                    p.AdvanceTo(TutorialStep10Peek.KeysAt - 10);
                    if (p.ExposureM > 1e-9 || p.Visible) q += "s" + sc + " exposed before keys " + p.ExposureM + "; ";
                    p.AdvanceTo(TutorialStep10Peek.SceneMs);
                    if (p.ContactMs < 0) { q += "s" + sc + " no contact; "; continue; }
                    if (!(p.ContactExposureM >= 0 && p.ContactExposureM < 0.5)) q += "s" + sc + " exposure at contact " + p.ContactExposureM + "; ";
                    if (TutorialStep10Peek.MeSide[sc] != 0 && !(Math.Abs(p.ContactRingOffX) > 1)) q += "s" + sc + " ring not offset " + p.ContactRingOffX + "; ";
                    if (p.Shots < 1) q += "s" + sc + " no shots; ";
                }
                if (TutorialStep10Math.ExposureText(0.153, "← 4.17%", true) != "你只露出 0.15 米 · 准星已偏 ← 4.17%") q += "text " + TutorialStep10Math.ExposureText(0.153, "← 4.17%", true) + "; ";
                Check("V43-S8 step 10 拉枪 enemy 5-10 m: hidden before the keys, contact in every scene, < 0.5 m of body showing at contact, ring already offset", q.Length == 0, q);
                // S9 (Leo) the dashboard memory read-out is only 「内存 24.3 MB」: no 「很小，不影响游戏」 or other comment next to it
                {
                    string m9 = "";
                    string src = System.IO.File.ReadAllText(System.IO.Path.Combine(V43Src(), "DashboardPanel.cs"));
                    if (src.IndexOf("MemNote", StringComparison.Ordinal) >= 0) m9 += "MemNote still there; ";
                    if (src.IndexOf("很小", StringComparison.Ordinal) >= 0 || src.IndexOf("不影响游戏\"", StringComparison.Ordinal) >= 0) m9 += "comment text; ";
                    if (src.IndexOf("MemLabel = \"内存\";", StringComparison.Ordinal) < 0) m9 += "label not 「内存」; ";
                    Check("V43-S9 dashboard memory line shows only 「内存 <number> MB」 (MemNote removed)", m9.Length == 0, m9);
                }
                string w = "";
                // S7 (V43-PERF) the timer may run only when the page and window are shown, not minimised, our app in front
                if (!DashWake.ShouldRun(true, true, true, false, 1, false, false, false)) w += "wake ours; ";
                if (!DashWake.ShouldRun(true, true, true, false, -1, false, false, false)) w += "wake unknown fg; ";
                if (DashWake.ShouldRun(true, true, true, false, 0, false, false, false)) w += "game in front runs; ";
                if (DashWake.ShouldRun(false, true, true, false, 1, false, false, false)) w += "hidden page runs; ";
                if (DashWake.ShouldRun(true, true, false, false, 1, false, false, false)) w += "hidden window runs; ";
                if (DashWake.ShouldRun(true, true, true, true, 1, false, false, false)) w += "minimised runs; ";
                if (DashWake.ShouldRun(true, false, true, false, 1, false, false, false)) w += "no handle runs; ";
                if (DashWake.ShouldRun(true, true, true, false, 1, true, false, false)) w += "drag runs; ";
                if (DashWake.ShouldRun(true, true, true, false, 1, false, true, false) || DashWake.ShouldRun(true, true, true, false, 1, false, false, true)) w += "failed/disposed runs; ";
                Check("V43-S7 dashboard timer stops when the game (another program) is in front, the page/window is hidden or minimised; runs again when shown + in front", w.Length == 0, w);
                                Check("V43-S6 dashboard 滤镜 line: 开 · 通用 / 关 · 黑夜模式 / 未启动, no new string without a change", d.Length == 0, d);
            }
        }
    }
}
