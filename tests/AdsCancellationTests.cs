// 用途：从实际按键读取到瞄准点显隐验证取消开镜，不依赖窗口或游戏。
using System;
using System.Collections.Generic;
using TarkovCrosshair;

public static class AdsCancellationTests
{
    // 计数只用于报告实际执行的行为断言数量。
    private static int checks;

    /// <summary>执行完整按键序列，断言失败时直接报告对应玩家行为。</summary>
    public static int Run()
    {
        checks = 0;
        InventoryExits();
        CancellationEdges();
        CustomBindings();
        HoldMode();
        FocusAndPreview();
        OtherStates();
        return checks;
    }

    /// <summary>背包中覆盖奇偶次数的物品右键，以及两种退出按键。</summary>
    private static void InventoryExits()
    {
        foreach (int exitKey in new int[] { 9, 27 })
        {
            for (int clicks = 0; clicks <= 3; clicks++)
            {
                Harness h = new Harness();
                h.Press(2);
                h.Press(9);
                for (int i = 0; i < clicks; i++) h.Press(2);
                h.Press(exitKey);
                h.Visible(true, "物品右键后退出背包应显示：" + exitKey + "/" + clicks);
                h.Press(2);
                h.Visible(false, "再次开镜应隐藏");
                h.Press(2);
                h.Visible(true, "再次关镜应显示");
            }
        }
    }

    /// <summary>取消优先，长按不会伪造开镜事件，每个取消键独立记录按下。</summary>
    private static void CancellationEdges()
    {
        Harness h = new Harness();
        h.Press(2);
        h.Press(9);
        h.Press(9);
        h.Press(27);
        h.Visible(true, "连续取消保持未开镜");
        h.Step(2, 9);
        h.Visible(true, "同帧开镜与取消时取消优先");
        h.Step(2);
        h.Visible(true, "仍按住开镜键不会在下一帧重新开镜");
        h.Step();
        h.Press(2);
        h.Visible(false, "松开再按可以重新开镜");

        h = new Harness();
        h.Step(9);
        h.Step(9, 2);
        h.Visible(false, "一直按住取消键不等于连续出现取消事件");
        h.Step(9);
        h.Step(9, 27);
        h.Visible(true, "按住 Tab 再按 Esc 仍会取消");
        h.Step(27, 2);
        h.Visible(false, "Esc 仍按住时新按右键允许开镜");
        h.Step(27, 9);
        h.Visible(true, "按住 Esc 再按 Tab 也会取消");
    }

    /// <summary>改键后只响应新的列表，覆盖四组按键记录的边界。</summary>
    private static void CustomBindings()
    {
        Harness h = new Harness();
        h.Config.Keys.CancelAdsKeys = new int[] { 63, 64, 127, 128, 191, 192, 254 };
        h.Press(2);
        h.Press(9);
        h.Press(27);
        h.Visible(false, "移除默认绑定后 Tab 和 Esc 不再取消");
        foreach (int key in h.Config.Keys.CancelAdsKeys)
        {
            h.Press(key);
            h.Visible(true, "自定义取消键生效：" + key);
            h.Press(2);
            h.Visible(false, "自定义取消后仍可再次开镜：" + key);
        }
        h.Config.Keys.CancelAdsKeys = new int[0];
        h.Press(9);
        h.Press(27);
        h.Visible(false, "清空列表后取消键禁用");
    }

    /// <summary>按住开镜的显隐由开镜键本身决定，取消键不改变其行为。</summary>
    private static void HoldMode()
    {
        Harness h = new Harness();
        h.Config.AdsMode = "hold";
        h.Step(2);
        h.Visible(false, "按住开镜时隐藏");
        h.Step(2, 9);
        h.Visible(false, "按住模式中 Tab 不提前显示");
        h.Step(2, 27);
        h.Visible(false, "按住模式中 Esc 不提前显示");
        h.Step(27);
        h.Visible(true, "松开开镜键后显示");
    }

