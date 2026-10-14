using System;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 保存一项统计的事件数、单位数与累计数值；三种口径保持独立，避免把次数误作数量。
    /// </summary>
    internal readonly struct StatisticMetrics : IEquatable<StatisticMetrics>
    {
        /// <summary>
        /// 创建一组非负的统计指标。
        /// </summary>
        internal StatisticMetrics(long events = 0L, long units = 0L, long amount = 0L)
        {
            if (events < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(events));
            }

            if (units < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(units));
            }

            if (amount < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            Events = events;
            Units = units;
            Amount = amount;
        }

        internal long Events { get; }

        internal long Units { get; }

        internal long Amount { get; }

        internal bool IsZero => Events == 0L && Units == 0L && Amount == 0L;

        /// <summary>
        /// 将另一组指标逐项累加，并在整数上限处饱和。
        /// </summary>
        internal StatisticMetrics Add(StatisticMetrics other)
        {
            // 极端情况下饱和到 long.MaxValue，统计累计不应因整数溢出而回绕或令游戏崩溃。
            return new StatisticMetrics(
                SaturatingAdd(Events, other.Events),
                SaturatingAdd(Units, other.Units),
                SaturatingAdd(Amount, other.Amount));
        }

        /// <summary>
        /// 判断两组指标的三个统计口径是否完全相同。
        /// </summary>
        public bool Equals(StatisticMetrics other)
        {
            return Events == other.Events && Units == other.Units && Amount == other.Amount;
        }

        public override bool Equals(object obj)
        {
            return obj is StatisticMetrics other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Events, Units, Amount);
        }

        /// <summary>
        /// 安全累加两个非负整数，避免长期统计发生溢出回绕。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }
    }
}
