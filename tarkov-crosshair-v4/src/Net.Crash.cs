// v5.1 (W28) NET: the question after a crash (the pure CrashStore is in Net.CrashStore.cs).
//   CrashStore.Save   called by Program.ReportCrash: writes crash-pending.txt (scrubbed, capped). Never throws, no network.
//   NetHub            on the next start asks with CrashPrompt: 打开邮箱（附上日志） / 不发送 / 以后都不问 (Esc = 不发送).
// v5.1 R3 (Leo): nothing is uploaded. 打开邮箱 scrubs crash.log, opens the user's mail program (to Leo, subject
// 「Tarkov Hip Aimer 崩溃记录 v5.1」) and the 数据 Data folder with crash.log selected; the user attaches it by hand.
// With 「程序出错后，问我…」 off (NetCrashAsk=false) the pending file is just deleted.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    /// <summary>The question after a crash, in the app's dark theme. Every size comes from the real font metrics.</summary>
    internal static class CrashPrompt
    {
        public const string Title = "要把错误记录发给作者吗？";
        public const string Note = "上次程序出错了。点「打开邮箱」会新建一封发给作者的邮件，并打开放着 crash.log 的文件夹，把 crash.log 拖进邮件当附件再发送。\r\n用户名、电脑名和文件路径会先从 crash.log 里去掉。程序自己不会上传任何东西。";
        public const string BtnSend = "打开邮箱（附上日志）", BtnNo = "不发送", BtnNever = "以后都不问", BtnShow = "看看内容", BtnHide = "收起内容";

        public static CrashChoice Show(IWin32Window owner, string log)
        {
            using (Form f = new Form())
            using (Font fTitle = Theme.Ui(12f, FontStyle.Bold))
            using (Font fBody = Theme.Ui(10f))
            {
                float s = 1f;
                try { using (Graphics g0 = Graphics.FromHwnd(IntPtr.Zero)) s = Math.Max(1f, g0.DpiX / 96f); } catch (Exception) { }
                f.Text = Program.ShortName;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = owner == null;
                f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                f.BackColor = Theme.ColSurface; f.ForeColor = Theme.ColText;
                f.AutoScaleMode = AutoScaleMode.None;
                f.Font = fBody;
                int pad = (int)Math.Ceiling(16 * s), gap = (int)Math.Ceiling(10 * s);

                string[] texts = { BtnSend, BtnNo, BtnNever };
                CrashChoice[] vals = { CrashChoice.OpenMail, CrashChoice.DontSend, CrashChoice.NeverAsk };
                int bh = NetTextFit.Height(fBody, s, 1.9f), bw = 0;
                foreach (string t in texts) bw = Math.Max(bw, NetTextFit.Width(t, fBody, s, 28));
                bw = Math.Max(bw, NetTextFit.Width(BtnHide, fBody, s, 28));
                int rowW = 4 * bw + 3 * gap;
                int textW = Math.Max(rowW, NetTextFit.Width(Title, fTitle, s, 8));

                Label l = new Label(); l.Text = Title; l.Font = fTitle; l.ForeColor = Theme.ColAccent; l.AutoSize = false;
                l.SetBounds(pad, pad, textW, NetTextFit.Height(fTitle, s, 1.4f));
                Label n = new Label(); n.Text = Note; n.ForeColor = Theme.ColMuted; n.AutoSize = false;
                Size nz = TextRenderer.MeasureText(Note, fBody, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                n.SetBounds(pad, l.Bottom + gap / 2, textW, (int)Math.Ceiling(nz.Height * 1.1f) + 2);
                f.Controls.Add(l); f.Controls.Add(n);

                TextBox box = new TextBox();
                box.Multiline = true; box.ReadOnly = true; box.ScrollBars = ScrollBars.Both; box.WordWrap = false;
                box.BackColor = Theme.ColInput; box.ForeColor = Theme.ColText; box.BorderStyle = BorderStyle.FixedSingle;
                box.Text = (log ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                box.SetBounds(pad, n.Bottom + gap, textW, (int)(220 * s));
                box.Visible = false;
                f.Controls.Add(box);

                CrashChoice result = CrashChoice.DontSend;
                int by = n.Bottom + gap * 2;
                Button[] buttons = new Button[4];
                for (int i = 0; i < 4; i++)
                {
                    Button b = new Button();
                    b.Text = i < 3 ? texts[i] : BtnShow;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = i == 0 ? Theme.ColAccent : Theme.ColBorder;
                    b.BackColor = Theme.ColInput; b.ForeColor = i == 0 ? Theme.ColAccent : Theme.ColText;
                    b.Size = new Size(bw, bh);
                    if (i < 3)
                    {
                        CrashChoice v = vals[i];
                        b.Click += delegate { result = v; f.Close(); };
                    }
                    buttons[i] = b;
                    f.Controls.Add(b);
                }
                f.AcceptButton = buttons[0];
                f.CancelButton = buttons[1];
                MethodInvoker place = delegate
                {
                    int y = box.Visible ? box.Bottom + gap : by;
                    for (int i = 0; i < 4; i++) buttons[i].Location = new Point(pad + i * (bw + gap), y);
                    f.ClientSize = new Size(2 * pad + textW, y + bh + pad);
                };
                buttons[3].Click += delegate
                {
                    box.Visible = !box.Visible;
                    buttons[3].Text = box.Visible ? BtnHide : BtnShow;
                    place();
                };
                place();
                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
                return result;
            }
        }
    }

    /// <summary>Sizes from real font metrics (Microsoft YaHei on Windows is wider than the Mono fallback), with a margin.</summary>
    internal static class NetTextFit
    {
        public static int Width(string text, Font f, float scale, int extraPx96)
        {
            Size z = TextRenderer.MeasureText(text ?? "", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return (int)Math.Ceiling(z.Width * 1.08f + extraPx96 * scale);
        }

        public static int Height(Font f, float scale, float lines)
        {
            int h = TextRenderer.MeasureText("国Ag", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Height;
            return (int)Math.Ceiling(h * lines);
        }
    }
}
