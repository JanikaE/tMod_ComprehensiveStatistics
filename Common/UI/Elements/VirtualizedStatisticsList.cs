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
        private const float HeaderHeight = 30f;
        private const float RowHeight = 34f;
        private const float ScrollbarWidth = 18f;
        private const int OverscanRows = 2;

        private readonly List<StatisticsListGroup> groups;
        // visibleEntries 是展开状态对应的扁平视图；rowPool 是可循环复用的 UI 控件池。
        private readonly List<DisplayEntry> visibleEntries = new List<DisplayEntry>();
        private readonly List<StatisticsListRow> rowPool = new List<StatisticsListRow>();

        private readonly UIElement rowViewport;
        private readonly UIScrollbar scrollbar;
        private readonly UIText emptyText;
        private StatisticsListSortMode sortMode;
        private string searchQuery = string.Empty;
        private bool hideZeroValues = true;
        private StatisticsContentSourceFilter sourceFilter;
        private float lastViewportHeight = -1f;
        private bool isEmptyTextAttached;

        internal VirtualizedStatisticsList(IEnumerable<StatisticsListGroup> groups)
        {
            this.groups = new List<StatisticsListGroup>(groups);

            UIText nameHeader = new UIText(Language.GetText("Mods.ComprehensiveStatistics.UI.List.NameColumn"), 0.76f);
            nameHeader.Left.Set(8f, 0f);
            nameHeader.Top.Set(5f, 0f);
            nameHeader.TextColor = new Color(170, 184, 220);
            Append(nameHeader);

            UIText metricHeader = new UIText(
                Language.GetText("Mods.ComprehensiveStatistics.UI.List.MetricColumn"),
                0.76f);
            metricHeader.Left.Set(-ScrollbarWidth - 8f, 1f);
            metricHeader.Top.Set(5f, 0f);
            metricHeader.HAlign = 1f;
            metricHeader.TextColor = new Color(170, 184, 220);
            Append(metricHeader);

            rowViewport = new UIElement();
            rowViewport.Top.Set(HeaderHeight, 0f);
            rowViewport.Width.Set(-ScrollbarWidth - 4f, 1f);
            rowViewport.Height.Set(-HeaderHeight, 1f);
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
            scrollbar.Top.Set(HeaderHeight, 0f);
            scrollbar.Width.Set(ScrollbarWidth, 0f);
            scrollbar.Height.Set(-HeaderHeight, 1f);
            Append(scrollbar);

            SetSortMode(StatisticsListSortMode.ValueDescending);
        }

        internal void SetSortMode(StatisticsListSortMode newSortMode)
        {
            sortMode = newSortMode;
            Comparison<StatisticsListItem> comparison = sortMode switch
            {
                StatisticsListSortMode.ValueAscending => CompareValueAscending,
                StatisticsListSortMode.Name => CompareName,
                _ => CompareValueDescending
            };

            foreach (StatisticsListGroup group in groups)
            {
                group.Items.Sort(comparison);
            }

            RebuildVisibleEntries(resetScroll: true);
        }

        internal void SetFilters(
            string newSearchQuery,
            bool newHideZeroValues,
            StatisticsContentSourceFilter newSourceFilter)
        {
            searchQuery = newSearchQuery?.Trim() ?? string.Empty;
            hideZeroValues = newHideZeroValues;
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
                        () => ToggleGroup(group));
                }
                else
                {
                    row.BindItem(entry.Item);
                }
            }

            rowViewport.Recalculate();
        }

        private void ToggleGroup(StatisticsListGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            RebuildVisibleEntries(resetScroll: false);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        private void RebuildVisibleEntries(bool resetScroll)
        {
            // 合计来自当前过滤结果；折叠只影响子项是否进入扁平视图，不改变筛选条件。
            visibleEntries.Clear();
            foreach (StatisticsListGroup group in groups)
            {
                bool groupMatchesSearch = MatchesSearch(group.Name);
                List<StatisticsListItem> filteredItems = new List<StatisticsListItem>();
                long totalEvents = 0L;
                long totalUnits = 0L;

                foreach (StatisticsListItem item in group.Items)
                {
                    if (!MatchesFilters(item, groupMatchesSearch))
                    {
                        continue;
                    }

                    filteredItems.Add(item);
                    totalEvents += item.Events;
                    totalUnits += item.Units;
                }

                if (filteredItems.Count == 0)
                {
                    continue;
                }

                visibleEntries.Add(DisplayEntry.ForGroup(group, totalEvents, totalUnits));
                if (!group.IsExpanded)
                {
                    continue;
                }

                foreach (StatisticsListItem item in filteredItems)
                {
                    visibleEntries.Add(DisplayEntry.ForItem(item));
                }
            }

            SetEmptyTextVisible(visibleEntries.Count == 0);
            if (resetScroll)
            {
                scrollbar.ViewPosition = 0f;
            }
        }

        private bool MatchesFilters(StatisticsListItem item, bool groupMatchesSearch)
        {
            if (hideZeroValues && item.Events == 0L && item.Units == 0L)
            {
                return false;
            }

            if (sourceFilter == StatisticsContentSourceFilter.Vanilla
                && item.Source != StatisticsContentSource.Vanilla)
            {
                return false;
            }

            if (sourceFilter == StatisticsContentSourceFilter.Modded
                && item.Source != StatisticsContentSource.Modded)
            {
                return false;
            }

            return groupMatchesSearch || MatchesSearch(item.Name);
        }

        private bool MatchesSearch(string value)
        {
            return searchQuery.Length == 0
                || value.IndexOf(searchQuery, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

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

        private static int CompareValueDescending(StatisticsListItem left, StatisticsListItem right)
        {
            int result = right.Units.CompareTo(left.Units);
            return result != 0 ? result : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        }

        private static int CompareValueAscending(StatisticsListItem left, StatisticsListItem right)
        {
            int result = left.Units.CompareTo(right.Units);
            return result != 0 ? result : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        }

        private static int CompareName(StatisticsListItem left, StatisticsListItem right)
        {
            return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// 表示扁平列表中的一个分组行或子项行。
        /// </summary>
        private sealed class DisplayEntry
        {
            // 两者始终只有一个非空，用统一序列表示两种行。
            private DisplayEntry(
                StatisticsListGroup group,
                StatisticsListItem item,
                long totalEvents,
                long totalUnits)
            {
                Group = group;
                Item = item;
                TotalEvents = totalEvents;
                TotalUnits = totalUnits;
            }

            internal StatisticsListGroup Group { get; }

            internal StatisticsListItem Item { get; }

            internal long TotalEvents { get; }

            internal long TotalUnits { get; }

            internal static DisplayEntry ForGroup(
                StatisticsListGroup group,
                long totalEvents,
                long totalUnits)
            {
                return new DisplayEntry(group, null, totalEvents, totalUnits);
            }

            internal static DisplayEntry ForItem(StatisticsListItem item)
            {
                return new DisplayEntry(null, item, 0L, 0L);
            }
        }
    }
}
