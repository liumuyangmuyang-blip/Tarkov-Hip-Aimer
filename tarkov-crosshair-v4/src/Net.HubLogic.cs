// v5.1 (W28) NET: the consent rules NetHub follows (pure, tested). Settings (INetSettings) are the source of truth.
//   统计       v5.1 (Leo 21 Nov 2026): ON by default (opt-out). NetStatsConsent null (fresh install / never answered) or true =
//              heartbeat; false (the user turned it off, or answered 不用 earlier) = nothing. No question any more.
//   错误记录    asked after a crash when NetCrashAsk is on (v5.1 R3: no worker needed, nothing uploaded; 打开邮箱 opens the
//              user's own mail program + the folder with crash.log).
//   检查更新    NetUpdateCheck on = check at start + every 6 h (GitHub only).
using System;

namespace TarkovCrosshair
{
    /// <summary>The three net switches as stored in settings.json (W23 fields). Save() persists them.</summary>
    internal interface INetSettings
    {
        bool UpdateCheck { get; set; }
        bool? StatsConsent { get; set; }
        bool CrashAsk { get; set; }
        void Save();
    }

    /// <summary>INetSettings over lambdas, so the wiring code can point it at GlobalSettings fields without this file knowing
    /// their class. In memory only when built with the parameterless constructor (tests, render harness).</summary>
    internal sealed class DelegateNetSettings : INetSettings
    {
        private readonly Func<bool> getUpdate, getCrash;
        private readonly Action<bool> setUpdate, setCrash;
        private readonly Func<bool?> getStats;
        private readonly Action<bool?> setStats;
        private readonly Action save;
        private bool memUpdate = true, memCrash = true;
        private bool? memStats;
        public int Saves { get; private set; }

        public DelegateNetSettings() { }

        public DelegateNetSettings(Func<bool> getUpdate, Action<bool> setUpdate, Func<bool?> getStats, Action<bool?> setStats,
            Func<bool> getCrash, Action<bool> setCrash, Action save)
        {
            this.getUpdate = getUpdate; this.setUpdate = setUpdate; this.getStats = getStats; this.setStats = setStats;
            this.getCrash = getCrash; this.setCrash = setCrash; this.save = save;
        }

        public bool UpdateCheck
        {
            get { return getUpdate != null ? getUpdate() : memUpdate; }
            set { if (setUpdate != null) setUpdate(value); else memUpdate = value; }
        }
        public bool? StatsConsent
        {
            get { return getStats != null ? getStats() : memStats; }
            set { if (setStats != null) setStats(value); else memStats = value; }
        }
        public bool CrashAsk
        {
            get { return getCrash != null ? getCrash() : memCrash; }
            set { if (setCrash != null) setCrash(value); else memCrash = value; }
        }
        public void Save() { Saves++; if (save != null) save(); }
    }

    internal static class NetRules
    {
        /// <summary>v5.1 (Leo 21 Nov 2026): never -- the one-time question is gone (stats are on by default, off in 常规).</summary>
        public static bool NeedsStatsQuestion(INetSettings s, string workerUrl)
        {
            return false;
        }

        /// <summary>Stats are on unless the user turned them off (StatsConsent false). null = default = on.</summary>
        public static bool StatsOn(INetSettings s)
        {
            return s != null && s.StatsConsent != false;
        }

        public static bool WantHeartbeat(INetSettings s, string workerUrl)
        {
            return s != null && UsageConfig.IsValidWorkerUrl(workerUrl) && StatsOn(s);
        }

        public static bool MayAskCrash(INetSettings s)
        {
            return s != null && s.CrashAsk;
        }

        /// <summary>Settings -> net-prefs.json mirror (the heartbeat and the updater read NetPrefs).</summary>
        public static void Mirror(INetSettings s, NetPrefs p)
        {
            if (s == null || p == null) return;
            p.AutoCheck = s.UpdateCheck;
            bool? c = s.StatsConsent;
            p.UsageAsked = true;           // v5.1: opt-out, nothing to ask
            p.UsageOptIn = c != false;     // null (default) or true = on
        }
    }
}
