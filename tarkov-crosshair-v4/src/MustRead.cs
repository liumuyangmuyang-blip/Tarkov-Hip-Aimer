// MustRead.cs -- v5.1 (W26, decision 4): the 必读 card. Shown in the first-run wizard (the user must click 「我知道了」) and
// reachable later from the 首页 (DashboardHome) and the 常见问题 page.
//
// To add an item: append one Item to MustRead.Items below. Nothing else changes; the wizard, the dialog and the home button
// all read this list. Keys in the text ({boost} {toggle} ...) are filled in from the user's settings (HelpText.Fill).
//
//   MustReadDialog.ShowCard(owner, g, onAck)   -> modal card; onAck runs after 「我知道了」 (sets G.MustReadAck = true)
//   new MustReadView(g)                        -> the same card as a control (the wizard's last-but-one step)
using System;
using System.Collections.Generic;
#if !UI_PURE_CHECK
using System.Drawing;
using System.Windows.Forms;
#endif

namespace TarkovCrosshair
{
    internal static class MustRead
    {
        public const string Title = "必读";
        public const string AckText = "我知道了";
        public const string Intro = "开始之前，请先看完这几条：";

        internal sealed class Item
        {
            public readonly string Head, Body;
            public Item(string head, string body) { Head = head; Body = body; }
        }

        /// <summary>The card, in order. Append new items at the end.</summary>
        public static readonly Item[] Items = new Item[]
        {
            // v5.1 round 2 (Leo R6): only what a total beginner must know, short sentences.
            new Item("战术装置用红外（IR）模式",
                "推荐 WMX200。用 T 打开装置，一直停在红外模式。"),
            new Item("不要同时开准星和激光",
                "两个指示器一起开，会干扰你。"),
            new Item("强化键是 {boost}",
                "游戏里「切换战术装置模式」也绑这个键。切到手电 = 强化开，切回红外 = 关。T 不算。"),
            new Item("用管理员权限运行",
                "启动时 Windows 问「是否允许」，点「是」。不然准星不会跟着动。"),
            new Item("开镜方式要和游戏一样",
                "游戏里按住开镜就选「按住」，点一下开镜就选「切换」。选错了开镜时还会看到准星。"),
        };

        /// <summary>Item heads/bodies with the user's keys filled in.</summary>
        public static string HeadOf(int i, GlobalSettings g) { return HelpText.Fill(Items[i].Head, g); }
        public static string BodyOf(int i, GlobalSettings g) { return HelpText.Fill(Items[i].Body, g); }

        /// <summary>The whole card as plain text (README / copy), numbered 1..N.</summary>
        public static string PlainText(GlobalSettings g)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(Title).Append('\n').Append(Intro).Append('\n');
            for (int i = 0; i < Items.Length; i++)
                sb.Append(i + 1).Append(". ").Append(HeadOf(i, g)).Append('\n').Append("   ").Append(BodyOf(i, g)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Marks the card as read (G.MustReadAck). Returns true when it changed.</summary>
        public static bool Acknowledge(GlobalSettings g)
        {
            if (g == null || g.MustReadAck) return false;
            g.MustReadAck = true;
            return true;
        }
    }

#if !UI_PURE_CHECK
    /// <summary>The 必读 card as a control: numbered items, each head in bold, body wrapped to the width. Every label is
    /// AutoSize with a MaximumSize width, so the text wraps from real font metrics and is never clipped.</summary>
    internal sealed class MustReadView : TableLayoutPanel
    {
        private readonly GlobalSettings g;
        private readonly List<Label> wrapped = new List<Label>();
        private Font bold;

        public MustReadView(GlobalSettings g, Font font)
        {
            this.g = g;
            if (font != null) Font = font;
            ColumnCount = 2;
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0);
            Padding = new Padding(0);
            bold = new Font(Font, FontStyle.Bold);
            int u = Math.Max(8, Font.Height);

