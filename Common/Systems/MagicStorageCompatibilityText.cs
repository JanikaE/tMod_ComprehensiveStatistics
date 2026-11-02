using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Networking;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>物品页只读取实际安装与协商状态，简短提示和 Shift 规则使用同一能力快照。</summary>
    internal sealed partial class MagicStorageCraftingSystem
    {
        /// <summary>生成玩家可读的魔法存储兼容说明，不访问个人统计或展示会话实现细节。</summary>
        internal static string GetCompatibilityText(bool details)
        {
            string Text(string key, params object[] args) => Language.GetTextValue("Mods.ComprehensiveStatistics.UI.CraftingCompatibility." + key, args);
            var lines = new List<string> { Text("Title") };
            if (upstreamVersion != null) lines.Add(Text("Version", upstreamVersion));
            bool multiplayer = Main.netMode == NetmodeID.MultiplayerClient;
            string state = upstreamVersion == null ? "NotInstalled" : !supported ? "Unsupported" : installedCapabilities == 0 ? "Unavailable"
                : !multiplayer ? "SinglePlayer" : !networkInstalled ? "Unavailable"
                : client.Server == Guid.Empty ? helloAttempts >= 10 ? "ServerUnavailable" : "Waiting"
                : client.Capabilities == 0 ? "NoCommon" : "Ready";
            lines.Add(Text(state));
            string Availability(byte capability)
            {
                if ((installedCapabilities & capability) == 0) return Text("Disabled");
                if (!multiplayer || networkInstalled && (client.Capabilities & capability) != 0) return Text("Enabled");
                return Text(state == "Waiting" ? "Pending" : "Disabled");
            }
            lines.Add(Text("Normal", Availability(MagicStorageConfirmation.Standard)));
            lines.Add(Text("Recursive", Availability(MagicStorageConfirmation.Recursive)));
            if (details)
            {
                lines.Add(Text("ChainRule")); lines.Add(Text("MultiplayerRule")); lines.Add(Text("HistoryRule"));
                if (multiplayer) lines.Add(Text("PendingCount", client.Count));
            }
            else lines.Add("[c/FFD86B:" + Text("HoldShift") + "]");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
