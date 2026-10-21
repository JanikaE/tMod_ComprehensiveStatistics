using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using Terraria.DataStructures;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 在玩家采掘入口处关联破坏者与目标内容，并只提交真正消失的实体物块、物体或背景墙。
    /// </summary>
    internal sealed class StatisticsTileMiningSystem : ModSystem
    {
        public override void Load()
        {
            On_Player.PickTile += HandlePickTile;
            On_Player.PickWall += HandlePickWall;
        }

        public override void Unload()
        {
            On_Player.PickTile -= HandlePickTile;
            On_Player.PickWall -= HandlePickWall;
        }

        /// <summary>
        /// 对一次玩家采掘调用保存目标快照，并在原版逻辑结束后确认目标确实被破坏。
        /// </summary>
        private static void HandlePickTile(
            On_Player.orig_PickTile orig,
            Player player,
            int x,
            int y,
            int pickPower)
        {
            bool shouldObserve = IsOwnedLocalMiningAttempt(
                player,
                x,
                y,
                pickPower,
                wallMining: false);
            ushort originalTileType = 0;
            TileObjectSnapshot tileObject = default;
            bool isTileObject = false;
            if (shouldObserve)
            {
                Tile originalTile = Framing.GetTileSafely(x, y);
                shouldObserve = originalTile.HasTile;
                TileObjectData objectData = shouldObserve
                    ? TileObjectData.GetTileData(originalTile)
                    : null;
                if (objectData != null)
                {
                    shouldObserve = TileObjectSnapshot.TryCreate(
                        x,
                        y,
                        originalTile,
                        objectData,
                        out tileObject);
                    isTileObject = shouldObserve;
                }
                else if (shouldObserve)
                {
                    originalTileType = originalTile.TileType;
                }
            }

            orig(player, x, y, pickPower);

            if (!shouldObserve)
            {
                return;
            }

            if (isTileObject)
            {
                if (tileObject.IsFullyRemoved())
                {
                    player.GetModPlayer<StatisticsPlayer>().RecordRemovedTileObject(
                        tileObject.TileType,
                        tileObject.Style);
                }

                return;
            }

            Tile resultingTile = Framing.GetTileSafely(x, y);
            if (resultingTile.HasTile && resultingTile.TileType == originalTileType)
            {
                // 仍是原物块说明本次只积累了采掘伤害，或被权限/强度检查阻止。
                return;
            }

            player.GetModPlayer<StatisticsPlayer>().RecordMinedTile(originalTileType);
        }

        /// <summary>
        /// 对一次锤墙调用保存目标快照，并在原版逻辑结束后确认背景墙确实被拆除。
        /// </summary>
        private static void HandlePickWall(
            On_Player.orig_PickWall orig,
            Player player,
            int x,
            int y,
            int hammerPower)
        {
            bool shouldObserve = IsOwnedLocalMiningAttempt(
                player,
                x,
                y,
                hammerPower,
                wallMining: true);
            ushort originalWallType = WallID.None;
            if (shouldObserve)
            {
                originalWallType = Framing.GetTileSafely(x, y).WallType;
                shouldObserve = originalWallType != WallID.None;
            }

            orig(player, x, y, hammerPower);

            if (!shouldObserve
                || Framing.GetTileSafely(x, y).WallType == originalWallType)
            {
                // 未达到破坏阈值、受保护或其他失败情形都会保留原背景墙。
                return;
            }

            player.GetModPlayer<StatisticsPlayer>().RecordRemovedWall(originalWallType);
        }

        /// <summary>
        /// 判断调用是否来自当前客户端正在进行的有效工具使用。
        /// 爆炸、液体、物块更新、掉落物块和世界事件直接走 WorldGen 路径，不会通过本入口。
        /// </summary>
        private static bool IsOwnedLocalMiningAttempt(
            Player player,
            int x,
            int y,
            int toolPower,
            bool wallMining)
        {
            if (Main.dedServ
                || Main.gameMenu
                || player?.active != true
                || player.whoAmI != Main.myPlayer
                || toolPower <= 0
                || !WorldGen.InWorld(x, y)
                || (player.itemAnimation <= 0
                    && player.itemTime <= 0
                    && !player.channel))
            {
                return false;
            }

            Item heldItem = player.HeldItem;
            if (heldItem == null || heldItem.IsAir)
            {
                return false;
            }

            // 原版工具按能力字段确认；模组物品若主动调用玩家采掘入口则视为明确归因。
            // 直接调用 WorldGen.KillTile/KillWall 的模组行为无法可靠识别破坏者，故意排除。
            return heldItem.ModItem != null
                || (wallMining
                    ? heldItem.hammer > 0
                    : heldItem.pick > 0 || heldItem.axe > 0);
        }

        /// <summary>
        /// 保存一次物体破坏前的类型、样式和完整占用范围，以便原版逻辑结束后只提交一次统计。
        /// </summary>
        private readonly struct TileObjectSnapshot
        {
            private TileObjectSnapshot(
                int tileType,
                int style,
                Point16 topLeft,
                int width,
                int height)
            {
                TileType = tileType;
                Style = style;
                TopLeft = topLeft;
                Width = width;
                Height = height;
            }

            internal int TileType { get; }

            internal int Style { get; }

            private Point16 TopLeft { get; }

            private int Width { get; }

            private int Height { get; }

            /// <summary>
            /// 从物体任意组成格恢复规范化样式和左上原点；普通地形物块没有对象数据。
            /// </summary>
            internal static bool TryCreate(
                int x,
                int y,
                Tile tile,
                TileObjectData data,
                out TileObjectSnapshot snapshot)
            {
                int style = FacilityRegistry.GetCanonicalTileStyle(tile);
                Point16 topLeft = TileObjectData.TopLeft(x, y);
                if (style < 0
                    || topLeft.X < 0
                    || topLeft.Y < 0
                    || data.Width <= 0
                    || data.Height <= 0
                    || !WorldGen.InWorld(topLeft.X, topLeft.Y)
                    || !WorldGen.InWorld(
                        topLeft.X + data.Width - 1,
                        topLeft.Y + data.Height - 1))
                {
                    snapshot = default;
                    return false;
                }

                snapshot = new TileObjectSnapshot(
                    tile.TileType,
                    style,
                    topLeft,
                    data.Width,
                    data.Height);
                return true;
            }

            /// <summary>
            /// 只有原对象占用范围内不再存在同类型、同样式组成格时，才视为完整拆除。
            /// </summary>
            internal bool IsFullyRemoved()
            {
                for (int offsetX = 0; offsetX < Width; offsetX++)
                {
                    for (int offsetY = 0; offsetY < Height; offsetY++)
                    {
                        Tile tile = Framing.GetTileSafely(
                            TopLeft.X + offsetX,
                            TopLeft.Y + offsetY);
                        if (tile.HasTile
                            && tile.TileType == TileType
                            && FacilityRegistry.GetCanonicalTileStyle(tile) == Style)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }
}
