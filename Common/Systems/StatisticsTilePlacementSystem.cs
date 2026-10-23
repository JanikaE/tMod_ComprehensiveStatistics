using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 将本地玩家的标准物块和背景墙放置流程与实际世界状态变化关联。
    /// </summary>
    internal sealed class StatisticsTilePlacementSystem : ModSystem
    {
        private static Player activeLocalPlacementPlayer;

        public override void Load()
        {
            On_Player.PlaceThing_Tiles += HandlePlaceThingTiles;
            On_Player.PlaceThing_Walls += HandlePlaceThingWalls;
            On_WorldGen.PlaceTile += HandlePlaceTile;
            On_WorldGen.ReplaceTile += HandleReplaceTile;
            On_WorldGen.PlaceWall += HandlePlaceWall;
            On_WorldGen.ReplaceWall += HandleReplaceWall;
        }

        public override void Unload()
        {
            On_Player.PlaceThing_Tiles -= HandlePlaceThingTiles;
            On_Player.PlaceThing_Walls -= HandlePlaceThingWalls;
            On_WorldGen.PlaceTile -= HandlePlaceTile;
            On_WorldGen.ReplaceTile -= HandleReplaceTile;
            On_WorldGen.PlaceWall -= HandlePlaceWall;
            On_WorldGen.ReplaceWall -= HandleReplaceWall;
            activeLocalPlacementPlayer = null;
        }

        /// <summary>
        /// 在原版物块放置流程内保存本地玩家归因上下文。
        /// </summary>
        private static void HandlePlaceThingTiles(
            On_Player.orig_PlaceThing_Tiles orig,
            Player player)
        {
            RunInPlacementScope(player, () => orig(player));
        }

        /// <summary>
        /// 在原版背景墙放置流程内保存本地玩家归因上下文。
        /// </summary>
        private static void HandlePlaceThingWalls(
            On_Player.orig_PlaceThing_Walls orig,
            Player player)
        {
            RunInPlacementScope(player, () => orig(player));
        }

        /// <summary>
        /// 执行一次玩家放置流程，并在异常或嵌套调用后恢复先前归因上下文。
        /// </summary>
        private static void RunInPlacementScope(Player player, System.Action action)
        {
            Player previousPlayer = activeLocalPlacementPlayer;
            activeLocalPlacementPlayer = IsOwnedLocalPlayer(player) ? player : null;
            try
            {
                action();
            }
            finally
            {
                activeLocalPlacementPlayer = previousPlayer;
            }
        }

        /// <summary>
        /// 比较普通物块放置调用前后的目标格位，只提交实际新增或替换成功的结果。
        /// </summary>
        private static bool HandlePlaceTile(
            On_WorldGen.orig_PlaceTile orig,
            int x,
            int y,
            int type,
            bool mute,
            bool forced,
            int player,
            int style)
        {
            TilePlacementSnapshot before = CaptureTile(x, y);
            bool result = orig(x, y, type, mute, forced, player, style);
            RecordChangedTile(x, y, before);
            return result;
        }

        /// <summary>
        /// 比较物块替换调用前后的目标格位；替换只计新内容的放置，不计旧内容的拆除。
        /// </summary>
        private static bool HandleReplaceTile(
            On_WorldGen.orig_ReplaceTile orig,
            int x,
            int y,
            ushort type,
            int style)
        {
            TilePlacementSnapshot before = CaptureTile(x, y);
            bool result = orig(x, y, type, style);
            RecordChangedTile(x, y, before);
            return result;
        }

        /// <summary>
        /// 比较普通背景墙放置调用前后的目标格位。
        /// </summary>
        private static void HandlePlaceWall(
            On_WorldGen.orig_PlaceWall orig,
            int x,
            int y,
            int type,
            bool mute)
        {
            ushort before = CaptureWall(x, y);
            orig(x, y, type, mute);
            RecordChangedWall(x, y, before);
        }

        /// <summary>
        /// 比较背景墙替换调用前后的目标格位；替换只计新墙的放置。
        /// </summary>
        private static bool HandleReplaceWall(
            On_WorldGen.orig_ReplaceWall orig,
            int x,
            int y,
            ushort type)
        {
            ushort before = CaptureWall(x, y);
            bool result = orig(x, y, type);
            RecordChangedWall(x, y, before);
            return result;
        }

        /// <summary>
        /// 保存目标格位放置前的物块类型和对象样式。
        /// </summary>
        private static TilePlacementSnapshot CaptureTile(int x, int y)
        {
            if (activeLocalPlacementPlayer == null || !WorldGen.InWorld(x, y))
            {
                return default;
            }

            return TilePlacementSnapshot.Create(Framing.GetTileSafely(x, y));
        }

        /// <summary>
        /// 保存目标格位放置前的背景墙类型。
        /// </summary>
        private static ushort CaptureWall(int x, int y)
        {
            return activeLocalPlacementPlayer != null && WorldGen.InWorld(x, y)
                ? Framing.GetTileSafely(x, y).WallType
                : WallID.None;
        }

        /// <summary>
        /// 将成功变化的目标格位按普通物块或家具/多格物体提交一次放置统计。
        /// </summary>
        private static void RecordChangedTile(
            int x,
            int y,
            TilePlacementSnapshot before)
        {
            Player player = activeLocalPlacementPlayer;
            if (player == null || !WorldGen.InWorld(x, y))
            {
                return;
            }

            Tile resultingTile = Framing.GetTileSafely(x, y);
            if (!resultingTile.HasTile || before.Matches(resultingTile))
            {
                return;
            }

            StatisticsPlayer statisticsPlayer = player.GetModPlayer<StatisticsPlayer>();
            TileObjectData objectData = TileObjectData.GetTileData(resultingTile);
            if (objectData == null)
            {
                statisticsPlayer.RecordPlacedTile(resultingTile.TileType);
                return;
            }

            int objectStyle = FacilityRegistry.GetCanonicalTileStyle(resultingTile);
            if (objectStyle >= 0)
            {
                statisticsPlayer.RecordPlacedTileObject(
                    resultingTile.TileType,
                    objectStyle);
            }
        }

        /// <summary>
        /// 将成功新增或替换的一格背景墙提交一次放置统计。
        /// </summary>
        private static void RecordChangedWall(int x, int y, ushort before)
        {
            Player player = activeLocalPlacementPlayer;
            if (player == null || !WorldGen.InWorld(x, y))
            {
                return;
            }

            ushort after = Framing.GetTileSafely(x, y).WallType;
            if (after == WallID.None || after == before)
            {
                return;
            }

            player.GetModPlayer<StatisticsPlayer>().RecordPlacedWall(after);
        }

        /// <summary>
        /// 判断玩家是否是当前客户端可唯一归因的活动角色。
        /// </summary>
        private static bool IsOwnedLocalPlayer(Player player)
        {
            return !Main.dedServ
                && !Main.gameMenu
                && player?.active == true
                && player.whoAmI == Main.myPlayer;
        }

        /// <summary>
        /// 保存一次物块放置前足以判定内容是否实际变化的状态。
        /// </summary>
        private readonly struct TilePlacementSnapshot
        {
            private TilePlacementSnapshot(bool hasTile, ushort tileType, int objectStyle)
            {
                HasTile = hasTile;
                TileType = tileType;
                ObjectStyle = objectStyle;
            }

            private bool HasTile { get; }

            private ushort TileType { get; }

            private int ObjectStyle { get; }

            /// <summary>
            /// 从一个格位创建放置前快照；非对象物块不保存帧样式。
            /// </summary>
            internal static TilePlacementSnapshot Create(Tile tile)
            {
                if (tile == null || !tile.HasTile)
                {
                    return default;
                }

                int objectStyle = TileObjectData.GetTileData(tile) != null
                    ? FacilityRegistry.GetCanonicalTileStyle(tile)
                    : -1;
                return new TilePlacementSnapshot(true, tile.TileType, objectStyle);
            }

            /// <summary>
            /// 判断放置后的格位是否仍是快照中的同一物块及同一对象样式。
            /// </summary>
            internal bool Matches(Tile tile)
            {
                if (!HasTile || tile == null || !tile.HasTile || tile.TileType != TileType)
                {
                    return false;
                }

                return ObjectStyle < 0
                    || FacilityRegistry.GetCanonicalTileStyle(tile) == ObjectStyle;
            }
        }
    }
}
