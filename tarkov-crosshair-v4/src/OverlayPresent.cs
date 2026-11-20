using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TarkovCrosshair
{
    /// <summary>
    /// v4.3 PERF (W7): pure helpers of the overlay render thread (compiled into the tests too; no WinForms, no Win32).
    /// </summary>
    internal sealed class PresentGate
    {
        private bool have;
        private int lastX, lastY;
        private byte lastSca;

        /// <summary>True when UpdateLayeredWindow must run this frame: position, opacity (colour/alpha) or image changed,
        /// or nothing was presented yet / the last present failed (Invalidate). An unchanged frame keeps the old image
        /// on screen (UpdateLayeredWindow keeps the last bitmap), so skipping it changes nothing visible.</summary>
        public bool Need(int x, int y, byte sca, bool imageChanged)
        {
            if (have && !imageChanged && x == lastX && y == lastY && sca == lastSca) return false;
            have = true; lastX = x; lastY = y; lastSca = sca;
            return true;
        }

        /// <summary>v5.0.1 (W14): something was presented since the last Invalidate (LastX / LastY are valid).</summary>
        public bool Have { get { return have; } }
        public int LastX { get { return lastX; } }
        public int LastY { get { return lastY; } }
        public byte LastSca { get { return lastSca; } }

        /// <summary>Forget the last present: the next frame presents (new DIB, failed present, window recreated).</summary>
        public void Invalidate() { have = false; }
    }

    /// <summary>
    /// v4.3 (W7, Leo: the crosshair lags after every alt-tab): render errors used to be counted for the whole session and
    /// the thread stopped at 100, so short failures around each alt-tab (UpdateLayeredWindow refused during a display-mode
    /// switch: UNVERIFIED which call fails on Leo's PC) added up and every failed frame also slept 10 ms. Now only errors
    /// without 1 s of clean frames in between count; 100 of those = Fatal, like before for a real permanent failure.
    /// </summary>
    internal sealed class RenderErrorBudget
    {
        public const int Limit = 100;
        private int errors;
        private long cleanSince = -1;
        private readonly long cleanTicks;

        public RenderErrorBudget(long ticksPerSecond) { cleanTicks = Math.Max(1, ticksPerSecond); }

        public int Errors { get { return errors; } }

        /// <summary>A frame without error at time 'now' (ticks).</summary>
        public void Ok(long now)
        {
            if (errors == 0) return;
            if (cleanSince < 0) cleanSince = now;
            else if (now - cleanSince >= cleanTicks) { errors = 0; cleanSince = -1; }
        }

        /// <summary>A failed frame. True = give up (Fatal).</summary>
        public bool Failed()
        {
            cleanSince = -1;
            errors++;
            return errors >= Limit;
        }
    }

    /// <summary>
    /// v4.3 (W7, Leo: after alt-tab back the crosshair starts a few seconds late). The render thread's foreground check,
    /// once per frame: GetForegroundWindow only (cheap). The process test and the title are read only when the foreground
    /// window CHANGES (and the title of the same foreign window again once a second, titles can change); the Tarkov HWND
    /// is therefore cached and re-recognised in the same frame it comes back. The title of OUR window is never read:
    /// GetWindowText on one of our own windows SENDS WM_GETTEXT to the UI thread and waits for it, so any UI-thread stall
    /// (display change, filter re-apply, 高级设置 relayout) froze the crosshair (was polled at 10 Hz). No process
    /// enumeration. Pure: the Win32 reads come in as delegates.
    /// </summary>
    internal sealed class ForegroundTracker
    {
        private IntPtr hwnd;
        private bool have;
        private int ours = -1;          // 1 ours, 0 another process, -1 none
        private bool match;
        private string title, pattern;
        private long titleAt;
        /// <summary>Diagnostics: process / title reads so far.</summary>
        public int OwnerReads, TitleReads;

        public int Ours { get { return ours; } }
        /// <summary>Title of the foreground window when it belongs to another process, else null.</summary>
        public string Title { get { return title; } }
        public bool Match { get { return match; } }

        /// <summary>Returns true when the foreground title contains 'titleContains' (another process only).</summary>
        /// <summary>v5.0.1 (W14): the last Update saw a different foreground window (or the first one).</summary>
        public bool Changed;

        public bool Update(IntPtr foreground, long now, long ticksPerSecond, Func<IntPtr, int> oursOf, Func<IntPtr, string> titleOf, string titleContains)
        {
            bool changed = !have || foreground != hwnd || !string.Equals(pattern, titleContains, StringComparison.Ordinal);
            Changed = changed;
            if (changed)
            {
                have = true;
                hwnd = foreground;
                pattern = titleContains;
                OwnerReads++;
                ours = foreground == IntPtr.Zero ? -1 : oursOf(foreground);
                ReadTitle(now, titleOf);
            }
            else if (ours == 0 && now - titleAt >= ticksPerSecond) ReadTitle(now, titleOf);
            return match;
        }

        private void ReadTitle(long now, Func<IntPtr, string> titleOf)
        {
            titleAt = now;
            if (ours != 0) { title = null; match = false; return; }
            TitleReads++;
            title = titleOf(hwnd);
            match = GameWindow.TitleMatches(title, pattern);
        }

        /// <summary>只在塔科夫前台时显示.</summary>
        public bool GameFocused(bool onlyWhenGameFocused) { return !onlyWhenGameFocused || match; }
    }

    /// <summary>
    /// v4.3 (W7): the render thread never waits for the log file. Program.Log takes a lock and appends to crash.log; the
    /// UI thread writes there too (filter: … lines), so a render-thread error line could wait for it. Lines go to the
    /// thread pool instead (order between lines from different threads is not kept; each line keeps its own time stamp
    /// from the sink).
    /// </summary>
    internal static class NonBlockingLog
    {
        public static Action<string> Wrap(Action<string> sink)
        {
            if (sink == null) return null;
            return delegate(string line)
            {
                try { ThreadPool.QueueUserWorkItem(delegate { try { sink(line); } catch (Exception) { } }); }
                catch (Exception) { }
            };
        }
    }

    /// <summary>What the render loop does with a frame, from its pixel level (v5.0.1 W14, v5.0.2 W16).</summary>
    internal enum FrameAction { Draw = 0, PresentHidden = 1, Skip = 2 }

    /// <summary>
    /// v5.0.2 (W16, Leo 18 Nov 2026: 开镜 = the crosshair COMPLETELY invisible, without changing the window). The window's
    /// SourceConstantAlpha is always WindowSca (255); opacity and fades live in the pixels (FadePixels). At level 0 (开镜 /
    /// 自由视角 / faded out) the first frame pushes ONE UpdateLayeredWindow of a same-size bitmap whose every pixel is 0, at
    /// the last position, with the same constant alpha (PresentHidden); after that nothing is drawn or presented until the
    /// level is above 0 again (Skip). No ShowWindow, no style, size or z-order change for 开镜. A refused push is retried
    /// next frame. (v5.0.1 pushed the old bitmap with constant alpha 0.)
    /// </summary>
    internal sealed class FrameVisibility
    {
        /// <summary>The window's SourceConstantAlpha, in every present (never changes).</summary>
        public const byte WindowSca = 255;
        private bool hiddenDone;
        /// <summary>Diagnostics: hidden presents asked for so far.</summary>
        public int HiddenPresents;

        public bool Hidden { get { return hiddenDone; } }

        /// <summary>level = 255 x opacity x fade (0 = invisible).</summary>
        public FrameAction Next(byte level)
        {
            if (level != 0) { hiddenDone = false; return FrameAction.Draw; }
            if (hiddenDone) return FrameAction.Skip;
            hiddenDone = true;
            HiddenPresents++;
            return FrameAction.PresentHidden;
        }

        /// <summary>The hidden present was refused: ask again next frame.</summary>
        public void HiddenPresentFailed() { hiddenDone = false; }

        /// <summary>Window (re)created: the next opacity-0 frame presents the hidden state again.</summary>
        public void Reset() { hiddenDone = false; }

        /// <summary>The pixel level of a frame: 255 x 不透明度 % x fade alpha, rounded, 0..255.</summary>
        public static byte Level(double opacityPct, double alpha)
        {
            double v = 255.0 * (opacityPct / 100.0) * alpha;
            if (!(v > 0)) return 0; // also NaN
            if (v >= 255) return 255;
            int n = (int)Math.Round(v);
            return (byte)(n < 0 ? 0 : (n > 255 ? 255 : n));
        }
    }

    /// <summary>
    /// v5.0.2 (W16): opacity and fades applied to the premultiplied pixels (what SourceConstantAlpha did before): every
    /// channel x level / 255, rounded. Level 255 = a plain copy, level 0 = all pixels 0 (fully transparent). No allocation.
    /// </summary>
    internal static class FadePixels
    {
        public static void Scale(int[] src, int[] dst, int n, byte level)
        {
            if (level == 255) { Array.Copy(src, dst, n); return; }
            if (level == 0) { Array.Clear(dst, 0, n); return; }
            uint k = level;
            for (int i = 0; i < n; i++)
            {
                uint p = (uint)src[i];
                if (p == 0) { dst[i] = 0; continue; }
                uint rb = (p & 0x00FF00FFu) * k + 0x00800080u;   // R and B (and A, G below) x level, + 128 (rounding)
                uint ag = ((p >> 8) & 0x00FF00FFu) * k + 0x00800080u;
                rb = ((rb + ((rb >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu; // = round(c x level / 255), exact for every c, level
                ag = ((ag + ((ag >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu;
                dst[i] = (int)(rb | (ag << 8));
            }
        }
    }

    /// <summary>What the render loop does with the window itself this frame (v5.0.2 W16).</summary>
    internal enum WindowOp { None = 0, Show = 1, Hide = 2 }

    /// <summary>
    /// v5.0.2 (W16, Leo decision 1): the window is really hidden (SW_HIDE) only for the long states: the crosshair is off
    /// (开关键 / tray) or the game is not in front (只在塔科夫前台时显示 on, and not our own window either). 开镜 / 自由视角
    /// never hide it (FrameVisibility). Hide happens once the all-zero frame was pushed; Show (SW_SHOWNOACTIVATE + one
    /// TOPMOST) as soon as the long state ends, before that frame draws. While hidden nothing is drawn or presented.
    /// </summary>
    internal sealed class WindowShowPolicy
    {
        private bool shown = true;
        public int Shows, Hides;

        public bool Shown { get { return shown; } }

        /// <summary>CreateWindow showed it.</summary>
        public void Created() { shown = true; }

        public static bool LongHidden(bool enabled, bool onlyWhenGameFocused, bool gameFocused, bool ownForeground)
        {
            return !enabled || (onlyWhenGameFocused && !gameFocused && !ownForeground);
        }

        /// <summary>Before drawing: Show when the long state ended.</summary>
        public WindowOp BeforeDraw(bool longHidden)
        {
            if (!longHidden && !shown) { shown = true; Shows++; return WindowOp.Show; }
            return WindowOp.None;
        }

        /// <summary>After drawing: Hide once the long state's frame is fully transparent (blankDone).</summary>
        public WindowOp AfterDraw(bool longHidden, bool blankDone)
        {
            if (longHidden && shown && blankDone) { shown = false; Hides++; return WindowOp.Hide; }
            return WindowOp.None;
        }

        /// <summary>A failed ShowWindow (rare): ask again next frame.</summary>
        public void ShowFailed() { shown = false; }
    }

    /// <summary>
    /// v5.0.2 (W16, AUDIT F18): each virtual key is read once per frame (GetAsyncKeyState was called 18 times per frame,
    /// Shift / Ctrl / Alt up to 3 times). NextFrame forgets the cache; no allocation.
    /// </summary>
    internal sealed class KeySnapshot
    {
        private readonly Func<int, bool> read;
        private readonly int[] gen = new int[256];
        private readonly bool[] down = new bool[256];
        private int cur = 1;
        public int Reads;
        public readonly Func<int, bool> IsDown;

        public KeySnapshot(Func<int, bool> read) { this.read = read; IsDown = Down; }

        public void NextFrame() { cur++; if (cur == int.MaxValue) { Array.Clear(gen, 0, gen.Length); cur = 1; } }

        private bool Down(int vk)
        {
            if (vk <= 0 || vk > 255) return false;
            if (gen[vk] == cur) return down[vk];
            gen[vk] = cur; Reads++;
            bool d = read(vk);
            down[vk] = d;
            return d;
        }
    }

    /// <summary>
    /// v5.0.1 (W14): when the overlay re-asserts HWND_TOPMOST. Was: SetWindowPos(HWND_TOPMOST) every second for the whole
    /// session, also over a fullscreen game (each call is a z-order change Windows has to re-evaluate; UNVERIFIED that it is
    /// a cause of the stutter). Now: never on a frame by itself; only after the foreground window changed, at most once a
    /// second (a change inside the second is kept pending and done when the second is over). Creation counts as the first.
    /// </summary>
    internal sealed class TopmostPolicy
    {
        private bool pending, have;
        private long lastAt;
        /// <summary>Diagnostics: SetWindowPos calls asked for so far.</summary>
        public int Calls;

        public void Created(long now) { have = true; lastAt = now; pending = false; probeAt = now; }

        /// <summary>v5.0.2 (W16, AUDIT F15): seconds between checks for another program's topmost window above ours.</summary>
        public const int ProbeSeconds = 10;
        private long probeAt;

        /// <summary>True every ProbeSeconds: look at the window above ours (cheap, no z-order change).</summary>
        public bool ProbeDue(long now, long ticksPerSecond)
        {
            if (now >= probeAt && now - probeAt < ProbeSeconds * ticksPerSecond) return false;
            probeAt = now;
            return true;
        }

        public bool Need(bool foregroundChanged, long now, long ticksPerSecond)
        {
            if (foregroundChanged) pending = true;
            if (!pending) return false;
            if (have && now >= lastAt && now - lastAt < ticksPerSecond) return false;
            pending = false; have = true; lastAt = now; Calls++;
            return true;
        }
    }

    /// <summary>
    /// v5.0.1 (W14, Leo: on some PCs the crosshair does not move at all). Frame pacing without DwmFlush. v4.3 (W7) paced the
    /// loop with DwmFlush and, at the same time, stopped presenting unchanged frames (PresentGate). DwmFlush waits for the
    /// next DWM composition; Windows 8+ composes only when something on screen changed, so with a still crosshair over a
    /// game that DWM does not compose (fullscreen / independent flip) DwmFlush could wait a long time and the loop stopped
    /// reading the keys (UNVERIFIED on the affected PCs; before v4.3 every frame presented, so DwmFlush always returned).
    /// Now: a high-resolution waitable timer (primary, injected; its wait has a timeout so it cannot hang) at the monitor
    /// refresh rate; after 3 failed waits in a row, or 30 waits in a row that return far too early, the pacer falls back to
    /// Sleep(1) for good. Every Wait returns within about one period + 50 ms whatever the primary does.
    /// </summary>
    internal sealed class FramePacer
    {
        /// <summary>Wait up to 'ticks' (clock ticks). false = the wait failed.</summary>
        public delegate bool PrimaryWait(long ticks);

        private readonly Func<long> now;
        private readonly long freq;
        private readonly Action sleep1;
        private PrimaryWait primary;
        private long next;
        private bool started;
        private int fails, fast;
        public int PrimaryFailures;
        public string LastError;
        /// <summary>Set once when the pacer gave up on the primary wait (the loop logs it).</summary>
        public bool FellBack;

        public FramePacer(Func<long> now, long ticksPerSecond, PrimaryWait primary, Action sleep1)
        {
            this.now = now; freq = Math.Max(1, ticksPerSecond); this.primary = primary; this.sleep1 = sleep1;
        }

        public bool UsingPrimary { get { return primary != null; } }

        public static int ClampHz(int hz) { return hz < 30 ? 30 : (hz > 500 ? 500 : hz); }

        // v5.0.2 (W16, AUDIT F3): phase lock to the display's vblank without blocking (no DwmFlush). anchor = a vblank time
        // and period = the exact refresh period, both in this pacer's clock ticks (from DwmGetCompositionTimingInfo on the
        // worker). Used only while the period is within 5 % of the monitor rate asked for; each wait then ends LeadMs
        // before a predicted vblank. Otherwise the free-running timer as before.
        private long vAnchor;
        private double vPeriod;
        public const double LeadMs = 1.5;
        /// <summary>Diagnostics: waits snapped to a vblank.</summary>
        public int Locked;

        public void SetVBlank(long anchorTicks, double periodTicks)
        {
            if (!(periodTicks > 0) || double.IsInfinity(periodTicks)) { vPeriod = 0; return; }
            vAnchor = anchorTicks; vPeriod = periodTicks;
        }

        public void ClearVBlank() { vPeriod = 0; }

        public bool PhaseLocked(int hz)
        {
            double p = freq / (double)ClampHz(hz);
            return vPeriod > 0 && Math.Abs(vPeriod - p) <= 0.05 * p;
        }

        public void Wait(int hz)
        {
            long period;
            long next = Plan(hz, out period);
            long remain = next - now();
            if (remain <= 0) return;
            PrimaryWait p = primary;
            if (p != null)
            {
                bool ok;
                try { ok = p(remain); }
                catch (Exception ex) { ok = false; LastError = ex.GetType().Name + ": " + ex.Message; }
                if (!ok)
                {
                    PrimaryFailures++;
                    if (++fails >= 3) { primary = null; FellBack = true; }
                }
                else
                {
                    fails = 0;
                    long left = next - now();
                    if (left > period / 2) { if (++fast >= 30) { primary = null; FellBack = true; } }
                    else { fast = 0; return; }
                }
            }
            int guard = 0;
            while (now() < next && guard++ < 1000) sleep1();
        }

        /// <summary>v5.0.2 (W16, Leo: minimum latency): the next frame deadline (vblank-locked when known), in clock ticks.
        /// The render loop waits for it in slices of at most 1 ms (Slice) and reads the keys between slices.</summary>
        public long Plan(int hz) { long period; return Plan(hz, out period); }

        /// <summary>v5.0.2 (W16): waits until 'deadline' or for at most maxTicks, whichever comes first (one timer wait, or
        /// one Sleep(1) on the fallback). true = the deadline has been reached. Same failure / too-early rules as Wait.</summary>
        public bool Slice(long deadline, long maxTicks)
        {
            long t = now();
            long remain = deadline - t;
            if (remain <= 0) return true;
            long w = remain < maxTicks ? remain : maxTicks;
            if (w < 1) w = 1;
            PrimaryWait p = primary;
            if (p != null)
            {
                bool ok;
                try { ok = p(w); }
                catch (Exception ex) { ok = false; LastError = ex.GetType().Name + ": " + ex.Message; }
                if (!ok)
                {
                    PrimaryFailures++;
                    if (++fails >= 3) { primary = null; FellBack = true; }
                    sleep1();
                }
                else
                {
                    fails = 0;
                    long waited = now() - t;
                    if (waited < w / 4 && w > freq / 4000) { if (++fast >= 30) { primary = null; FellBack = true; } }
                    else fast = 0;
                }
            }
            else sleep1();
            return now() >= deadline;
        }

        private long Plan(int hz, out long period)
        {
            period = freq / ClampHz(hz);
            bool lockv = PhaseLocked(hz);
            if (lockv) period = (long)Math.Round(vPeriod);
            if (period < 1) period = 1;
            long t = now();
            if (!started) { started = true; next = t; }
            next += period;
            if (t - next > period || next - t > 2 * period) next = t + period; // fell behind / clock jump: resync, no burst
            if (lockv)
            {
                long lead = (long)(LeadMs * freq / 1000.0);
                double k = Math.Round((next + lead - vAnchor) / vPeriod);
                long snapped = vAnchor + (long)Math.Round(k * vPeriod) - lead;
                if (snapped <= t) snapped += (long)Math.Round(vPeriod);
                next = snapped;
                Locked++;
            }
            return next;
        }

    }

    /// <summary>
    /// v5.0.2 (W16, Leo: competitive, minimum key-press -> pixel latency). Between two refresh-paced frames the render
    /// thread wakes every PollMs (1 ms, high-resolution timer), reads the bound keys (KeySnapshot, about 14 GetAsyncKeyState
    /// calls) and, when the key state changed, runs a frame at once (motion at the time of that read, then draw + push)
    /// instead of waiting for the next refresh tick. No queue, no extra smoothing. Pure: the key poll comes in as a delegate.
    /// While the keys are not live (game not in front) it does not poll: it just waits for the next frame.
    /// </summary>
    internal sealed class InputWake
    {
        public const double PollMs = 1.0;
        /// <summary>Diagnostics: frames started by a key change / by the refresh deadline; key polls.</summary>
        public int InputFrames, DeadlineFrames, Polls;
        private long pending;
        private bool havePending;

        /// <summary>Waits for the next frame. pollChanged = read the keys now, true when they differ from the last frame's.
        /// Returns true when a key change ended the wait (an input frame), false at the refresh deadline.</summary>
        public bool WaitNext(FramePacer pacer, int hz, long ticksPerSecond, Func<bool> pollChanged)
        {
            long deadline;
            if (havePending) { deadline = pending; havePending = false; } // an input frame ran: the refresh frame stays planned
            else deadline = pacer.Plan(hz);
            long slice = Math.Max(1, (long)(PollMs * ticksPerSecond / 1000.0));
            int guard = 0;
            while (guard++ < 100000)
            {
                if (pacer.Slice(deadline, pollChanged == null ? long.MaxValue : slice)) { DeadlineFrames++; return false; }
                if (pollChanged != null)
                {
                    Polls++;
                    if (pollChanged()) { InputFrames++; pending = deadline; havePending = true; return true; }
                }
            }
            DeadlineFrames++;
            return false;
        }
    }

    /// <summary>v5.0.1 (W14): per-phase timing of the render loop for the crash.log heartbeat (no allocation per frame).</summary>
    internal sealed class PhaseStats
    {
        public const int Pump = 0, Input = 1, Draw = 2, Wait = 3, Count = 4;
        public static readonly string[] Names = { "pump", "input+motion", "draw+present", "wait" };
        private readonly long[] sum = new long[Count];
        private readonly long[] max = new long[Count];
        private long frames;

        public long Frames { get { return frames; } }

        public void Add(int phase, long ticks)
        {
            if (ticks < 0) ticks = 0;
            sum[phase] += ticks;
            if (ticks > max[phase]) max[phase] = ticks;
        }

        public void Frame() { frames++; }

        /// <summary>"frames=N, pump 0.01/0.20 ms, ..." (average / max per frame) and reset.</summary>
        public string TakeLine(long ticksPerSecond)
        {
            StringBuilder b = new StringBuilder(160);
            b.Append("frames=").Append(frames);
            double k = 1000.0 / Math.Max(1, ticksPerSecond);
            for (int i = 0; i < Count; i++)
            {
                double avg = frames > 0 ? sum[i] * k / frames : 0;
                b.Append(", ").Append(Names[i]).Append(' ').Append(avg.ToString("0.000")).Append('/').Append((max[i] * k).ToString("0.00")).Append(" ms");
                sum[i] = 0; max[i] = 0;
            }
            frames = 0;
            return b.ToString();
        }
    }

    /// <summary>v5.0.1 (W14): heartbeat times: 5 s after start, then every 60 s for 5 minutes, then every 10 minutes.</summary>
    internal static class HeartbeatSchedule
    {
        public static long NextAfter(long started, long now, long ticksPerSecond)
        {
            long age = now - started;
            if (age < 5 * ticksPerSecond) return started + 5 * ticksPerSecond;
            if (age < 300 * ticksPerSecond) return now + 60 * ticksPerSecond;
            return now + 600 * ticksPerSecond;
        }
    }

    /// <summary>
    /// v5.0.1 (W14): stall detector. The render thread writes the time of its last finished frame and the phase it is in
    /// (Volatile, no lock); the overlay worker checks it about 5 times a second and logs once when no frame came for 2 s
    /// (naming the phase), and once when frames come again.
    /// </summary>
    internal sealed class StallWatch
    {
        private long lastFrame;
        private int phase;
        private bool reported;
        private long stallStart;

        public void Enter(int p) { Volatile.Write(ref phase, p); }
        public void Frame(long now) { Volatile.Write(ref lastFrame, now); }
        public bool Reported { get { return reported; } }

        public string Check(long now, long ticksPerSecond)
        {
            long lf = Volatile.Read(ref lastFrame);
            if (lf == 0) return null;
            long gap = now - lf;
            if (!reported && gap >= 2 * ticksPerSecond)
            {
                reported = true; stallStart = lf;
                int p = Volatile.Read(ref phase);
                string name = p >= 0 && p < PhaseStats.Count ? PhaseStats.Names[p] : "start";
                return "overlay: no frame for " + (gap / (double)ticksPerSecond).ToString("0.0") + " s, render thread in phase " + name;
            }
            if (reported && gap < ticksPerSecond / 2)
            {
                reported = false;
                return "overlay: frames again after a stall of " + ((lf - stallStart) / (double)ticksPerSecond).ToString("0.0") + " s";
            }
            return null;
        }
    }

    /// <summary>
    /// v5.0.1 (W14): the render thread's only way out. Log lines and callbacks (toggle -> UI, fatal -> UI) go into lock-free
    /// queues (ConcurrentQueue) plus an event Set; a background worker thread writes them to the sink (Program.Log, which
    /// takes a lock and appends to crash.log) and runs the callbacks in order. The render thread therefore never waits for
    /// the file, the log lock, the UI thread or the thread pool. Tick runs on the worker about 5 times a second (refresh-rate
    /// read, stall check). At most MaxLines lines wait at once (more are counted in Dropped).
    /// </summary>
    internal sealed class OverlayWorker
    {
        public const int MaxLines = 500;
        private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private int queued;
        private int dropped;
        private volatile bool stop;
        private Thread thread;
        public volatile Action<string> Sink;
        public volatile Action Tick;

        public int Dropped { get { return Volatile.Read(ref dropped); } }
        public int Queued { get { return Volatile.Read(ref queued); } }

        public void Start(string name)
        {
            stop = false;
            Thread t = new Thread(Loop);
            t.Name = name;
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            thread = t;
            t.Start();
        }

        public void Log(string line)
        {
            if (line == null) return;
            if (Interlocked.Increment(ref queued) > MaxLines) { Interlocked.Decrement(ref queued); Interlocked.Increment(ref dropped); return; }
            lines.Enqueue(line);
            try { wake.Set(); } catch (Exception) { }
        }

        public void Post(Action a)
        {
            if (a == null) return;
            actions.Enqueue(a);
            try { wake.Set(); } catch (Exception) { }
        }

        public void Stop(int waitMs)
        {
            stop = true;
            try { wake.Set(); } catch (Exception) { }
            Thread t = thread;
            if (t != null && t.IsAlive) t.Join(waitMs);
            thread = null;
        }

        private void Loop()
        {
            while (true)
            {
                try { wake.WaitOne(200); } catch (Exception) { Thread.Sleep(200); }
                Drain();
                Action tick = Tick;
                if (tick != null) { try { tick(); } catch (Exception) { } }
                if (stop) { Drain(); return; }
            }
        }

        /// <summary>Runs the queued callbacks, then writes the queued lines (worker thread; tests call it directly).</summary>
        public void Drain()
        {
            Action a;
            while (actions.TryDequeue(out a)) { try { a(); } catch (Exception) { } }
            string s;
            while (lines.TryDequeue(out s))
            {
                Interlocked.Decrement(ref queued);
                Action<string> sink = Sink;
                if (sink != null) { try { sink(s); } catch (Exception) { } }
            }
            int d = Interlocked.Exchange(ref dropped, 0);
            if (d > 0) { Action<string> sink = Sink; if (sink != null) { try { sink("overlay: " + d + " log lines dropped (queue full)"); } catch (Exception) { } } }
        }
    }
}


namespace TarkovCrosshair
{
    /// <summary>
    /// v5.1 (W22): the engine state the CS feel and the ammo hint read, taken from this frame's MotionEngine / input (pure).
    /// One place to wire W21's AmmoModel / MoveLevel / ShotSeq into (V51-W21-REQ.md); until they exist the fallbacks are used
    /// (ammo inactive, movement smoothed from CurrentDir, rounds counted from LMB x AmmoRpm).
    /// </summary>
    internal static class OverlayInputs
    {
        public static void Fill(MotionEngine eng, GlobalSettings g, InputState raw, bool keysLive, out AnimInput ai, out AmmoView av)
        {
            ai = new AnimInput();
            // W21 (V51-W21-REQ.md): MoveLevel follows the engine's own inertia curve; ShotSeq counts the rounds the ammo model
            // counts (the fire pulse and the ammo hint agree), Firing = LMB on the main weapon with the keys live.
            ai.MoveLevel = eng.MoveLevel;
            ai.Moving = eng.CurrentDir != -1;
            ai.Jumping = eng.Jumping;
            ai.Firing = eng.Firing;
            ai.ShotSeq = eng.AmmoShotSeq & 0x7FFFFFFF;
            ai.Rpm = g != null ? g.AmmoRpm : 600;
            ai.Semi = g != null && g.AmmoSemi;
            ai.Lean = eng.Lean;
            av = new AmmoView();
            av.Active = eng.AmmoActive;
            av.Band = eng.AmmoBand;
            av.ReloadSeq = eng.AmmoReloadSeq;
            av.ReloadKind = eng.AmmoReloadKind;
            av.ReloadMs = eng.AmmoReloadMs;
            av.Reloading = eng.Ammo.Reloading;
            av.Progress = eng.Ammo.ReloadProgress;
        }
    }
}
