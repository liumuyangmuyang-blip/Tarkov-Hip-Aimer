// Theme.cs -- dark "Tarkov" theme for Tarkov Hip Aimer -- by Leo.
// Drop-in, standalone. C# 5 / .NET Framework 4.8 / WinForms + System.Drawing only.
//
//   Theme.Apply(form)                          -> call at the END of a form's constructor
//   Theme.Header(title, subtitle, version)     -> header bar (Dock = Top); add it to the form
//   Theme.AppIcon()                            -> app.ico (or the drawn ring) for Form.Icon / NotifyIcon.Icon
//
// Controls whose Tag is a string starting with "theme:keep" are left untouched
// (e.g. colour-swatch buttons). Apply never changes Size, Location, Text, Dock,
// Anchor or fonts of existing controls.
//
// THEME_PURE_CHECK: when defined, only the pure colour / .ico maths is compiled
// (used by the Linux net8.0 self-test). The real app never defines it.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
#if !THEME_PURE_CHECK
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
#endif

internal static class Theme
{
    // ------------------------------------------------------------------ palette
    internal static readonly Color ColBackground = Color.FromArgb(0x1E, 0x20, 0x23); // form / tab strip
    internal static readonly Color ColSurface    = Color.FromArgb(0x26, 0x29, 0x2D); // tab pages, group boxes
    internal static readonly Color ColInput      = Color.FromArgb(0x2F, 0x33, 0x38); // buttons, text boxes, combos
    internal static readonly Color ColBorder     = Color.FromArgb(0x3A, 0x3F, 0x45);
    internal static readonly Color ColText       = Color.FromArgb(0xE8, 0xE6, 0xE1);
    internal static readonly Color ColMuted      = Color.FromArgb(0x9D, 0xA3, 0xA8);
    internal static readonly Color ColAccent     = Color.FromArgb(0xC8, 0xB0, 0x7A); // Tarkov tan
    internal static readonly Color ColHeader     = Color.FromArgb(0x19, 0x1B, 0x1E); // a shade darker than background
    internal static readonly Color ColHover      = Color.FromArgb(0x38, 0x3D, 0x43); // button hover fill
    internal static readonly Color ColPressed    = Blend(ColInput, ColAccent, 0.35);  // button pressed fill (#655F4F)
    internal static readonly Color ColRingInner  = Color.FromArgb(0x00, 0xDF, 0x00);
    internal static readonly Color ColRingOuter  = Color.FromArgb(0x00, 0x00, 0x00);

    internal const double MinContrast = 4.5;

