// FaqPage.cs -- v5.1 (W26, decision 13): the 常见问题 page body (WinForms). Questions and answers: FaqText.cs.
//
//   new FaqPage(G, Font, jumpToPage)   -> the page content (AutoSize; put it in the host's scrolling page body)
//   page.SetWrapWidth(px)        -> text column width (call on resize; the page also follows its parent's width by itself)
//
// Each entry: the question in bold, the answer below (wrapped from real font metrics), and an optional 「去…设置 →」 button
// that jumps to the page that fixes it. A 「查看必读」 button sits at the top.
#if !UI_PURE_CHECK
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class FaqPage : TableLayoutPanel
    {
        private readonly GlobalSettings g;
        private readonly List<Label> wraps = new List<Label>();
        private readonly List<Label> answers = new List<Label>();
        private readonly List<Label> questions = new List<Label>();
        private Font bold;
        private int wrapW;

        public FaqPage(GlobalSettings g, Font font, Action<string> jumpToPage)
        {
            this.g = g;
            if (font != null) Font = font;
            ColumnCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0);
            Padding = new Padding(0);
            SuspendLayout(); // v5.1.1 (X11): one layout for all the rows (was one per added row, O(n²))
            bold = Theme.Ui(Font.SizeInPoints, FontStyle.Bold);
            int u = Math.Max(10, Font.Height);

            Button must = new Button();
            must.Text = "查看必读";
            must.AutoSize = true;
            must.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            must.Padding = new Padding(u / 3, 0, u / 3, 0);
            must.Margin = new Padding(0, 0, 0, u);
            must.Tag = "theme:accent";
            must.Click += delegate { MustReadDialog.ShowCard(FindForm(), g, null); };
            Controls.Add(must);

            for (int i = 0; i < FaqText.Items.Length; i++)
            {
                FaqText.Entry e = FaqText.Items[i];
                Label q = new Label();
                q.Text = "问：" + FaqText.Question(i, g);
                q.AutoSize = true;
                q.Font = bold;
                q.ForeColor = Theme.ColAccent;
                q.Margin = new Padding(0, 0, 0, 2);
                Controls.Add(q);
                wraps.Add(q); questions.Add(q);

                Label a = new Label();
                a.Text = FaqText.Answer(i, g);
                a.AutoSize = true;
                a.Margin = new Padding(u, 0, 0, e.Jump != null ? 2 : u);
                Controls.Add(a);
                wraps.Add(a); answers.Add(a);

                if (e.Jump != null && jumpToPage != null)
                {
                    string page = e.Jump;
                    Button go = new Button();
                    go.Text = e.JumpText ?? "去设置 →";
                    go.AutoSize = true;
                    go.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    go.Padding = new Padding(u / 3, 0, u / 3, 0);
                    go.Margin = new Padding(u, 2, 0, u);
                    go.Click += delegate { try { jumpToPage(page); } catch (Exception ex) { Program.Log("UI: faq jump: " + ex); } };
                    Controls.Add(go);
                }
            }
            SetWrapWidth(u * 36);
            ResumeLayout(false);
        }

        /// <summary>Re-reads the answers (the user's keys may have changed). Cheap: text only.</summary>
        public void RefreshTexts()
        {
            // v5.1.1 (X11): set only what changed, all in one layout (this ran on every show of the page)
            SuspendLayout();
            bool changed = false;
            try
            {
                for (int i = 0; i < FaqText.Items.Length && i < answers.Count; i++)
                {
                    string q = "问：" + FaqText.Question(i, g), a = FaqText.Answer(i, g);
                    if (questions[i].Text != q) { questions[i].Text = q; changed = true; }
                    if (answers[i].Text != a) { answers[i].Text = a; changed = true; }
                }
            }
            finally { ResumeLayout(changed); }
        }

        public void SetWrapWidth(int px)
        {
            px = Math.Max(Font.Height * 8, px);
            if (px == wrapW) return;
            wrapW = px;
            SuspendLayout(); // v5.1.1 (X11): one layout for every label's new wrap width
            try { foreach (Label l in wraps) l.MaximumSize = new Size(px - l.Margin.Left, 0); }
            finally { ResumeLayout(true); }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null) Parent.Resize += delegate { FollowParent(); };
            FollowParent();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RefreshTexts();
        }

        private void FollowParent()
        {
            if (Parent == null || Parent.ClientSize.Width <= 0) return;
            int w = Parent.ClientSize.Width - Left - SystemInformation.VerticalScrollBarWidth - Font.Height;
            if (w > 0) SetWrapWidth(w);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bold != null) { bold.Dispose(); bold = null; }
            base.Dispose(disposing);
        }
    }
}
#endif
