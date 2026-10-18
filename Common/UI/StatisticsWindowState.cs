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
        private const int NpcsTabIndex = 4;
        private const int EconomyTabIndex = 5;
        private const int MovementTabIndex = 7;
        private const int SurvivalTabIndex = 8;
        private const float ToolbarTop = 38f;
        private const float ToolbarHeight = 28f;
        private const float ListTop = 74f;
        private const float ExpandCollapseButtonWidth = 94f;

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
        private HeaderActionButton expandCollapseButton;
        private StatisticsToolbar toolbar;
        private VirtualizedStatisticsList itemList;
        private VirtualizedStatisticsList npcList;
        private VirtualizedStatisticsList economyList;
        private VirtualizedStatisticsList movementList;
        private VirtualizedStatisticsList survivalList;
        private UIImageButton closeButton;
        private int selectedTabIndex;
        private Point lastViewportSize;
        private bool isItemListAttached;
        private bool isNpcListAttached;
        private bool isEconomyListAttached;
        private bool isMovementListAttached;
        private bool isSurvivalListAttached;

        internal bool IsWritingSearchText => toolbar?.IsWritingSearchText == true;

        /// <summary>
        /// 开始一次新的窗口会话，恢复各 Tab 列表默认的折叠与无排序状态。
        /// </summary>
        internal void ResetTransientViewState()
        {
            itemList?.ResetTransientState();
            npcList?.ResetTransientState();
            economyList?.ResetTransientState();
            movementList?.ResetTransientState();
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

            expandCollapseButton = new HeaderActionButton(ToggleAllGroups);
            expandCollapseButton.Left.Set(-ExpandCollapseButtonWidth, 1f);
            expandCollapseButton.Top.Set(0f, 0f);
            expandCollapseButton.Width.Set(ExpandCollapseButtonWidth, 0f);
            expandCollapseButton.Height.Set(28f, 0f);
            contentPanel.Append(expandCollapseButton);

            contentTitle = new UIText(string.Empty, 1.08f);
            contentTitle.Left.Set(2f, 0f);
            contentTitle.Top.Set(2f, 0f);
            contentPanel.Append(contentTitle);

            contentDescription = new UIText(string.Empty, 0.76f);
            contentDescription.Top.Set(6f, 0f);
            contentDescription.TextColor = new Color(190, 200, 225);
            contentDescription.DynamicallyScaleDownToWidth = true;
            contentPanel.Append(contentDescription);

            survivalList = new VirtualizedStatisticsList(
                CreateSurvivalGroups(data: null),
                StatisticsMetricDisplay.Mixed);
            survivalList.Top.Set(ListTop, 0f);
            survivalList.Width.Set(0f, 1f);
            survivalList.Height.Set(-ListTop, 1f);

            movementList = new VirtualizedStatisticsList(
                CreateMovementGroups(data: null),
                StatisticsMetricDisplay.Distance);
            movementList.Top.Set(ListTop, 0f);
            movementList.Width.Set(0f, 1f);
            movementList.Height.Set(-ListTop, 1f);

            itemList = new VirtualizedStatisticsList(
                CreateItemGroups(data: null),
                StatisticsMetricDisplay.ItemActivity);
            itemList.Top.Set(ListTop, 0f);
            itemList.Width.Set(0f, 1f);
            itemList.Height.Set(-ListTop, 1f);

            npcList = new VirtualizedStatisticsList(
                CreateNpcGroups(data: null, worldPublic: false),
                StatisticsMetricDisplay.Events);
            npcList.Top.Set(ListTop, 0f);
            npcList.Width.Set(0f, 1f);
            npcList.Height.Set(-ListTop, 1f);

            economyList = new VirtualizedStatisticsList(
                CreateEconomyGroups(data: null),
                StatisticsMetricDisplay.EconomyTransactions);
            economyList.Top.Set(ListTop, 0f);
            economyList.Width.Set(0f, 1f);
            economyList.Height.Set(-ListTop, 1f);

            itemList.ExpansionStateChanged += UpdateExpandCollapseButton;
            npcList.ExpansionStateChanged += UpdateExpandCollapseButton;
            economyList.ExpansionStateChanged += UpdateExpandCollapseButton;
            movementList.ExpansionStateChanged += UpdateExpandCollapseButton;
            survivalList.ExpansionStateChanged += UpdateExpandCollapseButton;

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
        /// 当服务器同步世界统计增量时，刷新当前可见的世界公共页面。
        /// </summary>
        internal void RefreshWorldPublicData()
        {
            if (toolbar?.Scope == StatisticsViewScope.CurrentWorldPublic)
            {
                UpdateStatisticsStartedAt(StatisticsViewScope.CurrentWorldPublic);
            }
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
            SetItemTotalsVisible(tabIndex == ItemsTabIndex);
            SetNpcTotalsVisible(tabIndex == NpcsTabIndex);
            SetEconomyTotalsVisible(tabIndex == EconomyTabIndex);
            SetMovementTotalsVisible(tabIndex == MovementTabIndex);
            SetSurvivalTotalsVisible(tabIndex == SurvivalTabIndex);
            UpdateExpandCollapseButton();
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
            // 为统计列表提供更多纵向空间；仍保留画布边距并在小分辨率下响应式收缩。
            float windowHeight = MathHelper.Min(760f, MathHelper.Max(400f, viewportHeight * 0.9f));
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
            itemList.SetFilters(searchQuery, sourceFilter);
            npcList.SetFilters(searchQuery, sourceFilter);
            economyList.SetFilters(searchQuery, sourceFilter);
            movementList.SetFilters(searchQuery, sourceFilter);
            survivalList.SetFilters(searchQuery, sourceFilter);
        }

        /// <summary>
        /// 对当前 Tab 的全部可展开分组执行统一展开或折叠。
        /// </summary>
        private void ToggleAllGroups()
        {
            VirtualizedStatisticsList activeList = GetActiveStatisticsList();
            if (activeList == null || !activeList.HasExpandableGroups)
            {
                return;
            }

            activeList.SetAllGroupsExpanded(!activeList.HasExpandedGroups);
        }

        /// <summary>
        /// 取得当前页面已实现的树形统计列表；占位页面返回空值。
        /// </summary>
        private VirtualizedStatisticsList GetActiveStatisticsList()
        {
            return selectedTabIndex switch
            {
                ItemsTabIndex => itemList,
                NpcsTabIndex => npcList,
                EconomyTabIndex => economyList,
                MovementTabIndex => movementList,
                SurvivalTabIndex => survivalList,
                _ => null
            };
        }

        /// <summary>
        /// 根据当前可见分组的展开状态刷新页头按钮文案和可用状态。
        /// </summary>
        private void UpdateExpandCollapseButton()
        {
            if (expandCollapseButton == null)
            {
                return;
            }

            VirtualizedStatisticsList activeList = GetActiveStatisticsList();
            bool enabled = activeList?.HasExpandableGroups == true;
            bool showCollapseAction = enabled && activeList.HasExpandedGroups;
            expandCollapseButton.SetText(Language.GetText(
                showCollapseAction
                    ? "Mods.ComprehensiveStatistics.UI.List.CollapseAll"
                    : "Mods.ComprehensiveStatistics.UI.List.ExpandAll"));
            expandCollapseButton.SetEnabled(enabled);
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
            contentDescription.Width.Set(
                -descriptionLeft - ExpandCollapseButtonWidth - 12f,
                1f);
            UpdateExpandCollapseButton();
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
            UpdateMovementTotals(scope);
            UpdateItemTotals(scope);
            UpdateNpcTotals(scope);
            UpdateEconomyTotals(scope);
        }

        /// <summary>
        /// 从当前角色范围重新生成 NPC 对话、最终击杀与协助击杀明细。
        /// </summary>
        private void UpdateNpcTotals(StatisticsViewScope scope)
        {
            if (selectedTabIndex != NpcsTabIndex)
            {
                return;
            }

            if (scope == StatisticsViewScope.CurrentWorldPublic)
            {
                StatisticsData worldData =
                    ModContent.GetInstance<StatisticsWorldSystem>().Data;
                npcList.SetGroups(CreateNpcGroups(worldData, worldPublic: true));
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            npcList.SetGroups(CreateNpcGroups(scopeData, worldPublic: false));
        }

        /// <summary>
        /// 从当前角色范围重新生成物品页的拾取统计。
        /// </summary>
        private void UpdateItemTotals(StatisticsViewScope scope)
        {
            if (selectedTabIndex != ItemsTabIndex)
            {
                return;
            }

            if (scope == StatisticsViewScope.CurrentWorldPublic)
            {
                itemList.SetGroups(CreateItemGroups(data: null));
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            itemList.SetGroups(CreateItemGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成经济页的买卖明细与护士治疗统计。
        /// </summary>
        private void UpdateEconomyTotals(StatisticsViewScope scope)
        {
            if (selectedTabIndex != EconomyTabIndex)
            {
                return;
            }

            if (scope == StatisticsViewScope.CurrentWorldPublic)
            {
                economyList.SetGroups(CreateEconomyGroups(data: null));
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            economyList.SetGroups(CreateEconomyGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成移动页的总距离统计。
        /// </summary>
        private void UpdateMovementTotals(StatisticsViewScope scope)
        {
            if (selectedTabIndex != MovementTabIndex)
            {
                return;
            }

            if (scope == StatisticsViewScope.CurrentWorldPublic)
            {
                movementList.SetGroups(CreateMovementGroups(data: null));
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            movementList.SetGroups(CreateMovementGroups(scopeData));
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
        /// 按需挂载物品统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetItemTotalsVisible(bool visible)
        {
            if (visible == isItemListAttached)
            {
                if (visible)
                {
                    UpdateItemTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                // 不可见时从 UI 树移除，避免隐藏列表继续接收输入和执行更新。
                contentPanel.Append(itemList);
                UpdateItemTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                itemList.Remove();
            }

            isItemListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 按需挂载 NPC 统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetNpcTotalsVisible(bool visible)
        {
            if (visible == isNpcListAttached)
            {
                if (visible)
                {
                    UpdateNpcTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(npcList);
                UpdateNpcTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                npcList.Remove();
            }

            isNpcListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 将 NPC 角色行为或世界死亡统计转换为对应范围的树形明细。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateNpcGroups(
            StatisticsData data,
            bool worldPublic)
        {
            Dictionary<StableContentKey, long> conversationsByNpc =
                new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, Dictionary<NpcChatButtonKind, long>> buttonsByNpc =
                new Dictionary<StableContentKey, Dictionary<NpcChatButtonKind, long>>();
            Dictionary<StableContentKey, long> finalKillsByNpc =
                new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, long> assistsByNpc =
                new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, long> worldDeathsByNpc =
                new Dictionary<StableContentKey, long>();
            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue || pair.Value.Events <= 0L)
                    {
                        continue;
                    }

                    StableContentKey content = pair.Key.Content.Value;
                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.NpcConversationStatisticId,
                        StringComparison.Ordinal))
                    {
                        conversationsByNpc.TryGetValue(content, out long current);
                        conversationsByNpc[content] = SaturatingAdd(
                            current,
                            pair.Value.Events);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.NpcChatButtonStatisticId,
                        StringComparison.Ordinal)
                        && StatisticKeys.TryParseNpcChatButtonKind(
                            pair.Key.Style,
                            out NpcChatButtonKind buttonKind))
                    {
                        AddNpcChatButtonMetrics(
                            buttonsByNpc,
                            content,
                            buttonKind,
                            pair.Value.Events);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.NpcFinalKillStatisticId,
                        StringComparison.Ordinal))
                    {
                        finalKillsByNpc.TryGetValue(content, out long current);
                        finalKillsByNpc[content] = SaturatingAdd(current, pair.Value.Events);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.NpcAssistStatisticId,
                        StringComparison.Ordinal))
                    {
                        assistsByNpc.TryGetValue(content, out long current);
                        assistsByNpc[content] = SaturatingAdd(current, pair.Value.Events);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.NpcWorldDeathStatisticId,
                        StringComparison.Ordinal))
                    {
                        worldDeathsByNpc.TryGetValue(content, out long current);
                        worldDeathsByNpc[content] = SaturatingAdd(current, pair.Value.Events);
                    }
                }
            }

            if (worldPublic)
            {
                return new[]
                {
                    new StatisticsListGroup(
                        "Mods.ComprehensiveStatistics.UI.NPCs.WorldDeathsGroup",
                        CreateNpcStatisticsItems(worldDeathsByNpc),
                        nameIsLocalizationKey: true,
                        metricDisplay: StatisticsMetricDisplay.Events)
                };
            }

            HashSet<StableContentKey> npcContents =
                new HashSet<StableContentKey>(conversationsByNpc.Keys);
            npcContents.UnionWith(buttonsByNpc.Keys);
            List<StableContentKey> orderedNpcContents =
                new List<StableContentKey>(npcContents);
            orderedNpcContents.Sort(CompareNpcContentKeys);

            List<StatisticsListGroup> npcGroups =
                new List<StatisticsListGroup>(orderedNpcContents.Count);
            foreach (StableContentKey content in orderedNpcContents)
            {
                conversationsByNpc.TryGetValue(content, out long conversationEvents);
                buttonsByNpc.TryGetValue(
                    content,
                    out Dictionary<NpcChatButtonKind, long> buttons);
                npcGroups.Add(CreateNpcDialogueGroup(
                    content,
                    conversationEvents,
                    buttons));
            }

            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.NPCs.DialogueGroup",
                    Array.Empty<StatisticsListItem>(),
                    nameIsLocalizationKey: true,
                    childGroups: npcGroups,
                    metricDisplay: StatisticsMetricDisplay.Events),
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.NPCs.FinalKillsGroup",
                    CreateNpcStatisticsItems(finalKillsByNpc),
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.Events),
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.NPCs.AssistsGroup",
                    CreateNpcStatisticsItems(assistsByNpc),
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.Events)
            };
        }

        /// <summary>
        /// 将按 NPC 汇总的事件字典转换为默认内容顺序的统计叶子项。
        /// </summary>
        private static List<StatisticsListItem> CreateNpcStatisticsItems(
            IReadOnlyDictionary<StableContentKey, long> eventsByNpc)
        {
            List<StableContentKey> orderedContents =
                new List<StableContentKey>(eventsByNpc.Keys);
            orderedContents.Sort(CompareNpcContentKeys);
            List<StatisticsListItem> items =
                new List<StatisticsListItem>(orderedContents.Count);
            foreach (StableContentKey content in orderedContents)
            {
                items.Add(CreateNpcStatisticsItem(content, eventsByNpc[content]));
            }

            return items;
        }

        /// <summary>
        /// 将 NPC 稳定内容键转换为带 NPC 首帧图标的统计叶子项。
        /// </summary>
        private static StatisticsListItem CreateNpcStatisticsItem(
            StableContentKey content,
            long events)
        {
            bool resolved = content.TryResolveNpcType(out int npcType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetNPCNameValue(npcType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.NPCs.MissingNpcContent",
                    content.ToString());
            Asset<Texture2D> icon = null;
            if (resolved)
            {
                Main.instance.LoadNPC(npcType);
                icon = TextureAssets.Npc[npcType];
            }

            return new StatisticsListItem(
                name,
                itemType: resolved ? npcType : 0,
                icon: icon,
                events: events,
                units: 0L,
                source: source,
                iconKind: resolved ? StatisticsIconKind.Npc : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? npcType
                    : null);
        }

        /// <summary>
        /// 向 NPC 与按钮功能的二维汇总表安全累加点击次数。
        /// </summary>
        private static void AddNpcChatButtonMetrics(
            IDictionary<StableContentKey, Dictionary<NpcChatButtonKind, long>> buttonsByNpc,
            StableContentKey content,
            NpcChatButtonKind buttonKind,
            long events)
        {
            if (!buttonsByNpc.TryGetValue(
                content,
                out Dictionary<NpcChatButtonKind, long> buttons))
            {
                buttons = new Dictionary<NpcChatButtonKind, long>();
                buttonsByNpc.Add(content, buttons);
            }

            buttons.TryGetValue(buttonKind, out long current);
            buttons[buttonKind] = SaturatingAdd(current, events);
        }

        /// <summary>
        /// 创建一个以 NPC 为父行、开始对话与按钮功能为叶子项的对话统计分组。
        /// </summary>
        private static StatisticsListGroup CreateNpcDialogueGroup(
            StableContentKey content,
            long conversationEvents,
            IReadOnlyDictionary<NpcChatButtonKind, long> buttons)
        {
            bool resolved = content.TryResolveNpcType(out int npcType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetNPCNameValue(npcType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.NPCs.MissingNpcContent",
                    content.ToString());
            Asset<Texture2D> icon = null;
            if (resolved)
            {
                Main.instance.LoadNPC(npcType);
                icon = TextureAssets.Npc[npcType];
            }

            int buttonCount = buttons?.Count ?? 0;
            List<StatisticsListItem> dialogueItems =
                new List<StatisticsListItem>(buttonCount + (conversationEvents > 0L ? 1 : 0));
            if (conversationEvents > 0L)
            {
                dialogueItems.Add(new StatisticsListItem(
                    "Mods.ComprehensiveStatistics.UI.NPCs.ConversationStarted",
                    itemType: 0,
                    icon: null,
                    events: conversationEvents,
                    units: 0L,
                    source: source,
                    nameIsLocalizationKey: true,
                    iconKind: StatisticsIconKind.None));
            }

            foreach (NpcChatButtonKind buttonKind in Enum.GetValues(typeof(NpcChatButtonKind)))
            {
                if (buttons == null
                    || !buttons.TryGetValue(buttonKind, out long events)
                    || events <= 0L)
                {
                    continue;
                }

                dialogueItems.Add(new StatisticsListItem(
                    $"Mods.ComprehensiveStatistics.UI.NPCs.ChatButtons.{buttonKind}",
                    itemType: 0,
                    icon: null,
                    events: events,
                    units: 0L,
                    source: source,
                    nameIsLocalizationKey: true,
                    iconKind: StatisticsIconKind.None));
            }

            return new StatisticsListGroup(
                name,
                dialogueItems,
                isExpanded: false,
                metricDisplay: StatisticsMetricDisplay.Events,
                iconType: resolved ? npcType : 0,
                icon: icon,
                iconKind: resolved ? StatisticsIconKind.Npc : StatisticsIconKind.None);
        }

        /// <summary>
        /// 按需挂载经济统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetEconomyTotalsVisible(bool visible)
        {
            if (visible == isEconomyListAttached)
            {
                if (visible)
                {
                    UpdateEconomyTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(economyList);
                UpdateEconomyTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                economyList.Remove();
            }

            isEconomyListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 将金币买卖、特殊货币买入及其他经济统计转换为可折叠树形表格。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateEconomyGroups(StatisticsData data)
        {
            Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>
                purchasesByMerchant = new Dictionary<StableContentKey,
                    Dictionary<StableContentKey, StatisticMetrics>>();
            Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>
                salesByMerchant = new Dictionary<StableContentKey,
                    Dictionary<StableContentKey, StatisticMetrics>>();
            Dictionary<StableContentKey,
                Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>>
                specialPurchasesByCurrency = new Dictionary<StableContentKey,
                    Dictionary<StableContentKey,
                        Dictionary<StableContentKey, StatisticMetrics>>>();
            Dictionary<StableContentKey, StatisticMetrics> reforgesByItem =
                new Dictionary<StableContentKey, StatisticMetrics>();
            StatisticMetrics nurseHealingWithDebuffsRemoved = default;
            StatisticMetrics nurseHealingWithoutDebuffsRemoved = default;
            StatisticMetrics taxCollectorCollections = default;
            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (pair.Key == StatisticKeys.TaxCollectorCollections)
                    {
                        taxCollectorCollections = taxCollectorCollections.Add(pair.Value);
                        continue;
                    }

                    if (pair.Key == StatisticKeys.NurseHealingWithDebuffsRemoved)
                    {
                        nurseHealingWithDebuffsRemoved =
                            nurseHealingWithDebuffsRemoved.Add(pair.Value);
                        continue;
                    }

                    if (pair.Key == StatisticKeys.NurseHealingWithoutDebuffsRemoved)
                    {
                        nurseHealingWithoutDebuffsRemoved =
                            nurseHealingWithoutDebuffsRemoved.Add(pair.Value);
                        continue;
                    }

                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ItemReforgeStatisticId,
                        StringComparison.Ordinal)
                        && pair.Key.Content.HasValue)
                    {
                        reforgesByItem.TryGetValue(
                            pair.Key.Content.Value,
                            out StatisticMetrics currentReforge);
                        reforgesByItem[pair.Key.Content.Value] =
                            currentReforge.Add(pair.Value);
                        continue;
                    }

                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.SpecialCurrencyPurchaseStatisticId,
                        StringComparison.Ordinal)
                        && pair.Key.Content.HasValue
                        && StatisticKeys.TryParseSpecialCurrencyPurchaseStyle(
                            pair.Key.Style,
                            out StableContentKey specialMerchant,
                            out StableContentKey currencyContent))
                    {
                        if (!specialPurchasesByCurrency.TryGetValue(
                            currencyContent,
                            out Dictionary<StableContentKey,
                                Dictionary<StableContentKey, StatisticMetrics>> merchants))
                        {
                            merchants = new Dictionary<StableContentKey,
                                Dictionary<StableContentKey, StatisticMetrics>>();
                            specialPurchasesByCurrency.Add(currencyContent, merchants);
                        }

                        if (!merchants.TryGetValue(
                            specialMerchant,
                            out Dictionary<StableContentKey, StatisticMetrics>
                                specialItemMetrics))
                        {
                            specialItemMetrics =
                                new Dictionary<StableContentKey, StatisticMetrics>();
                            merchants.Add(specialMerchant, specialItemMetrics);
                        }

                        specialItemMetrics.TryGetValue(
                            pair.Key.Content.Value,
                            out StatisticMetrics currentSpecialPurchase);
                        specialItemMetrics[pair.Key.Content.Value] =
                            currentSpecialPurchase.Add(pair.Value);
                        continue;
                    }

                    Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>
                        target;
                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ItemPurchaseStatisticId,
                        StringComparison.Ordinal))
                    {
                        target = purchasesByMerchant;
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ItemSaleStatisticId,
                        StringComparison.Ordinal))
                    {
                        target = salesByMerchant;
                    }
                    else
                    {
                        continue;
                    }

                    if (!pair.Key.Content.HasValue
                        || !StableContentKey.TryParse(
                            pair.Key.Style,
                            out StableContentKey merchantContent))
                    {
                        continue;
                    }

                    if (!target.TryGetValue(
                        merchantContent,
                        out Dictionary<StableContentKey, StatisticMetrics> itemMetrics))
                    {
                        itemMetrics = new Dictionary<StableContentKey, StatisticMetrics>();
                        target.Add(merchantContent, itemMetrics);
                    }

                    itemMetrics.TryGetValue(
                        pair.Key.Content.Value,
                        out StatisticMetrics current);
                    itemMetrics[pair.Key.Content.Value] = current.Add(pair.Value);
                }
            }

            List<StatisticsListGroup> groups = new List<StatisticsListGroup>
            {
                CreateEconomyTransactionGroup(
                    "Mods.ComprehensiveStatistics.UI.Economy.PurchasesGroup",
                    purchasesByMerchant)
            };
            groups.AddRange(new[]
            {
                CreateEconomyTransactionGroup(
                    "Mods.ComprehensiveStatistics.UI.Economy.SalesGroup",
                    salesByMerchant),
                CreateReforgeGroup(reforgesByItem),
                CreateNurseHealingGroup(
                    nurseHealingWithDebuffsRemoved,
                    nurseHealingWithoutDebuffsRemoved),
                CreateTaxCollectionGroup(taxCollectorCollections)
            });
            groups.AddRange(CreateSpecialCurrencyPurchaseGroups(
                specialPurchasesByCurrency));
            return groups;
        }

        /// <summary>
        /// 将不同 Debuff 处理结果合并为一行不可展开的护士治疗合计。
        /// </summary>
        private static StatisticsListGroup CreateNurseHealingGroup(
            StatisticMetrics withDebuffsRemoved,
            StatisticMetrics withoutDebuffsRemoved)
        {
            StatisticMetrics total = withDebuffsRemoved.Add(withoutDebuffsRemoved);
            string name = Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.Economy.NurseHealingGroupFormat",
                total.Events);
            StatisticsListItem aggregateItem = new StatisticsListItem(
                name,
                itemType: 0,
                icon: null,
                events: total.Events,
                units: total.Units,
                amount: total.Amount,
                source: StatisticsContentSource.Neutral,
                iconKind: StatisticsIconKind.None);
            Main.instance.LoadNPC(NPCID.Nurse);
            return new StatisticsListGroup(
                name,
                new[] { aggregateItem },
                metricDisplay: StatisticsMetricDisplay.EconomyTransactions,
                showChildren: false,
                iconType: NPCID.Nurse,
                icon: TextureAssets.Npc[NPCID.Nurse],
                iconKind: StatisticsIconKind.Npc);
        }

        /// <summary>
        /// 创建“重铸合计 → 物品”分组；数量列显示各物品成功重铸次数。
        /// </summary>
        private static StatisticsListGroup CreateReforgeGroup(
            Dictionary<StableContentKey, StatisticMetrics> metricsByItem)
        {
            List<StatisticsListItem> items =
                new List<StatisticsListItem>(metricsByItem.Count);
            foreach (KeyValuePair<StableContentKey, StatisticMetrics> reforge in metricsByItem)
            {
                StatisticMetrics displayMetrics = new StatisticMetrics(
                    events: reforge.Value.Events,
                    units: reforge.Value.Events,
                    amount: reforge.Value.Amount);
                items.Add(CreateEconomyTransactionItem(reforge.Key, displayMetrics));
            }

            Main.instance.LoadNPC(NPCID.GoblinTinkerer);
            return new StatisticsListGroup(
                "Mods.ComprehensiveStatistics.UI.Economy.ReforgesGroup",
                items,
                nameIsLocalizationKey: true,
                metricDisplay: StatisticsMetricDisplay.EconomyTransactions,
                iconType: NPCID.GoblinTinkerer,
                icon: TextureAssets.Npc[NPCID.GoblinTinkerer],
                iconKind: StatisticsIconKind.Npc);
        }

        /// <summary>
        /// 创建不可展开的税收官收集合计行，数量为领取次数，金额为实际获得的钱币。
        /// </summary>
        private static StatisticsListGroup CreateTaxCollectionGroup(
            StatisticMetrics collections)
        {
            string name = Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.Economy.TaxCollectorCollectionsGroup");
            StatisticsListItem aggregateItem = new StatisticsListItem(
                name,
                itemType: 0,
                icon: null,
                events: collections.Events,
                units: collections.Units,
                amount: collections.Amount,
                source: StatisticsContentSource.Neutral,
                iconKind: StatisticsIconKind.None);
            Main.instance.LoadNPC(NPCID.TaxCollector);
            return new StatisticsListGroup(
                name,
                new[] { aggregateItem },
                metricDisplay: StatisticsMetricDisplay.EconomyTransactions,
                showChildren: false,
                iconType: NPCID.TaxCollector,
                icon: TextureAssets.Npc[NPCID.TaxCollector],
                iconKind: StatisticsIconKind.Npc);
        }

        /// <summary>
        /// 为每一种特殊货币创建独立的“货币 → 商人 → 物品”买入树，避免与金币合计混算。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateSpecialCurrencyPurchaseGroups(
            Dictionary<StableContentKey,
                Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>>
                metricsByCurrency)
        {
            List<KeyValuePair<StableContentKey,
                Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>>>
                currencies = new List<KeyValuePair<StableContentKey,
                    Dictionary<StableContentKey,
                        Dictionary<StableContentKey, StatisticMetrics>>>>(metricsByCurrency);
            currencies.Sort((left, right) => StringComparer.Ordinal.Compare(
                left.Key.ToString(),
                right.Key.ToString()));

            List<StatisticsListGroup> groups =
                new List<StatisticsListGroup>(currencies.Count);
            foreach (KeyValuePair<StableContentKey,
                Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>>
                currency in currencies)
            {
                bool currencyResolved = currency.Key.TryResolveItemType(out int currencyItemType);
                string currencyName = currencyResolved
                    ? Lang.GetItemNameValue(currencyItemType)
                    : Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Economy.MissingCurrencyContent",
                        currency.Key.ToString());
                Asset<Texture2D> currencyIcon = null;
                if (currencyResolved)
                {
                    Main.instance.LoadItem(currencyItemType);
                    currencyIcon = TextureAssets.Item[currencyItemType];
                }

                int amountIconType = currencyResolved ? currencyItemType : -1;
                List<KeyValuePair<StableContentKey,
                    Dictionary<StableContentKey, StatisticMetrics>>> merchants =
                    new List<KeyValuePair<StableContentKey,
                        Dictionary<StableContentKey, StatisticMetrics>>>(currency.Value);
                merchants.Sort((left, right) => CompareNpcContentKeys(left.Key, right.Key));

                List<StatisticsListGroup> merchantGroups =
                    new List<StatisticsListGroup>(merchants.Count);
                foreach (KeyValuePair<StableContentKey,
                    Dictionary<StableContentKey, StatisticMetrics>> merchant in merchants)
                {
                    List<StatisticsListItem> items =
                        new List<StatisticsListItem>(merchant.Value.Count);
                    foreach (KeyValuePair<StableContentKey, StatisticMetrics> purchase
                        in merchant.Value)
                    {
                        items.Add(CreateEconomyTransactionItem(
                            purchase.Key,
                            purchase.Value,
                            amountIconType,
                            currencyIcon));
                    }

                    bool merchantResolved = merchant.Key.TryResolveNpcType(out int npcType);
                    string merchantName = merchantResolved
                        ? Lang.GetNPCNameValue(npcType)
                        : Language.GetTextValue(
                            "Mods.ComprehensiveStatistics.UI.Economy.MissingMerchantContent",
                            merchant.Key.ToString());
                    Asset<Texture2D> merchantIcon = null;
                    if (merchantResolved)
                    {
                        Main.instance.LoadNPC(npcType);
                        merchantIcon = TextureAssets.Npc[npcType];
                    }

                    merchantGroups.Add(new StatisticsListGroup(
                        merchantName,
                        items,
                        metricDisplay: StatisticsMetricDisplay.EconomyTransactions,
                        iconType: merchantResolved ? npcType : 0,
                        icon: merchantIcon,
                        iconKind: StatisticsIconKind.Npc,
                        amountIconType: amountIconType,
                        amountIcon: currencyIcon));
                }

                string groupName = Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Economy.SpecialCurrencyPurchasesGroupFormat",
                    currencyName);
                groups.Add(new StatisticsListGroup(
                    groupName,
                    Array.Empty<StatisticsListItem>(),
                    childGroups: merchantGroups,
                    metricDisplay: StatisticsMetricDisplay.EconomyTransactions,
                    iconType: currencyResolved ? currencyItemType : 0,
                    icon: currencyIcon,
                    iconKind: StatisticsIconKind.Item,
                    amountIconType: amountIconType,
                    amountIcon: currencyIcon));
            }

            return groups;
        }

        /// <summary>
        /// 为一种经济交易按商人生成稳定排序的分组及物品明细。
        /// </summary>
        private static StatisticsListGroup CreateEconomyTransactionGroup(
            string localizationKey,
            Dictionary<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>
                metricsByMerchant)
        {
            List<KeyValuePair<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>>
                merchants = new List<KeyValuePair<StableContentKey,
                    Dictionary<StableContentKey, StatisticMetrics>>>(metricsByMerchant);
            merchants.Sort((left, right) => CompareNpcContentKeys(left.Key, right.Key));

            List<StatisticsListGroup> merchantGroups =
                new List<StatisticsListGroup>(merchants.Count);
            foreach (KeyValuePair<StableContentKey, Dictionary<StableContentKey, StatisticMetrics>>
                merchant in merchants)
            {
                List<StatisticsListItem> items =
                    new List<StatisticsListItem>(merchant.Value.Count);
                foreach (KeyValuePair<StableContentKey, StatisticMetrics> transaction in merchant.Value)
                {
                    items.Add(CreateEconomyTransactionItem(transaction.Key, transaction.Value));
                }

                bool resolved = merchant.Key.TryResolveNpcType(out int npcType);
                string merchantName = resolved
                    ? Lang.GetNPCNameValue(npcType)
                    : Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Economy.MissingMerchantContent",
                        merchant.Key.ToString());
                Asset<Texture2D> merchantIcon = null;
                if (resolved)
                {
                    Main.instance.LoadNPC(npcType);
                    merchantIcon = TextureAssets.Npc[npcType];
                }

                merchantGroups.Add(new StatisticsListGroup(
                    merchantName,
                    items,
                    metricDisplay: StatisticsMetricDisplay.EconomyTransactions,
                    iconType: resolved ? npcType : 0,
                    icon: merchantIcon,
                    iconKind: StatisticsIconKind.Npc));
            }

            return new StatisticsListGroup(
                localizationKey,
                Array.Empty<StatisticsListItem>(),
                nameIsLocalizationKey: true,
                childGroups: merchantGroups,
                metricDisplay: StatisticsMetricDisplay.EconomyTransactions);
        }

        /// <summary>
        /// 将一项经济交易转换为带物品图标、数量、次数和金币金额的叶子项。
        /// </summary>
        private static StatisticsListItem CreateEconomyTransactionItem(
            StableContentKey content,
            StatisticMetrics metrics,
            int amountIconType = 0,
            Asset<Texture2D> amountIcon = null)
        {
            bool resolved = content.TryResolveItemType(out int itemType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetItemNameValue(itemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Items.MissingItemContent",
                    content.ToString());
            Asset<Texture2D> icon = null;
            if (resolved)
            {
                Main.instance.LoadItem(itemType);
                icon = TextureAssets.Item[itemType];
            }

            return new StatisticsListItem(
                name,
                itemType: resolved ? itemType : 0,
                icon: icon,
                events: metrics.Events,
                units: metrics.Units,
                amount: metrics.Amount,
                source: source,
                iconKind: resolved ? StatisticsIconKind.Item : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? itemType
                    : null,
                amountIconType: amountIconType,
                amountIcon: amountIcon);
        }

        /// <summary>
        /// 按原版 NPC 数字 ID、随后模组内容内部名的规则稳定排列商人分组。
        /// </summary>
        private static int CompareNpcContentKeys(StableContentKey left, StableContentKey right)
        {
            StatisticsContentSource leftSource = GetContentSource(left);
            StatisticsContentSource rightSource = GetContentSource(right);
            if (leftSource != rightSource)
            {
                return leftSource == StatisticsContentSource.Vanilla ? -1 : 1;
            }

            if (leftSource == StatisticsContentSource.Vanilla
                && left.TryResolveNpcType(out int leftNpcType)
                && right.TryResolveNpcType(out int rightNpcType))
            {
                return leftNpcType.CompareTo(rightNpcType);
            }

            int contentComparison = StringComparer.Ordinal.Compare(
                left.ContentName,
                right.ContentName);
            return contentComparison != 0
                ? contentComparison
                : StringComparer.Ordinal.Compare(left.ModName, right.ModName);
        }

        /// <summary>
        /// 将一个统计范围中的物品拾取、使用与丢弃明细转换为按物品类型展示的列表分组。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateItemGroups(StatisticsData data)
        {
            Dictionary<StableContentKey, StatisticMetrics> metricsByItem =
                new Dictionary<StableContentKey, StatisticMetrics>();
            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue)
                    {
                        continue;
                    }

                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ItemPickupStatisticId,
                        StringComparison.Ordinal)
                        && (pair.Value.Events > 0L || pair.Value.Units > 0L))
                    {
                        AddItemMetrics(
                            metricsByItem,
                            pair.Key.Content.Value,
                            new StatisticMetrics(units: pair.Value.Units));
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ItemUseStatisticId,
                        StringComparison.Ordinal)
                        && pair.Value.Events > 0L)
                    {
                        AddItemMetrics(
                            metricsByItem,
                            pair.Key.Content.Value,
                            new StatisticMetrics(events: pair.Value.Events));
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.ItemDiscardStatisticId,
                        StringComparison.Ordinal)
                        && (pair.Value.Events > 0L || pair.Value.Units > 0L))
                    {
                        AddItemMetrics(
                            metricsByItem,
                            pair.Key.Content.Value,
                            new StatisticMetrics(amount: pair.Value.Units));
                    }
                }
            }

            List<StatisticsListItem> items = new List<StatisticsListItem>(metricsByItem.Count);
            foreach (KeyValuePair<StableContentKey, StatisticMetrics> pair in metricsByItem)
            {
                items.Add(CreateItemStatisticsItem(
                    pair.Key,
                    pickupUnits: pair.Value.Units,
                    useEvents: pair.Value.Events,
                    discardUnits: pair.Value.Amount));
            }

            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Items.CombinedGroup",
                    items,
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.ItemActivity)
            };
        }

        /// <summary>
        /// 将同一物品的拾取数量、使用次数与丢弃数量合并到一个三列展示值中。
        /// </summary>
        private static void AddItemMetrics(
            IDictionary<StableContentKey, StatisticMetrics> metricsByItem,
            StableContentKey content,
            StatisticMetrics metrics)
        {
            metricsByItem.TryGetValue(content, out StatisticMetrics current);
            metricsByItem[content] = current.Add(metrics);
        }

        /// <summary>
        /// 将物品稳定内容键转换为带物品图标的统计叶子项。
        /// </summary>
        private static StatisticsListItem CreateItemStatisticsItem(
            StableContentKey content,
            long pickupUnits,
            long useEvents,
            long discardUnits)
        {
            bool resolved = content.TryResolveItemType(out int itemType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetItemNameValue(itemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Items.MissingItemContent",
                    content.ToString());
            Asset<Texture2D> icon = null;
            if (resolved)
            {
                Main.instance.LoadItem(itemType);
                icon = TextureAssets.Item[itemType];
            }

            return new StatisticsListItem(
                name,
                itemType: resolved ? itemType : 0,
                icon: icon,
                events: useEvents,
                units: pickupUnits,
                amount: discardUnits,
                source: source,
                iconKind: resolved ? StatisticsIconKind.Item : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? itemType
                    : null);
        }

        /// <summary>
        /// 按需挂载移动统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetMovementTotalsVisible(bool visible)
        {
            if (visible == isMovementListAttached)
            {
                if (visible)
                {
                    UpdateMovementTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(movementList);
                UpdateMovementTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                movementList.Remove();
            }

            isMovementListAttached = visible;
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
        /// 创建总移动距离树；特殊移动方式按来源物品展开，合计始终由叶子项计算。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateMovementGroups(StatisticsData data)
        {
            long distancePixels = data?
                .GetValue(StatisticKeys.TotalMovementDistance).Amount ?? 0L;
            List<StatisticsListItem> basicModes = new List<StatisticsListItem>();
            List<StatisticsListGroup> detailedModes = new List<StatisticsListGroup>();
            long classifiedDistancePixels = 0L;
            Array modes = Enum.GetValues(typeof(MovementModeKind));
            foreach (MovementModeKind mode in modes)
            {
                List<StatisticsListItem> details = CreateMovementModeDetails(
                    data,
                    mode,
                    out long modeDistancePixels);
                classifiedDistancePixels = SaturatingAdd(
                    classifiedDistancePixels,
                    modeDistancePixels);

                if (IsDetailedMovementMode(mode))
                {
                    detailedModes.Add(new StatisticsListGroup(
                        $"Mods.ComprehensiveStatistics.UI.Movement.Modes.{mode}",
                        details,
                        nameIsLocalizationKey: true,
                        metricDisplay: StatisticsMetricDisplay.Distance));
                    continue;
                }

                if (mode == MovementModeKind.Other)
                {
                    continue;
                }

                basicModes.Add(new StatisticsListItem(
                    $"Mods.ComprehensiveStatistics.UI.Movement.Modes.{mode}",
                    itemType: 0,
                    icon: null,
                    events: 0L,
                    units: 0L,
                    amount: modeDistancePixels,
                    source: StatisticsContentSource.Neutral,
                    nameIsLocalizationKey: true,
                    iconKind: StatisticsIconKind.None));
            }

            long otherDistancePixels = data?
                .GetValue(StatisticKeys.GetMovementModeDistanceKey(MovementModeKind.Other))
                .Amount ?? 0L;
            // D1 只有总距离，无法回溯移动方式；与 D3 的其他有效位移统一显示为
            // “其他或未分类”，避免两个含义高度重叠的兜底行。
            long legacyUnclassifiedPixels = Math.Max(
                distancePixels - classifiedDistancePixels,
                0L);
            otherDistancePixels = SaturatingAdd(
                otherDistancePixels,
                legacyUnclassifiedPixels);
            basicModes.Add(new StatisticsListItem(
                "Mods.ComprehensiveStatistics.UI.Movement.PreviouslyUnclassified",
                itemType: 0,
                icon: null,
                events: 0L,
                units: 0L,
                amount: otherDistancePixels,
                source: StatisticsContentSource.Neutral,
                nameIsLocalizationKey: true,
                iconKind: StatisticsIconKind.None));

            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Movement.MovementModes",
                    basicModes,
                    nameIsLocalizationKey: true,
                    childGroups: detailedModes,
                    metricDisplay: StatisticsMetricDisplay.Distance)
            };
        }

        /// <summary>
        /// 创建一个移动类别的物品明细，并返回无内容键数据与物品明细数据之和。
        /// </summary>
        private static List<StatisticsListItem> CreateMovementModeDetails(
            StatisticsData data,
            MovementModeKind mode,
            out long totalAmount)
        {
            List<StatisticsListItem> items = new List<StatisticsListItem>();
            StatisticKey baseKey = StatisticKeys.GetMovementModeDistanceKey(mode);
            long unclassifiedAmount = data?.GetValue(baseKey).Amount ?? 0L;
            totalAmount = unclassifiedAmount;

            if (IsDetailedMovementMode(mode) && data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue
                        || pair.Value.Amount <= 0L
                        || !string.Equals(
                            pair.Key.StatisticId,
                            baseKey.StatisticId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    items.Add(CreateMovementItem(pair.Key.Content.Value, pair.Value.Amount));
                    totalAmount = SaturatingAdd(totalAmount, pair.Value.Amount);
                }

                if (unclassifiedAmount > 0L)
                {
                    items.Add(new StatisticsListItem(
                        mode == MovementModeKind.Jumping
                            ? "Mods.ComprehensiveStatistics.UI.Movement.NormalOrUnclassifiedJump"
                            : "Mods.ComprehensiveStatistics.UI.Movement.PreviouslyUnclassified",
                        itemType: 0,
                        icon: null,
                        events: 0L,
                        units: 0L,
                        amount: unclassifiedAmount,
                        source: StatisticsContentSource.Neutral,
                        nameIsLocalizationKey: true,
                        iconKind: StatisticsIconKind.None));
                }
            }

            return items;
        }

        /// <summary>
        /// 将移动来源物品的稳定内容键转换为带物品图标的距离叶子项。
        /// </summary>
        private static StatisticsListItem CreateMovementItem(
            StableContentKey content,
            long amount)
        {
            bool resolved = content.TryResolveItemType(out int itemType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetItemNameValue(itemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Movement.MissingItemContent",
                    content.ToString());
            Asset<Texture2D> icon = null;
            if (resolved)
            {
                // 原版物品贴图按需加载；直接访问数组时，尚未在本次会话出现过的
                // 矿车、坐骑等内容可能仍是空槽位，导致历史统计行没有图标。
                Main.instance.LoadItem(itemType);
                icon = TextureAssets.Item[itemType];
            }

            return new StatisticsListItem(
                name,
                itemType: resolved ? itemType : 0,
                icon: icon,
                events: 0L,
                units: 0L,
                amount: amount,
                source: source,
                iconKind: resolved ? StatisticsIconKind.Item : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? itemType
                    : null);
        }

        /// <summary>
        /// 判断移动类别是否在 UI 中按来源物品展开。
        /// </summary>
        private static bool IsDetailedMovementMode(MovementModeKind mode)
        {
            return mode == MovementModeKind.Minecart
                || mode == MovementModeKind.OtherMount
                || mode == MovementModeKind.RopeOrPulley
                || mode == MovementModeKind.GrapplingHook
                || mode == MovementModeKind.Flying
                || mode == MovementModeKind.Jumping;
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
            int? vanillaContentId = null;
            Asset<Texture2D> icon = null;
            StatisticsIconKind iconKind = StatisticsIconKind.None;
            StatisticsContentSource source = GetContentSource(content);
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
                    // 原版 NPC 贴图同样是按需加载的，读取资产数组前必须显式加载。
                    Main.instance.LoadNPC(npcType);
                    icon = TextureAssets.Npc[npcType];
                    iconKind = StatisticsIconKind.Npc;
                    if (source == StatisticsContentSource.Vanilla)
                    {
                        vanillaContentId = npcType;
                    }
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
                    if (source == StatisticsContentSource.Vanilla)
                    {
                        vanillaContentId = buffType;
                    }
                }
            }

            return new StatisticsListItem(
                name,
                itemType: iconType,
                icon: icon,
                events: events,
                units: 0L,
                amount: amount,
                source: source,
                iconKind: iconKind,
                content: content,
                vanillaContentId: vanillaContentId);
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
        /// 页标题右侧的紧凑操作按钮；占位页保持显示但不响应点击。
        /// </summary>
        private sealed class HeaderActionButton : UIPanel
        {
            private readonly Action clickAction;
            private readonly UIText text;
            private bool enabled;

            internal HeaderActionButton(Action clickAction)
            {
                this.clickAction = clickAction;
                SetPadding(3f);
                OverflowHidden = true;
                OnLeftClick += HandleClick;

                text = new UIText(string.Empty, 0.7f);
                text.HAlign = 0.5f;
                text.VAlign = 0.5f;
                Append(text);
            }

            internal void SetText(LocalizedText value)
            {
                text.SetText(value);
            }

            internal void SetEnabled(bool value)
            {
                enabled = value;
            }

            public override void Update(GameTime gameTime)
            {
                if (!enabled)
                {
                    BackgroundColor = new Color(32, 40, 72, 180);
                    BorderColor = new Color(58, 70, 112, 180);
                    text.TextColor = new Color(118, 129, 160);
                }
                else
                {
                    BackgroundColor = IsMouseHovering
                        ? new Color(63, 82, 151, 245)
                        : new Color(45, 59, 110, 235);
                    BorderColor = new Color(75, 97, 174, 255);
                    text.TextColor = new Color(220, 226, 244);
                }

                base.Update(gameTime);
            }

            private void HandleClick(UIMouseEvent evt, UIElement listeningElement)
            {
                if (!enabled)
                {
                    return;
                }

                clickAction();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
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
