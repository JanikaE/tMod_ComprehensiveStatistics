using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Localization;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    // A4 阶段支持的基础排序方式；后续工具栏会复用这些语义。
    internal enum StatisticsListSortMode
    {
        ValueDescending,
        ValueAscending,
        Name
    }

    /// <summary>
    /// 标记统计条目的内容来源，供通用工具栏在不解析运行时 ID 的情况下筛选。
    /// </summary>
    internal enum StatisticsContentSource
    {
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

        internal StatisticsListItem(
            string name,
            int itemType,
            Asset<Texture2D> icon,
            long events,
            long units,
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
            Source = source;
        }

        internal string Name => localizationKey != null
            ? Language.GetTextValue(localizationKey)
            : name;

        internal int ItemType { get; }

        internal Asset<Texture2D> Icon { get; }

        internal long Events { get; }

        internal long Units { get; }

        internal StatisticsContentSource Source { get; }
    }

    /// <summary>
    /// 表示一组可折叠的统计项；合计始终由子项计算，避免出现第二份真值。
    /// </summary>
    internal sealed class StatisticsListGroup
    {
        private readonly string name;
        private readonly string localizationKey;

        internal StatisticsListGroup(
            string name,
            IEnumerable<StatisticsListItem> items,
            bool isExpanded = true,
            bool nameIsLocalizationKey = false)
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
            IsExpanded = isExpanded;

            CalculateTotals();
        }

        private void CalculateTotals()
        {
            foreach (StatisticsListItem item in Items)
            {
                TotalEvents += item.Events;
                TotalUnits += item.Units;
            }
        }

        internal string Name => localizationKey != null
            ? Language.GetTextValue(localizationKey)
            : name;

        internal List<StatisticsListItem> Items { get; }

        internal long TotalEvents { get; private set; }

        internal long TotalUnits { get; private set; }

        internal bool IsExpanded { get; set; }
    }
}