    /// <summary>按键继续遵守前台限制，预览不消费实时取消键。</summary>
    private static void FocusAndPreview()
    {
        Harness h = new Harness();
        h.Config.OnlyWhenGameFocused = true;
        h.Press(2);
        h.Engine.GameFocused = false;
        h.Engine.OwnForeground = true;
        h.Press(9);
        h.Engine.GameFocused = true;
        h.Step();
        h.Visible(false, "其他窗口按取消键不改变记录");
        h.Press(9);
        h.Visible(true, "回到游戏后取消生效");
        h.Config.OnlyWhenGameFocused = false;
        h.Engine.GameFocused = false;
        h.Press(2);
        h.Press(27);
        h.Visible(true, "关闭前台限制后保留原有全局读取行为");

        h = new Harness();
        h.Press(2);
        h.Preview = PreviewMode.Static;
        h.Press(9);
        h.Visible(true, "预览继续正常显示");
        h.Preview = PreviewMode.Live;
        h.Step();
        h.Visible(false, "预览中的取消键不会清掉实时开镜记录");
        h.Press(9);
        h.Visible(true, "退出预览后取消生效");
        h.Preview = PreviewMode.Static;
        h.Step(2);
        h.Preview = PreviewMode.Live;
        h.Step(2);
        h.Visible(true, "退出预览时仍按住的键不会伪造新按下");
    }

    /// <summary>取消只修改开镜记录，其他运动照常推进，自由视角和渐变仍有效。</summary>
    private static void OtherStates()
    {
        Harness h = new Harness();
        h.Profile.CurveAD.DurationMs = 0;
        h.Profile.Dirs[2].OffX = 40;
        h.Step(5, 6, 65, 81, 32);
        double move = h.Engine.MoveX;
        double jumpMs = h.Engine.JumpMs;
        h.Step(9, 65, 81);
        Check(h.Engine.LiveSwitched && h.Engine.Boosted, "取消不重置换手或强化准星");
        Check(move != 0 && h.Engine.MoveX != 0, "取消不清掉持续移动");
        Check(h.Engine.LeanTarget == -1, "取消不清掉侧身");
        Check(h.Engine.Jumping && h.Engine.JumpMs > jumpMs, "取消不重启跳跃计时");

        h = new Harness();
        h.Press(2);
        h.Step(9, 4);
        h.Visible(false, "取消后仍受自由视角隐藏控制");
        h.Step();
        h.Visible(true, "松开自由视角后显示");
        h = new Harness();
        h.Press(2);
        h.Config.ShowFadeMs = 50;
        h.Step(9);
        Check(Math.Abs(h.Engine.Alpha - 0.32) < 0.000001, "取消后沿用出现渐变");
    }

    /// <summary>统一给断言加上场景说明，避免失败时只看到布尔值。</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private sealed class Harness
    {
        // 只替代物理按键查询，输入过滤和运动引擎均使用生产代码。
        public readonly GlobalSettings Config = GlobalSettings.CreateDefault();
        public readonly Profile Profile;
        public readonly MotionEngine Engine = new MotionEngine();
        public PreviewMode Preview = PreviewMode.Live;
        private readonly LiveInputFilter filter = new LiveInputFilter();
        private readonly HashSet<int> pressed = new HashSet<int>();

        public Harness()
        {
            Config.AdsMode = "toggle";
            Config.OnlyWhenGameFocused = false;
            Config.ShowFadeMs = Config.HideFadeMs = 0;
            Profile = Config.Active;
        }

        /// <summary>按渲染线程顺序读取、过滤并处理一帧输入。</summary>
        public void Step(params int[] keys)
        {
            pressed.Clear();
            foreach (int key in keys) pressed.Add(key);
            InputState raw = FocusGate.KeysLive(Config.OnlyWhenGameFocused, Engine.GameFocused)
                ? MotionEngine.ReadInput(Config.Keys, pressed.Contains) : new InputState();
            Engine.Update(0.016, filter.Next(raw, Config.Enabled, Preview == PreviewMode.Live),
                Profile, Config, Preview, 1.0);
        }

        /// <summary>一次完整点击，包括松开，便于组合真实操作序列。</summary>
        public void Press(int key) { Step(key); Step(); }

        /// <summary>从显示结果验收，而不读取私有开镜记录。</summary>
        public void Visible(bool expected, string message)
        {
            Check(Engine.Alpha == (expected ? 1.0 : 0.0), message + "，Alpha=" + Engine.Alpha);
        }
    }
}
