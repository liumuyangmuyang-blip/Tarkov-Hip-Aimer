// DashboardHome.cs -- v5.1 (W26, decision 13): the 首页 = an always-visible status line on top + the hacker-math dashboard.
//
//   new DashboardHome(dashEngine, G, jumpToPage, runWizard)   -> replaces `new DashboardPanel(dashEngine, G)` in BuildDashboard
//   .Dash                                                     -> the inner DashboardPanel (for DebugFrames / AppendLog / ResetView)
//
// The top bar: 「● 准星：显示中 · 强化：关」 in words (StatusLine), a 「?」, then 「必读」「常见问题」「设置向导」 buttons.
// Polled twice a second (only while visible and not paused; IPausable), repainted only when the words change. The live state
// comes from StatusLine.LiveSource when the overlay publishes it, else from the dashboard's own engine + the settings.
#if !UI_PURE_CHECK
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    internal sealed class DashboardHome : Panel, IPausable
    {
        public readonly DashboardPanel Dash;
        private readonly MotionEngine engine;
        private readonly GlobalSettings g;
        private readonly StatusBarView bar;
        private readonly StatusLine line = new StatusLine();
        private readonly Timer poll = new Timer();
        private bool paused;

        public DashboardHome(MotionEngine engine, GlobalSettings g, Action<string> jumpToPage, Action runWizard)
        {
            this.engine = engine;
            this.g = g;
            Margin = new Padding(0);
            Padding = new Padding(0);
            Dash = new DashboardPanel(engine, g);
            Dash.Dock = DockStyle.Fill;

            bar = new StatusBarView(g, jumpToPage, runWizard);
            bar.Dock = DockStyle.Top;
            Controls.Add(Dash);
            Controls.Add(bar);
            Dash.BringToFront(); // docked last: fills what the bar leaves

            poll.Interval = 500;
            poll.Tick += delegate { Poll(); };
            VisibleChanged += delegate { UpdateTimer(); };
            HandleCreated += delegate { Poll(); UpdateTimer(); };
        }

        public void Pause() { paused = true; UpdateTimer(); }
        public void Resume() { paused = false; UpdateTimer(); Poll(); }

        private void UpdateTimer()
        {
            bool run = !paused && Visible && IsHandleCreated && !IsDisposed;
            if (run && !poll.Enabled) poll.Start();
            else if (!run && poll.Enabled) poll.Stop();
        }

        private void Poll()
        {
            StatusSnapshot s;
            if (!TryLive(out s)) s = FromEngine(engine, g);
            if (line.Update(s, g)) bar.SetStatus(line.Text, line.Level);
        }

        private static bool TryLive(out StatusSnapshot s)
        {
            s = new StatusSnapshot();
            Func<StatusSnapshot> f = StatusLine.LiveSource;
            if (f == null) return false;
            try { s = f(); return true; }
            catch (Exception) { return false; }
        }

        // MotionEngine.GrenadeHidden comes with W21's grenade toggle; read by name until then (no compile dependency).
        private static PropertyInfo grenadeProp;
        private static bool grenadeLooked;

        internal static StatusSnapshot FromEngine(MotionEngine e, GlobalSettings g)
        {
            StatusSnapshot s = new StatusSnapshot();
            s.Enabled = g == null || g.Enabled;
            s.BoostEnabled = g != null && g.BoostEnabled;
            s.AmmoOn = g != null && g.AmmoOn;
            s.FreeLookHidden = false;  // only the live source (overlay) knows these two; the home page is in front
            s.WaitingForGame = false;
            if (e == null) return s;
            try
            {
                s.AdsHidden = e.AdsActive;
                s.BoostOn = e.Boosted;
                s.HandSwitched = e.Debug.Switched;
                if (!grenadeLooked)
                {
                    grenadeLooked = true;
                    grenadeProp = typeof(MotionEngine).GetProperty("GrenadeHidden");
                    if (grenadeProp != null && grenadeProp.PropertyType != typeof(bool)) grenadeProp = null;
                }
                if (grenadeProp != null) s.GrenadeHidden = (bool)grenadeProp.GetValue(e, null);
            }
            catch (Exception) { }
            return s;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { poll.Stop(); poll.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>The status line row: coloured dot + words (owner-drawn, height from the font), 「?」, and three link buttons.</summary>
    internal sealed class StatusBarView : TableLayoutPanel
    {
        private readonly StatusText text;

        public StatusBarView(GlobalSettings g, Action<string> jumpToPage, Action runWizard)
        {
            ColumnCount = 5;
            RowCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int u = Math.Max(10, Font.Height);
            Padding = new Padding(u / 2, u / 3, u / 2, u / 3);

            text = new StatusText();
            text.Dock = DockStyle.Fill;
            Controls.Add(text, 0, 0);
            HelpMark help = HelpMark.For("status", Font, delegate { return g; });
            help.Anchor = AnchorStyles.Left;
            Controls.Add(help, 1, 0);

            Controls.Add(Link("必读", delegate { MustReadDialog.ShowCard(FindForm(), g, null); }), 2, 0);
            Controls.Add(Link(FaqText.PageTitle, delegate { if (jumpToPage != null) jumpToPage("faq"); }), 3, 0);
            Button wiz = Link("设置向导", delegate { if (runWizard != null) runWizard(); });
            wiz.Visible = runWizard != null;
            Controls.Add(wiz, 4, 0);
        }

        private Button Link(string t, Action click)
        {
            Button b = new Button();
            b.Text = t;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Margin = new Padding(Math.Max(4, Font.Height / 3), 0, 0, 0);
            b.Padding = new Padding(Font.Height / 3, 0, Font.Height / 3, 0);
            b.Anchor = AnchorStyles.Right;
            b.Click += delegate { try { click(); } catch (Exception ex) { Program.Log("UI: home " + t + ": " + ex); } };
            return b;
        }

        public void SetStatus(string words, StatusLevel level) { text.Set(words, level); }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); text.Font = Font; }
    }

    /// <summary>One line of status words with a coloured dot: green 显示中, cyan info (换手), amber 已隐藏, grey 已关闭.
    /// The words wrap onto a second line in a narrow window (never cut off, decision 9); the height follows the text.</summary>
    internal sealed class StatusText : Control
    {
        private string words = "";
        private StatusLevel level;

        public StatusText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0);
            AccessibleRole = AccessibleRole.StatusBar;
            FitHeight();
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitHeight(); }

        private int TextLeft { get { int d = Math.Max(6, Font.Height / 2); return d + d / 2 + 2; } }

        private const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix
            | TextFormatFlags.TextBoxControl;

        /// <summary>Height for the current words at the current width (one line minimum), from the real font metrics.</summary>
        private void FitHeight()
        {
            int one = TextRenderer.MeasureText("准星：显示中 Ag", Font).Height;
            int w = Math.Max(Font.Height * 4, Width - TextLeft);
            int need = words.Length == 0 ? one : TextRenderer.MeasureText(words, Font, new Size(w, int.MaxValue), Flags).Height;
            int h = Math.Max(one, need) + 6;
            if (MinimumSize.Height != h) MinimumSize = new Size(0, h);
            if (Height != h) Height = h;
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (words.Length > 0) FitHeight(); }

        public void Set(string w, StatusLevel lv)
        {
            words = w ?? "";
            level = lv;
            AccessibleName = words;
            FitHeight();
            Invalidate();
        }

        internal static Color ColourOf(StatusLevel lv)
        {
            switch (lv)
            {
                case StatusLevel.Off: return Theme.ColMuted;
                case StatusLevel.Hidden: return Theme.ColWarn;
                case StatusLevel.Info: return Theme.ColAccent2;
                default: return Theme.ColAccent;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics gr = e.Graphics;
            gr.Clear(Parent != null ? Parent.BackColor : Theme.ColBackground);
            int d = Math.Max(6, Font.Height / 2);
            int y = Math.Max(0, (TextRenderer.MeasureText("准", Font).Height - d) / 2 + 3);
            gr.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = ColourOf(level);
            using (SolidBrush b = new SolidBrush(c)) gr.FillEllipse(b, 1, y, d, d);
            gr.SmoothingMode = SmoothingMode.None;
            Rectangle r = new Rectangle(TextLeft, 3, Math.Max(0, Width - TextLeft), Height - 3);
            TextRenderer.DrawText(gr, words, Font, r, level == StatusLevel.Ok ? Theme.ColText : c, Flags);
        }

    }
}
#endif
