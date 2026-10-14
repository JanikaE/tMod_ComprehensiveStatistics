using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Localization;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>
    /// 定义统计列表支持的数值与名称排序方式。
    /// </summary>
    internal enum StatisticsListSortMode
    {
        ValueDescending,
        ValueAscending,
        Name
    }

    /// <summary>
    /// 指定列表数值列的展示口径，使同一虚拟列表可复用到数量、次数和累计值统计。
    /// </summary>
    internal enum StatisticsMetricDisplay
    {
        UnitsAndEvents,
        Events,
        Amount,
        Mixed
    }

    /// <summary>
    /// 标记统计条目的内容来源，供通用工具栏在不解析运行时 ID 的情况下筛选。
    /// </summary>
    internal enum StatisticsContentSource
    {
        Neutral,
        Vanilla,
        Modded
    }

    /// <summary>
    /// 列表叶子项，只保存展示所需数据，不承担统计累计职责。
    /// </summary>
    internal sealed class StatisticsListItem
    {
        private readonly string name;
        private readonly string localizationKey;

        /// <summary>
        /// 创建一个可选带物品图标和延迟本地化名称的列表叶子项。
        /// </summary>
        internal StatisticsListItem(
            string name,
            int itemType,
            Asset<Texture2D> icon,
            long events,
            long units,
            long amount = 0L,
            StatisticsContentSource source = StatisticsContentSource.Vanilla,
            bool nameIsLocalizationKey = false)
        {
            if (nameIsLocalizationKey)
            {
                // UIState 可能在模组加载阶段创建，此时本地化表尚未完成注册。
                localizationKey = name;
            }
            else
            {
                this.name = name;
            }

            ItemType = itemType;
            Icon = icon;
            Events = events;
            Units = units;
            Amount = amount;
            Source = source;
        }

        internal string Name => localizationKey != null
            ? Language.GetTextValue(localizationKey)
            : name;

        internal int ItemType { get; }

        internal Asset<Texture2D> Icon { get; }

        internal long Events { get; }

        internal long Units { get; }

        internal long Amount { get; }

        internal StatisticsContentSource Source { get; }
    }

    /// <summary>
    /// 表示一组可折叠的统计项；合计始终由子项计算，避免出现第二份真值。
    /// </summary>
    internal sealed class StatisticsListGroup
    {
        private readonly string name;
        private readonly string localizationKey;

        /// <summary>
        /// 创建一个分组，并从全部直接子项和子分组计算合计值。
        /// </summary>
        internal StatisticsListGroup(
            string name,
            IEnumerable<StatisticsListItem> items,
            bool isExpanded = true,
            bool nameIsLocalizationKey = false,
            IEnumerable<StatisticsListGroup> childGroups = null,
            StatisticsMetricDisplay? metricDisplay = null)
        {
            if (nameIsLocalizationKey)
            {
                // 模组本地化在部分加载阶段尚未就绪，因此保存键并在显示时解析。
                localizationKey = name;
            }
            else
            {
                this.name = name;
            }

            Items = new List<StatisticsListItem>(items);
            ChildGroups = childGroups != null
                ? new List<StatisticsListGroup>(childGroups)
                : new List<StatisticsListGroup>();
            IsExpanded = isExpanded;
            MetricDisplay = metricDisplay;

            CalculateTotals();
        }

        /// <summary>
        /// 递归子分组已计算的合计，生成当前分组的唯一汇总值。
        /// </summary>
        private void CalculateTotals()
        {
            foreach (StatisticsListItem item in Items)
            {
                TotalEvents = SaturatingAdd(TotalEvents, item.Events);
                TotalUnits = SaturatingAdd(TotalUnits, item.Units);
                TotalAmount = SaturatingAdd(TotalAmount, item.Amount);
            }

            foreach (StatisticsListGroup childGroup in ChildGroups)
            {
                TotalEvents = SaturatingAdd(TotalEvents, childGroup.TotalEvents);
                TotalUnits = SaturatingAdd(TotalUnits, childGroup.TotalUnits);
                TotalAmount = SaturatingAdd(TotalAmount, childGroup.TotalAmount);
            }
        }

        /// <summary>
        /// 安全累加非负合计，避免超大统计值发生溢出。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        internal string Name => localizationKey != null
            ? Language.GetTextValue(localizationKey)
            : name;

        internal List<StatisticsListItem> Items { get; }

        internal List<StatisticsListGroup> ChildGroups { get; }

        internal long TotalEvents { get; private set; }

        internal long TotalUnits { get; private set; }

        internal long TotalAmount { get; private set; }

        internal bool IsExpanded { get; set; }

        internal StatisticsMetricDisplay? MetricDisplay { get; }
    }
}
