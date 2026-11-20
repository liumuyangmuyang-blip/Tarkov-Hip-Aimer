// PURE file: only System. Compiles under net48 (C# 5) and net8.0.
// v5.1 (W21): 弹药提示 count model (decision 1). Main weapon only; the count is an estimate from LMB hold time x RPM
// (semi: one round per click) and is shown ONLY as one of 5 bands (满 / 大半 / 一半 / 少 / 快空), never as a number.
// Leo round 2: R2 both reloads swap in a NEW FULL mag after the reload time from the settings (AmmoReloadMs / AmmoQuickReloadMs);
// R3 no weapon-switch handling at all (the count never pauses, nothing cancels a reload).
using System;

namespace TarkovCrosshair
{
    public sealed class AmmoModel
    {
        /// <summary>Bands (W22 interface): 4 满, 3 大半, 2 一半, 1 少, 0 快空.</summary>
        public const int BandFull = 4, BandMost = 3, BandHalf = 2, BandLow = 1, BandNearEmpty = 0;
        public static readonly string[] BandNames = new string[] { "快空", "少", "一半", "大半", "满" };
        /// <summary>Lower fraction bound of bands 4..1 (rounds / mag). Below Low = 快空.</summary>
        public const double FullFrom = 0.9, MostFrom = 0.6, HalfFrom = 0.35, LowFrom = 0.15;
        /// <summary>Default reload times (R2, Leo's numbers; settings AmmoReloadMs / AmmoQuickReloadMs override them).</summary>
        public const double DefaultNormalReloadMs = 2400, DefaultQuickReloadMs = 900;
        public const double MinReloadMs = 100, MaxReloadMs = 10000;

        // ---- configuration (Configure each frame; cheap) ----
        private int mag = 30;
        private double rpm = 600;
        private bool semi;
        private double quickTapMs = 300;
        public double NormalReloadMs = DefaultNormalReloadMs;
        public double QuickReloadMs = DefaultQuickReloadMs;

        // ---- state ----
        private int rounds = 30;
        private bool reloading;
        private int reloadKind;      // 1 normal, 2 quick
        private double reloadT;      // ms since the current reload started
        private double sinceReloadPress = double.MaxValue;
        private bool firePrev, reloadPrev;
        private double shotClock;    // ms since the last counted round while LMB is held (auto)

        /// <summary>+1 at every reload accepted (normal or quick); ReloadKind says which one the latest was.</summary>
        public int ReloadSeq;
        /// <summary>1 = normal (R), 2 = quick (R double-tap), 0 = none yet.</summary>
        public int ReloadKind { get { return reloadKind; } }
        /// <summary>+1 per counted round.</summary>
        public int ShotSeq;

        public AmmoModel() { Reset(); }

        public int MagSize { get { return mag; } }
        /// <summary>The estimate (internal only: UI shows Band, never this number).</summary>
        public int Rounds { get { return rounds; } }
        public bool Reloading { get { return reloading; } }
        /// <summary>Duration of the reload running now (0 when none).</summary>
        public double ReloadMs { get { return !reloading ? 0 : (reloadKind == 2 ? QuickReloadMs : NormalReloadMs); } }
        /// <summary>0..1 through the running reload (1 when none).</summary>
        public double ReloadProgress
        {
            get
            {
                double d = ReloadMs;
                if (!reloading || d <= 0) return 1;
                double u = reloadT / d;
                return u < 0 ? 0 : (u > 1 ? 1 : u);
            }
        }
        public double Fraction { get { return mag <= 0 ? 0 : (double)rounds / mag; } }
        public int Band { get { return BandOf(rounds, mag); } }
        public bool Empty { get { return rounds <= 0; } }

        public static int BandOf(int rounds, int mag)
        {
            if (mag <= 0) return BandNearEmpty;
            double f = (double)rounds / mag;
            if (f >= FullFrom - 1e-9) return BandFull;
            if (f >= MostFrom - 1e-9) return BandMost;
            if (f >= HalfFrom - 1e-9) return BandHalf;
            if (f >= LowFrom - 1e-9) return BandLow;
            return BandNearEmpty;
        }

