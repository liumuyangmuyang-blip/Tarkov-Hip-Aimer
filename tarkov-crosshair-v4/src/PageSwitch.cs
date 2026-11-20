// v5.1.1 (X9, Leo 21 Nov: "每次点菜单 就卡一下"): what a switch to an already-built settings page still has to do.
// Pure logic (no WinForms), so the Linux tests can check that a repeat visit with nothing changed does no expensive step.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>The expensive steps of SettingsForm.ShowPage for one switch.</summary>
    internal struct PageSwitchPlan
    {
        /// <summary>Re-read this page's 百分比偏移 boxes (分辨率 / 画面比例 / FOV / 手 or a value changed since its last show).</summary>
        public bool Offsets;
        /// <summary>FitWrapping + Content / Scroller PerformLayout (size, text scale or content changed since its last fit).</summary>
        public bool Fit;
    }

    /// <summary>
    /// Per page: the settings version its offset boxes were last refreshed at, and the (width, height, text scale, version)
    /// it was last fitted at. A switch asks Plan(); the form marks what it did. Invalidate(page) / InvalidateAll() when a
    /// change does not go through the version (高级设置 applied lazily, a fold opened while the page was hidden).
    /// </summary>
    internal sealed class PageSwitchCache
    {
        private readonly Dictionary<int, long> offsetsAt = new Dictionary<int, long>();
        private readonly Dictionary<int, string> fitAt = new Dictionary<int, string>();

        private static string FitKey(int width, int height, float scale, long version)
        {
            return width + "x" + height + "@" + scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "#" + version;
        }

        public PageSwitchPlan Plan(int page, long version, int width, int height, float scale)
        {
            PageSwitchPlan p;
            long o;
            p.Offsets = !offsetsAt.TryGetValue(page, out o) || o != version;
            string f;
            // v5.1.1 (X11, Leo's Windows log: 'fit' 0.4-3 s on every switch): the text fit runs once per page per (client
            // width, text scale); a settings change or a height change no longer refits (InvalidateFit for lazy 高级设置)
            p.Fit = !fitAt.TryGetValue(page, out f) || f != FitKey(width, 0, scale, 0);
            return p;
        }

        public void MarkOffsets(int page, long version) { offsetsAt[page] = version; }

        public void MarkFit(int page, long version, int width, int height, float scale) { fitAt[page] = FitKey(width, 0, scale, 0); }

        public void Invalidate(int page) { offsetsAt.Remove(page); fitAt.Remove(page); }

        public void InvalidateFit(int page) { fitAt.Remove(page); }

        public void InvalidateAll() { offsetsAt.Clear(); fitAt.Clear(); }
    }

    /// <summary>Times the steps of one page switch; Report() is a crash.log line only when the whole switch took longer than
    /// the limit (default 30 ms), naming the page, the total and every step with its ms.</summary>
    internal sealed class SwitchTimer
    {
        public const long LimitMs = 30;
        private readonly Stopwatch total = Stopwatch.StartNew();
        private readonly Stopwatch step = Stopwatch.StartNew();
        private readonly List<KeyValuePair<string, long>> steps = new List<KeyValuePair<string, long>>();
        private readonly string page;

        public SwitchTimer(string page) { this.page = page; }

        /// <summary>Ends the step that ran since the last Step() (or the start).</summary>
        public void Step(string name)
        {
            steps.Add(new KeyValuePair<string, long>(name, step.ElapsedMilliseconds));
            step.Restart();
        }

        public long TotalMs { get { return total.ElapsedMilliseconds; } }

        public IList<KeyValuePair<string, long>> Steps { get { return steps; } }

        /// <summary>Null when the switch was within the limit.</summary>
        public string Report() { return Format(page, TotalMs, steps, LimitMs); }

        public static string Format(string page, long totalMs, IList<KeyValuePair<string, long>> steps, long limitMs)
        {
            if (totalMs <= limitMs) return null;
            StringBuilder sb = new StringBuilder();
            sb.Append("UI: 切换设置页 ").Append(page).Append(' ').Append(totalMs).Append(" ms (");
            string slow = null;
            long max = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(steps[i].Key).Append(' ').Append(steps[i].Value);
                if (steps[i].Value > max) { max = steps[i].Value; slow = steps[i].Key; }
            }
            sb.Append(")");
            if (slow != null) sb.Append(" 最慢: ").Append(slow);
            return sb.ToString();
        }
    }
}
