using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Tiles
{
    /// <summary>
    /// 钟没有可持久比较的状态；原版成功执行钟逻辑后到达此回调即视为一次完成交互。
    /// </summary>
    internal sealed class StatisticsGlobalTile : GlobalTile
    {
        public override void RightClick(int i, int j, int type)
        {
            Player player = Main.LocalPlayer;
            if (Main.dedServ
                || Main.gameMenu
                || player?.active != true
                || !FacilityRegistry.TryResolve(i, j, out ResolvedFacility facility)
                || facility.Definition.InteractionKind != FacilityInteractionKind.Clock)
            {
                return;
            }

            player.GetModPlayer<StatisticsPlayer>().RecordFacilityInteraction(facility.Definition);
        }
    }
}
