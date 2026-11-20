// HelpText.cs -- v5.1 (W26): the shared 「?」 help texts (decision 13). One place for every feature's short explanation, written
// for a complete beginner. Pure part (HelpText) compiles everywhere; the WinForms 「?」 mark (HelpMark) is skipped by the Linux
// tests (UI_PURE_CHECK).
//
//   Other owners:  row.Controls.Add(HelpMark.For("grenade", font));     // a small 「?」 beside a feature
//                  string t = HelpText.Body("grenade");                 // the text, e.g. for a tooltip
//
// Texts that name a key read it from the user's settings when a GlobalSettings is passed (HelpText.Body(id, g)), so they never
// show a stale default.
using System;
using System.Collections.Generic;
#if !UI_PURE_CHECK
using System.Drawing;
using System.Windows.Forms;
#endif

namespace TarkovCrosshair
{
    internal static class HelpText
    {
        /// <summary>One help entry: id (stable, used by callers), title, body. {boost} {grenade} {toggle} {ads} {reload} are
        /// replaced by the user's current keys when a GlobalSettings is given.</summary>
        internal sealed class Entry
        {
            public readonly string Id, Title, Body;
            public Entry(string id, string title, string body) { Id = id; Title = title; Body = body; }
        }

        // Order = the order of the 常见问题 / 帮助 lists. Append new entries at the end; never reuse an id.
        private static readonly Entry[] all = new Entry[]
        {
            new Entry("enable", "开关准星",
                "按 {toggle} 打开或关闭准星。\n快速按两下 {toggle}（关再开）= 全部复位：回到正常手、强化准星关闭、丢雷隐藏取消、侧身取消。"),
            new Entry("ads", "开镜方式",
                "开镜时准星会完全消失。\n「按住」：按住 {ads} 开镜，松开回到腰射。\n「切换」：点一下 {ads} 开镜，再点一下退出。\n要和游戏里的开镜设置一样，否则开镜时还会看到准星。"),
            new Entry("boost", "强化准星",
                "战术装置用 T 打开，保持在红外（IR）模式。\n强化键 = 游戏里「切换战术装置模式」的键（{boost}）：切到手电时强化准星打开，切回红外时关闭。单独按 T 不算。\n「切换」：按一下开、再按一下关；「按住」：按住时显示。\n对不上时把装置切回红外，再快速按两下 {toggle}，强化回到关闭。"),
            new Entry("grenade", "丢雷隐藏",
                "按 {grenade} 拿雷时准星隐藏；再按一次 {grenade} 才会重新显示，它不会自己回来。\n忘了按回来时，快速按两下 {toggle} 也会显示。\n不需要可以在设置里关掉这个功能。"),
            new Entry("hand", "换手（黄色准星）",
                "准星变成换手颜色（默认橙黄色）= 程序认为你现在是换手状态。\n程序只能靠按键判断你用哪只手；对不上时快速按两下 {toggle} 回到正常手。"),
            new Entry("ammo", "弹药提示",
                "填好主武器的弹匣容量和射速，按住左键的时间 × 射速 = 打出去的子弹数（半自动：点一下算一发）。\n只显示大概范围：满 / 大半 / 一半 / 少 / 快空，不显示具体数字。\n按 {reload} = 普通换弹，快速按两下 {reload} = 快速换弹；换弹时间到了就换上一个新的满弹匣。默认关闭。\n对不上时按一次 {reload}，换完就是满的。"),
            new Entry("stand", "站立静止偏移",
                "站着不动时准星的位置，默认 0（屏幕中心）。\n设置后站立点就是新的零点，其他方向会自动换算，屏幕上的位置保持不变。\n改之前会弹窗确认。改回 0 = 站立点回到屏幕中心。\n按两下 {toggle} 复位时回到站立点。"),
            new Entry("taps", "短按和反向",
                "轻点一下移动键，准星只动一点点然后回来。\n从 A 换到 D 时，准星从当前位置先减速再加速，不会一下子跳到另一边。\n「移动」页的高级设置里可以改「短按阈值」（默认 120 毫秒）和「换向最短时间」（默认 150 毫秒），改完可以马上试。"),
            new Entry("anim", "准星动态",
                "在「外观」→「准星动态」里设置。\n静态：准星只跟着移动偏移。跟随动作：移动时张开，开火时每一发张开一下。开火跳动：只在开火时张开。开火特效：开火时张开，加波纹和渐变色。竞速：干脆利落的张开和快速的红色波纹。\n每个效果都能单独开关和调数值，下面有实时预览。\n这些效果不增加延迟，中心始终空着，开镜、丢雷时完全看不到。"),
            new Entry("resolution", "分辨率",
                "填你在游戏里设置的分辨率。点「自动检测」会读取当前屏幕的分辨率。"),
            new Entry("aspect", "画面比例和拉伸",
                "填游戏设置里的画面比例。列表里没有就选「自定义」填 宽:高（例如 123:111）。\n和屏幕比例不一样时，游戏会把画面拉宽铺满屏幕，左右偏移自动按倍数换算（例如 ×1.20）。\n有黑边的话，在「常规」页打开高级设置，选「没拉宽，上下有黑边」或「没拉宽，左右有黑边」。"),
            new Entry("fov", "FOV（视野）",
                "填游戏设置里的 FOV，范围 50–75。"),
            new Entry("focus", "只在塔科夫前台时显示",
                "切到桌面或别的窗口时准星自动隐藏，回到游戏再显示。"),
            new Entry("autostart", "开机自动启动",
                "打开后，开机登录 Windows 时程序会自动在后台运行（右下角托盘图标）。"),
            new Entry("perf", "性能模式",
                "关掉设置窗口里的动画和发光效果，界面最多 30 帧，教程不提前准备。\n游戏里的准星不受影响。电脑比较吃力时打开。"),
            new Entry("filter", "滤镜",
                "调整整个屏幕的明暗，让暗处看得更清楚。可以选预设，也可以自己调曲线。"),
            new Entry("status", "状态栏",
                "首页顶部这一行用文字告诉你准星现在是什么状态，例如「已隐藏：开镜」「强化：开」「黄色 = 换手中」。"),
            new Entry("stretch", "画面有没有被拉宽",
                "默认是「拉宽了」，一般不用改；只有画面比例和屏幕比例不一样时才有这一项（高级设置里）。\n拉宽了：画面铺满屏幕，圆的东西变胖，左右偏移按倍数换算（例如 ×1.20）。\n没拉宽，上下有黑边 / 左右有黑边：画面没铺满，按黑边在哪一边选；左右偏移不乘倍数。"),
            new Entry("ammostyle", "弹药提示样式",
                "在「外观」→「弹药提示」里设置：一根小条，默认在准星下方。\n预设：简洁、CF 风格、科幻、点阵、粗条、胶囊、竞速；选完还能改长度、粗细、距离、分段、圆角、发光、背景槽、颜色。\n可以只在剩余少于某个比例时显示；低弹警告和紧急警告、普通换弹和快速换弹的动画都能单独设置，有实时预览。\n只改提示自己的颜色，不改准星颜色。"),
            new Entry("reload", "换弹键",
                "按一下 {reload} = 普通换弹，在双击时间内按两下 = 快速换弹。\n两种都是换上一个新的满弹匣：旧弹匣退出，新的满弹匣装进来；快速换弹更快，旧弹匣直接丢掉。\n换弹时间（默认普通 2400 毫秒、快速 900 毫秒）可以在设置里改，也可以在首页的「常调」里改。"),
            new Entry("tap", "短按阈值",
                "按 A / D 短于这个时间（默认 120 毫秒）算轻点：准星只动一点点然后回来。"),
            new Entry("mustread", "必读",
                "战术装置用红外（IR）模式（推荐 WMX200）；不要同时开准星和激光；强化键 = 切换战术装置模式的键（{boost}）；用管理员权限运行；开镜方式要和游戏一样。"),
            new Entry("reversems", "换向最短时间",
                "从 A 换到 D（或反过来）时，准星回到中间再走到另一边至少用这么久（默认 150 毫秒），不会一下子跳过去。"),
            new Entry("wizard", "首次设置向导",
                "一页一个问题，照游戏里的设置填。以后都可以在设置页改，也可以在首页重新运行向导。"),
            new Entry("update", "自动检查更新",
                "启动时检查有没有新版本。"),
            new Entry("stats", "匿名统计",
                "匿名统计有多少人在用、用了多久。默认不发，你同意了才发。"),
            new Entry("crash", "发送错误记录",
                "程序出错时问你要不要把错误记录发给开发者。发之前会把用户名和文件夹路径隐藏。不同意就不发。"),
            new Entry("feedback", "反馈",
                "写好点「发送」，会发到开发者的邮箱。想要回复就留个联系方式。"),
            new Entry("filtertier", "滤镜预设",
                "两个预设：「通用」和「黑夜模式」（夜图用）。选好再用「调试滤镜」调亮或调暗；自己做的预设也会列在这里。"),
            new Entry("filterstep", "调试滤镜",
                "11 格，-5 到 +5。往右更亮，往左更暗，0 = 当前预设。"),
            new Entry("filtercurve", "曲线",
                "横着是原来的亮度，竖着是滤镜后的亮度。拖 5 个点改形状。\n拖不动了 = 到了 Windows 允许的最大值。"),
        };

