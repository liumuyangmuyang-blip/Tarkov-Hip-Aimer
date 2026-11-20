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
    // ------------------------------------------------------------------ palette (v4.0: dark terminal + neon)
    internal static readonly Color ColBackground = Color.FromArgb(0x0A, 0x0E, 0x11); // window, nav
    internal static readonly Color ColSurface    = Color.FromArgb(0x10, 0x16, 0x1B); // pages, group boxes
    internal static readonly Color ColInput      = Color.FromArgb(0x17, 0x20, 0x28); // buttons, text boxes, combos
    internal static readonly Color ColBorder     = Color.FromArgb(0x22, 0x33, 0x3B);
    internal static readonly Color ColText       = Color.FromArgb(0xD6, 0xE9, 0xE1);
    internal static readonly Color ColMuted      = Color.FromArgb(0x7A, 0x96, 0x8F); // >= 4.5:1 on every surface
    internal static readonly Color ColAccent     = Color.FromArgb(0x39, 0xFF, 0x8C); // neon green
    internal static readonly Color ColAccent2    = Color.FromArgb(0x00, 0xE5, 0xFF); // cyan
    internal static readonly Color ColWarn       = Color.FromArgb(0xFF, 0xB5, 0x47); // amber
    internal static readonly Color ColHeader     = Color.FromArgb(0x07, 0x0A, 0x0C); // a shade darker than background
    internal static readonly Color ColHover      = Color.FromArgb(0x1D, 0x2A, 0x33); // hover fill
    internal static readonly Color ColGrid       = Color.FromArgb(0x15, 0x23, 0x2A); // chart grid lines
    internal static readonly Color ColPressed    = Blend(ColInput, ColAccent, 0.25);  // pressed fill
    internal static readonly Color ColCard       = Color.FromArgb(0x13, 0x1B, 0x21); // v5.1 flat: settings cards (Section)
    internal static readonly Color ColAccentSoft = Color.FromArgb(0x1B, 0x3A, 0x2C); // v5.1 flat: selected / checked fill
    internal static readonly Color ColOnAccent   = Color.FromArgb(0x06, 0x12, 0x0B); // v5.1 flat: text on a filled accent
    internal static readonly Color ColRingInner  = Color.FromArgb(0x00, 0xDF, 0x00);

    // ------------------------------------------------- v5.1 flat geometry (decision 14; SPEC v4 「unified FLAT design」)
    // One corner radius (0 = square corners everywhere outside the hacker dashboard), one border width, one accent bar,
    // one focus frame, one spacing scale 4 / 8 / 12 / 16 / 24 (Sp). Every other file uses these, no local values.
    internal const int Radius = 0;
    internal const int BorderPx = 1;
    internal const int AccentBarPx = 3;
    internal const int FocusPx = 1;
    internal static readonly int[] Spacing = { 4, 8, 12, 16, 24 };

    /// <summary>Spacing token at a scale: n is rounded to the nearest of 4 / 8 / 12 / 16 / 24, then x scale.</summary>
    internal static int Sp(int n, double scale)
    {
        int best = Spacing[0];
        for (int i = 1; i < Spacing.Length; i++) if (Math.Abs(Spacing[i] - n) < Math.Abs(best - n)) best = Spacing[i];
        if (double.IsNaN(scale) || scale <= 0) scale = 1;
        return (int)Math.Round(best * scale);
    }
    internal static readonly Color ColRingOuter  = Color.FromArgb(0x00, 0x00, 0x00);

    // ------------------------------------------------------- decode text effect (pure)
    private const string DecodeAscii = "01#%&*+=<>/\\$?7ABCDEFX";
    private const string DecodeWide = "０１＃％＆＊＋＝＜＞／＼＄？７ＡＢＣＤＥＦＸ";

    /// <summary>
    /// "Decoding" text effect: characters left of progress*length are the real text, the rest are scrambled glyphs of
    /// the same width class (full-width glyphs for CJK, ASCII for ASCII; spaces stay spaces). progress &lt;= 0 = all
    /// scrambled, &gt;= 1 = the text itself. Deterministic for a given seed. Never throws; null -> "".
    /// </summary>
    internal static string Decode(string text, double progress, int seed)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (double.IsNaN(progress) || progress >= 1) return text;
        if (progress < 0) progress = 0;
        int n = text.Length;
        int shown = (int)Math.Floor(progress * n);
        char[] r = text.ToCharArray();
        uint h = (uint)seed * 2654435761u + 12345u;
        for (int i = shown; i < n; i++)
        {
            char c = r[i];
            h = h * 1103515245u + 12345u;
            if (char.IsWhiteSpace(c) || char.IsSurrogate(c)) continue;
            int pick = (int)((h >> 8) % (uint)DecodeAscii.Length);
            r[i] = c > 0x2E7F ? DecodeWide[pick] : DecodeAscii[pick];
        }
        return new string(r);
    }

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

    /// <summary>
    /// v5.0.2 (UI M7): a themed drop-down list exactly 'height' px tall: ItemHeight = height minus the combo's frame (what
    /// Windows adds around the item), never below the text. Mono sets Height directly. Returns true when it changed.
    /// </summary>
    public static bool MatchComboHeight(ComboBox cb, int height)
    {
        if (cb == null || cb.IsDisposed || height <= 0 || cb.DrawMode == DrawMode.Normal) return false;
        try
        {
            int frame = Math.Max(0, cb.Height - cb.ItemHeight);
            int ih = Math.Max(cb.Font.Height + 1, height - frame);
            if (ih == cb.ItemHeight && cb.Height == ih + frame) return false;
            cb.ItemHeight = ih;
            try { if (cb.Height != ih + frame) cb.Height = ih + frame; } catch { }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// v5.0.2 (Leo: nothing ticks while the game is in front): Activated -> ForegroundState in front; Deactivate -> one
    /// check of the real foreground window (another of our windows, e.g. the tutorial or a dialog, keeps it "ours").
    /// Event-driven: no timer, no polling.
    /// </summary>
    public static void TrackForeground(Form f)
    {
        if (f == null) return;
        f.Activated += delegate { TarkovCrosshair.ForegroundState.Set(1); };
        f.Deactivate += delegate
        {
            try
            {
                if (f.IsDisposed || !f.IsHandleCreated) return;
                f.BeginInvoke((Action)delegate
                {
                    int fg = -1;
                    try { fg = TarkovCrosshair.Native.ForegroundIsOurs(); } catch { fg = -1; }
                    TarkovCrosshair.ForegroundState.Set(fg);
                });
            }
            catch { }
        };
    }

    /// <summary>Header bar: ring icon, decoding title, muted subtitle, version on the right, scanlines. Dock = Top.</summary>
    public static Control Header(string title, string subtitle, string version)
    {
        return new HeaderPanel(title ?? "", subtitle ?? "", version ?? "");
    }

    /// <summary>
    /// v4.1 drag freeze: calls IPausable.Pause (pause = true) or Resume on every control of the tree that implements it
    /// (dashboard, previews, animated titles, glow button). One failing control never stops the rest. Returns how many.
    /// </summary>
    public static int PauseTree(Control root, bool pause)
    {
        if (root == null) return 0;
        int n = 0;
        TarkovCrosshair.IPausable p = root as TarkovCrosshair.IPausable;
        if (p != null)
        {
            try { if (pause) p.Pause(); else p.Resume(); n++; } catch { }
        }
        Control[] kids;
        try { kids = Children(root); } catch { return n; }
        for (int i = 0; i < kids.Length; i++) n += PauseTree(kids[i], pause);
        return n;
    }

    /// <summary>
    /// StyleToolTip for every ToolTip field of a control the caller does not own (e.g. the 外观 page's StylePanel keeps
    /// its ToolTip private): fields found by reflection; ones already owner-drawn are left alone. Returns how many.
    /// </summary>
    public static int StyleToolTips(object owner, Func<Font> font)
    {
        int n = 0;
        if (owner == null || font == null) return 0;
        try
        {
            foreach (FieldInfo fi in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(ToolTip).IsAssignableFrom(fi.FieldType)) continue;
                ToolTip t = fi.GetValue(owner) as ToolTip;
                if (t == null || t.OwnerDraw) continue;
                StyleToolTip(t, font);
                n++;
            }
        }
        catch { }
        return n;
    }

    /// <summary>
    /// Dark, readable tooltips (SPEC v4: text >= 11 pt; the stock tooltip uses the ~9 pt system font): owner-drawn with
    /// font() (the caller's current UI font, so it follows the window's text size), wrapped at about 22 CJK characters.
    /// </summary>
    public static void StyleToolTip(ToolTip tip, Func<Font> font)
    {
        if (tip == null || font == null || tip.OwnerDraw) return; // already styled: never hook twice
        try
        {
            tip.OwnerDraw = true;
            tip.BackColor = ColInput;
            tip.ForeColor = ColText;
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
            tip.Popup += delegate(object s, PopupEventArgs e)
            {
                try
                {
                    Font f = font();
                    string text = tip.GetToolTip(e.AssociatedControl) ?? "";
                    int pad = Math.Max(4, f.Height / 3);
                    int maxW = f.Height * 23; // ~22 CJK characters per line
                    Size ts = TextRenderer.MeasureText(text, f, new Size(maxW, 0), flags);
                    e.ToolTipSize = new Size(ts.Width + 2 * pad + 2, ts.Height + 2 * pad + 2);
                }
                catch { }
            };
            tip.Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                try
                {
                    Font f = font();
                    int pad = Math.Max(4, f.Height / 3);
                    Rectangle r = e.Bounds;
                    using (SolidBrush b = new SolidBrush(ColInput)) e.Graphics.FillRectangle(b, r);
                    using (Pen p = new Pen(ColBorder)) e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    Rectangle tr = new Rectangle(r.X + pad + 1, r.Y + pad + 1, Math.Max(1, r.Width - 2 * pad - 2), Math.Max(1, r.Height - 2 * pad - 2));
                    TextRenderer.DrawText(e.Graphics, e.ToolTipText ?? "", f, tr, ColText, flags);
                }
                catch { e.DrawBackground(); e.DrawText(); }
            };
        }
        catch { }
    }

    /// <summary>Monospace font for numbers / terminal text (Cascadia Mono, Consolas, else generic). Caller disposes.</summary>
    public static Font Mono(float pt) { return Mono(pt, FontStyle.Regular); }

    public static Font Mono(float pt, FontStyle style)
    {
        string[] names = { "Cascadia Mono", "Consolas", "Lucida Console" };
        foreach (string n in names)
        {
            try
            {
                Font f = new Font(n, pt, style);
                if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
        }
        return new Font(FontFamily.GenericMonospace, pt, style);
    }

    /// <summary>UI font (Microsoft YaHei UI, else the system message font). Caller disposes.</summary>
    public static Font Ui(float pt) { return Ui(pt, FontStyle.Regular); }

    public static Font Ui(float pt, FontStyle style)
    {
        try
        {
            Font f = new Font("Microsoft YaHei UI", pt, style);
            if (f.Name == "Microsoft YaHei UI") return f;
            f.Dispose();
        }
        catch { }
        return new Font(SystemFonts.MessageBoxFont.FontFamily, pt, style);
    }

    /// <summary>
    /// v5.1 (decision 9): the font for one piece of owner-drawn text: mono only when asked AND the text has no Chinese
    /// (Cascadia / Consolas have no CJK glyphs; Windows substitutes a narrower font and widths go wrong), else the UI font.
    /// Caller disposes.
    /// </summary>
    public static Font FontFor(string text, float pt, FontStyle style, bool mono)
    {
        return mono && !TarkovCrosshair.TextFitMath.HasCjk(text) ? Mono(pt, style) : Ui(pt, style);
    }

    /// <summary>False when Windows animations are turned off: draw the final frame straight away.</summary>
    public static bool AnimationsOn
    {
        get
        {
            try { return SystemInformation.UIEffectsEnabled; } catch { return true; }
        }
    }

    /// <summary>v5.1 flat (decision 14): no texture outside the hacker dashboard -- draws nothing. The dashboard uses
    /// HackerScanlines.</summary>
    public static void DrawScanlines(Graphics g, Rectangle r, int alpha) { }

    /// <summary>v5.1 flat (decision 14): a solid 1 px-style line in c (was an accent -> transparent gradient). The dashboard
    /// uses HackerFadeLine.</summary>
    public static void DrawFadeLine(Graphics g, int x, int y, int w, int h, Color c)
    {
        if (g == null || w <= 0 || h <= 0) return;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(c.R, c.G, c.B))) g.FillRectangle(b, x, y, w, h);
    }

    /// <summary>Hacker dashboard only: subtle CRT scanlines (1 px dark line every 3 px).</summary>
    public static void HackerScanlines(Graphics g, Rectangle r, int alpha)
    {
        if (g == null || r.Width <= 0 || r.Height <= 0 || alpha <= 0) return;
        using (Pen p = new Pen(Color.FromArgb(Math.Min(255, alpha), 0, 0, 0)))
            for (int y = r.Top; y < r.Bottom; y += 3) g.DrawLine(p, r.Left, y, r.Right - 1, y);
    }

    /// <summary>Hacker dashboard only: accent -> transparent horizontal hairline.</summary>
    public static void HackerFadeLine(Graphics g, int x, int y, int w, int h, Color c)
    {
        if (g == null || w <= 1 || h <= 0) return;
        Rectangle r = new Rectangle(x, y, w, h);
        using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(x - 1, y, w + 2, h), c, Color.FromArgb(0, c), LinearGradientMode.Horizontal))
            g.FillRectangle(b, r);
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

    private static readonly ConditionalWeakTable<Control, object> MonoHooked = new ConditionalWeakTable<Control, object>();

    /// <summary>v5.1 (decision 9): a control showing Chinese in a mono font switches to the UI font now and whenever its
    /// text or font changes later (TextFit.FixMonoCjk). Owner-drawn controls pick their own fonts: Theme.FontFor.</summary>
    private static void HookMonoCjk(Control c)
    {
        if (c is TextBoxBase) return; // typed text: the user's business
        TarkovCrosshair.TextFit.FixMonoCjk(c);
        if (!MarkOnce(MonoHooked, c)) return;
        c.TextChanged += OnMonoCjkChanged;
        c.FontChanged += OnMonoCjkChanged;
    }

    private static void OnMonoCjkChanged(object sender, EventArgs e)
    {
        try { TarkovCrosshair.TextFit.FixMonoCjk(sender as Control); } catch { }
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
        try { HookMonoCjk(c); } catch { } // v5.1 decision 9: Chinese never in the monospace font (keep-tagged too)
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
            ButtonBase bb = (ButtonBase)c;
            Preserve(c, delegate { bb.FlatStyle = FlatStyle.Flat; }); // dark box instead of the white system box
            bb.FlatAppearance.BorderColor = ColMuted;
            bb.FlatAppearance.CheckedBackColor = ColAccentSoft;
            bb.FlatAppearance.MouseOverBackColor = ColHover;
            bb.FlatAppearance.MouseDownBackColor = ColPressed;
            bb.Cursor = Cursors.Hand;
            return;
        }
        RichTextBox rtb = c as RichTextBox;
        if (rtb != null)
        {
            rtb.BackColor = ColInput;
            rtb.ForeColor = ColText;
            Preserve(rtb, delegate { rtb.BorderStyle = BorderStyle.FixedSingle; });
            HookDarkScroll(rtb);
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
            HookSpin(ud); // v4.1 UI41-review #2: dark spin arrows instead of the light system ones
            return;
        }
        ComboBox cb = c as ComboBox;
        if (cb != null)
        {
            Preserve(cb, delegate { cb.FlatStyle = FlatStyle.Flat; });
            cb.BackColor = ColInput;
            cb.ForeColor = ColText;
            HookComboArrow(cb); // v4.1 UI41-review #2: dark drop-down button + border (flat combos paint them light)
            // Owner-drawn items: the height follows the font at every DPI (a fixed system height clipped the text in
            // the 200 % render) and the selection uses the theme instead of system blue.
            if (cb.DropDownStyle == ComboBoxStyle.DropDownList && cb.DrawMode == DrawMode.Normal && MarkOnce(PaintHooked, cb))
            {
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = cb.Font.Height + Px(cb, 6);
                try { cb.Height = cb.ItemHeight + Px(cb, 6); } catch { } // Windows derives it from ItemHeight anyway; Mono needs it
                cb.DrawItem += OnComboDrawItem;
                cb.FontChanged += delegate(object s, EventArgs e2) { try { cb.ItemHeight = cb.Font.Height + Px(cb, 6); } catch { } };
            }
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
            ScrollableControl sc = c as ScrollableControl;
            if (sc != null && sc.AutoScroll) HookDarkScroll(c); // dark scroll bars where a page can scroll
            return;
        }
        // Unknown control types (e.g. a custom preview canvas) are left alone on purpose.
    }

    private static void OnComboDrawItem(object sender, DrawItemEventArgs e)
    {
        try
        {
            ComboBox cb = sender as ComboBox;
            if (cb == null) return;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !edit;
            Color back = sel ? ColAccentSoft : ColInput;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index < 0 || e.Index >= cb.Items.Count) return;
            string text = cb.GetItemText(cb.Items[e.Index]);
            Color fg = !cb.Enabled ? ColMuted : (sel ? ColAccent : ColText);
            Rectangle r = new Rectangle(e.Bounds.X + Px(cb, 4), e.Bounds.Y, Math.Max(0, e.Bounds.Width - Px(cb, 6)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, cb.Font, r, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        catch { }
    }

    private static void StyleLabel(Label lb, Color parentBack)
    {
        lb.BackColor = MapBackground(lb.BackColor, parentBack, lb.BackColor.IsSystemColor);
        lb.ForeColor = MapForeground(lb.ForeColor, EffectiveBack(lb, parentBack));
    }

    private static bool IsAccent(Control c)
    {
        string s = c.Tag as string;
        return s != null && s.StartsWith("theme:accent", StringComparison.Ordinal);
    }

    private static Color RestBorder(Button b) { return IsAccent(b) ? ColAccent : ColBorder; }

    private static void StyleButton(Button b)
    {
        Color fg = b.ForeColor;
        bool accent = IsAccent(b);
        Preserve(b, delegate
        {
            b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat;
        });
        // v5.1 flat (decision 14): primary = filled accent with dark text; secondary = input fill + 1 px border
        b.BackColor = accent ? ColAccent : ColInput;
        b.ForeColor = accent ? ColOnAccent : MapForeground(fg, ColInput);
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.BorderSize = BorderPx;
        b.FlatAppearance.BorderColor = RestBorder(b);
        b.FlatAppearance.MouseOverBackColor = accent ? Blend(ColAccent, Color.White, 0.18) : ColHover;
        b.FlatAppearance.MouseDownBackColor = accent ? Blend(ColAccent, ColOnAccent, 0.25) : ColPressed;
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
            if (b != null && b.Enabled) b.FlatAppearance.BorderColor = IsAccent(b) ? ColAccent : ColAccent2;
        }
        catch { }
    }

    private static void OnButtonLeave(object sender, EventArgs e)
    {
        try
        {
            Button b = sender as Button;
            if (b != null) b.FlatAppearance.BorderColor = RestBorder(b);
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
                    gb.Enabled ? ColAccent2 : ColMuted, flags); // v5.1 flat: no corner ticks
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

    // ------------------------------------------- dark native parts (v4.1, UI41-review #2)
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

    private static readonly ConditionalWeakTable<Control, object> DarkScrollHooked = new ConditionalWeakTable<Control, object>();
    private static readonly ConditionalWeakTable<Control, object> SpinHooked = new ConditionalWeakTable<Control, object>();
    private static readonly ConditionalWeakTable<Control, object> ComboHooked = new ConditionalWeakTable<Control, object>();
    private static bool darkScrollUnavailable;

    /// <summary>Dark scroll bars (Windows 10 1809+: SetWindowTheme "DarkMode_Explorer"); harmless where it is not supported.
    /// UNVERIFIED on Windows (built and rendered on Mono, which draws its own scroll bars).</summary>
    private static void HookDarkScroll(Control c)
    {
        if (darkScrollUnavailable || !MarkOnce(DarkScrollHooked, c)) return;
        if (c.IsHandleCreated) DarkScroll(c.Handle);
        c.HandleCreated += OnDarkScrollHandle;
    }

    private static void OnDarkScrollHandle(object sender, EventArgs e)
    {
        Control c = sender as Control;
        if (c != null) DarkScroll(c.Handle);
    }

    private static void DarkScroll(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || darkScrollUnavailable) return;
        try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); }
        catch { darkScrollUnavailable = true; } // no uxtheme (Mono): stop trying
    }

    /// <summary>The spin buttons of a number box: painted over (dark fill, light arrows, hover cyan, pressed green) after the
    /// stock painting. .NET raises the inner UpDownButtons' Paint event after drawing its buttons.</summary>
    private static void HookSpin(UpDownBase ud)
    {
        if (!MarkOnce(SpinHooked, ud)) return;
        foreach (Control ch in ud.Controls)
        {
            if (ch is TextBoxBase) continue; // the edit part
            ch.Paint += OnSpinPaint;
            ch.Invalidate();
        }
    }

    private static void OnSpinPaint(object sender, PaintEventArgs e)
    {
        try
        {
            Control s = sender as Control;
            if (s == null) return;
            Graphics g = e.Graphics;
            int w = s.ClientSize.Width, h = s.ClientSize.Height;
            if (w <= 1 || h <= 1) return;
            int half = h / 2;
            Point m = s.PointToClient(Control.MousePosition);
            bool inside = s.ClientRectangle.Contains(m);
            bool pressed = inside && (Control.MouseButtons & MouseButtons.Left) != 0;
            bool enabled = s.Enabled && (s.Parent == null || s.Parent.Enabled);
            DrawSpinHalf(g, new Rectangle(0, 0, w, half), true, enabled, inside && m.Y < half, pressed && m.Y < half);
            DrawSpinHalf(g, new Rectangle(0, half, w, h - half), false, enabled, inside && m.Y >= half, pressed && m.Y >= half);
            using (Pen p = new Pen(ColBorder))
            {
                g.DrawLine(p, 0, 0, 0, h - 1);
                g.DrawLine(p, 1, half, w - 1, half);
            }
        }
        catch { }
    }

    private static void DrawSpinHalf(Graphics g, Rectangle r, bool up, bool enabled, bool hot, bool pressed)
    {
        Color back = pressed ? ColPressed : (hot && enabled ? ColHover : ColInput);
        using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, r);
        Color fg = !enabled ? ColMuted : (pressed ? ColAccent : (hot ? ColAccent2 : ColText));
        float aw = Math.Max(5f, Math.Min(r.Width - 6f, (r.Height - 2f) * 1.6f));
        float ah = aw / 2f;
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        PointF[] tri = up
            ? new PointF[] { new PointF(cx - aw / 2f, cy + ah / 2f), new PointF(cx + aw / 2f, cy + ah / 2f), new PointF(cx, cy - ah / 2f) }
            : new PointF[] { new PointF(cx - aw / 2f, cy - ah / 2f), new PointF(cx + aw / 2f, cy - ah / 2f), new PointF(cx, cy + ah / 2f) };
        SmoothingMode sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (SolidBrush b = new SolidBrush(fg)) g.FillPolygon(b, tri);
        g.SmoothingMode = sm;
    }

    /// <summary>Flat combo boxes paint their drop-down button light grey and the outer border white (FlatComboAdapter uses
    /// system colours): repainted dark right after every WM_PAINT through a subclass of the combo's window.</summary>
    private static void HookComboArrow(ComboBox cb)
    {
        if (!MarkOnce(ComboHooked, cb)) return;
        try { new ComboArrowPainter(cb); } catch { }
    }

    private sealed class ComboArrowPainter : NativeWindow
    {
        private const int WmPaint = 0x000F;
        private readonly ComboBox cb;

        public ComboArrowPainter(ComboBox cb)
        {
            this.cb = cb;
            cb.HandleCreated += delegate { try { if (Handle == IntPtr.Zero) AssignHandle(cb.Handle); } catch { } };
            cb.HandleDestroyed += delegate { try { ReleaseHandle(); } catch { } };
            if (cb.IsHandleCreated) AssignHandle(cb.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmPaint) { try { PaintButton(); } catch { } }
        }

        private void PaintButton()
        {
            if (cb.IsDisposed || cb.FlatStyle != FlatStyle.Flat || cb.DropDownStyle == ComboBoxStyle.Simple) return;
            int w = cb.Width, h = cb.Height;
            if (w < 8 || h < 6) return;
            int aw = Math.Max(Px(cb, 12), SystemInformation.HorizontalScrollBarArrowWidth);
            bool hot = cb.ClientRectangle.Contains(cb.PointToClient(Control.MousePosition));
            using (Graphics g = Graphics.FromHwnd(cb.Handle))
            {
                Rectangle btn = new Rectangle(w - aw - 2, 1, aw + 1, h - 2);
                using (SolidBrush b = new SolidBrush(hot && cb.Enabled ? ColHover : ColInput)) g.FillRectangle(b, btn);
                Color fg = !cb.Enabled ? ColMuted : (cb.DroppedDown ? ColAccent : (hot || cb.Focused ? ColAccent2 : ColText));
                float tw = Math.Max(5f, Math.Min(aw - 6f, h * 0.36f)), th = tw / 2f;
                float cx = btn.Left + btn.Width / 2f, cy = h / 2f;
                SmoothingMode sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(fg))
                    g.FillPolygon(b, new PointF[] { new PointF(cx - tw / 2f, cy - th / 2f), new PointF(cx + tw / 2f, cy - th / 2f), new PointF(cx, cy + th / 2f) });
                g.SmoothingMode = sm;
                using (Pen outer = new Pen(cb.Focused ? Blend(ColBorder, ColAccent2, 0.6) : ColBorder))
                    g.DrawRectangle(outer, 0, 0, w - 1, h - 1);
                using (Pen inner = new Pen(ColInput))
                    g.DrawRectangle(inner, 1, 1, w - 3, h - 3);
            }
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
    private sealed class HeaderPanel : Panel, TarkovCrosshair.IPausable
    {
        private const double DecodeMs = 650;
        private bool pausedAnim;

        /// <summary>v4.1 drag freeze: the decode animation stops on its current frame and goes on after Resume.</summary>
        public void Pause()
        {
            if (!timer.Enabled) return;
            timer.Stop(); clock.Stop(); pausedAnim = true;
        }

        public void Resume()
        {
            if (!pausedAnim) return;
            pausedAnim = false;
            if (IsDisposed) return;
            clock.Start(); timer.Start();
        }
        private readonly string title;
        private readonly string subtitle;
        private readonly string version;
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private readonly Timer timer = new Timer();

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
            timer.Interval = 30;
            timer.Tick += delegate
            {
                if (clock.Elapsed.TotalMilliseconds >= DecodeMs) { timer.Stop(); clock.Stop(); }
                Invalidate();
            };
            tf = FontFor(title, 13f, FontStyle.Bold, true); // v5.1: Chinese titles in the UI font
            FixHeight();
        }

        // v4.3.1: one fixed text size (no window-size scaling); fonts made once, not on every paint
        private readonly Font tf, sf = Ui(11f), vf = Mono(11f);

        /// <summary>v5.1 (decision 9): 48 px, or taller when the fonts need it (title / subtitle line + 8 px each side).</summary>
        private void FixHeight()
        {
            try
            {
                int need = Math.Max(tf == null ? 0 : tf.Height, sf.Height) + Px(this, 16) + Px(this, 1);
                Height = Math.Max(Px(this, 48), need);
            }
            catch { }
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // v5.1 flat (decision 14): no decode animation outside the dashboard -- the title is drawn final at once
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); tf.Dispose(); sf.Dispose(); vf.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                using (SolidBrush b = new SolidBrush(ColHeader)) g.FillRectangle(b, 0, 0, w, h); // v5.1 flat: solid fill
                int line = Math.Max(1, Px(this, BorderPx));
                using (SolidBrush b = new SolidBrush(ColBorder)) g.FillRectangle(b, 0, h - line, w, line);

                int pad = Px(this, 14);
                int icon = Px(this, 22);
                int textH = h - line;
                DrawRing(g, new RectangleF(pad, (textH - icon) / 2f, icon, icon));

                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                // point sizes: GDI scales them with the screen DPI already
                // SPEC v4 readability (>= 11 pt), one fixed size (v4.3.1)
                {
                    int right = w - pad;
                    if (version.Length > 0)
                    {
                        string v = "[ " + version + " ]";
                        Size vs = TextRenderer.MeasureText(g, v, vf, new Size(w, textH), flags);
                        TextRenderer.DrawText(g, v, vf, new Rectangle(right - vs.Width, 0, vs.Width, textH), ColMuted, flags);
                        right -= vs.Width + Px(this, 12);
                    }
                    int x = pad + icon + Px(this, 12);
                    string shown = title.ToUpperInvariant();
                    if (shown.Length > 0 && right > x)
                    {
                        Size ts = TextRenderer.MeasureText(g, title.ToUpperInvariant(), tf, new Size(w, textH), flags);
                        int tw = Math.Min(ts.Width, right - x);
                        TextRenderer.DrawText(g, shown, tf, new Rectangle(x, 0, tw, textH), ColAccent, flags);
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

#if !THEME_PURE_CHECK
namespace TarkovCrosshair
{
    /// <summary>One entry of the settings window's left navigation: "01  按键", hover, selected bar.</summary>
    internal sealed class NavItem : Control, ITextFitSelf
    {
        private bool selected, hover;
        public readonly string Index;
        public readonly string Title;
        private readonly float k;

        public NavItem(string index, string title, float scale)
        {
            Index = index ?? "";
            Title = title ?? "";
            k = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Tag = "theme:keep-nav";
            Cursor = Cursors.Hand;
            TabStop = true; // keyboard: Tab reaches the list, ↑ / ↓ move, Enter / Space open
            Text = Title;
            AccessibleName = Title;
            AccessibleRole = AccessibleRole.PageTab;
            Height = (int)Math.Round(40 * k);
        }

        /// <summary>v5.1 (decision 9): at least 40 px x scale, taller when the font needs it.</summary>
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            try { Height = Math.Max((int)Math.Round(40 * k), TextNeed(1f).Height); } catch { }
        }

        /// <summary>ITextFitSelf: "00" index (mono) + gap + title (UI font) + margins, at fontScale x the current size.</summary>
        public Size TextNeed(float fontScale)
        {
            float pt = Font.SizeInPoints * fontScale;
            double sc = TextFit.ScaleOf(this) * fontScale;
            using (Font mono = Theme.Mono(Math.Max(11f * fontScale, pt)))
            using (Font ui = new Font(Font.FontFamily, pt, Font.Style))
            {
                Size a = TextFit.NeedSize("00", mono, 0, sc), b = TextFit.NeedSize(Title, ui, 0, sc);
                int w = (int)Math.Round((16 + 12 + 4) * k * fontScale) + a.Width + b.Width;
                return new Size(w, Math.Max(a.Height, b.Height) + (int)Math.Round(8 * k * fontScale));
            }
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        /// <summary>↑ (-1) / ↓ (+1) pressed while this item has focus.</summary>
        public event Action<int> Navigate;

        protected override bool IsInputKey(Keys keyData)
        {
            Keys c = keyData & Keys.KeyCode;
            if (c == Keys.Up || c == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                Action<int> h = Navigate;
                if (h != null) h(e.KeyCode == Keys.Up ? -1 : 1);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                Color back = selected ? Theme.ColAccentSoft : (hover ? Theme.ColHover : Theme.ColBackground);
                using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, 0, 0, w, h);
                if (selected)
                {
                    int bar = Math.Max(2, (int)Math.Round(Theme.AccentBarPx * k));
                    using (SolidBrush b = new SolidBrush(Theme.ColAccent)) g.FillRectangle(b, 0, 0, bar, h); // v5.1 flat: bar only
                }
                TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                int x = (int)Math.Round(16 * k);
                using (Font mono = Theme.Mono(Math.Max(11f, Font.SizeInPoints))) // numbers >= 11 pt (SPEC v4)
                {
                    int iw = TextRenderer.MeasureText(g, "00", mono, new Size(w, h), f).Width;
                    TextRenderer.DrawText(g, Index, mono, new Rectangle(x, 0, iw + 2, h),
                        selected ? Theme.ColAccent : Theme.ColMuted, f); // >= 4.5:1 contrast (was a dimmer grey)
                    x += iw + (int)Math.Round(12 * k);
                }
                TextRenderer.DrawText(g, Title, Font, new Rectangle(x, 0, Math.Max(0, w - x - 4), h),
                    selected ? Theme.ColText : (hover ? Theme.ColText : Theme.ColMuted), f);
                if (Focused && ShowFocusCues)
                    using (Pen fp = new Pen(Theme.ColAccent2, Theme.FocusPx)) // v5.1 flat: one solid focus frame
                        g.DrawRectangle(fp, 1, 1, w - 3, h - 3);
            }
            catch { }
        }
    }

    /// <summary>Page heading: "▍ 标题  // 副标题", decodes in when Restart() is called; accent hairline below.</summary>
    internal sealed class PageTitle : Control, IPausable, ITextFitSelf
    {
        private const double DecodeMs = 420;
        private readonly string title, sub;
        private readonly float k;
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private readonly Timer timer = new Timer();
        private int seed;
        private bool pausedAnim;

        /// <summary>v4.1 drag freeze (IPausable): the decode stops on its current frame.</summary>
        public void Pause()
        {
            if (!timer.Enabled) return;
            timer.Stop(); clock.Stop(); pausedAnim = true;
        }

        public void Resume()
        {
            if (!pausedAnim) return;
            pausedAnim = false;
            if (IsDisposed) return;
            clock.Start(); timer.Start();
        }

        private bool compact;

        /// <summary>v4.1 (UI41-review #1): short windows get a one-line title without the hairline.</summary>
        public bool Compact
        {
            get { return compact; }
            set { if (compact == value) return; compact = value; Invalidate(); }
        }

        public PageTitle(string title, string sub, float scale)
        {
            this.title = title ?? "";
            this.sub = sub ?? "";
            k = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Tag = "theme:keep-title";
            TabStop = false;
            Text = this.title;
            timer.Interval = 30;
            timer.Tick += delegate
            {
                if (clock.Elapsed.TotalMilliseconds >= DecodeMs) { timer.Stop(); clock.Stop(); }
                Invalidate();
            };
        }

        private float TitlePt(float fontScale)
        {
            float pt = Font.SizeInPoints * fontScale;
            return compact ? Math.Max(13f * fontScale, pt * 1.2f) : Math.Max(16f * fontScale, pt * 1.5f);
        }

        /// <summary>ITextFitSelf (v5.1 decision 9): title (UI bold) + gap + subtitle, height from the title font + bar room.</summary>
        public Size TextNeed(float fontScale)
        {
            double sc = TextFit.ScaleOf(this) * fontScale;
            using (Font tf = Theme.Ui(TitlePt(fontScale), FontStyle.Bold))
            {
                Size t = TextFit.NeedSize(title, tf, 0, sc);
                int h = t.Height + (int)Math.Round((compact ? 4 : 10) * k * fontScale);
                int w = Math.Max(1, (int)Math.Round((Theme.AccentBarPx + 10) * k * fontScale)) + t.Width; // the subtitle may ellipsize
                return new Size(w, h);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int minH;
            try { minH = TextNeed(1f).Height; } catch { minH = 0; }
            if (compact) return new Size((int)Math.Round(320 * k), Math.Max(minH, (int)Math.Round(Font.Height * 1.45f + 4 * k)));
            return new Size((int)Math.Round(320 * k), Math.Max(minH, (int)Math.Round(Font.Height * 2.0f + 10 * k)));
        }

        /// <summary>v5.1 flat (decision 14): no decode animation outside the dashboard; kept so callers need no change.</summary>
        public void Restart()
        {
            seed++;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                Color back = Parent != null ? Parent.BackColor : Theme.ColSurface;
                if (back.A < 255) back = Theme.ColSurface;
                using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, 0, 0, w, h);
                TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                // compact (short window): one line, smaller title, no hairline (UI41-review #1)
                int lineY = compact ? h : h - (int)Math.Round(4 * k);
                int bar = Math.Max(2, (int)Math.Round(Theme.AccentBarPx * k));
                using (SolidBrush ab = new SolidBrush(Theme.ColAccent))
                {
                    if (compact) g.FillRectangle(ab, 0, (int)Math.Round(4 * k), bar, Math.Max(1, h - (int)Math.Round(8 * k)));
                    else g.FillRectangle(ab, 0, (int)Math.Round(8 * k), bar, lineY - (int)Math.Round(14 * k));
                }
                int x = bar + (int)Math.Round(10 * k);
                using (Font tf = Theme.Ui(TitlePt(1f), FontStyle.Bold)) // page titles >= 16 pt (compact >= 13 pt)
                {
                    string shown = title;
                    Size ts = TextRenderer.MeasureText(g, title, tf, new Size(w, h), f);
                    Rectangle tr = new Rectangle(x, 0, Math.Min(ts.Width + 4, Math.Max(0, w - x)), lineY - (int)Math.Round(2 * k));
                    TextRenderer.DrawText(g, shown, tf, tr, Theme.ColText, f);
                    x += ts.Width + (int)Math.Round(14 * k);
                }
                if (sub.Length > 0 && x < w)
                    using (Font sf = Theme.FontFor(sub, Math.Max(10.5f, Font.SizeInPoints * 0.95f), FontStyle.Regular, true)) // labels >= 10.5 pt; Chinese in the UI font
                        TextRenderer.DrawText(g, sub, sf,
                            new Rectangle(x, 0, w - x, lineY - (int)Math.Round(2 * k)), Theme.ColMuted, f | TextFormatFlags.EndEllipsis);
                if (!compact) using (SolidBrush lb = new SolidBrush(Theme.ColBorder)) g.FillRectangle(lb, 0, lineY, w, Theme.BorderPx); // v5.1 flat: solid underline
            }
            catch { }
        }
    }

    /// <summary>Clickable "▸ 可选 / 高级" header of a collapsed section. Toggled fires after Open changes.</summary>
    internal sealed class FoldHeader : Control, ITextFitSelf
    {
        private bool open, hover;
        private readonly float k;
        public event Action Toggled;

        public FoldHeader(string text, float scale)
        {
            Text = text ?? "";
            k = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Tag = "theme:keep-fold";
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.OutlineButton;
        }

        public bool Open
        {
            get { return open; }
            set { if (open == value) return; open = value; Invalidate(); Action h = Toggled; if (h != null) h(); }
        }

        /// <summary>ITextFitSelf (v5.1 decision 9): arrow + gap + text + margins.</summary>
        public Size TextNeed(float fontScale)
        {
            double sc = TextFit.ScaleOf(this) * fontScale;
            using (Font f = new Font(Font.FontFamily, Font.SizeInPoints * fontScale, Font.Style))
            {
                Size t = TextFit.NeedSize(Text, f, 0, sc);
                return new Size(t.Width + (int)Math.Round(60 * k * fontScale), t.Height + (int)Math.Round(8 * k * fontScale));
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t;
            try { t = TextNeed(1f); } catch { t = Size.Empty; }
            return new Size(Math.Max((int)Math.Round(420 * k), t.Width), Math.Max(t.Height, Font.Height + (int)Math.Round(12 * k)));
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Open = !Open; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Open = !Open; e.Handled = true; }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                Color back = Parent != null && Parent.BackColor.A == 255 ? Parent.BackColor : Theme.ColSurface;
                using (SolidBrush b = new SolidBrush(hover ? Theme.ColHover : back)) g.FillRectangle(b, 0, 0, w, h);
                Color c = hover || Focused ? Theme.ColAccent2 : Theme.ColMuted;
                TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int x = (int)Math.Round(12 * k); // inside the frame, like a card title
                string arrow = open ? "▾" : "▸";
                using (Font mono = Theme.Mono(Font.SizeInPoints))
                {
                    TextRenderer.DrawText(g, arrow, mono, new Rectangle(x, 0, w, h), c, f);
                    x += TextRenderer.MeasureText(g, "▾", mono, new Size(w, h), f).Width + (int)Math.Round(8 * k);
                }
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, w - x, h), c, f);
                // a fold looks like a (collapsed) card: v5.1 flat -- 1 px frame, no corner ticks
                if (w > 4 && h > 4)
                    using (Pen p = new Pen(Theme.ColBorder, Theme.BorderPx)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);
            }
            catch { }
        }
    }

    /// <summary>The 「首次设置教程」 button: neon frame; while Highlight is on it pulses (stops when hidden).</summary>
    internal sealed class GlowButton : Control, IPausable, ITextFitSelf
    {
        private bool highlight, hover, down, paused;
        private readonly float k;
        private readonly Timer timer = new Timer();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>v4.1 drag freeze: the pulse stops on its current frame.</summary>
        public void Pause() { paused = true; SyncTimer(); }
        public void Resume() { if (!paused) return; paused = false; SyncTimer(); }

        public GlowButton(string text, float scale)
        {
            Text = text ?? "";
            k = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Tag = "theme:keep-glow";
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            timer.Interval = 40;
            timer.Tick += delegate { Invalidate(); };
        }

        public bool Highlight
        {
            get { return highlight; }
            set { highlight = value; SyncTimer(); Invalidate(); }
        }

        /// <summary>ITextFitSelf (v5.1 decision 9): text + 12 px each side.</summary>
        public Size TextNeed(float fontScale)
        {
            double sc = TextFit.ScaleOf(this) * fontScale;
            using (Font f = new Font(Font.FontFamily, Font.SizeInPoints * fontScale, Font.Style))
            {
                Size t = TextFit.NeedSize(Text, f, 0, sc);
                return new Size(t.Width + (int)Math.Round(24 * k * fontScale), t.Height + (int)Math.Round(8 * k * fontScale));
            }
        }

        /// <summary>v5.1 flat (decision 14): no pulse any more (the highlight is a filled accent), so the timer never runs.</summary>
        private void SyncTimer()
        {
            if (IsDisposed) return;
            bool run = false;
            if (run && !timer.Enabled) timer.Start();
            else if (!run && timer.Enabled) timer.Stop();
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); SyncTimer(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.Handled = true; OnClick(EventArgs.Empty); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                Color back = Parent != null && Parent.BackColor.A == 255 ? Parent.BackColor : Theme.ColBackground;
                using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, 0, 0, w, h);
                // v5.1 flat (decision 14): highlight = primary (filled accent, dark text); otherwise secondary (input fill,
                // 1 px border, cyan border on hover / focus). No glow frames, no pulse.
                Rectangle r = new Rectangle(0, 0, w - 1, h - 1);
                Color fill = highlight ? (down ? Theme.Blend(Theme.ColAccent, Theme.ColOnAccent, 0.25) : (hover ? Theme.Blend(Theme.ColAccent, Color.White, 0.18) : Theme.ColAccent))
                                       : (down ? Theme.ColPressed : (hover ? Theme.ColHover : Theme.ColInput));
                Color edge = highlight ? Theme.ColAccent : (hover || Focused ? Theme.ColAccent2 : Theme.ColBorder);
                using (SolidBrush b = new SolidBrush(fill)) g.FillRectangle(b, r);
                using (Pen p = new Pen(edge, Theme.BorderPx)) g.DrawRectangle(p, r);
                if (highlight && Focused) using (Pen fp = new Pen(Theme.ColOnAccent, Theme.FocusPx)) g.DrawRectangle(fp, 2, 2, w - 5, h - 5);
                TextFormatFlags f = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(g, Text, Font, r, highlight ? Theme.ColOnAccent : (hover ? Theme.ColText : Theme.ColMuted), f);
            }
            catch { }
        }
    }
}
#endif


