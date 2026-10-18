using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Projectiles
{
    /// <summary>
    /// 观察浮标的生成与销毁，将真实抛竿及其最终结果交给玩家统计会话结算。
    /// </summary>
    internal sealed class StatisticsGlobalProjectile : GlobalProjectile
    {
        public override void OnSpawn(Projectile projectile, IEntitySource source)
        {
            if (projectile == null
                || !projectile.bobber
                || source is not IEntitySource_WithStatsFromItem itemSource)
            {
                return;
            }

            Player player = itemSource.Player;
            Item fishingRod = itemSource.Item;
            player?.GetModPlayer<StatisticsPlayer>()
                .ObserveFishingBobberSpawn(projectile, fishingRod);
        }

        public override bool PreKill(Projectile projectile, int timeLeft)
        {
            if (projectile?.bobber == true
                && projectile.owner >= 0
                && projectile.owner < Main.player.Length)
            {
                Main.player[projectile.owner]?
                    .GetModPlayer<StatisticsPlayer>()
                    .ObserveFishingBobberEnding(projectile);
            }

            // 该观察器只读取销毁前状态，不改变原版或其他模组的销毁决定。
            return true;
        }
    }
}
