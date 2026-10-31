using System.Collections.Generic;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using Terraria.Localization;

namespace ComprehensiveStatistics.Common.UI
{
    /// <summary>根据只读能力快照生成中英文提示，不在统计明细中添加技术诊断行。</summary>
    internal static class ImproveGameCompatibilityText
    {
        private const string Prefix = "Mods.ComprehensiveStatistics.UI.Compatibility.";
        private static readonly ImproveGameCapability[] Tools =
        {
            ImproveGameCapability.Construct, ImproveGameCapability.Building, ImproveGameCapability.Space,
            ImproveGameCapability.BurstSelector, ImproveGameCapability.FixedBurst, ImproveGameCapability.WallSelector,
            ImproveGameCapability.Robot, ImproveGameCapability.Vein, ImproveGameCapability.Liquid
        };

        /// <summary>按当前语言与 Shift 状态生成简短或完整提示；状态每帧重新取得，无需重开窗口。</summary>
        internal static string Format(ImproveGameCompatibilityStatus status, bool showDetails)
        {
            var lines = new List<string> { Text("Title") };
            if (status.Version != null) lines.Add(Text("Version", status.Version));
            lines.Add(Text("States." + status.State, status.EnabledCount));
            if (!showDetails)
            {
                lines.Add("[c/FFCC66:" + Text("HoldShiftForDetails") + "]");
                return string.Join("\n", lines);
            }

            if (status.State != ImproveGameCompatibilityState.NotInstalled && status.State != ImproveGameCompatibilityState.UnsupportedVersion)
            {
                foreach (var capability in Tools)
                    lines.Add(Text("ToolLine", Text("Tools." + capability), Text("Availability." + status.GetAvailability(capability))));
            }
            lines.Add(string.Empty);
            lines.Add(Text("ReplacementRule"));
            lines.Add(Text("LiquidRule"));
            lines.Add(Text("HistoryRule"));
            lines.Add(Text("MultiplayerRule"));
            lines.Add(Text("Limits"));
            lines.Add(Text("FurnitureLimit"));
            return string.Join("\n", lines);
        }

        /// <summary>统一使用本模组本地化键，不把诊断日志拼进玩家提示。</summary>
        private static string Text(string key, params object[] args) => Language.GetTextValue(Prefix + key, args);
    }
}
