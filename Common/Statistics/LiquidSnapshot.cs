namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 保存一个直接目标格位的液体标量；不持有可变 Tile 引用，也不自行扫描世界或判断归属。
    /// 默认值代表空格，空格残留类型统一归一化，避免零体积产生虚假类型替换。
    /// </summary>
    internal readonly struct LiquidSnapshot
    {
        internal const int UnitsPerFullTile = 255;

        /// <summary>仅由已验证的标量构造快照；外部通过 TryCreate 处理未知类型与非法数量。</summary>
        private LiquidSnapshot(LiquidKind kind, byte units)
        {
            Kind = kind;
            Units = units;
        }

        internal LiquidKind Kind { get; }

        internal byte Units { get; }

        /// <summary>
        /// 从真实状态的标量建立快照，拒绝未知的非空液体及越界数量，避免误归类为水。
        /// 空格忽略未被游戏清理的类型位；采集器遇到 false 时必须跳过该格，不能用默认空格兜底。
        /// </summary>
        internal static bool TryCreate(int liquidType, int units, out LiquidSnapshot snapshot)
        {
            snapshot = default;
            if (units < 0 || units > UnitsPerFullTile)
                return false;
            if (units == 0)
                return true;
            if (liquidType < (int)LiquidKind.Water || liquidType > (int)LiquidKind.Shimmer)
                return false;
            snapshot = new LiquidSnapshot((LiquidKind)liquidType, (byte)units);
            return true;
        }
    }
}
