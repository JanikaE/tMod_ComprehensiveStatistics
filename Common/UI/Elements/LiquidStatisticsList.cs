using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>将液体数据转换为虚拟列表模型；图标加载由运行中的窗口提供。</summary>
    internal static class LiquidStatisticsList
    {
        /// <summary>保持标准液体顺序和原始长整数，只在有非零数据时创建行。</summary>
        internal static IEnumerable<StatisticsListGroup> CreateGroups(StatisticsData data, Func<int, Asset<Texture2D>> getIcon)
        {
            var items = new List<StatisticsListItem>();
            foreach (LiquidKind liquid in Enum.GetValues<LiquidKind>())
            {
                long placed = data?.GetValue(StatisticKeys.GetPlacedLiquidKey(liquid)).Units ?? 0L;
                long removed = data?.GetValue(StatisticKeys.GetRemovedLiquidKey(liquid)).Units ?? 0L;
                if (placed == 0L && removed == 0L) continue;
                int iconType = liquid switch { LiquidKind.Water => ItemID.BottomlessBucket,
                    LiquidKind.Lava => ItemID.BottomlessLavaBucket, LiquidKind.Honey => ItemID.BottomlessHoneyBucket,
                    _ => ItemID.BottomlessShimmerBucket };
                items.Add(new StatisticsListItem(
                    "Mods.ComprehensiveStatistics.UI.Blocks.Liquids." + liquid,
                    iconType, getIcon?.Invoke(iconType), placed, removed,
                    nameIsLocalizationKey: true, content: StatisticKeys.GetPlacedLiquidKey(liquid).Content,
                    vanillaContentId: (int)liquid));
            }
            return new[] { new StatisticsListGroup("Mods.ComprehensiveStatistics.UI.Blocks.LiquidsGroup", items,
                nameIsLocalizationKey: true, metricDisplay: StatisticsMetricDisplay.LiquidVolume) };
        }
    }
}
