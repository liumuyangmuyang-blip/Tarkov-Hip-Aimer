// 用途：保存游戏动作按键及取消开镜键，处理旧配置兼容和按键校验。
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TarkovCrosshair
{
    [DataContract]
    public sealed class KeyBindings
    {
        // 现有游戏动作仍各用一个按键，新增取消键另存为列表。
        [DataMember(Name = "Forward")] public int Forward;
        [DataMember(Name = "Back")] public int Back;
        [DataMember(Name = "Left")] public int Left;
        [DataMember(Name = "Right")] public int Right;
        [DataMember(Name = "LeanLeft")] public int LeanLeft;
        [DataMember(Name = "LeanRight")] public int LeanRight;
        [DataMember(Name = "Fire")] public int Fire;
        [DataMember(Name = "Ads")] public int Ads;
        [DataMember(Name = "Toggle")] public int Toggle;
        [DataMember(Name = "FreeLook")] public int FreeLook; // 自由视角，默认鼠标中键
        [DataMember(Name = "Jump")] public int Jump;         // 跳跃，默认空格

        // 换手默认鼠标侧键1；通过属性识别旧配置是否缺少这个按键。
        private int handSwitch;
        /// <summary>读取缺少 HandSwitch 的旧配置时为 false。</summary>
        public bool HandSwitchPresent;
        [DataMember(Name = "HandSwitch")]
        public int HandSwitch { get { return handSwitch; } set { handSwitch = value; HandSwitchPresent = true; } }

        // 强化准星默认鼠标侧键2；同样通过属性识别旧配置。
        private int boost;
        /// <summary>读取缺少 Boost 的旧配置时为 false。</summary>
        public bool BoostPresent;
        [DataMember(Name = "Boost")]
        public int Boost { get { return boost; } set { boost = value; BoostPresent = true; } }

        // 多个取消键用于对齐开镜记录；它们不会向游戏发送按键。
        [DataMember(Name = "CancelAdsKeys")] public int[] CancelAdsKeys;

        public const int VkXButton1 = 0x05;
        public const int VkXButton2 = 0x06;
        public const int VkSpace = 0x20;

        // 按键页的动作顺序，同时用于检测一个按键是否已用于其他动作。
        public const int ActionCount = 13;
        public const int ActionHandSwitch = 4, ActionBoost = 12;
        public static readonly string[] ActionNames = new string[]
        {
            "开关准星", "开火", "开镜", "自由视角（转头）", "换手", "左侧身", "右侧身", "前进", "后退", "左移", "右移", "跳跃", "强化准星"
        };

        // 新设置先使用完整默认按键，再由玩家调整。
        public KeyBindings() { SetDefaults(); }

        // 旧配置缺少的字段沿用默认值，保留迁移所需的缺失标记。
        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); HandSwitchPresent = false; BoostPresent = false; }

        // 恢复动作按键，以及 Tab、Esc 组成的取消开镜默认键。
        public void SetDefaults()
        {
            Forward = 0x57; Back = 0x53; Left = 0x41; Right = 0x44;
            LeanLeft = 0x51; LeanRight = 0x45;
            Fire = 0x01; Ads = 0x02; Toggle = 0x24; FreeLook = 0x04; Jump = VkSpace;
            handSwitch = VkXButton1; HandSwitchPresent = true;
            boost = VkXButton2; BoostPresent = true;
            CancelAdsKeys = new int[] { 9, 27 }; // Tab、Esc 默认用于取消开镜。
        }

        // 数组必须单独复制，避免 UI 改键影响渲染线程正在使用的配置。
        public KeyBindings Clone()
        {
            KeyBindings copy = (KeyBindings)MemberwiseClone();
            copy.CancelAdsKeys = CancelAdsKeys == null ? new int[] { 9, 27 } : (int[])CancelAdsKeys.Clone();
            return copy;
        }

        // 按键设置页按动作编号获取当前绑定。
        public int Get(int action)
        {
            switch (action)
            {
                case 0: return Toggle;
                case 1: return Fire;
                case 2: return Ads;
                case 3: return FreeLook;
                case 4: return HandSwitch;
                case 5: return LeanLeft;
                case 6: return LeanRight;
                case 7: return Forward;
                case 8: return Back;
                case 9: return Left;
                case 10: return Right;
                case 11: return Jump;
                case 12: return Boost;
                default: return 0;
            }
        }

        // 按键设置页按动作编号更新绑定。
        public void Set(int action, int vk)
        {
            switch (action)
            {
                case 0: Toggle = vk; break;
                case 1: Fire = vk; break;
                case 2: Ads = vk; break;
                case 3: FreeLook = vk; break;
                case 4: HandSwitch = vk; break;
                case 5: LeanLeft = vk; break;
                case 6: LeanRight = vk; break;
                case 7: Forward = vk; break;
                case 8: Back = vk; break;
                case 9: Left = vk; break;
                case 10: Right = vk; break;
                case 11: Jump = vk; break;
                case 12: Boost = vk; break;
            }
        }

        /// <summary>查找指定动作以外的重复绑定，没有重复时返回 -1。</summary>
        public int FindOther(int vk, int except)
        {
            for (int a = 0; a < ActionCount; a++) if (a != except && Get(a) == vk) return a;
            return -1;
        }

        // 清理配置中的无效按键，保留旧配置迁移标记。
        public void Validate()
        {
            KeyBindings d = new KeyBindings();
            Forward = Vk(Forward, d.Forward); Back = Vk(Back, d.Back);
            Left = Vk(Left, d.Left); Right = Vk(Right, d.Right);
            LeanLeft = Vk(LeanLeft, d.LeanLeft); LeanRight = Vk(LeanRight, d.LeanRight);
            Fire = Vk(Fire, d.Fire); Ads = Vk(Ads, d.Ads); Toggle = Vk(Toggle, d.Toggle);
            FreeLook = Vk(FreeLook, d.FreeLook); Jump = Vk(Jump, d.Jump);
            bool present = HandSwitchPresent;
            handSwitch = Vk(handSwitch, d.HandSwitch);
            HandSwitchPresent = present; // 是否补齐旧配置由全局设置决定，这里只保留原标记
            bool bp = BoostPresent;
            boost = Vk(boost, d.Boost);
            BoostPresent = bp;
            CancelAdsKeys = NormalizeCancelAdsKeys(CancelAdsKeys);
        }

        // 只在设置校验时分配数组；实时读取直接使用结果，不在每帧做去重。
        private static int[] NormalizeCancelAdsKeys(int[] keys)
        {
            if (keys == null) return new int[] { 9, 27 };
            List<int> valid = new List<int>();
            bool[] seen = new bool[255];
            foreach (int key in keys)
            {
                if (key < 1 || key > 254 || seen[key]) continue;
                seen[key] = true;
                valid.Add(key);
            }
            return valid.ToArray();
        }

        // 游戏动作键只接受有效的 Windows 虚拟键码，否则使用原有默认值。
        private static int Vk(int v, int def) { return (v >= 1 && v <= 254) ? v : def; }
    }
}
