// StatusLine.cs -- v5.1 (W26, decision 13): the always-visible status line on the 首页, in plain words for a beginner:
// 「准星：显示中」「已隐藏：开镜」「已隐藏：丢雷（再按 G 显示）」「黄色 = 换手中」「强化：开」 ... Pure (no WinForms): the
// home page (DashboardHome) polls a StatusSnapshot twice a second and repaints only when Text changed.
//
// Snapshot source: StatusLine.LiveSource, when an owner of the live crosshair state sets it (the overlay engine runs on the
// render thread); otherwise DashboardHome builds the snapshot from the home page's own engine + the settings.
using System;
using System.Text;

namespace TarkovCrosshair
{
    internal struct StatusSnapshot
    {
        public bool Enabled;          // 开关准星 (G.Enabled)
        public bool AdsHidden;        // 开镜中 (hidden)
        public bool GrenadeHidden;    // 丢雷隐藏 latched
        public bool FreeLookHidden;   // 自由视角 held
        public bool WaitingForGame;   // 只在塔科夫前台时显示 and the game is not in front
        public bool HandSwitched;     // 换手中 (yellow / orange crosshair)
        public bool BoostOn;          // 强化准星 shown
        public bool BoostEnabled;     // 强化 feature on (G.BoostEnabled): show 「强化：关」 too
        public bool AmmoOn;           // 弹药提示 on
    }

    internal enum StatusLevel { Ok = 0, Info = 1, Hidden = 2, Off = 3 }

    internal sealed class StatusLine
    {
        /// <summary>Optional provider of the LIVE crosshair state (set by the overlay owner). Null = home page's own engine.</summary>
        public static Func<StatusSnapshot> LiveSource { get; set; }

        public const string Sep = " · ";

        public string Text { get; private set; }
        public StatusLevel Level { get; private set; }

        public StatusLine() { Text = ""; Level = StatusLevel.Ok; }

        /// <summary>Recomputes; true when Text or Level changed (repaint only then).</summary>
        public bool Update(StatusSnapshot s, GlobalSettings g)
        {
            StatusLevel lv;
            string t = Compose(s, g, out lv);
            if (t == Text && lv == Level) return false;
            Text = t; Level = lv;
            return true;
        }

        /// <summary>The words, most important first: why the crosshair is not visible (one reason), then 换手 / 强化 / 弹药.</summary>
        public static string Compose(StatusSnapshot s, GlobalSettings g, out StatusLevel level)
        {
            StringBuilder sb = new StringBuilder();
            level = StatusLevel.Ok;
            if (!s.Enabled)
            {
                sb.Append("准星：已关闭（按 ").Append(ToggleName(g)).Append(" 打开）");
                level = StatusLevel.Off;
            }
            else if (s.GrenadeHidden)
            {
                sb.Append("已隐藏：丢雷（再按 ").Append(GrenadeName(g)).Append(" 显示）");
                level = StatusLevel.Hidden;
            }
            else if (s.AdsHidden)
            {
                sb.Append("已隐藏：开镜");
                level = StatusLevel.Hidden;
            }
            else if (s.FreeLookHidden)
            {
                sb.Append("已隐藏：自由视角");
                level = StatusLevel.Hidden;
            }
            else if (s.WaitingForGame)
            {
                sb.Append("已隐藏：游戏不在前台");
                level = StatusLevel.Hidden;
            }
            else sb.Append("准星：显示中");

            if (s.Enabled)
            {
                if (s.HandSwitched) { sb.Append(Sep).Append("黄色 = 换手中"); if (level == StatusLevel.Ok) level = StatusLevel.Info; }
                if (s.BoostOn) sb.Append(Sep).Append("强化：开");
                else if (s.BoostEnabled) sb.Append(Sep).Append("强化：关");
                if (s.AmmoOn) sb.Append(Sep).Append("弹药提示：开");
            }
            if (g != null && g.PerfMode) sb.Append(Sep).Append("性能模式：开");
            return sb.ToString();
        }

        private static string ToggleName(GlobalSettings g)
        {
            try { if (g != null && g.Keys != null) { string c = g.Keys.ComboOf(KeyBindings.ActionToggle); if (!string.IsNullOrEmpty(c)) return c; } }
            catch (Exception) { }
            return "开关准星键";
        }

        private static string GrenadeName(GlobalSettings g)
        {
            return g != null && g.GrenadeKey > 0 ? KeyBindings.KeyName(g.GrenadeKey) : "丢雷键";
        }
    }
}
