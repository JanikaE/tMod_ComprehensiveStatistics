using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Localization;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>
    /// 定义统计列表可通过列标题选择的排序字段。
    /// </summary>
    internal enum StatisticsListSortColumn
    {
        Name,
        Events,
        Units,
        Amount,
        Rate
    }

    /// <summary>
    /// 定义列标题在无排序、正序和倒序之间的切换状态。
    /// </summary>
    internal enum StatisticsListSortDirection
    {
        None,
        Ascending,
        Descending
    }

    /// <summary>
    /// 指定列表数值列的展示口径，使同一虚拟列表可复用到数量、次数和累计值统计。
    /// </summary>
    internal enum StatisticsMetricDisplay
    {
        UnitsAndEvents,
        UnitsAndEventsHideEqual,
        Events,
        Amount,
        Distance,
        Mixed,
        WiringActivity,
        ItemActivity,
        AmmoActivity,
        FishingActivity,
        FishingEvents,
        FishingCatchActivity,
        KillOutcomes,
        EconomyTransactions,
        LiquidVolume
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
    /// 指定统计条目图标贴图的内容类型，以便按对应帧规则裁切。
    /// </summary>
    internal enum StatisticsIconKind
    {
        None,
        Item,
        Tile,
        Wall,
        Npc,
        Buff
    }

    /// <summary>
    /// 描述物块图集中一个多格对象的分片布局；绘制时去掉分片间的贴图边距并无缝拼合。
    /// </summary>
    internal sealed class StatisticsTileIconLayout
    {
        internal StatisticsTileIconLayout(
            int sourceX,
            int sourceY,
            int columns,
            int coordinateWidth,
            int coordinatePadding,
            int[] coordinateHeights)
        {
            SourceX = sourceX;
            SourceY = sourceY;
            Columns = columns;
            CoordinateWidth = coordinateWidth;
            CoordinatePadding = coordinatePadding;
            CoordinateHeights = coordinateHeights;
        }

        internal int SourceX { get; }

        internal int SourceY { get; }

        internal int Columns { get; }

        internal int CoordinateWidth { get; }

        internal int CoordinatePadding { get; }

        internal int[] CoordinateHeights { get; }
    }

    /// <summary>
    /// 列表叶子项，只保存展示所需数据，不承担统计累计职责。
    /// </summary>
    internal sealed class StatisticsListItem
    {
        private readonly string name;
        private readonly string localizationKey;

        /// <summary>
        /// 创建一个可选带内容图标和延迟本地化名称的列表叶子项。
        /// </summary>
        internal StatisticsListItem(
            string name,
            int itemType,
            Asset<Texture2D> icon,
            long events,
            long units,
            long amount = 0L,
            StatisticsContentSource source = StatisticsContentSource.Vanilla,
            bool nameIsLocalizationKey = false,
            StatisticsIconKind iconKind = StatisticsIconKind.Item,
            StableContentKey? content = null,
            int? vanillaContentId = null,
            int amountIconType = 0,
            Asset<Texture2D> amountIcon = null,
            StatisticsTileIconLayout tileIconLayout = null,
            long? rateUnits = null,
            long? rateAmount = null)
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
            IconKind = icon == null ? StatisticsIconKind.None : iconKind;
            Events = events;
            Units = units;
            Amount = amount;
            Source = source;
            Content = content;
            VanillaContentId = vanillaContentId;
            AmountIconType = amountIconType;
            AmountIcon = amountIcon;
            TileIconLayout = tileIconLayout;
            RateUnits = rateUnits ?? units;
            RateAmount = rateAmount ?? amount;
        }

        internal string Name => localizationKey != null
            ? Language.GetTextValue(localizationKey)
            : name;

        internal int ItemType { get; }

        internal Asset<Texture2D> Icon { get; }

        internal StatisticsIconKind IconKind { get; }

        internal long Events { get; }

        internal long Units { get; }

        internal long Amount { get; }

        // 比率可使用独立的累计分母与分子。弹药页用此排除非消耗型
        // 弹药的比率计算，同时仍在“应耗数量”列保留其使用次数。
        internal long RateUnits { get; }

        internal long RateAmount { get; }

        internal StatisticsContentSource Source { get; }

        // 内容稳定键与原版数字 ID 只参与“无排序”状态下的统一默认顺序，
        // 不影响名称列使用本地化显示名进行的显式排序。
        internal StableContentKey? Content { get; }

        internal int? VanillaContentId { get; }

        internal int AmountIconType { get; }

        internal Asset<Texture2D> AmountIcon { get; }

        /// <summary>
        /// 可选的多格物体图集布局，用于无缝拼合完整对象而非显示单个组成格。
        /// </summary>
        internal StatisticsTileIconLayout TileIconLayout { get; }
    }

    /// <summary>
    /// 表示一组可折叠的统计项；合计始终由子项计算，避免出现第二份真值。
    /// </summary>
    internal sealed class StatisticsListGroup
    {
        private readonly string name;
        private readonly string localizationKey;
        private readonly string stateKey;

        /// <summary>
        /// 创建一个分组，并从全部直接子项和子分组计算合计值。
        /// </summary>
        internal StatisticsListGroup(
            string name,
            IEnumerable<StatisticsListItem> items,
            bool isExpanded = true,
            bool nameIsLocalizationKey = false,
            IEnumerable<StatisticsListGroup> childGroups = null,
            StatisticsMetricDisplay? metricDisplay = null,
            bool showChildren = true,
            int iconType = 0,
            Asset<Texture2D> icon = null,
            StatisticsIconKind iconKind = StatisticsIconKind.None,
            int amountIconType = 0,
            Asset<Texture2D> amountIcon = null,
            string stateKey = null)
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
            DefaultIsExpanded = isExpanded;
            MetricDisplay = metricDisplay;
            ShowChildren = showChildren;
            IconType = iconType;
            Icon = icon;
            IconKind = icon == null ? StatisticsIconKind.None : iconKind;
            AmountIconType = amountIconType;
            AmountIcon = amountIcon;
            this.stateKey = stateKey ?? localizationKey ?? name;
            if (string.IsNullOrEmpty(this.stateKey))
            {
                throw new ArgumentException("A stable group state key is required.", nameof(stateKey));
            }

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
                TotalRateUnits = SaturatingAdd(TotalRateUnits, item.RateUnits);
                TotalRateAmount = SaturatingAdd(TotalRateAmount, item.RateAmount);
            }

            foreach (StatisticsListGroup childGroup in ChildGroups)
            {
                TotalEvents = SaturatingAdd(TotalEvents, childGroup.TotalEvents);
                TotalUnits = SaturatingAdd(TotalUnits, childGroup.TotalUnits);
                TotalAmount = SaturatingAdd(TotalAmount, childGroup.TotalAmount);
                TotalRateUnits = SaturatingAdd(TotalRateUnits, childGroup.TotalRateUnits);
                TotalRateAmount = SaturatingAdd(TotalRateAmount, childGroup.TotalRateAmount);
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

        internal long TotalRateUnits { get; private set; }

        internal long TotalRateAmount { get; private set; }

        internal bool IsExpanded { get; set; }

        internal bool DefaultIsExpanded { get; }

        internal string StateKey => stateKey;

        internal StatisticsMetricDisplay? MetricDisplay { get; }

        internal bool ShowChildren { get; }

        internal int IconType { get; }

        internal Asset<Texture2D> Icon { get; }

        internal StatisticsIconKind IconKind { get; }

        internal int AmountIconType { get; }

        internal Asset<Texture2D> AmountIcon { get; }
    }
}
