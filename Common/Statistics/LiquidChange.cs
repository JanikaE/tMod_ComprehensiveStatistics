using System;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 一个即时步骤内的液体双向体积差，不包含坐标、任务或玩家身份。
    /// 只允许从合法快照计算；多步操作必须逐步结算，不能用最终净差抹去已发生的移除和放置。
    /// </summary>
    internal readonly struct LiquidChange
    {
        /// <summary>保存从合法快照导出的两个非负方向，体积仍为 0–255 原始单位。</summary>
        private LiquidChange(LiquidKind removedKind, int removedUnits, LiquidKind placedKind, int placedUnits)
        {
            RemovedKind = removedKind;
            RemovedUnits = removedUnits;
            PlacedKind = placedKind;
            PlacedUnits = placedUnits;
        }

        internal LiquidKind RemovedKind { get; }

        internal int RemovedUnits { get; }

        internal LiquidKind PlacedKind { get; }

        internal int PlacedUnits { get; }

        internal bool IsZero => RemovedUnits == 0 && PlacedUnits == 0;

        /// <summary>
        /// 比较直接修改前后的即时状态：同类型只记录增减，异类型分别记录旧液体移除与新液体加入。
        /// 本方法不证明变更是玩家操作；自然流动等无归属状态不得因为能算出差量就提交统计。
        /// </summary>
        internal static LiquidChange Between(LiquidSnapshot before, LiquidSnapshot after)
        {
            if (before.Kind == after.Kind)
            {
                int difference = after.Units - before.Units;
                return new LiquidChange(before.Kind, Math.Max(0, -difference),
                    after.Kind, Math.Max(0, difference));
            }
            return new LiquidChange(before.Kind, before.Units, after.Kind, after.Units);
        }
    }
}
