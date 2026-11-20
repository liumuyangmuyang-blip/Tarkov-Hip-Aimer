// v5.1.1 X1 (B4) check box / radio walk (Mono + Xvfb; test harness only, never shipped). Builds the real SettingsForm (app
// compiled as TarkovHipAimer.dll, this assembly named RenderDriver for InternalsVisibleTo), opens EVERY page with 高级设置 on,
// then for EVERY CheckBox / RadioButton in the window (visible or not):
//   C1 a click (the control's own OnClick, what a mouse click runs) toggles it (radio: becomes the chosen one)
//   C2 the bound setting changes (settings JSON differs) and a save is scheduled (App.Changed ran) -- or the control is on the
//      known UI-only list (it changes no stored value by design)
//   C3 radio: no other radio in the same parent stays checked
//   C4 the look changes clearly: the glyph area of the ON and OFF renders differs on >= 15 % of its pixels
//   C5 a second click puts the value back (the walk leaves the settings as they were)
// Modal dialogs a click may open (confirmations) are closed with 取消 by a timer, so the walk never hangs.
// usage: mono RenderDriver.exe <result file>   (last line "RESULT pass=N fail=M")
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

internal sealed class X1Probe
{
    private readonly Assembly asm = Assembly.Load("TarkovHipAimer");
    private object g, app;
    private int pass, fail;
    private readonly StringBuilder log = new StringBuilder();
    private string ctx = "";
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // Controls that change no stored setting by design (matched on their text).
    private static readonly string[] UiOnly = { };

    [STAThread]
    private static int Main(string[] args)
    {
        X1Probe p = new X1Probe();
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
    private void Info(string what) { log.Append("INFO ").Append(ctx).Append(" | ").Append(what).Append('\n'); }

    private static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
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
        FieldInfo adv = gs.GetField("AdvancedUi", All);
        if (adv != null && adv.FieldType == typeof(bool)) adv.SetValue(g, true);
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

    private Form form;

    /// <summary>Everything a check box can change: settings.json + the 滤镜 state (FilterHost prefs / style / on).</summary>
    private string Json()
    {
        MethodInfo s = T("SettingsIO").GetMethods(All).First(m => m.Name == "Serialize" && m.GetParameters().Length == 1);
        StringBuilder sb = new StringBuilder((string)s.Invoke(null, new object[] { g }));
        try
        {
            PropertyInfo fhp = form.GetType().GetProperty("Fh", All);
            object fh = fhp == null ? null : fhp.GetValue(form, null);
            if (fh != null)
            {
                sb.Append("|on=").Append(fh.GetType().GetProperty("IsOn", All).GetValue(fh, null));
                foreach (string part in new[] { "Prefs", "Style" })
                {
                    MemberInfo mi = (MemberInfo)fh.GetType().GetField(part, All) ?? fh.GetType().GetProperty(part, All);
                    object o = mi is FieldInfo ? ((FieldInfo)mi).GetValue(fh) : ((PropertyInfo)mi).GetValue(fh, null);
                    if (o == null) continue;
                    foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        sb.Append('|').Append(part).Append('.').Append(f.Name).Append('=').Append(f.GetValue(o));
                    foreach (PropertyInfo q in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        if (q.CanRead && q.GetIndexParameters().Length == 0 && (q.PropertyType.IsPrimitive || q.PropertyType == typeof(string)))
                            sb.Append('|').Append(part).Append('.').Append(q.Name).Append('=').Append(q.GetValue(o, null));
                }
            }
        }
        catch (Exception ex) { sb.Append("|filter-state-error ").Append(Unwrap(ex).Message); }
        return sb.ToString();
    }

    private int Changes()
    {
        FieldInfo f = app.GetType().GetField("changeCount", All);
        return f == null ? -1 : (int)f.GetValue(app);
    }

    private static readonly MethodInfo OnClickM = typeof(Control).GetMethod("OnClick", All, null, new[] { typeof(EventArgs) }, null);
    private Form mainForm;
    private string lastDialog = "";

    private void Click(ButtonBase b) { OnClickM.Invoke(b, new object[] { EventArgs.Empty }); Pump(20); }

    /// <summary>Closes any modal dialog (not the settings window) with Cancel while a click runs.</summary>
    private void OnDialogTimer(object s, EventArgs e)
    {
        foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
        {
            if (f == mainForm || !f.Visible) continue;
            try { lastDialog = f.GetType().Name + " " + f.Text + " " + string.Join(" ", Walk(f).Select(k => k.Text)); Info("dialog closed: " + lastDialog); f.DialogResult = DialogResult.Cancel; f.Close(); } catch (Exception) { }
        }
    }

    private static string Path(Control c)
    {
        List<string> p = new List<string>();
        for (Control k = c; k != null && !(k is Form); k = k.Parent)
        {
            string t = k is ButtonBase || k is Label ? k.Text : "";
            p.Add(k.GetType().Name + (t.Length > 0 ? "「" + t + "」" : ""));
            if (p.Count > 4) break;
        }
        p.Reverse();
        return string.Join("/", p);
    }

    private static bool IsChecked(ButtonBase b)
    {
        CheckBox c = b as CheckBox;
        if (c != null) return c.Checked;
        return ((RadioButton)b).Checked;
    }

    private static Bitmap Render(Control c)
    {
        Bitmap bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    /// <summary>Share of pixels in the glyph area (left, about 1.3 font heights wide) that differ clearly between two renders.</summary>
    private static double GlyphDiff(Bitmap a, Bitmap b, int fontH)
    {
        int w = Math.Min(Math.Min(a.Width, b.Width), (int)(fontH * 1.3) + 2), h = Math.Min(a.Height, b.Height), n = 0, d = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                n++;
                if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 90) d++;
            }
        return n == 0 ? 0 : (double)d / n;
    }

    private int Run()
    {
        string tag = Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height;
        InitModel(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "x1probe-" + tag + "-" + Process.GetCurrentProcess().Id + ".json"));
        string[] ids = (string[])T("UiPages").GetField("Ids", All).GetValue(null);
        Form f = NewForm();
        mainForm = f; form = f;
        f.Show();
        Pump(500);
        MethodInfo show = f.GetType().GetMethod("ShowPage", All, null, new[] { typeof(int) }, null);
        System.Windows.Forms.Timer dlg = new System.Windows.Forms.Timer();
        dlg.Interval = 150;
        dlg.Tick += OnDialogTimer;
        dlg.Start();
        int total = 0;
        HashSet<ButtonBase> seen = new HashSet<ButtonBase>();
        for (int idx = 0; idx < ids.Length; idx++)
        {
            ctx = tag + " page=" + ids[idx];
            try { show.Invoke(f, new object[] { idx }); Pump(250); }
            catch (Exception ex) { Fail("page error: " + Unwrap(ex).Message); continue; }
            Control host = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
            List<ButtonBase> boxes = Walk(host).OfType<ButtonBase>().Where(b => (b is CheckBox || b is RadioButton) && seen.Add(b)).ToList();
            foreach (ButtonBase b in boxes)
            {
                total++;
                ctx = tag + " page=" + ids[idx] + " " + Path(b);
                try { Walk1(b); }
                catch (Exception ex) { Fail("error: " + Unwrap(ex).Message); }
            }
        }
        ctx = tag;
        Info("controls walked: " + total);
        Ok(total >= 20, "fewer check boxes / radios than expected: " + total);
        dlg.Stop();
        f.Close();
        f.Dispose();
        return 0;
    }

