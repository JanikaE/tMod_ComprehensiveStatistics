using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 在原版玩家放置入口建立归属，将 WorldGen 的实际目标交给共用变化收集器结算。
    /// </summary>
    internal sealed class StatisticsTilePlacementSystem : ModSystem
    {
        public override void Load()
        {
            On_Player.PlaceThing_Tiles += HandlePlaceThingTiles;
            On_Player.PlaceThing_Walls += HandlePlaceThingWalls;
            On_WorldGen.PlaceTile += HandlePlaceTile;
            On_WorldGen.ReplaceTile += HandleReplaceTile;
            On_WorldGen.PlaceWall += HandlePlaceWall;
            On_WorldGen.ReplaceWall += HandleReplaceWall;
            On_WorldGen.KillTile += HandleKillTile;
            On_WorldGen.KillWall += HandleKillWall;
        }

        public override void Unload()
        {
            On_Player.PlaceThing_Tiles -= HandlePlaceThingTiles;
            On_Player.PlaceThing_Walls -= HandlePlaceThingWalls;
            On_WorldGen.PlaceTile -= HandlePlaceTile;
            On_WorldGen.ReplaceTile -= HandleReplaceTile;
            On_WorldGen.PlaceWall -= HandlePlaceWall;
            On_WorldGen.ReplaceWall -= HandleReplaceWall;
            On_WorldGen.KillTile -= HandleKillTile;
            On_WorldGen.KillWall -= HandleKillWall;
            TileChangeScope.Reset();
        }

        public override void OnWorldLoad() => TileChangeScope.Reset();

        public override void OnWorldUnload() => TileChangeScope.Reset();

        /// <summary>
        /// 在同步放置步骤结束时结算；嵌套的普通放置和替换调用由同一作用域去重。
        /// </summary>
        private static void HandlePlaceThingTiles(On_Player.orig_PlaceThing_Tiles orig, Player player)
        {
            using (TileChangeScope.Begin(player))
                orig(player);
        }

        /// <summary>
        /// 为背景墙建立相同的玩家上下文，不把普通远程同步当作主动放置。
        /// </summary>
        private static void HandlePlaceThingWalls(On_Player.orig_PlaceThing_Walls orig, Player player)
        {
            using (TileChangeScope.Begin(player))
                orig(player);
        }

        /// <summary>
        /// 注册修改前的直接目标，由外层在完整同步步骤结束后读取最终类型与对象状态。
        /// </summary>
        private static bool HandlePlaceTile(On_WorldGen.orig_PlaceTile orig, int x, int y,
            int type, bool mute, bool forced, int player, int style)
        {
            TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Place);
            return orig(x, y, type, mute, forced, player, style);
        }

        /// <summary>
        /// 原版替换同时计旧拆除和新放置；内部嵌套 Place/Pick 不能覆盖外层意图。
        /// </summary>
        private static bool HandleReplaceTile(On_WorldGen.orig_ReplaceTile orig, int x, int y,
            ushort type, int style)
        {
            TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Replace);
            return orig(x, y, type, style);
        }

        /// <summary>
        /// 注册一格墙的放置前状态，重复和失败放置在统一结算时自然排除。
        /// </summary>
        private static void HandlePlaceWall(On_WorldGen.orig_PlaceWall orig, int x, int y,
            int type, bool mute)
        {
            TileChangeScope.Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Place);
            orig(x, y, type, mute);
        }

        /// <summary>
        /// 墙替换与普通放墙共享目标快照，防止内部调用重复提交。
        /// </summary>
        private static bool HandleReplaceWall(On_WorldGen.orig_ReplaceWall orig, int x, int y, ushort type)
        {
            TileChangeScope.Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Replace);
            return orig(x, y, type);
        }

        /// <summary>只为已归属的目标保留真实拆除证据，不因普通世界破坏创建新的统计目标。</summary>
        private static void HandleKillTile(On_WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
        {
            orig(x, y, fail, effectOnly, noItem);
            TileChangeScope.NoteRemoval(x, y, TileChangeLayer.Tile);
        }

        /// <summary>保留背景墙实际消失的证据，供同类型拆后重放结算。</summary>
        private static void HandleKillWall(On_WorldGen.orig_KillWall orig, int x, int y, bool fail)
        {
            orig(x, y, fail);
            TileChangeScope.NoteRemoval(x, y, TileChangeLayer.Wall);
        }
    }
}
