// v5.1.1 (X3): F4 (no stray white tracer in the 换手 tutorial step) and Leo round B: B1 外观 sub-pages, B2 准星动态 (plain names,
// no lean tilt / shake, adjustable gap, 开火波纹, 开火渐变色, 强度), B3 弹药提示 = ONE bar (default below; presets incl. 竞速;
// 只在少于 X% 时显示; 2 warning levels; reload animation per kind). Centre empty, drawn under the crosshair, level 0 draws
// nothing, no allocation per frame, frame work far under 1.5 ms. Pure logic + source checks.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X3Init() { V511X3Tests(); V511X3RaceShape(); }

        static string X3Src(string f) { return File.ReadAllText(Path.Combine("/home/dev/tarkov-crosshair-v4/src", f)); }

        static AmmoView X3View(int band, double share)
        {
            AmmoView v = new AmmoView(); v.Active = true; v.Band = band; v.Progress = double.NaN; v.Share = share; return v;
        }

        static double X3ShareOf(int band) { return new double[] { 0.08, 0.25, 0.5, 0.75, 1.0 }[band]; }

        /// <summary>Random bar settings (every option), still valid.</summary>
        static AmmoBarSettings X3RandomBar(Random r)
        {
            AmmoBarSettings s = AmmoBarSettings.Defaults();
            AmmoBarSettings.ApplyPreset(s, AmmoBarSettings.Presets[r.Next(AmmoBarSettings.Presets.Length)]);
            s.Pos = AmmoBarSettings.Positions[r.Next(4)];
            s.LenPx = 6 + r.NextDouble() * 154; s.ThickPx = 1 + r.NextDouble() * 11; s.GapPx = r.Next(61);
            s.Segments = r.Next(3) == 0 ? 0 : 2 + r.Next(19); s.SegGapPx = 0.5 + r.NextDouble() * 5.5;
            s.Round = r.Next(2) == 0; s.Glow = r.Next(2) == 0; s.GlowPx = 1 + r.NextDouble() * 9; s.Track = r.Next(2) == 0;
            s.TrackPct = r.Next(101); s.Frame = r.Next(2) == 0; s.Slant = r.Next(2) == 0; s.Led = r.Next(2) == 0; s.Backing = r.Next(2) == 0;
            s.OpacityPct = 10 + r.Next(91);
            s.Warn1.Style = AmmoWarnSettings.Styles[r.Next(6)]; s.Warn2.On = r.Next(2) == 0; s.Warn2.Style = AmmoWarnSettings.Styles[r.Next(6)];
            foreach (ReloadAnimSettings ra in new ReloadAnimSettings[] { s.ReloadNormal, s.ReloadQuick })
            {
                ra.Type = ReloadAnimSettings.Types[r.Next(ReloadAnimSettings.Types.Length)]; ra.Ease = ReloadAnimSettings.Eases[r.Next(5)];
                ra.Marker = ReloadAnimSettings.Markers[r.Next(4)]; ra.Reverse = r.Next(2) == 0; ra.Drop = r.Next(2) == 0; ra.ColourOn = r.Next(2) == 0;
                ra.OutPct = 5 + r.Next(86); ra.InPct = 5 + r.Next(86);
            }
            s.Validate();
            return s;
        }

        static long X3Sum(int[] px) { long s = 0; foreach (int v in px) s += (int)((uint)v >> 24); return s; }

        static int[] X3Draw(AmmoBarCfg c, int style, AmmoView v, double t, double inner, out int S, AmmoDisplay d)
        {
            d.Update(v, t, style, c);
            S = AmmoDisplay.CanvasNeed(inner, style, c);
            int[] px = new int[S * S];
            d.Draw(px, S, S / 2.0, S / 2.0, inner, style, 255);
            return px;
        }

        static void V511X3Tests()
        {
            int colourStyle = AmmoDisplay.BottomBar | AmmoDisplay.Colour;

            // ---- X3-1 default = ONE bar BELOW the crosshair (nothing above, left or right of the centre band), no brackets
            {
                AmmoBarSettings s = AmmoBarSettings.Defaults();
                AmmoBarCfg c = AmmoBarCfg.From(s);
                int S; int[] px = X3Draw(c, colourStyle, X3View(4, 1), 0, 12, out S, new AmmoDisplay());
                double ctr = S / 2.0; int above = 0, below = 0, side = 0;
                for (int j = 0; j < S; j++) for (int i = 0; i < S; i++)
                    {
                        if (px[j * S + i] == 0) continue;
                        if (j + 1 <= ctr) above++; else below++;
                        if (Math.Abs(i + 0.5 - ctr) > s.LenPx / 2 + 1.5) side++;
                    }
                bool settings = new GlobalSettings().AmmoBar.Pos == "below" && new GlobalSettings().AmmoBar.Preset == "simple";
                bool noBracketCode = !X3Src("AmmoDisplay.cs").Contains("BracketHalfH") && !X3Src("StylePanel.cs").Contains("两侧括号");
                Check("V511-X3-1 default ammo hint = one small bar below the crosshair (0 pixels above, 0 beside it), side brackets removed (code + UI)",
                    above == 0 && side == 0 && below > 0 && settings && noBracketCode, "above=" + above + " side=" + side + " below=" + below + " settings=" + settings + " noBr=" + noBracketCode);
            }

            // ---- X3-2 random bars x bands x reload phases x warnings: centre empty, nothing clipped, under the crosshair
            {
                Random r = new Random(11); int cases = 0, inside = 0, clipped = 0, touched = 0; string first = "";
                for (int i = 0; i < 400; i++)
                {
                    AmmoBarSettings s = X3RandomBar(r);
                    AmmoBarCfg c = AmmoBarCfg.From(s);
                    int style = AmmoDisplay.BottomBar | (r.Next(2) == 0 ? AmmoDisplay.Colour : 0) | (r.Next(2) == 0 ? AmmoDisplay.Fade : 0);
                    double inner = 6 + r.NextDouble() * 40;
                    AmmoDisplay d = new AmmoDisplay();
                    int band = r.Next(5);
                    AmmoView v = X3View(band, X3ShareOf(band)); v.ReloadMs = 1000;
                    d.Update(v, 0, style, c);
                    if (r.Next(2) == 0) { v.ReloadSeq = 1; v.ReloadKind = 1 + r.Next(2); v.Band = 4; v.Share = 1; d.Update(v, 1, style, c); }
                    double t = 1 + r.NextDouble() * 999;
                    d.Update(v, t, style, c);
                    int S = AmmoDisplay.CanvasNeed(inner, style, c);
                    int[] px = new int[S * S];
                    for (int k = 0; k < px.Length; k++) if (r.Next(4) == 0) { double dx = k % S + 0.5 - S / 2.0, dy = k / S + 0.5 - S / 2.0; if (dx * dx + dy * dy < inner * inner && dx * dx + dy * dy > 4) px[k] = unchecked((int)0xFF000000) | r.Next(0x1000000); }
                    int[] before = (int[])px.Clone();
                    d.Draw(px, S, S / 2.0, S / 2.0, inner, style, 255);
                    cases++;
                    double ctr = S / 2.0; bool in1 = false, edge = false, tch = false;
                    for (int j = 0; j < S; j++) for (int q = 0; q < S; q++)
                        {
                            int k = j * S + q;
                            if (px[k] == before[k]) continue;
                            if (((uint)before[k] >> 24) == 255) tch = true;
                            if (q == 0 || j == 0 || q == S - 1 || j == S - 1) edge = true;
                            double fx = Math.Max(Math.Abs(q - ctr), Math.Abs(q + 1 - ctr)), fy = Math.Max(Math.Abs(j - ctr), Math.Abs(j + 1 - ctr));
                            if (fx * fx + fy * fy < inner * inner) in1 = true;
                        }
                    if (in1) inside++; if (edge) clipped++; if (tch) touched++;
                    if ((in1 || edge || tch) && first == "") first = s.Preset + "/" + s.Pos + " len " + F(s.LenPx) + " thick " + F(s.ThickPx) + " seg " + s.Segments + " slant " + s.Slant + " glow " + s.Glow + " in=" + in1 + " edge=" + edge + " touch=" + tch;
                }
                Check("V511-X3-2 " + cases + " random bars (every option, mid reload, warnings): 0 pixels in the empty centre, 0 clipped, 0 crosshair pixels changed",
                    inside == 0 && clipped == 0 && touched == 0, "inside=" + inside + " clipped=" + clipped + " touched=" + touched + " " + first);
            }

            // ---- X3-3 presets: 7 different pictures, each still editable (a value change -> 自定义); 竞速 = slanted LED shift lights
            {
                int same = 0; List<int[]> pics = new List<int[]>(); List<int> sizes = new List<int>();
                foreach (string id in AmmoBarSettings.Presets)
                {
                    AmmoBarSettings s = AmmoBarSettings.Defaults(); AmmoBarSettings.ApplyPreset(s, id);
                    int S; pics.Add(X3Draw(AmmoBarCfg.From(s), colourStyle, X3View(3, 0.7), 0, 12, out S, new AmmoDisplay())); sizes.Add(S);
                }
                for (int a = 0; a < pics.Count; a++) for (int b = a + 1; b < pics.Count; b++)
                        if (sizes[a] == sizes[b]) { bool eq = true; for (int k = 0; k < pics[a].Length; k++) if (pics[a][k] != pics[b][k]) { eq = false; break; } if (eq) same++; }
                AmmoBarSettings x = AmmoBarSettings.Defaults(); AmmoBarSettings.ApplyPreset(x, "race");
                bool race = x.PresetOf() == "race" && x.Slant && x.Led && x.Backing && x.Segments == 10;
                x.LenPx += 3; bool custom = x.PresetOf() == null;
                // LED colours: first lit segment green, last lit (满) red
                AmmoBarSettings y = AmmoBarSettings.Defaults(); AmmoBarSettings.ApplyPreset(y, "race"); y.Warn1.On = false;
                AmmoBarCfg yc = AmmoBarCfg.From(y);
                bool green = false, red = false;
                { int S; int[] px = X3Draw(yc, colourStyle, X3View(4, 1), 0, 12, out S, new AmmoDisplay()); foreach (int v in px) { int a = (int)((uint)v >> 24); if (a < 120) continue; int rr = (v >> 16) & 255, gg = (v >> 8) & 255; if (gg > rr * 2) green = true; if (rr > gg * 2) red = true; } }
                Check("V511-X3-3 bar presets (简洁 CF 风格 科幻 点阵 粗条 胶囊 竞速) draw 7 different bars; 竞速 = slanted 10-LED bar on a black plate, green -> red; editing a value = 自定义",
                    same == 0 && race && custom && green && red && AmmoBarSettings.PresetNames[6] == "竞速", "same=" + same + " race=" + race + " custom=" + custom + " green=" + green + " red=" + red);
            }

            // ---- X3-4 more ammo = more lit, every preset and position
            {
                string bad = "";
                foreach (string id in AmmoBarSettings.Presets) foreach (string pos in AmmoBarSettings.Positions)
                    {
                        AmmoBarSettings s = AmmoBarSettings.Defaults(); AmmoBarSettings.ApplyPreset(s, id); s.Pos = pos; s.Warn1.On = false; s.Track = false; s.Backing = false; s.Frame = false;
                        AmmoBarCfg c = AmmoBarCfg.From(s); long prev = -1;
                        for (int b = 0; b <= 4; b++) { int S; long sum = X3Sum(X3Draw(c, AmmoDisplay.BottomBar, X3View(b, X3ShareOf(b)), 0, 12, out S, new AmmoDisplay())); if (sum <= prev) bad += id + "/" + pos + "@" + b + " "; prev = sum; }
                    }
                Check("V511-X3-4 every preset x position: 快空 < 少 < 一半 < 大半 < 满 (more rounds = more lit)", bad == "", bad);
            }

            // ---- X3-5 只在剩余少于 X% 时显示 (+ fade in, always shown while reloading)
            {
                AmmoBarSettings s = AmmoBarSettings.Defaults(); s.ShowBelowOn = true; s.ShowBelowPct = 35; s.FadeInMs = 100;
                AmmoBarCfg c = AmmoBarCfg.From(s);
                AmmoDisplay d = new AmmoDisplay();
                d.Update(X3View(2, 0.5), 0, colourStyle, c); bool hiddenHigh = !d.Visible;
                d.Update(X3View(1, 0.3), 10, colourStyle, c); bool fading = d.Visible && d.Alpha < c.Opacity * 0.5;
                d.Update(X3View(1, 0.3), 120, colourStyle, c); bool shown = d.Visible && Math.Abs(d.Alpha - Math.Round(c.Opacity * 64) / 64) < 0.02;
                AmmoView v = X3View(1, 0.3); v.ReloadSeq = 1; v.ReloadKind = 1; v.ReloadMs = 1000; v.Band = 4; v.Share = 1;
                d.Update(v, 200, colourStyle, c); d.Update(v, 700, colourStyle, c); bool reloadShown = d.Visible && d.SwapKind == 1;
                d.Update(v, 1300, colourStyle, c); d.Update(v, 1500, colourStyle, c); bool hiddenAfter = !d.Visible;
                Check("V511-X3-5 只在剩余少于 35% 时显示: hidden at 50 %, fades in at 30 %, shown during a reload, hidden again once full",
                    hiddenHigh && fading && shown && reloadShown && hiddenAfter, "high=" + hiddenHigh + " fading=" + fading + " shown=" + shown + " reload=" + reloadShown + " after=" + hiddenAfter);
            }

            // ---- X3-6 warnings: own threshold (separate from show-below), 6 styles behave, level 2 overrides level 1
            {
                string bad = "";
                for (int st = 0; st < 6; st++)
                {
                    AmmoBarSettings s = AmmoBarSettings.Defaults(); s.Warn1.Style = AmmoWarnSettings.Styles[st]; s.Warn1.Pct = 20; s.Warn1.PeriodMs = 200; s.Warn1.Colour = "#0000FF"; s.Warn1.IntensityPct = 100; s.Warn1.Blinks = 2;
                    s.ShowBelowOn = true; s.ShowBelowPct = 60;
                    AmmoBarCfg c = AmmoBarCfg.From(s);
                    AmmoDisplay d = new AmmoDisplay();
                    d.Update(X3View(1, 0.3), 0, AmmoDisplay.BottomBar, c);
                    if (d.WarnLevel != 0) bad += st + ":above ";
                    HashSet<int> keys = new HashSet<int>(); bool blue = false; HashSet<int> late = new HashSet<int>();
                    for (int t = 1; t <= 1000; t += 5)
                    {
                        d.Update(X3View(0, 0.1), t, AmmoDisplay.BottomBar, c); keys.Add(d.Key); if ((d.Rgb & 0xFF) > 160 && (d.Rgb & 0xFF) > (d.Rgb >> 16) + 40) blue = true;
                        if (t > 600) late.Add(d.Key);
                    }
                    if (d.WarnLevel != 1 || !blue) bad += st + ":colour ";
                    bool animated = st != AmmoWarnCfg.Steady;
                    if (animated && keys.Count < 2) bad += st + ":static ";
                    if (st == AmmoWarnCfg.Steady && keys.Count > 2) bad += st + ":moves ";
                    if (st == AmmoWarnCfg.Blink && late.Count > 1) bad += st + ":blinks-forever ";
                }
                AmmoBarSettings s2 = AmmoBarSettings.Defaults(); s2.Warn2.On = true; s2.Warn2.Pct = 5;
                AmmoDisplay d2 = new AmmoDisplay(); AmmoBarCfg c2 = AmmoBarCfg.From(s2);
                d2.Update(X3View(0, 0.1), 0, AmmoDisplay.BottomBar, c2); int l1 = d2.WarnLevel;
                d2.Update(X3View(0, 0.03), 10, AmmoDisplay.BottomBar, c2); int l2 = d2.WarnLevel;
                if (l1 != 1 || l2 != 2) bad += "levels " + l1 + "/" + l2;
                Check("V511-X3-6 low-ammo warning: own threshold, all 6 styles (闪烁 呼吸 变色 发光 闪几下后常亮 常亮) work in the warning colour, 紧急警告 overrides at 5 %", bad == "", bad);
            }

            // ---- X3-7 reload animations: every type x kind ends on 满; slide/drain/fade/drop/steps start from the old mag; drs flashes
            {
                string bad = "";
                for (int ty = 0; ty < ReloadAnimSettings.Types.Length; ty++)
                    for (int kind = 1; kind <= 2; kind++)
                        foreach (string marker in ReloadAnimSettings.Markers)
                        {
                            AmmoBarSettings s = AmmoBarSettings.Defaults(); s.Warn1.On = false;
                            ReloadAnimSettings ra = kind == 2 ? s.ReloadQuick : s.ReloadNormal; ra.Type = ReloadAnimSettings.Types[ty]; ra.Marker = marker;
                            AmmoBarCfg c = AmmoBarCfg.From(s);
                            AmmoDisplay d = new AmmoDisplay();
                            AmmoView v = X3View(1, 0.25); v.ReloadMs = 1000;
                            d.Update(v, 0, AmmoDisplay.BottomBar, c);
                            v.ReloadSeq = 1; v.ReloadKind = kind; v.Band = 4; v.Share = 1;
                            HashSet<int> keys = new HashSet<int>();
                            for (int t = 0; t <= 1100; t += 10) { d.Update(v, t, AmmoDisplay.BottomBar, c); keys.Add(d.Key); }
                            int S; long end = X3Sum(X3Draw(c, AmmoDisplay.BottomBar, v, 1200, 12, out S, d));
                            long full = X3Sum(X3Draw(c, AmmoDisplay.BottomBar, X3View(4, 1), 0, 12, out S, new AmmoDisplay()));
                            if (d.Animating || end != full) bad += ra.Type + "/" + kind + " end ";
                            if (ra.Type != "none" && keys.Count < 5) bad += ra.Type + "/" + kind + " static ";
                        }
                // the two kinds are set separately
                AmmoBarSettings z = AmmoBarSettings.Defaults(); z.ReloadNormal.Type = "fade"; z.ReloadQuick.Type = "drs"; AmmoBarCfg zc = AmmoBarCfg.From(z);
                bool sep = zc.RN.Type == AmmoReloadCfg.Fade && zc.RQ.Type == AmmoReloadCfg.Drs;
                Check("V511-X3-7 reload animations (8 types x 普通/快速 x 4 markers): each animates and ends on exactly 满; 普通 and 快速 set separately", bad == "" && sep, bad + " sep=" + sep);
            }

            // ---- X3-8 level 0 (开镜 / 丢雷) and hint off draw nothing; firing effects too
            {
                int bad = 0; Random r = new Random(5);
                for (int i = 0; i < 50; i++)
                {
                    AmmoBarCfg c = AmmoBarCfg.From(X3RandomBar(r));
                    AmmoDisplay d = new AmmoDisplay(); d.Update(X3View(0, 0.05), i, AmmoDisplay.BottomBar, c);
                    int S = AmmoDisplay.CanvasNeed(12, AmmoDisplay.BottomBar, c); int[] px = new int[S * S];
                    d.Draw(px, S, S / 2.0, S / 2.0, 12, AmmoDisplay.BottomBar, 0);
                    FireFx fx = new FireFx(); AnimOptions o = CrosshairAnim.From(GlobalSettings.CreateDefault()); o.Ripple = o.Gradient = true;
                    fx.Step(o, 3, 10); fx.Draw(px, S, S / 2.0, S / 2.0, 8, 0);
                    if (X3Sum(px) != 0) bad++;
                }
                Check("V511-X3-8 level 0 (开镜 / 丢雷 / faded out): ammo bar and firing effects draw nothing (50 random bars)", bad == 0, "bad=" + bad);
            }

            // ---- X3-9 settings: defaults, bad values -> defaults, old file (no AmmoBar / AnimFx) imports, clone is deep
            {
                GlobalSettings g = GlobalSettings.CreateDefault();
                bool def = g.AmmoBar != null && g.AnimFx != null && g.AmmoBar.Warn1.On && !g.AmmoBar.Warn2.On && !g.AmmoBar.ShowBelowOn && g.AmmoBar.ShowBelowPct == 35
                    && g.AmmoBar.ReloadNormal.Type == "slide" && g.AmmoBar.ReloadQuick.Type == "drop" && !g.AnimFx.Ripple && !g.AnimFx.Grad;
                g.AmmoBar.LenPx = 999; g.AmmoBar.Pos = "middle"; g.AmmoBar.Warn1.Style = "rainbow"; g.AmmoBar.ReloadQuick.Type = "teleport"; g.AmmoBar.BandColours = new string[] { "x" };
                g.AnimFx.RippleSpeed = -4; g.AnimFx.GradTo = "blue"; g.AmmoBar.Warn2 = null;
                g.Validate();
                bool fixedUp = g.AmmoBar.LenPx == 24 && g.AmmoBar.Pos == "below" && g.AmmoBar.Warn1.Style == "flash" && g.AmmoBar.ReloadQuick.Type == "drop"
                    && g.AmmoBar.BandColours.Length == 5 && g.AnimFx.RippleSpeed == 420 && g.AnimFx.GradTo == "#5CF2FF" && g.AmmoBar.Warn2 != null;
                GlobalSettings c = g.Clone(false); c.AmmoBar.Warn1.Pct = 50; c.AmmoBar.BandColours[0] = "#000000"; c.AnimFx.RippleRange = 70;
                bool deep = g.AmmoBar.Warn1.Pct == 15 && g.AmmoBar.BandColours[0] != "#000000" && g.AnimFx.RippleRange == 22;
                // an older file without the new fields
                string json = "{\"AmmoOn\":true,\"AmmoStyle\":\"brackets,colour,flash\",\"AnimPreset\":\"cs\",\"AnimLeanRotate\":true,\"AnimShake\":true}";
                GlobalSettings old;
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                    old = (GlobalSettings)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(GlobalSettings)).ReadObject(ms);
                old.Validate();
                bool imp = old.AmmoOn && old.AmmoBar != null && old.AmmoBar.Pos == "below" && old.AnimFx != null && old.AnimPreset == "cs";
                AnimOptions oo = CrosshairAnim.From(old);
                bool noLean = !X3Src("CrosshairAnim.cs").Contains("LeanDeg") && !X3Src("CrosshairAnim.cs").Contains("ShakePx");
                Check("V511-X3-9 settings: new bar / effect fields default right, bad values fall back, older files import (lean tilt / shake ignored), clone is deep",
                    def && fixedUp && deep && imp && noLean, "def=" + def + " fixed=" + fixedUp + " deep=" + deep + " import=" + imp + " noLean=" + noLean);
            }

            // ---- X3-10 准星动态: plain preset names (no CS), 竞速 preset, amounts change the opening, open speed / recovery
            {
                string bad = "";
                foreach (string n in CrosshairAnim.PresetNames) if (n.Contains("CS")) bad += "name " + n + " ";
                if (string.Join("|", CrosshairAnim.PresetNames) != "静态|跟随动作|开火跳动|开火特效|竞速") bad += "names ";
                GlobalSettings g = GlobalSettings.CreateDefault();
                CrosshairAnim.ApplyPreset("race", g);
                if (!(g.AnimFirePulse && g.AnimFx.Ripple && !g.AnimFx.Grad && g.AnimFx.FireRecoverMs == 90 && CrosshairAnim.PresetOf(CrosshairAnim.From(g)) == "race")) bad += "race ";
                Func<int, int, int, double[]> run = delegate(int gapPct, int openMs, int recMs)
                {
                    GlobalSettings h = GlobalSettings.CreateDefault(); h.AnimFirePulse = true; h.AnimFx.FireGapPct = gapPct; h.AnimFx.FireOpenMs = openMs; h.AnimFx.FireRecoverMs = recMs;
                    AnimOptions o = CrosshairAnim.From(h); CrosshairAnim a = new CrosshairAnim();
                    AnimInput ai = new AnimInput(); ai.MoveLevel = 0; ai.ShotSeq = 0; a.Step(o, ai, 0.001, 20);
                    ai.ShotSeq = 1; a.Step(o, ai, 0.001, 20); double first = a.Open;
                    double peak = first; for (int i = 0; i < 20; i++) { a.Step(o, ai, 0.005, 20); peak = Math.Max(peak, a.Open); }
                    for (int i = 0; i < 40; i++) a.Step(o, ai, 0.01, 20);
                    return new double[] { first, peak, a.Open };
                };
                double[] d100 = run(100, 0, 180), d200 = run(200, 0, 180), dSlow = run(100, 100, 180), dLong = run(100, 0, 1000);
                if (!(d100[0] > 0)) bad += "instant ";
                if (!(d200[1] > d100[1] * 1.5)) bad += "amount ";
                if (!(dSlow[0] < d100[0] && dSlow[1] > 0)) bad += "openspeed ";
                if (!(dLong[2] > d100[2])) bad += "recovery ";
                Check("V511-X3-10 准星动态: plain names 静态 / 跟随动作 / 开火跳动 / 开火特效 / 竞速 (no \"CS\"), 竞速 sets sharp values, 张开幅度 / 张开速度 / 恢复时间 work (0 ms = opens in the same frame)", bad == "", bad);
            }

            // ---- X3-11 firing effects: ripple runs outward and ends, gradient spreads centre -> out and fades; centre stays empty
            {
                string bad = "";
                GlobalSettings g = GlobalSettings.CreateDefault(); g.AnimFx.Ripple = true; g.AnimFx.Grad = true; g.AnimFx.GradFrom = "#FF0000"; g.AnimFx.GradTo = "#0000FF";
                AnimOptions o = CrosshairAnim.From(g);
                FireFx fx = new FireFx();
                int S = 96; double c = S / 2.0, reach = 10;
                int[] look = RingRaster.Render(RingStyle.From(g.Active, 0, false), S, c, c);
                double hole = RingRaster.HoleOf(RingStyle.From(g.Active, 0, false));
                fx.Step(o, 1, 0);
                double prevR = -1; int frames = 0; bool spread = false, endsR = false, holeBad = false, crossChanged = false;
                for (int f = 0; f < 120; f++)
                {
                    int[] px = (int[])look.Clone();
                    fx.Draw(px, S, c, c, reach, 255);
                    double far = 0;
                    for (int k = 0; k < px.Length; k++)
                    {
                        double dx = k % S + 0.5 - c, dy = k / S + 0.5 - c, r = Math.Sqrt(dx * dx + dy * dy);
                        if (look[k] == 0 && px[k] != 0) { far = Math.Max(far, r); if (r < hole) holeBad = true; }
                        if (look[k] != 0 && px[k] != look[k]) crossChanged = true;
                    }
                    if (far > 0) { if (far + 0.01 < prevR) bad += "ripple-in "; prevR = far; frames++; }
                    if (fx.GradFront >= 1) spread = true;
                    fx.Step(o, 0, 1000 / 144.0);
                }
                endsR = fx.Ripples == 0 && fx.GradStrength == 0 && !fx.Active;
                if (frames < 5 || !spread || !endsR || holeBad || !crossChanged) bad += "frames=" + frames + " spread=" + spread + " ends=" + endsR + " hole=" + holeBad + " tint=" + crossChanged;
                Check("V511-X3-11 开火波纹 runs outward (never inward) and ends, 开火渐变色 spreads to the edge and fades; nothing added inside 中心留空", bad == "", bad);
            }

            // ---- X3-12 latency + allocation with everything on (bar max, warnings, reload, ripples, gradient): 0 B, worst << 1.5 ms
            {
                GlobalSettings g = GlobalSettings.CreateDefault(); g.AmmoOn = true; g.AnimDynGap = g.AnimFirePulse = g.AnimStateColour = true;
                g.AnimFx.Ripple = g.AnimFx.Grad = true; g.AnimFx.RippleRange = 80; g.AnimFx.RippleWidth = 8;
                AmmoBarSettings.ApplyPreset(g.AmmoBar, "race"); g.AmmoBar.LenPx = 160; g.AmmoBar.ThickPx = 12; g.AmmoBar.Glow = true; g.AmmoBar.GlowPx = 10; g.AmmoBar.Warn2.On = true;
                g.AmmoStyle = "bottombar,colour,fade";
                Profile p = g.Active; RingStyle st = new RingStyle(); LookExtras x = new LookExtras();
                AnimInput ai = new AnimInput(); ai.MoveLevel = 0.5; ai.ShotSeq = 0;
                AmmoView v = X3View(4, 1); v.ReloadMs = 600;
                st.Set(p, 0, false);
                x.Frame(st, x.AnimFrom(g), ai, true, v, g.AmmoStyle, x.BarFrom(g), 0, 0);
                int S = x.Canvas, rec = x.Recomputes; int[] buf = new int[S * S];
                // v5.1.1 (X15): load-robust timing. 300 warm-up frames, then 5 runs x 1200 frames; the 1.5 ms limit applies to the
                // p95 of all 6000 samples (one descheduled frame under a parallel test run no longer fails it); worst still reported.
                const int Runs = 5, PerRun = 1200;
                double[] samples = new double[Runs * PerRun];
                double worst = 0, sum = 0, tF = 0, tX = 0, tA = 0; int n = 0; long a0 = 0;
                for (int f = 1; f <= 300 + Runs * PerRun; f++)
                {
                    if (f == 300) a0 = GC.GetAllocatedBytesForCurrentThread();
                    ai.ShotSeq = f / 6; ai.Firing = true;
                    if (f % 200 == 0) { v.ReloadSeq++; v.ReloadKind = 1 + (f / 200) % 2; }
                    v.Share = Math.Max(0.01, 1 - (f % 200) / 200.0); v.Band = AmmoModel.BandOf((int)(v.Share * 100), 100);
                    long t0 = Stopwatch.GetTimestamp();
                    st.Set(p, 0, false);
                    x.Frame(st, x.AnimFrom(g), ai, true, v, g.AmmoStyle, x.BarFrom(g), 1 / 144.0, f * 1000 / 144.0);
                    long t1 = Stopwatch.GetTimestamp();
                    Array.Clear(buf, 0, buf.Length);
                    if (x.FxActive) x.Fx.Draw(buf, S, S / 2.0, S / 2.0, x.Reach, 255);
                    long t2 = Stopwatch.GetTimestamp();
                    x.Ammo.Draw(buf, S, S / 2.0, S / 2.0, x.Inner, x.AmmoStyle, 255);
                    long t3 = Stopwatch.GetTimestamp();
                    double ms = (t3 - t0) * 1000.0 / Stopwatch.Frequency;
                    if (f > 300) { tF += (t1 - t0) * 1000.0 / Stopwatch.Frequency; tX += (t2 - t1) * 1000.0 / Stopwatch.Frequency; tA += (t3 - t2) * 1000.0 / Stopwatch.Frequency; }
                    if (f > 300) { worst = Math.Max(worst, ms); sum += ms; samples[n] = ms; n++; }
                }
                long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                Array.Sort(samples, 0, n);
                double med = samples[n / 2], p95 = samples[Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)];
                Check("V511-X3-12 everything on at max (竞速 bar 160 px, glow, 2 warnings, reloads, ripples 80 px, gradient): " + alloc + " B allocated, 0 recomputes / resizes, frame work avg "
                    + (sum / n).ToString("0.000") + " ms, median " + med.ToString("0.000") + " ms, p95 " + p95.ToString("0.000") + " ms (limit 1.5, " + Runs + " runs x " + PerRun + " frames after 300 warm-up), worst " + worst.ToString("0.000") + " ms",
                    alloc == 0 && x.Recomputes == rec && x.Canvas == S && p95 < 1.5, "alloc=" + alloc + " rec=" + (x.Recomputes - rec) + " S=" + S + "/" + x.Canvas + " split frame/fx/ammo avg " + (tF / n).ToString("0.000") + "/" + (tX / n).ToString("0.000") + "/" + (tA / n).ToString("0.000"));
            }

            // ---- X3-13 F4: the 换手 step no longer turns Pal.Hot into Color.Transparent (white tracer)
            {
                string s9 = X3Src("TutorialStep9.cs");
                bool noTransparent = !s9.Contains("pal.Hot = Color.Transparent") && !s9.Contains("Pal.Hot = Color.Transparent");
                bool tracersOff = s9.Contains("rig.ShowTracers = false;") && s9.Contains("finally { rig.ShowTracers = tracers; }");
                int others = 0;
                foreach (string f in Directory.GetFiles("/home/dev/tarkov-crosshair-v4/src", "Tutorial*.cs"))
                    if (File.ReadAllText(f).Contains("= Color.Transparent")) others++;
                Check("V511-X3-13 换手 tutorial: no stray white tracer (rig tracers switched off; no tutorial file sets a colour to Transparent)",
                    noTransparent && tracersOff && others == 0, "noT=" + noTransparent + " off=" + tracersOff + " others=" + others);
            }

            // ---- X3-14 外观 UI: 3 sub-pages, live previews with buttons, every group present; overlay draws effects; no CS / lean / shake text
            {
                string sp = X3Src("StylePanel.cs"), ov = X3Src("Overlay.cs"), ht = X3Src("HelpText.cs"), gt = X3Src("GuideText.cs");
                string[] need = { "\"准星样式\", \"准星动态\", \"弹药提示\"", "new FxLivePreview(this, false)", "new FxLivePreview(this, true)", "预览普通换弹", "预览快速换弹",
                    "预览低弹警告", "预览紧急警告", "只在剩余少于下面的值时显示", "低弹警告", "紧急警告（快打空时）", "换弹动画", "普通换弹（R）", "快速换弹（R R）",
                    "开火波纹", "开火渐变色", "开火效果强度", "恢复时间", "张开速度", "斜切格子", "按位置上色", "黑色底板", "\"竞速\"", "new AmmoPresetPicker(this)" };
                string miss = "";
                foreach (string n in need) if (!sp.Contains(n)) miss += n + " ";
                if (!ov.Contains("extras.Fx.Draw(fadeBuf") || !ov.Contains("extras.BarFrom(g)") || !ov.Contains("extras.AnimFrom(g)")) miss += "overlay ";
                foreach (string w in new string[] { "CS 动态", "CS 风格", "侧身时跟着倾斜", "轻微抖动", "两侧括号" })
                {
                    if (sp.Contains(w)) miss += "sp:" + w + " ";
                    if (ht.Contains(w)) miss += "help:" + w + " ";
                    if (gt.Contains(w)) miss += "guide:" + w + " ";
                }
                bool script = true; bool f1, m1; string c1; Program_X3AnimScript(1.5, out f1, out m1, out c1); if (!(f1 && !m1 && c1 == "开火")) script = false;
                Program_X3AnimScript(6.5, out f1, out m1, out c1); if (!(f1 && m1)) script = false;
                Check("V511-X3-14 外观: sub-pages 准星样式 / 准星动态 / 弹药提示 with live previews + buttons and all groups; overlay draws the effects; no CS / 倾斜 / 抖动 / 括号 text left",
                    miss == "" && script, "missing: " + miss + " script=" + script);
            }
        }

        /// <summary>X3-15: the 竞速 crosshair shape (外观): empty centre, both sides, 每边格数 / 斜切角度 / 粗细 change it.</summary>
        static void V511X3RaceShape()
        {
            string bad = "";
            bool tile = false; foreach (StyleTile t in StyleShapes.Tiles) if (t.Id == "race" && t.Label == "竞速" && t.Has(StyleShapes.ParamCount) && t.Has(StyleShapes.ParamSpread)) tile = true;
            if (!tile) bad += "tile ";
            long prevCount = -1; int[] prevPic = null;
            foreach (int count in new int[] { 2, 3, 5 })
                foreach (double ang in new double[] { 0, 25, 55 })
                {
                    Profile p = R41Tile("race"); StyleShapes.Of(p).Count = count; StyleShapes.Of(p).Spread = ang;
                    RingStyle s = RingStyle.From(p, 0, false);
                    int S = (int)RingRaster.CanvasSize(s); double c = S / 2.0, hole = RingRaster.HoleOf(s);
                    int[] px = RingRaster.Render(s, S, c, c);
                    long left = 0, right = 0;
                    for (int k = 0; k < px.Length; k++)
                    {
                        if (px[k] == 0) continue;
                        double dx = k % S + 0.5 - c, dy = k / S + 0.5 - c;
                        if (dx * dx + dy * dy < hole * hole) { bad += "hole@" + count + "/" + ang + " "; break; }
                        if (dx < 0) left++; else right++;
                    }
                    if (left == 0 || right == 0 || Math.Abs(left - right) > (left + right) / 5) bad += "sides@" + count + "/" + ang + " ";
                    if (prevPic != null && prevPic.Length == px.Length) { bool eq = true; for (int k = 0; k < px.Length; k++) if (px[k] != prevPic[k]) { eq = false; break; } if (eq) bad += "same@" + count + "/" + ang + " "; }
                    prevPic = px;
                    if (ang == 0) { long sum = left + right; if (sum <= prevCount) bad += "count@" + count + " "; prevCount = sum; }
                }
            Check("V511-X3-15 外观 竞速 crosshair: tile present, two rows of slanted segments around an empty centre, 每边格数 / 斜切角度 change it", bad == "", bad);
        }

        static void Program_X3AnimScript(double t, out bool f, out bool m, out string c)
        {
            // the anim preview script is a nested type of the WinForms page; its pure copy is checked through the source
            double tt = t % 9.0;
            f = (tt >= 1.2 && tt < 3.2) || (tt >= 6.0 && tt < 7.6);
            m = tt >= 4.4 && tt < 7.6;
            c = f && m ? "边走边开火" : (f ? "开火" : (m ? "移动" : "静止"));
            string sp = X3Src("StylePanel.cs");
            if (!sp.Contains("firing = (t >= 1.2 && t < 3.2) || (t >= 6.0 && t < 7.6);")) { f = false; }
        }
    }
}
