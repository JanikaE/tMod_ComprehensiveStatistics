using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ObjectData;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 保存目标的值快照；不保留指向世界格位的 Tile 引用，避免操作后读到已改变的旧值。
    /// </summary>
    internal readonly struct TileContentSnapshot
    {
        /// <summary>
        /// 保存内容身份和对象范围；默认值表示有效空格位之外的无效快照。
        /// </summary>
        private TileContentSnapshot(bool valid, bool hasTile, int type, int style,
            Point16 origin, int width, int height, int wallType, byte wires)
        {
            IsValid = valid;
            HasTile = hasTile;
            TileType = type;
            Style = style;
            Origin = origin;
            Width = width;
            Height = height;
            WallType = wallType;
            Wires = wires;
        }

        internal bool IsValid { get; }
        internal bool HasTile { get; }
        internal int TileType { get; }
        internal int Style { get; }
        internal Point16 Origin { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int WallType { get; }
        internal byte Wires { get; }
        internal bool IsObject => Width > 0 && Height > 0;
        internal bool HasKnownIdentity => !HasTile || Style >= 0 || !IsObject;

        /// <summary>
        /// 捕获一个直接目标及其对象范围；不扫描周边地形，也不把方向、涂色和坡形作为新内容。
        /// </summary>
        internal static TileContentSnapshot Capture(int x, int y)
        {
            if (!WorldGen.InWorld(x, y))
                return default;

            Tile tile = Framing.GetTileSafely(x, y);
            TileObjectData data = tile.HasTile ? TileObjectData.GetTileData(tile) : null;
            int style = data != null ? FacilityRegistry.GetCanonicalTileStyle(tile) : -1;
            Point16 origin = data != null ? TileObjectData.TopLeft(x, y) : new Point16(x, y);
            int width = data?.Width ?? 0;
            int height = data?.Height ?? 0;
            // 元数据不可解析时保留“对象但身份未知”，不能回退成普通方块逐格累计。
            if (data != null && (width <= 0 || height <= 0
                || !WorldGen.InWorld(origin.X, origin.Y)
                || !WorldGen.InWorld(origin.X + width - 1, origin.Y + height - 1)))
            {
                style = -1;
                width = height = 1;
            }

            byte wires = (byte)((tile.RedWire ? 1 : 0) | (tile.BlueWire ? 2 : 0)
                | (tile.GreenWire ? 4 : 0) | (tile.YellowWire ? 8 : 0)
                | (tile.HasActuator ? 16 : 0));
            return new TileContentSnapshot(true, tile.HasTile, tile.TileType, style,
                origin, width, height, tile.WallType, wires);
        }

        /// <summary>
        /// 判断目标是否仍是同一内容；家具需比较规范样式，普通地形不比较连接帧。
        /// </summary>
        internal bool SameContent(TileContentSnapshot other)
        {
            return HasTile == other.HasTile && (!HasTile
                || (TileType == other.TileType && IsObject == other.IsObject
                    && (!IsObject || (HasKnownIdentity && other.HasKnownIdentity
                        && Style == other.Style && Origin == other.Origin))));
        }

        /// <summary>
        /// 确认对象完整形成；多个组成格必须归属于同一原点、类型和规范样式。
        /// </summary>
        internal bool IsFullyPresent()
        {
            if (!IsValid || !HasTile || !HasKnownIdentity)
                return false;
            if (!IsObject)
                return true;

            for (int dx = 0; dx < Width; dx++)
                for (int dy = 0; dy < Height; dy++)
                {
                    TileContentSnapshot part = Capture(Origin.X + dx, Origin.Y + dy);
                    if (!part.IsValid || !SameContent(part))
                        return false;
                }
            return true;
        }

        /// <summary>
        /// 确认旧对象已完整消失；任何旧类型、旧样式的组成格残留都会阻止计数。
        /// </summary>
        internal bool IsFullyRemoved()
        {
            if (!IsValid || !HasTile || !IsObject || !HasKnownIdentity)
                return false;
            for (int dx = 0; dx < Width; dx++)
                for (int dy = 0; dy < Height; dy++)
                {
                    Tile tile = Framing.GetTileSafely(Origin.X + dx, Origin.Y + dy);
                    if (tile.HasTile && tile.TileType == TileType
                        && FacilityRegistry.GetCanonicalTileStyle(tile) == Style)
                        return false;
                }
            return true;
        }
    }
}