        public static int Count { get { return all.Length; } }

        public static IEnumerable<Entry> All { get { return all; } }

        public static Entry Find(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < all.Length; i++) if (all[i].Id == id) return all[i];
            return null;
        }

        public static string Title(string id) { Entry e = Find(id); return e == null ? "" : e.Title; }

        public static string Body(string id) { return Body(id, null); }

        /// <summary>The help text with the user's current keys filled in (g may be null: generic words are used).</summary>
        public static string Body(string id, GlobalSettings g)
        {
            Entry e = Find(id);
            return e == null ? "" : Fill(e.Body, g);
        }

        /// <summary>Replaces {toggle} {ads} {boost} {grenade} {reload} with the key names from g ("开关准星键" etc. when g is null).</summary>
        public static string Fill(string text, GlobalSettings g)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            KeyBindings k = g != null ? g.Keys : null;
            text = text.Replace("{toggle}", k != null ? Combo(k, KeyBindings.ActionToggle) : "开关准星键");
            text = text.Replace("{ads}", k != null ? Combo(k, KeyBindings.ActionAds) : "开镜键");
            text = text.Replace("{boost}", k != null ? Combo(k, KeyBindings.ActionBoost) : "强化键");
            text = text.Replace("{grenade}", g != null && g.GrenadeKey > 0 ? KeyBindings.KeyName(g.GrenadeKey) : "丢雷键");
            text = text.Replace("{reload}", g != null && g.AmmoReloadKey > 0 ? KeyBindings.KeyName(g.AmmoReloadKey) : "换弹键");
            return text;
        }

        private static string Combo(KeyBindings k, int action)
        {
            try { string s = k.ComboOf(action); return string.IsNullOrEmpty(s) ? "（未设置）" : s; }
            catch (Exception) { return "（未设置）"; }
        }
    }

