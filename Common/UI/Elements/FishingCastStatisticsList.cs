using System;
using System.Collections.Generic;
using System.Linq;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>按抛竿时的鱼竿投影互斥结果，旧合计扣除已知明细后作为未分类历史保留。</summary>
    internal static class FishingCastStatisticsList
    {
        private static readonly StatisticKey[] outcomes =
        {
            StatisticKeys.FishingSuccessfulCasts, StatisticKeys.FishingEmptyReels, StatisticKeys.FishingInterruptedCasts
        };
        private static readonly string[] labels = { "SuccessfulCatches", "EmptyReels", "InterruptedCasts" };

        /// <summary>生成鱼竿一级分组和三种结果叶子；图标解析可注入以独立验证生产投影。</summary>
        internal static IEnumerable<StatisticsListGroup> CreateGroups(StatisticsData data,
            Func<StableContentKey, (string Name, int Type, Asset<Texture2D> Icon)> resolve = null)
        {
            var groups = new List<StatisticsListGroup>();
            if (data == null) return groups;
            resolve ??= ResolveRod;
            var rods = new Dictionary<StableContentKey, long[]>();
            var remaining = outcomes.Select(k => data.GetValue(k).Events).ToArray();
            foreach (var pair in data.Entries)
            {
                int index = Array.FindIndex(outcomes, k => k.StatisticId == pair.Key.StatisticId);
                if (index < 0 || !pair.Key.Content.HasValue || pair.Value.Events == 0) continue;
                var content = pair.Key.Content.Value;
                if (!rods.TryGetValue(content, out var values)) rods[content] = values = new long[3];
                values[index] = new StatisticMetrics(events: values[index]).Add(pair.Value).Events;
                remaining[index] = Math.Max(0L, remaining[index] - pair.Value.Events);
            }
            foreach (var pair in rods.OrderBy(p => p.Key.ModName, StringComparer.Ordinal).ThenBy(p => p.Key.ContentName, StringComparer.Ordinal))
            {
                var display = resolve(pair.Key);
                var source = pair.Key.ModName == "Terraria" ? StatisticsContentSource.Vanilla : StatisticsContentSource.Modded;
                groups.Add(new StatisticsListGroup(display.Name, CreateItems(pair.Value, source, pair.Key),
                    metricDisplay: StatisticsMetricDisplay.Events, iconType: display.Type, icon: display.Icon,
                    iconKind: StatisticsIconKind.Item, stateKey: "fishing/rod/" + pair.Key));
            }
            if (remaining.Any(v => v > 0))
                groups.Add(new StatisticsListGroup("Mods.ComprehensiveStatistics.UI.Fishing.UnknownRod",
                    CreateItems(remaining, StatisticsContentSource.Neutral, null), nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.Events, stateKey: "fishing/rod/unknown"));
            return groups;
        }

        /// <summary>结果子项继承鱼竿来源，使搜索和原版/模组筛选后的合计一致。</summary>
        private static IEnumerable<StatisticsListItem> CreateItems(long[] values, StatisticsContentSource source, StableContentKey? content)
        {
            for (int i = 0; i < labels.Length; i++)
                yield return new StatisticsListItem("Mods.ComprehensiveStatistics.UI.Fishing." + labels[i], 0, null,
                    values[i], 0L, source: source, nameIsLocalizationKey: true,
                    iconKind: StatisticsIconKind.None, content: content, vanillaContentId: i);
        }

        /// <summary>解析具体鱼竿名称和图标，缺失模组内容仍保留稳定键。</summary>
        private static (string Name, int Type, Asset<Texture2D> Icon) ResolveRod(StableContentKey content)
        {
            if (!content.TryResolveItemType(out int type))
                return (Language.GetTextValue("Mods.ComprehensiveStatistics.UI.Fishing.MissingItemContent", content.ToString()), 0, null);
            Main.instance.LoadItem(type);
            return (Lang.GetItemNameValue(type), type, TextureAssets.Item[type]);
        }
    }
}