    // ------------------------------------------------------------ colour maths
    internal static double Channel(int c)
    {
        double s = c / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>WCAG relative luminance.</summary>
    internal static double Luminance(Color c)
    {
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>WCAG contrast ratio (1..21).</summary>
    internal static double Contrast(Color a, Color b)
    {
        double la = Luminance(a) + 0.05;
        double lb = Luminance(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    internal static Color Blend(Color a, Color b, double t)
    {
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Moves fg towards white (dark bg) or black (light bg) until it reaches min contrast.</summary>
    internal static Color EnsureContrast(Color fg, Color bg, double min)
    {
        Color opaque = Color.FromArgb(fg.R, fg.G, fg.B);
        if (Contrast(opaque, bg) >= min) return opaque;
        Color target = Luminance(bg) < 0.18 ? Color.White : Color.Black;
        for (int i = 1; i <= 20; i++)
        {
            Color c = Blend(opaque, target, i / 20.0);
            if (Contrast(c, bg) >= min) return c;
        }
        return target;
    }

    internal static bool IsGreyish(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        return (max - min) <= 28 && max >= 90 && max <= 215;
    }

    internal static bool IsNearBlack(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max < 90 && (max - min) < 40;
    }

    /// <summary>Text colour rule: black-ish -> text, grey hint -> muted, anything else kept but made readable.</summary>
    internal static Color MapForeground(Color fg, Color bg)
    {
        if (fg.A == 0 || IsNearBlack(fg)) return ColText;
        if (IsGreyish(fg)) return ColMuted;
        return EnsureContrast(fg, bg, MinContrast);
    }

    /// <summary>Background rule: transparent kept, light (old default) colours -> target, custom dark kept.</summary>
    internal static Color MapBackground(Color current, Color target, bool isSystemColor)
    {
        if (current.A == 0) return current;
        if (isSystemColor || Luminance(current) > 0.30) return target;
        return current;
    }

    // ------------------------------------------------------------- .ico writer
    /// <summary>Builds a multi-size 32bpp .ico. pixels[i] = top-down BGRA, sizes[i] x sizes[i].</summary>
    internal static byte[] BuildIco(IList<int> sizes, IList<byte[]> pixels)
    {
        int n = sizes.Count;
        List<byte[]> images = new List<byte[]>();
        for (int i = 0; i < n; i++) images.Add(BuildDib(sizes[i], pixels[i]));

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write((ushort)0);     // reserved
            w.Write((ushort)1);     // type = icon
            w.Write((ushort)n);     // count
            int offset = 6 + 16 * n;
            for (int i = 0; i < n; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);   // palette colours
                w.Write((byte)0);   // reserved
                w.Write((ushort)1); // planes
                w.Write((ushort)32);// bpp
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            for (int i = 0; i < n; i++) w.Write(images[i]);
            w.Flush();
            return ms.ToArray();
        }
    }

    private static byte[] BuildDib(int s, byte[] bgraTopDown)
    {
        int xorSize = s * s * 4;
        int maskStride = ((s + 31) / 32) * 4;
        int maskSize = maskStride * s;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(40);            // biSize
            w.Write(s);             // biWidth
            w.Write(s * 2);         // biHeight (XOR + AND)
            w.Write((ushort)1);     // planes
            w.Write((ushort)32);    // bit count
            w.Write(0);             // BI_RGB
            w.Write(xorSize + maskSize);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--) w.Write(bgraTopDown, y * s * 4, s * 4); // bottom-up
            byte[] mask = new byte[maskSize];
            for (int y = 0; y < s; y++)
            {
                int row = s - 1 - y; // bottom-up
                for (int x = 0; x < s; x++)
                {
                    if (bgraTopDown[(y * s + x) * 4 + 3] == 0)
                        mask[row * maskStride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            w.Write(mask);
            w.Flush();
            return ms.ToArray();
        }
    }

#if !THEME_PURE_CHECK
    // ================================================================ public API

    /// <summary>Restyles the form and its whole control tree (incl. controls added later). Never throws.</summary>
    public static void Apply(Form f)
    {
        if (f == null) return;
        try
        {
            try { f.BackColor = ColBackground; } catch { }
            try { f.ForeColor = ColText; } catch { }
            try { HookDarkTitleBar(f); } catch { }
            try { HookAdded(f); } catch { }
            Control[] kids = Children(f);
            for (int i = 0; i < kids.Length; i++) ApplyControl(kids[i], ColBackground);
        }
        catch { }
    }

    /// <summary>Header bar: ring icon, bold title, muted subtitle, muted version on the right. Dock = Top.</summary>
    public static Control Header(string title, string subtitle, string version)
    {
        return new HeaderPanel(title ?? "", subtitle ?? "", version ?? "");
    }

    /// <summary>App icon: app.ico next to the exe (multi-size 16..256, v3.0); fallback the drawn two-tone ring
    /// (16..64 px inside). Caller owns (and may Dispose) the result.</summary>
    public static Icon AppIcon()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(path))
            {
                byte[] file = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(file)) return new Icon(ms);
            }
        }
        catch { }
        try
        {
            byte[] ico = IconBytes();
            using (MemoryStream ms = new MemoryStream(ico)) return new Icon(ms);
        }
        catch { }
        try { return HiconIcon(32); } catch { }
        try { return (Icon)SystemIcons.Application.Clone(); } catch { }
        return null;
    }

    // ============================================================ tree walking
    private static readonly object Marker = new object();
    private static readonly ConditionalWeakTable<Control, object> AddedHooked = new ConditionalWeakTable<Control, object>();
    private static readonly ConditionalWeakTable<Control, object> PaintHooked = new ConditionalWeakTable<Control, object>();
    private static readonly ConditionalWeakTable<Control, object> TitleHooked = new ConditionalWeakTable<Control, object>();

    private static bool MarkOnce(ConditionalWeakTable<Control, object> table, Control c)
    {
        object o;
        if (table.TryGetValue(c, out o)) return false;
        table.Add(c, Marker);
        return true;
    }

    private static Control[] Children(Control c)
    {
        Control[] arr = new Control[c.Controls.Count];
        c.Controls.CopyTo(arr, 0);
        return arr;
    }

    private static bool IsKeep(Control c)
    {
        string s = c.Tag as string;
        return s != null && s.StartsWith("theme:keep", StringComparison.Ordinal);
    }

    private static bool CanRecurse(Control c)
    {
        return !(c is UpDownBase || c is TextBoxBase || c is ComboBox || c is ListControl ||
                 c is ButtonBase || c is Label || c is TrackBar || c is DataGridView ||
                 c is DateTimePicker || c is ScrollBar || c is WebBrowser || c is PropertyGrid ||
                 c is HeaderPanel);
    }

    private static Color EffectiveBack(Control c, Color fallback)
    {
        Color b = c.BackColor;
        return b.A < 255 ? fallback : b;
    }

    private static Color EffectiveBackUp(Control c)
    {
        while (c != null)
        {
            Color b = c.BackColor;
            if (b.A == 255) return b;
            c = c.Parent;
        }
        return ColBackground;
    }

    private static void HookAdded(Control c)
    {
        if (!MarkOnce(AddedHooked, c)) return;
        c.ControlAdded += OnControlAdded;
    }

    private static void OnControlAdded(object sender, ControlEventArgs e)
    {
        try
        {
            Control p = sender as Control;
            if (e == null || e.Control == null) return;
            ApplyControl(e.Control, p == null ? ColBackground : EffectiveBackUp(p));
        }
        catch { }
    }

    private static void ApplyControl(Control c, Color parentBack)
    {
        if (c == null) return;
        if (!IsKeep(c))
        {
            try { StyleControl(c, parentBack); } catch { }
        }
        try
        {
            if (!CanRecurse(c)) return;
            HookAdded(c);
            Color mine = EffectiveBack(c, parentBack);
            Control[] kids = Children(c);
            for (int i = 0; i < kids.Length; i++) ApplyControl(kids[i], mine);
        }
        catch { }
    }

    // ======================================================== per-type styling
    private static void StyleControl(Control c, Color parentBack)
    {
        if (c is HeaderPanel) return;

        if (c is Form)                // non-top-level / MDI child forms
        {
            c.BackColor = ColBackground;
            c.ForeColor = ColText;
            return;
        }
        TabControl tc = c as TabControl;
        if (tc != null) { StyleTabControl(tc); return; }

        TabPage tp = c as TabPage;
        if (tp != null)
        {
            tp.UseVisualStyleBackColor = false;
            tp.BackColor = ColSurface;
            tp.ForeColor = ColText;
            return;
        }
        GroupBox gb = c as GroupBox;
        if (gb != null) { StyleGroupBox(gb); return; }

        LinkLabel ll = c as LinkLabel;
        if (ll != null)
        {
            ll.LinkColor = ColAccent;
            ll.VisitedLinkColor = ColAccent;
            ll.ActiveLinkColor = Blend(ColAccent, Color.White, 0.3);
            ll.DisabledLinkColor = ColMuted;
            StyleLabel(ll, parentBack);
            return;
        }
        Label lb = c as Label;
        if (lb != null) { StyleLabel(lb, parentBack); return; }

        Button bt = c as Button;
        if (bt != null) { StyleButton(bt); return; }

        if (c is CheckBox || c is RadioButton)
        {
            c.BackColor = MapBackground(c.BackColor, parentBack, c.BackColor.IsSystemColor);
            c.ForeColor = MapForeground(c.ForeColor, EffectiveBack(c, parentBack));
            return;
        }
        RichTextBox rtb = c as RichTextBox;
        if (rtb != null)
        {
            rtb.BackColor = ColInput;
            rtb.ForeColor = ColText;
            Preserve(rtb, delegate { rtb.BorderStyle = BorderStyle.FixedSingle; });
            return;
        }
        TextBox tb = c as TextBox;
        if (tb != null)
        {
            tb.BackColor = ColInput;
            tb.ForeColor = ColText;
            Preserve(tb, delegate { tb.BorderStyle = BorderStyle.FixedSingle; });
            return;
        }
        UpDownBase ud = c as UpDownBase;   // NumericUpDown, DomainUpDown
        if (ud != null)
        {
            ud.BackColor = ColInput;
            ud.ForeColor = ColText;
            Preserve(ud, delegate { ud.BorderStyle = BorderStyle.FixedSingle; });
            return;
        }
        ComboBox cb = c as ComboBox;
        if (cb != null)
        {
            Preserve(cb, delegate { cb.FlatStyle = FlatStyle.Flat; });
            cb.BackColor = ColInput;
            cb.ForeColor = ColText;
            return;
        }
        ListBox lbx = c as ListBox;
        if (lbx != null)
        {
            lbx.BackColor = ColInput;
            lbx.ForeColor = ColText;
            return;
        }
        TrackBar trk = c as TrackBar;      // TrackBar does not accept Transparent
        if (trk != null)
        {
            trk.BackColor = parentBack;
            return;
        }
        if (c is Panel || c is UserControl || c is SplitContainer)
        {
            // Panel, TableLayoutPanel, FlowLayoutPanel, SplitterPanel, UserControl
            c.BackColor = MapBackground(c.BackColor, parentBack, c.BackColor.IsSystemColor);
            if (c.ForeColor.IsSystemColor || IsNearBlack(c.ForeColor)) c.ForeColor = ColText;
            return;
        }
        // Unknown control types (e.g. a custom preview canvas) are left alone on purpose.
    }

    private static void StyleLabel(Label lb, Color parentBack)
    {
        lb.BackColor = MapBackground(lb.BackColor, parentBack, lb.BackColor.IsSystemColor);
        lb.ForeColor = MapForeground(lb.ForeColor, EffectiveBack(lb, parentBack));
    }

    private static void StyleButton(Button b)
    {
        Color fg = b.ForeColor;
        Preserve(b, delegate
        {
            b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat;
        });
        b.BackColor = ColInput;
        b.ForeColor = MapForeground(fg, ColInput);
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = ColBorder;
        b.FlatAppearance.MouseOverBackColor = ColHover;
        b.FlatAppearance.MouseDownBackColor = ColPressed;
        if (MarkOnce(PaintHooked, b))
        {
            b.MouseEnter += OnButtonEnter;
            b.MouseLeave += OnButtonLeave;
            b.MouseDown += OnButtonEnter;
            b.EnabledChanged += OnButtonLeave;
        }
    }

    private static void OnButtonEnter(object sender, EventArgs e)
    {
        try
        {
            Button b = sender as Button;
            if (b != null && b.Enabled) b.FlatAppearance.BorderColor = ColAccent;
        }
        catch { }
    }

    private static void OnButtonLeave(object sender, EventArgs e)
    {
        try
        {
            Button b = sender as Button;
            if (b != null) b.FlatAppearance.BorderColor = ColBorder;
        }
        catch { }
    }

    /// <summary>Runs a style change and puts Location/Size back if the change moved them.</summary>
    private static void Preserve(Control c, Action change)
    {
        Point loc = c.Location;
        Size size = c.Size;
        change();
        try
        {
            if (c.Dock == DockStyle.None)
            {
                if (c.Location != loc) c.Location = loc;
                if (c.Size != size) c.Size = size;
            }
        }
        catch { }
    }

    private static bool TrySetStyle(Control c, ControlStyles styles, bool value)
    {
        try
        {
            MethodInfo m = typeof(Control).GetMethod("SetStyle", BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return false;
            m.Invoke(c, new object[] { styles, value });
            return true;
        }
        catch { return false; }
    }

    private static int Px(Control c, int px)
    {
        int dpi = 96;
        try { dpi = c.DeviceDpi; } catch { }
        if (dpi <= 0) dpi = 96;
        return (int)Math.Round(px * dpi / 96.0);
    }

    // ---------------------------------------------------------------- GroupBox
    private static void StyleGroupBox(GroupBox gb)
    {
        if (gb.FlatStyle == FlatStyle.System) gb.FlatStyle = FlatStyle.Standard; // System skips OnPaint
        gb.BackColor = ColSurface;
        gb.ForeColor = ColText;
        if (MarkOnce(PaintHooked, gb))
        {
            TrySetStyle(gb, ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            gb.Paint += OnGroupBoxPaint;
        }
        gb.Invalidate();
    }

    private static void OnGroupBoxPaint(object sender, PaintEventArgs e)
    {
        try
        {
            GroupBox gb = sender as GroupBox;
            if (gb == null || gb.Width < 2 || gb.Height < 2) return;
            Graphics g = e.Graphics;
            Color back = EffectiveBack(gb, ColSurface);
            using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, gb.ClientRectangle);

            string text = gb.Text;
            bool hasText = !string.IsNullOrEmpty(text);
            Font font = gb.Font;
            int top = hasText ? font.Height / 2 : 0;
            Rectangle box = new Rectangle(0, top, gb.Width - 1, Math.Max(1, gb.Height - top - 1));
            using (Pen p = new Pen(ColBorder)) g.DrawRectangle(p, box);

            if (hasText)
            {
                TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                int x = Px(gb, 8);
                int maxW = Math.Max(0, gb.Width - 2 * x);
                Size ts = TextRenderer.MeasureText(g, text, font, new Size(maxW, font.Height), flags);
                int w = Math.Min(ts.Width, maxW);
                using (SolidBrush b = new SolidBrush(back))
                    g.FillRectangle(b, new Rectangle(x - Px(gb, 3), 0, w + Px(gb, 6), font.Height));
                TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, w, font.Height),
                    gb.Enabled ? ColAccent : ColMuted, flags);
            }
        }
        catch { }
    }