#if !UI_PURE_CHECK
    /// <summary>A small round 「?」 beside a feature: hover shows the title, click opens the full help text (ThemedDialog).
    /// Sized from the font's real metrics, so it never clips at 150 / 200 %.</summary>
    internal sealed class HelpMark : Control
    {
        private readonly string id;
        private readonly Func<GlobalSettings> settings;
        private bool hover;

        public static HelpMark For(string id, Font font) { return new HelpMark(id, font, null); }

        public static HelpMark For(string id, Font font, Func<GlobalSettings> settings) { return new HelpMark(id, font, settings); }

        private HelpMark(string id, Font font, Func<GlobalSettings> settings)
        {
            this.id = id;
            this.settings = settings;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.SupportsTransparentBackColor, true);
            if (font != null) Font = font;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = "帮助：" + HelpText.Title(id);
            int d = Math.Max(14, TextRenderer.MeasureText("?", Font).Height);
            Size = new Size(d, d);
            Margin = new Padding(Math.Max(2, d / 4), 0, 0, 0);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            GlobalSettings g = null;
            try { if (settings != null) g = settings(); } catch (Exception) { g = null; }
            string body = HelpText.Title(id) + "\n\n" + HelpText.Body(id, g);
            ThemedDialog.Show(FindForm(), body, false);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color edge = hover || Focused ? Theme.ColAccent : Theme.ColMuted;
            using (SolidBrush b = new SolidBrush(hover ? Theme.ColHover : Theme.ColInput)) g.FillEllipse(b, r);
            using (Pen p = new Pen(edge)) g.DrawEllipse(p, r);
            TextRenderer.DrawText(g, "?", Font, new Rectangle(0, 0, Width, Height), edge,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
#endif
}
