using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ComprehensiveStatistics.Common.Configs
{
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
        public float InventoryButtonHorizontal = 0.95f;

        [DefaultValue(0.09f)]
        [Range(0f, 1f)]
        [Slider]
        public float InventoryButtonVertical = 0.09f;
    }
}
