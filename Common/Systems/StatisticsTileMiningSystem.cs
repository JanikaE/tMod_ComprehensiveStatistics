using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 仅为有效本地采掘建立目标观察，复用放置和兼容层的值快照、对象归一化及去重。
    /// </summary>
    internal sealed class StatisticsTileMiningSystem : ModSystem
    {
        public override void Load()
        {
            On_Player.PickTile += HandlePickTile;
            On_Player.PickWall += HandlePickWall;
        }

        public override void Unload()
        {
            On_Player.PickTile -= HandlePickTile;
            On_Player.PickWall -= HandlePickWall;
        }

        /// <summary>
        /// 观察一次直接采掘；失败、尚未达到破坏阈值及残留多格对象均不入账。
        /// </summary>
        private static void HandlePickTile(On_Player.orig_PickTile orig, Player player,
            int x, int y, int pickPower)
        {
            if (!IsOwnedLocalMiningAttempt(player, x, y, pickPower, false))
            {
                orig(player, x, y, pickPower);
                return;
            }
            using (TileChangeScope.Begin(player))
            {
                TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Remove);
                orig(player, x, y, pickPower);
            }
        }

        /// <summary>
        /// 背景墙采掘与物块使用不同内容层，避免同坐标的两种变化互相覆盖。
        /// </summary>
        private static void HandlePickWall(On_Player.orig_PickWall orig, Player player,
            int x, int y, int hammerPower)
        {
            if (!IsOwnedLocalMiningAttempt(player, x, y, hammerPower, true))
            {
                orig(player, x, y, hammerPower);
                return;
            }
            using (TileChangeScope.Begin(player))
            {
                TileChangeScope.Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Remove);
                orig(player, x, y, hammerPower);
            }
        }

        /// <summary>
        /// 保留原有工具活动检查；M1 不因重构而给未适配协程、爆炸或普通世界逻辑开放归属。
        /// </summary>
        private static bool IsOwnedLocalMiningAttempt(Player player, int x, int y,
            int toolPower, bool wallMining)
        {
            if (Main.dedServ || Main.gameMenu || player?.active != true
                || player.whoAmI != Main.myPlayer || toolPower <= 0 || !WorldGen.InWorld(x, y)
                || (player.itemAnimation <= 0 && player.itemTime <= 0 && !player.channel))
                return false;

            Item heldItem = player.HeldItem;
            if (heldItem == null || heldItem.IsAir)
                return false;
            // 模组物品主动调用 Pick 仍可归属；直接修改世界的工具须由之后的适配器显式绑定。
            return heldItem.ModItem != null
                || (wallMining ? heldItem.hammer > 0 : heldItem.pick > 0 || heldItem.axe > 0);
        }
    }
}
