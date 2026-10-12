using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ComprehensiveStatistics.Common.Configs
{
    /// <summary>
    /// 保存只影响本机显示效果的配置，不参与角色或世界数据同步。
    /// </summary>
    internal sealed class ClientConfig : ModConfig
    {
        public static ClientConfig Instance;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        public override void OnLoaded()
        {
            Instance = this;
        }

        [DefaultValue(0.95f)]
        [Range(0f, 1f)]
        [Slider]
        // 使用可移动区域的归一化坐标，分辨率变化后仍能保持相对位置。
        public float InventoryButtonHorizontal = 0.95f;

        [DefaultValue(0.09f)]
        [Range(0f, 1f)]
        [Slider]
        public float InventoryButtonVertical = 0.09f;
    }
}