        /// <summary>Settings from the file (clamped). A new mag size refills the mag. Reload times: R2 (NaN / out of range -> default).</summary>
        public void Configure(int magSize, double roundsPerMinute, bool semiAuto, double doubleTapMs, double normalReloadMs, double quickReloadMs)
        {
            NormalReloadMs = ClampReload(normalReloadMs, DefaultNormalReloadMs);
            QuickReloadMs = ClampReload(quickReloadMs, DefaultQuickReloadMs);
            Configure(magSize, roundsPerMinute, semiAuto, doubleTapMs);
        }

        private static double ClampReload(double v, double def)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) return def;
            return v < MinReloadMs ? MinReloadMs : (v > MaxReloadMs ? MaxReloadMs : v);
        }

        /// <summary>Mag size, RPM, semi, double-tap window (reload times unchanged).</summary>
        public void Configure(int magSize, double roundsPerMinute, bool semiAuto, double doubleTapMs)
        {
            if (magSize < 1) magSize = 1;
            if (magSize > 1000) magSize = 1000;
            if (double.IsNaN(roundsPerMinute) || roundsPerMinute < 1) roundsPerMinute = 1;
            if (roundsPerMinute > 3000) roundsPerMinute = 3000;
            if (double.IsNaN(doubleTapMs) || doubleTapMs < 50) doubleTapMs = 50;
            if (doubleTapMs > 1000) doubleTapMs = 1000;
            if (magSize != mag) { mag = magSize; rounds = magSize; reloading = false; }
            rpm = roundsPerMinute; semi = semiAuto; quickTapMs = doubleTapMs;
        }

        /// <summary>Full mag, no reload (start, F12 reset, feature switched on).</summary>
        public void Reset()
        {
            rounds = mag; reloading = false; reloadT = 0; sinceReloadPress = double.MaxValue;
            firePrev = reloadPrev = false; shotClock = 0;
        }

        /// <summary>Forget held keys without changing the count (focus regained: a key already down is not a press).</summary>
        public void SyncKeys(bool fire, bool reload)
        {
            firePrev = fire; reloadPrev = reload;
            shotClock = 0;
        }

        /// <summary>One frame of live keys (dtMs clamped to 0..100). fire = LMB, reload = 换弹键. Rules (game behaviour UNVERIFIED):
        /// - auto: the first round at the press, then one every 60000 / RPM ms while held; semi: one round per press.
        /// - no rounds counted while reloading or empty.
        /// - R = 普通换弹: the old mag stays in until NormalReloadMs, then a NEW FULL mag (a swap, not a top-up).
        /// - a second R within the double-tap window of a normal reload's press = 快速换弹: the old mag is dropped at once
        ///   (counts empty), a NEW FULL mag after QuickReloadMs (counted from the second press).
        /// - R3: no weapon keys; nothing pauses the count or cancels a reload.</summary>
        public void Update(double dtMs, bool fire, bool reload)
        {
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            if (dtMs > 100) dtMs = 100;
            bool fireEdge = fire && !firePrev, reloadEdge = reload && !reloadPrev;
            firePrev = fire; reloadPrev = reload;

            if (sinceReloadPress < double.MaxValue) sinceReloadPress += dtMs;
            if (reloading)
            {
                reloadT += dtMs;
                if (reloadT >= ReloadMs) { reloading = false; rounds = mag; }
            }
            if (reloadEdge)
            {
                if (reloading && reloadKind == 1 && sinceReloadPress <= quickTapMs)
                {
                    reloadKind = 2; reloadT = 0; rounds = 0; ReloadSeq++;
                }
                else if (!reloading)
                {
                    reloading = true; reloadKind = 1; reloadT = 0; ReloadSeq++;
                }
                sinceReloadPress = 0;
            }

            if (reloading || !fire) { shotClock = 0; return; }
            if (semi)
            {
                if (fireEdge) Shoot();
                return;
            }
            double interval = 60000.0 / rpm;
            if (fireEdge) { Shoot(); shotClock = 0; return; }
            shotClock += dtMs;
            while (shotClock >= interval - 1e-9 && rounds > 0) { shotClock -= interval; Shoot(); }
            if (rounds <= 0) shotClock = 0;
        }

        private void Shoot()
        {
            if (rounds <= 0) return;
            rounds--; ShotSeq++;
        }
    }
}
