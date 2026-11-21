// FaqText.cs -- v5.1 (W26, decision 13): the 常见问题 page's questions and answers (pure; FaqPage.cs draws them).
// Wording follows W31's README draft (V51-TEXT-DRAFT.md section E) so the page and the README say the same thing.
// Each answer can point to a page ("Jump" = a UiPages id / title the host's JumpToPage understands).
// Append new entries at the end. Keys in the text are filled in from the user's settings (HelpText.Fill).
using System;

namespace TarkovCrosshair
{
    internal static class FaqText
    {
        public const string PageTitle = "常见问题";

        internal sealed class Entry
        {
            public readonly string Q, A, Jump, JumpText;
            public Entry(string q, string a, string jump, string jumpText) { Q = q; A = a; Jump = jump; JumpText = jumpText; }
        }

        public static readonly Entry[] Items = new Entry[]
        {
            new Entry("为什么开镜还有准星？",
                "「开镜方式」和游戏里不一样。游戏里是按住开镜，这里也要选「按住」；游戏里是点一下开镜，这里选「切换」。\n改对以后，开镜时准星会完全消失。",
                "keys", "去设置开镜方式 →"),
            new Entry("为什么准星变黄了？",
                "准星变成换手颜色（默认橙黄色）= 程序认为你换手了。首页状态栏会显示「黄色 = 换手中」。\n程序只能靠按键判断你用哪只手，对不上时快速按两下 {toggle}（关再开），回到正常手。",
                "hand", "去换手设置 →"),
            new Entry("为什么准星不动？",
                "1. 程序要用管理员权限运行：退出程序，重新双击，Windows 问「是否允许」点「是」。\n2. 「只在塔科夫前台时显示」开着时，要先点进游戏窗口。\n3. 按了 {grenade}（丢雷）准星会隐藏，再按一次 {grenade} 显示。",
                null, null),
            new Entry("准星不显示？",
                "先看首页顶部的状态栏，它会写原因。\n1. 「准星：已关闭」：按一下 {toggle}。\n2. 开镜、自由视角、丢雷（{grenade}）时准星本来就会隐藏。\n3. 丢雷后没按回来：再按一次 {grenade}，或快速按两下 {toggle}。\n4. 游戏显示模式请用「无边框窗口」。",
                null, null),
            new Entry("丢完雷准星没回来？",
                "丢雷隐藏是开关：按 {grenade} 隐藏，再按 {grenade} 才显示，它不会自己回来。忘了的话快速按两下 {toggle}。不想要这个功能可以在设置里关掉。",
                null, null),
            new Entry("按 T 强化没反应？",
                "强化键是「切换战术装置模式」的键（现在是 {boost}），不是 T。T 只是开关战术装置，和强化无关。",
                "keys", "去设置强化键 →"),
            new Entry("强化准星和手电对不上？",
                "强化键每按一次就开 / 关一次。对不上时先把装置切回红外，再快速按两下 {toggle}，强化回到关闭。",
                null, null),
            new Entry("准星一边准、另一边偏很多？",
                "只有游戏里的「画面比例」和屏幕比例不一样时才会这样。看游戏画面：\n圆的东西变胖、铺满整个屏幕 → 选「拉宽了」；两边有黑边 → 选「有黑边」。",
                "general", "去分辨率设置 →"),
            new Entry("画面比例列表里没有我的比例？",
                "选「自定义」，直接填 宽:高，例如 123:111。1:1 也可以。",
                "general", "去分辨率设置 →"),
            new Entry("站着不动开枪弹孔也偏？",
                "在「移动」页设「站立静止偏移」。站立点会变成新的零点，其他方向自动换算，不用重调。",
                "movement", "去移动设置 →"),
            new Entry("弹药提示不准？",
                "1. 弹匣容量和射速要填主武器的。\n2. 全自动 / 半自动要选对。\n3. 换弹键要和游戏里一样。\n4. 提示换弹比游戏快或慢：改换弹时间（普通默认 3000 毫秒、快速默认 2200 毫秒），在「外观」→「弹药提示」的「先设置」里或首页「常调」里都能改。\n对不上时按一次 {reload}，换完就是满的。",
                null, null),
            new Entry("换弹后为什么是满的？",
                "按 {reload}（普通）或快速按两下 {reload}（快速）都是换上一个新的满弹匣，不是往旧弹匣里补子弹。快速换弹更快，旧弹匣直接丢掉。",
                null, null),
            new Entry("轻点 A / D 准星动得太多或太少？",
                "在「移动」页的高级设置里改「短按阈值」（默认 A/D 200 毫秒、W/S 160 毫秒）。A、D（或 W、S）来回换时准星太快或太慢，改「换向最短时间」（默认 A/D 250 毫秒、W/S 180 毫秒）。左右和前后分开设。改完马上就能试。",
                "movement", "去移动设置 →"),
            new Entry("滤镜只有两个预设？",
                "对，只留「通用」和「黑夜模式」。再用「调试滤镜」或曲线调亮调暗；你自己做的预设不会丢。",
                "filter", "去滤镜设置 →"),
            new Entry("打开设置窗口很卡？",
                "打开「性能模式」：设置窗口的动画和发光关掉，游戏里的准星不受影响。",
                "general", "去常规设置 →"),
            new Entry("游戏按键和默认的不一样怎么办？",
                "在「按键」页把每个键改成你游戏里用的键。程序不会读取游戏文件，按键需要你自己填。",
                "keys", "去按键设置 →"),
            new Entry("换手后准星位置不对？",
                "快速按两下 {toggle}（关再开）回到正常手，再按换手键。",
                null, null),
            new Entry("想重新看一遍必读或设置向导？",
                "首页顶部有「必读」和「设置向导」按钮，本页最上面也有「查看必读」。",
                null, null),
            new Entry("程序会上传什么？", // v5.1 (Leo 21 Nov 2026): anonymous stats on by default
                "默认发送匿名统计（随机编号、版本号、使用时长），不含个人信息。不想发就在「常规」→「联网」取消勾选，马上停止。错误记录和反馈只由你自己用邮件发。",
                "general", "去常规设置 →"),
        };

        public static string Question(int i, GlobalSettings g) { return HelpText.Fill(Items[i].Q, g); }
        public static string Answer(int i, GlobalSettings g) { return HelpText.Fill(Items[i].A, g); }
    }
}