            Label intro = new Label();
            intro.Text = MustRead.Intro;
            intro.AutoSize = true;
            intro.Margin = new Padding(0, 0, 0, u / 2);
            intro.ForeColor = Theme.ColMuted;
            Controls.Add(intro, 0, 0);
            SetColumnSpan(intro, 2);
            wrapped.Add(intro);

            for (int i = 0; i < MustRead.Items.Length; i++)
            {
                Label num = new Label();
                num.Text = (i + 1) + ".";
                num.AutoSize = true;
                num.Font = bold;
                num.ForeColor = Theme.ColAccent;
                num.Margin = new Padding(0, 0, u / 3, u / 2);
                Controls.Add(num, 0, i * 2 + 1);

                Label head = new Label();
                head.Text = MustRead.HeadOf(i, g);
                head.AutoSize = true;
                head.Font = bold;
                head.ForeColor = i == 0 ? Theme.ColAccent : Theme.ColText;
                head.Margin = new Padding(0, 0, 0, 2);
                Controls.Add(head, 1, i * 2 + 1);
                wrapped.Add(head);

                Label body = new Label();
                body.Text = MustRead.BodyOf(i, g);
                body.AutoSize = true;
                body.Margin = new Padding(0, 0, 0, u * 2 / 3);
                Controls.Add(body, 1, i * 2 + 2);
                wrapped.Add(body);
            }
            SetWrapWidth(u * 30);
        }

        /// <summary>Wrap width in px for the text column (the caller passes its own available width).</summary>
        public void SetWrapWidth(int px)
        {
            px = Math.Max(120, px);
            foreach (Label l in wrapped) l.MaximumSize = new Size(px, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bold != null) { bold.Dispose(); bold = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>The 必读 card as a modal dialog with one 「我知道了」 button (theme accent). Closing the window any other way
    /// does NOT acknowledge it.</summary>
    internal sealed class MustReadDialog : Form
    {
        private MustReadDialog(Form owner, GlobalSettings g)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = owner != null ? owner.Font : Theme.Ui(11f);
            Text = MustRead.Title + " · " + Program.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = owner == null;
            StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            TopMost = owner != null && owner.TopMost;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int u = Math.Max(8, Font.Height);

            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Padding = new Padding(u, u, u, u * 2 / 3);

            Label title = new Label();
            title.Text = MustRead.Title;
            title.AutoSize = true;
            title.Font = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold);
            title.ForeColor = Theme.ColAccent;
            title.Margin = new Padding(0, 0, 0, u / 2);
            t.Controls.Add(title);

            MustReadView v = new MustReadView(g, Font);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            v.SetWrapWidth(Math.Min(u * 30, work.Width - u * 6));
            t.Controls.Add(v);

            Button ok = new Button();
            ok.Text = MustRead.AckText;
            ok.DialogResult = DialogResult.OK;
            ok.AutoSize = true;
            ok.MinimumSize = new Size(u * 6, u + u / 2);
            ok.Padding = new Padding(u / 2, 0, u / 2, 0);
            ok.Tag = "theme:accent";
            ok.Anchor = AnchorStyles.Right;
            ok.Margin = new Padding(3, u / 2, 0, 3);
            t.Controls.Add(ok);

            Controls.Add(t);
            AcceptButton = ok;
            Theme.Apply(this);
        }

        /// <summary>Shows the card; true when the user clicked 「我知道了」 (then G.MustReadAck is set and onAck runs).</summary>
        public static bool ShowCard(Form owner, GlobalSettings g, Action onAck)
        {
            try
            {
                using (MustReadDialog d = new MustReadDialog(owner, g))
                {
                    if (d.ShowDialog(owner) != DialogResult.OK) return false;
                }
                if (MustRead.Acknowledge(g) && onAck != null) onAck();
                return true;
            }
            catch (Exception ex)
            {
                Program.Log("UI: must-read: " + ex);
                return false;
            }
        }
    }
#endif
}
