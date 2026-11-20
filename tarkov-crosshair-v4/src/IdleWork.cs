// v5.1.1 (X11, Leo's Windows log 21 Nov: pages took 1-7 s to build on the UI thread = 5 s freezes while "空闲"):
// a queue of small UI-thread jobs run in time-boxed slices (SettingsForm.QueueIdle). Pure (no WinForms), so the Linux
// tests check the budget, the order and the per-page flush.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TarkovCrosshair
{
    /// <summary>
    /// Jobs are Func&lt;bool&gt;: true = "call me again" (one small step of a bigger build), false = done. RunSlice runs jobs
    /// in order until the budget is used up; a job is never interrupted, so each step must itself be short (a few ms).
    /// Flush(owner) runs every pending job of one owner (a page id) at once: the page is about to be shown.
    /// </summary>
    internal sealed class IdleWork
    {
        private sealed class Job { public string Owner; public Func<bool> Step; public string Name; }
        private readonly LinkedList<Job> jobs = new LinkedList<Job>();

        /// <summary>Longest RunSlice so far (ms) and the job that ran last in it (probe / crash.log).</summary>
        public long MaxSliceMs;
        public string MaxSliceJob;
        /// <summary>Errors are reported here (crash.log) and the job is dropped.</summary>
        public Action<string, Exception> OnError;

        public int Pending { get { return jobs.Count; } }

        public void Enqueue(string owner, string name, Func<bool> step)
        {
            if (step == null) return;
            jobs.AddLast(new Job { Owner = owner, Step = step, Name = name ?? owner ?? "?" });
        }

        public bool HasOwner(string owner)
        {
            foreach (Job j in jobs) if (j.Owner == owner) return true;
            return false;
        }

        /// <summary>Runs jobs until budgetMs is used (at least one step). Returns true while jobs remain.</summary>
        public bool RunSlice(long budgetMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string last = null;
            while (jobs.Count > 0)
            {
                Job j = jobs.First.Value;
                last = j.Name;
                if (!RunOne(j)) jobs.RemoveFirst();
                if (sw.ElapsedMilliseconds >= budgetMs) break;
            }
            long ms = sw.ElapsedMilliseconds;
            if (ms > MaxSliceMs) { MaxSliceMs = ms; MaxSliceJob = last; }
            return jobs.Count > 0;
        }

        /// <summary>Every pending job of this owner, now, to the end (the page is being shown).</summary>
        public void Flush(string owner)
        {
            if (owner == null) return;
            for (int guard = 0; guard < 100000; guard++)
            {
                Job j = null;
                foreach (Job x in jobs) if (x.Owner == owner) { j = x; break; }
                if (j == null) return;
                if (!RunOne(j)) jobs.Remove(j);
            }
        }

        private bool RunOne(Job j)
        {
            try { return j.Step(); }
            catch (Exception ex) { if (OnError != null) OnError(j.Name, ex); return false; }
        }
    }
}
