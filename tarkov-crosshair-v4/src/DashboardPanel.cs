// DashboardPanel.cs -- v4.0 / v4.1 首页 dashboard (WinForms, C# 5 / .NET Framework 4.8).
//
//   new DashboardPanel(new MotionEngine(), app.G)   -> Dock it into the 首页 page.
//
// Pass a MotionEngine that nobody else updates (NOT the overlay's: that one lives on the render thread). By default
// the panel drives it itself on the UI thread: while our window is in front it polls the same key bindings with
// GetAsyncKeyState (MotionEngine.ReadInput), runs the opposite-key filter and MotionEngine.Update in Live mode, and
// draws the engine's public outputs. DriveEngine = false + SetHostInput(...) lets a host drive the engine instead.
//
// Layout: DashLayout (DashboardLogic.cs) computes every rectangle from the client size, the DPI and the measured
// fonts; v4.3.1: one fixed text size (made once, Windows DPI applies), a resize only re-lays out. Wide pages: chart +
// 实时数据 card on top, terminal + 按键 card below. Narrow pages: the data card stacks under the chart.
//
// v4.1 (DASH41F): offsets are shown as 百分比偏移 (OffsetUnits: % of half the picture height, arrows); the chart is in %
// with a range fixed per profile (DashRange); four HUD widgets in the chart corners show the live maths (运动方程 m = m0 +
// (m* - m0)·c(u), 换算 % = 100·v ÷ (H/2) with k and the FOV ratio, a scrolling 计算流 and the c(u) curves), plus the offset
// vector arrow + angle arc on the ring and a column-vector bracket around X / Y in 实时数据. DASH41G: 运动方程 and 计算流
// in 百分比偏移 too (only 换算 shows px: it is the px -> % conversion), the HUD boxes on one 2 x 2 grid, one value column.
//
// Cost (v4.1): a full-size FRAME bitmap holds the finished picture and a STATIC bitmap the cards, grid, labels and HUD
// frames. Each tick only marks the rectangles whose content changed (DashDirty: the ring + trail box, a changed row, a cap,
// the typing line, a HUD line, a blinking cursor). OnPaint restores those rectangles from the static layer, redraws the
// dynamic items touching them, and copies its paint rectangle of the frame to the window in ONE DrawImage (no WinForms
// double buffer: one copy fewer). The static layer is rebuilt on resize / DPI / key names, and only the chart part when
// the range or the profile curves change -- never per frame. Ticks: 15 ms while something moves or was pressed in the last
// second, 33 ms when idle (then nothing is painted but the blinking cursors), 100 ms with another window in front, 250 ms
// hidden. Antialiasing only for the ring, trail, arrow, arc, curves and key caps; pens, brushes, fonts, paths and strings
// are cached, and a steady value allocates nothing per frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TarkovCrosshair
{
    public sealed class DashboardPanel : UserControl, IPausable
    {
        // ------------------------------------------------------------------ texts (zh-CN UI; key names may be Latin)
        private const string TitleText = "实时仪表盘 // ∑ 运算核心";
        private const string NoteBackground = "窗口不在前台：已暂停读取按键（回到本窗口即可继续）";
        private const string NoteRingOff = "游戏内准星当前是关闭的；这里的演示照常工作";
        private const string ChartTitle = "准星实时轨迹 · 象限图";
        private const string ChartTag = "// f(t) → (x, y)";
        private const string StatsTitle = "实时数据";
        private const string StatsTag = "// v = (x, y)";
        private const string CapsTitle = "按键";
        private const string CapsTag = "// 输入";
        private const string TermTitle = "~# 运动日志 · 实时输出";
        private const string CentreText = "屏幕中心";
        private const string MemLabel = "内存";   // v4.3 (Leo): only the number next to it, no commentary
        private const string LiveTag = "● 实时";
        private const string PauseTag = "■ 暂停";
        private static readonly string[] HudTitles = new string[] { "∑ 运动方程", "⇄ 换算", "≡ 计算流", "∫ 曲线 c(u)" };
        private static readonly string[] RowLabels = new string[] { "X 偏移", "Y 偏移", "上抬偏移量", "侧身", "持枪手", "方向", "状态" };
        private static readonly string[] StateTexts = BuildStateTexts();

        private const string LogPaused = "> 窗口不在前台：暂停读取按键";
        private const string LogResumed = "> 回到窗口：继续读取按键";
        private static readonly string[] ChartRight = new string[]
        {
            "绿圈 = 这一刻子弹的落点（惯性：向右走，落点偏左）", "绿圈 = 子弹落点（向右走，落点偏左）", "单位 % · Y 向上为正"
        };
        private const string HandRight = "右手";
        private const string HandLeft = "左手（已换手）";

        private static string[] BuildStateTexts()
        {
            // bit 1 = 跳跃中, 2 = 强化准星, 4 = 开镜中, 8 = 冲刺 (Shift + W), 16 = 开火中 (v4.1)
            string[] r = new string[32];
            for (int i = 0; i < 32; i++)
            {
                List<string> parts = new List<string>();
                if ((i & 8) != 0) parts.Add("冲刺");
                if ((i & 1) != 0) parts.Add("跳跃中");
                if ((i & 16) != 0) parts.Add("开火中");
                if ((i & 2) != 0) parts.Add("强化准星");
                if ((i & 4) != 0) parts.Add("开镜中");
                r[i] = parts.Count == 0 ? "—" : string.Join(" · ", parts.ToArray());
            }
            return r;
        }

        // ------------------------------------------------------------------ model
        private readonly MotionEngine engine;
        private GlobalSettings settings;
        private bool driveEngine = true;
        private InputState hostInput;
        private LiveInputFilter filter = new LiveInputFilter();
        private readonly Func<int, bool> isDown = Native.IsDown; // cached delegate (no allocation per poll)
        private readonly HandView view0 = new HandView(), view1 = new HandView();

        // ------------------------------------------------------------------ timing
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastTicks;
        private double nowMs;
        private int interval;
        private bool wasLive;
        private bool foreground = true;
        private double lastFgMs = double.NegativeInfinity;
        private double lastActiveMs = double.NegativeInfinity; // last tick with a key down, the ring moving or text typing
        private int errors;
        private bool failed;
        private bool disposedFlag;
        private bool booted;

        // ------------------------------------------------------------------ live state
        private readonly DashTrail trail = new DashTrail(160, 1200);
        private readonly DashLog log = new DashLog(48);
        private readonly DashRange range = new DashRange();
        private double uiX, uiY;             // ring offset: SCREEN px, UI coords (X right +, Y up +)
        private double pctX, pctY;           // the same in 百分比偏移
        private double halfH = OffsetUnits.ReferenceHalfHeight;
        private double kFactor = 1, baseFov = 75, gameFov = 75;
        // v4.3 (W6): X stretch of a stretched 画面比例 (DashStretch.Ratio, 1 = native), cached with the settings signature
        private double stretchR = 1;
        // v4.3 (W6): 「滤镜：开 · 通用」 at the right end of the header note row (polled twice a second)
        private readonly DashFilterLine filterLine = new DashFilterLine();
        private Rectangle rFilter;
        private long settingsSig;
        private bool settingsSigValid;
        private readonly DashEvents events = new DashEvents();
        private readonly float[] glow = new float[DashKeys.Count];
        private readonly int[] glowBucket = new int[DashKeys.Count];
        private readonly float[] rowFlash = new float[DashLayout.RowCount];
        private readonly int[] rowBucket = new int[DashLayout.RowCount];
        private bool cursorOn;
        private int drawnLogVersion = -1, drawnLogCount = -1, drawnTyped = -1, drawnTypingIdx = -1;
        private string note = "";
        private string hintText = "";
        /// <summary>v5.0.2 (UI L16): the same hint without 「按」 and 「，准星实时跟着动」, drawn when the full one does not fit.</summary>
        private string hintShort = "";
        private int keySig;
        private bool keySigValid;
        private double lastKeyCheckMs = double.NegativeInfinity;
        private readonly string[] capNames = new string[DashKeys.Count];   // the bound key's name (RefreshKeyNames)
        private readonly string[] capLabels = new string[DashKeys.Count];  // what fits on the cap (DoLayout)
        private readonly bool[] capAscii = new bool[DashKeys.Count];
        private readonly bool[] capUseUi = new bool[DashKeys.Count];

        private readonly DashPct txX = new DashPct(), txY = new DashPct(), txRise = new DashPct();
        private readonly DashText txMem = new DashText();
        private string leanText = DashFormat.LeanCentre;
        private string dirText = "静止", handText = HandRight, stateText = "—";
        private double lastTextMs = double.NegativeInfinity;

        // HUD maths
        private readonly DashTravel travelX = new DashTravel(), travelY = new DashTravel();
        private readonly DashMathText math = new DashMathText();
        private readonly DashStream stream = new DashStream(6);
        private char hudAxis = 'X';
        private double lastStreamMs = double.NegativeInfinity, streamStateMs, streamLastX = double.NaN, streamLastY = double.NaN;
        private int drawnStreamVersion = -1;
        private double hudDecodeStartMs = double.NaN;
        private int hudDecodeFrame = -1;
        private const double HudDecodeMs = 260;
        private readonly char[] hudBuf = new char[64];
        private readonly float[] dotU = new float[2], dotC = new float[2];
        private readonly bool[] dotOn = new bool[2];

        private Process proc;
        private double lastMemMs = double.NegativeInfinity;

        private readonly char[] decodeBuf = new char[TitleText.Length];
        private string titleShown = TitleText;
        private double decodeStartMs = double.NaN;
        private int decodeFrame = -1;
        private const double DecodeMs = 900;

        // ------------------------------------------------------------------ layout (pixels)
        private float s = 1f;          // pixels per design unit (DPI scale x zoom)
        private bool fontsMade;
        private bool layoutDirty = true;
        private readonly DashLayout lay = new DashLayout();
        private Rectangle rHeader, rTitle, rNote, rRight1, rRight2, rChart, rChartTitle, rChartInner, rStats, rStatsTitle, rLive;
        private Rectangle rTerm, rTermTitle, rTermInner, rCapsCard, rCapsTitle, rCombo;
        private readonly string[] comboTexts = new string[1 << DashKeys.Count]; // "> W+D" per key mask, built once per binding set
        private string[] logNames = DashKeys.Names;
        private int shownMask;
        private Rectangle rMemVal, rMemLab;
        private readonly Rectangle[] rRows = new Rectangle[DashLayout.RowCount];
        private readonly Rectangle[] rValues = new Rectangle[DashLayout.RowCount];
        private int rowsShown;
        private bool brackets;
        private Rectangle rBracket;
        private readonly Rectangle[] rCaps = new Rectangle[DashKeys.Count];
        private readonly Rectangle[] rCapGlow = new Rectangle[DashKeys.Count];
        private readonly GraphicsPath[] capPaths = new GraphicsPath[DashKeys.Count];
        private readonly GraphicsPath[] glowPaths = new GraphicsPath[DashKeys.Count];
        private bool capsShown;
        private float charW;
        private float lineH;
        private float titleW;
        private float uiEm = 14f;
        private Rectangle rPlate;
        private bool plateShown, plateDrawn;
        // HUD widgets (0 运动方程, 1 换算, 2 计算流, 3 曲线)
        private readonly Rectangle[] rHud = new Rectangle[DashHud.Count];
        private readonly int[] hudLines = new int[DashHud.Count];
        private Rectangle rCurvePlot;
        private int hudPad, hudTitleH;

        // ------------------------------------------------------------------ frame + static layer
        private Bitmap frameBmp, staticBmp;
        private Graphics frameG;
        private bool frameValid;
        private bool staticDirty = true;
        private Rectangle staticRegion;            // empty = whole client when staticDirty
        private readonly DashDirty dirty = new DashDirty();
        private Rectangle chartDynPrev;            // what the chart's dynamic layer covered on the last frame
        private double lastTrailPaintMs = double.NegativeInfinity;
        private float drawnPx = float.NaN, drawnPy = float.NaN;

        // ------------------------------------------------------------------ resources
        private static readonly Color ColGreen = Theme.ColRingInner;
        private static readonly Color ColTermBg = Color.FromArgb(0x07, 0x0A, 0x08);
        private static readonly Color ColChartBg = Theme.Blend(Theme.ColBackground, Color.Black, 0.35);
        private static readonly Color ColGreenDim = Theme.Blend(ColTermBg, Theme.ColRingInner, 0.64); // 4.8:1 on the terminal (SPEC: >= 4.5:1)
        private static readonly Color ColGreenMid = Theme.Blend(ColTermBg, Theme.ColRingInner, 0.70);
        private static readonly Color ColBright = Color.FromArgb(0x7C, 0xFF, 0x7C);
        private static readonly Color ColTag = Theme.Blend(Theme.ColSurface, Theme.ColRingInner, 0.75); // 6.1:1 on the cards, 6.6:1 on the chart (SPEC >= 4.5:1)
        private static readonly Color ColHudBg = Theme.Blend(ColChartBg, Color.Black, 0.30);
        private static readonly Color ColHudText = Theme.Blend(ColHudBg, Theme.ColRingInner, 0.72);   // >= 4.5:1 on the HUD plate
        private static readonly Color ColHudFormula = Theme.Blend(ColHudBg, Theme.ColMuted, 0.95);
        private static readonly Color ColCyan = Theme.ColAccent2;

        private Font fTitle, fBody, fSmall, fMono, fMonoSmall, fValue, fCap, fCapUi, fTag, fTermUi;
        private SolidBrush bBack, bSurface, bChartBg, bText, bMuted, bAccent, bGreen, bGreenDim, bGreenMid, bTermBg, bWarn, bTag;
        private SolidBrush bHudBg, bHudText, bHudFormula, bHudNew, bCyan, bArrow;
        private Pen pBorder, pGrid, pGridMinor, pAxis, pVector, pRingGlow, pRingOuter, pRingInner, pOrigin, pTermBorder, pHeaderLine;
        private Pen pBracket, pProj, pTick, pPlate, pHudFrame, pCurveX, pCurveY, pCurveAxis, pArc, pMatrix;
        private readonly Pen[] trailPens = new Pen[12];
        private readonly SolidBrush[] capFill = new SolidBrush[4];
        private readonly SolidBrush[] capGlowFill = new SolidBrush[4];
        private readonly SolidBrush[] capTextB = new SolidBrush[4];
        private readonly SolidBrush[] valueB = new SolidBrush[4];
        private readonly SolidBrush[] riseB = new SolidBrush[4];
        private readonly Pen[] capBorder = new Pen[4];
        private TextureBrush scanBrush;
        private Bitmap scanBmp;
        private StringFormat sfTypo, sfTypoEllipsis, sfCenter, sfRight, sfLeftMid, sfRightMid;
        private StringFormat sfLeftTop, sfRightTop, sfLeftBottom, sfRightBottom;
        private readonly Dictionary<int, string> gridLabels = new Dictionary<int, string>();
        private readonly PointF[] arrowHead = new PointF[3];
        // trail polylines: one per alpha bucket, from a pool of point arrays by length (no allocation once warm)
        private readonly PointF[][] polyPool = new PointF[162][];
        private readonly PointF[] trailPts = new PointF[162];
        private readonly int[] trailBucket = new int[162];
        // terminal line widths (measured once per line, small LRU by string reference)
        private readonly string[] measuredLine = new string[16];
        private readonly float[] measuredW = new float[16];
        private readonly string[] measuredFit = new string[16];
        private readonly int[] measuredFitW = new int[16];
        private int measuredNext;
        // one-entry "fits with …" caches: header note, key-combo read-out, 实时数据 values
        private string noteFitSrc, noteFit, comboFitSrc, comboFit;
        private int noteFitW, comboFitW;
        private readonly string[] rowFitSrc = new string[DashLayout.RowCount], rowFit = new string[DashLayout.RowCount];
        private readonly int[] rowFitW = new int[DashLayout.RowCount];

        // ------------------------------------------------------------------ perf counters (read by the render harness / tests)
        internal int PaintCount, RegionCount, FrozenPaints, LayoutCount, TickCount, ReleaseCount;
        internal double LastPaintMs, RenderMsSum, BlitMsSum, ChartMsSum, StaticMsSum, CardsMsSum, TermMsSum, FrozenMsSum;
        internal long PaintedPixels;

        // ------------------------------------------------------------------ drag freeze + memory
        private bool paused;            // between Pause and Resume (UI41F: the settings window is being moved / resized)
        private bool framePainted;      // the frame bitmap holds a finished picture
        private const double ReleaseAfterMs = 5000;

        // ================================================================== public API

        public DashboardPanel(MotionEngine engine, GlobalSettings g)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (g == null) throw new ArgumentNullException("g");
            this.engine = engine;
            this.settings = g;

            // v4.1: the panel keeps its own finished frame; WinForms' double buffer would only add a copy
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            SetStyle(ControlStyles.Selectable, false);
            DoubleBuffered = false;
            TabStop = false;
            BackColor = Theme.ColBackground;
            ForeColor = Theme.ColText;

            CreateResources();
            RefreshKeyNames();
            try { CheckSettings(g); } catch (Exception) { settingsSigValid = false; }
            PollFilter();
            try { proc = Process.GetCurrentProcess(); } catch { proc = null; }

            timer = new System.Windows.Forms.Timer();
            timer.Tick += OnTick;
            SetInterval(15);
            lastTicks = clock.ElapsedTicks;
        }

        /// <summary>The settings object read every tick (keys, profile, modes). Set it again if the host replaces its GlobalSettings.</summary>
        public GlobalSettings Settings
        {
            get { return settings; }
            set
            {
                if (value == null) return;
                settings = value;
                keySigValid = false;
                settingsSigValid = false;
                lastKeyCheckMs = double.NegativeInfinity;
            }
        }

        /// <summary>True (default): the panel reads keys and updates the engine itself. False: the host updates the engine and
        /// hands the raw key state in with SetHostInput (the key caps / log then show that state).</summary>
        public bool DriveEngine
        {
            get { return driveEngine; }
            set { driveEngine = value; }
        }

        /// <summary>Raw (unfiltered) key state from the host, used when DriveEngine is false.</summary>
        public void SetHostInput(InputState raw) { hostInput = raw; }

        /// <summary>Adds a line to the terminal box.</summary>
        public void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            log.Add(line, nowMs);
        }

        /// <summary>Clears the trail and the log, resets the engine and plays the boot lines again.</summary>
        public void ResetView()
        {
            if (driveEngine) engine.Reset();
            trail.Clear();
            log.Clear();
            stream.Clear();
            range.Reset();
            travelX.Reset(); travelY.Reset();
            filter = new LiveInputFilter();
            events.Reset();
            booted = false;
            staticDirty = true; staticRegion = Rectangle.Empty;
            frameValid = false;
            Invalidate();
        }

        /// <summary>
        /// v4.1 window drag freeze (IPausable; SettingsForm calls it through Theme.PauseTree on ResizeBegin = WM_ENTERSIZEMOVE).
        /// Stops the timer, so no key reading, engine update, layout or new frame happens; WM_PAINT only copies the last
        /// finished frame (newly exposed area = plain background). Calling it again does nothing.
        /// </summary>
        public void Pause()
        {
            if (paused || disposedFlag) return;
            paused = true;
            try { timer.Stop(); } catch (Exception) { }
        }

        /// <summary>
        /// Ends Pause (ResizeEnd = WM_EXITSIZEMOVE): one redraw -- with one relayout at the final size and text size when the
        /// size or DPI changed meanwhile -- then the timer runs again (only while the control has a window; a hidden page just
        /// goes back to its slow hidden tick). A Resume without a Pause does nothing.
        /// </summary>
        public void Resume()
        {
            if (!paused) return;
            paused = false;
            if (disposedFlag) return;
            lastTicks = clock.ElapsedTicks;          // the frozen time is not one long frame
            Wake();
            Invalidate();
        }

        // ---- v4.3 (W6, V43-PERF): the timer is fully stopped while nobody can see the page or another program is in front

        private Form hostForm;
        private bool sleeping, sleepAfterFrame;

        /// <summary>True when the timer is stopped because the page / window is hidden or another program is in front.</summary>
        public bool Sleeping { get { return sleeping; } }

        private bool WakeAllowed()
        {
            Form f = null;
            try { f = FindForm(); } catch (Exception) { f = null; }
            int fg = -1;
            try { fg = Native.ForegroundIsOurs(); } catch (Exception) { fg = -1; }
            return DashWake.ShouldRun(Visible && Width >= 8 && Height >= 8, IsHandleCreated, f != null && f.Visible,
                f != null && f.WindowState == FormWindowState.Minimized, fg, paused, failed, disposedFlag);
        }

        /// <summary>Starts the timer again when the page can be seen and our app is in front (else it stays stopped).</summary>
        private void Wake()
        {
            AttachForm();
            if (!WakeAllowed()) { Sleep(); return; }
            bool was = sleeping || !timer.Enabled;
            sleeping = false;
            if (!was) return;
            lastTicks = clock.ElapsedTicks;   // the stopped time is not one long frame
            if (!foreground) { foreground = true; log.Add(LogResumed, clock.ElapsedTicks * 1000.0 / Stopwatch.Frequency); }
            try { timer.Start(); } catch (Exception) { }
            Invalidate();
        }

        /// <summary>Stops the timer completely. v4.3.1 (Alt+Tab lag): the frame + static pictures are KEPT while asleep (hidden
        /// page, minimised, game in front), so the first paint after the window comes back is a plain copy of the last frame,
        /// not a full rebuild of every card, text cache and bitmap. They are freed with the panel (Dispose).</summary>
        private void Sleep()
        {
            if (disposedFlag) return;
            try { timer.Stop(); } catch (Exception) { }
            if (paused) return;   // a window drag keeps its own state
            sleeping = true;
            if (wasLive) { wasLive = false; filter = new LiveInputFilter(); }
        }

        /// <summary>Follows the window the panel lives in (Activated = our app came back in front; Deactivate = another
        /// program or window took it; VisibleChanged / Resize = shown, hidden, minimised, restored).</summary>
        private void AttachForm()
        {
            Form f = null;
            try { f = FindForm(); } catch (Exception) { f = null; }
            if (ReferenceEquals(f, hostForm)) return;
            DetachForm();
            hostForm = f;
            if (f == null) return;
            f.Activated += OnHostWake;
            f.VisibleChanged += OnHostWake;
            f.Resize += OnHostWake;
            f.Deactivate += OnHostDeactivate;
        }

        private void DetachForm()
        {
            if (hostForm == null) return;
            try
            {
                hostForm.Activated -= OnHostWake;
                hostForm.VisibleChanged -= OnHostWake;
                hostForm.Resize -= OnHostWake;
                hostForm.Deactivate -= OnHostDeactivate;
            }
            catch (Exception) { }
            hostForm = null;
        }

        private void OnHostWake(object sender, EventArgs e) { if (!disposedFlag) Wake(); }

        /// <summary>Deactivate also fires when one of OUR windows (tutorial, a dialog) takes the focus; the timer then keeps
        /// running (process-level foreground, as the key reading uses). The check runs after the switch has finished.</summary>
        private void OnHostDeactivate(object sender, EventArgs e)
        {
            if (disposedFlag || !IsHandleCreated) return;
            try { BeginInvoke(new MethodInvoker(delegate { if (!disposedFlag && !WakeAllowed()) Sleep(); })); }
            catch (Exception) { }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (disposedFlag) return;
            if (Visible) Wake(); else Sleep();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (!disposedFlag) AttachForm();
        }

        /// <summary>True between Pause and Resume.</summary>
        public bool Paused { get { return paused; } }

        // ================================================================== lifecycle

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!disposedFlag && !paused) { sleeping = true; Wake(); }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            timer.Stop();
            base.OnHandleDestroyed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            layoutDirty = true;
            staticDirty = true; staticRegion = Rectangle.Empty;
            frameValid = false;
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            layoutDirty = true;
            staticDirty = true; staticRegion = Rectangle.Empty;
            frameValid = false;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposedFlag)
            {
                disposedFlag = true;
                DetachForm();
                try { timer.Stop(); timer.Tick -= OnTick; timer.Dispose(); } catch { }
                DisposeResources();
                DisposeBuffers();
                for (int i = 0; i < DashKeys.Count; i++)
                {
                    if (capPaths[i] != null) { capPaths[i].Dispose(); capPaths[i] = null; }
                    if (glowPaths[i] != null) { glowPaths[i].Dispose(); glowPaths[i] = null; }
                }
                if (proc != null) { try { proc.Dispose(); } catch { } proc = null; }
            }
            base.Dispose(disposing);
        }

        /// <summary>Frees the frame + static bitmaps (the only large caches: 2 x client width x height x 4 bytes) and the text
        /// caches. Everything is rebuilt by the next paint. Used when the 首页 page has been hidden for ReleaseAfterMs.</summary>
        private void ReleaseCaches()
        {
            DisposeBuffers();
            framePainted = false;
            frameValid = false;
            staticDirty = true; staticRegion = Rectangle.Empty;
            gridLabels.Clear();
            Array.Clear(measuredLine, 0, measuredLine.Length);
            Array.Clear(measuredFit, 0, measuredFit.Length);
            Array.Clear(polyPool, 0, polyPool.Length);
            Array.Clear(comboTexts, 0, comboTexts.Length);
            Array.Clear(rowFitSrc, 0, rowFitSrc.Length);
            Array.Clear(rowFit, 0, rowFit.Length);
            noteFitSrc = noteFit = comboFitSrc = comboFit = null;
            ReleaseCount++;
        }

        private void DisposeBuffers()
        {
            // the GDI+ wrappers first (they point into the DIB memory), then the DC and the DIB section
            if (frameG != null) { frameG.Dispose(); frameG = null; }
            if (frameBmp != null) { frameBmp.Dispose(); frameBmp = null; }
            if (dibDC != IntPtr.Zero) { try { SelectObject(dibDC, dibOld); DeleteDC(dibDC); } catch { } dibDC = IntPtr.Zero; dibOld = IntPtr.Zero; }
            if (dibSection != IntPtr.Zero) { try { DeleteObject(dibSection); } catch { } dibSection = IntPtr.Zero; }
            if (staticBmp != null) { staticBmp.Dispose(); staticBmp = null; }
        }

        // ---------------------------------------------------------------- frame buffer: a GDI DIB section on Windows
        // GDI+ draws into the DIB's memory through a Bitmap wrapper (same drawing code as anywhere else); WM_PAINT copies
        // the paint box to the window with one GDI BitBlt (the cheapest screen copy there is). Where that is not available
        // (Mono, a failed call) the frame is a plain Bitmap copied with Graphics.DrawImage.

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int bmiColors; // one RGBQUAD (unused for 32-bit BI_RGB)
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        private const uint SrcCopy = 0x00CC0020;

        private IntPtr dibSection, dibOld, dibDC;
        private bool dibFailed;

        private Bitmap CreateFrameBitmap(int w, int h)
        {
            if (!dibFailed)
            {
                try
                {
                    BitmapInfo bi = new BitmapInfo();
                    bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0; // top-down BI_RGB
                    IntPtr bits;
                    IntPtr hb = CreateDIBSection(IntPtr.Zero, ref bi, 0, out bits, IntPtr.Zero, 0);
                    if (hb != IntPtr.Zero && bits != IntPtr.Zero)
                    {
                        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
                        if (dc != IntPtr.Zero)
                        {
                            IntPtr old = SelectObject(dc, hb);
                            Bitmap b = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                            dibSection = hb; dibDC = dc; dibOld = old;
                            return b;
                        }
                        DeleteObject(hb);
                    }
                }
                catch (Exception) { }
                dibFailed = true;
            }
            return new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        }

        private void SetInterval(int ms)
        {
            if (ms == interval) return;
            interval = ms;
            timer.Interval = ms;
        }

        private bool CanAnimate()
        {
            if (!Visible || Width < 8 || Height < 8 || !IsHandleCreated) return false;
            Form f = FindForm();
            if (f == null || !f.Visible || f.WindowState == FormWindowState.Minimized) return false;
            return true;
        }

        // ================================================================== tick (UI thread)

        private void OnTick(object sender, EventArgs e)
        {
            if (disposedFlag || failed || paused) return;
            sleepAfterFrame = false;
            try
            {
                Tick();
                if (sleepAfterFrame) { sleepAfterFrame = false; Sleep(); }
            }
            catch (Exception)
            {
                errors++;
                if (errors >= 50)
                {
                    failed = true;
                    timer.Stop();
                    frameValid = false;
                    Invalidate();
                }
            }
        }

        /// <summary>Hint line, key-cap labels: rebuilt only when a shown key is rebound (checked twice a second).</summary>
        private void RefreshKeyNames()
        {
            KeyBindings k = settings != null ? settings.Keys : null;
            keySig = DashKeyNames.Signature(k);
            keySigValid = true;
            hintText = DashKeyNames.HintLine(k);
            hintShort = DashKeyNames.HintLineShort(k);
            logNames = DashKeyNames.LogNames(k);
            events.Names = logNames;
            Array.Clear(comboTexts, 0, comboTexts.Length);
            for (int i = 0; i < DashKeys.Count; i++)
            {
                bool ascii;
                capNames[i] = DashKeyNames.Cap(k, i, out ascii);
                capLabels[i] = capNames[i];
                capAscii[i] = ascii;
            }
            layoutDirty = true;   // cap labels are fitted to the caps in DoLayout
            staticDirty = true; staticRegion = Rectangle.Empty;
            frameValid = false;
        }

        /// <summary>Everything the chart's static layer and the % depend on: picture size, k, FOVs, the profile's range and
        /// curves. Checked twice a second (no allocation); a change re-renders the chart part of the static layer once.</summary>
        private void CheckSettings(GlobalSettings g)
        {
            if (g == null) return;
            Profile p = g.Active;
            double hh = OffsetUnits.HalfHeight(g), k = 1;
            try { k = ViewScale.Factor(g); } catch (Exception) { k = 1; }
            double sr = DashStretch.Ratio(g);   // v4.3: 画面比例 stretch (resolution / 画面比例 / FOV all feed the signature)
            double floor = DashRange.ProfilePeakPct(p, g, view0, view1);
            long sig = 17;
            sig = Mix(sig, hh); sig = Mix(sig, k); sig = Mix(sig, floor); sig = Mix(sig, g.ResBaseFov); sig = Mix(sig, g.ResFov);
            sig = Mix(sig, sr); sig = Mix(sig, g.ResGameW); sig = Mix(sig, g.ResGameH);
            sig = unchecked(sig * 31 + (g.ResAspect == null ? 0 : g.ResAspect.GetHashCode()));
            if (p != null)
            {
                sig = CurveSig(sig, p.CurveAD); sig = CurveSig(sig, p.CurveWS);
                sig = unchecked(sig * 31 + (p.Name == null ? 0 : p.Name.GetHashCode()));
            }
            if (settingsSigValid && sig == settingsSig) return;
            settingsSig = sig; settingsSigValid = true;
            halfH = hh; kFactor = k; baseFov = g.ResBaseFov; gameFov = g.ResFov;
            if (sr != stretchR) { stretchR = sr; trail.Clear(); }   // old trail points were in the old X scale
            range.SetFloor(floor);
            math.Invalidate();
            events.Units = g;
            MarkChartStatic();
        }

        /// <summary>v4.3 (W6): 「滤镜：开/关 · 预设名」 from FilterHost's public state (read only; twice a second). Repaints only
        /// rFilter, and only when the text changed.</summary>
        private void PollFilter()
        {
            bool started = false, on = false; string preset = null;
            try
            {
                FilterHost f = Program.ActiveFilter;
                if (f != null) { started = f.Started; on = f.IsOn; preset = f.PresetName; }
            }
            catch (Exception) { started = false; }
            if (filterLine.Update(started, on, preset) && !rFilter.IsEmpty) Dirty(rFilter);
        }

        private static long Mix(long h, double v)
        {
            long b = double.IsNaN(v) ? 0 : BitConverter.DoubleToInt64Bits(v);
            return unchecked(h * 1000003 + b);
        }

        private static long CurveSig(long h, MoveCurve c)
        {
            if (c == null) return unchecked(h * 31 + 7);
            h = Mix(h, c.DurationMs);
            if (c.Speeds != null) for (int i = 0; i < c.Speeds.Length; i++) h = Mix(h, c.Speeds[i]);
            return h;
        }

        /// <summary>The chart part of the static layer changed (range, curves, picture size): re-render only that part.</summary>
        private void MarkChartStatic()
        {
            if (staticDirty && staticRegion.IsEmpty) return; // a full rebuild is already pending
            staticRegion = staticDirty ? Rectangle.Union(staticRegion, rChart) : rChart;
            staticDirty = true;
            if (!rChart.IsEmpty) Dirty(rChart);
            else frameValid = false;
        }

        /// <summary>Marks r for the next frame, 3 px larger on every side: antialiased glyphs and pens bleed up to ~2 px out of
        /// the box they are laid out in, and a full redraw would show that bleed (checked pixel for pixel by the harness).</summary>
        private void Dirty(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            dirty.Add(new DashRect(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6));
        }

        private void Dirty(DashRect r) { dirty.Add(r); }

        private void Tick()
        {
            long t = clock.ElapsedTicks;
            double dt = (t - lastTicks) / (double)Stopwatch.Frequency;
            lastTicks = t;
            nowMs = t * 1000.0 / Stopwatch.Frequency;

            TickCount++;
            if (!CanAnimate())
            {
                // v4.3 (V43-PERF): nobody can see the page: stop the timer completely (Wake starts it on show / restore)
                Sleep();
                return;
            }
            bool becameLive = !wasLive;
            if (becameLive)
            {
                wasLive = true;
                dt = 0;
                decodeStartMs = nowMs; // the title decodes every time the dashboard comes into view
                decodeFrame = -1;
                hudDecodeStartMs = nowMs;
                hudDecodeFrame = -1;
            }
            GlobalSettings g = settings;
            if (!settingsSigValid) CheckSettings(g);
            if (nowMs - lastKeyCheckMs >= 500)
            {
                lastKeyCheckMs = nowMs;
                if (!keySigValid || DashKeyNames.Signature(g.Keys) != keySig) RefreshKeyNames();
                CheckSettings(g);
                PollFilter();
            }
            if (!booted) Boot();

            // Foreground (our process) polled at 4 Hz; keys are only read while our window is in front.
            if (nowMs - lastFgMs >= 250)
            {
                lastFgMs = nowMs;
                int fg = Native.ForegroundIsOurs();
                if (fg != -1)
                {
                    bool now = fg == 1;
                    if (now != foreground && !becameLive) log.Add(now ? LogResumed : LogPaused, nowMs);
                    foreground = now;
                    // v4.3 (V43-PERF): another program (the game) is in front: this tick still draws its frame (暂停 note + log
                    // line), then the timer stops completely; the window's Activated / VisibleChanged starts it again
                    if (!now) sleepAfterFrame = true;
                }
            }
            SetInterval(!foreground ? 100 : (nowMs - lastActiveMs < 1000 ? 15 : 33));

            InputState raw;
            int dirIdx;
            bool sprint, firing;
            Profile p = g.Active;
            if (driveEngine)
            {
                raw = new InputState();
                if (foreground && g.Keys != null) raw = MotionEngine.ReadInput(g.Keys, isDown);
                InputState resolved = filter.Next(raw, true, true);
                sprint = resolved.Shift && resolved.Fwd;    // 冲刺 = Shift + resolved W (MotionEngine's rule)
                firing = resolved.Fire;
                dirIdx = MotionEngine.DirIndex(resolved); // the same table MotionEngine.Update uses for this input
                if (p != null)
                {
                    engine.GameFocused = true;   // the dashboard is its own window: the 只在塔科夫前台 gate does not apply here
                    engine.OwnForeground = true;
                    engine.Update(dt, resolved, p, g, PreviewMode.Live, 1.0);
                }
            }
            else { raw = hostInput; dirIdx = MotionEngine.DirIndex(raw); sprint = raw.Shift && raw.Fwd; firing = raw.Fire; }
            if (p != null)
            {
                try { p.FillView(0, view0); p.FillView(1, view1); } catch (Exception) { } // other readers of view0/view1 (AxisTarget)
                // v5.1 (W26, decision 7): the 运动方程 HUD shows the engine's running slow-start leg, exactly
                travelX.StepLeg(engine, 0);
                travelY.StepLeg(engine, 1);
            }

            bool live = !layoutDirty && frameValid; // dirty rectangles need a valid layout + frame; else the next paint does all
            float cx, cy, cpp;
            ChartGeom(out cx, out cy, out cpp);

            // ---- position (+ %), trail, range
            double x = engine.OffsetX, y = -engine.OffsetY; // UI coords: X right +, Y up +
            if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
            if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
            // v4.3 (W6): the chart and the % use the PROFILE scale (X ÷ the 画面比例 stretch), so X % here = the settings pages'
            // X %; x stays the real screen px for the 换算 HUD and the log (DashFormat / DashStream divide it themselves)
            double xs = stretchR == 1.0 ? x : x / stretchR;
            bool moved = Math.Abs(xs - uiX) > 0.005 || Math.Abs(y - uiY) > 0.005;
            uiX = xs; uiY = y;
            pctX = OffsetUnits.ScreenToPct(xs, g); pctY = OffsetUnits.ScreenToPct(y, g);
            trail.Add(nowMs, xs, y, 0.2);
            int trailBefore = trail.Count;
            trail.Trim(nowMs);
            bool trailChanged = trail.Count > 0 || trailBefore > 0;
            double peakPx = Math.Max(trail.AbsPeak(), Math.Max(Math.Abs(xs), Math.Abs(y)));
            if (range.Update(peakPx * 100.0 / (halfH > 0 ? halfH : OffsetUnits.ReferenceHalfHeight), nowMs))
            {
                MarkChartStatic();
                ChartGeom(out cx, out cy, out cpp);
            }

            // ---- keys, events
            int mask = DashKeys.FromInput(raw);
            if (mask != shownMask)
            {
                shownMask = mask;
                if (live) Dirty(rCombo);
                hudDecodeStartMs = nowMs; hudDecodeFrame = -1;   // a new key state: the HUD numbers re-decode
                streamStateMs = nowMs;
            }
            bool hand = engine.LiveSwitched, boost = engine.LiveBoost;
            bool ads = engine.AdsActive;
            events.Units = g;
            events.Step(mask, hand, boost, ads, x, y, nowMs, log);

            float fall = (float)(dt / 0.18);
            for (int i = 0; i < DashKeys.Count; i++)
            {
                bool down = (mask & DashKeys.Bits[i]) != 0;
                float gl = glow[i];
                if (down) gl = 1f;
                else if (gl > 0f) { gl -= fall; if (gl < 0f) gl = 0f; }
                glow[i] = gl;
                int b = (int)Math.Round(gl * 3f);
                if (b != glowBucket[i]) { glowBucket[i] = b; if (live && capsShown) Dirty(rCapGlow[i]); }
            }

            // ---- texts (20 Hz); a changed value flashes bright and fades back ("decoded" feel)
            if (nowMs - lastTextMs >= 50)
            {
                lastTextMs = nowMs;
                if (txX.Set(pctX, OffsetAxis.X)) Flash(0);
                if (txY.Set(pctY, OffsetAxis.Y)) Flash(1);
                if (txRise.Set(Math.Abs(OffsetUnits.ScreenToPct(engine.Recoil * kFactor, g)), OffsetAxis.Y)) Flash(2);
                // v4.1 (DOCCHECK41 D5): 居中 / 左侧身 / 右侧身 (+「（过渡中）」), no % under the 百分比偏移 rows; fixed strings
                string lt = DashFormat.LeanText(engine.Lean);
                if (!ReferenceEquals(lt, leanText)) { leanText = lt; Flash(3); }
                string hn = engine.Hand == 1 ? HandLeft : HandRight;
                if (!ReferenceEquals(hn, handText)) { handText = hn; Flash(4); }
                string dn = dirIdx >= 0 && dirIdx < Profile.DirNames.Length ? Profile.DirNames[dirIdx] : "静止";
                if (!ReferenceEquals(dn, dirText)) { dirText = dn; Flash(5); }
                int si = (engine.Jumping ? 1 : 0) | (engine.Boosted ? 2 : 0) | (ads ? 4 : 0) | (sprint ? 8 : 0) | (firing && !ads ? 16 : 0);
                string st = StateTexts[si];
                if (!ReferenceEquals(st, stateText)) { stateText = st; Flash(6); }
                string nt = !foreground ? NoteBackground : (!g.Enabled ? NoteRingOff : hintText);
                if (!ReferenceEquals(nt, note))
                {
                    note = nt;
                    if (live) { Dirty(rNote); if (rNote.Height <= 0) Dirty(rChartTitle); }
                }

                // HUD: 运动方程 follows the travelling axis with the bigger move (X on a tie) and keeps the last one when both
                // are still; 换算 shows the live vector
                if (travelX.Active || travelY.Active)
                {
                    double mx = travelX.Active ? Math.Abs(travelX.Target - travelX.Start) : -1;
                    double my = travelY.Active ? Math.Abs(travelY.Target - travelY.Start) : -1;
                    hudAxis = my > mx ? 'Y' : 'X';
                }
                if (math.UpdateA(hudAxis, hudAxis == 'Y' ? travelY : travelX, g) && live) Dirty(rHud[0]);
                if (math.UpdateB(x, y, pctX, pctY, kFactor, halfH, baseFov, gameFov, stretchR) && live) Dirty(rHud[1]);
                // curve dots (u, c) per axis
                for (int a = 0; a < 2; a++)
                {
                    DashTravel tr = a == 0 ? travelX : travelY;
                    bool on = tr.Active;
                    float u = (float)tr.U, c = (float)tr.C;
                    if (on != dotOn[a] || (on && (Math.Abs(u - dotU[a]) > 0.004f || Math.Abs(c - dotC[a]) > 0.004f)))
                    {
                        dotOn[a] = on; dotU[a] = u; dotC[a] = c;
                        if (live) Dirty(rHud[3]);
                    }
                }
            }
            float fade = (float)(dt / 0.45);
            for (int i = 0; i < rowFlash.Length; i++)
            {
                float v = rowFlash[i];
                if (v > 0f) { v -= fade; if (v < 0f) v = 0f; rowFlash[i] = v; }
                int b = (int)Math.Ceiling(v * 3f - 0.001f);
                if (b < 0) b = 0;
                if (b != rowBucket[i]) { rowBucket[i] = b; if (live && i < rowsShown) Dirty(rValues[i]); }
            }

            // ---- 计算流: one sample every 100 ms while the ring moves
            if (nowMs - lastStreamMs >= 100)
            {
                bool changed = double.IsNaN(streamLastX) || Math.Abs(x - streamLastX) > 0.005 || Math.Abs(y - streamLastY) > 0.005;
                if (changed)
                {
                    lastStreamMs = nowMs;
                    streamLastX = x; streamLastY = y;
                    stream.Add(DashStream.Sample((nowMs - streamStateMs) / 1000.0, x, y, g));
                }
            }
            if (stream.Version != drawnStreamVersion) { drawnStreamVersion = stream.Version; if (live) Dirty(rHud[2]); }

            // ---- memory (1 s)
            if (nowMs - lastMemMs >= 1000)
            {
                lastMemMs = nowMs;
                if (ReadMemory() && live) Dirty(rMemVal);
            }

            // ---- terminal + title + blinking cursors
            bool typing = log.Typing(nowMs);
            bool cur = typing || ((long)(nowMs / 530.0) % 2) == 0;
            if (cur != cursorOn)
            {
                cursorOn = cur;
                if (live) { Dirty(TitleCursorRect()); Dirty(rLive); Dirty(NewestLineRect()); }
            }
            // (not live: the next paint redraws everything and SyncDrawnTerminal records what it drew -- the row maths needs the
            // real layout, so nothing is tracked before it exists)
            if (live)
            {
                int ti = TypingIndex(), tv = ti >= 0 ? log.Visible(ti, nowMs) : -1;
                if (log.Version != drawnLogVersion || log.Count != drawnLogCount)
                {
                    drawnLogVersion = log.Version; drawnLogCount = log.Count; drawnTypingIdx = ti; drawnTyped = tv;
                    Dirty(rTermInner);   // a new line scrolls the whole box
                }
                else if (ti != drawnTypingIdx || tv != drawnTyped)
                {
                    if (drawnTypingIdx >= 0) Dirty(RowRect(drawnTypingIdx));   // the line that just finished (final glyphs, "…")
                    if (ti >= 0) Dirty(RowRect(ti));
                    Dirty(NewestLineRect());                                    // the cursor sits on the newest line
                    drawnTypingIdx = ti; drawnTyped = tv;
                }
            }
            if (!double.IsNaN(decodeStartMs))
            {
                double prog = (nowMs - decodeStartMs) / DecodeMs;
                int frame = (int)((nowMs - decodeStartMs) / 40.0); // new scramble 25 times a second
                if (prog >= 1 || !Theme.AnimationsOn)
                {
                    titleShown = TitleText;
                    decodeStartMs = double.NaN;
                    if (live) Dirty(rTitle);
                }
                else if (frame != decodeFrame)
                {
                    decodeFrame = frame;
                    DashDecode.Fill(decodeBuf, TitleText, prog, frame);
                    titleShown = new string(decodeBuf); // bounded: only during the 0.9 s decode
                    if (live) Dirty(rTitle);
                }
            }
            if (!double.IsNaN(hudDecodeStartMs))
            {
                double prog = (nowMs - hudDecodeStartMs) / HudDecodeMs;
                int frame = (int)((nowMs - hudDecodeStartMs) / 40.0);
                if (prog >= 1 || !Theme.AnimationsOn) { hudDecodeStartMs = double.NaN; hudDecodeFrame = -1; if (live) { Dirty(rHud[0]); Dirty(rHud[1]); } }
                else if (frame != hudDecodeFrame) { hudDecodeFrame = frame; if (live) { Dirty(rHud[0]); Dirty(rHud[1]); } }
            }

            // ---- the chart's dynamic layer: ring, arrow, arc, projections, trail, 屏幕中心 tag
            if (live)
            {
                float lo = rChartInner.X + 2, hi = rChartInner.Right - 2, top = rChartInner.Y + 2, bot = rChartInner.Bottom - 2;
                float px = ClampF(cx + (float)(x * cpp), lo, hi), py = ClampF(cy - (float)(y * cpp), top, bot);
                bool ringMoved = float.IsNaN(drawnPx) || Math.Abs(px - drawnPx) > 0.05f || Math.Abs(py - drawnPy) > 0.05f;
                bool plateNow = PlateVisible(px, py);
                bool plateToggled = plateNow != plateDrawn;
                // only the trail fading (the ring still): ~30 frames a second is enough for a fade; the frame that clears
                // the last segment and every ring move are drawn at once
                bool fadeOnly = !ringMoved && !plateToggled && trailChanged && !(trailBefore > 0 && trail.Count == 0);
                if (fadeOnly && nowMs - lastTrailPaintMs < 30) { }
                else if (ringMoved || trailChanged || plateToggled)
                {
                    lastTrailPaintMs = nowMs;
                    Rectangle box = ChartDynBox(cx, cy, cpp, px, py, plateToggled);
                    Dirty(Rectangle.Union(box, chartDynPrev));
                    chartDynPrev = box;
                    drawnPx = px; drawnPy = py;
                    plateDrawn = plateNow;
                }
            }
            else { drawnPx = float.NaN; plateDrawn = plateShown; }

            if (mask != 0 || moved || typing || !double.IsNaN(decodeStartMs) || trail.Count > 0) lastActiveMs = nowMs;
            RequestPaint();
        }

        /// <summary>Asks Windows to paint the dirty rectangles (or everything when the frame is not valid).</summary>
        private void RequestPaint()
        {
            if (layoutDirty || !frameValid || dirty.All) { Invalidate(); return; }
            for (int i = 0; i < dirty.Count; i++)
            {
                DashRect d = dirty[i];
                Invalidate(new Rectangle(d.X, d.Y, d.W, d.H));
            }
        }

        /// <summary>The movement target m* of one axis for the current keys, with the current hand blend (the engine's
        /// AxisTarget: lean + move are additive since v3.0, so only the direction matters). view0 / view1 = this tick's views.</summary>
        private double AxisTarget(int axis)
        {
            int dir = engine.CurrentDir;
            if (dir < 0 || dir >= Profile.DirCount) return 0;
            double b = engine.HandBlend;
            double a0 = axis == 0 ? view0.DX[dir] : view0.DY[dir], a1 = axis == 0 ? view1.DX[dir] : view1.DY[dir];
            return a0 + (a1 - a0) * b;
        }

        private void Flash(int row)
        {
            if (row >= 0 && row < rowFlash.Length)
            {
                rowFlash[row] = 1f;
                if (!layoutDirty && frameValid && row < rowsShown) Dirty(rValues[row]);
            }
        }

        private void Boot()
        {
            booted = true;
            log.Add("> 仪表盘已启动 · " + Program.Version, nowMs);
            log.Add("> 载入运动核心 ........ 完成", nowMs);
            log.Add("> 只读取键盘鼠标按键，不读取、不修改游戏", nowMs);
            log.Add("> 偏移单位：百分比偏移（屏幕中心到上边缘 = 100%）", nowMs);
            string[] tryLines = DashKeyNames.TryLines(settings != null ? settings.Keys : null);
            for (int i = 0; i < tryLines.Length; i++) log.Add(tryLines[i], nowMs);
        }

        // ---------------------------------------------------------------- memory: GetProcessMemoryInfo (fast), Process fallback

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx counters, uint size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private bool memApiFailed;

        /// <summary>Private bytes of this process. v4.1: psapi GetProcessMemoryInfo (microseconds); .NET Framework's
        /// Process.Refresh + PrivateMemorySize64 snapshots every process on the PC (AUDIT-DASH P2), so it is only the fallback.</summary>
        private bool ReadMemory()
        {
            if (!memApiFailed)
            {
                try
                {
                    ProcessMemoryCountersEx c;
                    if (GetProcessMemoryInfo(GetCurrentProcess(), out c, (uint)Marshal.SizeOf(typeof(ProcessMemoryCountersEx))) && c.PrivateUsage != UIntPtr.Zero)
                        return txMem.Megabytes((long)c.PrivateUsage.ToUInt64(), "", "");
                }
                catch (Exception) { }
                memApiFailed = true;
            }
            if (proc == null) return false;
            try
            {
                proc.Refresh();
                return txMem.Megabytes(proc.PrivateMemorySize64, "", "");
            }
            catch { return false; }
        }

        // ================================================================== layout

        private int S(float px) { return (int)Math.Round(px * s); }

        private static Rectangle R(DashRect r) { return new Rectangle(r.X, r.Y, r.W, r.H); }

        private static DashRect D(Rectangle r) { return new DashRect(r.X, r.Y, r.Width, r.Height); }

        private void DoLayout(Graphics gr)
        {
            layoutDirty = false;
            LayoutCount++;
            int dpi = 96;
            try { dpi = DeviceDpi; } catch { }
            if (dpi <= 0) dpi = 96;
            float ds = dpi / 96f;
            int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);

            // v4.3.1 (Leo): ONE fixed text size, whatever the window size; fonts are made once (Windows DPI applies)
            const double zoom = 1.0;
            if (!fontsMade || fBody == null) { CreateFonts(); fontsMade = true; }
            s = ds;

            // measure the fonts (pixel heights at this Graphics' DPI) and the fixed-width header block
            DashMetrics m = new DashMetrics();
            m.Title = fTitle.GetHeight(gr); m.Body = fBody.GetHeight(gr); m.Small = fSmall.GetHeight(gr);
            m.Value = fValue.GetHeight(gr); m.Mono = Math.Max(fMono.GetHeight(gr), fTermUi.GetHeight(gr)); m.Cap = fCap.GetHeight(gr);
            float memValW = gr.MeasureString("0000.0 MB", fValue, PointF.Empty, sfTypo).Width + 2 * s;
            float memLabW = gr.MeasureString(MemLabel, fSmall, PointF.Empty, sfTypo).Width + 2 * s;
            m.RightW = memValW + S(6) + memLabW;
            titleW = gr.MeasureString(TitleText, fTitle, PointF.Empty, sfTypo).Width;
            lay.Arrange(w, h, ds, zoom, m);

            rHeader = R(lay.Header); rTitle = R(lay.HeaderTitle); rNote = R(lay.HeaderNote);
            // v4.3 (W6): the 滤镜 line takes the right end of the note row when the row is wide enough (else it is not shown)
            rFilter = Rectangle.Empty;
            if (rNote.Height > 0)
            {
                int fw = (int)Math.Ceiling(gr.MeasureString(DashFilterLine.Widest, fSmall, PointF.Empty, sfTypo).Width) + S(4);
                int hw = (int)Math.Ceiling(gr.MeasureString(hintText ?? "", fSmall, PointF.Empty, sfTypo).Width);
                if (rNote.Width >= fw + S(16) + Math.Min(hw, S(220)))
                {
                    rFilter = new Rectangle(rNote.Right - fw, rNote.Y, fw, rNote.Height);
                    rNote = new Rectangle(rNote.X, rNote.Y, rNote.Width - fw - S(16), rNote.Height);
                }
            }
            rRight1 = R(lay.HeaderRight1); rRight2 = R(lay.HeaderRight2);
            rMemVal = new Rectangle(rRight1.Right - (int)memValW, rRight1.Y, (int)memValW, rRight1.Height);
            rMemLab = new Rectangle(Math.Max(rRight1.X, rMemVal.X - S(6) - (int)memLabW), rRight1.Y,
                                    Math.Max(0, Math.Min((int)memLabW, rMemVal.X - S(6) - rRight1.X)), rRight1.Height);
            rChart = R(lay.Chart); rChartTitle = R(lay.ChartTitle); rChartInner = R(lay.ChartInner);
            rStats = R(lay.Data); rStatsTitle = R(lay.DataTitle);
            rTerm = R(lay.Term); rTermTitle = R(lay.TermTitle); rTermInner = R(lay.TermInner);
            rowsShown = lay.RowsShown;
            for (int i = 0; i < rRows.Length; i++)
            {   // a value lives right of its label (so a long 状态 text is cut with "…", never drawn over the label)
                rRows[i] = R(lay.Rows[i]);
                int lw = rRows[i].Width > 0 ? (int)Math.Ceiling(gr.MeasureString(RowLabels[i], fBody, PointF.Empty, sfTypo).Width) + S(10) : 0;
                rValues[i] = new Rectangle(rRows[i].X + lw, rRows[i].Y, Math.Max(0, rRows[i].Width - lw), rRows[i].Height);
                rowFitSrc[i] = null;
            }
            noteFitSrc = null; comboFitSrc = null;
            capsShown = lay.CapsShown;
            rCapsCard = R(lay.Caps); rCapsTitle = R(lay.CapsTitle); rCombo = R(lay.CapsCombo);
            int liveW = (int)Math.Ceiling(Math.Max(gr.MeasureString(LiveTag, fSmall, PointF.Empty, sfTypo).Width,
                                                   gr.MeasureString(PauseTag, fSmall, PointF.Empty, sfTypo).Width)) + S(4);
            rLive = rStatsTitle.Width > S(60) ? new Rectangle(rStatsTitle.Right - liveW, rStatsTitle.Y, liveW, rStatsTitle.Height) : Rectangle.Empty;

            // 实时数据: X / Y as a column vector [x; y] (brackets) when both rows are stacked in one column
            brackets = false;
            if (lay.Columns == 1 && rowsShown >= 2)
            {
                float vw = gr.MeasureString("← 100.00%", fValue, PointF.Empty, sfTypo).Width;
                float lw = Math.Max(gr.MeasureString(RowLabels[0], fBody, PointF.Empty, sfTypo).Width,
                                    gr.MeasureString(RowLabels[1], fBody, PointF.Empty, sfTypo).Width);
                int right = rRows[0].Right - S(2);
                int left = right - (int)Math.Ceiling(vw) - S(16);
                if (left > rRows[0].X + (int)Math.Ceiling(lw) + S(10))
                {
                    brackets = true;
                    rBracket = new Rectangle(left, rRows[0].Y + S(4), right - left, rRows[1].Bottom - rRows[0].Y - S(8));
                    for (int i = 0; i < 2; i++)
                        rValues[i] = new Rectangle(left + S(7), rRows[i].Y, Math.Max(0, right - left - S(14)), rRows[i].Height);
                    // v4.1 (DASH41G) one value column: every other value ends where X / Y end inside the brackets
                    int vr = rValues[0].Right;
                    for (int i = 2; i < rowsShown; i++)
                        if (rValues[i].Width > 0 && vr < rValues[i].Right)
                            rValues[i] = new Rectangle(rValues[i].X, rValues[i].Y, Math.Max(0, vr - rValues[i].X), rValues[i].Height);
                }
            }

            float rad = Math.Max(3f, lay.CapSize * 0.16f);
            for (int i = 0; i < DashKeys.Count; i++)
            {
                rCaps[i] = R(lay.CapRects[i]);
                Rectangle gw = rCaps[i];
                gw.Inflate(S(3) + 2, S(3) + 2);
                rCapGlow[i] = gw;
                if (capPaths[i] != null) { capPaths[i].Dispose(); capPaths[i] = null; }
                if (glowPaths[i] != null) { glowPaths[i].Dispose(); glowPaths[i] = null; }
                if (!capsShown || rCaps[i].Width < 2 || rCaps[i].Height < 2) continue;
                capPaths[i] = Rounded(rCaps[i], rad);
                Rectangle g2 = rCaps[i];
                g2.Inflate(S(3), S(3));
                glowPaths[i] = Rounded(g2, rad + S(3));
                // ASCII name in the monospace font when it fits, else the short Chinese action name (UI font); still too
                // wide: cut with "…". Always from the bound name (capNames), so a bigger window shows the full name again.
                string lab = capNames[i];
                bool ui = !capAscii[i];
                float room = rCaps[i].Width - S(4);
                float aw = gr.MeasureString(lab, ui ? fCapUi : fCap, PointF.Empty, sfTypo).Width;
                if (aw > room && !ui)
                {   // too long for the cap: the short Chinese action name, if that is narrower
                    float cw = gr.MeasureString(DashKeyNames.CapFallback[i], fCapUi, PointF.Empty, sfTypo).Width;
                    if (cw < aw) { lab = DashKeyNames.CapFallback[i]; ui = true; aw = cw; }
                }
                if (aw > room) lab = FitText(gr, lab, ui ? fCapUi : fCap, room);
                capLabels[i] = lab;
                capUseUi[i] = ui;
            }

            ScaledPens();
            MeasureMono(gr);
            LayoutChart(gr);
            Array.Clear(measuredLine, 0, measuredLine.Length);
            chartDynPrev = rChartInner;
            drawnPx = float.NaN;
        }

        /// <summary>屏幕中心 tag + the four HUD widgets (pure placement in DashHud).</summary>
        private void LayoutChart(Graphics gr)
        {
            float cx = rChartInner.X + rChartInner.Width / 2f, cy = rChartInner.Y + rChartInner.Height / 2f;
            float smallH = fSmall.GetHeight(gr);
            SizeF ts = gr.MeasureString(CentreText, fSmall, PointF.Empty, sfTypo);
            rPlate = R(lay.CentreLabel(cx, cy, (int)Math.Ceiling(ts.Width), (int)Math.Ceiling(smallH)));
            plateShown = rChartInner.Contains(rPlate);
            plateDrawn = plateShown;

            hudPad = Math.Max(2, S(5));
            hudTitleH = (int)Math.Ceiling(smallH) + S(3);
            int mono = (int)Math.Ceiling(lineH);
            float lab = gr.MeasureString("-200", fSmall, PointF.Empty, sfTypo).Width;
            int[] needW = new int[DashHud.Count];
            int[] want = new int[] { DashMathText.ALines, DashMathText.BLines, 5, 4 };
            int[] min = new int[] { 2, 2, 2, 3 };   // small windows still get the formula and its value
            // widest line + 1 (v4.1 DASH41G, all in 百分比偏移): 「m0=→ 10.42% m*=← 10.42%」 (23), 「  = ← 100.00%  ↑ 100.00%」 (24),
            // 计算流 「12.34s ← 10.42% ↓ 10.42%」 (24); DashHud.Place then gives each column one width and each row one height
            int[] chars = new int[] { 24, 25, 25, 16 };
            for (int i = 0; i < DashHud.Count; i++)
            {
                float tw = gr.MeasureString(HudTitles[i], fSmall, PointF.Empty, sfTypo).Width;
                needW[i] = (int)Math.Ceiling(Math.Max(chars[i] * charW, tw)) + 2 * hudPad;
            }
            DashRect[] rects = new DashRect[DashHud.Count];
            int tagH = (int)Math.Ceiling(smallH) + S(8) + S(4);
            DashHud.Place(D(rChartInner), cx, cy, tagH, (int)Math.Ceiling(smallH) + S(6), (int)Math.Ceiling(lab) + S(10), S(6),
                hudTitleH, mono, hudPad, needW, want, min, plateShown ? D(rPlate) : new DashRect(0, 0, 0, 0), rects, hudLines);
            for (int i = 0; i < DashHud.Count; i++) rHud[i] = R(rects[i]);
            if (!rHud[3].IsEmpty)
            {
                Rectangle b = rHud[3];
                rCurvePlot = new Rectangle(b.X + hudPad + S(4), b.Y + hudTitleH + hudPad, b.Width - 2 * hudPad - S(8), b.Height - hudTitleH - 2 * hudPad);
                if (rCurvePlot.Width < S(30) || rCurvePlot.Height < S(20)) { rHud[3] = Rectangle.Empty; rCurvePlot = Rectangle.Empty; }
            }
            else rCurvePlot = Rectangle.Empty;
        }

        private static GraphicsPath Rounded(Rectangle r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void MeasureMono(Graphics gr)
        {
            SizeF sz = gr.MeasureString("MMMMMMMMMMMMMMMMMMMM", fMono, PointF.Empty, sfTypo);
            charW = sz.Width / 20f;
            lineH = Math.Max(lay.LineH, 8f);
            uiEm = fTermUi.SizeInPoints * gr.DpiY / 72f;
            if (charW <= 0) charW = 7f * s;
        }

        /// <summary>Chart centre and scale: cpp = chart px per SCREEN px (the chart is in %: ppu px per %).</summary>
        private void ChartGeom(out float cx, out float cy, out float cpp)
        {
            cx = rChartInner.X + rChartInner.Width / 2f;
            cy = rChartInner.Y + rChartInner.Height / 2f;
            float ppu = Ppu();
            double hh = halfH > 0 ? halfH : OffsetUnits.ReferenceHalfHeight;
            cpp = (float)(ppu * 100.0 / hh);
        }

        /// <summary>Chart px per 1 %.</summary>
        private float Ppu()
        {
            float half = Math.Min(rChartInner.Width, rChartInner.Height) / 2f - S(14);
            if (half < 10) half = 10;
            return half / (float)range.Range;
        }

        private bool PlateVisible(float px, float py)
        {
            if (!plateShown) return false;
            float rr = 12f * s + S(6);
            float nx = ClampF(px, rPlate.X, rPlate.Right), ny = ClampF(py, rPlate.Y, rPlate.Bottom);
            return (nx - px) * (nx - px) + (ny - py) * (ny - py) >= rr * rr;
        }

        /// <summary>Everything the chart's dynamic layer draws this frame (ring + glow, arrow, arc, projections + ticks, trail).</summary>
        private Rectangle ChartDynBox(float cx, float cy, float cpp, float px, float py, bool plateToggled)
        {
            float pad = 12f * s + 4f;            // ring glow (8 px pen on an 8 px radius) + antialias
            float padC = S(16) + 4f;             // the angle arc around the origin
            float x0 = Math.Min(cx - padC, px - pad), x1 = Math.Max(cx + padC, px + pad);
            float y0 = Math.Min(cy - padC, py - pad), y1 = Math.Max(cy + padC, py + pad);
            float lo = rChartInner.X + 2, hi = rChartInner.Right - 2, top = rChartInner.Y + 2, bot = rChartInner.Bottom - 2;
            float tp = 3f * s + 3f;
            for (int i = 0; i < trail.Count; i++)
            {
                double tx, ty, tt;
                trail.Get(i, out tx, out ty, out tt);
                float ax = ClampF(cx + (float)(tx * cpp), lo, hi), ay = ClampF(cy - (float)(ty * cpp), top, bot);
                if (ax - tp < x0) x0 = ax - tp;
                if (ax + tp > x1) x1 = ax + tp;
                if (ay - tp < y0) y0 = ay - tp;
                if (ay + tp > y1) y1 = ay + tp;
            }
            Rectangle r = Rectangle.FromLTRB((int)Math.Floor(x0), (int)Math.Floor(y0), (int)Math.Ceiling(x1) + 1, (int)Math.Ceiling(y1) + 1);
            if (plateToggled && plateShown)   // the tag and its leader line from the centre
                r = Rectangle.Union(r, Rectangle.Inflate(Rectangle.Union(rPlate, new Rectangle((int)cx - S(6), (int)cy, S(7), S(7))), 3, 3));
            r.Intersect(rChartInner);
            return r;
        }

        private Rectangle TitleCursorRect()
        {
            return new Rectangle(rTitle.X + (int)titleW + S(5) - 1, rTitle.Y, Math.Max(2, S(7)) + 3, rTitle.Height);
        }

        private int TermFit() { return lineH <= 0 ? 1 : Math.Max(1, (int)(rTermInner.Height / lineH)); }

        /// <summary>The terminal row of log line i (empty when it is scrolled out); i = Count - 1 is the newest.</summary>
        private Rectangle RowRect(int i)
        {
            if (lineH <= 0 || rTermInner.Height < 2) return Rectangle.Empty;
            int count = log.Count, first = Math.Max(0, count - TermFit());
            if (count == 0) i = first = 0;
            if (i < first || (count > 0 && i >= count)) return Rectangle.Empty;
            int row = i - first;
            return new Rectangle(rTermInner.X, rTermInner.Y + (int)Math.Floor(row * lineH), rTermInner.Width, (int)Math.Ceiling(lineH) + 3);
        }

        private Rectangle NewestLineRect() { return RowRect(log.Count - 1); }

        /// <summary>After a full redraw: the terminal state it drew is the new baseline for the per-row dirty tracking.</summary>
        private void SyncDrawnTerminal()
        {
            drawnLogVersion = log.Version; drawnLogCount = log.Count;
            drawnTypingIdx = TypingIndex();
            drawnTyped = drawnTypingIdx >= 0 ? log.Visible(drawnTypingIdx, nowMs) : -1;
        }

        /// <summary>The visible line that is being typed now (lines type one after another, so it is the first unfinished
        /// one), or -1.</summary>
        private int TypingIndex()
        {
            int count = log.Count, first = Math.Max(0, count - TermFit()), ti = -1;
            for (int i = count - 1; i >= first; i--)
            {
                if (log.Visible(i, nowMs) < log.Line(i).Length) ti = i;
                else break;
            }
            return ti;
        }

        // ================================================================== painting

        protected override void OnPaintBackground(PaintEventArgs e) { /* everything is painted in OnPaint */ }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics gr = e.Graphics;
            long t0 = Stopwatch.GetTimestamp();
            if (paused)
            {   // drag freeze: copy the last finished frame, no layout, no new frame
                try { PaintFrozen(gr, e.ClipRectangle); } catch (Exception) { errors++; }
                FrozenPaints++;
                FrozenMsSum += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                return;
            }
            try
            {
                int w = ClientSize.Width, h = ClientSize.Height;
                if (w < 1 || h < 1) return;
                if (layoutDirty) { DoLayout(gr); frameValid = false; staticDirty = true; staticRegion = Rectangle.Empty; }
                EnsureBuffers(w, h, gr.DpiX, gr.DpiY);
                if (staticDirty)
                {
                    long ts = Stopwatch.GetTimestamp();
                    RenderStatic();
                    StaticMsSum += (Stopwatch.GetTimestamp() - ts) * 1000.0 / Stopwatch.Frequency;
                }
                if (!frameValid) { dirty.SetAll(); frameValid = true; }
                long t1 = Stopwatch.GetTimestamp();
                RenderFrame(w, h);
                long t2 = Stopwatch.GetTimestamp();
                RenderMsSum += (t2 - t1) * 1000.0 / Stopwatch.Frequency;
                Rectangle c = e.ClipRectangle;
                c.Intersect(new Rectangle(0, 0, w, h));
                Blit(gr, c);
                BlitMsSum += (Stopwatch.GetTimestamp() - t2) * 1000.0 / Stopwatch.Frequency;
            }
            catch (Exception)
            {
                errors++;
                frameValid = false;
            }
            PaintCount++;
            LastPaintMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Copies c of the finished frame to the window: one GDI BitBlt from the DIB section (Windows), else one
        /// DrawImage. Windows clips it to the update region (= our invalidated rectangles plus anything it uncovered itself),
        /// so the gaps between our rectangles are not drawn.</summary>
        private void Blit(Graphics gr, Rectangle c)
        {
            if (c.Width <= 0 || c.Height <= 0 || frameBmp == null) return;
            gr.CompositingMode = CompositingMode.SourceCopy;
            gr.InterpolationMode = InterpolationMode.NearestNeighbor;
            gr.PixelOffsetMode = PixelOffsetMode.None;
            bool done = false;
            if (dibDC != IntPtr.Zero)
            {
                if (frameG != null) frameG.Flush(FlushIntention.Sync);
                IntPtr hdc = IntPtr.Zero;
                try { hdc = gr.GetHdc(); done = BitBlt(hdc, c.X, c.Y, c.Width, c.Height, dibDC, c.X, c.Y, SrcCopy); }
                catch (Exception) { done = false; }
                finally { if (hdc != IntPtr.Zero) gr.ReleaseHdc(hdc); }
            }
            if (!done) gr.DrawImage(frameBmp, c, c.X, c.Y, c.Width, c.Height, GraphicsUnit.Pixel);
            PaintedPixels += (long)c.Width * c.Height;
            gr.CompositingMode = CompositingMode.SourceOver;
        }

        /// <summary>Paused: the last finished frame where it covers c, plain background where it does not (the panel grew,
        /// or nothing was drawn yet). No layout, no rendering.</summary>
        private void PaintFrozen(Graphics gr, Rectangle c)
        {
            c.Intersect(new Rectangle(0, 0, Math.Max(0, ClientSize.Width), Math.Max(0, ClientSize.Height)));
            if (c.Width <= 0 || c.Height <= 0) return;
            Rectangle f = frameBmp != null && framePainted ? Rectangle.Intersect(c, new Rectangle(0, 0, frameBmp.Width, frameBmp.Height)) : Rectangle.Empty;
            if (f.Width <= 0 || f.Height <= 0) { gr.FillRectangle(bBack, c); return; }
            Blit(gr, f);
            if (c.Right > f.Right) gr.FillRectangle(bBack, f.Right, c.Y, c.Right - f.Right, c.Height);
            if (c.Bottom > f.Bottom) gr.FillRectangle(bBack, c.X, f.Bottom, f.Right - c.X, c.Bottom - f.Bottom);
        }

        private void EnsureBuffers(int w, int h, float dpiX, float dpiY)
        {
            if (frameBmp != null && frameBmp.Width == w && frameBmp.Height == h) return;
            framePainted = false;
            DisposeBuffers();
            float dx = dpiX > 0 ? dpiX : 96f, dy = dpiY > 0 ? dpiY : 96f;
            frameBmp = CreateFrameBitmap(w, h);
            frameBmp.SetResolution(dx, dy); // point-sized fonts render at the screen size
            staticBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            staticBmp.SetResolution(dx, dy);
            frameG = Graphics.FromImage(frameBmp);
            frameG.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            staticDirty = true; staticRegion = Rectangle.Empty;
            frameValid = false;
            dirty.SetAll();
        }

        private void RenderStatic()
        {
            int w = staticBmp.Width, h = staticBmp.Height;
            Rectangle region = staticRegion.IsEmpty ? new Rectangle(0, 0, w, h) : Rectangle.Intersect(staticRegion, new Rectangle(0, 0, w, h));
            staticDirty = false;
            staticRegion = Rectangle.Empty;
            if (region.Width <= 0 || region.Height <= 0) return;
            using (Graphics sg = Graphics.FromImage(staticBmp))
            {
                sg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                sg.SetClip(region);
                DrawStatic(sg, w, h, region);
            }
            if (region.Width >= w && region.Height >= h) dirty.SetAll();
            else Dirty(region);
        }

        private void RenderFrame(int w, int h)
        {
            Rectangle client = new Rectangle(0, 0, w, h);
            if (dirty.All) { RenderRegion(frameG, client); RegionCount++; SyncDrawnTerminal(); }
            else
            {
                for (int i = 0; i < dirty.Count; i++)
                {
                    DashRect d = dirty[i];
                    Rectangle r = Rectangle.Intersect(new Rectangle(d.X, d.Y, d.W, d.H), client);
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    RenderRegion(frameG, r);
                    RegionCount++;
                }
            }
            dirty.Clear();
            framePainted = true;
        }

        /// <summary>Render harness / test hook: the current frame and a from-scratch render of the same state (they must be
        /// pixel-identical: proof that the dirty rectangles cover every change). Not used by the app.</summary>
        internal Bitmap[] DebugFrames()
        {
            if (frameBmp == null || staticBmp == null) return null;
            // the trail fade is drawn at ~30 fps on purpose: bring just the chart's dynamic box up to date first, so any other
            // difference means a change that was never marked dirty
            float cx, cy, cpp;
            ChartGeom(out cx, out cy, out cpp);
            float lo = rChartInner.X + 2, hi = rChartInner.Right - 2, top = rChartInner.Y + 2, bot = rChartInner.Bottom - 2;
            float px = ClampF(cx + (float)(uiX * cpp), lo, hi), py = ClampF(cy - (float)(uiY * cpp), top, bot);
            Rectangle box = ChartDynBox(cx, cy, cpp, px, py, false);
            Dirty(Rectangle.Union(box, chartDynPrev));
            chartDynPrev = box;
            RenderFrame(frameBmp.Width, frameBmp.Height);
            Bitmap full = new Bitmap(frameBmp.Width, frameBmp.Height, PixelFormat.Format32bppPArgb);
            full.SetResolution(frameBmp.HorizontalResolution, frameBmp.VerticalResolution);
            using (Graphics g = Graphics.FromImage(full))
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                RenderRegion(g, new Rectangle(0, 0, full.Width, full.Height));
            }
            return new Bitmap[] { (Bitmap)frameBmp.Clone(), full };
        }

        /// <summary>Restores r from the static layer and redraws every dynamic item touching it (clipped to r).</summary>
        private void RenderRegion(Graphics fg, Rectangle r)
        {
            fg.SetClip(r);
            fg.CompositingMode = CompositingMode.SourceCopy;
            fg.InterpolationMode = InterpolationMode.NearestNeighbor;
            fg.DrawImage(staticBmp, r, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel);
            fg.CompositingMode = CompositingMode.SourceOver;
            if (failed)
            {
                fg.DrawString("仪表盘刷新出错，已停止（不影响游戏内准星）", fBody, bWarn, rChartInner, sfCenter);
                fg.ResetClip();
                return;
            }
            long t0 = Stopwatch.GetTimestamp();
            // an item is redrawn when r touches its box grown by the ~2 px antialias bleed (its pixels inside r are then
            // drawn again; the clip stays r)
            Rectangle ri = Rectangle.Inflate(r, 3, 3);
            if (ri.IntersectsWith(rHeader)) DrawHeaderDynamic(fg, ri);
            if (ri.IntersectsWith(rStats)) DrawStatsDynamic(fg, ri);
            if (capsShown && ri.IntersectsWith(rCapsCard)) DrawCapsDynamic(fg, ri);
            long t1 = Stopwatch.GetTimestamp();
            if (ri.IntersectsWith(rTermInner))
            {
                fg.SetClip(Rectangle.Intersect(r, rTermInner));
                DrawTerminalDynamic(fg, ri);
                fg.SetClip(r);
            }
            long t2 = Stopwatch.GetTimestamp();
            if (ri.IntersectsWith(rChartInner))
            {
                fg.SetClip(Rectangle.Intersect(r, rChartInner));
                DrawChartDynamic(fg, ri);
                fg.SetClip(r);
            }
            long t3 = Stopwatch.GetTimestamp();
            if (rNote.Height <= 0 && ri.IntersectsWith(rChartTitle)) DrawChartTitleNote(fg);
            fg.ResetClip();
            double f = 1000.0 / Stopwatch.Frequency;
            CardsMsSum += (t1 - t0) * f; TermMsSum += (t2 - t1) * f; ChartMsSum += (t3 - t2) * f;
        }

        // ---------------------------------------------------------------- static layer

        /// <summary>Card title: a small accent bar, the Chinese title, then a dim tag (「// v = (x, y)」) that ends before rightLimit.</summary>
        private void DrawCardTitle(Graphics sg, Rectangle r, string title, string tag, int rightLimit)
        {
            if (r.Width < 4 || r.Height < 4) return;
            int bh = Math.Max(2, r.Height / 2 - S(2));
            sg.FillRectangle(bAccent, r.X, r.Y + (r.Height - bh) / 2, Math.Max(2, S(3)), bh);
            Rectangle t = new Rectangle(r.X + S(9), r.Y, Math.Max(0, r.Width - S(9)), r.Height);
            sg.DrawString(title, fBody, bAccent, t, sfLeftMid);
            if (tag == null) return;
            float tw = sg.MeasureString(title, fBody, PointF.Empty, sfTypo).Width;
            int gx = t.X + (int)tw + S(8), gr = Math.Min(t.Right, rightLimit);
            Rectangle g = new Rectangle(gx, r.Y, Math.Max(0, gr - gx), r.Height);
            Font f = MonoSafe(tag) ? fTag : fSmall;
            if (g.Width > S(40) && sg.MeasureString(tag, f, PointF.Empty, sfTypo).Width <= g.Width) sg.DrawString(tag, f, bTag, g, sfLeftMid);
        }

        /// <summary>HUD corner brackets on a card.</summary>
        private void DrawBrackets(Graphics sg, Rectangle r)
        {
            if (r.Width < S(30) || r.Height < S(30)) return;
            int L = S(10);
            int x0 = r.X, y0 = r.Y, x1 = r.Right - 1, y1 = r.Bottom - 1;
            sg.DrawLine(pBracket, x0, y0, x0 + L, y0); sg.DrawLine(pBracket, x0, y0, x0, y0 + L);
            sg.DrawLine(pBracket, x1, y0, x1 - L, y0); sg.DrawLine(pBracket, x1, y0, x1, y0 + L);
            sg.DrawLine(pBracket, x0, y1, x0 + L, y1); sg.DrawLine(pBracket, x0, y1, x0, y1 - L);
            sg.DrawLine(pBracket, x1, y1, x1 - L, y1); sg.DrawLine(pBracket, x1, y1, x1, y1 - L);
        }

        private void DrawStatic(Graphics sg, int w, int h, Rectangle region)
        {
            sg.FillRectangle(bBack, region);

            // header
            if (region.IntersectsWith(rHeader))
            {
                sg.FillRectangle(bSurface, rHeader);
                sg.DrawLine(pHeaderLine, 0, rHeader.Bottom - 1, w, rHeader.Bottom - 1);
                if (rMemLab.Width > 0) sg.DrawString(MemLabel, fSmall, bMuted, rMemLab, sfRightMid);
            }

            // chart frame + title
            if (rChart.Width > 2 && rChart.Height > 2 && region.IntersectsWith(rChart))
            {
                sg.FillRectangle(bChartBg, rChart);
                sg.DrawRectangle(pBorder, rChart.X, rChart.Y, rChart.Width - 1, rChart.Height - 1);
                sg.DrawLine(pBorder, rChart.X, rChartInner.Y - 1, rChart.Right - 1, rChartInner.Y - 1);
                Rectangle tr = new Rectangle(rChart.X + S(10), rChart.Y, rChart.Width - S(20), rChartTitle.Height);
                DrawCardTitle(sg, tr, ChartTitle, ChartTag, int.MaxValue);
                {   // right side of the title bar: what the green ring means (the longest text that fits), else the unit
                    float tw = sg.MeasureString(ChartTitle, fBody, PointF.Empty, sfTypo).Width
                             + sg.MeasureString(ChartTag, fTag, PointF.Empty, sfTypo).Width + S(40);
                    for (int i = 0; i < ChartRight.Length; i++)
                    {
                        if (lay.CompactHeader && i == ChartRight.Length - 1) break;   // short page: no unit text
                        float uw = sg.MeasureString(ChartRight[i], fSmall, PointF.Empty, sfTypo).Width;
                        if (tw + uw < tr.Width) { sg.DrawString(ChartRight[i], fSmall, bMuted, tr, sfRightMid); break; }
                    }
                }
                DrawGrid(sg);
                DrawHudStatic(sg);
                DrawBrackets(sg, rChart);
            }

            // stats card
            if (rStats.Width > 2 && rStats.Height > 2 && region.IntersectsWith(rStats))
            {
                sg.FillRectangle(bSurface, rStats);
                sg.DrawRectangle(pBorder, rStats.X, rStats.Y, rStats.Width - 1, rStats.Height - 1);
                DrawCardTitle(sg, rStatsTitle, StatsTitle, StatsTag, rLive.IsEmpty ? int.MaxValue : rLive.X - S(6));
                int cols = lay.Columns;
                for (int i = 0; i < rowsShown; i++)
                {
                    sg.DrawString(RowLabels[i], fBody, bMuted, rRows[i], sfLeftMid);
                    bool lastInCol = i + cols >= rowsShown;
                    if (!lastInCol && !(brackets && i == 0))
                        sg.DrawLine(pGridMinor, rRows[i].X, rRows[i].Bottom - 1, rRows[i].Right, rRows[i].Bottom - 1);
                }
                if (brackets)
                {   // column vector [x; y]: square brackets spanning the X and Y values
                    int bx0 = rBracket.X, bx1 = rBracket.Right, by0 = rBracket.Y, by1 = rBracket.Bottom, tk = S(5);
                    sg.DrawLine(pMatrix, bx0, by0, bx0, by1); sg.DrawLine(pMatrix, bx0, by0, bx0 + tk, by0); sg.DrawLine(pMatrix, bx0, by1, bx0 + tk, by1);
                    sg.DrawLine(pMatrix, bx1, by0, bx1, by1); sg.DrawLine(pMatrix, bx1, by0, bx1 - tk, by0); sg.DrawLine(pMatrix, bx1, by1, bx1 - tk, by1);
                }
                DrawBrackets(sg, rStats);
            }

            // key caps card
            if (capsShown && region.IntersectsWith(rCapsCard))
            {
                sg.FillRectangle(bSurface, rCapsCard);
                sg.DrawRectangle(pBorder, rCapsCard.X, rCapsCard.Y, rCapsCard.Width - 1, rCapsCard.Height - 1);
                DrawCardTitle(sg, rCapsTitle, CapsTitle, CapsTag, int.MaxValue);
                DrawBrackets(sg, rCapsCard);
            }

            // terminal frame
            if (rTerm.Width > 2 && rTerm.Height > 2 && region.IntersectsWith(rTerm))
            {
                sg.FillRectangle(bTermBg, rTerm);
                int titleBottom = rTermTitle.Bottom;
                sg.DrawLine(pTermBorder, rTerm.X, titleBottom, rTerm.Right - 1, titleBottom);
                int dr = S(4);
                int dotsW = 3 * S(14);
                Rectangle tt = new Rectangle(rTermTitle.X, rTermTitle.Y, Math.Max(0, rTermTitle.Width - dotsW - S(6)), rTermTitle.Height);
                sg.DrawString(TermTitle, MonoSafe(TermTitle) ? fMonoSmall : fSmall, bGreenMid, tt, sfLeftMid);
                // three window dots (decoration)
                for (int i = 0; i < 3; i++)
                    sg.FillEllipse(bGreenDim, rTermTitle.Right - (3 - i) * S(14), rTermTitle.Y + rTermTitle.Height / 2 - dr, dr * 2, dr * 2);
                if (scanBrush != null) sg.FillRectangle(scanBrush, rTerm);
                sg.DrawRectangle(pTermBorder, rTerm.X, rTerm.Y, rTerm.Width - 1, rTerm.Height - 1);
            }
        }

        private string GridLabel(int v)
        {
            string t;
            if (gridLabels.TryGetValue(v, out t)) return t;
            if (gridLabels.Count >= 256) gridLabels.Clear();   // bounded (the ladder has far fewer labels)
            t = v == 0 ? "0" : (v > 0 ? "+" + v.ToString() : v.ToString());
            gridLabels[v] = t;
            return t;
        }

        private void DrawGrid(Graphics sg)
        {
            if (rChartInner.Width < 4 || rChartInner.Height < 4) return;
            Region keep = sg.Clip;
            sg.SetClip(rChartInner, CombineMode.Intersect);
            float cx = rChartInner.X + rChartInner.Width / 2f, cy = rChartInner.Y + rChartInner.Height / 2f;
            float ppu = Ppu();
            double step = range.Step;
            float halfW = rChartInner.Width / 2f, halfHgt = rChartInner.Height / 2f;
            float stepPx = (float)(step * ppu);
            float smallH = fSmall.GetHeight(sg);
            if (stepPx >= 4)
            {
                // minor lines at half steps, major at steps
                float minorPx = stepPx / 2f;
                if (minorPx >= 6)
                {
                    for (float d = minorPx; d <= halfW + 1; d += stepPx)
                    { sg.DrawLine(pGridMinor, cx + d, rChartInner.Y, cx + d, rChartInner.Bottom); sg.DrawLine(pGridMinor, cx - d, rChartInner.Y, cx - d, rChartInner.Bottom); }
                    for (float d = minorPx; d <= halfHgt + 1; d += stepPx)
                    { sg.DrawLine(pGridMinor, rChartInner.X, cy + d, rChartInner.Right, cy + d); sg.DrawLine(pGridMinor, rChartInner.X, cy - d, rChartInner.Right, cy - d); }
                }
                // X labels: every k-th grid line so neighbouring labels keep a gap of at least 20 % of a label
                int maxN = Math.Max(1, (int)Math.Floor((halfW + 1) / stepPx));
                float widest = sg.MeasureString(GridLabel(-(int)Math.Round(maxN * step)), fSmall, PointF.Empty, sfTypo).Width;
                int stride = Math.Max(1, (int)Math.Ceiling((widest * 1.2f + S(6)) / stepPx));
                int n = 1;
                for (float d = stepPx; d <= halfW + 1; d += stepPx, n++)
                {
                    sg.DrawLine(pGrid, cx + d, rChartInner.Y, cx + d, rChartInner.Bottom);
                    sg.DrawLine(pGrid, cx - d, rChartInner.Y, cx - d, rChartInner.Bottom);
                    if (n % stride != 0) continue;
                    int v = (int)Math.Round(n * step);
                    string lp = GridLabel(v), ln = GridLabel(-v);
                    float lwp = sg.MeasureString(lp, fSmall, PointF.Empty, sfTypo).Width;
                    float lwn = sg.MeasureString(ln, fSmall, PointF.Empty, sfTypo).Width;
                    if (cx + d + S(2) + lwp <= rChartInner.Right - S(2)) sg.DrawString(lp, fSmall, bMuted, cx + d + S(2), cy + S(2));
                    if (cx - d + S(2) + lwn <= cx - S(2)) sg.DrawString(ln, fSmall, bMuted, cx - d + S(2), cy + S(2));
                }
                n = 1;
                for (float d = stepPx; d <= halfHgt + 1; d += stepPx, n++)
                {
                    sg.DrawLine(pGrid, rChartInner.X, cy + d, rChartInner.Right, cy + d);
                    sg.DrawLine(pGrid, rChartInner.X, cy - d, rChartInner.Right, cy - d);
                    if (stepPx >= smallH + S(4))
                    {
                        int v = (int)Math.Round(n * step);
                        sg.DrawString(GridLabel(v), fSmall, bMuted, cx + S(6), cy - d - smallH);   // up = +
                        // AUDIT-DASH P2: the first negative label would sit in the X-label row, so it starts one step lower
                        if (d - smallH >= smallH + S(4)) sg.DrawString(GridLabel(-v), fSmall, bMuted, cx + S(6), cy + d - smallH);
                    }
                }
            }
            sg.DrawLine(pAxis, rChartInner.X, cy, rChartInner.Right, cy);
            sg.DrawLine(pAxis, cx, rChartInner.Y, cx, rChartInner.Bottom);

            // quadrant tags
            int m = S(8);
            Rectangle q = new Rectangle(rChartInner.X + m, rChartInner.Y + m, rChartInner.Width - 2 * m, rChartInner.Height - 2 * m);
            sg.DrawString("Ⅱ 左上", fSmall, bMuted, q, sfLeftTop);
            sg.DrawString("Ⅰ 右上", fSmall, bMuted, q, sfRightTop);
            sg.DrawString("Ⅲ 左下", fSmall, bMuted, q, sfLeftBottom);
            sg.DrawString("Ⅳ 右下", fSmall, bMuted, q, sfRightBottom);

            // origin = screen centre (the game's own centre dot): a small reticle; the 「屏幕中心」 tag is dynamic (it steps
            // aside when the ring passes over it)
            SmoothingMode sm = sg.SmoothingMode;
            sg.SmoothingMode = SmoothingMode.AntiAlias;
            float or = 3f * s;
            sg.DrawEllipse(pOrigin, cx - or, cy - or, or * 2, or * 2);
            sg.SmoothingMode = sm;
            sg.Clip = keep;
            keep.Dispose();
        }

        /// <summary>HUD widget plates, frames and titles; the curve plot's axes and both curves c(u) (X green, Y cyan).</summary>
        private void DrawHudStatic(Graphics sg)
        {
            for (int i = 0; i < DashHud.Count; i++)
            {
                Rectangle b = rHud[i];
                if (b.IsEmpty) continue;
                sg.FillRectangle(bHudBg, b);
                sg.DrawRectangle(pHudFrame, b.X, b.Y, b.Width - 1, b.Height - 1);
                int L = Math.Min(S(8), b.Height / 3);
                sg.DrawLine(pBracket, b.X, b.Y, b.X + L, b.Y); sg.DrawLine(pBracket, b.X, b.Y, b.X, b.Y + L);
                sg.DrawLine(pBracket, b.Right - 1, b.Bottom - 1, b.Right - 1 - L, b.Bottom - 1); sg.DrawLine(pBracket, b.Right - 1, b.Bottom - 1, b.Right - 1, b.Bottom - 1 - L);
                Rectangle t = new Rectangle(b.X + hudPad, b.Y + hudPad / 2, b.Width - 2 * hudPad, hudTitleH);
                sg.DrawString(HudTitles[i], fSmall, bTag, t, sfLeftMid);
            }
            if (rCurvePlot.IsEmpty) return;
            Profile p = settings != null ? settings.Active : null;
            Rectangle pr = rCurvePlot;
            sg.DrawLine(pCurveAxis, pr.X, pr.Bottom, pr.Right, pr.Bottom);
            sg.DrawLine(pCurveAxis, pr.X, pr.Y, pr.X, pr.Bottom);
            if (p == null) return;
            SmoothingMode sm = sg.SmoothingMode;
            sg.SmoothingMode = SmoothingMode.AntiAlias;
            for (int a = 1; a >= 0; a--)
            {
                MoveCurve c = a == 0 ? p.CurveAD : p.CurveWS;
                if (c == null || c.Speeds == null) continue;
                PointF[] pts = new PointF[33]; // static layer only (rebuilt when the curves change)
                for (int k = 0; k <= 32; k++)
                {
                    double u = k / 32.0, v = 0;
                    try { v = MotionCurve.Progress(c.Speeds, u); } catch (Exception) { v = u; }
                    pts[k] = new PointF(pr.X + (float)(u * pr.Width), pr.Bottom - (float)(Math.Max(0, Math.Min(1.05, v)) * pr.Height));
                }
                sg.DrawLines(a == 0 ? pCurveX : pCurveY, pts);
            }
            sg.SmoothingMode = sm;
            Rectangle lg = new Rectangle(pr.X + S(3), pr.Y, pr.Width - S(3), (int)Math.Ceiling(fSmall.GetHeight(sg)));
            sg.DrawString("X", fMonoSmall, bGreen, lg, sfLeftTop);
            Rectangle lg2 = new Rectangle(lg.X + (int)Math.Ceiling(charW * 2), lg.Y, Math.Max(0, lg.Width - (int)Math.Ceiling(charW * 2)), lg.Height);
            sg.DrawString("Y", fMonoSmall, bCyan, lg2, sfLeftTop);
        }

        // ---------------------------------------------------------------- dynamic parts

        private void DrawHeaderDynamic(Graphics gr, Rectangle clip)
        {
            if (clip.IntersectsWith(rTitle))
            {
                gr.DrawString(titleShown, fTitle, bAccent, rTitle, sfLeftMid);
                if (double.IsNaN(decodeStartMs) && cursorOn && titleW + S(16) < rTitle.Width)
                {   // blinking block cursor after the decoded title
                    int ch = Math.Max(2, (int)(fTitle.GetHeight(gr) * 0.62f));
                    gr.FillRectangle(bGreen, rTitle.X + titleW + S(5), rTitle.Y + (rTitle.Height - ch) / 2 + S(1), Math.Max(2, S(7)), ch);
                }
            }
            if (rNote.Height > 0 && clip.IntersectsWith(rNote))
            {
                string shown = note;
                if (ReferenceEquals(note, hintText) && hintShort.Length > 0 && gr.MeasureString(note ?? "", fSmall, PointF.Empty, sfTypo).Width > rNote.Width)
                    shown = hintShort; // v5.0.2 (UI L16): the short hint instead of a cut one
                gr.DrawString(Fit(gr, shown, fSmall, rNote.Width, ref noteFitSrc, ref noteFitW, ref noteFit), fSmall,
                    ReferenceEquals(note, hintText) ? bMuted : bWarn, rNote, sfLeftMid);
            }
            if (!rFilter.IsEmpty && clip.IntersectsWith(rFilter))
                gr.DrawString(filterLine.Text, fSmall, filterLine.Text.StartsWith(DashFilterLine.Prefix + DashFilterLine.On, StringComparison.Ordinal) ? bGreen : bMuted,
                    rFilter, sfRightMid);
            if (clip.IntersectsWith(rMemVal)) gr.DrawString(txMem.Text, fValue, bGreen, rMemVal, sfRightMid);
        }

        /// <summary>Compact header (short page): warnings go into the chart's title bar instead.</summary>
        private void DrawChartTitleNote(Graphics gr)
        {
            if (ReferenceEquals(note, hintText) || rChartTitle.Width <= 0) return;
            Rectangle tr = new Rectangle(rChart.X + rChart.Width / 2, rChart.Y, rChart.Width / 2 - S(10), rChartTitle.Height);
            gr.FillRectangle(bChartBg, tr);
            gr.DrawString(Fit(gr, note, fSmall, tr.Width, ref noteFitSrc, ref noteFitW, ref noteFit), fSmall, bWarn, tr, sfRightMid);
        }

        private void DrawStatsDynamic(Graphics gr, Rectangle clip)
        {
            if (!rLive.IsEmpty && clip.IntersectsWith(rLive))
            {
                if (foreground) { if (cursorOn) gr.DrawString(LiveTag, fSmall, bGreen, rLive, sfRightMid); }
                else gr.DrawString(PauseTag, fSmall, bMuted, rLive, sfRightMid);
            }
            for (int i = 0; i < rowsShown; i++)
            {
                if (!clip.IntersectsWith(rValues[i])) continue;
                int b = rowBucket[i];
                if (b < 0) b = 0; if (b > 3) b = 3;
                string t;
                switch (i)
                {
                    case 0: t = txX.Text; break;
                    case 1: t = txY.Text; break;
                    case 2: t = txRise.Text; break;
                    case 3: t = leanText; break;
                    case 4: t = handText; break;
                    case 5: t = dirText; break;
                    default: t = stateText; break;
                }
                Font f = i < 3 ? fValue : fBody;
                SolidBrush br = i == 2 || (i == 4 && engine.Hand == 1) ? riseB[b] : valueB[b];
                // the value never runs into its label: cut to the space right of the label, with "…" (rows 3..6 are text)
                gr.DrawString(Fit(gr, t, f, rValues[i].Width, ref rowFitSrc[i], ref rowFitW[i], ref rowFit[i]), f, br, rValues[i], sfRightMid);
            }
        }

        private void DrawCapsDynamic(Graphics gr, Rectangle clip)
        {
            SmoothingMode sm = gr.SmoothingMode;
            gr.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < DashKeys.Count; i++)
            {
                if (capPaths[i] == null || !clip.IntersectsWith(rCapGlow[i])) continue;
                int b = glowBucket[i];
                if (b < 0) b = 0; if (b > 3) b = 3;
                if (b > 0 && glowPaths[i] != null) gr.FillPath(capGlowFill[b], glowPaths[i]);
                gr.FillPath(capFill[b], capPaths[i]);
                gr.DrawPath(capBorder[b], capPaths[i]);
                gr.DrawString(capLabels[i], capUseUi[i] ? fCapUi : fCap, capTextB[b], rCaps[i], sfCenter);
            }
            gr.SmoothingMode = sm;
            if (rCombo.Height > 0 && clip.IntersectsWith(rCombo))
            {
                int mk = shownMask & ((1 << DashKeys.Count) - 1);
                string ct = comboTexts[mk];
                if (ct == null) { ct = "> " + DashFormat.KeyCombo(mk, logNames) + (DashKeys.Sprinting(mk) ? " 冲刺" : ""); comboTexts[mk] = ct; } // once per mask, then cached
                Font cf = MonoSafe(ct) ? fMono : fTermUi;
                gr.DrawString(Fit(gr, ct, cf, rCombo.Width, ref comboFitSrc, ref comboFitW, ref comboFit), cf, mk != 0 ? bGreen : bTag, rCombo, sfCenter);
            }
        }

        private void DrawTerminalDynamic(Graphics gr, Rectangle clip)
        {
            if (lineH <= 0 || rTermInner.Height < 2 || rTermInner.Width < 2) return;
            int fit = Math.Max(1, (int)(rTermInner.Height / lineH));
            int count = log.Count;
            int first = Math.Max(0, count - fit);
            float y = rTermInner.Y;
            float x = rTermInner.X;
            float maxW = rTermInner.Width;
            for (int i = first; i < count; i++, y += lineH)
            {
                if (y > clip.Bottom || y + lineH + 2 < clip.Y) continue;
                string line = log.Line(i);
                int vis = log.Visible(i, nowMs);
                bool newest = i == count - 1;
                SolidBrush br = newest ? bGreen : (i >= count - 3 ? bGreenMid : bGreenDim);
                bool mono = MonoSafe(line);
                Font f = mono ? fMono : fTermUi;
                float fullW = LineWidth(gr, line, f);
                float typedW = vis >= line.Length ? fullW : TypedWidth(line, vis, fullW);
                if (vis > 0)
                {
                    if (vis >= line.Length)
                    {   // finished: in full, or cut with "…" (brute-render P2-3; was clipped mid-glyph)
                        if (fullW <= maxW) gr.DrawString(line, f, br, new RectangleF(x, y, maxW + 2f, lineH + 2f), sfTypo);
                        else
                        {
                            float room = maxW - (newest ? charW * 1.2f : 0f);
                            gr.DrawString(FittedLine(gr, line, f, room), f, br, new RectangleF(x, y, maxW + 2f, lineH + 2f), sfTypo);
                            if (newest) typedW = Math.Min(typedW, room);
                        }
                    }
                    else gr.DrawString(line, f, br, new RectangleF(x, y, Math.Min(maxW, typedW + 0.5f), lineH + 2f), sfTypo); // clipped = typed-out part
                }
                if (newest && cursorOn)
                {
                    float cxp = x + Math.Min(maxW - charW, typedW + 1f);
                    gr.FillRectangle(bGreen, cxp, y + 1f, Math.Max(2f, charW * 0.9f), lineH - 2f);
                }
            }
            if (count == 0 && cursorOn) gr.FillRectangle(bGreen, x, y + 1f, Math.Max(2f, charW * 0.9f), lineH - 2f);
        }

        /// <summary>Width of a whole terminal line, measured once per line (small LRU by string reference).</summary>
        private float LineWidth(Graphics gr, string line, Font f)
        {
            int k = MeasuredSlot(line);
            if (k >= 0) return measuredW[k];
            float w = gr.MeasureString(line, f, PointF.Empty, sfTypo).Width;
            measuredLine[measuredNext] = line;
            measuredW[measuredNext] = w;
            measuredFit[measuredNext] = null;
            measuredNext = (measuredNext + 1) % measuredLine.Length;
            return w;
        }

        private int MeasuredSlot(string line)
        {
            for (int k = 0; k < measuredLine.Length; k++) if (ReferenceEquals(measuredLine[k], line)) return k;
            return -1;
        }

        /// <summary>A finished terminal line that is wider than the box: cut with "…" (built once per line and box width).</summary>
        private string FittedLine(Graphics gr, string line, Font f, float maxW)
        {
            int k = MeasuredSlot(line);
            if (k < 0) { LineWidth(gr, line, f); k = MeasuredSlot(line); }
            int w = (int)maxW;
            if (k >= 0 && measuredFit[k] != null && measuredFitW[k] == w) return measuredFit[k];
            string fit = FitText(gr, line, f, maxW);
            if (k >= 0) { measuredFit[k] = fit; measuredFitW[k] = w; }
            return fit;
        }

        /// <summary>t, or its longest prefix + "…" that fits maxW (the same on every renderer: no reliance on the
        /// StringFormat's own trimming). Allocates only when the text has to be cut.</summary>
        private string FitText(Graphics gr, string t, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(t) || maxW <= 0) return t;
            if (gr.MeasureString(t, f, PointF.Empty, sfTypo).Width <= maxW) return t;
            int lo = 0, hi = t.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (char.IsHighSurrogate(t[mid - 1])) mid--;   // never split a surrogate pair
                if (mid <= lo) { hi = lo; break; }
                if (gr.MeasureString(t.Substring(0, mid).TrimEnd() + "…", f, PointF.Empty, sfTypo).Width <= maxW) lo = mid;
                else hi = mid - 1;
            }
            return lo <= 0 ? "…" : t.Substring(0, lo).TrimEnd() + "…";
        }

        /// <summary>FitText with a one-entry cache (source string by reference + width): steady texts cost nothing per frame.</summary>
        private string Fit(Graphics gr, string t, Font f, int maxW, ref string src, ref int srcW, ref string fit)
        {
            if (ReferenceEquals(t, src) && maxW == srcW && fit != null) return fit;
            src = t; srcW = maxW;
            fit = FitText(gr, t, f, maxW);
            return fit;
        }

        /// <summary>Width of the first n chars of a line: the measured full width split by the CJK = 1 em / Latin = 0.55 em model.</summary>
        private static float TypedWidth(string t, int n, float fullW)
        {
            if (n <= 0) return 0;
            double all = DashKeyNames.EstimateEm(t);
            if (all <= 0) return 0;
            double part = 0;
            for (int i = 0; i < n && i < t.Length; i++) part += t[i] > 0x2E7F ? 1.0 : 0.55;
            return (float)(fullW * part / all);
        }

        /// <summary>True when every char is printable ASCII or one of the maths symbols the monospace fonts carry
        /// (Consolas / Cascadia Mono, WGL4); CJK text uses the UI font.</summary>
        private static bool MonoSafe(string t)
        {
            if (t == null) return true;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c >= 0x20 && c <= 0x7E) continue;
                switch (c)
                {
                    case '·': case '÷': case '×': case '±': case '°': case '≈': case '≤': case '≥': case '√': case '∑': case '∫':
                    case '←': case '→': case '↑': case '↓': case 'Δ': case 'Σ': case 'θ': case 'τ': case 'π': case '—': case '∞':
                        continue;
                }
                return false;
            }
            return true;
        }

        private void DrawChartDynamic(Graphics gr, Rectangle clip)
        {
            if (rChartInner.Width < 4 || rChartInner.Height < 4) return;
            float cx, cy, cpp;
            ChartGeom(out cx, out cy, out cpp);
            float lo = rChartInner.X + 2, hi = rChartInner.Right - 2, top = rChartInner.Y + 2, bot = rChartInner.Bottom - 2;
            float px = ClampF(cx + (float)(uiX * cpp), lo, hi);
            float py = ClampF(cy - (float)(uiY * cpp), top, bot);

            // 「屏幕中心」 tag + leader, hidden while the ring (or its glow) is over it
            bool plateNow = PlateVisible(px, py);
            if (plateNow && clip.IntersectsWith(Rectangle.Union(rPlate, new Rectangle((int)cx - S(6), (int)cy, S(7), S(7)))))
            {
                gr.DrawLine(pPlate, cx - S(5), cy + S(5), rPlate.Right, rPlate.Y);
                gr.FillRectangle(bChartBg, rPlate);
                gr.DrawRectangle(pPlate, rPlate.X, rPlate.Y, rPlate.Width - 1, rPlate.Height - 1);
                gr.DrawString(CentreText, fSmall, bMuted, rPlate, sfCenter);
            }

            // HUD widgets (text under the trail and the ring)
            for (int i = 0; i < DashHud.Count; i++) if (!rHud[i].IsEmpty && clip.IntersectsWith(rHud[i])) DrawHud(gr, i);

            SmoothingMode sm = gr.SmoothingMode;
            gr.SmoothingMode = SmoothingMode.AntiAlias;
            bool off = Math.Abs(px - cx) > S(6) || Math.Abs(py - cy) > S(6);
            // projection lines from the ring to both axes + ticks on the axes (HUD read-out)
            if (off)
            {
                gr.DrawLine(pProj, px, py, px, cy);
                gr.DrawLine(pProj, px, py, cx, py);
                int tk = S(4);
                gr.DrawLine(pTick, px, cy - tk, px, cy + tk);
                gr.DrawLine(pTick, cx - tk, py, cx + tk, py);
            }

            DrawTrail(gr, cx, cy, cpp, px, py, lo, hi, top, bot);

            // offset vector: an arrow from the screen centre to the ring's edge + the angle arc θ at the origin
            float dx = px - cx, dy = py - cy;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float r = 8f * s;
            if (len > r + S(4))
            {
                float ux = dx / len, uy = dy / len;
                float ex = px - ux * (r + 2f * s), ey = py - uy * (r + 2f * s);
                gr.DrawLine(pVector, cx, cy, ex, ey);
                float ah = Math.Max(5f, 6f * s), aw = Math.Max(3f, 3.2f * s);
                arrowHead[0] = new PointF(ex + ux * 1f, ey + uy * 1f);
                arrowHead[1] = new PointF(ex - ux * ah + uy * aw, ey - uy * ah - ux * aw);
                arrowHead[2] = new PointF(ex - ux * ah - uy * aw, ey - uy * ah + ux * aw);
                gr.FillPolygon(bArrow, arrowHead);
                float ar = Math.Min(S(16), len * 0.45f);
                if (ar > S(6))
                {
                    double ang = DashMathText.Angle(uiX, uiY);        // UI: counter-clockwise from +X
                    float sweep = (float)(ang > 180 ? 360 - ang : ang);
                    gr.DrawArc(pArc, cx - ar, cy - ar, ar * 2, ar * 2, 0f, ang > 180 ? sweep : -sweep);
                }
            }

            // the ring (two-tone like the overlay; the centre stays empty)
            gr.DrawEllipse(pRingGlow, px - r, py - r, r * 2, r * 2);
            gr.DrawEllipse(pRingOuter, px - r, py - r, r * 2, r * 2);
            gr.DrawEllipse(pRingInner, px - r, py - r, r * 2, r * 2);
            gr.SmoothingMode = sm;
        }

        /// <summary>The trail as polylines, one per fade step (12 pens): far fewer GDI+ calls than one per segment.</summary>
        private void DrawTrail(Graphics gr, float cx, float cy, float cpp, float px, float py, float lo, float hi, float top, float bot)
        {
            int n = trail.Count;
            if (n <= 0) return;
            double maxAge = trail.MaxAgeMs;
            int m = 0;
            for (int i = 0; i <= n && m < trailPts.Length; i++)
            {
                double tx, ty, tt;
                if (i < n) trail.Get(i, out tx, out ty, out tt);
                else { tx = uiX; ty = uiY; tt = nowMs; }
                trailPts[m] = new PointF(ClampF(cx + (float)(tx * cpp), lo, hi), ClampF(cy - (float)(ty * cpp), top, bot));
                double life = 1.0 - (nowMs - tt) / maxAge;
                int k = (int)(life * trailPens.Length);
                if (k >= trailPens.Length) k = trailPens.Length - 1;
                trailBucket[m] = life > 0 ? (k < 0 ? 0 : k) : -1;
                m++;
            }
            // segment j (pts[j-1] -> pts[j]) uses the bucket of its newer end, like v4.0
            int start = 1;
            while (start < m)
            {
                int b = trailBucket[start];
                int end = start;
                while (end + 1 < m && trailBucket[end + 1] == b) end++;
                if (b >= 0)
                {
                    int cnt = end - start + 2; // points start-1 .. end
                    PointF[] arr = polyPool[cnt];
                    if (arr == null) { arr = new PointF[cnt]; polyPool[cnt] = arr; }
                    Array.Copy(trailPts, start - 1, arr, 0, cnt);
                    gr.DrawLines(trailPens[b], arr);
                }
                start = end + 1;
            }
        }

        /// <summary>One HUD widget's live text (or the curve dots). During the HUD decode a short glyph window sweeps over
        /// the lines (bounded: 260 ms after a key change).</summary>
        private void DrawHud(Graphics gr, int i)
        {
            Rectangle b = rHud[i];
            float x = b.X + hudPad, y = b.Y + hudPad / 2 + hudTitleH, w = b.Width - 2 * hudPad;
            int n = hudLines[i];
            double prog = double.IsNaN(hudDecodeStartMs) ? 1.0 : (nowMs - hudDecodeStartMs) / HudDecodeMs;
            if (i == 0 || i == 1)
            {
                DashLine[] lines = i == 0 ? math.A : math.B;
                for (int k = 0; k < n && k < lines.Length; k++, y += lineH)
                {
                    string t = lines[k].Text;
                    bool formula = k == 0, result = k == 1;   // DashMathText: [0] formula, [1] its value, then the terms
                    if (prog < 1 && !formula) t = Decode(t, prog, k);
                    // the box is sized for the widest line; a font with wider glyphs still never cuts a glyph: "…"
                    gr.DrawString(t, fMono, formula ? bHudFormula : (result ? bHudNew : bHudText), new RectangleF(x, y, w, lineH + 2f), sfTypoEllipsis);
                }
            }
            else if (i == 2)
            {
                int cnt = stream.Count, show = Math.Min(n, cnt), first = cnt - show;
                for (int k = 0; k < show; k++, y += lineH)
                {
                    bool newest = first + k == cnt - 1;
                    gr.DrawString(stream.Line(first + k), fMono, newest ? bHudNew : (k >= show - 3 ? bHudText : bHudFormula), new RectangleF(x, y, w, lineH + 2f), sfTypoEllipsis);
                }
            }
            else if (!rCurvePlot.IsEmpty)
            {
                SmoothingMode sm = gr.SmoothingMode;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                float dr = Math.Max(2.5f, 3f * s);
                for (int a = 0; a < 2; a++)
                {
                    if (!dotOn[a]) continue;
                    float dx = rCurvePlot.X + dotU[a] * rCurvePlot.Width, dy = rCurvePlot.Bottom - Math.Max(0f, Math.Min(1.05f, dotC[a])) * rCurvePlot.Height;
                    gr.FillEllipse(a == 0 ? bGreen : bCyan, dx - dr, dy - dr, dr * 2, dr * 2);
                }
                gr.SmoothingMode = sm;
            }
        }

        private string Decode(string t, double prog, int salt)
        {
            if (string.IsNullOrEmpty(t)) return t;
            int n = Math.Min(t.Length, hudBuf.Length);
            DashDecode.Sweep(hudBuf, t, prog, hudDecodeFrame + salt * 7, 4);
            return new string(hudBuf, 0, n); // bounded: only during the HUD decode
        }

        private static float ClampF(float v, float lo, float hi)
        {
            if (float.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // ================================================================== resources

        private static Font MakeFont(string[] families, float pt, FontStyle style)
        {
            for (int i = 0; i < families.Length; i++)
            {
                try
                {
                    Font f = new Font(families[i], pt, style, GraphicsUnit.Point);
                    if (string.Equals(f.Name, families[i], StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, pt, style, GraphicsUnit.Point);
        }

        private static Color A(Color c, int alpha) { return Color.FromArgb(alpha, c.R, c.G, c.B); }

        private static readonly string[] UiFamilies = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Segoe UI" };
        private static readonly string[] MonoFamilies = new string[] { "Consolas", "Cascadia Mono", "Lucida Console", "Courier New" };

        /// <summary>Fonts in points (so they follow the Windows DPI); one fixed size each (v4.3.1), made once.</summary>
        private void CreateFonts()
        {
            DisposeFonts();
            // SPEC (Leo, 16 Nov): at 100 % body 11 pt, labels 10.5 pt, page title 16 pt, numbers 11-12 pt, terminal 10.5 pt.
            // The HUD uses the terminal / label sizes.
            fTitle = MakeFont(UiFamilies, 16f, FontStyle.Bold);
            fBody = MakeFont(UiFamilies, 11f, FontStyle.Regular);
            fSmall = MakeFont(UiFamilies, 10.5f, FontStyle.Regular);
            fCapUi = MakeFont(UiFamilies, 10.5f, FontStyle.Bold);
            fMono = MakeFont(MonoFamilies, 10.5f, FontStyle.Regular);
            fMonoSmall = MakeFont(MonoFamilies, 10.5f, FontStyle.Regular);
            fTermUi = MakeFont(UiFamilies, 10.5f, FontStyle.Regular);   // terminal lines with Chinese text
            fTag = MakeFont(MonoFamilies, 10.5f, FontStyle.Regular);
            fValue = MakeFont(MonoFamilies, 12f, FontStyle.Bold);
            fCap = MakeFont(MonoFamilies, 11f, FontStyle.Bold);
        }

        /// <summary>v4.3.1 test hook: the body font's point size (constant for every window size).</summary>
        internal float BodyPt { get { return fBody != null ? fBody.SizeInPoints : 0f; } }

        private void CreateResources()
        {
            CreateFonts();
            fontsMade = true;

            bBack = new SolidBrush(Theme.ColBackground);
            bSurface = new SolidBrush(Theme.ColSurface);
            bChartBg = new SolidBrush(ColChartBg);
            bText = new SolidBrush(Theme.ColText);
            bMuted = new SolidBrush(Theme.ColMuted);
            bAccent = new SolidBrush(Theme.ColAccent);
            bGreen = new SolidBrush(ColGreen);
            bGreenDim = new SolidBrush(ColGreenDim);
            bGreenMid = new SolidBrush(ColGreenMid);
            bTag = new SolidBrush(ColTag);
            bTermBg = new SolidBrush(ColTermBg);
            bWarn = new SolidBrush(Theme.Blend(Theme.ColAccent, Color.FromArgb(0xFF, 0x80, 0x40), 0.5));
            bHudBg = new SolidBrush(A(ColHudBg, 235));
            bHudText = new SolidBrush(ColHudText);
            bHudFormula = new SolidBrush(ColHudFormula);
            bHudNew = new SolidBrush(ColBright);
            bCyan = new SolidBrush(ColCyan);
            bArrow = new SolidBrush(A(Theme.ColAccent, 220));

            pBorder = new Pen(Theme.ColBorder);
            pGrid = new Pen(A(Theme.ColBorder, 200));
            pGridMinor = new Pen(A(Theme.ColBorder, 90));
            pAxis = new Pen(A(Theme.ColAccent, 150));
            pHeaderLine = new Pen(A(Theme.ColAccent, 120));
            pTermBorder = new Pen(Theme.Blend(ColTermBg, ColGreen, 0.30));
            pOrigin = new Pen(Theme.ColMuted);
            pPlate = new Pen(A(Theme.ColMuted, 110));
            pHudFrame = new Pen(Theme.Blend(ColHudBg, ColGreen, 0.28));
            pCurveAxis = new Pen(A(Theme.ColMuted, 140));

            for (int b = 0; b < 4; b++)
            {
                double t = b / 3.0;
                capFill[b] = new SolidBrush(Theme.Blend(Theme.ColInput, Color.FromArgb(0x0E, 0x3B, 0x12), t));
                capGlowFill[b] = new SolidBrush(A(ColGreen, (int)(70 * t)));
                capTextB[b] = new SolidBrush(Theme.Blend(Theme.ColMuted, ColBright, t));
                capBorder[b] = new Pen(Theme.Blend(Theme.ColBorder, ColGreen, t));
                valueB[b] = new SolidBrush(Theme.Blend(Theme.ColText, ColBright, t * 0.9));
                riseB[b] = new SolidBrush(Theme.Blend(Theme.ColAccent, ColBright, t * 0.9));
            }

            sfTypo = (StringFormat)StringFormat.GenericTypographic.Clone();
            sfTypo.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces; // no NoClip: the layout box clips
            sfTypo.Trimming = StringTrimming.None;
            sfTypoEllipsis = (StringFormat)StringFormat.GenericTypographic.Clone();
            sfTypoEllipsis.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            sfTypoEllipsis.Trimming = StringTrimming.EllipsisCharacter;
            sfCenter = MakeSf(StringAlignment.Center, StringAlignment.Center);
            sfRight = MakeSf(StringAlignment.Far, StringAlignment.Near);
            sfLeftMid = MakeSf(StringAlignment.Near, StringAlignment.Center);
            sfRightMid = MakeSf(StringAlignment.Far, StringAlignment.Center);
            sfLeftTop = MakeSf(StringAlignment.Near, StringAlignment.Near);
            sfRightTop = MakeSf(StringAlignment.Far, StringAlignment.Near);
            sfLeftBottom = MakeSf(StringAlignment.Near, StringAlignment.Far);
            sfRightBottom = MakeSf(StringAlignment.Far, StringAlignment.Far);

            // scanlines: 1 dark line every 3 px over the terminal (cached texture)
            scanBmp = new Bitmap(1, 3, PixelFormat.Format32bppArgb);
            scanBmp.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));
            scanBmp.SetPixel(0, 1, Color.FromArgb(0, 0, 0, 0));
            scanBmp.SetPixel(0, 2, Color.FromArgb(70, 0, 0, 0));
            scanBrush = new TextureBrush(scanBmp, WrapMode.Tile);

            ScaledPens();
        }

        private static StringFormat MakeSf(StringAlignment h, StringAlignment v)
        {
            StringFormat f = new StringFormat(StringFormatFlags.NoWrap);
            f.Alignment = h;
            f.LineAlignment = v;
            f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }

        /// <summary>Pens whose width depends on the DPI scale / zoom (rebuilt on layout).</summary>
        private void ScaledPens()
        {
            DisposePen(ref pVector); DisposePen(ref pRingGlow); DisposePen(ref pRingOuter); DisposePen(ref pRingInner);
            DisposePen(ref pBracket); DisposePen(ref pProj); DisposePen(ref pTick); DisposePen(ref pCurveX); DisposePen(ref pCurveY);
            DisposePen(ref pArc); DisposePen(ref pMatrix);
            pVector = new Pen(A(Theme.ColAccent, 170), Math.Max(1f, 1.4f * s));
            pRingGlow = new Pen(A(ColGreen, 45), 8f * s);
            pRingOuter = new Pen(Color.Black, 4.5f * s);
            pRingInner = new Pen(ColGreen, 2.2f * s);
            pBracket = new Pen(A(Theme.ColAccent, 210), Math.Max(1f, 1.5f * s));
            pProj = new Pen(A(ColGreen, 70), Math.Max(1f, s));
            pProj.DashStyle = DashStyle.Dot;
            pTick = new Pen(A(ColGreen, 200), Math.Max(1f, 1.5f * s));
            pCurveX = new Pen(ColGreen, Math.Max(1f, 1.5f * s));
            pCurveY = new Pen(A(ColCyan, 210), Math.Max(1f, 1.3f * s));
            pCurveY.DashStyle = DashStyle.Dash;
            pArc = new Pen(A(ColCyan, 190), Math.Max(1f, 1.2f * s));
            pMatrix = new Pen(A(Theme.ColAccent2, 200), Math.Max(1f, 1.3f * s));
            for (int i = 0; i < trailPens.Length; i++)
            {
                if (trailPens[i] != null) trailPens[i].Dispose();
                double life = (i + 1) / (double)trailPens.Length;
                int alpha = (int)Math.Round(230 * life * life);
                Pen p = new Pen(A(ColGreen, Math.Max(12, alpha)), Math.Max(1f, (1f + 1.6f * (float)life) * s));
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                trailPens[i] = p;
            }
        }

        private static void DisposePen(ref Pen p) { if (p != null) { p.Dispose(); p = null; } }
        private static void DisposeBrush(ref SolidBrush b) { if (b != null) { b.Dispose(); b = null; } }
        private static void DisposeFont(ref Font f) { if (f != null) { f.Dispose(); f = null; } }
        private static void DisposeSf(ref StringFormat f) { if (f != null) { f.Dispose(); f = null; } }

        private void DisposeFonts()
        {
            DisposeFont(ref fTitle); DisposeFont(ref fBody); DisposeFont(ref fSmall); DisposeFont(ref fMono);
            DisposeFont(ref fMonoSmall); DisposeFont(ref fValue); DisposeFont(ref fCap); DisposeFont(ref fCapUi); DisposeFont(ref fTag); DisposeFont(ref fTermUi);
        }

        private void DisposeResources()
        {
            DisposeFonts();
            DisposeBrush(ref bBack); DisposeBrush(ref bSurface); DisposeBrush(ref bChartBg); DisposeBrush(ref bText);
            DisposeBrush(ref bMuted); DisposeBrush(ref bAccent); DisposeBrush(ref bGreen); DisposeBrush(ref bGreenDim);
            DisposeBrush(ref bGreenMid); DisposeBrush(ref bTermBg); DisposeBrush(ref bWarn); DisposeBrush(ref bTag);
            DisposeBrush(ref bHudBg); DisposeBrush(ref bHudText); DisposeBrush(ref bHudFormula); DisposeBrush(ref bHudNew);
            DisposeBrush(ref bCyan); DisposeBrush(ref bArrow);
            DisposePen(ref pBorder); DisposePen(ref pGrid); DisposePen(ref pGridMinor); DisposePen(ref pAxis);
            DisposePen(ref pVector); DisposePen(ref pRingGlow); DisposePen(ref pRingOuter); DisposePen(ref pRingInner);
            DisposePen(ref pOrigin); DisposePen(ref pTermBorder); DisposePen(ref pHeaderLine);
            DisposePen(ref pBracket); DisposePen(ref pProj); DisposePen(ref pTick); DisposePen(ref pPlate);
            DisposePen(ref pHudFrame); DisposePen(ref pCurveX); DisposePen(ref pCurveY); DisposePen(ref pCurveAxis);
            DisposePen(ref pArc); DisposePen(ref pMatrix);
            for (int i = 0; i < trailPens.Length; i++) DisposePen(ref trailPens[i]);
            for (int b = 0; b < 4; b++)
            {
                DisposeBrush(ref capFill[b]); DisposeBrush(ref capGlowFill[b]); DisposeBrush(ref capTextB[b]); DisposePen(ref capBorder[b]);
                DisposeBrush(ref valueB[b]); DisposeBrush(ref riseB[b]);
            }
            if (scanBrush != null) { scanBrush.Dispose(); scanBrush = null; }
            if (scanBmp != null) { scanBmp.Dispose(); scanBmp = null; }
            DisposeSf(ref sfTypo); DisposeSf(ref sfTypoEllipsis); DisposeSf(ref sfCenter); DisposeSf(ref sfRight); DisposeSf(ref sfLeftMid); DisposeSf(ref sfRightMid);
            DisposeSf(ref sfLeftTop); DisposeSf(ref sfRightTop); DisposeSf(ref sfLeftBottom); DisposeSf(ref sfRightBottom);
        }
    }
}
