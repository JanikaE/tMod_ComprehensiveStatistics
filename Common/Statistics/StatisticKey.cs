using System;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 直接伤害的互斥主类别；枚举顺序同时作为生存页的固定展示顺序。
    /// </summary>
    internal enum DirectDamageSourceKind
    {
        NpcContact,
        HostileProjectile,
        PvP,
        Fall,
        Drowning,
        Lava,
        TrapOrTile,
        Other
    }

    /// <summary>
    /// 不经过普通受伤回调的生命减少来源；单一 Debuff 的具体类型由稳定内容键保存。
    /// </summary>
    internal enum ContinuousLifeLossSourceKind
    {
        SingleDebuff,
        MultipleDebuffs,
        Unknown
    }

    /// <summary>
    /// 互斥移动类别；枚举顺序与移动页定义顺序一致，实际归因优先级由采集器明确执行。
    /// </summary>
    internal enum MovementModeKind
    {
        Minecart,
        OtherMount,
        RopeOrPulley,
        GrapplingHook,
        Flying,
        Jumping,
        Falling,
        Walking,
        Other
    }

    /// <summary>
    /// 集中定义正式统计键，使采集、存档和 UI 使用同一稳定标识。
    /// </summary>
    internal static class StatisticKeys
    {
        internal const string ItemPickupStatisticId = "items/pickups";
        internal const string ItemUseStatisticId = "items/uses";
        internal const string ItemDiscardStatisticId = "items/discards";

        internal static readonly StatisticKey TotalMovementDistance =
            new StatisticKey("movement/total-distance");

        private static readonly StatisticKey[] MovementModeDistanceKeys =
        {
            new StatisticKey("movement/distance/minecart"),
            new StatisticKey("movement/distance/other-mount"),
            new StatisticKey("movement/distance/rope-or-pulley"),
            new StatisticKey("movement/distance/grappling-hook"),
            new StatisticKey("movement/distance/flying"),
            new StatisticKey("movement/distance/jumping"),
            new StatisticKey("movement/distance/falling"),
            new StatisticKey("movement/distance/walking"),
            new StatisticKey("movement/distance/other")
        };

        internal static readonly StatisticKey PlayerDeaths =
            new StatisticKey("survival/player-deaths");

        internal static readonly StatisticKey MultipleContinuousDamageDeaths =
            new StatisticKey("survival/player-deaths/continuous/multiple-debuffs");

        internal static readonly StatisticKey UnknownContinuousLifeLossDeaths =
            new StatisticKey("survival/player-deaths/continuous/unknown");

        internal static readonly StatisticKey UnknownDeathSource =
            new StatisticKey("survival/player-deaths/unknown");

        internal static readonly StatisticKey DirectLifeLost =
            new StatisticKey("survival/direct-life-lost");

        internal static readonly StatisticKey MultipleContinuousDamageEffects =
            new StatisticKey("survival/continuous-life-lost/multiple-debuffs");

        internal static readonly StatisticKey UnknownContinuousLifeLoss =
            new StatisticKey("survival/continuous-life-lost/unknown");

        internal const string ContinuousDamageDebuffStatisticId =
            "survival/continuous-life-lost/debuff";

        internal const string ContinuousDamageDebuffDeathStatisticId =
            "survival/player-deaths/continuous/debuff";

        /// <summary>
        /// 获取指定物品对应的拾取统计键。
        /// </summary>
        internal static StatisticKey GetItemPickupKey(StableContentKey content)
        {
            return new StatisticKey(ItemPickupStatisticId, content);
        }

        /// <summary>
        /// 获取指定物品对应的使用次数统计键。
        /// </summary>
        internal static StatisticKey GetItemUseKey(StableContentKey content)
        {
            return new StatisticKey(ItemUseStatisticId, content);
        }

        /// <summary>
        /// 获取指定物品对应的彻底丢弃统计键。
        /// </summary>
        internal static StatisticKey GetItemDiscardKey(StableContentKey content)
        {
            return new StatisticKey(ItemDiscardStatisticId, content);
        }

        /// <summary>
        /// 获取基础移动类别对应的距离统计键。
        /// </summary>
        internal static StatisticKey GetMovementModeDistanceKey(MovementModeKind mode)
        {
            return MovementModeDistanceKeys[(int)mode];
        }

        /// <summary>
        /// 获取指定移动类别与来源物品对应的距离明细键。
        /// </summary>
        internal static StatisticKey GetMovementModeDistanceKey(
            MovementModeKind mode,
            StableContentKey content)
        {
            return new StatisticKey(
                MovementModeDistanceKeys[(int)mode].StatisticId,
                content);
        }

        private static readonly StatisticKey[] DirectDamageSourceKeys =
        {
            new StatisticKey("survival/direct-life-lost/npc-contact"),
            new StatisticKey("survival/direct-life-lost/hostile-projectile"),
            new StatisticKey("survival/direct-life-lost/pvp"),
            new StatisticKey("survival/direct-life-lost/fall"),
            new StatisticKey("survival/direct-life-lost/drowning"),
            new StatisticKey("survival/direct-life-lost/lava"),
            new StatisticKey("survival/direct-life-lost/trap-or-tile"),
            new StatisticKey("survival/direct-life-lost/other")
        };

        private static readonly StatisticKey[] DirectDeathSourceKeys =
        {
            new StatisticKey("survival/player-deaths/direct/npc-contact"),
            new StatisticKey("survival/player-deaths/direct/hostile-projectile"),
            new StatisticKey("survival/player-deaths/direct/pvp"),
            new StatisticKey("survival/player-deaths/direct/fall"),
            new StatisticKey("survival/player-deaths/direct/drowning"),
            new StatisticKey("survival/player-deaths/direct/lava"),
            new StatisticKey("survival/player-deaths/direct/trap-or-tile"),
            new StatisticKey("survival/player-deaths/direct/other")
        };

        /// <summary>
        /// 获取直接生命损失主类别对应的统计键。
        /// </summary>
        internal static StatisticKey GetDirectDamageSourceKey(DirectDamageSourceKind source)
        {
            return DirectDamageSourceKeys[(int)source];
        }

        /// <summary>
        /// 获取指定 NPC 接触伤害明细的统计键。
        /// </summary>
        internal static StatisticKey GetNpcDirectDamageKey(StableContentKey content)
        {
            return new StatisticKey(
                DirectDamageSourceKeys[(int)DirectDamageSourceKind.NpcContact].StatisticId,
                content);
        }

        /// <summary>
        /// 获取直接伤害死亡主类别对应的统计键。
        /// </summary>
        internal static StatisticKey GetDirectDeathSourceKey(DirectDamageSourceKind source)
        {
            return DirectDeathSourceKeys[(int)source];
        }

        /// <summary>
        /// 获取指定 NPC 接触致死明细的统计键。
        /// </summary>
        internal static StatisticKey GetNpcDirectDeathKey(StableContentKey content)
        {
            return new StatisticKey(
                DirectDeathSourceKeys[(int)DirectDamageSourceKind.NpcContact].StatisticId,
                content);
        }

        /// <summary>
        /// 获取指定持续伤害 Debuff 的生命损失统计键。
        /// </summary>
        internal static StatisticKey GetContinuousDamageDebuffKey(StableContentKey content)
        {
            return new StatisticKey(ContinuousDamageDebuffStatisticId, content);
        }

        /// <summary>
        /// 获取指定持续伤害 Debuff 的死亡统计键。
        /// </summary>
        internal static StatisticKey GetContinuousDamageDebuffDeathKey(
            StableContentKey content)
        {
            return new StatisticKey(ContinuousDamageDebuffDeathStatisticId, content);
        }
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
        /// <summary>
        /// 创建由统计标识、可选内容键和样式键组成的稳定统计键。
        /// </summary>
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

        /// <summary>
        /// 按序比较统计键的所有稳定组成部分。
        /// </summary>
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

        /// <summary>
        /// 判断两个统计键是否相等。
        /// </summary>
        public static bool operator ==(StatisticKey left, StatisticKey right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// 判断两个统计键是否不相等。
        /// </summary>
        public static bool operator !=(StatisticKey left, StatisticKey right)
        {
            return !left.Equals(right);
        }
    }
}
