// v4.3.1 G1 structural layout probe (Mono + Xvfb; test harness only, never shipped).
// Builds the real SettingsForm (app compiled as TarkovHipAimer.dll with InternalsVisibleTo("RenderDriver"), so this
// assembly is also named RenderDriver) and checks the 移动 / 侧身 / 换手 / 跳跃 pages STRUCTURALLY at several window widths
// (minimum .. maximised), with 高级设置 off and on and the 换手 rows auto and custom:
//   S1 every multi-column grid places its children at FIXED cells (no flow: a hidden cell can never shift the others)
//   S2 every visible child lies inside its own cell band (column x range, row y range) -> nothing wrapped into another row/column
//   S3 no two visible siblings overlap
//   S4 a row's column-0 label is on the same row as every control of that row (vertical ranges intersect) and left of it
//   S5 nothing is clipped: each visible child inside its parent's client area, every control inside the page width
//   S6 page semantics: 换手 header 换手后 左右/上下 above the % cells of the same column (same left x), each key row has its
//      label | % cell(s) | 自动 or 恢复自动 in its own row; 移动 / 侧身 rows have label | 左右 % | 上下 %; 跳跃 phase rows.
// usage: mono RenderDriver.exe <result file>   (one line per check failure, last line "RESULT pass=N fail=M")
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class G1Probe
{
    private readonly Assembly asm = Assembly.Load("TarkovHipAimer");
    private object g, app;
    private int pass, fail;
    private readonly StringBuilder log = new StringBuilder();
    private string ctx = "";
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        G1Probe p = new G1Probe();
        int rc;
        try { rc = p.Run(); }
        catch (Exception ex) { p.Fail("probe crashed: " + Unwrap(ex)); rc = 1; }
        string res = p.log.ToString() + "RESULT pass=" + p.pass + " fail=" + p.fail + "\n";
        if (args.Length > 0) File.WriteAllText(args[0], res); else Console.Write(res);
        return p.fail == 0 && rc == 0 ? 0 : 1;
    }

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

    private void Ok(bool ok, string what) { if (ok) pass++; else Fail(what); }
    private void Fail(string what) { fail++; if (fail <= 400) log.Append("FAIL ").Append(ctx).Append(" | ").Append(what).Append('\n'); }

    private static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(8); } while (sw.ElapsedMilliseconds < ms);
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
        if (t.IsInstanceOfType(app)) return app;
        if (t == typeof(string)) return "";
        if (t == typeof(Action)) return (Action)delegate { };
        if (t == typeof(Action<string>)) return (Action<string>)delegate { };
        if (t == typeof(Action<int>)) return (Action<int>)delegate { };
        if (t.IsValueType) return Activator.CreateInstance(t);
        if (t.GetConstructor(Type.EmptyTypes) != null) return Activator.CreateInstance(t);
        return null;
    }

    private Form NewForm()
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
        p.GetType().GetMethod("AllHandAuto", All).Invoke(p, null);
        if (custom)
        {
            MethodInfo set = p.GetType().GetMethod("SetHandCustom", All);
            int[] rows = (int[])T("UiPages").GetMethod("HandRows", All).Invoke(null, null);
            // every other row custom: 恢复自动 and 自动 rows mixed in one table (the screenshot case)
            for (int i = 0; i < rows.Length; i += 2) set.Invoke(p, new object[] { rows[i], 37.5 + i, -21.25 });
        }
    }

    private int Run()
    {
        string tag = Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height + "@" + Dpi() + "dpi";
        InitModel(Path.Combine(Path.GetTempPath(), "g1probe-" + tag + "-" + Process.GetCurrentProcess().Id + ".json"));
        string[] ids = (string[])T("UiPages").GetField("Ids", All).GetValue(null);
        string[] pageIds = { "movement", "lean", "hand", "jump" };
        foreach (bool adv in new[] { false, true })
            foreach (bool custom in new[] { false, true })
            {
                SetModel(adv, custom);
                Form f = NewForm();
                f.Show();
                Pump(600);
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Size min = f.MinimumSize.Width > 0 ? f.MinimumSize : new Size(Math.Min(wa.Width, 800), Math.Min(wa.Height, 600));
                List<int> widths = new List<int> { min.Width };
                for (int s = 1; s <= 3; s++) widths.Add(min.Width + (wa.Width - min.Width) * s / 4);
                widths.Add(wa.Width);
                foreach (int w in widths.Distinct())
                {
                    f.Bounds = new Rectangle(wa.X, wa.Y, Math.Max(w, min.Width), wa.Width == w ? wa.Height : Math.Max(min.Height, Math.Min(wa.Height, min.Height + 80)));
                    Pump(250);
                    foreach (string id in pageIds)
                    {
                        int idx = Array.IndexOf(ids, id);
                        f.GetType().GetMethod("ShowPage", All, null, new[] { typeof(int) }, null).Invoke(f, new object[] { idx });
                        Pump(300);
                        OpenFolds(f);
                        ctx = tag + " adv=" + (adv ? 1 : 0) + " custom=" + (custom ? 1 : 0) + " win=" + f.Width + "x" + f.Height + " page=" + id;
                        CheckPage(f, id);
                    }
                }
                f.Close();
                f.Dispose();
                Pump(100);
            }
        return 0;
    }

    private static int Dpi()
    {
        try { using (Graphics gr = Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(gr.DpiX); } catch (Exception) { return 96; }
    }

    private void OpenFolds(Form f)
    {
        Control host = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
        bool any = false;
        foreach (Control h in Walk(host).Where(c => c.GetType().Name == "FoldHeader" && c.Visible).ToList())
        {
            Control parent = h.Parent;
            foreach (Control sib in parent.Controls) if (sib != h && !sib.Visible) { sib.Visible = true; any = true; }
        }
        if (any)
        {
            MethodInfo refit = f.GetType().GetMethod("PostRefit", All, null, Type.EmptyTypes, null);
            if (refit != null) refit.Invoke(f, null);
            Pump(300);
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

    private static Rectangle In(Control c, Control root)
    {
        Point p = root.PointToClient(c.PointToScreen(Point.Empty));
        return new Rectangle(p, c.Size);
    }

    private static string D(Control k)
    {
        string t = k.Text ?? "";
        if (t.Length > 20) t = t.Substring(0, 20) + "..";
        return k.GetType().Name + (t.Length > 0 ? "'" + t.Replace("\n", " ") + "'" : "");
    }

    private void CheckPage(Form f, string id)
    {
        Control host = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
        List<Control> vis = Walk(host).Where(c => c.Visible).ToList();
        int hostW = host.ClientSize.Width;
        // the scroller of the page: controls must fit its client width (no sideways scrolling / cut-off)
        foreach (Control c in vis)
        {
            if (c.Width <= 0 || c.Height <= 0) continue;
            ScrollableControl sc = null;
            for (Control p = c.Parent; p != null && p != host.Parent; p = p.Parent)
                if (p is ScrollableControl && ((ScrollableControl)p).AutoScroll) { sc = (ScrollableControl)p; break; }
            if (sc == null) continue;
            Rectangle r = In(c, sc);
            Ok(r.Right <= sc.ClientSize.Width + 1, "S5 clipped at page right: " + D(c) + " right " + r.Right + " > page " + sc.ClientSize.Width);
        }
        foreach (TableLayoutPanel t in vis.OfType<TableLayoutPanel>()) CheckGrid(t);
        CheckSemantics(vis, id);
    }

    private void CheckGrid(TableLayoutPanel t)
    {
        List<Control> kids = t.Controls.Cast<Control>().Where(k => k.Visible && k.Width > 0 && k.Height > 0).ToList();
        // S5 inside the parent
        foreach (Control k in kids)
            Ok(k.Left >= -1 && k.Top >= -1 && k.Right <= t.ClientSize.Width + 1 && k.Bottom <= t.ClientSize.Height + 1,
                "S5 outside parent: " + D(k) + " " + k.Bounds + " in " + D(t) + " " + t.ClientSize);
        // S3 no overlap
        for (int i = 0; i < kids.Count; i++)
            for (int j = i + 1; j < kids.Count; j++)
            {
                Rectangle x = Rectangle.Intersect(kids[i].Bounds, kids[j].Bounds);
                Ok(!(x.Width > 1 && x.Height > 1), "S3 overlap: " + D(kids[i]) + " / " + D(kids[j]) + " " + x.Size);
            }
        if (t.ColumnCount < 2 || t.GetType() != typeof(TableLayoutPanel)) return;
        int[] cw = t.GetColumnWidths(), rh = t.GetRowHeights();
        Func<int, int> x0 = c => t.Padding.Left + cw.Take(c).Sum();
        Func<int, int> y0 = r => t.Padding.Top + rh.Take(r).Sum();
        Dictionary<int, Control> rowLabel = new Dictionary<int, Control>();
        foreach (Control k in kids)
        {
            // S1 fixed cell
            TableLayoutPanelCellPosition cp = t.GetCellPosition(k);
            Ok(cp.Column >= 0 && cp.Row >= 0, "S1 flowed (no fixed cell): " + D(k) + " in " + D(t));
            TableLayoutPanelCellPosition at = t.GetPositionFromControl(k);
            if (at.Column < 0 || at.Row < 0 || at.Column >= cw.Length || at.Row >= rh.Length) { Fail("S2 no cell: " + D(k)); continue; }
            int cs = Math.Max(1, t.GetColumnSpan(k)), rs = Math.Max(1, t.GetRowSpan(k));
            int bx0 = x0(at.Column), bx1 = x0(Math.Min(cw.Length, at.Column + cs)), by0 = y0(at.Row), by1 = y0(Math.Min(rh.Length, at.Row + rs));
            // S2 inside its own cell band
            Ok(k.Left >= bx0 - 1 && k.Right <= bx1 + 1 && k.Top >= by0 - 1 && k.Bottom <= by1 + 1,
                "S2 out of cell (" + at.Column + "," + at.Row + "): " + D(k) + " " + k.Bounds + " band x " + bx0 + ".." + bx1 + " y " + by0 + ".." + by1);
            if (at.Column == 0 && cs == 1 && k is Label && !string.IsNullOrEmpty(k.Text)) rowLabel[at.Row] = k;
        }
        // S4 label left of and on the same row as its controls
        foreach (KeyValuePair<int, Control> kv in rowLabel)
        {
            Control l = kv.Value;
            foreach (Control k in kids)
            {
                if (k == l) continue;
                TableLayoutPanelCellPosition at = t.GetPositionFromControl(k);
                if (at.Row != kv.Key || at.Column == 0) continue;
                Ok(l.Right <= k.Left + 1, "S4 label not left of its control: " + D(l) + " right " + l.Right + " / " + D(k) + " left " + k.Left);
                Ok(l.Top < k.Bottom && k.Top < l.Bottom, "S4 label not on its control's row: " + D(l) + " y " + l.Top + ".." + l.Bottom + " / " + D(k) + " y " + k.Top + ".." + k.Bottom);
            }
        }
    }

    // ---------------- page semantics

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

    private void CheckSemantics(List<Control> vis, string id)
    {
        if (id == "hand")
        {
            TableLayoutPanel t = GridWithHeader(vis, "换手后 左右");
            if (t == null) { Fail("S6 换手 grid not found"); return; }
            Control hx = vis.OfType<Label>().First(l => l.Text == "换手后 左右"), hy = vis.OfType<Label>().FirstOrDefault(l => l.Text == "换手后 上下");
            int cx = t.GetPositionFromControl(hx).Column, row0 = t.GetPositionFromControl(hx).Row;
            Ok(hy != null && t.GetPositionFromControl(hy).Column == cx + 1 && t.GetPositionFromControl(hy).Row == row0, "S6 换手后 上下 header right of 换手后 左右 in the header row");
            int rows = 0;
            for (int r = row0 + 1; r < t.RowCount; r++)
            {
                Control name = At(t, 0, r);
                if (name == null) continue;
                rows++;
                Control fx = At(t, cx, r), fy = At(t, cx + 1, r), last = At(t, cx + 2, r);
                Ok(name is Label && !string.IsNullOrEmpty(name.Text), "S6 换手 row " + r + " column 0 is a key label: " + D(name));
                Ok(Has(fx, IsPct), "S6 换手 row " + r + " (" + name.Text + ") 左右 % cell in column " + cx);
                bool drift = name.Text == "后坐力水平偏移";
                Ok(drift ? fy == null || !Has(fy, IsPct) : Has(fy, IsPct), "S6 换手 row " + r + " (" + name.Text + ") 上下 % cell in column " + (cx + 1));
                Ok(Has(last, c => (c is Label && c.Text == "自动") || (c is Button && c.Text == "恢复自动")),
                    "S6 换手 row " + r + " (" + name.Text + ") 自动 / 恢复自动 in the last column");
                if (fx != null) Ok(Math.Abs(fx.Left - hx.Left) <= 1, "S6 换手 左右 cell starts under its header: cell " + fx.Left + " header " + hx.Left);
                if (fy != null && hy != null && !drift) Ok(Math.Abs(fy.Left - hy.Left) <= 1, "S6 换手 上下 cell starts under its header: cell " + fy.Left + " header " + hy.Left);
            }
            int want = ((int[])T("UiPages").GetMethod("HandRows", All).Invoke(null, null)).Length - (UiAdvanced() ? 0 : 1);
            Ok(rows == want, "S6 换手 key rows: " + rows + " want " + want);
        }
        else if (id == "movement" || id == "lean")
        {
            TableLayoutPanel t = GridWithHeader(vis, "上下");
            if (t == null) { Fail("S6 " + id + " grid not found"); return; }
            int rows = 0;
            for (int r = 1; r < t.RowCount; r++)
            {
                Control name = At(t, 0, r);
                if (name == null || (name.Text ?? "") == "过渡时间") continue;
                rows++;
                Ok(Has(At(t, 1, r), IsPct) && Has(At(t, 2, r), IsPct), "S6 " + id + " row " + D(name) + ": 左右 and 上下 % cells");
            }
            Ok(rows == (id == "movement" ? 4 : 2), "S6 " + id + " key rows " + rows);
        }
        else if (id == "jump" && UiAdvanced())
        {
            TableLayoutPanel t = GridWithHeader(vis, "时长 ms");
            if (t == null) { Fail("S6 跳跃 phase grid not found"); return; }
            for (int r = 1; r < t.RowCount; r++)
            {
                Control name = At(t, 0, r);
                if (name == null) continue;
                Ok(At(t, 1, r) is NumericUpDown && At(t, 3, r) is ComboBox, "S6 跳跃 row " + D(name) + ": ms box + curve choice");
            }
        }
    }

    private bool UiAdvanced() { return (bool)T("UiPrefs").GetMethod("Advanced", All).Invoke(null, new object[] { g }); }
}
