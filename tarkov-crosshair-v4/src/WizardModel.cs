// WizardModel.cs -- v5.1 (W26, decision 13): the first-run 设置向导 (pure: steps, validation, apply). WizardForm.cs draws it.
//
// Flow: 欢迎 -> 分辨率 -> 画面比例（+ 有没有被拉宽） -> FOV -> 开镜方式 -> 游戏按键 -> 准星快捷键 -> 开机启动 -> 必读 -> 完成.
// The user edits a DRAFT (GlobalSettings.Clone(false)); Apply copies only the fields the wizard owns into the live settings,
// so profiles and everything else stay untouched. 必读 must be acknowledged (我知道了) before 完成.
// Game keys are typed by the user (key capture); the wizard never reads Tarkov's files.
using System;
using System.Collections.Generic;

namespace TarkovCrosshair
{
    internal sealed class WizardModel
    {
        public enum Step { Welcome, Resolution, Aspect, Fov, Ads, GameKeys, Hotkeys, Autostart, MustRead, Done }

        public static readonly string[] Titles = new string[]
        {
            "欢迎", "分辨率", "画面比例", "FOV（视野）", "开镜方式", "游戏按键", "准星快捷键", "开机启动", "必读", "完成"
        };

        public static readonly string[] Leads = new string[]
        {
            "几步就能设置好。每一步都按游戏里的设置填，不确定的保持默认就行。",
            "游戏里的分辨率是多少？",
            "游戏里的画面比例是多少？",
            "游戏里的 FOV 是多少？",
            "游戏里开镜是「按住右键」还是「点一下右键」？",
            "下面这些键请改成你游戏里用的键。点一下按钮，再按键盘或鼠标。",
            "准星自己的快捷键。",
            "开机后要不要自动运行？",
            "最后，请看完这几条。",
            "设置完成！之后随时可以在设置里修改。",
        };

        public const int MinW = 640, MaxW = 7680, MinH = 480, MaxH = 4320;
        public const int FovMin = 50, FovMax = 75;

        /// <summary>Common resolutions (W, H). The wizard also offers 自动检测 and 自定义.</summary>
        public static readonly int[][] ResPresets = new int[][]
        {
            new int[] { 1920, 1080 }, new int[] { 2560, 1440 }, new int[] { 3840, 2160 }, new int[] { 1920, 1200 },
            new int[] { 2560, 1600 }, new int[] { 2560, 1080 }, new int[] { 3440, 1440 }, new int[] { 1680, 1050 },
            new int[] { 1600, 900 }, new int[] { 1440, 1080 }, new int[] { 1280, 1024 }, new int[] { 1280, 960 },
            new int[] { 2160, 1440 },
        };

        /// <summary>画面比例 choices: the game's list + 1:1; "custom" = the user types W:H.</summary>
        public static string[] AspectChoices()
        {
            List<string> r = new List<string>(ViewScale.GameAspectIds);
            if (!r.Contains("1:1")) r.Add("1:1");
            r.Add(CustomAspect);
            return r.ToArray();
        }

        public const string CustomAspect = "custom";
        public const string CustomAspectLabel = "自定义…";

        /// <summary>Answer to 「画面有没有被拉宽」: null = 不确定 / not asked.</summary>
        public bool? Stretched;

        public readonly GlobalSettings Draft;
        private int index;

        public WizardModel(GlobalSettings live)
        {
            Draft = live != null ? live.Clone(false) : new GlobalSettings();
            if (Draft.Keys == null) Draft.Keys = new KeyBindings();
            if (Draft.ResFov < FovMin || Draft.ResFov > FovMax) Draft.ResFov = FovMax;
            Stretched = ReadStretched(live);
        }

        public int Index { get { return index; } }
        public Step Current { get { return (Step)index; } }
        public int Count { get { return Titles.Length; } }
        public bool IsFirst { get { return index == 0; } }
        public bool IsLast { get { return index == Count - 1; } }
        public string Title { get { return Titles[index]; } }
        public string Lead { get { return Leads[index]; } }

        /// <summary>「第 3 步 / 共 10 步」</summary>
        public string Progress { get { return "第 " + (index + 1) + " 步 / 共 " + Count + " 步"; } }

