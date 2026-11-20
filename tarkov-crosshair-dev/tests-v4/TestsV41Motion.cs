// MOTION41F (v4.1): the five BRUTE40 motion / input P2 fixes (brute-motion FINDINGS.md), run through the overlay's own
// per-frame key chain and a chain fuzz (v4.3: the mouse-sway / turn-key tests went with the feature).
// Self-registers through a module initializer, so Tests.cs Main needs no edit.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TarkovCrosshair.Tests
{
    public static partial class Program
    {
        [ModuleInitializer]
        internal static void V41MotionInit() { V41MotionTests(); }

        const int M41A = 0x41, M41D = 0x44, M41W = 0x57, M41S = 0x53, M41Q = 0x51, M41Rmb = 0x02, M41Tab = 0x09, M41Esc = 0x1B,
            M41F12 = 0x7B, M41LCtrl = 0xA2, M41LAlt = 0xA4;

        /// <summary>Overlay.Run's per-frame key chain: 开关键 edge (focus-regain guard) -> ReadInput -> LiveInputFilter -> Update.
        /// Enabled is the overlay's localEnabled (the 开关键 flips it; setting it directly = the settings window's switch).</summary>
        sealed class M41Rig
        {
            public GlobalSettings G; public Profile P; public MotionEngine E = new MotionEngine();
            public LiveInputFilter F = new LiveInputFilter();
            public HashSet<int> Down = new HashSet<int>();
            public bool Enabled, Focused = true;
            public PreviewMode Pm = PreviewMode.Live;
            public double Fps = 144;
            public InputState Raw, Fed; public bool Live = true, RingOnThisFrame;
            bool prevToggle, prevLive = true;
            readonly Func<int, bool> isDown;

            public M41Rig(GlobalSettings g) { G = g; P = g.Active; Enabled = g.Enabled; isDown = delegate(int vk) { return BxDown(Down, vk); }; }

            public void Frame() { Frame(1.0 / Fps); }

            public void Frame(double dt)
            {
                E.GameFocused = Focused;
                bool live = FocusGate.KeysLive(G.OnlyWhenGameFocused, Focused);
                bool t = live && MotionEngine.IsActionDown(G.Keys, KeyBindings.ActionToggle, isDown);
                if (live && !prevLive) prevToggle = t;
                prevLive = live;
                if (t && !prevToggle) Enabled = !Enabled;
                prevToggle = t;
                Raw = live ? MotionEngine.ReadInput(G.Keys, isDown) : new InputState();
                Fed = F.Next(Raw, Enabled, Pm == PreviewMode.Live, live);
                bool was = G.Enabled;
                G.Enabled = Enabled;
                RingOnThisFrame = G.Enabled && !was;
                Live = live;
                E.Update(dt, Fed, P, G, Pm, 1.0);
            }

            public void Run(double s) { int n = (int)Math.Round(s * Fps); for (int i = 0; i < n; i++) Frame(); }
            public void Press(int vk) { Down.Add(vk); Frame(); }
            public void Release(int vk) { Down.Remove(vk); Frame(); }
            public void Tap(int vk) { Press(vk); Run(0.05); Release(vk); Run(0.05); }
        }

        static GlobalSettings M41G(string ads, string lean)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.AdsMode = ads; g.LeanMode = lean;
            g.ShowFadeMs = 0; g.HideFadeMs = 0;
            g.Validate();
            return g;
        }

        static string M41Name(int vk) { return KeyBindings.KeyName(vk); }

        static int M41DirOf(int vk) { return vk == M41W ? 0 : vk == M41S ? 1 : vk == M41A ? 2 : 3; }

        static void V41MotionTests()
        {
            V41MotionOppositeKeys();
            V41MotionAdsLatch();
            V41MotionTabEsc();
            V41MotionAltCtrlFocus();
            V41MotionSnap();
            V41MotionFuzz();
        }

        // ---------------- P2-1: opposite keys held through 开关键 x2

        static void V41MotionOppositeKeys()
        {
            int[][] pairs = { new[] { M41A, M41D }, new[] { M41D, M41A }, new[] { M41W, M41S }, new[] { M41S, M41W } };
            foreach (int[] pr in pairs)
            {
                GlobalSettings g = M41G("hold", "hold");
                M41Rig x = new M41Rig(g);
                int want = M41DirOf(pr[1]), older = M41DirOf(pr[0]);
                x.Run(0.2); x.Press(pr[0]); x.Run(0.3); x.Press(pr[1]); x.Run(1.2); // v5.1 (W21): A->D = back to 0, then out (~0.3 + 0.43 s)
                int before = x.E.CurrentDir; double ox = x.E.OffsetX, oy = x.E.OffsetY;
                x.Tap(M41F12); x.Run(0.2);
                bool off = !g.Enabled; int whileOff = x.E.CurrentDir;
                x.Tap(M41F12); x.Run(0.3);
                int after = x.E.CurrentDir;
                bool samePlace = x.E.OffsetX == ox && x.E.OffsetY == oy && x.E.Alpha == 1;
                x.Enabled = false; x.Run(0.2); x.Enabled = true; x.Run(0.3); // the settings window's switch: same rule
                int afterUi = x.E.CurrentDir;
                x.Release(pr[1]); x.Run(0.6); // the order really survived: letting the newer key go falls back to the older one
                int fallback = x.E.CurrentDir;
                Check("V41-MF1 " + M41Name(pr[0]) + " held, then " + M41Name(pr[1]) + ", 开关键 x2 with both held: " + M41Name(pr[1]) + " still wins (dir " + want
                    + "), ring in the same place; settings-window switch the same; letting " + M41Name(pr[1]) + " go falls back to " + M41Name(pr[0]),
                    before == want && off && whileOff == want && after == want && samePlace && afterUi == want && fallback == older,
                    "before=" + before + " off=" + off + " whileOff=" + whileOff + " after=" + after + " samePlace=" + samePlace + " ui=" + afterUi + " fallback=" + fallback);
            }
            {
                // unchanged (v2.4.1): the resolver is not fed during a preview, so back at 实时 two held keys are one fresh press
                GlobalSettings g = M41G("hold", "hold");
                M41Rig x = new M41Rig(g);
                x.Press(M41A); x.Run(0.2); x.Press(M41D); x.Run(0.2);
                bool d0 = x.E.CurrentDir == 3;
                x.Pm = PreviewMode.DirW; x.Run(0.3); x.Pm = PreviewMode.Live; x.Run(0.3);
                bool centred = x.E.CurrentDir == -1;
                x.Release(M41A); x.Run(0.2);
                bool d1 = x.E.CurrentDir == 3;
                Check("V41-MF1 unchanged: back from a preview to 实时 with A and D held -> centre until one is let go (preview -> 实时 still resets the order)",
                    d0 && centred && d1, "d0=" + d0 + " centred=" + centred + " d1=" + d1);
            }
        }

        // ---------------- P2-2: stale 开镜 latch after 切换 -> 按住 -> 切换

        static void V41MotionAdsLatch()
        {
            GlobalSettings g = M41G("toggle", "hold");
            M41Rig x = new M41Rig(g);
            x.Run(0.2); x.Tap(M41Rmb); x.Run(0.2);
            bool latched = x.E.AdsToggled && x.E.AdsActive && x.E.Alpha == 0;
            g.AdsMode = "hold"; x.Run(0.2);
            bool hold = !x.E.AdsActive && !x.E.AdsToggled && x.E.Alpha == 1;
            g.AdsMode = "toggle"; x.Run(0.2);
            bool back = !x.E.AdsActive && !x.E.AdsToggled && x.E.Alpha == 1;
            x.Tap(M41Rmb); x.Run(0.2);
            bool again = x.E.AdsToggled && x.E.Alpha == 0;
            Check("V41-MF2 开镜方式 切换 (latched) -> 按住 -> 切换: no 开镜 comes back, ring shown; the next 右键 latches as usual",
                latched && hold && back && again, "latched=" + latched + " hold=" + hold + " back=" + back + " again=" + again);
        }

        // ---------------- P2-3: Tab and Esc each cancel

        static void V41MotionTabEsc()
        {
            foreach (bool tabFirst in new[] { true, false })
            {
                GlobalSettings g = M41G("toggle", "hold");
                M41Rig x = new M41Rig(g);
                int held = tabFirst ? M41Tab : M41Esc, other = tabFirst ? M41Esc : M41Tab;
                x.Run(0.2); x.Press(held); x.Run(0.2);
                x.Tap(M41Rmb); x.Run(0.2);
                bool latched = x.E.AdsToggled && x.E.Alpha == 0; // a Tab / Esc that is only held does not cancel a new 开镜
                x.Press(other); x.Run(0.2);
                bool cancelled = !x.E.AdsToggled && x.E.Alpha == 1;
                Check("V41-MF3 " + (tabFirst ? "Tab held, 开镜 latched, then Esc" : "Esc held, 开镜 latched, then Tab") + " -> cancelled (each key's own press counts)",
                    latched && cancelled, "latched=" + latched + " cancelled=" + cancelled);
            }
            {
                GlobalSettings g = M41G("toggle", "hold"); g.AdsCancelTabEsc = false;
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Tap(M41Rmb); x.Tap(M41Tab); x.Tap(M41Esc); x.Press(M41Tab); x.Press(M41Esc); x.Run(0.2);
                bool kept = x.E.AdsToggled;
                GlobalSettings g2 = M41G("toggle", "hold");
                M41Rig y = new M41Rig(g2);
                y.Run(0.2); y.Tap(M41Rmb); y.Down.Add(M41Tab); y.Down.Add(M41Esc); y.Frame(); y.Run(0.2);
                bool both = !y.E.AdsToggled;
                Check("V41-MF3 Tab/Esc 取消开镜 off: neither cancels; on: Tab + Esc in the same frame cancels", kept && both, "kept=" + kept + " both=" + both);
            }
            {
                // a caller that only sets Cancel (tutorial / dashboard fakes) keeps working
                GlobalSettings g = M41G("toggle", "hold");
                MotionEngine e = new MotionEngine();
                InputState none = new InputState(), rmb = new InputState(), c = new InputState();
                rmb.Ads = true; c.Cancel = true;
                e.Update(0.01, none, g.Active, g, PreviewMode.Live, 1); e.Update(0.01, rmb, g.Active, g, PreviewMode.Live, 1);
                e.Update(0.01, none, g.Active, g, PreviewMode.Live, 1);
                bool l = e.AdsToggled;
                e.Update(0.01, c, g.Active, g, PreviewMode.Live, 1);
                bool cleared = !e.AdsToggled;
                // ReadInput reports Tab and Esc on their own, Cancel = either
                bool rd = true;
                for (int m = 0; m < 4; m++)
                {
                    HashSet<int> d = new HashSet<int>();
                    if ((m & 1) != 0) d.Add(M41Tab);
                    if ((m & 2) != 0) d.Add(M41Esc);
                    InputState i = MotionEngine.ReadInput(g.Keys, delegate(int vk) { return d.Contains(vk); });
                    rd &= i.Tab == ((m & 1) != 0) && i.Esc == ((m & 2) != 0) && i.Cancel == (m != 0);
                }
                Check("V41-MF3 Cancel-only callers still cancel on their press; ReadInput: Tab, Esc each set, Cancel = Tab or Esc", l && cleared && rd,
                    "latched=" + l + " cleared=" + cleared + " readInput=" + rd);
            }
        }

        // ---------------- P2-4: Alt/Ctrl+右键 held across a focus change

        static void V41MotionAltCtrlFocus()
        {
            foreach (int mod in new[] { M41LCtrl, M41LAlt })
            {
                GlobalSettings g = M41G("hold", "hold"); g.AdsIgnoreAltCtrl = true; g.OnlyWhenGameFocused = true;
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Press(mod); x.Run(0.1); x.Press(M41Rmb); x.Run(0.2);
                bool ignored = !x.E.AdsActive && x.E.Alpha == 1;
                x.Focused = false; x.Run(0.3);
                bool hiddenOff = x.E.Alpha == 0; // game not in front: ring hidden (只在塔科夫前台时显示)
                x.Focused = true; x.Run(0.3);
                bool still = !x.E.AdsActive && x.E.Alpha == 1;
                x.Release(mod); x.Run(0.2);
                bool whole = !x.E.AdsActive && x.E.Alpha == 1; // the whole press stays ignored, also after the mod is let go
                x.Release(M41Rmb); x.Run(0.1); x.Press(M41Rmb); x.Run(0.2);
                bool plain = x.E.AdsActive && x.E.Alpha == 0;
                Check("V41-MF4 按住 开镜: " + M41Name(mod) + "+右键 held (ignored), game loses and regains focus with both held -> still ignored, also after "
                    + M41Name(mod) + " is let go; a plain 右键 after = 开镜", ignored && hiddenOff && still && whole && plain,
                    "ignored=" + ignored + " hiddenOff=" + hiddenOff + " still=" + still + " whole=" + whole + " plain=" + plain);
            }
            {
                GlobalSettings g = M41G("toggle", "hold"); g.AdsIgnoreAltCtrl = true; g.OnlyWhenGameFocused = true;
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Press(M41LCtrl); x.Press(M41Rmb); x.Run(0.2);
                x.Focused = false; x.Run(0.3); x.Focused = true; x.Run(0.3); x.Release(M41LCtrl); x.Release(M41Rmb); x.Run(0.2);
                bool none = !x.E.AdsToggled && x.E.Alpha == 1;
                Check("V41-MF4 切换 开镜: Ctrl+右键 held across a focus change never latches", none, "toggled=" + x.E.AdsToggled);
            }
        }

        // ---------------- P2-5: 开关键 x2 snaps (Leo, 17 Nov 2026: no slide)

        /// <summary>Ring-on frame through the rig (开关键 pressed), then 1 s more: returns the ring-on offset and the largest move after it.</summary>
        static void M41RingOnPath(M41Rig x, out double ox, out double oy, out double worst, out bool ringOn, out double blend, out double lean)
        {
            x.Tap(M41F12); x.Run(0.3); // ring off
            x.Down.Add(M41F12); x.Frame();
            ringOn = x.RingOnThisFrame && x.G.Enabled;
            ox = x.E.OffsetX; oy = x.E.OffsetY; blend = x.E.HandBlend; lean = x.E.Lean;
            worst = 0;
            for (int i = 0; i < 144; i++)
            {
                if (i == 7) x.Down.Remove(M41F12);
                x.Frame();
                worst = Math.Max(worst, Math.Max(Math.Abs(x.E.OffsetX - ox), Math.Abs(x.E.OffsetY - oy)));
            }
        }

        static void V41MotionSnap()
        {
            {
                // D held + 换手 (the brute report's 45.1 px / 368 ms slide)
                GlobalSettings g = GlobalSettings.CreateDefault(); g.HandEnabled = true; g.HandMode = "toggle";
                int hk = g.Keys.Get(KeyBindings.ActionHandSwitch);
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Press(M41D); x.Run(0.3); x.Tap(hk); x.Run(1.5);
                bool set = x.E.LiveSwitched && x.E.HandBlend == 1;
                double ox, oy, worst, blend, lean; bool on;
                M41RingOnPath(x, out ox, out oy, out worst, out on, out blend, out lean);
                HandView v = new HandView(); x.P.FillView(0, v);
                double rs = g.ResolutionScale;
                bool spot = Math.Abs(ox - rs * (v.DX[3] + v.SX)) < 1e-9 && Math.Abs(oy + rs * (v.DY[3] + v.SY)) < 1e-9;
                Check("V41-MF5 开关键 x2 with D held after 换手 (Leo默认): ring at the 正常手 D spot on the first frame back on, 0 px move over the next second",
                    set && on && blend == 0 && spot && worst == 0, "set=" + set + " on=" + on + " blend=" + blend + " spot=" + spot + " move=" + F(worst) + " px");
            }
            {
                // 切换 侧身 Q latched + 换手, no movement key (the 19.3 px / 347 ms slide)
                GlobalSettings g = GlobalSettings.CreateDefault(); g.HandEnabled = true; g.HandMode = "toggle"; g.LeanMode = "toggle";
                int hk = g.Keys.Get(KeyBindings.ActionHandSwitch);
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Tap(M41Q); x.Tap(hk); x.Run(1.5);
                bool set = x.E.LeanTarget == -1 && x.E.HandBlend == 1 && Math.Abs(x.E.Lean + 1) < 1e-3;
                double ox, oy, worst, blend, lean; bool on;
                M41RingOnPath(x, out ox, out oy, out worst, out on, out blend, out lean);
                HandView v = new HandView(); x.P.FillView(0, v);
                double rs = g.ResolutionScale;
                bool centre = Math.Abs(ox - rs * v.SX) < 1e-9 && Math.Abs(oy + rs * v.SY) < 1e-9;
                Check("V41-MF5 开关键 x2 with 切换 Q latched + 换手: lean 0 and 正常手 on the first frame back on (ring at its rest spot), 0 px move after",
                    set && on && blend == 0 && lean == 0 && centre && worst == 0, "set=" + set + " blend=" + blend + " lean=" + lean + " off=(" + F(ox) + "," + F(oy) + ") move=" + F(worst));
            }
            {
                // 按住 侧身 Q held + 换手: the lean stays (held), the hand snaps to 正常手
                GlobalSettings g = GlobalSettings.CreateDefault(); g.HandEnabled = true; g.HandMode = "toggle"; g.LeanMode = "hold";
                int hk = g.Keys.Get(KeyBindings.ActionHandSwitch);
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Press(M41Q); x.Tap(hk); x.Run(1.5);
                bool set = x.E.LeanTarget == -1 && x.E.HandBlend == 1;
                double ox, oy, worst, blend, lean; bool on;
                M41RingOnPath(x, out ox, out oy, out worst, out on, out blend, out lean);
                HandView v = new HandView(); x.P.FillView(0, v);
                double rs = g.ResolutionScale;
                bool spot = Math.Abs(ox - rs * v.QX) < 1e-9 && Math.Abs(oy + rs * v.QY) < 1e-9; // X8: standing + lean = from the centre (the 站立 point fades out)
                Check("V41-MF5 开关键 x2 with 按住 Q held + 换手: 正常手 Q spot on the first frame back on, 0 px move after",
                    set && on && blend == 0 && lean == -1 && spot && worst == 0, "set=" + set + " blend=" + blend + " lean=" + lean + " spot=" + spot + " move=" + F(worst));
            }
            {
                // 换手 pressed while the ring is off, ring switched on mid-换手过渡 from the settings window: no slide either
                GlobalSettings g = GlobalSettings.CreateDefault(); g.HandEnabled = true; g.HandMode = "toggle"; g.Active.HandSwitchMs = 400;
                int hk = g.Keys.Get(KeyBindings.ActionHandSwitch);
                M41Rig x = new M41Rig(g);
                x.Run(0.2); x.Press(M41D); x.Run(0.5);
                x.Enabled = false; x.Run(0.2); x.Press(hk); x.Run(0.03);
                bool mid = x.E.HandBlend > 0 && x.E.HandBlend < 1;
                x.Release(hk); x.Enabled = true; x.Frame();
                double ox = x.E.OffsetX, oy = x.E.OffsetY, b = x.E.HandBlend, worst = 0;
                for (int i = 0; i < 144; i++) { x.Frame(); worst = Math.Max(worst, Math.Max(Math.Abs(x.E.OffsetX - ox), Math.Abs(x.E.OffsetY - oy))); }
                Check("V41-MF5 ring switched on (settings window) in the middle of a 换手过渡: 正常手 at once, 0 px move after", mid && b == 0 && worst == 0,
                    "mid=" + mid + " blend=" + b + " move=" + F(worst));
            }
        }

        // ---------------- chain fuzz: random keys / combos / focus / 开关键 / settings switch / mode flips / hostile dt

        static ulong M41Fuzz(int frames, int seed, int[] bad, string[] first, out int ringOns, out int cancels, out int blocks)
        {
            Random r = new Random(seed);
            ulong h = 1469598103934665603UL;
            int done = 0; ringOns = 0; cancels = 0; blocks = 0;
            while (done < frames)
            {
                GlobalSettings g = BxRandomConfig(r, true);
                M41Rig x = new M41Rig(g);
                double bx, by; BxBounds(x.P, g, out bx, out by);
                // independent models: last-pressed W/S A/D (fed while keys are live, never reset by the ring), the Alt/Ctrl block
                bool[] down = new bool[4]; long[] pressed = new long[4]; long tick = 0;
                bool blocked = false, prevLiveM = true; InputState prevFed = new InputState();
                int len = r.Next(2000, 15000);
                for (int f = 0; f < len && done < frames; f++, done++)
                {
                    int ev = r.Next(100);
                    if (ev < 18) x.Down.Add(BxPhysical[r.Next(BxPhysical.Length)]);
                    else if (ev < 36 && x.Down.Count > 0) { int[] arr = new int[x.Down.Count]; x.Down.CopyTo(arr); x.Down.Remove(arr[r.Next(arr.Length)]); }
                    else if (ev < 38) x.Down.Clear();
                    else if (ev < 40) { x.Down.Add(BxW); x.Down.Add(r.Next(2) == 0 ? BxLShift : BxRShift); }
                    else if (ev < 42) { x.Down.Add(r.Next(2) == 0 ? BxLCtrl : BxLAlt); x.Down.Add(BxRmb); }
                    else if (ev < 44) x.Down.Add(r.Next(2) == 0 ? BxTab : BxEsc);
                    else if (ev < 46) { x.Down.Add(BxA); x.Down.Add(BxD); }
                    if (x.Down.Contains(BxF12) && r.Next(4) != 0) x.Down.Remove(BxF12);
                    if (r.Next(300) == 0) x.Focused = !x.Focused;
                    if (r.Next(6000) == 0) x.Enabled = !x.Enabled;
                    if (r.Next(4000) == 0) g.AdsMode = g.AdsMode == "hold" ? "toggle" : "hold";
                    if (r.Next(4000) == 0) g.LeanMode = g.LeanMode == "hold" ? "toggle" : "hold";
                    double dt = BxDt(r);
                    x.Frame(dt);
                    MotionEngine e = x.E;
                    h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetX)) * 1099511628211UL;
                    h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.OffsetY)) * 1099511628211UL;
                    h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(e.Alpha)) * 1099511628211UL;
                    Func<string> where = delegate { return "seed=" + seed + " frame=" + done + " ads=" + g.AdsMode + " lean=" + g.LeanMode + " keys=" + BxKeys(x.Down) + " focused=" + x.Focused + " enabled=" + g.Enabled + " dt=" + dt; };
                    if (!(BxFinite(e.OffsetX) && BxFinite(e.OffsetY) && BxFinite(e.Alpha) && BxFinite(e.Lean) && BxFinite(e.Recoil) && BxFinite(e.HandBlend)
                        && BxFinite(e.JumpOffset) && BxFinite(e.MoveX) && BxFinite(e.MoveY)))
                    { bad[0]++; if (first[0] == null) first[0] = where(); }
                    if (Math.Abs(e.OffsetX) > bx || Math.Abs(e.OffsetY) > by) { bad[1]++; if (first[1] == null) first[1] = where() + " off=(" + e.OffsetX + "," + e.OffsetY + ")"; }
                    if (!(e.Alpha >= 0 && e.Alpha <= 1)) { bad[2]++; if (first[2] == null) first[2] = where() + " alpha=" + e.Alpha; }
                    if (g.AdsMode == "hold" && e.AdsToggled) { bad[3]++; if (first[3] == null) first[3] = where(); }
                    if (x.RingOnThisFrame)
                    {
                        ringOns++;
                        if (e.HandBlend != (e.LiveSwitched ? 1.0 : 0.0) || e.Lean != e.LeanTarget) { bad[4]++; if (first[4] == null) first[4] = where() + " blend=" + e.HandBlend + " lean=" + e.Lean + "/" + e.LeanTarget; }
                    }
                    // direction model
                    if (x.Live)
                    {
                        tick++;
                        bool[] now = { x.Raw.Left, x.Raw.Right, x.Raw.Back, x.Raw.Fwd };
                        for (int i = 0; i < 4; i++) { if (now[i] && !down[i]) pressed[i] = tick; down[i] = now[i]; }
                    }
                    bool l = down[0] && !(down[1] && pressed[1] > pressed[0]), rr = down[1] && !(down[0] && pressed[0] > pressed[1]);
                    bool bk = down[2] && !(down[3] && pressed[3] > pressed[2]), fw = down[3] && !(down[2] && pressed[2] > pressed[3]);
                    if (l && rr) { l = false; rr = false; }
                    if (bk && fw) { bk = false; fw = false; }
                    if (!x.Live) { l = rr = bk = fw = false; }
                    int dirM = MotionEngine.DirFromSigns((fw ? 1 : 0) - (bk ? 1 : 0), (rr ? 1 : 0) - (l ? 1 : 0));
                    if (e.CurrentDir != dirM) { bad[5]++; if (first[5] == null) first[5] = where() + " engine=" + e.CurrentDir + " model=" + dirM; }
                    // Tab / Esc: each key's own press cancels a 切换 latch (no edge on a focus-regain frame)
                    bool regained = x.Live && !prevLiveM;
                    InputState fed = x.Fed;
                    bool tabUp = !regained && fed.Tab && !prevFed.Tab, escUp = !regained && fed.Esc && !prevFed.Esc;
                    if (g.AdsMode == "toggle" && g.AdsCancelTabEsc && (tabUp || escUp))
                    {
                        cancels++;
                        if (e.AdsToggled) { bad[6]++; if (first[6] == null) first[6] = where(); }
                    }
                    // Alt/Ctrl+右键 block (hold mode check): set on a 右键 press with Alt/Ctrl, ended only by a release seen while live
                    bool adsEdge = !regained && fed.Ads && !prevFed.Ads;
                    if (adsEdge) blocked = g.AdsIgnoreAltCtrl && g.Keys.Ads == BxRmb && g.Keys.AdsMods == 0 && fed.Mod;
                    if (!fed.Ads && x.Live) blocked = false;
                    if (blocked) blocks++;
                    if (g.AdsMode == "hold" && e.AdsActive != (fed.Ads && !blocked)) { bad[7]++; if (first[7] == null) first[7] = where() + " engine=" + e.AdsActive + " blocked=" + blocked; }
                    prevFed = fed; prevLiveM = x.Live;
                }
            }
            return h;
        }

        static void V41MotionFuzz()
        {
            string[] names = { "finite outputs", "offset inside the profile's max box", "alpha in [0,1]", "按住 开镜 never keeps a 切换 latch (P2-2)",
                "ring-on frame: hand blend 0/1 and lean == target exactly (P2-5 snap)", "direction == last-pressed model with no reset on ring-on (P2-1)",
                "Tab or Esc press clears the 切换 latch, also while the other is held (P2-3)", "按住 开镜 == 右键 held and not Alt/Ctrl-blocked, block kept across focus gaps (P2-4)" };
            int n = 400000;
            int[] bad = new int[names.Length]; string[] first = new string[names.Length];
            int ringOns, cancels, blocks;
            DateTime t0 = DateTime.UtcNow;
            ulong h1 = M41Fuzz(n, 41117, bad, first, out ringOns, out cancels, out blocks);
            int[] bad2 = new int[names.Length]; string[] first2 = new string[names.Length]; int r2, c2, b2;
            ulong h2 = M41Fuzz(n, 41117, bad2, first2, out r2, out c2, out b2);
            double sec = (DateTime.UtcNow - t0).TotalSeconds;
            for (int i = 0; i < names.Length; i++)
                Check("V41-MF7 chain fuzz " + n + " frames: " + names[i], bad[i] == 0, bad[i] + " frames" + (first[i] == null ? "" : " first: " + first[i]));
            Check("V41-MF7 chain fuzz deterministic (same seed -> same output hash) and the cases occurred (" + ringOns + " ring-on frames, " + cancels + " Tab/Esc presses in 切换, "
                + blocks + " blocked frames; " + sec.ToString("0.0") + " s)", h1 == h2 && ringOns > 100 && cancels > 100 && blocks > 100, h1.ToString("x16") + " vs " + h2.ToString("x16"));
        }
    }
}
