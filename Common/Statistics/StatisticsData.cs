using System;
using System.Collections.Generic;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 定义当前统计存档格式版本；格式发生不兼容变化时必须递增此值。
    /// </summary>
    internal static class StatisticsDataFormat
    {
        internal const int CurrentVersion = 1;
    }

    /// <summary>
    /// 保存单个查看范围的元数据与稀疏统计条目。
    /// </summary>
    internal sealed class StatisticsData
    {
        private readonly Dictionary<StatisticKey, StatisticMetrics> entries =
            new Dictionary<StatisticKey, StatisticMetrics>();

        internal StatisticsData(DateTimeOffset startedAtUtc)
            : this(StatisticsDataFormat.CurrentVersion, startedAtUtc)
        {
        }

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

        internal void Add(StatisticKey key, StatisticMetrics delta)
        {
            // 零值不进入字典，保证后续 B2 序列化天然保持稀疏。
            if (delta.IsZero)
            {
                return;
            }

            entries.TryGetValue(key, out StatisticMetrics current);
            entries[key] = current.Add(delta);
        }

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

        internal CharacterStatisticsData(DateTimeOffset startedAtUtc)
        {
            Career = new StatisticsData(startedAtUtc);
        }

        internal CharacterStatisticsData(
            StatisticsData career,
            IDictionary<Guid, StatisticsData> loadedWorldSlices)
        {
            Career = career ?? throw new ArgumentNullException(nameof(career));

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

        internal StatisticsData GetOrCreateWorldSlice(Guid worldId, DateTimeOffset startedAtUtc)
        {
            if (!worldSlices.TryGetValue(worldId, out StatisticsData worldSlice))
            {
                worldSlice = new StatisticsData(startedAtUtc);
                worldSlices.Add(worldId, worldSlice);
            }

            return worldSlice;
        }

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
    }
}
