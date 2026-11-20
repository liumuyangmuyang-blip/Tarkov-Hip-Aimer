using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

// v5.0.2 (W17): UI speed helpers, pure (no WinForms) so tests-v4 can run them.
//   SettingsSaver    settings.json is written on a background thread (the UI thread only takes a snapshot)
//   ForegroundState  "is one of our windows in front?" from Activated / Deactivate (no polling, no timer)
//   LogRotation      crash.log over 1 MB is moved to crash.old.log when the log writer starts (AUDIT F17)
//   PageBuildTimes   per-page construction ms of the settings window (crash.log + tests)

namespace TarkovCrosshair
{
    /// <summary>
    /// One background writer for settings.json. Request queues the newest snapshot (an older one still waiting is dropped:
    /// only the last state matters); the writer calls Write(snapshot) and then Done(snapshot, error) (the caller marshals to
    /// the UI thread). Flush waits until nothing is queued or being written. The thread waits on a monitor between saves,
    /// so an idle app has no wake-ups from it.
    /// </summary>
    internal sealed class SettingsSaver<T> where T : class
    {
        private readonly object sync = new object();
        private readonly Func<T, string> write;
        private T pending;
        private Action<T, string> pendingDone;
        private bool busy;
        private Thread thread;
        public int Writes;

        public SettingsSaver(Func<T, string> write) { this.write = write; }

        public bool Idle { get { lock (sync) return pending == null && !busy; } }

        public void Request(T snapshot, Action<T, string> done)
        {
            if (snapshot == null) return;
            lock (sync)
            {
                pending = snapshot;
                pendingDone = done;
                if (thread == null)
                {
                    Thread t = new Thread(Loop);
                    t.IsBackground = true;
                    t.Name = "SettingsSaver";
                    thread = t;
                    t.Start();
                }
                Monitor.PulseAll(sync);
            }
        }

        private void Loop()
        {
            while (true)
            {
                T snap; Action<T, string> done;
                lock (sync)
                {
                    while (pending == null) { busy = false; Monitor.PulseAll(sync); Monitor.Wait(sync); }
                    snap = pending; done = pendingDone;
                    pending = null; pendingDone = null;
                    busy = true;
                }
                string err;
                try { err = write(snap); }
                catch (Exception ex) { err = ex.Message; }
                Interlocked.Increment(ref Writes);
                try { if (done != null) done(snap, err); } catch (Exception) { }
            }
        }