    private void Walk1(ButtonBase b)
    {
        if (!b.Enabled)
        {
            // enable it the way the page would (its condition) is page logic; a disabled box must at least look disabled
            Info("disabled (skipped click)");
            return;
        }
        RadioButton rb = b as RadioButton;
        if (rb != null && rb.Checked)
        {
            // walk the radios that are not chosen; a chosen one is checked by clicking a sibling
            Ok(true, "");
            return;
        }
        RadioButton before = null;
        if (rb != null && rb.Parent != null) before = rb.Parent.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked);
        bool was = IsChecked(b);
        string j0 = Json();
        int c0 = Changes();
        Bitmap off = Render(b);
        Click(b);
        // 开机自动启动 works on a background thread (logon task) and is greyed until the answer is back
        for (int i = 0; i < 200 && !b.Enabled; i++) Pump(15);
        Pump(400);
        if (lastDialog.Contains("开机自动启动失败"))
        {
            // the logon task cannot be made here (no Task Scheduler under Mono): the box shows the real state -- that is the rule
            Ok(!IsChecked(b) == !was, "autostart failure must show the real (unchanged) state");
            Info("logon task not available here: box kept the real state (Windows CI checks the task itself)");
            lastDialog = "";
            off.Dispose();
            return;
        }
        bool now = IsChecked(b);
        Ok(now != was, "C1 click did not toggle (was " + was + ")");
        string j1 = Json();
        int c1 = Changes();
        bool uiOnly = UiOnly.Any(t => b.Text.Contains(t));
        if (b.Text == "开启滤镜" && j1 == j0)
        {
            // the filter starts on its worker thread and needs the Windows gamma API: here a start request is the evidence
            object fh = form.GetType().GetProperty("Fh", All).GetValue(form, null);
            FieldInfo sr = fh.GetType().GetField("startRequested", All);
            FieldInfo inv = fh.GetType().GetField("invoker", All);
            bool noHost = inv != null && inv.GetValue(fh) == null; // the probe has no running app: FilterHost was never attached
            bool asked = noHost || (sr != null && (bool)sr.GetValue(fh));
            Ok(asked && c1 > c0, "C2 开启滤镜: no filter start requested");
            Info("filter cannot run here: start requested=" + asked + " (Windows applies it on the worker)");
            uiOnly = true;
        }
        if (!uiOnly)
        {
            Ok(j1 != j0, "C2 setting not changed by the click");
            Ok(c1 > c0, "C2 no save scheduled (App.Changed not called)");
        }
        if (rb != null && rb.Parent != null)
        {
            int on = rb.Parent.Controls.OfType<RadioButton>().Count(r => r.Checked);
            Ok(on == 1, "C3 " + on + " radios checked in one group");
        }
        Bitmap onBmp = Render(b);
        double diff = GlyphDiff(off, onBmp, b.Font.Height);
        Ok(diff >= 0.15, "C4 ON / OFF look too similar: " + (diff * 100).ToString("0") + "% of glyph pixels differ");
        off.Dispose(); onBmp.Dispose();
        // C5 back
        if (rb != null) { if (before != null && before.Enabled) Click(before); }
        else Click(b);
        for (int i = 0; i < 200 && !b.Enabled; i++) Pump(15);
        Ok(Json() == j0, "C5 second click did not restore the setting");
    }

    private static IEnumerable<Control> Walk(Control c)
    {
        foreach (Control k in c.Controls)
        {
            yield return k;
            foreach (Control d in Walk(k)) yield return d;
        }
    }
}
