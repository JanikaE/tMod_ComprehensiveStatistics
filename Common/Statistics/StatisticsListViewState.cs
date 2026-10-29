using System;
using System.Collections.Generic;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>保存列表的稳定排序字段、排序方向及逻辑像素滚动位置。</summary>
    internal readonly struct StatisticsListViewState
    {
        /// <summary>创建列表视图快照，默认值代表无排序和列表顶部。</summary>
        internal StatisticsListViewState(string sortColumn, string sortDirection, float scrollPosition)
        {
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            ScrollPosition = float.IsFinite(scrollPosition) ? Math.Max(0f, scrollPosition) : 0f;
        }

        internal string SortColumn { get; }
        internal string SortDirection { get; }
        internal float ScrollPosition { get; }
    }

    /// <summary>以 Tab 和可选世界 UUID 隔离列表视图；空世界表示人物生涯。</summary>
    internal sealed class StatisticsListViewStateStore
    {
        private readonly Dictionary<(string Tab, Guid? WorldId), StatisticsListViewState> entries = new();

        internal IReadOnlyDictionary<(string Tab, Guid? WorldId), StatisticsListViewState> Entries => entries;

        /// <summary>读取指定上下文的快照，旧存档缺少记录时返回默认值。</summary>
        internal StatisticsListViewState GetTabState(string tab, Guid? worldId)
        {
            return entries.TryGetValue((tab, worldId), out var state) ? state : default;
        }

        /// <summary>只替换当前上下文，不影响其他 Tab 或世界。</summary>
        internal void SetTabState(string tab, Guid? worldId, StatisticsListViewState state)
        {
            entries[(tab, worldId)] = state;
        }
    }
}
