// v5.1 R3 (W28): the 反馈 email box (Leo: no in-app send box; users email him directly). Shows Leo's address with 复制 and
// 打开邮箱 (mailto with subject 「Tarkov Hip Aimer v5.1 反馈」 and a body template: version, Windows, screen / scale).
// Nothing is sent by the app. Host: width = page width, height = PreferredHeightFor(width). Sizes from real font metrics.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class FeedbackPanel : Panel
    {
        public const string HintText = "有问题或建议，直接发邮件给作者：";
        public const string NoteText = "点「打开邮箱」会用你电脑上的邮件软件新建一封邮件，版本号、Windows 版本和屏幕大小已经写好。没有邮件软件就复制邮箱，在网页邮箱里发。";
        public const string BtnCopy = "复制", BtnOpen = "打开邮箱";

        private readonly Func<FeedbackEnv> env;
        private readonly Label hint, address, note, status;
        private readonly Button copy, open;
        private readonly Font fBody, fAddr, fSmall;

        public FeedbackPanel(Func<FeedbackEnv> env)
        {
            this.env = env;
            BackColor = Theme.ColSurface; ForeColor = Theme.ColText;
            fBody = Theme.Ui(10.5f); fAddr = Theme.Ui(12f, FontStyle.Bold); fSmall = Theme.Ui(9.5f);
            hint = MakeLabel(HintText, fBody, Theme.ColText);
            address = MakeLabel(NetMail.Address, fAddr, Theme.ColAccent);
            note = MakeLabel(NoteText, fSmall, Theme.ColMuted);
            status = MakeLabel("", fBody, Theme.ColMuted);
            copy = MakeButton(BtnCopy, false); open = MakeButton(BtnOpen, true);
            copy.Click += delegate { Copy(); };
            open.Click += delegate { OpenMail(); };
            Controls.AddRange(new Control[] { hint, address, copy, open, note, status });
        }

        public string StatusText { get { return status.Text; } }

        private Label MakeLabel(string t, Font f, Color c)
        {
            Label l = new Label(); l.Text = t; l.Font = f; l.ForeColor = c; l.AutoSize = false; l.UseMnemonic = false; l.BackColor = Color.Transparent;
            return l;
        }

        private Button MakeButton(string t, bool primary)
        {
            Button b = new Button(); b.Text = t; b.Font = fBody; b.FlatStyle = FlatStyle.Flat; b.UseMnemonic = false;
            b.FlatAppearance.BorderColor = primary ? Theme.ColAccent : Theme.ColBorder;
            b.BackColor = Theme.ColInput; b.ForeColor = primary ? Theme.ColAccent : Theme.ColText;
            return b;
        }

        private float Scale_()
        {
            try { using (Graphics g = CreateGraphics()) return Math.Max(1f, g.DpiX / 96f); } catch (Exception) { return 1f; }
        }

        private static int Wrapped(Label l, int width)
        {
            Size z = TextRenderer.MeasureText(l.Text.Length == 0 ? "国" : l.Text, l.Font, new Size(Math.Max(10, width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return (int)Math.Ceiling(z.Height * 1.1f) + 2;
        }

        public int PreferredHeightFor(int width)
        {
            float s = Scale_();
            int pad = (int)Math.Ceiling(14 * s), gap = (int)Math.Ceiling(8 * s);
            int w = Math.Max((int)(200 * s), width - 2 * pad);
            int bh = NetTextFit.Height(fBody, s, 1.9f);
            int bw = Math.Max(NetTextFit.Width(BtnCopy, fBody, s, 28), NetTextFit.Width(BtnOpen, fBody, s, 28));
            int y = pad;
            hint.SetBounds(pad, y, w, Wrapped(hint, w)); y = hint.Bottom + gap / 2;
            int aw = NetTextFit.Width(NetMail.Address, fAddr, s, 8);
            int ah = Math.Max(bh, NetTextFit.Height(fAddr, s, 1.4f));
            if (aw + 2 * (bw + gap) <= w)
            {
                address.SetBounds(pad, y, aw, ah);
                address.TextAlign = ContentAlignment.MiddleLeft;
                copy.SetBounds(pad + aw + gap, y + (ah - bh) / 2, bw, bh);
                open.SetBounds(copy.Right + gap, copy.Top, bw, bh);
                y += ah + gap;
            }
            else
            {
                address.SetBounds(pad, y, Math.Min(w, aw), ah); y = address.Bottom + gap / 2;
                copy.SetBounds(pad, y, bw, bh); open.SetBounds(copy.Right + gap, y, bw, bh); y += bh + gap;
            }
            note.SetBounds(pad, y, w, Wrapped(note, w)); y = note.Bottom + gap / 2;
            status.SetBounds(pad, y, w, Wrapped(status, w)); y = status.Bottom + pad;
            return y;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width > 0) PreferredHeightFor(Width);
        }

        private void SetStatus(string t, bool bad)
        {
            status.Text = t;
            status.ForeColor = bad ? Theme.ColWarn : Theme.ColAccent;
            PerformLayout();
        }

        private void Copy()
        {
            try { Clipboard.SetText(NetMail.Address); SetStatus("已复制邮箱 " + NetMail.Address, false); }
            catch (Exception) { SetStatus("复制失败，请手动记下邮箱 " + NetMail.Address, true); }
        }

        private void OpenMail()
        {
            FeedbackEnv e;
            try { e = env != null ? env() : new FeedbackEnv(); } catch (Exception) { e = new FeedbackEnv(); }
            if (NetMail.Open(NetMail.FeedbackLink(e))) SetStatus("已打开邮件软件，写好后点发送就行。", false);
            else { Copy(); SetStatus("这台电脑没有设置邮件软件。邮箱已复制，请在网页邮箱（例如 QQ 邮箱、Gmail）里新建邮件发给作者。", true); }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { fBody.Dispose(); fAddr.Dispose(); fSmall.Dispose(); }
        }
    }
}
