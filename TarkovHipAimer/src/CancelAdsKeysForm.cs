// 用途：通过按键录入、移除和恢复默认来编辑多个取消开镜键。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class CancelAdsKeysForm : Form
    {
        // 编辑副本，取消对话框时不修改正在使用的绑定。
        private readonly KeyBindings bindings;
        private readonly List<int> keys;
        private readonly ListBox keyList = new ListBox();
        private readonly Button captureButton = new Button();
        private readonly Button stopCaptureButton = new Button();
        private readonly Label prompt = new Label();
        private bool capturing;
        private bool suppressNextClick;

        /// <summary>返回确定后的独立数组，避免界面和渲染线程共用可变列表。</summary>
        public int[] SelectedKeys { get { return keys.ToArray(); } }

        /// <summary>构建编辑窗口；Esc 在录入时也可以作为取消开镜键保存。</summary>
        public CancelAdsKeysForm(KeyBindings current)
        {
            bindings = current.Clone();
            bindings.Validate();
            keys = new List<int>(bindings.CancelAdsKeys);
            Text = "取消开镜键";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(510, 330);
            MinimumSize = new Size(480, 330);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            BuildContent();
            RefreshList();
            Theme.Apply(this);
        }

        /// <summary>列表和按钮分行放置，缩放时由布局容器分配空间。</summary>
        private void BuildContent()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12);
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            Label description = new Label();
            description.AutoSize = true;
            description.Dock = DockStyle.Fill;
            description.Text = "仅对“点一下切换”开镜生效。按下任一取消键，工具就重新认定为未开镜。\n默认 Tab、Esc；可配置多个键，也可清空禁用。不向游戏发送按键，不单独控制背包内显隐。";
            description.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(description, 0, 0);
            keyList.Name = "cancelAdsKeys";
            keyList.Dock = DockStyle.Fill;
            keyList.IntegralHeight = false;
            keyList.MouseDown += CaptureMouse;
            layout.Controls.Add(keyList, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.Dock = DockStyle.Fill;
            actions.WrapContents = false;
            captureButton.Name = "captureKey";
            captureButton.AutoSize = true;
            captureButton.Text = "添加按键";
            captureButton.Click += delegate
            {
                if (suppressNextClick) { suppressNextClick = false; return; }
                SetCapturing(true);
            };
            captureButton.MouseDown += CaptureMouse;
            actions.Controls.Add(captureButton);
            // 停止录入使用独立按钮，避免被误录为鼠标左键。
            stopCaptureButton.Name = "stopCapture";
            stopCaptureButton.Text = "取消录入";
            stopCaptureButton.AutoSize = true;
            stopCaptureButton.Click += delegate { SetCapturing(false); };
            actions.Controls.Add(stopCaptureButton);
            actions.Controls.Add(ActionButton("移除选中", RemoveSelected));
            actions.Controls.Add(ActionButton("清空", delegate { SetCapturing(false); keys.Clear(); RefreshList(); }));
            actions.Controls.Add(ActionButton("恢复默认", delegate
            {
                SetCapturing(false);
                keys.Clear();
                keys.AddRange(new KeyBindings().CancelAdsKeys);
                RefreshList();
            }));
            layout.Controls.Add(actions, 0, 2);
            prompt.AutoSize = true;
            prompt.Dock = DockStyle.Fill;
            prompt.Text = "点击添加后按键；录入鼠标键时，再点击添加按钮或列表。";
            layout.Controls.Add(prompt, 0, 3);
            FlowLayoutPanel confirm = new FlowLayoutPanel();
            confirm.AutoSize = true;
            confirm.Dock = DockStyle.Fill;
            confirm.FlowDirection = FlowDirection.RightToLeft;
            Button cancel = ActionButton("取消", delegate { });
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = ActionButton("确定", delegate { });
            ok.DialogResult = DialogResult.OK;
            confirm.Controls.Add(cancel);
            confirm.Controls.Add(ok);
            layout.Controls.Add(confirm, 0, 4);
            AcceptButton = ok;
            CancelButton = cancel;
            SetCapturing(false);
        }

        /// <summary>为常规操作创建自动宽度按钮。</summary>
        private static Button ActionButton(string text, Action action)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Click += delegate { action(); };
            return button;
        }

        /// <summary>切换录入状态，取消录入通过按钮完成，以便允许绑定 Esc。</summary>
        private void SetCapturing(bool value)
        {
            capturing = value;
            captureButton.Text = value ? "请按键…" : "添加按键";
            stopCaptureButton.Enabled = value;
            prompt.Text = value ? "请按键（Esc 也可绑定）；点击“取消录入”可停止。"
                : "点击添加后按键；录入鼠标键时，再点击添加按钮或列表。";
        }

        /// <summary>移除当前项；没有选择时保持原列表。</summary>
        private void RemoveSelected()
        {
            SetCapturing(false);
            int index = keyList.SelectedIndex;
            if (index < 0) return;
            keys.RemoveAt(index);
            RefreshList();
        }

        /// <summary>展示可读键名，重复录入不会创建重复项。</summary>
        private void RefreshList()
        {
            keyList.Items.Clear();
            foreach (int key in keys) keyList.Items.Add(SettingsForm.KeyName(key));
            if (keyList.Items.Count > 0) keyList.SelectedIndex = keyList.Items.Count - 1;
        }

        /// <summary>添加按键，复用其他绑定的冲突提醒，确认后才加入。</summary>
        private void FinishCapture(int vk)
        {
            SetCapturing(false);
            if (vk < 1 || vk > 254) return;
            if (keys.Contains(vk)) { prompt.Text = "这个键已经在列表中。"; return; }
            int other = bindings.FindOther(vk, -1);
            if (other >= 0)
            {
                DialogResult answer = MessageBox.Show(this, "这个键已经用于「" + KeyBindings.ActionNames[other] + "」，还要作为取消开镜键吗？",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                // 弹窗会接走鼠标松开事件，此时不再等待按钮的后续点击。
                suppressNextClick = false;
                if (answer != DialogResult.Yes) return;
            }
            keys.Add(vk);
            RefreshList();
        }

        /// <summary>录入模式下截获 Tab、Esc 等导航键，普通操作仍由窗口处理。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!capturing) return base.ProcessCmdKey(ref msg, keyData);
            FinishCapture((int)(keyData & Keys.KeyCode));
            return true;
        }

        /// <summary>鼠标按键只在用户明确开始录入后处理，不使用全局鼠标钩子。</summary>
        private void CaptureMouse(object sender, MouseEventArgs e)
        {
            if (!capturing) return;
            int vk = 0;
            if (e.Button == MouseButtons.Left) vk = 1;
            else if (e.Button == MouseButtons.Right) vk = 2;
            else if (e.Button == MouseButtons.Middle) vk = 4;
            else if (e.Button == MouseButtons.XButton1) vk = 5;
            else if (e.Button == MouseButtons.XButton2) vk = 6;
            if (vk == 0) return;
            suppressNextClick = sender == captureButton && e.Button == MouseButtons.Left;
            FinishCapture(vk);
        }
    }
}
