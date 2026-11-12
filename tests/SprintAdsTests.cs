// 用途：通过实际输入和显隐计算验证奔跑取消开镜，并检查 v3.2 的原有开镜规则。
using System;
using System.Collections.Generic;
using TarkovCrosshair;

public static class SprintAdsTests
{
    // 只统计行为断言，失败时由场景说明指出具体操作。
    private static int checks;

    /// <summary>测试从按键采集到显隐结果的完整链路，不启动窗口或发送真实按键。</summary>
    public static int Run()
    {
        checks = 0;
        SprintSequences();
        InputBoundaries();
        FocusAndPreview();
        OtherStates();
        UpstreamAdsRules();
        return checks;
    }

    /// <summary>覆盖三种按键顺序，以及通用、左侧和右侧 Shift。</summary>
    private static void SprintSequences()
    {
        foreach (int shift in new int[] { 16, 160, 161 })
        foreach (int first in new int[] { 0, 87, shift })
        {
            Harness h = new Harness();
            h.Press(2);
            if (first != 0) h.Step(first);
            h.Visible(false, "单按前进或 Shift 不取消开镜");
            h.Step(87, shift);
            h.Visible(true, "奔跑后显示准心：" + shift + "/" + first);
            h.Step(87, shift);
            h.Visible(true, "持续奔跑保持未开镜");
            h.Step();
            h.Visible(true, "停止奔跑不会恢复旧开镜记录");
            h.Press(2);
            h.Visible(false, "奔跑后再次右键正常开镜");
            h.Step(87, shift);
            h.Visible(true, "下一次奔跑仍能取消开镜");
            h.Step(87, shift, 2);
            h.Visible(false, "组合一直按住时允许新的右键开镜");
            h.Step(87, shift, 2);
            h.Visible(false, "持续组合不会反复清掉新的开镜记录");
            h.Step(87, shift);
            h.Step(87, shift, 2);
            h.Visible(true, "组合按住期间右键仍能正常关镜");
        }
    }

    /// <summary>取消优先，奔跑独立于 Tab/Esc 开关，并沿用已有改键和方向处理。</summary>
    private static void InputBoundaries()
    {
        Harness h = new Harness();
        h.Step(87, 16, 2);
        h.Visible(true, "奔跑与开镜同帧时取消优先");
        h.Step(2);
        h.Visible(true, "奔跑后右键仍按住不会补触发开镜");
        h.Step();
        h.Press(2);
        h.Visible(false, "松开再按右键可以重新开镜");

        h = new Harness();
        h.Config.Keys.Forward = 70;
        h.Config.AdsCancelTabEsc = false;
        h.Press(2);
        h.Step(87, 16);
        h.Visible(false, "前进改键后 W 不再触发奔跑取消");
        h.Step(70, 16);
        h.Visible(true, "自定义前进键生效，且不依赖 Tab/Esc 开关");

        h = new Harness();
        h.Press(2);
        h.Step(87);
        h.Step(87, 83, 16);
        h.Visible(false, "后按后退时不把仍按住的 W 误认为前进");
        h.Step(87, 16);
        h.Visible(true, "松开后退恢复前进时可以触发奔跑取消");

        h = new Harness();
        h.Config.AdsMode = "hold";
        h.Step(2);
        h.Step(2, 87, 16);
        h.Visible(false, "按住开镜模式仍由开镜键决定显隐");
        h.Step(87, 16);
        h.Visible(true, "按住模式松开开镜键正常显示");
    }

    /// <summary>其他窗口和预览中的组合键不修改实时开镜记录。</summary>
    private static void FocusAndPreview()
    {
        Harness h = new Harness();
        h.Config.OnlyWhenGameFocused = true;
        h.Press(2);
        h.Engine.GameFocused = false;
        h.Engine.OwnForeground = true;
        h.Step(87, 16);
        h.Engine.GameFocused = true;
        h.Step();
        h.Visible(false, "其他窗口的前进加 Shift 不取消开镜");
        h.Step(87, 16);
        h.Visible(true, "回到游戏后奔跑取消生效");

        h = new Harness();
        h.Press(2);
        h.Preview = PreviewMode.Static;
        h.Step(87, 16);
        h.Visible(true, "预览中的前进加 Shift 不影响预览显示");
        h.Preview = PreviewMode.Live;
        h.Step(87, 16);
        h.Visible(false, "返回实时时不把已按住的组合认成新按下");
        h.Step();
        h.Step(87, 16);
        h.Visible(true, "返回实时后重新奔跑可以取消开镜");
    }

