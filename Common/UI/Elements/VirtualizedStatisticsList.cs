using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>
    /// 虚拟化分组列表；无论数据量多大，只创建当前可见行及少量缓冲行。
    /// </summary>
    internal sealed class VirtualizedStatisticsList : UIElement
    {
        private const float RowHeight = 34f;
        private const float ScrollbarWidth = 18f;
        private const int OverscanRows = 2;

        private readonly List<StatisticsListGroup> groups;
        private readonly StatisticsMetricDisplay metricDisplay;
        // visibleEntries 是展开状态对应的扁平视图；rowPool 是可循环复用的 UI 控件池。
        private readonly List<DisplayEntry> visibleEntries = new List<DisplayEntry>();
        private readonly List<StatisticsListRow> rowPool = new List<StatisticsListRow>();

        private readonly UIElement rowViewport;
        private readonly UIScrollbar scrollbar;
        private readonly UIText emptyText;
        private StatisticsListSortMode sortMode;
        private string searchQuery = string.Empty;
        private StatisticsContentSourceFilter sourceFilter;
        private float lastViewportHeight = -1f;
        private bool isEmptyTextAttached;

        /// <summary>
        /// 创建仅实例化可见行的分组统计列表。
        /// </summary>
        internal VirtualizedStatisticsList(
            IEnumerable<StatisticsListGroup> groups,
            StatisticsMetricDisplay metricDisplay = StatisticsMetricDisplay.UnitsAndEvents)
        {
            this.groups = new List<StatisticsListGroup>(groups);
            this.metricDisplay = metricDisplay;

            rowViewport = new UIElement();
            rowViewport.Top.Set(0f, 0f);
            rowViewport.Width.Set(-ScrollbarWidth - 4f, 1f);
            rowViewport.Height.Set(0f, 1f);
            rowViewport.OverflowHidden = true;
            Append(rowViewport);

            emptyText = new UIText(
                Language.GetText("Mods.ComprehensiveStatistics.UI.List.NoResults"),
                0.82f);
            emptyText.HAlign = 0.5f;
            emptyText.Top.Set(12f, 0f);
            emptyText.TextColor = new Color(170, 184, 220);

            scrollbar = new UIScrollbar();
            scrollbar.Left.Set(-ScrollbarWidth, 1f);
            scrollbar.Top.Set(0f, 0f);
            scrollbar.Width.Set(ScrollbarWidth, 0f);
            scrollbar.Height.Set(0f, 1f);
            Append(scrollbar);

            SetSortMode(StatisticsListSortMode.ValueDescending);
        }

        /// <summary>
        /// 替换完整数据源，并沿用当前排序方式重建可见内容。
        /// </summary>
        internal void SetGroups(IEnumerable<StatisticsListGroup> newGroups)
        {
            groups.Clear();
            groups.AddRange(newGroups);
            SetSortMode(sortMode);
        }

        /// <summary>
        /// 设置排序方式，递归排序各分组中的叶子项。
        /// </summary>
        internal void SetSortMode(StatisticsListSortMode newSortMode)
        {
            sortMode = newSortMode;
            foreach (StatisticsListGroup group in groups)
            {
                SortGroup(group, metricDisplay);
            }

            RebuildVisibleEntries(resetScroll: true);
        }

        /// <summary>
        /// 设置名称与内容来源筛选，并从列表顶部展示新结果。
        /// </summary>
        internal void SetFilters(
            string newSearchQuery,
            StatisticsContentSourceFilter newSourceFilter)
        {
            searchQuery = newSearchQuery?.Trim() ?? string.Empty;
            sourceFilter = newSourceFilter;
            RebuildVisibleEntries(resetScroll: true);
        }

        public override void Update(GameTime gameTime)
        {
            // 滚动条以逻辑行总高度为范围，不需要为不可见数据创建 UIElement。
            CalculatedStyle viewportDimensions = rowViewport.GetInnerDimensions();
            float viewportHeight = MathHelper.Max(1f, viewportDimensions.Height);
            float contentHeight = visibleEntries.Count * RowHeight;
            scrollbar.SetView(viewportHeight, MathHelper.Max(viewportHeight, contentHeight));

            if (IsMouseHovering)
            {
                Main.LocalPlayer.mouseInterface = true;

                if (PlayerInput.ScrollWheelDeltaForUI != 0)
                {
                    // 每个滚轮刻度移动三行，在长列表中兼顾速度和可控性。
                    float wheelRows = PlayerInput.ScrollWheelDeltaForUI / 120f;
                    scrollbar.ViewPosition = MathHelper.Clamp(
                        scrollbar.ViewPosition - wheelRows * RowHeight * 3f,
                        0f,
                        MathHelper.Max(0f, contentHeight - viewportHeight));
                }
            }

            EnsureRowPool(viewportHeight);
            BindVisibleRows(viewportHeight);
            base.Update(gameTime);
        }

        /// <summary>
        /// 按视口高度扩充可复用行池，并保留少量滚动缓冲行。
        /// </summary>
        private void EnsureRowPool(float viewportHeight)
        {
            // 多保留两行作为上下滚动缓冲，防止边缘出现短暂空白。
            int requiredRows = Math.Max(1, (int)Math.Ceiling(viewportHeight / RowHeight) + OverscanRows);
            while (rowPool.Count < requiredRows)
            {
                StatisticsListRow row = new StatisticsListRow();
                row.Width.Set(0f, 1f);
                row.Height.Set(RowHeight - 2f, 0f);
                rowPool.Add(row);
                rowViewport.Append(row);
            }

            if (lastViewportHeight != viewportHeight)
            {
                lastViewportHeight = viewportHeight;
                Recalculate();
            }
        }

        /// <summary>
        /// 将当前滚动区间内的扁平数据连续绑定到固定行池。
        /// </summary>
        private void BindVisibleRows(float viewportHeight)
        {
            // 根据滚动偏移定位第一条可见数据，再把连续数据绑定到固定行池。
            float viewPosition = scrollbar.ViewPosition;
            int firstEntryIndex = Math.Max(0, (int)(viewPosition / RowHeight));
            float firstRowOffset = -(viewPosition - firstEntryIndex * RowHeight);
            int rowsNeeded = Math.Min(
                rowPool.Count,
                (int)Math.Ceiling((viewportHeight - firstRowOffset) / RowHeight) + 1);

            for (int poolIndex = 0; poolIndex < rowPool.Count; poolIndex++)
            {
                StatisticsListRow row = rowPool[poolIndex];
                int entryIndex = firstEntryIndex + poolIndex;

                if (poolIndex >= rowsNeeded || entryIndex >= visibleEntries.Count)
                {
                    row.Top.Set(0f, 0f);
                    row.Width.Set(0f, 0f);
                    row.Height.Set(0f, 0f);
                    row.ClearBinding();
                    continue;
                }

                row.Top.Set(firstRowOffset + poolIndex * RowHeight, 0f);
                row.Width.Set(0f, 1f);
                row.Height.Set(RowHeight - 2f, 0f);

                DisplayEntry entry = visibleEntries[entryIndex];
                if (entry.Group != null)
                {
                    StatisticsListGroup group = entry.Group;
                    row.BindGroup(
                        group.Name,
                        group.IsExpanded,
                        entry.TotalEvents,
                        entry.TotalUnits,
                        entry.TotalAmount,
                        entry.MetricDisplay,
                        entry.Depth,
                        () => ToggleGroup(group));
                }
                else
                {
                    row.BindItem(entry.Item, entry.MetricDisplay, entry.Depth);
                }
            }

            rowViewport.Recalculate();
        }

        /// <summary>
        /// 切换分组展开状态，并保持当前滚动位置重建扁平视图。
        /// </summary>
        private void ToggleGroup(StatisticsListGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            RebuildVisibleEntries(resetScroll: false);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        /// <summary>
        /// 根据筛选条件和展开状态重建当前可见的扁平条目序列。
        /// </summary>
        private void RebuildVisibleEntries(bool resetScroll)
        {
            // 先递归构建过滤后的分组树，再按展开状态展平；父组合计始终来自可见叶子项。
            visibleEntries.Clear();
            foreach (StatisticsListGroup group in groups)
            {
                FilteredGroup filteredGroup = BuildFilteredGroup(
                    group,
                    ancestorMatchesSearch: false);
                if (filteredGroup != null)
                {
                    AppendDisplayGroup(filteredGroup, depth: 0, metricDisplay);
                }
            }

            SetEmptyTextVisible(visibleEntries.Count == 0);
            if (resetScroll)
            {
                scrollbar.ViewPosition = 0f;
            }
        }

        /// <summary>
        /// 按分组实际显示口径递归排序全部叶子项。
        /// </summary>
        private void SortGroup(
            StatisticsListGroup group,
            StatisticsMetricDisplay inheritedMetricDisplay)
        {
            StatisticsMetricDisplay resolvedMetricDisplay =
                group.MetricDisplay ?? inheritedMetricDisplay;
            Comparison<StatisticsListItem> comparison = sortMode switch
            {
                StatisticsListSortMode.ValueAscending =>
                    (left, right) => CompareValueAscending(
                        left,
                        right,
                        resolvedMetricDisplay),
                StatisticsListSortMode.Name => CompareName,
                _ => (left, right) => CompareValueDescending(
                    left,
                    right,
                    resolvedMetricDisplay)
            };
            group.Items.Sort(comparison);
            foreach (StatisticsListGroup childGroup in group.ChildGroups)
            {
                SortGroup(childGroup, resolvedMetricDisplay);
            }
        }

        /// <summary>
        /// 递归构建只含匹配叶子项的临时分组树，并重新计算可见合计。
        /// </summary>
        private FilteredGroup BuildFilteredGroup(
            StatisticsListGroup group,
            bool ancestorMatchesSearch)
        {
            bool groupMatchesSearch = ancestorMatchesSearch || MatchesSearch(group.Name);
            FilteredGroup result = new FilteredGroup(group);

            foreach (StatisticsListGroup childGroup in group.ChildGroups)
            {
                FilteredGroup filteredChild = BuildFilteredGroup(
                    childGroup,
                    groupMatchesSearch);
                if (filteredChild == null)
                {
                    continue;
                }

                result.ChildGroups.Add(filteredChild);
                result.AddTotals(
                    filteredChild.TotalEvents,
                    filteredChild.TotalUnits,
                    filteredChild.TotalAmount);
            }

            foreach (StatisticsListItem item in group.Items)
            {
                if (!MatchesFilters(item, groupMatchesSearch))
                {
                    continue;
                }

                result.Items.Add(item);
                result.AddTotals(item.Events, item.Units, item.Amount);
            }

            return result.Items.Count == 0 && result.ChildGroups.Count == 0
                ? null
                : result;
        }

        /// <summary>
        /// 按展开状态将筛选树深度优先展平为可渲染行。
        /// </summary>
        private void AppendDisplayGroup(
            FilteredGroup filteredGroup,
            int depth,
            StatisticsMetricDisplay inheritedMetricDisplay)
        {
            StatisticsMetricDisplay resolvedMetricDisplay =
                filteredGroup.Group.MetricDisplay ?? inheritedMetricDisplay;
            visibleEntries.Add(DisplayEntry.ForGroup(
                filteredGroup.Group,
                filteredGroup.TotalEvents,
                filteredGroup.TotalUnits,
                filteredGroup.TotalAmount,
                depth,
                resolvedMetricDisplay));
            if (!filteredGroup.Group.IsExpanded)
            {
                return;
            }

            foreach (FilteredGroup childGroup in filteredGroup.ChildGroups)
            {
                AppendDisplayGroup(
                    childGroup,
                    depth + 1,
                    resolvedMetricDisplay);
            }

            foreach (StatisticsListItem item in filteredGroup.Items)
            {
                visibleEntries.Add(DisplayEntry.ForItem(
                    item,
                    depth + 1,
                    resolvedMetricDisplay));
            }
        }

        /// <summary>
        /// 判断叶子项是否满足非零、来源和名称筛选条件。
        /// </summary>
        private bool MatchesFilters(StatisticsListItem item, bool groupMatchesSearch)
        {
            // 零值在所有统计页固定隐藏，避免工具栏状态与稀疏存档语义产生歧义。
            if (item.Events == 0L
                && item.Units == 0L
                && item.Amount == 0L)
            {
                return false;
            }

            if (sourceFilter == StatisticsContentSourceFilter.Vanilla
                && item.Source != StatisticsContentSource.Neutral
                && item.Source != StatisticsContentSource.Vanilla)
            {
                return false;
            }

            if (sourceFilter == StatisticsContentSourceFilter.Modded
                && item.Source != StatisticsContentSource.Neutral
                && item.Source != StatisticsContentSource.Modded)
            {
                return false;
            }

            return groupMatchesSearch || MatchesSearch(item.Name);
        }

        /// <summary>
        /// 使用当前区域性的忽略大小写规则匹配搜索文本。
        /// </summary>
        private bool MatchesSearch(string value)
        {
            return searchQuery.Length == 0
                || value.IndexOf(searchQuery, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// 按需挂载或移除空结果提示，避免重复修改 UI 树。
        /// </summary>
        private void SetEmptyTextVisible(bool visible)
        {
            if (visible == isEmptyTextAttached)
            {
                return;
            }

            if (visible)
            {
                rowViewport.Append(emptyText);
            }
            else
            {
                emptyText.Remove();
            }

            isEmptyTextAttached = visible;
        }

        /// <summary>
        /// 按显示数值降序比较叶子项，数值相同时按名称排序。
        /// </summary>
        private static int CompareValueDescending(
            StatisticsListItem left,
            StatisticsListItem right,
            StatisticsMetricDisplay metricDisplay)
        {
            int result = GetSortValue(right, metricDisplay).CompareTo(
                GetSortValue(left, metricDisplay));
            return result != 0 ? result : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// 按显示数值升序比较叶子项，数值相同时按名称排序。
        /// </summary>
        private static int CompareValueAscending(
            StatisticsListItem left,
            StatisticsListItem right,
            StatisticsMetricDisplay metricDisplay)
        {
            int result = GetSortValue(left, metricDisplay).CompareTo(
                GetSortValue(right, metricDisplay));
            return result != 0 ? result : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// 获取指定显示口径实际参与排序的数值。
        /// </summary>
        private static long GetSortValue(
            StatisticsListItem item,
            StatisticsMetricDisplay metricDisplay)
        {
            return metricDisplay switch
            {
                StatisticsMetricDisplay.Events => item.Events,
                StatisticsMetricDisplay.Amount => item.Amount,
                _ => item.Units
            };
        }

        /// <summary>
        /// 按当前区域性的名称顺序比较叶子项。
        /// </summary>
        private static int CompareName(StatisticsListItem left, StatisticsListItem right)
        {
            return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// 安全累加筛选后的分组合计，避免整数溢出。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        /// <summary>
        /// 保存一次筛选产生的临时分组树及合计，不修改持久的展开状态和原始数据。
        /// </summary>
        private sealed class FilteredGroup
        {
            /// <summary>
            /// 创建关联原始分组但不修改其内容的筛选结果节点。
            /// </summary>
            internal FilteredGroup(StatisticsListGroup group)
            {
                Group = group;
            }

            internal StatisticsListGroup Group { get; }

            internal List<StatisticsListItem> Items { get; } = new List<StatisticsListItem>();

            internal List<FilteredGroup> ChildGroups { get; } = new List<FilteredGroup>();

            internal long TotalEvents { get; private set; }

            internal long TotalUnits { get; private set; }

            internal long TotalAmount { get; private set; }

            /// <summary>
            /// 将一个可见子节点的指标累加到筛选后合计。
            /// </summary>
            internal void AddTotals(long events, long units, long amount)
            {
                TotalEvents = SaturatingAdd(TotalEvents, events);
                TotalUnits = SaturatingAdd(TotalUnits, units);
                TotalAmount = SaturatingAdd(TotalAmount, amount);
            }
        }

        /// <summary>
        /// 表示扁平列表中的一个分组行或子项行。
        /// </summary>
        private sealed class DisplayEntry
        {
            // 两者始终只有一个非空，用统一序列表示两种行。
            /// <summary>
            /// 创建一个分组或叶子项对应的扁平显示条目。
            /// </summary>
            private DisplayEntry(
                StatisticsListGroup group,
                StatisticsListItem item,
                long totalEvents,
                long totalUnits,
                long totalAmount,
                int depth,
                StatisticsMetricDisplay metricDisplay)
            {
                Group = group;
                Item = item;
                TotalEvents = totalEvents;
                TotalUnits = totalUnits;
                TotalAmount = totalAmount;
                Depth = depth;
                MetricDisplay = metricDisplay;
            }

            internal StatisticsListGroup Group { get; }

            internal StatisticsListItem Item { get; }

            internal long TotalEvents { get; }

            internal long TotalUnits { get; }

            internal long TotalAmount { get; }

            internal int Depth { get; }

            internal StatisticsMetricDisplay MetricDisplay { get; }

            /// <summary>
            /// 创建携带筛选后合计的分组显示条目。
            /// </summary>
            internal static DisplayEntry ForGroup(
                StatisticsListGroup group,
                long totalEvents,
                long totalUnits,
                long totalAmount,
                int depth,
                StatisticsMetricDisplay metricDisplay)
            {
                return new DisplayEntry(
                    group,
                    null,
                    totalEvents,
                    totalUnits,
                    totalAmount,
                    depth,
                    metricDisplay);
            }

            /// <summary>
            /// 创建指定树深度的叶子项显示条目。
            /// </summary>
            internal static DisplayEntry ForItem(
                StatisticsListItem item,
                int depth,
                StatisticsMetricDisplay metricDisplay)
            {
                return new DisplayEntry(
                    null,
                    item,
                    0L,
                    0L,
                    0L,
                    depth,
                    metricDisplay);
            }
        }
    }
}