        /// <summary>Why 下一步 is blocked on this step (null = can go on).</summary>
        public string Blocker()
        {
            switch (Current)
            {
                case Step.Resolution: return ResolutionError(Draft.ResGameW, Draft.ResGameH);
                case Step.Aspect: return AspectError(Draft.ResAspect);
                case Step.Fov: return Draft.ResFov >= FovMin && Draft.ResFov <= FovMax ? null : "FOV 请填 50 到 75 之间的整数。";
                case Step.GameKeys:
                case Step.Hotkeys: return KeyClash();
                case Step.MustRead: return Draft.MustReadAck ? null : "请先点「" + MustRead.AckText + "」。";
                default: return null;
            }
        }

        public bool Next()
        {
            if (IsLast || Blocker() != null) return false;
            index++;
            return true;
        }

        public bool Back()
        {
            if (IsFirst) return false;
            index--;
            return true;
        }

        public void GoTo(Step s) { index = Math.Max(0, Math.Min(Count - 1, (int)s)); }

        // ---------------------------------------------------------------- per-step rules

        public static string ResolutionError(double w, double h)
        {
            if (!(w >= MinW && w <= MaxW)) return "宽度请填 " + MinW + " 到 " + MaxW + "。";
            if (!(h >= MinH && h <= MaxH)) return "高度请填 " + MinH + " 到 " + MaxH + "。";
            return null;
        }

        /// <summary>Reads 「1920x1080」「1920×1080」「1920*1080」「1920 1080」. False for anything else or out of range.</summary>
        public static bool TryParseResolution(string s, out int w, out int h)
        {
            w = h = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim().Replace('×', 'x').Replace('X', 'x').Replace('*', 'x').Replace('＊', 'x').Replace(' ', 'x');
            while (t.Contains("xx")) t = t.Replace("xx", "x");
            string[] p = t.Split('x');
            if (p.Length != 2) return false;
            if (!int.TryParse(p[0], out w) || !int.TryParse(p[1], out h)) { w = h = 0; return false; }
            return ResolutionError(w, h) == null;
        }

        public static string AspectError(string id)
        {
            if (id == null || id == CustomAspect) return "请填画面比例，例如 16:9。";
            string n = ViewScale.NormalizeAspect(id);
            if (n == "auto" && id != "auto") return "画面比例要写成 宽:高，例如 16:9 或 123:111。";
            return null;
        }

        /// <summary>The ratio the user typed for 自定义 (any W:H). Returns false and leaves Draft unchanged when unreadable.</summary>
        public bool SetCustomAspect(string text)
        {
            int w, h;
            string t = (text ?? "").Trim().Replace('：', ':');
            if (!ViewScale.TryParseRatio(t, out w, out h)) return false;
            string n = ViewScale.NormalizeAspect(w + ":" + h);
            if (n == "auto") return false;
            Draft.ResAspect = n;
            Draft.ResAspectCustom = n;
            return true;
        }

        /// <summary>The chosen ratio differs from the resolution's own ratio: the game may stretch it (ask) or show bars.</summary>
        public bool NeedsStretchQuestion
        {
            get { return Math.Abs(ViewScale.StretchX(Draft.ResGameW, Draft.ResGameH, Draft.ResAspect) - 1.0) > 1e-6; }
        }

        /// <summary>「横向 ×1.20（拉伸）」 for the current draft, or "" when not stretched.</summary>
        public string StretchText
        {
            get
            {
                double sx = ViewScale.StretchX(Draft.ResGameW, Draft.ResGameH, Draft.ResAspect);
                if (Math.Abs(sx - 1.0) <= 1e-6) return "";
                return "横向 ×" + sx.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "（拉伸）";
            }
        }

        /// <summary>Hint beside the FOV box (the box itself is clamped to 50..75).</summary>
        public static string FovHint(double fov)
        {
            if (fov < FovMin) return "游戏里 FOV 最小是 50。";
            if (fov > FovMax) return "游戏里 FOV 最大是 75。";
            return "";
        }

