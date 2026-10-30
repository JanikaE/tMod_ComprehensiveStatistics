using System;
using ComprehensiveStatistics.Common.Networking;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>区分未安装、版本不支持、单人、多人协商中和实际可用状态。</summary>
    internal enum ImproveGameCompatibilityState
    { NotInstalled, UnsupportedVersion, SinglePlayer, WaitingForServer, ServerUnavailable, NoCompatibleTools, MultiplayerReady }

    /// <summary>表示一个工具在当前执行端的真实可用状态；等待不冒充已启用。</summary>
    internal enum ImproveGameToolAvailability { Disabled, Waiting, Enabled }

    /// <summary>只读显示快照，不访问玩家统计、不持久化，也不改变兼容采集决策。</summary>
    internal readonly record struct ImproveGameCompatibilityStatus(string Version, ImproveGameCompatibilityState State,
        ImproveGameCapability Local, ImproveGameCapability Active)
    {
        /// <summary>从实际安装、加载与协商结果生成状态；低于最低版本或缺失模组时关闭显示能力。</summary>
        internal static ImproveGameCompatibilityStatus Create(string version, bool supported, bool multiplayer,
            ImproveGameCapability singlePlayer, ImproveGameCapability multiplayerLocal, bool serverConfirmed,
            bool timedOut, ImproveGameCapability negotiated)
        {
            if (version == null) return new(null, ImproveGameCompatibilityState.NotInstalled, 0, 0);
            if (!supported) return new(version, ImproveGameCompatibilityState.UnsupportedVersion, 0, 0);
            var local = multiplayer ? multiplayerLocal : singlePlayer;
            if (local == 0) return new(version, ImproveGameCompatibilityState.NoCompatibleTools, 0, 0);
            if (!multiplayer) return new(version, ImproveGameCompatibilityState.SinglePlayer, local, local);
            if (!serverConfirmed) return new(version, timedOut ? ImproveGameCompatibilityState.ServerUnavailable
                : ImproveGameCompatibilityState.WaitingForServer, local, 0);
            var active = negotiated & local;
            return new(version, active == 0 ? ImproveGameCompatibilityState.NoCompatibleTools
                : ImproveGameCompatibilityState.MultiplayerReady, local, active);
        }

        /// <summary>按工具区分本机未适配、等待服务器和双方实际启用，不根据单个总开关推断全覆盖。</summary>
        internal ImproveGameToolAvailability GetAvailability(ImproveGameCapability capability)
        {
            int bits = (ushort)capability;
            if (bits == 0 || (bits & (bits - 1)) != 0 || (Local & capability) == 0) return ImproveGameToolAvailability.Disabled;
            if ((Active & capability) != 0) return ImproveGameToolAvailability.Enabled;
            return State == ImproveGameCompatibilityState.WaitingForServer ? ImproveGameToolAvailability.Waiting
                : ImproveGameToolAvailability.Disabled;
        }

        /// <summary>统计实际启用能力数，部分适配不显示为全部兼容。</summary>
        internal int EnabledCount
        {
            get
            {
                int count = 0;
                foreach (ImproveGameCapability capability in Enum.GetValues<ImproveGameCapability>())
                    if (GetAvailability(capability) == ImproveGameToolAvailability.Enabled) count++;
                return count;
            }
        }
    }
}