#if !THEME_PURE_CHECK
namespace TarkovCrosshair
{
    /// <summary>
    /// A titled settings card (v4.0, replaces GroupBox, whose auto-size clips its last row on some systems):
    /// a 1-column auto-size grid = cyan title row + content, a hairline frame and terminal corner ticks.
    /// </summary>
    internal sealed class Section : TableLayoutPanel
    {
        private readonly float k;
        public readonly Label TitleLabel;

        public Section(string title, Control content, float scale)
        {
            k = scale;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Px(16), Px(12), Px(16), Px(12)); // v5.1 flat: spacing scale 4/8/12/16/24
            Margin = new Padding(0, 0, Px(12), Px(12));
            BackColor = Theme.ColCard;
            Tag = "theme:keep-section";
            TitleLabel = new Label();
            TitleLabel.Text = title ?? "";
            TitleLabel.AutoSize = true;
            TitleLabel.ForeColor = Theme.ColAccent2;
            TitleLabel.BackColor = Color.Transparent;
            TitleLabel.Tag = "theme:keep-sectitle";
            TitleLabel.Margin = new Padding(Px(3), 0, 0, Px(8)); // UIALIGN41 #10: the title starts where the row labels do (their margin is 3)
            TitleLabel.Visible = TitleLabel.Text.Length > 0;
            Controls.Add(TitleLabel, 0, 0);
            content.Margin = new Padding(0);
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(content, 0, 1);
        }

        private int Px(int v) { return (int)Math.Round(v * k); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try
            {
                Graphics g = e.Graphics;
                int w = ClientSize.Width, h = ClientSize.Height;
                if (w < 4 || h < 4) return;
                using (Pen p = new Pen(Theme.ColBorder, Theme.BorderPx)) g.DrawRectangle(p, 0, 0, w - 1, h - 1); // v5.1 flat: no corner ticks
            }
            catch { }
        }
    }
}
#endif

namespace TarkovCrosshair
{
    /// <summary>
    /// v4.1 window drag freeze (Leo, 17 Nov 2026): while the settings / tutorial window is moved or resized
    /// (ResizeBegin .. ResizeEnd = WM_ENTERSIZEMOVE .. WM_EXITSIZEMOVE) every control that runs timers, animations or live
    /// computation stops them and keeps showing its last frame; Resume() restarts what was running. The window calls
    /// Theme.PauseTree(window, true / false). Pause / Resume must be cheap, must not relayout, and a Resume without a Pause
    /// (or a second Pause) must be harmless. Pure interface: compiled everywhere (tests, renderers).
    /// </summary>
    internal interface IPausable
    {
        void Pause();
        void Resume();
    }
}

