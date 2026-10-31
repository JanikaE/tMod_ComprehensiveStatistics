using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.UI;
using Hjson;
using Terraria.Localization;

/// <summary>使用生产状态与提示生成器验证真实中英文资源，不启动游戏或访问角色。</summary>
internal static class Program
{
    private const ImproveGameCapability All = (ImproveGameCapability)511;
    private static int checks;

    /// <summary>验证状态、实际能力、语言键、占位符与所有提示分支。</summary>
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the repository root.");
        Check(!ImproveGameVersionPolicy.Supports(null), "missing version is unsupported");
        foreach (string version in new[] { "1.8.1", "1.8.1.99", "1.7.99" })
            Check(!ImproveGameVersionPolicy.Supports(new Version(version)), "below minimum: " + version);
        foreach (string version in new[] { "1.8.2", "1.8.2.0", "1.8.3", "1.8.4", "1.9.0", "2.0", "99.0" })
            Check(ImproveGameVersionPolicy.Supports(new Version(version)), "forward compatible: " + version);
        var zh = Load(Path.Combine(args[0], "Localization/zh-Hans_Mods.ComprehensiveStatistics.hjson"));
        var en = Load(Path.Combine(args[0], "Localization/en-US_Mods.ComprehensiveStatistics.hjson"));
        Check(zh.Keys.Order().SequenceEqual(en.Keys.Order()), "localization key parity");
        foreach (string key in zh.Keys)
            Check(Placeholders(zh[key]).SequenceEqual(Placeholders(en[key])), "placeholder parity: " + key);
        var cases = new[]
        {
            (Status(null, true, false, All, All, true, false, All), ImproveGameCompatibilityState.NotInstalled, 0),
            (Status("1.8.1", false, false, All, All, true, false, All), ImproveGameCompatibilityState.UnsupportedVersion, 0),
            (Status("99.0", ImproveGameVersionPolicy.Supports(new Version(99, 0)), true, All, All, true, false, All), ImproveGameCompatibilityState.MultiplayerReady, 9),
            (Status("1.8.4", ImproveGameVersionPolicy.Supports(new Version(1, 8, 4)), false, 0, 0, false, false, 0), ImproveGameCompatibilityState.NoCompatibleTools, 0),
            (Status("1.8.2", true, false, All, 0, false, false, 0), ImproveGameCompatibilityState.SinglePlayer, 9),
            (Status("1.8.2", true, false, ImproveGameCapability.Building, All, true, false, All), ImproveGameCompatibilityState.SinglePlayer, 1),
            (Status("1.8.2", true, true, All, All, false, false, All), ImproveGameCompatibilityState.WaitingForServer, 0),
            (Status("1.8.2", true, true, All, All, false, true, All), ImproveGameCompatibilityState.ServerUnavailable, 0),
            (Status("1.8.2", true, true, All, 0, false, false, All), ImproveGameCompatibilityState.NoCompatibleTools, 0),
            (Status("1.8.2", true, true, All, All, true, false, 0), ImproveGameCompatibilityState.NoCompatibleTools, 0),
            (Status("1.8.2", true, true, All, All, true, false, All), ImproveGameCompatibilityState.MultiplayerReady, 9),
            (Status("1.8.3", true, true, All, All, true, false, ImproveGameCapability.Space), ImproveGameCompatibilityState.MultiplayerReady, 1),
            (Status("1.8.2", true, true, All, ImproveGameCapability.Building, true, false, All), ImproveGameCompatibilityState.MultiplayerReady, 1)
        };
        foreach (var (status, state, count) in cases)
        {
            Check(status.State == state && status.EnabledCount == count, "state and active count");
            Check(status.GetAvailability(ImproveGameCapability.None) == ImproveGameToolAvailability.Disabled, "zero is not a tool");
            Check(status.GetAvailability(All) == ImproveGameToolAvailability.Disabled, "combined mask is not a tool");
            foreach (var language in new[] { zh, en })
            {
                Language.Values = language;
                string summary = ImproveGameCompatibilityText.Format(status, false);
                Check(summary.Contains(language["UI.Compatibility.Title"])
                    && summary.Contains(string.Format(CultureInfo.InvariantCulture, language["UI.Compatibility.States." + state], count)),
                    "summary retains title and actual state");
                Check(summary.Contains("[c/FFCC66:" + language["UI.Compatibility.HoldShiftForDetails"] + "]")
                    && summary.Split('\n').Length == (status.Version == null ? 3 : 4), "compact summary with colored Shift hint");
                Check(!summary.Contains(language["UI.Compatibility.ReplacementRule"]), "details hidden by default");
                string text = ImproveGameCompatibilityText.Format(status, true);
                Check(text.Contains(language["UI.Compatibility.ReplacementRule"])
                    && text.Contains(language["UI.Compatibility.FurnitureLimit"])
                    && !text.Contains(language["UI.Compatibility.HoldShiftForDetails"]), "full rules shown while Shift is held");
                Check(!text.Contains("Mods.ComprehensiveStatistics") && !Regex.IsMatch(text, @"\{\d"), "resolved text and placeholders");
                Check(text.Split('\n').All(line => line.Length <= 88), "compact tooltip lines");
                if (state == ImproveGameCompatibilityState.WaitingForServer)
                    Check(text.Contains(language["UI.Compatibility.Availability.Waiting"]), "waiting is visible");
                if (count == 1)
                    Check(text.Contains("1/9") && text.Contains(language["UI.Compatibility.Availability.Disabled"]), "partial capability shown honestly");
            }
        }
        var pending = Status("1.8.2", true, true, All, ImproveGameCapability.Building, false, false, 0);
        Check(pending.GetAvailability(ImproveGameCapability.Building) == ImproveGameToolAvailability.Waiting
            && pending.GetAvailability(ImproveGameCapability.Space) == ImproveGameToolAvailability.Disabled, "waiting only for installed capabilities");
        Console.WriteLine($"COMPATIBILITY_STATUS_CHECKS_OK: {checks} assertions; {zh.Count} matching localization keys; missing/unsupported/single/partial/pending/unavailable/negotiated states in both languages");
    }

    /// <summary>保持测试输入直观，调用的仍是生产状态推导。</summary>
    private static ImproveGameCompatibilityStatus Status(string version, bool supported, bool multiplayer,
        ImproveGameCapability single, ImproveGameCapability local, bool server, bool timeout, ImproveGameCapability negotiated)
        => ImproveGameCompatibilityStatus.Create(version, supported, multiplayer, single, local, server, timeout, negotiated);

    /// <summary>使用 tModLoader 自带 Hjson 解析器读取真实资源。</summary>
    private static Dictionary<string, string> Load(string path)
    {
        var result = new Dictionary<string, string>();
        Flatten(HjsonValue.Load(path), string.Empty, result);
        return result;
    }

    /// <summary>展开与 tModLoader 本地化相同的点号路径。</summary>
    private static void Flatten(JsonValue value, string prefix, Dictionary<string, string> output)
    {
        if (value is JsonObject obj)
        { foreach (var pair in obj) Flatten(pair.Value, prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key, output); }
        else output.Add(prefix, (string)value);
    }

    /// <summary>比较占位符多重集合，避免漏译一个参数或重复使用错误参数。</summary>
    private static string[] Placeholders(string text) => Regex.Matches(text, @"\{(\d+)(?:[^}]*)\}")
        .Select(match => match.Groups[1].Value).Order().ToArray();

    /// <summary>报告具体失败条件。</summary>
    private static void Check(bool passed, string name) { if (!passed) throw new Exception(name); checks++; }
}

namespace Terraria.Localization
{
    /// <summary>只替代游戏语言全局状态，提示逻辑和实际资源均使用生产文件。</summary>
    internal static class Language
    {
        internal static Dictionary<string, string> Values;
        /// <summary>缺失键直接失败，使用当前语言的完整原文格式化。</summary>
        internal static string GetTextValue(string key, params object[] args)
            => string.Format(CultureInfo.InvariantCulture, Values[key.Replace("Mods.ComprehensiveStatistics.", string.Empty)], args);
    }
}
