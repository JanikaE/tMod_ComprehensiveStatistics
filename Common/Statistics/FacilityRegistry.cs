using System;
using System.Collections.Generic;
using System.Globalization;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 设施页的固定一级分类；枚举顺序同时作为默认展示顺序。
    /// </summary>
    internal enum FacilityCategory
    {
        BuffStation,
        Furniture,
        Container,
        Pylon,
        Clock,
        Mechanism
    }

    /// <summary>
    /// 决定采集器用哪一种状态变化确认交互成功；它与 UI 分类相互独立。
    /// </summary>
    internal enum FacilityInteractionKind
    {
        BuffStation,
        Door,
        Container,
        Bed,
        Seat,
        Clock,
        Pylon,
        Mechanism
    }

    /// <summary>
    /// 描述一种逻辑设施。一个定义可以对应多个运行时物块类型，例如门的开、关状态。
    /// </summary>
    internal sealed class FacilityDefinition
    {
        internal FacilityDefinition(
            StableContentKey content,
            string style,
            FacilityCategory category,
            FacilityInteractionKind interactionKind,
            int iconItemType,
            int canonicalTileType,
            int canonicalStyle,
            int buffType,
            int buffDurationTicks)
        {
            Content = content;
            Style = style;
            Category = category;
            InteractionKind = interactionKind;
            IconItemType = iconItemType;
            CanonicalTileType = canonicalTileType;
            CanonicalStyle = canonicalStyle;
            BuffType = buffType;
            BuffDurationTicks = buffDurationTicks;
        }

        internal StableContentKey Content { get; }

        internal string Style { get; }

        internal FacilityCategory Category { get; }

        internal FacilityInteractionKind InteractionKind { get; }

        internal int IconItemType { get; }

        internal int CanonicalTileType { get; }

        internal int CanonicalStyle { get; }

        /// <summary>
        /// 增益站预期施加的 Buff；非增益设施为零。
        /// </summary>
        internal int BuffType { get; }

        /// <summary>
        /// 原版交互请求的 Buff 时长，以游戏 tick 为单位；非增益设施为零。
        /// </summary>
        internal int BuffDurationTicks { get; }
    }

    /// <summary>
    /// 一次命中的已登记设施，包含规范化定义、实际样式和多格物体左上原点。
    /// </summary>
    internal readonly struct ResolvedFacility
    {
        internal ResolvedFacility(
            FacilityDefinition definition,
            int observedTileType,
            int observedStyle,
            Point16 topLeft)
        {
            Definition = definition;
            ObservedTileType = observedTileType;
            ObservedStyle = observedStyle;
            TopLeft = topLeft;
        }

        internal FacilityDefinition Definition { get; }

        internal int ObservedTileType { get; }

        internal int ObservedStyle { get; }

        internal Point16 TopLeft { get; }
    }

    /// <summary>
    /// 原版设施的显式注册表。采集器只处理这里登记的类型与样式，避免把普通右键误报为设施交互。
    /// </summary>
    internal static class FacilityRegistry
    {
        private static readonly Dictionary<RuntimeFacilityKey, FacilityDefinition> runtimeDefinitions =
            new Dictionary<RuntimeFacilityKey, FacilityDefinition>();
        private static readonly Dictionary<PersistedFacilityKey, FacilityDefinition> persistedDefinitions =
            new Dictionary<PersistedFacilityKey, FacilityDefinition>();

        internal static void LoadVanillaDefinitions()
        {
            runtimeDefinitions.Clear();
            persistedDefinitions.Clear();

            // 原版右键逻辑中除蛋糕片为两分钟外，其余主动增益站均施加三十分钟 Buff。
            // 明确登记效果数据，采集器无需按物块 ID 再维护一份平行映射。
            RegisterVanillaBuffStation(ItemID.AmmoBox, BuffID.AmmoBox, 108000);
            RegisterVanillaBuffStation(ItemID.BewitchingTable, BuffID.Bewitched, 108000);
            RegisterVanillaBuffStation(ItemID.CrystalBall, BuffID.Clairvoyance, 108000);
            RegisterVanillaBuffStation(ItemID.SharpeningStation, BuffID.Sharpened, 108000);
            RegisterVanillaBuffStation(ItemID.WarTable, BuffID.WarTable, 108000);
            RegisterVanillaBuffStation(ItemID.SliceOfCake, BuffID.SugarRush, 7200);

            // 按放置物品穷举原版样式，以稳定的“物块内容键 + placeStyle”保存。
            // 开门、活板门和高门的开关状态都映射回关闭状态对应的同一逻辑设施。
            RegisterVanillaItemsForTile(
                TileID.ClosedDoor,
                FacilityCategory.Furniture,
                FacilityInteractionKind.Door,
                TileID.OpenDoor);
            RegisterVanillaItemsForTile(
                TileID.TrapdoorClosed,
                FacilityCategory.Furniture,
                FacilityInteractionKind.Door,
                TileID.TrapdoorOpen);
            RegisterVanillaItemsForTile(
                TileID.TallGateClosed,
                FacilityCategory.Furniture,
                FacilityInteractionKind.Door,
                TileID.TallGateOpen);
            RegisterVanillaItemsForTile(
                TileID.Beds,
                FacilityCategory.Furniture,
                FacilityInteractionKind.Bed);
            RegisterVanillaItemsForTile(
                TileID.Chairs,
                FacilityCategory.Furniture,
                FacilityInteractionKind.Seat);
            RegisterVanillaItemsForTile(
                TileID.Toilets,
                FacilityCategory.Furniture,
                FacilityInteractionKind.Seat);

            RegisterVanillaItemsForTile(
                TileID.Containers,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);
            RegisterVanillaItemsForTile(
                TileID.Containers2,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);
            RegisterVanillaItemsForTile(
                TileID.Dressers,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);
            RegisterVanillaItemsForTile(
                TileID.PiggyBank,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);
            RegisterVanillaItemsForTile(
                TileID.Safes,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);
            RegisterVanillaItemsForTile(
                TileID.DefendersForge,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);
            RegisterVanillaItemsForTile(
                TileID.VoidVault,
                FacilityCategory.Container,
                FacilityInteractionKind.Container);

            RegisterVanillaItemsForTile(
                TileID.GrandfatherClocks,
                FacilityCategory.Clock,
                FacilityInteractionKind.Clock);
            RegisterVanillaItemsForTile(
                TileID.TeleportationPylon,
                FacilityCategory.Pylon,
                FacilityInteractionKind.Pylon);

            // 只登记由玩家主动右键触发的机关。压力板走移动/弹幕触发路径，故意不进入注册表。
            RegisterVanillaItemsForTile(
                TileID.Switches,
                FacilityCategory.Mechanism,
                FacilityInteractionKind.Mechanism);
            RegisterVanillaItemsForTile(
                TileID.Lever,
                FacilityCategory.Mechanism,
                FacilityInteractionKind.Mechanism);
            RegisterVanillaItemsForTile(
                TileID.Timers,
                FacilityCategory.Mechanism,
                FacilityInteractionKind.Mechanism);
            RegisterVanillaItemsForTile(
                TileID.Detonator,
                FacilityCategory.Mechanism,
                FacilityInteractionKind.Mechanism);
        }

        internal static void Unload()
        {
            runtimeDefinitions.Clear();
            persistedDefinitions.Clear();
        }

        /// <summary>
        /// 将任意一个被点击的格子解析为已登记设施，同时归一到完整物体左上原点。
        /// </summary>
        internal static bool TryResolve(int i, int j, out ResolvedFacility facility)
        {
            facility = default;
            if (!WorldGen.InWorld(i, j))
            {
                return false;
            }

            Tile tile = Framing.GetTileSafely(i, j);
            if (!tile.HasTile)
            {
                return false;
            }

            int style = GetCanonicalTileStyle(tile);
            if (style < 0
                || !runtimeDefinitions.TryGetValue(
                    new RuntimeFacilityKey(tile.TileType, style),
                    out FacilityDefinition definition))
            {
                return false;
            }

            Point16 topLeft = TileObjectData.TopLeft(i, j);
            if (topLeft.X < 0 || topLeft.Y < 0)
            {
                topLeft = new Point16(i, j);
            }

            facility = new ResolvedFacility(definition, tile.TileType, style, topLeft);
            return true;
        }

        /// <summary>
        /// 将原版机关的开关动画帧排除在设施样式之外。
        /// TileObjectData 的通用算法会把这些状态帧解释成新样式，且墙壁开关没有可用的对象数据。
        /// </summary>
        internal static int GetCanonicalTileStyle(Tile tile)
        {
            switch (tile.TileType)
            {
                case TileID.Switches:
                case TileID.Lever:
                case TileID.Detonator:
                    // 这些原版机关各自只有一个可放置样式；帧变化仅表示开关状态。
                    return 0;
                case TileID.Timers:
                    // 五种计时器横向排列，frameY 只表示启用/关闭状态。
                    return Math.Max(tile.TileFrameX / 18, 0);
                default:
                    return TileObjectData.GetTileStyle(tile);
            }
        }

        /// <summary>
        /// 归一已经持久化的物体样式；旧版曾将单一样式机关的状态帧误存为不同样式。
        /// </summary>
        internal static int NormalizeTileObjectStyle(int tileType, int style)
        {
            return tileType switch
            {
                TileID.Switches or TileID.Lever or TileID.Detonator => 0,
                _ => style
            };
        }

        /// <summary>
        /// 从存档键恢复当前会话中的设施定义，供 UI 解析名称、图标与分类。
        /// </summary>
        internal static bool TryGetDefinition(
            StableContentKey content,
            string style,
            out FacilityDefinition definition)
        {
            return persistedDefinitions.TryGetValue(
                new PersistedFacilityKey(content, style),
                out definition);
        }

        /// <summary>
        /// 以放置物品为权威来源登记原版类型、样式、名称图标以及可选的状态物块类型。
        /// </summary>
        private static void RegisterVanillaItemsForTile(
            int canonicalTileType,
            FacilityCategory category,
            FacilityInteractionKind interactionKind,
            params int[] additionalTileTypes)
        {
            int registeredStyles = 0;
            HashSet<int> seenStyles = new HashSet<int>();
            for (int itemType = 1; itemType < ItemID.Count; itemType++)
            {
                if (!ContentSamples.ItemsByType.TryGetValue(itemType, out Item item)
                    || item == null
                    || item.IsAir
                    || item.createTile != canonicalTileType)
                {
                    continue;
                }

                int style = Math.Max(item.placeStyle, 0);
                if (!seenStyles.Add(style))
                {
                    // 少数等价放置物若共享同一物块样式，固定采用物品 ID 最小者作为图标。
                    continue;
                }

                RegisterVanillaItem(
                    itemType,
                    category,
                    interactionKind,
                    buffType: 0,
                    buffDurationTicks: 0,
                    additionalTileTypes);
                registeredStyles++;
            }

            if (registeredStyles == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot register vanilla facility tile {canonicalTileType}: no placing items were found.");
            }
        }

        private static void RegisterVanillaBuffStation(
            int itemType,
            int buffType,
            int buffDurationTicks)
        {
            if (buffType <= 0 || buffDurationTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buffType),
                    "Buff stations require a positive buff type and duration.");
            }

            RegisterVanillaItem(
                itemType,
                FacilityCategory.BuffStation,
                FacilityInteractionKind.BuffStation,
                buffType,
                buffDurationTicks,
                Array.Empty<int>());
        }

        private static void RegisterVanillaItem(
            int itemType,
            FacilityCategory category,
            FacilityInteractionKind interactionKind,
            int buffType,
            int buffDurationTicks,
            params int[] additionalTileTypes)
        {
            if (!ContentSamples.ItemsByType.TryGetValue(itemType, out Item item)
                || item == null
                || item.IsAir
                || item.createTile < TileID.Dirt
                || !StableContentKey.TryCreateForTileType(
                    item.createTile,
                    out StableContentKey content))
            {
                throw new InvalidOperationException(
                    $"Cannot register vanilla facility item {itemType}: it has no valid placeable tile.");
            }

            int canonicalStyle = Math.Max(item.placeStyle, 0);
            string persistedStyle = canonicalStyle.ToString(CultureInfo.InvariantCulture);
            FacilityDefinition definition = new FacilityDefinition(
                content,
                persistedStyle,
                category,
                interactionKind,
                itemType,
                item.createTile,
                canonicalStyle,
                buffType,
                buffDurationTicks);

            PersistedFacilityKey persistedKey = new PersistedFacilityKey(content, persistedStyle);
            if (!persistedDefinitions.TryAdd(persistedKey, definition))
            {
                throw new InvalidOperationException(
                    $"Duplicate persisted facility registration: {content}#{persistedStyle}.");
            }

            RegisterRuntimeTile(item.createTile, canonicalStyle, definition);
            if (additionalTileTypes == null)
            {
                return;
            }

            foreach (int tileType in additionalTileTypes)
            {
                RegisterRuntimeTile(tileType, canonicalStyle, definition);
            }
        }

        private static void RegisterRuntimeTile(
            int tileType,
            int style,
            FacilityDefinition definition)
        {
            RuntimeFacilityKey key = new RuntimeFacilityKey(tileType, style);
            if (!runtimeDefinitions.TryAdd(key, definition))
            {
                throw new InvalidOperationException(
                    $"Duplicate runtime facility registration: tile {tileType}, style {style}.");
            }
        }

        private readonly struct RuntimeFacilityKey : IEquatable<RuntimeFacilityKey>
        {
            internal RuntimeFacilityKey(int tileType, int style)
            {
                TileType = tileType;
                Style = style;
            }

            private int TileType { get; }

            private int Style { get; }

            public bool Equals(RuntimeFacilityKey other)
            {
                return TileType == other.TileType && Style == other.Style;
            }

            public override bool Equals(object obj)
            {
                return obj is RuntimeFacilityKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(TileType, Style);
            }
        }

        private readonly struct PersistedFacilityKey : IEquatable<PersistedFacilityKey>
        {
            internal PersistedFacilityKey(StableContentKey content, string style)
            {
                Content = content;
                Style = style ?? string.Empty;
            }

            private StableContentKey Content { get; }

            private string Style { get; }

            public bool Equals(PersistedFacilityKey other)
            {
                return Content.Equals(other.Content)
                    && string.Equals(Style, other.Style, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is PersistedFacilityKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    Content,
                    Style == null ? 0 : StringComparer.Ordinal.GetHashCode(Style));
            }
        }
    }

    /// <summary>
    /// 在所有内容样本就绪后创建设施注册表，并在重载时清理静态引用。
    /// </summary>
    internal sealed class FacilityRegistrySystem : ModSystem
    {
        public override void PostSetupContent()
        {
            FacilityRegistry.LoadVanillaDefinitions();
        }

        public override void Unload()
        {
            FacilityRegistry.Unload();
        }
    }
}
