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
    /// NPC 对话按钮的稳定功能分类；枚举名称会写入存档，已有成员不可重命名。
    /// </summary>
    internal enum NpcChatButtonKind
    {
        ShopFirst,
        QuestFirst,
        HelpFirst,
        HealingFirst,
        TaxCollectionFirst,
        CurseFirst,
        CustomFirst,
        ShopSecond,
        WorldStatusOrPurifySecond,
        CraftingSecond,
        ReforgeSecond,
        HairStyleSecond,
        DyeTradeSecond,
        MusicSecond,
        TavernkeepHelpSecond,
        CustomSecond
    }

    /// <summary>
    /// 成功渔获所在的液体类型；枚举名称写入明细键，已有成员不可重命名。
    /// </summary>
    internal enum FishingLiquidKind
    {
        Water,
        Honey,
        Lava
    }

    /// <summary>
    /// 集中定义正式统计键，使采集、存档和 UI 使用同一稳定标识。
    /// </summary>
    internal static class StatisticKeys
    {
        internal const string ItemPickupStatisticId = "items/pickups";
        internal const string ItemUseStatisticId = "items/uses";
        internal const string ItemDiscardStatisticId = "items/discards";
        internal const string ItemPurchaseStatisticId = "economy/purchases";
        internal const string SpecialCurrencyPurchaseStatisticId =
            "economy/purchases/special-currency";
        internal const string ItemSaleStatisticId = "economy/sales";
        internal const string NurseHealingStatisticId = "economy/nurse-healing";
        internal const string ItemReforgeStatisticId = "economy/reforges";
        internal const string NpcConversationStatisticId = "npcs/conversations";
        internal const string NpcChatButtonStatisticId = "npcs/chat-buttons";
        internal const string NpcFinalKillStatisticId = "npcs/final-kills";
        internal const string NpcAssistStatisticId = "npcs/assists";
        internal const string NpcWorldDeathStatisticId = "npcs/world-deaths";
        internal const string FishingItemCatchStatisticId = "fishing/catches/items";
        internal const string FishingNpcCatchStatisticId = "fishing/catches/npcs";
        internal const string FacilityInteractionStatisticId = "interactions/facilities";

        internal static readonly StatisticKey FishingSuccessfulCatches =
            new StatisticKey("fishing/successful-catches");

        internal static readonly StatisticKey FishingCasts =
            new StatisticKey("fishing/casts");

        internal static readonly StatisticKey FishingSuccessfulCasts =
            new StatisticKey("fishing/results/successful");

        internal static readonly StatisticKey FishingEmptyReels =
            new StatisticKey("fishing/results/empty-reels");

        internal static readonly StatisticKey FishingInterruptedCasts =
            new StatisticKey("fishing/results/interrupted");

        private static readonly StatisticKey[] FishingLiquidCatchKeys =
        {
            new StatisticKey("fishing/catches/liquid/water"),
            new StatisticKey("fishing/catches/liquid/honey"),
            new StatisticKey("fishing/catches/liquid/lava")
        };

        internal static readonly StatisticKey NurseHealingWithDebuffsRemoved =
            new StatisticKey(NurseHealingStatisticId, style: "debuffs-removed");

        internal static readonly StatisticKey NurseHealingWithoutDebuffsRemoved =
            new StatisticKey(NurseHealingStatisticId, style: "no-debuffs-removed");

        internal static readonly StatisticKey TaxCollectorCollections =
            new StatisticKey("economy/tax-collector-collections");

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
        /// 获取指定物品与商人组合对应的买入统计键。
        /// </summary>
        internal static StatisticKey GetItemPurchaseKey(
            StableContentKey itemContent,
            StableContentKey merchantContent)
        {
            // StatisticKey 的 Content 保存表格叶子项物品，Style 保存第二个稳定维度商人；
            // 两者都不依赖运行时数字 ID，模组加载顺序改变后仍可恢复。
            return new StatisticKey(
                ItemPurchaseStatisticId,
                itemContent,
                merchantContent.ToString());
        }

        /// <summary>
        /// 获取物品、商人和特殊货币组合对应的买入统计键。
        /// </summary>
        internal static StatisticKey GetSpecialCurrencyItemPurchaseKey(
            StableContentKey itemContent,
            StableContentKey merchantContent,
            StableContentKey currencyContent)
        {
            return new StatisticKey(
                SpecialCurrencyPurchaseStatisticId,
                itemContent,
                $"{merchantContent}|{currencyContent}");
        }

        /// <summary>
        /// 从特殊货币买入统计的样式维度恢复商人与货币代表物品。
        /// </summary>
        internal static bool TryParseSpecialCurrencyPurchaseStyle(
            string style,
            out StableContentKey merchantContent,
            out StableContentKey currencyContent)
        {
            int separatorIndex = style?.IndexOf('|') ?? -1;
            if (separatorIndex <= 0 || separatorIndex >= style.Length - 1)
            {
                merchantContent = default;
                currencyContent = default;
                return false;
            }

            bool merchantParsed = StableContentKey.TryParse(
                style.Substring(0, separatorIndex),
                out merchantContent);
            bool currencyParsed = StableContentKey.TryParse(
                style.Substring(separatorIndex + 1),
                out currencyContent);
            return merchantParsed && currencyParsed;
        }

        /// <summary>
        /// 获取指定物品与商人组合对应的卖出统计键。
        /// </summary>
        internal static StatisticKey GetItemSaleKey(
            StableContentKey itemContent,
            StableContentKey merchantContent)
        {
            return new StatisticKey(
                ItemSaleStatisticId,
                itemContent,
                merchantContent.ToString());
        }

        /// <summary>
        /// 获取一次护士治疗按是否移除 Debuff 分类后的统计键。
        /// </summary>
        internal static StatisticKey GetNurseHealingKey(bool removedDebuffs)
        {
            return removedDebuffs
                ? NurseHealingWithDebuffsRemoved
                : NurseHealingWithoutDebuffsRemoved;
        }

        /// <summary>
        /// 获取指定物品对应的成功重铸统计键。
        /// </summary>
        internal static StatisticKey GetItemReforgeKey(StableContentKey itemContent)
        {
            return new StatisticKey(ItemReforgeStatisticId, itemContent);
        }

        /// <summary>
        /// 获取指定 NPC 对应的开始对话次数统计键。
        /// </summary>
        internal static StatisticKey GetNpcConversationKey(StableContentKey npcContent)
        {
            return new StatisticKey(NpcConversationStatisticId, npcContent);
        }

        /// <summary>
        /// 获取指定 NPC 与按钮功能组合对应的点击次数统计键。
        /// </summary>
        internal static StatisticKey GetNpcChatButtonKey(
            StableContentKey npcContent,
            NpcChatButtonKind buttonKind)
        {
            return new StatisticKey(
                NpcChatButtonStatisticId,
                npcContent,
                buttonKind.ToString());
        }

        /// <summary>
        /// 获取指定 NPC 主体对应的角色最终击杀统计键。
        /// </summary>
        internal static StatisticKey GetNpcFinalKillKey(StableContentKey npcContent)
        {
            return new StatisticKey(NpcFinalKillStatisticId, npcContent);
        }

        /// <summary>
        /// 获取指定 NPC 主体对应的角色协助击杀统计键。
        /// </summary>
        internal static StatisticKey GetNpcAssistKey(StableContentKey npcContent)
        {
            return new StatisticKey(NpcAssistStatisticId, npcContent);
        }

        /// <summary>
        /// 获取指定 NPC 主体对应的当前世界实际死亡统计键。
        /// </summary>
        internal static StatisticKey GetNpcWorldDeathKey(StableContentKey npcContent)
        {
            return new StatisticKey(NpcWorldDeathStatisticId, npcContent);
        }

        /// <summary>
        /// 获取指定物品及其来源液体对应的成功物品渔获统计键。
        /// </summary>
        internal static StatisticKey GetFishingItemCatchKey(
            StableContentKey itemContent,
            FishingLiquidKind liquid)
        {
            return new StatisticKey(
                FishingItemCatchStatisticId,
                itemContent,
                liquid.ToString());
        }

        /// <summary>
        /// 获取指定 NPC 及其来源液体对应的成功 NPC 渔获统计键。
        /// </summary>
        internal static StatisticKey GetFishingNpcCatchKey(
            StableContentKey npcContent,
            FishingLiquidKind liquid)
        {
            return new StatisticKey(
                FishingNpcCatchStatisticId,
                npcContent,
                liquid.ToString());
        }

        /// <summary>
        /// 获取指定液体类型对应的成功渔获统计键。
        /// </summary>
        internal static StatisticKey GetFishingLiquidCatchKey(FishingLiquidKind liquid)
        {
            return FishingLiquidCatchKeys[(int)liquid];
        }

        /// <summary>
        /// 获取指定规范化物块内容与样式对应的设施交互统计键。
        /// </summary>
        internal static StatisticKey GetFacilityInteractionKey(FacilityDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return new StatisticKey(
                $"{FacilityInteractionStatisticId}/{definition.Category}",
                definition.Content,
                definition.Style);
        }

        /// <summary>
        /// 从设施交互统计标识恢复一级分类；分类写入标识后即使定义暂时缺失也能正确分组。
        /// </summary>
        internal static bool TryParseFacilityCategory(
            string statisticId,
            out FacilityCategory category)
        {
            string prefix = FacilityInteractionStatisticId + "/";
            if (statisticId == null
                || !statisticId.StartsWith(prefix, StringComparison.Ordinal)
                || !Enum.TryParse(
                    statisticId.Substring(prefix.Length),
                    ignoreCase: false,
                    out category)
                || !Enum.IsDefined(typeof(FacilityCategory), category))
            {
                category = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 从渔获明细键的样式维度恢复来源液体。
        /// </summary>
        internal static bool TryParseFishingLiquidKind(
            string style,
            out FishingLiquidKind liquid)
        {
            return Enum.TryParse(style, ignoreCase: false, out liquid)
                && Enum.IsDefined(typeof(FishingLiquidKind), liquid);
        }

        /// <summary>
        /// 从存档样式维度恢复 NPC 对话按钮的稳定功能分类。
        /// </summary>
        internal static bool TryParseNpcChatButtonKind(
            string style,
            out NpcChatButtonKind buttonKind)
        {
            return Enum.TryParse(style, ignoreCase: false, out buttonKind)
                && Enum.IsDefined(typeof(NpcChatButtonKind), buttonKind);
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
