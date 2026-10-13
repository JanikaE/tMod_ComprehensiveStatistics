using System;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 集中定义正式统计键，使采集、存档和 UI 使用同一稳定标识。
    /// </summary>
    internal static class StatisticKeys
    {
        internal static readonly StatisticKey PlayerDeaths =
            new StatisticKey("survival/player-deaths");
    }

    /// <summary>
    /// 集中保存开发阶段使用的临时统计键，避免测试项与正式统计键发生碰撞。
    /// </summary>
    internal static class InternalStatisticKeys
    {
        // B2 验收完成后暂时保留到发布前整理；正式发布时必须移除该测试项及其 UI。
        internal static readonly StatisticKey B2WorldEntries =
            new StatisticKey("internal/b2-world-entries");
    }

    /// <summary>
    /// 唯一标识一项统计；可选内容键与样式键用于区分内容明细和多格物体样式。
    /// </summary>
    internal readonly struct StatisticKey : IEquatable<StatisticKey>
    {
        internal StatisticKey(
            string statisticId,
            StableContentKey? content = null,
            string style = null)
        {
            if (string.IsNullOrWhiteSpace(statisticId))
            {
                throw new ArgumentException("Statistic ID cannot be empty.", nameof(statisticId));
            }

            StatisticId = statisticId;
            Content = content;
            Style = style ?? string.Empty;
        }

        internal string StatisticId { get; }

        internal StableContentKey? Content { get; }

        internal string Style { get; }

        public bool Equals(StatisticKey other)
        {
            return string.Equals(StatisticId, other.StatisticId, StringComparison.Ordinal)
                && Nullable.Equals(Content, other.Content)
                && string.Equals(Style, other.Style, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StatisticKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StatisticId == null ? 0 : StringComparer.Ordinal.GetHashCode(StatisticId),
                Content,
                Style == null ? 0 : StringComparer.Ordinal.GetHashCode(Style));
        }

        public override string ToString()
        {
            string key = Content.HasValue
                ? $"{StatisticId}:{Content.Value}"
                : StatisticId;
            return !string.IsNullOrEmpty(Style) ? $"{key}#{Style}" : key;
        }

        public static bool operator ==(StatisticKey left, StatisticKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StatisticKey left, StatisticKey right)
        {
            return !left.Equals(right);
        }
    }
}
