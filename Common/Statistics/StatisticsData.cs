using System;
using System.Collections.Generic;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 定义当前统计存档及统计口径版本；按设计在新增或改变统计含义时递增。
    /// </summary>
    internal static class StatisticsDataFormat
    {
        internal const int CurrentVersion = 4;

        /// <summary>版本 4 新增整数液体体积键；版本 1/2/3 原字段与累计原样保留，不倒推历史液体或拆除。</summary>
        internal static bool CanLoad(int version) => version >= 1 && version <= CurrentVersion;
    }

    /// <summary>
    /// 保存单个查看范围的元数据与稀疏统计条目。
    /// </summary>
    internal sealed class StatisticsData
    {
        private readonly Dictionary<StatisticKey, StatisticMetrics> entries =
            new Dictionary<StatisticKey, StatisticMetrics>();

        /// <summary>
        /// 使用当前存档格式创建空统计范围。
        /// </summary>
        internal StatisticsData(DateTimeOffset startedAtUtc)
            : this(StatisticsDataFormat.CurrentVersion, startedAtUtc)
        {
        }

        /// <summary>
        /// 使用指定格式版本和统计起始时间创建统计范围。
        /// </summary>
        internal StatisticsData(int formatVersion, DateTimeOffset startedAtUtc)
        {
            if (formatVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(formatVersion));
            }

            FormatVersion = formatVersion;
            StartedAtUtc = startedAtUtc.ToUniversalTime();
        }

        internal int FormatVersion { get; }

        internal DateTimeOffset StartedAtUtc { get; }

        internal IReadOnlyDictionary<StatisticKey, StatisticMetrics> Entries => entries;

        /// <summary>
        /// 将非零增量安全地累加到指定统计项。
        /// </summary>
        internal void Add(StatisticKey key, StatisticMetrics delta)
        {
            // 零值不进入字典，保证长期存档天然保持稀疏。
            if (delta.IsZero)
            {
                return;
            }

            entries.TryGetValue(key, out StatisticMetrics current);
            entries[key] = current.Add(delta);
        }

        /// <summary>
        /// 从指定统计项扣除一次可逆操作，并在归零后恢复稀疏存储。
        /// </summary>
        internal void Subtract(StatisticKey key, StatisticMetrics delta)
        {
            if (delta.IsZero || !entries.TryGetValue(key, out StatisticMetrics current))
            {
                return;
            }

            StatisticMetrics updated = current.Subtract(delta);
            if (updated.IsZero)
            {
                entries.Remove(key);
            }
            else
            {
                entries[key] = updated;
            }
        }

        /// <summary>
        /// 获取指定统计项；未记录的键返回零值。
        /// </summary>
        internal StatisticMetrics GetValue(StatisticKey key)
        {
            return entries.TryGetValue(key, out StatisticMetrics value)
                ? value
                : default;
        }
    }

    /// <summary>
    /// 持有一个角色的生涯数据和按世界唯一标识隔离的数据切片。
    /// </summary>
    internal sealed class CharacterStatisticsData
    {
        private readonly Dictionary<Guid, StatisticsData> worldSlices =
            new Dictionary<Guid, StatisticsData>();

        /// <summary>
        /// 创建包含空角色生涯范围的统计数据。
        /// </summary>
        internal CharacterStatisticsData(DateTimeOffset startedAtUtc)
        {
            Career = new StatisticsData(startedAtUtc);
            ExpansionStates = new StatisticsExpansionStateStore();
        }

        /// <summary>
        /// 从已加载的角色生涯和世界切片恢复统计数据。
        /// </summary>
        internal CharacterStatisticsData(
            StatisticsData career,
            IDictionary<Guid, StatisticsData> loadedWorldSlices,
            StatisticsExpansionStateStore expansionStates = null)
        {
            Career = career ?? throw new ArgumentNullException(nameof(career));
            ExpansionStates = expansionStates ?? new StatisticsExpansionStateStore();

            if (loadedWorldSlices == null)
            {
                throw new ArgumentNullException(nameof(loadedWorldSlices));
            }

            foreach (KeyValuePair<Guid, StatisticsData> pair in loadedWorldSlices)
            {
                if (pair.Value != null)
                {
                    worldSlices[pair.Key] = pair.Value;
                }
            }
        }

        internal StatisticsData Career { get; }

        internal IReadOnlyDictionary<Guid, StatisticsData> WorldSlices => worldSlices;

        internal StatisticsExpansionStateStore ExpansionStates { get; }

        internal StatisticsListViewStateStore ListViewStates { get; } = new();

        internal Dictionary<string, string> SelectedSubTabs { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 获取指定世界切片；不存在时以当前时间创建。
        /// </summary>
        internal StatisticsData GetOrCreateWorldSlice(Guid worldId, DateTimeOffset startedAtUtc)
        {
            if (!worldSlices.TryGetValue(worldId, out StatisticsData worldSlice))
            {
                worldSlice = new StatisticsData(startedAtUtc);
                worldSlices.Add(worldId, worldSlice);
            }

            return worldSlice;
        }

        /// <summary>
        /// 将一次增量同时写入角色生涯和当前世界两个范围。
        /// </summary>
        internal void Add(
            Guid worldId,
            StatisticKey key,
            StatisticMetrics delta,
            DateTimeOffset startedAtUtc)
        {
            // 同一玩家行为的两份归属在一个入口提交，采集器不能漏写其中一个范围。
            Career.Add(key, delta);
            GetOrCreateWorldSlice(worldId, startedAtUtc).Add(key, delta);
        }

        /// <summary>
        /// 将一个已确认液体步骤的两个方向分别写入双范围，保留原始整数精度。
        /// 本入口不判断来源；调用方必须先确认玩家归属、世界会话与直接操作证据。
        /// </summary>
        internal void AddLiquidChange(Guid worldId, LiquidChange change, DateTimeOffset startedAtUtc)
        {
            // 只有实际非零方向才能进入 Add，空步骤不能创建世界切片或改变开始时间。
            if (change.RemovedUnits > 0)
            {
                Add(worldId, StatisticKeys.GetRemovedLiquidKey(change.RemovedKind),
                    new StatisticMetrics(units: change.RemovedUnits), startedAtUtc);
            }
            if (change.PlacedUnits > 0)
            {
                Add(worldId, StatisticKeys.GetPlacedLiquidKey(change.PlacedKind),
                    new StatisticMetrics(units: change.PlacedUnits), startedAtUtc);
            }
        }

        /// <summary>
        /// 将可逆操作同时从角色生涯和当前世界切片扣除。
        /// </summary>
        internal void Subtract(
            Guid worldId,
            StatisticKey key,
            StatisticMetrics delta,
            DateTimeOffset startedAtUtc)
        {
            Career.Subtract(key, delta);
            GetOrCreateWorldSlice(worldId, startedAtUtc).Subtract(key, delta);
        }
    }
}
