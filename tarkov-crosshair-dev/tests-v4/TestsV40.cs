using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TarkovCrosshair.Tests
{
    // v4.0 CORE: 组合键 + W/A/S/D suppression, fixed WASD, 冲刺取消侧身, PR #2 port (冲刺取消开镜), TutorialDone,
    // curve preset list, old-file compatibility, package folder names, brute force.
    public static partial class Program
    {
        const int VkW = 87, VkS = 83, VkA = 65, VkD = 68, VkQ = 81, VkE = 69, VkF = 70, VkSpace = 32;
        const int VkShift = 16, VkLShift = 160, VkRShift = 161, VkCtrl = 17, VkLCtrl = 162, VkRCtrl = 163, VkAlt = 18, VkLAlt = 164, VkRAlt = 165;
        const int VkLmb = 1, VkRmb = 2, VkMmb = 4, VkX1 = 5, VkX2 = 6, VkTabK = 9, VkEsc = 27, VkF12 = 0x7B;

        static void V40Tests()
        {
            PrTwoPort("PR2", false);
            PrTwoPort("PR2[切换侧身+冲刺取消侧身 on]", true);
            SprintCancelOffV40();
            LeanCancelV40();
            ComboV40();
            WasdFixedV40();
            SettingsV40();
            OldFilesV40();
            CurveV40();
            UserDirV40();
            ResolutionV40();
            BruteV40();
        }

        // ------------------------------------------------------------------ harness (same chain as the render thread)

        /// <summary>Generic Shift/Ctrl/Alt include both sides, like GetAsyncKeyState.</summary>
        static bool FakeDown(HashSet<int> pressed, int key)
        {
            if (key == VkShift) return pressed.Contains(VkShift) || pressed.Contains(VkLShift) || pressed.Contains(VkRShift);
            if (key == VkAlt) return pressed.Contains(VkAlt) || pressed.Contains(VkLAlt) || pressed.Contains(VkRAlt);
            if (key == VkCtrl) return pressed.Contains(VkCtrl) || pressed.Contains(VkLCtrl) || pressed.Contains(VkRCtrl);
            return pressed.Contains(key);
        }

        sealed class Hx
        {
            public readonly GlobalSettings Config = GlobalSettings.CreateDefault();
            public readonly Profile Profile;
            public readonly MotionEngine Engine = new MotionEngine();
            public PreviewMode Preview = PreviewMode.Live;
            readonly LiveInputFilter filter = new LiveInputFilter();
            readonly HashSet<int> pressed = new HashSet<int>();
            public InputState LastRaw;

            public Hx()
            {
                Config.AdsMode = "toggle";
                Config.OnlyWhenGameFocused = false;
                Config.ShowFadeMs = Config.HideFadeMs = 0;
                // v5.1 (W23): the fresh-install 强化 key is Ctrl+T now; these v4.0 cases press 侧键2 (VkX2) for it
                Config.Keys.SetCombo(KeyBindings.ActionBoost, KeyBindings.VkXButton2, 0);
                Profile = Config.Active;
            }

            public void Step(params int[] keys)
            {
                pressed.Clear();
                foreach (int key in keys) pressed.Add(key);
                InputState raw = FocusGate.KeysLive(Config.OnlyWhenGameFocused, Engine.GameFocused)
                    ? MotionEngine.ReadInput(Config.Keys, delegate(int k) { return FakeDown(pressed, k); }) : new InputState();
                LastRaw = raw;
                Engine.Update(0.016, filter.Next(raw, Config.Enabled, Preview == PreviewMode.Live), Profile, Config, Preview, 1.0);
            }

            public void Press(int key) { Step(key); Step(); }
        }

        static int prChecks;
        static void PrCheck(string prefix, bool ok, string msg) { prChecks++; Check(prefix + " " + msg, ok, ""); }
        static void Vis(string prefix, Hx h, bool expected, string msg)
        {
            prChecks++;
            Check(prefix + " " + msg, h.Engine.Alpha == (expected ? 1.0 : 0.0), "Alpha=" + h.Engine.Alpha);
        }

        static Hx NewHx(bool toggleLean)
        {
            Hx h = new Hx();
            if (toggleLean) { h.Config.LeanMode = "toggle"; h.Config.LeanCancelSprint = true; }
            return h;
        }

        // ------------------------------------------------------------------ PR #2 SprintAdsTests, every scenario

        static void PrTwoPort(string px, bool toggleLean)
        {
            prChecks = 0;
            // SprintSequences: 3 key orders x generic / left / right Shift
            foreach (int shift in new int[] { VkShift, VkLShift, VkRShift })
                foreach (int first in new int[] { 0, VkW, shift })
                {
                    string c = "(Shift " + shift + ", first " + first + ")";
                    Hx h = NewHx(toggleLean);
                    h.Press(VkRmb);
                    if (first != 0) h.Step(first);
                    Vis(px, h, false, "单按前进或 Shift 不取消开镜 " + c);
                    h.Step(VkW, shift);
                    Vis(px, h, true, "奔跑后显示准心 " + c);
                    h.Step(VkW, shift);
                    Vis(px, h, true, "持续奔跑保持未开镜 " + c);
                    h.Step();
                    Vis(px, h, true, "停止奔跑不会恢复旧开镜记录 " + c);
                    h.Press(VkRmb);
                    Vis(px, h, false, "奔跑后再次右键正常开镜 " + c);
                    h.Step(VkW, shift);
                    Vis(px, h, true, "下一次奔跑仍能取消开镜 " + c);
                    h.Step(VkW, shift, VkRmb);
                    Vis(px, h, false, "组合一直按住时允许新的右键开镜 " + c);
                    h.Step(VkW, shift, VkRmb);
                    Vis(px, h, false, "持续组合不会反复清掉新的开镜记录 " + c);
                    h.Step(VkW, shift);
                    h.Step(VkW, shift, VkRmb);
                    Vis(px, h, true, "组合按住期间右键仍能正常关镜 " + c);
                }

            // InputBoundaries
            {
                Hx h = NewHx(toggleLean);
                h.Step(VkW, VkShift, VkRmb);
                Vis(px, h, true, "奔跑与开镜同帧时取消优先");
                h.Step(VkRmb);
                Vis(px, h, true, "奔跑后右键仍按住不会补触发开镜");
                h.Step();
                h.Press(VkRmb);
                Vis(px, h, false, "松开再按右键可以重新开镜");

                // v4.0: 前后左右 fixed to WASD -> the PR's 「前进改键」 case becomes: a file saying Forward = F changes nothing.
                h = NewHx(toggleLean);
                h.Config.Keys.Forward = VkF;
                h.Config.AdsCancelTabEsc = false;
                h.Press(VkRmb);
                h.Step(VkF, VkShift);
                Vis(px, h, false, "WASD 固定：F + Shift 不是冲刺");
                h.Step();
                h.Step(VkW, VkShift);
                Vis(px, h, true, "WASD 固定：W + Shift 取消开镜，且不依赖 Tab/Esc 开关");

                h = NewHx(toggleLean);
                h.Press(VkRmb);
                h.Step(VkW);
                h.Step(VkW, VkS, VkShift);
                Vis(px, h, false, "后按后退时不把仍按住的 W 误认为前进");
                h.Step(VkW, VkShift);
                Vis(px, h, true, "松开后退恢复前进时可以触发奔跑取消");

                h = NewHx(toggleLean);
                h.Config.AdsMode = "hold";
                h.Step(VkRmb);
                h.Step(VkRmb, VkW, VkShift);
                Vis(px, h, false, "按住开镜模式仍由开镜键决定显隐");
                h.Step(VkW, VkShift);
                Vis(px, h, true, "按住模式松开开镜键正常显示");
            }

            // FocusAndPreview
            {
                Hx h = NewHx(toggleLean);
                h.Config.OnlyWhenGameFocused = true;
                h.Press(VkRmb);
                h.Engine.GameFocused = false;
                h.Engine.OwnForeground = true;
                h.Step(VkW, VkShift);
                h.Engine.GameFocused = true;
                h.Step();
                Vis(px, h, false, "其他窗口的前进加 Shift 不取消开镜");
                h.Step(VkW, VkShift);
                Vis(px, h, true, "回到游戏后奔跑取消生效");

                h = NewHx(toggleLean);
                h.Press(VkRmb);
                h.Preview = PreviewMode.Static;
                h.Step(VkW, VkShift);
                Vis(px, h, true, "预览中的前进加 Shift 不影响预览显示");
                h.Preview = PreviewMode.Live;
                h.Step(VkW, VkShift);
                Vis(px, h, false, "返回实时时不把已按住的组合认成新按下");
                h.Step();
                h.Step(VkW, VkShift);
                Vis(px, h, true, "返回实时后重新奔跑可以取消开镜");
            }

            // OtherStates
            {
                Hx h = NewHx(toggleLean);
                h.Press(VkRmb);
                h.Step(VkW, VkShift, VkX1, VkX2, VkQ, VkSpace);
                double jumpMs = h.Engine.JumpMs;
                Vis(px, h, true, "奔跑取消后正常显示");
                // toggle lean + 冲刺取消侧身: the same-frame Q is cancelled by the sprint edge (cancel wins), so lean 0 there.
                int wantLean = toggleLean ? 0 : -1;
                PrCheck(px, h.Engine.LiveSwitched && h.Engine.Boosted && h.Engine.LeanTarget == wantLean,
                    "奔跑取消不重置换手、强化准星" + (toggleLean ? "（切换侧身：同帧 Q 被冲刺取消）" : "和侧身"));
                h.Step(VkW, VkShift, VkQ);
                PrCheck(px, h.Engine.CurrentDir == 0 && h.Engine.Jumping && h.Engine.JumpMs > jumpMs, "奔跑取消不打断前进或重启跳跃计时");

                h = NewHx(toggleLean);
                h.Press(VkRmb);
                h.Step(VkW, VkShift, VkMmb);
                Vis(px, h, false, "奔跑取消后自由视角仍优先隐藏");
                h.Step(VkW, VkShift);
                Vis(px, h, true, "松开自由视角后显示");
                h = NewHx(toggleLean);
                h.Press(VkRmb);
                h.Config.ShowFadeMs = 50;
                h.Step(VkW, VkShift);
                PrCheck(px, Math.Abs(h.Engine.Alpha - 0.32) < 0.000001, "奔跑取消沿用出现渐变 Alpha=" + h.Engine.Alpha);
            }

            // UpstreamAdsRules
            {
                Hx h = NewHx(toggleLean);
                h.Press(VkRmb);
                h.Config.Enabled = false;
                h.Step();
                h.Config.Enabled = true;
                h.Step();
                Vis(px, h, true, "重新启用准星继续清掉开镜记录");
                h.Press(VkRmb);
                h.Config.AdsCancelTabEsc = false;
                h.Press(VkTabK);
                Vis(px, h, false, "关闭 Tab/Esc 取消开镜后仍遵守设置");
                h.Config.AdsCancelTabEsc = true;
                h.Press(VkEsc);
                Vis(px, h, true, "开启 Tab/Esc 取消开镜后继续生效");

                foreach (string mode in new string[] { "toggle", "hold" })
                    foreach (int modifier in new int[] { VkAlt, VkCtrl })
                    {
                        string c = "(" + mode + ", " + modifier + ")";
                        h = NewHx(toggleLean);
                        h.Config.AdsMode = mode;
                        h.Step(modifier, VkRmb);
                        Vis(px, h, true, "切换瞄具不被当成开镜 " + c);
                        h.Step(VkRmb);
                        Vis(px, h, true, "先松 Alt/Ctrl 不补触发开镜 " + c);
                        h.Step();
                        h.Step(VkRmb);
                        Vis(px, h, false, "普通右键继续正常开镜 " + c);
                        h = NewHx(toggleLean);
                        h.Config.AdsMode = mode;
                        h.Config.AdsIgnoreAltCtrl = false;
                        h.Step(modifier, VkRmb);
                        Vis(px, h, false, "关闭组合键过滤后继续遵守设置 " + c);
                    }
            }
            Console.WriteLine("   info: " + px + " ported scenario checks = " + prChecks);
        }

        // ------------------------------------------------------------------ our extra: 冲刺取消开镜 checkbox off

        static void SprintCancelOffV40()
        {
            Hx h = new Hx();
            h.Config.AdsCancelSprint = false;
            h.Press(VkRmb);
            h.Step(VkW, VkShift);
            bool stays = h.Engine.Alpha == 0 && h.Engine.AdsToggled && !h.Engine.AdsCancelledBySprint;
            h = new Hx();
            h.Press(VkRmb);
            h.Step(VkW, VkShift);
            bool flag = h.Engine.AdsCancelledBySprint;
            h.Step(VkW, VkShift);
            bool flagOnce = !h.Engine.AdsCancelledBySprint;
            Check("V40-S1 ☐冲刺取消开镜 off: Shift+W keeps 开镜; on: AdsCancelledBySprint true only on the cancel frame", stays && flag && flagOnce,
                "off stays=" + stays + " flag=" + flag + " once=" + flagOnce);
        }

        // ------------------------------------------------------------------ 冲刺取消侧身 (v4.0)

        static Hx LeanHx(bool cancel)
        {
            Hx h = new Hx();
            h.Config.LeanMode = "toggle";
            h.Config.LeanCancelSprint = cancel;
            h.Config.AdsMode = "hold";
            return h;
        }

        static void LeanCancelV40()
        {
            // 1 basic: Q latched, Shift+W clears (left/right/generic Shift, both orders)
            bool basic = true; string bd = "";
            foreach (int shift in new int[] { VkShift, VkLShift, VkRShift })
                foreach (int lk in new int[] { VkQ, VkE })
                    foreach (int order in new int[] { 0, 1, 2 })
                    {
                        Hx h = LeanHx(true);
                        h.Press(lk);
                        int want = lk == VkQ ? -1 : 1;
                        bool latched = h.Engine.LeanTarget == want;
                        if (order == 1) { h.Step(VkW); latched = latched && h.Engine.LeanTarget == want; }
                        if (order == 2) { h.Step(shift); latched = latched && h.Engine.LeanTarget == want; }
                        h.Step(VkW, shift);
                        bool cleared = h.Engine.LeanTarget == 0 && h.Engine.LeanCancelled;
                        h.Step(VkW, shift);
                        bool flagOnce = !h.Engine.LeanCancelled && h.Engine.LeanTarget == 0;
                        if (!(latched && cleared && flagOnce)) { basic = false; bd += " " + shift + "/" + lk + "/" + order; }
                    }
            Check("V40-L1 切换侧身: Q/E latched, Shift+W rising edge clears it (generic/left/right Shift, any order); W alone or Shift alone does not",
                basic, basic ? "18 cases ok" : "bad:" + bd);

            // 2 holding the combo does not cancel a NEW lean; releasing and re-sprinting cancels again
            {
                Hx h = LeanHx(true);
                h.Press(VkQ);
                h.Step(VkW, VkShift);
                h.Step(VkW, VkShift, VkE);
                bool newLean = h.Engine.LeanTarget == 1;
                h.Step(VkW, VkShift);
                bool kept = h.Engine.LeanTarget == 1;
                h.Step(VkW);
                h.Step(VkW, VkShift);
                bool again = h.Engine.LeanTarget == 0;
                Check("V40-L2 combo held: a new E lean stays; Shift re-pressed (W still held) cancels again", newLean && kept && again,
                    "new=" + newLean + " kept=" + kept + " again=" + again);
            }
            // 3 cancel wins over a same-frame lean press
            {
                Hx h = LeanHx(true);
                h.Step(VkW, VkShift, VkQ);
                bool a = h.Engine.LeanTarget == 0;
                h.Step(VkW, VkShift);
                h.Step(VkW, VkShift, VkQ);
                bool b = h.Engine.LeanTarget == -1;
                Check("V40-L3 sprint edge + Q in the same frame: cancel wins; Q again while sprinting leans", a && b, "same frame=" + a + " later=" + b);
            }
            // 4 hold mode unaffected; option off unaffected
            {
                Hx h = LeanHx(true); h.Config.LeanMode = "hold";
                h.Step(VkQ); h.Step(VkQ, VkW, VkShift);
                bool hold = h.Engine.LeanTarget == -1 && !h.Engine.LeanCancelled;
                Hx o = LeanHx(false);
                o.Press(VkQ); o.Step(VkW, VkShift);
                bool off = o.Engine.LeanTarget == -1;
                Check("V40-L4 按住侧身: Shift+W does nothing; ☐冲刺取消侧身 off: latch kept", hold && off, "hold=" + hold + " off=" + off);
            }
            // 5 direction filter: W held, S pressed later -> not forward
            {
                Hx h = LeanHx(true);
                h.Press(VkQ);
                h.Step(VkW); h.Step(VkW, VkS); h.Step(VkW, VkS, VkShift);
                bool notFwd = h.Engine.LeanTarget == -1;
                h.Step(VkW, VkShift);
                bool fwd = h.Engine.LeanTarget == 0;
                Check("V40-L5 W held + S pressed later = not forward (no cancel); S released -> W forward again -> cancel", notFwd && fwd, "S=" + notFwd + " back to W=" + fwd);
            }
            // 6 game unfocused, preview, return to Live with combo held
            {
                Hx h = LeanHx(true);
                h.Config.OnlyWhenGameFocused = true;
                h.Press(VkQ);
                h.Engine.GameFocused = false; h.Engine.OwnForeground = true;
                h.Step(VkW, VkShift);
                h.Engine.GameFocused = true;
                h.Step();
                bool unf = h.Engine.LeanTarget == -1;
                Hx p = LeanHx(true);
                p.Press(VkQ);
                p.Preview = PreviewMode.Static; p.Step(VkW, VkShift);
                p.Preview = PreviewMode.Live; p.Step(VkW, VkShift);
                bool noEdge = p.Engine.LeanTarget == -1;
                p.Step(); p.Step(VkW, VkShift);
                bool after = p.Engine.LeanTarget == 0;
                Check("V40-L6 game not in front: no cancel; preview -> Live with Shift+W held is not a new edge; re-sprint cancels",
                    unf && noEdge && after, "unfocused=" + unf + " noEdge=" + noEdge + " after=" + after);
            }
            // 7 does not reset hand / boost / ADS-hold / jump / movement; lean eases back over LeanMs (no jump)
            {
                Hx h = LeanHx(true);
                h.Press(VkX1); h.Press(VkX2); h.Press(VkQ);
                for (int i = 0; i < 40; i++) h.Step();
                double leanBefore = h.Engine.Lean;
                h.Step(VkW, VkShift, VkSpace);
                bool kept = h.Engine.LiveSwitched && h.Engine.LiveBoost && h.Engine.Jumping && h.Engine.CurrentDir == 0 && h.Engine.LeanTarget == 0;
                bool smooth = Math.Abs(h.Engine.Lean - leanBefore) < 0.2 && h.Engine.Lean < 0;
                for (int i = 0; i < 200; i++) h.Step(VkW, VkShift);
                bool settled = Math.Abs(h.Engine.Lean) < 1e-3;
                Check("V40-L7 冲刺取消侧身 keeps 换手/强化/跳跃/前进; lean eases back to 0 over 侧身时间 (no jump)", kept && smooth && settled,
                    "kept=" + kept + " smooth=" + smooth + " (" + F(leanBefore) + " -> " + F(h.Engine.Lean) + ") settled=" + settled);
            }
            // 8 independent of 冲刺取消开镜 and ADS mode
            {
                Hx h = LeanHx(true); h.Config.AdsMode = "toggle"; h.Config.AdsCancelSprint = false;
                h.Press(VkRmb); h.Press(VkE); h.Step(VkW, VkShift);
                bool a = h.Engine.LeanTarget == 0 && h.Engine.AdsToggled;
                Hx k = LeanHx(false); k.Config.AdsMode = "toggle";
                k.Press(VkRmb); k.Press(VkE); k.Step(VkW, VkShift);
                bool b = k.Engine.LeanTarget == 1 && !k.Engine.AdsToggled;
                Check("V40-L8 冲刺取消侧身 and 冲刺取消开镜 are independent switches", a && b, "lean only=" + a + " ads only=" + b);
            }
            // 9 a 组合键 binding on W (Shift+W bound to 强化) suppresses W, so Shift+W is not a sprint
            {
                Hx h = LeanHx(true);
                h.Config.Keys.SetCombo(KeyBindings.ActionBoost, VkW, KeyBindings.ModShift);
                h.Press(VkQ);
                h.Step(VkW, VkShift);
                bool r = h.Engine.LeanTarget == -1 && h.Engine.LiveBoost && !h.LastRaw.Fwd;
                Check("V40-L9 Shift+W bound as a 组合键: W suppressed, so no 冲刺 cancel (组合键 wins)", r, "lean=" + h.Engine.LeanTarget + " boost=" + h.Engine.LiveBoost);
            }
        }

        // ------------------------------------------------------------------ 组合键

        static InputState Read(KeyBindings k, params int[] keys)
        {
            HashSet<int> s = new HashSet<int>(keys);
            return MotionEngine.ReadInput(k, delegate(int v) { return FakeDown(s, v); });
        }

        static void ComboV40()
        {
            KeyBindings k = new KeyBindings();
            k.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
            InputState k1_1 = Read(k, VkLCtrl, VkD), k1_2 = Read(k, VkRCtrl, VkD), k1_3 = Read(k, VkD), k1_4 = Read(k, VkCtrl), k1_5 = Read(k, VkE);
            Check("V40-K1 右侧身 = Ctrl+D: Ctrl+D (left or right Ctrl) leans right and D does NOT move; D alone moves; Ctrl alone nothing; E no longer leans",
                k1_1.LeanR && !k1_1.Right && k1_2.LeanR && !k1_2.Right && !k1_3.LeanR && k1_3.Right && !k1_4.LeanR && !k1_4.Right && !k1_5.LeanR,
                "Ctrl+D lean=" + k1_1.LeanR + " move=" + k1_1.Right + " D lean=" + k1_3.LeanR + " move=" + k1_3.Right);

            // engine: ring does not move right while Ctrl+D is held
            {
                Hx h = new Hx(); h.Config.AdsMode = "hold";
                h.Config.Keys.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                for (int i = 0; i < 100; i++) h.Step(VkCtrl, VkD);
                bool lean = h.Engine.LeanTarget == 1 && h.Engine.MoveX == 0 && h.Engine.MoveY == 0 && h.Engine.CurrentDir == -1;
                for (int i = 0; i < 100; i++) h.Step(VkD);
                bool move = h.Engine.LeanTarget == 0 && h.Engine.CurrentDir == 3;
                for (int i = 0; i < 60; i++) h.Step(VkW, VkCtrl, VkD);
                bool wStill = h.Engine.CurrentDir == 0 && h.Engine.LeanTarget == 1;
                Check("V40-K2 engine: Ctrl+D leans right with no 右移; D alone = 右移; W + Ctrl+D = 前进 + 右侧身 (D suppressed only)", lean && move && wStill,
                    "lean=" + lean + " move=" + move + " W+combo=" + wStill);
            }
            // all mods required; extra mods on a plain binding do not matter; Shift/Alt sides
            {
                KeyBindings m = new KeyBindings();
                m.SetCombo(KeyBindings.ActionBoost, VkD, KeyBindings.ModCtrl | KeyBindings.ModShift);
                bool need = !Read(m, VkCtrl, VkD).Boost && !Read(m, VkShift, VkD).Boost && Read(m, VkLCtrl, VkRShift, VkD).Boost;
                bool sup = Read(m, VkCtrl, VkD).Right && !Read(m, VkCtrl, VkShift, VkD).Right;
                KeyBindings p = new KeyBindings();
                bool plain = Read(p, VkCtrl, VkAlt, VkShift, VkE).LeanR && Read(p, VkCtrl, VkD).Right;
                m.SetCombo(KeyBindings.ActionJump, VkSpace, KeyBindings.ModAlt);
                bool alt = Read(m, VkLAlt, VkSpace).Jump && Read(m, VkRAlt, VkSpace).Jump && !Read(m, VkSpace).Jump;
                Check("V40-K3 every modifier of a 组合键 is required; partial combo = plain D moves; plain bindings ignore extra modifiers; Alt sides",
                    need && sup && plain && alt, "need=" + need + " suppress=" + sup + " plain=" + plain + " alt=" + alt);
            }
            // every rebindable action can be a combo
            {
                bool all = true; string bad = "";
                foreach (int act in KeyBindings.RebindableActions)
                {
                    if (act == KeyBindings.ActionToggle) continue;
                    KeyBindings m = new KeyBindings();
                    m.SetCombo(act, 0x4B /* K */, KeyBindings.ModCtrl);
                    InputState on = Read(m, VkCtrl, 0x4B), off = Read(m, 0x4B);
                    bool got = ActionState(on, act) && !ActionState(off, act) && m.GetMods(act) == KeyBindings.ModCtrl && m.Get(act) == 0x4B;
                    if (!got) { all = false; bad += " " + act; }
                }
                KeyBindings t = new KeyBindings(); t.SetCombo(KeyBindings.ActionToggle, VkF12, KeyBindings.ModShift);
                HashSet<int> s1 = new HashSet<int> { VkShift, VkF12 }, s2 = new HashSet<int> { VkF12 };
                bool tog = MotionEngine.IsActionDown(t, 0, delegate(int v) { return FakeDown(s1, v); }) && !MotionEngine.IsActionDown(t, 0, delegate(int v) { return FakeDown(s2, v); });
                Check("V40-K4 every rebindable key accepts a 组合键 (开火/开镜/自由视角/换手/侧身/跳跃/强化 via ReadInput; 开关准星 via IsActionDown)",
                    all && tog, "bad:" + bad + " toggle=" + tog);
            }
            // movement actions refuse combos; Normalize; names
            {
                KeyBindings m = new KeyBindings();
                m.SetCombo(KeyBindings.ActionRight, 0x4C, KeyBindings.ModCtrl); m.SetMods(KeyBindings.ActionForward, 7);
                bool fixedKeys = m.Right == VkD && m.GetMods(KeyBindings.ActionRight) == 0 && m.GetMods(KeyBindings.ActionForward) == 0 && m.Forward == VkW;
                m.SetCombo(KeyBindings.ActionFire, VkCtrl, KeyBindings.ModCtrl | KeyBindings.ModAlt | 64);
                bool norm = m.FireMods == KeyBindings.ModAlt;
                bool names = KeyBindings.ComboName(VkD, KeyBindings.ModCtrl) == "Ctrl+D"
                    && KeyBindings.ComboName(VkQ, 7) == "Ctrl+Shift+Alt+Q"
                    && KeyBindings.ComboName(VkRmb, KeyBindings.ModAlt) == "Alt+鼠标右键"
                    && KeyBindings.ComboName(VkF12, 0) == "F12" && KeyBindings.ComboName(VkCtrl, KeyBindings.ModCtrl) == "Ctrl"
                    && KeyBindings.KeyName(VkSpace) == "Space" && KeyBindings.KeyName(VkX1) == "鼠标侧键1（XButton1）" && KeyBindings.KeyName(0x24) == "Home"
                    && KeyBindings.KeyName(0x31) == "1" && KeyBindings.KeyName(0x60) == "小键盘0" && KeyBindings.KeyName(0xFE).Length > 0;
                bool namesAll = true;
                for (int v = 1; v <= 254; v++) for (int mm = 0; mm < 8; mm++) { string n = KeyBindings.ComboName(v, mm); if (string.IsNullOrEmpty(n)) namesAll = false; }
                KeyBindings cap = new KeyBindings();
                HashSet<int> held = new HashSet<int> { VkLCtrl, VkRAlt };
                bool modsDown = KeyBindings.ModsDown(delegate(int v) { return FakeDown(held, v); }) == (KeyBindings.ModCtrl | KeyBindings.ModAlt);
                cap.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                bool combo = cap.ComboOf(KeyBindings.ActionLeanRight) == "Ctrl+D";
                Check("V40-K5 前后左右 refuse keys/combos; mods normalised (own bit and unknown bits dropped); names 「Ctrl+D」 etc. for every key; ModsDown for capture",
                    fixedKeys && norm && names && namesAll && modsDown && combo, "fixed=" + fixedKeys + " norm=" + norm + " names=" + names + " all=" + namesAll + " modsDown=" + modsDown);
            }
            // conflicts
            {
                KeyBindings m = new KeyBindings();
                m.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                bool a1 = m.FindOtherCombo(VkD, 0, KeyBindings.ActionLeanRight) == KeyBindings.ActionRight;   // plain D = 右移
                bool a2 = m.FindOtherCombo(VkD, KeyBindings.ModCtrl, KeyBindings.ActionBoost) == KeyBindings.ActionLeanRight;
                bool a3 = m.FindOtherCombo(VkD, KeyBindings.ModAlt, KeyBindings.ActionBoost) == -1;
                bool a4 = m.FindOther(VkA, KeyBindings.ActionLeanLeft) == KeyBindings.ActionLeft;
                Check("V40-K6 「这个键已经用于」: same key AND same modifiers only (Ctrl+D ≠ D, Alt+D free)", a1 && a2 && a3 && a4, a1 + " " + a2 + " " + a3 + " " + a4);
            }
            // two combos on the same movement key; combo on W
            {
                KeyBindings m = new KeyBindings();
                m.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                m.SetCombo(KeyBindings.ActionBoost, VkD, KeyBindings.ModAlt);
                m.SetCombo(KeyBindings.ActionLeanLeft, VkA, KeyBindings.ModCtrl);
                m.SetCombo(KeyBindings.ActionJump, VkW, KeyBindings.ModAlt);
                InputState i1 = Read(m, VkAlt, VkD), i2 = Read(m, VkCtrl, VkA, VkD), i3 = Read(m, VkAlt, VkW, VkS), i4 = Read(m, VkW, VkA, VkS, VkD);
                bool r = i1.Boost && !i1.Right && !i1.LeanR && i2.LeanL && i2.LeanR && !i2.Left && !i2.Right && i3.Jump && !i3.Fwd && i3.Back && i4.Fwd && i4.Left && i4.Back && i4.Right;
                Check("V40-K7 several 组合键 on W/A/D: each suppresses only its own movement key; no modifier = all four move", r, "");
            }
            // ADS combo vs the Alt/Ctrl+右键 rule
            {
                Hx h = new Hx(); h.Config.AdsMode = "hold";
                h.Config.Keys.SetCombo(KeyBindings.ActionAds, VkRmb, KeyBindings.ModCtrl);
                h.Step(VkCtrl, VkRmb);
                bool comboAds = h.Engine.Alpha == 0;
                h.Step(VkRmb);
                bool plainNot = h.Engine.Alpha == 1;
                Hx p = new Hx(); p.Config.AdsMode = "hold";
                p.Step(VkCtrl, VkRmb);
                bool ruleKept = p.Engine.Alpha == 1;
                Check("V40-K8 开镜 = Ctrl+右键 bound on purpose counts as 开镜; plain 右键 keeps the Alt/Ctrl+右键 = 切瞄具 rule", comboAds && plainNot && ruleKept,
                    "combo=" + comboAds + " plain right alone=" + plainNot + " rule=" + ruleKept);
            }
            // save / load / old file
            {
                GlobalSettings g = GlobalSettings.CreateDefault();
                g.Keys.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                g.Keys.SetCombo(KeyBindings.ActionToggle, VkF12, KeyBindings.ModShift | KeyBindings.ModAlt);
                string json = SettingsIO.Serialize(g);
                GlobalSettings b; string err;
                bool rt = SettingsIO.TryParse(json, out b, out err) && b.Keys.LeanRightMods == 1 && b.Keys.ToggleMods == 6 && b.Keys.LeanRight == VkD && SettingsIO.Serialize(b) == json;
                JsonNode n = JsonNode.Parse(json);
                foreach (string f in new string[] { "LeanLeftMods", "LeanRightMods", "FireMods", "AdsMods", "ToggleMods", "FreeLookMods", "JumpMods", "HandSwitchMods", "BoostMods" })
                    n["Keys"].AsObject().Remove(f);
                GlobalSettings o;
                bool old = SettingsIO.TryParse(n.ToJsonString(), out o, out err);
                bool zero = old;
                if (old) foreach (int act in KeyBindings.RebindableActions) if (o.Keys.GetMods(act) != 0) zero = false;
                JsonNode bad = JsonNode.Parse(json); bad["Keys"]["FireMods"] = 255; bad["Keys"]["AdsMods"] = -3;
                GlobalSettings bb; bool clamp = SettingsIO.TryParse(bad.ToJsonString(), out bb, out err) && bb.Keys.FireMods == 7 && bb.Keys.AdsMods == (-3 & 7);
                GlobalSettings rk = GlobalSettings.CreateDefault(); rk.Keys.SetCombo(5, VkA, 1); rk.LeanCancelSprint = false; rk.ResetKeys();
                bool reset = rk.Keys.LeanLeftMods == 0 && rk.Keys.LeanLeft == VkQ && rk.LeanCancelSprint;
                Check("V40-K9 组合键 saved + reloaded exactly; older file (no *Mods) = plain keys; bad mods masked; 按键恢复默认 clears combos",
                    rt && zero && clamp && reset, "round trip=" + rt + " old=" + zero + " clamp=" + clamp + " reset=" + reset);
            }
        }

        static bool ActionState(InputState i, int act)
        {
            switch (act)
            {
                case 1: return i.Fire;
                case 2: return i.Ads;
                case 3: return i.FreeLook;
                case 4: return i.HandSwitch;
                case 5: return i.LeanL;
                case 6: return i.LeanR;
                case 11: return i.Jump;
                case 12: return i.Boost;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ fixed WASD

        static void WasdFixedV40()
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            JsonNode n = JsonNode.Parse(SettingsIO.Serialize(g));
            n["Keys"]["Forward"] = 0x26; n["Keys"]["Back"] = 0x28; n["Keys"]["Left"] = 0x25; n["Keys"]["Right"] = 0x27;
            GlobalSettings b; string err;
            bool forced = SettingsIO.TryParse(n.ToJsonString(), out b, out err) && b.Keys.Forward == VkW && b.Keys.Back == VkS && b.Keys.Left == VkA && b.Keys.Right == VkD;
            KeyBindings k = new KeyBindings(); k.Forward = 0x26; k.Right = 0x27; // never validated: ReadInput still uses W/S/A/D
            InputState i = Read(k, 0x26, 0x27), j = Read(k, VkW, VkD);
            bool read = !i.Fwd && !i.Right && j.Fwd && j.Right;
            KeyBindings kd = new KeyBindings(); GlobalSettings c = GlobalSettings.CreateDefault();
            bool defs = kd.Forward == VkW && kd.Back == VkS && kd.Left == VkA && kd.Right == VkD && c.Keys.Forward == VkW;
            bool page = Array.IndexOf(KeyBindings.RebindableActions, 7) < 0 && Array.IndexOf(KeyBindings.RebindableActions, 10) < 0 && KeyBindings.RebindableActions.Length == 9;
            Check("V40-W1 前后左右 fixed W/S/A/D: old file with arrow keys is forced on load; ReadInput always reads W/S/A/D; not on the 按键 page",
                forced && read && defs && page, "forced=" + forced + " read=" + read + " defaults=" + defs + " page=" + page);
        }

        // ------------------------------------------------------------------ new globals

        static void SettingsV40()
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            bool defs = g.LeanCancelSprint && !g.TutorialDone && new GlobalSettings().LeanCancelSprint && !new GlobalSettings().TutorialDone;
            g.TutorialDone = true; g.LeanCancelSprint = false;
            string json = SettingsIO.Serialize(g);
            GlobalSettings b; string err;
            bool rt = SettingsIO.TryParse(json, out b, out err) && b.TutorialDone && !b.LeanCancelSprint;
            JsonNode n = JsonNode.Parse(json); n.AsObject().Remove("TutorialDone"); n.AsObject().Remove("LeanCancelRun");
            GlobalSettings o; bool old = SettingsIO.TryParse(n.ToJsonString(), out o, out err) && !o.TutorialDone && o.LeanCancelSprint;
            Check("V40-G1 TutorialDone (default false) and LeanCancelSprint (default true): saved, reloaded; older files -> false / true",
                defs && rt && old, "defaults=" + defs + " round trip=" + rt + " old=" + old);
        }

        // ------------------------------------------------------------------ old v3.x files keep every value

        static readonly string[] ForcedOnLoad = { "Version", "Keys.Forward", "Keys.Back", "Keys.Left", "Keys.Right", "ResBaseW", "ResBaseH", "StandOffsetX", "StandOffsetY", "CenterOffsetX", "CenterOffsetY" };

        static void Leaves(JsonElement e, string path, Dictionary<string, string> into)
        {
            if (e.ValueKind == JsonValueKind.Object) { foreach (JsonProperty p in e.EnumerateObject()) Leaves(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name, into); }
            else if (e.ValueKind == JsonValueKind.Array) { int i = 0; foreach (JsonElement x in e.EnumerateArray()) Leaves(x, path + "[" + (i++) + "]", into); }
            else into[path] = e.ValueKind == JsonValueKind.Number ? e.GetDouble().ToString("R") : e.ToString();
        }

        static bool Forced(string path)
        {
            string last = path;
            int dot = path.LastIndexOf('.');
            string tail = dot >= 0 ? path.Substring(dot + 1) : path;
            foreach (string f in ForcedOnLoad) if (path == f || path.EndsWith("." + f) || tail == f) return true;
            if (path.Contains("Dirs[4]") || path.Contains("Dirs[5]") || path.Contains("Dirs[6]") || path.Contains("Dirs[7]")) return true; // diagonals automatic since v3.0
            return false;
        }

        static void OldFilesV40()
        {
            // (a) Leo's real v3.0 file + (b) a v3.6-shaped file: every v3.x field set to a non-default value, v4.0 fields removed.
            List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
            files.Add(new KeyValuePair<string, string>("leo-v30-1027-settings.json", System.IO.File.ReadAllText(Fixture("leo-v30-1027-settings.json"))));
            {
                GlobalSettings g = GlobalSettings.CreateDefault();
                g.Keys.Toggle = 0x75; g.Keys.Fire = 0x4C; g.Keys.Ads = 0x4D; g.Keys.FreeLook = 0x4E; g.Keys.HandSwitch = 0x4F; g.Keys.LeanLeft = 0x31; g.Keys.LeanRight = 0x32; g.Keys.Jump = 0x33; g.Keys.Boost = 0x34;
                g.AdsMode = "toggle"; g.LeanMode = "toggle"; g.HandMode = "hold"; g.HandEnabled = false; g.BoostEnabled = false; g.MonitorIndex = 2;
                g.FrameMode = "fixed"; g.Fps = 240; g.ShowFadeMs = 33; g.HideFadeMs = 44; g.SettingsTopMost = false; g.ShowSettingsOnStart = false; g.HideOnFreeLook = false;
                g.ResGameW = 2560; g.ResGameH = 1600; g.OnlyWhenGameFocused = false; g.GameWindowTitle = "Tarkov X"; g.Autostart = true; g.SplashOnStart = false;
                g.AdsCancelTabEsc = false; g.AdsIgnoreAltCtrl = false; g.AdsCancelSprint = false; g.Enabled = false;
                Profile p = g.Profiles[0];
                p.Diameter = 21; p.Thickness = 2.5; p.Color = "#123456"; p.Shape = Profile.Shapes[Profile.Shapes.Length - 1]; p.CrossGap = 7; p.GlowPx = 3;
                p.Dirs[0] = new DirSetting(1.5, -40); p.Dirs[1] = new DirSetting(-2, 13); p.Dirs[2] = new DirSetting(70, 1); p.Dirs[3] = new DirSetting(-28, 2); Profile.RecomputeDiagonals(p);
                p.CurveAD = new MoveCurve(250, MotionCurve.V23Feel()); p.CurveWS = new MoveCurve(123, new double[] { 5, 50, 95, 40, 3 });
                p.RecoilMaxPx = 61; p.RecoilRiseMs = 333; p.RecoilClickKickPx = 9; p.RecoilFallMs = 222; p.RecoilDelayMs = 41; p.RecoilDriftX = -4;
                p.LeanQX = -7; p.LeanQY = -21; p.LeanEX = 11; p.LeanEY = -19; p.LeanMs = 260; p.HandSwitchMs = 410;
                p.SetHandCustom(Profile.HandDrift, 3, 0);
                p.Jump.Enabled = false; p.Jump.Phases[0].EndPx = -120;
                Profile q = Profile.CreateBlank("第二套 AK"); q.Validate(); q.Dirs[0] = new DirSetting(0, -10); Profile.RecomputeDiagonals(q); g.Profiles.Add(q); g.ActiveProfile = q.Name;
                p.SavedDefault = p.CloneValues(); p.SavedDefaultAt = "2026-11-13 10:00";
                g.Validate();
                JsonNode n = JsonNode.Parse(SettingsIO.Serialize(g));
                n.AsObject().Remove("TutorialDone"); n.AsObject().Remove("LeanCancelRun");
                foreach (string f in new string[] { "LeanLeftMods", "LeanRightMods", "FireMods", "AdsMods", "ToggleMods", "FreeLookMods", "JumpMods", "HandSwitchMods", "BoostMods" })
                    n["Keys"].AsObject().Remove(f);
                files.Add(new KeyValuePair<string, string>("v3.6-shaped (every field non-default)", n.ToJsonString()));
            }
            foreach (KeyValuePair<string, string> f in files)
            {
                GlobalSettings g; string err;
                bool ok = SettingsIO.TryParse(f.Value, out g, out err);
                List<string> diffs = new List<string>(); int compared = 0;
                if (ok)
                {
                    Dictionary<string, string> before = new Dictionary<string, string>(), after = new Dictionary<string, string>();
                    using (JsonDocument d = JsonDocument.Parse(f.Value)) Leaves(d.RootElement, "", before);
                    using (JsonDocument d = JsonDocument.Parse(SettingsIO.Serialize(g))) Leaves(d.RootElement, "", after);
                    foreach (KeyValuePair<string, string> kv in before)
                    {
                        if (Forced(kv.Key)) continue;
                        string v;
                        compared++;
                        if (!after.TryGetValue(kv.Key, out v)) diffs.Add(kv.Key + " missing");
                        else if (v != kv.Value) diffs.Add(kv.Key + ": " + kv.Value + " -> " + v);
                    }
                    ok = ok && g.LeanCancelSprint && !g.TutorialDone;
                    foreach (int act in KeyBindings.RebindableActions) if (g.Keys.GetMods(act) != 0) ok = false;
                }
                Check("V40-O1 " + f.Key + ": every saved value kept on load (except forced WASD / 站着偏移 / 调参分辨率 / auto diagonals); new fields default",
                    ok && diffs.Count == 0 && compared > 50, "compared=" + compared + (diffs.Count > 0 ? " diffs: " + string.Join("; ", diffs.GetRange(0, Math.Min(8, diffs.Count))) : "") + (err ?? ""));
            }
        }

        // ------------------------------------------------------------------ curve

        static void CurveV40()
        {
            Profile p = Profile.CreateLeoDefault("x"); // v5.0.2 (W18, UI-C H1): 推荐 = the built-in 「Leo默认」 curve, 430 ms 0/15/45/75/100
            bool rec = p.CurveAD.IsRecommended && p.CurveWS.IsRecommended && p.CurveAD.DurationMs == 430 && string.Join(",", p.CurveAD.Speeds) == "0,15,45,75,100";
            bool gone = true; foreach (string s in MotionCurve.PresetNames) if (s.Contains("手感") || s.Contains("v2.3") || s.Contains("v3.2")) gone = false;
            p.CurveAD.DurationMs = 100; p.CurveWS.SetPreset(3);
            bool changed = !p.CurveAD.IsRecommended && !p.CurveWS.IsRecommended;
            p.ResetCurvesToRecommended();
            bool back = p.CurveAD.IsRecommended && p.CurveWS.IsRecommended;
            bool uiPresets = true;
            for (int i = 0; i < MotionCurve.PresetNames.Length; i++) { MoveCurve c = new MoveCurve(); c.SetPreset(i); c.Validate(); if (c.Speeds.Length != 5) uiPresets = false; }
            Check("V40-C1 curve: 「v2.3 手感」 gone from the preset list; 推荐 = Leo默认 430 ms (0/15/45/75/100); ResetCurvesToRecommended; old curve values still load (see O1)",
                rec && gone && changed && back && uiPresets, "rec=" + rec + " gone=" + gone + " list=" + string.Join("/", MotionCurve.PresetNames));
        }

        // ------------------------------------------------------------------ package folder names

        static void UserDirV40()
        {
            string root = "/x/HipAimer";
            HashSet<string> files = new HashSet<string>();
            Func<string, bool> ex = delegate(string f) { return files.Contains(f.Replace('\\', '/')); };
            Func<string, Func<string, bool>, string> UserDirFor = delegate(string d, Func<string, bool> fe) { return (SettingsIO.UserDirFor(d, fe) ?? "").Replace('\\', '/'); }; // v5.1.1: Windows gives \
            files.Add(root + "/启动 Start-HipAimer.bat");
            bool newNames = UserDirFor(root + "/程序 app/", ex) == root;
            bool oldFolderNewBat = UserDirFor(root + "/app", ex) == root;
            files.Clear(); files.Add(root + "/Start-HipAimer.bat");
            bool v36 = UserDirFor(root + "/app/", ex) == root && UserDirFor(root + "/程序 app", ex) == root;
            files.Clear();
            bool noBat = UserDirFor(root + "/程序 app/", ex) == root + "/程序 app";
            bool other = UserDirFor(root + "/bin", delegate(string f) { return true; }) == root + "/bin";
            bool cross = SettingsIO.CrosshairFolderName == "准星样式 Crosshairs";
            Check("V40-U1 user folder: 「程序 app」 or 「app」 next to 「启动 Start-HipAimer.bat」 or 「Start-HipAimer.bat」; no bat = exe folder; 准星样式 Crosshairs name",
                newNames && oldFolderNewBat && v36 && noBat && other && cross, newNames + " " + oldFolderNewBat + " " + v36 + " " + noBat + " " + other + " " + cross);
        }

        // ------------------------------------------------------------------ 游戏分辨率 auto-detect (v4.0)

        static void ResolutionV40()
        {
            int[][] screens = { new[] { 1280, 720 }, new[] { 1366, 768 }, new[] { 1600, 900 }, new[] { 1920, 1080 }, new[] { 1920, 1200 },
                new[] { 2160, 1440 }, new[] { 2560, 1080 }, new[] { 2560, 1440 }, new[] { 2560, 1600 }, new[] { 3440, 1440 }, new[] { 3840, 1600 }, new[] { 3840, 2160 } };
            string res = ""; bool all = true;
            foreach (int[] sc in screens)
            {
                // first run: no settings.json -> Load writes the defaults with the detected screen
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v40res-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                string f = System.IO.Path.Combine(dir, "settings.json");
                int[] copy = sc;
                SettingsIO.ScreenProbe = delegate { return new int[] { copy[0], copy[1] }; };
                string msg;
                GlobalSettings g = SettingsIO.Load(f, out msg);
                GlobalSettings again = SettingsIO.Load(f, out msg);   // saved: second start keeps it (no re-detect over the file)
                SettingsIO.ScreenProbe = null;
                System.IO.File.Delete(f); System.IO.Directory.Delete(dir);
                double want = sc[1] / 1440.0;
                bool det = g.ResGameW == sc[0] && g.ResGameH == sc[1] && g.ResBaseW == 2160 && g.ResBaseH == 1440 && Math.Abs(g.ResolutionScale - want) < 1e-12
                    && again.ResGameW == sc[0] && again.ResGameH == sc[1] && Math.Abs(ResolutionDetect.ScaleFor(sc[1]) - want) < 1e-12;
                // engine: every offset scales by s on both axes (D held, then Q lean, then recoil)
                Profile p = g.Active; g.ShowFadeMs = 0; g.OnlyWhenGameFocused = false;
                g.ResAspect = "auto"; g.ResStretched = null; // X18: the scale-by-height rule is checked at the native aspect (first run now 5:4 拉伸)
                GlobalSettings g0 = g.Clone(false); g0.Profiles.Add(p); g0.ResGameW = 2160; g0.ResGameH = 1440;
                double worst = 0;
                foreach (string k in new string[] { "D", "W", "SA", "Q", "EL", "L" })
                {
                    MotionEngine a = new MotionEngine(), b = new MotionEngine();
                    for (int i = 0; i < 1500; i++) { a.Update(0.001, In(k), p, g, PreviewMode.Live, 1); b.Update(0.001, In(k), p, g0, PreviewMode.Live, 1); }
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.OffsetX - want * b.OffsetX), Math.Abs(a.OffsetY - want * b.OffsetY)));
                }
                bool eng = worst < 1e-9;
                if (!(det && eng)) all = false;
                res += sc[0] + "x" + sc[1] + " s=" + F(want) + (det && eng ? "" : " BAD(det=" + det + " diff=" + worst + ")") + "  ";
            }
            Check("V40-R1 first run (no settings.json): 游戏分辨率 = detected screen, saved; offsets scale by height/1440 on both axes, 1280x720 .. 3840x2160 + ultrawide",
                all, res);

            // probe failures, clamps, existing file untouched, re-detect helper
            GlobalSettings h = GlobalSettings.CreateDefault();
            bool none = !h.DetectGameResolution(null) && !h.DetectGameResolution(delegate { return null; })
                && !h.DetectGameResolution(delegate { return new int[] { 0, 1080 }; }) && !h.DetectGameResolution(delegate { return new int[] { 1920 }; })
                && !h.DetectGameResolution(delegate { throw new InvalidOperationException(); }) && h.ResGameW == 1920 && h.ResGameH == 1440;
            bool clamp = h.DetectGameResolution(delegate { return new int[] { 100, 100 }; }) && h.ResGameW == 640 && h.ResGameH == 480
                && h.DetectGameResolution(delegate { return new int[] { 15360, 8640 }; }) && h.ResGameW == 7680 && h.ResGameH == 4320;
            bool redetect = h.DetectGameResolution(delegate { return new int[] { 2560, 1440 }; }) && h.ResGameW == 2560 && h.ResGameH == 1440 && h.ResolutionScale == 1.0;
            string dir2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v40res-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir2);
            string f2 = System.IO.Path.Combine(dir2, "settings.json");
            GlobalSettings mine = GlobalSettings.CreateDefault(); mine.ResGameW = 1920; mine.ResGameH = 1080; SettingsIO.Save(mine, f2);
            SettingsIO.ScreenProbe = delegate { return new int[] { 3840, 2160 }; };
            string m2; GlobalSettings loaded = SettingsIO.Load(f2, out m2);
            SettingsIO.ScreenProbe = delegate { throw new Exception("no screen"); };
            System.IO.File.Delete(f2);
            GlobalSettings fresh = SettingsIO.Load(f2, out m2);
            SettingsIO.ScreenProbe = null;
            System.IO.File.Delete(f2); System.IO.Directory.Delete(dir2);
            bool kept = loaded.ResGameW == 1920 && loaded.ResGameH == 1080;
            bool fallback = fresh.ResGameW == 1800 && fresh.ResGameH == 1440 && m2 == null; // Leo 21 Nov 2026 tuned default: his 1800×1440
            Check("V40-R2 detect fails (null / 0 / short / throws) = built-in 1800x1440 (Leo 21 Nov 2026) kept; sizes clamped 640..7680 x 480..4320; DetectGameResolution re-detects; an existing settings.json is never overwritten",
                none && clamp && redetect && kept && fallback, "none=" + none + " clamp=" + clamp + " redetect=" + redetect + " existing kept=" + kept + " fallback=" + fallback);
        }

        // ------------------------------------------------------------------ brute force

        /// <summary>v3.6 ReadInput, verbatim logic (reference for the "no 组合键 = unchanged" invariant).</summary>
        static InputState ReadInputV36(KeyBindings k, Func<int, bool> isDown)
        {
            InputState i = new InputState();
            i.Fwd = isDown(k.Forward); i.Back = isDown(k.Back); i.Left = isDown(k.Left); i.Right = isDown(k.Right);
            i.LeanL = isDown(k.LeanLeft); i.LeanR = isDown(k.LeanRight);
            i.Fire = isDown(k.Fire); i.Ads = isDown(k.Ads); i.FreeLook = isDown(k.FreeLook);
            i.HandSwitch = isDown(k.HandSwitch);
            i.Jump = isDown(k.Jump);
            i.Boost = isDown(k.Boost);
            i.Cancel = isDown(0x09) || isDown(0x1B);
            i.Mod = isDown(0x12) || isDown(0x11);
            i.Shift = isDown(0x10);
            return i;
        }

        static bool SameIn(InputState a, InputState b)
        {
            return a.Fwd == b.Fwd && a.Back == b.Back && a.Left == b.Left && a.Right == b.Right && a.LeanL == b.LeanL && a.LeanR == b.LeanR
                && a.Fire == b.Fire && a.Ads == b.Ads && a.FreeLook == b.FreeLook && a.HandSwitch == b.HandSwitch && a.Jump == b.Jump
                && a.Boost == b.Boost && a.Cancel == b.Cancel && a.Mod == b.Mod && a.Shift == b.Shift;
        }

        static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        static readonly int[] Pool = { VkW, VkA, VkS, VkD, VkQ, VkE, VkSpace, VkLShift, VkRShift, VkLCtrl, VkRCtrl, VkLAlt, VkRAlt,
            VkLmb, VkRmb, VkMmb, VkX1, VkX2, VkTabK, VkEsc, VkF, 0x4B };

        static void BruteV40()
        {
            Random rnd = new Random(4040);
            const int Frames = 100000;
            // B1: no 组合键 -> ReadInput identical to v3.6 for 100k random key sets
            {
                KeyBindings k = new KeyBindings(); int bad = 0;
                HashSet<int> s = new HashSet<int>();
                Func<int, bool> dn = delegate(int v) { return FakeDown(s, v); };
                for (int f = 0; f < Frames; f++)
                {
                    s.Clear(); foreach (int v in Pool) if (rnd.Next(4) == 0) s.Add(v);
                    if (!SameIn(MotionEngine.ReadInput(k, dn), ReadInputV36(k, dn))) bad++;
                }
                Check("V40-B1 100k random frames, no 组合键: ReadInput identical to v3.6", bad == 0, "mismatches=" + bad);
            }
            // B2: random 组合键 bindings -> per-frame invariants (combo down rule, suppression, plain = v3.6)
            {
                int bad = 0; string first = "";
                HashSet<int> s = new HashSet<int>();
                Func<int, bool> dn = delegate(int v) { return FakeDown(s, v); };
                int[] keysPool = { VkW, VkA, VkS, VkD, VkQ, VkE, VkSpace, VkLmb, VkRmb, VkMmb, VkX1, VkX2, VkF, 0x4B };
                KeyBindings k = null;
                for (int f = 0; f < Frames; f++)
                {
                    if (f % 500 == 0)
                    {
                        k = new KeyBindings();
                        foreach (int act in KeyBindings.RebindableActions)
                            if (rnd.Next(3) == 0) k.SetCombo(act, keysPool[rnd.Next(keysPool.Length)], rnd.Next(8));
                    }
                    s.Clear(); foreach (int v in Pool) if (rnd.Next(3) == 0) s.Add(v);
                    InputState r = MotionEngine.ReadInput(k, dn);
                    InputState plain = ReadInputV36(k, dn);
                    bool w = dn(VkW), a = dn(VkA), sk = dn(VkS), d = dn(VkD);
                    foreach (int act in KeyBindings.RebindableActions)
                    {
                        int vk = k.Get(act), m = k.GetMods(act);
                        bool want = dn(vk) && ((m & 1) == 0 || dn(VkCtrl)) && ((m & 2) == 0 || dn(VkShift)) && ((m & 4) == 0 || dn(VkAlt));
                        // v5.1 (W25): 强化 is EXACTLY its combo (no extra modifier held) when it has modifiers
                        int nm = KeyBindings.NormalizeMods(vk, m), om = nm | KeyBindings.ModBitOf(vk);
                        bool wantAct = want;
                        if (act == KeyBindings.ActionBoost && nm != 0)
                            wantAct = want && ((om & 1) != 0 || !dn(VkCtrl)) && ((om & 2) != 0 || !dn(VkShift)) && ((om & 4) != 0 || !dn(VkAlt));
                        if (act != 0 && ActionState(r, act) != wantAct) { bad++; if (first.Length == 0) first = "action " + act; }
                        if (act == 0 && MotionEngine.IsActionDown(k, 0, dn) != want) { bad++; if (first.Length == 0) first = "toggle"; }
                        if (m != 0 && want)
                        {
                            if (vk == VkW) w = false; if (vk == VkA) a = false; if (vk == VkS) sk = false; if (vk == VkD) d = false;
                        }
                    }
                    if (r.Fwd != w || r.Left != a || r.Back != sk || r.Right != d) { bad++; if (first.Length == 0) first = "movement"; }
                    if (r.Cancel != plain.Cancel || r.Mod != plain.Mod || r.Shift != plain.Shift) { bad++; if (first.Length == 0) first = "cancel/mod/shift"; }
                    // invariant: when no modifier is held, combos are never down and everything else equals v3.6
                    if (!dn(VkCtrl) && !dn(VkShift) && !dn(VkAlt))
                    {
                        foreach (int act in KeyBindings.RebindableActions)
                            if (act != 0 && k.GetMods(act) != 0 && ActionState(r, act)) { bad++; if (first.Length == 0) first = "combo without mods"; }
                        if (r.Fwd != plain.Fwd || r.Back != plain.Back || r.Left != plain.Left || r.Right != plain.Right) { bad++; if (first.Length == 0) first = "no-mod movement"; }
                    }
                }
                Check("V40-B2 100k random frames x random 组合键 bindings: combo = key + all mods; W/A/S/D suppressed exactly while a combo on it is down; no modifier held = v3.6",
                    bad == 0, "violations=" + bad + " first=" + first);
            }
            // B3: engine fed random frames (random modes / combos / focus / preview toggles): no NaN, alpha in [0,1], bounded, never stuck
            {
                int nan = 0, stuck = 0, bound = 0, checks = 0; string firstStuck = "";
                Hx h = null;
                for (int f = 0; f < Frames; f++)
                {
                    if (f % 2000 == 0)
                    {
                        h = new Hx();
                        h.Config.LeanMode = rnd.Next(2) == 0 ? "toggle" : "hold";
                        h.Config.AdsMode = rnd.Next(2) == 0 ? "toggle" : "hold";
                        h.Config.HandMode = rnd.Next(2) == 0 ? "toggle" : "hold";
                        h.Config.LeanCancelSprint = rnd.Next(2) == 0;
                        h.Config.AdsCancelSprint = rnd.Next(2) == 0;
                        h.Config.ShowFadeMs = rnd.Next(2) == 0 ? 0 : 50;
                        h.Config.HideFadeMs = rnd.Next(2) == 0 ? 0 : 30;
                        h.Profile.Jump.Enabled = rnd.Next(2) == 0;
                        if (rnd.Next(2) == 0) h.Config.Keys.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                        if (rnd.Next(2) == 0) h.Config.Keys.SetCombo(KeyBindings.ActionLeanLeft, VkA, KeyBindings.ModCtrl);
                        if (rnd.Next(3) == 0) h.Config.Keys.SetCombo(KeyBindings.ActionBoost, VkW, KeyBindings.ModAlt);
                    }
                    if (rnd.Next(400) == 0) h.Preview = h.Preview == PreviewMode.Live ? (PreviewMode)rnd.Next(1, 18) : PreviewMode.Live;
                    if (rnd.Next(300) == 0) h.Engine.GameFocused = !h.Engine.GameFocused;
                    if (rnd.Next(500) == 0) h.Config.Enabled = !h.Config.Enabled;
                    List<int> ks = new List<int>(); foreach (int v in Pool) if (rnd.Next(5) == 0) ks.Add(v);
                    if (rnd.Next(4) == 0) ks.Clear();
                    h.Step(ks.ToArray());
                    MotionEngine e = h.Engine;
                    if (!Finite(e.OffsetX) || !Finite(e.OffsetY) || !Finite(e.Alpha) || !Finite(e.Lean) || !Finite(e.Recoil) || !Finite(e.MoveX) || !Finite(e.MoveY) || e.Alpha < 0 || e.Alpha > 1) nan++;
                    if (Math.Abs(e.OffsetX) > 2000 || Math.Abs(e.OffsetY) > 2000) bound++;
                    // every ~250 frames: let go of everything for 3 s in Live with the game in front -> the ring must settle
                    if (f % 250 == 249)
                    {
                        h.Preview = PreviewMode.Live; h.Engine.GameFocused = true; h.Config.Enabled = true;
                        for (int i = 0; i < 190; i++) h.Step();
                        checks++;
                        bool leanOk = Math.Abs(e.Lean - e.LeanTarget) < 1e-3 && (h.Config.LeanMode == "toggle" || e.LeanTarget == 0);
                        bool ok = e.MoveX == 0 && e.MoveY == 0 && !e.Traveling(0) && !e.Traveling(1) && e.CurrentDir == -1 && !e.Jumping
                            && e.Recoil < 0.05 && leanOk && e.Alpha == (e.AdsToggled ? 0.0 : 1.0);
                        // and one sprint (Shift+W) then release clears the toggle lean when the option is on
                        if (ok && h.Config.LeanMode == "toggle" && h.Config.LeanCancelSprint)
                        {
                            h.Step(VkW, VkLShift);
                            for (int i = 0; i < 190; i++) h.Step();
                            ok = e.LeanTarget == 0 && Math.Abs(e.Lean) < 1e-3 && e.MoveX == 0 && e.MoveY == 0;
                        }
                        if (!ok) { stuck++; if (firstStuck.Length == 0) firstStuck = "f=" + f + " move=(" + F(e.MoveX) + "," + F(e.MoveY) + ") lean=" + F(e.Lean) + "/" + e.LeanTarget + " recoil=" + F(e.Recoil) + " alpha=" + F(e.Alpha) + " ads=" + e.AdsToggled; }
                    }
                }
                Check("V40-B3 100k random engine frames (all modes, 组合键, focus, preview, F12): no NaN, alpha 0..1, |offset| ≤ 2000, ring never stuck after release (" + checks + " checks)",
                    nan == 0 && stuck == 0 && bound == 0, "nan=" + nan + " out of bounds=" + bound + " stuck=" + stuck + " " + firstStuck);
            }
            // B4: combo vs no-combo engines: with 右侧身 = Ctrl+D and NO Ctrl ever held, the engine equals a plain engine where E is never pressed
            {
                Random r2 = new Random(77); double worst = 0;
                Hx a = new Hx(), b = new Hx();
                foreach (Hx x in new Hx[] { a, b }) { x.Config.LeanMode = "toggle"; x.Config.AdsMode = "hold"; }
                b.Config.Keys.SetCombo(KeyBindings.ActionLeanRight, VkD, KeyBindings.ModCtrl);
                int[] pool = { VkW, VkA, VkS, VkD, VkQ, VkE, VkLShift, VkLmb, VkRmb, VkX1, VkSpace };
                for (int f = 0; f < Frames; f++)
                {
                    List<int> ks = new List<int>(); foreach (int v in pool) if (r2.Next(4) == 0) ks.Add(v);
                    b.Step(ks.ToArray());
                    ks.Remove(VkE);
                    a.Step(ks.ToArray());
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.Engine.OffsetX - b.Engine.OffsetX), Math.Abs(a.Engine.OffsetY - b.Engine.OffsetY)));
                }
                Check("V40-B4 100k frames: 右侧身 = Ctrl+D with Ctrl never held behaves exactly like a plain setup with E never pressed", worst == 0, "max |diff|=" + worst);
            }
        }
    }
}
