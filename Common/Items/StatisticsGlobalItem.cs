using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Systems;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Items
{
    /// <summary>
    /// 从所有物品统一转发拾取与使用事件，使普通与模组物品共享统计入口。
    /// </summary>
    internal sealed class StatisticsGlobalItem : GlobalItem
    {
        public override bool OnPickup(Item item, Player player)
        {
            // GlobalItem 在 ModItem.OnPickup 前执行，可观察到“拾取即消耗”且不会进入
            // 普通背包的特殊资源；实际是否成功及转移数量仍由 StatisticsPlayer 帧末确认。
            player?.GetModPlayer<StatisticsPlayer>().ObserveItemPickup(item);
            return true;
        }

        public override void UseAnimation(Item item, Player player)
        {
            // 普通、自动使用、多段攻击与特殊右键都会从实际动画入口经过这里；
            // StatisticsPlayer 仅对持续引导会话增加额外去重，避免模组手动重启动画时重复累计。
            player?.GetModPlayer<StatisticsPlayer>().RecordItemUseAnimation(item);
        }

        public override bool? UseItem(Item item, Player player)
        {
            // 快速治疗、快速魔力和快速增益直接调用 ItemLoader.UseItem，不会开始普通
            // 使用动画。只在对应快捷操作的作用域内补记，避免与 UseAnimation 重复。
            if (StatisticsQuickUseSystem.IsQuickUseActive(player))
            {
                player?.GetModPlayer<StatisticsPlayer>().RecordItemUse(item);
            }

            return null;
        }
    }
}
