// v5.1 W30 text-fit probe (Mono + Xvfb; test harness only, never shipped). Builds the real SettingsForm (app compiled as
// TarkovHipAimer.dll, this assembly named RenderDriver for InternalsVisibleTo), shows EVERY page (folds opened) at the
// minimum and the full window width, and runs TextFit.Check on the page host at the screen's real scale:
//   BEFORE lines = what the pages build today; then TextFit.Fix (what W25's FitPage does) and FAIL lines = what is still
//   too small / clipped / Chinese in mono after the fix. Mono stands Noto in for Microsoft YaHei (renamed by fonts.conf);
//   TextFitMath's YaHei width floor keeps the check conservative.
// usage: mono RenderDriver.exe <result file>   (last line "RESULT pass=N fail=M before=K")
using System;
using System.Collections;
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

internal sealed class W30Probe
{
    private readonly Assembly asm = Assembly.Load("TarkovHipAimer");
    private object g, app;
    private int pass, fail, before;
    private readonly StringBuilder log = new StringBuilder();
    private string ctx = "";
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        W30Probe p = new W30Probe();
        int rc;
        try { rc = p.Run(); }
        catch (Exception ex) { p.Fail("probe crashed: " + Unwrap(ex)); rc = 1; }
        string res = p.log.ToString() + "RESULT pass=" + p.pass + " fail=" + p.fail + " before=" + p.before + "\n";
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


    private IList Check(Control host)
    {
        Type tf = T("TextFit");
        double cur = (double)tf.GetMethod("ScaleOf", All).Invoke(null, new object[] { host });
        return (IList)tf.GetMethod("Check", All, null, new[] { typeof(Control), typeof(double) }, null).Invoke(null, new object[] { host, cur });
    }

    private int Run()
    {
        string tag = Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height + "@" + Dpi() + "dpi";
        InitModel(Path.Combine(Path.GetTempPath(), "w30probe-" + tag + "-" + Process.GetCurrentProcess().Id + ".json"));
        string[] ids = (string[])T("UiPages").GetField("Ids", All).GetValue(null);
        Form f = NewForm();
        f.Show();
        Pump(600);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Size min = f.MinimumSize.Width > 0 ? f.MinimumSize : new Size(Math.Min(wa.Width, 800), Math.Min(wa.Height, 600));
        foreach (int w in new[] { min.Width, wa.Width }.Distinct())
        {
            f.Bounds = new Rectangle(wa.X, wa.Y, Math.Max(w, min.Width), wa.Width == w ? wa.Height : Math.Max(min.Height, Math.Min(wa.Height, min.Height + 80)));
            Pump(250);
            for (int idx = 0; idx < ids.Length; idx++)
            {
                string id = ids[idx];
                ctx = tag + " win=" + f.Width + "x" + f.Height + " page=" + id;
                try
                {
                    f.GetType().GetMethod("ShowPage", All, null, new[] { typeof(int) }, null).Invoke(f, new object[] { idx });
                    Pump(300);
                    OpenFolds(f);
                    Control host = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
                    IList b = Check(host);
                    foreach (object o in b) { before++; log.Append("BEFORE ").Append(ctx).Append(" | ").Append(o).Append('\n'); }
                    T("TextFit").GetMethod("Fix", All).Invoke(null, new object[] { host });
                    Pump(250);
                    IList a = Check(host);
                    if (a.Count == 0) pass++;
                    foreach (object o in a) Fail(o.ToString());
                }
                catch (Exception ex) { Fail("page error: " + Unwrap(ex).Message); }
            }
        }
        // the window shell itself (nav, header, bottom links)
        ctx = tag + " shell";
        Control ph = (Control)f.GetType().GetField("pageHost", All).GetValue(f);
        ph.Name = "pageHost";
        IList s = Check(f);
        int shellBad = 0;
        foreach (object o in s) { string t = o.ToString(); if (t.Contains("/pageHost")) continue; shellBad++; Fail(t); }
        if (shellBad == 0) pass++;
        f.Close();
        f.Dispose();
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
}
