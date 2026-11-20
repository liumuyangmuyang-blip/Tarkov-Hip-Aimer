// v5.1 (W22): overlay / appearance. Ammo hint (decision 1: 5 bands only, styles combinable, drawn UNDER the crosshair outside
// its reach, refill animations, hidden with the crosshair on 开镜 / 丢雷), CS feel (decision 6: presets, dynamic gap, fire pulse,
// lean rotation, state colour, shake; centre stays empty; canvas never resizes while animating), v5.0.2 latency and the 开镜
// all-zero frame kept. Pure logic + source checks; Windows is UNVERIFIED.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V51W22Init() { V51W22Tests(); V51W22UiChecks(); V51W22GrenadeLatency(); }

        /// <summary>W22-13: a G press (丢雷) is seen by the 1 ms poll and the all-zero frame is pushed in the same frame;
        /// G again shows the crosshair; F12-style re-enable shows it; time from the press to the push under 1.5 ms.</summary>
        static void V51W22GrenadeLatency()
        {
            W22Sim s = new W22Sim(144);
            s.G.AmmoOn = true; s.G.GrenadeHideOn = true; s.G.AnimDynGap = s.G.AnimFirePulse = true;
            InputState idle = new InputState(), g = new InputState(); g.Grenade = true;
            for (int i = 0; i < 300; i++) s.Step(idle);
            int bl = s.Blanks;
            long t0 = Stopwatch.GetTimestamp();
            s.Step(g);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            bool hiddenSameFrame = s.Blanks == bl + 1 && s.E.GrenadeHidden && s.BlankBad == 0;
            for (int i = 0; i < 400; i++) s.Step(idle);
            bool staysHidden = s.Blanks == bl + 1 && s.E.GrenadeHidden; // does not come back by itself
            s.Step(g); for (int i = 0; i < 200; i++) s.Step(idle);
            int up = s.Uploads;
            bool shownAgain = !s.E.GrenadeHidden && s.E.Alpha > 0.99;
            Check("V51-W22-13 丢雷: G press -> one all-zero frame in the same frame (" + ms.ToString("0.000") + " ms of frame work, limit 1.5), stays hidden 400 frames, G again shows it (crosshair + ammo hint)",
                hiddenSameFrame && staysHidden && shownAgain && ms < 1.5, "same=" + hiddenSameFrame + " stays=" + staysHidden + " shown=" + shownAgain + " uploads=" + up);
        }

        /// <summary>Overlay.Run / Draw with the v5.1 extras (window calls counted, pixels kept).</summary>
        sealed class W22Sim
        {
            public readonly MotionEngine E = new MotionEngine();
            public readonly RingStyle Style = new RingStyle();
            public readonly LookExtras X = new LookExtras();
            public readonly FrameVisibility Vis = new FrameVisibility();
            public readonly PresentGate Gate = new PresentGate();
            public readonly RasterChangeDetector Changed = new RasterChangeDetector();
            public readonly RasterCache Cache = new RasterCache();
            public GlobalSettings G; public Profile P;
            public int Uploads, Blanks, BlankBad, SizeChanges, Draws, Frames, LastS = -1;
            public int[] Buf = new int[0], Img = new int[0];
            public double LastCx, LastCy;
            int dibLevel = -1, dibAmmoKey; byte dibAmmoLevel;
            public double T; public readonly double Dt;

            public W22Sim(int hz)
            {
                Dt = 1.0 / hz;
                G = GlobalSettings.CreateDefault(); G.Enabled = true; G.OnlyWhenGameFocused = false;
                P = G.Active;
            }

            public void Step(InputState input)
            {
                T += Dt; Frames++;
                E.GameFocused = true;
                E.Update(Dt, input, P, G, PreviewMode.Live, 1.0);
                Style.Set(P, E.HandBlend, E.Boosted);
                AnimInput ai; AmmoView av;
                OverlayInputs.Fill(E, G, input, true, out ai, out av);
                X.Frame(Style, CrosshairAnim.From(G), ai, G.AmmoOn, av, G.AmmoStyle, Dt, T * 1000);
                byte cross = FrameVisibility.Level(Style.OpacityPct, E.Alpha);
                byte ammo = X.Ammo.Visible ? FrameVisibility.Level(100, E.Alpha) : (byte)0;
                byte level = cross > ammo ? cross : ammo;
                FrameAction act = Vis.Next(level);
                if (act == FrameAction.Draw) Draw(cross, ammo);
                else if (act == FrameAction.PresentHidden && Gate.Have)
                {
                    FadePixels.Scale(Buf, Buf, Buf.Length, 0);
                    foreach (int v in Buf) if (v != 0) { BlankBad++; break; }
                    Blanks++; Gate.Invalidate(); dibLevel = -1;
                }
            }

            void Draw(byte level, byte ammoLevel)
            {
                Draws++;
                int S = X.Canvas;
                if (S != LastS) { if (LastS >= 0) SizeChanges++; LastS = S; Buf = new int[S * S]; dibLevel = -1; dibAmmoKey = -1; Changed.Reset(); Gate.Invalidate(); }
                double Xp = 1280 + E.OffsetX + X.Anim.ShakeX, Yp = 720 + E.OffsetY + X.Anim.ShakeY;
                if (S > 128) { Xp = Math.Round(Xp); Yp = Math.Round(Yp); }
                int wx = (int)Math.Floor(Xp) - S / 2, wy = (int)Math.Floor(Yp) - S / 2;
                double cx = Math.Round((Xp - wx) * 8.0) / 8.0, cy = Math.Round((Yp - wy) * 8.0) / 8.0;
                RasterCache.Normalise(S, ref wx, ref cx); RasterCache.Normalise(S, ref wy, ref cy);
                LastCx = cx; LastCy = cy;
                bool am = ammoLevel != 0 && X.Ammo.Visible;
                int key = am ? X.Ammo.Key : 0;
                bool img = Changed.Changed(Style, S, cx, cy) | level != dibLevel | key != dibAmmoKey | (am && ammoLevel != dibAmmoLevel);
                if (img)
                {
                    int[] im = Cache.Image(Style, S, cx, cy);
                    if (Img.Length != S * S) Img = new int[S * S];
                    Array.Copy(im, Img, S * S);
                    FadePixels.Scale(im, Buf, S * S, level);
                    if (am) X.Ammo.Draw(Buf, S, cx, cy, X.Inner, X.AmmoStyle, ammoLevel);
                    dibLevel = level; dibAmmoKey = key; dibAmmoLevel = ammoLevel;
                }
                if (!Gate.Need(wx, wy, FrameVisibility.WindowSca, img)) return;
                Uploads++;
            }
        }

        static Profile W22Look(Random r, int i)
        {
            string[] ids = { "dotring", "ring", "chevron", "corners", "fourdots" };
            string[] v3 = { "ring", "cross", "ringcross", "square", "dot" };
            Profile p;
            if (i % 2 == 0) p = R41Tile(ids[(i / 2) % ids.Length]);
            else { p = new Profile(); p.Shape = v3[(i / 2) % v3.Length]; StyleShapes.Of(p).Kind = ""; }
            p.Diameter = 6 + r.NextDouble() * 40; p.Thickness = 1 + r.NextDouble() * 4;
            p.Feather = r.NextDouble() * 3; p.GlowPx = r.Next(3) == 0 ? r.NextDouble() * 6 : 0; p.GlowOpacityPct = 60;
            p.Gradient = r.Next(2) == 0 ? "twotone" : "none"; p.OuterThickness = r.NextDouble() * 3;
            p.CrossGap = 2 + r.NextDouble() * 6; p.CrossLength = 3 + r.NextDouble() * 8;
            return p;
        }

        static void V51W22Tests()
        {
            InputState idle = new InputState();

            // ---- W22-1 every option off, ammo off: canvas and look exactly as v5.0.2, nothing allocated per frame
            {
                Random r = new Random(1); int bad = 0; string d = "";
                for (int i = 0; i < 200; i++)
                {
                    Profile p = W22Look(r, i);
                    RingStyle a = RingStyle.From(p, 0, false), b = RingStyle.From(p, 0, false);
                    LookExtras x = new LookExtras();
                    RasterChangeDetector det = new RasterChangeDetector(); det.Changed(b, 0, 0, 0);
                    AnimInput ai = new AnimInput(); ai.MoveLevel = 1; ai.Firing = true; ai.ShotSeq = i; ai.Lean = 1;
                    x.Frame(a, new AnimOptions(), ai, false, new AmmoView(), "brackets,colour", 0.01, i * 10);
                    if (x.Canvas != RingRaster.CanvasSize(b) || det.Changed(a, 0, 0, 0) || x.Ammo.Visible) { bad++; if (bad < 3) d += p.Shape + "/" + StyleShapes.Of(p).Kind + " "; }
                }
                W22Sim s = new W22Sim(360);
                InputState mv = new InputState(); mv.Right = true;
                for (int i = 0; i < 400; i++) s.Step(idle);
                for (int i = 0; i < 4000; i++) s.Step(i % 400 < 200 ? mv : idle); // warm-up: every phase image cached (v5.0.2 rule)
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++) s.Step(i % 400 < 200 ? mv : idle);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                Check("V51-W22-1 静态 preset + ammo hint off: 200 random looks keep the v5.0.2 canvas and pixels (" + bad + " differ), 2000 frames moving allocate " + bytes + " B in the extras path",
                    bad == 0 && bytes == 0 && s.SizeChanges == 0, d + " bytes=" + bytes + " sizes=" + s.SizeChanges);
            }

            // ---- W22-2 the ammo hint never changes a crosshair pixel, never draws inside 中心留空, all 5 styles combined
            {
                Random r = new Random(2); int touched = 0, holeBad = 0, empty = 0, n = 0; string d = "";
                for (int i = 0; i < 300; i++)
                {
                    Profile p = W22Look(r, i);
                    int style = 1 + r.Next(AmmoDisplay.AllStyles);
                    RingStyle s = RingStyle.From(p, 0, false);
                    LookExtras x = new LookExtras();
                    AmmoView av = new AmmoView(); av.Active = true; av.Band = r.Next(5); av.Progress = double.NaN;
                    x.Frame(s, new AnimOptions(), new AnimInput(), true, av, AmmoDisplay.FormatStyle(style), 0.01, 1000);
                    if (i % 2 == 1) // R2: a frame in the middle of a mag swap (normal or quick, random phase)
                    {
                        av.ReloadSeq = 1; av.ReloadKind = 1 + r.Next(2);
                        x.Frame(s, new AnimOptions(), new AnimInput(), true, av, AmmoDisplay.FormatStyle(style), 0.01, 1001);
                        x.Frame(s, new AnimOptions(), new AnimInput(), true, av, AmmoDisplay.FormatStyle(style), 0.01, 1001 + r.NextDouble() * 890);
                    }
                    int S = x.Canvas; double cx = S / 2.0 + r.Next(8) / 8.0, cy = S / 2.0 + r.Next(8) / 8.0;
                    int[] look = RingRaster.Render(s, S, cx, cy);
                    int[] buf = (int[])look.Clone();
                    x.Ammo.Draw(buf, S, cx, cy, x.Inner, x.AmmoStyle, 255);
                    int added = 0; double hole = RingRaster.HoleOf(s);
                    for (int k = 0; k < S * S; k++)
                    {
                        if (look[k] != 0 && buf[k] != look[k]) touched++;
                        if (buf[k] != look[k]) added++;
                        double dx = k % S + 0.5 - cx, dy = k / S + 0.5 - cy;
                        if (dx * dx + dy * dy < hole * hole && buf[k] != 0) holeBad++;
                    }
                    if (added == 0) { empty++; if (empty < 3) d += "empty " + AmmoDisplay.FormatStyle(style) + " band " + av.Band + "; "; }
                    n++;
                }
                Check("V51-W22-2 ammo hint drawn under / outside the look: " + n + " random looks x random style combos: " + touched + " crosshair pixels changed, " + holeBad + " pixels inside 中心留空, " + empty + " hints with nothing drawn",
                    touched == 0 && holeBad == 0 && empty == 0, d);
            }

            // ---- W22-3 only bands (5), green -> yellow -> red, a shot changes the band at once, static band = no new pixels
            {
                AmmoDisplay a = new AmmoDisplay(); bool ok = true; string d = "";
                int prevKey = 0; double prevFill = 2;
                for (int b = 4; b >= 0; b--)
                {
                    AmmoView v = new AmmoView(); v.Active = true; v.Band = b; v.Progress = double.NaN;
                    a.Update(v, 1000 + b, AmmoDisplay.Brackets | AmmoDisplay.Colour);
                    if (Math.Abs(a.Fill - AmmoDisplay.BandFill[b]) > 1e-9 || a.Fill >= prevFill || a.Key == prevKey) { ok = false; d += "band " + b + " fill " + F(a.Fill) + "; "; }
                    if (a.Rgb != AmmoDisplay.ColourOf(b)) { ok = false; d += "colour " + b + "; "; }
                    prevKey = a.Key; prevFill = a.Fill;
                }
                int k0 = a.Key; bool still = true;
                for (int t = 0; t < 1000; t++) { AmmoView v = new AmmoView(); v.Active = true; v.Band = 0; v.Progress = double.NaN; a.Update(v, 2000 + t, AmmoDisplay.Brackets | AmmoDisplay.Colour); if (a.Key != k0) still = false; }
                AmmoView off = new AmmoView(); a.Update(off, 5000, AmmoDisplay.DefaultStyle);
                bool offOk = !a.Visible;
                int r4 = AmmoDisplay.ColourOf(4) >> 16, g4 = (AmmoDisplay.ColourOf(4) >> 8) & 255, r0 = AmmoDisplay.ColourOf(0) >> 16, g0 = (AmmoDisplay.ColourOf(0) >> 8) & 255, r2 = AmmoDisplay.ColourOf(2) >> 16, g2 = (AmmoDisplay.ColourOf(2) >> 8) & 255;
                bool gyr = g4 > r4 && r0 > g0 && r2 > 200 && g2 > 200;
                Check("V51-W22-3 ammo hint shows 5 bands (满 1.0 .. 快空 0.1 fill), green -> yellow -> red, a band change redraws at once, a steady band never redraws (1000 frames), other weapon = nothing",
                    ok && still && offOk && gyr && AmmoDisplay.BandNames.Length == 5, d + " still=" + still + " off=" + offOk + " gyr=" + gyr);
            }

            // ---- W22-4 reload animations: R = slow refill from the current band; RR = empty then fast refill; follows the model
            {
                bool ok = true; string d = "";
                // R2: reload = MAG SWAP for R and R R: old bar leaves, empty moment, NEW FULL bar slides in; quick faster, old dropped
                foreach (int kind in new int[] { 1, 2 })
                {
                    AmmoDisplay a = new AmmoDisplay();
                    AmmoView v = new AmmoView(); v.Active = true; v.Band = 2; v.Progress = double.NaN;
                    a.Update(v, 0, AmmoDisplay.DefaultStyle);
                    v.ReloadSeq = 1; v.ReloadKind = kind;
                    double len = kind == 2 ? AmmoDisplay.QuickReloadMs : AmmoDisplay.NormalReloadMs;
                    double oe = kind == 2 ? AmmoDisplay.QuickOutEnd : AmmoDisplay.OutEnd, ins = kind == 2 ? AmmoDisplay.QuickInStart : AmmoDisplay.InStart;
                    a.Update(v, 0, AmmoDisplay.DefaultStyle);
                    a.Update(v, len * oe * 0.5, AmmoDisplay.DefaultStyle);
                    double leaving = a.Fill; bool swapping = a.Animating && a.SwapKind == kind;
                    a.Update(v, len * (oe + ins) / 2, AmmoDisplay.DefaultStyle);
                    double gap = a.Fill;
                    a.Update(v, len * (ins + 1) / 2, AmmoDisplay.DefaultStyle);
                    double arriving = a.Fill;
                    double prev = -1; bool mono = true;
                    for (double t = len * ins; t < len; t += 5) { a.Update(v, t, AmmoDisplay.DefaultStyle); if (a.Fill < prev - 1e-9) mono = false; prev = a.Fill; }
                    double nearEnd = a.Fill; // the model may still report the old band: the hint already shows the new full mag
                    v.Band = 4; a.Update(v, len + 1, AmmoDisplay.DefaultStyle);
                    bool end = a.Fill == 1 && !a.Animating;
                    if (!(swapping && leaving >= 0 && leaving < 0.5 && gap == 0 && arriving > 0 && arriving < 1 && mono && nearEnd > 0.99 && end))
                    { ok = false; d += "kind" + kind + " leave=" + F(leaving) + " gap=" + F(gap) + " arrive=" + F(arriving) + " mono=" + mono + " near=" + F(nearEnd) + " end=" + end + "; "; }
                }
                // normal: the old bar slides out INSIDE its track; quick: it is DROPPED below the track
                {
                    int st = AmmoDisplay.Brackets;
                    double inner = AmmoDisplay.Inner(8); int S = AmmoDisplay.CanvasNeed(inner, st) + 40; double c0 = S / 2.0;
                    double bot = c0 + AmmoDisplay.BracketHalfH(inner) + AmmoDisplay.Line;
                    int[] below = new int[3];
                    for (int kind = 1; kind <= 2; kind++)
                    {
                        AmmoDisplay a = new AmmoDisplay(); AmmoView v = new AmmoView(); v.Active = true; v.Band = 4; v.Progress = double.NaN;
                        a.Update(v, 0, st); v.ReloadSeq = 1; v.ReloadKind = kind;
                        double len = kind == 2 ? AmmoDisplay.QuickReloadMs : AmmoDisplay.NormalReloadMs, oe = kind == 2 ? AmmoDisplay.QuickOutEnd : AmmoDisplay.OutEnd;
                        a.Update(v, 0, st); a.Update(v, len * oe * 0.6, st);
                        int[] buf = new int[S * S]; a.Draw(buf, S, c0, c0, inner, st, 255);
                        for (int k = 0; k < S * S; k++) if (buf[k] != 0 && k / S > bot + 1) below[kind]++;
                    }
                    if (below[1] != 0 || below[2] == 0) { ok = false; d += "below normal=" + below[1] + " quick=" + below[2] + "; "; }
                }
                // the quick swap ends sooner than the normal one (absolute time)
                if (!(AmmoDisplay.QuickReloadMs < AmmoDisplay.NormalReloadMs)) { ok = false; d += "quick not faster; "; }
                // with the engine's AmmoModel: R and R R both end on a full mag
                {
                    W22Sim s = new W22Sim(144); s.G.AmmoOn = true; s.G.AmmoMagSize = 30; s.G.AmmoRpm = 600;
                    InputState fire = new InputState(), rk = new InputState(); fire.Fire = true; rk.Reload = true;
                    for (int i = 0; i < 20; i++) s.Step(idle);
                    for (int i = 0; i < 144 * 2; i++) s.Step(fire); // 20 rounds at 600 RPM -> 少 / 快空
                    s.Step(idle);
                    double low = s.X.Ammo.Fill;
                    s.Step(rk); s.Step(idle);
                    bool started = s.X.Ammo.Animating && s.X.Ammo.SwapKind == 1;
                    for (int i = 0; i < 144 * 4; i++) s.Step(idle);
                    bool full1 = s.X.Ammo.Fill == 1 && !s.X.Ammo.Animating;
                    for (int i = 0; i < 144; i++) s.Step(fire);
                    s.Step(idle);
                    s.Step(rk); s.Step(idle); s.Step(rk); s.Step(idle);
                    bool quick = s.X.Ammo.SwapKind == 2;
                    for (int i = 0; i < 144 * 3; i++) s.Step(idle);
                    bool full2 = s.X.Ammo.Fill == 1 && !s.X.Ammo.Animating;
                    if (!(low <= 0.25 && started && full1 && quick && full2)) { ok = false; d += "model low=" + F(low) + " started=" + started + " full1=" + full1 + " quick=" + quick + " full2=" + full2 + "; "; }
                }
                Check("V51-W22-4 (R2) reload = mag swap: R and R R both move the old bar out (R inside its track, R R dropped below it and faster), an empty moment, then a NEW FULL bar slides in (monotone) and ends full; with the engine's AmmoModel both reloads end on a full mag",
                    ok, d);
            }

            // ---- W22-5 快空 flash (4 Hz) only with the flash style; fade dims with fewer rounds
            {
                AmmoDisplay a = new AmmoDisplay(); AmmoView v = new AmmoView(); v.Active = true; v.Band = 0; v.Progress = double.NaN;
                HashSet<int> keys = new HashSet<int>(); int changes = 0, prev = 0;
                for (int t = 0; t < 1000; t++) { a.Update(v, t, AmmoDisplay.Brackets | AmmoDisplay.Flash); keys.Add(a.Key); if (a.Key != prev) changes++; prev = a.Key; }
                HashSet<int> noFlash = new HashSet<int>();
                for (int t = 0; t < 1000; t++) { a.Update(v, t, AmmoDisplay.Brackets); noFlash.Add(a.Key); }
                double[] al = new double[5];
                for (int b = 0; b < 5; b++) { v.Band = b; a.Update(v, 0, AmmoDisplay.Brackets | AmmoDisplay.Fade); al[b] = a.Alpha; }
                bool fade = al[0] < al[1] && al[1] < al[2] && al[2] < al[3] && al[3] < al[4];
                Check("V51-W22-5 快空 blinks at 4 Hz with 闪烁 (" + changes + " changes in 1 s), steady without it; 变淡 dims with fewer rounds",
                    keys.Count == 2 && changes >= 7 && changes <= 9 && noFlash.Count == 1 && fade, "keys=" + keys.Count + " changes=" + changes + " fade=" + fade);
            }

            // ---- W22-6 ammo hint on + every CS option: 开镜 and 丢雷 still = ONE all-zero frame, no resize, nothing allocated
            {
                bool ok = true; string d = "";
                foreach (string what in new string[] { "ads", "grenade" })
                {
                    W22Sim s = new W22Sim(360);
                    s.G.AmmoOn = true; s.G.AmmoStyle = "brackets,bottombar,colour,fade,flash"; s.G.GrenadeHideOn = true;
                    s.G.AnimDynGap = s.G.AnimFirePulse = s.G.AnimLeanRotate = s.G.AnimStateColour = s.G.AnimShake = true;
                    InputState press = new InputState();
                    if (what == "ads") press.Ads = true; else press.Grenade = true;
                    for (int i = 0; i < 360; i++) s.Step(idle);
                    int up = s.Uploads, bl = s.Blanks, sz = s.SizeChanges;
                    for (int c = 0; c < 2; c++) W22Hide(s, what, press, idle);
                    bl = s.Blanks; up = s.Uploads;
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    for (int c = 0; c < 300; c++) W22Hide(s, what, press, idle);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                    int blanks = s.Blanks - bl;
                    if (blanks != 300 || s.BlankBad != 0 || s.SizeChanges != sz || bytes != 0) { ok = false; d += what + ": blanks=" + blanks + " bad=" + s.BlankBad + " sizes=" + (s.SizeChanges - sz) + " bytes=" + bytes + "; "; }
                }
                Check("V51-W22-6 ammo hint + all CS options on: 300 开镜 and 300 丢雷 cycles = one all-zero frame each (hint hidden with the crosshair), no window resize, 0 bytes allocated",
                    ok, d);
            }

            // ---- W22-7 CS feel: gap opens with movement / jump / rounds and recovers; centre stays empty; canvas constant
            {
                bool ok = true; string d = "";
                Random r = new Random(7); int holeBad = 0, grow = 0, sizes = 0;
                AnimOptions all = new AnimOptions(); all.DynGap = all.FirePulse = all.LeanRotate = all.StateColour = all.Shake = true;
                for (int i = 0; i < 120; i++)
                {
                    Profile p = W22Look(r, i);
                    LookExtras x = new LookExtras();
                    int S0 = -1;
                    for (int f = 0; f < 60; f++)
                    {
                        RingStyle s = RingStyle.From(p, 0, false);
                        AnimInput ai = new AnimInput(); ai.MoveLevel = (f % 20) / 19.0; ai.Jumping = f % 7 == 0; ai.Firing = f % 3 == 0; ai.ShotSeq = f / 2; ai.Lean = Math.Sin(f);
                        x.Frame(s, all, ai, true, new AmmoView(), "brackets", 1 / 144.0, f * 7);
                        if (S0 < 0) S0 = x.Canvas; else if (x.Canvas != S0) sizes++;
                        if (RingRaster.CanvasSize(s) > x.Canvas) grow++;
                        if (f % 10 == 0)
                        {
                            int S = x.Canvas; int[] img = RingRaster.Render(s, S, S / 2.0, S / 2.0); double h = RingRaster.HoleOf(RingStyle.From(p, 0, false));
                            for (int k = 0; k < S * S; k++) { double dx = k % S + 0.5 - S / 2.0, dy = k / S + 0.5 - S / 2.0; if (dx * dx + dy * dy < h * h && img[k] != 0) holeBad++; }
                        }
                    }
                }
                if (holeBad != 0 || grow != 0 || sizes != 0) { ok = false; d += "hole=" + holeBad + " grow=" + grow + " sizes=" + sizes + "; "; }
                // opening follows movement and returns to 0; one round's pulse is gone after 300 ms
                CrosshairAnim a = new CrosshairAnim(); AnimOptions cs = CrosshairAnim.PresetOptions("cs");
                AnimInput mv = new AnimInput(); mv.MoveLevel = 1; mv.ShotSeq = 0;
                a.Step(cs, mv, 0.01, 10); double openMove = a.Open;
                AnimInput st = new AnimInput(); st.MoveLevel = 0; st.ShotSeq = 0;
                a.Step(cs, st, 0.01, 10); double openStill = a.Open;
                st.ShotSeq = 1; a.Step(cs, st, 0.001, 10); double pulse = a.Open;
                for (int i = 0; i < 30; i++) a.Step(cs, st, 0.01, 10);
                double after = a.Open;
                CrosshairAnim lg = new CrosshairAnim(); AnimOptions legacy = CrosshairAnim.PresetOptions("classic");
                lg.Step(legacy, mv, 0.01, 10); double legacyMove = lg.Open;
                if (!(openMove > 0) || openStill != 0 || !(pulse > 0) || after != 0 || legacyMove != 0) { ok = false; d += "move=" + F(openMove) + " still=" + F(openStill) + " pulse=" + F(pulse) + " after300=" + F(after) + " classicMove=" + F(legacyMove) + "; "; }
                // fallback (no ShotSeq): auto at 600 RPM = 10 pulses / s; semi = one per click
                CrosshairAnim fb = new CrosshairAnim(); AnimInput fi = new AnimInput(); fi.MoveLevel = 0; fi.ShotSeq = -1; fi.Firing = true; fi.Rpm = 600;
                int peaks = 0; double pv = 0; bool rising = false;
                for (int i = 0; i < 1000; i++) { fb.Step(cs, fi, 0.001, 10); if (fb.Open > pv) rising = true; else if (rising && fb.Open < pv) { peaks++; rising = false; } pv = fb.Open; }
                if (peaks < 9 || peaks > 11) { ok = false; d += "auto peaks=" + peaks + "; "; }
                Check("V51-W22-7 CS feel: 120 random looks x 60 animated frames: 0 pixels inside 中心留空, canvas never resized or exceeded; gap opens with movement and closes when still, a round pulses and recovers within 300 ms, 经典 = fire only, 600 RPM = 10 pulses / s",
                    ok, d);
            }

            // ---- W22-8 presets and style text round trips (shared with W23's settings)
            {
                bool ok = true; string d = "";
                GlobalSettings g = GlobalSettings.CreateDefault();
                foreach (string p in CrosshairAnim.Presets) { CrosshairAnim.ApplyPreset(p, g); if (CrosshairAnim.PresetOf(CrosshairAnim.From(g)) != p || g.AnimPreset != p) { ok = false; d += p + " "; } }
                CrosshairAnim.ApplyPreset("bogus", g); if (g.AnimPreset != "static" || CrosshairAnim.From(g).Any) { ok = false; d += "bogus "; }
                bool shakeOff = true; foreach (string p in CrosshairAnim.Presets) if (CrosshairAnim.PresetOptions(p).Shake) shakeOff = false;
                GlobalSettings def = GlobalSettings.CreateDefault();
                bool defaults = def.AnimPreset == "static" && !CrosshairAnim.From(def).Any && !def.AmmoOn;
                for (int f = 1; f <= AmmoDisplay.AllStyles; f++)
                {
                    string txt = AmmoDisplay.FormatStyle(f);
                    if (AmmoDisplay.ParseStyle(txt) != AmmoDisplay.ParseStyle(GlobalSettings.NormalizeAmmoStyle(txt))) { ok = false; d += "style " + f + " "; }
                }
                if (AmmoDisplay.ParseStyle(null) != AmmoDisplay.DefaultStyle || AmmoDisplay.ParseStyle("colour") != (AmmoDisplay.Colour | AmmoDisplay.Brackets) || AmmoDisplay.ParseStyle("color,bottombar") != (AmmoDisplay.Colour | AmmoDisplay.BottomBar)) { ok = false; d += "parse "; }
                Check("V51-W22-8 presets 静态 / CS 动态 / 经典 round trip, unknown = 静态, shake off in every preset, defaults = 静态 + ammo off; ammo style text agrees with Settings.NormalizeAmmoStyle for all 31 combinations",
                    ok && shakeOff && defaults, d + " shakeOff=" + shakeOff + " defaults=" + defaults);
            }

            // ---- W22-9 latency: the extra per-frame work (anim + ammo + compose) keeps key -> push under 1.5 ms
            {
                W22Sim s = new W22Sim(360);
                s.G.AmmoOn = true; s.G.AmmoStyle = "brackets,bottombar,colour,fade,flash";
                s.G.AnimDynGap = s.G.AnimFirePulse = s.G.AnimLeanRotate = s.G.AnimStateColour = true;
                Random r = new Random(9);
                for (int i = 0; i < 200; i++) s.Step(idle);
                long freq = Stopwatch.Frequency; double max = 0, sum = 0; int n = 0;
                InputState cur = new InputState();
                for (int i = 0; i < 3000; i++)
                {
                    if (r.Next(6) == 0)
                    {
                        cur = new InputState(); cur.Right = r.Next(2) == 0; cur.Left = !cur.Right && r.Next(2) == 0; cur.Fire = r.Next(3) == 0;
                        cur.LeanR = r.Next(5) == 0; cur.Reload = r.Next(40) == 0; cur.Jump = r.Next(30) == 0;
                    }
                    long t0 = Stopwatch.GetTimestamp();
                    s.Step(cur);
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / freq;
                    if (i > 50) { if (ms > max) max = ms; sum += ms; n++; }
                }
                Check("V51-W22-9 latency: engine + CS feel + ammo hint + compose per frame avg " + (sum / n).ToString("0.000") + " ms, max " + max.ToString("0.000") + " ms over " + n + " frames with random presses (limit 1.5 ms, v5.0.2 key -> push budget)",
                    max < 1.5, "max=" + max.ToString("0.000"));
            }

            // ---- W22-10 source rules: hint composed under the look; 丢雷 / 开镜 never touch the window; no new timers
            {
                string ov = File.ReadAllText(W22Src("Overlay.cs"));
                bool under = ov.Contains("extras.Ammo.Draw(fadeBuf") && ov.Contains("extras.Frame(style");
                bool canvas = ov.Contains("int S = extras.Canvas;");
                int showCalls = 0, idx = 0; while ((idx = ov.IndexOf("Native.ShowWindow(", idx, StringComparison.Ordinal)) >= 0) { showCalls++; idx++; }
                bool noTimer = !ov.Contains("System.Threading.Timer") && !ov.Contains("new Timer(");
                Check("V51-W22-10 Overlay.cs: ammo hint composed into the same canvas under the look, canvas from LookExtras, ShowWindow only where v5.0.2 had it (3 mentions: off / game away / comment), no timers",
                    under && canvas && showCalls == 3 && noTimer, "under=" + under + " canvas=" + canvas + " show=" + showCalls + " timer=" + !noTimer);
            }
        }

        static void V51W22UiChecks()
        {
            string sp = File.ReadAllText(W22Src("StylePanel.cs"));
            bool sections = sp.Contains("\"准星动态（CS 风格）\", BuildAnim()") && sp.Contains("\"弹药提示\", BuildAmmo()");
            bool presets = sp.Contains("CrosshairAnim.ApplyPreset(id, g)") && sp.Contains("CrosshairAnim.PresetNames[i]");
            bool styles = sp.Contains("AmmoDisplay.FormatStyle(cur)") && sp.Contains("AmmoDisplay.Brackets, AmmoDisplay.BottomBar, AmmoDisplay.Colour, AmmoDisplay.Fade, AmmoDisplay.Flash");
            bool preview = sp.Contains("previewExtras.Ammo.Draw(px");
            bool noWords = !sp.Contains("慢启动") && !sp.Contains("未实测") && !sp.Contains("示意") && !sp.Contains("估计");
            bool flat = !sp.Contains("LinearGradientBrush lg") && !sp.Contains("DashStyle.Dot") && !sp.Contains("FromArgb(0x2A, 0x33, 0x2E)");
            bool fit = sp.Contains("TextFit.NeedSize(lblSub.Text");
            Check("V51-W22-11 外观 page: 准星动态 (presets 静态 / CS 动态 / 经典 + 5 options) and 弹药提示 (on/off + 5 combinable styles, at least one shape kept) sections, preview draws the hint, no 慢启动 / disclaimer words, flat leftovers gone, lblSub height from TextFit",
                sections && presets && styles && preview && noWords && flat && fit,
                "sections=" + sections + " presets=" + presets + " styles=" + styles + " preview=" + preview + " words=" + noWords + " flat=" + flat + " fit=" + fit);
            string ov = File.ReadAllText(W22Src("Overlay.cs"));
            bool feed = ov.Contains("FilterKeyFeed.FrameAll(keys.IsDown);");
            bool live = ov.Contains("StatusLine.LiveSource = delegate { return LastStatus; };");
            bool poll = ov.Contains("MotionEngine.ReadInput(pg, keys.IsDown)") && ov.Contains("a.Grenade == b.Grenade && a.Reload == b.Reload");
            Check("V51-W22-12 Overlay.cs: filter keys fed from the frame snapshot (W24), live status for the 首页 (W26), the 1 ms poll reads 丢雷 / 换弹 / weapon keys too (a G press starts a frame at once)",
                feed && live && poll, "feed=" + feed + " live=" + live + " poll=" + poll);
        }

        static void W22Hide(W22Sim s, string what, InputState press, InputState idle)
        {
            if (what == "ads") { for (int i = 0; i < 30; i++) s.Step(press); for (int i = 0; i < 30; i++) s.Step(idle); }
            else
            {
                s.Step(press); for (int i = 0; i < 29; i++) s.Step(idle); // G: hidden
                s.Step(press); for (int i = 0; i < 29; i++) s.Step(idle); // G again: shown
            }
        }

        static string W22Src(string f)
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string c = Path.Combine(d, "..", "tarkov-crosshair-v4", "src", f);
                if (File.Exists(c)) return c;
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return "/home/dev/tarkov-crosshair-v4/src/" + f;
        }
    }
}
