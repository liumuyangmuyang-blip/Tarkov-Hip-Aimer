// TutorialStep9.cs -- 首次设置教程 script step 9 「换手」. System.Drawing only (no WinForms), C# 5, no per-frame allocation.
// Demo: the gun swaps hands, the offset map flips (X 取反, A<->D, Q<->E), then shots in the switched hand land a little off
// the auto-mirrored ring (「非绝对镜像」, 差值 · 示意) until the value is re-measured. Fields edit the ACTIVE profile's
// 换手自定义 items (D/A X, W/S Y, Q/E X+Y). The pure maths (TutorialStep9Math) compiles alone with TUTSTEP9_PURE_ONLY.
using System;
using System.Collections.Generic;
#if !TUTSTEP9_PURE_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>
    /// Pure maths / timing of the 换手 step. Every number is 示意 (demo timing, demo differences), not measured game data.
    /// Item indices are Profile's 换手 items: 0 W, 1 S, 2 A, 3 D, 5 Q (左侧身), 6 E (右侧身).
    /// </summary>
    public static class TutorialStep9Math
    {
        public const int ItemW = 0, ItemS = 1, ItemA = 2, ItemD = 3, ItemQ = 5, ItemE = 6;
        /// <summary>The six items re-measured in the switched hand, in the script's order (D, A, W, S, Q, E).</summary>
        public static readonly int[] Order = new int[] { ItemD, ItemA, ItemW, ItemS, ItemQ, ItemE };
        public static readonly string[] OrderNames = new string[] { "D", "A", "W", "S", "Q", "E" };

        // ---- demo loop (ms) ----
        public const int LoopMs = 10600;
        /// <summary>Normal hand: hold D and fire (the ring sits on the holes: tuned in the earlier steps).</summary>
        public const int D1From = 400, D1To = 2300, Fire1From = 900, Fire1To = 2000;
        /// <summary>The 换手 interval (toggle mode: a press at its start and at its end).</summary>
        public const int HandFrom = 2700, HandTo = 9700;
        /// <summary>Switched hand: hold D, fire, re-measure (the ring slides onto the holes), fire again.</summary>
        public const int D2From = 3700, D2To = 9300, Fire2From = 4300, Fire2To = 5500, Fire3From = 7100, Fire3To = 8800;
        public const int AdjustFrom = 5900, AdjustMs = 850;

        /// <summary>Shots in one settled pose before an item counts as re-measured (live).</summary>
        public const int ShotsNeeded = 2;
        /// <summary>Minimum 「对上」 tolerance (UI px).</summary>
        public const double MinTolPx = 4;
        /// <summary>Settled lean: |lean - target| below this.</summary>
        public const double LeanSettle = 0.06;

        /// <summary>Smoothstep 0..1.</summary>
        public static double Smooth(double u)
        {
            if (double.IsNaN(u) || u <= 0) return 0;
            if (u >= 1) return 1;
            return u * u * (3 - 2 * u);
        }

        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        public static double Wrap(double tMs)
        {
            if (!(tMs > 0) || double.IsInfinity(tMs)) return 0;
            return tMs % LoopMs;
        }

        /// <summary>Scripted re-measure progress at loop time t: 0 = ring at the 自动 mirror, 1 = on the switched-hand holes.</summary>
        public static double ScriptAdjust(double tMs)
        {
            double t = Wrap(tMs);
            return Smooth((t - AdjustFrom) / AdjustMs);
        }

        /// <summary>Caption phase of the scripted loop: 0 正常手, 1 换手中/换手后开枪, 2 重测, 3 对上了.</summary>
        public static int ScriptPhase(double tMs)
        {
            double t = Wrap(tMs);
            if (t < HandFrom || t >= HandTo) return 0;
            if (t < AdjustFrom) return 1;
            if (t < AdjustFrom + AdjustMs) return 2;
            return 3;
        }

        /// <summary>
        /// 示意 difference between the real switched hand and the 自动 mirror (「非绝对镜像」), UI px (X right +, Y up +).
        /// D / A differ in X, W / S in Y, Q / E in both -- the axes the earlier steps tell the user to tune.
        /// </summary>
        public static void DemoDelta(int item, out double dx, out double dy)
        {
            switch (item)
            {
                case ItemD: dx = 11; dy = 0; break;
                case ItemA: dx = -9; dy = 0; break;
                case ItemW: dx = 0; dy = -8; break;
                case ItemS: dx = 0; dy = 8; break;
                case ItemQ: dx = 8; dy = -6; break;
                case ItemE: dx = -7; dy = -5; break;
                default: dx = 0; dy = 0; break;
            }
        }

        /// <summary>
        /// Where the demo's switched-hand bullets go for one item: the user's own value once they typed one (we trust their
        /// measurement), else the 自动 mirror plus the 示意 difference.
        /// </summary>
        public static void TruthValue(int item, bool custom, double userX, double userY, double autoX, double autoY, out double x, out double y)
        {
            if (custom) { x = userX; y = userY; return; }
            double dx, dy;
            DemoDelta(item, out dx, out dy);
            x = autoX + dx; y = autoY + dy;
        }

        /// <summary>The 换手 item a settled single-key pose tests: dir 0..3 (W S A D) with no lean, or a lean with no movement.
        /// -1 = nothing to test (several keys, a diagonal, or nothing held).</summary>
        public static int ItemOf(int dir, int leanTarget)
        {
            if (leanTarget != 0) return dir < 0 ? (leanTarget < 0 ? ItemQ : ItemE) : -1;
            if (dir >= 0 && dir <= 3) return dir; // W S A D == items 0..3
            return -1;
        }

        public static int Bit(int item) { return item >= 0 && item < 31 ? 1 << item : 0; }

        /// <summary>Mask of all six items.</summary>
        public static int AllMask
        {
            get { int m = 0; for (int i = 0; i < Order.Length; i++) m |= Bit(Order[i]); return m; }
        }

        /// <summary>Settled = the hand blend has arrived, the lean has arrived, and neither movement axis is travelling.</summary>
        public static bool Settled(double blend, int handTarget, double lean, int leanTarget, bool travelX, bool travelY)
        {
            if (double.IsNaN(blend) || double.IsNaN(lean)) return false;
            if (Math.Abs(blend - handTarget) > 1e-6) return false;
            if (Math.Abs(lean - leanTarget) > LeanSettle) return false;
            return !travelX && !travelY;
        }

        /// <summary>Live prompt: 0 (press 换手) until the hand was switched once, then 1; done once all six were re-measured.</summary>
        public static int PromptIndex(bool everSwitched, int testedMask, out bool done)
        {
            int all = AllMask;
            done = everSwitched && (testedMask & all) == all;
            return everSwitched ? 1 : 0;
        }

        public static int CountTested(int mask)
        {
            int n = 0;
            for (int i = 0; i < Order.Length; i++) if ((mask & Bit(Order[i])) != 0) n++;
            return n;
        }

        public static double Gap(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            double d = Math.Sqrt(dx * dx + dy * dy);
            return double.IsNaN(d) ? double.PositiveInfinity : d;
        }

        /// <summary>「对上」: the hole centre lies inside the ring (radius = diameter / 2, at least MinTolPx).</summary>
        public static bool Matched(double gapPx, double ringDiameterUi)
        {
            double tol = ringDiameterUi / 2.0;
            if (double.IsNaN(tol) || tol < MinTolPx) tol = MinTolPx;
            return gapPx <= tol;
        }

        /// <summary>Rig zoom (before K) so that extentUi (at least minExtentUi) fills 'fill' of halfPx.</summary>
        public static double FitZoom(double halfPx, double k, double extentUi, double minExtentUi, double fill)
        {
            if (!(k > 0)) k = 1;
            if (!(halfPx > 0)) return 0.05;
            if (!(fill > 0)) fill = 1;
            double e = Math.Abs(extentUi);
            if (double.IsNaN(e) || double.IsInfinity(e)) e = 0;
            if (e < minExtentUi) e = minExtentUi;
            double z = halfPx * fill / (e * k);
            if (z < 0.05) z = 0.05;
            if (z > 40) z = 40;
            return z;
        }

        /// <summary>Offset-map scale (map px per UI px) so the longest arrow (at least minLen) reaches 'reach' map px.</summary>
        public static double MapScale(double reachPx, double longestUi, double minLen)
        {
            double l = Math.Abs(longestUi);
            if (double.IsNaN(l) || double.IsInfinity(l)) l = 0;
            if (l < minLen) l = minLen;
            if (!(l > 0) || !(reachPx > 0)) return 0;
            return reachPx / l;
        }
    }

