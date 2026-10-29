using System;
using System.Collections.Generic;

// 只模拟采集器读取的格位、对象元数据与记录出口；不会加载游戏或改动真实角色/世界。
namespace Terraria.DataStructures
{
    /// <summary>提供测试格位坐标的值语义。</summary>
    internal readonly record struct Point16(int X, int Y);
}

namespace Terraria.ID
{
    /// <summary>提供空墙标识。</summary>
    internal static class WallID { internal const int None = 0; }
    /// <summary>匹配原版网络端编号。</summary>
    internal static class NetmodeID { internal const int SinglePlayer=0, MultiplayerClient=1, Server=2; }
}

namespace Terraria
{
    /// <summary>模拟可变世界格位，验证快照没有保留可变引用。</summary>
    internal sealed class Tile
    {
        internal bool HasTile, RedWire, BlueWire, GreenWire, YellowWire, HasActuator;
        internal int TileType, WallType, Style, OriginX, OriginY, Color = 0, Slope = 0;
    }

    /// <summary>模拟玩家身份及记录出口。</summary>
    internal sealed partial class Player
    {
        internal bool active = true;
        internal int whoAmI = 0;
        internal readonly ComprehensiveStatistics.Common.Players.StatisticsPlayer Statistics = new();
        /// <summary>返回测试用的统计出口。</summary>
        internal T GetModPlayer<T>() => Main.netMode == 2 ? throw new Exception("Server accessed character statistics.") : (T)(object)Statistics;
    }

    /// <summary>隔离测试会话和世界状态。</summary>
    internal static partial class Main
    {
        internal static bool dedServ, gameMenu;
        internal static int myPlayer, netMode = 0;
        internal static Player[] player = { new() };
        internal static readonly Tile[,] Tiles = new Tile[16, 16];
    }

    /// <summary>只实现采集器要求的世界边界。</summary>
    internal static partial class WorldGen
    {
        /// <summary>检查测试世界边界。</summary>
        internal static bool InWorld(int x, int y) => x >= 0 && y >= 0 && x < 16 && y < 16;
    }

    /// <summary>读取测试格位。</summary>
    internal static class Framing
    {
        /// <summary>返回指定可变格位。</summary>
        internal static Tile GetTileSafely(int x, int y) => Main.Tiles[x, y];
    }
}

namespace Terraria.ObjectData
{
    /// <summary>类型 100 为 2×2 家具，其余为普通地形。</summary>
    internal sealed class TileObjectData
    {
        internal int Width = 2, Height = 2;
        internal Terraria.DataStructures.Point16 Origin = new(0,0);
        /// <summary>提供原版网络家具入口使用的元数据查询。</summary>
        internal static TileObjectData GetTileData(int type,int style,int alternate)=>type==100?new TileObjectData():null;
        /// <summary>解析测试家具元数据。</summary>
        internal static TileObjectData GetTileData(Terraria.Tile tile) => tile.TileType == 100 ? new() : null;
        /// <summary>返回预设的家具规范原点。</summary>
        internal static Terraria.DataStructures.Point16 TopLeft(int x, int y)
        {
            Terraria.Tile tile = Terraria.Main.Tiles[x, y];
            return new(tile.OriginX, tile.OriginY);
        }
    }
}

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>只提供测试用家具样式解析。</summary>
    internal static class FacilityRegistry
    {
        /// <summary>返回测试家具的规范样式，负数代表无法解析。</summary>
        internal static int GetCanonicalTileStyle(Terraria.Tile tile) => tile.Style;
    }

    /// <summary>布线组件顺序与生产代码一致。</summary>
    internal enum WiringComponentKind { RedWire, BlueWire, GreenWire, YellowWire, Actuator }
}

namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>记录采集结果供断言，不复刻生产统计逻辑。</summary>
    internal sealed partial class StatisticsPlayer
    {
        internal readonly List<string> Records = new();
        /// <summary>保存地形放置结果。</summary>
        internal void RecordPlacedTile(int type) => Records.Add($"place:{type}");
        /// <summary>保存地形拆除结果。</summary>
        internal void RecordMinedTile(int type) => Records.Add($"remove:{type}");
        /// <summary>保存墙放置结果。</summary>
        internal void RecordPlacedWall(int type) => Records.Add($"wall-place:{type}");
        /// <summary>保存墙拆除结果。</summary>
        internal void RecordRemovedWall(int type) => Records.Add($"wall-remove:{type}");
        /// <summary>保存家具放置结果。</summary>
        internal void RecordPlacedTileObject(int type, int style) => Records.Add($"object-place:{type}:{style}");
        /// <summary>保存家具拆除结果。</summary>
        internal void RecordRemovedTileObject(int type, int style) => Records.Add($"object-remove:{type}:{style}");
        /// <summary>保存逐组件真实变化供断言。</summary>
        internal void RecordWiringComponentChanges(global::ComprehensiveStatistics.Common.Statistics.WiringComponentKind kind,
            bool placed, long count) => Records.Add($"wire:{kind}:{placed}:{count}");
    }
}
