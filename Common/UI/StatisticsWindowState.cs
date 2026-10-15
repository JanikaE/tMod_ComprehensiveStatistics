using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Systems;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.UI.Elements;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI
{
    /// <summary>
    /// 统计主窗口，负责九个 Tab 的导航、响应式布局以及各页内容的装载。
    /// </summary>
    internal sealed class StatisticsWindowState : UIState
    {
        private const int TabCount = 9;
        private const float WindowMargin = 12f;
        private const float WindowPadding = 12f;
        private const float HeaderHeight = 40f;
        private const float ColumnGap = 10f;
        private const float TabGap = 4f;
        private const int ItemsTabIndex = 2;
        private const int SurvivalTabIndex = 8;
        private const float ToolbarTop = 38f;
        private const float ToolbarHeight = 28f;
        private const float ListTop = 74f;

        // 顺序同时决定导航显示顺序，键名用于拼接各 Tab 的本地化路径。
        private static readonly string[] TabLocalizationKeys =
        {
            "Overview",
            "Blocks",
            "Items",
            "Interactions",
            "NPCs",
            "Economy",
            "Fishing",
            "Movement",
            "Survival"
        };

        // 第一版只有概览和 NPC 页会展示世界公共统计；其余页面记录的都是角色行为。
        private static readonly bool[] TabSupportsWorldPublicScope =
        {
            true,
            false,
            false,
            false,
            true,
            false,
            false,
            false,
            false
        };

        private readonly List<UIPanel> tabButtons = new List<UIPanel>(TabCount);

        private UIPanel window;
        private UIPanel navigationPanel;
        private UIPanel contentPanel;
        private UIText windowTitle;
        private UIText contentTitle;
        private UIText contentDescription;
        private UIText statisticsStartedAt;
        private StatisticsToolbar toolbar;
        private VirtualizedStatisticsList demoList;
        private VirtualizedStatisticsList survivalList;
        private UIImageButton closeButton;
        private int selectedTabIndex;
        private Point lastViewportSize;
        private bool isDemoListAttached;
        private bool isSurvivalListAttached;

        internal bool IsWritingSearchText => toolbar?.IsWritingSearchText == true;

        /// <summary>
        /// 开始一次新的窗口会话，恢复各 Tab 列表默认的折叠与无排序状态。
        /// </summary>
        internal void ResetTransientViewState()
        {
            demoList?.ResetTransientState();
            survivalList?.ResetTransientState();
        }

        public override void OnInitialize()
        {
            // 外层窗口只保存公共标题和关闭按钮，导航与内容分别放入左右两列。
            window = new UIPanel();
            window.HAlign = 0.5f;
            window.VAlign = 0.5f;
            window.SetPadding(WindowPadding);
            window.BackgroundColor = new Color(33, 43, 79, 245);
            window.BorderColor = new Color(89, 116, 213, 255);
            Append(window);

            windowTitle = new UIText(Language.GetText("Mods.ComprehensiveStatistics.UI.WindowTitle"), 1.15f);
            windowTitle.Left.Set(4f, 0f);
            windowTitle.Top.Set(4f, 0f);
            window.Append(windowTitle);

            statisticsStartedAt = new UIText(string.Empty, 0.68f);
            statisticsStartedAt.Top.Set(8f, 0f);
            statisticsStartedAt.TextColor = new Color(151, 170, 214);
            window.Append(statisticsStartedAt);

            Asset<Texture2D> closeTexture = ModContent.Request<Texture2D>("Terraria/Images/UI/ButtonDelete");
            closeButton = new UIImageButton(closeTexture);
            closeButton.Width.Set(22f, 0f);
            closeButton.Height.Set(22f, 0f);
            closeButton.Left.Set(-26f, 1f);
            closeButton.Top.Set(0f, 0f);
            closeButton.OnLeftClick += CloseWindow;
            window.Append(closeButton);

            navigationPanel = new UIPanel();
            navigationPanel.SetPadding(4f);
            navigationPanel.BackgroundColor = new Color(22, 29, 55, 230);
            navigationPanel.BorderColor = new Color(65, 84, 150, 255);
            window.Append(navigationPanel);

            for (int index = 0; index < TabCount; index++)
            {
                // 复制循环变量，确保每个点击事件绑定到自己的 Tab。
                int tabIndex = index;
                UIPanel tabButton = new UIPanel();
                tabButton.SetPadding(4f);
                tabButton.OnLeftClick += (evt, listeningElement) => SelectTab(tabIndex);

                UIText tabLabel = new UIText(GetTabText(index, "Title"), 0.82f);
                tabLabel.HAlign = 0.5f;
                tabLabel.VAlign = 0.5f;
                tabButton.Append(tabLabel);

                tabButtons.Add(tabButton);
                navigationPanel.Append(tabButton);
            }

            contentPanel = new UIPanel();
            contentPanel.SetPadding(12f);
            contentPanel.BackgroundColor = new Color(24, 32, 61, 235);
            contentPanel.BorderColor = new Color(65, 84, 150, 255);
            window.Append(contentPanel);

            contentTitle = new UIText(string.Empty, 1.08f);
            contentTitle.Left.Set(2f, 0f);
            contentTitle.Top.Set(2f, 0f);
            contentPanel.Append(contentTitle);

            contentDescription = new UIText(string.Empty, 0.76f);
            contentDescription.Top.Set(6f, 0f);
            contentDescription.TextColor = new Color(190, 200, 225);
            contentPanel.Append(contentDescription);

            survivalList = new VirtualizedStatisticsList(
                CreateSurvivalGroups(data: null),
                StatisticsMetricDisplay.Mixed);
            survivalList.Top.Set(ListTop, 0f);
            survivalList.Width.Set(0f, 1f);
            survivalList.Height.Set(-ListTop, 1f);

            demoList = CreateDemoList();
            demoList.Top.Set(ListTop, 0f);
            demoList.Width.Set(0f, 1f);
            demoList.Height.Set(-ListTop, 1f);

            toolbar = new StatisticsToolbar(
                HandleScopeChanged,
                HandleFiltersChanged);
            toolbar.Top.Set(ToolbarTop, 0f);
            toolbar.Width.Set(0f, 1f);
            toolbar.Height.Set(ToolbarHeight, 0f);
            contentPanel.Append(toolbar);

            SelectTab(0, playSound: false);
            HandleFiltersChanged(
                string.Empty,
                StatisticsContentSourceFilter.All);
            UpdateResponsiveLayout(force: true);
        }

        public override void OnActivate()
        {
            base.OnActivate();

            // UIState 在 ModSystem.Load 中会预激活一次；窗口实际打开时重新解析格式化文本，
            // 避免首次显示仍保留尚未注册时得到的本地化键名。
            if (contentTitle != null && toolbar != null)
            {
                UpdateWindowHeaderLayout();
                UpdatePageHeading();
            }
        }

        public override void Update(GameTime gameTime)
        {
            // 窗口为模态界面：阻止点击窗口及其背景时使用手持物品或与世界交互。
            Main.LocalPlayer.mouseInterface = true;

            UpdateResponsiveLayout();
            UpdateTabColors();

            if (closeButton.IsMouseHovering)
            {
                Main.hoverItemName = Language.GetTextValue("Mods.ComprehensiveStatistics.UI.CloseButtonTooltip");
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// 选择有效页面，并同步范围能力、内容挂载和选中外观。
        /// </summary>
        private void SelectTab(int tabIndex, bool playSound = true)
        {
            if (tabIndex < 0 || tabIndex >= TabCount)
            {
                return;
            }

            bool changed = selectedTabIndex != tabIndex;
            selectedTabIndex = tabIndex;
            toolbar.SetWorldPublicScopeAvailable(TabSupportsWorldPublicScope[tabIndex]);
            UpdatePageHeading();
            // A4 临时列表仍只用于物品页；正式生存总计单独挂载到生存页。
            SetDemoListVisible(tabIndex == ItemsTabIndex);
            SetSurvivalTotalsVisible(tabIndex == SurvivalTabIndex);
            UpdateTabColors();

            if (changed && playSound)
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }

        /// <summary>
        /// 根据选中和悬停状态刷新全部导航按钮颜色。
        /// </summary>
        private void UpdateTabColors()
        {
            // 选中状态优先于悬停状态，保证当前页面始终有明确视觉标识。
            for (int index = 0; index < tabButtons.Count; index++)
            {
                UIPanel tabButton = tabButtons[index];
                if (index == selectedTabIndex)
                {
                    tabButton.BackgroundColor = new Color(73, 96, 178, 245);
                    tabButton.BorderColor = new Color(130, 158, 238, 255);
                }
                else if (tabButton.IsMouseHovering)
                {
                    tabButton.BackgroundColor = new Color(52, 69, 128, 235);
                    tabButton.BorderColor = new Color(93, 119, 207, 255);
                }
                else
                {
                    tabButton.BackgroundColor = new Color(38, 49, 91, 225);
                    tabButton.BorderColor = new Color(65, 84, 150, 255);
                }
            }
        }

        /// <summary>
        /// 根据逻辑视口尺寸重新计算窗口、双栏和导航按钮布局。
        /// </summary>
        private void UpdateResponsiveLayout(bool force = false)
        {
            // Terraria UI 使用缩放后的逻辑尺寸，布局计算不能直接使用物理像素。
            float viewportWidth = Main.screenWidth / Main.UIScale;
            float viewportHeight = Main.screenHeight / Main.UIScale;
            Point viewportSize = new Point((int)viewportWidth, (int)viewportHeight);

            if (!force && viewportSize == lastViewportSize)
            {
                return;
            }

            lastViewportSize = viewportSize;

            // 窗口永远不超过可用 UI 画布；极小画布下压缩控件但不让窗口越界。
            float availableWidth = MathHelper.Max(1f, viewportWidth - WindowMargin * 2f);
            float availableHeight = MathHelper.Max(1f, viewportHeight - WindowMargin * 2f);
            float windowWidth = MathHelper.Min(920f, MathHelper.Max(460f, viewportWidth * 0.82f));
            float windowHeight = MathHelper.Min(680f, MathHelper.Max(360f, viewportHeight * 0.82f));
            windowWidth = MathHelper.Min(windowWidth, availableWidth);
            windowHeight = MathHelper.Min(windowHeight, availableHeight);

            window.Width.Set(windowWidth, 0f);
            window.Height.Set(windowHeight, 0f);

            float innerWidth = MathHelper.Max(1f, windowWidth - WindowPadding * 2f);
            float innerHeight = MathHelper.Max(1f, windowHeight - WindowPadding * 2f);
            float bodyHeight = MathHelper.Max(1f, innerHeight - HeaderHeight);
            float navigationWidth = MathHelper.Clamp(innerWidth * 0.24f, 124f, 180f);
            navigationWidth = MathHelper.Min(navigationWidth, MathHelper.Max(1f, innerWidth - ColumnGap - 80f));

            navigationPanel.Left.Set(0f, 0f);
            navigationPanel.Top.Set(HeaderHeight, 0f);
            navigationPanel.Width.Set(navigationWidth, 0f);
            navigationPanel.Height.Set(bodyHeight, 0f);

            contentPanel.Left.Set(navigationWidth + ColumnGap, 0f);
            contentPanel.Top.Set(HeaderHeight, 0f);
            contentPanel.Width.Set(-(navigationWidth + ColumnGap), 1f);
            contentPanel.Height.Set(bodyHeight, 0f);

            float navigationInnerHeight = MathHelper.Max(1f, bodyHeight - 8f);
            // 高度不足时同步缩小间距，确保九个 Tab 仍全部留在窗口内。
            float effectiveTabGap = navigationInnerHeight >= 120f ? TabGap : 1f;
            float tabHeight = MathHelper.Max(
                1f,
                (navigationInnerHeight - effectiveTabGap * (TabCount - 1)) / TabCount);
            for (int index = 0; index < tabButtons.Count; index++)
            {
                UIPanel tabButton = tabButtons[index];
                tabButton.Top.Set(index * (tabHeight + effectiveTabGap), 0f);
                tabButton.Width.Set(0f, 1f);
                tabButton.Height.Set(tabHeight, 0f);
            }

            Recalculate();
        }

        /// <summary>
        /// 获取指定页面字段的本地化文本。
        /// </summary>
        private static LocalizedText GetTabText(int tabIndex, string field)
        {
            return Language.GetText($"Mods.ComprehensiveStatistics.UI.Tabs.{TabLocalizationKeys[tabIndex]}.{field}");
        }

        /// <summary>
        /// 在查看范围变化后刷新页头及其范围相关数据。
        /// </summary>
        private void HandleScopeChanged(StatisticsViewScope scope)
        {
            UpdatePageHeading();
        }

        /// <summary>
        /// 将公共筛选状态同步到所有可切换的统计列表。
        /// </summary>
        private void HandleFiltersChanged(
            string searchQuery,
            StatisticsContentSourceFilter sourceFilter)
        {
            demoList.SetFilters(searchQuery, sourceFilter);
            survivalList.SetFilters(searchQuery, sourceFilter);
        }

        /// <summary>
        /// 刷新当前页面标题、说明和统计起始时间。
        /// </summary>
        private void UpdatePageHeading()
        {
            StatisticsViewScope scope = toolbar?.Scope ?? StatisticsViewScope.CharacterCareer;
            string titleText = GetTabText(selectedTabIndex, "Title").Value;
            contentTitle.SetText(titleText);
            contentDescription.SetText(GetTabText(selectedTabIndex, "Description"));

            float titleWidth = FontAssets.MouseText.Value.MeasureString(titleText).X * 1.08f;
            float descriptionLeft = titleWidth + 18f;
            contentDescription.Left.Set(descriptionLeft, 0f);
            contentDescription.Width.Set(-descriptionLeft - 4f, 1f);
            UpdateStatisticsStartedAt(scope);
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 根据本地化窗口标题宽度安排统计起始时间的位置。
        /// </summary>
        private void UpdateWindowHeaderLayout()
        {
            string titleText = Language.GetTextValue("Mods.ComprehensiveStatistics.UI.WindowTitle");
            windowTitle.SetText(titleText);
            float titleWidth = FontAssets.MouseText.Value.MeasureString(titleText).X * 1.15f;
            statisticsStartedAt.Left.Set(4f + titleWidth + 18f, 0f);
            window.Recalculate();
        }

        /// <summary>
        /// 显示当前查看范围的统计起始时间，并刷新范围数据。
        /// </summary>
        private void UpdateStatisticsStartedAt(StatisticsViewScope scope)
        {
            DateTimeOffset? startedAtUtc = null;

            if (!Main.gameMenu && Main.LocalPlayer?.active == true)
            {
                if (scope == StatisticsViewScope.CurrentWorldPublic)
                {
                    startedAtUtc = ModContent.GetInstance<StatisticsWorldSystem>().Data?.StartedAtUtc;
                }
                else
                {
                    StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
                    startedAtUtc = scope == StatisticsViewScope.CharacterCurrentWorld
                        ? statisticsPlayer.CurrentWorldSlice?.StartedAtUtc
                        : statisticsPlayer.Data?.Career.StartedAtUtc;
                }
            }

            if (startedAtUtc.HasValue)
            {
                statisticsStartedAt.SetText(Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.StatisticsStartedAt",
                    startedAtUtc.Value.ToLocalTime()));
            }
            else
            {
                statisticsStartedAt.SetText(
                    Language.GetText("Mods.ComprehensiveStatistics.UI.StatisticsStartUnavailable"));
            }

            UpdateSurvivalTotals(scope);
        }

        /// <summary>
        /// 从当前角色范围重新生成生存页的统计分组。
        /// </summary>
        private void UpdateSurvivalTotals(StatisticsViewScope scope)
        {
            if (selectedTabIndex != SurvivalTabIndex)
            {
                return;
            }

            if (scope == StatisticsViewScope.CurrentWorldPublic)
            {
                survivalList.SetGroups(CreateSurvivalGroups(data: null));
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            survivalList.SetGroups(CreateSurvivalGroups(scopeData));
        }

        /// <summary>
        /// 按需挂载或移除物品页的临时验证列表。
        /// </summary>
        private void SetDemoListVisible(bool visible)
        {
            if (visible == isDemoListAttached)
            {
                return;
            }

            if (visible)
            {
                // 不可见时从 UI 树移除，避免隐藏列表继续接收输入和执行更新。
                contentPanel.Append(demoList);
            }
            else
            {
                demoList.Remove();
            }

            isDemoListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 按需挂载生存统计列表，并在显示时刷新其数据。
        /// </summary>
        private void SetSurvivalTotalsVisible(bool visible)
        {
            if (visible == isSurvivalListAttached)
            {
                if (visible)
                {
                    UpdateSurvivalTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(survivalList);
                UpdateSurvivalTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                survivalList.Remove();
            }

            isSurvivalListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 从一个统计范围创建按伤害性质归类的三列生存统计。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateSurvivalGroups(StatisticsData data)
        {
            long totalDeaths = data?.GetValue(StatisticKeys.PlayerDeaths).Events ?? 0L;
            StatisticsListGroup directGroup = CreateDirectSurvivalGroup(
                data,
                out long directDeaths);
            StatisticsListGroup continuousGroup = CreateContinuousSurvivalGroup(
                data,
                out long continuousDeaths);

            long unknownDeaths = data?
                .GetValue(StatisticKeys.UnknownDeathSource).Events ?? 0L;
            long classifiedDeaths = SaturatingAdd(directDeaths, continuousDeaths);
            classifiedDeaths = SaturatingAdd(classifiedDeaths, unknownDeaths);
            // 较早版本只有死亡总数；无法回溯到直接或持续伤害的差额单列保留。
            if (totalDeaths > classifiedDeaths)
            {
                unknownDeaths = SaturatingAdd(
                    unknownDeaths,
                    totalDeaths - classifiedDeaths);
            }

            StatisticsListGroup unknownGroup = new StatisticsListGroup(
                "Mods.ComprehensiveStatistics.UI.Survival.UnknownDeathSource",
                new[]
                {
                    new StatisticsListItem(
                        "Mods.ComprehensiveStatistics.UI.Survival.UnknownDeathSource",
                        itemType: 0,
                        icon: null,
                        events: unknownDeaths,
                        units: 0L,
                        amount: 0L,
                        source: StatisticsContentSource.Neutral,
                        nameIsLocalizationKey: true)
                },
                nameIsLocalizationKey: true,
                metricDisplay: StatisticsMetricDisplay.Mixed,
                showChildren: false);

            return new[] { directGroup, continuousGroup, unknownGroup };
        }

        /// <summary>
        /// 创建直接伤害分类，并在同一行合并对应的死亡次数与掉血总量。
        /// </summary>
        private static StatisticsListGroup CreateDirectSurvivalGroup(
            StatisticsData data,
            out long totalDeaths)
        {
            Array sources = Enum.GetValues(typeof(DirectDamageSourceKind));
            Dictionary<DirectDamageSourceKind, long> deaths =
                new Dictionary<DirectDamageSourceKind, long>(sources.Length);
            Dictionary<DirectDamageSourceKind, long> amounts =
                new Dictionary<DirectDamageSourceKind, long>(sources.Length);
            long classifiedAmount = 0L;
            totalDeaths = 0L;

            foreach (DirectDamageSourceKind source in sources)
            {
                long events = data?
                    .GetValue(StatisticKeys.GetDirectDeathSourceKey(source)).Events ?? 0L;
                long amount = data?
                    .GetValue(StatisticKeys.GetDirectDamageSourceKey(source)).Amount ?? 0L;
                deaths[source] = events;
                amounts[source] = amount;
                totalDeaths = SaturatingAdd(totalDeaths, events);
                classifiedAmount = SaturatingAdd(classifiedAmount, amount);
            }

            // C1 只有直接掉血总量；无法追溯来源的历史差额归入直接伤害的“其他”。
            long directTotal = data?.GetValue(StatisticKeys.DirectLifeLost).Amount ?? 0L;
            if (directTotal > classifiedAmount)
            {
                amounts[DirectDamageSourceKind.Other] = SaturatingAdd(
                    amounts[DirectDamageSourceKind.Other],
                    directTotal - classifiedAmount);
            }

            List<StatisticsListItem> items = new List<StatisticsListItem>(sources.Length);
            foreach (DirectDamageSourceKind source in sources)
            {
                // NPC 来源具有稳定内容键明细，由子分组承载，避免在父级重复求和。
                if (source == DirectDamageSourceKind.NpcContact)
                {
                    continue;
                }

                items.Add(new StatisticsListItem(
                    $"Mods.ComprehensiveStatistics.UI.Survival.DirectDamageSources.{source}",
                    itemType: 0,
                    icon: null,
                    events: deaths[source],
                    units: 0L,
                    amount: amounts[source],
                    source: StatisticsContentSource.Neutral,
                    nameIsLocalizationKey: true));
            }

            List<StatisticsListGroup> childGroups = new List<StatisticsListGroup>();
            List<StatisticsListItem> npcItems = CreateNpcSurvivalItems(
                data,
                deaths[DirectDamageSourceKind.NpcContact],
                amounts[DirectDamageSourceKind.NpcContact]);
            if (npcItems.Count > 0)
            {
                childGroups.Add(new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Survival.DirectDamageSources.NpcContact",
                    npcItems,
                    isExpanded: false,
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.Mixed));
            }

            return new StatisticsListGroup(
                "Mods.ComprehensiveStatistics.UI.Survival.DirectDamageGroup",
                items,
                nameIsLocalizationKey: true,
                childGroups: childGroups,
                metricDisplay: StatisticsMetricDisplay.Mixed);
        }

        /// <summary>
        /// 合并 NPC 接触伤害的死亡与掉血明细，并保留只有聚合值的旧版数据。
        /// </summary>
        private static List<StatisticsListItem> CreateNpcSurvivalItems(
            StatisticsData data,
            long npcContactDeaths,
            long npcContactAmount)
        {
            Dictionary<StableContentKey, long> deaths = new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, long> amounts = new Dictionary<StableContentKey, long>();
            HashSet<StableContentKey> contents = new HashSet<StableContentKey>();
            List<StatisticsListItem> items = new List<StatisticsListItem>();
            long detailedDeaths = 0L;
            long detailedAmount = 0L;
            string deathStatisticId = StatisticKeys
                .GetDirectDeathSourceKey(DirectDamageSourceKind.NpcContact)
                .StatisticId;
            string damageStatisticId = StatisticKeys
                .GetDirectDamageSourceKey(DirectDamageSourceKind.NpcContact)
                .StatisticId;

            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue)
                    {
                        continue;
                    }

                    StableContentKey content = pair.Key.Content.Value;
                    if (string.Equals(pair.Key.StatisticId, deathStatisticId, StringComparison.Ordinal)
                        && pair.Value.Events > 0L)
                    {
                        deaths[content] = pair.Value.Events;
                        contents.Add(content);
                    }
                    else if (string.Equals(pair.Key.StatisticId, damageStatisticId, StringComparison.Ordinal)
                        && pair.Value.Amount > 0L)
                    {
                        amounts[content] = pair.Value.Amount;
                        contents.Add(content);
                    }
                }

                foreach (StableContentKey content in contents)
                {
                    deaths.TryGetValue(content, out long events);
                    amounts.TryGetValue(content, out long amount);
                    items.Add(CreateContentItem(content, isNpc: true, events, amount));
                    detailedDeaths = SaturatingAdd(detailedDeaths, events);
                    detailedAmount = SaturatingAdd(detailedAmount, amount);
                }
            }

            long unclassifiedDeaths = Math.Max(0L, npcContactDeaths - detailedDeaths);
            long unclassifiedAmount = Math.Max(0L, npcContactAmount - detailedAmount);
            if (unclassifiedDeaths > 0L || unclassifiedAmount > 0L)
            {
                items.Add(new StatisticsListItem(
                    "Mods.ComprehensiveStatistics.UI.Survival.UnclassifiedNpcDamage",
                    itemType: 0,
                    icon: null,
                    events: unclassifiedDeaths,
                    units: 0L,
                    amount: unclassifiedAmount,
                    source: StatisticsContentSource.Neutral,
                    nameIsLocalizationKey: true));
            }

            return items;
        }

        /// <summary>
        /// 创建持续伤害分类，并按 Debuff 合并死亡次数与掉血总量。
        /// </summary>
        private static StatisticsListGroup CreateContinuousSurvivalGroup(
            StatisticsData data,
            out long totalDeaths)
        {
            Dictionary<StableContentKey, long> deaths = new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, long> amounts = new Dictionary<StableContentKey, long>();
            HashSet<StableContentKey> contents = new HashSet<StableContentKey>();
            List<StatisticsListItem> items = new List<StatisticsListItem>();
            totalDeaths = 0L;
            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue)
                    {
                        continue;
                    }

                    StableContentKey content = pair.Key.Content.Value;
                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ContinuousDamageDebuffDeathStatisticId,
                        StringComparison.Ordinal) && pair.Value.Events > 0L)
                    {
                        deaths[content] = pair.Value.Events;
                        contents.Add(content);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ContinuousDamageDebuffStatisticId,
                        StringComparison.Ordinal) && pair.Value.Amount > 0L)
                    {
                        amounts[content] = pair.Value.Amount;
                        contents.Add(content);
                    }
                }

                foreach (StableContentKey content in contents)
                {
                    deaths.TryGetValue(content, out long events);
                    amounts.TryGetValue(content, out long amount);
                    items.Add(CreateContentItem(content, isNpc: false, events, amount));
                    totalDeaths = SaturatingAdd(totalDeaths, events);
                }
            }

            long multipleDeaths = data?
                .GetValue(StatisticKeys.MultipleContinuousDamageDeaths).Events ?? 0L;
            long unknownDeaths = data?
                .GetValue(StatisticKeys.UnknownContinuousLifeLossDeaths).Events ?? 0L;
            long multipleAmount = data?
                .GetValue(StatisticKeys.MultipleContinuousDamageEffects).Amount ?? 0L;
            long unknownAmount = data?
                .GetValue(StatisticKeys.UnknownContinuousLifeLoss).Amount ?? 0L;
            totalDeaths = SaturatingAdd(totalDeaths, multipleDeaths);
            totalDeaths = SaturatingAdd(totalDeaths, unknownDeaths);

            items.Add(new StatisticsListItem(
                "Mods.ComprehensiveStatistics.UI.Survival.MultipleContinuousDamageEffects",
                itemType: 0,
                icon: null,
                events: multipleDeaths,
                units: 0L,
                amount: multipleAmount,
                source: StatisticsContentSource.Neutral,
                nameIsLocalizationKey: true));
            items.Add(new StatisticsListItem(
                "Mods.ComprehensiveStatistics.UI.Survival.UnknownContinuousLifeLoss",
                itemType: 0,
                icon: null,
                events: unknownDeaths,
                units: 0L,
                amount: unknownAmount,
                source: StatisticsContentSource.Neutral,
                nameIsLocalizationKey: true));
            return new StatisticsListGroup(
                "Mods.ComprehensiveStatistics.UI.Survival.ContinuousLifeLossGroup",
                items,
                nameIsLocalizationKey: true,
                metricDisplay: StatisticsMetricDisplay.Mixed);
        }

        /// <summary>
        /// 将一个 NPC 或 Debuff 稳定内容键转换为合并后的三列叶子项。
        /// </summary>
        private static StatisticsListItem CreateContentItem(
            StableContentKey content,
            bool isNpc,
            long events,
            long amount)
        {
            string name;
            int iconType = 0;
            Asset<Texture2D> icon = null;
            StatisticsIconKind iconKind = StatisticsIconKind.None;
            if (isNpc)
            {
                bool resolved = content.TryResolveNpcType(out int npcType);
                name = resolved
                    ? Lang.GetNPCNameValue(npcType)
                    : Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Survival.MissingNpcContent",
                        content.ToString());
                if (resolved)
                {
                    iconType = npcType;
                    icon = TextureAssets.Npc[npcType];
                    iconKind = StatisticsIconKind.Npc;
                }
            }
            else
            {
                bool resolved = content.TryResolveBuffType(out int buffType);
                name = resolved
                    ? Lang.GetBuffName(buffType)
                    : Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Survival.MissingBuffContent",
                        content.ToString());
                if (resolved)
                {
                    iconType = buffType;
                    icon = TextureAssets.Buff[buffType];
                    iconKind = StatisticsIconKind.Buff;
                }
            }

            return new StatisticsListItem(
                name,
                itemType: iconType,
                icon: icon,
                events: events,
                units: 0L,
                amount: amount,
                source: GetContentSource(content),
                iconKind: iconKind);
        }

        /// <summary>
        /// 安全累加 UI 汇总值，避免超大历史数据发生整数溢出。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        /// <summary>
        /// 根据稳定内容键判断条目来自原版还是模组。
        /// </summary>
        private static StatisticsContentSource GetContentSource(StableContentKey content)
        {
            return string.Equals(content.ModName, "Terraria", StringComparison.Ordinal)
                ? StatisticsContentSource.Vanilla
                : StatisticsContentSource.Modded;
        }

        /// <summary>
        /// 创建用于验证虚拟化、排序和筛选行为的确定性临时物品列表。
        /// </summary>
        private static VirtualizedStatisticsList CreateDemoList()
        {
            // A4 仅使用确定性的临时数据验证组件；正式统计接入后会整体替换。
            List<StatisticsListItem> pickupItems = new List<StatisticsListItem>();
            List<StatisticsListItem> useItems = new List<StatisticsListItem>();

            for (int itemType = 1; itemType <= 140; itemType++)
            {
                string itemName = Lang.GetItemNameValue(itemType);
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                // 每 17 项保留一个零值，确保列表始终不会渲染零值条目。
                bool isZeroSample = itemType % 17 == 0;
                long events = isZeroSample ? 0L : 1L + itemType * 17L % 251L;
                long units = isZeroSample ? 0L : events + itemType * 113L % 12500L;
                StatisticsListItem item = new StatisticsListItem(
                    itemName,
                    itemType,
                    ModContent.Request<Texture2D>(
                        $"Terraria/Images/Item_{itemType}",
                        // 临时数据立即加载图标，避免虚拟行首次出现时短暂缺图。
                        AssetRequestMode.ImmediateLoad),
                    events,
                    units);

                if (itemType <= 70)
                {
                    pickupItems.Add(item);
                }
                else
                {
                    useItems.Add(item);
                }
            }

            // 临时模组来源条目不伪装成原版物品，用于独立验证来源筛选和无图标行。
            pickupItems.Add(new StatisticsListItem(
                "Mods.ComprehensiveStatistics.UI.List.DemoModdedPickup",
                itemType: 0,
                icon: null,
                events: 12L,
                units: 345L,
                source: StatisticsContentSource.Modded,
                nameIsLocalizationKey: true));
            useItems.Add(new StatisticsListItem(
                "Mods.ComprehensiveStatistics.UI.List.DemoModdedUse",
                itemType: 0,
                icon: null,
                events: 0L,
                units: 0L,
                source: StatisticsContentSource.Modded,
                nameIsLocalizationKey: true));

            StatisticsListGroup[] groups =
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.List.DemoPickupGroup",
                    pickupItems,
                    nameIsLocalizationKey: true),
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.List.DemoUseGroup",
                    useItems,
                    isExpanded: false,
                    nameIsLocalizationKey: true)
            };

            return new VirtualizedStatisticsList(groups);
        }

        /// <summary>
        /// 将关闭按钮点击转交给统一统计 UI 系统。
        /// </summary>
        private static void CloseWindow(UIMouseEvent evt, UIElement listeningElement)
        {
            ModContent.GetInstance<StatisticsUISystem>().Close();
        }
    }
}