        /// <summary>Waits (max ms) until every queued save is on disk. True when idle.</summary>
        public bool Flush(int ms)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            lock (sync)
            {
                while (pending != null || busy)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return false;
                    Monitor.Wait(sync, left);
                }
                return true;
            }
        }

        /// <summary>Drops a queued (not yet started) save; the caller writes synchronously instead (exit).</summary>
        public void DropPending() { lock (sync) { pending = null; pendingDone = null; Monitor.PulseAll(sync); } }
    }

    /// <summary>
    /// Whether one of our windows is in front, driven by events instead of a timer (Leo, v5.0.2: no timer ticks while the
    /// game is in front). Activated (any of our windows) -> in front. Deactivate -> the caller checks the real foreground
    /// window once (Native.ForegroundIsOurs) and reports it with Set. Changed fires on the UI thread on every change.
    /// </summary>
    internal static class ForegroundState
    {
        private static bool ours = true;
        public static event Action<bool> Changed;

        public static bool Ours { get { return ours; } }

        /// <summary>fgIsOurs: 1 ours, 0 another program, -1 unknown (keeps the current state).</summary>
        public static void Set(int fgIsOurs)
        {
            if (fgIsOurs < 0) return;
            bool now = fgIsOurs == 1;
            if (now == ours) return;
            ours = now;
            Action<bool> h = Changed;
            if (h != null) h(now);
        }

        /// <summary>Tests only.</summary>
        internal static void Reset() { ours = true; Changed = null; }
    }

    /// <summary>AUDIT F17: crash.log never grew past about 1 MB in v5.0.2: at the first log line of a run, a bigger file is
    /// moved to crash.old.log (replacing the older one). Never throws.</summary>
    internal static class LogRotation
    {
        public const long MaxBytes = 1024 * 1024;
        public const string OldName = "crash.old.log";

        /// <summary>Returns true when the file was moved.</summary>
        public static bool RotateIfBig(string logPath)
        {
            try
            {
                FileInfo fi = new FileInfo(logPath);
                if (!fi.Exists || fi.Length <= MaxBytes) return false;
                string old = Path.Combine(fi.DirectoryName ?? "", OldName);
                if (File.Exists(old)) File.Delete(old);
                File.Move(logPath, old);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Construction cost per settings page (first visit), for crash.log and the timing tests.</summary>
    internal static class PageBuildTimes
    {
        private static readonly object sync = new object();
        private static readonly List<KeyValuePair<string, long>> items = new List<KeyValuePair<string, long>>();

        public static void Add(string page, long ms)
        {
            lock (sync) { items.Add(new KeyValuePair<string, long>(page, ms)); if (items.Count > 200) items.RemoveAt(0); }
        }

        public static List<KeyValuePair<string, long>> Snapshot() { lock (sync) return new List<KeyValuePair<string, long>>(items); }

        /// <summary>The crash.log line for one page.</summary>
        public static string Line(string title, long buildMs, long applyMs)
        {
            return "启动耗时 设置页「" + title + "」: 第一次打开时创建 " + buildMs + " ms，填值和排版 " + applyMs + " ms";
        }
    }

    /// <summary>
    /// v5.1 (W25, decision 8) 性能模式: one switch for the settings UI (the crosshair is never affected). While On the
    /// dashboard / glow animations are off, UI animation timers run at FrameMs = 33 (<= 30 fps) instead of 16, and the
    /// tutorials are not built in the background. Set from GlobalSettings.PerfMode (SettingsForm / App); Changed fires on
    /// the UI thread.
    /// </summary>
    internal static class UiPerf
    {
        private static bool on;
        public static event Action<bool> Changed;
        public static bool On { get { return on; } }
        /// <summary>Interval for a UI animation timer: 16 ms normally, 33 ms (<= 30 fps) in 性能模式.</summary>
        public static int FrameMs { get { return on ? 33 : 16; } }
        /// <summary>Clamp an animation timer's own interval to the 性能模式 rate (never faster than 33 ms while On).</summary>
        public static int Interval(int wantedMs) { return on ? Math.Max(33, wantedMs) : wantedMs; }

        public static void Set(bool value)
        {
            if (value == on) return;
            on = value;
            Action<bool> h = Changed;
            if (h != null) h(value);
        }

        /// <summary>Tests only.</summary>
        internal static void Reset() { on = false; Changed = null; }
    }

    /// <summary>
    /// v5.1 (W25, decision 8): which settings page to build next in the background. Pages are built one per idle slot of
    /// the UI thread, in the order given (the navigation order), skipping pages already built. Pure.
    /// </summary>
    internal sealed class PrebuildQueue
    {
        private readonly int[] order;
        private int next;
        public PrebuildQueue(int[] order) { this.order = order ?? new int[0]; }

        /// <summary>Next page index whose built(i) is false, or -1 when every page is built.</summary>
        public int Next(Func<int, bool> built)
        {
            while (next < order.Length)
            {
                int i = order[next++];
                if (!built(i)) return i;
            }
            return -1;
        }

        public bool Done { get { return next >= order.Length; } }
    }

    /// <summary>
    /// v5.1 (W25, decision 13 navigation): the settings window's menu. Main menu = 准星 (a group: clicking it opens its
    /// sub-menu and shows its first page) and 滤镜; below a gap the small links (首页 指南 反馈 + pages others add).
    /// Items reference pages by id (UiPages.Ids). Pure, so tests-v4 checks the order, the visible list and ↑ / ↓.
    /// </summary>
    internal sealed class NavModel
    {
        public sealed class Item
        {
            public string Id;       // page id; a group's id is "group:<name>"
            public string Title;
            public bool Group;      // 准星: opens / closes its children
            public string Parent;   // group id of a sub-menu page, else null
            public bool Link;       // small link below the main menu
        }

        public const string CrosshairGroup = "group:crosshair";
        public readonly List<Item> Items = new List<Item>();
        public bool Expanded = true;

        /// <summary>The v5.1 menu: 准星 ▸ (外观 移动 侧身 换手 跳跃 后坐力 按键 常规), 滤镜; links 首页 指南 反馈.</summary>
        public static NavModel Default()
        {
            NavModel m = new NavModel();
            m.Add(CrosshairGroup, "准星", true, null, false);
            foreach (string id in SubPages) m.Add(id, null, false, CrosshairGroup, false);
            m.Add("filter", null, false, null, false);
            foreach (string id in Links) m.Add(id, null, false, null, true);
            return m;
        }

        /// <summary>Sub-menu of 准星, in this order (Leo, decision 13).</summary>
        public static readonly string[] SubPages = { "style", "movement", "lean", "hand", "jump", "recoil", "keys", "general" };
        public static readonly string[] Links = { "dashboard", "guide", "feedback" };

        public Item Add(string id, string title, bool group, string parent, bool link)
        {
            Item it = new Item();
            it.Id = id; it.Title = title; it.Group = group; it.Parent = parent; it.Link = link;
            Items.Add(it);
            return it;
        }

        /// <summary>Adds a link (e.g. W26's 常见问题) before 反馈, or at the end.</summary>
        public void AddLink(string id, string title)
        {
            int at = Items.FindIndex(x => x.Link && x.Id == "feedback");
            Item it = new Item();
            it.Id = id; it.Title = title; it.Link = true;
            if (at < 0) Items.Add(it); else Items.Insert(at, it);
        }

        public bool IsVisible(Item it) { return it.Parent == null || Expanded; }

        public List<Item> Visible()
        {
            List<Item> r = new List<Item>();
            foreach (Item it in Items) if (IsVisible(it)) r.Add(it);
            return r;
        }

        /// <summary>The group's first child (what clicking 准星 shows).</summary>
        public string FirstChild(string group)
        {
            foreach (Item it in Items) if (it.Parent == group) return it.Id;
            return null;
        }

        /// <summary>Index in Items of the item for a page id (-1 = not in the menu).</summary>
        public int IndexOfPage(string id)
        {
            for (int i = 0; i < Items.Count; i++) if (!Items[i].Group && Items[i].Id == id) return i;
            return -1;
        }

        /// <summary>↑ (-1) / ↓ (+1) from item `from` over the visible items, wrapping round.</summary>
        public int Step(int from, int d)
        {
            List<int> vis = new List<int>();
            for (int i = 0; i < Items.Count; i++) if (IsVisible(Items[i])) vis.Add(i);
            if (vis.Count == 0) return -1;
            int at = vis.IndexOf(from);
            if (at < 0) return vis[0];
            int n = vis.Count;
            return vis[((at + d) % n + n) % n];
        }
    }

    /// <summary>
    /// v5.1.1 (X2, Leo F6 「别的也都卡」): the settings window's code is compiled to machine code on a low-priority background
    /// thread right after start, so the UI thread does not stop to compile when it builds the window and each page (on
    /// .NET Framework every method is compiled the first time it runs, in every process). Order: the settings window and its
    /// pages first, the rest after. Finite: one pass, then the thread ends. Nothing runs: methods are only compiled.
    /// </summary>
    internal static class PreJit
    {
        /// <summary>Methods compiled / failed and ms of the last run (tests, crash.log).</summary>
        public static int Compiled, Failed;
        public static long Ms = -1;
        private static int started;

        /// <summary>Starts the one background pass (only the first call does anything). log gets one line at the end.</summary>
        public static void Start(System.Reflection.Assembly asm, bool skipTutorials, Action<string> log)
        {
            if (asm == null || Interlocked.Exchange(ref started, 1) != 0) return;
            Thread t = new Thread(delegate() { Run(asm, skipTutorials, log); });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Name = "PreJit";
            t.Start();
        }

        /// <summary>Lower = earlier. The settings window shell and the pages a user opens first come first.</summary>
        public static int Rank(string typeName)
        {
            string n = typeName ?? "";
            int plus = n.IndexOf('+');
            if (plus >= 0) n = n.Substring(0, plus); // nested (lambda) classes go with their owner
            int dot = n.LastIndexOf('.');
            if (dot >= 0) n = n.Substring(dot + 1);
            if (n.StartsWith("<")) return 9; // compiler-generated at namespace level
            if (n == "SettingsForm" || n == "Theme" || n == "TextFit") return 0;
            if (n.StartsWith("Dashboard") || n == "StatusLine" || n == "HelpText" || n == "StylePanel" || n == "NavModel") return 1;
            if (n.StartsWith("Filter") && !n.StartsWith("FilterTutorial")) return 2;
            if (n == "CurveEditor" || n.StartsWith("Wizard") || n == "SetupWizard" || n.StartsWith("Faq") || n == "MustRead") return 2;
            if (n.StartsWith("Tutorial") || n.StartsWith("FilterTutorial")) return 4;
            return 3;
        }

        public static bool IsTutorial(string typeName)
        {
            string n = typeName ?? "";
            int dot = n.LastIndexOf('.');
            if (dot >= 0) n = n.Substring(dot + 1);
            return n.StartsWith("Tutorial") || n.StartsWith("FilterTutorial");
        }

        /// <summary>The pass itself (public for tests: it may be called directly, on any thread).</summary>
        public static void Run(System.Reflection.Assembly asm, bool skipTutorials, Action<string> log)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            int ok = 0, bad = 0;
            try
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types ?? new Type[0]; }
                List<Type> list = new List<Type>();
                foreach (Type t in types)
                {
                    if (t == null || t.ContainsGenericParameters || t.IsInterface) continue;
                    if (skipTutorials && IsTutorial(t.FullName)) continue;
                    list.Add(t);
                }
                list.Sort(delegate(Type a, Type b)
                {
                    int r = Rank(a.FullName).CompareTo(Rank(b.FullName));
                    return r != 0 ? r : string.CompareOrdinal(a.FullName, b.FullName);
                });
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                foreach (Type t in list)
                {
                    List<System.Reflection.MethodBase> ms = new List<System.Reflection.MethodBase>();
                    try { ms.AddRange(t.GetConstructors(all)); ms.AddRange(t.GetMethods(all)); } catch (Exception) { bad++; continue; }
                    foreach (System.Reflection.MethodBase m in ms)
                    {
                        if (m.IsAbstract || m.ContainsGenericParameters) continue;
                        if ((m.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0) continue;
                        System.Reflection.MethodImplAttributes f = m.GetMethodImplementationFlags();
                        if ((f & (System.Reflection.MethodImplAttributes.InternalCall | System.Reflection.MethodImplAttributes.Runtime
                            | System.Reflection.MethodImplAttributes.Native)) != 0) continue;
                        try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); ok++; }
                        catch (Exception) { bad++; }
                    }
                }
            }
            catch (Exception ex) { if (log != null) log("PreJit: " + ex.Message); }
            Compiled = ok; Failed = bad; Ms = sw.ElapsedMilliseconds;
            if (log != null) log("启动耗时 后台预编译设置窗口代码: " + ok + " 个方法，" + sw.ElapsedMilliseconds + " ms（后台线程，界面不等它）" + (bad > 0 ? "，跳过 " + bad : ""));
        }
    }
}