        /// <summary>Two rebindable actions with the same key + modifiers (null = none).</summary>
        public string KeyClash()
        {
            KeyBindings k = Draft.Keys;
            foreach (int a in KeyBindings.RebindableActions)
                foreach (int b in KeyBindings.RebindableActions)
                {
                    if (b <= a) continue;
                    if (k.Get(a) != 0 && k.Get(a) == k.Get(b) && k.GetMods(a) == k.GetMods(b))
                        return "「" + KeyBindings.ActionNames[a] + "」和「" + KeyBindings.ActionNames[b] + "」用了同一个键，请改一个。";
                }
            if (Draft.GrenadeHideOn && Draft.GrenadeKey > 0)
                foreach (int a in KeyBindings.RebindableActions)
                    if (k.Get(a) == Draft.GrenadeKey && k.GetMods(a) == 0)
                        return "「丢雷」和「" + KeyBindings.ActionNames[a] + "」用了同一个键，请改一个。";
            return null;
        }

        // ---------------------------------------------------------------- apply

        /// <summary>Copies the wizard's fields from the draft into live and sets WizardDone (and MustReadAck when read).
        /// Never touches profiles or any field the wizard does not show.</summary>
        public void Apply(GlobalSettings live)
        {
            if (live == null) return;
            GlobalSettings d = Draft;
            live.ResGameW = d.ResGameW; live.ResGameH = d.ResGameH;
            live.ResAspect = d.ResAspect;
            if (!string.IsNullOrEmpty(d.ResAspectCustom)) live.ResAspectCustom = d.ResAspectCustom;
            live.ResFov = Math.Max(FovMin, Math.Min(FovMax, Math.Round(d.ResFov)));
            live.AdsMode = d.AdsMode == "toggle" ? "toggle" : "hold";
            if (live.Keys == null) live.Keys = new KeyBindings();
            foreach (int a in KeyBindings.RebindableActions) live.Keys.SetCombo(a, d.Keys.Get(a), d.Keys.GetMods(a));
            live.BoostToggle = d.BoostToggle;
            live.GrenadeHideOn = d.GrenadeHideOn;
            if (d.GrenadeKey > 0) live.GrenadeKey = d.GrenadeKey;
            if (d.AmmoReloadKey > 0) live.AmmoReloadKey = d.AmmoReloadKey;
            live.Autostart = d.Autostart;
            if (d.MustReadAck) live.MustReadAck = true;
            WriteStretched(live, NeedsStretchQuestion ? Stretched : null);
            live.WizardDone = true;
        }

        /// <summary>「跳过向导」: keep every value as it is, only remember the wizard was offered.</summary>
        public static void Skip(GlobalSettings live) { if (live != null) live.WizardDone = true; }

        // ResStretched is requested from W23 (V51-W23-REQ.md); until it exists in Settings.cs this reads / writes it by name.
        private static System.Reflection.MemberInfo stretchMember;
        private static bool stretchLooked;

        private static System.Reflection.MemberInfo StretchMember()
        {
            if (stretchLooked) return stretchMember;
            stretchLooked = true;
            try
            {
                Type t = typeof(GlobalSettings);
                System.Reflection.MemberInfo m = t.GetField("ResStretched");
                if (m == null) m = t.GetProperty("ResStretched");
                stretchMember = m;
            }
            catch (Exception) { stretchMember = null; }
            return stretchMember;
        }

        public static bool StretchFieldExists { get { return StretchMember() != null; } }

        private static bool? ReadStretched(GlobalSettings g)
        {
            if (g == null) return null;
            try
            {
                System.Reflection.MemberInfo m = StretchMember();
                object v = null;
                System.Reflection.FieldInfo f = m as System.Reflection.FieldInfo;
                System.Reflection.PropertyInfo p = m as System.Reflection.PropertyInfo;
                if (f != null) v = f.GetValue(g);
                else if (p != null) v = p.GetValue(g, null);
                if (v is bool) return (bool)v;
            }
            catch (Exception) { }
            return null;
        }

        private static void WriteStretched(GlobalSettings g, bool? v)
        {
            try
            {
                System.Reflection.MemberInfo m = StretchMember();
                System.Reflection.FieldInfo f = m as System.Reflection.FieldInfo;
                System.Reflection.PropertyInfo p = m as System.Reflection.PropertyInfo;
                Type vt = f != null ? f.FieldType : p != null ? p.PropertyType : null;
                if (vt == null) return;
                object val;
                if (vt == typeof(bool?)) val = v;
                else if (vt == typeof(bool)) val = v ?? true;
                else return;
                if (f != null) f.SetValue(g, val);
                else if (p.CanWrite) p.SetValue(g, val, null);
            }
            catch (Exception) { }
        }
    }
}
