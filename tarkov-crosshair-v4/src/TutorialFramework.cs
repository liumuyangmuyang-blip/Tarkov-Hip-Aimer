// TutorialFramework.cs -- 首次设置教程 (v4.0) step framework, shared drawing helpers and the first-person demo rig.
// Uses System.Drawing only (no WinForms), so every step can be rendered offscreen by the test harness.
// C# 5. Per-frame code paths must not allocate: use the scratch pens/brushes (gx.P / gx.B), gx.Pts(n),
// TutorialNum for numbers, and cache any other dynamic string until its value changes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TarkovCrosshair
{
    /// <summary>Page keys passed to the form's jumpToPage callback (the UI maps them to its settings pages).</summary>
    public static class TutorialPages
    {
        public const string Home = "首页", Keys = "按键", General = "常规", Recoil = "后坐力", Move = "移动",
            Lean = "侧身", Hand = "换手", Jump = "跳跃", Look = "外观", Feedback = "反馈";
        /// <summary>The settings pages in page order (for the 完成 step).</summary>
        public static readonly string[] SettingsOrder = new string[] { Keys, General, Recoil, Move, Lean, Hand, Jump, Look, Feedback };
        /// <summary>Card labels for SettingsOrder.</summary>
        public static readonly string[] SettingsLabels = new string[] { "按键", "分辨率 / 常规", "后坐力", "移动", "侧身", "换手", "跳跃", "外观 / 准星样式", "反馈" };
    }

    /// <summary>One input row of a step, bound to the ACTIVE profile. The form builds a real control for it.</summary>
    public sealed class TutorialField
    {
        public const int KindNumber = 0, KindCheck = 1;
        public int Kind;
        /// <summary>Row label, e.g. "D 的 X（左右）".</summary>
        public string Label;
        /// <summary>Small tag after the label: "推荐改" / "可选" / "不建议改" / null.</summary>
        public string Tag;
        /// <summary>Highlighted (the value this step is about).</summary>
        public bool Main;
        public double Min = -500, Max = 500, Increment = 1;
        public int Decimals;
        /// <summary>"px" / "ms" / null.</summary>
        public string Unit;
        public Func<Profile, double> Get;
        public Action<Profile, double> Set;
        public Func<Profile, bool> GetCheck;
        public Action<Profile, bool> SetCheck;
        /// <summary>null = always enabled.</summary>
        public Func<Profile, bool> Enabled;
        /// <summary>Optional: called after Set / SetCheck with the profile (e.g. RecomputeDiagonals). The form calls it.</summary>
        public Action<Profile> After;
        /// <summary>Collapsed under a 「可选」 expander (script: "Y 输入框显示但默认折叠，旁边写「可选」").</summary>
        public bool Optional;
        /// <summary>v4.1 百分比偏移 (Leo, 17 Nov 2026): PctNone = a plain number; PctX / PctY = an offset the form shows and
        /// reads as [arrow] + positive number + %, 2 decimals (spin 0.10 %, typing 0.01 %). Get / Set / Min / Max stay in
        /// PROFILE px (storage unchanged, never rounded); the form converts through OffsetUnits with the real settings.</summary>
        public int Pct;
        public const int PctNone = 0, PctX = 1, PctY = 2;
        /// <summary>Pct fields: the arrow is a toggle that flips the direction (false = the range has one direction only,
        /// e.g. 上抬偏移量 is always ↑; the arrow is then a plain sign).</summary>
        public bool PctSigned;
        /// <summary>Pct fields: the direction the arrow shows while the value is 0 (+1 = → / ↑, -1 = ← / ↓).</summary>
        public int ZeroSign = 1;

        /// <summary>v5.1.1 (X5): PctX fields stored relative to an X origin (a move direction from the 站立静止偏移 point);
        /// null = 0. The % shown is the on-screen distance from it (OffsetUnits.ToPctAt).</summary>
        public Func<Profile, double> OriginX;
        /// <summary>v5.1.1 (X5): a 换手 value (the FOV sides swap).</summary>
        public bool Switched;

        /// <summary>v5.1.1 (X5): the box % for profile p (origin and hand applied), positive, 2 decimals.</summary>
        public double PctOf(Profile p, GlobalSettings g)
        {
            return Math.Abs(OffsetUnits.Round(OffsetUnits.ToPctAt(Get(p), OriginOf(p), Axis, Switched, g)));
        }

        /// <summary>v5.1.1 (X5): typed |pct| in direction sign -> stored px (origin and hand applied), clamped to Min..Max.</summary>
        public double PxOf(Profile p, double pct, int sign, GlobalSettings g)
        {
            double px = OffsetUnits.ToPxAt((sign < 0 ? -1.0 : 1.0) * Math.Abs(pct), OriginOf(p), Axis, Switched, g);
            if (double.IsNaN(px) || double.IsInfinity(px)) px = 0;
            if (px < Min) px = Min;
            if (px > Max) px = Max;
            return px;
        }

        /// <summary>The origin for profile p (0 when none / unusable).</summary>
        public double OriginOf(Profile p)
        {
            if (OriginX == null || p == null) return 0;
            try { double o = OriginX(p); return double.IsNaN(o) || double.IsInfinity(o) ? 0 : o; } catch (Exception) { return 0; }
        }

        /// <summary>The OffsetAxis of a Pct field (X for PctX, else Y).</summary>
        public OffsetAxis Axis { get { return Pct == PctX ? OffsetAxis.X : OffsetAxis.Y; } }

        public static TutorialField Number(string label, string tag, bool main, double min, double max, string unit,
            Func<Profile, double> get, Action<Profile, double> set)
        {
            TutorialField f = new TutorialField();
            f.Kind = KindNumber; f.Label = label; f.Tag = tag; f.Main = main; f.Min = min; f.Max = max; f.Unit = unit;
            f.Get = get; f.Set = set;
            return f;
        }

        /// <summary>An offset field (百分比偏移). minPx / maxPx = the PROFILE px range (as the settings page); a range that
        /// cannot go below 0 has a fixed direction (no toggle).</summary>
        public static TutorialField Offset(string label, string tag, bool main, OffsetAxis axis, double minPx, double maxPx,
            Func<Profile, double> get, Action<Profile, double> set)
        {
            TutorialField f = Number(label, tag, main, minPx, maxPx, OffsetUnits.Unit, get, set);
            f.Pct = axis == OffsetAxis.X ? PctX : PctY;
            f.PctSigned = minPx < 0;
            f.Decimals = OffsetUnits.Decimals;
            f.Increment = OffsetUnits.SpinStepPct;
            return f;
        }

        public static TutorialField Check(string label, string tag, Func<Profile, bool> get, Action<Profile, bool> set)
        {
            TutorialField f = new TutorialField();
            f.Kind = KindCheck; f.Label = label; f.Tag = tag; f.GetCheck = get; f.SetCheck = set;
            return f;
        }
    }

    /// <summary>Everything a step may read or call. One instance per tutorial window.</summary>
    public sealed class TutorialContext
    {
        /// <summary>The real settings (the app's object). Steps bind fields to P; never replace G or its profiles.</summary>
        public readonly GlobalSettings G;
        public TutorialContext(GlobalSettings g) { G = g; Arbiter = new LiveArbiter(); }

        /// <summary>The active profile (the one the fields edit).</summary>
        public Profile P { get { return G.Active; } }

        /// <summary>This frame's live keys: 组合键 + suppression via MotionEngine.ReadInput, opposite keys resolved like the
        /// overlay (last pressed wins). Empty while the tutorial window is not the active window.</summary>
        public InputState Live;
        /// <summary>Left mouse held on the scene (= 开枪 in the demos), or the 开火 key if it is a keyboard key.</summary>
        public bool LiveFire;
        /// <summary>Mask (TKey) of the live keys this frame.</summary>
        public int LiveMask;
        /// <summary>Live vs demo script (any live key/click -> live for 2.5 s, then the script restarts).</summary>
        public readonly LiveArbiter Arbiter;
        /// <summary>ms since the current step was entered.</summary>
        public double StepMs;
        /// <summary>The current step was already shown before (text appears instantly; the animation replays).</summary>
        public bool Revisit;
        /// <summary>Layout scale = DPI / 96 (v4.3.1: no window-size scale). gx.S(px) uses the same value.</summary>
        public float K = 1f;
        /// <summary>Scene size in design px (scene px / K). Set by the frame before Update / Draw.</summary>
        public float SceneW, SceneH;
        /// <summary>Small scene (below 700 x 330 design px, e.g. 1280x720 at 150 %): draw only the core picture + one tag
        /// (hide tag columns, mini plots and side panels). Set by the frame.</summary>
        public bool Compact;
        public const float CompactW = 700, CompactH = 330;
        /// <summary>v4.1: Windows "show animations" is off. Looping demos play once to a key frame and pause (the user can
        /// press 继续 or a key). Set by the form (false in the offscreen renderer unless forced).</summary>
        public bool ReducedMotion;

        // ---- set by the form ----
        internal Action ChangedHook;
        internal Action FinishHook;
        internal Action<string> CloseAndJumpHook;

        /// <summary>Call after changing a profile value outside a TutorialField (fields do this themselves).</summary>
        public void NotifyChanged() { Action h = ChangedHook; if (h != null) h(); }

        /// <summary>完成: sets G.TutorialDone = true and saves settings.json now.</summary>
        public void Finish() { Action h = FinishHook; if (h != null) h(); }

        /// <summary>完成 + close the tutorial + open that settings page (TutorialPages key).</summary>
        public void CloseAndJump(string page) { Action<string> h = CloseAndJumpHook; if (h != null) h(page); }

        /// <summary>True when the scripted demo should drive the step (no live input in the last 2.5 s).</summary>
        public bool Scripted { get { return !Arbiter.Live; } }

        public bool LeanToggle { get { return G.LeanMode == "toggle"; } }
        public bool HandToggle { get { return G.HandMode != "hold"; } }

        /// <summary>Display name of an action's binding incl. 组合键 exactly as on the 按键 page, e.g. "Ctrl+D",
        /// "鼠标侧键1（XButton1）" (KeyBindings.ActionLeanLeft ...). Use it on key caps; running text uses PromptKey.</summary>
        public string KeyName(int action)
        {
            try { return G.Keys.ComboOf(action); } catch (Exception) { return "?"; }
        }

        private const string Side1 = "鼠标侧键1（XButton1）", Side2 = "鼠标侧键2（XButton2）";

        /// <summary>Key name for running text (prompts, body, notes): like KeyName but plain Chinese for Space and the side
        /// buttons -- 「空格」, 「鼠标侧键1」, 「鼠标侧键2」 (组合键 keep their modifiers: 「Ctrl+空格」). v4.1 (DOCCHECK41 R15): no space
        /// before the digit, the same spelling as the 按键 page (「鼠标侧键1（XButton1）」) and the 首页 hint.</summary>
        public string PromptKey(int action)
        {
            string n = KeyName(action);
            if (n.EndsWith(Side1)) return n.Substring(0, n.Length - Side1.Length) + "鼠标侧键1";
            if (n.EndsWith(Side2)) return n.Substring(0, n.Length - Side2.Length) + "鼠标侧键2";
            if (n == "Space" || n.EndsWith("+Space")) return n.Substring(0, n.Length - 5) + "空格";
            return n;
        }

        /// <summary>The value of one script placeholder (without braces), or null when it is not one.</summary>
        private string Placeholder(string key)
        {
            switch (key)
            {
                case "开枪键": case "fire": return PromptKey(KeyBindings.ActionFire);
                case "左侧身": case "Q": return PromptKey(KeyBindings.ActionLeanLeft);
                case "右侧身": case "E": return PromptKey(KeyBindings.ActionLeanRight);
                case "换手键": case "hand": return PromptKey(KeyBindings.ActionHandSwitch);
                case "跳跃键": case "jump": return PromptKey(KeyBindings.ActionJump);
                case "当前上抬": case "rise":
                    {
                        // v4.1 百分比偏移: 「↑ 3.33%」 (the settings pages' unit)
                        Profile p = P;
                        return p == null ? "?" : TutorialPct.Y(p.RecoilMaxPx, G);
                    }
                default: return null;
            }
        }

        private static bool IsLatin(char c) { return c < 0x80 && char.IsLetterOrDigit(c); }

        /// <summary>A CJK character (not punctuation): a Latin key name next to it gets a space.</summary>
        private static bool IsCjkText(char c)
        {
            return c >= 0x2E80 && "，。、；：！？（）「」『』《》〈〉·…—～".IndexOf(c) < 0;
        }

        /// <summary>Replaces the script placeholders (v4-tutorial-script.md) with the user's real bindings / values:
        /// {开枪键} {左侧身} {右侧身} {换手键} {跳跃键} {当前上抬}; short aliases {fire} {Q} {E} {hand} {jump} {rise}.
        /// Key names are the running-text form (PromptKey). A Latin key name between Chinese characters gets a space on that
        /// side, like the hand-written prompts: 「按{左侧身}侧身」 -> 「按 Q 侧身」, 「按{换手键}换手」 -> 「按鼠标侧键1 换手」.</summary>
        public string Fill(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('{') < 0) return s;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 16);
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch == '{')
                {
                    int j = s.IndexOf('}', i + 1);
                    string val = j > i ? Placeholder(s.Substring(i + 1, j - i - 1)) : null;
                    if (val != null)
                    {
                        if (val.Length > 0 && IsLatin(val[0]) && sb.Length > 0 && IsCjkText(sb[sb.Length - 1])) sb.Append(' ');
                        sb.Append(val);
                        if (val.Length > 0 && IsLatin(val[val.Length - 1]) && j + 1 < s.Length && IsCjkText(s[j + 1])) sb.Append(' ');
                        i = j + 1;
                        continue;
                    }
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>The small terminal under the scene (typed lines). Steps Push lines on events (allocates then only).</summary>
        public readonly TutorialTerminal Term = new TutorialTerminal();

        /// <summary>A copy of the settings for a demo MotionEngine: ring on, no focus gate, scale 1 (UI px), no fades,
        /// hold 开镜 (never fed), the user's 侧身 / 换手 modes. handEnabled forces 换手检测 on or off for the demo.</summary>
        public GlobalSettings DemoSettings(bool handEnabled)
        {
            GlobalSettings d = G.Clone(false);
            d.Enabled = true;
            d.OnlyWhenGameFocused = false;
            // the demo screen = the 调参 screen (v4.1: resolution, 画面比例 AND FOV), so ViewScale.Factor is exactly 1 and the
            // demo engines' UI px are profile px (the % labels convert them with the user's real settings)
            d.ResGameW = d.ResBaseW; d.ResGameH = d.ResBaseH;
            d.ResAspect = d.ResBaseAspect; d.ResFov = d.ResBaseFov;
            d.ShowFadeMs = 0; d.HideFadeMs = 0;
            d.AdsMode = "hold";
            d.HideOnFreeLook = false;
            d.BoostEnabled = false;
            d.HandEnabled = handEnabled;
            d.PreviewMode = PreviewMode.Live;
            d.EditHand = 0;
            return d;
        }

        /// <summary>The live input for the engine this frame, or the script sample when Scripted (fire out).</summary>
        public InputState Input(TutorialScript script, out bool fire, out int litMask)
        {
            if (!Scripted || script == null)
            {
                fire = LiveFire;
                litMask = LiveMask;
                InputState i = Live;
                i.Fire = fire;
                return i;
            }
            InputState s = script.Sample(Arbiter.ScriptMs, LeanToggle, HandToggle, out fire);
            litMask = script.HeldAt(Arbiter.ScriptMs);
            return s;
        }
    }

    /// <summary>
    /// One page of the wizard. Text fields may contain {Q} {E} {hand} {jump} {fire} (filled with the user's keys).
    /// The frame draws header, text panel, inputs and footer; the step draws only its scene rectangle.
    /// </summary>
    public abstract class TutorialStep
    {
        /// <summary>Stable id ("why", "hipfire", ...).</summary>
        public string Id;
        /// <summary>Chapter tag shown above the title ("原理", "实战校准", ...).</summary>
        public string Chapter;
        public string Title;
        /// <summary>Main text, typed in. '\n' = new paragraph.</summary>
        public string Body;
        /// <summary>Muted note under the body, or null.</summary>
        public string Note;
        /// <summary>「开发者的话」 box text, or null.</summary>
        public string DevNote;
        /// <summary>Footer hint (what to press / click), or null.</summary>
        public string Hint;
        /// <summary>Input rows bound to the active profile, or null.</summary>
        public TutorialField[] Fields;
        /// <summary>Left click on the scene = 开枪 (LiveFire) unless a chip was hit. False = clicks only hit chips.</summary>
        public bool ClickFires = true;
        /// <summary>Key bits (TKey) the footer key-cap strip shows for this step (0 = none).</summary>
        public int KeysShown;
        /// <summary>Terminal lines typed in when the step is entered (script 「终端行」), or null. More via c.Term.Push.</summary>
        public string[] Terminal;
        /// <summary>Sequential on-screen prompts (script 「屏幕提示（按顺序）」); the frame shows Prompts[PromptIndex] (falls
        /// back to Hint). Steps advance PromptIndex themselves.</summary>
        public string[] Prompts;
        public int PromptIndex;
        /// <summary>Small text under the input rows (e.g. 「可以先跳过，之后在「后坐力」页再填。」), or null.</summary>
        public string FieldNote;
        /// <summary>TutorialPages key for the 「去设置页」 link of this step, or null.</summary>
        public string SettingsPage;
        /// <summary>Set true when the step's task is done: 下一步 glows and the dot turns green (skipped steps stay amber).</summary>
        public bool Done;
        /// <summary>The step's only task is to read it and press 下一步 (e.g. 去靶场): 下一步 marks its dot done.</summary>
        public bool DoneOnNext;

        /// <summary>Called when the step becomes current (also on revisit: restart the animation here).</summary>
        public virtual void Enter(TutorialContext c) { }
        public virtual void Leave(TutorialContext c) { }
        /// <summary>Per frame, before Draw. dtMs is clamped to 0..100.</summary>
        public abstract void Update(TutorialContext c, double dtMs);
        /// <summary>Draw the scene into 'scene' (pixels). Use gx helpers; no allocation per frame.</summary>
        public abstract void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene);
        /// <summary>A chip drawn with gx.Chip(id, ...) was clicked.</summary>
        public virtual void OnChip(TutorialContext c, int id) { }
        /// <summary>A field of this step changed the profile (refresh demo clones here).</summary>
        public virtual void OnFieldChanged(TutorialContext c) { }
        /// <summary>v4.1 (UIALIGN41 #3): the field note this step shows while it is open, for the frame's one-layout-for-all-steps
        /// plan (TutorialFrame.MakePlan). Default: FieldNote as it is now.</summary>
        public virtual string LayoutFieldNote(TutorialContext c) { return FieldNote; }
        /// <summary>Key bits lit in the footer this frame (default: live keys or the script's held keys).</summary>
        public int LitKeys;
    }

    // =====================================================================================================
    // Drawing
    // =====================================================================================================

    /// <summary>
    /// Tutorial colours. FromTheme() = the app's Theme.cs colours (the form AND the offscreen renderer use it, so renders
    /// look like the product): Neon = Theme accent (neon green: ring, HUD, primary button), Accent = Theme amber (labels,
    /// 「开发者的话」, chapter tags), Cyan = Theme cyan (aim). Default() = the v3.6 palette (kept for old tests).
    /// One colour per key for holes / key lights, the same in every step (never Neon, which is the ring): KeyColor(TKey.X).
    /// </summary>
    public sealed class TutorialPalette
    {
        public Color Bg, Surface, Border, Text, Muted, Accent, Neon, Hot, Cyan, Red, SceneBg, Panel;
        /// <summary>Holes / lights per key: W cyan, S violet, A orange, D yellow, Q pink, E blue.</summary>
        public Color KeyW, KeyS, KeyA, KeyD, KeyQ, KeyE;

        public static TutorialPalette Make(Color bg, Color surface, Color border, Color text, Color muted, Color accent, Color neon)
        {
            TutorialPalette p = new TutorialPalette();
            p.Bg = bg; p.Surface = surface; p.Border = border; p.Text = text; p.Muted = muted; p.Accent = accent; p.Neon = neon;
            p.Hot = Color.FromArgb(0xFF, 0x8A, 0x3D);
            p.Cyan = Color.FromArgb(0x4F, 0xD8, 0xFF);
            p.Red = Color.FromArgb(0xFF, 0x4D, 0x4D);
            p.SceneBg = Mix(bg, Color.Black, 0.45);
            p.Panel = Mix(bg, Color.Black, 0.2);
            p.KeyW = Color.FromArgb(0x00, 0xE5, 0xFF);
            p.KeyS = Color.FromArgb(0xB3, 0x88, 0xFF);
            p.KeyA = Color.FromArgb(0xFF, 0x8A, 0x3D);
            p.KeyD = Color.FromArgb(0xFF, 0xE1, 0x4D);
            p.KeyQ = Color.FromArgb(0xFF, 0x6F, 0xB5);
            p.KeyE = Color.FromArgb(0x5C, 0x9D, 0xFF);
            return p;
        }

        /// <summary>The app's colours (Theme.cs). Compiles wherever Theme.cs does (the app; the renderer with THEME_PURE_CHECK).</summary>
        public static TutorialPalette FromTheme()
        {
            TutorialPalette p = Make(Theme.ColBackground, Theme.ColSurface, Theme.ColBorder, Theme.ColText, Theme.ColMuted,
                Theme.ColWarn, Theme.ColAccent);
            p.Cyan = Theme.ColAccent2;
            p.KeyW = Theme.ColAccent2;
            return p;
        }

        /// <summary>Colour of one movement / lean key (TKey.W .. TKey.E); Hot for anything else.</summary>
        public Color KeyColor(int tkey)
        {
            switch (tkey)
            {
                case TKey.W: return KeyW;
                case TKey.S: return KeyS;
                case TKey.A: return KeyA;
                case TKey.D: return KeyD;
                case TKey.Q: return KeyQ;
                case TKey.E: return KeyE;
                default: return Hot;
            }
        }

        public static TutorialPalette Default()
        {
            return Make(Color.FromArgb(0x1E, 0x20, 0x23), Color.FromArgb(0x26, 0x29, 0x2D), Color.FromArgb(0x3A, 0x3F, 0x45),
                Color.FromArgb(0xE8, 0xE6, 0xE1), Color.FromArgb(0x9D, 0xA3, 0xA8), Color.FromArgb(0xC8, 0xB0, 0x7A),
                Color.FromArgb(0x00, 0xDF, 0x00));
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>
    /// Fonts (points). SPEC "bigger, more readable text" minimums at 100 % DPI and scale 1.0: tutorial titles 20 pt, tutorial
    /// body 13 pt, labels 10.5 pt, numbers / data (monospace) 11 pt, terminal 10.5 pt, buttons 11 pt. v4.3.1: the forms pass scale 1 (one fixed
    /// text size, whatever the window size). The form passes the family; tests a private one.
    /// </summary>
    public sealed class TutorialFonts : IDisposable
    {
        public const float TitlePt = 20f, BodyPt = 13f, LabelPt = 10.5f, TagPt = 10.5f, MonoPt = 11.5f, MonoSmallPt = 11f,
            BigPt = 13f, KeyPt = 11f, HugePt = 24f, ButtonPt = 11f, NotePt = 11f, NumberPt = 11f;
        /// <summary>Title 20 pt bold; Body 13 pt (tutorial text, prompts, 开发者的话); Small = labels 10.5 pt; Tag 10.5 pt bold;
        /// Mono 11.5 pt (terminal, data); MonoSmall 11 pt (HUD numbers); Big 13 pt bold; Key 11 pt bold; Huge 24 pt bold;
        /// Button 11 pt (buttons and chips); Note 11 pt (small explanatory text, links).</summary>
        public readonly Font Title, Body, Small, Tag, Mono, MonoSmall, Big, Key, Huge, Button, Note;
        public readonly float Scale;

        public TutorialFonts(FontFamily ui, FontFamily mono, float scale) : this(ui, mono, scale, 1f) { }

        /// <summary>pxScale: an extra factor on every size for an offscreen renderer that draws on a 96-DPI bitmap but must
        /// show the pixel sizes of a higher-DPI screen (1.5 = 150 %). The app always uses 1: on screen the Graphics' DPI
        /// already turns points into the right number of pixels.</summary>
        public TutorialFonts(FontFamily ui, FontFamily mono, float scale, float pxScale)
        {
            if (!(scale >= 1f)) scale = 1f;
            if (scale > 1.8f) scale = 1.8f;
            if (!(pxScale > 0.25f) || pxScale > 6f) pxScale = 1f;
            Scale = scale;
            float s = scale * pxScale;
            Title = new Font(ui, Q(TitlePt * s), FontStyle.Bold);
            Body = new Font(ui, Q(BodyPt * s), FontStyle.Regular);
            Small = new Font(ui, Q(LabelPt * s), FontStyle.Regular);
            Tag = new Font(ui, Q(TagPt * s), FontStyle.Bold);
            Big = new Font(ui, Q(BigPt * s), FontStyle.Bold);
            Key = new Font(mono, Q(KeyPt * s), FontStyle.Bold);
            Mono = new Font(mono, Q(MonoPt * s), FontStyle.Regular);
            MonoSmall = new Font(mono, Q(MonoSmallPt * s), FontStyle.Regular);
            Huge = new Font(ui, Q(HugePt * s), FontStyle.Bold);
            Button = new Font(ui, Q(ButtonPt * s), FontStyle.Regular);
            Note = new Font(ui, Q(NotePt * s), FontStyle.Regular);
        }

        private static float Q(float pt) { return (float)(Math.Round(pt * 4) / 4.0); } // quarter points: stable sizes

        public void Dispose()
        {
            Font[] all = { Title, Body, Small, Tag, Mono, MonoSmall, Big, Key, Huge, Button, Note };
            foreach (Font f in all) if (f != null) f.Dispose();
        }
    }

    /// <summary>
    /// Shared drawing helpers. One instance per window; G is set before each frame. Scratch pens/brushes are reused:
    /// gx.P(colour, width) / gx.B(colour) return one of a small ring of objects -- use them immediately, never keep them.
    /// </summary>
    public sealed class TutorialGfx : IDisposable
    {
        public Graphics G;
        public float K = 1f;
        public readonly TutorialPalette Pal;
        /// <summary>Current fonts (replaced on resize by SetFonts -- read gx.F every frame, never keep a Font).</summary>
        public TutorialFonts F { get { return fonts; } }
        private TutorialFonts fonts;
        /// <summary>Mouse position over the canvas (for chip hover), or (-1, -1).</summary>
        public PointF Mouse = new PointF(-1, -1);
        /// <summary>Total animation time (ms) for pulsing effects.</summary>
        public double TimeMs;

        private readonly Pen[] pens = new Pen[16];
        private readonly SolidBrush[] brushes = new SolidBrush[16];
        private int pi, bi;
        private readonly Dictionary<int, PointF[]> pts = new Dictionary<int, PointF[]>();
        public readonly StringFormat Near, Center, Far, CenterMid, NearMid, FarMid;

        // chips recorded during the last Draw (hit-test)
        private const int MaxChips = 32;
        private readonly RectangleF[] chipRect = new RectangleF[MaxChips];
        private readonly int[] chipId = new int[MaxChips];
        private int chipCount;

        public TutorialGfx(TutorialPalette pal, TutorialFonts fonts)
        {
            Pal = pal; this.fonts = fonts;
            for (int i = 0; i < pens.Length; i++) { pens[i] = new Pen(Color.White, 1f); pens[i].StartCap = LineCap.Round; pens[i].EndCap = LineCap.Round; pens[i].LineJoin = LineJoin.Round; }
            for (int i = 0; i < brushes.Length; i++) brushes[i] = new SolidBrush(Color.White);
            Near = Fmt(StringAlignment.Near, StringAlignment.Near);
            Center = Fmt(StringAlignment.Center, StringAlignment.Near);
            Far = Fmt(StringAlignment.Far, StringAlignment.Near);
            CenterMid = Fmt(StringAlignment.Center, StringAlignment.Center);
            NearMid = Fmt(StringAlignment.Near, StringAlignment.Center);
            FarMid = Fmt(StringAlignment.Far, StringAlignment.Center);
        }

        private static StringFormat Fmt(StringAlignment h, StringAlignment v)
        {
            StringFormat f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = h; f.LineAlignment = v;
            f.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
            f.Trimming = StringTrimming.None;
            return f;
        }

        /// <summary>Swaps in new fonts (window resized); disposes the old ones unless they are the same object.</summary>
        public void SetFonts(TutorialFonts f)
        {
            if (f == null || f == fonts) return;
            TutorialFonts old = fonts;
            fonts = f;
            if (old != null) old.Dispose();
        }

        /// <summary>The background text is assumed to sit on (for the readability rule).</summary>
        public Color TextBg;

        /// <summary>SPEC: text the user must read is never below 4.5:1 contrast. Colours with alpha &gt;= 120 are composited
        /// over TextBg and lightened if needed (fading / decorative text below alpha 120 is left alone; dark text too).</summary>
        public Color Readable(Color c)
        {
            if (c.A < 120) return c;
            Color bg = TextBg.A == 255 ? TextBg : Pal.SceneBg;
            int r, g, b;
            TutorialContrast.Readable(c.A, c.R, c.G, c.B, bg.R, bg.G, bg.B, out r, out g, out b);
            return Color.FromArgb(255, r, g, b);
        }

        public void Dispose()
        {
            if (fonts != null) fonts.Dispose();
            for (int i = 0; i < BgSlots; i++) if (bgCache[i] != null) { bgCache[i].Dispose(); bgCache[i] = null; }
            foreach (Pen p in pens) if (p != null) p.Dispose();
            foreach (SolidBrush b in brushes) if (b != null) b.Dispose();
            Near.Dispose(); Center.Dispose(); Far.Dispose(); CenterMid.Dispose(); NearMid.Dispose(); FarMid.Dispose();
        }

        /// <summary>Begin a frame: clears the chip list.</summary>
        public void BeginFrame(Graphics g)
        {
            G = g;
            chipCount = 0;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Bilinear;
        }

        /// <summary>Scales a 96-DPI length.</summary>
        public float S(float px) { return px * K; }

        public static Color A(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0; if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        public static Color A(Color c, double alpha01) { return A(c, (int)Math.Round(alpha01 * 255)); }

        /// <summary>Opaque blend a -> b (t 0..1).</summary>
        public static Color Mix(Color a, Color b, double t) { return TutorialPalette.Mix(a, b, t); }

        /// <summary>Scratch pen (width in 96-DPI px, scaled by K).</summary>
        public Pen P(Color c, float width)
        {
            Pen p = pens[pi]; pi = (pi + 1) % pens.Length;
            float w = width * K;
            // NaN / infinite widths (a poisoned value) would make GDI+ throw while painting: clamp (v4.1, brute-ui F5)
            p.Color = c; p.Width = w > 0.5f ? (w < 400f ? w : 400f) : 0.5f; p.DashStyle = DashStyle.Solid;
            return p;
        }

        /// <summary>True when every value is a finite number (drawing helpers skip non-finite geometry instead of throwing).</summary>
        public static bool Fin(float a, float b) { return !float.IsNaN(a) && !float.IsInfinity(a) && !float.IsNaN(b) && !float.IsInfinity(b); }

        public static bool Fin(RectangleF r) { return Fin(r.X, r.Y) && Fin(r.Width, r.Height); }

        // ---------------- v4.1 scene bookkeeping (reset by BeginScene, used by the frame after the step's Draw) ----------------
        // Tags (pills) drawn in the scene are counted per view (Leo: at most TutorialPlace.MaxTagsPerView per view); sub-views
        // (plots, HUD boxes, split panels) are registered; obstacles (tags, chips, key caps, overlay views) keep the deferred
        // labels (e.g. the wall's 「10 米」) off everything the step drew. Fixed arrays: no allocation per frame.
        private const int MaxRecs = 64, MaxViews = 16, MaxLate = 6;
        private readonly float[] tagRecs = new float[4 * MaxRecs];
        private readonly string[] tagTexts = new string[MaxRecs];
        private int tagRecCount;
        private readonly float[] viewRecs = new float[4 * MaxViews];
        private readonly int[] viewCounts = new int[MaxViews];
        private int viewRecCount;
        private readonly float[] obsRecs = new float[4 * MaxRecs];
        private int obsRecCount;
        private readonly string[] lateText = new string[MaxLate];
        private readonly Color[] lateCol = new Color[MaxLate];
        private readonly RectangleF[] lateBounds = new RectangleF[MaxLate];
        private readonly float[] lateX = new float[MaxLate], lateY = new float[MaxLate];
        private int lateCount;
        private bool recording;

        /// <summary>Starts recording the scene (the frame calls it before the step's Draw).</summary>
        public void BeginScene() { tagRecCount = 0; viewRecCount = 0; obsRecCount = 0; lateCount = 0; recording = true; }

        /// <summary>Stops recording (panel / footer tags are not scene tags).</summary>
        public void EndScene() { recording = false; }

        private void AddRec(float[] a, ref int n, RectangleF r, int max)
        {
            if (n >= max || !Fin(r)) return;
            a[4 * n] = r.X; a[4 * n + 1] = r.Y; a[4 * n + 2] = r.Width; a[4 * n + 3] = r.Height;
            n++;
        }

        /// <summary>A sub-view of the scene (its tags are counted on their own). overlay = drawn over the main picture (a plot,
        /// a HUD box): deferred labels keep out of it. Split panels side by side pass false.</summary>
        public void RegisterView(RectangleF r, bool overlay)
        {
            if (!recording) return;
            AddRec(viewRecs, ref viewRecCount, r, MaxViews);
            if (overlay) AddRec(obsRecs, ref obsRecCount, r, MaxRecs);
        }

        /// <summary>Something deferred labels must not cover (a text block, a marker).</summary>
        public void AddObstacle(RectangleF r)
        {
            if (recording) AddRec(obsRecs, ref obsRecCount, r, MaxRecs);
        }

        /// <summary>A pill the step drew itself (not through Tag / TagValue): counted in the per-view tag budget.</summary>
        public void NoteTag(RectangleF r, string s) { RecordTag(r, s); }

        private void RecordTag(RectangleF r, string s)
        {
            if (!recording) return;
            if (tagRecCount < MaxRecs) tagTexts[tagRecCount] = s;
            AddRec(tagRecs, ref tagRecCount, r, MaxRecs);
            AddRec(obsRecs, ref obsRecCount, r, MaxRecs);
        }

        /// <summary>Most tags in one view of the scene drawn since BeginScene (harness check: &lt;= TutorialPlace.MaxTagsPerView).</summary>
        public int MaxTagsInAView(RectangleF scene)
        {
            return TutorialPlace.MaxPerView(tagRecs, tagRecCount, viewRecs, viewRecCount, scene.X, scene.Y, scene.Width, scene.Height, viewCounts);
        }

        /// <summary>The scene tags drawn since BeginScene, joined by 「 | 」 (harness report only: allocates).</summary>
        public string SceneTagList()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < tagRecCount; i++) { if (i > 0) sb.Append(" | "); sb.Append(tagTexts[i]); }
            return sb.ToString();
        }

        /// <summary>A label drawn AFTER the step (by FlushLate): placed at (x, y) or moved down until it touches no tag, chip,
        /// key cap or overlay view the step drew, kept inside bounds; skipped when there is no free spot. Upright text in c on
        /// a dark backing. Used for the wall's distance label (v4.1: it was hidden under the HUD tags).</summary>
        public void LateLabel(string s, Color c, float x, float y, RectangleF bounds)
        {
            if (string.IsNullOrEmpty(s) || lateCount >= MaxLate || !Fin(x, y) || !Fin(bounds)) return;
            lateText[lateCount] = s; lateCol[lateCount] = c; lateX[lateCount] = x; lateY[lateCount] = y; lateBounds[lateCount] = bounds;
            lateCount++;
        }

        /// <summary>Draws the deferred labels (the frame calls it after the step's Draw, transform and clip reset).</summary>
        public void FlushLate(RectangleF scene)
        {
            if (lateCount == 0) return;
            for (int i = 0; i < chipCount && obsRecCount < MaxRecs; i++) AddRec(obsRecs, ref obsRecCount, chipRect[i], MaxRecs);
            G.SetClip(scene);
            for (int i = 0; i < lateCount; i++)
            {
                SizeF sz = Measure(lateText[i], F.Small);
                float padX = S(4), padY = S(1);
                float w = sz.Width + 2 * padX, h = sz.Height + 2 * padY;
                RectangleF b = RectangleF.Intersect(lateBounds[i], scene);
                double ox, oy;
                if (b.Width < w || b.Height < h) continue;
                // it may move down at most 2.5 label heights (further down it would sit on the picture): else it is left out
                double maxY = Math.Min(b.Bottom, lateY[i] + 3.5 * h);
                if (!TutorialPlace.Down(lateX[i], lateY[i], w, h, b.Left, b.Top, b.Right, maxY, obsRecs, obsRecCount, S(4), out ox, out oy)) continue;
                RectangleF r = new RectangleF((float)ox, (float)oy, w, h);
                G.FillRectangle(B(A(Pal.SceneBg, 190)), r);
                G.DrawString(lateText[i], F.Small, B(Readable(lateCol[i])), r.X + padX, r.Y + padY, Near);
                AddRec(obsRecs, ref obsRecCount, r, MaxRecs);
            }
            G.ResetClip();
            lateCount = 0;
        }

        public Pen Dashed(Color c, float width)
        {
            Pen p = P(c, width);
            p.DashStyle = DashStyle.Dash;
            return p;
        }

        public SolidBrush B(Color c)
        {
            SolidBrush b = brushes[bi]; bi = (bi + 1) % brushes.Length;
            b.Color = c;
            return b;
        }

        /// <summary>A reusable PointF array of exactly n points (one per n).</summary>
        public PointF[] Pts(int n)
        {
            PointF[] a;
            if (!pts.TryGetValue(n, out a)) { a = new PointF[n]; pts[n] = a; }
            return a;
        }

        // ---------------- primitives ----------------

        public void GlowLine(Color c, float w, float x1, float y1, float x2, float y2)
        {
            G.DrawLine(P(A(c, 40), w * 4f), x1, y1, x2, y2);
            G.DrawLine(P(A(c, 90), w * 2f), x1, y1, x2, y2);
            G.DrawLine(P(c, w), x1, y1, x2, y2);
        }

        public void GlowCircle(Color c, float w, float cx, float cy, float r)
        {
            if (!(r > 0) || !Fin(cx, cy) || float.IsInfinity(r)) return;
            G.DrawEllipse(P(A(c, 40), w * 4f), cx - r, cy - r, 2 * r, 2 * r);
            G.DrawEllipse(P(A(c, 90), w * 2f), cx - r, cy - r, 2 * r, 2 * r);
            G.DrawEllipse(P(c, w), cx - r, cy - r, 2 * r, 2 * r);
        }

        public void Dot(Color c, float cx, float cy, float r)
        {
            if (!(r > 0) || !Fin(cx, cy) || float.IsInfinity(r)) return;
            G.FillEllipse(B(c), cx - r, cy - r, 2 * r, 2 * r);
        }

        public void GlowDot(Color c, float cx, float cy, float r)
        {
            Dot(A(c, 35), cx, cy, r * 3.2f);
            Dot(A(c, 80), cx, cy, r * 1.9f);
            Dot(c, cx, cy, r);
        }

        /// <summary>HUD corner brackets around r.</summary>
        public void HudFrame(RectangleF r, Color c, float len)
        {
            if (!Fin(r)) return;
            float l = S(len);
            Pen p = P(c, 1.5f);
            G.DrawLine(p, r.Left, r.Top, r.Left + l, r.Top); G.DrawLine(p, r.Left, r.Top, r.Left, r.Top + l);
            G.DrawLine(p, r.Right, r.Top, r.Right - l, r.Top); G.DrawLine(p, r.Right, r.Top, r.Right, r.Top + l);
            G.DrawLine(p, r.Left, r.Bottom, r.Left + l, r.Bottom); G.DrawLine(p, r.Left, r.Bottom, r.Left, r.Bottom - l);
            G.DrawLine(p, r.Right, r.Bottom, r.Right - l, r.Bottom); G.DrawLine(p, r.Right, r.Bottom, r.Right, r.Bottom - l);
        }

        /// <summary>Text with alignment: align 0 = left, 1 = centre, 2 = right of x; y = top.</summary>
        public void Text(string s, Font f, Color c, float x, float y, int align)
        {
            if (string.IsNullOrEmpty(s) || !Fin(x, y)) return;
            StringFormat fmt = align == 1 ? Center : (align == 2 ? Far : Near);
            G.DrawString(s, f, B(Readable(c)), x, y, fmt);
        }

        /// <summary>Text vertically centred on y.</summary>
        public void TextMid(string s, Font f, Color c, float x, float y, int align)
        {
            if (string.IsNullOrEmpty(s) || !Fin(x, y)) return;
            StringFormat fmt = align == 1 ? CenterMid : (align == 2 ? FarMid : NearMid);
            G.DrawString(s, f, B(Readable(c)), x, y, fmt);
        }

        public void TextIn(string s, Font f, Color c, RectangleF r, StringFormat fmt)
        {
            if (string.IsNullOrEmpty(s)) return;
            G.DrawString(s, f, B(Readable(c)), r, fmt);
        }

        public SizeF Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return SizeF.Empty;
            return G.MeasureString(s, f, PointF.Empty, Near);
        }

        /// <summary>Line height of a font (px). Stack lines with this instead of a fixed gx.S(14): fonts grow with the window.</summary>
        public float LineH(Font f) { return f.GetHeight(G); }

        /// <summary>Two lines (e.g. 「左侧身」 / 「弹孔中心」) stacked with the font's own line height; align as Text, y = top.
        /// Returns the height used.</summary>
        public float TextLines(string first, string second, Font f, Color c, float x, float y, int align)
        {
            float lh = f.GetHeight(G);
            Text(first, f, c, x, y, align);
            if (string.IsNullOrEmpty(second)) return lh;
            Text(second, f, c, x, y + lh, align);
            return 2 * lh;
        }

        /// <summary>
        /// One pill with a label and a live value inside it, e.g. 「准星：你的上抬偏移量  24 px」 or 「X 偏移 · 示意  +75」: both
        /// are measured, so the value never sticks out of the pill. The value is drawn in valueColor (Text when Empty).
        /// align as Text. Returns the pill rectangle. Cache the value string until it changes (TutorialNum for numbers).
        /// </summary>
        public RectangleF TagValue(string label, string value, float x, float y, Color c, Color valueColor, int align)
        {
            if (string.IsNullOrEmpty(value)) return Tag(label, x, y, c, align);
            if (!Fin(x, y)) return RectangleF.Empty;
            if (string.IsNullOrEmpty(label)) label = "";
            SizeF ls = Measure(label, F.Tag), vs = Measure(value, F.Tag);
            float padX = S(6), padY = S(2.5f), gap = label.Length > 0 ? S(7) : 0;
            float w = ls.Width + gap + vs.Width + 2 * padX, h = Math.Max(ls.Height, vs.Height) + 2 * padY;
            float left = align == 1 ? x - w / 2 : (align == 2 ? x - w : x);
            RectangleF r = new RectangleF(left, y, w, h);
            G.FillRectangle(B(A(Pal.SceneBg, 200)), r);
            G.DrawRectangle(P(A(c, 200), 1f), r.X, r.Y, r.Width, r.Height);
            if (label.Length > 0) G.DrawString(label, F.Tag, B(Readable(c)), r.X + padX, r.Y + padY, Near);
            Color vc = valueColor.A == 0 ? Pal.Text : valueColor;
            G.DrawString(value, F.Tag, B(Readable(vc)), r.X + padX + ls.Width + gap, r.Y + padY, Near);
            RecordTag(r, label);
            return r;
        }

        /// <summary>Small pill label; align as Text. Returns its rectangle.</summary>
        public RectangleF Tag(string s, float x, float y, Color c, int align)
        {
            if (string.IsNullOrEmpty(s) || !Fin(x, y)) return RectangleF.Empty;
            SizeF sz = Measure(s, F.Tag);
            float padX = S(6), padY = S(2.5f);
            float w = sz.Width + 2 * padX, h = sz.Height + 2 * padY;
            float left = align == 1 ? x - w / 2 : (align == 2 ? x - w : x);
            RectangleF r = new RectangleF(left, y, w, h);
            G.FillRectangle(B(A(Pal.SceneBg, 200)), r);
            G.DrawRectangle(P(A(c, 200), 1f), r.X, r.Y, r.Width, r.Height);
            G.DrawString(s, F.Tag, B(Readable(c)), r.X + padX, r.Y + padY, Near);
            RecordTag(r, s);
            return r;
        }

        public const int MarkDot = 0, MarkRing = 1, MarkLine = 2, MarkDash = 3;

        /// <summary>One legend line (v4.1: replaces extra HUD tags, a view shows at most two): a small colour mark (MarkDot /
        /// MarkRing / MarkLine / MarkDash) + text in the label font on a faint backing, top-left at (x, y); align 2 = right
        /// edge at x. Deferred labels keep out of it. Returns its rectangle.</summary>
        public RectangleF Legend(string s, float x, float y, Color c, int mark, int align)
        {
            if (string.IsNullOrEmpty(s) || !Fin(x, y)) return RectangleF.Empty;
            SizeF sz = Measure(s, F.Small);
            float m = S(12), gap = S(5), padX = S(4), padY = S(1);
            float w = padX + m + gap + sz.Width + padX, h = sz.Height + 2 * padY;
            float left = align == 2 ? x - w : (align == 1 ? x - w / 2 : x);
            RectangleF r = new RectangleF(left, y, w, h);
            G.FillRectangle(B(A(Pal.SceneBg, 150)), r);
            float mx = r.Left + padX, my = r.Top + h / 2;
            switch (mark)
            {
                case MarkRing: G.DrawEllipse(P(c, 1.4f), mx + m / 2 - S(4), my - S(4), S(8), S(8)); break;
                case MarkLine: G.DrawLine(P(c, 1.8f), mx, my, mx + m, my); break;
                case MarkDash: G.DrawLine(Dashed(c, 1.6f), mx, my, mx + m, my); break;
                default: Dot(c, mx + m / 2, my, S(3)); break;
            }
            G.DrawString(s, F.Small, B(Readable(c)), mx + m + gap, r.Top + padY, Near);
            AddObstacle(r);
            return r;
        }

        /// <summary>Clickable chip (recorded for HitChip). Hover = mouse inside. Returns its rectangle.</summary>
        public RectangleF Chip(int id, RectangleF r, string text, bool active)
        {
            bool hover = r.Contains(Mouse);
            Color edge = active ? Pal.Neon : (hover ? Pal.Accent : Pal.Border);
            G.FillRectangle(B(active ? A(Pal.Neon, 40) : A(Pal.SceneBg, 220)), r);
            if (active) G.DrawRectangle(P(A(Pal.Neon, 70), 4f), r.X, r.Y, r.Width, r.Height);
            G.DrawRectangle(P(edge, 1.2f), r.X, r.Y, r.Width, r.Height);
            G.DrawString(text, F.Button, B(Readable(active ? Pal.Text : (hover ? Pal.Text : Pal.Muted))), new RectangleF(r.X, r.Y, r.Width, r.Height), CenterMid);
            if (chipCount < MaxChips) { chipRect[chipCount] = r; chipId[chipCount] = id; chipCount++; }
            return r;
        }

        /// <summary>Registers an invisible clickable area (e.g. a card drawn by the step).</summary>
        public void HitArea(int id, RectangleF r)
        {
            if (chipCount < MaxChips) { chipRect[chipCount] = r; chipId[chipCount] = id; chipCount++; }
        }

        /// <summary>Chip id under p from the last frame, or -1.</summary>
        public int HitChip(PointF p)
        {
            for (int i = chipCount - 1; i >= 0; i--) if (chipRect[i].Contains(p)) return chipId[i];
            return -1;
        }

        public bool HoverAny() { return HitChip(Mouse) >= 0; }

        /// <summary>One key cap (lit = glowing).</summary>
        public void KeyCap(float x, float y, float size, string label, bool lit, bool focus)
        {
            if (!Fin(x, y) || !(size > 0)) return;
            RectangleF r = new RectangleF(x, y, size, size);
            AddObstacle(r);
            if (lit) { G.FillRectangle(B(A(Pal.Neon, 60)), r.X - S(2), r.Y - S(2), r.Width + S(4), r.Height + S(4)); }
            G.FillRectangle(B(lit ? A(Pal.Neon, 150) : A(Pal.Surface, 235)), r);
            G.DrawRectangle(P(lit ? Pal.Neon : (focus ? Pal.Accent : Pal.Border), lit ? 1.6f : 1.1f), r.X, r.Y, r.Width, r.Height);
            G.DrawString(label, F.Key, B(lit ? Color.Black : Readable(focus ? Pal.Text : Pal.Muted)), r, CenterMid);
        }

        /// <summary>Q W E / A S D cluster (+ mouse when showFire) at x, y; lit = TKey bits held, focus = bits this step is about.</summary>
        public SizeF KeyCluster(float x, float y, float cap, int lit, int focus, bool showFire)
        {
            float c = S(cap), gap = S(4);
            KeyCap(x, y, c, "Q", (lit & TKey.Q) != 0, (focus & TKey.Q) != 0);
            KeyCap(x + c + gap, y, c, "W", (lit & TKey.W) != 0, (focus & TKey.W) != 0);
            KeyCap(x + 2 * (c + gap), y, c, "E", (lit & TKey.E) != 0, (focus & TKey.E) != 0);
            KeyCap(x + c * 0.25f, y + c + gap, c, "A", (lit & TKey.A) != 0, (focus & TKey.A) != 0);
            KeyCap(x + c * 0.25f + c + gap, y + c + gap, c, "S", (lit & TKey.S) != 0, (focus & TKey.S) != 0);
            KeyCap(x + c * 0.25f + 2 * (c + gap), y + c + gap, c, "D", (lit & TKey.D) != 0, (focus & TKey.D) != 0);
            float w = c * 3.25f + 2 * gap;
            if (showFire)
            {
                float mx = x + w + S(10), mw = c * 0.9f, mh = c * 1.35f;
                bool on = (lit & TKey.Fire) != 0;
                RectangleF body = new RectangleF(mx, y + S(2), mw, mh);
                G.FillEllipse(B(A(Pal.Surface, 235)), body);
                G.DrawEllipse(P(Pal.Border, 1.1f), body);
                // left button
                G.FillPie(B(on ? Pal.Hot : A(Pal.Border, 160)), body.X, body.Y, body.Width, body.Width, 180, 90);
                G.DrawLine(P(Pal.Border, 1f), body.X + body.Width / 2, body.Y, body.X + body.Width / 2, body.Y + body.Width / 2);
                w = mx + mw - x;
            }
            return new SizeF(w, 2 * c + gap);
        }

        /// <summary>Two-tone ring like the overlay (outer outline under the inner colour).</summary>
        public void Ring(float cx, float cy, float diameter, Color inner, Color outer, float stroke)
        {
            if (!Fin(cx, cy)) return;
            float r = diameter / 2f;
            if (!(r >= 1)) r = 1;
            if (r > 100000f) r = 100000f;
            G.DrawEllipse(P(A(outer, 230), stroke + 2.2f), cx - r, cy - r, 2 * r, 2 * r);
            G.DrawEllipse(P(inner, stroke), cx - r, cy - r, 2 * r, 2 * r);
        }

        /// <summary>A bullet hole on the wall: dark core, hot rim, fading glow (fresh = 0 .. 1 old).</summary>
        public void Hole(float x, float y, float radius, double fresh01, double alpha01, Color rim)
        {
            if (alpha01 <= 0.01) return;
            float r = Math.Max(1.2f, radius);
            if (fresh01 > 0) Dot(A(rim, alpha01 * 90 * fresh01 / 255.0 * 2.8), x, y, r * (2.2f + 1.5f * (float)fresh01));
            Dot(A(rim, alpha01 * 0.85), x, y, r);
            Dot(A(Color.Black, alpha01 * 0.9), x, y, r * 0.55f);
        }

        /// <summary>Vertical measuring bracket from y1 to y2 at x with a label on its right.</summary>
        public void Bracket(float x, float y1, float y2, Color c, string label)
        {
            float t = S(6);
            Pen p = P(c, 1.4f);
            G.DrawLine(p, x, y1, x, y2);
            G.DrawLine(p, x - t, y1, x + t, y1);
            G.DrawLine(p, x - t, y2, x + t, y2);
            if (!string.IsNullOrEmpty(label)) TextMid(label, F.Small, c, x + S(8), (y1 + y2) / 2, 0);
        }

        /// <summary>Arrow from (x1,y1) to (x2,y2).</summary>
        public void Arrow(Color c, float w, float x1, float y1, float x2, float y2)
        {
            if (!Fin(x1, y1) || !Fin(x2, y2)) return;
            G.DrawLine(P(c, w), x1, y1, x2, y2);
            double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            dx /= len; dy /= len;
            float h = S(7);
            PointF[] tri = Pts(3);
            tri[0] = new PointF(x2, y2);
            tri[1] = new PointF((float)(x2 - dx * h - dy * h * 0.55), (float)(y2 - dy * h + dx * h * 0.55));
            tri[2] = new PointF((float)(x2 - dx * h + dy * h * 0.55), (float)(y2 - dy * h - dx * h * 0.55));
            G.FillPolygon(B(c), tri);
        }

        private const int BgSlots = 3;
        private readonly Bitmap[] bgCache = new Bitmap[BgSlots];
        private readonly Size[] bgSize = new Size[BgSlots];
        private readonly float[] bgK = new float[BgSlots];
        private readonly long[] bgUse = new long[BgSlots];
        private long bgClock;

        /// <summary>Scene background: dark fill + faint scan lines, drawn from a cached bitmap per size (3 sizes kept,
        /// rebuilt only when the size or scale changes), so it costs one blit per frame.</summary>
        public void SceneBackground(RectangleF r)
        {
            int w = Math.Max(1, (int)Math.Ceiling(r.Width)), h = Math.Max(1, (int)Math.Ceiling(r.Height));
            int slot = -1, lru = 0;
            for (int i = 0; i < BgSlots; i++)
            {
                if (bgCache[i] != null && bgSize[i].Width == w && bgSize[i].Height == h && bgK[i] == K) { slot = i; break; }
                if (bgUse[i] < bgUse[lru]) lru = i;
            }
            if (slot < 0)
            {
                slot = lru;
                if (bgCache[slot] != null) bgCache[slot].Dispose();
                Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics bg = Graphics.FromImage(bmp))
                using (SolidBrush fill = new SolidBrush(Pal.SceneBg))
                using (Pen scan = new Pen(A(Pal.Neon, 10), 1f))
                {
                    bg.FillRectangle(fill, 0, 0, w, h);
                    float step = Math.Max(2f, S(4));
                    for (float y = 0; y < h; y += step) bg.DrawLine(scan, 0, y, w, y);
                }
                bgCache[slot] = bmp; bgSize[slot] = new Size(w, h); bgK[slot] = K;
            }
            bgUse[slot] = ++bgClock;
            InterpolationMode im = G.InterpolationMode;
            G.InterpolationMode = InterpolationMode.NearestNeighbor; // 1:1 blit, no filtering
            G.DrawImage(bgCache[slot], r.Left, r.Top, w, h);
            G.InterpolationMode = im;
        }

        /// <summary>Perspective floor grid below the horizon (y = horizon), rolled by roll radians around (cx, horizon).</summary>
        public void Floor(RectangleF r, float cx, float horizon, double roll, Color c)
        {
            // no Save/Restore (they allocate): the transform is reset explicitly at the end
            G.TranslateTransform(cx, horizon);
            G.RotateTransform((float)(roll * 180.0 / Math.PI));
            float w = r.Width * 1.6f, h = r.Bottom - horizon + r.Height * 0.4f;
            Pen p = P(A(c, 26), 1f);
            for (int i = -10; i <= 10; i++)
            {
                float xb = i * w / 10f;
                G.DrawLine(p, 0, 0, xb * 1.4f, h);
            }
            for (int j = 1; j <= 9; j++)
            {
                float y = (float)(h * Math.Pow(j / 9.0, 2.2));
                G.DrawLine(p, -w, y, w, y);
            }
            G.DrawLine(P(A(c, 60), 1f), -w, 0, w, 0);
            G.ResetTransform();
        }

        /// <summary>Rotates following drawing by roll radians around (cx, cy). Call EndRoll() after.</summary>
        public void BeginRoll(float cx, float cy, double roll)
        {
            G.TranslateTransform(cx, cy);
            G.RotateTransform((float)(roll * 180.0 / Math.PI));
            G.TranslateTransform(-cx, -cy);
        }

        public void EndRoll() { G.ResetTransform(); }

        /// <summary>A target wall: dark board with a glowing edge and a 0.2 m grid, centred on (cx, cy), w x h px.</summary>
        public void Wall(float cx, float cy, float w, float h, Color edge, string label)
        {
            RectangleF r = new RectangleF(cx - w / 2, cy - h / 2, w, h);
            G.FillRectangle(B(A(Pal.Surface, 245)), r);
            int nx = 8, ny = 6;
            Pen grid = P(A(Pal.Neon, 22), 1f);
            for (int i = 1; i < nx; i++) { float x = r.Left + r.Width * i / nx; G.DrawLine(grid, x, r.Top, x, r.Bottom); }
            for (int j = 1; j < ny; j++) { float y = r.Top + r.Height * j / ny; G.DrawLine(grid, r.Left, y, r.Right, y); }
            G.DrawRectangle(P(A(edge, 70), 4f), r.X, r.Y, r.Width, r.Height);
            G.DrawRectangle(P(edge, 1.3f), r.X, r.Y, r.Width, r.Height);
            if (!string.IsNullOrEmpty(label)) Tag(label, r.Left + S(6), r.Top + S(6), edge, 0);
        }

        // ---------------- first-person rifle (low-poly 3D, 示意) ----------------
        // Parts are boxes in gun space (metres): u along the barrel (0 = rear of the upper receiver), v to the gun's right,
        // w up. Columns: u0, u1, v0, v1, w0, w1, tint (0 metal, 1 dark metal, 2 polymer, 3 glove, 4 optic). Drawn in this
        // fixed order, far to near, which is correct for the camera above-left-behind the gun (mirrored for the other hand).
        private static readonly float[] GunBox = {
            0.740f, 0.800f, -0.011f, 0.011f, -0.011f, 0.011f, 1,     // flash hider
            0.530f, 0.745f, -0.0075f, 0.0075f, -0.0075f, 0.0075f, 1, // barrel
            0.545f, 0.580f, -0.012f, 0.012f, -0.014f, 0.022f, 1,     // gas block / front sight base
            0.180f, 0.550f, -0.021f, 0.021f, -0.024f, 0.028f, 0,     // handguard
            0.200f, 0.545f, -0.010f, 0.010f, 0.028f, 0.036f, 1,      // top rail
            0.300f, 0.420f, -0.050f, -0.006f, -0.050f, -0.006f, 3,   // support hand: palm under the left side
            0.312f, 0.329f, -0.030f, -0.008f, -0.006f, 0.030f, 3,    // fingers over the top-left edge
            0.335f, 0.352f, -0.030f, -0.008f, -0.006f, 0.031f, 3,
            0.358f, 0.375f, -0.030f, -0.008f, -0.006f, 0.031f, 3,
            0.381f, 0.398f, -0.030f, -0.008f, -0.006f, 0.029f, 3,
            0.100f, 0.165f, -0.013f, 0.013f, -0.210f, -0.130f, 2,    // magazine, lower part (curves forward)
            0.085f, 0.150f, -0.013f, 0.013f, -0.130f, -0.045f, 2,    // magazine, upper part
            0.020f, 0.175f, -0.015f, 0.015f, -0.050f, -0.010f, 1,    // lower receiver
            -0.040f, 0.185f, -0.017f, 0.017f, -0.012f, 0.032f, 0,    // upper receiver
            0.060f, 0.130f, -0.012f, 0.012f, 0.032f, 0.040f, 1,      // optic mount
            0.045f, 0.140f, -0.016f, 0.016f, 0.040f, 0.074f, 4,      // optic housing
        };
        private const int GunCols = 7, GunOptic = 15, GunRail = 4;
        /// <summary>Cross-section scale of the parts (a first-person gun reads better a little chunky).</summary>
        private const double GunThick = 2.0;
        private const float GunMuzzleU = 0.800f;
        /// <summary>Gun placement for the right hand in camera space (metres, x right, y down, z forward). The gun points at
        /// the screen centre 25 m away (the 示意 zero point), so the muzzle sits bottom-right of the 5-15 m impacts.</summary>
        public const float GunRearX = 0.308f, GunRearY = 0.331f, GunRearZ = 0.50f; // muzzle stays where it was; less perspective
        private static readonly int[] GunFace = { 0, 2, 6, 4, /* -u */ 1, 3, 7, 5, /* +u */ 0, 1, 5, 4, /* -v */ 2, 3, 7, 6, /* +v */
                                                  0, 1, 3, 2, /* -w */ 4, 5, 7, 6 /* +w */ };
        private static readonly float[] GunLight = { 1.10f, 0.75f, 0.85f, 0.85f, 0.55f, 1.55f };
        private static readonly int[] GunTintR = { 46, 30, 36, 40, 26 }, GunTintG = { 52, 34, 38, 46, 30 }, GunTintB = { 58, 38, 40, 40, 34 };
        private readonly double[] gunC = new double[8 * 3];
        private readonly float[] gunPx = new float[8], gunPy = new float[8];
        private double gax, gay, gaz, grx, gry, grz, ggx, ggy, ggz, gox, goy, goz, gsgn, gfocal, gcx, gcy, gzRear, gzMuzzle, gswX, gswY;
        /// <summary>Screen bounds of the last Gun() drawing (keep labels out of it). Empty when nothing was drawn.</summary>
        public RectangleF LastGunBounds;

        /// <summary>Screen offset (px, from the scene centre, y down) of the right-hand gun's muzzle at rest (no sway / kick /
        /// hand switch). Steps use it to keep impacts above-left of the muzzle (the bullet flies from the muzzle to them).</summary>
        public static PointF RestMuzzleOffset(RectangleF scene)
        {
            double rx = GunRearX, ry = GunRearY, rz = GunRearZ;
            double tx = -rx, ty = -ry, tz = 25.0 - rz, tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            double mx = rx + tx / tl * GunMuzzleU, my = ry + ty / tl * GunMuzzleU, mz = rz + tz / tl * GunMuzzleU;
            double f = scene.Height * 1.05;
            return new PointF((float)(f * mx / mz), (float)(f * my / mz));
        }

        /// <summary>Gun space (u, v, w) -> camera space (x, y, z) for the current Gun() call.</summary>
        private void GunPoint(double u, double v, double w, out double x, out double y, out double z)
        {
            double vv = v * gsgn * GunThick;
            w *= GunThick;
            x = gox + gax * u + grx * vv + ggx * w;
            y = goy + gay * u + gry * vv + ggy * w;
            z = goz + gaz * u + grz * vv + ggz * w;
        }

        /// <summary>Camera space -> screen, with the sway offset weighted by depth (the muzzle sways most).</summary>
        private PointF GunProject(double x, double y, double z)
        {
            if (z < 0.05) z = 0.05;
            double t = (z - gzRear) / Math.Max(0.05, gzMuzzle - gzRear);
            if (t < 0) t = 0; if (t > 1) t = 1;
            double sw = 0.55 + 0.45 * t;
            return new PointF((float)(gcx + gfocal * x / z + gswX * sw), (float)(gcy + gfocal * y / z + gswY * sw));
        }

        /// <summary>
        /// Stylised first-person rifle (low-poly 3D boxes with neon edges, 示意). handBlend 0 = right hand (bottom-right) .. 1 =
        /// switched (bottom-left); mid-switch the gun dips out of view and comes back on the other side. sway / kick in px.
        /// Returns the muzzle point. LastGunBounds = its screen rectangle.
        /// </summary>
        public PointF Gun(RectangleF scene, float swayX, float swayY, float kick, double handBlend)
        {
            return Gun(scene, swayX, swayY, kick, handBlend, 1f);
        }

        /// <summary>Same with an overall opacity (0..1), e.g. 0.6 so holes behind the gun stay readable.</summary>
        public PointF Gun(RectangleF scene, float swayX, float swayY, float kick, double handBlend, float alpha)
        {
            double b = handBlend < 0 ? 0 : (handBlend > 1 ? 1 : handBlend);
            if (alpha <= 0.01f) { LastGunBounds = RectangleF.Empty; return new PointF(scene.Right, scene.Bottom); }
            if (alpha > 1f) alpha = 1f;
            bool left = b >= 0.5;
            double drop = Math.Sin(b * Math.PI);
            gsgn = left ? -1 : 1;
            gcx = scene.Left + scene.Width / 2.0;
            gcy = scene.Top + scene.Height / 2.0;
            gfocal = scene.Height * 1.05;
            gswX = swayX; gswY = swayY;
            double k = kick < 0 ? 0 : kick;
            gox = gsgn * GunRearX; goy = GunRearY + drop * 0.45 + k * 0.0006; goz = GunRearZ - k * 0.0012;
            // forward axis toward the zero point (raised a little by the kick), right and up axes
            double tx = -gox, ty = -k * 0.08 - goy, tz = 25.0 - goz;
            double tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            gax = tx / tl; gay = ty / tl; gaz = tz / tl;
            // right = a x up(0,-1,0) -> (az, 0, -ax) normalised; gun up = right x a
            double rl = Math.Sqrt(gaz * gaz + gax * gax);
            grx = gaz / rl; gry = 0; grz = -gax / rl;
            ggx = gry * gaz - grz * gay; ggy = grz * gax - grx * gaz; ggz = grx * gay - gry * gax;
            gzRear = goz + gaz * 0.18; gzMuzzle = goz + gaz * GunMuzzleU;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            PointF[] quad = Pts(4);
            int boxes = GunBox.Length / GunCols;
            for (int bi2 = 0; bi2 < boxes; bi2++)
            {
                int o = bi2 * GunCols;
                int tint = (int)GunBox[o + 6];
                bool skip = false;
                for (int c = 0; c < 8; c++)
                {
                    double u = (c & 1) != 0 ? GunBox[o + 1] : GunBox[o];
                    double v = (c & 2) != 0 ? GunBox[o + 3] : GunBox[o + 2];
                    double w = (c & 4) != 0 ? GunBox[o + 5] : GunBox[o + 4];
                    double x, y, z;
                    GunPoint(u, v, w, out x, out y, out z);
                    gunC[c * 3] = x; gunC[c * 3 + 1] = y; gunC[c * 3 + 2] = z;
                    if (z < 0.06) skip = true;
                    PointF p = GunProject(x, y, z);
                    gunPx[c] = p.X; gunPy[c] = p.Y;
                }
                if (skip) continue;
                for (int f = 0; f < 6; f++)
                {
                    int i0 = GunFace[f * 4], i1 = GunFace[f * 4 + 1], i2 = GunFace[f * 4 + 2], i3 = GunFace[f * 4 + 3];
                    // outward normal of the face in camera space
                    double nx, ny, nz;
                    switch (f)
                    {
                        case 0: nx = -gax; ny = -gay; nz = -gaz; break;
                        case 1: nx = gax; ny = gay; nz = gaz; break;
                        case 2: nx = -grx * gsgn; ny = -gry * gsgn; nz = -grz * gsgn; break;
                        case 3: nx = grx * gsgn; ny = gry * gsgn; nz = grz * gsgn; break;
                        case 4: nx = -ggx; ny = -ggy; nz = -ggz; break;
                        default: nx = ggx; ny = ggy; nz = ggz; break;
                    }
                    double fx = (gunC[i0 * 3] + gunC[i2 * 3]) * 0.5, fy = (gunC[i0 * 3 + 1] + gunC[i2 * 3 + 1]) * 0.5, fz = (gunC[i0 * 3 + 2] + gunC[i2 * 3 + 2]) * 0.5;
                    if (nx * fx + ny * fy + nz * fz >= 0) continue; // faces away from the camera
                    quad[0] = new PointF(gunPx[i0], gunPy[i0]); quad[1] = new PointF(gunPx[i1], gunPy[i1]);
                    quad[2] = new PointF(gunPx[i2], gunPy[i2]); quad[3] = new PointF(gunPx[i3], gunPy[i3]);
                    float li = GunLight[f];
                    int cr = Math.Min(255, (int)(GunTintR[tint] * li)), cg = Math.Min(255, (int)(GunTintG[tint] * li)), cb = Math.Min(255, (int)(GunTintB[tint] * li));
                    G.FillPolygon(B(Color.FromArgb((int)(252 * alpha), cr, cg, cb)), quad);
                    // neon highlight on the top faces, faint edges elsewhere (reads as one solid gun, not a wire stack)
                    int ea = tint == 3 ? 55 : (f == 5 ? 165 : 38);
                    G.DrawPolygon(P(A(Pal.Neon, (int)(ea * alpha)), f == 5 ? 1.2f : 1f), quad);
                    for (int q = 0; q < 4; q++)
                    {
                        if (quad[q].X < minX) minX = quad[q].X; if (quad[q].X > maxX) maxX = quad[q].X;
                        if (quad[q].Y < minY) minY = quad[q].Y; if (quad[q].Y > maxY) maxY = quad[q].Y;
                    }
                    // optic: glass on the rear face
                    if (bi2 == GunOptic && f == 0)
                    {
                        PointF[] lens = Pts(4);
                        float mx = (quad[0].X + quad[1].X + quad[2].X + quad[3].X) / 4f, my = (quad[0].Y + quad[1].Y + quad[2].Y + quad[3].Y) / 4f;
                        for (int q = 0; q < 4; q++) lens[q] = new PointF(mx + (quad[q].X - mx) * 0.62f, my + (quad[q].Y - my) * 0.62f);
                        G.FillPolygon(B(A(Pal.Cyan, (int)(70 * alpha))), lens);
                        G.DrawPolygon(P(A(Pal.Cyan, (int)(200 * alpha)), 1.1f), lens);
                    }
                }
                // rail slots on the top rail
                if (bi2 == GunRail)
                {
                    Pen slot = P(A(Pal.Neon, (int)(80 * alpha)), 1f);
                    for (int t = 0; t < 10; t++)
                    {
                        double u = 0.215 + t * 0.0335, x1, y1, z1, x2, y2, z2;
                        GunPoint(u, -0.009, 0.036, out x1, out y1, out z1);
                        GunPoint(u, 0.009, 0.036, out x2, out y2, out z2);
                        G.DrawLine(slot, GunProject(x1, y1, z1), GunProject(x2, y2, z2));
                    }
                }
            }
            double mxw, myw, mzw;
            GunPoint(GunMuzzleU, 0, 0, out mxw, out myw, out mzw);
            PointF muzzle = GunProject(mxw, myw, mzw);
            if (minX <= maxX)
            {
                RectangleF bb = RectangleF.FromLTRB(minX, minY, maxX, maxY);
                bb.Intersect(scene);
                LastGunBounds = bb;
            }
            else LastGunBounds = RectangleF.Empty;
            return muzzle;
        }

        /// <summary>Muzzle flash star at p (life 0..1).</summary>
        public void Flash(PointF p, double life01)
        {
            if (life01 <= 0) return;
            float r = S(16) * (float)(0.6 + 0.4 * life01);
            Dot(A(Pal.Hot, 70 * life01 / 255.0 * 3), p.X, p.Y, r);
            Dot(A(Color.White, life01 * 0.9), p.X, p.Y, r * 0.35f);
            PointF[] star = Pts(8);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4 + 0.3;
                float rr = (i % 2 == 0) ? r : r * 0.45f;
                star[i] = new PointF((float)(p.X + Math.Cos(a) * rr), (float)(p.Y + Math.Sin(a) * rr));
            }
            G.FillPolygon(B(A(Pal.Hot, life01 * 0.8)), star);
        }

        /// <summary>Small plot frame with axis line; returns inner rect.</summary>
        public RectangleF PlotFrame(RectangleF r, string title)
        {
            return PlotFrame(r, title, null, null);
        }

        /// <summary>
        /// Plot frame with a title and an optional legend (each entry = a short line sample in its colour + the label). The
        /// legend goes on the title row when both fit, otherwise on its own row(s) under the title, so title and legend never
        /// overlap. Returns the inner plot rectangle below them. legend / colors: static arrays (no allocation per frame).
        /// </summary>
        public RectangleF PlotFrame(RectangleF r, string title, string[] legend, Color[] colors)
        {
            RegisterView(r, true);   // a plot is its own view (tag budget) and keeps deferred labels out
            G.FillRectangle(B(A(Pal.SceneBg, 230)), r);
            G.DrawRectangle(P(A(Pal.Neon, 90), 1f), r.X, r.Y, r.Width, r.Height);
            Font f = F.MonoSmall;
            float lh = f.GetHeight(G), pad = S(6), top = r.Top + S(3);
            float titleW = string.IsNullOrEmpty(title) ? 0 : Measure(title, f).Width;
            if (titleW > 0) Text(title, f, A(Pal.Neon, 220), r.Left + pad, top, 0);
            float y = top + (titleW > 0 ? lh : 0);
            int n = legend == null ? 0 : legend.Length;
            if (n > 0)
            {
                float sample = S(14), gapIn = S(4), gapOut = S(12), avail = r.Width - 2 * pad;
                float total = 0;
                for (int i = 0; i < n; i++) total += (i > 0 ? gapOut : 0) + sample + gapIn + Measure(legend[i], f).Width;
                bool sameRow = titleW > 0 && titleW + S(14) + total <= avail;
                float lx = sameRow ? r.Right - pad - total : r.Left + pad;
                float ly = sameRow ? top : y + S(1);
                for (int i = 0; i < n; i++)
                {
                    float tw = Measure(legend[i], f).Width;
                    float need = sample + gapIn + tw;
                    if (!sameRow && lx > r.Left + pad && lx + need > r.Right - pad) { lx = r.Left + pad; ly += lh; } // wrap
                    Color c = colors != null && i < colors.Length ? colors[i] : Pal.Text;
                    float my = ly + lh / 2;
                    G.DrawLine(P(c, 2f), lx, my, lx + sample, my);
                    Text(legend[i], f, c, lx + sample + gapIn, ly, 0);
                    lx += need + gapOut;
                }
                if (!sameRow) y = ly + lh;
            }
            float innerTop = y + S(4);
            return new RectangleF(r.Left + S(8), innerTop, r.Width - S(16), Math.Max(S(8), r.Bottom - S(6) - innerTop));
        }

    }

    // =====================================================================================================
    // First-person demo rig
    // =====================================================================================================

    /// <summary>A bullet hole kept in wall coordinates (metres, X right +, Y up +) so it stays on the wall.</summary>
    public struct TutorialHole
    {
        public double WX, WY, AgeMs;
        public int Kind;
    }

    /// <summary>
    /// The first-person demo used by most steps: a TRUTH MotionEngine (where the 示意 gun really points = where the bullets
    /// go) and a RING MotionEngine (the program's ring), both fed the same input. Steps change TruthP / RingP values
    /// (clones, never the user's profile) to show "untuned" vs "tuned". Holes = truth aim (+ hipfire parallax when
    /// UseParallax) at the shot time. Coordinates: UI px, X right +, Y up +; on screen = centre + UI * Zoom * K.
    /// </summary>
    public sealed class FpRig
    {
        public readonly MotionEngine Truth = new MotionEngine();
        public readonly MotionEngine Ring = new MotionEngine();
        public Profile TruthP, RingP;
        public GlobalSettings DemoG;

        // ---- options ----
        /// <summary>Demo px per UI px (before K).</summary>
        public double Zoom = 1.8;
        /// <summary>Current wall distance (eases toward TargetDistanceM over ~600 ms).</summary>
        public double DistanceM = 10, TargetDistanceM = 10;
        public bool UseParallax;
        public bool ShowRing = true, ShowAim, ShowCentre, ShowGun = true, ShowWall = true, ShowFloor = true;
        /// <summary>World roll per full lean (radians, 示意). Direction: Q (左侧身) turns the scene counter-clockwise, E clockwise
        /// -- Leo confirmed this in game (16 Nov 2026); do not flip it.</summary>
        public double RollPerLean = 6.0 * Math.PI / 180.0;
        /// <summary>Gun opacity (1 = solid). Holes hidden behind the gun always get an outline drawn over it.</summary>
        public float GunAlpha = 1f;
        /// <summary>Screen rectangle of the gun after the last Draw (keep labels out of it); Empty when no gun.</summary>
        public RectangleF GunBounds;
        /// <summary>Where tracers (and the muzzle flash) start. Empty (default) = the rig gun's real muzzle when ShowGun, else
        /// the bottom corner on the gun's side (left after 换手). A step that draws its own gun sets this every frame to that
        /// gun's muzzle (the point gx.Gun returns).</summary>
        public PointF TracerOrigin = PointF.Empty;
        /// <summary>Draw the tracer lines (false = holes only).</summary>
        public bool ShowTracers = true;
        /// <summary>Output: the tracer origin used by the last Draw (screen px).</summary>
        public PointF MuzzlePoint;
        /// <summary>Output: the muzzle of the gun the rig drew in the last Draw (screen px, follows sway, kick and 换手);
        /// Empty when the rig drew no gun (ShowGun false).</summary>
        public PointF Muzzle;
        public double ShotIntervalMs = 75;
        /// <summary>Random spread radius of the holes (UI px).</summary>
        public double SpreadPx = 2.2;
        public double HoleFadeMs = 9000;
        /// <summary>Kind stored with new holes (steps colour holes by kind, e.g. 0 = normal, 1 = second phase).</summary>
        public int NextHoleKind;
        /// <summary>Wall size in metres (示意).</summary>
        public double WallWm = 1.6, WallHm = 1.2;
        public string WallLabel;
        /// <summary>Non-empty: Draw clips to this rectangle (inside the scene) instead of the whole scene (wipe effects).</summary>
        public RectangleF ClipOverride = RectangleF.Empty;

        // ---- outputs (updated by Update) ----
        public double AimX, AimY;   // where bullets go now (UI px), incl. parallax
        public double RingX, RingY; // the ring (UI px)
        public double Roll;         // radians
        public double HandBlend;    // 0 正常手 .. 1 换手
        public int ShotsThisFrame;
        public int ShotsTotal;
        public double LastShotX, LastShotY;
        public bool Firing;

        private const int MaxHoles = 200;
        public readonly TutorialHole[] Holes = new TutorialHole[MaxHoles];
        public int HoleCount;
        private int holeHead;
        private const int MaxTracers = 12;
        private readonly double[] trX = new double[MaxTracers], trY = new double[MaxTracers], trAge = new double[MaxTracers];
        private int trHead;
        private double flashAge = 999, kick;
        private bool prevFire;
        private double nextShotMs, clockMs;
        private double distFrom = 10, distT = 1;
        private readonly TutorialRng rng = new TutorialRng(12345);

        /// <summary>Fresh rig: clones of the profiles, engines reset, holes cleared.</summary>
        public void Setup(TutorialContext c, Profile truthSource, Profile ringSource, bool handEnabled)
        {
            DemoG = c.DemoSettings(handEnabled);
            TruthP = truthSource.CloneValues();
            RingP = ringSource.CloneValues();
            Truth.Reset(); Ring.Reset();
            ClearHoles();
            prevFire = false; nextShotMs = 0; clockMs = 0; flashAge = 999; kick = 0;
            ShotsTotal = 0; ShotsThisFrame = 0;
            for (int i = 0; i < MaxTracers; i++) trAge[i] = 999;
            DistanceM = TargetDistanceM; distFrom = DistanceM; distT = 1;
            rng.Seed(12345);
        }

        public void ClearHoles() { HoleCount = 0; holeHead = 0; }

        /// <summary>Jump the wall to a distance at once (no easing).</summary>
        public void SetDistanceNow(double m) { DistanceM = TargetDistanceM = m; distFrom = m; distT = 1; }

        /// <summary>Ease the wall to a new distance.</summary>
        public void MoveWallTo(double m)
        {
            if (m == TargetDistanceM) return;
            distFrom = DistanceM; TargetDistanceM = m; distT = 0;
        }

        private static void UiOffset(MotionEngine e, out double x, out double y) { x = e.OffsetX; y = -e.OffsetY; }

        public void Update(double dtMs, InputState input, bool fire)
        {
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            if (dtMs > 100) dtMs = 100;
            clockMs += dtMs;
            input.Fire = fire;
            Truth.Update(dtMs / 1000.0, input, TruthP, DemoG, PreviewMode.Live, 1.0);
            Ring.Update(dtMs / 1000.0, input, RingP, DemoG, PreviewMode.Live, 1.0);

            if (distT < 1)
            {
                distT = Math.Min(1, distT + dtMs / 650.0);
                DistanceM = TutorialGeometry.Lerp(distFrom, TargetDistanceM, TutorialGeometry.Smooth(distT));
            }
            double tx, ty;
            UiOffset(Truth, out tx, out ty);
            if (UseParallax)
            {
                double px, py;
                TutorialGeometry.HipImpactPx(DistanceM, out px, out py);
                tx += px; ty += py;
            }
            AimX = tx; AimY = ty;
            UiOffset(Ring, out RingX, out RingY);
            Roll = Truth.Lean * RollPerLean;
            HandBlend = Truth.HandBlend;

            // shots
            ShotsThisFrame = 0;
            Firing = fire;
            if (fire)
            {
                if (!prevFire) { Shoot(); nextShotMs = clockMs + ShotIntervalMs; }
                else
                {
                    int guard = 0;
                    while (clockMs >= nextShotMs && guard++ < 4) { Shoot(); nextShotMs += ShotIntervalMs; }
                }
            }
            prevFire = fire;

            // ageing
            for (int i = 0; i < HoleCount; i++) Holes[i].AgeMs += dtMs;
            for (int i = 0; i < MaxTracers; i++) trAge[i] += dtMs;
            flashAge += dtMs;
            kick *= Math.Exp(-dtMs / 70.0);
        }

        private void Shoot()
        {
            double sx = AimX + rng.Spread() * SpreadPx, sy = AimY + rng.Spread() * SpreadPx;
            // screen -> wall (undo the roll so the hole stays on the rolled wall)
            double c = Math.Cos(-Roll), s = Math.Sin(-Roll);
            double ux = sx * c - (-sy) * s, uyDown = sx * s + (-sy) * c; // rotate in screen (y-down) space
            TutorialHole h = new TutorialHole();
            h.WX = TutorialGeometry.ScreenToWall(ux, DistanceM);
            h.WY = TutorialGeometry.ScreenToWall(-uyDown, DistanceM);
            h.AgeMs = 0; h.Kind = NextHoleKind;
            if (HoleCount < MaxHoles) { Holes[HoleCount++] = h; }
            else { Holes[holeHead] = h; holeHead = (holeHead + 1) % MaxHoles; }
            trX[trHead] = sx; trY[trHead] = sy; trAge[trHead] = 0; trHead = (trHead + 1) % MaxTracers;
            flashAge = 0; kick = 1;
            ShotsThisFrame++; ShotsTotal++;
            LastShotX = sx; LastShotY = sy;
        }

        /// <summary>Screen point of a UI offset (no roll).</summary>
        public PointF ToScreen(RectangleF scene, float k, double uiX, double uiY)
        {
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            return new PointF((float)(cx + uiX * Zoom * k), (float)(cy - uiY * Zoom * k));
        }

        /// <summary>Hole colour by kind (steps may override by drawing holes themselves).</summary>
        public Color[] KindColors;

        public void Draw(TutorialGfx gx, RectangleF scene)
        {
            Graphics g = gx.G;
            float k = gx.K;
            float cx = scene.Left + scene.Width / 2f, cy = scene.Top + scene.Height / 2f;
            g.SetClip(ClipOverride.IsEmpty ? scene : RectangleF.Intersect(scene, ClipOverride));
            gx.SceneBackground(scene);
            if (ShowFloor) gx.Floor(scene, cx, cy + gx.S(70), Roll, gx.Pal.Neon);

            float z = (float)(Zoom * k);
            // world (rolled): wall + holes
            gx.BeginRoll(cx, cy, Roll);
            float wallW = 0, wallH = 0;
            if (ShowWall)
            {
                wallW = (float)(TutorialGeometry.ProjectedPx(WallWm, DistanceM) * z); wallH = (float)(TutorialGeometry.ProjectedPx(WallHm, DistanceM) * z);
                gx.Wall(cx, cy, wallW, wallH, gx.Pal.Accent, null);
            }
            for (int i = 0; i < HoleCount; i++)
            {
                TutorialHole h = Holes[i];
                double life = 1.0 - h.AgeMs / HoleFadeMs;
                if (life <= 0) continue;
                double ux = TutorialGeometry.WallToScreen(h.WX, DistanceM), uy = TutorialGeometry.WallToScreen(h.WY, DistanceM);
                float hx = (float)(cx + ux * z), hy = (float)(cy - uy * z);
                double fresh = Math.Max(0, 1 - h.AgeMs / 300.0);
                Color rim = (KindColors != null && h.Kind >= 0 && h.Kind < KindColors.Length) ? KindColors[h.Kind] : gx.Pal.Hot;
                gx.Hole(hx, hy, gx.S(2.6f) * (float)Math.Min(1.6, 10.0 / Math.Max(4, DistanceM) + 0.3), fresh, Math.Min(1, life * 2), rim);
            }
            gx.EndRoll();
            // v4.1: the wall's distance label is upright text at the (rolled) top-left corner of the wall, drawn after the
            // step (gx.LateLabel), so it is moved below the HUD tags / key caps instead of being hidden under them
            if (ShowWall && !string.IsNullOrEmpty(WallLabel) && wallW > 0 && wallH > 0)
            {
                double cr = Math.Cos(Roll), sr = Math.Sin(Roll);
                double lx = -wallW / 2 + gx.S(6), ly = -wallH / 2 + gx.S(6);           // inside the corner, unrolled
                float ax = (float)(cx + lx * cr - ly * sr), ay = (float)(cy + lx * sr + ly * cr);
                float hw = (float)(Math.Abs(wallW / 2 * cr) + Math.Abs(wallH / 2 * sr)), hh = (float)(Math.Abs(wallW / 2 * sr) + Math.Abs(wallH / 2 * cr));
                RectangleF wb = new RectangleF(cx - hw, cy - hh, 2 * hw, 2 * hh);
                RectangleF bounds = ClipOverride.IsEmpty ? scene : RectangleF.Intersect(scene, ClipOverride);
                bounds = RectangleF.Intersect(bounds, wb);
                bounds.Inflate(-gx.S(4), -gx.S(4));
                gx.LateLabel(WallLabel, gx.Pal.Accent, ax, ay, bounds);
            }

            if (ShowCentre)
            {
                gx.Dot(Color.FromArgb(230, 255, 255, 255), cx, cy, gx.S(1.6f));
            }
            // tracers
            PointF muzzle = PointF.Empty;
            GunBounds = RectangleF.Empty;
            Muzzle = PointF.Empty;
            if (ShowGun)
            {
                float swayX = (float)(Truth.MoveX * 0.35 * z * 0.6), swayY = (float)(-Truth.MoveY * 0.35 * z * 0.6);
                muzzle = gx.Gun(scene, swayX, swayY, (float)(kick * gx.S(6)), HandBlend, GunAlpha);
                Muzzle = muzzle;
                GunBounds = gx.LastGunBounds;
                // holes behind the gun: an outline over the gun shows where they are (the key fact of most steps)
                if (!GunBounds.IsEmpty)
                {
                    double cr = Math.Cos(Roll), sr = Math.Sin(Roll);
                    for (int i = 0; i < HoleCount; i++)
                    {
                        TutorialHole h = Holes[i];
                        double life = 1.0 - h.AgeMs / HoleFadeMs;
                        if (life <= 0) continue;
                        double ux = TutorialGeometry.WallToScreen(h.WX, DistanceM) * z, uy = -TutorialGeometry.WallToScreen(h.WY, DistanceM) * z;
                        float hx = (float)(cx + ux * cr - uy * sr), hy = (float)(cy + ux * sr + uy * cr); // same roll as the wall
                        if (!GunBounds.Contains(hx, hy)) continue;
                        Color rim = (KindColors != null && h.Kind >= 0 && h.Kind < KindColors.Length) ? KindColors[h.Kind] : gx.Pal.Hot;
                        float rr = gx.S(5.5f);
                        g.DrawEllipse(gx.P(TutorialGfx.A(rim, Math.Min(1, life * 2) * 0.95), 1.4f), hx - rr, hy - rr, 2 * rr, 2 * rr);
                        gx.Dot(TutorialGfx.A(rim, Math.Min(1, life * 2) * 0.9), hx, hy, gx.S(1.6f));
                    }
                }
            }
            // tracer origin: the step's own point (TracerOrigin), else the rig gun's muzzle, else the bottom corner on the side
            // of the hand holding the gun (bottom-left after 换手)
            bool ownOrigin = !TracerOrigin.IsEmpty;
            PointF from = ownOrigin ? TracerOrigin
                : (ShowGun ? muzzle : new PointF(HandBlend >= 0.5 ? scene.Left + gx.S(40) : scene.Right - gx.S(40), scene.Bottom - gx.S(10)));
            MuzzlePoint = from;
            if (ShowTracers)
            {
                for (int i = 0; i < MaxTracers; i++)
                {
                    if (trAge[i] > 90) continue;
                    double life = 1 - trAge[i] / 90.0;
                    PointF hit = new PointF((float)(cx + trX[i] * z), (float)(cy - trY[i] * z));
                    g.DrawLine(gx.P(TutorialGfx.A(gx.Pal.Hot, life * 0.9), 1.6f), from, hit);
                }
            }
            if ((ShowGun || ownOrigin) && flashAge < 50) gx.Flash(from, 1 - flashAge / 50.0);
            if (ShowAim)
            {
                float ax = (float)(cx + AimX * z), ay = (float)(cy - AimY * z);
                gx.GlowDot(gx.Pal.Cyan, ax, ay, gx.S(2.6f));
            }
            if (ShowRing && RingP != null)
            {
                float rx = (float)(cx + RingX * z), ry = (float)(cy - RingY * z);
                Color inner = ParseColor(RingP.Color, gx.Pal.Neon), outer = ParseColor(RingP.Color2, Color.Black);
                float d = Math.Max(gx.S(16), (float)(RingP.Diameter * z));
                gx.Ring(rx, ry, d, inner, outer, Math.Max(1.5f, (float)(RingP.Thickness * Zoom * 0.8)));
                gx.AddObstacle(new RectangleF(rx - d / 2 - gx.S(2), ry - d / 2 - gx.S(2), d + gx.S(4), d + gx.S(4))); // labels keep off the ring
            }
            g.ResetClip();
        }

        public static Color ParseColor(string s, Color fallback)
        {
            int r, g, b;
            if (ColorUtil.TryParse(s, out r, out g, out b)) return Color.FromArgb(r, g, b);
            return fallback;
        }
    }

    // =====================================================================================================
    // Step registration. TutorialStepsA (steps 1-5, src/TutorialStepsA.cs) and TutorialStepsB (steps 6-10, written by TUT-B in
    // src/TutorialStepsB.cs) both return their steps in page order. TutorialStepsB is a partial class: TUT-B implements
    //     static partial void AddSteps(List<TutorialStep> steps) { steps.Add(new ...); ... }
    // in its own file. Until that file exists, Create() returns placeholder pages so the wizard still runs end to end.
    // =====================================================================================================

    public static partial class TutorialStepsB
    {
        static partial void AddSteps(List<TutorialStep> steps);

        public static List<TutorialStep> Create()
        {
            List<TutorialStep> l = new List<TutorialStep>();
            AddSteps(l);
            if (l.Count == 0)
            {
                l.Add(new TutorialPlaceholderStep("lean", "侧身", "侧身：站着不动，侧身开枪", false));
                l.Add(new TutorialPlaceholderStep("hand", "换手", "换手：所有偏移会左右反过来", false));
                l.Add(new TutorialPlaceholderStep("jump", "跳跃", "跳跃：子弹按预设路线走", false));
                l.Add(new TutorialPlaceholderStep("peek", "开发者的话", "最佳拉枪：右手 D + E，换手 Q + A", false));
                l.Add(new TutorialPlaceholderStep("done", "完成", "全部完成！", true));
            }
            return l;
        }
    }

    /// <summary>Stand-in page until TutorialStepsB.cs exists (also a minimal 完成 page with the settings cards).</summary>
    public sealed class TutorialPlaceholderStep : TutorialStep
    {
        private readonly bool finish;

        public TutorialPlaceholderStep(string id, string chapter, string title, bool finish)
        {
            Id = id; Chapter = chapter; Title = title; this.finish = finish;
            Body = finish ? "基本数值都调好了。点下面任意一页，回到那个设置页精调。\n这个教程随时可以在设置页重新打开。" : "（这一页的动画还在制作中。）";
            ClickFires = false;
        }

        public override void Enter(TutorialContext c) { if (finish) c.Finish(); }
        public override void Update(TutorialContext c, double dtMs) { }

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            gx.SceneBackground(scene);
            if (!finish) { gx.TextMid(Title, gx.F.Big, gx.Pal.Muted, scene.Left + scene.Width / 2, scene.Top + scene.Height / 2, 1); return; }
            int n = TutorialPages.SettingsOrder.Length;
            float cw = gx.S(170), ch = gx.S(44), gap = gx.S(12);
            int cols = 3;
            float totalW = cols * cw + (cols - 1) * gap;
            float x0 = scene.Left + (scene.Width - totalW) / 2, y0 = scene.Top + gx.S(60);
            for (int i = 0; i < n; i++)
            {
                RectangleF r = new RectangleF(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + gap), cw, ch);
                gx.Chip(i, r, TutorialPages.SettingsLabels[i], false);
            }
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (finish && id >= 0 && id < TutorialPages.SettingsOrder.Length) c.CloseAndJump(TutorialPages.SettingsOrder[id]);
        }
    }
}
