using ComprehensiveStatistics.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>
    /// 将玩家快捷键输入转交给统一的 UI 系统处理。
    /// </summary>
    [Autoload(Side = ModSide.Client)]
    internal sealed class StatisticsUIPlayer : ModPlayer
    {
        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            StatisticsUISystem uiSystem = ModContent.GetInstance<StatisticsUISystem>();
            if (!Main.gameMenu
                && !uiSystem.IsTakingTextInput
                && ComprehensiveStatistics.ToggleStatisticsKeybind?.JustPressed == true)
            {
                uiSystem.Toggle();
            }
        }
    }
}
