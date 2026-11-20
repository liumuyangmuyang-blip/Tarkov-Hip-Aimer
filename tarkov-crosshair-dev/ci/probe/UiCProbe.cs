// W29 (v5.1) Windows CI copy of the V501-UI-C probe: runs on REAL Windows (real WinForms, real Microsoft YaHei UI metrics,
// real Windows display scale set per run by ci/tools/DpiTool.exe). Added for CI: modes "timing" (settings window and page
// open times) and "windows" (every other Form in the app, e.g. the v5.1 wizard / 必读 / 常见问题 windows), UIC_SHOTS_ALL=1
// (screenshots at every scale), UIC_EXPECT_DPI (fail when Windows did not switch to the requested scale).
// Original header:
// Reviewer C (V501-UI-C) structural UI probe. Test harness only (Mono + Xvfb), never shipped, never edits the app.
// Builds the real windows from a source SNAPSHOT (app compiled as TarkovHipAimer.dll with InternalsVisibleTo("RenderDriver"),
// so this assembly is named RenderDriver) and checks the CONTROL TREE, not pixels (Mono draws differently from Windows):
//   S1  every child of a multi-column TableLayoutPanel sits at a FIXED cell (flow placement skips hidden children on Windows
//       -> the v4.3 换手 scramble); no two visible children share a cell (Windows moves the second one to the next free cell)
//   S2  every visible child lies inside its own column/row band
//   S3  no two visible siblings overlap
//   S4  a row's column-0 label is on the same row as every control of that row and left of it
//   S5  nothing outside its parent; nothing past the right edge of the page scroller (no sideways scrolling)
//   S6  page semantics (换手 / 移动 / 侧身 / 跳跃 tables, from the G1 probe)
//   S7  text fits its box (labels, buttons, check boxes, number boxes, combos, owner-drawn nav / fold / glow / title)
//   S8  fonts are fixed: the same font (name, pt, style) on every control at every window size and 高级设置 state
//   T*  tutorial canvas (fields, labels, header, footer), F* filter tutorial (its own CheckLayout), D* dialogs, B* dashboard
// usage: mono RenderDriver.exe <result file> <mode: settings|dialogs|tutorial|filtertut> [shot dir]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed class UiC
{
    private readonly Assembly asm = Assembly.Load("TarkovHipAimer");
    private object g, app;
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private string ctx = "", tag = "", shotDir;
    private int pass, fail;
    private readonly Dictionary<string, int> failCount = new Dictionary<string, int>();
    private readonly Dictionary<string, string> failExample = new Dictionary<string, string>();
    private readonly Dictionary<string, int> infoCount = new Dictionary<string, int>();
    private readonly Dictionary<string, string> infoExample = new Dictionary<string, string>();
    private readonly List<string> fontRows = new List<string>();
    private readonly List<string> shots = new List<string>();
    private float k = 1f;

    [STAThread]
    private static int Main(string[] args)
    {
        UiC p = new UiC();
        string mode = args.Length > 1 ? args[1] : "settings";
        p.shotDir = args.Length > 2 ? args[2] : null;
        int rc = 0;
        try
        {
            p.k = Dpi() / 96f;
            p.tag = Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height + "@" + (int)Math.Round(p.k * 100) + "%";
            p.InitModel(Path.Combine(Path.GetTempPath(), "uic-" + mode + "-" + p.tag.Replace("%", "") + "-" + Process.GetCurrentProcess().Id + ".json"));
            p.Info("X metrics", "VerticalScrollBarWidth=" + SystemInformation.VerticalScrollBarWidth + " k=" + p.k + " workarea=" + Screen.PrimaryScreen.WorkingArea + " winmetrics=" + WinMetrics);
            p.SelfTest();
            string want = Environment.GetEnvironmentVariable("UIC_EXPECT_DPI");
            int wantDpi;
            if (!string.IsNullOrEmpty(want) && int.TryParse(want, out wantDpi))
                p.Ok(Dpi() == wantDpi, "X Windows did not switch to the requested display scale (results are at the real scale shown in the tag)",
                    () => "wanted " + wantDpi + " dpi, process has " + Dpi() + " dpi");
            switch (mode)
            {
                case "settings": p.RunSettings(); break;
                case "dialogs": p.RunDialogs(); break;
                case "tutorial": p.RunTutorial(); break;
                case "filtertut": p.RunFilterTutorial(); break;
                case "timing": p.RunTiming(); break;
                case "windows": p.RunWindows(); break;
                default: throw new ArgumentException("mode " + mode);
            }
        }
        catch (Exception ex) { p.Fail("X crash", "probe crashed: " + Unwrap(ex)); rc = 1; }
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, int> kv in p.failCount.OrderBy(x => x.Key))
            sb.Append("FAIL x").Append(kv.Value).Append(" | ").Append(kv.Key).Append(" || ").Append(p.failExample[kv.Key]).Append('\n');
        foreach (KeyValuePair<string, int> kv in p.infoCount.OrderBy(x => x.Key))
            sb.Append("INFO x").Append(kv.Value).Append(" | ").Append(kv.Key).Append(" || ").Append(p.infoExample[kv.Key]).Append('\n');
        foreach (string f in p.fontRows) sb.Append("FONT ").Append(f).Append('\n');
        foreach (string s in p.shots) sb.Append("SHOT ").Append(s).Append('\n');
        foreach (string s in p.times) sb.Append("TIME ").Append(p.tag).Append(' ').Append(s).Append('\n');
        sb.Append("RESULT ").Append(p.tag).Append(" mode=").Append(mode).Append(" pass=").Append(p.pass).Append(" fail=").Append(p.fail).Append('\n');
        if (args.Length > 0) File.WriteAllText(args[0], sb.ToString(), new UTF8Encoding(false)); else Console.Write(sb.ToString());
        return rc;
    }

    // ------------------------------------------------------------------ W29: timing + other windows

    private static readonly bool ShotsAll = Environment.GetEnvironmentVariable("UIC_SHOTS_ALL") == "1";
    private readonly List<string> times = new List<string>();

    private void Time(string key, Stopwatch sw)
    {
        times.Add(key + " " + sw.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
    }

    /// <summary>Settings window: build, first show, every page's first open and second open (open = ShowPage + a full
    /// synchronous repaint of the window, so the number includes drawing), hide/show again, a second window (warm JIT).</summary>
    private void RunTiming()
    {
        string[] ids = (string[])T("UiPages").GetField("Ids", All).GetValue(null);
        Stopwatch sw = Stopwatch.StartNew();
        Form f = NewSettings();
        Time("settings.build", sw);
        sw = Stopwatch.StartNew();
        f.Show();
        f.Refresh();
        Application.DoEvents();
        Time("settings.first-show", sw);
        MethodInfo show = f.GetType().GetMethod("ShowPage", All, null, new[] { typeof(int) }, null);
        for (int round = 0; round < 2; round++)
        {
            for (int idx = 0; idx < ids.Length; idx++)
            {
                sw = Stopwatch.StartNew();
                show.Invoke(f, new object[] { idx });
                f.Refresh();
                Application.DoEvents();
                Time("page." + (idx + 1).ToString("00") + "-" + ids[idx] + (round == 0 ? ".first" : ".again"), sw);
                pass++;
            }
            if (round == 0) Pump(3000); // idle time: lets any background page building finish before the second round
        }
        sw = Stopwatch.StartNew();
        f.Hide();
        Application.DoEvents();
        f.Show();
        f.Refresh();
        Application.DoEvents();
        Time("settings.hide-show", sw);
        f.Close();
        f.Dispose();
        Pump(100);
        sw = Stopwatch.StartNew();
        Form f2 = NewSettings();
        f2.Show();
        f2.Refresh();
        Application.DoEvents();
        Time("settings.second-window-build-show", sw);
        f2.Close();
        f2.Dispose();
    }

    private static readonly string[] Covered = { "SettingsForm", "TutorialForm", "FilterTutorialForm", "ThemedDialog", "NameDialog" };

    /// <summary>Every other window class in the app (v5.1 wizard, 必读, 常见问题, feedback, update ...): open it, step through its
    /// pages when it has a page method, run the generic control-tree checks, take a screenshot.</summary>
    private void RunWindows()
    {
        Type[] ts;
        try { ts = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types.Where(x => x != null).ToArray(); }
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Form owner = NewSettings();
        owner.Show();
        Pump(300);
        foreach (Type t in ts.Where(x => typeof(Form).IsAssignableFrom(x) && !x.IsAbstract && !x.ContainsGenericParameters).OrderBy(x => x.Name))
        {
            if (Covered.Contains(t.Name) || t.Name.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            ctx = "window=" + t.Name;
            Form w = null;
            try
            {
                ConstructorInfo c = t.GetConstructors(All).OrderByDescending(x => x.GetParameters().Length).FirstOrDefault();
                if (c == null) { Info("W window has no constructor the probe can call: " + t.Name, t.FullName); continue; }
                object[] a = c.GetParameters().Select(p => typeof(Form).IsAssignableFrom(p.ParameterType) && p.ParameterType.IsInstanceOfType(owner) ? owner : Arg(p.ParameterType)).ToArray();
                w = (Form)c.Invoke(a);
                w.StartPosition = FormStartPosition.Manual;
                w.Location = wa.Location;
                w.Show();
                Pump(400);
                Ok(w.Width <= wa.Width && w.Height <= wa.Height, "W window larger than the screen: " + t.Name, () => "window " + w.Size + " work area " + wa.Size);
                MethodInfo go = null;
                foreach (string n in new[] { "Go", "GoTo", "ShowStep", "ShowPage", "SetStep" })
                {
                    go = t.GetMethods(All).FirstOrDefault(m => m.Name == n && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));
                    if (go != null) break;
                }
                int count = go == null ? 1 : StepCount(w);
                if (go != null && count <= 0) { Info("W window has a page method but no step count, first page only: " + t.Name, go.Name); count = 1; go = null; }
                for (int i = 0; i < count; i++)
                {
                    if (go != null) { go.Invoke(w, new object[] { i }); Pump(150); }
                    ctx = "window=" + t.Name + " step=" + (i + 1);
                    fontOf.Clear();
                    CheckTree(w, "window/" + t.Name, null, false);
                    if (shotDir != null) Shot(w, "window-" + t.Name + "-" + (i + 1).ToString("00"));
                }
            }
            catch (Exception ex) { Fail("W window could not be opened by the probe: " + t.Name, Unwrap(ex).GetType().Name + ": " + Unwrap(ex).Message); }
            finally { if (w != null) { try { w.Close(); w.Dispose(); } catch (Exception) { } } Pump(60); }
        }
        owner.Close();
        owner.Dispose();
    }

    private int StepCount(object w)
    {
        foreach (string n in new[] { "StepCount", "PageCount", "Steps", "Pages" })
        {
            for (Type t = w.GetType(); t != null && t != typeof(Form); t = t.BaseType)
            {
                object v = null;
                FieldInfo fi = t.GetField(n, All | BindingFlags.DeclaredOnly);
                if (fi != null) v = fi.GetValue(fi.IsStatic ? null : w);
                PropertyInfo pi = v == null ? t.GetProperty(n, All | BindingFlags.DeclaredOnly) : null;
                if (pi != null && pi.GetIndexParameters().Length == 0) v = pi.GetValue(w, null);
                if (v is int) return (int)v;
                System.Collections.ICollection col = v as System.Collections.ICollection;
                if (col != null) return col.Count;
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ plumbing

    private static Exception Unwrap(Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        return e;
    }

    private Type T(string n)
    {
        Type[] ts;
        try { ts = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types.Where(x => x != null).ToArray(); }
        return ts.FirstOrDefault(t => t.Name == n);
    }

    private void Ok(bool ok, string sig, Func<string> detail)
    {
        if (ok) { pass++; return; }
        Fail(sig, detail());
    }

    private void Fail(string sig, string detail)
    {
        fail++;
        int n;
        failCount.TryGetValue(sig, out n);
        failCount[sig] = n + 1;
        if (n == 0) failExample[sig] = tag + " " + ctx + " :: " + detail;
    }

    private void Info(string sig, string detail)
    {
        int n;
        infoCount.TryGetValue(sig, out n);
        infoCount[sig] = n + 1;
        if (n == 0) infoExample[sig] = tag + " " + ctx + " :: " + detail;
    }

    private static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(6); } while (sw.ElapsedMilliseconds < ms);
    }

    private static int Dpi()
    {
        try { using (Graphics gr = Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(gr.DpiX); } catch (Exception) { return 96; }
    }

    private void InitModel(string path)
    {
        Type gs = T("GlobalSettings"), io = T("SettingsIO");
        if (File.Exists(path)) File.Delete(path);
        MethodInfo load = io.GetMethods(All).First(m => m.Name == "Load" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
        object[] a = { path, null };
        g = load.Invoke(null, a);
        Type appT = T("App");
        app = FormatterServices.GetUninitializedObject(appT);
        foreach (FieldInfo f in appT.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Type ft = f.FieldType;
            object v = null;
            try
            {
                if (ft == gs) v = g;
                else if (ft == typeof(string)) v = path;
                else if (ft == typeof(EventWaitHandle)) v = new EventWaitHandle(false, EventResetMode.AutoReset);
                else if (typeof(Form).IsAssignableFrom(ft) || ft.IsValueType || ft.IsAbstract || ft.IsInterface || typeof(Delegate).IsAssignableFrom(ft)) v = null;
                else if (ft.GetConstructor(Type.EmptyTypes) != null) v = Activator.CreateInstance(ft);
            }
            catch (Exception) { v = null; }
            if (v != null) f.SetValue(app, v);
        }
    }

    private object Arg(Type t)
    {
        if (t.IsInstanceOfType(g)) return g;
        if (app != null && t.IsInstanceOfType(app)) return app;
        if (t == typeof(string)) return "";
        if (t == typeof(Action)) return (Action)delegate { };
        if (t == typeof(Action<string>)) return (Action<string>)delegate { };
        if (t == typeof(Action<int>)) return (Action<int>)delegate { };
        if (t.IsValueType) return Activator.CreateInstance(t);
        if (t.GetConstructor(Type.EmptyTypes) != null) return Activator.CreateInstance(t);
        return null;
    }

    private Form NewSettings()
    {
        Type st = T("SettingsForm");
        ConstructorInfo c = st.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderByDescending(x => x.GetParameters().Length).First();
        Form f = (Form)c.Invoke(c.GetParameters().Select(p => Arg(p.ParameterType)).ToArray());
        FieldInfo ff = app.GetType().GetField("form", All);
        if (ff != null && ff.FieldType.IsInstanceOfType(f)) ff.SetValue(app, f);
        f.StartPosition = FormStartPosition.Manual;
        return f;
    }

    private object Profile() { return g.GetType().GetProperty("Active", All).GetValue(g, null); }

    private void SetModel(bool adv, bool custom)
    {
        T("UiPrefs").GetMethod("SetAdvanced", All).Invoke(null, new object[] { g, adv });
        object p = Profile();
        MethodInfo auto = p.GetType().GetMethod("AllHandAuto", All);
        if (auto != null) auto.Invoke(p, null);
        if (custom)
        {
            MethodInfo set = p.GetType().GetMethod("SetHandCustom", All);
            int[] rows = (int[])T("UiPages").GetMethod("HandRows", All).Invoke(null, null);
            for (int i = 0; i < rows.Length; i += 2) set.Invoke(p, new object[] { rows[i], 37.5 + i, -21.25 });
        }
    }

    private static IEnumerable<Control> Walk(Control c)
    {
        foreach (Control k in c.Controls)
        {
            yield return k;
            foreach (Control d in Walk(k)) yield return d;
        }
    }

    private static bool Shown(Control c)
    {
        for (Control x = c; x != null; x = x.Parent) if (!x.Visible) return false;
        return true;
    }

    private static string D(Control k)
    {
        string t = k.Text ?? "";
        if (k is TableLayoutPanel && !(k.GetType().Name == "Section") && t.Length == 0) return k.GetType().Name + ((TableLayoutPanel)k).ColumnCount + "c";
        if (k.GetType().Name == "Section")
        {
            FieldInfo fi = k.GetType().GetField("TitleLabel", All);
            Label tl = fi != null ? fi.GetValue(k) as Label : null;
            return "Section'" + (tl != null ? tl.Text : "") + "'";
        }
        if (t.Length > 18) t = t.Substring(0, 18) + "..";
        return k.GetType().Name + (t.Length > 0 ? "'" + t.Replace("\n", " ").Replace("\r", "") + "'" : "");
    }

    /// <summary>Stable path of a control below root: ancestor descriptors (Sections and labelled controls keep their text).</summary>
    private static string PathOf(Control c, Control root)
    {
        List<string> parts = new List<string>();
        for (Control x = c; x != null && x != root; x = x.Parent) parts.Add(D(x));
        parts.Reverse();
        if (parts.Count > 6) parts = parts.Skip(parts.Count - 6).ToList();
        return string.Join(">", parts);
    }

    private static string R(Rectangle r) { return "[" + r.X + "," + r.Y + " " + r.Width + "x" + r.Height + "]"; }
    private static string RF(RectangleF r) { return string.Format(CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#} {2:0.#}x{3:0.#}]", r.X, r.Y, r.Width, r.Height); }

    private static Rectangle In(Control c, Control root)
    {
        Point p = root.PointToClient(c.PointToScreen(Point.Empty));
        return new Rectangle(p, c.Size);
    }

    private void Shot(Control c, string name)
    {
        if (shotDir == null) return;
        try
        {
            Directory.CreateDirectory(shotDir);
            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
            r.Intersect(Screen.PrimaryScreen.Bounds);
            if (r.Width <= 0 || r.Height <= 0) return;
            using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics gr = Graphics.FromImage(bmp)) gr.CopyFromScreen(r.Location, Point.Empty, r.Size);
                string file = Path.Combine(shotDir, tag.Replace("%", "pct") + "-" + name + ".png");
                bmp.Save(file, ImageFormat.Png);
                shots.Add(file);
            }
        }
        catch (Exception ex) { Info("X shot failed", ex.Message); }
    }

    // ------------------------------------------------------------------ generic control-tree checks

    /// <summary>Per TableLayoutPanel: the children that were hidden in at least one checked state (visibility toggles).</summary>
    private readonly Dictionary<TableLayoutPanel, HashSet<Control>> hiddenSeen = new Dictionary<TableLayoutPanel, HashSet<Control>>();
    private readonly Dictionary<TableLayoutPanel, string> flowGrids = new Dictionary<TableLayoutPanel, string>();
    private readonly Dictionary<Control, string> fontOf = new Dictionary<Control, string>();

    private static string FontKey(Font f)
    {
        if (f == null) return "null";
        return f.Name + "|" + f.SizeInPoints.ToString("0.##", CultureInfo.InvariantCulture) + "pt|" + f.Style;
    }

    private static bool HasCjk(string s)
    {
        foreach (char ch in s ?? "") if (ch >= 0x2E80 && ch <= 0x9FFF || ch >= 0xFF00 && ch <= 0xFFEF || ch >= 0x3000 && ch <= 0x303F) return true;
        return false;
    }

    private static bool IsMonoFont(Font f)
    {
        string n = f == null ? "" : f.Name;
        return n == "Consolas" || n == "Cascadia Mono" || n == "Lucida Console" || n == "Courier New" || n.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Every check on the visible tree below root. scroller = the page's scrolling body (S5 right edge), may be null.</summary>
    private void CheckTree(Control root, string area, Control scroller, bool recordFonts)
    {
        List<Control> vis = Walk(root).Where(c => Shown(c)).ToList();
        if (Shown(root)) vis.Insert(0, root);
        foreach (Control c in vis)
        {
            CheckContainer(c, root, area);
            CheckText(c, root, area);
            CheckFont(c, root, area, recordFonts);
        }
        if (scroller != null)
        {
            foreach (Control c in vis)
            {
                if (c == scroller || c.Width <= 0 || c.Height <= 0) continue;
                bool inside = false;
                for (Control p = c.Parent; p != null; p = p.Parent) if (p == scroller) { inside = true; break; }
                if (!inside) continue;
                Rectangle r = In(c, scroller);
                Ok(r.Right <= scroller.ClientSize.Width + 1, area + " S5 past the page's right edge (sideways scroll): " + PathOf(c, root),
                    () => "right " + r.Right + " > page client " + scroller.ClientSize.Width);
            }
            ScrollableControl sc = scroller as ScrollableControl;
            if (sc != null) Ok(!sc.HorizontalScroll.Visible, area + " S5 horizontal scroll bar on the page", () => "display " + sc.DisplayRectangle.Size + " client " + sc.ClientSize);
        }
    }

    private void CheckContainer(Control c, Control root, string area)
    {
        if (c.Controls.Count == 0) return;
        List<Control> all = c.Controls.Cast<Control>().ToList();
        List<Control> kids = all.Where(x => x.Visible && x.Width > 0 && x.Height > 0).ToList();
        bool scrolling = c is ScrollableControl && ((ScrollableControl)c).AutoScroll;
        string cp = PathOf(c, root);
        if (!scrolling && !(c is Form))
            foreach (Control x in kids)
            {
                if (x.Dock == DockStyle.Fill) continue;
                Rectangle b = x.Bounds;
                Size cs = c.ClientSize;
                Ok(b.Left >= -1 && b.Top >= -1 && b.Right <= cs.Width + 1 && b.Bottom <= cs.Height + 1,
                    area + " S5 outside parent: " + PathOf(x, root), () => R(b) + " in " + D(c) + " client " + cs.Width + "x" + cs.Height);
            }
        // S3 overlap of visible siblings (docked-fill siblings fill what the others leave)
        for (int i = 0; i < kids.Count; i++)
            for (int j = i + 1; j < kids.Count; j++)
            {
                Control a = kids[i], b = kids[j];
                if (a.Dock == DockStyle.Fill || b.Dock == DockStyle.Fill) continue;
                Rectangle x = Rectangle.Intersect(a.Bounds, b.Bounds);
                Ok(!(x.Width > 1 && x.Height > 1), area + " S3 overlap: " + PathOf(a, root) + " / " + D(b), () => R(a.Bounds) + " x " + R(b.Bounds));
            }
        TableLayoutPanel t = c as TableLayoutPanel;
        if (t != null) CheckGrid(t, all, kids, root, area);
    }

    private void CheckGrid(TableLayoutPanel t, List<Control> all, List<Control> kids, Control root, string area)
    {
        string tp = PathOf(t, root);
        HashSet<Control> hid;
        if (!hiddenSeen.TryGetValue(t, out hid)) { hid = new HashSet<Control>(); hiddenSeen[t] = hid; }
        foreach (Control x in all) if (!x.Visible) hid.Add(x);
        if (t.ColumnCount >= 2)
        {
            foreach (Control x in all)
            {
                TableLayoutPanelCellPosition p = t.GetCellPosition(x);
                bool flow = p.Column < 0 || p.Row < 0;
                if (flow)
                {
                    string sig = area + " S1 flow-placed child in a " + t.ColumnCount + "-column grid: " + tp + ">" + D(x);
                    Fail(sig, "cell " + p.Column + "," + p.Row + "; hidden children in this grid now: " + all.Count(y => !y.Visible) + " of " + all.Count);
                    flowGrids[t] = tp;
                }
                else pass++;
            }
            // two visible children in one cell (Windows bumps the later one to the next free cell)
            Dictionary<long, Control> taken = new Dictionary<long, Control>();
            foreach (Control x in kids)
            {
                TableLayoutPanelCellPosition p = t.GetCellPosition(x);
                if (p.Column < 0 || p.Row < 0) continue;
                int cs = Math.Max(1, t.GetColumnSpan(x)), rs = Math.Max(1, t.GetRowSpan(x));
                for (int cc = p.Column; cc < p.Column + cs; cc++)
                    for (int rr = p.Row; rr < p.Row + rs; rr++)
                    {
                        long key = ((long)rr << 20) | (long)cc;
                        Control other;
                        if (taken.TryGetValue(key, out other))
                            Fail(area + " S1 two visible controls in one cell: " + tp + ">" + D(other) + " / " + D(x), "cell " + cc + "," + rr);
                        else { taken[key] = x; pass++; }
                    }
            }
        }
        int[] cw, rh;
        try { cw = t.GetColumnWidths(); rh = t.GetRowHeights(); } catch (Exception) { return; }
        Func<int, int> x0 = col => t.Padding.Left + cw.Take(col).Sum();
        Func<int, int> y0 = row => t.Padding.Top + rh.Take(row).Sum();
        Dictionary<int, Control> rowLabel = new Dictionary<int, Control>();
        foreach (Control x in kids)
        {
            TableLayoutPanelCellPosition at = t.GetPositionFromControl(x);
            if (at.Column < 0 || at.Row < 0 || at.Column >= cw.Length || at.Row >= rh.Length) { Fail(area + " S2 no cell: " + tp + ">" + D(x), "pos " + at.Column + "," + at.Row); continue; }
            int cs = Math.Max(1, t.GetColumnSpan(x)), rs = Math.Max(1, t.GetRowSpan(x));
            int bx0 = x0(at.Column), bx1 = x0(Math.Min(cw.Length, at.Column + cs)), by0 = y0(at.Row), by1 = y0(Math.Min(rh.Length, at.Row + rs));
            Rectangle b = x.Bounds;
            Ok(b.Left >= bx0 - 1 && b.Right <= bx1 + 1 && b.Top >= by0 - 1 && b.Bottom <= by1 + 1,
                area + " S2 out of its cell: " + tp + ">" + D(x), () => "cell (" + at.Column + "," + at.Row + ") band x " + bx0 + ".." + bx1 + " y " + by0 + ".." + by1 + " control " + R(b));
            if (t.ColumnCount >= 2 && at.Column == 0 && cs == 1 && x is Label && !string.IsNullOrEmpty(x.Text)) rowLabel[at.Row] = x;
        }
        foreach (KeyValuePair<int, Control> kv in rowLabel)
        {
            Control l = kv.Value;
            foreach (Control x in kids)
            {
                if (x == l) continue;
                TableLayoutPanelCellPosition at = t.GetPositionFromControl(x);
                if (at.Row != kv.Key || at.Column == 0) continue;
                Ok(l.Right <= x.Left + 1, area + " S4 label not left of its control: " + tp + ">" + D(l) + " / " + D(x), () => "label right " + l.Right + " control left " + x.Left);
                Ok(l.Top < x.Bottom && x.Top < l.Bottom, area + " S4 label not on its control's row: " + tp + ">" + D(l) + " / " + D(x),
                    () => "label y " + l.Top + ".." + l.Bottom + " control y " + x.Top + ".." + x.Bottom);
            }
        }
    }

    private void CheckFont(Control c, Control root, string area, bool record)
    {
        string fk = FontKey(c.Font);
        string old;
        if (fontOf.TryGetValue(c, out old))
            Ok(old == fk, area + " S8 font changed with window size / state: " + PathOf(c, root), () => old + " -> " + fk);
        else fontOf[c] = fk;
        if (record && !string.IsNullOrEmpty(c.Text) && (c is Label || c is ButtonBase || c.GetType().Name == "NavItem" || c.GetType().Name == "FoldHeader" || c.GetType().Name == "PageTitle"))
            fontRows.Add(area + "\t" + PathOf(c, root) + "\t" + fk);
        if (!string.IsNullOrEmpty(c.Text) && HasCjk(c.Text) && IsMonoFont(c.Font) && (c is Label || c is ButtonBase))
            Info(area + " CJK text in a monospace font (Windows: Consolas has no CJK glyphs, GDI falls back to another CJK font): " + PathOf(c, root), c.Font.Name + " '" + c.Text + "'");
    }

    private static readonly bool WinMetrics = Environment.GetEnvironmentVariable("UIC_WINMETRICS") == "1";
    private int SpinW() { return WinMetrics ? (int)Math.Round(17 * k) : SystemInformation.VerticalScrollBarWidth; }

    /// <summary>The flow detector must see a flow-placed child (self-test, so a silent 0 is not taken for a pass).</summary>
    private void SelfTest()
    {
        TableLayoutPanel t = new TableLayoutPanel();
        t.ColumnCount = 3;
        Label a = new Label(), b = new Label();
        t.Controls.Add(a);
        t.Controls.Add(b, 2, 0);
        TableLayoutPanelCellPosition pa = t.GetCellPosition(a), pb = t.GetCellPosition(b);
        Ok(pa.Column < 0 && pa.Row < 0 && pb.Column == 2 && pb.Row == 0, "X self-test: flow detector", () => "flow " + pa.Column + "," + pa.Row + " fixed " + pb.Column + "," + pb.Row);
        t.Dispose();
    }

    private static string NumText(decimal v, int dec)
    {
        return v.ToString("F" + Math.Max(0, dec), CultureInfo.CurrentCulture);
    }

    private void CheckText(Control c, Control root, string area)
    {
        string text = c.Text ?? "";
        if (c.Width <= 0 || c.Height <= 0) return;
        string path = PathOf(c, root);
        string tn = c.GetType().Name;
        Label l = c as Label;
        if (l != null && !(c is LinkLabel && false))
        {
            if (text.Length == 0) return;
            // the label's own measure (the same function sizes and draws it): a label smaller than that is cut
            Size one = l.GetPreferredSize(Size.Empty);
            if (l.AutoEllipsis)
            {
                if (one.Width > l.Width + 1) Info(area + " S7i ellipsis label shows only part of its text: " + path, "needs " + one.Width + " has " + l.Width + " '" + text + "'");
                return;
            }
            Size proposed = l.AutoSize ? (l.MaximumSize.Width > 0 ? new Size(l.MaximumSize.Width, 0) : Size.Empty) : new Size(l.Width, 0);
            Size need = l.GetPreferredSize(proposed);
            if (l.AutoSize && l.MaximumSize.Width > 0 && need.Width > l.MaximumSize.Width) need = new Size(need.Width, need.Height); // a word longer than the wrap width
            Ok(need.Width <= l.Width + 1, area + " S7 label text wider than its box: " + path, () => "needs " + need.Width + "x" + need.Height + " has " + l.Width + "x" + l.Height + " (font " + FontKey(l.Font) + ", max " + l.MaximumSize + ")");
            Ok(need.Height <= l.Height + 1, area + " S7 label text taller than its box (lines cut): " + path, () => "needs " + need.Width + "x" + need.Height + " has " + l.Width + "x" + l.Height);
            if (!l.AutoSize && need.Width <= l.Width + 1 && l.Width - one.Width < 4 && one.Width <= l.Width)
                Info(area + " S7t fixed-size label with < 4 px to spare (Windows YaHei metrics may wrap/cut it): " + path, "needs " + one.Width + " has " + l.Width);
            return;
        }
        ButtonBase bb = c as ButtonBase;
        if (bb != null)
        {
            if (text.Length == 0) return;
            Size pref = bb.GetPreferredSize(Size.Empty);
            Ok(pref.Width <= bb.Width + 1, area + " S7 button / check box text wider than the control: " + path, () => "preferred " + pref.Width + "x" + pref.Height + " has " + bb.Width + "x" + bb.Height);
            Size th = TextRenderer.MeasureText(text, bb.Font);
            int inner = bb.ClientSize.Height - bb.Padding.Vertical - (bb is CheckBox || bb is RadioButton ? 0 : 4);
            Ok(th.Height <= inner + 1, area + " S7 button text taller than the button (cut top / bottom): " + path, () => "text " + th.Height + " inner " + inner + " button " + bb.Height);
            return;
        }
        NumericUpDown n = c as NumericUpDown;
        if (n != null)
        {
            string wide = NumText(n.Maximum, n.DecimalPlaces), lo = NumText(n.Minimum, n.DecimalPlaces);
            int need = Math.Max(TextRenderer.MeasureText(wide, n.Font, Size.Empty, TextFormatFlags.NoPadding).Width,
                TextRenderer.MeasureText(lo, n.Font, Size.Empty, TextFormatFlags.NoPadding).Width);
            int edit = n.Width - SpinW() - 4 - 4; // spin buttons, 3D border, the edit's own margins
            Ok(need <= edit, area + " S7 number box narrower than its widest value: " + path, () => "'" + wide + "' needs " + need + " edit area " + edit + " (box " + n.Width + ", spin " + SpinW() + ")");
            return;
        }
        ComboBox cb = c as ComboBox;
        if (cb != null)
        {
            int need = 0; string w = "";
            foreach (object it in cb.Items)
            {
                string s = cb.GetItemText(it);
                int iw = TextRenderer.MeasureText(s, cb.Font).Width;
                if (iw > need) { need = iw; w = s; }
            }
            int room = cb.Width - SpinW() - 6;
            // Windows: the drop-down arrow is SM_CXVSCROLL at the system DPI (17 px at 100 %). Widths built from
            // SystemInformation.VerticalScrollBarWidth (Choice) grow with it; fixed / stretched widths do not.
            int winSpin = (int)Math.Round(17 * k);
            bool derived = Math.Abs(cb.Width - Math.Max((int)Math.Round(70 * k), need + SpinW() + (int)Math.Round(16 * k))) <= 2;
            int winRoom = derived ? room : cb.Width - winSpin - 6;
            Ok(need <= room, area + " S7 combo narrower than its longest item: " + path, () => "'" + w + "' needs " + need + " room " + room + " (combo " + cb.Width + ")");
            if (need <= room) Ok(need <= winRoom, area + " S7w combo narrower than its longest item with the Windows arrow width: " + path,
                () => "'" + w + "' needs " + need + " room on Windows " + winRoom + " (combo " + cb.Width + ", arrow " + winSpin + ")");
            return;
        }
        if (text.Length == 0) return;
        if (tn == "NavItem")
        {
            Font f = c.Font;
            using (Font mono = (Font)T("Theme").GetMethod("Mono", All, null, new[] { typeof(float) }, null).Invoke(null, new object[] { Math.Max(11f, f.SizeInPoints) }))
            {
                TextFormatFlags fl = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int x = (int)Math.Round(16 * k) + TextRenderer.MeasureText("00", mono, new Size(c.Width, c.Height), fl).Width + (int)Math.Round(12 * k);
                string title = (string)c.GetType().GetField("Title", All).GetValue(c);
                int need = x + TextRenderer.MeasureText(title, f, new Size(int.MaxValue, c.Height), fl).Width + 4;
                Ok(need <= c.Width, area + " S7 navigation text cut (…): " + path, () => "needs " + need + " has " + c.Width);
            }
            return;
        }
        if (tn == "FoldHeader")
        {
            using (Font mono = (Font)T("Theme").GetMethod("Mono", All, null, new[] { typeof(float) }, null).Invoke(null, new object[] { c.Font.SizeInPoints }))
            {
                TextFormatFlags fl = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int x = (int)Math.Round(12 * k) + TextRenderer.MeasureText("▾", mono, new Size(c.Width, c.Height), fl).Width + (int)Math.Round(8 * k);
                int need = x + TextRenderer.MeasureText(text, c.Font, new Size(int.MaxValue, c.Height), fl).Width;
                Ok(need <= c.Width, area + " S7 fold header text cut: " + path, () => "needs " + need + " has " + c.Width);
            }
            return;
        }
        if (tn == "GlowButton")
        {
            int need = TextRenderer.MeasureText(text, c.Font).Width;
            Ok(need <= c.Width - 2, area + " S7 tutorial button text cut (…): " + path, () => "needs " + need + " has " + c.Width);
            return;
        }
        if (tn == "PageTitle")
        {
            bool compact = (bool)c.GetType().GetProperty("Compact", All).GetValue(c, null);
            float pt = compact ? Math.Max(13f, c.Font.SizeInPoints * 1.2f) : Math.Max(16f, c.Font.SizeInPoints * 1.5f);
            using (Font tf = (Font)T("Theme").GetMethod("Ui", All, null, new[] { typeof(float), typeof(FontStyle) }, null).Invoke(null, new object[] { pt, FontStyle.Bold }))
            {
                TextFormatFlags fl = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int bar = Math.Max(2, (int)Math.Round(3 * k));
                int x = bar + (int)Math.Round(10 * k);
                Size ts = TextRenderer.MeasureText(text, tf, new Size(c.Width, c.Height), fl);
                Ok(x + ts.Width + 4 <= c.Width, area + " S7 page title cut: " + path, () => "needs " + (x + ts.Width + 4) + " has " + c.Width);
                Ok(ts.Height <= c.Height, area + " S7 page title taller than its strip: " + path, () => "text " + ts.Height + " strip " + c.Height);
            }
        }
    }

    // ------------------------------------------------------------------ settings window

    private void RunSettings()
    {
        string[] ids = (string[])T("UiPages").GetField("Ids", All).GetValue(null);
        bool[][] states = { new[] { false, false }, new[] { true, true }, new[] { false, true }, new[] { true, false } };
        bool leo = tag.StartsWith("2160x1440") || tag.StartsWith("1920x1080@100") || tag.StartsWith("1280x720@100");
        foreach (bool[] st in states)
        {
            bool adv = st[0], custom = st[1];
            SetModel(adv, custom);
            Form f = NewSettings();
            fontOf.Clear();
            f.Show();
            Pump(700);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size def = f.Size;
            Size min = f.MinimumSize.Width > 0 ? f.MinimumSize : new Size(Math.Min(wa.Width, 800), Math.Min(wa.Height, 600));
            Ok(min.Width <= wa.Width && min.Height <= wa.Height, "W minimum window larger than the screen", () => "min " + min + " work area " + wa.Size);
            Ok(def.Width <= wa.Width && def.Height <= wa.Height, "W default window larger than the screen", () => "default " + def + " work area " + wa.Size);
            var sizes = new List<KeyValuePair<string, Rectangle>>();
            sizes.Add(new KeyValuePair<string, Rectangle>("min", new Rectangle(wa.X, wa.Y, min.Width, min.Height)));
            sizes.Add(new KeyValuePair<string, Rectangle>("mid", new Rectangle(wa.X, wa.Y, (min.Width + wa.Width) / 2, Math.Max(min.Height, Math.Min(wa.Height, (min.Height + wa.Height) / 2)))));
            sizes.Add(new KeyValuePair<string, Rectangle>("default", new Rectangle(wa.X, wa.Y, def.Width, def.Height)));
            sizes.Add(new KeyValuePair<string, Rectangle>("max", wa));
            bool firstSize = true;
            foreach (KeyValuePair<string, Rectangle> sz in sizes)
            {
                f.Bounds = sz.Value;
                Pump(250);
                // chrome (header, top bar, navigation) once per size
                ctx = "adv=" + (adv ? 1 : 0) + " custom=" + (custom ? 1 : 0) + " win=" + sz.Key + " " + f.ClientSize.Width + "x" + f.ClientSize.Height + " area=chrome";
                Control topBar = (Control)f.GetType().GetField("topBar", All).GetValue(f);
                Control navHost = (Control)f.GetType().GetField("navHost", All).GetValue(f);
                Control header = (Control)f.GetType().GetField("header", All).GetValue(f);
                CheckTree(topBar, "topbar", null, firstSize && !adv && !custom);
                CheckTree(navHost, "nav", null, firstSize && !adv && !custom);
                if (header.Visible) CheckHeader(header);
                for (int idx = 0; idx < ids.Length; idx++)
                {
                    string id = ids[idx];
                    f.GetType().GetMethod("ShowPage", All, null, new[] { typeof(int) }, null).Invoke(f, new object[] { idx });
                    Pump(id == "dashboard" ? 500 : 260);
                    OpenFolds(f);
                    ctx = "adv=" + (adv ? 1 : 0) + " custom=" + (custom ? 1 : 0) + " win=" + sz.Key + " " + f.ClientSize.Width + "x" + f.ClientSize.Height + " page=" + id;
                    Control host = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
                    System.Collections.IList pages = (System.Collections.IList)f.GetType().GetField("pages", All).GetValue(f);
                    object pg = pages[idx];
                    Control pHost = (Control)pg.GetType().GetField("Host", All).GetValue(pg);
                    Control scroller = (Control)pg.GetType().GetField("Scroller", All).GetValue(pg);
                    CheckTree(pHost, "page/" + id, scroller, firstSize && !adv && !custom);
                    if (id == "dashboard") CheckDashboard(pHost);
                    CheckSemantics(Walk(pHost).Where(Shown).ToList(), id);
                    if (shotDir != null && (ShotsAll ? sz.Key == "default" && adv == custom : leo && ((sz.Key == "min" || sz.Key == "max") && (adv == custom))))
                        Shot(f, "settings-" + (adv ? "adv" : "basic") + "-" + sz.Key + "-" + (idx + 1).ToString("00") + "-" + id);
                }
                firstSize = false;
            }
            f.Close();
            f.Dispose();
            Pump(80);
        }
        foreach (KeyValuePair<TableLayoutPanel, string> kv in flowGrids)
        {
            HashSet<Control> hid;
            if (hiddenSeen.TryGetValue(kv.Key, out hid) && hid.Count > 0)
                Info("S1r RISK flow grid whose children toggle visibility (Windows scrambles it): " + kv.Value, string.Join(", ", hid.Select(D)));
        }
    }

    private void OpenFolds(Form f)
    {
        Control host = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
        bool any = false;
        foreach (Control h in Walk(host).Where(c => c.GetType().Name == "FoldHeader" && Shown(c)).ToList())
        {
            PropertyInfo op = h.GetType().GetProperty("Open", All);
            if (op != null && !(bool)op.GetValue(h, null)) { op.SetValue(h, true, null); any = true; }
        }
        if (any) Pump(250);
    }

    private void CheckHeader(Control header)
    {
        // HeaderPanel: title (mono 13 bold, upper case) + "// subtitle" (UI 11) + "[ version ]" (mono 11): what does not fit is cut with …
        try
        {
            Type th = T("Theme");
            FieldInfo ft = header.GetType().GetField("title", All), fs = header.GetType().GetField("subtitle", All), fv = header.GetType().GetField("version", All);
            FieldInfo ftf = header.GetType().GetField("tf", All), fsf = header.GetType().GetField("sf", All), fvf = header.GetType().GetField("vf", All);
            if (ft == null || ftf == null) return;
            string title = ((string)ft.GetValue(header)).ToUpperInvariant(), sub = (string)fs.GetValue(header), ver = (string)fv.GetValue(header);
            Font tf = (Font)ftf.GetValue(header), sf = (Font)fsf.GetValue(header), vf = (Font)fvf.GetValue(header);
            TextFormatFlags fl = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int pad = (int)Math.Round(14 * k), icon = (int)Math.Round(22 * k);
            int w = header.ClientSize.Width;
            int need = pad + icon + (int)Math.Round(12 * k) + TextRenderer.MeasureText(title, tf, Size.Empty, fl).Width + (int)Math.Round(10 * k)
                + TextRenderer.MeasureText("// " + sub, sf, Size.Empty, fl).Width + (int)Math.Round(12 * k) + TextRenderer.MeasureText("[ " + ver + " ]", vf, Size.Empty, fl).Width + pad;
            if (need > w) Info("chrome S7i header subtitle cut with …", "needs " + need + " has " + w);
            else pass++;
        }
        catch (Exception ex) { Info("X header check failed", ex.Message); }
    }

    private void CheckDashboard(Control pHost)
    {
        Control dash = Walk(pHost).FirstOrDefault(c => c.GetType().Name == "DashboardPanel");
        if (dash == null) { Fail("B dashboard panel missing", "no DashboardPanel"); return; }
        object lay = null;
        foreach (FieldInfo fi in dash.GetType().GetFields(All)) if (fi.FieldType.Name == "DashLayout") { lay = fi.GetValue(dash); if (lay != null) break; }
        if (lay == null) { Info("B dashboard layout object not found", ""); return; }
        Func<string, Rectangle?> Rect = delegate(string name)
        {
            FieldInfo fi = lay.GetType().GetField(name, All);
            if (fi == null) return null;
            object v = fi.GetValue(lay);
            if (v == null) return null;
            PropertyInfo px = v.GetType().GetProperty("X") ?? null;
            FieldInfo fx = v.GetType().GetField("X"), fy = v.GetType().GetField("Y"), fw = v.GetType().GetField("W") ?? v.GetType().GetField("Width"), fh = v.GetType().GetField("H") ?? v.GetType().GetField("Height");
            if (fx == null || fy == null || fw == null || fh == null)
            {
                PropertyInfo pr = v.GetType().GetProperty("Rect") ?? v.GetType().GetProperty("R");
                if (pr != null) return (Rectangle)pr.GetValue(v, null);
                return null;
            }
            return new Rectangle(Convert.ToInt32(fx.GetValue(v)), Convert.ToInt32(fy.GetValue(v)), Convert.ToInt32(fw.GetValue(v)), Convert.ToInt32(fh.GetValue(v)));
        };
        string[] cards = { "Header", "Chart", "Data", "Term", "Caps" };
        bool capsShown = (bool)lay.GetType().GetField("CapsShown", All).GetValue(lay);
        List<KeyValuePair<string, Rectangle>> rs = new List<KeyValuePair<string, Rectangle>>();
        foreach (string cn in cards)
        {
            if (cn == "Caps" && !capsShown) continue;
            Rectangle? r = Rect(cn);
            if (r == null) continue;
            rs.Add(new KeyValuePair<string, Rectangle>(cn, r.Value));
            Rectangle rr = r.Value;
            Ok(rr.Left >= -1 && rr.Top >= -1 && rr.Right <= dash.ClientSize.Width + 1 && rr.Bottom <= dash.ClientSize.Height + 1, "B dashboard card outside the panel: " + cn, () => R(rr) + " panel " + dash.ClientSize);
            Ok(rr.Width > 0 && rr.Height > 0, "B dashboard card has no size: " + cn, () => R(rr));
        }
        for (int i = 0; i < rs.Count; i++)
            for (int j = i + 1; j < rs.Count; j++)
            {
                Rectangle x = Rectangle.Intersect(rs[i].Value, rs[j].Value);
                Ok(!(x.Width > 1 && x.Height > 1), "B dashboard cards overlap: " + rs[i].Key + " / " + rs[j].Key, () => R(rs[i].Value) + " x " + R(rs[j].Value));
            }
    }

    // page semantics (from the G1 probe: 换手 / 移动 / 侧身 / 跳跃)
    private static TableLayoutPanel GridWithHeader(List<Control> vis, string head)
    {
        Label h = vis.OfType<Label>().FirstOrDefault(l => l.Text == head && l.Parent is TableLayoutPanel && ((TableLayoutPanel)l.Parent).ColumnCount >= 3);
        return h == null ? null : (TableLayoutPanel)h.Parent;
    }

    private static Control At(TableLayoutPanel t, int col, int row)
    {
        Control c = t.GetControlFromPosition(col, row);
        return c != null && c.Visible ? c : null;
    }

    private static bool Has(Control c, Func<Control, bool> pred)
    {
        if (c == null) return false;
        if (pred(c)) return true;
        return Walk(c).Any(x => x.Visible && pred(x));
    }

    private static bool IsPct(Control c) { return c.GetType().Name == "PctUpDown"; }

    private bool UiAdvanced() { return (bool)T("UiPrefs").GetMethod("Advanced", All).Invoke(null, new object[] { g }); }

    private void CheckSemantics(List<Control> vis, string id)
    {
        if (id == "hand")
        {
            TableLayoutPanel t = GridWithHeader(vis, "换手后 左右");
            if (t == null) { Fail("S6 换手 grid not found", ""); return; }
            Control hx = vis.OfType<Label>().First(l => l.Text == "换手后 左右"), hy = vis.OfType<Label>().FirstOrDefault(l => l.Text == "换手后 上下");
            int cx = t.GetPositionFromControl(hx).Column, row0 = t.GetPositionFromControl(hx).Row;
            Ok(hy != null && t.GetPositionFromControl(hy).Column == cx + 1 && t.GetPositionFromControl(hy).Row == row0, "S6 换手 header 上下 right of 左右", () => "");
            int rows = 0;
            for (int r = row0 + 1; r < t.RowCount; r++)
            {
                Control name = At(t, 0, r);
                if (name == null) continue;
                rows++;
                Control fx = At(t, cx, r), fy = At(t, cx + 1, r), last = At(t, cx + 2, r);
                bool drift = name.Text == "后坐力水平偏移";
                Ok(name is Label && !string.IsNullOrEmpty(name.Text), "S6 换手 row label", () => D(name));
                Ok(Has(fx, IsPct), "S6 换手 左右 % cell under its header", () => name.Text);
                Ok(drift ? fy == null || !Has(fy, IsPct) : Has(fy, IsPct), "S6 换手 上下 % cell under its header", () => name.Text);
                Ok(Has(last, c => (c is Label && c.Text == "自动") || (c is Button && c.Text == "恢复自动")), "S6 换手 自动 / 恢复自动 in the last column", () => name.Text);
                if (fx != null) Ok(Math.Abs(fx.Left - hx.Left) <= 1, "S6 换手 左右 cell starts under its header", () => "cell " + fx.Left + " header " + hx.Left);
                if (fy != null && hy != null && !drift) Ok(Math.Abs(fy.Left - hy.Left) <= 1, "S6 换手 上下 cell starts under its header", () => "cell " + fy.Left + " header " + hy.Left);
            }
            int want = ((int[])T("UiPages").GetMethod("HandRows", All).Invoke(null, null)).Length - (UiAdvanced() ? 0 : 1);
            Ok(rows == want, "S6 换手 key rows", () => rows + " want " + want);
        }
        else if (id == "movement" || id == "lean")
        {
            TableLayoutPanel t = GridWithHeader(vis, "上下");
            if (t == null) { Fail("S6 " + id + " grid not found", ""); return; }
            int rows = 0;
            for (int r = 1; r < t.RowCount; r++)
            {
                Control name = At(t, 0, r);
                if (name == null || (name.Text ?? "") == "过渡时间") continue;
                rows++;
                Ok(Has(At(t, 1, r), IsPct) && Has(At(t, 2, r), IsPct), "S6 " + id + " row has 左右 and 上下 % cells", () => D(name));
            }
            Ok(rows == (id == "movement" ? 4 : 2), "S6 " + id + " key rows", () => rows.ToString());
        }
        else if (id == "jump" && UiAdvanced())
        {
            TableLayoutPanel t = GridWithHeader(vis, "时长 ms");
            if (t == null) { Fail("S6 跳跃 phase grid not found", ""); return; }
            for (int r = 1; r < t.RowCount; r++)
            {
                Control name = At(t, 0, r);
                if (name == null) continue;
                Ok(At(t, 1, r) is NumericUpDown && At(t, 3, r) is ComboBox, "S6 跳跃 phase row: ms box + curve choice", () => D(name));
            }
        }
    }

    // ------------------------------------------------------------------ dialogs

    private void RunDialogs()
    {
        Form owner = NewSettings();
        owner.Show();
        Pump(500);
        Type td = T("ThemedDialog"), nd = T("NameDialog");
        string chinese = (string)(T("Program").GetField("ChineseName", All) != null ? T("Program").GetField("ChineseName", All).GetValue(null) : T("Program").GetProperty("ChineseName", All).GetValue(null, null));
        string ver = (string)(T("Program").GetField("Version", All) != null ? T("Program").GetField("Version", All).GetValue(null) : T("Program").GetProperty("Version", All).GetValue(null, null));
        List<string[]> msgs = new List<string[]>();
        string listFile = Environment.GetEnvironmentVariable("UIC_DIALOGS");
        if (!string.IsNullOrEmpty(listFile) && File.Exists(listFile))
            foreach (string line in File.ReadAllLines(listFile, Encoding.UTF8))
            {
                if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split('\t');
                // name \t question(0/1) \t yes \t no \t message (\n escaped)
                msgs.Add(new[] { p[0], p[1], p.Length > 2 ? p[2] : "", p.Length > 3 ? p[3] : "", (p.Length > 4 ? p[4] : "").Replace("\\n", "\n").Replace("{ChineseName}", chinese).Replace("{Version}", ver) });
            }
        foreach (string[] m in msgs)
        {
            ctx = "dialog=" + m[0];
            Form d = null;
            try
            {
                ConstructorInfo c = td.GetConstructors(All).OrderByDescending(x => x.GetParameters().Length).First();
                d = (Form)c.Invoke(new object[] { owner, m[4], m[1] == "1", m[2].Length > 0 ? m[2] : null, m[3].Length > 0 ? m[3] : null });
                CheckDialog(d, owner, m[0]);
            }
            catch (Exception ex) { Fail("D dialog failed to build: " + m[0], Unwrap(ex).Message); }
            finally { if (d != null) { try { d.Close(); d.Dispose(); } catch (Exception) { } } }
        }
        string[][] names = { new[] { "新建配置", "", null }, new[] { "重命名配置", "Leo默认 (旧)", null }, new[] { "另存为新预设", "夜图", "给这套滤镜设置起个名字，例如 夜图 / 雪地" } };
        foreach (string[] nm in names)
        {
            ctx = "dialog=name:" + nm[0];
            Form d = null;
            try
            {
                ConstructorInfo c = nd.GetConstructors(All).OrderByDescending(x => x.GetParameters().Length).First();
                d = (Form)c.Invoke(new object[] { nm[0], nm[1], owner.Font, false, nm[2] });
                CheckDialog(d, owner, "name:" + nm[0]);
            }
            catch (Exception ex) { Fail("D name dialog failed to build: " + nm[0], Unwrap(ex).Message); }
            finally { if (d != null) { try { d.Close(); d.Dispose(); } catch (Exception) { } } }
        }
        owner.Close();
        owner.Dispose();
    }

    private void CheckDialog(Form d, Form owner, string name)
    {
        d.StartPosition = FormStartPosition.Manual;
        d.Location = Screen.PrimaryScreen.WorkingArea.Location;
        d.Show(owner);
        Pump(300);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Ok(d.Width <= wa.Width && d.Height <= wa.Height, "D dialog larger than the screen: " + name, () => "dialog " + d.Size + " work area " + wa.Size);
        fontOf.Clear();
        CheckTree(d, "dialog/" + name, null, false);
        foreach (Control c in Walk(d).Where(Shown))
        {
            if (c is ButtonBase || c is TextBox)
            {
                Rectangle r = In(c, d);
                Ok(r.Right <= d.ClientSize.Width + 1 && r.Bottom <= d.ClientSize.Height + 1 && r.Left >= -1 && r.Top >= -1,
                    "D control outside the dialog: " + name + " " + D(c), () => R(r) + " client " + d.ClientSize);
            }
        }
        if (shotDir != null && (ShotsAll || tag.StartsWith("2160x1440@200") || tag.StartsWith("1920x1080@100") || tag.StartsWith("1280x720@125")))
            Shot(d, "dialog-" + Regex.Replace(name, "[^A-Za-z0-9_-]", "_"));
    }

    // ------------------------------------------------------------------ crosshair tutorial

    private object F(object o, string name)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo fi = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (fi != null) return fi.GetValue(o);
            PropertyInfo pi = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
            if (pi != null && pi.GetIndexParameters().Length == 0) return pi.GetValue(o, null);
        }
        throw new MissingMemberException(o.GetType().Name + "." + name);
    }

    private void RunTutorial()
    {
        Type tt = T("TutorialForm");
        foreach (bool done in new[] { false, true })
        {
            g.GetType().GetField("TutorialDone", All).SetValue(g, done);
            Form f = (Form)tt.GetConstructors(All).OrderByDescending(x => x.GetParameters().Length).First().Invoke(new object[] { g, (Action<string>)delegate { } });
            f.StartPosition = FormStartPosition.Manual;
            f.Show();
            Pump(500);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size def = f.Size, min = f.MinimumSize;
            var sizes = new List<KeyValuePair<string, Rectangle>> {
                new KeyValuePair<string, Rectangle>("min", new Rectangle(wa.X, wa.Y, min.Width, min.Height)),
                new KeyValuePair<string, Rectangle>("default", new Rectangle(wa.X, wa.Y, def.Width, def.Height)),
                new KeyValuePair<string, Rectangle>("max", wa) };
            object frame = F(f, "frame");
            System.Collections.IList steps = (System.Collections.IList)F(frame, "Steps");
            MethodInfo go = frame.GetType().GetMethod("Go", All);
            MethodInfo toggleExp = frame.GetType().GetMethod("ToggleExpander", All);
            foreach (KeyValuePair<string, Rectangle> sz in sizes)
            {
                if (done && sz.Key != "default") continue; // the replay title only changes the header
                f.Bounds = sz.Value;
                Pump(300);
                // the form relayouts at ResizeEnd only while a drag runs; a programmatic size goes through OnCanvasResize
                for (int i = 0; i < steps.Count; i++)
                {
                    go.Invoke(frame, new object[] { i });
                    Pump(60);
                    ctx = "tutorial done=" + (done ? 1 : 0) + " win=" + sz.Key + " " + f.ClientSize.Width + "x" + f.ClientSize.Height + " step=" + (i + 1);
                    CheckTutorialStep(f, frame, i, false);
                    bool hasOpt = false;
                    object st = steps[i];
                    Array fields = F(st, "Fields") as Array;
                    if (fields != null) foreach (object fd in fields) if ((bool)F(fd, "Optional")) hasOpt = true;
                    if (hasOpt)
                    {
                        toggleExp.Invoke(frame, null);
                        Pump(60);
                        ctx += " expanded";
                        CheckTutorialStep(f, frame, i, true);
                        toggleExp.Invoke(frame, null);
                    }
                    if (shotDir != null && !done && (ShotsAll ? sz.Key == "default" : (sz.Key == "min" || sz.Key == "max") && (tag.StartsWith("2160x1440@200") || tag.StartsWith("1920x1080@100") || tag.StartsWith("1280x720@125"))))
                        Shot(f, "tutorial-" + sz.Key + "-s" + (i + 1).ToString("00"));
                }
            }
            f.Close();
            f.Dispose();
            Pump(80);
        }
    }

    private void CheckTutorialStep(Form f, object frame, int step, bool expanded)
    {
        Control canvas = (Control)F(f, "canvas");
        object gx = F(frame, "Gx");
        object fonts = F(gx, "F");
        Font small = (Font)F(fonts, "Small"), tagF = (Font)F(fonts, "Tag"), big = (Font)F(fonts, "Big"), button = (Font)F(fonts, "Button");
        // measured like the frame lays text out: its own measuring Graphics at the screen DPI, the frame's StringFormat
        Graphics mg = (Graphics)F(frame, "measureG");
        StringFormat near = (StringFormat)F(gx, "Near");
        Func<string, Font, SizeF> M = (s, ft) => string.IsNullOrEmpty(s) ? SizeF.Empty : mg.MeasureString(s, ft, PointF.Empty, near);
        RectangleF rPanel = (RectangleF)F(frame, "rPanel"), rHeader = (RectangleF)F(frame, "rHeader"), rFooter = (RectangleF)F(frame, "rFooter"),
            rPrompt = (RectangleF)F(frame, "rPrompt"), bExit = (RectangleF)F(frame, "bExit"), bNext = (RectangleF)F(frame, "bNext"),
            rScene = (RectangleF)F(frame, "rScene"), rTerm = (RectangleF)F(frame, "rTerm");
        Size client = canvas.ClientSize;
        // canvas parts inside the window and apart
        foreach (var kv in new[] { new { n = "panel", r = rPanel }, new { n = "scene", r = rScene }, new { n = "terminal", r = rTerm }, new { n = "footer", r = rFooter } })
            Ok(kv.r.Left >= -0.5f && kv.r.Top >= -0.5f && kv.r.Right <= client.Width + 0.5f && kv.r.Bottom <= client.Height + 0.5f, "T canvas part outside the window: " + kv.n, () => RF(kv.r) + " client " + client);
        Ok(rPrompt.Right <= bExit.Left + 0.5f, "T footer prompt runs under the buttons", () => "prompt " + RF(rPrompt) + " exit " + RF(bExit));
        Ok(rPrompt.Width >= 120 * k - 0.5f, "T footer prompt narrower than 120 px", () => RF(rPrompt));
        // header: title | dots | step counter
        RectangleF[] dots = (RectangleF[])F(frame, "dotRects");
        string headerTitle = (string)F(frame, "headerTitle"), count = (string)F(frame, "sStepCount");
        if (dots.Length > 0)
        {
            float titleRight = 16 * k + 26 * k + M(headerTitle, big).Width;
            Ok(titleRight <= dots[0].Left - 4 * k, "T header title runs into the step dots", () => "title right " + titleRight + " first dot " + dots[0].Left);
            float countLeft = rHeader.Right - 18 * k - M(count, small).Width;
            Ok(countLeft >= dots[dots.Length - 1].Right + 4 * k, "T header step counter runs into the step dots: '" + count + "'", () => "counter left " + countLeft + " last dot " + dots[dots.Length - 1].Right);
            Ok(countLeft >= titleRight + 4 * k, "T header step counter runs into the title", () => "counter left " + countLeft + " title right " + titleRight);
        }
        // footer buttons: labels fit (they are sized from the labels) and stay in the footer
        foreach (string b in new[] { "bExit", "bSkip", "bPrev", "bNext" })
        {
            RectangleF r = (RectangleF)F(frame, b);
            Ok(r.Left >= rPrompt.Right - 0.5f && r.Right <= client.Width + 0.5f && r.Top >= rFooter.Top - 0.5f && r.Bottom <= rFooter.Bottom + 0.5f, "T footer button outside the footer: " + b, () => RF(r));
        }
        // text blocks: no line wider than the panel column
        float pad = 18 * k;
        float innerW = Math.Max(120 * k, rPanel.Width - 2 * pad - 8 * k);
        foreach (string tb in new[] { "tbTitle", "tbBody", "tbNote", "tbFieldNote" })
        {
            object b = F(frame, tb);
            float w = (float)F(b, "Width");
            Ok(w <= innerW + 1, "T text line wider than the panel column: " + tb, () => "line " + w + " column " + innerW);
        }
        bool overflow = (bool)F(frame, "PanelOverflow");
        string winKey = Regex.Match(ctx, @"win=(\w+)").Groups[1].Value;
        if (overflow) Info("T panel content needs scrolling (" + (expanded ? "optional rows open" : "normal view") + ") win=" + winKey + " step " + (step + 1).ToString("00"), "step " + (step + 1));
        // fields: the label is drawn without clipping left of the arrow / box; the real box must fit its painted slot
        RectangleF[] lab = (RectangleF[])F(frame, "fieldLabel"), ctl = (RectangleF[])F(frame, "fieldCtl"), arr = (RectangleF[])F(frame, "fieldArrow");
        bool[] shown = (bool[])F(frame, "fieldShown");
        object cur = F(frame, "Current");
        Array fields = F(cur, "Fields") as Array;
        float scroll = (float)F(frame, "scroll");
        List<Control> fc = ((System.Collections.IList)F(f, "fieldCtl")).Cast<Control>().ToList();
        for (int i = 0; fields != null && i < fields.Length; i++)
        {
            if (!shown[i]) continue;
            object fd = fields.GetValue(i);
            string label = (string)F(fd, "Label") ?? "";
            float lw = M(label, small).Width;
            Ok(lw <= lab[i].Width + 0.5f, "T field label wider than its slot (runs under the arrow / box): '" + label + "'", () => "label " + lw + " slot " + lab[i].Width + " step " + (step + 1));
            if (!arr[i].IsEmpty) Ok(lab[i].Right <= arr[i].Left + 0.5f, "T field label slot overlaps its arrow", () => RF(lab[i]) + " " + RF(arr[i]));
            string unit = (string)F(fd, "Unit");
            if (!string.IsNullOrEmpty(unit))
            {
                float ur = ctl[i].Right + 6 * k + M(unit, small).Width;
                Ok(ur <= rPanel.Right - 2 * k, "T field unit past the panel edge: '" + unit + "'", () => "unit right " + ur + " panel right " + rPanel.Right);
            }
            if (i < fc.Count && fc[i].Visible)
            {
                Control c = fc[i];
                NumericUpDown n = c as NumericUpDown;
                if (n != null)
                {
                    int ph = n.PreferredHeight;
                    // Windows: UpDownBase has ControlStyles.FixedHeight -> the box is PreferredHeight tall whatever Bounds says
                    Ok(ph <= ctl[i].Height + 1, "T number box taller than its painted slot on Windows (FixedHeight): field '" + label + "'", () => "PreferredHeight " + ph + " slot " + ctl[i].Height);
                    if (ph < ctl[i].Height - 3) Info("T number box shorter than its slot / arrow on Windows (top-aligned, " + (ctl[i].Height - ph) + " px gap)", "field '" + label + "' slot " + ctl[i].Height + " box " + ph);
                    CheckText(n, f, "tutorial");
                }
            }
        }
        // real controls on the canvas do not overlap each other
        List<Control> on = fc.Where(c => c.Visible).ToList();
        for (int i = 0; i < on.Count; i++)
            for (int j = i + 1; j < on.Count; j++)
            {
                Rectangle x = Rectangle.Intersect(on[i].Bounds, on[j].Bounds);
                Ok(!(x.Width > 1 && x.Height > 1), "T field controls overlap", () => R(on[i].Bounds) + " x " + R(on[j].Bounds));
            }
    }

    // ------------------------------------------------------------------ filter tutorial

    private void RunFilterTutorial()
    {
        Type ft = T("FilterTutorialForm");
        Form f = (Form)ft.GetConstructors(All).OrderByDescending(x => x.GetParameters().Length).First().Invoke(new object[] { null });
        f.StartPosition = FormStartPosition.Manual;
        f.Show();
        Pump(500);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Size def = f.Size, min = f.MinimumSize;
        var sizes = new List<KeyValuePair<string, Rectangle>> {
            new KeyValuePair<string, Rectangle>("min", new Rectangle(wa.X, wa.Y, min.Width, min.Height)),
            new KeyValuePair<string, Rectangle>("default", new Rectangle(wa.X, wa.Y, def.Width, def.Height)),
            new KeyValuePair<string, Rectangle>("max", wa) };
        object view = F(f, "view"), model = F(f, "model");
        Control canvas = (Control)F(f, "canvas");
        view.GetType().GetField("RecordTexts", All).SetValue(view, true);
        int count = (int)model.GetType().GetField("StepCount", All).GetValue(null);
        MethodInfo go = model.GetType().GetMethod("Go", All);
        MethodInfo draw = view.GetType().GetMethod("Draw", All);
        MethodInfo check = view.GetType().GetMethod("CheckLayout", All);
        MethodInfo setClient = view.GetType().GetMethod("SetClient", All);
        foreach (KeyValuePair<string, Rectangle> sz in sizes)
        {
            f.Bounds = sz.Value;
            Pump(300);
            Size cs = canvas.ClientSize;
            setClient.Invoke(view, new object[] { cs, k });
            for (int i = 0; i < count; i++)
            {
                go.Invoke(model, new object[] { i });
                ctx = "filtertut win=" + sz.Key + " " + cs.Width + "x" + cs.Height + " step=" + (i + 1);
                using (Bitmap b = new Bitmap(Math.Max(1, cs.Width), Math.Max(1, cs.Height), PixelFormat.Format32bppPArgb))
                {
                    b.SetResolution(96f * k, 96f * k);
                    using (Graphics gr = Graphics.FromImage(b))
                    {
                        draw.Invoke(view, new object[] { gr, 1000.0 });
                        draw.Invoke(view, new object[] { gr, 1500.0 });
                    }
                    List<string> bad = (List<string>)check.Invoke(view, null);
                    int recorded = ((System.Collections.ICollection)F(view, "Texts")).Count;
                    Ok(recorded > 3, "F filter tutorial recorded no texts (check not effective)", () => "texts " + recorded);
                    if (bad.Count == 0) pass++;
                    foreach (string s in bad)
                    {
                        string sig = Regex.Replace(s, @"\[[^\]]*\]", "[..]");
                        Fail("F filter tutorial " + sig, s);
                    }
                    if (shotDir != null && (ShotsAll ? sz.Key == "default" : (sz.Key == "min" || sz.Key == "max") && (tag.StartsWith("2160x1440@200") || tag.StartsWith("1920x1080@100") || tag.StartsWith("1280x720@125"))))
                    {
                        Directory.CreateDirectory(shotDir);
                        string file = Path.Combine(shotDir, tag.Replace("%", "pct") + "-filtertut-" + sz.Key + "-s" + (i + 1).ToString("00") + ".png");
                        b.Save(file, ImageFormat.Png);
                        shots.Add(file);
                    }
                }
            }
        }
        f.Close();
        f.Dispose();
    }
}
