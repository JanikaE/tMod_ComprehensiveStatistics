using ComprehensiveStatistics.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Players
{
    [Autoload(Side = ModSide.Client)]
    internal sealed class StatisticsUIPlayer : ModPlayer
    {
        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            if (!Main.gameMenu && ComprehensiveStatistics.ToggleStatisticsKeybind?.JustPressed == true)
            {
                ModContent.GetInstance<StatisticsUISystem>().Toggle();
            }
        }
    }
}