    /// <summary>取消开镜不会重置运动状态，显示仍遵守自由视角和渐变设置。</summary>
    private static void OtherStates()
    {
        Harness h = new Harness();
        h.Press(2);
        h.Step(87, 16, 5, 6, 81, 32);
        double jumpMs = h.Engine.JumpMs;
        h.Visible(true, "奔跑取消后正常显示");
        Check(h.Engine.LiveSwitched && h.Engine.Boosted && h.Engine.LeanTarget == -1,
            "奔跑取消不重置换手、强化准星和侧身");
        h.Step(87, 16, 81);
        Check(h.Engine.CurrentDir == 0 && h.Engine.Jumping && h.Engine.JumpMs > jumpMs,
            "奔跑取消不打断前进或重启跳跃计时");

        h = new Harness();
        h.Press(2);
        h.Step(87, 16, 4);
        h.Visible(false, "奔跑取消后自由视角仍优先隐藏");
        h.Step(87, 16);
        h.Visible(true, "松开自由视角后显示");
        h = new Harness();
        h.Press(2);
        h.Config.ShowFadeMs = 50;
        h.Step(87, 16);
        Check(Math.Abs(h.Engine.Alpha - 0.32) < 0.000001, "奔跑取消沿用出现渐变");
    }

    /// <summary>保留 v3.2 的 F12 重置、Tab/Esc 开关和 Alt/Ctrl 右键过滤。</summary>
    private static void UpstreamAdsRules()
    {
        Harness h = new Harness();
        h.Press(2);
        h.Config.Enabled = false;
        h.Step();
        h.Config.Enabled = true;
        h.Step();
        h.Visible(true, "重新启用准星继续清掉开镜记录");
        h.Press(2);
        h.Config.AdsCancelTabEsc = false;
        h.Press(9);
        h.Visible(false, "关闭 Tab/Esc 取消开镜后仍遵守设置");
        h.Config.AdsCancelTabEsc = true;
        h.Press(27);
        h.Visible(true, "开启 Tab/Esc 取消开镜后继续生效");

        foreach (string mode in new string[] { "toggle", "hold" })
        foreach (int modifier in new int[] { 18, 17 })
        {
            h = new Harness();
            h.Config.AdsMode = mode;
            h.Step(modifier, 2);
            h.Visible(true, "切换瞄具不被当成开镜：" + mode);
            h.Step(2);
            h.Visible(true, "先松 Alt/Ctrl 不补触发开镜：" + mode);
            h.Step();
            h.Step(2);
            h.Visible(false, "普通右键继续正常开镜：" + mode);
            h = new Harness();
            h.Config.AdsMode = mode;
            h.Config.AdsIgnoreAltCtrl = false;
            h.Step(modifier, 2);
            h.Visible(false, "关闭组合键过滤后继续遵守设置：" + mode);
        }
    }

    /// <summary>从实际输出检查显隐，避免只验证内部状态变量。</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private sealed class Harness
    {
        // 仅替换物理按键查询，其他步骤都使用生产代码。
        public readonly GlobalSettings Config = GlobalSettings.CreateDefault();
        public readonly Profile Profile;
        public readonly MotionEngine Engine = new MotionEngine();
        public PreviewMode Preview = PreviewMode.Live;
        private readonly LiveInputFilter filter = new LiveInputFilter();
        private readonly HashSet<int> pressed = new HashSet<int>();

        /// <summary>使用点按开镜和即时显隐，让每帧结果可直接断言。</summary>
        public Harness()
        {
            Config.AdsMode = "toggle";
            Config.OnlyWhenGameFocused = false;
            Config.ShowFadeMs = Config.HideFadeMs = 0;
            Profile = Config.Active;
        }

        /// <summary>按渲染线程顺序采集、过滤并处理一帧输入。</summary>
        public void Step(params int[] keys)
        {
            pressed.Clear();
            foreach (int key in keys) pressed.Add(key);
            InputState raw = FocusGate.KeysLive(Config.OnlyWhenGameFocused, Engine.GameFocused)
                ? MotionEngine.ReadInput(Config.Keys, IsDown) : new InputState();
            Engine.Update(0.016, filter.Next(raw, Config.Enabled, Preview == PreviewMode.Live),
                Profile, Config, Preview, 1.0);
        }

        /// <summary>通用 Shift/Alt/Ctrl 查询同时包含左右两侧按键。</summary>
        private bool IsDown(int key)
        {
            if (key == 16) return pressed.Contains(16) || pressed.Contains(160) || pressed.Contains(161);
            if (key == 18) return pressed.Contains(18) || pressed.Contains(164) || pressed.Contains(165);
            if (key == 17) return pressed.Contains(17) || pressed.Contains(162) || pressed.Contains(163);
            return pressed.Contains(key);
        }

        /// <summary>模拟按下再松开一次按键。</summary>
        public void Press(int key) { Step(key); Step(); }

        /// <summary>检查最终透明度，并在失败时保留玩家操作说明。</summary>
        public void Visible(bool expected, string message)
        {
            Check(Engine.Alpha == (expected ? 1.0 : 0.0), message + "，Alpha=" + Engine.Alpha);
        }
    }
}