    // -------------------------------------------------------------- TabControl
    // Preferred: switch the TabControl to UserPaint (via reflection) so the whole
    // control -- tab headers AND the empty strip behind them -- is drawn by us.
    // Fallback (if SetStyle can't be reached): OwnerDrawFixed headers only; the
    // empty strip area then stays system-coloured.
    private static void StyleTabControl(TabControl tc)
    {
        if (!MarkOnce(PaintHooked, tc)) { tc.Invalidate(); return; }
        bool userPaint = TrySetStyle(tc,
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        if (userPaint)
        {
            tc.Paint += OnTabPaint;
        }
        else
        {
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.DrawItem += OnTabDrawItem;
        }
        tc.Invalidate();
    }

    private static void OnTabPaint(object sender, PaintEventArgs e)
    {
        try
        {
            TabControl tc = sender as TabControl;
            if (tc == null) return;
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(ColBackground)) g.FillRectangle(b, tc.ClientRectangle);

            Rectangle page = tc.DisplayRectangle;
            page.Inflate(2, 2);
            page.Intersect(tc.ClientRectangle);
            using (SolidBrush b = new SolidBrush(ColSurface)) g.FillRectangle(b, page);
            using (Pen p = new Pen(ColBorder))
                g.DrawRectangle(p, page.X, page.Y, Math.Max(0, page.Width - 1), Math.Max(0, page.Height - 1));

            for (int i = 0; i < tc.TabCount; i++)
            {
                Rectangle r = tc.GetTabRect(i);
                if (r.Width <= 0 || r.Height <= 0) continue;
                DrawTab(g, tc, i, r, i == tc.SelectedIndex, page);
            }
        }
        catch { }
    }

