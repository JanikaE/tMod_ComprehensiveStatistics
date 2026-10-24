using System;
using System.Collections.Generic;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 标识人物生涯或指定世界中某个 Tab 的一条分组展开状态。
    /// </summary>
    internal readonly struct StatisticsExpansionStateKey : IEquatable<StatisticsExpansionStateKey>
    {
        /// <summary>
        /// 创建一条由 Tab、分组路径和可选世界 UUID 组成的稳定状态键。
        /// </summary>
        internal StatisticsExpansionStateKey(string tabKey, string groupPath, Guid? worldId)
        {
            if (string.IsNullOrEmpty(tabKey))
            {
                throw new ArgumentException("A tab key is required.", nameof(tabKey));
            }

            if (string.IsNullOrEmpty(groupPath))
            {
                throw new ArgumentException("A group path is required.", nameof(groupPath));
            }

            TabKey = tabKey;
            GroupPath = groupPath;
            WorldId = worldId;
        }

        internal string TabKey { get; }

        internal string GroupPath { get; }

        internal Guid? WorldId { get; }

        public bool Equals(StatisticsExpansionStateKey other)
        {
            return WorldId == other.WorldId
                && string.Equals(TabKey, other.TabKey, StringComparison.Ordinal)
                && string.Equals(GroupPath, other.GroupPath, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StatisticsExpansionStateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(WorldId, TabKey, GroupPath);
        }
    }

    /// <summary>
    /// 保存人物各 Tab 的分组展开状态；空世界标识代表人物生涯范围，其他状态按世界隔离。
    /// </summary>
    internal sealed class StatisticsExpansionStateStore
    {
        private readonly Dictionary<StatisticsExpansionStateKey, bool> entries =
            new Dictionary<StatisticsExpansionStateKey, bool>();

        internal IReadOnlyDictionary<StatisticsExpansionStateKey, bool> Entries => entries;

        /// <summary>
        /// 取得指定 Tab 与范围的全部分组状态，并返回与内部存储隔离的快照。
        /// </summary>
        internal IReadOnlyDictionary<string, bool> GetTabState(string tabKey, Guid? worldId)
        {
            Dictionary<string, bool> result = new Dictionary<string, bool>();
            foreach (KeyValuePair<StatisticsExpansionStateKey, bool> pair in entries)
            {
                if (pair.Key.WorldId == worldId
                    && string.Equals(pair.Key.TabKey, tabKey, StringComparison.Ordinal))
                {
                    result[pair.Key.GroupPath] = pair.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// 用完整快照替换指定 Tab 与范围的分组状态，其他 Tab 和世界保持不变。
        /// </summary>
        internal void ReplaceTabState(
            string tabKey,
            Guid? worldId,
            IReadOnlyDictionary<string, bool> states)
        {
            if (string.IsNullOrEmpty(tabKey))
            {
                throw new ArgumentException("A tab key is required.", nameof(tabKey));
            }

            List<StatisticsExpansionStateKey> keysToRemove =
                new List<StatisticsExpansionStateKey>();
            foreach (StatisticsExpansionStateKey key in entries.Keys)
            {
                if (key.WorldId == worldId
                    && string.Equals(key.TabKey, tabKey, StringComparison.Ordinal))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (StatisticsExpansionStateKey key in keysToRemove)
            {
                entries.Remove(key);
            }

            if (states == null)
            {
                return;
            }

            foreach (KeyValuePair<string, bool> pair in states)
            {
                if (!string.IsNullOrEmpty(pair.Key))
                {
                    entries[new StatisticsExpansionStateKey(tabKey, pair.Key, worldId)] = pair.Value;
                }
            }
        }

        /// <summary>
        /// 加入一条从人物存档恢复的有效状态；重复键以后读取到的值为准。
        /// </summary>
        internal void AddLoadedState(StatisticsExpansionStateKey key, bool expanded)
        {
            entries[key] = expanded;
        }
    }
}
