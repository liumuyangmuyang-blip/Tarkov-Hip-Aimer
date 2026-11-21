using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    // v4.0 BRUTE: independent brute-force / adversarial tests for Motion.cs, InputResolver.cs, Settings.cs.
    // Every check is reported through Check(); failures are summarised (with a minimal repro) in v4-requests/BRUTE.md.
    public static partial class Program
    {
        // ---- key universe (side-specific modifiers only: the generic Shift/Ctrl/Alt are derived, like GetAsyncKeyState) ----
        const int BxW = 0x57, BxS = 0x53, BxA = 0x41, BxD = 0x44, BxQ = 0x51, BxE = 0x45, BxF = 0x46, BxSpace = 0x20;
        const int BxLmb = 1, BxRmb = 2, BxMmb = 4, BxX1 = 5, BxX2 = 6, BxTab = 9, BxEsc = 0x1B, BxF12 = 0x7B;
        const int BxShiftG = 0x10, BxCtrlG = 0x11, BxAltG = 0x12;
        const int BxLShift = 0xA0, BxRShift = 0xA1, BxLCtrl = 0xA2, BxRCtrl = 0xA3, BxLAlt = 0xA4, BxRAlt = 0xA5;
        static readonly int[] BxPhysical = { BxW, BxS, BxA, BxD, BxQ, BxE, BxF, BxSpace, BxLmb, BxRmb, BxMmb, BxX1, BxX2,
            BxTab, BxEsc, BxF12, BxLShift, BxRShift, BxLCtrl, BxRCtrl, BxLAlt, BxRAlt, 0x43, 0x56, 0x47, 0x5A, 0x31, 0x32, 0x33 };
        static readonly int[] BxBindable = { BxW, BxS, BxA, BxD, BxQ, BxE, BxF, BxSpace, BxLmb, BxRmb, BxMmb, BxX1, BxX2,
            BxF12, 0x43, 0x56, 0x47, 0x5A, 0x31, BxShiftG, BxCtrlG, BxAltG, BxLShift, BxRCtrl, BxLAlt };

        static List<string> bxFails = new List<string>();
        static List<string> bxNotes = new List<string>();

        static void BxCheck(string name, bool ok, string detail, string repro)
        {
            Check("BRUTE " + name, ok, detail);
            if (!ok) bxFails.Add("- **" + name + "** -- " + detail + (repro == null ? "" : "\n  - repro: " + repro));
        }

        static bool BxDown(HashSet<int> p, int vk)
        {
            if (vk == BxShiftG) return p.Contains(BxLShift) || p.Contains(BxRShift);
            if (vk == BxCtrlG) return p.Contains(BxLCtrl) || p.Contains(BxRCtrl);
            if (vk == BxAltG) return p.Contains(BxLAlt) || p.Contains(BxRAlt);
            return p.Contains(vk);
        }

        static string BxKeys(HashSet<int> p)
        {
            List<string> l = new List<string>();
            foreach (int k in p) l.Add(KeyBindings.KeyName(k));
            l.Sort(StringComparer.Ordinal);
            return "{" + string.Join(",", l) + "}";
        }

        static double BxDt(Random r)
        {
            int c = r.Next(1000);
            if (c < 650) return (1 + r.NextDouble() * 49) / 1000.0;
            if (c < 800) return 1.0 / 144;
            if (c < 880) return 0;
            if (c < 930) return 0.1 + r.NextDouble() * 5;   // spike (alt-tab, breakpoint)
            if (c < 940) return double.NaN;
            if (c < 950) return -r.NextDouble();
            if (c < 955) return double.PositiveInfinity;
            if (c < 960) return double.NegativeInfinity;
            if (c < 970) return double.Epsilon;
            if (c < 980) return 1e-9;
            return 0.05 + r.NextDouble() * 0.05;
        }

        static void BxRandomKeys(Random r, KeyBindings k)
        {
            foreach (int a in KeyBindings.RebindableActions)
            {
                int vk = BxBindable[r.Next(BxBindable.Length)];
                int mods = r.Next(10) < 4 ? r.Next(1, 8) : 0;
                k.SetCombo(a, vk, mods);
            }
        }

        static GlobalSettings BxRandomConfig(Random r, bool randomKeys)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.AdsMode = r.Next(2) == 0 ? "hold" : "toggle";
            g.LeanMode = r.Next(2) == 0 ? "hold" : "toggle";
            g.HandMode = r.Next(2) == 0 ? "hold" : "toggle";
            g.HandEnabled = r.Next(4) != 0; g.BoostEnabled = r.Next(4) != 0;
            g.AdsCancelTabEsc = r.Next(2) == 0; g.AdsIgnoreAltCtrl = r.Next(2) == 0; g.AdsCancelSprint = r.Next(3) != 0;
            g.LeanCancelSprint = r.Next(3) != 0; g.HideOnFreeLook = r.Next(2) == 0;
            g.OnlyWhenGameFocused = r.Next(2) == 0;
            g.ShowFadeMs = r.Next(3) == 0 ? 0 : r.Next(1001); g.HideFadeMs = r.Next(3) == 0 ? 0 : r.Next(1001);
            int[] hs = { 480, 720, 1080, 1440, 1600, 2160, 4320 };
            g.ResGameH = hs[r.Next(hs.Length)]; g.ResGameW = g.ResGameH * 16 / 9;
            if (randomKeys && r.Next(3) != 0) BxRandomKeys(r, g.Keys);
            Profile p = g.Active;
            if (r.Next(3) != 0) BxRandomProfile(r, p);
            g.Validate();
            return g;
        }

        static double BxR(Random r, double lo, double hi) { return lo + r.NextDouble() * (hi - lo); }

        static void BxRandomProfile(Random r, Profile p)
        {
            for (int i = 0; i < 4; i++) p.Dirs[i] = new DirSetting(BxR(r, -500, 500), BxR(r, -500, 500));
            Profile.RecomputeDiagonals(p);
            p.LeanQX = BxR(r, -500, 500); p.LeanQY = BxR(r, -500, 500); p.LeanEX = BxR(r, -500, 500); p.LeanEY = BxR(r, -500, 500);
            p.LeanMs = r.Next(3) == 0 ? 0 : BxR(r, 0, 2000);
            p.RecoilMaxPx = r.Next(5) == 0 ? 0 : BxR(r, 0, 1000); p.RecoilRiseMs = BxR(r, 10, 5000); p.RecoilFallMs = BxR(r, 10, 5000);
            p.RecoilClickKickPx = BxR(r, 0, 300); p.RecoilDriftX = BxR(r, -500, 500); p.RecoilDelayMs = BxR(r, 0, 300);
            p.HandSwitchMs = r.Next(3) == 0 ? 0 : BxR(r, 0, 2000);
            p.CurveAD.DurationMs = r.Next(4) == 0 ? 0 : BxR(r, 0, 2000); p.CurveWS.DurationMs = r.Next(4) == 0 ? 0 : BxR(r, 0, 2000);
            for (int i = 0; i < 5; i++) { p.CurveAD.Speeds[i] = r.Next(101); p.CurveWS.Speeds[i] = r.Next(101); }
            for (int i = 0; i < 8; i++) if (r.Next(3) == 0) p.SetHandCustom(i, BxR(r, -500, 500), BxR(r, -500, 500));
            p.Jump.Enabled = r.Next(3) != 0;
            for (int i = 0; i < 3; i++)
            {
                p.Jump.Phases[i].Ms = r.Next(0, 1000);
                p.Jump.Phases[i].EndPx = BxR(r, -500, 500);
                p.Jump.Phases[i].Curve = JumpSettings.Curves[r.Next(4)];
            }
            p.Jump.TotalMs = p.Jump.Phases[0].Ms + p.Jump.Phases[1].Ms + p.Jump.Phases[2].Ms;
        }

        /// <summary>Upper bounds of |OffsetX| / |OffsetY| from the profile (both hands, every component at its max).</summary>
        static void BxBounds(Profile p, GlobalSettings g, out double bx, out double by)
        {
            double mdx = 0, mdy = 0, ml = 0, mly = 0, md = 0, msx = 0, msy = 0;
            for (int h = 0; h < 2; h++)
            {
                HandView v = new HandView();
                p.FillView(h, v);
                for (int i = 0; i < Profile.DirCount; i++) { mdx = Math.Max(mdx, Math.Abs(v.DX[i])); mdy = Math.Max(mdy, Math.Abs(v.DY[i])); }
                ml = Math.Max(ml, Math.Max(Math.Abs(v.QX), Math.Abs(v.EX)));
                mly = Math.Max(mly, Math.Max(Math.Abs(v.QY), Math.Abs(v.EY)));
                md = Math.Max(md, Math.Abs(v.Drift));
                msx = Math.Max(msx, Math.Abs(v.SX)); msy = Math.Max(msy, Math.Abs(v.SY));
            }
            double mj = 0;
            if (p.Jump != null) foreach (JumpPhase ph in p.Jump.Phases) mj = Math.Max(mj, Math.Abs(ph.EndPx));
            double rs = g.ResolutionScale;
            bx = rs * (mdx + ml + md + msx) * Math.Max(1.0, ViewScale.FovRatio(g)) + 1e-6; // v5.1.1 (X5): the other side x gameFov / baseFov
            by = rs * (mdy + mly + Math.Max(0, p.RecoilMaxPx) + mj + msy) + 1e-6;
        }

        static bool BxFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        // ------------------------------------------------------------------ the harness (same chain as Overlay's render loop)

        sealed class BxRig
        {
            public GlobalSettings G; public Profile P; public MotionEngine E = new MotionEngine();
            public LiveInputFilter Filter = new LiveInputFilter();
            public HashSet<int> Pressed = new HashSet<int>();
            public PreviewMode Pm = PreviewMode.Live; public double Speed = 1.0;
            public bool PrevToggle; public InputState Fed; public bool ToggledThisFrame; public bool PrevLive = true, Live = true;
            public BxRig(GlobalSettings g) { G = g; P = g.Active; }

            public void Frame(double dt)
            {
                Func<int, bool> isDown = delegate(int vk) { return BxDown(Pressed, vk); };
                bool live = FocusGate.KeysLive(G.OnlyWhenGameFocused, E.GameFocused);
                bool t = live && MotionEngine.IsActionDown(G.Keys, KeyBindings.ActionToggle, isDown);
                if (live && !PrevLive) PrevToggle = t; // v4.0: a 开关键 held across a focus regain is not a press (Overlay.Run)
                PrevLive = live; Live = live;
                ToggledThisFrame = t && !PrevToggle;
                if (ToggledThisFrame) G.Enabled = !G.Enabled;
                PrevToggle = t;
                InputState raw = live ? MotionEngine.ReadInput(G.Keys, isDown) : new InputState();
                Fed = Filter.Next(raw, G.Enabled, Pm == PreviewMode.Live, live);
                E.Update(dt, Fed, P, G, Pm, Speed);
            }
        }

        static string BxState(BxRig x)
        {
            return "cfg[ads=" + x.G.AdsMode + " lean=" + x.G.LeanMode + " hand=" + x.G.HandMode + " focusOpt=" + x.G.OnlyWhenGameFocused
                + " cancelRun=" + x.G.AdsCancelSprint + " leanCancelRun=" + x.G.LeanCancelSprint + "] keys=" + BxKeys(x.Pressed)
                + " focused=" + x.E.GameFocused + " pm=" + x.Pm + " enabled=" + x.G.Enabled;
        }

        // ------------------------------------------------------------------ entry

        static void V4BruteTests()
        {
            DateTime t0 = DateTime.UtcNow;
            BxComboOracle(400000);
            BxComboExamples();
            BxEngineLive(1500000, 20261116);
            BxEnginePreviewAndChaos(600000, 777);
            BxDtExtremes();
            BxToggleReset(20000);
            BxDeterminism();
            BxFocusRegainNotes();
            BxSettingsFuzz(6000);
            BxOldFiles();
            BxWriteReport((DateTime.UtcNow - t0).TotalSeconds);
        }

        // ------------------------------------------------------------------ 1. ReadInput vs an independent oracle

        static void BxComboOracle(int n)
        {
            Random r = new Random(4242);
            int bad = 0; string first = null;
            for (int it = 0; it < n; it++)
            {
                KeyBindings k = new KeyBindings();
                if (r.Next(5) != 0) BxRandomKeys(r, k);
                if (r.Next(3) == 0) k.Validate();
                HashSet<int> p = new HashSet<int>();
                int cnt = r.Next(0, 9);
                for (int j = 0; j < cnt; j++) p.Add(BxPhysical[r.Next(BxPhysical.Length)]);
                Func<int, bool> isDown = delegate(int vk) { return BxDown(p, vk); };
                InputState got = MotionEngine.ReadInput(k, isDown);

                // oracle
                bool ctrl = isDown(BxCtrlG), shift = isDown(BxShiftG), alt = isDown(BxAltG);
                Func<int, bool> combo = delegate(int a)
                {
                    int vk = k.Get(a);
                    if (vk <= 0 || !isDown(vk)) return false;
                    int own = (vk == BxCtrlG || vk == BxLCtrl || vk == BxRCtrl) ? 1 : (vk == BxShiftG || vk == BxLShift || vk == BxRShift) ? 2 : (vk == BxAltG || vk == BxLAlt || vk == BxRAlt) ? 4 : 0;
                    int m = k.GetMods(a) & 7 & ~own;
                    if ((m & 1) != 0 && !ctrl) return false;
                    if ((m & 2) != 0 && !shift) return false;
                    if ((m & 4) != 0 && !alt) return false;
                    return true;
                };
                // v5.1 (W25): 强化 with modifiers = exactly that combo (an extra Ctrl / Shift / Alt held = not down)
                Func<int, bool> exact = delegate(int a)
                {
                    if (!combo(a)) return false;
                    int vk = k.Get(a);
                    int m = KeyBindings.NormalizeMods(vk, k.GetMods(a));
                    if (m == 0) return true;
                    m |= KeyBindings.ModBitOf(vk);
                    return ((m & 1) != 0 || !ctrl) && ((m & 2) != 0 || !shift) && ((m & 4) != 0 || !alt);
                };
                bool[] mv = { isDown(BxW), isDown(BxS), isDown(BxA), isDown(BxD) };
                int[] mvk = { BxW, BxS, BxA, BxD };
                foreach (int a in KeyBindings.RebindableActions)
                {
                    int vk = k.Get(a);
                    int own = KeyBindings.ModBitOf(vk);
                    if ((k.GetMods(a) & 7 & ~own) == 0) continue;
                    for (int m = 0; m < 4; m++) if (vk == mvk[m] && combo(a)) mv[m] = false;
                }
                bool ok = got.Fwd == mv[0] && got.Back == mv[1] && got.Left == mv[2] && got.Right == mv[3]
                    && got.LeanL == combo(5) && got.LeanR == combo(6) && got.Fire == combo(1) && got.Ads == combo(2)
                    && got.FreeLook == combo(3) && got.HandSwitch == combo(4) && got.Jump == combo(11) && got.Boost == exact(12)
                    && got.Cancel == (isDown(BxTab) || isDown(BxEsc)) && got.Mod == (alt || ctrl) && got.Shift == shift;
                if (!ok)
                {
                    bad++;
                    if (first == null)
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (int a in KeyBindings.RebindableActions) sb.Append(a + "=" + k.ComboOf(a) + " ");
                        first = "bindings " + sb + "pressed " + BxKeys(p);
                    }
                }
            }
            BxCheck("C1 ReadInput == independent oracle (组合键 down rule + W/A/S/D suppression, L/R modifiers) x" + n,
                bad == 0, bad + " mismatches", first);
        }

        static void BxComboExamples()
        {
            // Spec example: 右侧身 = Ctrl+D. Ctrl+D leans right, the ring does not move right; plain D still moves.
            foreach (int ctrl in new int[] { BxLCtrl, BxRCtrl })
                foreach (string lm in new string[] { "hold", "toggle" })
                {
                    GlobalSettings g = GlobalSettings.CreateDefault();
                    g.LeanMode = lm; g.OnlyWhenGameFocused = false; g.Validate();
                    g.Keys.SetCombo(KeyBindings.ActionLeanRight, BxD, KeyBindings.ModCtrl);
                    BxRig x = new BxRig(g);
                    x.Pressed.Add(ctrl); x.Pressed.Add(BxD);
                    for (int i = 0; i < 200; i++) x.Frame(0.016);
                    bool a = x.E.LeanTarget == 1 && x.E.MoveX == 0 && x.E.MoveY == 0 && x.E.CurrentDir == -1;
                    x.Pressed.Clear(); for (int i = 0; i < 10; i++) x.Frame(0.016);
                    x.Pressed.Add(BxD); for (int i = 0; i < 200; i++) x.Frame(0.016);
                    bool b = x.E.CurrentDir == 3 && (lm == "toggle" ? x.E.LeanTarget == 1 : x.E.LeanTarget == 0);
                    // Ctrl+D held, release Ctrl (D still held): moves right, no longer leaning (hold mode)
                    BxRig y = new BxRig(g.Clone(true)); y.P = y.G.Active;
                    y.Pressed.Add(ctrl); y.Pressed.Add(BxD); for (int i = 0; i < 50; i++) y.Frame(0.016);
                    y.Pressed.Remove(ctrl); for (int i = 0; i < 50; i++) y.Frame(0.016);
                    bool c = y.E.CurrentDir == 3 && (lm == "toggle" || y.E.LeanTarget == 0);
                    BxCheck("C2 Ctrl+D 右侧身 (" + KeyBindings.KeyName(ctrl) + ", " + lm + "): leans, ring does not move; plain D moves; release Ctrl -> D moves",
                        a && b && c, "a=" + a + " b=" + b + " c=" + c, a && b && c ? null : "LeanRight=Ctrl+D, " + lm + " lean, hold " + KeyBindings.KeyName(ctrl) + "+D");
                }
            // Combo on W/S/A: Shift+W bound to 跳跃 suppresses W -> Shift+W is not a sprint, ring does not move forward.
            {
                GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false; g.AdsMode = "toggle";
                g.Keys.SetCombo(KeyBindings.ActionJump, BxW, KeyBindings.ModShift);
                BxRig x = new BxRig(g);
                x.Pressed.Add(BxRmb); x.Frame(0.016); x.Pressed.Clear(); x.Frame(0.016);
                x.Pressed.Add(BxLShift); x.Pressed.Add(BxW);
                for (int i = 0; i < 5; i++) x.Frame(0.016);
                bool ok = x.E.AdsToggled && !x.Fed.Fwd && x.E.CurrentDir == -1;
                BxCheck("C3 跳跃=Shift+W: W suppressed while the combo is down, so no sprint-cancel of 开镜 and no forward move",
                    ok, "AdsToggled=" + x.E.AdsToggled + " Fwd=" + x.Fed.Fwd + " dir=" + x.E.CurrentDir, null);
            }
            // FindOtherCombo: Ctrl+D vs D are different; same combo clashes; L/R modifier as key is its own key.
            {
                KeyBindings k = new KeyBindings();
                k.SetCombo(KeyBindings.ActionLeanRight, BxD, KeyBindings.ModCtrl);
                bool a = k.FindOtherCombo(BxD, 0, KeyBindings.ActionLeanRight) == KeyBindings.ActionRight;
                bool b = k.FindOtherCombo(BxD, KeyBindings.ModCtrl, KeyBindings.ActionJump) == KeyBindings.ActionLeanRight;
                bool c = k.FindOtherCombo(BxD, KeyBindings.ModCtrl | KeyBindings.ModShift, -1) == -1;
                BxCheck("C4 FindOtherCombo: Ctrl+D != D, Ctrl+D == Ctrl+D, Ctrl+Shift+D free", a && b && c, "a=" + a + " b=" + b + " c=" + c, null);
            }
            // Set(action, modifierKey) after SetCombo with that modifier: GetMods must stay normalised (Ctrl + Ctrl = Ctrl).
            {
                KeyBindings k = new KeyBindings();
                k.SetCombo(KeyBindings.ActionLeanRight, BxD, KeyBindings.ModCtrl);
                k.Set(KeyBindings.ActionLeanRight, BxCtrlG);
                int m = k.GetMods(KeyBindings.ActionLeanRight);
                bool ok = m == KeyBindings.NormalizeMods(BxCtrlG, m) && k.ComboOf(KeyBindings.ActionLeanRight) == "Ctrl";
                BxCheck("C5 [API only, no UI caller] Set(右侧身, Ctrl) after SetCombo(Ctrl+D): mods stay normalised", ok,
                    "GetMods=" + m + " ComboOf=" + k.ComboOf(KeyBindings.ActionLeanRight) + " FindOtherCombo(Ctrl,0)=" + k.FindOtherCombo(BxCtrlG, 0, -1),
                    "k.SetCombo(6, 0x44, ModCtrl); k.Set(6, 0x11); k.GetMods(6) -> 1 (stale, should be 0)");
            }
            // Movement actions cannot be rebound or given modifiers.
            {
                KeyBindings k = new KeyBindings();
                bool ok = true;
                for (int a = 7; a <= 10; a++) { k.Set(a, 0x43); k.SetCombo(a, 0x43, 1); k.SetMods(a, 7); ok &= k.GetMods(a) == 0; }
                ok &= k.Forward == BxW && k.Back == BxS && k.Left == BxA && k.Right == BxD;
                BxCheck("C6 W/S/A/D cannot be rebound (Set/SetCombo/SetMods ignored)", ok, "", null);
            }
        }

        // ------------------------------------------------------------------ 2. live engine fuzz with a latch oracle

        sealed class BxOracle
        {
            public bool Ads, Blocked, PrevEnabled = true, PrevLive = true; public int Lean; public InputState Prev;
            public bool LeanCancelled, AdsCancelled;
            public void Step(InputState i, GlobalSettings g) { Step(i, g, true); }
            public void Step(InputState i, GlobalSettings g, bool live)
            {
                bool ringOn = g.Enabled && !PrevEnabled; PrevEnabled = g.Enabled;
                if (live && !PrevLive) Prev = i; // v4.0: keys held when the game comes back to front are not a new press
                PrevLive = live;
                bool sprint = i.Shift && i.Fwd && !(Prev.Shift && Prev.Fwd);
                LeanCancelled = false; AdsCancelled = false;
                if (g.LeanMode == "toggle")
                {
                    if (ringOn) Lean = 0; // v4.0: 开关键 double press clears a latched 切换 lean
                    if (i.LeanL && !Prev.LeanL) Lean = Lean == -1 ? 0 : -1;
                    if (i.LeanR && !Prev.LeanR) Lean = Lean == 1 ? 0 : 1;
                    if (g.LeanCancelSprint && sprint) { LeanCancelled = Lean != 0; Lean = 0; }
                }
                else Lean = (i.LeanL && !i.LeanR) ? -1 : (i.LeanR && !i.LeanL) ? 1 : 0;
                if (ringOn) Ads = false;
                bool edge = i.Ads && !Prev.Ads;
                if (edge) Blocked = g.AdsIgnoreAltCtrl && g.Keys.Ads == BxRmb && g.Keys.AdsMods == 0 && i.Mod;
                // v4.1 (BRUTE40 P2-4, MOTION41F): only a release seen while keys are live ends the ignored Alt/Ctrl+右键 press
                // (unfocused frames read every key as up).
                if (!i.Ads && live) Blocked = false;
                if (g.AdsMode == "toggle")
                {
                    if (edge && !Blocked) Ads = !Ads;
                    // v4.1 (BRUTE40 P2-3): Tab and Esc each cancel on their own press, also while the other is held.
                    if (g.AdsCancelTabEsc && ((i.Cancel && !Prev.Cancel) || (i.Tab && !Prev.Tab) || (i.Esc && !Prev.Esc))) Ads = false;
                    if (g.AdsCancelSprint && sprint) { AdsCancelled = Ads; Ads = false; }
                }
                else Ads = false;
                Prev = i;
            }
        }

        static void BxEngineLive(int frames, int seed)
        {
            Random r = new Random(seed);
            int nan = 0, bound = 0, alpha = 0, latch = 0, edgeOnly = 0, shiftAlone = 0, rest = 0, restChecks = 0, misc = 0, held = 0, heldChecks = 0;
            string fNan = null, fBound = null, fAlpha = null, fLatch = null, fEdge = null, fShift = null, fRest = null, fMisc = null, fHeld = null;
            int done = 0;
            while (done < frames)
            {
                GlobalSettings g = BxRandomConfig(r, true);
                BxRig x = new BxRig(g);
                BxOracle o = new BxOracle();
                double bx, by; BxBounds(x.P, g, out bx, out by);
                int len = r.Next(2000, 20000);
                for (int f = 0; f < len; f++, done++)
                {
                    // keys: random walk over the key set
                    int ev = r.Next(100);
                    if (ev < 18) x.Pressed.Add(BxPhysical[r.Next(BxPhysical.Length)]);
                    else if (ev < 36 && x.Pressed.Count > 0) { int[] arr = new int[x.Pressed.Count]; x.Pressed.CopyTo(arr); x.Pressed.Remove(arr[r.Next(arr.Length)]); }
                    else if (ev < 38) x.Pressed.Clear();
                    else if (ev < 41) { x.Pressed.Add(BxW); x.Pressed.Add(r.Next(2) == 0 ? BxLShift : BxRShift); } // sprint
                    if (x.Pressed.Contains(BxF12) && r.Next(4) != 0) x.Pressed.Remove(BxF12); // F12 taps are short
                    if (r.Next(400) == 0) x.E.GameFocused = !x.E.GameFocused;
                    if (r.Next(5000) == 0) x.E.OwnForeground = !x.E.OwnForeground;
                    bool restRun = r.Next(3000) == 0;
                    double dt = BxDt(r);
                    bool prevAds = x.E.AdsToggled; int prevLeanT = x.E.LeanTarget;
                    InputState prevFed = x.Fed; bool wasLive = x.PrevLive;
                    x.Frame(dt);
                    o.Step(x.Fed, g, x.Live);
                    MotionEngine e = x.E;
                    if (!(BxFinite(e.OffsetX) && BxFinite(e.OffsetY) && BxFinite(e.Alpha) && BxFinite(e.Lean) && BxFinite(e.Recoil) && BxFinite(e.HandBlend) && BxFinite(e.JumpOffset)))
                    { nan++; if (fNan == null) fNan = BxState(x) + " dt=" + dt; }
                    if (Math.Abs(e.OffsetX) > bx || Math.Abs(e.OffsetY) > by || e.Recoil < 0 || e.Recoil > x.P.RecoilMaxPx + 1e-9)
                    { bound++; if (fBound == null) fBound = BxState(x) + " off=(" + e.OffsetX + "," + e.OffsetY + ") bound=(" + bx + "," + by + ")"; }
                    if (e.Alpha < 0 || e.Alpha > 1 || (!g.Enabled && g.HideFadeMs == 0 && e.Alpha != 0)) { alpha++; if (fAlpha == null) fAlpha = BxState(x) + " alpha=" + e.Alpha; }
                    if (e.AdsToggled != o.Ads || e.LeanTarget != o.Lean || e.LeanCancelled != o.LeanCancelled || e.AdsCancelledBySprint != o.AdsCancelled)
                    { latch++; if (fLatch == null) fLatch = BxState(x) + " engine(ads=" + e.AdsToggled + ",lean=" + e.LeanTarget + ",lc=" + e.LeanCancelled + ",ac=" + e.AdsCancelledBySprint + ") oracle(ads=" + o.Ads + ",lean=" + o.Lean + ")"; }
                    // only a rising edge cancels: held sprint + new 开镜 press keeps 开镜; held sprint + Q keeps the lean
                    bool sprintNow = x.Fed.Shift && x.Fed.Fwd, sprintPrev = prevFed.Shift && prevFed.Fwd;
                    if (sprintNow && sprintPrev)
                    {
                        if (e.LeanCancelled || e.AdsCancelledBySprint) { edgeOnly++; if (fEdge == null) fEdge = BxState(x); }
                        // v4.1 (P2-3): a Tab or Esc press that lands on the same frame cancels, so such frames are not "held" cases.
                        bool cancelEdgeNow = (x.Fed.Cancel && !prevFed.Cancel) || (x.Fed.Tab && !prevFed.Tab) || (x.Fed.Esc && !prevFed.Esc);
                        if (g.AdsMode == "toggle" && x.Fed.Ads && !prevFed.Ads && !cancelEdgeNow && !x.Fed.Mod && !x.ToggledThisFrame && !(g.Enabled && x.ToggledThisFrame) && wasLive)
                        {
                            heldChecks++;
                            if (e.AdsToggled == prevAds) { held++; if (fHeld == null) fHeld = BxState(x); }
                        }
                    }
                    if (!x.Fed.Fwd && (e.LeanCancelled || e.AdsCancelledBySprint)) { shiftAlone++; if (fShift == null) fShift = BxState(x); }
                    // misc invariants
                    if ((!g.HandEnabled && e.LiveSwitched) || (!g.BoostEnabled && e.LiveBoost) || e.HandBlend < -1e-12 || e.HandBlend > 1 + 1e-12
                        || (!e.Jumping && e.JumpOffset != 0) || (e.Jumping && (e.JumpMs < 0 || e.JumpMs > x.P.Jump.TotalMs))
                        || (g.HideOnFreeLook && x.Fed.FreeLook && g.HideFadeMs == 0 && e.Alpha != 0)
                        || (g.AdsMode == "hold" && e.AdsToggled))
                    { misc++; if (fMisc == null) fMisc = BxState(x) + " hand=" + e.LiveSwitched + " boost=" + e.LiveBoost + " blend=" + e.HandBlend + " jump=" + e.Jumping + "/" + e.JumpOffset + " alpha=" + e.Alpha; }

                    if (restRun)
                    {
                        // 20 s with no key: movement, recoil and jump settle; lean too unless a 切换 lean is latched.
                        x.Pressed.Clear(); x.E.GameFocused = true;
                        for (int k = 0; k < 800; k++) { x.Frame(0.025); o.Step(x.Fed, g, x.Live); done++; }
                        restChecks++;
                        double lx = 0, ly = 0;
                        bool ok = x.E.MoveX == 0 && x.E.MoveY == 0 && !x.E.Jumping && x.E.Recoil < 0.01 && x.E.CurrentDir == -1;
                        // v5.1.1 (X5, Leo 21 Nov 2026): rest = the 站立 point (+ lean), FOV side rule on the final X
                        HandView v = new HandView(); x.P.FillView(x.E.Hand, v);
                        if (x.E.LeanTarget != 0) { lx = x.E.LeanTarget < 0 ? v.QX : v.EX; ly = x.E.LeanTarget < 0 ? v.QY : v.EY; }
                        // X18 (Leo 21 Nov 2026 tuned default: 站立侧身 own targets): standing still uses the standing lean
                        if (x.E.LeanTarget != 0 && v.StandLeanOn) { lx = x.E.LeanTarget < 0 ? v.SQX : v.SEX; ly = x.E.LeanTarget < 0 ? v.SQY : v.SEY; }
                        // v5.1.1 (X8, Leo 21 Nov option A): a latched lean counts from the screen centre (the 站立 point fades out)
                        double sx = x.E.LeanTarget != 0 && x.P.StandAsZero ? 0 : v.SX, sy = x.E.LeanTarget != 0 && x.P.StandAsZero ? 0 : v.SY;
                        double rs = g.ResolutionScale, rsx = g.ResolutionScaleX;
                        if (x.E.HandBlend == 0 || x.E.HandBlend == 1)
                            ok &= Math.Abs(x.E.OffsetX - ViewScale.FovX(rsx * (lx + sx), ViewScale.FovRatio(g), x.E.HandBlend)) < 0.01 && Math.Abs(x.E.OffsetY + rs * (ly + sy)) < 0.01;
                        if (!ok) { rest++; if (fRest == null) fRest = BxState(x) + " off=(" + x.E.OffsetX + "," + x.E.OffsetY + ") move=(" + x.E.MoveX + "," + x.E.MoveY + ") recoil=" + x.E.Recoil + " leanT=" + x.E.LeanTarget + " lean=(" + lx + "," + ly + ")"; }
                    }
                }
            }
            string n = " (" + done + " frames, random dt incl. 0/NaN/-/Inf/spikes, random keys/combos/focus/F12)";
            BxCheck("E1 no NaN/Inf in offsets/alpha/lean/recoil/blend/jump" + n, nan == 0, nan + " frames", fNan);
            BxCheck("E2 |offset| within profile max (both hands: dir + lean + drift + recoil cap + jump), 0<=recoil<=cap", bound == 0, bound + " frames", fBound);
            BxCheck("E3 alpha in [0,1]; ring off + no hide fade -> alpha 0", alpha == 0, alpha + " frames", fAlpha);
            BxCheck("E4 开镜 latch / lean target / cancel flags == oracle every frame", latch == 0, latch + " frames", fLatch);
            BxCheck("E5 冲刺 held (no rising edge) never cancels", edgeOnly == 0, edgeOnly + " frames", fEdge);
            BxCheck("E6 冲刺 held + new 开镜 press -> 开镜 toggles (holding the combo does not keep cancelling) [" + heldChecks + " cases]", held == 0, held + " cases", fHeld);
            BxCheck("E7 no cancel without forward (Shift alone / W suppressed)", shiftAlone == 0, shiftAlone + " frames", fShift);
            BxCheck("E8 hand/boost disabled, blend 0..1, jump clock, free-look hide, hold-mode latch", misc == 0, misc + " frames", fMisc);
            BxCheck("E9 ring returns to rest after 20 s with no key (lean latch -> full lean) [" + restChecks + " checks]", rest == 0, rest + " checks", fRest);
        }

        // ------------------------------------------------------------------ 3. previews, preview speeds, chaos

        static void BxEnginePreviewAndChaos(int frames, int seed)
        {
            Random r = new Random(seed);
            Array modes = Enum.GetValues(typeof(PreviewMode));
            double[] speeds = { 1, 0.1, 0.5, 0, -1, 5, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            int nan = 0, bound = 0, stuck = 0, stuckChecks = 0; string fNan = null, fBound = null, fStuck = null;
            int done = 0;
            while (done < frames)
            {
                GlobalSettings g = BxRandomConfig(r, true);
                BxRig x = new BxRig(g);
                double bx, by; BxBounds(x.P, g, out bx, out by);
                int len = r.Next(1000, 10000);
                for (int f = 0; f < len; f++, done++)
                {
                    if (r.Next(10) < 3) x.Pressed.Add(BxPhysical[r.Next(BxPhysical.Length)]);
                    else if (r.Next(10) < 3) x.Pressed.Clear();
                    if (r.Next(200) == 0) { x.Pm = (PreviewMode)modes.GetValue(r.Next(modes.Length)); x.Speed = speeds[r.Next(speeds.Length)]; }
                    if (r.Next(150) == 0) x.E.GameFocused = !x.E.GameFocused;
                    if (r.Next(2000) == 0) g.EditHand = 1 - g.EditHand;
                    if (r.Next(3000) == 0) x.E.Reset();
                    double dt = BxDt(r);
                    x.Frame(dt);
                    MotionEngine e = x.E;
                    if (!(BxFinite(e.OffsetX) && BxFinite(e.OffsetY) && BxFinite(e.Alpha) && BxFinite(e.Lean) && BxFinite(e.Recoil) && BxFinite(e.HandBlend)))
                    { nan++; if (fNan == null) fNan = BxState(x) + " speed=" + x.Speed + " dt=" + dt; }
                    if (Math.Abs(e.OffsetX) > bx || Math.Abs(e.OffsetY) > by) { bound++; if (fBound == null) fBound = BxState(x) + " off=(" + e.OffsetX + "," + e.OffsetY + ")"; }
                    if (r.Next(4000) == 0)
                    {
                        x.Pm = PreviewMode.Live; x.Speed = 1; x.Pressed.Clear(); x.E.GameFocused = true;
                        for (int k = 0; k < 800; k++) { x.Frame(0.025); done++; }
                        stuckChecks++;
                        bool ok = x.E.MoveX == 0 && x.E.MoveY == 0 && !x.E.Jumping && x.E.Recoil < 0.01 && (x.E.LeanTarget != 0 || Math.Abs(x.E.Lean) < 1e-3);
                        if (!ok) { stuck++; if (fStuck == null) fStuck = BxState(x) + " move=(" + x.E.MoveX + "," + x.E.MoveY + ") recoil=" + x.E.Recoil + " lean=" + x.E.Lean; }
                    }
                }
            }
            BxCheck("P1 previews + preview speed 0/neg/NaN/Inf + Reset + EditHand: no NaN/Inf (" + done + " frames)", nan == 0, nan + " frames", fNan);
            BxCheck("P2 previews: |offset| within profile max", bound == 0, bound + " frames", fBound);
            BxCheck("P3 back to Live, no key for 20 s: ring at rest [" + stuckChecks + " checks]", stuck == 0, stuck + " checks", fStuck);
        }

        static void BxDtExtremes()
        {
            double[] bad = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1e308, 1e308, -0.0, double.Epsilon, double.MaxValue };
            bool ok = true; string why = null;
            foreach (double d in bad)
            {
                GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false;
                BxRig x = new BxRig(g);
                x.Pressed.Add(BxA); x.Pressed.Add(BxLmb); x.Pressed.Add(BxQ);
                for (int i = 0; i < 50; i++) { x.Frame(i % 2 == 0 ? d : 0.016); }
                bool f = BxFinite(x.E.OffsetX) && BxFinite(x.E.OffsetY) && BxFinite(x.E.Alpha);
                // A plain dt = 0 frame must not advance anything
                double ox = x.E.OffsetX, oy = x.E.OffsetY, tm = x.E.TravelMs(0);
                x.Frame(0);
                bool z = x.E.OffsetX == ox && x.E.OffsetY == oy && x.E.TravelMs(0) == tm;
                if (!(f && z)) { ok = false; if (why == null) why = "dt=" + d + " finite=" + f + " zeroFrameStill=" + z; }
            }
            BxCheck("D1 dt NaN/±Inf/±huge/-0/Epsilon alternating with 16 ms: finite; a dt=0 frame changes nothing", ok, why ?? "", why);
        }

        // ------------------------------------------------------------------ 4. F12 double press

        static void BxToggleReset(int n)
        {
            Random r = new Random(1212);
            int bad = 0, leanKept = 0, leanCases = 0; string first = null;
            for (int it = 0; it < n; it++)
            {
                GlobalSettings g = BxRandomConfig(r, true);
                g.OnlyWhenGameFocused = false;
                int tvk = r.Next(3) == 0 ? BxBindable[r.Next(BxBindable.Length)] : BxF12;
                int tmods = r.Next(3) == 0 ? r.Next(1, 8) : 0;
                tmods = KeyBindings.NormalizeMods(tvk, tmods);
                // a 开关键 that is also (part of) another binding would legitimately trigger that action too: keep it unique
                bool clash = false;
                foreach (int a in KeyBindings.RebindableActions)
                {
                    if (a == KeyBindings.ActionToggle) continue;
                    int ov = g.Keys.Get(a);
                    if (ov == tvk || (KeyBindings.IsModifierVk(tvk) && KeyBindings.ModBitOf(ov) == KeyBindings.ModBitOf(tvk)) || (tmods != 0 && KeyBindings.IsModifierVk(ov)) || (KeyBindings.IsModifierVk(tvk) && (g.Keys.GetMods(a) & KeyBindings.ModBitOf(tvk)) != 0)) clash = true;
                }
                if (clash)
                {
                    tvk = BxF12; tmods = 0;
                    foreach (int a in KeyBindings.RebindableActions) if (a != KeyBindings.ActionToggle && g.Keys.Get(a) == BxF12) g.Keys.SetCombo(a, 0x43, g.Keys.GetMods(a));
                }
                g.Keys.SetCombo(KeyBindings.ActionToggle, tvk, tmods);
                BxRig x = new BxRig(g);
                int len = r.Next(10, 600);
                for (int f = 0; f < len; f++)
                {
                    if (r.Next(10) < 3) x.Pressed.Add(BxPhysical[r.Next(BxPhysical.Length)]);
                    else if (r.Next(10) < 3) x.Pressed.Clear();
                    x.Frame(BxDt(r));
                }
                x.Pressed.Clear(); x.Frame(0.016); x.Frame(0.016);
                if (!g.Enabled) { /* ring is off: one press turns it on (that is the reset) */ }
                else
                {
                    BxPress(x, g.Keys.Get(0), g.Keys.GetMods(0)); // off
                }
                bool wasOff = !g.Enabled;
                int leanBefore = x.E.LeanTarget;
                BxPress(x, g.Keys.Get(0), g.Keys.GetMods(0)); // on
                for (int k = 0; k < 120; k++) x.Frame(0.025); // 3 s: hand blend settles
                bool ok = wasOff && g.Enabled && !x.E.LiveSwitched && x.E.Hand == 0 && x.E.HandBlend == 0 && !x.E.LiveBoost && !x.E.Boosted
                    && !x.E.AdsToggled && !x.E.Jumping && x.E.LeanTarget == 0;
                if (!ok)
                {
                    bad++;
                    if (first == null) first = BxState(x) + " toggle=" + g.Keys.ComboOf(0) + " wasOff=" + wasOff + " hand=" + x.E.LiveSwitched + " blend=" + x.E.HandBlend + " boost=" + x.E.LiveBoost + " ads=" + x.E.AdsToggled + " jump=" + x.E.Jumping + " leanT=" + x.E.LeanTarget;
                }
                if (g.LeanMode == "toggle" && leanBefore != 0) { leanCases++; if (x.E.LeanTarget != 0) leanKept++; }
            }
            BxCheck("F1 开关键 double press (F12 or random 组合键) always resets: 正常手 (blend 0), 强化 off, 开镜 latch off, no jump, 切换 lean cleared (x" + n + ")",
                bad == 0, bad + " cases", first);
            BxCheck("F2 开关键 double press clears a latched 切换 lean (v4.0 decision; " + leanCases + " latched cases)", leanCases > 0 && leanKept == 0,
                leanKept + "/" + leanCases + " kept", null);
        }

        static void BxPress(BxRig x, int vk, int mods)
        {
            int m = KeyBindings.NormalizeMods(vk, mods);
            if ((m & 1) != 0) x.Pressed.Add(BxLCtrl);
            if ((m & 2) != 0) x.Pressed.Add(BxLShift);
            if ((m & 4) != 0) x.Pressed.Add(BxLAlt);
            if (vk == BxShiftG) vk = BxRShift; else if (vk == BxCtrlG) vk = BxRCtrl; else if (vk == BxAltG) vk = BxRAlt;
            x.Pressed.Add(vk);
            x.Frame(0.016);
            x.Pressed.Clear();
            x.Frame(0.016);
        }

        // ------------------------------------------------------------------ 5. determinism

        static ulong BxRunHash(int seed, int frames)
        {
            Random r = new Random(seed);
            GlobalSettings g = BxRandomConfig(r, true);
            BxRig x = new BxRig(g);
            ulong h = 1469598103934665603UL;
            for (int f = 0; f < frames; f++)
            {
                if (r.Next(10) < 3) x.Pressed.Add(BxPhysical[r.Next(BxPhysical.Length)]);
                else if (r.Next(10) < 3) x.Pressed.Clear();
                if (r.Next(300) == 0) x.E.GameFocused = !x.E.GameFocused;
                x.Frame(BxDt(r));
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(x.E.OffsetX)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(x.E.OffsetY)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(x.E.Alpha)) * 1099511628211UL;
            }
            return h;
        }

        static void BxDeterminism()
        {
            bool ok = true; string why = null;
            for (int s = 1; s <= 20; s++)
            {
                ulong a = BxRunHash(s, 20000), b = BxRunHash(s, 20000);
                if (a != b) { ok = false; why = "seed " + s; break; }
            }
            // Reset() returns the engine to the exact initial state
            GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = false; g.LeanMode = "toggle"; g.AdsMode = "toggle";
            BxRig x = new BxRig(g); Random r = new Random(9);
            for (int f = 0; f < 5000; f++) { if (r.Next(10) < 4) x.Pressed.Add(BxPhysical[r.Next(BxPhysical.Length)]); else x.Pressed.Clear(); x.Frame(0.016); }
            x.E.Reset(); x.Filter.Resolver.Reset(); x.Pressed.Clear(); x.PrevToggle = false;
            BxRig y = new BxRig(g.Clone(true)); y.P = x.P; y.G = g;
            bool same = true;
            for (int f = 0; f < 3000; f++)
            {
                int k = BxPhysical[f * 7 % BxPhysical.Length]; if (k == BxF12) k = BxQ;
                if (f % 3 == 0) { x.Pressed.Add(k); y.Pressed.Add(k); } else if (f % 5 == 0) { x.Pressed.Clear(); y.Pressed.Clear(); }
                x.Frame(0.016); y.Frame(0.016);
                if (x.E.OffsetX != y.E.OffsetX || x.E.OffsetY != y.E.OffsetY || x.E.Alpha != y.E.Alpha) { same = false; break; }
            }
            BxCheck("R1 same seed -> bit-identical trace (20 seeds x 20k frames); Reset() == fresh engine", ok && same, (why ?? "") + " resetSame=" + same, null);
        }

        // ------------------------------------------------------------------ 6. focus regain with keys held (spec gap probe)

        static void BxFocusRegainNotes()
        {
            // v4.0 decision: keys already held when the game comes back to front are NOT a new press (PR #2's preview->实时 rule).
            // (1) Shift+W held across the focus change: 开镜 latch and 切换 lean stay, no cancel flag on the regain frame.
            GlobalSettings g = GlobalSettings.CreateDefault(); g.OnlyWhenGameFocused = true; g.AdsMode = "toggle"; g.LeanMode = "toggle";
            g.AdsCancelSprint = true; g.LeanCancelSprint = true; g.Validate();
            BxRig x = new BxRig(g);
            x.Pressed.Add(BxRmb); x.Frame(0.016); x.Pressed.Clear(); x.Frame(0.016);
            x.Pressed.Add(BxQ); x.Frame(0.016); x.Pressed.Clear(); x.Frame(0.016);
            bool before = x.E.AdsToggled && x.E.LeanTarget == -1;
            x.E.GameFocused = false; x.Pressed.Add(BxW); x.Pressed.Add(BxLShift); x.Frame(0.016); x.Frame(0.016);
            x.E.GameFocused = true; x.Frame(0.016);
            bool kept = x.E.AdsToggled && x.E.LeanTarget == -1 && !x.E.LeanCancelled && !x.E.AdsCancelledBySprint;
            x.Frame(0.016);
            bool keptHeld = x.E.AdsToggled && x.E.LeanTarget == -1;
            // a NEW sprint after the regain still cancels
            x.Pressed.Remove(BxW); x.Frame(0.016); x.Pressed.Add(BxW); x.Frame(0.016);
            bool newSprint = !x.E.AdsToggled && x.E.LeanTarget == 0 && x.E.LeanCancelled && x.E.AdsCancelledBySprint;
            BxCheck("G1 focus regain with Shift+W held: 开镜 latch + 切换 lean kept, no cancel; a new 冲刺 after it still cancels",
                before && kept && keptHeld && newSprint, "before=" + before + " kept=" + kept + " held=" + keptHeld + " newSprint=" + newSprint,
                "OnlyWhenGameFocused on; latch 右键 + Q; unfocus; hold LShift+W; focus");

            // (2) 换手 (toggle) + 开镜 (toggle) + Q (toggle) + 强化 + Space + LMB held across the focus change: nothing toggles/starts.
            GlobalSettings g2 = GlobalSettings.CreateDefault(); g2.OnlyWhenGameFocused = true; g2.AdsMode = "toggle"; g2.LeanMode = "toggle";
            g2.HandEnabled = true; g2.HandMode = "toggle"; g2.BoostEnabled = true; g2.Validate();
            g2.Keys.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkXButton2, 0); // v5.1 (W23): default is Ctrl+T; this case holds plain keys
            if (g2.Active.Jump != null) g2.Active.Jump.Enabled = true;
            BxRig y = new BxRig(g2);
            y.E.GameFocused = false;
            int[] held = { g2.Keys.Get(KeyBindings.ActionHandSwitch), g2.Keys.Get(KeyBindings.ActionAds), g2.Keys.Get(KeyBindings.ActionLeanLeft),
                g2.Keys.Get(KeyBindings.ActionBoost), g2.Keys.Get(KeyBindings.ActionJump) };
            foreach (int vk in held) if (vk > 0) y.Pressed.Add(vk);
            y.Frame(0.016); y.Frame(0.016);
            y.E.GameFocused = true; y.Frame(0.016); y.Frame(0.016);
            bool none = !y.E.LiveSwitched && !y.E.AdsToggled && y.E.LeanTarget == 0 && !y.E.LiveBoost && !y.E.Jumping;
            // releasing and pressing again after the regain works as usual
            y.Pressed.Clear(); y.Frame(0.016);
            foreach (int vk in held) if (vk > 0) y.Pressed.Add(vk);
            y.Frame(0.016);
            bool fresh = y.E.LiveSwitched && y.E.AdsToggled && y.E.LeanTarget == -1 && y.E.LiveBoost && y.E.Jumping;
            BxCheck("G2 focus regain with 换手/开镜/Q/强化/跳跃 held: no toggle, no jump; a fresh press after it works",
                none && fresh, "none=" + none + " fresh=" + fresh + " hand=" + y.E.LiveSwitched + " ads=" + y.E.AdsToggled + " lean=" + y.E.LeanTarget
                + " boost=" + y.E.LiveBoost + " jump=" + y.E.Jumping, "keys " + string.Join(",", held));

            // (3) 开关键 (F12) held across the focus change does not flip the ring.
            GlobalSettings g3 = GlobalSettings.CreateDefault(); g3.OnlyWhenGameFocused = true; g3.Validate();
            BxRig z = new BxRig(g3);
            bool on0 = g3.Enabled;
            z.E.GameFocused = false; BxPressHold(z, g3.Keys.Get(0), g3.Keys.GetMods(0)); z.Frame(0.016);
            z.E.GameFocused = true; z.Frame(0.016); z.Frame(0.016);
            bool noFlip = g3.Enabled == on0;
            z.Pressed.Clear(); z.Frame(0.016); BxPress(z, g3.Keys.Get(0), g3.Keys.GetMods(0));
            bool flipAfter = g3.Enabled != on0;
            BxCheck("G3 开关键 held across a focus regain does not toggle the ring; a fresh press after it does", noFlip && flipAfter,
                "noFlip=" + noFlip + " flipAfter=" + flipAfter, null);

            // (4) A held, then D held (D wins); focus lost and regained with both still held: D still wins (press order kept).
            GlobalSettings g4 = GlobalSettings.CreateDefault(); g4.OnlyWhenGameFocused = true; g4.Validate();
            BxRig w = new BxRig(g4);
            w.Pressed.Add(BxA); w.Frame(0.016); w.Pressed.Add(BxD); w.Frame(0.016);
            bool dWins = w.Fed.Right && !w.Fed.Left;
            w.E.GameFocused = false; w.Frame(0.016); w.Frame(0.016);
            bool gated = !w.Fed.Left && !w.Fed.Right;
            w.E.GameFocused = true; w.Frame(0.016);
            bool still = w.Fed.Right && !w.Fed.Left;
            w.Pressed.Remove(BxD); w.Frame(0.016);
            bool back = w.Fed.Left && !w.Fed.Right;
            BxCheck("G4 opposite keys held across a focus regain keep their press order (D after A still wins, release D -> A)",
                dWins && gated && still && back, "dWins=" + dWins + " gated=" + gated + " still=" + still + " back=" + back, null);
        }

        static void BxPressHold(BxRig x, int vk, int mods)
        {
            int m = KeyBindings.NormalizeMods(vk, mods);
            if ((m & 1) != 0) x.Pressed.Add(BxLCtrl);
            if ((m & 2) != 0) x.Pressed.Add(BxLShift);
            if ((m & 4) != 0) x.Pressed.Add(BxLAlt);
            if (vk == BxShiftG) vk = BxRShift; else if (vk == BxCtrlG) vk = BxRCtrl; else if (vk == BxAltG) vk = BxRAlt;
            x.Pressed.Add(vk);
        }

        // ------------------------------------------------------------------ 7. settings fuzz

        static List<string> BxRangeErrors(GlobalSettings g)
        {
            List<string> e = new List<string>();
            Action<bool, string> need = delegate(bool c, string m) { if (!c) e.Add(m); };
            BxNoNaN(g, "g", e, 0);
            KeyBindings k = g.Keys;
            need(k != null, "Keys null");
            if (k != null)
            {
                need(k.Forward == BxW && k.Back == BxS && k.Left == BxA && k.Right == BxD, "WASD not forced");
                for (int a = 0; a < KeyBindings.ActionCount; a++)
                {
                    int vk = k.Get(a), m = k.GetMods(a);
                    need(vk >= 1 && vk <= 254, "vk out of range a=" + a + " vk=" + vk);
                    need(m >= 0 && m <= 7 && m == KeyBindings.NormalizeMods(vk, m), "mods not normalised a=" + a + " mods=" + m);
                }
            }
            need(g.AdsMode == "hold" || g.AdsMode == "toggle", "AdsMode " + g.AdsMode);
            need(g.LeanMode == "hold" || g.LeanMode == "toggle", "LeanMode " + g.LeanMode);
            need(g.HandMode == "hold" || g.HandMode == "toggle", "HandMode " + g.HandMode);
            need(g.FrameMode == "vsync" || g.FrameMode == "fixed", "FrameMode " + g.FrameMode);
            need(g.Fps >= 30 && g.Fps <= 360, "Fps " + g.Fps);
            need(g.ShowFadeMs >= 0 && g.ShowFadeMs <= 1000 && g.HideFadeMs >= 0 && g.HideFadeMs <= 1000, "fade");
            need(g.ResGameW >= 640 && g.ResGameW <= 7680 && g.ResGameH >= 480 && g.ResGameH <= 4320, "ResGame " + g.ResGameW + "x" + g.ResGameH);
            // v4.1: 调参分辨率 is editable (高级设置); older files still get 2160x1440 (TestsV41Core). FOV / aspect in range.
            need(g.ResBaseW >= 640 && g.ResBaseW <= 7680 && g.ResBaseH >= 480 && g.ResBaseH <= 4320 && g.ResBaseW == Math.Round(g.ResBaseW) && g.ResBaseH == Math.Round(g.ResBaseH), "ResBase " + g.ResBaseW + "x" + g.ResBaseH);
            need(g.ResV41Present && g.ResFov >= 50 && g.ResFov <= 75 && g.ResBaseFov >= 50 && g.ResBaseFov <= 75 && g.ResFov == Math.Round(g.ResFov) && g.ResBaseFov == Math.Round(g.ResBaseFov)
                && g.ResAspect != null && ViewScale.NormalizeAspect(g.ResAspect) == g.ResAspect && g.ResBaseAspect != null && ViewScale.NormalizeAspect(g.ResBaseAspect) == g.ResBaseAspect /* v5.1 (W34, decision 12): any W:H */ && (g.ResFovType == "v" || g.ResFovType == "h")
                && g.ResolutionScale > 0 && !double.IsNaN(g.ResolutionScale) && !double.IsInfinity(g.ResolutionScale), "v4.1 view fields");
            need(g.MonitorIndex >= -1 && g.MonitorIndex <= 15, "MonitorIndex");
            need(!string.IsNullOrEmpty(g.GameWindowTitle) && g.GameWindowTitle.Length <= 200 && g.GameWindowTitle == g.GameWindowTitle.Trim(), "GameWindowTitle");
            need(g.Profiles != null && g.Profiles.Count > 0, "no profiles");
            if (g.Profiles != null && g.Profiles.Count > 0)
            {
                HashSet<string> names = new HashSet<string>();
                foreach (Profile p in g.Profiles)
                {
                    need(p != null, "null profile"); if (p == null) continue;
                    need(!string.IsNullOrEmpty(p.Name) && p.Name == p.Name.Trim() && names.Add(p.Name), "profile name '" + p.Name + "'");
                    BxProfileErrors(p, e, true);
                }
                need(g.FindProfile(g.ActiveProfile) != null, "ActiveProfile '" + g.ActiveProfile + "' missing");
            }
            return e;
        }

        static void BxProfileErrors(Profile p, List<string> e, bool top)
        {
            Action<bool, string> need = delegate(bool c, string m) { if (!c) e.Add(p.Name + ": " + m); };
            need(p.Dirs != null && p.Dirs.Count == 8, "Dirs count");
            if (p.Dirs != null && p.Dirs.Count == 8)
            {
                // v5.1 (W23, decision 5): directions are stored relative to the 站立静止偏移; the limits hold FROM THE SCREEN CENTRE
                double csx = p.StandAsZero ? p.StandOffsetX : 0, csy = p.StandAsZero ? p.StandOffsetY : 0;
                for (int i = 0; i < 8; i++) { DirSetting d = p.Dirs[i]; double lim = (i < 4 ? 500 : 0.7071 * 1000) + 1e-9; need(d != null && Math.Abs(d.OffX + csx) <= lim && Math.Abs(d.OffY + csy) <= lim, "dir range " + i); }
                double k = 0.7071;
                // v5.1 (W34, decision 5 / W23 RecomputeDiagonals): with 站立静止偏移 on, the diagonal is 0.7071 x the centre-based
                // sum, stored relative to the stand point; stand 0 = the v3.0 rule
                double sx = p.StandAsZero ? p.StandOffsetX : 0;
                double wantWa = sx == 0 ? k * (p.Dirs[0].OffX + p.Dirs[2].OffX) : k * (p.Dirs[0].OffX + p.Dirs[2].OffX + 2 * sx) - sx;
                need(Math.Abs(p.Dirs[4].OffX - wantWa) < 1e-9, "diagonal WA not automatic");
            }
            need(p.HandCustom != null && p.HandCustom.Count == 8 && !p.HandCustom.Contains(null), "HandCustom");
            foreach (MoveCurve c in new MoveCurve[] { p.CurveAD, p.CurveWS })
            {
                need(c != null, "curve null");
                if (c == null) continue;
                need(c.DurationMs >= 0 && c.DurationMs <= 2000, "curve ms");
                need(c.Speeds != null && c.Speeds.Length == 5, "speeds len");
                if (c.Speeds != null) foreach (double s in c.Speeds) need(s >= 0 && s <= 100, "speed range");
            }
            need(p.Jump != null && p.Jump.Phases != null && p.Jump.Phases.Count == 3, "jump");
            if (p.Jump != null && p.Jump.Phases != null && p.Jump.Phases.Count == 3)
            {
                double sum = 0; foreach (JumpPhase ph in p.Jump.Phases) { need(ph != null && ph.Ms >= 0 && Math.Abs(ph.EndPx) <= 500, "phase"); if (ph != null) sum += ph.Ms; }
                need(p.Jump.TotalMs >= 100 && p.Jump.TotalMs <= 3000, "jump total " + p.Jump.TotalMs);
                need(sum == p.Jump.TotalMs, "jump phases sum " + sum + " != total " + p.Jump.TotalMs);
                need(p.Jump.Phases[2] == null || p.Jump.Phases[2].EndPx == 0, "jump phase 3 end");
            }
            need(p.RecoilMaxPx >= 0 && p.RecoilMaxPx <= 1000 && p.RecoilRiseMs >= 10 && p.RecoilFallMs >= 10 && p.RecoilDelayMs >= 0 && p.RecoilDelayMs <= 300, "recoil");
            need(p.LeanMs >= 0 && p.LeanMs <= 2000 && p.HandSwitchMs >= 0 && p.HandSwitchMs <= 2000, "lean/hand ms");
            need(Math.Abs(p.LeanQX) <= 500 && Math.Abs(p.LeanEX) <= 500 && Math.Abs(p.LeanQY) <= 500 && Math.Abs(p.LeanEY) <= 500, "lean range");
            // v5.1 (W23, decision 5): 站立静止偏移 may be set (StandAsZero on, within ±200); off -> exactly 0
            if (top) need(p.StandAsZero ? Math.Abs(p.StandOffsetX) <= Profile.StandLimitPx && Math.Abs(p.StandOffsetY) <= Profile.StandLimitPx
                : p.StandOffsetX == 0 && p.StandOffsetY == 0, "stand offset out of range / not 0 while off");
            need(p.Diameter >= 4 && p.Diameter <= 300 && p.OpacityPct >= 5 && p.OpacityPct <= 100, "look");
            need(p.Color != null && p.Color.Length == 7 && p.Color[0] == '#', "color");
            need(p.Boost != null && p.Style != null, "boost/style null");
            if (p.SavedDefault != null) { need(p.SavedDefault.SavedDefault == null, "nested snapshot"); BxProfileErrors(p.SavedDefault, e, false); }
        }

        static void BxNoNaN(object o, string path, List<string> e, int depth)
        {
            if (o == null || depth > 6) return;
            Type t = o.GetType();
            if (t.Namespace != "TarkovCrosshair") return;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v = f.GetValue(o);
                if (v is double) { double d = (double)v; if (!BxFinite(d)) e.Add("NaN/Inf at " + path + "." + f.Name); }
                else if (v is double[]) { foreach (double d in (double[])v) if (!BxFinite(d)) e.Add("NaN/Inf in " + path + "." + f.Name); }
                else if (v is System.Collections.IList && !(v is Array)) { int i = 0; foreach (object it in (System.Collections.IList)v) BxNoNaN(it, path + "." + f.Name + "[" + (i++) + "]", e, depth + 1); }
                else if (v != null && v.GetType().Namespace == "TarkovCrosshair" && !v.GetType().IsEnum) BxNoNaN(v, path + "." + f.Name, e, depth + 1);
            }
        }

        static JsonNode BxRandomValue(Random r)
        {
            switch (r.Next(18))
            {
                case 0: return null;
                case 1: return JsonValue.Create(true);
                case 2: return JsonValue.Create("garbage");
                case 3: return JsonValue.Create(0);
                case 4: return JsonValue.Create(-1);
                case 5: return JsonValue.Create(1e308);
                case 6: return JsonValue.Create(-1e308);
                case 7: return JsonValue.Create(2147483648L);
                case 8: return JsonValue.Create(r.NextDouble() * 20000 - 10000);
                case 9: return new JsonObject();
                case 10: return new JsonArray();
                case 11: return JsonValue.Create("NaN");
                case 12: return JsonValue.Create("Infinity");
                case 13: return JsonValue.Create(new string('字', 5000));
                case 14: return JsonValue.Create("#ZZZZZZ");
                case 15: return JsonValue.Create(1e-320);
                case 16: { JsonArray a = new JsonArray(); for (int i = 0; i < 50; i++) a.Add(JsonValue.Create(i)); return a; }
                default: return JsonValue.Create(r.Next(-100, 100000));
            }
        }

        /// <summary>Same JSON type as the original value (number -> number, string -> string ...) but a hostile value.</summary>
        static JsonNode BxSameTypeValue(Random r, JsonNode orig)
        {
            if (orig is JsonValue)
            {
                JsonValue v = (JsonValue)orig;
                string s;
                bool b;
                double d;
                if (v.TryGetValue<bool>(out b)) return JsonValue.Create(r.Next(2) == 0);
                if (v.TryGetValue<string>(out s))
                {
                    string[] opts = { "", " ", "toggle ", "HOLD", "x", "#12345", "#gg0000", new string('a', 3000), "null", "../../evil.png", "C:\\x.png", "😀" };
                    return JsonValue.Create(opts[r.Next(opts.Length)]);
                }
                long l;
                if (v.TryGetValue<long>(out l))
                {
                    long[] io = { 0, -1, 1, 2147483647, -2147483648, 255, 256, 7, 8, 1000000 };
                    return JsonValue.Create(io[r.Next(io.Length)]);
                }
                if (v.TryGetValue<double>(out d))
                {
                    double[] opts = { 0, -1, 1e9, -1e9, 1e308, -1e308, 0.5, -0.0, 255, 256, 1e-320, r.NextDouble() * 1e6 - 5e5 };
                    return JsonValue.Create(opts[r.Next(opts.Length)]);
                }
            }
            return BxRandomValue(r);
        }

        static void BxMutate(Random r, JsonNode n, double p, int[] kinds)
        {
            if (n is JsonObject)
            {
                JsonObject o = (JsonObject)n;
                List<string> keys = new List<string>();
                foreach (KeyValuePair<string, JsonNode> kv in o) keys.Add(kv.Key);
                foreach (string key in keys)
                {
                    if (r.NextDouble() < p)
                    {
                        int kind = kinds[r.Next(kinds.Length)];
                        if (kind == 0) o.Remove(key);
                        else if (kind == 1) { if (o[key] is JsonValue) o[key] = BxSameTypeValue(r, o[key]); else if (o[key] != null) BxMutate(r, o[key], p, kinds); }
                        else if (kind == 2) o[key] = BxRandomValue(r);
                        else o["Unknown" + r.Next(1000)] = BxRandomValue(r);
                    }
                    else if (o.ContainsKey(key)) BxMutate(r, o[key], p, kinds);
                }
            }
            else if (n is JsonArray)
            {
                JsonArray a = (JsonArray)n;
                for (int i = a.Count - 1; i >= 0; i--)
                {
                    if (r.NextDouble() < p / 2 && Array.IndexOf(kinds, 0) >= 0) a.RemoveAt(i);
                    else BxMutate(r, a[i], p, kinds);
                }
                if (r.NextDouble() < p && Array.IndexOf(kinds, 2) >= 0) a.Add(BxRandomValue(r));
            }
        }

        /// <summary>v4.1 (BRUTE40 P2-14): a folder of its own for every run (brute-settings/run-&lt;guid&gt;), so parallel runs and
        /// other tools that use brute-settings never lose files to this test; BxCleanScratch removes only that folder's files.</summary>
        static string BxScratch()
        {
            string s = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            if (string.IsNullOrEmpty(s)) s = outDir ?? Path.GetTempPath();
            string d = Path.Combine(s, "brute-settings", "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>Deletes the files of a BxScratch run folder (all of them were made by this run), then the folder.</summary>
        static void BxCleanScratch(string runDir)
        {
            if (string.IsNullOrEmpty(runDir) || !Path.GetFileName(runDir).StartsWith("run-")) return; // never a shared folder
            foreach (string f in Directory.GetFiles(runDir)) File.Delete(f);
            try { Directory.Delete(runDir); } catch (Exception) { }
        }

        static void BxSettingsFuzz(int n)
        {
            Random r = new Random(31337);
            List<string> bases = new List<string>();
            bases.Add(SettingsIO.Serialize(GlobalSettings.CreateDefault()));
            for (int i = 0; i < 4; i++) { GlobalSettings g = BxRandomConfig(r, true); g.Profiles.Add(Profile.CreateBlank("B" + i)); g.Active.SaveAsDefault("x"); bases.Add(SettingsIO.Serialize(g)); }
            foreach (string f in BxFixtureFiles()) bases.Add(File.ReadAllText(f));

            string[] catNames = { "missing fields only", "same-type hostile values", "wrong-type values", "unknown fields", "everything" };
            int[][] cats = { new[] { 0 }, new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 0, 1, 2, 3 } };
            int[] tried = new int[cats.Length], accepted = new int[cats.Length];
            int threw = 0, rangeBad = 0, idemBad = 0, missingRejected = 0, sameTypeRejected = 0;
            string fThrew = null, fRange = null, fIdem = null, fMissing = null, fSame = null;
            for (int it = 0; it < n; it++)
            {
                int c = it % cats.Length;
                string src = bases[r.Next(bases.Count)];
                JsonNode root;
                try { root = JsonNode.Parse(src); } catch (Exception) { continue; }
                BxMutate(r, root, r.Next(2) == 0 ? 0.05 : 0.3, cats[c]);
                string json = root.ToJsonString();
                tried[c]++;
                GlobalSettings g = null; string err = null; bool ok;
                try { ok = SettingsIO.TryParse(json, out g, out err); }
                catch (Exception ex) { threw++; if (fThrew == null) fThrew = ex.GetType().Name + " on " + Trunc(json); continue; }
                if (!ok)
                {
                    if (c == 0) { missingRejected++; if (fMissing == null) fMissing = err + " | " + Trunc(json); }
                    if (c == 1) { sameTypeRejected++; if (fSame == null) fSame = err + " | " + Trunc(json); }
                    continue;
                }
                accepted[c]++;
                List<string> e = BxRangeErrors(g);
                if (e.Count > 0) { rangeBad++; if (fRange == null) fRange = string.Join("; ", e.GetRange(0, Math.Min(4, e.Count))) + " | " + Trunc(json); }
                string s1 = SettingsIO.Serialize(g);
                GlobalSettings g2; string err2;
                if (!SettingsIO.TryParse(s1, out g2, out err2) || SettingsIO.Serialize(g2) != s1) { idemBad++; if (fIdem == null) fIdem = (err2 ?? "re-serialised text differs") + " | " + Trunc(json); }
            }
            // raw byte garbage
            string[] raw = { "", " ", "null", "[]", "{", "}", "{}", "\uFEFF", "\uFEFF{}", "{\"Profiles\":null}", "{\"Profiles\":[null,null]}",
                "{\"Keys\":null,\"Profiles\":[{}]}", "{\"Profiles\":[{\"Name\":null,\"Dirs\":[null]}]}", "{\"Profiles\":[{\"Jump\":{\"Phases\":[null,null,null,null]}}]}",
                "{\"ActiveProfile\":\"nope\",\"Profiles\":[{\"Name\":\"a\"},{\"Name\":\"a\"},{\"Name\":\" a \"}]}", "{\"Version\":99999999999}",
                "{\"Profiles\":[{\"SavedDefault\":{\"SavedDefault\":{\"Name\":\"deep\"}}}]}", "{\"Profiles\":[{\"CurveAD\":{\"Speeds\":[]}, \"CurveWS\":{\"Speeds\":null}}]}",
                "{\"Profiles\":[{\"HandCustom\":[null,{\"Custom\":true,\"X\":1e308,\"Y\":-1e308}]}]}", "{\"Keys\":{\"LeanRight\":68,\"LeanRightMods\":255,\"Toggle\":0,\"ToggleMods\":-1}}",
                "{\"Keys\":{\"Forward\":67,\"Left\":0,\"Back\":999}}", "{\"ResGameW\":0,\"ResGameH\":-5,\"Fps\":1e308}", "{\"GameWindowTitle\":\"   \"}" };
            int rawThrew = 0, rawRange = 0; string fRaw = null;
            foreach (string s in raw)
            {
                GlobalSettings g; string err;
                try
                {
                    if (SettingsIO.TryParse(s, out g, out err))
                    {
                        List<string> e = BxRangeErrors(g);
                        if (e.Count > 0) { rawRange++; if (fRaw == null) fRaw = s + " -> " + string.Join("; ", e); }
                    }
                }
                catch (Exception ex) { rawThrew++; if (fRaw == null) fRaw = s + " -> " + ex.GetType().Name; }
            }
            // byte flips / truncation through the real Load() path
            string dir = BxScratch();
            int loadThrew = 0, loadBad = 0, loads = 0; string fLoad = null;
            byte[] seedBytes = Encoding.UTF8.GetBytes(bases[0]);
            for (int it = 0; it < 300; it++)
            {
                byte[] b = (byte[])seedBytes.Clone();
                int mode = it % 3;
                if (mode == 0) Array.Resize(ref b, r.Next(b.Length));
                else if (mode == 1) for (int j = 0; j < 1 + r.Next(10); j++) b[r.Next(b.Length)] = (byte)r.Next(256);
                else { b = new byte[r.Next(1, 2000)]; r.NextBytes(b); }
                string path = Path.Combine(dir, "settings.json");
                foreach (string old in Directory.GetFiles(dir)) File.Delete(old);
                File.WriteAllBytes(path, b);
                loads++;
                try
                {
                    string msg;
                    GlobalSettings g = SettingsIO.Load(path, out msg);
                    List<string> e = BxRangeErrors(g);
                    if (e.Count > 0) { loadBad++; if (fLoad == null) fLoad = string.Join("; ", e); }
                }
                catch (Exception ex) { loadThrew++; if (fLoad == null) fLoad = ex.GetType().Name + ": " + ex.Message; }
            }
            BxCleanScratch(dir);

            BxCheck("S1 fuzzed settings.json (" + n + " files, 5 mutation kinds): TryParse never throws", threw == 0, threw + " threw", fThrew);
            BxCheck("S2 every accepted fuzzed file is fully in range (WASD forced, mods normalised, modes, fps, res, names unique, dirs/curves/jump/hand lists, no NaN)", rangeBad == 0, rangeBad + " files", fRange);
            BxCheck("S3 accepted file: Serialize -> TryParse -> Serialize is identical (Validate is idempotent)", idemBad == 0, idemBad + " files", fIdem);
            BxCheck("S4 files with only MISSING fields are always accepted (no reset to defaults)", missingRejected == 0, missingRejected + "/" + tried[0] + " rejected", fMissing);
            BxCheck("S5 hand-crafted garbage (" + raw.Length + " cases: nulls, empty lists, deep snapshot, bad mods, NaN-ish): no throw, in range", rawThrew == 0 && rawRange == 0, rawThrew + " threw, " + rawRange + " out of range", fRaw);
            BxCheck("S6 SettingsIO.Load on " + loads + " corrupted/truncated/random-byte files: never throws, result in range", loadThrew == 0 && loadBad == 0, loadThrew + " threw, " + loadBad + " bad", fLoad);
            StringBuilder st = new StringBuilder();
            for (int c = 0; c < cats.Length; c++) st.Append(catNames[c] + ": " + accepted[c] + "/" + tried[c] + " accepted; ");
            // v4.0 SETTINGS-FIX: tolerant load -- a wrong-type member is dropped (gets its default), the rest of the file is kept.
            bool allAccepted = true;
            for (int c = 0; c < cats.Length; c++) allAccepted &= accepted[c] == tried[c];
            BxCheck("S7 tolerant load: every fuzzed file of every mutation kind is accepted (wrong-type values default only that member)", allAccepted, st.ToString(), null);
            bxNotes.Add("Settings fuzz acceptance: " + st + "Since SETTINGS-FIX a wrong-type member only resets that member (Load keeps a settings.bad-*.json copy); "
                + "only a file that is not JSON at all is renamed and replaced by the defaults. "
                + "Same-type hostile values rejected: " + sameTypeRejected + "/" + tried[1] + (fSame == null ? "" : " (first: " + Trunc(fSame) + ")") + ".");
        }

        static string Trunc(string s) { s = s ?? ""; s = s.Replace("\n", " ").Replace("\r", ""); return s.Length > 300 ? s.Substring(0, 300) + "..." : s; }

        static List<string> BxFixtureFiles()
        {
            List<string> files = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            string dev = "/home/dev/tarkov-crosshair-dev";
            List<string> dirs = new List<string>();
            dirs.Add(Path.Combine(dev, "tests-v4", "fixtures"));
            if (Directory.Exists(dev)) foreach (string b in Directory.GetDirectories(dev, "backup-*")) foreach (string d in Directory.GetDirectories(b, "fixtures", SearchOption.AllDirectories)) dirs.Add(d);
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (string f in Directory.GetFiles(d, "*.json"))
                {
                    string h = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(f)));
                    if (seen.Add(h)) files.Add(f);
                }
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        static void BxOldFiles()
        {
            List<string> files = BxFixtureFiles();
            int bad = 0; string first = null; int ok = 0;
            foreach (string f in files)
            {
                GlobalSettings g; string err;
                bool parsed;
                try { parsed = SettingsIO.TryParse(File.ReadAllText(f), out g, out err); }
                catch (Exception ex) { bad++; if (first == null) first = Path.GetFileName(f) + ": " + ex.GetType().Name; continue; }
                if (!parsed) { bad++; if (first == null) first = Path.GetFileName(f) + ": rejected " + err; continue; }
                List<string> e = BxRangeErrors(g);
                bool v4 = g.LeanCancelSprint && !g.TutorialDone && g.Keys.LeanRightMods == 0 && g.Keys.ToggleMods == 0;
                if (e.Count > 0 || !v4) { bad++; if (first == null) first = Path.GetFileName(f) + ": " + string.Join("; ", e) + " v4defaults=" + v4; }
                else ok++;
                // engine runs on every old profile without NaN
                foreach (Profile p in g.Profiles)
                {
                    g.ActiveProfile = p.Name; g.OnlyWhenGameFocused = false;
                    BxRig x = new BxRig(g); Random r = new Random(5);
                    double bx, by; BxBounds(p, g, out bx, out by);
                    for (int i = 0; i < 3000; i++)
                    {
                        if (r.Next(10) < 3) x.Pressed.Add(BxPhysical[r.Next(BxPhysical.Length)]); else if (r.Next(10) < 3) x.Pressed.Clear();
                        x.Frame(BxDt(r));
                        if (!BxFinite(x.E.OffsetX) || !BxFinite(x.E.OffsetY) || Math.Abs(x.E.OffsetX) > bx || Math.Abs(x.E.OffsetY) > by)
                        { bad++; if (first == null) first = Path.GetFileName(f) + " profile " + p.Name + " engine off=(" + x.E.OffsetX + "," + x.E.OffsetY + ")"; break; }
                    }
                }
            }
            // a v3.6-shaped file: today's file with every v4.0 field removed
            string v36;
            {
                JsonNode root = JsonNode.Parse(SettingsIO.Serialize(GlobalSettings.CreateDefault()));
                JsonObject o = (JsonObject)root;
                o.Remove("LeanCancelRun"); o.Remove("TutorialDone");
                JsonObject k = (JsonObject)o["Keys"];
                foreach (string m in new string[] { "LeanLeftMods", "LeanRightMods", "FireMods", "AdsMods", "ToggleMods", "FreeLookMods", "JumpMods", "HandSwitchMods", "BoostMods" }) k.Remove(m);
                k["Forward"] = 0x26; k["Left"] = 0x25; // a v3.x user who moved with the arrow keys
                v36 = o.ToJsonString();
            }
            GlobalSettings g36; string e36;
            bool p36 = SettingsIO.TryParse(v36, out g36, out e36);
            bool ok36 = p36 && g36.LeanCancelSprint && !g36.TutorialDone && g36.Keys.Forward == BxW && g36.Keys.Left == BxA && BxRangeErrors(g36).Count == 0;
            BxCheck("O1 old settings files from tests-v4/fixtures + backup-*/ (" + files.Count + " unique, v1..v3.0): load, in range, v4 defaults, engine finite + bounded", bad == 0, ok + " ok, " + bad + " bad", first);
            BxCheck("O2 v3.6-shaped file (no v4 fields, arrow keys for W/A): LeanCancelSprint on, TutorialDone off, mods 0, WASD forced", ok36, "parsed=" + p36 + " " + e36, null);
        }

        // ------------------------------------------------------------------ report

        static void BxWriteReport(double secs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# BRUTE findings (tests-v4/TestsV4Brute.cs)");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " by the test run (" + secs.ToString("0") + " s). Product code was NOT changed.");
            sb.AppendLine();
            sb.AppendLine("## Failures (" + bxFails.Count + ")");
            if (bxFails.Count == 0) sb.AppendLine("None.");
            foreach (string f in bxFails) sb.AppendLine(f);
            sb.AppendLine();
            sb.AppendLine("## Questions / observations (not counted as failures)");
            foreach (string nt in bxNotes) sb.AppendLine("- " + nt);
            try
            {
                string d = "/home/dev/tarkov-crosshair-dev/v4-requests";
                if (Directory.Exists(d)) File.WriteAllText(Path.Combine(d, "BRUTE.md"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Console.WriteLine("BRUTE.md not written: " + ex.Message); }
            Console.WriteLine("BRUTE: " + bxFails.Count + " failures; notes:");
            foreach (string nt in bxNotes) Console.WriteLine("  NOTE " + nt);
        }
    }
}
