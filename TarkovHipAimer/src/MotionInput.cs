// 用途：记录取消开镜按键，处理取消优先和点一下切换开镜的状态。
namespace TarkovCrosshair
{
    /// <summary>用四个整数保存按键状态，避免高帧率下每帧创建数组或集合。</summary>
    public struct CancelAdsInput
    {
        // 每个整数存放 64 个虚拟键码，覆盖 Windows 的全部有效按键。
        private ulong keys0, keys1, keys2, keys3;

        /// <summary>记录一个当前按住的取消键；无效键码不参与计算。</summary>
        public void SetDown(int vk)
        {
            if (vk < 1 || vk > 254) return;
            ulong bit = 1UL << (vk & 63);
            switch (vk >> 6)
            {
                case 0: keys0 |= bit; break;
                case 1: keys1 |= bit; break;
                case 2: keys2 |= bit; break;
                case 3: keys3 |= bit; break;
            }
        }

        /// <summary>任一取消键新按下时返回 true，其他取消键仍按住也不影响。</summary>
        public bool HasNewPress(CancelAdsInput previous)
        {
            return ((keys0 & ~previous.keys0) | (keys1 & ~previous.keys1) |
                (keys2 & ~previous.keys2) | (keys3 & ~previous.keys3)) != 0;
        }
    }

    public sealed partial class MotionEngine
    {
        /// <summary>点一下切换时取消优先；按住开镜及预览的显隐规则保持不变。</summary>
        private bool UpdateAds(InputState input, GlobalSettings g, bool preview)
        {
            if (g.AdsMode == "toggle")
            {
                // 只清零开镜记录，不重置换手、强化准星或运动状态。
                if (input.CancelAds.HasNewPress(prev.CancelAds)) adsToggled = false;
                else if (input.Ads && !prev.Ads) adsToggled = !adsToggled;
                return !preview && adsToggled;
            }
            return !preview && input.Ads;
        }
    }
}
