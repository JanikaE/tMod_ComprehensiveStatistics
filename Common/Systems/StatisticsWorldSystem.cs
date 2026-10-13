using System;
using ComprehensiveStatistics.Common.Statistics;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 持有当前世界的公共统计范围；B1 仅建立生命周期，后续世界事件会写入此容器。
    /// </summary>
    internal sealed class StatisticsWorldSystem : ModSystem
    {
        internal StatisticsData Data { get; private set; }

        public override void OnWorldLoad()
        {
            Data = new StatisticsData(DateTimeOffset.UtcNow);
        }

        public override void OnWorldUnload()
        {
            Data = null;
        }
    }
}
