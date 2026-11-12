// 用途：维护开镜记录，处理奔跑、Tab/Esc、准星重启和切瞄具组合键。
namespace TarkovCrosshair
{
    public sealed partial class MotionEngine
    {
        /// <summary>沿用 v3.2 的开镜规则，仅在前进加 Shift 刚成立时补充强制关镜。</summary>
        private bool UpdateAds(InputState input, GlobalSettings g, bool preview, bool ringOnEdge)
        {
            // 准星关闭后重新开启，继续按 v3.2 规则清掉开镜记录。
            if (ringOnEdge) adsToggled = false;
            bool adsEdge = input.Ads && !prev.Ads;
            if (adsEdge) adsPressBlocked = g.AdsIgnoreAltCtrl && g.Keys != null && g.Keys.Ads == VkRButton && input.Mod;
            if (!input.Ads) adsPressBlocked = false;
            bool adsActive;
            if (g.AdsMode == "toggle")
            {
                if (adsEdge && !adsPressBlocked) adsToggled = !adsToggled;
                // 前进已经过方向过滤；持续按住组合不会反复取消后续开镜。
                bool sprintStarted = input.Fwd && input.ShiftHeld && !(prev.Fwd && prev.ShiftHeld);
                // 取消优先于同帧开镜，且奔跑不受 Tab/Esc 开关影响。
                if (sprintStarted || (g.AdsCancelTabEsc && input.Cancel && !prev.Cancel)) adsToggled = false;
                adsActive = adsToggled;
            }
            else
            {
                // 按住模式继续由开镜键和切瞄具过滤决定显隐。
                adsActive = input.Ads && !adsPressBlocked;
            }
            return !preview && adsActive;
        }
    }
}
