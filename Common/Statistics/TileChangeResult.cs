namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>限定可确认的施工结果类型，不允许网络端指定任意统计指标。</summary>
    internal enum TileChangeKind : byte
    {
        TilePlaced = 1, TileRemoved, ObjectPlaced, ObjectRemoved, WallPlaced, WallRemoved,
        WiringPlaced, WiringRemoved
    }

    /// <summary>描述共用结算器确认的一次真实结果；类型编号仅在当前执行端内部使用。</summary>
    internal readonly record struct TileChangeResult(TileChangeKind Kind, int Type, int Style = 0);
}
