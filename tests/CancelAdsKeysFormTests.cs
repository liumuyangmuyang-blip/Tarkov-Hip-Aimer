// 用途：验证取消键编辑窗口的实际按钮与键盘导航，不发送全局按键。
using System;
using System.Drawing;
using System.Windows.Forms;
using TarkovCrosshair;

public static class CancelAdsKeysFormTests
{
    /// <summary>在屏幕外创建编辑窗口，直接向它处理消息，不碰游戏或其他窗口。</summary>
    public static int Run()
    {
        int checks = 0;
        KeyBindings original = new KeyBindings();
        using (CancelAdsKeysForm form = new CancelAdsKeysForm(original))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            Button add = FindButton(form, "添加按键");
            Button stop = FindButton(form, "取消录入");
            Button clear = FindButton(form, "清空");
            clear.PerformClick();
            Assert(form.SelectedKeys.Length == 0, "清空编辑列表"); checks++;
            add.PerformClick();
            SendKey(form, Keys.Escape);
            Assert(form.SelectedKeys.Length == 1 && form.SelectedKeys[0] == 27, "录入 Esc 不会关闭窗口"); checks++;
            add.PerformClick();
            SendKey(form, Keys.Tab);
            Assert(form.SelectedKeys.Length == 2 && form.SelectedKeys[1] == 9, "录入 Tab 不会切换焦点"); checks++;
            add.PerformClick();
            SendKey(form, Keys.F10);
            Assert(form.SelectedKeys.Length == 3 && form.SelectedKeys[2] == 121, "录入普通自定义键"); checks++;
            add.PerformClick();
            SendKey(form, Keys.I);
            Assert(form.SelectedKeys.Length == 4 && form.SelectedKeys[3] == 73, "录入字母键"); checks++;
            add.PerformClick();
            stop.PerformClick();
            Assert(form.SelectedKeys.Length == 4 && !stop.Enabled, "取消录入不会新增鼠标键"); checks++;
            add.PerformClick();
            SendKey(form, Keys.Tab);
            Assert(form.SelectedKeys.Length == 4, "重复按键不会加入两次"); checks++;
            FindButton(form, "移除选中").PerformClick();
            Assert(form.SelectedKeys.Length == 3, "移除选中项"); checks++;
            FindButton(form, "恢复默认").PerformClick();
            Assert(form.SelectedKeys.Length == 2 && form.SelectedKeys[0] == 9 && form.SelectedKeys[1] == 27, "恢复默认 Tab 和 Esc"); checks++;
            clear.PerformClick();
            FindButton(form, "取消").PerformClick();
            Assert(original.CancelAdsKeys.Length == 2 && original.CancelAdsKeys[0] == 9, "取消窗口不改原绑定"); checks++;
        }
        return checks;
    }

    /// <summary>只通过窗口公开的预处理入口提交消息，不向系统发送键盘输入。</summary>
    private static void SendKey(Form form, Keys key)
    {
        Message message = Message.Create(form.Handle, 0x100, new IntPtr((int)key), IntPtr.Zero);
        Assert(form.PreProcessMessage(ref message), "窗口应消费录入按键：" + key);
    }

    /// <summary>按用户看到的按钮文字寻找操作入口。</summary>
    private static Button FindButton(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            Button button = child as Button;
            if (button != null && button.Text == text) return button;
            Button nested = FindButton(child, text);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>用具体操作名称报告失败。</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