    private static void OnTabDrawItem(object sender, DrawItemEventArgs e)
    {
        try
        {
            TabControl tc = sender as TabControl;
            if (tc == null || e.Index < 0 || e.Index >= tc.TabCount) return;
            DrawTab(e.Graphics, tc, e.Index, e.Bounds, e.Index == tc.SelectedIndex, Rectangle.Empty);
        }
        catch { }
    }

    private static void DrawTab(Graphics g, TabControl tc, int index, Rectangle r, bool selected, Rectangle page)
    {
        bool top = tc.Alignment == TabAlignment.Top;
        Rectangle fill = r;
        if (selected && top && !page.IsEmpty)
            fill = new Rectangle(r.X, r.Y, r.Width, Math.Max(r.Height, page.Y - r.Y + 1)); // merge into page

        using (SolidBrush b = new SolidBrush(selected ? ColSurface : ColBackground)) g.FillRectangle(b, fill);
        using (Pen p = new Pen(ColBorder))
        {
            if (top)
            {
                g.DrawLine(p, fill.Left, fill.Bottom - 1, fill.Left, fill.Top);
                g.DrawLine(p, fill.Left, fill.Top, fill.Right - 1, fill.Top);
                g.DrawLine(p, fill.Right - 1, fill.Top, fill.Right - 1, fill.Bottom - 1);
            }
            else
            {
                g.DrawRectangle(p, fill.X, fill.Y, Math.Max(0, fill.Width - 1), Math.Max(0, fill.Height - 1));
            }
        }
        if (selected)
        {
            int bar = Math.Max(2, Px(tc, 2));
            using (SolidBrush b = new SolidBrush(ColAccent))
                g.FillRectangle(b, new Rectangle(r.X, r.Y, r.Width, bar));
        }
        string text = tc.TabPages[index].Text;
        TextRenderer.DrawText(g, text, tc.Font, r, selected ? ColAccent : ColMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    // ------------------------------------------------------- dark title bar
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static void HookDarkTitleBar(Form f)
    {
        if (f.IsHandleCreated) DarkTitleBar(f.Handle);
        if (MarkOnce(TitleHooked, f)) f.HandleCreated += OnFormHandleCreated; // handle can be recreated
    }

    private static void OnFormHandleCreated(object sender, EventArgs e)
    {
        try
        {
            Form f = sender as Form;
            if (f != null) DarkTitleBar(f.Handle);
        }
        catch { }
    }

    private static void DarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        int on = 1;
        int hr = -1;
        try { hr = DwmSetWindowAttribute(hwnd, 20, ref on, 4); } catch { } // DWMWA_USE_IMMERSIVE_DARK_MODE (20H1+)
        if (hr != 0)
        {
            try { DwmSetWindowAttribute(hwnd, 19, ref on, 4); } catch { }  // pre-20H1 value
        }
    }

    // ---------------------------------------------------------------- ring art
    /// <summary>Two-tone ring fitted in bounds: black outline under a green ring.</summary>
    internal static void DrawRing(Graphics g, RectangleF bounds)
    {
        float s = Math.Min(bounds.Width, bounds.Height);
        if (s < 4) return;
        float o = Math.Max(1f, (float)Math.Round(s / 16f));      // black outline width each side
        float w = Math.Max(2f, (float)Math.Round(s * 0.14f));    // green stroke
        float bw = w + 2 * o;
        float rad = s / 2f - bw / 2f;
        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        RectangleF circle = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);

        SmoothingMode oldSm = g.SmoothingMode;
        PixelOffsetMode oldPo = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (Pen outer = new Pen(ColRingOuter, bw))
        using (Pen inner = new Pen(ColRingInner, w))
        {
            g.DrawEllipse(outer, circle);
            g.DrawEllipse(inner, circle);
        }
        g.SmoothingMode = oldSm;
        g.PixelOffsetMode = oldPo;
    }

