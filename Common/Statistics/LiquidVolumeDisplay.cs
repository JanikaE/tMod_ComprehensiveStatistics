using System.Globalization;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>液体体积只在显示时精确换算，整数排序与合计不经过舍入。</summary>
    internal static class LiquidVolumeDisplay
    {
        /// <summary>255 原始单位为一格，最多三位小数并省略尾零，最小非零体积不显示成零。</summary>
        internal static string Format(long units) => (units / 255m).ToString("#,0.###", CultureInfo.CurrentCulture);
    }
}
