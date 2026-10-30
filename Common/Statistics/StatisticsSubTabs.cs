using System;
using System.Collections.Generic;
using System.Linq;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>定义主页面下稳定的子页身份与本地化键，不依赖按钮序号或语言。</summary>
    internal sealed record StatisticsSubTab(string MainTab, string Id, string GroupKey)
    {
        internal string PageKey => MainTab + "/" + Id;
        internal string LabelKey => "Mods.ComprehensiveStatistics.UI.SubTabs." + MainTab + "." + Id;
    }

    /// <summary>共用子页注册及旧物块视图迁移规则，未来主页面沿用同一注册方式。</summary>
    internal static class StatisticsSubTabs
    {
        private static readonly StatisticsSubTab[] blocks =
        {
            new("Blocks", "Tiles", "Mods.ComprehensiveStatistics.UI.Blocks.SolidTilesGroup"),
            new("Blocks", "Walls", "Mods.ComprehensiveStatistics.UI.Blocks.BackgroundWallsGroup"),
            new("Blocks", "Wiring", "Mods.ComprehensiveStatistics.UI.Blocks.WiringComponentsGroup"),
            new("Blocks", "Liquids", "Mods.ComprehensiveStatistics.UI.Blocks.LiquidsGroup")
        };

        /// <summary>返回主页面注册的固定子页；未注册页面不预留子页栏。</summary>
        internal static IReadOnlyList<StatisticsSubTab> For(string mainTab) => mainTab == "Blocks" ? blocks : Array.Empty<StatisticsSubTab>();

        /// <summary>未知或旧档缺少的子页身份回退到有效默认页。</summary>
        internal static string Resolve(string mainTab, string id) => For(mainTab).FirstOrDefault(p => p.Id == id)?.Id
            ?? For(mainTab).FirstOrDefault()?.Id;

        /// <summary>按生涯与世界分别迁移旧状态，已保存的新子页绝不被旧主页面覆盖。</summary>
        internal static void Migrate(CharacterStatisticsData data)
        {
            // 一次索引全部范围，避免世界切片多时为每个子页反复扫描整个状态表。
            var oldFolds = data.ExpansionStates.Entries.Where(p => p.Key.TabKey == "Blocks").ToLookup(p => p.Key.WorldId);
            var existing = data.ListViewStates.Entries.Keys.Concat(data.ExpansionStates.Entries.Keys
                .Select(k => (Tab: k.TabKey, k.WorldId))).ToHashSet();
            var contexts = data.ListViewStates.Entries.Keys.Where(k => k.Tab == "Blocks").Select(k => k.WorldId)
                .Concat(oldFolds.Select(group => group.Key)).Distinct().ToArray();
            foreach (Guid? world in contexts)
            {
                foreach (StatisticsSubTab page in blocks.Take(3))
                {
                    if (existing.Contains((page.PageKey, world))) continue;
                    var old = data.ListViewStates.GetTabState("Blocks", world);
                    data.ListViewStates.SetTabState(page.PageKey, world,
                        new StatisticsListViewState(old.SortColumn, old.SortDirection, 0f));
                    string root = "\u001f" + page.GroupKey;
                    foreach (var pair in oldFolds[world].Where(p => p.Key.GroupPath == root
                        || p.Key.GroupPath.StartsWith(root + "\u001f", StringComparison.Ordinal)))
                        data.ExpansionStates.AddLoadedState(new StatisticsExpansionStateKey(page.PageKey, pair.Key.GroupPath, world), pair.Value);
                }
            }
        }
    }
}
