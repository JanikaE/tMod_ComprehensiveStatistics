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
        private const float ColumnHeaderHeight = 26f;
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
        private readonly List<SortHeader> columnHeaders = new List<SortHeader>();
        private readonly Dictionary<string, bool> expansionStates = new Dictionary<string, bool>();
        private StatisticsListSortColumn sortColumn = StatisticsListSortColumn.Name;
        private StatisticsListSortDirection sortDirection;
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
            rowViewport.Top.Set(ColumnHeaderHeight, 0f);
            rowViewport.Width.Set(-ScrollbarWidth - 4f, 1f);
            rowViewport.Height.Set(-ColumnHeaderHeight, 1f);
            rowViewport.OverflowHidden = true;
            Append(rowViewport);

            AppendColumnHeaders();

            emptyText = new UIText(
                Language.GetText("Mods.ComprehensiveStatistics.UI.List.NoResults"),
                0.82f);
            emptyText.HAlign = 0.5f;
            emptyText.Top.Set(12f, 0f);
            emptyText.TextColor = new Color(170, 184, 220);

            scrollbar = new UIScrollbar();
            scrollbar.Left.Set(-ScrollbarWidth, 1f);
            scrollbar.Top.Set(ColumnHeaderHeight, 0f);
            scrollbar.Width.Set(ScrollbarWidth, 0f);
            scrollbar.Height.Set(-ColumnHeaderHeight, 1f);
            Append(scrollbar);

            ApplySort(resetScroll: true);
        }

        /// <summary>
        /// 替换完整数据源，并沿用当前排序方式重建可见内容。
        /// </summary>
        internal void SetGroups(IEnumerable<StatisticsListGroup> newGroups)
        {
            CaptureExpansionStates(groups, parentPath: string.Empty);
            groups.Clear();
            groups.AddRange(newGroups);
            ApplyExpansionStates(groups, parentPath: string.Empty);
            ApplySort(resetScroll: true);
        }

        /// <summary>
        /// 清除本次窗口会话的折叠与排序状态，并恢复各分组定义的默认状态。
        /// </summary>
        internal void ResetTransientState()
        {
            expansionStates.Clear();
            ResetExpansionStates(groups);
            sortColumn = StatisticsListSortColumn.Name;
            sortDirection = StatisticsListSortDirection.None;
            ApplySort(resetScroll: true);
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

        public override void OnActivate()
        {
            base.OnActivate();
            RefreshColumnHeaders();
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
                        group.ShowChildren,
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
            CaptureExpansionStates(groups, parentPath: string.Empty);
            RebuildVisibleEntries(resetScroll: false);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        /// <summary>
        /// 递归保存当前数据树的折叠状态；保留暂时不在新范围中的旧键以便稍后恢复。
        /// </summary>
        private void CaptureExpansionStates(
            IEnumerable<StatisticsListGroup> sourceGroups,
            string parentPath)
        {
            foreach (StatisticsListGroup group in sourceGroups)
            {
                string path = CreateGroupPath(parentPath, group.StateKey);
                expansionStates[path] = group.IsExpanded;
                CaptureExpansionStates(group.ChildGroups, path);
            }
        }

        /// <summary>
        /// 将本次窗口会话中保存的折叠状态应用到新生成的数据树。
        /// </summary>
        private void ApplyExpansionStates(
            IEnumerable<StatisticsListGroup> targetGroups,
            string parentPath)
        {
            foreach (StatisticsListGroup group in targetGroups)
            {
                string path = CreateGroupPath(parentPath, group.StateKey);
                if (expansionStates.TryGetValue(path, out bool isExpanded))
                {
                    group.IsExpanded = isExpanded;
                }

                ApplyExpansionStates(group.ChildGroups, path);
            }
        }

        /// <summary>
        /// 递归恢复当前数据树中每个分组定义的默认折叠状态。
        /// </summary>
        private static void ResetExpansionStates(IEnumerable<StatisticsListGroup> sourceGroups)
        {
            foreach (StatisticsListGroup group in sourceGroups)
            {
                group.IsExpanded = group.DefaultIsExpanded;
                ResetExpansionStates(group.ChildGroups);
            }
        }

        /// <summary>
        /// 使用本地化键或原始名称构造不受排序位置影响的分组状态路径。
        /// </summary>
        private static string CreateGroupPath(string parentPath, string stateKey)
        {
            return string.Concat(parentPath, "\u001f", stateKey);
        }

        /// <summary>
        /// 根据筛选条件和展开状态重建当前可见的扁平条目序列。
        /// </summary>
        private void RebuildVisibleEntries(bool resetScroll)
        {
            // 先递归构建过滤后的分组树，再按展开状态展平；父组合计始终来自可见叶子项。
            visibleEntries.Clear();
            List<FilteredGroup> filteredGroups = new List<FilteredGroup>();
            foreach (StatisticsListGroup group in groups)
            {
                FilteredGroup filteredGroup = BuildFilteredGroup(
                    group,
                    ancestorMatchesSearch: false);
                if (filteredGroup != null)
                {
                    filteredGroups.Add(filteredGroup);
                }
            }

            // 无排序时保留数据定义顺序；启用列排序后，顶层分组也必须参与比较。
            if (sortDirection != StatisticsListSortDirection.None)
            {
                filteredGroups.Sort(CompareFilteredGroups);
            }
            foreach (FilteredGroup filteredGroup in filteredGroups)
            {
                AppendDisplayGroup(filteredGroup, depth: 0, metricDisplay);
            }

            SetEmptyTextVisible(visibleEntries.Count == 0);
            if (resetScroll)
            {
                scrollbar.ViewPosition = 0f;
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
            if (!filteredGroup.Group.IsExpanded || !filteredGroup.Group.ShowChildren)
            {
                return;
            }

            // 子分组和普通条目属于同一展示层级，必须放入同一序列比较，不能让分组固定靠前。
            List<SiblingEntry> siblings = new List<SiblingEntry>(
                filteredGroup.ChildGroups.Count + filteredGroup.Items.Count);
            foreach (FilteredGroup childGroup in filteredGroup.ChildGroups)
            {
                siblings.Add(SiblingEntry.ForGroup(childGroup));
            }

            foreach (StatisticsListItem item in filteredGroup.Items)
            {
                siblings.Add(SiblingEntry.ForItem(item));
            }

            if (sortDirection != StatisticsListSortDirection.None)
            {
                siblings.Sort(CompareSiblingEntries);
            }
            foreach (SiblingEntry sibling in siblings)
            {
                if (sibling.Group != null)
                {
                    AppendDisplayGroup(
                        sibling.Group,
                        depth + 1,
                        resolvedMetricDisplay);
                }
                else
                {
                    visibleEntries.Add(DisplayEntry.ForItem(
                        sibling.Item,
                        depth + 1,
                        resolvedMetricDisplay));
                }
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
        /// 按选中列比较两个顶层筛选分组。
        /// </summary>
        private int CompareFilteredGroups(FilteredGroup left, FilteredGroup right)
        {
            return CompareRows(
                left.Group.Name,
                left.TotalEvents,
                left.TotalUnits,
                left.TotalAmount,
                right.Group.Name,
                right.TotalEvents,
                right.TotalUnits,
                right.TotalAmount);
        }

        /// <summary>
        /// 按选中列比较同一层级的分组行或普通条目行。
        /// </summary>
        private int CompareSiblingEntries(SiblingEntry left, SiblingEntry right)
        {
            return CompareRows(
                left.Name,
                left.Events,
                left.Units,
                left.Amount,
                right.Name,
                right.Events,
                right.Units,
                right.Amount);
        }

        /// <summary>
        /// 比较两行的名称或数值，并在数值相同时使用名称保证顺序确定。
        /// </summary>
        private int CompareRows(
            string leftName,
            long leftEvents,
            long leftUnits,
            long leftAmount,
            string rightName,
            long rightEvents,
            long rightUnits,
            long rightAmount)
        {
            int result = sortColumn == StatisticsListSortColumn.Name
                ? string.Compare(leftName, rightName, StringComparison.CurrentCulture)
                : GetSortValue(leftEvents, leftUnits, leftAmount, sortColumn).CompareTo(
                    GetSortValue(rightEvents, rightUnits, rightAmount, sortColumn));
            if (sortDirection == StatisticsListSortDirection.Descending)
            {
                result = -result;
            }

            return result != 0
                ? result
                : string.Compare(leftName, rightName, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// 获取指定列实际参与排序的数值。
        /// </summary>
        private static long GetSortValue(
            long events,
            long units,
            long amount,
            StatisticsListSortColumn column)
        {
            return column switch
            {
                StatisticsListSortColumn.Events => events,
                StatisticsListSortColumn.Amount => amount,
                _ => units
            };
        }

        /// <summary>
        /// 为当前指标口径添加可点击的列标题。
        /// </summary>
        private void AppendColumnHeaders()
        {
            string nameKey = metricDisplay == StatisticsMetricDisplay.Mixed
                ? "Mods.ComprehensiveStatistics.UI.Survival.CategoryColumn"
                : "Mods.ComprehensiveStatistics.UI.List.NameColumn";
            float nameRightReserve = metricDisplay == StatisticsMetricDisplay.Mixed
                ? 254f
                : 160f;
            AddColumnHeader(
                nameKey,
                StatisticsListSortColumn.Name,
                leftPixels: 0f,
                widthPixels: -nameRightReserve,
                widthPercent: 1f);

            if (metricDisplay == StatisticsMetricDisplay.Mixed)
            {
                AddColumnHeader(
                    "Mods.ComprehensiveStatistics.UI.Survival.DeathsColumn",
                    StatisticsListSortColumn.Events,
                    leftPixels: -254f,
                    widthPixels: 116f,
                    widthPercent: 0f,
                    leftPercent: 1f);
                AddColumnHeader(
                    "Mods.ComprehensiveStatistics.UI.Survival.LifeLostColumn",
                    StatisticsListSortColumn.Amount,
                    leftPixels: -138f,
                    widthPixels: 116f,
                    widthPercent: 0f,
                    leftPercent: 1f);
                return;
            }

            (string localizationKey, StatisticsListSortColumn column) = metricDisplay switch
            {
                StatisticsMetricDisplay.Events => (
                    "Mods.ComprehensiveStatistics.UI.List.EventsColumn",
                    StatisticsListSortColumn.Events),
                StatisticsMetricDisplay.Amount => (
                    "Mods.ComprehensiveStatistics.UI.List.ValueColumn",
                    StatisticsListSortColumn.Amount),
                StatisticsMetricDisplay.Distance => (
                    "Mods.ComprehensiveStatistics.UI.List.DistanceColumn",
                    StatisticsListSortColumn.Amount),
                _ => (
                    "Mods.ComprehensiveStatistics.UI.List.MetricColumn",
                    StatisticsListSortColumn.Units)
            };
            AddColumnHeader(
                localizationKey,
                column,
                leftPixels: -160f,
                widthPixels: 138f,
                widthPercent: 0f,
                leftPercent: 1f);
        }

        /// <summary>
        /// 创建一个具有指定布局和排序字段的列标题。
        /// </summary>
        private void AddColumnHeader(
            string localizationKey,
            StatisticsListSortColumn column,
            float leftPixels,
            float widthPixels,
            float widthPercent,
            float leftPercent = 0f)
        {
            SortHeader header = new SortHeader(
                localizationKey,
                column,
                SelectSortColumn);
            header.Left.Set(leftPixels, leftPercent);
            header.Width.Set(widthPixels, widthPercent);
            header.Height.Set(ColumnHeaderHeight - 2f, 0f);
            columnHeaders.Add(header);
            Append(header);
        }

        /// <summary>
        /// 选择列标题；同一列依次在正序、倒序和无排序之间循环。
        /// </summary>
        private void SelectSortColumn(StatisticsListSortColumn column)
        {
            if (sortColumn != column)
            {
                sortColumn = column;
                sortDirection = StatisticsListSortDirection.Ascending;
            }
            else
            {
                sortDirection = sortDirection switch
                {
                    StatisticsListSortDirection.None => StatisticsListSortDirection.Ascending,
                    StatisticsListSortDirection.Ascending => StatisticsListSortDirection.Descending,
                    _ => StatisticsListSortDirection.None
                };
            }

            ApplySort(resetScroll: true);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        /// <summary>
        /// 应用当前列排序，并同步列标题方向标记与可见行。
        /// </summary>
        private void ApplySort(bool resetScroll)
        {
            RefreshColumnHeaders();
            RebuildVisibleEntries(resetScroll);
        }

        /// <summary>
        /// 刷新所有列标题的本地化文字和当前排序方向。
        /// </summary>
        private void RefreshColumnHeaders()
        {
            foreach (SortHeader header in columnHeaders)
            {
                header.Refresh(
                    direction: header.Column == sortColumn
                        ? sortDirection
                        : StatisticsListSortDirection.None);
            }
        }

        /// <summary>
        /// 安全累加筛选后的分组合计，避免整数溢出。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        /// <summary>
        /// 可点击的列表列标题，负责显示当前排序方向并转发目标字段。
        /// </summary>
        private sealed class SortHeader : UIPanel
        {
            private readonly string localizationKey;
            private readonly Action<StatisticsListSortColumn> clickAction;
            private readonly UIText text;

            /// <summary>
            /// 创建绑定到指定排序字段的列标题。
            /// </summary>
            internal SortHeader(
                string localizationKey,
                StatisticsListSortColumn column,
                Action<StatisticsListSortColumn> clickAction)
            {
                this.localizationKey = localizationKey;
                this.clickAction = clickAction;
                Column = column;

                SetPadding(2f);
                BorderColor = new Color(65, 84, 150, 220);
                OnLeftClick += HandleClick;

                text = new UIText(string.Empty, 0.7f);
                text.HAlign = 0.5f;
                text.VAlign = 0.5f;
                text.TextColor = new Color(190, 204, 235);
                Append(text);
            }

            internal StatisticsListSortColumn Column { get; }

            /// <summary>
            /// 使用当前语言和选择状态刷新标题文字。
            /// </summary>
            internal void Refresh(StatisticsListSortDirection direction)
            {
                string label = Language.GetTextValue(localizationKey);
                text.SetText(direction switch
                {
                    StatisticsListSortDirection.Ascending => $"{label} ↑",
                    StatisticsListSortDirection.Descending => $"{label} ↓",
                    _ => label
                });
            }

            public override void Update(GameTime gameTime)
            {
                BackgroundColor = IsMouseHovering
                    ? new Color(54, 70, 129, 235)
                    : new Color(35, 46, 86, 220);
                base.Update(gameTime);
            }

            /// <summary>
            /// 将点击转换为当前列的排序请求。
            /// </summary>
            private void HandleClick(UIMouseEvent evt, UIElement listeningElement)
            {
                clickAction(Column);
            }
        }

        /// <summary>
        /// 统一表示同一树层级中的筛选分组或普通条目，供两者混合排序。
        /// </summary>
        private sealed class SiblingEntry
        {
            /// <summary>
            /// 创建一个分组或普通条目对应的同级排序节点。
            /// </summary>
            private SiblingEntry(FilteredGroup group, StatisticsListItem item)
            {
                Group = group;
                Item = item;
            }

            internal FilteredGroup Group { get; }

            internal StatisticsListItem Item { get; }

            internal string Name => Group?.Group.Name ?? Item.Name;

            internal long Events => Group?.TotalEvents ?? Item.Events;

            internal long Units => Group?.TotalUnits ?? Item.Units;

            internal long Amount => Group?.TotalAmount ?? Item.Amount;

            /// <summary>
            /// 创建携带筛选后合计的分组排序节点。
            /// </summary>
            internal static SiblingEntry ForGroup(FilteredGroup group)
            {
                return new SiblingEntry(group, item: null);
            }

            /// <summary>
            /// 创建普通条目排序节点。
            /// </summary>
            internal static SiblingEntry ForItem(StatisticsListItem item)
            {
                return new SiblingEntry(group: null, item);
            }
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
