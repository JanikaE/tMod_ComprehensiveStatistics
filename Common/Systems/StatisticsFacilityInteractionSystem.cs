using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 在原版设施逻辑前后比较与设施对应的状态，只记录实际产生效果的交互。
    /// </summary>
    internal sealed class StatisticsFacilityInteractionSystem : ModSystem
    {
        public override void Load()
        {
            On_Player.TileInteractionsUse += HandleTileInteractionsUse;
        }

        public override void Unload()
        {
            On_Player.TileInteractionsUse -= HandleTileInteractionsUse;
        }

        private static void HandleTileInteractionsUse(
            On_Player.orig_TileInteractionsUse orig,
            Player player,
            int tileX,
            int tileY)
        {
            ResolvedFacility facility = default;
            bool observeInteraction = Main.netMode != NetmodeID.Server
                && player?.active == true
                && player.whoAmI == Main.myPlayer
                && player.tileInteractAttempted
                && FacilityRegistry.TryResolve(tileX, tileY, out facility)
                && CanObserve(facility.Definition);
            FacilityInteractionSnapshot before = observeInteraction
                ? FacilityInteractionSnapshot.Capture(player, facility, tileX, tileY)
                : default;

            orig(player, tileX, tileY);

            if (!observeInteraction
                || !before.HasSuccessfulChange(player, facility, tileX, tileY))
            {
                return;
            }

            player
                .GetModPlayer<StatisticsPlayer>()
                .RecordFacilityInteraction(facility.Definition);
        }

        private static bool CanObserve(FacilityDefinition definition)
        {
            switch (definition.InteractionKind)
            {
                case FacilityInteractionKind.BuffStation:
                    return definition.BuffType > 0 && definition.BuffDurationTicks > 0;
                case FacilityInteractionKind.Door:
                case FacilityInteractionKind.Container:
                case FacilityInteractionKind.Bed:
                case FacilityInteractionKind.Seat:
                case FacilityInteractionKind.Pylon:
                case FacilityInteractionKind.Mechanism:
                    return true;
                default:
                    // 钟由成功到达 GlobalTile.RightClick 确认。
                    return false;
            }
        }

        private readonly struct FacilityInteractionSnapshot
        {
            private const int TileSnapshotRadius = 6;
            private const ulong FnvOffsetBasis = 14695981039346656037UL;
            private const ulong FnvPrime = 1099511628211UL;

            private FacilityInteractionSnapshot(
                int buffTime,
                int chest,
                int chestX,
                int chestY,
                int spawnX,
                int spawnY,
                bool isSitting,
                int sittingIndex,
                bool isSleeping,
                int sleepingIndex,
                Vector2 playerBottom,
                bool mapFullscreen,
                ulong tileStateHash)
            {
                BuffTime = buffTime;
                Chest = chest;
                ChestX = chestX;
                ChestY = chestY;
                SpawnX = spawnX;
                SpawnY = spawnY;
                IsSitting = isSitting;
                SittingIndex = sittingIndex;
                IsSleeping = isSleeping;
                SleepingIndex = sleepingIndex;
                PlayerBottom = playerBottom;
                MapFullscreen = mapFullscreen;
                TileStateHash = tileStateHash;
            }

            private int BuffTime { get; }

            private int Chest { get; }

            private int ChestX { get; }

            private int ChestY { get; }

            private int SpawnX { get; }

            private int SpawnY { get; }

            private bool IsSitting { get; }

            private int SittingIndex { get; }

            private bool IsSleeping { get; }

            private int SleepingIndex { get; }

            private Vector2 PlayerBottom { get; }

            private bool MapFullscreen { get; }

            private ulong TileStateHash { get; }

            internal static FacilityInteractionSnapshot Capture(
                Player player,
                ResolvedFacility facility,
                int tileX,
                int tileY)
            {
                FacilityInteractionKind kind = facility.Definition.InteractionKind;
                bool observeTiles = kind == FacilityInteractionKind.Door
                    || kind == FacilityInteractionKind.Container
                    || kind == FacilityInteractionKind.Mechanism;

                return new FacilityInteractionSnapshot(
                    kind == FacilityInteractionKind.BuffStation
                        ? GetBuffTime(player, facility.Definition.BuffType)
                        : 0,
                    player.chest,
                    player.chestX,
                    player.chestY,
                    player.SpawnX,
                    player.SpawnY,
                    player.sitting.isSitting,
                    player.sitting.sittingIndex,
                    player.sleeping.isSleeping,
                    player.sleeping.sleepingIndex,
                    player.Bottom,
                    Main.mapFullscreen,
                    observeTiles ? GetTileStateHash(tileX, tileY) : 0UL);
            }

            internal bool HasSuccessfulChange(
                Player player,
                ResolvedFacility facility,
                int tileX,
                int tileY)
            {
                switch (facility.Definition.InteractionKind)
                {
                    case FacilityInteractionKind.BuffStation:
                        return GetBuffTime(player, facility.Definition.BuffType) > BuffTime;
                    case FacilityInteractionKind.Door:
                        return GetTileStateHash(tileX, tileY) != TileStateHash;
                    case FacilityInteractionKind.Container:
                        return player.chest != Chest
                            || player.chestX != ChestX
                            || player.chestY != ChestY
                            || GetTileStateHash(tileX, tileY) != TileStateHash;
                    case FacilityInteractionKind.Bed:
                        return player.SpawnX != SpawnX
                            || player.SpawnY != SpawnY
                            || player.sleeping.isSleeping != IsSleeping
                            || player.sleeping.sleepingIndex != SleepingIndex
                            || player.Bottom != PlayerBottom;
                    case FacilityInteractionKind.Seat:
                        return player.sitting.isSitting != IsSitting
                            || player.sitting.sittingIndex != SittingIndex
                            || player.Bottom != PlayerBottom;
                    case FacilityInteractionKind.Pylon:
                        return !MapFullscreen && Main.mapFullscreen;
                    case FacilityInteractionKind.Mechanism:
                        return GetTileStateHash(tileX, tileY) != TileStateHash;
                    default:
                        return false;
                }
            }

            private static ulong GetTileStateHash(int centerX, int centerY)
            {
                int minX = System.Math.Max(centerX - TileSnapshotRadius, 0);
                int maxX = System.Math.Min(centerX + TileSnapshotRadius, Main.maxTilesX - 1);
                int minY = System.Math.Max(centerY - TileSnapshotRadius, 0);
                int maxY = System.Math.Min(centerY + TileSnapshotRadius, Main.maxTilesY - 1);
                ulong hash = FnvOffsetBasis;

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        Tile tile = Framing.GetTileSafely(x, y);
                        hash = Mix(hash, tile.HasTile ? 1UL : 0UL);
                        hash = Mix(hash, tile.TileType);
                        hash = Mix(hash, unchecked((ushort)tile.TileFrameX));
                        hash = Mix(hash, unchecked((ushort)tile.TileFrameY));
                    }
                }

                return hash;
            }

            private static ulong Mix(ulong hash, ulong value)
            {
                return (hash ^ value) * FnvPrime;
            }
        }

        /// <summary>
        /// 获取指定 Buff 当前剩余时间；未持有时返回零。
        /// </summary>
        private static int GetBuffTime(Player player, int buffType)
        {
            int buffIndex = player.FindBuffIndex(buffType);
            return buffIndex >= 0 ? player.buffTime[buffIndex] : 0;
        }
    }
}
