using System;
using System.Diagnostics;
using System.Threading;

// v5.0.1 (W13): startup timing + UI-thread watchdog, so Leo's next test on another PC shows in crash.log where the time goes.
//   启动耗时 <phase>: <ms since the process started> ms（上一步 <ms> ms）   one line per startup phase
//   界面卡住：<ms> ms 没有响应，当时在「<phase>」                       the UI thread did not answer a ping for > 3 s
//   界面恢复：卡了 <ms> ms，在「<phase>」                                 ... and when it answers again
// Pure (no WinForms): the ping goes through a post delegate (App: Control.BeginInvoke); tested with a fake clock.

namespace TarkovCrosshair
{
    internal static class StartupClock
    {
        private static readonly Stopwatch sw = Stopwatch.StartNew();
        private static readonly double offsetMs = ProcessAgeMs();
        private static readonly object sync = new object();
        private static string phase = "进程启动";
        private static long phaseAt, lastMark;
        private static bool done;
        /// <summary>Where the lines go (default Program.Log; tests replace it).</summary>
        public static Action<string> Sink = null;

        private static double ProcessAgeMs()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    double ms = (DateTime.Now - p.StartTime).TotalMilliseconds;
                    return ms > 0 && ms < 600000 ? ms : 0;
                }
            }
            catch (Exception) { return 0; }
        }

        /// <summary>Milliseconds since the process started (Windows' process start time + a monotonic clock).</summary>
        public static long Ms { get { return (long)(offsetMs + sw.Elapsed.TotalMilliseconds); } }

        public static string Phase { get { lock (sync) return phase; } }
        public static long PhaseAt { get { lock (sync) return phaseAt; } }
        public static bool StartupDone { get { lock (sync) return done; } }

        /// <summary>Startup phase reached: one 启动耗时 line, and the watchdog's current phase.</summary>
        public static void Mark(string name)
        {
            long now = Ms;
            string line;
            lock (sync)
            {
                line = "启动耗时 " + name + ": " + now + " ms（上一步 " + (now - lastMark) + " ms）";
                lastMark = now;
                phase = name;
                phaseAt = now;
            }
            Write(line);
        }

        /// <summary>Last startup line ("启动完成"); later phases are set with SetPhase (no log line).</summary>
        public static void Done(string name)
        {
            Mark(name);
            lock (sync) done = true;
        }

        /// <summary>Runtime UI work the watchdog should name if it stalls (no log line).</summary>
        public static void SetPhase(string name)
        {
            long now = Ms;
            lock (sync) { phase = name; phaseAt = now; }
        }

        internal static void Write(string line)
        {
            Action<string> s = Sink;
            try { if (s != null) s(line); else Program.Log(line); } catch (Exception) { }
        }
    }

    /// <summary>Pings the UI thread every IntervalMs; logs once when a ping is not answered within ThresholdMs, and once
    /// more when it is. Before the UI can be pinged (Post null) it watches the startup phase instead.</summary>
    internal sealed class UiWatchdog
    {
        public Action<Action> Post;
        public Func<long> Now = delegate { return StartupClock.Ms; };
        public Func<string> CurrentPhase = delegate { return StartupClock.Phase; };
        public Func<long> CurrentPhaseAt = delegate { return StartupClock.PhaseAt; };
        public Func<bool> StartupFinished = delegate { return StartupClock.StartupDone; };
        public Action<string> Log = StartupClock.Write;
        public int ThresholdMs = 3000, IntervalMs = 500;

        private readonly object sync = new object();
        private bool pending, reported, phaseReported;
        private long sentAt;
        private string stallPhase = "";
        private volatile bool stopped;
        private Thread thread;
        public int Reports;

        /// <summary>v5.0.2 (Leo: no wake-ups while the settings window is closed): set = pinging, reset = the thread waits.</summary>
        private readonly ManualResetEvent awake = new ManualResetEvent(true);
        private volatile bool idle;

        /// <summary>True while the watchdog sleeps (startup done, no settings window open).</summary>
        public bool Idle { get { return idle; } }

        /// <summary>v5.0.2: idle = true stops the pings (0 wake-ups) once startup is done; false starts them again (the
        /// settings window opened, so the UI thread has real work the watchdog should watch). Startup is always watched.</summary>
        public void SetIdle(bool on)
        {
            if (on && !StartupFinished()) return;
            idle = on;
            if (on)
            {
                lock (sync) { pending = false; reported = false; }
                awake.Reset();
            }
            else awake.Set();
        }

        public void Start()
        {
            Thread t = new Thread(delegate()
            {
                while (!stopped)
                {
                    awake.WaitOne();
                    if (stopped) return;
                    Thread.Sleep(IntervalMs);
                    if (idle) continue;
                    try { Tick(); } catch (Exception) { }
                }
            });
            t.IsBackground = true;
            t.Name = "UiWatchdog";
            thread = t;
            t.Start();
        }

        public void Stop() { stopped = true; try { awake.Set(); } catch (Exception) { } }

        /// <summary>One check (the thread calls it every IntervalMs; tests call it directly).</summary>
        public void Tick()
        {
            long now = Now();
            Action<Action> post = Post;
            if (post == null)
            {
                // the UI thread has no message loop yet: a startup phase that takes too long is the stall
                if (StartupFinished()) return;
                long at = CurrentPhaseAt();
                string ph = CurrentPhase();
                bool log = false;
                lock (sync)
                {
                    if (!phaseReported && now - at > ThresholdMs) { phaseReported = true; stallPhase = ph; log = true; }
                    else if (phaseReported && ph != stallPhase) phaseReported = false;
                }
                if (log) { Reports++; Log("界面卡住：启动阶段「" + ph + "」已经 " + (now - at) + " ms 还没完成"); }
                return;
            }
            bool send = false, stall = false;
            long waited = 0;
            string phase = null;
            lock (sync)
            {
                if (!pending) { pending = true; sentAt = now; send = true; }
                else if (!reported && now - sentAt > ThresholdMs)
                {
                    reported = true; stall = true; waited = now - sentAt;
                    stallPhase = CurrentPhase();
                    phase = stallPhase;
                }
            }
            if (stall)
            {
                Reports++;
                Log("界面卡住：" + waited + " ms 没有响应，当时在「" + phase + "」（这一步从启动后 " + CurrentPhaseAt() + " ms 开始）");
            }
            if (send)
            {
                try { post(Beat); }
                catch (Exception) { lock (sync) pending = false; }
            }
        }

        /// <summary>Runs on the UI thread: the ping was answered.</summary>
        private void Beat()
        {
            long now = Now();
            bool recovered; long took; string ph;
            lock (sync)
            {
                recovered = reported;
                took = now - sentAt;
                ph = stallPhase;
                pending = false;
                reported = false;
            }
            if (recovered) Log("界面恢复：卡了 " + took + " ms，在「" + ph + "」");
        }
    }
}
