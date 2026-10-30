using System;

namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>区分服务器执行与客户端施工验证能力，双方按实际已安装入口协商。</summary>
    [Flags]
    internal enum ImproveGameCapability : ushort
    {
        None = 0, Space = 1, BurstSelector = 2, WallSelector = 4, Vein = 8, Construct = 16, Building = 32, FixedBurst = 64, Robot = 128,
        Liquid = 256, NativeLiquid = 512
    }
}