#if !TUTSTEP9_PURE_ONLY
    public static class TutorialStep9
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order.</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep9Page());
        }
    }

    /// <summary>
    /// Script step 9 「换手」. Truth (holes) = the user's normal hand, and in the switched hand each item's own value when the
    /// user typed one, else the 自动 mirror + a 示意 difference. Ring = the program: the user's 换手 values (自动 = mirror). In
    /// the scripted loop the ring slides from the mirror onto the holes (「重测，填入新数值」); live, it shows the user's values.
    /// </summary>
    internal sealed class TutorialStep9Page : TutorialStep
    {
        private static readonly TutorialScript Script = new TutorialScript(TutorialStep9Math.LoopMs,
            new ScriptSeg(TutorialStep9Math.D1From, TutorialStep9Math.D1To, TKey.D),
            new ScriptSeg(TutorialStep9Math.Fire1From, TutorialStep9Math.Fire1To, TKey.Fire),
            new ScriptSeg(TutorialStep9Math.HandFrom, TutorialStep9Math.HandTo, TKey.Hand),
            new ScriptSeg(TutorialStep9Math.D2From, TutorialStep9Math.D2To, TKey.D),
            new ScriptSeg(TutorialStep9Math.Fire2From, TutorialStep9Math.Fire2To, TKey.Fire),
            new ScriptSeg(TutorialStep9Math.Fire3From, TutorialStep9Math.Fire3To, TKey.Fire));

        private static double HX(Profile p, int item) { double x, y; p.HandValue(item, out x, out y); return x; }
        private static double HY(Profile p, int item) { double x, y; p.HandValue(item, out x, out y); return y; }

        private static TutorialField FX(string label, string tag, bool main, int item)
        {
            return TutorialField.Offset(label, tag, main, OffsetAxis.X, -500, 500,
                delegate(Profile p) { return HX(p, item); },
                delegate(Profile p, double v) { p.SetHandCustom(item, v, HY(p, item)); });
        }

        private static TutorialField FY(string label, string tag, bool main, int item)
        {
            return TutorialField.Offset(label, tag, main, OffsetAxis.Y, -500, 500,
                delegate(Profile p) { return HY(p, item); },
                delegate(Profile p, double v) { p.SetHandCustom(item, HX(p, item), v); });
        }

        private static TutorialField Opt(TutorialField f) { f.Optional = true; return f; }

        private static readonly TutorialField[] HandFields = new TutorialField[]
        {
            FX("换手 D：左右", "推荐改", true, TutorialStep9Math.ItemD),
            FX("换手 A：左右", "推荐改", true, TutorialStep9Math.ItemA),
            FY("换手 W：上下", "推荐改", true, TutorialStep9Math.ItemW),
            FY("换手 S：上下", "推荐改", true, TutorialStep9Math.ItemS),
            FX("换手 左侧身：左右", "推荐改", true, TutorialStep9Math.ItemQ),
            Opt(FY("换手 左侧身：上下", "可选", false, TutorialStep9Math.ItemQ)),
            FX("换手 右侧身：左右", "推荐改", true, TutorialStep9Math.ItemE),
            Opt(FY("换手 右侧身：上下", "可选", false, TutorialStep9Math.ItemE)),
            Opt(FY("换手 D：上下", "上下不推荐改", false, TutorialStep9Math.ItemD)),
            Opt(FY("换手 A：上下", "上下不推荐改", false, TutorialStep9Math.ItemA)),
            Opt(FX("换手 W：左右", "左右不推荐改", false, TutorialStep9Math.ItemW)),
            Opt(FX("换手 S：左右", "左右不推荐改", false, TutorialStep9Math.ItemS)),
        };

        // ---- constant HUD text ----
        private const string WallLabel = "10 米";
        private const string TagNormal = "正常手";
        private const string TagSwitched = "换手";
        private const string TagSwitching = "换手中 …";
        private const string TagHoles = "弹孔：换手后的真实落点";
        private const string TagRing = "准星：你的换手数值";
        private const string TagDiff = "差值";
        private const string TagMatch = "对上了";
        private const string TagAdjust = "重测，填入新数值 …";
        private const string TagOneKey = "一次只按一个键";
        private const string TagHandOff = "换手检测未开启 · 到「按键」页打开";
        private const string MapTitle = "各键的偏移";
        private const string MapNormal = "正常手";
        private const string MapFlip = "取反中 …";
        private const string MapSwitched = "X 取反 · A↔D · Q↔E";
        private const string ListTitle = "换手重测";
        private const string ListOf6 = " / 6";
        private const string SceneNote = "不填＝自动镜像 · 以游戏弹孔为准";
        private const string CurAuto = "当前：自动镜像";
        private string curLine = CurAuto;   // 「当前：…」 rebuilt in Enter / OnFieldChanged only (never per frame)

        /// <summary>「当前：自动镜像」, or the custom 换手 values, e.g. 「当前：D ← 8.33% · W ↓ 3.47%（其余自动）」.</summary>
        private static string BuildCurrent(Profile p, GlobalSettings g)
        {
            if (p == null || !p.AnyHandCustom()) return CurAuto;
            System.Text.StringBuilder sb = new System.Text.StringBuilder("当前：");
            int n = 0; bool allSix = true;
            for (int i = 0; i < TutorialStep9Math.Order.Length; i++)
            {
                int item = TutorialStep9Math.Order[i];
                if (!p.IsHandCustom(item)) { allSix = false; continue; }
                double x, y; p.HandValue(item, out x, out y);
                if (n > 0) sb.Append(" · ");
                sb.Append(TutorialStep9Math.OrderNames[i]).Append(' ');
                if (item == TutorialStep9Math.ItemD || item == TutorialStep9Math.ItemA) sb.Append(TutorialPct.X(x, g));
                else if (item == TutorialStep9Math.ItemW || item == TutorialStep9Math.ItemS) sb.Append(TutorialPct.Y(y, g));
                else sb.Append(TutorialPct.Vec(x, y, g));
                n++;
            }
            if (n == 0) return "当前：自动镜像（其他项已自定义）";
            if (!allSix) sb.Append("（其余自动）");
            return sb.ToString();
        }
        private const string LabelPx = "px", LabelX = "X", LabelY = "Y", LabelHandKey = "换手";
        // v4.1 百分比偏移 labels (rebuilt only when the shown value changes) + the settings they convert with
        private GlobalSettings curG;
        private bool compactNow;
        private readonly TutorialPctLabel gapPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Magnitude);
        private readonly TutorialPctLabel dxPct = new TutorialPctLabel(OffsetAxis.X, TutorialPctLabel.Signed);
        private readonly TutorialPctLabel dyPct = new TutorialPctLabel(OffsetAxis.Y, TutorialPctLabel.Signed);
        private static readonly string[] ArrowNames = new string[] { "W", "S", "A", "D", "Q", "E" };
        private static readonly int[] MapItem = new int[] { 0, 1, 2, 3, 5, 6 };
        private static readonly string[] TermDone = new string[]
        {
            "> 换手重测  D ……完成", "> 换手重测  A ……完成", "> 换手重测  W ……完成",
            "> 换手重测  S ……完成", "> 换手重测  Q ……完成", "> 换手重测  E ……完成",
        };
        private const string TermAll = "> 六项重测完成  回到「换手」页可精调";

        private readonly FpRig rig = new FpRig();
        private readonly HandView view0 = new HandView(), view1 = new HandView();
        private Color[] kindColors;
        // Hole colour per key = the shared TutorialPalette key colours (pal.KeyColor), also the checklist / map legend;
        // normal-hand holes grey; switched-hand holes of no single key in the text colour.
        private const int KindNormal = 7, KindOther = 8;
        private float hudBottom = -1;     // bottom of last frame's left HUD column (the wall's top edge stays below it)
        private double flashMs = 999, gunKick;
        // own tracers from the lowered gun's muzzle (the rig's fallback tracer origin is always bottom-right)
        private const int MaxTr = 8;
        private readonly double[] trX = new double[MaxTr], trY = new double[MaxTr], trAge = new double[MaxTr];
        private int trHead;
        private string holdTag;           // built once in Enter (c.Fill), null in toggle mode
        private double prevT = -1;
        private double adj = 1;
        private int phase;

        // live progress
        private bool everSwitched;
        private int tested;
        private int poseItem = -1, poseShots;

        // shot accumulator of the current settled pose (UI px)
        private const int MaxAcc = 16;
        private readonly double[] accX = new double[MaxAcc], accY = new double[MaxAcc];
        private int accN, accHead, accKey = int.MinValue;
        private double meanX, meanY;
        private bool settled;
        private int curItem = -1;

        public TutorialStep9Page()
        {
            Id = "hand";
            Chapter = "实战校准";
            Title = "换手";
            Body = "按换手键，枪换到另一只手。\n所有偏移量会反过来。\n但不是完全镜像，要再测一遍。";
            DevNote = "按换手键后，程序会把所有偏移量都反过来。但换手并不是绝对镜像，偏移量会有变化，所以请在换手状态下把前面的测试全部再做一遍，填入新的数值。";
            Prompts = new string[] { "按{换手键}换手", "换手状态下，把 D、A、W、S、Q、E 再测一遍" };
            Hint = Prompts[0];
            Terminal = new string[] { "> 换手  偏移取反 ……完成", "> 非绝对镜像  需要重测" };
            FieldNote = null;   // the panel is full at 1180x760 (dev note must stay open); the note is drawn in the scene
            SettingsPage = TutorialPages.Hand;
            Fields = HandFields;
            KeysShown = TKey.W | TKey.S | TKey.A | TKey.D | TKey.Q | TKey.E | TKey.Fire | TKey.Hand;
            ClickFires = true;
        }

        public override void Enter(TutorialContext c)
        {
            rig.TargetDistanceM = 10;
            rig.Setup(c, c.P, c.P, true);
            Profile t = rig.TruthP, r = rig.RingP;
            // only the hand shows: no recoil, no jump (示意)
            t.RecoilMaxPx = 0; t.RecoilClickKickPx = 0; r.RecoilMaxPx = 0; r.RecoilClickKickPx = 0;
            if (t.Jump != null) t.Jump.Enabled = false;
            if (r.Jump != null) r.Jump.Enabled = false;
            // make the six items custom once on the clones (allocates here only); their X / Y are written per frame
            for (int i = 0; i < TutorialStep9Math.Order.Length; i++)
            {
                int item = TutorialStep9Math.Order[i];
                t.SetHandCustom(item, 0, 0);
                r.SetHandCustom(item, 0, 0);
            }
            rig.SetDistanceNow(10);
            rig.WallLabel = WallLabel;
            rig.UseParallax = false;
            rig.ShowRing = true; rig.ShowAim = false; rig.ShowCentre = true;
            rig.WallWm = 1.9; rig.WallHm = 1.35;
            TutorialNum.Signed(0); // warm the number cache once (not during the animation)
            holdTag = c.HandToggle ? null : c.Fill("按住模式：按住{换手键}期间是换手");
            prevT = -1; adj = 1; phase = 0; hudBottom = -1; flashMs = 999; gunKick = 0;
            for (int k = 0; k < MaxTr; k++) trAge[k] = 999;
            everSwitched = false; tested = 0; poseItem = -1; poseShots = 0;
            ResetAcc();
            PromptIndex = 0; Done = false;
            SyncValues(c.P, true);
            curLine = BuildCurrent(c.P, c.G);
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            Profile p = c.P, r = rig.RingP, t = rig.TruthP;
            curLine = BuildCurrent(p, c.G);
            if (p == null || r == null || t == null) return;
            r.Diameter = p.Diameter; r.Thickness = p.Thickness; r.Color = p.Color; r.Color2 = p.Color2;
            r.HandSwitchMs = p.HandSwitchMs; t.HandSwitchMs = p.HandSwitchMs;
            r.LeanMs = p.LeanMs; t.LeanMs = p.LeanMs;
        }

        private void ResetAcc() { accN = 0; accHead = 0; accKey = int.MinValue; meanX = 0; meanY = 0; }

        /// <summary>Writes the six switched-hand items of both clones from the user's profile (no allocation).</summary>
        private void SyncValues(Profile p, bool scripted)
        {
            if (p == null || rig.TruthP == null || rig.RingP == null) return;
            List<HandCustomItem> th = rig.TruthP.HandCustom, rh = rig.RingP.HandCustom;
            if (th == null || rh == null) return;
            for (int i = 0; i < TutorialStep9Math.Order.Length; i++)
            {
                int item = TutorialStep9Math.Order[i];
                if (item >= th.Count || item >= rh.Count || th[item] == null || rh[item] == null) continue;
                double ux, uy, ax, ay, tx, ty;
                p.HandValue(item, out ux, out uy);
                p.HandAuto(item, out ax, out ay);
                TutorialStep9Math.TruthValue(item, p.IsHandCustom(item), ux, uy, ax, ay, out tx, out ty);
                th[item].X = tx; th[item].Y = ty;
                if (scripted)
                {
                    // the demo re-measure: 自动 mirror -> the real switched-hand value (D only is demonstrated; others stay put)
                    double k = item == TutorialStep9Math.ItemD ? adj : 1.0;
                    rh[item].X = TutorialStep9Math.Lerp(ax, tx, k);
                    rh[item].Y = TutorialStep9Math.Lerp(ay, ty, k);
                }
                else { rh[item].X = ux; rh[item].Y = uy; }
            }
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            bool fire; int lit;
            InputState i = c.Input(Script, out fire, out lit);
            LitKeys = lit;
            if (c.Arbiter.Resumed)
            {
                // the script starts from the normal hand: drop whatever hand / lean the live input left behind
                rig.Truth.Reset(); rig.Ring.Reset(); rig.ClearHoles(); ResetAcc();
            }
            if (c.Scripted)
            {
                double t = Script.Wrap(c.Arbiter.ScriptMs);
                if (t < prevT) { rig.ClearHoles(); ResetAcc(); }
                prevT = t;
                adj = TutorialStep9Math.ScriptAdjust(t);
                phase = TutorialStep9Math.ScriptPhase(t);
            }
            else { prevT = -1; adj = 1; phase = -1; }
            SyncValues(c.P, c.Scripted);

            int hand = rig.Truth.Hand;
            int poseNow = TutorialStep9Math.ItemOf(rig.Truth.CurrentDir, rig.Truth.LeanTarget);
            rig.NextHoleKind = hand != 1 ? KindNormal : (poseNow >= 0 ? poseNow : KindOther);
            rig.Update(dtMs, i, fire);
            for (int k = 0; k < MaxTr; k++) trAge[k] += dtMs;
            if (rig.ShotsThisFrame > 0)
            {
                flashMs = 0; gunKick = 1;
                trX[trHead] = rig.LastShotX; trY[trHead] = rig.LastShotY; trAge[trHead] = 0; trHead = (trHead + 1) % MaxTr;
            }
            else flashMs += dtMs;
            gunKick *= Math.Exp(-dtMs / 70.0);

            MotionEngine e = rig.Truth;
            int dir = e.CurrentDir, lt = e.LeanTarget;
            settled = TutorialStep9Math.Settled(e.HandBlend, e.Hand, e.Lean, lt, e.Traveling(0), e.Traveling(1));
            curItem = TutorialStep9Math.ItemOf(dir, lt);
            int key = settled ? (e.Hand * 1000 + (dir + 1) * 10 + (lt + 1)) : int.MinValue;
            if (key != accKey) { accN = 0; accHead = 0; accKey = key; poseShots = 0; poseItem = curItem; }
            if (settled && rig.ShotsThisFrame > 0)
            {
                accX[accHead] = rig.LastShotX; accY[accHead] = rig.LastShotY;
                accHead = (accHead + 1) % MaxAcc;
                if (accN < MaxAcc) accN++;
                poseShots += rig.ShotsThisFrame;
            }
            if (accN > 0)
            {
                double sx = 0, sy = 0;
                for (int k = 0; k < accN; k++) { sx += accX[k]; sy += accY[k]; }
                meanX = sx / accN; meanY = sy / accN;
            }

            if (!c.Scripted)
            {
                if (hand == 1) everSwitched = true;
                if (hand == 1 && settled && poseItem >= 0 && poseShots >= TutorialStep9Math.ShotsNeeded)
                {
                    int b = TutorialStep9Math.Bit(poseItem);
                    if ((tested & b) == 0)
                    {
                        tested |= b;
                        for (int k = 0; k < TutorialStep9Math.Order.Length; k++)
                            if (TutorialStep9Math.Order[k] == poseItem) c.Term.Push(TermDone[k]);
                        bool doneNow;
                        TutorialStep9Math.PromptIndex(everSwitched, tested, out doneNow);
                        if (doneNow && !Done) c.Term.Push(TermAll);
                    }
                }
                bool done;
                PromptIndex = TutorialStep9Math.PromptIndex(everSwitched, tested, out done);
                if (done) Done = true;
            }
        }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            curG = c.G;
            TutorialPalette pal = gx.Pal;
            if (kindColors == null)
            {
                kindColors = new Color[9];
                kindColors[TutorialStep9Math.ItemW] = pal.KeyColor(TKey.W); kindColors[TutorialStep9Math.ItemS] = pal.KeyColor(TKey.S);
                kindColors[TutorialStep9Math.ItemA] = pal.KeyColor(TKey.A); kindColors[TutorialStep9Math.ItemD] = pal.KeyColor(TKey.D);
                kindColors[4] = pal.Muted;
                kindColors[TutorialStep9Math.ItemQ] = pal.KeyColor(TKey.Q); kindColors[TutorialStep9Math.ItemE] = pal.KeyColor(TKey.E);
                kindColors[KindNormal] = TutorialGfx.A(pal.Muted, 230); kindColors[KindOther] = pal.Text;
            }
            rig.KindColors = kindColors;
            bool compact = c.Compact || scene.Width / gx.K < TutorialContext.CompactW || scene.Height / gx.K < TutorialContext.CompactH;
            compactNow = compact;
            float pad = gx.S(10);
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            float mapW = gx.S(186), mapH = gx.S(160);
            float rightCol = compact ? 0 : mapW + pad;

            // wall area: below last frame's left HUD column, clear of the right column (keys + offset map)
            float top = hudBottom > scene.Top ? hudBottom : scene.Top + gx.S(compact ? 40 : 150);
            float wallHalfH = Math.Max(scene.Height * 0.2f, Math.Min(scene.Height * 0.36f, cy - top - gx.S(8)));
            float wallHalfW = Math.Max(scene.Width * 0.14f, Math.Min(scene.Width * 0.43f, scene.Width / 2f - rightCol - 2 * pad));

            // zoom: the largest offset of either hand fits inside the wall
            Profile rp = rig.RingP;
            double ext = 20;
            if (rp != null)
            {
                rp.FillView(0, view0); rp.FillView(1, view1);
                ext = Math.Max(Longest(view0), Longest(view1)) + 16;
            }
            rig.Zoom = TutorialStep9Math.FitZoom(Math.Min(wallHalfW, wallHalfH), gx.K, ext, 40, 0.82);
            double pxPerM = TutorialGeometry.ProjectedPx(1, rig.DistanceM) * rig.Zoom * gx.K;
            if (pxPerM > 0) { rig.WallWm = 2 * wallHalfW / pxPerM; rig.WallHm = 2 * wallHalfH / pxPerM; }
            rig.ShowGun = false;   // drawn smaller and lower below, so holes and labels stay clear of it
            // the rig's own tracers (Pal.Hot) start at a fixed bottom-right point, wrong for the switched hand: hide them
            // for this call (every hole colour comes from KindColors, so nothing else uses Pal.Hot inside rig.Draw)
            Color hot = pal.Hot;
            pal.Hot = Color.Transparent;
            try { rig.Draw(gx, scene); }
            finally { pal.Hot = hot; }

            Graphics g = gx.G;
            g.SetClip(scene);
            DrawGun(gx, scene, (float)(rig.Zoom * gx.K));
            double blend = rig.Ring.HandBlend;
            int hand = rig.Truth.Hand;
            bool switched = blend > 0.98, switching = blend > 0.02 && blend < 0.98;
            double ringD = rp != null ? rp.Diameter : 12;
            // the ring again on top of the gun (the rig drew it under)
            if (rp != null)
            {
                PointF rr0 = rig.ToScreen(scene, gx.K, rig.RingX, rig.RingY);
                Color inner = FpRig.ParseColor(rp.Color, pal.Neon), outer = FpRig.ParseColor(rp.Color2, Color.Black);
                float d = Math.Max(gx.S(16), (float)(rp.Diameter * rig.Zoom * gx.K));
                gx.Ring(rr0.X, rr0.Y, d, inner, outer, Math.Max(1.5f, (float)(rp.Thickness * rig.Zoom * 0.8)));
            }

            // ---- gap marker (switched hand, settled single-key pose, 2+ shots) ----
            if (hand == 1 && settled && curItem >= 0 && accN >= 2 && !switching)
            {
                PointF m = rig.ToScreen(scene, gx.K, meanX, meanY);
                PointF rr = rig.ToScreen(scene, gx.K, rig.RingX, rig.RingY);
                double gap = TutorialStep9Math.Gap(meanX, meanY, rig.RingX, rig.RingY);
                bool ok = TutorialStep9Math.Matched(gap, ringD);
                DrawGap(gx, scene, m, rr, gap, meanX - rig.RingX, meanY - rig.RingY, ok);
            }

            // ---- HUD left column (compact: the hand state only) ----
            RectangleF t1 = gx.Tag(switching ? TagSwitching : (switched ? TagSwitched : TagNormal), scene.Left + pad, scene.Top + pad,
                switched || switching ? pal.Hot : pal.Accent, 0);
            float ly = t1.Bottom + gx.S(5);
            if (!compact)
            {
                // v4.1: max 2 tags per view (the hand state + the result pill); the rest are plain legend lines
                RectangleF t2 = gx.Legend(TagHoles, scene.Left + pad, ly, pal.Hot, TutorialGfx.MarkDot, 0);
                RectangleF t3 = gx.Legend(TagRing, scene.Left + pad, t2.Bottom + gx.S(3), pal.Neon, TutorialGfx.MarkRing, 0);
                ly = t3.Bottom + gx.S(5);
                if (holdTag != null) { RectangleF t4 = gx.Legend(holdTag, scene.Left + pad, ly, pal.Muted, TutorialGfx.MarkDot, 0); ly = t4.Bottom + gx.S(5); }
                if (!c.G.HandEnabled) { RectangleF t5 = gx.Legend(TagHandOff, scene.Left + pad, ly, pal.Red, TutorialGfx.MarkDot, 0); ly = t5.Bottom + gx.S(5); }
                ly = DrawChecklist(gx, c, scene.Left + pad, ly + gx.S(4));
            }
            else if (!c.G.HandEnabled) { RectangleF t5 = gx.Legend(TagHandOff, scene.Left + pad, ly, pal.Red, TutorialGfx.MarkDot, 0); ly = t5.Bottom + gx.S(5); }
            hudBottom = ly;
            if (!compact)
            {
                // field note (moved from the text panel): centred under the wall, between the two gun positions
                SizeF ns = gx.Measure(SceneNote, gx.F.Small), cs = gx.Measure(curLine, gx.F.Small);
                float bw = Math.Max(ns.Width, cs.Width), bh = cs.Height + gx.S(2) + ns.Height;
                float ny = Math.Min(scene.Bottom - bh - gx.S(10), cy + wallHalfH + gx.S(8));
                g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 190)), cx - bw / 2f - gx.S(6), ny - gx.S(2), bw + gx.S(12), bh + gx.S(4));
                gx.Text(curLine, gx.F.Small, pal.Text, cx, ny, 1);
                gx.Text(SceneNote, gx.F.Small, pal.Muted, cx, ny + cs.Height + gx.S(2), 1);
            }

            // ---- centre captions (between the columns, above the wall) ----
            float capY = compact ? scene.Top + pad : Math.Max(scene.Top + pad, cy - wallHalfH - gx.S(30));
            if (!compact && c.Scripted && phase == 2) gx.Legend(TagAdjust, cx, capY, pal.Neon, TutorialGfx.MarkDot, 1);
            else if (!compact && !c.Scripted && hand == 1 && !switching && rig.Firing && curItem < 0 && (rig.Truth.CurrentDir >= 0 || rig.Truth.LeanTarget != 0))
                gx.Legend(TagOneKey, cx, capY, pal.Hot, TutorialGfx.MarkDot, 1);

            // ---- right column: keys + 换手 key + offset map ----
            if (!compact)
            {
                float capS = 26;
                SizeF probe = gx.Measure("W", gx.F.Key);
                if (probe.Height + gx.S(8) > gx.S(capS)) capS = (probe.Height + gx.S(8)) / gx.K;
                float clusterW = gx.S(capS) * 3.25f + gx.S(8) + gx.S(10) + gx.S(capS) * 0.9f;
                float kx = scene.Right - pad - Math.Max(clusterW, mapW), ky = scene.Top + pad;
                SizeF ks = gx.KeyCluster(kx, ky, capS, LitKeys, KeysShown & ~TKey.Hand, true);
                float hy = ky + ks.Height + gx.S(6);
                RectangleF hk = DrawHandKey(gx, kx, hy, (LitKeys & TKey.Hand) != 0);
                float mx = scene.Right - pad - mapW, my = hk.Bottom + gx.S(10);
                float capH = gx.Measure(MapFlip, gx.F.Small).Height + gx.S(6);
                if (my + mapH + capH < scene.Bottom - gx.S(4)) DrawMap(gx, new RectangleF(mx, my, mapW, mapH), blend);
            }
            g.ResetClip();
        }

        /// <summary>The 示意 rifle at ~70 % size in the bottom corner (moves to the other side on 换手); own kick and flash.</summary>
        private void DrawGun(TutorialGfx gx, RectangleF scene, float z)
        {
            float shrink = 0.70f;
            RectangleF gr = new RectangleF(scene.Left, scene.Bottom - scene.Height * shrink, scene.Width, scene.Height * shrink);
            float swayX = (float)(rig.Truth.MoveX * 0.35 * z * 0.6), swayY = (float)(-rig.Truth.MoveY * 0.35 * z * 0.6);
            PointF muzzle = gx.Gun(gr, swayX, swayY, (float)(gunKick * gx.S(5)), rig.HandBlend);
            for (int i = 0; i < MaxTr; i++)
            {
                if (trAge[i] > 90) continue;
                PointF hit = rig.ToScreen(scene, gx.K, trX[i], trY[i]);
                gx.G.DrawLine(gx.P(TutorialGfx.A(gx.Pal.Hot, (1 - trAge[i] / 90.0) * 0.9), 1.6f), muzzle, hit);
            }
            if (flashMs < 50) gx.Flash(muzzle, 1 - flashMs / 50.0);
        }

        private static double Longest(HandView v)
        {
            double m = 0;
            for (int i = 0; i < 4; i++) m = Math.Max(m, Math.Max(Math.Abs(v.DX[i]), Math.Abs(v.DY[i])));
            m = Math.Max(m, Math.Max(Math.Abs(v.QX), Math.Abs(v.QY)));
            m = Math.Max(m, Math.Max(Math.Abs(v.EX), Math.Abs(v.EY)));
            return double.IsNaN(m) || double.IsInfinity(m) ? 0 : m;
        }

        private void DrawGap(TutorialGfx gx, RectangleF scene, PointF m, PointF ring, double gap, double dx, double dy, bool ok)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            float a = gx.S(7);
            Color col = ok ? pal.Neon : pal.Hot;
            Pen pen = gx.P(TutorialGfx.A(col, 230), 1.2f);
            g.DrawLine(pen, m.X - a, m.Y, m.X + a, m.Y);
            g.DrawLine(pen, m.X, m.Y - a, m.X, m.Y + a);
            float pulse = (float)(0.5 + 0.5 * Math.Sin(gx.TimeMs / 260.0));
            g.DrawEllipse(gx.P(TutorialGfx.A(col, 70 + (int)(80 * pulse)), 1f), m.X - a * 1.7f, m.Y - a * 1.7f, a * 3.4f, a * 3.4f);
            if (!ok) g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Hot, 210), 1.2f), m.X, m.Y, ring.X, ring.Y);

            // label block beside the hole centre, on the side away from the ring; measured, kept inside the scene
            string n = gapPct.Of(gap, curG), sx = dxPct.Of(dx, curG), sy = dyPct.Of(dy, curG); // v4.1 百分比偏移
            SizeF tagSz = gx.Measure(ok ? TagMatch : TagDiff, gx.F.Tag);
            float tagW = tagSz.Width + gx.S(12), tagH = tagSz.Height + gx.S(5);
            SizeF nSz = gx.Measure(n, gx.F.Mono), pSz = gx.Measure(LabelPx, gx.F.MonoSmall);
            float w1 = gx.Measure(LabelX, gx.F.MonoSmall).Width, w2 = gx.Measure(sx, gx.F.MonoSmall).Width;
            float w3 = gx.Measure(LabelY, gx.F.MonoSmall).Width, w4 = gx.Measure(sy, gx.F.MonoSmall).Width;
            float rowH2 = pSz.Height;
            float compW = w1 + gx.S(3) + w2 + gx.S(10) + w3 + gx.S(3) + w4;
            float blockW = ok ? tagW : Math.Max(tagW, Math.Max(nSz.Width + gx.S(3) + pSz.Width, compactNow ? 0 : compW));
            float blockH = ok ? tagH : tagH + gx.S(3) + nSz.Height + (compactNow ? 0 : gx.S(2) + rowH2); // small scene: no X / Y row
            bool right = m.X >= ring.X;
            float bx = right ? m.X + gx.S(18) : m.X - gx.S(18) - blockW;
            if (bx + blockW > scene.Right - gx.S(6)) bx = m.X - gx.S(18) - blockW;
            if (bx < scene.Left + gx.S(6)) bx = m.X + gx.S(18);
            float by = m.Y - blockH - gx.S(14);   // above the holes, never on them
            if (by < scene.Top + gx.S(6)) by = m.Y + gx.S(14);
            if (by + blockH > scene.Bottom - gx.S(6)) by = scene.Bottom - gx.S(6) - blockH;
            // backing plate so the numbers stay readable over holes / the wall grid
            g.FillRectangle(gx.B(TutorialGfx.A(pal.SceneBg, 170)), bx - gx.S(4), by - gx.S(3), blockW + gx.S(8), blockH + gx.S(6));
            if (ok) { gx.Tag(TagMatch, bx, by, pal.Neon, 0); return; }
            RectangleF tr = gx.Tag(TagDiff, bx, by, pal.Hot, 0);
            float ny = tr.Bottom + gx.S(3);
            gx.Text(n, gx.F.Mono, pal.Hot, bx, ny, 0);
            // X / Y components (UI px, X right +, Y up +)
            float cy2 = ny + nSz.Height + gx.S(2);
            if (compactNow) return;
            float x = bx;
            gx.Text(LabelX, gx.F.MonoSmall, pal.Muted, x, cy2, 0); x += w1 + gx.S(3);
            gx.Text(sx, gx.F.MonoSmall, pal.Text, x, cy2, 0); x += w2 + gx.S(10);
            gx.Text(LabelY, gx.F.MonoSmall, pal.Muted, x, cy2, 0); x += w3 + gx.S(3);
            gx.Text(sy, gx.F.MonoSmall, pal.Text, x, cy2, 0);
        }

        /// <summary>The 换手 key cap, sized to its text. Returns its rectangle.</summary>
        private RectangleF DrawHandKey(TutorialGfx gx, float x, float y, bool lit)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            SizeF t = gx.Measure(LabelHandKey, gx.F.Tag);
            float w = Math.Max(gx.S(56), t.Width + gx.S(18)), h = Math.Max(gx.S(22), t.Height + gx.S(8));
            RectangleF r = new RectangleF(x, y, w, h);
            if (lit) g.FillRectangle(gx.B(TutorialGfx.A(pal.Hot, 60)), r.X - gx.S(2), r.Y - gx.S(2), r.Width + gx.S(4), r.Height + gx.S(4));
            g.FillRectangle(gx.B(lit ? TutorialGfx.A(pal.Hot, 170) : TutorialGfx.A(pal.Surface, 235)), r);
            g.DrawRectangle(gx.P(lit ? pal.Hot : pal.Accent, lit ? 1.6f : 1.1f), r.X, r.Y, r.Width, r.Height);
            gx.TextIn(LabelHandKey, gx.F.Tag, lit ? Color.Black : pal.Text, r, gx.CenterMid);
            return r;
        }

        private Color KeyColor(int item)
        {
            return kindColors != null && item >= 0 && item < kindColors.Length ? kindColors[item] : Color.White;
        }

        /// <summary>D A W S Q E boxes (each in its hole colour = the legend): filled green = re-measured in the switched hand,
        /// highlighted = the pose being tested now. Returns the bottom.</summary>
        private float DrawChecklist(TutorialGfx gx, TutorialContext c, float x, float y)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            int n = TutorialStep9Math.CountTested(tested);
            SizeF title = gx.Measure(ListTitle, gx.F.Tag);
            gx.Text(ListTitle, gx.F.Tag, pal.Muted, x, y, 0);
            gx.Text(TutorialNum.Int(n), gx.F.MonoSmall, n == 6 ? pal.Neon : pal.Text, x + title.Width + gx.S(6), y, 0);
            gx.Text(ListOf6, gx.F.MonoSmall, pal.Muted, x + title.Width + gx.S(6) + gx.Measure(TutorialNum.Int(n), gx.F.MonoSmall).Width, y, 0);
            SizeF k = gx.Measure("W", gx.F.Key);
            float s = Math.Max(gx.S(22), Math.Max(k.Width, k.Height) + gx.S(8)), gap = gx.S(5);
            float by = y + title.Height + gx.S(5);
            int demoItem = c.Scripted && rig.Truth.Hand == 1 ? TutorialStep9Math.ItemD : -1;
            int nowItem = !c.Scripted && rig.Truth.Hand == 1 && settled ? curItem : demoItem;
            for (int i = 0; i < TutorialStep9Math.Order.Length; i++)
            {
                int item = TutorialStep9Math.Order[i];
                bool ok = (tested & TutorialStep9Math.Bit(item)) != 0;
                bool now = item == nowItem;
                Color kc = KeyColor(item);
                RectangleF r = new RectangleF(x + i * (s + gap), by, s, s);
                if (now) g.FillRectangle(gx.B(TutorialGfx.A(kc, 60)), r.X - gx.S(3), r.Y - gx.S(3), r.Width + gx.S(6), r.Height + gx.S(6));
                g.FillRectangle(gx.B(ok ? TutorialGfx.A(pal.Neon, 160) : TutorialGfx.A(pal.SceneBg, 235)), r);
                g.DrawRectangle(gx.P(ok ? pal.Neon : kc, now ? 1.8f : 1.2f), r.X, r.Y, r.Width, r.Height);
                gx.TextIn(TutorialStep9Math.OrderNames[i], gx.F.Key, ok ? Color.Black : kc, r, gx.CenterMid);
            }
            return by + s + gx.S(8);
        }

        /// <summary>The offset map: one arrow per W S A D Q E from the centre, morphing with the hand blend; a mirror line
        /// sweeps across while the hand switches.</summary>
        private void DrawMap(TutorialGfx gx, RectangleF box, double blend)
        {
            TutorialPalette pal = gx.Pal;
            Graphics g = gx.G;
            RectangleF inner = gx.PlotFrame(box, MapTitle);
            float ocx = inner.Left + inner.Width / 2f, ocy = inner.Top + inner.Height / 2f;
            // axes
            Pen axis = gx.P(TutorialGfx.A(pal.Neon, 40), 1f);
            g.DrawLine(axis, inner.Left, ocy, inner.Right, ocy);
            g.DrawLine(axis, ocx, inner.Top, ocx, inner.Bottom);
            double longest = Math.Max(Longest(view0), Longest(view1));
            double sc = TutorialStep9Math.MapScale(Math.Min(inner.Width, inner.Height) * 0.5 - gx.S(10), longest, 10);
            for (int i = 0; i < 6; i++)
            {
                double x0, y0, x1, y1;
                ArrowOf(view0, i, out x0, out y0);
                ArrowOf(view1, i, out x1, out y1);
                double vx = TutorialStep9Math.Lerp(x0, x1, blend), vy = TutorialStep9Math.Lerp(y0, y1, blend);
                float ex = (float)(ocx + vx * sc), ey = (float)(ocy - vy * sc);
                bool lean = i >= 4;
                Color kc = KeyColor(MapItem[i]);
                Color ac = lean ? TutorialGfx.A(kc, 170) : kc;
                if (Math.Abs(ex - ocx) + Math.Abs(ey - ocy) > gx.S(4)) gx.Arrow(ac, lean ? 1.1f : 1.5f, ocx, ocy, ex, ey);
                else gx.Dot(ac, ex, ey, gx.S(2));
                if (lean) continue; // Q / E: colour legend at the bottom (their short arrows would bury the labels)
                double len = Math.Sqrt((ex - ocx) * (ex - ocx) + (ey - ocy) * (ey - ocy));
                SizeF ls = gx.Measure(ArrowNames[i], gx.F.MonoSmall);
                float off = Math.Max(ls.Width, ls.Height) * 0.5f + gx.S(5);
                float lx = ex, ly = ey - off;
                if (len > 0.5) { lx = (float)(ex + (ex - ocx) / len * off); ly = (float)(ey + (ey - ocy) / len * off); }
                gx.TextMid(ArrowNames[i], gx.F.MonoSmall, kc, lx, ly, 1);
            }
            // Q / E legend (bottom-left of the plot)
            {
                SizeF qs = gx.Measure(ArrowNames[4], gx.F.MonoSmall);
                float lyy = inner.Bottom - qs.Height * 0.5f, lxx = inner.Left + gx.S(2);
                for (int j = 4; j < 6; j++)
                {
                    Color kc = KeyColor(MapItem[j]);
                    g.DrawLine(gx.P(kc, 1.4f), lxx, lyy, lxx + gx.S(12), lyy);
                    gx.TextMid(ArrowNames[j], gx.F.MonoSmall, kc, lxx + gx.S(15), lyy, 0);
                    lxx += gx.S(15) + qs.Width + gx.S(10);
                }
            }
            gx.GlowDot(pal.Text, ocx, ocy, gx.S(1.6f));
            bool switching = blend > 0.02 && blend < 0.98;
            if (switching)
            {
                // mirror line sweeping across the map
                float sx = inner.Left + (float)(inner.Width * blend);
                gx.GlowLine(pal.Hot, 1.2f, sx, inner.Top, sx, inner.Bottom);
            }
            string cap = switching ? MapFlip : (blend >= 0.98 ? MapSwitched : MapNormal);
            gx.Text(cap, gx.F.Small, blend >= 0.5 ? pal.Hot : pal.Accent, box.Left + box.Width / 2f, box.Bottom + gx.S(4), 1);
        }

        private static void ArrowOf(HandView v, int i, out double x, out double y)
        {
            switch (i)
            {
                case 0: x = v.DX[0]; y = v.DY[0]; break;
                case 1: x = v.DX[1]; y = v.DY[1]; break;
                case 2: x = v.DX[2]; y = v.DY[2]; break;
                case 3: x = v.DX[3]; y = v.DY[3]; break;
                case 4: x = v.QX; y = v.QY; break;
                default: x = v.EX; y = v.EY; break;
            }
        }
    }
#endif
}