    private static Bitmap RenderRing(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            DrawRing(g, new RectangleF(0, 0, size, size));
        }
        return bmp;
    }

    private static byte[] cachedIco;

    private static byte[] IconBytes()
    {
        if (cachedIco != null) return cachedIco;
        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64 };
        List<int> sz = new List<int>();
        List<byte[]> px = new List<byte[]>();
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            using (Bitmap bmp = RenderRing(s))
            {
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] buf = new byte[s * s * 4];
                    for (int y = 0; y < s; y++)
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), buf, y * s * 4, s * 4);
                    sz.Add(s);
                    px.Add(buf);
                }
                finally { bmp.UnlockBits(data); }
            }
        }
        cachedIco = BuildIco(sz, px);
        return cachedIco;
    }

    /// <summary>Fallback: GetHicon -> managed clone (owns its own copy) -> DestroyIcon on the original.</summary>
    private static Icon HiconIcon(int size)
    {
        using (Bitmap bmp = RenderRing(size))
        {
            IntPtr h = bmp.GetHicon();
            try
            {
                using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
            }
            finally { DestroyIcon(h); }
        }
    }

    // ------------------------------------------------------------------ header
    private sealed class HeaderPanel : Panel
    {
        private readonly string title;
        private readonly string subtitle;
        private readonly string version;

        public HeaderPanel(string title, string subtitle, string version)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.version = version;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            BackColor = ColHeader;
            ForeColor = ColText;
            Tag = "theme:keep-header";
            TabStop = false;
            FixHeight();
        }

        private void FixHeight()
        {
            try { Height = Px(this, 44); } catch { }
        }

        // Height is DPI-scaled by us; stop form auto-scaling from scaling it a second time.
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified & ~BoundsSpecified.Height);
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            FixHeight();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FixHeight();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                using (SolidBrush b = new SolidBrush(ColHeader)) g.FillRectangle(b, 0, 0, w, h);
                int line = Math.Max(1, Px(this, 1));
                using (SolidBrush b = new SolidBrush(ColAccent)) g.FillRectangle(b, 0, h - line, w, line);

                int pad = Px(this, 12);
                int icon = Px(this, 22);
                int textH = h - line;
                DrawRing(g, new RectangleF(pad, (textH - icon) / 2f, icon, icon));

                float s = Px(this, 1000) / 1000f;
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                using (Font tf = new Font("Microsoft YaHei UI", 16f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font sf = new Font("Microsoft YaHei UI", 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    int right = w - pad;
                    if (version.Length > 0)
                    {
                        Size vs = TextRenderer.MeasureText(g, version, sf, new Size(w, textH), flags);
                        TextRenderer.DrawText(g, version, sf, new Rectangle(right - vs.Width, 0, vs.Width, textH), ColMuted, flags);
                        right -= vs.Width + Px(this, 12);
                    }
                    int x = pad + icon + Px(this, 10);
                    if (title.Length > 0 && right > x)
                    {
                        Size ts = TextRenderer.MeasureText(g, title, tf, new Size(w, textH), flags);
                        int tw = Math.Min(ts.Width, right - x);
                        TextRenderer.DrawText(g, title, tf, new Rectangle(x, 0, tw, textH), ColText, flags);
                        x += tw + Px(this, 10);
                    }
                    if (subtitle.Length > 0 && right > x)
                        TextRenderer.DrawText(g, subtitle, sf, new Rectangle(x, 0, right - x, textH), ColMuted, flags);
                }
            }
            catch { }
        }
    }
#endif
}
